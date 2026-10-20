import {
  AppstoreOutlined, AuditOutlined, FieldTimeOutlined, FileAddOutlined, PhoneOutlined, CalendarOutlined, CarOutlined, ClusterOutlined, CloudServerOutlined, CustomerServiceOutlined, EnvironmentOutlined,
  FileSearchOutlined, InboxOutlined,
  KeyOutlined, LogoutOutlined, MobileOutlined, ProjectOutlined, ScheduleOutlined, SearchOutlined, TeamOutlined, UnorderedListOutlined, UserOutlined,
} from '@ant-design/icons';
import { ProLayout, type MenuDataItem } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { Badge, Button, Dropdown, Input, Space, Typography } from 'antd';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { get } from '../api/client';
import { QuickCallProvider, useQuickCall } from './QuickCall';
import type { ServerInfo } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { brand } from '../theme';

interface NavEntry {
  path: string;
  name: string;
  icon?: React.ReactNode;
  permission?: string;
  children?: NavEntry[];
}

const navigation: NavEntry[] = [
  { path: '/start', name: 'Übersicht', icon: <AppstoreOutlined /> },
  {
    path: '/auftraege', name: 'Auftragsbearbeitung', children: [
      { path: '/tagesuebersicht', name: 'Tagesübersicht', icon: <ScheduleOutlined />, permission: 'bauakten.read' },
      { path: '/bauakten', name: 'Bauakten', icon: <ProjectOutlined />, permission: 'bauakten.read' },
      { path: '/anfragen', name: 'Anfragen', icon: <InboxOutlined />, permission: 'contacts.read' },
      { path: '/bauakten/karte', name: 'Baustellenkarte', icon: <EnvironmentOutlined />, permission: 'bauakten.read' },
    ],
  },
  {
    path: '/disposition', name: 'Planung', children: [
      { path: '/planung', name: 'Kolonnenplanung', icon: <ClusterOutlined />, permission: 'planning.read' },
      { path: '/planung/kalender', name: 'Kalender', icon: <CalendarOutlined />, permission: 'planning.read' },
      { path: '/planung/tageseinteilung', name: 'Tageseinteilung', icon: <FieldTimeOutlined />, permission: 'planning.read' },
      { path: '/planung/arbeitsplan', name: 'Arbeitsplan', icon: <UnorderedListOutlined />, permission: 'planning.read' },
    ],
  },
  {
    path: '/stammdaten', name: 'Büro und Stammdaten', children: [
      { path: '/kunden', name: 'Kunden', icon: <TeamOutlined />, permission: 'contacts.read' },
      { path: '/anrufe', name: 'Anrufe', icon: <PhoneOutlined />, permission: 'contacts.read' },
      { path: '/planung/ressourcen', name: 'Kolonnen und Geräte', icon: <CarOutlined />, permission: 'planning.read' },
    ],
  },
  {
    path: '/it', name: 'IT Center', children: [
      { path: '/verwaltung/benutzer', name: 'Benutzer', icon: <UserOutlined />, permission: 'users.manage' },
      { path: '/it-center/geraete', name: 'Geräte', icon: <MobileOutlined />, permission: 'devices.read' },
      { path: '/it-center/helpdesk', name: 'Helpdesk', icon: <CustomerServiceOutlined />, permission: 'helpdesk.agent' },
    ],
  },
  { path: '/system', name: 'System', children: [{ path: '/system/sicherung', name: 'Sicherung', icon: <CloudServerOutlined />, permission: 'backup.manage' },
    { path: '/system/protokoll', name: 'Protokoll', icon: <AuditOutlined />, permission: 'audit.read' }] },
];

function Logo() {
  return (
    <svg width="22" height="22" viewBox="0 0 22 22" aria-hidden="true">
      <rect x="2" y="4" width="18" height="3.5" rx="1" fill="#fff" />
      <rect x="2" y="9.5" width="13" height="3.5" rx="1" fill="#fff" />
      <rect x="2" y="15" width="18" height="3.5" rx="1" fill={brand.blue} />
    </svg>
  );
}

/** Programmrahmen: Kopf in Navy mit Suche und Benutzer, gruppierte Navigation, Statuszeile. */
export function AppLayout() {
  return <QuickCallProvider><Shell /></QuickCallProvider>;
}

function Shell() {
  const quickCall = useQuickCall();
  const { user, has, signOut } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const server = useQuery({ queryKey: ['server-info'], queryFn: () => get<ServerInfo>('server-info'), staleTime: Infinity });

  const filter = (entries: NavEntry[]): MenuDataItem[] =>
    entries
      .map((e) => ({ ...e, children: e.children ? filter(e.children) : undefined }))
      .filter((e) => (e.children ? e.children.length > 0 : !e.permission || has(e.permission)));

  const selected = location.pathname === '/bauakten' && location.search.includes('status=inquiry') ? '/bauakten?status=inquiry' : location.pathname;

  return (
    <ProLayout
      title="EstrichONE"
      logo={<Logo />}
      layout="mix"
      splitMenus={false}
      fixSiderbar
      fixedHeader
      siderWidth={236}
      contentStyle={{ minHeight: 'calc(100vh - 52px - 33px)' }}
      menu={{ type: 'group' }}
      route={{ path: '/', routes: filter(navigation) }}
      location={{ pathname: selected }}
      selectedKeys={[selected]}
      token={{
        header: { colorBgHeader: brand.navy, colorHeaderTitle: '#fff', colorTextRightActionsItem: '#fff', heightLayoutHeader: 52 },
        sider: { colorMenuBackground: '#fff', colorBgMenuItemSelected: '#E6EEF8', colorTextMenuSelected: brand.navy, colorTextMenuTitle: '#6B7280' },
        pageContainer: { paddingBlockPageContainerContent: 16, paddingInlinePageContainerContent: 24 },
      }}
      menuItemRender={(item, dom) => (
        <a href={item.path} onClick={(event) => { event.preventDefault(); navigate(item.path ?? '/'); }}>{dom}</a>
      )}
      onMenuHeaderClick={() => navigate('/start')}
      actionsRender={() => [
        has('bauakten.create') && (
          <Button key="new" type="primary" icon={<FileAddOutlined />} className="header-button" onClick={() => navigate('/bauakten?neu=bauakte')}>Neue Akte</Button>
        ),
        has('contacts.edit') && (
          <Button key="phone" icon={<PhoneOutlined />} className="header-button ghost" onClick={() => quickCall.open()}>Telefon</Button>
        ),
        <Input.Search
          key="search"
          placeholder="Bauakte suchen: Nummer, Bauvorhaben, Ort"
          allowClear
          prefix={<SearchOutlined />}
          aria-label="Bauakten suchen"
          style={{ width: 300 }}
          onSearch={(value) => navigate(value.trim() ? `/bauakten?search=${encodeURIComponent(value.trim())}` : '/bauakten')}
        />,
        server.data ? <Typography.Text key="company" style={{ color: '#dbe5f3' }}>{server.data.serverName}</Typography.Text> : null,
      ].filter(Boolean)}
      avatarProps={{
        title: user?.displayName,
        size: 'small',
        style: { background: brand.blue },
        children: (user?.displayName ?? '?').split(' ').map((p) => p[0]).slice(0, 2).join(''),
        render: (_, dom) => (
          <Dropdown
            menu={{
              items: [
                { key: 'password', icon: <KeyOutlined />, label: 'Passwort ändern', onClick: () => navigate('/konto/passwort') },
                { type: 'divider' },
                { key: 'logout', icon: <LogoutOutlined />, label: 'Abmelden', onClick: async () => { await signOut(); navigate('/anmelden'); } },
              ],
            }}
          >
            <button type="button" aria-label={`Benutzermenü ${user?.displayName ?? ''}`} style={{ all: 'unset', cursor: 'pointer' }}>{dom}</button>
          </Dropdown>
        ),
      }}
      footerRender={() => (
        <div style={{ display: 'flex', gap: 16, padding: '6px 24px', fontSize: 12, color: '#4B5563', borderTop: '1px solid #E5E7EB', background: '#F8FAFC' }}>
          <Space size={6}><Badge status={server.isError ? 'error' : 'success'} />{server.isError ? 'Server nicht erreichbar' : `Verbunden mit ${server.data?.serverName ?? 'Server'}`}</Space>
          <span>Server {server.data?.serverVersion ?? '–'}</span>
          <span style={{ marginLeft: 'auto' }}><FileSearchOutlined /> {user?.displayName}</span>
          <span>{new Date().toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' })}</span>
        </div>
      )}
    >
      <Outlet />
    </ProLayout>
  );
}
