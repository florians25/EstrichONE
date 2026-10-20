import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { DirectoryUser, NoteDto } from '../../core/api.models';
import { uuidv7 } from '../../core/ids';
import { problemMessage } from '../../core/problem';
import { formatDateTime } from '../../shared/status';

/** Notizen im Verlaufsstil: neueste zuerst, angeheftete oben. */
@Component({
  selector: 'eo-bauakte-notes',
  imports: [FormsModule],
  template: `
    @if (error()) { <div class="message error" role="alert">{{ error() }}</div> }
    @if (canWrite()) {
      <form class="card new" (ngSubmit)="create()">
        <label class="visually-hidden" for="note-text">Neue Notiz</label>
        <textarea id="note-text" name="text" rows="3" placeholder="Notiz schreiben …" [(ngModel)]="text"></textarea>
        <div class="actions"><button class="button primary" type="submit" [disabled]="!text.trim()">Notiz speichern</button></div>
      </form>
    }
    @for (note of notes(); track note.id) {
      <article class="card note">
        <div class="meta">@if (note.isPinned) { <span class="status hint">Angeheftet</span> } {{ name(note.authorUserId) }} · {{ date(note.createdAt) }}</div>
        <p>{{ note.text }}</p>
      </article>
    } @empty {
      <p class="empty">Noch keine Notizen.</p>
    }
  `,
  styles: `
    .new { max-width: 720px; margin-bottom: 16px; }
    .new textarea { width: 100%; font: inherit; padding: 8px 10px; border: 1px solid var(--border-control); border-radius: var(--radius-control); background: var(--surface); color: var(--text-primary); }
    .new .actions { margin-top: 8px; }
    .note { max-width: 720px; margin-bottom: 8px; }
    .note p { white-space: pre-wrap; margin: 6px 0 0; }
    .meta, .empty { color: var(--text-secondary); font-size: 13px; }
  `,
})
export class BauakteNotesComponent {
  private readonly http = inject(HttpClient);

  readonly bauakteId = input.required<string>();
  readonly canWrite = input(false);
  readonly people = input<DirectoryUser[]>([]);
  protected readonly notes = signal<NoteDto[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly date = formatDateTime;
  protected text = '';

  constructor() {
    effect(() => void this.load(this.bauakteId()));
  }

  private async load(id: string): Promise<void> {
    try {
      this.notes.set(await firstValueFrom(this.http.get<NoteDto[]>(`/api/v1/bauakten/${id}/notes`)));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected name(id: string): string {
    return this.people().find((p) => p.id === id)?.displayName ?? '–';
  }

  protected async create(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`/api/v1/bauakten/${this.bauakteId()}/notes`, { id: uuidv7(), text: this.text.trim() }));
      this.text = '';
      await this.load(this.bauakteId());
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
