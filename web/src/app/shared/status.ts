import { Component, computed, input } from '@angular/core';
import { BauakteStatus, DeviceStatus } from '../core/api.models';

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
