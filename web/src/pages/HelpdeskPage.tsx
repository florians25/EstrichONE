import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { Select, Space, Tag, Typography } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { get } from '../api/client';
import type { TicketCategory, TicketPriority, TicketSummary } from '../api/models';
import { ticketCategoryLabels, ticketPriorityLabels, TicketStatusTag, waitingSince } from '../shared/status';

type Filter = 'open' | 'mine' | 'unassigned' | 'all';
const priorityColors: Record<TicketPriority, string> = { low: 'default', normal: 'blue', high: 'orange', urgent: 'red' };

/** IT Center › Helpdesk: Liste mit Filtern, am längsten wartende zuerst. */
export default function HelpdeskPage() {
  const navigate = useNavigate();
  const action = useRef<ActionType>(undefined);
  const [filter, setFilter] = useState<Filter>('open');
  const [category, setCategory] = useState<TicketCategory | null>(null);

  useEffect(() => {
    action.current?.reload();
  }, [filter, category]);

  const columns: ProColumns<TicketSummary>[] = [
    { title: 'Nummer', dataIndex: 'number', width: 120, render: (_, t) => <Link className="tabular" to={`/it-center/helpdesk/${t.id}`}>{t.number}</Link> },
    {
      title: 'Problem', dataIndex: 'title', render: (_, t) => (
        <Space direction="vertical" size={0}>
          <Space size={6}>{t.title}{t.reporterBlocked && <Tag color="error" bordered={false}>Kann nicht arbeiten</Tag>}</Space>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{ticketCategoryLabels[t.category]}</Typography.Text>
        </Space>
      ),
    },
    { title: 'Status', dataIndex: 'status', width: 150, render: (_, t) => <TicketStatusTag status={t.status} /> },
    { title: 'Priorität', dataIndex: 'priority', width: 110, render: (_, t) => <Tag color={priorityColors[t.priority]} bordered={false}>{ticketPriorityLabels[t.priority]}</Tag> },
    { title: 'Melder', dataIndex: 'reporterDisplayName', width: 170, render: (_, t) => t.reporterDisplayName ?? '–' },
    { title: 'Zuständig', dataIndex: 'assigneeName', width: 170, render: (_, t) => t.assigneeName ?? <Typography.Text type="secondary">nicht zugewiesen</Typography.Text> },
    { title: 'Wartet seit', dataIndex: 'lastActivityAt', width: 130, align: 'right', render: (_, t) => <span className="tabular">{waitingSince(t.lastActivityAt)}</span> },
  ];

  return (
    <PageContainer title="Helpdesk" subTitle="Gemeldete Probleme aus den Apps und dem Büro">
      <ProTable<TicketSummary>
        actionRef={action}
        rowKey="id"
        size="small"
        bordered
        cardBordered
        search={false}
        columns={columns}
        pagination={{ pageSize: 50, showSizeChanger: false }}
        locale={{ emptyText: 'Keine Tickets in dieser Ansicht.' }}
        options={{ reload: true, density: true, setting: true }}
        onRow={(t) => ({ onDoubleClick: () => navigate(`/it-center/helpdesk/${t.id}`) })}
        toolbar={{
          menu: {
            type: 'tab',
            activeKey: filter,
            items: [
              { key: 'open', label: 'Offen' }, { key: 'mine', label: 'Mir zugewiesen' },
              { key: 'unassigned', label: 'Nicht zugewiesen' }, { key: 'all', label: 'Alle' },
            ],
            onChange: (key) => setFilter(key as Filter),
          },
          actions: [
            <Select key="category" aria-label="Kategorie" allowClear placeholder="Alle Kategorien" style={{ width: 200 }} value={category}
              onChange={(v) => setCategory(v ?? null)}
              options={(Object.keys(ticketCategoryLabels) as TicketCategory[]).map((c) => ({ value: c, label: ticketCategoryLabels[c] }))} />,
          ],
        }}
        request={async () => {
          const data = await get<TicketSummary[]>('tickets', { filter, category });
          return { data, success: true, total: data.length };
        }}
      />
    </PageContainer>
  );
}
