import { PlusOutlined } from '@ant-design/icons';
import { ModalForm, PageContainer, ProFormDigit, ProFormSelect, ProFormSwitch, ProFormText, ProFormTextArea, ProTable, type ProColumns } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Input, Popconfirm, Select, Space, Switch, Tag, Typography } from 'antd';
import { useState } from 'react';
import { del, get, problemMessage, put } from '../../api/client';
import type { MaterialCategory, MaterialResponse, SupplierResponse } from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { uuidv7 } from '../../shared/ids';
import { CategoryTag, categoryLabels, money, units } from './shared';

interface MaterialForm {
  name: string;
  articleNumber?: string;
  category: MaterialCategory;
  unit: string;
  supplierId?: string | null;
  price?: number | null;
  notes?: string;
  isActive: boolean;
}

/** Materialstamm: Artikel mit Einheit, Standardlieferant und Einkaufspreis. */
export default function MaterialsPage() {
  const { has } = useAuth();
  const { message } = App.useApp();
  const queries = useQueryClient();
  const canManage = has('materials.manage');
  const [category, setCategory] = useState<MaterialCategory | null>(null);
  const [search, setSearch] = useState('');
  const [inactive, setInactive] = useState(false);
  const [editing, setEditing] = useState<MaterialResponse | 'new' | null>(null);
  const materials = useQuery({ queryKey: ['materials', 'all', inactive], queryFn: () => get<MaterialResponse[]>('materials', { includeInactive: inactive || undefined }) });
  const suppliers = useQuery({ queryKey: ['suppliers'], queryFn: () => get<SupplierResponse[]>('suppliers') });
  const refresh = () => void queries.invalidateQueries({ queryKey: ['materials'] });

  const needle = search.trim().toLowerCase();
  const rows = (materials.data ?? []).filter((m) => (!category || m.category === category)
    && (!needle || m.name.toLowerCase().includes(needle) || (m.articleNumber ?? '').toLowerCase().includes(needle) || (m.supplierName ?? '').toLowerCase().includes(needle)));

  const columns: ProColumns<MaterialResponse>[] = [
    {
      title: 'Bezeichnung', dataIndex: 'name', render: (_, m) => (
        <Space direction="vertical" size={0}>
          {canManage ? <a onClick={() => setEditing(m)}>{m.name}</a> : m.name}
          {m.notes && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{m.notes}</Typography.Text>}
        </Space>
      ),
    },
    { title: 'Art.-Nr.', dataIndex: 'articleNumber', width: 120, render: (_, m) => m.articleNumber ?? '–' },
    { title: 'Kategorie', dataIndex: 'category', width: 190, render: (_, m) => <CategoryTag category={m.category} /> },
    { title: 'Einheit', dataIndex: 'unit', width: 90 },
    { title: 'Lieferant', dataIndex: 'supplierName', width: 200, render: (_, m) => m.supplierName ?? '–' },
    { title: 'Preis je Einheit', dataIndex: 'price', width: 140, align: 'right', render: (_, m) => <span className="tabular">{money(m.price)}</span> },
    ...(inactive ? [{ title: 'Status', dataIndex: 'isActive', width: 100, render: (_: unknown, m: MaterialResponse) => m.isActive ? <Tag color="success" bordered={false}>Aktiv</Tag> : <Tag bordered={false}>Inaktiv</Tag> }] : []),
    ...(canManage ? [{
      title: '', key: 'actions', width: 60, render: (_: unknown, m: MaterialResponse) => (
        <Popconfirm title="Artikel löschen?" description="Bestehende Bestellungen behalten ihre Positionen." okText="Löschen" okButtonProps={{ danger: true }} cancelText="Abbrechen"
          onConfirm={async () => { await del(`materials/${m.id}`); refresh(); }}>
          <Button type="link" danger size="small">Löschen</Button>
        </Popconfirm>
      ),
    }] : []),
  ];

  const initial: MaterialForm = editing && editing !== 'new'
    ? { name: editing.name, articleNumber: editing.articleNumber ?? '', category: editing.category, unit: editing.unit, supplierId: editing.supplierId, price: editing.price, notes: editing.notes ?? '', isActive: editing.isActive }
    : { name: '', category: 'screed', unit: 'Sack', isActive: true };

  return (
    <PageContainer
      title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">Material</Typography.Text>Materialstamm</Space>}
      extra={canManage && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>Artikel anlegen</Button>}
    >
      <ProTable<MaterialResponse>
        rowKey="id" size="small" bordered cardBordered search={false} options={false} columns={columns}
        dataSource={rows} loading={materials.isLoading} pagination={{ pageSize: 50, hideOnSinglePage: true }}
        headerTitle={`${rows.length} Artikel`}
        toolBarRender={() => [
          <Input.Search key="search" allowClear placeholder="Bezeichnung, Art.-Nr., Lieferant" style={{ width: 280 }} onSearch={setSearch} onChange={(e) => setSearch(e.target.value)} aria-label="Artikel durchsuchen" />,
          <Select key="cat" allowClear placeholder="Alle Kategorien" style={{ width: 220 }} value={category} onChange={(v) => setCategory(v ?? null)} aria-label="Kategorie"
            options={(Object.keys(categoryLabels) as MaterialCategory[]).map((c) => ({ value: c, label: categoryLabels[c] }))} />,
          <Space key="inactive"><Switch size="small" checked={inactive} onChange={setInactive} aria-label="Inaktive anzeigen" />Inaktive anzeigen</Space>,
        ]}
        locale={{ emptyText: 'Noch keine Artikel. Legen Sie die Materialien an, die Sie regelmäßig bestellen.' }}
      />
      <ModalForm<MaterialForm>
        key={editing && editing !== 'new' ? editing.id : 'new'}
        title={editing === 'new' ? 'Artikel anlegen' : 'Artikel bearbeiten'}
        open={editing !== null} width={620} initialValues={initial}
        modalProps={{ destroyOnHidden: true, onCancel: () => setEditing(null) }}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        onOpenChange={(o) => { if (!o) setEditing(null); }}
        onFinish={async (v) => {
          const id = editing && editing !== 'new' ? editing.id : uuidv7();
          try {
            await put(`materials/${id}`, { id, name: v.name.trim(), articleNumber: v.articleNumber || null, category: v.category, unit: v.unit.trim(), supplierId: v.supplierId ?? null, price: v.price ?? null, notes: v.notes || null, isActive: v.isActive });
            message.success('Artikel gespeichert');
            setEditing(null);
            refresh();
            return true;
          } catch (e) {
            message.error(problemMessage(e));
            return false;
          }
        }}
      >
        <ProFormText name="name" label="Bezeichnung" rules={[{ required: true, whitespace: true, message: 'Bitte die Bezeichnung angeben.' }]} placeholder="z. B. Zementestrich CT-C25-F4, Sackware 40 kg" />
        <Space.Compact block>
          <ProFormSelect name="category" label="Kategorie" width="md" allowClear={false}
            options={(Object.keys(categoryLabels) as MaterialCategory[]).map((c) => ({ value: c, label: categoryLabels[c] }))} />
          <ProFormText name="articleNumber" label="Artikelnummer" width="sm" />
        </Space.Compact>
        <Space.Compact block>
          <ProFormSelect name="unit" label="Einheit" width="sm" rules={[{ required: true }]} fieldProps={{ showSearch: true, mode: undefined }}
            options={units.map((u) => ({ value: u, label: u }))} />
          <ProFormDigit name="price" label="Einkaufspreis je Einheit (netto)" width="sm" min={0} fieldProps={{ precision: 2, decimalSeparator: ',', addonAfter: '€' }} />
        </Space.Compact>
        <ProFormSelect name="supplierId" label="Standardlieferant" allowClear showSearch options={(suppliers.data ?? []).map((s) => ({ value: s.id, label: s.name }))} />
        <ProFormTextArea name="notes" label="Hinweise" fieldProps={{ rows: 2 }} placeholder="z. B. Verbrauch ca. 20 kg/m² je cm" />
        <ProFormSwitch name="isActive" label="Aktiv (in Bestellungen auswählbar)" />
      </ModalForm>
    </PageContainer>
  );
}
