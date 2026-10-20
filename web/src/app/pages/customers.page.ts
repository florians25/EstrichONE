import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ContactKind, ContactResponse } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { uuidv7 } from '../core/ids';
import { problemMessage } from '../core/problem';

interface ContactForm {
  id: string | null;
  version: number;
  kind: ContactKind;
  name: string;
  customerNumber: string;
  street: string;
  postalCode: string;
  city: string;
  phone: string;
  email: string;
  notes: string;
}

/** Kunden und Projektbeteiligte: suchen, anlegen, bearbeiten (Import über die API, AP-06). */
@Component({
  selector: 'eo-customers',
  imports: [FormsModule],
  template: `
    <div class="title">
      <h1>Kunden</h1>
      @if (canEdit()) { <button class="button primary" type="button" (click)="open(null)">Kunde anlegen</button> }
    </div>
    <form class="search" role="search" (ngSubmit)="load()">
      <label class="visually-hidden" for="customer-search">Suche</label>
      <input id="customer-search" name="search" type="search" placeholder="Name, Kundennummer oder Ort" [(ngModel)]="search" />
      <button class="button" type="submit">Suchen</button>
    </form>
    @if (error()) { <div class="message error" role="alert">{{ error() }}</div> }
    <table class="table">
      <thead><tr><th scope="col">Name</th><th scope="col">Kundennummer</th><th scope="col">Ort</th><th scope="col">Telefon</th>
        <th scope="col">E-Mail</th><th scope="col"><span class="visually-hidden">Aktionen</span></th></tr></thead>
      <tbody>
        @for (c of contacts(); track c.id) {
          <tr>
            <td>{{ c.name }}<br /><small>{{ c.kind === 'company' ? 'Firma' : 'Privat' }}</small></td>
            <td class="numeric">{{ c.customerNumber ?? '–' }}</td>
            <td>{{ place(c) }}</td>
            <td>@if (c.phone) { <a [href]="'tel:' + c.phone">{{ c.phone }}</a> } @else { – }</td>
            <td>@if (c.email) { <a [href]="'mailto:' + c.email">{{ c.email }}</a> } @else { – }</td>
            <td class="actions">@if (canEdit()) { <button class="button small" type="button" (click)="open(c)">Bearbeiten</button> }</td>
          </tr>
        } @empty {
          <tr><td colspan="6">Keine Kunden gefunden.</td></tr>
        }
      </tbody>
    </table>

    <dialog #dialog aria-labelledby="customer-title">
      <h2 id="customer-title">{{ form.id ? 'Kunde bearbeiten' : 'Kunde anlegen' }}</h2>
      <form (ngSubmit)="save()">
        <fieldset class="field">
          <legend>Art</legend>
          <label><input type="radio" name="kind" value="person" [(ngModel)]="form.kind" /> Privat</label>
          <label><input type="radio" name="kind" value="company" [(ngModel)]="form.kind" /> Firma</label>
        </fieldset>
        <div class="field"><label for="c-name">Name</label><input id="c-name" name="name" required [(ngModel)]="form.name" /></div>
        <div class="field"><label for="c-number">Kundennummer</label><input id="c-number" name="customerNumber" [(ngModel)]="form.customerNumber" /></div>
        <div class="field"><label for="c-street">Straße</label><input id="c-street" name="street" [(ngModel)]="form.street" /></div>
        <div class="row">
          <div class="field"><label for="c-zip">PLZ</label><input id="c-zip" name="postalCode" [(ngModel)]="form.postalCode" /></div>
          <div class="field grow"><label for="c-city">Ort</label><input id="c-city" name="city" [(ngModel)]="form.city" /></div>
        </div>
        <div class="field"><label for="c-phone">Telefon</label><input id="c-phone" name="phone" type="tel" [(ngModel)]="form.phone" /></div>
        <div class="field"><label for="c-mail">E-Mail</label><input id="c-mail" name="email" type="email" [(ngModel)]="form.email" /></div>
        <div class="field"><label for="c-notes">Notizen</label><textarea id="c-notes" name="notes" rows="3" [(ngModel)]="form.notes"></textarea></div>
        @if (dialogError()) { <div class="message error" role="alert">{{ dialogError() }}</div> }
        <div class="actions">
          <button class="button" type="button" (click)="dialog.close()">Abbrechen</button>
          <button class="button primary" type="submit" [disabled]="!form.name.trim()">Speichern</button>
        </div>
      </form>
    </dialog>
  `,
  styles: `
    .search { display: flex; gap: 8px; margin-bottom: 16px; max-width: 560px; }
    .search input { flex: 1; font: inherit; min-height: 36px; padding: 6px 10px; border: 1px solid var(--border-control); border-radius: var(--radius-control); background: var(--surface); color: var(--text-primary); }
    .row { display: flex; gap: 12px; }
    .row .field:first-child { width: 110px; }
    .grow { flex: 1; }
    fieldset.field { border: 0; padding: 0; flex-direction: row; gap: 16px; }
  `,
})
export class CustomersPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  protected readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected search = '';
  protected readonly contacts = signal<ContactResponse[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly dialogError = signal<string | null>(null);
  protected form: ContactForm = CustomersPage.empty();
  protected readonly canEdit = () => this.auth.permissions().has('contacts.edit');

  constructor() {
    void this.load();
  }

  private static empty(): ContactForm {
    return { id: null, version: 0, kind: 'person', name: '', customerNumber: '', street: '', postalCode: '', city: '', phone: '', email: '', notes: '' };
  }

  async load(): Promise<void> {
    try {
      const params: Record<string, string | number> = { limit: 100 };
      if (this.search.trim()) {
        params['search'] = this.search.trim();
      }
      this.contacts.set(await firstValueFrom(this.http.get<ContactResponse[]>('/api/v1/contacts', { params })));
      this.error.set(null);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected place(c: ContactResponse): string {
    return [c.postalCode, c.city].filter(Boolean).join(' ') || '–';
  }

  protected open(contact: ContactResponse | null): void {
    this.dialogError.set(null);
    this.form = contact
      ? {
          id: contact.id, version: contact.version, kind: contact.kind, name: contact.name, customerNumber: contact.customerNumber ?? '',
          street: contact.street ?? '', postalCode: contact.postalCode ?? '', city: contact.city ?? '', phone: contact.phone ?? '',
          email: contact.email ?? '', notes: contact.notes ?? '',
        }
      : CustomersPage.empty();
    this.dialog().nativeElement.showModal();
  }

  protected async save(): Promise<void> {
    const f = this.form;
    const body = {
      kind: f.kind, name: f.name.trim(), customerNumber: f.customerNumber || null, street: f.street || null, postalCode: f.postalCode || null,
      city: f.city || null, phone: f.phone || null, email: f.email || null, notes: f.notes || null,
    };
    try {
      if (f.id) {
        await firstValueFrom(this.http.put(`/api/v1/contacts/${f.id}`, { ...body, version: f.version }));
      } else {
        await firstValueFrom(this.http.post('/api/v1/contacts', { ...body, id: uuidv7() }));
      }
      this.dialog().nativeElement.close();
      await this.load();
    } catch (error) {
      this.dialogError.set(problemMessage(error));
    }
  }
}
