import { HttpClient } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { FileResponse } from '../../core/api.models';
import { uuidv7 } from '../../core/ids';
import { problemMessage } from '../../core/problem';
import { formatDateTime } from '../../shared/status';

/** Fotos, Videos und Dokumente: ansehen, herunterladen, aus dem Browser hochladen (gleicher Ablauf wie die Apps). */
@Component({
  selector: 'eo-bauakte-files',
  template: `
    @if (error()) { <div class="message error" role="alert">{{ error() }}</div> }
    @if (canUpload()) {
      <div class="card upload" [class.drag]="dragging()" (dragover)="$event.preventDefault(); dragging.set(true)" (dragleave)="dragging.set(false)"
        (drop)="drop($event)">
        <label class="button primary" for="file-input">Dateien hochladen</label>
        <input id="file-input" type="file" multiple class="visually-hidden" (change)="choose($event)" />
        <span>oder hierher ziehen. Fotos, PDF-Dokumente und Videos.</span>
        @if (progress()) { <span class="progress" role="status">{{ progress() }}</span> }
      </div>
    }
    <h3>Fotos ({{ photos().length }})</h3>
    @if (photos().length === 0) { <p class="empty">Noch keine Fotos.</p> }
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
    <h3>Dokumente und Videos ({{ documents().length }})</h3>
    <table class="table">
      <thead><tr><th scope="col">Datei</th><th scope="col" class="numeric">Größe</th><th scope="col">Hochgeladen</th></tr></thead>
      <tbody>
        @for (file of documents(); track file.id) {
          <tr>
            <td>@if (file.state === 'stored') { <a [href]="'/api/v1/files/' + file.id + '/content'" target="_blank" rel="noopener">{{ file.originalName }}</a> }
              @else { {{ file.originalName }} <small>(Upload ausstehend)</small> }</td>
            <td class="numeric">{{ size(file.sizeBytes) }}</td>
            <td class="numeric">{{ date(file.createdAt) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="3">Noch keine Dokumente.</td></tr>
        }
      </tbody>
    </table>
  `,
  styles: `
    .upload { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; margin-bottom: 16px; border-style: dashed; }
    .upload.drag { border-color: var(--action-primary); background: var(--info-bg); }
    .upload label { cursor: pointer; display: inline-flex; align-items: center; }
    .progress { margin-left: auto; color: var(--text-secondary); }
    .empty { color: var(--text-secondary); }
    .photos { list-style: none; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 8px; }
    .photos img, .pending { width: 100%; aspect-ratio: 1; object-fit: cover; border-radius: var(--radius-control); border: 1px solid var(--line); display: grid; place-items: center; background: var(--surface); color: var(--text-secondary); font-size: 13px; }
  `,
})
export class BauakteFilesComponent {
  private readonly http = inject(HttpClient);

  readonly bauakteId = input.required<string>();
  readonly canUpload = input(false);
  protected readonly files = signal<FileResponse[]>([]);
  protected readonly photos = computed(() => this.files().filter((f) => f.kind === 'photo'));
  protected readonly documents = computed(() => this.files().filter((f) => f.kind !== 'photo'));
  protected readonly error = signal<string | null>(null);
  protected readonly progress = signal<string | null>(null);
  protected readonly dragging = signal(false);
  protected readonly date = formatDateTime;

  constructor() {
    effect(() => void this.load(this.bauakteId()));
  }

  private async load(id: string): Promise<void> {
    try {
      this.files.set(await firstValueFrom(this.http.get<FileResponse[]>(`/api/v1/bauakten/${id}/files`)));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected url(photo: FileResponse, variant: 'thumb' | 'display'): string {
    return `/api/v1/files/${photo.id}/content?variant=${photo.variants === 'ready' ? variant : 'original'}`;
  }

  protected size(bytes: number): string {
    return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toLocaleString('de-DE', { maximumFractionDigits: 1 })} MB`;
  }

  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    void this.upload(Array.from(input.files ?? []));
    input.value = '';
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    void this.upload(Array.from(event.dataTransfer?.files ?? []));
  }

  /** Ankündigen (mit SHA-256), dann Inhalt senden – der Server prüft Typ und Prüfsumme. */
  private async upload(files: File[]): Promise<void> {
    this.error.set(null);
    let done = 0;
    for (const file of files) {
      this.progress.set(`Lade ${done + 1} von ${files.length} hoch …`);
      try {
        const bytes = await file.arrayBuffer();
        const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), (b) => b.toString(16).padStart(2, '0')).join('');
        const contentType = file.type || 'application/octet-stream';
        const kind = contentType.startsWith('image/') ? 'photo' : contentType.startsWith('video/') ? 'video' : 'document';
        const id = uuidv7();
        await firstValueFrom(this.http.post('/api/v1/files', {
          id, bauakteId: this.bauakteId(), kind, originalName: file.name, contentType, sizeBytes: file.size, sha256: hash,
          capturedAt: kind === 'photo' ? new Date(file.lastModified).toISOString() : null,
        }));
        await firstValueFrom(this.http.put(`/api/v1/files/${id}/content`, bytes, { headers: { 'Content-Type': 'application/octet-stream' } }));
        done++;
      } catch (error) {
        this.error.set(`${file.name}: ${problemMessage(error)}`);
      }
    }
    this.progress.set(done > 0 ? `${done} ${done === 1 ? 'Datei' : 'Dateien'} hochgeladen.` : null);
    await this.load(this.bauakteId());
  }
}
