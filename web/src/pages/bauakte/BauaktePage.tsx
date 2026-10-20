import { CheckOutlined, EditOutlined, EnvironmentOutlined, PhoneOutlined, ReloadOutlined } from '@ant-design/icons';
import { DrawerForm, PageContainer, ProDescriptions, ProFormSelect, ProFormText, ProFormTextArea } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Form, Result, Space, Spin, Timeline, Typography } from 'antd';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError, get, problemMessage, put } from '../../api/client';
import type { BauakteDetail, BauakteHistoryEntry, BauakteStatus, ContactResponse, DirectoryUser } from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { CustomerSelect } from '../../shared/CustomerSelect';
import { address, BauakteStatusTag, bauakteStatusLabels, formatDateTime } from '../../shared/status';
import { BauakteFiles } from './Files';
import { BauakteNotes } from './Notes';
import { BauakteOverview } from './Overview';
import { BauakteRecords } from './Records';
import { BauakteRooms } from './Rooms';
import { BauakteWork } from './Work';

interface EditForm {
  title: string;
  status: BauakteStatus;
  customerId: string | null;
  street?: string;
  postalCode?: string;
  city?: string;
  responsibleUserId: string | null;
  description?: string;
  latitude?: number | null;
  longitude?: number | null;
}

/** Bauakte im Büro (AP-18): Stammdaten, Räume, Aufgaben, Notizen, Dateien, Prüfungen, Verlauf. */
export default function BauaktePage() {
  const { id = '' } = useParams();
  const { has } = useAuth();
  const { message } = App.useApp();
  const queries = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [tab, setTab] = useState('overview');

  const bauakte = useQuery({ queryKey: ['bauakte', id], queryFn: () => get<BauakteDetail>(`bauakten/${id}`) });
  const people = useQuery({ queryKey: ['directory'], queryFn: () => get<DirectoryUser[]>('directory/users'), staleTime: 60_000 });
  const customer = useQuery({
    queryKey: ['contact', bauakte.data?.customerId], enabled: !!bauakte.data?.customerId && has('contacts.read'),
    queryFn: () => get<ContactResponse>(`contacts/${bauakte.data!.customerId}`),
  });
  const history = useQuery({ queryKey: ['history', id], enabled: tab === 'history', queryFn: () => get<BauakteHistoryEntry[]>(`bauakten/${id}/history`) });

  if (bauakte.isLoading) {
    return <Spin style={{ display: 'block', marginTop: 80 }} />;
  }
  if (bauakte.error || !bauakte.data) {
    return <Result status="404" title="Bauakte nicht gefunden" subTitle={problemMessage(bauakte.error)} extra={<Link to="/bauakten">Zu den Bauakten</Link>} />;
  }
  const b = bauakte.data;
  const name = (userId: string | null) => (userId && people.data?.find((p) => p.id === userId)?.displayName) || '–';
  const canEdit = has('bauakten.edit');
  const maps = b.latitude !== null && b.longitude !== null
    ? `https://www.openstreetmap.org/?mlat=${b.latitude}&mlon=${b.longitude}#map=18/${b.latitude}/${b.longitude}`
    : address(b) !== '–' ? `https://www.openstreetmap.org/search?query=${encodeURIComponent(address(b))}` : null;

  /** Speichern mit der gelesenen Version (If-Match); bei zwischenzeitlicher Änderung klarer Hinweis statt Überschreiben. */
  const save = async (values: Partial<EditForm>) => {
    try {
      const updated = await put<BauakteDetail>(`bauakten/${b.id}`, {
        title: (values.title ?? b.title).trim(), status: values.status ?? b.status, street: values.street ?? b.street ?? null,
        postalCode: values.postalCode ?? b.postalCode ?? null, city: values.city ?? b.city ?? null,
        latitude: 'latitude' in values ? values.latitude ?? null : b.latitude, longitude: 'longitude' in values ? values.longitude ?? null : b.longitude,
        description: values.description ?? b.description ?? null,
        customerId: 'customerId' in values ? values.customerId ?? null : b.customerId,
        responsibleUserId: 'responsibleUserId' in values ? values.responsibleUserId ?? null : b.responsibleUserId,
      }, { 'If-Match': `"${b.version}"` });
      queries.setQueryData(['bauakte', id], updated);
      void queries.invalidateQueries({ queryKey: ['history', id] });
      message.success('Gespeichert');
      return true;
    } catch (e) {
      message.error(e instanceof ApiError && e.code === 'VERSION_CONFLICT'
        ? 'Die Bauakte wurde inzwischen von jemand anderem geändert. Bitte neu laden und die Änderung erneut vornehmen.'
        : problemMessage(e));
      return false;
    }
  };

  return (
    <PageContainer
      breadcrumb={{ items: [{ title: <Link to="/bauakten">Bauakten</Link> }, { title: b.number ?? 'Neue Bauakte' }] }}
      title={<Space size={12}><Typography.Text type="secondary" className="tabular">{b.number ?? 'noch ohne Nummer'}</Typography.Text>{b.title}</Space>}
      tags={<BauakteStatusTag status={b.status} />}
      extra={[
        maps && <Button key="map" icon={<EnvironmentOutlined />} href={maps} target="_blank">Karte</Button>,
        customer.data?.phone && <Button key="call" icon={<PhoneOutlined />} href={`tel:${customer.data.phone}`}>Anrufen</Button>,
        <Button key="reload" icon={<ReloadOutlined />} onClick={() => void queries.invalidateQueries({ queryKey: ['bauakte', id] })}>Aktualisieren</Button>,
        canEdit && b.status === 'inquiry' && <Button key="order" icon={<CheckOutlined />} onClick={() => void save({ status: 'planned' })}>Als Auftrag übernehmen</Button>,
        canEdit && <Button key="edit" type="primary" icon={<EditOutlined />} onClick={() => setEditing(true)}>Bearbeiten</Button>,
      ].filter(Boolean)}
      content={
        <ProDescriptions column={{ xs: 1, md: 2, xl: 3 }} size="small" className="facts">
          <ProDescriptions.Item label="Kunde">{customer.data?.name ?? '–'}</ProDescriptions.Item>
          <ProDescriptions.Item label="Baustelle">{address(b)}</ProDescriptions.Item>
          <ProDescriptions.Item label="Zuständig">{name(b.responsibleUserId)}</ProDescriptions.Item>
          <ProDescriptions.Item label="Telefon">{customer.data?.phone ?? '–'}</ProDescriptions.Item>
          <ProDescriptions.Item label="Angelegt">{formatDateTime(b.createdAt)} · {name(b.createdByUserId)}</ProDescriptions.Item>
          <ProDescriptions.Item label="Geändert">{formatDateTime(b.updatedAt)}</ProDescriptions.Item>
        </ProDescriptions>
      }
      tabActiveKey={tab}
      onTabChange={setTab}
      tabList={[
        { key: 'overview', tab: 'Übersicht' },
        { key: 'rooms', tab: 'Räume und Flächen' },
        { key: 'work', tab: 'Aufgaben und Mängel' },
        { key: 'notes', tab: 'Notizen' },
        { key: 'files', tab: 'Fotos und Dokumente' },
        { key: 'records', tab: 'Prüfungen und Bautagebuch' },
        { key: 'history', tab: 'Verlauf' },
      ]}
    >
      {tab === 'overview' && (
        <BauakteOverview bauakte={b} canEdit={canEdit} onTab={setTab}
          onLocation={(latitude, longitude) => save({ latitude, longitude })} />
      )}
      {tab === 'rooms' && <BauakteRooms bauakteId={b.id} canEdit={canEdit} />}
      {tab === 'work' && <BauakteWork bauakteId={b.id} canEdit={has('work.edit')} people={people.data ?? []} />}
      {tab === 'notes' && <BauakteNotes bauakteId={b.id} canWrite={has('notes.write')} people={people.data ?? []} />}
      {tab === 'files' && <BauakteFiles bauakteId={b.id} canUpload={has('files.upload')} />}
      {tab === 'records' && <BauakteRecords bauakteId={b.id} people={people.data ?? []} />}
      {tab === 'history' && (
        <Card size="small">
          <Timeline items={(history.data ?? []).map((h) => ({ children: <><Typography.Text type="secondary" className="tabular">{formatDateTime(h.at)}</Typography.Text> {h.text}</> }))} />
          {history.data?.length === 0 && <Typography.Text type="secondary">Noch keine Einträge.</Typography.Text>}
        </Card>
      )}

      <DrawerForm<EditForm>
        title="Bauakte bearbeiten"
        open={editing}
        width={520}
        initialValues={{ title: b.title, status: b.status, customerId: b.customerId, street: b.street ?? '', postalCode: b.postalCode ?? '', city: b.city ?? '', responsibleUserId: b.responsibleUserId, description: b.description ?? '' }}
        drawerProps={{ destroyOnHidden: true, onClose: () => setEditing(false) }}
        onOpenChange={setEditing}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        onFinish={async (values) => {
          const ok = await save(values);
          if (ok) setEditing(false);
          return ok;
        }}
      >
        <ProFormText name="title" label="Bauvorhaben" rules={[{ required: true }]} />
        <ProFormSelect name="status" label="Status" options={Object.entries(bauakteStatusLabels).map(([value, label]) => ({ value, label }))} />
        <Form.Item name="customerId" label="Kunde"><CustomerSelect /></Form.Item>
        <ProFormText name="street" label="Straße" />
        <Space.Compact block>
          <ProFormText name="postalCode" label="PLZ" width={120} />
          <ProFormText name="city" label="Ort" width="md" />
        </Space.Compact>
        <ProFormSelect name="responsibleUserId" label="Zuständig" allowClear
          options={(people.data ?? []).filter((p) => p.isActive).map((p) => ({ value: p.id, label: p.displayName }))} />
        <ProFormTextArea name="description" label="Beschreibung" fieldProps={{ rows: 5 }} />
      </DrawerForm>
    </PageContainer>
  );
}
