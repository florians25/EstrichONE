import { AppleOutlined, AndroidOutlined, MobileOutlined, QrcodeOutlined, WindowsOutlined } from '@ant-design/icons';
import { ModalForm, PageContainer, ProFormRadio, ProFormText, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { Alert, App, Button, Modal, Result, Space, Typography } from 'antd';
import QRCode from 'qrcode';
import { useRef, useState } from 'react';
import { get, post, problemMessage } from '../api/client';
import type { DeviceKind, DeviceResponse, EnrollmentCodeResponse } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { DeviceStatusTag, formatDateTime } from '../shared/status';

const platformIcons = { ios: <AppleOutlined />, android: <AndroidOutlined />, windows: <WindowsOutlined />, other: <MobileOutlined /> };

/** IT Center › Geräte: freigeben, sperren, entfernen, neues Gerät per QR-Code einrichten. */
export default function DevicesPage() {
  const { has } = useAuth();
  const { message, modal } = App.useApp();
  const action = useRef<ActionType>(undefined);
  const [enrolling, setEnrolling] = useState(false);
  const [code, setCode] = useState<{ code: EnrollmentCodeResponse; qr: string | null } | null>(null);
  const [pending, setPending] = useState(0);
  const canManage = has('devices.manage');

  const act = async (d: DeviceResponse, verb: 'approve' | 'lock' | 'unlock' | 'retire') => {
    try {
      await post(`devices/${d.id}/${verb}`);
      action.current?.reload();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const retire = (d: DeviceResponse) => modal.confirm({
    title: `${d.name} entfernen?`, content: 'Alle Sitzungen des Geräts enden sofort; es muss neu eingerichtet werden.',
    okText: 'Entfernen', okButtonProps: { danger: true }, cancelText: 'Abbrechen', onOk: () => act(d, 'retire'),
  });

  const createCode = async (values: { kind: DeviceKind; deviceName?: string }) => {
    try {
      const created = await post<EnrollmentCodeResponse>('devices/enrollment-codes', { kind: values.kind, deviceName: values.deviceName || null });
      const qr = created.link ? await QRCode.toDataURL(created.link, { width: 512, margin: 1, errorCorrectionLevel: 'M' }) : null;
      setCode({ code: created, qr });
      setEnrolling(false);
      return true;
    } catch (e) {
      message.error(problemMessage(e));
      return false;
    }
  };

  const columns: ProColumns<DeviceResponse>[] = [
    {
      title: 'Gerät', dataIndex: 'name', render: (_, d) => (
        <Space>
          <span style={{ fontSize: 18, color: '#0B2D5B' }}>{platformIcons[d.platform]}</span>
          <span>{d.name}<br /><Typography.Text type="secondary" style={{ fontSize: 12 }}>{d.model ?? d.platform}</Typography.Text></span>
        </Space>
      ),
    },
    { title: 'Nutzung', dataIndex: 'kind', width: 120, render: (_, d) => (d.kind === 'pool' ? 'Poolgerät' : 'Persönlich') },
    { title: 'Status', dataIndex: 'status', width: 150, render: (_, d) => <DeviceStatusTag status={d.status} /> },
    { title: 'App / System', key: 'versions', width: 180, render: (_, d) => <Typography.Text type="secondary">{d.appVersion ?? '–'} · {d.osVersion ?? '–'}</Typography.Text> },
    { title: 'Zuletzt gesehen', dataIndex: 'lastSeenAt', width: 170, render: (_, d) => <span className="tabular">{formatDateTime(d.lastSeenAt)}</span> },
    {
      title: 'Aktionen', key: 'actions', width: 210, hideInTable: !canManage, render: (_, d) => (
        <Space size={0}>
          {d.status === 'pending' && <Button type="link" size="small" onClick={() => void act(d, 'approve')}>Freigeben</Button>}
          {d.status === 'active' && <Button type="link" size="small" onClick={() => void act(d, 'lock')}>Sperren</Button>}
          {d.status === 'locked' && <Button type="link" size="small" onClick={() => void act(d, 'unlock')}>Entsperren</Button>}
          {d.status !== 'retired' && <Button type="link" size="small" danger onClick={() => retire(d)}>Entfernen</Button>}
        </Space>
      ),
    },
  ];

  return (
    <PageContainer title="Geräte" subTitle="iPhones, iPads, Android-Geräte und Windows-Arbeitsplätze">
      {pending > 0 && (
        <Alert role="status" type="warning" showIcon style={{ marginBottom: 16 }}
          message={`${pending} ${pending === 1 ? 'Gerät wartet' : 'Geräte warten'} auf Freigabe.`} />
      )}
      <ProTable<DeviceResponse>
        actionRef={action}
        rowKey="id"
        size="small"
        bordered
        cardBordered
        search={false}
        columns={columns}
        pagination={false}
        locale={{ emptyText: 'Noch keine Geräte registriert.' }}
        options={{ reload: true, density: true, setting: true }}
        toolbar={{ actions: canManage ? [<Button key="new" type="primary" icon={<QrcodeOutlined />} onClick={() => setEnrolling(true)}>Gerät einrichten</Button>] : [] }}
        request={async () => {
          const data = await get<DeviceResponse[]>('devices');
          setPending(data.filter((d) => d.status === 'pending').length);
          return { data, success: true, total: data.length };
        }}
      />
      <ModalForm<{ kind: DeviceKind; deviceName?: string }>
        title="Gerät einrichten"
        open={enrolling}
        width={480}
        initialValues={{ kind: 'personal' }}
        modalProps={{ destroyOnHidden: true, onCancel: () => setEnrolling(false) }}
        submitter={{ searchConfig: { submitText: 'Code erzeugen', resetText: 'Abbrechen' } }}
        onOpenChange={setEnrolling}
        onFinish={createCode}
      >
        <ProFormRadio.Group name="kind" label="Nutzung" layout="vertical"
          options={[{ value: 'personal', label: 'Persönliches Gerät' }, { value: 'pool', label: 'Poolgerät (mehrere Personen)' }]} />
        <ProFormText name="deviceName" label="Gerätename (optional)" placeholder="z. B. Fahrzeug 3" />
        <Typography.Paragraph type="secondary">Ein Gerät, das mit diesem Code eingerichtet wird, ist sofort freigegeben.</Typography.Paragraph>
      </ModalForm>
      <Modal title="Gerät einrichten" open={code !== null} onCancel={() => setCode(null)} width={440}
        footer={<Button type="primary" onClick={() => { setCode(null); action.current?.reload(); }}>Fertig</Button>}>
        {code && (
          <Result
            style={{ padding: '8px 0' }}
            icon={code.qr
              ? <img src={code.qr} alt="QR-Code zur Einrichtung" width={240} height={240} style={{ background: '#fff', padding: 8, borderRadius: 8, border: '1px solid #f0f0f0' }} />
              : <QrcodeOutlined />}
            title={<>Code zum Abtippen: <Typography.Text code copyable data-testid="enrollment-code" style={{ fontSize: 20, letterSpacing: '0.08em' }}>{code.code.code}</Typography.Text></>}
            subTitle={<>
              In der App „QR-Code scannen“ wählen. Der Code gilt bis {new Intl.DateTimeFormat('de-DE', { timeStyle: 'short' }).format(new Date(code.code.expiresAt))} Uhr und nur einmal.
              {!code.qr && <Alert style={{ marginTop: 12 }} type="info" message="Für einen QR-Code muss die öffentliche Serveradresse konfiguriert sein." />}
            </>}
          />
        )}
      </Modal>
    </PageContainer>
  );
}
