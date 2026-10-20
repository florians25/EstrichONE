import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { DatePicker, Descriptions, Select, Space, Tag, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { get } from '../api/client';
import type { AuditLogItem, AuditLogPage, DirectoryUser } from '../api/models';
import { formatDateTime } from '../shared/status';

/** Bekannte Ereignisgruppen für den Filter (Präfix der Aktion). */
const groups = [
  { value: 'auth.', label: 'Anmeldung und Sitzungen' },
  { value: 'bauakte.', label: 'Bauakten' },
  { value: 'users.', label: 'Benutzer' },
  { value: 'access.', label: 'Rollen und Rechte' },
  { value: 'devices.', label: 'Geräte' },
  { value: 'backup.', label: 'Sicherung' },
  { value: 'setup.', label: 'Einrichtung' },
];

const actionTexts: Record<string, string> = {
  'auth.login.succeeded': 'Anmeldung', 'auth.login.failed': 'Anmeldung fehlgeschlagen', 'auth.login.locked_out': 'Konto vorübergehend gesperrt',
  'auth.logout': 'Abmeldung', 'auth.refresh.reused': 'Wiederverwendetes Sitzungs-Token (Sitzung beendet)',
  'auth.password.changed': 'Passwort geändert', 'auth.session.revoked': 'Sitzung beendet',
  'users.created': 'Benutzer angelegt', 'users.updated': 'Benutzer geändert', 'users.password_reset': 'Passwort zurückgesetzt',
  'users.activated': 'Benutzer entsperrt', 'users.deactivated': 'Benutzer gesperrt', 'access.user_roles.changed': 'Rollen geändert',
  'devices.registered': 'Gerät registriert', 'devices.attached': 'Gerät verknüpft', 'devices.enrollment_code_created': 'Einrichtungscode erzeugt',
  'devices.enrollment_code_rejected': 'Einrichtungscode abgelehnt', 'devices.approved': 'Gerät freigegeben', 'devices.locked': 'Gerät gesperrt',
  'devices.unlocked': 'Gerät entsperrt', 'devices.retired': 'Gerät entfernt',
  'backup.run_requested': 'Sicherung angefordert', 'setup.completed': 'Ersteinrichtung abgeschlossen',
  'bauakte.created': 'Bauakte angelegt', 'bauakte.updated': 'Bauakte geändert', 'bauakte.deleted': 'Bauakte gelöscht',
};

export function actionColor(action: string): string {
  if (action.includes('failed') || action.includes('reused') || action.includes('locked')) return 'red';
  if (action.startsWith('auth.')) return 'blue';
  if (action.startsWith('bauakte.')) return 'geekblue';
  if (action.startsWith('devices.')) return 'purple';
  if (action.startsWith('users.') || action.startsWith('access.')) return 'gold';
  return 'default';
}

/** System › Protokoll: das nur anfügbare Audit-Log, gefiltert und seitenweise. */
export default function AuditPage() {
  const action = useRef<ActionType>(undefined);
  const cursors = useRef<Record<number, string | null>>({ 1: null });
  const people = useQuery({ queryKey: ['directory'], queryFn: () => get<DirectoryUser[]>('directory/users'), staleTime: 60_000 });
  const [group, setGroup] = useState<string | null>(null);
  const [actor, setActor] = useState<string | null>(null);
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);

  useEffect(() => {
    cursors.current = { 1: null };
    action.current?.reloadAndRest?.();
  }, [group, actor, range]);

  const columns: ProColumns<AuditLogItem>[] = [
    { title: 'Zeitpunkt', dataIndex: 'occurredAt', width: 170, render: (_, e) => <span className="tabular">{formatDateTime(e.occurredAt)}</span> },
    {
      title: 'Ereignis', dataIndex: 'action', render: (_, e) => (
        <Space direction="vertical" size={0}>
          <span>{actionTexts[e.action] ?? e.action}</span>
          {actionTexts[e.action] && <Typography.Text type="secondary" style={{ fontSize: 12 }} code>{e.action}</Typography.Text>}
        </Space>
      ),
    },
    { title: 'Bereich', key: 'group', width: 120, render: (_, e) => <Tag color={actionColor(e.action)} bordered={false}>{e.action.split('.')[0]}</Tag> },
    { title: 'Person', dataIndex: 'actorName', width: 170, render: (_, e) => e.actorName ?? <Typography.Text type="secondary">System</Typography.Text> },
    {
      title: 'Objekt', key: 'entity', width: 200, render: (_, e) => e.entityType === 'bauakte' && e.entityId
        ? <Link to={`/bauakten/${e.entityId}`}>Bauakte öffnen</Link>
        : e.entityType ? <Typography.Text type="secondary">{e.entityType}</Typography.Text> : '–',
    },
    { title: 'IP-Adresse', dataIndex: 'ipAddress', width: 140, render: (_, e) => <span className="tabular">{e.ipAddress ?? '–'}</span> },
  ];

  return (
    <PageContainer title="Protokoll" subTitle="Nachvollziehbar, wer wann was getan hat. Einträge lassen sich nicht ändern oder löschen.">
      <ProTable<AuditLogItem>
        actionRef={action}
        rowKey="id"
        size="small"
        bordered
        cardBordered
        search={false}
        columns={columns}
        pagination={{ pageSize: 100, showSizeChanger: false }}
        options={{ reload: true, density: true, setting: true }}
        expandable={{
          rowExpandable: (e) => Object.keys(e.details).length > 0,
          expandedRowRender: (e) => (
            <Descriptions size="small" column={2} items={Object.entries(e.details).map(([k, v]) => ({ key: k, label: k, children: v ?? '–' }))} />
          ),
        }}
        toolbar={{
          actions: [
            <Select key="group" allowClear placeholder="Alle Bereiche" style={{ width: 210 }} options={groups} value={group}
              onChange={(v) => setGroup(v ?? null)} aria-label="Bereich" />,
            <Select key="actor" allowClear showSearch optionFilterProp="label" placeholder="Alle Personen" style={{ width: 200 }} value={actor}
              onChange={(v) => setActor(v ?? null)} aria-label="Person"
              options={(people.data ?? []).map((p) => ({ value: p.id, label: p.displayName }))} />,
            <DatePicker.RangePicker key="range" format="DD.MM.YYYY" value={range} onChange={(v) => setRange(v)} aria-label="Zeitraum" />,
          ],
        }}
        request={async ({ current = 1 }) => {
          const page = await get<AuditLogPage>('audit', {
            limit: 100, action: group, actorUserId: actor, cursor: cursors.current[current] ?? null,
            from: range?.[0]?.startOf('day').toISOString(), to: range?.[1]?.add(1, 'day').startOf('day').toISOString(),
          });
          cursors.current[current + 1] = page.nextCursor;
          return { data: page.items, success: true, total: (current - 1) * 100 + page.items.length + (page.nextCursor ? 1 : 0) };
        }}
      />
    </PageContainer>
  );
}
