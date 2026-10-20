import { MailOutlined, PhoneOutlined, PlusOutlined } from '@ant-design/icons';
import { ModalForm, PageContainer, ProFormSwitch, ProFormText, ProFormTextArea, ProTable, type ProColumns } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Popconfirm, Space, Switch, Tag, Typography } from 'antd';
import { useState } from 'react';
import { del, get, problemMessage, put } from '../../api/client';
import type { SupplierResponse } from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { uuidv7 } from '../../shared/ids';

type SupplierForm = Omit<SupplierResponse, 'id' | 'version'>;

/** Lieferanten mit Bestellhinweisen (Bestellschluss, Mindestmengen …). */
export default function SuppliersPage() {
  const { has } = useAuth();
  const { message } = App.useApp();
  const queries = useQueryClient();
  const canManage = has('materials.manage');
  const [inactive, setInactive] = useState(false);
  const [editing, setEditing] = useState<SupplierResponse | 'new' | null>(null);
  const suppliers = useQuery({ queryKey: ['suppliers', 'all', inactive], queryFn: () => get<SupplierResponse[]>('suppliers', { includeInactive: inactive || undefined }) });
  const refresh = () => void queries.invalidateQueries({ queryKey: ['suppliers'] });

  const columns: ProColumns<SupplierResponse>[] = [
    {
      title: 'Lieferant', dataIndex: 'name', render: (_, s) => (
        <Space direction="vertical" size={0}>
          {canManage ? <a onClick={() => setEditing(s)}><strong>{s.name}</strong></a> : <strong>{s.name}</strong>}
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{[s.street, [s.postalCode, s.city].filter(Boolean).join(' ')].filter(Boolean).join(', ') || '–'}</Typography.Text>
        </Space>
      ),
    },
    { title: 'Kundennr.', dataIndex: 'customerNumber', width: 120, render: (_, s) => s.customerNumber ?? '–' },
    {
      title: 'Kontakt', key: 'contact', width: 280, render: (_, s) => (
        <Space direction="vertical" size={0}>
          {s.contactName && <span>{s.contactName}</span>}
          {s.phone && <a href={`tel:${s.phone}`}><PhoneOutlined /> {s.phone}</a>}
          {s.email && <a href={`mailto:${s.email}`}><MailOutlined /> {s.email}</a>}
        </Space>
      ),
    },
    { title: 'Bestellhinweise', dataIndex: 'orderNotes', render: (_, s) => s.orderNotes ?? '–' },
    ...(inactive ? [{ title: 'Status', dataIndex: 'isActive', width: 100, render: (_: unknown, s: SupplierResponse) => s.isActive ? <Tag color="success" bordered={false}>Aktiv</Tag> : <Tag bordered={false}>Inaktiv</Tag> }] : []),
    ...(canManage ? [{
      title: '', key: 'actions', width: 70, render: (_: unknown, s: SupplierResponse) => (
        <Popconfirm title="Lieferant löschen?" okText="Löschen" okButtonProps={{ danger: true }} cancelText="Abbrechen"
          onConfirm={async () => { try { await del(`suppliers/${s.id}`); refresh(); } catch (e) { message.error(problemMessage(e)); } }}>
          <Button type="link" danger size="small">Löschen</Button>
        </Popconfirm>
      ),
    }] : []),
  ];

  const initial: Partial<SupplierForm> = editing && editing !== 'new'
    ? { ...editing }
    : { isActive: true };

  return (
    <PageContainer
      title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">Material</Typography.Text>Lieferanten</Space>}
      extra={canManage && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>Lieferant anlegen</Button>}
    >
      <ProTable<SupplierResponse>
        rowKey="id" size="small" bordered cardBordered search={false} options={false} columns={columns}
        dataSource={suppliers.data} loading={suppliers.isLoading} pagination={false}
        headerTitle={`${suppliers.data?.length ?? 0} Lieferanten`}
        toolBarRender={() => [<Space key="inactive"><Switch size="small" checked={inactive} onChange={setInactive} aria-label="Inaktive anzeigen" />Inaktive anzeigen</Space>]}
        locale={{ emptyText: 'Noch keine Lieferanten angelegt.' }}
      />
      <ModalForm<SupplierForm>
        key={editing && editing !== 'new' ? editing.id : 'new'}
        title={editing === 'new' ? 'Lieferant anlegen' : 'Lieferant bearbeiten'}
        open={editing !== null} width={620} initialValues={initial}
        modalProps={{ destroyOnHidden: true, onCancel: () => setEditing(null) }}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        onOpenChange={(o) => { if (!o) setEditing(null); }}
        onFinish={async (v) => {
          const id = editing && editing !== 'new' ? editing.id : uuidv7();
          try {
            await put(`suppliers/${id}`, { ...v, id, name: v.name.trim(), email: v.email || null });
            message.success('Lieferant gespeichert');
            setEditing(null);
            refresh();
            return true;
          } catch (e) {
            message.error(problemMessage(e));
            return false;
          }
        }}
      >
        <ProFormText name="name" label="Name" rules={[{ required: true, whitespace: true, message: 'Bitte den Namen angeben.' }]} />
        <Space.Compact block>
          <ProFormText name="customerNumber" label="Unsere Kundennummer" width="sm" />
          <ProFormText name="contactName" label="Ansprechpartner" width="md" />
        </Space.Compact>
        <Space.Compact block>
          <ProFormText name="phone" label="Telefon (Disposition)" width="sm" />
          <ProFormText name="email" label="E-Mail für Bestellungen" width="md" rules={[{ type: 'email', message: 'Bitte eine gültige E-Mail-Adresse angeben.' }]} />
        </Space.Compact>
        <ProFormText name="street" label="Straße" />
        <Space.Compact block>
          <ProFormText name="postalCode" label="PLZ" width="xs" />
          <ProFormText name="city" label="Ort" width="md" />
        </Space.Compact>
        <ProFormTextArea name="orderNotes" label="Bestellhinweise" fieldProps={{ rows: 2 }} placeholder="z. B. Bestellschluss 12 Uhr für Lieferung am Folgetag, Mindestmenge 5 t" />
        <ProFormSwitch name="isActive" label="Aktiv" />
      </ModalForm>
    </PageContainer>
  );
}
