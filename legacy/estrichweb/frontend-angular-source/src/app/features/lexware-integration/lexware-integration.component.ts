import { Component, computed, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { DatePipe, NgClass } from '@angular/common';
import { forkJoin } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe, NgClass, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, MatTableModule, MatSnackBarModule],
  template: `
    <section class="page-head compact-head office-head">
      <div>
        <p class="eyebrow">Büro · Schnittstelle</p>
        <h1><mat-icon>sync_alt</mat-icon>Lexware Office</h1>
      </div>
      <div class="head-actions">
        <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon>Aktualisieren</button>
        @if (canWrite) { <button mat-flat-button color="primary" (click)="saveConfig()"><mat-icon>save</mat-icon>Speichern</button> }
      </div>
    </section>

    <!-- Statusband: die vier Angaben, die man zuerst wissen will -->
    <section class="lx-status" [class.is-on]="!!config()?.['is_active']">
      <div class="lx-status-main">
        <span class="lx-dot" [class.on]="!!config()?.['is_active']"></span>
        <div>
          <b>{{ config()?.['is_active'] ? 'Schnittstelle aktiv' : 'Schnittstelle inaktiv' }}</b>
          <small>{{ config()?.['has_api_key'] ? 'API-Schlüssel hinterlegt' : 'Kein API-Schlüssel hinterlegt' }}</small>
        </div>
      </div>
      <dl class="lx-status-facts">
        <div><dt>Letzter Test</dt><dd>{{ config()?.['last_test_at'] || '—' }}</dd></div>
        <div><dt>Letzter Abgleich</dt><dd>{{ config()?.['last_auto_sync_at'] || '—' }}</dd></div>
        <div><dt>Intervall</dt><dd>{{ config()?.['auto_sync_interval_minutes'] || 4 }} Min.</dd></div>
      </dl>
      @if (config()?.['last_error']) {
        <p class="lx-error"><mat-icon>error</mat-icon><span>{{ config()?.['last_error'] }}</span></p>
      }
    </section>

    <section class="lexware-layout">
      <mat-card class="admin-panel-card">
        <h2>Verbindung &amp; Abgleich</h2>
        <p class="muted">Der Schlüssel wird ausschließlich serverseitig gespeichert – hier ist nur sichtbar, ob einer hinterlegt ist.</p>
        <form [formGroup]="form" class="lx-form">
          <div class="lx-fieldrow">
            <mat-form-field appearance="outline"><mat-label>API-Basis-URL</mat-label><input matInput formControlName="api_base_url"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Neuer API-Schlüssel</mat-label><input matInput type="password" formControlName="api_key" autocomplete="off" placeholder="nur bei Änderung ausfüllen"></mat-form-field>
          </div>

          <h3 class="lx-sub">Automatischer Abgleich</h3>
          <div class="lx-toggles">
            <label class="lx-toggle"><mat-slide-toggle formControlName="is_active"></mat-slide-toggle><span><b>Schnittstelle aktiv</b><small>Ohne Haken findet kein Abgleich statt.</small></span></label>
            <label class="lx-toggle"><mat-slide-toggle formControlName="auto_sync_customers"></mat-slide-toggle><span><b>Kunden abgleichen</b><small>Kundendaten aus Lexware übernehmen.</small></span></label>
            <label class="lx-toggle"><mat-slide-toggle formControlName="auto_sync_projects"></mat-slide-toggle><span><b>Aufträge verknüpfen</b><small>Lexware-Referenz an Bauakten binden.</small></span></label>
          </div>
          <div class="lx-interval">
            <mat-form-field appearance="outline"><mat-label>Intervall in Minuten</mat-label><input matInput type="number" min="1" formControlName="auto_sync_interval_minutes"></mat-form-field>
            <span class="lx-interval-hint">Wie oft im Hintergrund abgeglichen wird.</span>
          </div>

          <h3 class="lx-sub">Was übernommen wird</h3>
          <div class="lx-toggles">
            <label class="lx-toggle"><mat-slide-toggle formControlName="sync_contacts"></mat-slide-toggle><span><b>Kontakte vollständig</b><small>Auch Ansprechpartner und Anschriften.</small></span></label>
            <label class="lx-toggle"><mat-slide-toggle formControlName="sync_quotations"></mat-slide-toggle><span><b>Angebote</b><small>Zum Verknüpfen mit Bauakten.</small></span></label>
            <label class="lx-toggle"><mat-slide-toggle formControlName="sync_invoices"></mat-slide-toggle><span><b>Rechnungen</b><small>Rechnungsstand je Bauakte.</small></span></label>
            <label class="lx-toggle"><mat-slide-toggle formControlName="sync_vouchers"></mat-slide-toggle><span><b>Belege</b><small>Sonstige Belege aus Lexware.</small></span></label>
          </div>
        </form>

        <h3 class="lx-sub">Aktionen</h3>
        <div class="lx-actions">
          <button mat-stroked-button (click)="testConnection()"><mat-icon>wifi_tethering</mat-icon>Verbindung testen</button>
          <button mat-stroked-button (click)="loadRemoteContacts()"><mat-icon>contacts</mat-icon>Kontakte prüfen</button>
          <button mat-stroked-button (click)="syncCustomers()"><mat-icon>group_add</mat-icon>Kunden importieren</button>
          <button mat-stroked-button (click)="syncProjects()"><mat-icon>link</mat-icon>Aufträge verknüpfen</button>
          <button mat-stroked-button (click)="syncDocuments()"><mat-icon>receipt_long</mat-icon>Belege abrufen</button>
          @if (canWrite) { <button mat-flat-button color="primary" (click)="runAutoSync(true)"><mat-icon>sync</mat-icon>Jetzt abgleichen</button> }
        </div>
      </mat-card>

      <mat-card class="admin-panel-card">
        <h2>Verknüpfung zur Baustellenakte</h2>
        <p class="muted">Aufträge bleiben führend in EstrichWeb. Lexware-IDs werden als externe Referenzen gespeichert.</p>
        <div class="link-grid">
          <mat-form-field appearance="outline"><mat-label>Kunde</mat-label><mat-select [value]="selectedCustomer()" (valueChange)="selectedCustomer.set($event)">
            @for (customer of customers(); track customer['id']) { <mat-option [value]="customer['id']">{{ customer['customer_no'] || '—' }} · {{ customer['name'] }}</mat-option> }
          </mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Auftrag</mat-label><mat-select [value]="selectedProject()" (valueChange)="selectedProject.set($event)">
            @for (project of activeProjects(); track project['id']) { <mat-option [value]="project['id']">{{ project['project_no'] || '—' }} · {{ project['name'] }}</mat-option> }
          </mat-select></mat-form-field>
        </div>
        <div class="button-row">@if (canWrite) { <button mat-flat-button color="primary" (click)="linkSelected()">Lokale Verknüpfung speichern</button> }</div>
        <p class="storage-hint">Beim Kunden-Sync werden Kundendaten nach customers geschrieben. Beim Auftrags-Sync wird eine Lexware-Referenz an projects gebunden, damit Angebot/Rechnung später direkt aus der Auftragsakte erzeugt werden kann.</p>
      </mat-card>

      <mat-card class="admin-panel-card lexware-offer-card">
        <h2>Lexware-Angebot mit Auftrag verknüpfen</h2>
        <div class="link-grid">
          <mat-form-field appearance="outline"><mat-label>Auftrag</mat-label><mat-select [value]="selectedProject()" (valueChange)="selectedProject.set($event)">
            @for (project of activeProjects(); track project['id']) { <mat-option [value]="project['id']">{{ project['project_no'] || '—' }} · {{ project['name'] }}</mat-option> }
          </mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Lexware-Angebot</mat-label><mat-select [value]="selectedQuotation()" (valueChange)="selectedQuotation.set($event)">
            @for (quotation of quotations(); track quotation['id']) { <mat-option [value]="quotation['id']">{{ quotation['lexware_number'] || quotation['lexware_id'] }} · {{ quotation['title'] || 'Angebot' }} @if (quotation['local_entity_id']) { · verknüpft }</mat-option> }
          </mat-select></mat-form-field>
        </div>
        <div class="button-row">@if (canWrite) { <button mat-flat-button color="primary" [disabled]="!selectedProject() || !selectedQuotation()" (click)="linkQuotation()"><mat-icon>link</mat-icon>Angebot verknüpfen</button> }<button mat-stroked-button (click)="syncDocuments()"><mat-icon>sync</mat-icon>Angebote abrufen</button></div>
      </mat-card>
    </section>

    @if (remoteContacts().length) {
      <section class="result-section-title"><h2>Lexware Kontakte</h2><span>{{ remoteContacts().length }} geladen</span></section>
      <mat-card class="table-card module-list-card"><div class="responsive-table-wrap"><table class="data-table plain-detail-table"><thead><tr><th>Name</th><th>E-Mail</th><th>Lexware-ID</th></tr></thead><tbody>@for (contact of remoteContacts(); track contact['id']) { <tr><td>{{ contactName(contact) }}</td><td>{{ contactEmail(contact) }}</td><td>{{ contact['id'] }}</td></tr> }</tbody></table></div></mat-card>
    }


    @if (syncedRecords().length) {
      <section class="result-section-title"><h2>Synchronisierte Lexware-Daten</h2><span>{{ syncedRecords().length }} Datensätze</span></section>
      <mat-card class="table-card module-list-card"><div class="responsive-table-wrap"><table class="data-table plain-detail-table"><thead><tr><th>Typ</th><th>Nummer</th><th>Titel</th><th>Status</th><th>Betrag</th><th>Sync</th></tr></thead><tbody>@for (row of syncedRecords(); track row['id']) { <tr><td>{{ row['entity_type'] }}</td><td>{{ row['lexware_number'] || row['lexware_id'] }}</td><td>{{ row['title'] || '—' }}</td><td>{{ row['status'] || '—' }}</td><td>{{ row['amount_total'] || '—' }} {{ row['currency'] || '' }}</td><td>{{ row['last_synced_at'] | date:'dd.MM.yyyy HH:mm' }}</td></tr> }</tbody></table></div></mat-card>
    }

    <section class="result-section-title"><h2>Letzte Sync-Vorgänge</h2><span>{{ logs().length }} Einträge</span></section>
    <mat-card class="table-card module-list-card">
      <div class="responsive-table-wrap">
        <table mat-table [dataSource]="logs()" class="data-table">
          <ng-container matColumnDef="entity_type"><th mat-header-cell *matHeaderCellDef>Objekt</th><td mat-cell *matCellDef="let row">{{ row['entity_type'] }}</td></ng-container>
          <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let row"><span class="status-pill" [ngClass]="'status-' + (row['status'] || 'open')">{{ row['status'] || '—' }}</span></td></ng-container>
          <ng-container matColumnDef="message"><th mat-header-cell *matHeaderCellDef>Meldung</th><td mat-cell *matCellDef="let row">{{ row['message'] || row['error_message'] || '—' }}</td></ng-container>
          <ng-container matColumnDef="created_at"><th mat-header-cell *matHeaderCellDef>Zeit</th><td mat-cell *matCellDef="let row">{{ row['created_at'] | date:'dd.MM.yyyy HH:mm' }}</td></ng-container>
          <tr mat-header-row *matHeaderRowDef="logColumns"></tr><tr mat-row *matRowDef="let row; columns: logColumns"></tr>
        </table>
      </div>
      @if (!logs().length) { <p class="empty"><mat-icon>sync_disabled</mat-icon>Noch keine Lexware-Syncs gespeichert.</p> }
    </mat-card>
  `
})
export class LexwareIntegrationComponent {

  private readonly auth = inject(AuthService);
  readonly canWrite = this.auth.canWrite('integrations');  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  private readonly snack = inject(MatSnackBar);
  readonly config = signal<GenericRecord | null>(null);
  readonly logs = signal<GenericRecord[]>([]);
  readonly customers = signal<GenericRecord[]>([]);
  readonly projects = signal<GenericRecord[]>([]);
  readonly remoteContacts = signal<GenericRecord[]>([]);
  readonly syncedRecords = signal<GenericRecord[]>([]);
  readonly quotations = signal<GenericRecord[]>([]);
  readonly selectedCustomer = signal('');
  readonly selectedProject = signal('');
  readonly selectedQuotation = signal('');
  readonly logColumns = ['entity_type', 'status', 'message', 'created_at'];
  readonly form = this.fb.nonNullable.group({ api_base_url: ['https://api.lexware.io'], api_key: [''], is_active: [false], auto_sync_customers: [true], auto_sync_projects: [true], auto_sync_interval_minutes: [4], sync_contacts: [true], sync_vouchers: [true], sync_invoices: [true], sync_quotations: [true] });
  readonly activeProjects = computed(() => this.projects().filter((p) => !['completed', 'closed', 'archived'].includes(String(p['status'] || '').toLowerCase())));

  constructor() { this.load(); }

  load(): void {
    forkJoin({
      status: this.api.lexwareStatus(),
      logs: this.api.list('lexware_sync_logs', '', 100),
      customers: this.api.list('customers', '', 500),
      projects: this.api.list('projects', '', 500),
      records: this.api.lexwareSyncedRecords(),
      quotations: this.api.lexwareQuotations()
    }).subscribe({
      next: ({ status, logs, customers, projects, records, quotations }) => {
        this.config.set(status);
        this.form.patchValue({ api_base_url: status['api_base_url'] || 'https://api.lexware.io', api_key: '', is_active: !!status['is_active'], auto_sync_customers: status['auto_sync_customers'] !== 0, auto_sync_projects: status['auto_sync_projects'] !== 0, auto_sync_interval_minutes: Number(status['auto_sync_interval_minutes'] || 4), sync_contacts: status['sync_contacts'] !== 0, sync_vouchers: status['sync_vouchers'] !== 0, sync_invoices: status['sync_invoices'] !== 0, sync_quotations: status['sync_quotations'] !== 0 });
        this.logs.set(logs.items); this.customers.set(customers.items); this.projects.set(projects.items); this.syncedRecords.set(records.items || []); this.quotations.set(quotations.items || []);
        this.selectedCustomer.set(customers.items[0]?.['id'] || ''); this.selectedProject.set(this.activeProjects()[0]?.['id'] || ''); this.selectedQuotation.set((quotations.items || [])[0]?.['id'] || '');
      },
      error: () => this.snack.open('Lexware-Daten konnten nicht geladen werden', 'OK', { duration: 3500 })
    });
  }

  loadRemoteContacts(): void { this.api.lexwareContacts().subscribe({ next: (res) => { const items = res['content'] || res['items'] || (Array.isArray(res) ? res : []); this.remoteContacts.set(items); this.snack.open('Lexware Kontakte geladen', 'OK', { duration: 2200 }); }, error: () => this.snack.open('Lexware Kontakte konnten nicht geladen werden', 'OK', { duration: 3500 }) }); }
  contactName(contact: GenericRecord): string { return contact['company']?.['name'] || `${contact['person']?.['firstName'] || ''} ${contact['person']?.['lastName'] || ''}`.trim() || contact['id']; }
  contactEmail(contact: GenericRecord): string { const emails = contact['emailAddresses']?.['business'] || contact['emailAddresses']?.['office']; return Array.isArray(emails) ? (emails[0] || '—') : (emails || '—'); }

  saveConfig(): void { this.api.lexwareSaveConfig(this.form.getRawValue()).subscribe({ next: () => { this.snack.open('Lexware-Konfiguration gespeichert', 'OK', { duration: 2200 }); this.load(); }, error: () => this.snack.open('Speichern fehlgeschlagen', 'OK', { duration: 3200 }) }); }
  testConnection(): void { this.api.lexwareTest().subscribe({ next: () => { this.snack.open('Lexware-Verbindung geprüft', 'OK', { duration: 2500 }); this.load(); }, error: () => this.snack.open('Lexware-Test fehlgeschlagen', 'OK', { duration: 3500 }) }); }
  syncCustomers(): void { this.api.lexwareSyncCustomers().subscribe({ next: () => { this.snack.open('Kunden-Sync gestartet/abgeschlossen', 'OK', { duration: 2500 }); this.load(); }, error: () => this.snack.open('Kunden-Sync fehlgeschlagen', 'OK', { duration: 3500 }) }); }
  syncProjects(): void { this.api.lexwareSyncProjects().subscribe({ next: () => { this.snack.open('Auftragsverknüpfungen aktualisiert', 'OK', { duration: 2500 }); this.load(); }, error: () => this.snack.open('Auftrags-Sync fehlgeschlagen', 'OK', { duration: 3500 }) }); }
  syncDocuments(): void { this.api.lexwareSyncDocuments().subscribe({ next: () => { this.snack.open('Belege synchronisiert', 'OK', { duration: 2500 }); this.load(); }, error: () => this.snack.open('Beleg-Sync fehlgeschlagen', 'OK', { duration: 3500 }) }); }
  runAutoSync(force = false): void { this.api.lexwareAutoSync(force).subscribe({ next: (res) => { this.snack.open(res['skipped'] ? 'Auto-Sync noch nicht fällig' : 'Auto-Sync ausgeführt', 'OK', { duration: 2500 }); this.load(); }, error: () => this.snack.open('Auto-Sync fehlgeschlagen', 'OK', { duration: 3500 }) }); }
  linkSelected(): void { this.api.lexwareLink({ customer_id: this.selectedCustomer(), project_id: this.selectedProject() }).subscribe({ next: () => { this.snack.open('Lexware-Verknüpfung gespeichert', 'OK', { duration: 2200 }); this.load(); }, error: () => this.snack.open('Verknüpfung fehlgeschlagen', 'OK', { duration: 3500 }) }); }
  linkQuotation(): void { this.api.lexwareLinkQuotation(this.selectedProject(), this.selectedQuotation()).subscribe({ next: () => { this.snack.open('Lexware-Angebot mit Auftrag verknüpft', 'OK', { duration: 2400 }); this.load(); }, error: () => this.snack.open('Angebot konnte nicht verknüpft werden', 'OK', { duration: 3500 }) }); }
}
