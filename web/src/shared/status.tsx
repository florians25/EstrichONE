import { Badge, Tag } from 'antd';
import type { BauakteStatus, DeviceStatus, TicketCategory, TicketPriority, TicketStatus } from '../api/models';

/** Status immer mit Farbe, Punkt und Text (05, Abschnitt 2.3). */
function StatusTag({ color, text, strike }: { color: string; text: string; strike?: boolean }) {
  return (
    <Tag color={color} bordered={false} style={{ fontWeight: 600, textDecoration: strike ? 'line-through' : undefined }}>
      <Badge color="currentColor" style={{ marginRight: 6 }} />
      {text}
    </Tag>
  );
}

export const bauakteStatusLabels: Record<BauakteStatus, string> = {
  inquiry: 'Anfrage', planned: 'Geplant', inProgress: 'In Ausführung', drying: 'Belegreif / Trocknung', completed: 'Abgeschlossen', cancelled: 'Storniert',
};
const bauakteColors: Record<BauakteStatus, string> = {
  inquiry: 'default', planned: 'blue', inProgress: 'geekblue', drying: 'orange', completed: 'green', cancelled: 'default',
};

export function BauakteStatusTag({ status }: { status: BauakteStatus }) {
  return <StatusTag color={bauakteColors[status]} text={bauakteStatusLabels[status]} strike={status === 'cancelled'} />;
}

const deviceStatus: Record<DeviceStatus, [string, string]> = {
  pending: ['orange', 'Wartet auf Freigabe'], active: ['green', 'Freigegeben'], locked: ['red', 'Gesperrt'], retired: ['default', 'Entfernt'],
};

export function DeviceStatusTag({ status }: { status: DeviceStatus }) {
  return <StatusTag color={deviceStatus[status][0]} text={deviceStatus[status][1]} />;
}

export const ticketStatusLabels: Record<TicketStatus, string> = {
  new: 'Neu', inProgress: 'In Bearbeitung', waitingForReporter: 'Rückfrage', resolved: 'Gelöst', closed: 'Geschlossen',
};
const ticketColors: Record<TicketStatus, string> = { new: 'blue', inProgress: 'geekblue', waitingForReporter: 'orange', resolved: 'green', closed: 'default' };

export function TicketStatusTag({ status }: { status: TicketStatus }) {
  return <StatusTag color={ticketColors[status]} text={ticketStatusLabels[status]} />;
}

export const ticketCategoryLabels: Record<TicketCategory, string> = {
  signIn: 'Anmeldung', sync: 'Synchronisation', appError: 'App-Fehler', deviceDefect: 'Gerät defekt', officeIt: 'Drucker/Büro-IT',
  access: 'Zugang/Rechte', other: 'Sonstiges',
};

export const ticketPriorityLabels: Record<TicketPriority, string> = { low: 'Niedrig', normal: 'Normal', high: 'Hoch', urgent: 'Dringend' };

export function formatDateTime(value: string | null | undefined): string {
  return value ? new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '–';
}

/** „seit 3 Std.“ – wie lange ein Ticket schon wartet. */
export function waitingSince(value: string, now = Date.now()): string {
  const minutes = Math.max(0, Math.round((now - new Date(value).getTime()) / 60000));
  if (minutes < 60) {
    return `${minutes} Min.`;
  }
  const hours = Math.round(minutes / 60);
  return hours < 48 ? `${hours} Std.` : `${Math.round(hours / 24)} Tage`;
}

export function address(b: { street: string | null; postalCode: string | null; city: string | null }): string {
  const place = [b.postalCode, b.city].filter(Boolean).join(' ');
  return [b.street, place].filter(Boolean).join(', ') || '–';
}
