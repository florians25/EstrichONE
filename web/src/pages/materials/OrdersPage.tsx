import { CarOutlined, ClockCircleOutlined, FileTextOutlined, PlusOutlined, ReloadOutlined, WarningOutlined } from '@ant-design/icons';
import { PageContainer, ProTable, StatisticCard, type ProColumns } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, Input, Segmented, Select, Space, Typography } from 'antd';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { get } from '../../api/client';
import type { OrderResponse, SupplierResponse } from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { isoDay } from '../../shared/days';
import { OrderDetail } from './OrderDetail';
import { OrderEditor } from './OrderEditor';
import { day, deliveryAddress, OrderStatusTag, OrderTotal } from './shared';

type View = 'open' | 'draft' | 'delivered' | 'cancelled' | 'all';
const views: { value: View; label: string }[] = [
  { value: 'open', label: 'Offen' }, { value: 'draft', label: 'Entwürfe' }, { value: 'delivered', label: 'Geliefert' },
  { value: 'cancelled', label: 'Storniert' }, { value: 'all', label: 'Alle' },
];

/** Materialbestellungen: Übersicht nach Liefertermin, Bestellschein, Status (AP-20). */
export default function OrdersPage() {
  const { has } = useAuth();
  const queries = useQueryClient();
  const [params, setParams] = useSearchParams();
  const [view, setView] = useState<View>('open');
  const [supplierId, setSupplierId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [creating, setCreating] = useState(false);
  const canManage = has('materials.manage');
  const selected = params.get('bestellung');
  const today = isoDay(new Date());

  const suppliers = useQuery({ queryKey: ['suppliers'], queryFn: () => get<SupplierResponse[]>('suppliers', { includeInactive: true }) });
  const orders = useQuery({
    queryKey: ['material-orders', 'list', view, supplierId, search],
    queryFn: () => get<OrderResponse[]>('material-orders', {
      open: view === 'open' || undefined, status: ['draft', 'delivered', 'cancelled'].includes(view) ? view : undefined,
      supplierId: supplierId ?? undefined, search: search || undefined,
    }),
  });
  const open = useQuery({ queryKey: ['material-orders', 'open-all'], queryFn: () => get<OrderResponse[]>('material-orders', { open: true }) });
  const drafts = useQuery({ queryKey: ['material-orders', 'drafts'], queryFn: () => get<OrderResponse[]>('material-orders', { status: 'draft' }) });
  const todays = (open.data ?? []).filter((o) => o.deliveryDate === today);
  const overdue = (open.data ?? []).filter((o) => o.deliveryDate && o.deliveryDate < today);
  const select = (id: string | null) => setParams((p) => { if (id) p.set('bestellung', id); else p.delete('bestellung'); return p; }, { replace: true });

  const columns: ProColumns<OrderResponse>[] = [
    { title: 'Nummer', dataIndex: 'number', width: 130, render: (_, o) => <a onClick={() => select(o.id)} className="tabular nowrap">{o.number}</a> },
    {
      title: 'Liefertermin', dataIndex: 'deliveryDate', width: 190, render: (_, o) => (
        <Space direction="vertical" size={0}>
          <span className={`tabular${o.deliveryDate && o.deliveryDate < today && ['ordered', 'confirmed'].includes(o.status) ? ' text-danger' : ''}`}>
            {o.deliveryDate ? day(o.deliveryDate) : 'nach Absprache'}
          </span>
          {o.deliveryTime && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{o.deliveryTime}</Typography.Text>}
        </Space>
      ),
    },
    { title: 'Lieferant', dataIndex: 'supplierName', width: 200 },
    {
      title: 'Bauakte / Lieferadresse', key: 'site', width: 300, render: (_, o) => (
        <Space direction="vertical" size={0}>
          {o.bauakteId ? <Link to={`/bauakten/${o.bauakteId}?reiter=material`}>{o.bauakteNumber} · {o.bauakteTitle}</Link> : <span>Lager / ohne Bauakte</span>}
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{deliveryAddress(o) || '–'}</Typography.Text>
        </Space>
      ),
    },
    {
      title: 'Positionen', key: 'lines', render: (_, o) => (
        <Typography.Text ellipsis style={{ maxWidth: 320 }}>{o.lines.map((l) => `${l.quantity.toLocaleString('de-DE')} ${l.unit} ${l.description}`).join(' · ') || '–'}</Typography.Text>
      ),
    },
    { title: 'Summe', dataIndex: 'total', width: 120, align: 'right', render: (_, o) => <OrderTotal order={o} /> },
    { title: 'Status', dataIndex: 'status', width: 110, render: (_, o) => <OrderStatusTag status={o.status} /> },
  ];

  return (
    <PageContainer
      title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">Material</Typography.Text>Bestellungen</Space>}
      extra={[
        canManage && <Button key="new" type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>Material bestellen</Button>,
        <Button key="reload" icon={<ReloadOutlined />} onClick={() => void queries.invalidateQueries({ queryKey: ['material-orders'] })}>Aktualisieren</Button>,
      ].filter(Boolean)}
    >
      <StatisticCard.Group direction="row" style={{ marginBottom: 16 }}>
        <StatisticCard statistic={{ title: 'Lieferungen heute', value: todays.length, icon: <CarOutlined className="stat-icon" /> }} />
        <StatisticCard statistic={{ title: 'Offene Bestellungen', value: open.data?.length ?? '–', icon: <ClockCircleOutlined className="stat-icon" /> }} />
        <StatisticCard statistic={{ title: 'Noch nicht abgeschickt', value: drafts.data?.length ?? '–', icon: <FileTextOutlined className="stat-icon" /> }} />
        <StatisticCard statistic={{ title: 'Lieferung überfällig', value: overdue.length, status: overdue.length ? 'error' : undefined, icon: <WarningOutlined className="stat-icon" /> }} />
      </StatisticCard.Group>
      <Card size="small" style={{ marginBottom: 12 }}>
        <Space wrap size={16}>
          <Segmented<View> options={views} value={view} onChange={setView} aria-label="Ansicht" />
          <Select allowClear placeholder="Alle Lieferanten" style={{ width: 240 }} value={supplierId} onChange={(v) => setSupplierId(v ?? null)} aria-label="Lieferant"
            options={(suppliers.data ?? []).map((s) => ({ value: s.id, label: s.name }))} />
          <Input.Search allowClear placeholder="Nummer, Material, Ort" style={{ width: 280 }} onSearch={setSearch} aria-label="Bestellungen durchsuchen" />
        </Space>
      </Card>
      <ProTable<OrderResponse>
        rowKey="id" size="small" bordered cardBordered search={false} options={false} columns={columns}
        dataSource={orders.data} loading={orders.isLoading} pagination={{ pageSize: 50, hideOnSinglePage: true }}
        headerTitle={`${orders.data?.length ?? 0} Bestellungen`}
        onRow={(o) => ({ onClick: (e) => { if (!(e.target as HTMLElement).closest('a')) select(o.id); }, style: { cursor: 'pointer' } })}
        locale={{ emptyText: view === 'open' ? 'Keine offenen Bestellungen.' : 'Keine Bestellungen in dieser Ansicht.' }}
      />
      {selected && <OrderDetail orderId={selected} canManage={canManage} onClose={() => select(null)} />}
      {creating && <OrderEditor editing={{ order: null }} onClose={() => setCreating(false)}
        onSaved={(o) => { setCreating(false); void queries.invalidateQueries({ queryKey: ['material-orders'] }); select(o.id); }} />}
    </PageContainer>
  );
}
