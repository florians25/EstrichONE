import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, ElementRef, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BauakteDetail, BauakteListItem, BauaktePage, BauakteStatus } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { uuidv7 } from '../core/ids';
import { problemMessage } from '../core/problem';
import { CustomerPickerComponent } from '../shared/customer-picker.component';
import { IconComponent } from '../shared/icon.component';
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
  imports: [FormsModule, RouterLink, BauakteStatusComponent, CustomerPickerComponent, IconComponent],
  template: `
    <div class="title"><h1>{{ statusFilter() === 'inquiry' ? 'Anfragen' : 'Bauakten' }}</h1></div>
    <div class="toolbar" role="toolbar" aria-label="Bauakten">
      @if (canCreate()) {
        <button class="tool" type="button" (click)="openNew('planned')"><eo-icon name="folder-plus" />Neue Bauakte</button>
        <button class="tool" type="button" (click)="openNew('inquiry')"><eo-icon name="inbox" />Neue Anfrage</button>
        <span class="sep" aria-hidden="true"></span>
      }
      <button class="tool" type="button" [disabled]="!selected()" (click)="openSelected()"><eo-icon name="edit" />Öffnen</button>
      <button class="tool" type="button" (click)="load(true)"><eo-icon name="refresh" />Aktualisieren</button>
      <span class="sep" aria-hidden="true"></span>
      <label class="visually-hidden" for="status-filter">Status</label>
      <select id="status-filter" [value]="statusFilter() ?? ''" (change)="setStatus($any($event.target).value)">
        @for (tab of tabs; track tab.label) { <option [value]="tab.value ?? ''">{{ tab.label }}</option> }
      </select>
      <span class="grow"></span>
      <form class="search" role="search" (ngSubmit)="load(true)">
        <label class="visually-hidden" for="search">Suche</label>
        <input id="search" name="search" type="search" placeholder="Nummer, Bauvorhaben, Adresse" [(ngModel)]="searchText" />
        <button class="tool" type="submit"><eo-icon name="search" />Suchen</button>
      </form>
    </div>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    <table class="table">
      <thead>
        <tr><th scope="col" class="rownum"><span class="visually-hidden">Zeile</span></th><th scope="col">Nummer</th><th scope="col">Bauvorhaben</th>
          <th scope="col">Baustelle</th><th scope="col">Status</th><th scope="col" class="numeric">Geändert</th></tr>
      </thead>
      <tbody>
        @for (item of items(); track item.id; let i = $index) {
          <tr [class.selected]="selected() === item.id" (click)="selected.set(item.id)" (dblclick)="open(item.id)">
            <td class="rownum">{{ i + 1 }}</td>
            <td class="numeric left">{{ item.number ?? 'neu' }}</td>
            <td><a [routerLink]="['/bauakten', item.id]">{{ item.title }}</a></td>
            <td>{{ address(item) }}</td>
            <td><eo-bauakte-status [status]="item.status" /></td>
            <td class="numeric">{{ date(item.updatedAt) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="6" class="empty">{{ loading() ? 'Wird geladen …' : 'Keine Bauakten gefunden.' }}</td></tr>
        }
      </tbody>
    </table>
    <div class="footer-bar">
      <span>{{ items().length }} {{ items().length === 1 ? 'Eintrag' : 'Einträge' }}{{ nextCursor() ? ' (weitere vorhanden)' : '' }}</span>
      @if (nextCursor()) {
        <button class="button small" type="button" (click)="load(false)" [disabled]="loading()">Weitere laden</button>
      }
    </div>

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
    .search { display: flex; gap: 4px; align-items: center; }
    .search input { width: 280px; }
    td.left { text-align: left; }
    tbody tr { cursor: default; }
    .empty { color: var(--text-secondary); text-align: center; padding: 24px; }
    .footer-bar { display: flex; align-items: center; gap: 12px; padding: 6px 2px; font-size: 12px; color: var(--text-secondary); }
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
  /** Aus der Adresse: ?status=inquiry, ?search=…, ?neu=bauakte|anfrage (Kacheln und globale Suche). */
  readonly statusParam = input<string | undefined>(undefined, { alias: 'status' });
  readonly searchParam = input<string | undefined>(undefined, { alias: 'search' });
  readonly neu = input<string | undefined>(undefined);
  protected searchText = '';
  protected readonly statusFilter = signal<BauakteStatus | null>(null);
  protected readonly selected = signal<string | null>(null);
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
    effect(() => {
      const status = this.statusParam() as BauakteStatus | undefined;
      const search = this.searchParam();
      const neu = this.neu();
      untracked(() => {
        this.statusFilter.set(status ?? null);
        this.searchText = search ?? '';
        void this.load(true);
        if (neu === 'bauakte' || neu === 'anfrage') {
          setTimeout(() => this.openNew(neu === 'anfrage' ? 'inquiry' : 'planned'));
        }
      });
    });
  }

  protected setStatus(value: string): void {
    void this.router.navigate(['/bauakten'], { queryParams: { status: value || null, search: this.searchText.trim() || null } });
  }

  protected open(id: string): void {
    void this.router.navigate(['/bauakten', id]);
  }

  protected openSelected(): void {
    const id = this.selected();
    if (id) {
      this.open(id);
    }
  }

  async load(reset: boolean): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    let params = new HttpParams().set('limit', 50);
    if (this.searchText.trim()) {
      params = params.set('search', this.searchText.trim());
    }
    const status = this.statusFilter();
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
