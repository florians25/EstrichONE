import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Component, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BauakteDetail, BauakteHistoryEntry, BauakteStatus, ContactResponse, DirectoryUser } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { problemCode, problemMessage } from '../core/problem';
import { CustomerPickerComponent } from '../shared/customer-picker.component';
import { IconComponent } from '../shared/icon.component';
import { BauakteStatusComponent, formatDateTime } from '../shared/status';
import { BauakteFilesComponent } from './bauakte/files.component';
import { BauakteNotesComponent } from './bauakte/notes.component';
import { BauakteRecordsComponent } from './bauakte/records.component';
import { BauakteRoomsComponent } from './bauakte/rooms.component';
import { BauakteWorkComponent } from './bauakte/work.component';

type Tab = 'overview' | 'rooms' | 'work' | 'notes' | 'files' | 'records' | 'history';

interface EditForm {
  title: string;
  status: BauakteStatus;
  street: string;
  postalCode: string;
  city: string;
  description: string;
  customerId: string | null;
  responsibleUserId: string | null;
}

/** Bauakte im Büro (AP-18): Übersicht und Bearbeitung, Räume, Aufgaben, Notizen, Dateien, Prüfungen, Verlauf. */
@Component({
  selector: 'eo-bauakte-detail',
  imports: [
    FormsModule, RouterLink, IconComponent, BauakteStatusComponent, CustomerPickerComponent, BauakteRoomsComponent, BauakteWorkComponent,
    BauakteNotesComponent, BauakteFilesComponent, BauakteRecordsComponent,
  ],
  template: `
    <nav class="crumbs" aria-label="Pfad"><a routerLink="/bauakten">Bauakten</a> › <span>{{ bauakte()?.number ?? 'Bauakte' }}</span></nav>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    @if (bauakte(); as b) {
      <div class="toolbar" role="toolbar" aria-label="Bauakte">
        @if (canEdit()) {
          <button class="tool" type="button" (click)="openEdit()"><eo-icon name="edit" />Bearbeiten</button>
          @if (b.status === 'inquiry') {
            <button class="tool primary" type="button" (click)="convertInquiry()"><eo-icon name="check" />Als Auftrag übernehmen</button>
          }
          <span class="sep" aria-hidden="true"></span>
        }
        <button class="tool" type="button" (click)="load(b.id)"><eo-icon name="refresh" />Aktualisieren</button>
        @if (mapsUrl(b)) { <a class="tool" [href]="mapsUrl(b)" target="_blank" rel="noopener"><eo-icon name="pin" />Karte</a> }
        @if (customer()?.phone) { <a class="tool" [href]="'tel:' + customer()!.phone"><eo-icon name="phone" />Kunde anrufen</a> }
      </div>
      <section class="pane head-pane" aria-labelledby="bauakte-title">
        <div class="pane-title head-title">
          <span class="number">{{ b.number ?? 'noch ohne Nummer' }}</span>
          <h1 id="bauakte-title">{{ b.title }}</h1>
          <eo-bauakte-status [status]="b.status" />
        </div>
        <div class="facts">
          <dl>
            <dt>Kunde</dt><dd>{{ customer()?.name ?? '–' }}</dd>
            <dt>Baustelle</dt><dd>{{ address(b) }}</dd>
            <dt>Zuständig</dt><dd>{{ name(b.responsibleUserId) }}</dd>
          </dl>
          <dl>
            <dt>Telefon</dt><dd>{{ customer()?.phone ?? '–' }}</dd>
            <dt>Angelegt</dt><dd>{{ date(b.createdAt) }} · {{ name(b.createdByUserId) }}</dd>
            <dt>Geändert</dt><dd>{{ date(b.updatedAt) }}</dd>
          </dl>
        </div>
      </section>
      <div class="tabs" role="tablist" aria-label="Bereiche der Bauakte">
        @for (t of tabs; track t.id) {
          <button type="button" role="tab" class="tab" [class.active]="tab() === t.id" [attr.aria-selected]="tab() === t.id" (click)="tab.set(t.id)">{{ t.label }}</button>
        }
      </div>
      @switch (tab()) {
        @case ('overview') {
          <div class="pane">
            <div class="pane-title">Beschreibung</div>
            <p class="pre">{{ b.description ?? 'Keine Beschreibung.' }}</p>
          </div>
        }
        @case ('rooms') { <eo-bauakte-rooms [bauakteId]="b.id" [canEdit]="canEdit()" /> }
        @case ('work') { <eo-bauakte-work [bauakteId]="b.id" [canEdit]="has('work.edit')" [people]="people()" /> }
        @case ('notes') { <eo-bauakte-notes [bauakteId]="b.id" [canWrite]="has('notes.write')" [people]="people()" /> }
        @case ('files') { <eo-bauakte-files [bauakteId]="b.id" [canUpload]="has('files.upload')" /> }
        @case ('records') { <eo-bauakte-records [bauakteId]="b.id" [people]="people()" /> }
        @case ('history') {
          <ol class="history">
            @for (entry of history(); track $index) {
              <li><span class="muted">{{ date(entry.at) }}</span> {{ entry.text }}</li>
            } @empty {
              <li class="muted">Noch keine Einträge.</li>
            }
          </ol>
        }
      }

      <dialog #dialog aria-labelledby="edit-title">
        <h2 id="edit-title">Bauakte bearbeiten</h2>
        <form (ngSubmit)="save()">
          <div class="field"><label for="e-title">Bauvorhaben</label><input id="e-title" name="title" required [(ngModel)]="form.title" /></div>
          <div class="field"><label for="e-status">Status</label>
            <select id="e-status" name="status" [(ngModel)]="form.status">
              @for (s of statuses; track s.value) { <option [value]="s.value">{{ s.label }}</option> }
            </select></div>
          <eo-customer-picker inputId="e-customer" [(customerId)]="form.customerId" />
          <div class="field"><label for="e-street">Straße</label><input id="e-street" name="street" [(ngModel)]="form.street" /></div>
          <div class="row">
            <div class="field"><label for="e-zip">PLZ</label><input id="e-zip" name="postalCode" [(ngModel)]="form.postalCode" /></div>
            <div class="field grow"><label for="e-city">Ort</label><input id="e-city" name="city" [(ngModel)]="form.city" /></div>
          </div>
          <div class="field"><label for="e-resp">Zuständig</label>
            <select id="e-resp" name="responsible" [(ngModel)]="form.responsibleUserId">
              <option [ngValue]="null">niemand</option>
              @for (p of activePeople(); track p.id) { <option [ngValue]="p.id">{{ p.displayName }}</option> }
            </select></div>
          <div class="field"><label for="e-desc">Beschreibung</label><textarea id="e-desc" name="description" rows="4" [(ngModel)]="form.description"></textarea></div>
          @if (dialogError()) { <div class="message error" role="alert">{{ dialogError() }}</div> }
          <div class="actions">
            <button class="button" type="button" (click)="dialog.close()">Abbrechen</button>
            <button class="button primary" type="submit" [disabled]="!form.title.trim()">Speichern</button>
          </div>
        </form>
      </dialog>
    }
  `,
  styles: `
    .crumbs { font-size: 12px; color: var(--text-secondary); margin-bottom: 8px; }
    .head-pane { margin-bottom: 12px; }
    .head-title { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .head-title h1 { margin: 0; font-size: 17px; }
    .number { font-variant-numeric: tabular-nums; font-weight: 600; color: var(--text-secondary); }
    .facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 0 32px; }
    .facts dl { display: grid; grid-template-columns: 110px 1fr; gap: 4px 12px; margin: 0; }
    dt { color: var(--text-secondary); }
    dd { margin: 0; }
    .pre { white-space: pre-wrap; }
    .muted { color: var(--text-secondary); }
    .history { padding-left: 20px; display: grid; gap: 6px; max-width: 720px; }
    .row { display: flex; gap: 12px; }
    .row .field:first-child { width: 110px; }
    .grow { flex: 1; }
  `,
})
export class BauakteDetailPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  readonly id = input.required<string>();
  protected readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'overview', label: 'Übersicht' },
    { id: 'rooms', label: 'Räume und Flächen' },
    { id: 'work', label: 'Aufgaben und Mängel' },
    { id: 'notes', label: 'Notizen' },
    { id: 'files', label: 'Fotos und Dokumente' },
    { id: 'records', label: 'Prüfungen und Bautagebuch' },
    { id: 'history', label: 'Verlauf' },
  ];
  protected readonly statuses: { value: BauakteStatus; label: string }[] = [
    { value: 'inquiry', label: 'Anfrage' },
    { value: 'planned', label: 'Geplant' },
    { value: 'inProgress', label: 'In Ausführung' },
    { value: 'drying', label: 'Belegreif / Trocknung' },
    { value: 'completed', label: 'Abgeschlossen' },
    { value: 'cancelled', label: 'Storniert' },
  ];
  protected readonly tab = signal<Tab>('overview');
  protected readonly bauakte = signal<BauakteDetail | null>(null);
  protected readonly customer = signal<ContactResponse | null>(null);
  protected readonly people = signal<DirectoryUser[]>([]);
  protected readonly activePeople = computed(() => this.people().filter((p) => p.isActive));
  protected readonly history = signal<BauakteHistoryEntry[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly date = formatDateTime;
  protected form: EditForm = { title: '', status: 'planned', street: '', postalCode: '', city: '', description: '', customerId: null, responsibleUserId: null };

  constructor() {
    effect(() => void this.load(this.id()));
    effect(() => {
      if (this.tab() === 'history') {
        void this.loadHistory(this.id());
      }
    });
  }

  protected has(permission: string): boolean {
    return this.auth.permissions().has(permission);
  }

  protected canEdit(): boolean {
    return this.has('bauakten.edit');
  }

  protected async load(id: string): Promise<void> {
    try {
      const [bauakte, people] = await Promise.all([
        firstValueFrom(this.http.get<BauakteDetail>(`/api/v1/bauakten/${id}`)),
        firstValueFrom(this.http.get<DirectoryUser[]>('/api/v1/directory/users')),
      ]);
      this.people.set(people);
      this.apply(bauakte);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private apply(bauakte: BauakteDetail): void {
    this.bauakte.set(bauakte);
    this.error.set(null);
    this.customer.set(null);
    if (bauakte.customerId && this.has('contacts.read')) {
      void firstValueFrom(this.http.get<ContactResponse>(`/api/v1/contacts/${bauakte.customerId}`)).then((c) => this.customer.set(c), () => undefined);
    }
  }

  private async loadHistory(id: string): Promise<void> {
    try {
      this.history.set(await firstValueFrom(this.http.get<BauakteHistoryEntry[]>(`/api/v1/bauakten/${id}/history`)));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected name(id: string | null): string {
    return (id && this.people().find((p) => p.id === id)?.displayName) || '–';
  }

  protected address(b: BauakteDetail): string {
    const place = [b.postalCode, b.city].filter(Boolean).join(' ');
    return [b.street, place].filter(Boolean).join(', ') || '–';
  }

  protected mapsUrl(b: BauakteDetail): string | null {
    if (b.latitude !== null && b.longitude !== null) {
      return `https://www.openstreetmap.org/?mlat=${b.latitude}&mlon=${b.longitude}#map=18/${b.latitude}/${b.longitude}`;
    }
    const address = this.address(b);
    return address === '–' ? null : `https://www.openstreetmap.org/search?query=${encodeURIComponent(address)}`;
  }

  protected openEdit(): void {
    const b = this.bauakte();
    if (!b) {
      return;
    }
    this.form = {
      title: b.title, status: b.status, street: b.street ?? '', postalCode: b.postalCode ?? '', city: b.city ?? '', description: b.description ?? '',
      customerId: b.customerId, responsibleUserId: b.responsibleUserId,
    };
    this.dialogError.set(null);
    this.dialog()?.nativeElement.showModal();
  }

  protected async convertInquiry(): Promise<void> {
    const b = this.bauakte();
    if (b) {
      this.form = {
        title: b.title, status: 'planned', street: b.street ?? '', postalCode: b.postalCode ?? '', city: b.city ?? '', description: b.description ?? '',
        customerId: b.customerId, responsibleUserId: b.responsibleUserId,
      };
      await this.save();
    }
  }

  /** Speichern mit der gelesenen Version (If-Match); bei zwischenzeitlicher Änderung klarer Hinweis statt Überschreiben. */
  protected async save(): Promise<void> {
    const b = this.bauakte();
    if (!b) {
      return;
    }
    const f = this.form;
    try {
      const updated = await firstValueFrom(this.http.put<BauakteDetail>(`/api/v1/bauakten/${b.id}`, {
        title: f.title.trim(), status: f.status, street: f.street || null, postalCode: f.postalCode || null, city: f.city || null,
        latitude: b.latitude, longitude: b.longitude, description: f.description || null, customerId: f.customerId, responsibleUserId: f.responsibleUserId,
      }, { headers: new HttpHeaders({ 'If-Match': `"${b.version}"` }) }));
      this.apply(updated);
      this.dialog()?.nativeElement.close();
    } catch (error) {
      const message = problemCode(error) === 'VERSION_CONFLICT'
        ? 'Die Bauakte wurde inzwischen von jemand anderem geändert. Bitte die Seite neu laden und die Änderung erneut vornehmen.'
        : problemMessage(error);
      if (this.dialog()?.nativeElement.open) {
        this.dialogError.set(message);
      } else {
        this.error.set(message);
      }
    }
  }
}
