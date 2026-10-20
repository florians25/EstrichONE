import { CheckOutlined, CopyOutlined, DeleteOutlined, EditOutlined, MailOutlined, PrinterOutlined, RollbackOutlined, SendOutlined, StopOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Drawer, Dropdown, Space, Typography } from 'antd';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { del, get, post, problemMessage } from '../../api/client';
import type { CompanyResponse, OrderResponse, OrderStatus } from '../../api/models';
import { OrderEditor } from './OrderEditor';
import { mailto, OrderSheet, OrderStatusTag, orderText } from './shared';

export function useCompany() {
  return useQuery({ queryKey: ['company'], staleTime: 300_000, queryFn: () => get<CompanyResponse>('company').catch(() => null) });
}

/** Bestellung ansehen (Bestellschein), abschicken, Status führen, drucken oder per E-Mail senden. */
export function OrderDetail({ orderId, canManage, onClose }: { orderId: string; canManage: boolean; onClose: () => void }) {
  const { message, modal } = App.useApp();
  const queries = useQueryClient();
  const company = useCompany();
  const order = useQuery({ queryKey: ['material-orders', orderId], queryFn: () => get<OrderResponse>(`material-orders/${orderId}`) });
  const [editing, setEditing] = useState(false);
  const o = order.data;

  const refresh = () => {
    void queries.invalidateQueries({ queryKey: ['material-orders'] });
    void queries.invalidateQueries({ queryKey: ['hints'] });
  };

  const setStatus = async (status: OrderStatus, text: string) => {
    try {
      await post(`material-orders/${orderId}/status`, { status });
      message.success(text);
      refresh();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const remove = () => modal.confirm({
    title: `Bestellung ${o?.number} löschen?`, okText: 'Löschen', okButtonProps: { danger: true }, cancelText: 'Abbrechen',
    onOk: async () => {
      try {
        await del(`material-orders/${orderId}`);
        refresh();
        onClose();
      } catch (e) {
        message.error(problemMessage(e));
      }
    },
  });

  const send = () => {
    if (!o) return;
    if (!o.supplierEmail) {
      void setStatus('ordered', `Bestellung ${o.number} als bestellt markiert`);
      return;
    }
    // Das E-Mail-Programm öffnet sich; ob die Mail wirklich rausging, weiß nur der Benutzer.
    window.location.href = mailto(o, company.data ?? null);
    modal.confirm({
      title: 'E-Mail gesendet?', content: `Wenn die Bestellung an ${o.supplierEmail} gesendet ist, wird sie als „Bestellt“ markiert.`,
      okText: 'Ja, als bestellt markieren', cancelText: 'Noch nicht',
      onOk: () => setStatus('ordered', `Bestellung ${o.number} als bestellt markiert`),
    });
  };

  const copy = async () => {
    if (!o) return;
    await navigator.clipboard.writeText(orderText(o, company.data ?? null));
    message.success('Bestelltext kopiert');
  };

  const editable = o && ['draft', 'ordered', 'confirmed'].includes(o.status);
  const actions = o && canManage ? [
    o.status === 'draft' && (
      <Dropdown.Button key="send" type="primary" icon={<SendOutlined />} onClick={send} disabled={o.lines.length === 0}
        menu={{ items: [{ key: 'mark', label: 'Nur als bestellt markieren (telefonisch/Fax)', onClick: () => void setStatus('ordered', 'Als bestellt markiert') }] }}>
        {o.supplierEmail ? 'Per E-Mail bestellen' : 'Als bestellt markieren'}
      </Dropdown.Button>
    ),
    o.status === 'ordered' && <Button key="confirm" icon={<CheckOutlined />} onClick={() => void setStatus('confirmed', 'Lieferung bestätigt')}>Bestätigt</Button>,
    ['ordered', 'confirmed'].includes(o.status) && (
      <Button key="delivered" type="primary" className="button-success" icon={<CheckOutlined />} onClick={() => void setStatus('delivered', 'Als geliefert erfasst')}>Geliefert</Button>
    ),
  ].filter(Boolean) : [];

  const more = o && canManage ? [
    editable ? { key: 'edit', icon: <EditOutlined />, label: 'Bearbeiten', onClick: () => setEditing(true) } : null,
    o.status !== 'draft' && o.supplierEmail ? { key: 'mail', icon: <MailOutlined />, label: 'E-Mail an Lieferanten', onClick: () => { window.location.href = mailto(o, company.data ?? null); } } : null,
    o.status === 'ordered' ? { key: 'back', icon: <RollbackOutlined />, label: 'Zurück zum Entwurf', onClick: () => void setStatus('draft', 'Wieder Entwurf') } : null,
    o.status === 'delivered' ? { key: 'undeliver', icon: <RollbackOutlined />, label: 'Lieferung zurücknehmen', onClick: () => void setStatus('confirmed', 'Lieferung zurückgenommen') } : null,
    o.status === 'cancelled' ? { key: 'reopen', icon: <RollbackOutlined />, label: 'Wieder als Entwurf öffnen', onClick: () => void setStatus('draft', 'Wieder Entwurf') } : null,
    editable ? { key: 'cancel', icon: <StopOutlined />, label: 'Stornieren', danger: true, onClick: () => void setStatus('cancelled', 'Bestellung storniert') } : null,
    ['draft', 'cancelled'].includes(o.status) ? { key: 'delete', icon: <DeleteOutlined />, label: 'Löschen', danger: true, onClick: remove } : null,
  ].filter((x) => x !== null) : [];

  return (
    <Drawer
      open width={860} onClose={onClose} loading={order.isLoading}
      title={o && <Space>{`Bestellung ${o.number}`}<OrderStatusTag status={o.status} /></Space>}
      extra={o && (
        <Space wrap>
          {actions}
          <Button icon={<PrinterOutlined />} href={`/bestellungen/${o.id}/druck`} target="_blank">Drucken</Button>
          <Button icon={<CopyOutlined />} onClick={() => void copy()}>Text kopieren</Button>
          {more.length > 0 && <Dropdown menu={{ items: more }}><Button>Weitere</Button></Dropdown>}
        </Space>
      )}
    >
      {o && (
        <>
          <Space size={16} wrap style={{ marginBottom: 12 }}>
            {o.bauakteId && <Link to={`/bauakten/${o.bauakteId}?reiter=material`}>Bauakte {o.bauakteNumber} öffnen</Link>}
            {o.supplierPhone && <a href={`tel:${o.supplierPhone}`}>{o.supplierName} anrufen ({o.supplierPhone})</a>}
            {o.orderedAt && <Typography.Text type="secondary">Bestellt am {new Date(o.orderedAt).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })}</Typography.Text>}
            {o.deliveredAt && <Typography.Text type="secondary">Geliefert erfasst am {new Date(o.deliveredAt).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })}</Typography.Text>}
          </Space>
          <div className="sheet-preview"><OrderSheet order={o} company={company.data ?? null} /></div>
        </>
      )}
      {editing && o && <OrderEditor editing={{ order: o }} onClose={() => setEditing(false)} onSaved={() => { setEditing(false); refresh(); }} />}
    </Drawer>
  );
}

/** Druckansicht ohne Navigation: öffnet den Druckdialog, sobald die Daten da sind. */
export function OrderPrintView({ orderId }: { orderId: string }) {
  const company = useCompany();
  const order = useQuery({ queryKey: ['material-orders', orderId], queryFn: () => get<OrderResponse>(`material-orders/${orderId}`) });
  const ready = order.data && !company.isLoading;
  return (
    <div className="print-page">
      <div className="print-toolbar">
        <Button type="primary" icon={<PrinterOutlined />} onClick={() => window.print()} disabled={!ready}>Drucken</Button>
        <Button onClick={() => window.close()}>Schließen</Button>
      </div>
      {ready && <OrderSheet order={order.data!} company={company.data ?? null} />}
      {order.isError && <Typography.Text type="danger">{problemMessage(order.error)}</Typography.Text>}
    </div>
  );
}
