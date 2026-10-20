import { CalendarOutlined, CheckOutlined, InboxOutlined, PhoneOutlined, UserDeleteOutlined, WarningOutlined } from '@ant-design/icons';
import { PageContainer, StatisticCard } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Col, Empty, List, Row, Space, Tag, Typography } from 'antd';
import { Link } from 'react-router-dom';
import { get, post, problemMessage } from '../api/client';
import type { BauaktePage, CallResponse, OrderResponse, PlanningRange, WorkItemDto } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { isoDay, parseDay } from '../shared/days';
import { BauakteStatusTag, formatDateTime } from '../shared/status';
import { OrderStatusTag, quantity } from './materials/shared';
import { caller, callTime, InquiryStatusTag } from './office/calls';
import { absenceNames, kindColors, kindIcons, kindLabels, timeRange } from './planning/shared';

/** Tagesübersicht: was heute ansteht – Einsätze je Kolonne, Abwesenheiten, Wiedervorlagen, Anfragen, eigene Aufgaben. */
export default function TodayPage() {
  const { has } = useAuth();
  const { message } = App.useApp();
  const queries = useQueryClient();
  const today = isoDay(new Date());
  const planning = useQuery({ queryKey: ['planning', today, today], enabled: has('planning.read'), queryFn: () => get<PlanningRange>('planning/range', { from: today, to: today }) });
  const due = useQuery({ queryKey: ['calls', 'due'], enabled: has('contacts.read'), queryFn: () => get<CallResponse[]>('calls', { due: true }) });
  const inquiries = useQuery({ queryKey: ['calls', 'inquiries-open'], enabled: has('contacts.read'), queryFn: () => get<CallResponse[]>('calls', { kind: 'inquiry', open: true, limit: 6 }) });
  const work = useQuery({ queryKey: ['work', 'mine'], enabled: has('work.read'), queryFn: () => get<WorkItemDto[]>('work-items/mine') });
  const deliveries = useQuery({ queryKey: ['material-orders', 'today', today], enabled: has('materials.read'), queryFn: () => get<OrderResponse[]>('material-orders', { from: today, to: today }) });
  const recent = useQuery({ queryKey: ['bauakten', 'recent'], queryFn: () => get<BauaktePage>('bauakten', { limit: 8 }) });

  const assignments = planning.data?.assignments ?? [];
  const absences = planning.data?.absences ?? [];
  const warnings = assignments.filter((a) => a.warnings.length > 0).length;
  const byCrew = [...new Map(assignments.map((a) => [a.crewName ?? 'Ohne Kolonne', assignments.filter((x) => (x.crewName ?? 'Ohne Kolonne') === (a.crewName ?? 'Ohne Kolonne'))])).entries()];
  const openWork = (work.data ?? []).filter((w) => w.status !== 'done');

  const done = async (c: CallResponse) => {
    try {
      await post(`calls/${c.id}/follow-up-done`);
      void queries.invalidateQueries({ queryKey: ['calls'] });
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <PageContainer
      title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">Bauakten</Typography.Text>Tagesübersicht</Space>}
      subTitle={parseDay(today).toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' })}
    >
      <StatisticCard.Group direction="row" style={{ marginBottom: 16 }}>
        <StatisticCard statistic={{ title: 'Einsätze heute', value: has('planning.read') ? assignments.length : '–', icon: <CalendarOutlined className="stat-icon" /> }} />
        <StatisticCard statistic={{ title: 'Abwesend', value: has('planning.read') ? absences.length : '–', icon: <UserDeleteOutlined className="stat-icon" /> }} />
        <StatisticCard statistic={{ title: 'Hinweise in der Planung', value: warnings, icon: <WarningOutlined className="stat-icon" />, status: warnings ? 'warning' : 'default' }} />
        <StatisticCard statistic={{ title: 'Wiedervorlagen fällig', value: has('contacts.read') ? due.data?.length ?? '–' : '–', icon: <PhoneOutlined className="stat-icon" /> }} />
        <StatisticCard statistic={{ title: 'Offene Anfragen', value: has('contacts.read') ? inquiries.data?.length ?? '–' : '–', icon: <InboxOutlined className="stat-icon" /> }} />
      </StatisticCard.Group>
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={14}>
          <Card size="small" title="Einsätze heute" extra={<Link to="/planung/tageseinteilung">Tageseinteilung</Link>} loading={planning.isLoading}>
            {assignments.length === 0
              ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Heute ist nichts eingeplant." />
              : byCrew.map(([crew, items]) => (
                <div key={crew} className="today-crew">
                  <div className="section-title">{crew}</div>
                  <List size="small" dataSource={items} renderItem={(a) => (
                    <List.Item extra={a.warnings.length > 0 && <Tag color="warning" bordered={false} title={a.warnings.join(' ')}>{a.warnings.length} Hinweise</Tag>}>
                      <List.Item.Meta
                        avatar={<span style={{ color: kindColors[a.kind], fontSize: 16 }}>{kindIcons[a.kind]}</span>}
                        title={<Space wrap size={6}><span className="tabular">{timeRange(a) || 'ganztags'}</span><Link to={`/bauakten/${a.bauakteId}`}>{a.bauakteNumber} {a.bauakteTitle}</Link><Tag bordered={false}>{kindLabels[a.kind]}</Tag></Space>}
                        description={<span>{a.activity}{a.bauakteAddress && ` · ${a.bauakteAddress}`} · {a.people.map((p) => p.name).join(', ')}{a.resources.length > 0 && ` · ${a.resources.map((r) => r.name).join(', ')}`}</span>}
                      />
                    </List.Item>
                  )} />
                </div>
              ))}
          </Card>
        </Col>
        <Col xs={24} xl={10}>
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            {absences.length > 0 && (
              <Card size="small" title="Heute abwesend">
                <Space wrap>{absences.map((a) => <Tag key={a.id} color="orange" bordered={false}>{a.userName} · {absenceNames[a.kind] ?? a.kind}</Tag>)}</Space>
              </Card>
            )}
            {has('materials.read') && (
              <Card size="small" title="Lieferungen heute" extra={<Link to="/bestellungen">Alle Bestellungen</Link>} loading={deliveries.isLoading}>
                <List size="small" dataSource={(deliveries.data ?? []).filter((o) => o.status !== 'cancelled')} locale={{ emptyText: 'Heute keine Lieferungen.' }} renderItem={(o) => (
                  <List.Item extra={<OrderStatusTag status={o.status} />}>
                    <List.Item.Meta
                      title={<Space wrap size={6}>{o.deliveryTime && <span className="tabular">{o.deliveryTime}</span>}<Link to={`/bestellungen?bestellung=${o.id}`}>{o.supplierName}</Link>
                        {o.bauakteId && <Link to={`/bauakten/${o.bauakteId}?reiter=material`}>{o.bauakteNumber} {o.bauakteTitle}</Link>}</Space>}
                      description={o.lines.map((l) => `${quantity(l.quantity)} ${l.unit} ${l.description}`).join(' · ')} />
                  </List.Item>
                )} />
              </Card>
            )}
            {has('contacts.read') && (
              <Card size="small" title="Fällige Wiedervorlagen" extra={<Link to="/anrufe">Alle Anrufe</Link>} loading={due.isLoading}>
                <List size="small" dataSource={due.data ?? []} locale={{ emptyText: 'Keine fälligen Wiedervorlagen.' }} renderItem={(c) => (
                  <List.Item actions={has('contacts.edit') ? [<Button key="done" size="small" type="link" icon={<CheckOutlined />} onClick={() => void done(c)}>Erledigt</Button>] : []}>
                    <List.Item.Meta title={<Space>{caller(c)}{c.callerPhone && <a href={`tel:${c.callerPhone}`}>{c.callerPhone}</a>}</Space>}
                      description={<>{c.subject ?? c.notes?.slice(0, 80) ?? '–'}<br /><Typography.Text type="secondary" style={{ fontSize: 12 }}>{callTime(c.calledAt)}{c.nextStep && ` · ${c.nextStep}`}</Typography.Text></>} />
                  </List.Item>
                )} />
              </Card>
            )}
            {has('contacts.read') && (
              <Card size="small" title="Offene Anfragen" extra={<Link to="/anfragen">Alle Anfragen</Link>} loading={inquiries.isLoading}>
                <List size="small" dataSource={inquiries.data ?? []} locale={{ emptyText: 'Keine offenen Anfragen.' }} renderItem={(c) => (
                  <List.Item extra={<InquiryStatusTag status={c.inquiryStatus} />}>
                    <List.Item.Meta title={caller(c)} description={c.subject ?? '–'} />
                  </List.Item>
                )} />
              </Card>
            )}
            {has('work.read') && (
              <Card size="small" title="Meine offenen Aufgaben" loading={work.isLoading}>
                <List size="small" dataSource={openWork.slice(0, 8)} locale={{ emptyText: 'Keine offenen Aufgaben.' }} renderItem={(w) => (
                  <List.Item extra={w.dueDate && <Typography.Text type={w.dueDate < today ? 'danger' : 'secondary'} className="tabular">{parseDay(w.dueDate).toLocaleDateString('de-DE')}</Typography.Text>}>
                    <Space>{w.kind === 'defect' ? <WarningOutlined style={{ color: '#FA8C16' }} /> : <CheckOutlined style={{ color: '#8C8C8C' }} />}
                      {w.bauakteId ? <Link to={`/bauakten/${w.bauakteId}?reiter=aufgaben`}>{w.title}</Link> : w.title}</Space>
                  </List.Item>
                )} />
              </Card>
            )}
            <Card size="small" title="Zuletzt geänderte Bauakten" extra={<Link to="/bauakten">Alle Bauakten</Link>} loading={recent.isLoading}>
              <List size="small" dataSource={recent.data?.items ?? []} renderItem={(b) => (
                <List.Item extra={<BauakteStatusTag status={b.status} />}>
                  <List.Item.Meta title={<Link to={`/bauakten/${b.id}`}>{b.number} {b.title}</Link>} description={formatDateTime(b.updatedAt)} />
                </List.Item>
              )} />
            </Card>
          </Space>
        </Col>
      </Row>
    </PageContainer>
  );
}
