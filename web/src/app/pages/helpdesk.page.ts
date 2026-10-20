import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TicketCategory, TicketSummary } from '../core/api.models';
import { problemMessage } from '../core/problem';
import { TicketStatusComponent, ticketCategoryLabels, ticketPriorityLabels, waitingSince } from '../shared/status';

type Filter = 'open' | 'mine' | 'unassigned' | 'all';

/** IT Center › Helpdesk (14, Abschnitt 4): Liste mit Filtern, am längsten wartende zuerst. */
@Component({
  selector: 'eo-helpdesk',
  imports: [FormsModule, RouterLink, TicketStatusComponent],
  template: `
    <h1>Helpdesk</h1>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    <div class="filters" role="group" aria-label="Filter">
      @for (option of filters; track option.value) {
        <button type="button" class="button small" [class.primary]="filter() === option.value" [attr.aria-pressed]="filter() === option.value"
          (click)="filter.set(option.value)">{{ option.label }}</button>
      }
      <label class="category">
        Kategorie
        <select [ngModel]="category()" (ngModelChange)="category.set($event)">
          <option [ngValue]="null">Alle</option>
          @for (c of categoryKeys; track c) {
            <option [ngValue]="c">{{ categories[c] }}</option>
          }
        </select>
      </label>
    </div>
    <table class="table">
      <thead>
        <tr><th scope="col">Nummer</th><th scope="col">Problem</th><th scope="col">Status</th><th scope="col">Priorität</th>
          <th scope="col">Melder</th><th scope="col">Zuständig</th><th scope="col">Wartet seit</th></tr>
      </thead>
      <tbody>
        @for (t of tickets(); track t.id) {
          <tr>
            <td class="numeric"><a [routerLink]="['/it-center/helpdesk', t.id]">{{ t.number }}</a></td>
            <td>
              {{ t.title }}<br /><small>{{ categories[t.category] }}</small>
              @if (t.reporterBlocked) { <span class="status error">Kann nicht arbeiten</span> }
            </td>
            <td><eo-ticket-status [status]="t.status" /></td>
            <td>{{ priorities[t.priority] }}</td>
            <td>{{ t.reporterDisplayName ?? '–' }}</td>
            <td>{{ t.assigneeName ?? 'nicht zugewiesen' }}</td>
            <td class="numeric">{{ since(t.lastActivityAt) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="7">Keine Tickets in dieser Ansicht.</td></tr>
        }
      </tbody>
    </table>
  `,
  styles: `
    .filters { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 16px; }
    .category { margin-left: auto; display: flex; gap: 8px; align-items: center; }
    .status.error { margin-left: 6px; }
  `,
})
export class HelpdeskPage {
  private readonly http = inject(HttpClient);

  protected readonly filters: { value: Filter; label: string }[] = [
    { value: 'open', label: 'Offen' },
    { value: 'mine', label: 'Mir zugewiesen' },
    { value: 'unassigned', label: 'Nicht zugewiesen' },
    { value: 'all', label: 'Alle' },
  ];
  protected readonly categories = ticketCategoryLabels;
  protected readonly categoryKeys = Object.keys(ticketCategoryLabels) as TicketCategory[];
  protected readonly priorities = ticketPriorityLabels;
  protected readonly filter = signal<Filter>('open');
  protected readonly category = signal<TicketCategory | null>(null);
  protected readonly tickets = signal<TicketSummary[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly since = (value: string) => waitingSince(value);

  constructor() {
    effect(() => void this.load(this.filter(), this.category()));
  }

  private async load(filter: Filter, category: TicketCategory | null): Promise<void> {
    try {
      const params: Record<string, string> = { filter };
      if (category) {
        params['category'] = category;
      }
      this.tickets.set(await firstValueFrom(this.http.get<TicketSummary[]>('/api/v1/tickets', { params })));
      this.error.set(null);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
