import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BackupRunInfo, BackupStatusResponse } from '../core/api.models';
import { problemMessage } from '../core/problem';
import { formatDateTime } from '../shared/status';

/** Backup Center (docs/reengineering/16): Zustand der Sicherungen, Sicherung jetzt anstoßen. */
@Component({
  selector: 'eo-backup',
  template: `
    <h1>Sicherung</h1>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    @if (status(); as s) {
      <div class="message" [class.success]="s.healthy" [class.error]="!s.healthy" role="status">{{ s.summary }}</div>
      <table class="table">
        <tbody>
          @for (row of rows(s); track row.label) {
            <tr>
              <th scope="row">{{ row.label }}</th>
              <td>
                @if (row.info; as info) {
                  <span class="status" [class.success]="info.status === 'succeeded'" [class.error]="info.status !== 'succeeded'">
                    {{ info.status === 'succeeded' ? 'Erfolgreich' : 'Fehlgeschlagen' }}
                  </span>
                  {{ date(info.at) }} – {{ info.message }}
                } @else {
                  <span class="status neutral">Noch nie</span>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
      <p>
        <button class="button primary" type="button" (click)="runNow()" [disabled]="s.runRequested || !s.configured">
          {{ s.runRequested ? 'Sicherung angefordert …' : 'Jetzt sichern' }}
        </button>
      </p>
    }
  `,
  styles: `th { width: 220px; text-align: left; font-weight: 500; color: var(--text-secondary); padding: 8px 12px; }`,
})
export class BackupPage {
  private readonly http = inject(HttpClient);
  protected readonly status = signal<BackupStatusResponse | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly date = formatDateTime;

  constructor() {
    void this.reload();
  }

  protected rows(s: BackupStatusResponse): { label: string; info: BackupRunInfo | null }[] {
    return [
      { label: 'Letzte Sicherung', info: s.lastRun },
      { label: 'Letzte erfolgreiche', info: s.lastSuccess },
      { label: 'Integritätsprüfung', info: s.lastCheck },
      { label: 'Wiederherstellungstest', info: s.lastRestoreTest },
    ];
  }

  protected async runNow(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/backup/run', null));
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private async reload(): Promise<void> {
    try {
      this.status.set(await firstValueFrom(this.http.get<BackupStatusResponse>('/api/v1/backup/status')));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
