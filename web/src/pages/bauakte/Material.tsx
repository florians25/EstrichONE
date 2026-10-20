import { CalculatorOutlined, PlusOutlined, ShoppingCartOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, Checkbox, Col, Empty, InputNumber, Row, Space, Table, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { get } from '../../api/client';
import type { BauakteDetail, OrderResponse, RoomOverview } from '../../api/models';
import { uuidv7 } from '../../shared/ids';
import { OrderDetail } from '../materials/OrderDetail';
import { OrderEditor, type LineDraft, type OrderEditing } from '../materials/OrderEditor';
import { day, OrderStatusTag, OrderTotal, quantity } from '../materials/shared';

interface Need {
  key: string;
  description: string;
  detail: string;
  amount: number;
  unit: string;
}

const roundUp = (v: number, step: number) => Math.ceil(v / step - 1e-9) * step;

/** Bedarf aus „Räume & Flächen“: Estrichvolumen je Estrichart und Dämmfläche je Dicke. */
export function requirements(rooms: RoomOverview, reservePercent: number): Need[] {
  const factor = 1 + reservePercent / 100;
  const screed = new Map<string, { area: number; volume: number; missing: number }>();
  const insulation = new Map<number, number>();
  for (const r of rooms.rooms) {
    const area = r.areaSquareMeters ?? 0;
    if (area <= 0) continue;
    const name = r.screedTypeName ?? 'Estrich (Art offen)';
    const entry = screed.get(name) ?? { area: 0, volume: 0, missing: 0 };
    entry.area += area;
    if (r.thicknessMillimeters) entry.volume += (area * r.thicknessMillimeters) / 1000;
    else entry.missing += 1;
    screed.set(name, entry);
    if (r.insulationMillimeters) insulation.set(r.insulationMillimeters, (insulation.get(r.insulationMillimeters) ?? 0) + area);
  }
  const needs: Need[] = [];
  for (const [name, e] of screed) {
    if (e.volume > 0) {
      needs.push({
        key: `screed-${name}`, description: name, unit: 'm³', amount: roundUp(e.volume * factor, 0.5),
        detail: `${quantity(e.area)} m² · ${quantity(Math.round(e.volume * 100) / 100)} m³ netto${e.missing ? ` · ${e.missing} Räume ohne Dicke nicht berechnet` : ''}`,
      });
    }
  }
  for (const [mm, area] of [...insulation].sort((a, b) => a[0] - b[0])) {
    needs.push({ key: `ins-${mm}`, description: `Dämmung ${mm} mm`, unit: 'm²', amount: roundUp(area * factor, 1), detail: `${quantity(area)} m² netto` });
  }
  return needs;
}

/** Reiter „Material“ der Bauakte: Bedarf aus den Räumen, Bestellungen für diese Baustelle. */
export function BauakteMaterial({ bauakte, canManage }: { bauakte: BauakteDetail; canManage: boolean }) {
  const queries = useQueryClient();
  const rooms = useQuery({ queryKey: ['rooms', bauakte.id], queryFn: () => get<RoomOverview>(`bauakten/${bauakte.id}/rooms`) });
  const orders = useQuery({ queryKey: ['material-orders', 'bauakte', bauakte.id], queryFn: () => get<OrderResponse[]>('material-orders', { bauakteId: bauakte.id }) });
  const [reserve, setReserve] = useState(5);
  const [chosen, setChosen] = useState<string[] | null>(null);
  const [editing, setEditing] = useState<OrderEditing | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const needs = useMemo(() => (rooms.data ? requirements(rooms.data, reserve) : []), [rooms.data, reserve]);
  const picked = chosen ?? needs.map((n) => n.key);
  const label = `${bauakte.number ?? 'neu'} · ${bauakte.title}`;

  const orderNeeds = () => {
    const lines: LineDraft[] = needs.filter((n) => picked.includes(n.key))
      .map((n) => ({ id: uuidv7(), materialId: null, description: n.description, quantity: n.amount, unit: n.unit, unitPrice: null }));
    setEditing({ order: null, bauakte: { id: bauakte.id, label }, lines });
  };

  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} xl={9}>
        <Card size="small" title={<Space><CalculatorOutlined />Materialbedarf aus Räumen & Flächen</Space>} loading={rooms.isLoading}>
          {needs.length === 0 ? (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Für eine Bedarfsrechnung bitte unter „Räume & Flächen“ Flächen und Estrichdicken erfassen." />
          ) : (
            <Space direction="vertical" style={{ width: '100%' }}>
              <Space>Reserve / Verschnitt
                <InputNumber min={0} max={50} value={reserve} onChange={(v) => setReserve(v ?? 0)} addonAfter="%" style={{ width: 110 }} aria-label="Reserve in Prozent" />
              </Space>
              {needs.map((n) => (
                <div key={n.key} className="need-row">
                  <Checkbox checked={picked.includes(n.key)} aria-label={n.description}
                    onChange={(e) => setChosen(e.target.checked ? [...picked, n.key] : picked.filter((k) => k !== n.key))} />
                  <div style={{ flex: 1 }}>
                    <div><strong>{n.description}</strong></div>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>{n.detail}</Typography.Text>
                  </div>
                  <strong className="tabular">{quantity(n.amount)} {n.unit}</strong>
                </div>
              ))}
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>Volumen = Fläche × Estrichdicke, aufgerundet auf 0,5 m³; Dämmung auf volle m².</Typography.Text>
              {canManage && <Button type="primary" icon={<ShoppingCartOutlined />} disabled={picked.length === 0} onClick={orderNeeds}>Auswahl bestellen</Button>}
            </Space>
          )}
        </Card>
      </Col>
      <Col xs={24} xl={15}>
        <Card size="small" title="Bestellungen für diese Baustelle" loading={orders.isLoading}
          extra={canManage && <Button size="small" icon={<PlusOutlined />} onClick={() => setEditing({ order: null, bauakte: { id: bauakte.id, label } })}>Material bestellen</Button>}>
          <Table<OrderResponse>
            rowKey="id" size="small" pagination={false} dataSource={orders.data ?? []}
            locale={{ emptyText: 'Noch keine Bestellungen.' }}
            onRow={(o) => ({ onClick: () => setSelected(o.id), style: { cursor: 'pointer' } })}
            columns={[
              { title: 'Nummer', dataIndex: 'number', width: 120, render: (v: string) => <span className="tabular nowrap">{v}</span> },
              { title: 'Liefertermin', dataIndex: 'deliveryDate', width: 170, render: (_, o) => <span className="tabular">{o.deliveryDate ? day(o.deliveryDate) : 'nach Absprache'}{o.deliveryTime ? `, ${o.deliveryTime}` : ''}</span> },
              { title: 'Lieferant', dataIndex: 'supplierName', width: 160 },
              { title: 'Positionen', key: 'lines', render: (_, o) => o.lines.map((l) => `${quantity(l.quantity)} ${l.unit} ${l.description}`).join(' · ') },
              { title: 'Summe', dataIndex: 'total', width: 110, align: 'right', render: (_, o) => <OrderTotal order={o} /> },
              { title: 'Status', dataIndex: 'status', width: 100, render: (_, o) => <OrderStatusTag status={o.status} /> },
            ]}
          />
        </Card>
      </Col>
      {editing && <OrderEditor editing={editing} onClose={() => setEditing(null)}
        onSaved={(o) => { setEditing(null); void queries.invalidateQueries({ queryKey: ['material-orders'] }); void queries.invalidateQueries({ queryKey: ['hints'] }); setSelected(o.id); }} />}
      {selected && <OrderDetail orderId={selected} canManage={canManage} onClose={() => setSelected(null)} />}
    </Row>
  );
}
