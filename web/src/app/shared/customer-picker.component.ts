import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, input, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ContactResponse } from '../core/api.models';
import { uuidv7 } from '../core/ids';
import { problemMessage } from '../core/problem';

/** Kunde suchen oder neu anlegen (Name genügt). Liefert die ID des gewählten Kunden. */
@Component({
  selector: 'eo-customer-picker',
  imports: [FormsModule],
  template: `
    <div class="field">
      <label [for]="inputId()">Kunde</label>
      @if (selected(); as c) {
        <div class="selected">
          <span>{{ c.name }}@if (c.customerNumber) { <small> · {{ c.customerNumber }}</small> }@if (c.city) { <small> · {{ c.city }}</small> }</span>
          <button class="button small" type="button" (click)="clear()">Ändern</button>
        </div>
      } @else {
        <input [id]="inputId()" type="search" autocomplete="off" placeholder="Name, Kundennummer oder Ort"
          [ngModel]="query()" [ngModelOptions]="{ standalone: true }" (ngModelChange)="search($event)" />
        @if (query().trim().length > 1) {
          <ul class="results" role="listbox" aria-label="Kunden">
            @for (c of results(); track c.id) {
              <li role="option" [attr.aria-selected]="false"><button type="button" (click)="choose(c)">{{ c.name }}
                @if (c.city) { <small>· {{ c.city }}</small> }</button></li>
            }
            <li><button type="button" class="new" (click)="create()">„{{ query().trim() }}“ als neuen Kunden anlegen</button></li>
          </ul>
        }
        @if (error()) { <span class="help error">{{ error() }}</span> }
      }
    </div>
  `,
  styles: `
    .selected { display: flex; align-items: center; justify-content: space-between; gap: 8px; min-height: 36px; }
    .results { list-style: none; margin: 4px 0 0; padding: 4px; border: 1px solid var(--line); border-radius: var(--radius-control); background: var(--surface-raised); max-height: 220px; overflow: auto; }
    .results button { width: 100%; text-align: left; background: none; border: 0; padding: 6px 8px; font: inherit; color: inherit; cursor: pointer; border-radius: 4px; }
    .results button:hover, .results button:focus-visible { background: var(--surface-alt); }
    .results .new { color: var(--action-primary); }
    .error { color: var(--error); }
  `,
})
export class CustomerPickerComponent {
  private readonly http = inject(HttpClient);

  readonly inputId = input('customer');
  readonly customerId = model<string | null>(null);
  protected readonly selected = signal<ContactResponse | null>(null);
  protected readonly query = signal('');
  protected readonly results = signal<ContactResponse[]>([]);
  protected readonly error = signal<string | null>(null);
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const id = this.customerId();
      if (id && this.selected()?.id !== id) {
        void firstValueFrom(this.http.get<ContactResponse>(`/api/v1/contacts/${id}`)).then((c) => this.selected.set(c), () => undefined);
      }
      if (!id) {
        this.selected.set(null);
      }
    });
  }

  protected search(value: string): void {
    this.query.set(value);
    clearTimeout(this.timer);
    this.timer = setTimeout(async () => {
      if (value.trim().length < 2) {
        this.results.set([]);
        return;
      }
      try {
        this.results.set(await firstValueFrom(this.http.get<ContactResponse[]>('/api/v1/contacts', { params: { search: value.trim(), limit: 8 } })));
      } catch (error) {
        this.error.set(problemMessage(error));
      }
    }, 200);
  }

  protected choose(contact: ContactResponse): void {
    this.selected.set(contact);
    this.customerId.set(contact.id);
  }

  protected async create(): Promise<void> {
    try {
      const contact = await firstValueFrom(
        this.http.post<ContactResponse>('/api/v1/contacts', { id: uuidv7(), kind: 'person', name: this.query().trim() }),
      );
      this.choose(contact);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected clear(): void {
    this.selected.set(null);
    this.customerId.set(null);
    this.query.set('');
  }
}
