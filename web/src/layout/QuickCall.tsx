import { PhoneOutlined, SaveOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { App, Button, DatePicker, Drawer, Form, Input, Segmented, Select, Space, Switch, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { get, problemMessage, put } from '../api/client';
import type { BauaktePage, CallDirection, CallKind } from '../api/models';
import { CustomerSelect } from '../shared/CustomerSelect';
import { uuidv7 } from '../shared/ids';

const QuickCallContext = createContext<{ open: () => void }>({ open: () => undefined });

export const useQuickCall = () => useContext(QuickCallContext);

interface QuickForm {
  who: string;
  phone?: string;
  direction: CallDirection;
  customerId?: string | null;
  bauakteId?: string | null;
  notes?: string;
  followUp: boolean;
  dueDate?: Dayjs | null;
}

function BauakteSearch({ value, onChange }: { value?: string | null; onChange?: (v: string | null) => void }) {
  const [options, setOptions] = useState<{ value: string; label: string }[]>([]);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  return (
    <Select aria-label="Bauakte" showSearch allowClear filterOption={false} placeholder="Nummer oder Bauvorhaben" value={value} notFoundContent={null}
      onChange={(v) => onChange?.(v ?? null)} options={options}
      onSearch={(text) => {
        clearTimeout(timer.current);
        timer.current = setTimeout(async () => {
          if (text.trim().length < 2) return;
          const page = await get<BauaktePage>('bauakten', { search: text.trim(), limit: 8 });
          setOptions(page.items.map((b) => ({ value: b.id, label: `${b.number ?? 'neu'} · ${b.title}` })));
        }, 200);
      }} />
  );
}

/**
 * Telefon-Schnellerfassung (wie Estrich Web): während des Gesprächs Wer und Notiz, Speichern mit Strg+Enter.
 * Als Anruf (Gesprächsnotiz) oder direkt als Anfrage. Alles Weitere lässt sich später ergänzen.
 */
export function QuickCallProvider({ children }: { children: ReactNode }) {
  const { message } = App.useApp();
  const queries = useQueryClient();
  const [open, setOpen] = useState(false);
  const [startedAt, setStartedAt] = useState(Date.now());
  const [now, setNow] = useState(Date.now());
  const [form] = Form.useForm<QuickForm>();

  useEffect(() => {
    if (!open) return undefined;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [open]);

  const start = () => {
    form.resetFields();
    setStartedAt(Date.now());
    setNow(Date.now());
    setOpen(true);
  };

  const save = async (kind: CallKind) => {
    const v = await form.validateFields();
    const id = uuidv7();
    const looksLikePhone = /^[+\d][\d\s/()-]{5,}$/.test(v.who.trim());
    try {
      await put(`calls/${id}`, {
        id, kind, direction: v.direction, source: 'phone', callerName: looksLikePhone ? null : v.who.trim(),
        callerPhone: v.phone || (looksLikePhone ? v.who.trim() : null), customerId: v.customerId ?? null,
        bauakteId: kind === 'call' ? v.bauakteId ?? null : null, notes: v.notes || null, subject: kind === 'inquiry' ? v.notes?.split('\n')[0]?.slice(0, 120) || null : null,
        followUp: v.followUp, dueDate: v.dueDate ? v.dueDate.format('YYYY-MM-DD') : null,
        calledAt: new Date(startedAt).toISOString(),
      });
      void queries.invalidateQueries({ queryKey: ['calls'] });
      message.success(kind === 'inquiry' ? 'Anfrage gespeichert' : 'Gesprächsnotiz gespeichert');
      setOpen(false);
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const seconds = Math.max(0, Math.floor((now - startedAt) / 1000));
  return (
    <QuickCallContext.Provider value={{ open: start }}>
      {children}
      <Drawer open={open} onClose={() => setOpen(false)} width={440} destroyOnHidden
        title={<Space><PhoneOutlined />Telefon-Schnellerfassung</Space>}
        extra={<span className="quick-live" role="timer"><span className="quick-dot" />Gespräch läuft – {String(Math.floor(seconds / 60)).padStart(2, '0')}:{String(seconds % 60).padStart(2, '0')}</span>}
        footer={<Space style={{ justifyContent: 'space-between', width: '100%' }}>
          <Button onClick={() => void save('inquiry')}>Als Anfrage speichern</Button>
          <Button type="primary" icon={<SaveOutlined />} onClick={() => void save('call')}>Speichern (Strg+Enter)</Button>
        </Space>}>
        <Form<QuickForm> form={form} layout="vertical" initialValues={{ direction: 'inbound', followUp: false }}
          onKeyDown={(e) => { if (e.ctrlKey && e.key === 'Enter') { e.preventDefault(); void save('call'); } }}>
          <Form.Item name="who" label="Wer ruft an?" rules={[{ required: true, whitespace: true, message: 'Name, Firma oder Telefonnummer angeben.' }]}>
            <Input autoFocus placeholder="Name, Firma oder Telefonnummer" autoComplete="off" />
          </Form.Item>
          <Space.Compact block>
            <Form.Item name="phone" label="Rückrufnummer" style={{ flex: 1 }}><Input placeholder="optional" /></Form.Item>
            <Form.Item name="direction" label="Richtung">
              <Segmented options={[{ value: 'inbound', label: 'Eingehend' }, { value: 'outbound', label: 'Ausgehend' }]} />
            </Form.Item>
          </Space.Compact>
          <Form.Item name="notes" label="Notiz">
            <Input.TextArea rows={6} placeholder="Worum geht es? Was wurde besprochen?" />
          </Form.Item>
          <Form.Item name="customerId" label="Kunde zuordnen"><CustomerSelect /></Form.Item>
          <Form.Item name="bauakteId" label="Bauakte zuordnen"><BauakteSearch /></Form.Item>
          <Space align="end">
            <Form.Item name="followUp" label="Wiedervorlage" valuePropName="checked"><Switch /></Form.Item>
            <Form.Item name="dueDate" label="am"><DatePicker format="DD.MM.YYYY" /></Form.Item>
          </Space>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>Der Zeitpunkt des Gesprächsbeginns wird übernommen. Details lassen sich unter „Anrufe“ ergänzen.</Typography.Text>
        </Form>
      </Drawer>
    </QuickCallContext.Provider>
  );
}
