import { ArrowDownOutlined, ArrowUpOutlined } from '@ant-design/icons';
import { ModalForm, ProFormDatePicker, ProFormDigit, ProFormRadio, ProFormSelect, ProFormSwitch, ProFormText, ProFormTextArea } from '@ant-design/pro-components';
import { App, Form, Space, Tag, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { put, problemMessage } from '../../api/client';
import type { CallDirection, CallKind, CallPriority, CallResponse, CallSource, InquiryStatus } from '../../api/models';
import { CustomerSelect } from '../../shared/CustomerSelect';
import { uuidv7 } from '../../shared/ids';

export const inquiryStatusLabels: Record<InquiryStatus, string> = {
  open: 'Offen', inProgress: 'In Bearbeitung', converted: 'Bauakte erstellt', cancelled: 'Abgebrochen',
};
export const inquiryStatusColors: Record<InquiryStatus, string> = { open: 'blue', inProgress: 'gold', converted: 'green', cancelled: 'default' };
export const sourceLabels: Record<CallSource, string> = { phone: 'Telefon', email: 'E-Mail', website: 'Website', personal: 'Persönlich', other: 'Sonstiges' };
export const priorityLabels: Record<CallPriority, string> = { low: 'Niedrig', normal: 'Normal', high: 'Hoch' };

export function InquiryStatusTag({ status }: { status: InquiryStatus }) {
  return <Tag color={inquiryStatusColors[status]} bordered={false} style={{ fontWeight: 600 }}>{inquiryStatusLabels[status]}</Tag>;
}

export function DirectionIcon({ direction }: { direction: CallDirection }) {
  return direction === 'outbound'
    ? <span className="call-dir out" title="Ausgehend" aria-label="Ausgehend"><ArrowUpOutlined /></span>
    : <span className="call-dir in" title="Eingehend" aria-label="Eingehend"><ArrowDownOutlined /></span>;
}

/** Wer: Name, Firma, Telefon – in einer Zeile. */
export function caller(c: Pick<CallResponse, 'callerName' | 'company' | 'customerName'>): string {
  return [c.callerName, c.company ?? c.customerName].filter(Boolean).join(' · ') || '–';
}

export const callTime = (v: string) => new Date(v).toLocaleString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });

interface FormValues {
  direction: CallDirection;
  source: CallSource;
  priority: CallPriority;
  callerName?: string;
  callerPhone?: string;
  callerEmail?: string;
  company?: string;
  customerId?: string | null;
  subject?: string;
  notes?: string;
  followUp?: boolean;
  dueDate?: Dayjs | null;
  nextStep?: string;
  areaSquareMeters?: number | null;
  screedType?: string;
  desiredStart?: Dayjs | null;
  siteStreet?: string;
  sitePostalCode?: string;
  siteCity?: string;
}

/** Vollständiges Formular für Anruf oder Anfrage (Schnellerfassung siehe Kopfzeile „Telefon“). */
export function CallFormModal({ kind, call, open, onClose, onSaved }: {
  kind: CallKind; call: CallResponse | null; open: boolean; onClose: () => void; onSaved: (c: CallResponse) => void;
}) {
  const { message } = App.useApp();
  const inquiry = kind === 'inquiry';
  const initial: FormValues = call
    ? {
        direction: call.direction, source: call.source, priority: call.priority, callerName: call.callerName ?? '', callerPhone: call.callerPhone ?? '',
        callerEmail: call.callerEmail ?? '', company: call.company ?? '', customerId: call.customerId, subject: call.subject ?? '', notes: call.notes ?? '',
        followUp: call.followUp, dueDate: call.dueDate ? dayjs(call.dueDate) : null, nextStep: call.nextStep ?? '',
        areaSquareMeters: call.areaSquareMeters, screedType: call.screedType ?? '', desiredStart: call.desiredStart ? dayjs(call.desiredStart) : null,
        siteStreet: call.siteStreet ?? '', sitePostalCode: call.sitePostalCode ?? '', siteCity: call.siteCity ?? '',
      }
    : { direction: 'inbound', source: inquiry ? 'email' : 'phone', priority: 'normal', followUp: false };

  return (
    <ModalForm<FormValues>
      key={call?.id ?? `new-${kind}`}
      title={call ? (inquiry ? 'Anfrage bearbeiten' : 'Anruf bearbeiten') : (inquiry ? 'Anfrage erfassen' : 'Anruf erfassen')}
      open={open} width={720} initialValues={initial}
      modalProps={{ destroyOnHidden: true, onCancel: onClose }}
      submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
      onOpenChange={(o) => { if (!o) onClose(); }}
      onFinish={async (v) => {
        const id = call?.id ?? uuidv7();
        try {
          const saved = await put<CallResponse>(`calls/${id}`, {
            id, kind, direction: v.direction, source: v.source, priority: v.priority, callerName: v.callerName || null, callerPhone: v.callerPhone || null,
            callerEmail: v.callerEmail || null, company: v.company || null, customerId: v.customerId ?? null, bauakteId: call?.bauakteId ?? null,
            subject: v.subject || null, notes: v.notes || null, followUp: !!v.followUp, dueDate: v.dueDate ? v.dueDate.format('YYYY-MM-DD') : null,
            nextStep: v.nextStep || null, areaSquareMeters: v.areaSquareMeters ?? null, screedType: v.screedType || null,
            desiredStart: v.desiredStart ? v.desiredStart.format('YYYY-MM-DD') : null, siteStreet: v.siteStreet || null,
            sitePostalCode: v.sitePostalCode || null, siteCity: v.siteCity || null, calledAt: call?.calledAt ?? null,
          });
          message.success(inquiry ? 'Anfrage gespeichert' : 'Anruf gespeichert');
          onSaved(saved);
          return true;
        } catch (e) {
          message.error(problemMessage(e));
          return false;
        }
      }}
    >
      <Space wrap size={24}>
        {!inquiry && <ProFormRadio.Group name="direction" label="Richtung" radioType="button"
          options={[{ value: 'inbound', label: 'Eingehend' }, { value: 'outbound', label: 'Ausgehend' }]} />}
        <ProFormSelect name="source" label="Kanal" width="sm" allowClear={false} options={(Object.keys(sourceLabels) as CallSource[]).map((k) => ({ value: k, label: sourceLabels[k] }))} />
        <ProFormSelect name="priority" label="Priorität" width="xs" allowClear={false} options={(Object.keys(priorityLabels) as CallPriority[]).map((k) => ({ value: k, label: priorityLabels[k] }))} />
      </Space>
      <Space.Compact block>
        <ProFormText name="callerName" label="Name" width="md" />
        <ProFormText name="company" label="Firma" width="md" />
      </Space.Compact>
      <Space.Compact block>
        <ProFormText name="callerPhone" label="Telefon" width="md" />
        <ProFormText name="callerEmail" label="E-Mail" width="md" rules={[{ type: 'email', message: 'Bitte eine gültige E-Mail-Adresse angeben.' }]} />
      </Space.Compact>
      <Form.Item name="customerId" label="Kunde (Stammdaten)" extra={inquiry ? 'Ohne Kunde wird beim Umwandeln einer aus Name/Firma angelegt.' : undefined}>
        <CustomerSelect />
      </Form.Item>
      <ProFormText name="subject" label={inquiry ? 'Anliegen' : 'Betreff'} />
      <ProFormTextArea name="notes" label={inquiry ? 'Ticket-Notiz' : 'Gesprächsnotiz'} fieldProps={{ rows: 4 }} />
      {inquiry && (
        <>
          <Typography.Text strong>Baustelle laut Anfrage</Typography.Text>
          <Space.Compact block style={{ marginTop: 8 }}>
            <ProFormText name="siteStreet" label="Straße" width="md" />
            <ProFormText name="sitePostalCode" label="PLZ" width={100} />
            <ProFormText name="siteCity" label="Ort" width="sm" />
          </Space.Compact>
          <Space.Compact block>
            <ProFormDigit name="areaSquareMeters" label="Fläche (m²)" width="sm" min={0} fieldProps={{ precision: 2, decimalSeparator: ',' }} />
            <ProFormText name="screedType" label="Estrichart" width="sm" placeholder="z. B. CAF, CT" />
            <ProFormDatePicker name="desiredStart" label="Wunschtermin" width="sm" fieldProps={{ format: 'DD.MM.YYYY' }} />
          </Space.Compact>
        </>
      )}
      <Space wrap size={24} align="end">
        <ProFormSwitch name="followUp" label="Wiedervorlage nötig" />
        <ProFormDatePicker name="dueDate" label="Wiedervorlage am" width="sm" fieldProps={{ format: 'DD.MM.YYYY' }} />
        <ProFormText name="nextStep" label="Nächster Schritt" width="md" placeholder="z. B. Angebot schicken" />
      </Space>
    </ModalForm>
  );
}
