import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BauakteDetail, BauakteListItem, BauaktePage, BauakteStatus } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { uuidv7 } from '../core/ids';
import { problemMessage } from '../core/problem';
import { CustomerPickerComponent } from '../shared/customer-picker.component';
import { BauakteStatusComponent, formatDateTime } from '../shared/status';

interface NewForm {
  title: string;
  street: string;
  postalCode: string;
  city: string;
  description: string;
  customerId: string | null;
}

/** Bauakten im Büro (AP-18): suchen, nach Status filtern, anlegen – auch als Anfrage. */
@Component({
  selector: 'eo-bauakten',
  imports: [FormsModule, RouterLink, BauakteStatusComponent, CustomerPickerComponent],
  template: `
    <div class="title">
      <h1>Bauakten</h1>
      @if (canCreate()) {
        <div class="actions">
          <button class="button" type="button" (click)="openNew('inquiry')">Neue Anfrage</button>
          <button class="button primary" type="button" (click)="openNew('planned')">Neue Bauakte</button>
        </div>
      }
    </div>
    <div class="tabs" role="tablist" aria-label="Status">
      @for (tab of tabs; track tab.label) {
        <button type="button" role="tab" class="tab" [class.active]="status() === tab.value" [attr.aria-selected]="status() === tab.value"
          (click)="status.set(tab.value); load(true)">{{ tab.label }}</button>
      }
    </div>
    <form class="search" role="search" (ngSubmit)="load(true)">
      <label class="visually-hidden" for="search">Suche</label>
      <input id="search" name="search" type="search" placeholder="Nummer, Bauvorhaben, Adresse" [(ngModel)]="search" />
      <button class="button" type="submit">Suchen</button>
    </form>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    <table class="table">
      <thead>
        <tr><th scope="col">Nummer</th><th scope="col">Bauvorhaben</th><th scope="col">Baustelle</th><th scope="col">Status</th><th scope="col">Geändert</th></tr>
      </thead>
      <tbody>
        @for (item of items(); track item.id) {
          <tr>
            <td class="numeric">{{ item.number ?? 'neu' }}</td>
            <td><a [routerLink]="['/bauakten', item.id]">{{ item.title }}</a></td>
            <td>{{ address(item) }}</td>
            <td><eo-bauakte-status [status]="item.status" /></td>
            <td class="numeric">{{ date(item.updatedAt) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="5">{{ loading() ? 'Wird geladen …' : 'Keine Bauakten gefunden.' }}</td></tr>
        }
      </tbody>
    </table>
    @if (nextCursor()) {
      <p><button class="button" type="button" (click)="load(false)" [disabled]="loading()">Weitere laden</button></p>
    }

    <dialog #dialog aria-labelledby="new-title">
      <h2 id="new-title">{{ newStatus === 'inquiry' ? 'Neue Anfrage' : 'Neue Bauakte' }}</h2>
      <form (ngSubmit)="create()">
        <div class="field"><label for="n-title">Bauvorhaben</label>
          <input id="n-title" name="title" required placeholder="z. B. EFH Müller, Neubau" [(ngModel)]="form.title" /></div>
        <eo-customer-picker inputId="n-customer" [(customerId)]="form.customerId" />
        <div class="field"><label for="n-street">Straße</label><input id="n-street" name="street" [(ngModel)]="form.street" /></div>
        <div class="row">
          <div class="field"><label for="n-zip">PLZ</label><input id="n-zip" name="postalCode" [(ngModel)]="form.postalCode" /></div>
          <div class="field grow"><label for="n-city">Ort</label><input id="n-city" name="city" [(ngModel)]="form.city" /></div>
        </div>
        <div class="field"><label for="n-desc">Beschreibung</label><textarea id="n-desc" name="description" rows="3" [(ngModel)]="form.description"></textarea></div>
        @if (dialogError()) { <div class="message error" role="alert">{{ dialogError() }}</div> }
        <div class="actions">
          <button class="button" type="button" (click)="dialog.close()">Abbrechen</button>
          <button class="button primary" type="submit" [disabled]="!form.title.trim()">Anlegen</button>
        </div>
      </form>
    </dialog>
  `,
  styles: `
    .search { display: flex; gap: 8px; margin-bottom: 16px; max-width: 560px; }
    .search input { flex: 1; font: inherit; min-height: 36px; padding: 6px 10px; border: 1px solid var(--border-control); border-radius: var(--radius-control); background: var(--surface); color: var(--text-primary); }
    .tabs { display: flex; gap: 4px; flex-wrap: wrap; margin-bottom: 12px; border-bottom: 1px solid var(--line); }
    .tab { background: none; border: 0; border-bottom: 2px solid transparent; padding: 8px 12px; font: inherit; color: var(--text-secondary); cursor: pointer; }
    .tab.active { color: var(--text-primary); border-bottom-color: var(--action-primary); font-weight: 600; }
    .row { display: flex; gap: 12px; }
    .row .field:first-child { width: 110px; }
    .grow { flex: 1; }
  `,
})
export class BauaktenPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly tabs: { label: string; value: BauakteStatus | null }[] = [
    { label: 'Alle', value: null },
    { label: 'Anfragen', value: 'inquiry' },
    { label: 'Geplant', value: 'planned' },
    { label: 'In Ausführung', value: 'inProgress' },
    { label: 'Trocknung', value: 'drying' },
    { label: 'Abgeschlossen', value: 'completed' },
  ];
  protected readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected search = '';
  protected readonly status = signal<BauakteStatus | null>(null);
  protected readonly items = signal<BauakteListItem[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly date = formatDateTime;
  protected form: NewForm = { title: '', street: '', postalCode: '', city: '', description: '', customerId: null };
  protected newStatus: 'inquiry' | 'planned' = 'planned';
  protected readonly canCreate = () => this.auth.permissions().has('bauakten.create');

  constructor() {
    void this.load(true);
  }

  async load(reset: boolean): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    let params = new HttpParams().set('limit', 50);
    if (this.search.trim()) {
      params = params.set('search', this.search.trim());
    }
    const status = this.status();
    if (status) {
      params = params.set('status', status);
    }
    const cursor = this.nextCursor();
    if (!reset && cursor) {
      params = params.set('cursor', cursor);
    }
    try {
      const page = await firstValueFrom(this.http.get<BauaktePage>('/api/v1/bauakten', { params }));
      this.items.set(reset ? page.items : [...this.items(), ...page.items]);
      this.nextCursor.set(page.nextCursor);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected openNew(status: 'inquiry' | 'planned'): void {
    this.newStatus = status;
    this.form = { title: '', street: '', postalCode: '', city: '', description: '', customerId: null };
    this.dialogError.set(null);
    this.dialog().nativeElement.showModal();
  }

  protected async create(): Promise<void> {
    const f = this.form;
    try {
      const created = await firstValueFrom(this.http.post<BauakteDetail>('/api/v1/bauakten', {
        id: uuidv7(), title: f.title.trim(), street: f.street || null, postalCode: f.postalCode || null, city: f.city || null,
        description: f.description || null, customerId: f.customerId, status: this.newStatus,
      }));
      this.dialog().nativeElement.close();
      await this.router.navigate(['/bauakten', created.id]);
    } catch (error) {
      this.dialogError.set(problemMessage(error));
    }
  }

  protected address(item: BauakteListItem): string {
    const place = [item.postalCode, item.city].filter(Boolean).join(' ');
    return [item.street, place].filter(Boolean).join(', ') || '–';
  }
}
