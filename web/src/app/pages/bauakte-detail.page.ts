import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BauakteDetail, FileResponse } from '../core/api.models';
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
    }
  `,
  styles: `
    .head { display: flex; gap: 12px; align-items: center; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
    .facts { display: grid; grid-template-columns: 160px 1fr; gap: 8px 16px; max-width: 720px; }
    dt { color: var(--text-secondary); }
    dd { margin: 0; }
    .photos { list-style: none; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 8px; }
    .photos img, .pending { width: 100%; aspect-ratio: 1; object-fit: cover; border-radius: var(--radius-control); border: 1px solid var(--line); display: grid; place-items: center; background: var(--surface); color: var(--text-secondary); font-size: 13px; }
  `,
})
export class BauakteDetailPage {
  private readonly http = inject(HttpClient);

  readonly id = input.required<string>();
  protected readonly bauakte = signal<BauakteDetail | null>(null);
  protected readonly photos = signal<FileResponse[]>([]);
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
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected url(photo: FileResponse, variant: 'thumb' | 'display'): string {
    return `/api/v1/files/${photo.id}/content?variant=${photo.variants === 'ready' ? variant : 'original'}`;
  }

  protected address(b: BauakteDetail): string {
    const place = [b.postalCode, b.city].filter(Boolean).join(' ');
    return [b.street, place].filter(Boolean).join(', ') || '–';
  }
}
