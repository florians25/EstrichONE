import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DiaryEntry, DirectoryUser, Inspection } from '../../core/api.models';
import { problemMessage } from '../../core/problem';
import { formatDateTime } from '../../shared/status';

/** Prüfungen (Erstbegehung, CM-Messung) und Bautagebuch – erfasst in den Apps, im Büro zur Ansicht. */
@Component({
  selector: 'eo-bauakte-records',
  template: `
    @if (error()) { <div class="message error" role="alert">{{ error() }}</div> }
    <h3>Prüfungen ({{ inspections().length }})</h3>
    @if (inspections().length === 0) {
      <p class="empty">Noch keine Erstbegehung oder CM-Messung.</p>
    } @else {
      <table class="table">
        <thead><tr><th scope="col">Art</th><th scope="col">Ergebnis</th><th scope="col">Bemerkung</th><th scope="col">Durch</th><th scope="col">Zeitpunkt</th></tr></thead>
        <tbody>
          @for (i of inspections(); track i.id) {
            <tr>
              <td>{{ i.kind === 'cmMeasurement' ? 'CM-Messung' : 'Erstbegehung' }}@if (i.correctsInspectionId) { <span class="muted"> (Korrektur)</span> }</td>
              <td>
                @if (i.kind === 'cmMeasurement') {
                  {{ i.location }}: {{ number(i.measuredCmPercent) }} CM-% (Grenzwert {{ number(i.limitCmPercent) }})
                  <span class="status" [class.success]="i.isReadyForCovering" [class.hint]="!i.isReadyForCovering">{{ i.isReadyForCovering ? 'Belegreif' : 'Noch nicht belegreif' }}</span>
                } @else if (i.notRequired) {
                  Nicht notwendig
                } @else {
                  <ul class="checks">
                    @for (check of checkList(i); track check.key) {
                      <li><span class="status" [class.success]="check.result === 'ok'" [class.hint]="check.result === 'notOk'" [class.neutral]="check.result === 'notApplicable'">{{ resultText[check.result] }}</span> {{ check.title }}</li>
                    }
                  </ul>
                }
              </td>
              <td>{{ i.remarks ?? '–' }}</td>
              <td>{{ name(i.performedByUserId) }}</td>
              <td class="numeric">{{ date(i.performedAt) }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
    <h3>Bautagebuch ({{ diary().length }})</h3>
    @if (diary().length === 0) { <p class="empty">Noch keine Einträge.</p> }
    @for (entry of diary(); track entry.id) {
      <article class="card diary">
        <header><strong>{{ day(entry.day) }}</strong><span class="muted">{{ weatherLine(entry) }}</span></header>
        <p>{{ entry.text }}</p>
        <footer class="muted">{{ name(entry.authorUserId) }}@if (entry.editedAt) { · bearbeitet }</footer>
      </article>
    }
  `,
  styles: `
    .muted, .empty { color: var(--text-secondary); }
    .checks { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .diary { max-width: 720px; margin-bottom: 8px; }
    .diary header { display: flex; justify-content: space-between; gap: 12px; }
    .diary p { white-space: pre-wrap; margin: 8px 0; }
  `,
})
export class BauakteRecordsComponent {
  private readonly http = inject(HttpClient);

  readonly bauakteId = input.required<string>();
  readonly people = input<DirectoryUser[]>([]);
  protected readonly diary = signal<DiaryEntry[]>([]);
  protected readonly inspections = signal<Inspection[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly date = formatDateTime;
  protected readonly resultText: Record<string, string> = { ok: 'In Ordnung', notOk: 'Mangel', notApplicable: 'Entfällt' };
  private static readonly checkTitles: Record<string, string> = {
    substrate: 'Untergrund tragfähig, eben und sauber',
    edgeInsulation: 'Randdämmstreifen vorhanden',
    heatingPressureTest: 'Fußbodenheizung abgedrückt',
    access: 'Zufahrt und Pumpenstandort',
    utilities: 'Wasser und Strom vorhanden',
  };
  private static readonly weatherNames: Record<string, string> = {
    sunny: 'sonnig', cloudy: 'bewölkt', rain: 'Regen', snow: 'Schnee', frost: 'Frost', wind: 'Wind',
  };

  constructor() {
    effect(() => void this.load(this.bauakteId()));
  }

  private async load(id: string): Promise<void> {
    try {
      const [diary, inspections] = await Promise.all([
        firstValueFrom(this.http.get<DiaryEntry[]>(`/api/v1/bauakten/${id}/diary`)),
        firstValueFrom(this.http.get<Inspection[]>(`/api/v1/bauakten/${id}/inspections`)),
      ]);
      this.diary.set(diary);
      this.inspections.set(inspections);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected name(id: string): string {
    return this.people().find((p) => p.id === id)?.displayName ?? '–';
  }

  protected number(value: number | null): string {
    return value === null ? '–' : value.toLocaleString('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 2 });
  }

  protected day(value: string): string {
    const [year, month, dayOfMonth] = value.split('-').map(Number);
    return new Date(year, month - 1, dayOfMonth).toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  protected weatherLine(entry: DiaryEntry): string {
    return [
      entry.weather ? BauakteRecordsComponent.weatherNames[entry.weather] : null,
      entry.temperatureCelsius !== null ? `${entry.temperatureCelsius} °C` : null,
      entry.workersPresent !== null ? `${entry.workersPresent} anwesend` : null,
    ].filter(Boolean).join(' · ');
  }

  protected checkList(inspection: Inspection): { key: string; title: string; result: string }[] {
    return Object.keys(BauakteRecordsComponent.checkTitles)
      .filter((key) => inspection.checks[key])
      .map((key) => ({ key, title: BauakteRecordsComponent.checkTitles[key], result: inspection.checks[key] }));
  }
}
