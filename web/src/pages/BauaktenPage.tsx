import { FileAddOutlined, InboxOutlined } from '@ant-design/icons';
import { ModalForm, PageContainer, ProFormText, ProFormTextArea, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { App, Button, Form, Space } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { get, post, problemMessage } from '../api/client';
import type { BauakteDetail, BauakteListItem, BauaktePage, BauakteStatus } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { CustomerSelect } from '../shared/CustomerSelect';
import { uuidv7 } from '../shared/ids';
import { address, BauakteStatusTag, bauakteStatusLabels, formatDateTime } from '../shared/status';

interface NewForm {
  title: string;
  customerId?: string | null;
  street?: string;
  postalCode?: string;
  city?: string;
  description?: string;
}

/** Bauakten im Büro (AP-18): suchen, nach Status filtern, anlegen – auch als Anfrage. */
export default function BauaktenPage() {
  const { has } = useAuth();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const action = useRef<ActionType>(undefined);
  const cursors = useRef<Record<number, string | null>>({ 1: null });
  const [creating, setCreating] = useState<'planned' | 'inquiry' | null>(null);
  const status = (params.get('status') as BauakteStatus | null) ?? undefined;
  const search = params.get('search') ?? undefined;

  useEffect(() => {
    const neu = params.get('neu');
    if (neu === 'bauakte' || neu === 'anfrage') {
      setCreating(neu === 'anfrage' ? 'inquiry' : 'planned');
    }
  }, [params]);

  useEffect(() => {
    cursors.current = { 1: null };
    action.current?.reloadAndRest?.();
  }, [status, search]);

  const columns: ProColumns<BauakteListItem>[] = [
    { title: 'Nummer', dataIndex: 'number', width: 110, render: (_, r) => <span className="tabular">{r.number ?? 'neu'}</span>, search: false },
    { title: 'Bauvorhaben', dataIndex: 'title', render: (_, r) => <Link to={`/bauakten/${r.id}`}>{r.title}</Link>, search: false },
    { title: 'Baustelle', key: 'address', render: (_, r) => address(r), search: false },
    { title: 'Status', dataIndex: 'status', width: 190, render: (_, r) => <BauakteStatusTag status={r.status} />, search: false },
    { title: 'Geändert', dataIndex: 'updatedAt', width: 170, align: 'right', render: (_, r) => <span className="tabular">{formatDateTime(r.updatedAt)}</span>, search: false },
  ];

  const create = async (values: NewForm) => {
    try {
      const created = await post<BauakteDetail>('bauakten', {
        id: uuidv7(), title: values.title.trim(), customerId: values.customerId ?? null, street: values.street || null,
        postalCode: values.postalCode || null, city: values.city || null, description: values.description || null, status: creating,
      });
      message.success(creating === 'inquiry' ? 'Anfrage angelegt' : 'Bauakte angelegt');
      navigate(`/bauakten/${created.id}`);
      return true;
    } catch (e) {
      message.error(problemMessage(e));
      return false;
    }
  };

  const setFilter = (next: { status?: string; search?: string }) => {
    const p = new URLSearchParams();
    const s = 'status' in next ? next.status : status;
    const q = 'search' in next ? next.search : search;
    if (s) p.set('status', s);
    if (q) p.set('search', q);
    setParams(p);
  };

  return (
    <PageContainer title={status === 'inquiry' ? 'Anfragen' : 'Bauakten'}>
      <ProTable<BauakteListItem>
        actionRef={action}
        rowKey="id"
        size="small"
        bordered
        search={false}
        columns={columns}
        cardBordered
        pagination={{ pageSize: 50, showSizeChanger: false }}
        onRow={(record) => ({ onDoubleClick: () => navigate(`/bauakten/${record.id}`) })}
        options={{ reload: true, density: true, setting: true }}
        toolbar={{
          search: { placeholder: 'Nummer, Bauvorhaben, Adresse', defaultValue: search, allowClear: true, onSearch: (v: string) => setFilter({ search: v.trim() || undefined }), style: { width: 300 } },
          menu: {
            type: 'tab',
            activeKey: status ?? 'all',
            items: [{ key: 'all', label: 'Alle' }, ...(['inquiry', 'planned', 'inProgress', 'drying', 'completed'] as BauakteStatus[]).map((s) => ({ key: s, label: bauakteStatusLabels[s] }))],
            onChange: (key) => setFilter({ status: key === 'all' ? undefined : String(key) }),
          },
          actions: has('bauakten.create') ? [
            <Button key="inquiry" icon={<InboxOutlined />} onClick={() => setCreating('inquiry')}>Neue Anfrage</Button>,
            <Button key="new" type="primary" icon={<FileAddOutlined />} onClick={() => setCreating('planned')}>Neue Bauakte</Button>,
          ] : [],
        }}
        request={async ({ current = 1 }) => {
          const cursor = cursors.current[current] ?? null;
          const page = await get<BauaktePage>('bauakten', { limit: 50, status, search, cursor });
          cursors.current[current + 1] = page.nextCursor;
          return { data: page.items, success: true, total: (current - 1) * 50 + page.items.length + (page.nextCursor ? 1 : 0) };
        }}
      />
      <ModalForm<NewForm>
        title={creating === 'inquiry' ? 'Neue Anfrage' : 'Neue Bauakte'}
        open={creating !== null}
        width={560}
        modalProps={{ destroyOnHidden: true, onCancel: () => setCreating(null) }}
        submitter={{ searchConfig: { submitText: 'Anlegen', resetText: 'Abbrechen' } }}
        onOpenChange={(open) => { if (!open) setCreating(null); }}
        onFinish={create}
      >
        <ProFormText name="title" label="Bauvorhaben" placeholder="z. B. EFH Müller, Neubau" rules={[{ required: true, message: 'Bitte das Bauvorhaben angeben.' }]} />
        <Form.Item name="customerId" label="Kunde"><CustomerSelect /></Form.Item>
        <ProFormText name="street" label="Straße" />
        <Space.Compact block>
          <ProFormText name="postalCode" label="PLZ" width={120} />
          <ProFormText name="city" label="Ort" width="md" />
        </Space.Compact>
        <ProFormTextArea name="description" label="Beschreibung" fieldProps={{ rows: 3 }} />
      </ModalForm>
    </PageContainer>
  );
}
