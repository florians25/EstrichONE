import { LockOutlined, MessageOutlined, SendOutlined, UserOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Checkbox, Col, Collapse, Descriptions, Form, Image, Input, Result, Row, Select, Space, Spin, Tag, Timeline, Typography } from 'antd';
import { useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { get, patch, post, problemMessage } from '../api/client';
import type { DirectoryUser, TicketCategory, TicketDetail, TicketPriority, TicketStatus } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { formatDateTime, ticketCategoryLabels, ticketPriorityLabels, ticketStatusLabels, TicketStatusTag } from '../shared/status';

const eventTexts: Record<string, string> = {
  created: 'hat das Problem gemeldet', status: 'hat den Status geändert', assignee: 'hat die Zuständigkeit geändert',
  priority: 'hat die Priorität geändert', category: 'hat die Kategorie geändert',
};
/** Ereignisse speichern die C#-Namen (`InProgress`), die API liefert sonst camelCase. */
const camel = (value: string) => value.charAt(0).toLowerCase() + value.slice(1);

export function describeEvent(kind: string, to: string | null): string {
  const base = eventTexts[kind] ?? kind;
  if (kind === 'status' && to) {
    return `${base}: ${ticketStatusLabels[camel(to) as TicketStatus] ?? to}`;
  }
  if (kind === 'priority' && to) {
    return `${base}: ${ticketPriorityLabels[camel(to) as TicketPriority] ?? to}`;
  }
  return base;
}

const options = <K extends string>(labels: Record<K, string>) => (Object.keys(labels) as K[]).map((k) => ({ value: k, label: labels[k] }));

interface Edit {
  status: TicketStatus;
  priority: TicketPriority;
  category: TicketCategory;
  assignee: string | null;
  resolution: string;
}

/** Ticket bearbeiten: Verlauf (öffentlich/intern), Diagnosedaten, Zuweisen, Status, Lösung. */
export default function TicketPage() {
  const { id = '' } = useParams();
  const { user } = useAuth();
  const { message } = App.useApp();
  const queries = useQueryClient();
  const ticket = useQuery({ queryKey: ['ticket', id], queryFn: () => get<TicketDetail>(`tickets/${id}`) });
  const people = useQuery({ queryKey: ['directory'], queryFn: () => get<DirectoryUser[]>('directory/users'), staleTime: 60_000 });
  const [edit, setEdit] = useState<Edit | null>(null);
  const [reply, setReply] = useState('');
  const [internal, setInternal] = useState(false);
  const t = ticket.data;

  useEffect(() => {
    if (t) {
      setEdit({ status: t.summary.status, priority: t.summary.priority, category: t.summary.category, assignee: t.summary.assigneeUserId, resolution: t.resolution ?? '' });
    }
  }, [t]);

  const timeline = useMemo(() => {
    if (!t) {
      return [];
    }
    const comments = t.comments.map((c) => ({ at: c.createdAt, who: c.authorName ?? '–', text: c.text, internal: c.isInternal, event: false }));
    const events = t.events.map((e) => ({
      at: e.at, who: e.actorName ?? (e.kind === 'created' ? (t.summary.reporterDisplayName ?? 'Gerät') : 'System'),
      text: describeEvent(e.kind, e.toValue), internal: false, event: true,
    }));
    return [...comments, ...events].sort((a, b) => a.at.localeCompare(b.at));
  }, [t]);

  if (ticket.isLoading || !edit) {
    return ticket.error
      ? <Result status="404" title="Ticket nicht gefunden" subTitle={problemMessage(ticket.error)} extra={<Link to="/it-center/helpdesk">Zum Helpdesk</Link>} />
      : <Spin style={{ display: 'block', marginTop: 80 }} />;
  }
  if (!t) {
    return null;
  }

  const apply = (updated: TicketDetail) => {
    queries.setQueryData(['ticket', id], updated);
    void queries.invalidateQueries({ queryKey: ['tickets'] });
  };

  const save = async (next: Edit) => {
    try {
      apply(await patch<TicketDetail>(`tickets/${t.summary.id}`, {
        status: next.status !== t.summary.status ? next.status : null,
        priority: next.priority !== t.summary.priority ? next.priority : null,
        category: next.category !== t.summary.category ? next.category : null,
        assigneeUserId: next.assignee && next.assignee !== t.summary.assigneeUserId ? next.assignee : null,
        unassign: !next.assignee && !!t.summary.assigneeUserId,
        resolution: next.resolution || null,
      }));
      message.success('Ticket gespeichert');
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const comment = async () => {
    try {
      apply(await post<TicketDetail>(`tickets/${t.summary.id}/comments`, { text: reply, isInternal: internal }));
      setReply('');
      setInternal(false);
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const diagnostics = Object.entries(t.diagnostics ?? {}).map(([key, value]) => ({
    key, label: key,
    children: <span className="pre">{Array.isArray(value) ? value.map((v) => (typeof v === 'object' ? JSON.stringify(v) : String(v))).join('\n') : String(value)}</span>,
  }));
  const needsResolution = edit.status === 'resolved' || edit.status === 'closed';

  return (
    <PageContainer
      title={t.summary.title}
      breadcrumb={{ items: [{ title: <Link to="/it-center/helpdesk">Helpdesk</Link> }, { title: t.summary.number }] }}
      tags={<Space size={4}><Tag bordered={false} className="tabular">{t.summary.number}</Tag><TicketStatusTag status={t.summary.status} />
        {t.summary.reporterBlocked && <Tag color="error" bordered={false}>Kann nicht arbeiten</Tag>}</Space>}
    >
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={16}>
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            {t.description && <Card size="small" title="Beschreibung"><Typography.Paragraph className="pre" style={{ margin: 0 }}>{t.description}</Typography.Paragraph></Card>}
            <Card size="small" title="Verlauf">
              <Timeline
                items={timeline.map((item) => ({
                  color: item.internal ? 'orange' : item.event ? 'gray' : 'blue',
                  dot: item.event ? undefined : item.internal ? <LockOutlined /> : <MessageOutlined />,
                  children: (
                    <div className={item.internal ? 'ticket-internal' : undefined}>
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {item.who} · {formatDateTime(item.at)}{item.internal && <> · <strong>Interne Notiz</strong></>}
                      </Typography.Text>
                      <div className="pre" style={item.event ? { color: 'rgba(0,0,0,.55)', fontStyle: 'italic' } : undefined}>{item.text}</div>
                    </div>
                  ),
                }))}
              />
              <Space direction="vertical" style={{ width: '100%' }}>
                <Input.TextArea aria-label="Antwort oder Notiz" rows={3} placeholder="Antwort oder interne Notiz …" value={reply} onChange={(e) => setReply(e.target.value)} />
                <Space style={{ justifyContent: 'space-between', width: '100%' }}>
                  <Checkbox checked={internal} onChange={(e) => setInternal(e.target.checked)}>Interne Notiz (für den Melder unsichtbar)</Checkbox>
                  <Button type="primary" icon={internal ? <LockOutlined /> : <SendOutlined />} disabled={!reply.trim()} onClick={comment}>
                    {internal ? 'Notiz speichern' : 'Antwort senden'}
                  </Button>
                </Space>
              </Space>
            </Card>
          </Space>
        </Col>
        <Col xs={24} lg={8}>
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Card size="small" title="Bearbeitung">
              <Form layout="vertical" requiredMark={false}>
                <Form.Item label="Status"><Select aria-label="Status" value={edit.status} onChange={(status) => setEdit({ ...edit, status })} options={options(ticketStatusLabels)} /></Form.Item>
                {needsResolution && (
                  <Form.Item label="Lösung (Pflicht)">
                    <Input.TextArea aria-label="Lösung" rows={3} value={edit.resolution} onChange={(e) => setEdit({ ...edit, resolution: e.target.value })} />
                  </Form.Item>
                )}
                <Form.Item label="Priorität"><Select aria-label="Priorität" value={edit.priority} onChange={(priority) => setEdit({ ...edit, priority })} options={options(ticketPriorityLabels)} /></Form.Item>
                <Form.Item label="Kategorie"><Select aria-label="Kategorie" value={edit.category} onChange={(category) => setEdit({ ...edit, category })} options={options(ticketCategoryLabels)} /></Form.Item>
                <Form.Item label="Zuständig">
                  <Select aria-label="Zuständig" allowClear placeholder="Niemand" value={edit.assignee} onChange={(v) => setEdit({ ...edit, assignee: v ?? null })}
                    options={(people.data ?? []).filter((p) => p.isActive).map((p) => ({ value: p.id, label: p.displayName }))} />
                </Form.Item>
                <Space>
                  {user && edit.assignee !== user.id && (
                    <Button icon={<UserOutlined />} onClick={() => { const next = { ...edit, assignee: user.id }; setEdit(next); void save(next); }}>Mir zuweisen</Button>
                  )}
                  <Button type="primary" onClick={() => void save(edit)}>Speichern</Button>
                </Space>
              </Form>
            </Card>
            <Card size="small" title="Details">
              <Descriptions column={1} size="small" items={[
                { key: 'reporter', label: 'Melder', children: t.summary.reporterDisplayName ?? '–' },
                { key: 'device', label: 'Gerät', children: t.deviceId ? <Link to="/it-center/geraete">{t.deviceName ?? 'Gerät'}</Link> : '–' },
                { key: 'bauakte', label: 'Bauakte', children: t.bauakteId ? <Link to={`/bauakten/${t.bauakteId}`}>öffnen</Link> : '–' },
                { key: 'created', label: 'Gemeldet', children: `${formatDateTime(t.summary.createdAt)} · ${t.summary.source}` },
              ]} />
              {t.attachmentFileIds.length > 0 && (
                <Image.PreviewGroup>
                  <Space wrap style={{ marginTop: 8 }}>
                    {t.attachmentFileIds.map((f) => (
                      <Image key={f} width={88} height={88} style={{ objectFit: 'cover', borderRadius: 6 }} alt="Anhang"
                        src={`/api/v1/files/${f}/content?variant=thumb`} preview={{ src: `/api/v1/files/${f}/content` }} />
                    ))}
                  </Space>
                </Image.PreviewGroup>
              )}
            </Card>
            {diagnostics.length > 0 && (
              <Collapse size="small" items={[{ key: 'd', label: 'Diagnosedaten', children: <Descriptions column={1} size="small" items={diagnostics} /> }]} />
            )}
          </Space>
        </Col>
      </Row>
    </PageContainer>
  );
}
