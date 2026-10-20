import { CheckOutlined, DeleteOutlined, EditOutlined, PhoneOutlined, PlusOutlined, PrinterOutlined, ReloadOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Empty, Input, Popconfirm, Space, Switch, Tag, Typography } from 'antd';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { del, get, post, problemMessage } from '../../api/client';
import type { CallResponse } from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { parseDay } from '../../shared/days';
import { useQuickCall } from '../../layout/QuickCall';
import { CallFormModal, callTime, caller, DirectionIcon } from './calls';

/** Anrufe: Gesprächsnotizen mit Wiedervorlage, Verknüpfung zu Kunde und Bauakte (wie Estrich Web „Anrufe“). */
export default function CallsPage() {
  const { has } = useAuth();
  const { message } = App.useApp();
  const queries = useQueryClient();
  const quick = useQuickCall();
  const [search, setSearch] = useState('');
  const [onlyDue, setOnlyDue] = useState(false);
  const [editing, setEditing] = useState<CallResponse | 'new' | null>(null);
  const calls = useQuery({ queryKey: ['calls', 'call', search, onlyDue], queryFn: () => get<CallResponse[]>('calls', { kind: 'call', search: search || undefined, due: onlyDue || undefined }) });
  const canWrite = has('contacts.edit');
  const refresh = () => void queries.invalidateQueries({ queryKey: ['calls'] });
  const today = new Date().toISOString().slice(0, 10);

  const done = async (c: CallResponse) => {
    try {
      await post(`calls/${c.id}/follow-up-done`);
      refresh();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <PageContainer
      title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">Büro</Typography.Text>Anrufe</Space>}
      extra={[
        canWrite && <Button key="quick" type="primary" icon={<PhoneOutlined />} onClick={() => quick.open()}>Schnellerfassung</Button>,
        canWrite && <Button key="full" icon={<PlusOutlined />} onClick={() => setEditing('new')}>Anruf mit allen Feldern</Button>,
        <Button key="print" icon={<PrinterOutlined />} disabled={!calls.data?.length} onClick={() => window.print()}>Liste drucken</Button>,
        <Button key="reload" icon={<ReloadOutlined />} onClick={() => void calls.refetch()}>Aktualisieren</Button>,
      ].filter(Boolean)}
    >
      <Card size="small" style={{ marginBottom: 12 }}>
        <Space wrap size={16}>
          <Input.Search allowClear placeholder="Name, Firma, Telefon, Betreff" style={{ width: 320 }} aria-label="Anrufe durchsuchen" onSearch={setSearch} />
          <Space><Switch size="small" checked={onlyDue} onChange={setOnlyDue} aria-label="Nur fällige Wiedervorlagen" />Nur fällige Wiedervorlagen</Space>
        </Space>
      </Card>
      <Card size="small" loading={calls.isLoading} title={`${calls.data?.length ?? 0} Anrufe`}>
        {calls.data?.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Keine Anrufe in dieser Ansicht." />}
        <div className="call-list">
          {(calls.data ?? []).map((c) => (
            <article key={c.id} className={`call-row${c.followUp ? ' follow-up' : ''}`}>
              <DirectionIcon direction={c.direction} />
              <div className="call-main">
                <div className="call-head">
                  <strong>{caller(c)}</strong>
                  {c.callerPhone && <a href={`tel:${c.callerPhone}`} className="tabular">{c.callerPhone}</a>}
                  <Typography.Text type="secondary" className="tabular">{callTime(c.calledAt)}</Typography.Text>
                  {c.followUp && (
                    <Tag color={c.dueDate && c.dueDate < today ? 'red' : 'orange'} bordered={false}>
                      Wiedervorlage{c.dueDate ? ` ${parseDay(c.dueDate).toLocaleDateString('de-DE')}` : ''}
                    </Tag>
                  )}
                </div>
                {c.subject && <div className="call-subject">{c.subject}</div>}
                {c.notes && <Typography.Paragraph className="pre call-notes" ellipsis={{ rows: 3, expandable: true, symbol: 'mehr' }}>{c.notes}</Typography.Paragraph>}
                <Space size={12} wrap className="call-meta">
                  {c.bauakteId && <Link to={`/bauakten/${c.bauakteId}`}>{c.bauakteLabel}</Link>}
                  {c.nextStep && <span>Nächster Schritt: {c.nextStep}</span>}
                  <span>Bearbeiter: {c.handledByName ?? '–'}</span>
                </Space>
              </div>
              {canWrite && (
                <Space size={0} className="call-actions">
                  {c.followUp && <Button type="text" size="small" icon={<CheckOutlined />} onClick={() => void done(c)}>Erledigt</Button>}
                  <Button type="text" size="small" icon={<EditOutlined />} aria-label="Bearbeiten" onClick={() => setEditing(c)} />
                  <Popconfirm title="Anruf löschen?" okText="Löschen" okButtonProps={{ danger: true }} cancelText="Abbrechen"
                    onConfirm={async () => { await del(`calls/${c.id}`); refresh(); }}>
                    <Button type="text" size="small" danger icon={<DeleteOutlined />} aria-label="Löschen" />
                  </Popconfirm>
                </Space>
              )}
            </article>
          ))}
        </div>
      </Card>
      {editing !== null && <CallFormModal kind="call" call={editing === 'new' ? null : editing} open onClose={() => setEditing(null)}
        onSaved={() => { setEditing(null); refresh(); }} />}
    </PageContainer>
  );
}
