import {
  CheckCircleFilled, CheckOutlined, DeleteOutlined, DownOutlined, EditOutlined, EnvironmentOutlined, FlagFilled, MoreOutlined,
  PhoneOutlined, PrinterOutlined, ReloadOutlined, StopOutlined, UserSwitchOutlined,
} from '@ant-design/icons';
import { DrawerForm, ModalForm, PageContainer, ProFormDateRangePicker, ProFormSelect, ProFormText, ProFormTextArea } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Divider, Dropdown, Form, Input, Modal, Result, Space, Spin, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ApiError, get, post, problemMessage, put } from '../../api/client';
import type { BauakteDetail, BauakteHint, BauakteMember, BauakteStatus, ContactResponse, DirectoryUser } from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { CustomerSelect } from '../../shared/CustomerSelect';
import { address, BauakteStatusTag, bauakteStatusLabels } from '../../shared/status';
import { BauakteContacts } from './Contacts';
import { BauakteDocuments, BauaktePhotos, BauakteVideos } from './Files';
import { BauakteNotes } from './Notes';
import { BauakteOverview } from './Overview';
import { BauakteDiary, BauakteInspections } from './Records';
import { BauakteRooms } from './Rooms';
import { BauakteWork } from './Work';

/** Alle bearbeitbaren Felder; ein Speichern sendet immer den vollständigen Stand (PUT mit If-Match). */
export interface BauakteChanges {
  title?: string;
  status?: BauakteStatus;
  customerId?: string | null;
  street?: string | null;
  postalCode?: string | null;
  city?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  responsibleUserId?: string | null;
  description?: string | null;
  plannedStart?: string | null;
  plannedEnd?: string | null;
  siteContactName?: string | null;
  siteContactPhone?: string | null;
  siteContactEmail?: string | null;
  siteNotes?: string | null;
  cancellationReason?: string | null;
}

interface EditForm {
  title: string;
  status: BauakteStatus;
  customerId: string | null;
  street?: string;
  postalCode?: string;
  city?: string;
  responsibleUserId: string | null;
  period?: [Dayjs | null, Dayjs | null];
  description?: string;
  siteNotes?: string;
}

const tabs = [
  { key: 'uebersicht', tab: 'Übersicht' },
  { key: 'kontakte', tab: 'Kontakte' },
  { key: 'notizen', tab: 'Notizen & Verlauf' },
  { key: 'fotos', tab: 'Fotos' },
  { key: 'videos', tab: 'Videos' },
  { key: 'dokumente', tab: 'Dokumente' },
  { key: 'tagesuebersicht', tab: 'Tagesübersicht' },
  { key: 'erstbegehung', tab: 'Erstbegehung' },
  { key: 'raeume', tab: 'Räume & Flächen' },
  { key: 'aufgaben', tab: 'Aufgaben & Mängel' },
];

const hintTypes = { info: 'info', success: 'success', warning: 'warning', danger: 'error' } as const;
const flowStatuses: BauakteStatus[] = ['inquiry', 'planned', 'inProgress', 'drying', 'completed'];

/** Bauakte im Büro: Kopf mit Status und Aktionen, Hinweiszeilen, Reiter wie in Estrich Web. */
export default function BauaktePage() {
  const { id = '' } = useParams();
  const { has, user } = useAuth();
  const { message } = App.useApp();
  const queries = useQueryClient();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const tab = tabs.some((t) => t.key === params.get('reiter')) ? params.get('reiter')! : 'uebersicht';
  const setTab = (key: string) => setParams((p) => { p.set('reiter', key); return p; }, { replace: true });
  const [editing, setEditing] = useState(false);
  const [dialog, setDialog] = useState<'cancel' | 'delete' | 'assign' | null>(null);

  const bauakte = useQuery({ queryKey: ['bauakte', id], queryFn: () => get<BauakteDetail>(`bauakten/${id}`) });
  const people = useQuery({ queryKey: ['directory'], queryFn: () => get<DirectoryUser[]>('directory/users'), staleTime: 60_000 });
  const hints = useQuery({ queryKey: ['hints', id], queryFn: () => get<BauakteHint[]>(`bauakten/${id}/hints`), enabled: bauakte.isSuccess });
  const members = useQuery({ queryKey: ['members', id], queryFn: () => get<BauakteMember[]>(`bauakten/${id}/members`), enabled: bauakte.isSuccess });
  const customer = useQuery({
    queryKey: ['contact', bauakte.data?.customerId], enabled: !!bauakte.data?.customerId && has('contacts.read'),
    queryFn: () => get<ContactResponse>(`contacts/${bauakte.data!.customerId}`),
  });

  if (bauakte.isLoading) {
    return <Spin style={{ display: 'block', marginTop: 80 }} />;
  }
  if (bauakte.error || !bauakte.data) {
    return <Result status="404" title="Bauakte nicht gefunden" subTitle={problemMessage(bauakte.error)} extra={<Link to="/bauakten">Zu den Bauakten</Link>} />;
  }
  const b = bauakte.data;
  const name = (userId: string | null) => (userId && people.data?.find((p) => p.id === userId)?.displayName) || '–';
  const canEdit = has('bauakten.edit');
  const route = b.latitude !== null && b.longitude !== null
    ? `https://www.openstreetmap.org/directions?to=${b.latitude}%2C${b.longitude}`
    : address(b) !== '–' ? `https://www.openstreetmap.org/search?query=${encodeURIComponent(address(b))}` : null;
  const phone = b.siteContactPhone ?? customer.data?.phone ?? null;

  const refresh = () => {
    for (const key of ['history', 'hints', 'members', 'notes-timeline']) {
      void queries.invalidateQueries({ queryKey: [key, id] });
    }
  };

  /** Speichern mit der gelesenen Version (If-Match); bei zwischenzeitlicher Änderung klarer Hinweis statt Überschreiben. */
  const save = async (changes: BauakteChanges, success = 'Gespeichert') => {
    try {
      const updated = await put<BauakteDetail>(`bauakten/${b.id}`, {
        title: b.title, status: b.status, street: b.street, postalCode: b.postalCode, city: b.city, latitude: b.latitude, longitude: b.longitude,
        description: b.description, customerId: b.customerId, responsibleUserId: b.responsibleUserId, plannedStart: b.plannedStart,
        plannedEnd: b.plannedEnd, siteContactName: b.siteContactName, siteContactPhone: b.siteContactPhone, siteContactEmail: b.siteContactEmail,
        siteNotes: b.siteNotes, cancellationReason: b.cancellationReason,
        ...changes,
      }, { 'If-Match': `"${b.version}"` });
      queries.setQueryData(['bauakte', id], updated);
      void queries.invalidateQueries({ queryKey: ['bauakten'] });
      refresh();
      message.success(success);
      return true;
    } catch (e) {
      message.error(e instanceof ApiError && e.code === 'VERSION_CONFLICT'
        ? 'Die Bauakte wurde inzwischen von jemand anderem geändert. Bitte neu laden und die Änderung erneut vornehmen.'
        : problemMessage(e));
      return false;
    }
  };

  const assignedText = members.data?.length
    ? members.data.length === 1 ? members.data[0].displayName : `${name(b.responsibleUserId)} +${members.data.length - 1}`
    : name(b.responsibleUserId);

  const header = (
    <div className="bauakte-head">
      <Typography.Text type="secondary" className="overline">Bauakte {b.number ?? '(noch ohne Nummer)'}</Typography.Text>
      <Typography.Title level={2} style={{ margin: 0 }}>{b.title}</Typography.Title>
    </div>
  );

  return (
    <PageContainer
      className="bauakte-page"
      breadcrumb={{ items: [{ title: <Link to="/bauakten">Bauakten</Link> }, { title: b.number ?? 'Neue Bauakte' }] }}
      title={header}
      extra={[
        <Dropdown key="status" disabled={!canEdit || b.status === 'cancelled'} trigger={['click']}
          menu={{
            selectedKeys: [b.status],
            items: flowStatuses.map((s) => ({ key: s, label: bauakteStatusLabels[s] })),
            onClick: ({ key }) => key !== b.status && void save({ status: key as BauakteStatus }, `Status: ${bauakteStatusLabels[key as BauakteStatus]}`),
          }}>
          <Button className="status-button" aria-label={`Status: ${bauakteStatusLabels[b.status]}`}>
            <FlagFilled /> <BauakteStatusTag status={b.status} /> {canEdit && b.status !== 'cancelled' && <DownOutlined />}
          </Button>
        </Dropdown>,
        canEdit && b.status === 'inquiry' && (
          <Button key="order" type="primary" icon={<CheckOutlined />} onClick={() => void save({ status: 'planned' }, 'Als Auftrag übernommen')}>Als Auftrag übernehmen</Button>
        ),
        canEdit && b.status !== 'inquiry' && b.status !== 'completed' && b.status !== 'cancelled' && (
          <Button key="done" className="button-success" icon={<CheckCircleFilled />} onClick={() => void save({ status: 'completed' }, 'Bauakte abgeschlossen')}>Abschließen</Button>
        ),
        <Button key="assign" icon={<UserSwitchOutlined />} disabled={!canEdit} onClick={() => setDialog('assign')}>Zugewiesen: {assignedText}</Button>,
        <Dropdown key="actions" trigger={['click']}
          menu={{
            items: [
              canEdit ? { key: 'edit', icon: <EditOutlined />, label: 'Bearbeiten', onClick: () => setEditing(true) } : null,
              { key: 'print', icon: <PrinterOutlined />, label: 'Bauaktenblatt drucken', onClick: () => { setTab('uebersicht'); setTimeout(() => window.print(), 300); } },
              route ? { key: 'route', icon: <EnvironmentOutlined />, label: 'Route planen', onClick: () => window.open(route, '_blank', 'noopener') } : null,
              phone ? { key: 'call', icon: <PhoneOutlined />, label: `Anrufen (${phone})`, onClick: () => { window.location.href = `tel:${phone}`; } } : null,
              { key: 'reload', icon: <ReloadOutlined />, label: 'Aktualisieren', onClick: () => { void bauakte.refetch(); refresh(); } },
              { type: 'divider' as const },
              canEdit && b.status !== 'cancelled' ? { key: 'cancel', icon: <StopOutlined />, label: 'Bauakte stornieren', onClick: () => setDialog('cancel') } : null,
              canEdit && b.status === 'cancelled' ? { key: 'reactivate', icon: <ReloadOutlined />, label: 'Storno aufheben', onClick: () => void save({ status: 'planned' }, 'Storno aufgehoben') } : null,
              has('bauakten.delete') ? { key: 'delete', icon: <DeleteOutlined />, label: 'In den Papierkorb', danger: true, onClick: () => setDialog('delete') } : null,
            ].filter(Boolean),
          }}>
          <Button icon={<MoreOutlined />}>Aktionen</Button>
        </Dropdown>,
      ].filter(Boolean)}
      content={(hints.data?.length ?? 0) > 0 && (
        <Space direction="vertical" size={6} style={{ width: '100%' }} className="hints" role="list" aria-label="Hinweise">
          {hints.data!.map((h) => (
            <Alert key={h.kind + h.text} role="listitem" type={hintTypes[h.severity]} showIcon banner={false} message={h.text} className="hint" />
          ))}
        </Space>
      )}
      tabActiveKey={tab}
      onTabChange={setTab}
      tabList={tabs}
      tabProps={{ size: 'middle', tabBarGutter: 18 }}
    >
      {tab === 'uebersicht' && (
        <BauakteOverview bauakte={b} customer={customer.data ?? null} members={members.data ?? []} canEdit={canEdit} onTab={setTab}
          onEdit={() => setEditing(true)} onLocation={(latitude, longitude) => save({ latitude, longitude }, 'Standort gespeichert')} />
      )}
      {tab === 'kontakte' && (
        <BauakteContacts bauakte={b} customer={customer.data ?? null} members={members.data ?? []} canEdit={canEdit}
          onSave={(changes) => save(changes)} onAssign={() => setDialog('assign')} />
      )}
      {tab === 'notizen' && <BauakteNotes bauakteId={b.id} canWrite={has('notes.write')} people={people.data ?? []} currentUserId={user?.id ?? null} />}
      {tab === 'fotos' && <BauaktePhotos bauakteId={b.id} canUpload={has('files.upload')} canDelete={has('files.delete')} />}
      {tab === 'videos' && <BauakteVideos bauakteId={b.id} canUpload={has('files.upload')} canDelete={has('files.delete')} />}
      {tab === 'dokumente' && <BauakteDocuments bauakteId={b.id} canUpload={has('files.upload')} canDelete={has('files.delete')} people={people.data ?? []} />}
      {tab === 'tagesuebersicht' && <BauakteDiary bauakteId={b.id} people={people.data ?? []} />}
      {tab === 'erstbegehung' && <BauakteInspections bauakte={b} people={people.data ?? []} />}
      {tab === 'raeume' && <BauakteRooms bauakteId={b.id} canEdit={canEdit} />}
      {tab === 'aufgaben' && <BauakteWork bauakteId={b.id} canEdit={has('work.edit')} people={people.data ?? []} />}

      <DrawerForm<EditForm>
        title="Bauakte bearbeiten"
        open={editing}
        width={560}
        initialValues={{
          title: b.title, status: b.status, customerId: b.customerId, street: b.street ?? '', postalCode: b.postalCode ?? '', city: b.city ?? '',
          responsibleUserId: b.responsibleUserId, description: b.description ?? '', siteNotes: b.siteNotes ?? '',
          period: [b.plannedStart ? dayjs(b.plannedStart) : null, b.plannedEnd ? dayjs(b.plannedEnd) : null],
        }}
        drawerProps={{ destroyOnHidden: true, onClose: () => setEditing(false) }}
        onOpenChange={setEditing}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        onFinish={async (v) => {
          const ok = await save({
            title: v.title.trim(), status: v.status, customerId: v.customerId ?? null, street: v.street || null, postalCode: v.postalCode || null,
            city: v.city || null, responsibleUserId: v.responsibleUserId ?? null, description: v.description || null, siteNotes: v.siteNotes || null,
            plannedStart: v.period?.[0]?.format('YYYY-MM-DD') ?? null, plannedEnd: v.period?.[1]?.format('YYYY-MM-DD') ?? null,
          });
          if (ok) setEditing(false);
          return ok;
        }}
      >
        <Divider orientation="left" orientationMargin={0} plain>Basisdaten</Divider>
        <ProFormText name="title" label="Bauvorhaben" rules={[{ required: true, message: 'Bitte das Bauvorhaben angeben.' }]} />
        <ProFormSelect name="status" label="Status" allowClear={false} disabled={b.status === 'cancelled'}
          options={flowStatuses.map((value) => ({ value, label: bauakteStatusLabels[value] }))} />
        <Form.Item name="customerId" label="Kunde"><CustomerSelect /></Form.Item>
        <ProFormDateRangePicker name="period" label="Ausführung (Beginn – Ende)" fieldProps={{ format: 'DD.MM.YYYY', allowEmpty: [true, true] }} />
        <ProFormSelect name="responsibleUserId" label="Zuständig" allowClear showSearch
          options={(people.data ?? []).filter((p) => p.isActive).map((p) => ({ value: p.id, label: p.displayName }))} />
        <Divider orientation="left" orientationMargin={0} plain>Baustelle</Divider>
        <ProFormText name="street" label="Straße" />
        <Space.Compact block>
          <ProFormText name="postalCode" label="PLZ" width={120} />
          <ProFormText name="city" label="Ort" width="md" />
        </Space.Compact>
        <ProFormTextArea name="siteNotes" label="Hinweise / Behinderungen" extra="Wird in der Bauakte und in den Apps hervorgehoben (Zufahrt, Schlüssel, Strom …)."
          fieldProps={{ rows: 3, maxLength: 2000, showCount: true }} />
        <ProFormTextArea name="description" label="Beschreibung" fieldProps={{ rows: 5 }} />
      </DrawerForm>

      <CancelDialog open={dialog === 'cancel'} onClose={() => setDialog(null)}
        onConfirm={(reason) => save({ status: 'cancelled', cancellationReason: reason }, 'Bauakte storniert')} />
      <DeleteDialog open={dialog === 'delete'} title={b.title} onClose={() => setDialog(null)}
        onConfirm={async (password) => {
          try {
            await post(`bauakten/${b.id}/delete`, { password });
            message.success('Bauakte in den Papierkorb gelegt');
            void queries.invalidateQueries({ queryKey: ['bauakten'] });
            navigate('/bauakten');
            return null;
          } catch (e) {
            return problemMessage(e);
          }
        }} />
      <AssignDialog open={dialog === 'assign'} bauakte={b} members={members.data ?? []} people={people.data ?? []} onClose={() => setDialog(null)}
        onSaved={() => { setDialog(null); void bauakte.refetch(); refresh(); }} />
    </PageContainer>
  );
}

function CancelDialog({ open, onClose, onConfirm }: { open: boolean; onClose: () => void; onConfirm: (reason: string) => Promise<boolean> }) {
  return (
    <ModalForm<{ reason: string }>
      title="Bauakte stornieren" open={open} width={480}
      modalProps={{ destroyOnHidden: true, onCancel: onClose }}
      submitter={{ searchConfig: { submitText: 'Stornieren', resetText: 'Abbrechen' }, submitButtonProps: { danger: true } }}
      onOpenChange={(o) => { if (!o) onClose(); }}
      onFinish={async (v) => { const ok = await onConfirm(v.reason.trim()); if (ok) onClose(); return ok; }}
    >
      <Typography.Paragraph type="secondary">Die Bauakte bleibt mit allen Fotos, Notizen und dem Verlauf erhalten und lässt sich wieder aktivieren.</Typography.Paragraph>
      <ProFormTextArea name="reason" label="Stornogrund" placeholder="z. B. Kunde hat abgesagt" fieldProps={{ rows: 3, maxLength: 500 }}
        rules={[{ required: true, whitespace: true, message: 'Bitte einen Stornogrund angeben.' }]} />
    </ModalForm>
  );
}

function DeleteDialog({ open, title, onClose, onConfirm }: {
  open: boolean; title: string; onClose: () => void; onConfirm: (password: string) => Promise<string | null>;
}) {
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  return (
    <Modal title="Bauakte löschen" open={open} onCancel={() => { setPassword(''); setError(null); onClose(); }} destroyOnHidden
      okText="In den Papierkorb" okButtonProps={{ danger: true, disabled: !password, loading: busy }} cancelText="Abbrechen"
      onOk={async () => { setBusy(true); const result = await onConfirm(password); setBusy(false); setError(result); }}>
      <Typography.Paragraph>
        Die Bauakte <strong>„{title}“</strong> wird in den Papierkorb gelegt und ist für alle nicht mehr sichtbar.
        Bitte zur Bestätigung das eigene Passwort eingeben.
      </Typography.Paragraph>
      <Form layout="vertical">
        <Form.Item label="Passwort bestätigen" htmlFor="delete-password" validateStatus={error ? 'error' : undefined} help={error}>
          <Input.Password id="delete-password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus />
        </Form.Item>
      </Form>
    </Modal>
  );
}

function AssignDialog({ open, bauakte, members, people, onClose, onSaved }: {
  open: boolean; bauakte: BauakteDetail; members: BauakteMember[]; people: DirectoryUser[]; onClose: () => void; onSaved: () => void;
}) {
  const { message } = App.useApp();
  const active = people.filter((p) => p.isActive).map((p) => ({ value: p.id, label: p.displayName }));
  return (
    <ModalForm<{ responsibleUserId: string | null; memberUserIds: string[] }>
      key={`${bauakte.version}-${members.length}`}
      title="Bauakte zuweisen" open={open} width={520}
      initialValues={{ responsibleUserId: bauakte.responsibleUserId, memberUserIds: members.filter((m) => m.role === 'member').map((m) => m.userId) }}
      modalProps={{ destroyOnHidden: true, onCancel: onClose }}
      submitter={{ searchConfig: { submitText: 'Zuweisen', resetText: 'Abbrechen' } }}
      onOpenChange={(o) => { if (!o) onClose(); }}
      onFinish={async (v) => {
        try {
          if (v.responsibleUserId !== bauakte.responsibleUserId) {
            await put(`bauakten/${bauakte.id}`, {
              title: bauakte.title, status: bauakte.status, street: bauakte.street, postalCode: bauakte.postalCode, city: bauakte.city,
              latitude: bauakte.latitude, longitude: bauakte.longitude, description: bauakte.description, customerId: bauakte.customerId,
              responsibleUserId: v.responsibleUserId ?? null, plannedStart: bauakte.plannedStart, plannedEnd: bauakte.plannedEnd,
              siteContactName: bauakte.siteContactName, siteContactPhone: bauakte.siteContactPhone, siteContactEmail: bauakte.siteContactEmail,
              siteNotes: bauakte.siteNotes, cancellationReason: bauakte.cancellationReason,
            }, { 'If-Match': `"${bauakte.version}"` });
          }
          await put(`bauakten/${bauakte.id}/members`, { userIds: (v.memberUserIds ?? []).filter((u) => u !== v.responsibleUserId) });
          message.success('Zuweisung gespeichert');
          onSaved();
          return true;
        } catch (e) {
          message.error(problemMessage(e));
          return false;
        }
      }}
    >
      <ProFormSelect name="responsibleUserId" label="Zuständig (Bauleitung)" options={active} showSearch allowClear />
      <ProFormSelect name="memberUserIds" label="Zugewiesene Mitarbeiter" mode="multiple" options={active} showSearch
        extra="Zugewiesene Mitarbeiter sehen die Bauakte auch ohne das Recht „alle Bauakten sehen“, z. B. in der App." />
    </ModalForm>
  );
}
