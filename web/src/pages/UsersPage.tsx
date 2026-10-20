import { KeyOutlined, LockOutlined, UnlockOutlined, UserAddOutlined } from '@ant-design/icons';
import { ModalForm, PageContainer, ProFormCheckbox, ProFormText, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, Avatar, Button, Space, Tag, Typography } from 'antd';
import { useRef, useState } from 'react';
import { get, post, problemMessage, put } from '../api/client';
import type { RoleResponse, UserListItem, UserWithPassword } from '../api/models';
import { formatDateTime } from '../shared/status';

interface UserForm {
  displayName: string;
  userName: string;
  email?: string;
  phone?: string;
  roleIds: string[];
}

/** Benutzer verwalten: Einmalpasswort und Zwangswechsel, keine Passwörter per E-Mail. */
export default function UsersPage() {
  const { message, modal } = App.useApp();
  const action = useRef<ActionType>(undefined);
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => get<RoleResponse[]>('access/roles') });
  const [editing, setEditing] = useState<UserListItem | 'new' | null>(null);
  const [oneTime, setOneTime] = useState<UserWithPassword | null>(null);
  const roleName = (id: string) => roles.data?.find((r) => r.id === id)?.name ?? '?';

  const run = async (work: () => Promise<unknown>) => {
    try {
      await work();
      action.current?.reload();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const resetPassword = (u: UserListItem) => modal.confirm({
    title: `Passwort von ${u.displayName} zurücksetzen?`,
    content: 'Alle Sitzungen der Person werden beendet. Sie erhalten ein Einmalpasswort zum Weitergeben.',
    okText: 'Zurücksetzen', cancelText: 'Abbrechen',
    onOk: () => run(async () => setOneTime(await post<UserWithPassword>(`users/${u.id}/reset-password`))),
  });

  const setActive = (u: UserListItem, active: boolean) => active
    ? void run(() => post(`users/${u.id}/activate`))
    : modal.confirm({
        title: `${u.displayName} sperren?`, content: 'Die Person wird sofort auf allen Geräten abgemeldet.',
        okText: 'Sperren', okButtonProps: { danger: true }, cancelText: 'Abbrechen',
        onOk: () => run(() => post(`users/${u.id}/deactivate`)),
      });

  const save = async (values: UserForm) => {
    const contact = { displayName: values.displayName.trim(), email: values.email || null, phone: values.phone || null };
    try {
      if (editing && editing !== 'new') {
        await put(`users/${editing.id}`, contact);
        await put(`access/users/${editing.id}/roles`, { roleIds: values.roleIds ?? [] });
        message.success('Benutzer gespeichert');
      } else {
        setOneTime(await post<UserWithPassword>('users', { ...contact, userName: values.userName.trim(), roleIds: values.roleIds ?? [] }));
      }
      setEditing(null);
      action.current?.reload();
      return true;
    } catch (e) {
      message.error(problemMessage(e));
      return false;
    }
  };

  const columns: ProColumns<UserListItem>[] = [
    {
      title: 'Name', dataIndex: 'displayName', render: (_, u) => (
        <Space>
          <Avatar size="small" style={{ background: '#0B2D5B' }}>{u.displayName.slice(0, 1).toUpperCase()}</Avatar>
          <span>{u.displayName}<br /><Typography.Text type="secondary" style={{ fontSize: 12 }}>{u.email ?? ''}</Typography.Text></span>
        </Space>
      ),
    },
    { title: 'Anmeldename', dataIndex: 'userName', width: 160 },
    { title: 'Rollen', key: 'roles', render: (_, u) => u.roleIds.length ? u.roleIds.map((id) => <Tag key={id} bordered={false} color="blue">{roleName(id)}</Tag>) : '–' },
    { title: 'Status', dataIndex: 'isActive', width: 110, render: (_, u) => u.isActive ? <Tag color="success" bordered={false}>Aktiv</Tag> : <Tag color="error" bordered={false}>Gesperrt</Tag> },
    { title: 'Letzte Anmeldung', dataIndex: 'lastLoginAt', width: 170, render: (_, u) => <span className="tabular">{formatDateTime(u.lastLoginAt)}</span> },
    {
      title: 'Aktionen', key: 'actions', width: 320, render: (_, u) => (
        <Space size={0} wrap>
          <Button type="link" size="small" onClick={() => setEditing(u)}>Bearbeiten</Button>
          <Button type="link" size="small" icon={<KeyOutlined />} onClick={() => resetPassword(u)}>Passwort zurücksetzen</Button>
          {u.isActive
            ? <Button type="link" size="small" danger icon={<LockOutlined />} onClick={() => setActive(u, false)}>Sperren</Button>
            : <Button type="link" size="small" icon={<UnlockOutlined />} onClick={() => setActive(u, true)}>Entsperren</Button>}
        </Space>
      ),
    },
  ];

  const initial: UserForm = editing && editing !== 'new'
    ? { displayName: editing.displayName, userName: editing.userName, email: editing.email ?? '', phone: editing.phone ?? '', roleIds: editing.roleIds }
    : { displayName: '', userName: '', roleIds: [] };

  return (
    <PageContainer title="Benutzer" subTitle="Konten, Rollen und Zugänge">
      {oneTime && (
        <Alert
          data-testid="one-time-password"
          type="success"
          showIcon
          closable
          onClose={() => setOneTime(null)}
          style={{ marginBottom: 16 }}
          message={<>Einmalpasswort für <strong>{oneTime.user.displayName}</strong>: <Typography.Text code copyable={{ text: oneTime.oneTimePassword, tooltips: ['Kopieren', 'Kopiert'] }} style={{ fontSize: 16 }}>{oneTime.oneTimePassword}</Typography.Text></>}
          description="Es wird nur jetzt angezeigt. Bei der ersten Anmeldung muss ein eigenes Passwort gewählt werden."
        />
      )}
      <ProTable<UserListItem>
        actionRef={action}
        rowKey="id"
        size="small"
        bordered
        cardBordered
        search={false}
        columns={columns}
        pagination={false}
        options={{ reload: true, density: true, setting: true }}
        toolbar={{ actions: [<Button key="new" type="primary" icon={<UserAddOutlined />} onClick={() => setEditing('new')}>Benutzer anlegen</Button>] }}
        request={async () => {
          const data = await get<UserListItem[]>('users');
          return { data, success: true, total: data.length };
        }}
      />
      <ModalForm<UserForm>
        key={editing && editing !== 'new' ? editing.id : 'new'}
        title={editing === 'new' ? 'Benutzer anlegen' : 'Benutzer bearbeiten'}
        open={editing !== null}
        width={560}
        initialValues={initial}
        modalProps={{ destroyOnHidden: true, onCancel: () => setEditing(null) }}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        onOpenChange={(open) => { if (!open) setEditing(null); }}
        onFinish={save}
      >
        <ProFormText name="displayName" label="Name" rules={[{ required: true, message: 'Bitte den Namen angeben.' }]} />
        <ProFormText name="userName" label="Anmeldename" disabled={editing !== 'new'} extra="Für Mitarbeiter ohne E-Mail-Adresse, z. B. „m.huber“."
          rules={[{ required: true, message: 'Bitte einen Anmeldenamen angeben.' }]} />
        <ProFormText name="email" label="E-Mail (optional)" rules={[{ type: 'email', message: 'Bitte eine gültige E-Mail-Adresse angeben.' }]} />
        <ProFormText name="phone" label="Telefon (optional)" />
        <ProFormCheckbox.Group name="roleIds" label="Rollen" layout="vertical"
          options={(roles.data ?? []).map((r) => ({
            value: r.id,
            label: <span><strong>{r.name}</strong><br /><Typography.Text type="secondary" style={{ fontSize: 12 }}>{r.description}</Typography.Text></span>,
          }))} />
      </ModalForm>
    </PageContainer>
  );
}
