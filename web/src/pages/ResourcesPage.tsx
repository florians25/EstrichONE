import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { ModalForm, PageContainer, ProFormColorPicker, ProFormDateRangePicker, ProFormSelect, ProFormText, ProTable } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Space, Tag, Typography } from 'antd';
import type { Color } from 'antd/es/color-picker';
import type { Dayjs } from 'dayjs';
import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { del, get, problemMessage, put } from '../api/client';
import type { AbsenceKind, AbsenceView, CrewResponse, DirectoryUser, PlanningRange, ResourceKind, ResourceResponse } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { addDays, isoDay, parseDay } from '../shared/days';
import { uuidv7 } from '../shared/ids';

export const resourceKindLabels: Record<ResourceKind, string> = { vehicle: 'Fahrzeug', pump: 'Pumpe', machine: 'Maschine' };
export const absenceKindLabels: Record<AbsenceKind, string> = { vacation: 'Urlaub', sick: 'Krank', training: 'Schulung', other: 'Sonstiges' };
const absenceColors: Record<AbsenceKind, string> = { vacation: 'cyan', sick: 'red', training: 'purple', other: 'default' };
const tabs = { kolonnen: 'Kolonnen', geraete: 'Fahrzeuge und Geräte', abwesenheiten: 'Abwesenheiten' } as const;
type TabKey = keyof typeof tabs;
const day = (value: string) => parseDay(value).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
const options = <K extends string>(labels: Record<K, string>) => (Object.keys(labels) as K[]).map((k) => ({ value: k, label: labels[k] }));

interface CrewForm { name: string; leaderUserId?: string | null; memberUserIds: string[]; color: string | Color }
interface ResourceForm { kind: ResourceKind; name: string; identifier?: string }
interface AbsenceForm { userId: string; kind: AbsenceKind; range: [Dayjs, Dayjs] }

/** Stammdaten der Planung: Kolonnen, Fahrzeuge und Geräte, Abwesenheiten. */
export default function ResourcesPage() {
  const { has } = useAuth();
  const { message, modal } = App.useApp();
  const queries = useQueryClient();
  const [params, setParams] = useSearchParams();
  const tab: TabKey = (params.get('reiter') as TabKey | null) && params.get('reiter')! in tabs ? params.get('reiter') as TabKey : 'kolonnen';
  const canManage = has('planning.manage');
  const today = isoDay(new Date());

  const crews = useQuery({ queryKey: ['crews'], queryFn: () => get<CrewResponse[]>('planning/crews') });
  const resources = useQuery({ queryKey: ['resources'], queryFn: () => get<ResourceResponse[]>('planning/resources') });
  const people = useQuery({ queryKey: ['directory'], queryFn: () => get<DirectoryUser[]>('directory/users'), staleTime: 60_000 });
  const absences = useQuery({ queryKey: ['absences', today], queryFn: () => get<PlanningRange>('planning/range', { from: today, to: addDays(today, 60) }).then((r) => r.absences) });

  const [crew, setCrew] = useState<CrewResponse | 'new' | null>(null);
  const [resource, setResource] = useState<ResourceResponse | 'new' | null>(null);
  const [absence, setAbsence] = useState(false);

  const active = (people.data ?? []).filter((p) => p.isActive).map((p) => ({ value: p.id, label: p.displayName }));
  const name = (id: string | null) => (id && people.data?.find((p) => p.id === id)?.displayName) || '–';

  const run = async (work: () => Promise<unknown>, keys: string[], success?: string) => {
    try {
      await work();
      keys.forEach((k) => void queries.invalidateQueries({ queryKey: [k] }));
      if (success) {
        message.success(success);
      }
      return true;
    } catch (e) {
      message.error(problemMessage(e));
      return false;
    }
  };

  const putCrew = (id: string, c: { name: string; leaderUserId: string | null; memberUserIds: string[]; color: string | null; isActive: boolean; sortOrder: number }) =>
    run(() => put(`planning/crews/${id}`, c), ['crews', 'planning'], 'Kolonne gespeichert');

  const saveCrew = (v: CrewForm) => {
    const existing = crew !== 'new' ? crew : null;
    const color = typeof v.color === 'string' ? v.color : v.color.toHexString();
    return putCrew(existing?.id ?? uuidv7(), {
      name: v.name.trim(), leaderUserId: v.leaderUserId ?? null, memberUserIds: v.memberUserIds ?? [], color,
      isActive: existing?.isActive ?? true, sortOrder: existing?.sortOrder ?? (crews.data?.length ?? 0) + 1,
    }).then((ok) => { if (ok) setCrew(null); return ok; });
  };

  const putResource = (id: string, r: { kind: ResourceKind; name: string; identifier: string | null; notes: string | null; isActive: boolean }) =>
    run(() => put(`planning/resources/${id}`, r), ['resources', 'planning'], 'Gespeichert');

  const saveResource = (v: ResourceForm) => {
    const existing = resource !== 'new' ? resource : null;
    return putResource(existing?.id ?? uuidv7(), {
      kind: v.kind, name: v.name.trim(), identifier: v.identifier || null, notes: existing?.notes ?? null, isActive: existing?.isActive ?? true,
    }).then((ok) => { if (ok) setResource(null); return ok; });
  };

  const saveAbsence = (v: AbsenceForm) =>
    run(() => put(`planning/absences/${uuidv7()}`, { userId: v.userId, kind: v.kind, from: v.range[0].format('YYYY-MM-DD'), to: v.range[1].format('YYYY-MM-DD') }),
      ['absences', 'planning'], 'Abwesenheit eingetragen').then((ok) => { if (ok) setAbsence(false); return ok; });

  const removeAbsence = (a: AbsenceView) => modal.confirm({
    title: `Abwesenheit von ${a.userName} entfernen?`, okText: 'Entfernen', okButtonProps: { danger: true }, cancelText: 'Abbrechen',
    onOk: () => run(() => del(`planning/absences/${a.id}`), ['absences', 'planning']),
  });

  const addButton = {
    kolonnen: <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => setCrew('new')}>Kolonne anlegen</Button>,
    geraete: <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => setResource('new')}>Fahrzeug oder Gerät hinzufügen</Button>,
    abwesenheiten: <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => setAbsence(true)}>Abwesenheit eintragen</Button>,
  }[tab];

  const tableProps = { rowKey: 'id', size: 'small' as const, bordered: true, cardBordered: true, search: false as const, pagination: false as const, options: { reload: false, density: true, setting: true } };

  return (
    <PageContainer
      title="Kolonnen und Geräte"
      subTitle="Stammdaten der Einsatzplanung"
      tabActiveKey={tab}
      onTabChange={(key) => setParams({ reiter: key })}
      tabList={(Object.keys(tabs) as TabKey[]).map((k) => ({ key: k, tab: tabs[k] }))}
      extra={canManage ? addButton : undefined}
    >
      {tab === 'kolonnen' && (
        <ProTable<CrewResponse> {...tableProps} dataSource={crews.data} loading={crews.isLoading} locale={{ emptyText: 'Noch keine Kolonnen.' }}
          columns={[
            { title: 'Kolonne', dataIndex: 'name', render: (_, c) => <Space><span className="swatch" style={{ background: c.color ?? '#d9d9d9' }} aria-hidden="true" />{c.name}</Space> },
            { title: 'Kolonnenführer', dataIndex: 'leaderUserId', width: 200, render: (_, c) => name(c.leaderUserId) },
            { title: 'Mitglieder', key: 'members', render: (_, c) => c.memberUserIds.length ? c.memberUserIds.map((id) => <Tag key={id} bordered={false}>{name(id)}</Tag>) : '–' },
            { title: 'Status', dataIndex: 'isActive', width: 110, render: (_, c) => c.isActive ? <Tag color="success" bordered={false}>Aktiv</Tag> : <Tag bordered={false}>Inaktiv</Tag> },
            {
              title: 'Aktionen', key: 'actions', width: 200, hideInTable: !canManage, render: (_, c) => (
                <Space size={0}>
                  <Button type="link" size="small" onClick={() => setCrew(c)}>Bearbeiten</Button>
                  <Button type="link" size="small" onClick={() => void putCrew(c.id, { ...c, isActive: !c.isActive })}>{c.isActive ? 'Deaktivieren' : 'Aktivieren'}</Button>
                </Space>
              ),
            },
          ]} />
      )}
      {tab === 'geraete' && (
        <ProTable<ResourceResponse> {...tableProps} dataSource={resources.data} loading={resources.isLoading} locale={{ emptyText: 'Noch keine Fahrzeuge oder Geräte.' }}
          columns={[
            { title: 'Bezeichnung', dataIndex: 'name' },
            { title: 'Art', dataIndex: 'kind', width: 140, render: (_, r) => resourceKindLabels[r.kind] },
            { title: 'Kennzeichen / Nr.', dataIndex: 'identifier', width: 200, render: (_, r) => r.identifier ?? '–' },
            { title: 'Status', dataIndex: 'isActive', width: 140, render: (_, r) => r.isActive ? <Tag color="success" bordered={false}>Verfügbar</Tag> : <Tag bordered={false}>Außer Betrieb</Tag> },
            {
              title: 'Aktionen', key: 'actions', width: 230, hideInTable: !canManage, render: (_, r) => (
                <Space size={0}>
                  <Button type="link" size="small" onClick={() => setResource(r)}>Bearbeiten</Button>
                  <Button type="link" size="small" onClick={() => void putResource(r.id, { ...r, isActive: !r.isActive })}>{r.isActive ? 'Außer Betrieb' : 'Wieder verfügbar'}</Button>
                </Space>
              ),
            },
          ]} />
      )}
      {tab === 'abwesenheiten' && (
        <ProTable<AbsenceView> {...tableProps} dataSource={absences.data} loading={absences.isLoading} locale={{ emptyText: 'Keine Abwesenheiten eingetragen.' }}
          headerTitle={<Typography.Text type="secondary">Abwesenheiten der nächsten 60 Tage</Typography.Text>}
          columns={[
            { title: 'Mitarbeiter', dataIndex: 'userName' },
            { title: 'Art', dataIndex: 'kind', width: 140, render: (_, a) => <Tag color={absenceColors[a.kind]} bordered={false}>{absenceKindLabels[a.kind]}</Tag> },
            { title: 'Zeitraum', key: 'range', width: 240, render: (_, a) => <span className="tabular">{day(a.from)} – {day(a.to)}</span> },
            {
              title: 'Aktionen', key: 'actions', width: 120, hideInTable: !canManage, render: (_, a) => (
                <Button type="link" size="small" danger icon={<DeleteOutlined />} onClick={() => removeAbsence(a)}>Entfernen</Button>
              ),
            },
          ]} />
      )}

      <ModalForm<CrewForm>
        key={crew && crew !== 'new' ? crew.id : 'new-crew'}
        title={crew === 'new' ? 'Kolonne anlegen' : 'Kolonne bearbeiten'}
        open={crew !== null}
        width={520}
        initialValues={crew && crew !== 'new'
          ? { name: crew.name, leaderUserId: crew.leaderUserId, memberUserIds: crew.memberUserIds, color: crew.color ?? '#0062CC' }
          : { name: '', memberUserIds: [], color: '#0062CC' }}
        modalProps={{ destroyOnHidden: true, onCancel: () => setCrew(null) }}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        onOpenChange={(open) => { if (!open) setCrew(null); }}
        onFinish={saveCrew}
      >
        <ProFormText name="name" label="Name der Kolonne" placeholder="z. B. Kolonne Nord" rules={[{ required: true, message: 'Bitte einen Namen angeben.' }]} />
        <ProFormSelect name="leaderUserId" label="Kolonnenführer" options={active} allowClear showSearch />
        <ProFormSelect name="memberUserIds" label="Mitglieder" mode="multiple" options={active} showSearch />
        <ProFormColorPicker name="color" label="Farbe in der Plantafel" />
      </ModalForm>

      <ModalForm<ResourceForm>
        key={resource && resource !== 'new' ? resource.id : 'new-resource'}
        title={resource === 'new' ? 'Fahrzeug oder Gerät hinzufügen' : 'Fahrzeug oder Gerät bearbeiten'}
        open={resource !== null}
        width={480}
        initialValues={resource && resource !== 'new' ? { kind: resource.kind, name: resource.name, identifier: resource.identifier ?? '' } : { kind: 'pump', name: '' }}
        modalProps={{ destroyOnHidden: true, onCancel: () => setResource(null) }}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        onOpenChange={(open) => { if (!open) setResource(null); }}
        onFinish={saveResource}
      >
        <ProFormSelect name="kind" label="Art" options={options(resourceKindLabels)} allowClear={false} />
        <ProFormText name="name" label="Bezeichnung" placeholder="z. B. Mixokret 2" rules={[{ required: true, message: 'Bitte eine Bezeichnung angeben.' }]} />
        <ProFormText name="identifier" label="Kennzeichen oder Seriennummer" />
      </ModalForm>

      <ModalForm<AbsenceForm>
        title="Abwesenheit eintragen"
        open={absence}
        width={480}
        initialValues={{ kind: 'vacation' }}
        modalProps={{ destroyOnHidden: true, onCancel: () => setAbsence(false) }}
        submitter={{ searchConfig: { submitText: 'Eintragen', resetText: 'Abbrechen' } }}
        onOpenChange={setAbsence}
        onFinish={saveAbsence}
      >
        <ProFormSelect name="userId" label="Mitarbeiter" options={active} showSearch rules={[{ required: true, message: 'Bitte einen Mitarbeiter wählen.' }]} />
        <ProFormSelect name="kind" label="Art" options={options(absenceKindLabels)} allowClear={false} />
        <ProFormDateRangePicker name="range" label="Zeitraum" fieldProps={{ format: 'DD.MM.YYYY' }} rules={[{ required: true, message: 'Bitte den Zeitraum angeben.' }]} />
      </ModalForm>
    </PageContainer>
  );
}
