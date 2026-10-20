import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { DirectoryUser, WorkItemDto, WorkItemKind } from '../../core/api.models';
import { uuidv7 } from '../../core/ids';
import { problemMessage } from '../../core/problem';

/** Aufgaben und Mängel einer Bauakte: anlegen, zuweisen, abhaken. */
@Component({
  selector: 'eo-bauakte-work',
  imports: [FormsModule],
  template: `
    @if (error()) { <div class="message error" role="alert">{{ error() }}</div> }
    @if (canEdit()) {
      <form class="card new" (ngSubmit)="create()">
        <select name="kind" aria-label="Art" [(ngModel)]="kind"><option value="task">Aufgabe</option><option value="defect">Mangel</option></select>
        <input name="title" aria-label="Titel" required [placeholder]="kind === 'defect' ? 'Was ist mangelhaft?' : 'Was ist zu tun?'" [(ngModel)]="title" />
        <input name="due" type="date" aria-label="Fällig am" [(ngModel)]="due" />
        <select name="assignee" aria-label="Zuständig" [(ngModel)]="assignee">
          <option [ngValue]="null">Niemand</option>
          @for (u of people(); track u.id) { <option [ngValue]="u.id">{{ u.displayName }}</option> }
        </select>
        <button class="button primary" type="submit" [disabled]="!title.trim()">Hinzufügen</button>
      </form>
    }
    <table class="table">
      <thead><tr><th scope="col"><span class="visually-hidden">Erledigt</span></th><th scope="col">Aufgabe</th><th scope="col">Art</th>
        <th scope="col">Fällig</th><th scope="col">Zuständig</th></tr></thead>
      <tbody>
        @for (item of items(); track item.id) {
          <tr [class.done]="item.status === 'done'">
            <td><input type="checkbox" [checked]="item.status === 'done'" [disabled]="!canEdit()" (change)="toggle(item)"
              [attr.aria-label]="(item.status === 'done' ? 'Wieder öffnen: ' : 'Erledigt: ') + item.title" /></td>
            <td>{{ item.title }}@if (item.description) { <br /><small>{{ item.description }}</small> }</td>
            <td>@if (item.kind === 'defect') { <span class="status hint">Mangel</span> } @else { Aufgabe }</td>
            <td class="numeric" [class.overdue]="overdue(item)">{{ day(item.dueDate) }}</td>
            <td>{{ name(item.assigneeUserId) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="5">Keine Aufgaben oder Mängel.</td></tr>
        }
      </tbody>
    </table>
  `,
  styles: `
    .new { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-bottom: 16px; }
    .new input[name='title'] { flex: 1; min-width: 220px; }
    .new input, .new select { font: inherit; min-height: 36px; padding: 6px 10px; border: 1px solid var(--border-control); border-radius: var(--radius-control); background: var(--surface); color: var(--text-primary); }
    tr.done td:nth-child(2) { text-decoration: line-through; color: var(--text-secondary); }
    .overdue { color: var(--error); font-weight: 600; }
  `,
})
export class BauakteWorkComponent {
  private readonly http = inject(HttpClient);

  readonly bauakteId = input.required<string>();
  readonly canEdit = input(false);
  readonly people = input<DirectoryUser[]>([]);
  protected readonly items = signal<WorkItemDto[]>([]);
  protected readonly error = signal<string | null>(null);
  protected kind: WorkItemKind = 'task';
  protected title = '';
  protected due = '';
  protected assignee: string | null = null;

  constructor() {
    effect(() => void this.load(this.bauakteId()));
  }

  private async load(id: string): Promise<void> {
    try {
      this.items.set(await firstValueFrom(this.http.get<WorkItemDto[]>(`/api/v1/bauakten/${id}/work-items`)));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected name(id: string | null): string {
    return (id && this.people().find((p) => p.id === id)?.displayName) || '–';
  }

  protected day(value: string | null): string {
    if (!value) {
      return '–';
    }
    const [y, m, d] = value.split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  protected overdue(item: WorkItemDto): boolean {
    const today = new Date().toISOString().slice(0, 10);
    return item.status !== 'done' && !!item.dueDate && item.dueDate < today;
  }

  protected async create(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/work-items', {
        id: uuidv7(), bauakteId: this.bauakteId(), kind: this.kind, title: this.title.trim(), dueDate: this.due || null, assigneeUserId: this.assignee,
      }));
      this.title = '';
      this.due = '';
      await this.load(this.bauakteId());
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async toggle(item: WorkItemDto): Promise<void> {
    try {
      await firstValueFrom(this.http.patch(`/api/v1/work-items/${item.id}`, { status: item.status === 'done' ? 'open' : 'done' }));
      await this.load(this.bauakteId());
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
