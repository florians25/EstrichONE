import { Component, computed, input } from '@angular/core';
import { BauakteStatus, DeviceStatus, TicketCategory, TicketPriority, TicketStatus } from '../core/api.models';

const bauakteStatus: Record<BauakteStatus, { label: string; tone: string }> = {
  inquiry: { label: 'Anfrage', tone: 'neutral' },
  planned: { label: 'Geplant', tone: 'info' },
  inProgress: { label: 'In Ausführung', tone: 'navy' },
  drying: { label: 'Belegreif / Trocknung', tone: 'hint' },
  completed: { label: 'Abgeschlossen', tone: 'success' },
  cancelled: { label: 'Storniert', tone: 'neutral cancelled' },
};

/** Status einer Bauakte: Farbe, Punkt und Text (05, Abschnitt 2.3). */
@Component({
  selector: 'eo-bauakte-status',
  template: `<span class="status {{ info().tone }}">{{ info().label }}</span>`,
})
export class BauakteStatusComponent {
  readonly status = input.required<BauakteStatus>();
  protected readonly info = computed(() => bauakteStatus[this.status()]);
}

const deviceStatus: Record<DeviceStatus, { label: string; tone: string }> = {
  pending: { label: 'Wartet auf Freigabe', tone: 'hint' },
  active: { label: 'Freigegeben', tone: 'success' },
  locked: { label: 'Gesperrt', tone: 'error' },
  retired: { label: 'Entfernt', tone: 'neutral' },
};

@Component({
  selector: 'eo-device-status',
  template: `<span class="status {{ info().tone }}">{{ info().label }}</span>`,
})
export class DeviceStatusComponent {
  readonly status = input.required<DeviceStatus>();
  protected readonly info = computed(() => deviceStatus[this.status()]);
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '–';
  }
  return new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
}

export const ticketCategoryLabels: Record<TicketCategory, string> = {
  signIn: 'Anmeldung',
  sync: 'Synchronisation',
  appError: 'App-Fehler',
  deviceDefect: 'Gerät defekt',
  officeIt: 'Drucker/Büro-IT',
  access: 'Zugang/Rechte',
  other: 'Sonstiges',
};

export const ticketPriorityLabels: Record<TicketPriority, string> = { low: 'Niedrig', normal: 'Normal', high: 'Hoch', urgent: 'Dringend' };

const ticketStatus: Record<TicketStatus, { label: string; tone: string }> = {
  new: { label: 'Neu', tone: 'info' },
  inProgress: { label: 'In Bearbeitung', tone: 'navy' },
  waitingForReporter: { label: 'Rückfrage', tone: 'hint' },
  resolved: { label: 'Gelöst', tone: 'success' },
  closed: { label: 'Geschlossen', tone: 'neutral' },
};

export const ticketStatusLabels = Object.fromEntries(Object.entries(ticketStatus).map(([key, value]) => [key, value.label])) as Record<TicketStatus, string>;

@Component({
  selector: 'eo-ticket-status',
  template: `<span class="status {{ info().tone }}">{{ info().label }}</span>`,
})
export class TicketStatusComponent {
  readonly status = input.required<TicketStatus>();
  protected readonly info = computed(() => ticketStatus[this.status()]);
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
