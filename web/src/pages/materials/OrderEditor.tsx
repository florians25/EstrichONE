import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, AutoComplete, Button, DatePicker, Divider, Drawer, Empty, Form, Input, InputNumber, Select, Space, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useState } from 'react';
import { get, problemMessage, put } from '../../api/client';
import type { MaterialResponse, OrderResponse, SupplierResponse } from '../../api/models';
import { BauakteSelect } from '../../shared/BauakteSelect';
import { uuidv7 } from '../../shared/ids';
import { deliveryWindows, money, units } from './shared';

export interface LineDraft {
  id: string;
  materialId?: string | null;
  description: string;
  quantity: number | null;
  unit: string;
  unitPrice?: number | null;
}

interface OrderForm {
  supplierId?: string;
  bauakteId?: string | null;
  deliveryDate?: Dayjs | null;
  deliveryTime?: string;
  deliveryStreet?: string;
  deliveryPostalCode?: string;
  deliveryCity?: string;
  siteContact?: string;
  notes?: string;
  lines: LineDraft[];
}

/** Neue Bestellung (optional schon mit Bauakte und Positionen) oder vorhandene bearbeiten. */
export type OrderEditing =
  | { order: OrderResponse }
  | { order: null; bauakte?: { id: string; label: string }; lines?: LineDraft[] };

export const emptyLine = (): LineDraft => ({ id: uuidv7(), materialId: null, description: '', quantity: null, unit: 'Sack', unitPrice: null });

export function OrderEditor({ editing, onClose, onSaved }: { editing: OrderEditing; onClose: () => void; onSaved: (o: OrderResponse) => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<OrderForm>();
  const [saving, setSaving] = useState(false);
  const suppliers = useQuery({ queryKey: ['suppliers'], queryFn: () => get<SupplierResponse[]>('suppliers') });
  const materials = useQuery({ queryKey: ['materials'], queryFn: () => get<MaterialResponse[]>('materials') });
  const supplierId = Form.useWatch('supplierId', form);
  const lines = Form.useWatch('lines', form) ?? [];
  const supplier = suppliers.data?.find((s) => s.id === supplierId);
  const o = editing.order;

  const initial: OrderForm = o
    ? {
        supplierId: o.supplierId, bauakteId: o.bauakteId, deliveryDate: o.deliveryDate ? dayjs(o.deliveryDate) : null, deliveryTime: o.deliveryTime ?? '',
        deliveryStreet: o.deliveryStreet ?? '', deliveryPostalCode: o.deliveryPostalCode ?? '', deliveryCity: o.deliveryCity ?? '',
        siteContact: o.siteContact ?? '', notes: o.notes ?? '',
        lines: o.lines.map((l) => ({ id: l.id, materialId: l.materialId, description: l.description, quantity: l.quantity, unit: l.unit, unitPrice: l.unitPrice })),
      }
    : { bauakteId: editing.bauakte?.id ?? null, lines: editing.lines?.length ? editing.lines : [emptyLine()] };

  // Artikel des gewählten Lieferanten zuerst.
  const materialOptions = [...(materials.data ?? [])]
    .sort((a, b) => Number(b.supplierId === supplierId) - Number(a.supplierId === supplierId))
    .map((m) => ({ value: m.id, label: `${m.name}${m.articleNumber ? ` · ${m.articleNumber}` : ''}${m.supplierName && m.supplierId !== supplierId ? ` (${m.supplierName})` : ''}` }));

  const pick = (index: number, materialId: string | null) => {
    const m = materials.data?.find((x) => x.id === materialId);
    if (!m) {
      return;
    }
    const current = form.getFieldValue(['lines', index]) as LineDraft;
    form.setFieldValue(['lines', index], { ...current, materialId, description: m.name, unit: m.unit, unitPrice: m.price });
    if (!form.getFieldValue('supplierId') && m.supplierId) {
      form.setFieldValue('supplierId', m.supplierId);
    }
  };

  const total = lines.reduce((sum, l) => (l?.unitPrice != null && l?.quantity ? sum + l.unitPrice * l.quantity : sum), 0);

  const save = async () => {
    const v = await form.validateFields();
    setSaving(true);
    try {
      const id = o?.id ?? uuidv7();
      const saved = await put<OrderResponse>(`material-orders/${id}`, {
        id, supplierId: v.supplierId, bauakteId: v.bauakteId ?? null, deliveryDate: v.deliveryDate?.format('YYYY-MM-DD') ?? null,
        deliveryTime: v.deliveryTime || null, deliveryStreet: v.deliveryStreet || null, deliveryPostalCode: v.deliveryPostalCode || null,
        deliveryCity: v.deliveryCity || null, siteContact: v.siteContact || null, notes: v.notes || null,
        lines: (v.lines ?? []).map((l) => ({ id: l.id, materialId: l.materialId ?? null, description: l.description.trim(), quantity: l.quantity, unit: l.unit.trim(), unitPrice: l.unitPrice ?? null })),
      });
      message.success(o ? 'Bestellung gespeichert' : `Bestellung ${saved.number} angelegt`);
      onSaved(saved);
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer
      open width={920} onClose={onClose} destroyOnHidden
      title={o ? `Bestellung ${o.number} bearbeiten` : 'Material bestellen'}
      extra={<Space><Button onClick={onClose}>Abbrechen</Button><Button type="primary" loading={saving} onClick={() => void save()}>Speichern</Button></Space>}
    >
      <Form<OrderForm> form={form} layout="vertical" initialValues={initial} requiredMark={false}>
        <div className="form-grid-2">
          <Form.Item name="supplierId" label="Lieferant" rules={[{ required: true, message: 'Bitte einen Lieferanten wählen.' }]}>
            <Select showSearch optionFilterProp="label" placeholder="Lieferant wählen" loading={suppliers.isLoading}
              notFoundContent="Noch keine Lieferanten angelegt – unter Material › Lieferanten."
              options={(suppliers.data ?? []).map((s) => ({ value: s.id, label: s.name }))} />
          </Form.Item>
          <Form.Item name="bauakteId" label="Bauakte (optional)" extra="Ohne eigene Lieferadresse wird auf die Baustelle geliefert.">
            <BauakteSelect initialLabel={o?.bauakteId ? `${o.bauakteNumber ?? 'neu'} · ${o.bauakteTitle}` : editing.order === null ? editing.bauakte?.label ?? null : null} />
          </Form.Item>
        </div>
        {supplier?.orderNotes && <Alert type="info" showIcon message={`${supplier.name}: ${supplier.orderNotes}`} style={{ marginBottom: 16 }} />}
        <div className="form-grid-2">
          <Form.Item name="deliveryDate" label="Liefertermin">
            <DatePicker format="dd, DD.MM.YYYY" style={{ width: '100%' }} placeholder="nach Absprache" />
          </Form.Item>
          <Form.Item name="deliveryTime" label="Zeitfenster">
            <AutoComplete options={deliveryWindows.map((value) => ({ value }))} placeholder="z. B. 7:00–8:00" />
          </Form.Item>
        </div>
        <div className="form-grid-address">
          <Form.Item name="deliveryStreet" label="Lieferadresse: Straße"><Input placeholder="leer = Baustelle der Bauakte" /></Form.Item>
          <Form.Item name="deliveryPostalCode" label="PLZ"><Input /></Form.Item>
          <Form.Item name="deliveryCity" label="Ort"><Input /></Form.Item>
        </div>
        <Form.Item name="siteContact" label="Ansprechpartner vor Ort" extra="Leer = Ansprechpartner der Bauakte.">
          <Input placeholder="Name, Telefon" />
        </Form.Item>

        <Divider orientation="left" orientationMargin={0}>Positionen</Divider>
        <Form.List name="lines">
          {(fields, { add, remove }) => (
            <div className="order-lines">
              {fields.length > 0 && (
                <div className="order-line order-line-head" aria-hidden>
                  <span>Artikel</span><span>Bezeichnung</span><span>Menge</span><span>Einheit</span><span>Einzelpreis</span><span>Summe</span><span />
                </div>
              )}
              {fields.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Positionen." />}
              {fields.map((field, index) => {
                const l = lines[index];
                return (
                  <div key={field.key} className="order-line">
                    <Form.Item name={[field.name, 'materialId']} noStyle>
                      <Select allowClear showSearch optionFilterProp="label" placeholder="aus Materialstamm" aria-label={`Artikel Position ${index + 1}`}
                        options={materialOptions} onChange={(v: string | undefined) => pick(index, v ?? null)} popupMatchSelectWidth={360} />
                    </Form.Item>
                    <Form.Item name={[field.name, 'description']} rules={[{ required: true, whitespace: true, message: 'Bezeichnung fehlt' }]} style={{ margin: 0 }}>
                      <Input aria-label={`Bezeichnung Position ${index + 1}`} />
                    </Form.Item>
                    <Form.Item name={[field.name, 'quantity']} rules={[{ required: true, message: 'Menge fehlt' }]} style={{ margin: 0 }}>
                      <InputNumber min={0.001} decimalSeparator="," style={{ width: '100%' }} aria-label={`Menge Position ${index + 1}`} />
                    </Form.Item>
                    <Form.Item name={[field.name, 'unit']} rules={[{ required: true, whitespace: true, message: 'Einheit fehlt' }]} style={{ margin: 0 }}>
                      <AutoComplete options={units.map((value) => ({ value }))} aria-label={`Einheit Position ${index + 1}`} />
                    </Form.Item>
                    <Form.Item name={[field.name, 'unitPrice']} style={{ margin: 0 }}>
                      <InputNumber min={0} decimalSeparator="," precision={2} addonAfter="€" style={{ width: '100%' }} aria-label={`Einzelpreis Position ${index + 1}`} />
                    </Form.Item>
                    <span className="tabular order-line-sum">{l?.unitPrice != null && l?.quantity ? money(l.unitPrice * l.quantity) : ''}</span>
                    <Button type="text" danger icon={<DeleteOutlined />} aria-label={`Position ${index + 1} entfernen`} onClick={() => remove(field.name)} />
                  </div>
                );
              })}
              <Space style={{ justifyContent: 'space-between', width: '100%', marginTop: 8 }}>
                <Button icon={<PlusOutlined />} onClick={() => add(emptyLine())}>Position hinzufügen</Button>
                {total > 0 && (
                  <Typography.Text strong className="tabular">
                    {lines.every((l) => l?.unitPrice != null) ? 'Summe netto' : 'Summe netto der Positionen mit Preis'} {money(total)}
                  </Typography.Text>
                )}
              </Space>
            </div>
          )}
        </Form.List>
        <Form.Item name="notes" label="Hinweise an den Lieferanten" style={{ marginTop: 16 }}>
          <Input.TextArea rows={3} placeholder="z. B. Anfahrt nur über Hofeinfahrt, Kran nötig, Pumpe vor Ort" />
        </Form.Item>
      </Form>
    </Drawer>
  );
}
