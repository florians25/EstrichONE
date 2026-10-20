import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BauakteDetail, BauakteHistoryEntry, DiaryEntry, DirectoryUser, FileResponse, Inspection } from '../core/api.models';
import { problemMessage } from '../core/problem';
import { BauakteStatusComponent, formatDateTime } from '../shared/status';

@Component({
  selector: 'eo-bauakte-detail',
  imports: [RouterLink, BauakteStatusComponent],
  template: `
    <p><a routerLink="/bauakten">← Alle Bauakten</a></p>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    @if (bauakte(); as b) {
      <div class="head">
        <span class="number">{{ b.number ?? 'noch ohne Nummer' }}</span>
        <eo-bauakte-status [status]="b.status" />
      </div>
      <h1>{{ b.title }}</h1>
      <dl class="card facts">
        <dt>Baustelle</dt><dd>{{ address(b) }}</dd>
        <dt>Beschreibung</dt><dd>{{ b.description ?? '–' }}</dd>
        <dt>Angelegt</dt><dd>{{ date(b.createdAt) }}</dd>
        <dt>Geändert</dt><dd>{{ date(b.updatedAt) }}</dd>
      </dl>
      <h2>Prüfungen ({{ inspections().length }})</h2>
      @if (inspections().length === 0) {
        <p class="empty">Noch keine Erstbegehung oder CM-Messung.</p>
      } @else {
        <table class="card">
          <thead><tr><th>Art</th><th>Ergebnis</th><th>Bemerkung</th><th>Durch</th><th>Zeitpunkt</th></tr></thead>
          <tbody>
            @for (i of inspections(); track i.id) {
              <tr>
                <td>{{ i.kind === 'cmMeasurement' ? 'CM-Messung' : 'Erstbegehung' }}@if (i.correctsInspectionId) { <span class="muted"> (Korrektur)</span> }</td>
                <td>
                  @if (i.kind === 'cmMeasurement') {
                    {{ i.location }}: {{ number(i.measuredCmPercent) }} CM-% (Grenzwert {{ number(i.limitCmPercent) }})
                    <span class="chip" [class.ok]="i.isReadyForCovering" [class.wait]="!i.isReadyForCovering">
                      <span class="dot" aria-hidden="true"></span>{{ i.isReadyForCovering ? 'Belegreif' : 'Noch nicht belegreif' }}
                    </span>
                  } @else if (i.notRequired) {
                    Nicht notwendig
                  } @else {
                    <ul class="checks">
                      @for (check of checkList(i); track check.key) {
                        <li><span class="chip" [class.ok]="check.result === 'ok'" [class.wait]="check.result === 'notOk'"><span class="dot" aria-hidden="true"></span>{{ resultText[check.result] }}</span> {{ check.title }}</li>
                      }
                    </ul>
                  }
                </td>
                <td>{{ i.remarks ?? '–' }}</td>
                <td>{{ name(i.performedByUserId) }}</td>
                <td>{{ date(i.performedAt) }}</td>
              </tr>
            }
          </tbody>
        </table>
      }

      <h2>Bautagebuch ({{ diary().length }})</h2>
      @if (diary().length === 0) {
        <p class="empty">Noch keine Einträge.</p>
      }
      @for (entry of diary(); track entry.id) {
        <article class="card diary">
          <header>
            <strong>{{ day(entry.day) }}</strong>
            <span class="muted">{{ weatherLine(entry) }}</span>
          </header>
          <p>{{ entry.text }}</p>
          <footer class="muted">{{ name(entry.authorUserId) }}@if (entry.editedAt) { · bearbeitet }</footer>
        </article>
      }

      <h2>Fotos ({{ photos().length }})</h2>
      @if (photos().length === 0) {
        <p>Noch keine Fotos.</p>
      }
      <ul class="photos">
        @for (photo of photos(); track photo.id) {
          <li>
            @if (photo.state === 'stored') {
              <a [href]="url(photo, 'display')" target="_blank" rel="noopener">
                <img [src]="url(photo, 'thumb')" [alt]="'Foto vom ' + date(photo.capturedAt ?? photo.createdAt)" loading="lazy" />
              </a>
            } @else {
              <span class="pending">Upload ausstehend</span>
            }
          </li>
        }
      </ul>

      <h2>Verlauf</h2>
      <ol class="history">
        @for (entry of history(); track $index) {
          <li><span class="muted">{{ date(entry.at) }}</span> {{ entry.text }}</li>
        }
      </ol>
    }
  `,
  styles: `
    .head { display: flex; gap: 12px; align-items: center; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
    .facts { display: grid; grid-template-columns: 160px 1fr; gap: 8px 16px; max-width: 720px; }
    dt { color: var(--text-secondary); }
    dd { margin: 0; }
    .photos { list-style: none; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 8px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: 8px; border-bottom: 1px solid var(--line); vertical-align: top; }
    th { color: var(--text-secondary); font-weight: 600; font-size: 13px; }
    .muted, .empty { color: var(--text-secondary); }
    .chip { display: inline-flex; align-items: center; gap: 6px; padding: 2px 8px; border-radius: 999px; font-size: 13px; font-weight: 600; background: var(--neutral-bg, var(--surface)); }
    .chip .dot { width: 8px; height: 8px; border-radius: 50%; background: currentColor; }
    .chip.ok { color: var(--success); background: var(--success-bg); }
    .chip.wait { color: var(--hint); background: var(--hint-bg); }
    .checks { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .diary { max-width: 720px; margin-bottom: 8px; }
    .diary header { display: flex; justify-content: space-between; gap: 12px; }
    .diary p { white-space: pre-wrap; margin: 8px 0; }
    .history { padding-left: 20px; display: grid; gap: 6px; max-width: 720px; }
    .photos img, .pending { width: 100%; aspect-ratio: 1; object-fit: cover; border-radius: var(--radius-control); border: 1px solid var(--line); display: grid; place-items: center; background: var(--surface); color: var(--text-secondary); font-size: 13px; }
  `,
})
export class BauakteDetailPage {
  private readonly http = inject(HttpClient);

  readonly id = input.required<string>();
  protected readonly bauakte = signal<BauakteDetail | null>(null);
  protected readonly photos = signal<FileResponse[]>([]);
  protected readonly diary = signal<DiaryEntry[]>([]);
  protected readonly inspections = signal<Inspection[]>([]);
  protected readonly history = signal<BauakteHistoryEntry[]>([]);
  private readonly names = signal<Map<string, string>>(new Map());
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
  protected readonly error = signal<string | null>(null);
  protected readonly date = formatDateTime;

  constructor() {
    effect(() => void this.load(this.id()));
  }

  private async load(id: string): Promise<void> {
    try {
      this.bauakte.set(await firstValueFrom(this.http.get<BauakteDetail>(`/api/v1/bauakten/${id}`)));
      const files = await firstValueFrom(this.http.get<FileResponse[]>(`/api/v1/bauakten/${id}/files`));
      this.photos.set(files.filter((file) => file.kind === 'photo'));
      const [diary, inspections, history, users] = await Promise.all([
        firstValueFrom(this.http.get<DiaryEntry[]>(`/api/v1/bauakten/${id}/diary`)),
        firstValueFrom(this.http.get<Inspection[]>(`/api/v1/bauakten/${id}/inspections`)),
        firstValueFrom(this.http.get<BauakteHistoryEntry[]>(`/api/v1/bauakten/${id}/history`)),
        firstValueFrom(this.http.get<DirectoryUser[]>('/api/v1/directory/users')),
      ]);
      this.diary.set(diary);
      this.inspections.set(inspections);
      this.history.set(history);
      this.names.set(new Map(users.map((user) => [user.id, user.displayName])));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected url(photo: FileResponse, variant: 'thumb' | 'display'): string {
    return `/api/v1/files/${photo.id}/content?variant=${photo.variants === 'ready' ? variant : 'original'}`;
  }

  protected name(userId: string): string {
    return this.names().get(userId) ?? '–';
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
      entry.weather ? BauakteDetailPage.weatherNames[entry.weather] : null,
      entry.temperatureCelsius !== null ? `${entry.temperatureCelsius} °C` : null,
      entry.workersPresent !== null ? `${entry.workersPresent} anwesend` : null,
    ].filter(Boolean).join(' · ');
  }

  protected checkList(inspection: Inspection): { key: string; title: string; result: string }[] {
    return Object.keys(BauakteDetailPage.checkTitles)
      .filter((key) => inspection.checks[key])
      .map((key) => ({ key, title: BauakteDetailPage.checkTitles[key], result: inspection.checks[key] }));
  }

  protected address(b: BauakteDetail): string {
    const place = [b.postalCode, b.city].filter(Boolean).join(' ');
    return [b.street, place].filter(Boolean).join(', ') || '–';
  }
}
