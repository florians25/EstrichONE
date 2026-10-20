import { CloseCircleOutlined, EditOutlined, FolderOpenOutlined, PlayCircleOutlined, PlusOutlined, ReloadOutlined, UndoOutlined } from '@ant-design/icons';
import { PageContainer, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { App, Button, Descriptions, Drawer, Popconfirm, Space, Steps, Typography } from 'antd';
import { useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { get, post, problemMessage } from '../../api/client';
import type { CallResponse, ConvertInquiryResponse, InquiryStatus } from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { parseDay } from '../../shared/days';
import { CallFormModal, callTime, caller, InquiryStatusTag, sourceLabels } from './calls';

const day = (v: string | null) => (v ? parseDay(v).toLocaleDateString('de-DE') : '–');

/** Anfragen als Tickets: Offen → In Bearbeitung → Bauakte erstellt (oder Abgebrochen), wie in Estrich Web. */
export default function InquiriesPage() {
  const { has } = useAuth();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const action = useRef<ActionType>(undefined);
  const [tab, setTab] = useState<'open' | 'all'>('open');
  const [editing, setEditing] = useState<CallResponse | 'new' | null>(null);
  const [detail, setDetail] = useState<CallResponse | null>(null);
  const canWrite = has('contacts.edit');

  const reload = () => action.current?.reload();
  const setStatus = async (c: CallResponse, status: InquiryStatus) => {
    try {
      setDetail(await post<CallResponse>(`calls/${c.id}/status`, { status }));
      reload();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const convert = async (c: CallResponse) => {
    try {
      const result = await post<ConvertInquiryResponse>(`calls/${c.id}/convert`, {});
      message.success(`Bauakte ${result.bauakteNumber ?? ''} angelegt`);
      navigate(`/bauakten/${result.bauakteId}`);
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const columns: ProColumns<CallResponse>[] = [
    { title: 'Status', dataIndex: 'inquiryStatus', width: 150, render: (_, c) => <InquiryStatusTag status={c.inquiryStatus} /> },
    { title: 'Eingang', dataIndex: 'calledAt', width: 150, render: (_, c) => <span className="tabular">{callTime(c.calledAt)}</span> },
    { title: 'Anfragender', key: 'caller', width: 240, ellipsis: true, render: (_, c) => <a onClick={(e) => { e.stopPropagation(); setDetail(c); }}>{caller(c)}</a> },
    { title: 'Anliegen', dataIndex: 'subject', ellipsis: true, render: (_, c) => c.subject ?? '–' },
    { title: 'Ort', key: 'city', width: 140, render: (_, c) => c.siteCity ?? '–' },
    { title: 'Fläche', dataIndex: 'areaSquareMeters', width: 110, align: 'right', render: (_, c) => (c.areaSquareMeters ? `${c.areaSquareMeters.toLocaleString('de-DE')} m²` : '–') },
    { title: 'Wunschtermin', dataIndex: 'desiredStart', width: 130, render: (_, c) => day(c.desiredStart) },
    { title: 'Kanal', dataIndex: 'source', width: 100, render: (_, c) => sourceLabels[c.source] },
    { title: 'Bearbeiter', dataIndex: 'handledByName', width: 150, render: (_, c) => c.handledByName ?? '–' },
  ];
  const step = (s: InquiryStatus) => ({ open: 0, inProgress: 1, converted: 2, cancelled: 2 })[s];

  return (
    <PageContainer title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">Auftragsbearbeitung</Typography.Text>Anfragen</Space>}>
      <ProTable<CallResponse>
        actionRef={action} rowKey="id" size="small" bordered cardBordered search={false} columns={columns} pagination={{ pageSize: 50 }}
        options={{ reload: true, density: true, setting: true }}
        onRow={(c) => ({ onClick: () => setDetail(c), style: { cursor: 'pointer' } })}
        locale={{ emptyText: 'Keine Anfragen in dieser Ansicht.' }}
        toolbar={{
          menu: { type: 'tab', activeKey: tab, items: [{ key: 'open', label: 'Offen und in Bearbeitung' }, { key: 'all', label: 'Alle' }], onChange: (k) => { setTab(k as 'open' | 'all'); setTimeout(reload); } },
          actions: canWrite ? [<Button key="new" type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>Anfrage erfassen</Button>] : [],
        }}
        params={{ tab }}
        request={async (p) => {
          const data = await get<CallResponse[]>('calls', { kind: 'inquiry', open: p.tab === 'open' || undefined });
          return { data, success: true, total: data.length };
        }}
      />
      <Drawer title={detail ? `Anfrage: ${caller(detail)}` : ''} open={detail !== null} onClose={() => setDetail(null)} width={560}
        extra={detail && canWrite && detail.inquiryStatus !== 'converted' && <Button icon={<EditOutlined />} onClick={() => setEditing(detail)}>Bearbeiten</Button>}>
        {detail && (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Steps size="small" current={step(detail.inquiryStatus)} status={detail.inquiryStatus === 'cancelled' ? 'error' : undefined}
              items={[{ title: 'Offen' }, { title: 'In Bearbeitung' }, { title: detail.inquiryStatus === 'cancelled' ? 'Abgebrochen' : 'Bauakte erstellt' }]} />
            <Descriptions column={1} size="small" bordered items={[
              { key: 'status', label: 'Status', children: <InquiryStatusTag status={detail.inquiryStatus} /> },
              { key: 'when', label: 'Eingang', children: `${callTime(detail.calledAt)} · ${sourceLabels[detail.source]}` },
              { key: 'name', label: 'Name', children: detail.callerName ?? '–' },
              { key: 'company', label: 'Firma', children: detail.company ?? '–' },
              { key: 'phone', label: 'Telefon', children: detail.callerPhone ? <a href={`tel:${detail.callerPhone}`}>{detail.callerPhone}</a> : '–' },
              { key: 'mail', label: 'E-Mail', children: detail.callerEmail ? <a href={`mailto:${detail.callerEmail}`}>{detail.callerEmail}</a> : '–' },
              { key: 'customer', label: 'Kunde', children: detail.customerName ?? '–' },
              { key: 'site', label: 'Baustelle', children: [detail.siteStreet, [detail.sitePostalCode, detail.siteCity].filter(Boolean).join(' ')].filter(Boolean).join(', ') || '–' },
              { key: 'area', label: 'Fläche / Estrich', children: `${detail.areaSquareMeters ? `${detail.areaSquareMeters.toLocaleString('de-DE')} m²` : '–'} · ${detail.screedType ?? '–'}` },
              { key: 'start', label: 'Wunschtermin', children: day(detail.desiredStart) },
              { key: 'subject', label: 'Anliegen', children: detail.subject ?? '–' },
              { key: 'notes', label: 'Ticket-Notiz', children: <span className="pre">{detail.notes ?? '–'}</span> },
              { key: 'next', label: 'Nächster Schritt', children: detail.nextStep ?? '–' },
              { key: 'bauakte', label: 'Bauakte', children: detail.convertedBauakteId ? <Link to={`/bauakten/${detail.convertedBauakteId}`}>{detail.convertedBauakteNumber ?? 'öffnen'}</Link> : '–' },
            ]} />
            {canWrite && detail.inquiryStatus !== 'converted' && (
              <Space wrap>
                {detail.inquiryStatus === 'open' && <Button icon={<PlayCircleOutlined />} onClick={() => void setStatus(detail, 'inProgress')}>In Bearbeitung</Button>}
                {detail.inquiryStatus !== 'cancelled'
                  ? <Button icon={<CloseCircleOutlined />} onClick={() => void setStatus(detail, 'cancelled')}>Abbrechen</Button>
                  : <Button icon={<UndoOutlined />} onClick={() => void setStatus(detail, 'open')}>Wieder öffnen</Button>}
                {has('bauakten.create') && detail.inquiryStatus !== 'cancelled' && (
                  <Popconfirm title="Anfrage in eine Bauakte umwandeln?" description="Ohne Kunde wird einer aus Name/Firma angelegt." okText="Umwandeln" cancelText="Abbrechen"
                    onConfirm={() => void convert(detail)}>
                    <Button type="primary" icon={<FolderOpenOutlined />}>In Bauakte umwandeln</Button>
                  </Popconfirm>
                )}
              </Space>
            )}
            <Button icon={<ReloadOutlined />} type="link" onClick={async () => setDetail(await get<CallResponse>(`calls/${detail.id}`))}>Aktualisieren</Button>
          </Space>
        )}
      </Drawer>
      {editing !== null && <CallFormModal kind="inquiry" call={editing === 'new' ? null : editing} open onClose={() => setEditing(null)}
        onSaved={(c) => { setEditing(null); if (detail) setDetail(c); reload(); }} />}
    </PageContainer>
  );
}
