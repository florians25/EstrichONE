import { MailOutlined, PhoneOutlined, UserAddOutlined } from '@ant-design/icons';
import { ModalForm, PageContainer, ProFormRadio, ProFormText, ProFormTextArea, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { App, Button, Space, Tag } from 'antd';
import { useRef, useState } from 'react';
import { get, post, problemMessage, put } from '../api/client';
import type { ContactKind, ContactResponse } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { uuidv7 } from '../shared/ids';

interface ContactForm {
  kind: ContactKind;
  name: string;
  customerNumber?: string;
  street?: string;
  postalCode?: string;
  city?: string;
  phone?: string;
  email?: string;
  notes?: string;
}

/** Kundenstamm: suchen, anlegen, bearbeiten (mit Versionsprüfung). */
export default function CustomersPage() {
  const { has } = useAuth();
  const { message } = App.useApp();
  const action = useRef<ActionType>(undefined);
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<ContactResponse | 'new' | null>(null);
  const canEdit = has('contacts.edit');

  const columns: ProColumns<ContactResponse>[] = [
    { title: 'Kd.-Nr.', dataIndex: 'customerNumber', width: 110, render: (_, c) => <span className="tabular">{c.customerNumber ?? '–'}</span> },
    {
      title: 'Name', dataIndex: 'name', render: (_, c) => canEdit
        ? <Button type="link" size="small" style={{ padding: 0 }} onClick={() => setEditing(c)}>{c.name}</Button>
        : c.name,
    },
    { title: 'Art', dataIndex: 'kind', width: 110, render: (_, c) => <Tag bordered={false}>{c.kind === 'company' ? 'Firma' : 'Person'}</Tag> },
    { title: 'Straße', dataIndex: 'street', render: (_, c) => c.street ?? '–' },
    { title: 'Ort', key: 'place', width: 200, render: (_, c) => [c.postalCode, c.city].filter(Boolean).join(' ') || '–' },
    { title: 'Telefon', dataIndex: 'phone', width: 170, render: (_, c) => c.phone ? <a href={`tel:${c.phone}`}><PhoneOutlined /> {c.phone}</a> : '–' },
    { title: 'E-Mail', dataIndex: 'email', width: 230, ellipsis: true, render: (_, c) => c.email ? <a href={`mailto:${c.email}`}><MailOutlined /> {c.email}</a> : '–' },
  ];

  const save = async (values: ContactForm) => {
    const body = {
      kind: values.kind, name: values.name.trim(), customerNumber: values.customerNumber || null, street: values.street || null,
      postalCode: values.postalCode || null, city: values.city || null, phone: values.phone || null, email: values.email || null,
      notes: values.notes || null,
    };
    try {
      if (editing && editing !== 'new') {
        await put(`contacts/${editing.id}`, { ...body, version: editing.version });
      } else {
        await post('contacts', { ...body, id: uuidv7() });
      }
      message.success('Kunde gespeichert');
      setEditing(null);
      action.current?.reload();
      return true;
    } catch (e) {
      message.error(problemMessage(e));
      return false;
    }
  };

  const initial: ContactForm = editing && editing !== 'new'
    ? {
        kind: editing.kind, name: editing.name, customerNumber: editing.customerNumber ?? '', street: editing.street ?? '',
        postalCode: editing.postalCode ?? '', city: editing.city ?? '', phone: editing.phone ?? '', email: editing.email ?? '', notes: editing.notes ?? '',
      }
    : { kind: 'person', name: '' };

  return (
    <PageContainer title="Kunden" subTitle="Kundenstamm für Bauakten und Anfragen">
      <ProTable<ContactResponse>
        actionRef={action}
        rowKey="id"
        size="small"
        bordered
        cardBordered
        search={false}
        columns={columns}
        pagination={{ pageSize: 25, showSizeChanger: false }}
        options={{ reload: true, density: true, setting: true }}
        onRow={(c) => ({ onDoubleClick: () => canEdit && setEditing(c) })}
        toolbar={{
          search: { placeholder: 'Name, Kundennummer, Ort', allowClear: true, onSearch: (v: string) => { setSearch(v.trim()); action.current?.reload(); }, style: { width: 300 } },
          actions: canEdit ? [<Button key="new" type="primary" icon={<UserAddOutlined />} onClick={() => setEditing('new')}>Neuer Kunde</Button>] : [],
        }}
        params={{ search }}
        request={async (p) => {
          const data = await get<ContactResponse[]>('contacts', { limit: 200, search: (p.search as string) || undefined });
          return { data, success: true, total: data.length };
        }}
      />
      <ModalForm<ContactForm>
        key={editing && editing !== 'new' ? editing.id : 'new'}
        title={editing === 'new' ? 'Neuer Kunde' : 'Kunde bearbeiten'}
        open={editing !== null}
        width={620}
        initialValues={initial}
        modalProps={{ destroyOnHidden: true, onCancel: () => setEditing(null) }}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        onOpenChange={(open) => { if (!open) setEditing(null); }}
        onFinish={save}
      >
        <ProFormRadio.Group name="kind" label="Art" radioType="button" options={[{ value: 'person', label: 'Person' }, { value: 'company', label: 'Firma' }]} />
        <Space.Compact block>
          <ProFormText name="name" label="Name" width="lg" rules={[{ required: true, message: 'Bitte den Namen angeben.' }]} />
          <ProFormText name="customerNumber" label="Kundennummer" width="sm" />
        </Space.Compact>
        <ProFormText name="street" label="Straße" />
        <Space.Compact block>
          <ProFormText name="postalCode" label="PLZ" width={120} />
          <ProFormText name="city" label="Ort" width="md" />
        </Space.Compact>
        <Space.Compact block>
          <ProFormText name="phone" label="Telefon" width="sm" />
          <ProFormText name="email" label="E-Mail" width="md" rules={[{ type: 'email', message: 'Bitte eine gültige E-Mail-Adresse angeben.' }]} />
        </Space.Compact>
        <ProFormTextArea name="notes" label="Notizen" fieldProps={{ rows: 3 }} />
      </ModalForm>
    </PageContainer>
  );
}
