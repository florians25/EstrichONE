import {
  AppstoreAddOutlined, CalendarOutlined, CarOutlined, CloudServerOutlined, CustomerServiceOutlined, FileAddOutlined, InboxOutlined,
  KeyOutlined, MobileOutlined, ProjectOutlined, ScheduleOutlined, TeamOutlined, ToolOutlined, UserOutlined, WarningOutlined,
} from '@ant-design/icons';
import { PageContainer, ProCard, StatisticCard } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { Badge, Card, Col, Empty, List, Row, Space, Tag, Typography } from 'antd';
import type { ReactNode } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { get } from '../api/client';
import type { BackupStatusResponse, BauaktePage, PlanningRange, TicketSummary } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { isoDay } from '../shared/days';
import { formatDateTime } from '../shared/status';
import { brand } from '../theme';

interface Tile {
  label: string;
  icon: ReactNode;
  to: string;
  permission?: string;
}

const groups: { label: string; tiles: Tile[] }[] = [
  {
    label: 'Auftragsbearbeitung',
    tiles: [
      { label: 'Anfragen', icon: <InboxOutlined />, to: '/bauakten?status=inquiry', permission: 'bauakten.read' },
      { label: 'Bauakten', icon: <ProjectOutlined />, to: '/bauakten', permission: 'bauakten.read' },
      { label: 'Neue Bauakte', icon: <FileAddOutlined />, to: '/bauakten?neu=bauakte', permission: 'bauakten.create' },
      { label: 'Neue Anfrage', icon: <AppstoreAddOutlined />, to: '/bauakten?neu=anfrage', permission: 'bauakten.create' },
    ],
  },
  {
    label: 'Planung',
    tiles: [
      { label: 'Wochenplan', icon: <CalendarOutlined />, to: '/planung', permission: 'planning.read' },
      { label: 'Kolonnen', icon: <TeamOutlined />, to: '/planung/ressourcen?reiter=kolonnen', permission: 'planning.read' },
      { label: 'Fahrzeuge und Geräte', icon: <CarOutlined />, to: '/planung/ressourcen?reiter=geraete', permission: 'planning.read' },
      { label: 'Abwesenheiten', icon: <ScheduleOutlined />, to: '/planung/ressourcen?reiter=abwesenheiten', permission: 'planning.read' },
    ],
  },
  { label: 'Stammdaten', tiles: [{ label: 'Kunden', icon: <TeamOutlined />, to: '/kunden', permission: 'contacts.read' }] },
  {
    label: 'IT Center',
    tiles: [
      { label: 'Benutzer', icon: <UserOutlined />, to: '/verwaltung/benutzer', permission: 'users.manage' },
      { label: 'Geräte', icon: <MobileOutlined />, to: '/it-center/geraete', permission: 'devices.read' },
      { label: 'Helpdesk', icon: <CustomerServiceOutlined />, to: '/it-center/helpdesk', permission: 'helpdesk.agent' },
    ],
  },
  {
    label: 'System',
    tiles: [
      { label: 'Sicherung', icon: <CloudServerOutlined />, to: '/system/sicherung', permission: 'backup.manage' },
      { label: 'Passwort ändern', icon: <KeyOutlined />, to: '/konto/passwort' },
    ],
  },
];

/** Startseite: Kennzahlen, Programmkacheln nach Fachgruppen, heutige Einsätze und Sicherung. */
export default function StartPage() {
  const { has, user } = useAuth();
  const navigate = useNavigate();
  const today = isoDay(new Date());

  const assignments = useQuery({
    queryKey: ['planning', today], enabled: has('planning.read'),
    queryFn: () => get<PlanningRange>('planning/range', { from: today, to: today }),
  });
  const inquiries = useQuery({
    queryKey: ['bauakten', 'inquiry-count'], enabled: has('bauakten.read'),
    queryFn: () => get<BauaktePage>('bauakten', { status: 'inquiry', limit: 100 }),
  });
  const running = useQuery({
    queryKey: ['bauakten', 'running-count'], enabled: has('bauakten.read'),
    queryFn: () => get<BauaktePage>('bauakten', { status: 'inProgress', limit: 100 }),
  });
  const tickets = useQuery({
    queryKey: ['tickets', 'open'], enabled: has('helpdesk.agent'),
    queryFn: () => get<TicketSummary[]>('tickets', { filter: 'open' }),
  });
  const backup = useQuery({ queryKey: ['backup'], enabled: has('backup.manage'), queryFn: () => get<BackupStatusResponse>('backup/status') });

  const count = (page?: BauaktePage) => (page ? (page.nextCursor ? '100+' : page.items.length) : '–');
  const visible = groups
    .map((g) => ({ ...g, tiles: g.tiles.filter((t) => !t.permission || has(t.permission)) }))
    .filter((g) => g.tiles.length > 0);
  const blocked = tickets.data?.filter((t) => t.reporterBlocked).length ?? 0;

  return (
    <PageContainer
      title={`Guten Tag, ${user?.displayName ?? ''}`}
      subTitle={new Date().toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' })}
    >
      <StatisticCard.Group direction="row" style={{ marginBottom: 16 }}>
        {has('planning.read') && <StatisticCard statistic={{ title: 'Einsätze heute', value: assignments.data?.assignments.length ?? '–', icon: <CalendarOutlined style={{ color: brand.navy, fontSize: 28 }} /> }} />}
        {has('bauakten.read') && <StatisticCard statistic={{ title: 'Offene Anfragen', value: count(inquiries.data), icon: <InboxOutlined style={{ color: brand.navy, fontSize: 28 }} /> }} onClick={() => navigate('/bauakten?status=inquiry')} />}
        {has('bauakten.read') && <StatisticCard statistic={{ title: 'Baustellen in Ausführung', value: count(running.data), icon: <ToolOutlined style={{ color: brand.navy, fontSize: 28 }} /> }} />}
        {has('helpdesk.agent') && (
          <StatisticCard statistic={{
            title: 'Offene Tickets', value: tickets.data?.length ?? '–', icon: <CustomerServiceOutlined style={{ color: brand.navy, fontSize: 28 }} />,
            description: blocked > 0 ? <Typography.Text type="danger"><WarningOutlined /> {blocked} können nicht arbeiten</Typography.Text> : undefined,
          }} />
        )}
      </StatisticCard.Group>

      <Row gutter={[16, 16]}>
        <Col xs={24} xl={16}>
          {visible.map((group) => (
            <ProCard key={group.label} title={group.label} headerBordered style={{ marginBottom: 16 }} bodyStyle={{ padding: 16 }}>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12 }}>
                {group.tiles.map((tile) => (
                  <Card key={tile.label} hoverable size="small" onClick={() => navigate(tile.to)} role="link" aria-label={tile.label}
                    tabIndex={0} onKeyDown={(e) => { if (e.key === 'Enter') navigate(tile.to); }}
                    style={{ width: 118, textAlign: 'center' }} styles={{ body: { padding: '16px 8px 12px' } }}>
                    <div style={{ fontSize: 30, color: brand.navy, lineHeight: 1 }}>{tile.icon}</div>
                    <div style={{ marginTop: 10, fontSize: 13, lineHeight: '16px', minHeight: 32 }}>{tile.label}</div>
                  </Card>
                ))}
              </div>
            </ProCard>
          ))}
        </Col>
        <Col xs={24} xl={8}>
          {has('planning.read') && (
            <ProCard title="Einsätze heute" headerBordered extra={<Link to="/planung">Wochenplan</Link>} style={{ marginBottom: 16 }}>
              <List
                dataSource={assignments.data?.assignments ?? []}
                locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Heute ist nichts eingeplant." /> }}
                renderItem={(a) => (
                  <List.Item onClick={() => navigate(`/bauakten/${a.bauakteId}`)} style={{ cursor: 'pointer' }}>
                    <List.Item.Meta
                      avatar={<Typography.Text strong className="tabular" style={{ color: brand.navy }}>{a.startTime?.slice(0, 5) ?? '–'}</Typography.Text>}
                      title={<Space>{a.bauakteNumber}<span>{a.bauakteTitle}</span>{a.warnings.length > 0 && <Tag color="warning">{a.warnings.length} Hinweise</Tag>}</Space>}
                      description={[a.activity, a.crewName].filter(Boolean).join(' · ')}
                    />
                  </List.Item>
                )}
              />
            </ProCard>
          )}
          {has('backup.manage') && backup.data && (
            <ProCard title="Sicherung" headerBordered extra={<Link to="/system/sicherung">Öffnen</Link>}>
              {backup.data.lastSuccess
                ? <Space direction="vertical"><Badge status={backup.data.healthy ? 'success' : 'warning'} text={backup.data.healthy ? 'Aktuell' : 'Veraltet'} />
                  <Typography.Text type="secondary">Letzte erfolgreiche Sicherung: {formatDateTime(backup.data.lastSuccess.at)}</Typography.Text></Space>
                : <Badge status="error" text="Bisher wurde keine Sicherung abgeschlossen." />}
            </ProCard>
          )}
        </Col>
      </Row>
    </PageContainer>
  );
}
