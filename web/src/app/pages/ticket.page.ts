import { HttpClient } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DirectoryUser, TicketCategory, TicketDetail, TicketPriority, TicketStatus } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { problemMessage } from '../core/problem';
import { TicketStatusComponent, formatDateTime, ticketCategoryLabels, ticketPriorityLabels, ticketStatusLabels } from '../shared/status';

/** Ticket bearbeiten: Verlauf (öffentlich/intern), Diagnosedaten, Zuweisen, Status, Lösung (14, Abschnitt 4). */
@Component({
  selector: 'eo-ticket',
  imports: [FormsModule, RouterLink, TicketStatusComponent],
  template: `
    <p><a routerLink="/it-center/helpdesk">← Alle Tickets</a></p>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    @if (ticket(); as t) {
      <div class="head"><span class="number">{{ t.summary.number }}</span><eo-ticket-status [status]="t.summary.status" /></div>
      <h1>{{ t.summary.title }}</h1>
      <div class="columns">
        <section>
          @if (t.description) {
            <p class="description">{{ t.description }}</p>
          }
          <h2>Verlauf</h2>
          <ol class="timeline">
            @for (item of timeline(); track $index) {
              <li [class.internal]="item.internal">
                <div class="meta">{{ item.who }} · {{ date(item.at) }}@if (item.internal) { · <strong>Interne Notiz</strong> }</div>
                <div [class.event]="item.event">{{ item.text }}</div>
              </li>
            }
          </ol>
          <form class="card reply" (ngSubmit)="comment()">
            <label class="field">Antwort oder Notiz<textarea name="text" rows="3" [(ngModel)]="replyText" required></textarea></label>
            <label class="check"><input type="checkbox" name="internal" [(ngModel)]="internal" /> Interne Notiz (für den Melder unsichtbar)</label>
            <div class="actions"><button class="button primary" type="submit" [disabled]="!replyText.trim()">{{ internal ? 'Notiz speichern' : 'Antwort senden' }}</button></div>
          </form>
        </section>
        <aside class="card">
          <h2>Bearbeitung</h2>
          <label class="field">Status
            <select name="status" [(ngModel)]="status">
              @for (s of statusKeys; track s) { <option [value]="s">{{ statuses[s] }}</option> }
            </select>
          </label>
          @if (status === 'resolved' || status === 'closed') {
            <label class="field">Lösung (Pflicht)<textarea name="resolution" rows="3" [(ngModel)]="resolution"></textarea></label>
          }
          <label class="field">Priorität
            <select name="priority" [(ngModel)]="priority">
              @for (p of priorityKeys; track p) { <option [value]="p">{{ priorities[p] }}</option> }
            </select>
          </label>
          <label class="field">Kategorie
            <select name="category" [(ngModel)]="category">
              @for (c of categoryKeys; track c) { <option [value]="c">{{ categories[c] }}</option> }
            </select>
          </label>
          <label class="field">Zuständig
            <select name="assignee" [(ngModel)]="assignee">
              <option value="">Niemand</option>
              @for (u of agents(); track u.id) { <option [value]="u.id">{{ u.displayName }}</option> }
            </select>
          </label>
          <div class="actions">
            @if (assignee !== me()) { <button class="button" type="button" (click)="assignee = me(); save()">Mir zuweisen</button> }
            <button class="button primary" type="button" (click)="save()">Speichern</button>
          </div>
          <dl>
            <dt>Melder</dt><dd>{{ t.summary.reporterDisplayName ?? '–' }}@if (t.summary.reporterBlocked) { <span class="status error">Kann nicht arbeiten</span> }</dd>
            <dt>Gerät</dt><dd>@if (t.deviceId) { <a routerLink="/it-center/geraete">{{ t.deviceName ?? 'Gerät' }}</a> } @else { – }</dd>
            <dt>Bauakte</dt><dd>@if (t.bauakteId) { <a [routerLink]="['/bauakten', t.bauakteId]">öffnen</a> } @else { – }</dd>
            <dt>Gemeldet</dt><dd>{{ date(t.summary.createdAt) }} · {{ t.summary.source }}</dd>
          </dl>
          @if (t.attachmentFileIds.length > 0) {
            <h3>Anhänge</h3>
            <ul class="attachments">
              @for (file of t.attachmentFileIds; track file) {
                <li><a [href]="'/api/v1/files/' + file + '/content'" target="_blank" rel="noopener">
                  <img [src]="'/api/v1/files/' + file + '/content?variant=thumb'" alt="Anhang" loading="lazy" /></a></li>
              }
            </ul>
          }
          @if (t.diagnostics) {
            <details>
              <summary>Diagnosedaten</summary>
              <dl class="diagnostics">
                @for (entry of diagnostics(); track entry.key) { <dt>{{ entry.key }}</dt><dd>{{ entry.value }}</dd> }
              </dl>
            </details>
          }
        </aside>
      </div>
    }
  `,
  styles: `
    .head { display: flex; gap: 12px; align-items: center; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
    .columns { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 24px; align-items: start; }
    @media (max-width: 1000px) { .columns { grid-template-columns: 1fr; } }
    .description { white-space: pre-wrap; }
    .timeline { list-style: none; padding: 0; display: grid; gap: 10px; }
    .timeline li { border-left: 3px solid var(--line); padding: 4px 12px; }
    .timeline li.internal { border-left-color: var(--hint); background: var(--hint-bg); }
    .timeline .meta { color: var(--text-secondary); font-size: 13px; }
    .timeline .event { color: var(--text-secondary); font-style: italic; }
    .reply textarea, aside textarea, aside select { width: 100%; }
    .check { display: flex; gap: 8px; align-items: center; margin: 8px 0; }
    aside dl { display: grid; grid-template-columns: 90px 1fr; gap: 6px 10px; margin-top: 16px; }
    aside dt { color: var(--text-secondary); }
    aside dd { margin: 0; }
    .diagnostics { font-size: 13px; word-break: break-word; }
    .attachments { list-style: none; padding: 0; display: flex; flex-wrap: wrap; gap: 8px; }
    .attachments img { width: 88px; height: 88px; object-fit: cover; border-radius: var(--radius-control); border: 1px solid var(--line); }
  `,
})
export class TicketPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  readonly id = input.required<string>();
  protected readonly ticket = signal<TicketDetail | null>(null);
  protected readonly agents = signal<DirectoryUser[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly date = formatDateTime;
  protected readonly statuses = ticketStatusLabels;
  protected readonly statusKeys = Object.keys(ticketStatusLabels) as TicketStatus[];
  protected readonly priorities = ticketPriorityLabels;
  protected readonly priorityKeys = Object.keys(ticketPriorityLabels) as TicketPriority[];
  protected readonly categories = ticketCategoryLabels;
  protected readonly categoryKeys = Object.keys(ticketCategoryLabels) as TicketCategory[];
  protected readonly me = computed(() => this.auth.user()?.id ?? '');

  protected replyText = '';
  protected internal = false;
  protected status: TicketStatus = 'new';
  protected priority: TicketPriority = 'normal';
  protected category: TicketCategory = 'other';
  protected assignee = '';
  protected resolution = '';

  private static readonly eventTexts: Record<string, string> = {
    created: 'hat das Problem gemeldet', status: 'hat den Status geändert', assignee: 'hat die Zuständigkeit geändert',
    priority: 'hat die Priorität geändert', category: 'hat die Kategorie geändert',
  };

  protected readonly timeline = computed(() => {
    const t = this.ticket();
    if (!t) {
      return [];
    }
    const comments = t.comments.map((c) => ({ at: c.createdAt, who: c.authorName ?? '–', text: c.text, internal: c.isInternal, event: false }));
    const events = t.events.map((e) => ({
      at: e.at,
      who: e.actorName ?? (e.kind === 'created' ? (t.summary.reporterDisplayName ?? 'Gerät') : 'System'),
      text: this.describe(e.kind, e.fromValue, e.toValue),
      internal: false,
      event: true,
    }));
    return [...comments, ...events].sort((a, b) => a.at.localeCompare(b.at));
  });

  protected readonly diagnostics = computed(() =>
    Object.entries(this.ticket()?.diagnostics ?? {}).map(([key, value]) => ({
      key,
      value: Array.isArray(value) ? value.map((v) => (typeof v === 'object' ? JSON.stringify(v) : String(v))).join('\n') : String(value),
    })),
  );

  constructor() {
    effect(() => void this.load(this.id()));
  }

  private describe(kind: string, from: string | null, to: string | null): string {
    const base = TicketPage.eventTexts[kind] ?? kind;
    if (kind === 'status' && to) {
      return `${base}: ${this.statuses[this.camel(to) as TicketStatus] ?? to}`;
    }
    if (kind === 'priority' && to) {
      return `${base}: ${this.priorities[this.camel(to) as TicketPriority] ?? to}`;
    }
    return base;
  }

  /** Ereignisse speichern die C#-Namen (`InProgress`), die API liefert sonst camelCase. */
  private camel(value: string): string {
    return value.charAt(0).toLowerCase() + value.slice(1);
  }

  private async load(id: string): Promise<void> {
    try {
      const [ticket, users] = await Promise.all([
        firstValueFrom(this.http.get<TicketDetail>(`/api/v1/tickets/${id}`)),
        firstValueFrom(this.http.get<DirectoryUser[]>('/api/v1/directory/users')),
      ]);
      this.agents.set(users.filter((u) => u.isActive));
      this.apply(ticket);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private apply(ticket: TicketDetail): void {
    this.ticket.set(ticket);
    this.status = ticket.summary.status;
    this.priority = ticket.summary.priority;
    this.category = ticket.summary.category;
    this.assignee = ticket.summary.assigneeUserId ?? '';
    this.resolution = ticket.resolution ?? '';
    this.error.set(null);
  }

  protected async save(): Promise<void> {
    const t = this.ticket();
    if (!t) {
      return;
    }
    try {
      const body = {
        status: this.status !== t.summary.status ? this.status : null,
        priority: this.priority !== t.summary.priority ? this.priority : null,
        category: this.category !== t.summary.category ? this.category : null,
        assigneeUserId: this.assignee && this.assignee !== t.summary.assigneeUserId ? this.assignee : null,
        unassign: !this.assignee && !!t.summary.assigneeUserId,
        resolution: this.resolution || null,
      };
      this.apply(await firstValueFrom(this.http.patch<TicketDetail>(`/api/v1/tickets/${t.summary.id}`, body)));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async comment(): Promise<void> {
    const t = this.ticket();
    if (!t || !this.replyText.trim()) {
      return;
    }
    try {
      const body = { text: this.replyText, isInternal: this.internal };
      this.apply(await firstValueFrom(this.http.post<TicketDetail>(`/api/v1/tickets/${t.summary.id}/comments`, body)));
      this.replyText = '';
      this.internal = false;
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
