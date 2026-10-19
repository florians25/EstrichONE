import { Component, Inject, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';

interface ChangelogEntry { version: string; date: string; title: string; notes: string; }

@Component({
  standalone: true,
  selector: 'ew-whats-new-dialog',
  imports: [DatePipe, MatButtonModule, MatIconModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title class="whatsnew-dialog-title">
      <span><mat-icon>auto_awesome</mat-icon>Was gibt es Neues</span>
    </h2>
    <mat-dialog-content class="whatsnew-dialog">
      @if (loading()) {
        <div class="whatsnew-loading">Lädt…</div>
      } @else {
        @if (changelog().length) {
          <div class="whatsnew-list">
            @for (entry of changelog(); track $index) {
              <article class="whatsnew-item">
                <div class="whatsnew-marker"><span>{{ entry.version || (entry.date ? (entry.date | date:'dd.MM.') : 'Neu') }}</span></div>
                <div class="whatsnew-body">
                  <header><b>{{ entry.title || 'Update' }}</b>@if (entry.date) { <time>{{ entry.date | date:'dd.MM.yyyy' }}</time> }</header>
                  @if (entry.notes) { <ul>@for (line of noteLines(entry.notes); track line) { <li>{{ line }}</li> }</ul> }
                </div>
              </article>
            }
          </div>
        } @else if (staticText()) {
          <pre class="whatsnew-static-text">{{ staticText() }}</pre>
        } @else {
          <div class="empty-pane"><mat-icon>celebration</mat-icon><span>Noch keine Versionshinweise veröffentlicht.</span></div>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-flat-button color="primary" mat-dialog-close>Schließen</button>
    </mat-dialog-actions>
  `
})
export class WhatsNewDialogComponent {
  private readonly api = inject(ApiService);
  readonly changelog = signal<ChangelogEntry[]>([]);
  readonly staticText = signal('');
  readonly loading = signal(true);

  constructor(@Inject(MAT_DIALOG_DATA) public data: unknown) { this.load(); }

  noteLines(notes: string): string[] { return String(notes).split('\n').map((l) => l.trim()).filter(Boolean); }

  load(): void {
    this.loading.set(true);
    this.api.publicAppInfo().subscribe({
      next: (info) => {
        const c = info?.['changelog'];
        this.changelog.set(Array.isArray(c) ? c : []);
        this.staticText.set(String(info?.['changelog_text'] || '').trim());
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }
}
