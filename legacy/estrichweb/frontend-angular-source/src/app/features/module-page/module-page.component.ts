import { Component, Inject, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators, FormControl } from '@angular/forms';
import { NgClass } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { ENTERPRISE_MODULES, EnterpriseModule, ModuleField, moduleByKey } from '../../core/module-registry';
import { OfflineService } from '../../core/offline.service';

// Maps *_id form fields to the module they reference, so the form can render a
// proper dropdown instead of a free-text ID field.
const RELATION_MAP: Record<string, string> = {
  customer_id: 'customers', project_id: 'projects', site_id: 'construction_sites',
  crew_id: 'crews', vehicle_id: 'vehicles', machine_id: 'machines', room_id: 'rooms',
  role_id: 'roles', user_id: 'users', foreman_user_id: 'users', assigned_user_id: 'users',
  created_by: 'users', checked_by: 'users', manager_user_id: 'users'
};
function relationLabel(record: GenericRecord): string {
  const no = record['customer_no'] || record['project_no'] || record['site_no'] || record['employee_no'];
  const name = record['name'] || ((record['first_name'] || record['last_name']) ? `${record['first_name'] || ''} ${record['last_name'] || ''}`.trim() : '') || record['email'] || record['plate'] || record['license_plate'] || record['title'];
  return [no, name].filter(Boolean).join(' · ') || String(record['id'] || '—');
}

interface RecordDialogData {
  module: EnterpriseModule;
  record?: GenericRecord | null;
  relations?: Record<string, GenericRecord[]>;
}

@Component({
  standalone: true,
  selector: 'ew-vehicle-documents-dialog',
  imports: [NgClass, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title><mat-icon>folder_shared</mat-icon>Dokumente · {{ data.vehicle['name'] || data.vehicle['plate'] || 'Fahrzeug' }}</h2>
    <mat-dialog-content class="vehicle-docs-dialog">
      <div class="vehicle-docs-actions">
        @if (canWrite) { <button mat-flat-button color="primary" (click)="pick()"><mat-icon>upload_file</mat-icon>Dokument hochladen</button> }
        <small>Fahrzeugschein, TÜV/HU, Versicherung, Wartungsnachweise … (PDF, Bild, Office)</small>
      </div>
      @if (loading()) { <div class="vehicle-docs-loading"><mat-spinner diameter="26"></mat-spinner></div> }
      @if (uploading()) { <p class="vehicle-docs-hint"><mat-spinner diameter="16"></mat-spinner> Wird hochgeladen …</p> }
      @if (!loading() && !docs().length) { <div class="vehicle-docs-empty"><mat-icon>description</mat-icon><span>Noch keine Dokumente hinterlegt.</span></div> }
      @if (docs().length) {
        <ul class="vehicle-docs-list">
          @for (doc of docs(); track doc['id']) {
            <li>
              <mat-icon>{{ docIcon(doc) }}</mat-icon>
              <div class="vehicle-doc-meta"><b>{{ doc['name'] || doc['original_name'] || 'Dokument' }}</b><small>{{ docDate(doc) }} · {{ docSize(doc) }}</small></div>
              <a mat-icon-button [href]="api.documentViewUrl(doc['id'])" target="_blank" rel="noopener" aria-label="Öffnen"><mat-icon>open_in_new</mat-icon></a>
              @if (canDelete) { <button mat-icon-button class="vehicle-doc-del" (click)="remove(doc)" aria-label="Löschen"><mat-icon>delete</mat-icon></button> }
            </li>
          }
        </ul>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-stroked-button mat-dialog-close>Schließen</button></mat-dialog-actions>
  `
})
export class VehicleDocumentsDialogComponent {
  readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly snack = inject(MatSnackBar);
  readonly canWrite = this.auth.canWrite('documents');
  readonly canDelete = this.auth.canDelete('documents');
  readonly docs = signal<GenericRecord[]>([]);
  readonly loading = signal(true);
  readonly uploading = signal(false);
  constructor(@Inject(MAT_DIALOG_DATA) public data: { vehicle: GenericRecord }) { this.reload(); }
  reload(): void {
    this.loading.set(true);
    this.api.vehicleDocuments(String(this.data.vehicle['id'])).subscribe({
      next: (res) => { this.docs.set(res.items || []); this.loading.set(false); },
      error: () => { this.docs.set([]); this.loading.set(false); }
    });
  }
  pick(): void {
    const input = document.createElement('input');
    input.type = 'file'; input.accept = '.pdf,.doc,.docx,.xls,.xlsx,image/*'; input.multiple = true;
    input.onchange = () => {
      const files = Array.from(input.files || []);
      if (!files.length) return;
      this.uploading.set(true);
      let done = 0;
      files.forEach((file) => {
        this.api.uploadVehicleDocument(String(this.data.vehicle['id']), file).subscribe({
          next: () => { done += 1; if (done === files.length) { this.uploading.set(false); this.snack.open('Dokument(e) gespeichert', 'OK', { duration: 2200 }); this.reload(); } },
          error: () => { this.uploading.set(false); this.snack.open('Upload fehlgeschlagen: ' + file.name, 'OK', { duration: 3600 }); }
        });
      });
    };
    input.click();
  }
  remove(doc: GenericRecord): void {
    this.api.deleteVehicleDocument(String(this.data.vehicle['id']), String(doc['id'])).subscribe({
      next: () => { this.docs.update((items) => items.filter((d) => d['id'] !== doc['id'])); this.snack.open('Dokument gelöscht', 'OK', { duration: 1800 }); },
      error: () => this.snack.open('Löschen fehlgeschlagen', 'OK', { duration: 3000 })
    });
  }
  docIcon(doc: GenericRecord): string { const n = String(doc['name'] || doc['original_name'] || '').toLowerCase(); if (n.endsWith('.pdf')) return 'picture_as_pdf'; if (/\.(jpe?g|png|webp|heic|gif)$/.test(n)) return 'image'; if (/\.(xls|xlsx|csv)$/.test(n)) return 'table_chart'; if (/\.(doc|docx)$/.test(n)) return 'description'; return 'insert_drive_file'; }
  docDate(doc: GenericRecord): string { const raw = String(doc['created_at'] || doc['updated_at'] || '').replace(' ', 'T'); const d = new Date(raw); return Number.isNaN(d.getTime()) ? '—' : new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(d); }
  docSize(doc: GenericRecord): string { const b = Number(doc['size_bytes'] || 0); if (!b) return '—'; if (b < 1024) return `${b} B`; if (b < 1048576) return `${(b / 1024).toFixed(0)} KB`; return `${(b / 1048576).toFixed(1)} MB`; }
}

@Component({
  standalone: true,
  selector: 'ew-resource-detail-dialog',
  imports: [NgClass, MatButtonModule, MatIconModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title><mat-icon>{{ data.module.icon }}</mat-icon>{{ recordTitle() }}</h2>
    <mat-dialog-content class="resource-detail-dialog">
      @if (statusValue()) { <span class="status-pill resource-detail-status" [ngClass]="'status-' + statusValue()">{{ statusLabel(statusValue()) }}</span> }
      <dl class="resource-detail-list">
        @for (field of visibleFields(); track field.key) {
          <div class="resource-detail-item">
            <dt>{{ field.label }}</dt>
            <dd>{{ displayValue(field) }}</dd>
          </div>
        }
      </dl>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-stroked-button mat-dialog-close>Schließen</button>
      @if (data.canEdit) { <button mat-flat-button color="primary" [mat-dialog-close]="'edit'"><mat-icon>edit</mat-icon>Bearbeiten</button> }
    </mat-dialog-actions>
  `
})
export class ResourceDetailDialogComponent {
  constructor(@Inject(MAT_DIALOG_DATA) public data: { module: EnterpriseModule; record: GenericRecord; canEdit?: boolean }) {}
  recordTitle(): string { const r = this.data.record; return String(r['name'] || r['title'] || r['plate'] || 'Datensatz'); }
  statusValue(): string { return String(this.data.record['status'] ?? ''); }
  visibleFields(): ModuleField[] { return this.data.module.fields.filter((f) => f.key !== 'status'); }
  displayValue(field: ModuleField): string {
    const v = this.data.record[field.key];
    if (v === null || v === undefined || v === '') return '—';
    if (field.type === 'boolean') return v ? 'Ja' : 'Nein';
    if (field.type === 'date') { const d = new Date(String(v).replace(' ', 'T')); return Number.isNaN(d.getTime()) ? String(v) : new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(d); }
    return String(v);
  }
  statusLabel(value: string): string {
    const map: Record<string,string> = { available:'Verfügbar', planned:'Geplant', maintenance:'In Wartung', defect:'Defekt', inactive:'Inaktiv', active:'Aktiv', reserved:'Reserviert', repair:'In Reparatur' };
    return map[value] || value || '—';
  }
}

@Component({
  selector: 'ew-record-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, NgClass, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title><span class="dialog-title-icon"><mat-icon>{{ data.record ? 'edit' : 'add_circle' }}</mat-icon></span><span>{{ data.record ? 'Datensatz bearbeiten' : data.module.label + ' erfassen' }}</span></h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid dialog-form">
        @for (field of data.module.fields; track field.key) {
          <mat-form-field [ngClass]="{ wide: field.type === 'textarea' || field.type === 'json' }" appearance="outline">
            <mat-label>{{ fieldLabel(field) }}</mat-label>
            @if (isRelation(field)) {
              <mat-select [formControlName]="field.key" panelClass="ew-search-select-panel">
                @if (!field.required) { <mat-option [value]="null">— nicht zugeordnet —</mat-option> }
                @for (rel of relationsFor(field); track rel['id']) { <mat-option [value]="rel['id']">{{ relLabel(rel) }}</mat-option> }
              </mat-select>
            } @else if (field.type === 'select') {
              <mat-select [formControlName]="field.key">
                @for (option of field.options ?? []; track option) { <mat-option [value]="option">{{ optionLabel(option) }}</mat-option> }
              </mat-select>
            } @else if (field.type === 'textarea' || field.type === 'json') {
              <textarea matInput [formControlName]="field.key" rows="4"></textarea>
            } @else if (field.type === 'boolean') {
              <mat-select [formControlName]="field.key"><mat-option [value]="true">Ja</mat-option><mat-option [value]="false">Nein</mat-option></mat-select>
            } @else {
              <input matInput [type]="inputType(field)" [formControlName]="field.key" [readonly]="isAutoProjectNo(field)" [placeholder]="isAutoProjectNo(field) ? 'wird automatisch vergeben' : ''">
            }
          </mat-form-field>
        }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="save()"><mat-icon>save</mat-icon> Speichern</button>
    </mat-dialog-actions>
  `
})
export class RecordDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<RecordDialogComponent>);
  readonly form = this.fb.group<Record<string, FormControl<string | number | boolean | null>>>({});

  constructor(@Inject(MAT_DIALOG_DATA) public data: RecordDialogData) {
    const group: Record<string, FormControl<string | number | boolean | null>> = {};
    data.module.fields.forEach((field) => {
      const validators = field.required ? [Validators.required] : [];
      const current = data.record?.[field.key];
      group[field.key] = new FormControl<string | number | boolean | null>(current ?? this.defaultValue(field), validators);
    });
    this.form = this.fb.group(group);
  }

  isRelation(field: ModuleField): boolean { return !!RELATION_MAP[field.key] && (field.type === 'text' || field.type === 'select'); }
  relationsFor(field: ModuleField): GenericRecord[] { return this.data.relations?.[field.key] ?? []; }
  relLabel(rec: GenericRecord): string { return relationLabel(rec); }
  fieldLabel(field: ModuleField): string {
    if (this.isAutoProjectNo(field)) return 'Bauaktennummer (automatisch)';
    if (this.isRelation(field)) return field.label.replace(/\s*[-–]?\s*ID$/i, '').trim() || field.label;
    return field.label;
  }

  isAutoProjectNo(field: ModuleField): boolean {
    return this.data.module.key === 'projects' && field.key === 'project_no';
  }

  save(): void {
    if (this.form.invalid) return;
    this.ref.close(this.form.getRawValue());
  }

  optionLabel(option: string): string {
    const map: Record<string,string> = {
      new:'Neu', planned:'Geplant', in_progress:'In Arbeit', delayed:'Verzögert', defect:'Mangel', completed:'Abgeschlossen',
      open:'Offen', assigned:'Zugewiesen', done:'Erledigt', archived:'Archiviert', closed:'Geschlossen',
      available:'Verfügbar', maintenance:'In Wartung', inactive:'Inaktiv', active:'Aktiv', reserved:'Reserviert', broken:'Defekt', retired:'Ausgemustert',
      low:'Niedrig', medium:'Normal', normal:'Normal', high:'Hoch', critical:'Kritisch', urgent:'Dringend',
      intern:'Intern', kunde:'Kunde', subunternehmer:'Subunternehmer', öffentlich:'Öffentlich',
      site_visit:'Erstbegehung', installation:'Ausführung', measurement:'Messung / QM', acceptance:'Abnahme', delivery:'Lieferung', meeting:'Besprechung', office:'Interner Termin'
    };
    return map[String(option)] || String(option);
  }

  inputType(field: ModuleField): string {
    if (field.type === 'number') return 'number';
    if (field.type === 'date') return 'date';
    if (field.type === 'datetime') return 'datetime-local';
    if (field.type === 'email') return 'email';
    if (field.type === 'phone') return 'tel';
    return 'text';
  }

  defaultValue(field: ModuleField): string | number | boolean | null {
    if (field.type === 'boolean') return false;
    if (field.type === 'number') return null;
    return '';
  }
}

@Component({
  standalone: true,
  imports: [NgClass, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatTableModule, MatChipsModule, MatSnackBarModule, MatProgressSpinnerModule, MatDialogModule],
  template: `
    <section class="page-head compact-head">
      <div>
        <p class="eyebrow">{{ module().category }} · {{ module().permission }}</p>
        <h1><mat-icon>{{ module().icon }}</mat-icon>{{ module().label }}</h1>
      </div>
      <div class="head-actions">
        @if (isCustomers()) {
          @if (canWrite()) { <button mat-flat-button color="primary" (click)="openCreate()"><mat-icon>person_add</mat-icon> Neuer Kunde</button> }
          @if (canWrite()) { <button mat-stroked-button (click)="syncLexwareCustomers()" [disabled]="lexwareSyncing()"><mat-icon>sync</mat-icon> {{ lexwareSyncing() ? 'Aktualisiere…' : 'Lexware abgleichen' }}</button> }
        } @else {
          @if (canWrite()) { <button mat-flat-button color="primary" (click)="openCreate()"><mat-icon>add</mat-icon> Neu erfassen</button> }
        }
        <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon> Aktualisieren</button>
      </div>
    </section>

    @if (isCustomers()) {
      <div class="customer-source-tabs">
        <button class="cst" [class.is-active]="customerSource() === ''" (click)="setCustomerSource('')"><mat-icon>groups</mat-icon> Alle Kunden</button>
        <button class="cst" [class.is-active]="customerSource() === 'lexware'" (click)="setCustomerSource('lexware')"><mat-icon>sync</mat-icon> Lexware-Kunden</button>
        <button class="cst" [class.is-active]="customerSource() === 'manual'" (click)="setCustomerSource('manual')"><mat-icon>edit_note</mat-icon> Manuell (aus Bauakten)</button>
      </div>
    }

    @if (isCustomers() && lexwareSyncing()) {
      <div class="lexware-sync-progress">
        <div class="lsp-head"><mat-icon class="lsp-spin">sync</mat-icon><b>Kunden werden aus Lexware aktualisiert…</b></div>
        <div class="lsp-bar"><div class="lsp-fill" [class.indeterminate]="syncTotal() === 0" [style.width.%]="syncTotal() > 0 ? syncPercent() : 100"></div></div>
        <div class="lsp-stats">
          @if (syncTotal() > 0) { <span>{{ syncProcessed() }} / {{ syncTotal() }} ({{ syncPercent() }} %)</span> } @else { <span>Seite {{ syncPage() }} · {{ syncProcessed() }} verarbeitet</span> }
          <span>· {{ syncCreated() }} neu · {{ syncUpdated() }} aktualisiert · {{ syncSuppliers() }} Lieferanten</span>
        </div>
      </div>
    }

    @if (isCustomers()) {
      <mat-card class="lexware-notice-card">
        <mat-icon>info</mat-icon>
        <div>
          <strong>Kunden aus Lexware oder direkt hier anlegen.</strong>
          <p>Kunden können direkt in EstrichWeb angelegt werden – auch ohne Kundennummer und direkt aus der Bauakte heraus. Zusätzlich lässt sich der Bestand jederzeit mit Lexware Office abgleichen.</p>
          <a mat-stroked-button routerLink="/lexware-integration"><mat-icon>sync_alt</mat-icon> Zur Lexware-Integration</a>
        </div>
      </mat-card>
    }

    <mat-card class="table-card module-list-card">
      <div class="table-tools">
        <mat-form-field appearance="outline">
          <mat-label>Suche</mat-label>
          <input matInput [value]="query()" (input)="search($any($event.target).value)">
          <mat-icon matPrefix>search</mat-icon>
        </mat-form-field>
        @if (hasStatusField()) {
          <mat-form-field appearance="outline" class="table-filter-field">
            <mat-label>Status</mat-label>
            <mat-select [value]="statusFilterValue()" (selectionChange)="statusFilterValue.set($event.value)">
              <mat-option value="">Alle</mat-option>
              @for (opt of statusOptions(); track opt) { <mat-option [value]="opt">{{ valueLabel(opt) }}</mat-option> }
            </mat-select>
          </mat-form-field>
        }
        @if (filterRelationField(); as rf) {
          <mat-form-field appearance="outline" class="table-filter-field">
            <mat-label>{{ rf.label }}</mat-label>
            <mat-select [value]="relationFilterValue()" (selectionChange)="relationFilterValue.set($event.value)">
              <mat-option value="">Alle</mat-option>
              @for (rel of relationsFor(rf.key); track rel['id']) { <mat-option [value]="rel['id']">{{ relLabelText(rel) }}</mat-option> }
            </mat-select>
          </mat-form-field>
        }
        @if (hasActiveFilter()) { <button mat-stroked-button (click)="clearFilters()"><mat-icon>backspace</mat-icon>Filter zurücksetzen</button> }
        @if (loading()) { <mat-spinner diameter="28"></mat-spinner> }
        <span class="table-count">Gesamt: <b>{{ total() }}</b>@if (total() > pageSize()) { <span> · {{ rangeStart() }}–{{ rangeEnd() }}</span> }</span>
      </div>

      <div class="responsive-table-wrap desktop-table-only">
        <table mat-table [dataSource]="filteredItems()" class="data-table pro-data-table">
          @if (hasRowActions()) {
            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Aktion</th>
              <td mat-cell *matCellDef="let row" data-label="Aktion">
                <div class="row-action-group">
                  @if (isResourceModule()) { <button mat-stroked-button class="resource-detail-btn" (click)="openResourceDetail(row, $event)"><mat-icon>info</mat-icon>Details</button> }
                  @if (isVehicles() && canManageVehicleDocs()) { <button mat-stroked-button class="vehicle-docs-btn" (click)="openVehicleDocs(row, $event)"><mat-icon>folder_shared</mat-icon>Dokumente</button> }
                  @if (canWrite()) { <button mat-stroked-button class="row-edit-btn" (click)="openEdit(row, $event)"><mat-icon>edit</mat-icon>Bearbeiten</button> }
                  @if (canDeleteCurrent()) { <button mat-stroked-button class="square-delete-text" (click)="deleteRecord(row, $event)"><mat-icon>delete</mat-icon>Löschen</button> }
                </div>
              </td>
            </ng-container>
          }
          @for (column of displayedColumns(); track column) {
            <ng-container [matColumnDef]="column">
              <th mat-header-cell *matHeaderCellDef>{{ columnLabel(column) }}</th>
              <td mat-cell *matCellDef="let row" [attr.data-label]="columnLabel(column)"><span [ngClass]="statusClass(row, column)">{{ display(row, column) }}</span></td>
            </ng-container>
          }
          <tr mat-header-row *matHeaderRowDef="tableColumns()"></tr>
          <tr mat-row class="clickable-row" *matRowDef="let row; columns: tableColumns();" (click)="isResourceModule() ? openResourceDetail(row) : openEdit(row)"></tr>
        </table>
      </div>

      <section class="mobile-record-list">
        @for (row of filteredItems(); track row['id']) {
          <article class="mobile-record-card" (click)="isResourceModule() ? openResourceDetail(row) : openEdit(row)">
            <header>
              <div>
                <strong>{{ recordTitle(row) }}</strong>
                <span>{{ recordSubtitle(row) }}</span>
              </div>
              @if (row['status']) { <span class="status-pill" [ngClass]="statusClass(row, 'status')">{{ display(row, 'status') }}</span> }
            </header>
            <dl>
              @for (column of mobileColumns(); track column) {
                <div><dt>{{ columnLabel(column) }}</dt><dd>{{ display(row, column) }}</dd></div>
              }
            </dl>
            <div class="mobile-record-actions">
              @if (isVehicles() && canManageVehicleDocs()) { <button mat-stroked-button class="vehicle-docs-btn mobile-vehicle-docs" (click)="openVehicleDocs(row, $event)"><mat-icon>folder_shared</mat-icon>Dokumente</button> }
              @if (canWrite()) { <button mat-stroked-button class="mobile-edit-record" (click)="openEdit(row, $event)"><mat-icon>edit</mat-icon>Bearbeiten</button> }
              @if (canDeleteCurrent()) { <button mat-stroked-button class="square-delete-text mobile-delete-record" (click)="deleteRecord(row, $event)"><mat-icon>delete</mat-icon>{{ deleteLabel() }}</button> }
            </div>
          </article>
        }
      </section>

      @if (!filteredItems().length && !loading()) { <p class="empty"><mat-icon>inbox</mat-icon>Noch keine Datensätze vorhanden.</p> }

      @if (pageCount() > 1) {
        <div class="table-pagination">
          <button mat-stroked-button (click)="goToPage(0)" [disabled]="pageIndex() === 0" title="Erste Seite"><mat-icon>first_page</mat-icon></button>
          <button mat-stroked-button (click)="prevPage()" [disabled]="pageIndex() === 0"><mat-icon>chevron_left</mat-icon> Zurück</button>
          <span class="page-info">Seite {{ pageIndex() + 1 }} / {{ pageCount() }}</span>
          <button mat-stroked-button (click)="nextPage()" [disabled]="pageIndex() + 1 >= pageCount()">Weiter <mat-icon>chevron_right</mat-icon></button>
          <button mat-stroked-button (click)="goToPage(pageCount() - 1)" [disabled]="pageIndex() + 1 >= pageCount()" title="Letzte Seite"><mat-icon>last_page</mat-icon></button>
        </div>
      }
    </mat-card>

    @if (canWrite()) { <button class="mobile-fab" mat-fab color="primary" (click)="openCreate()" aria-label="Neu erfassen"><mat-icon>add</mat-icon></button> }
  `
})
export class ModulePageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly snack = inject(MatSnackBar);
  private readonly offline = inject(OfflineService);
  private readonly dialog = inject(MatDialog);

  readonly moduleKey = signal(ENTERPRISE_MODULES[0].key);
  readonly module = computed<EnterpriseModule>(() => moduleByKey(this.moduleKey()) ?? ENTERPRISE_MODULES[0]);
  readonly relations = signal<Record<string, GenericRecord[]>>({});
  readonly lexwareSyncing = signal(false);
  readonly customerSource = signal<'' | 'lexware' | 'manual'>('');
  readonly syncProcessed = signal(0);
  readonly syncCreated = signal(0);
  readonly syncUpdated = signal(0);
  readonly syncSuppliers = signal(0);
  readonly syncTotal = signal(0);
  readonly syncPage = signal(0);
  readonly syncPercent = computed(() => { const t = this.syncTotal(); return t > 0 ? Math.min(100, Math.round((this.syncProcessed() / t) * 100)) : 0; });
  loadRelations(): void {
    const map: Record<string, GenericRecord[]> = {};
    this.relations.set({});
    // Alle Verweisfelder laden – auch solche, die nur in der Tabelle angezeigt
    // und nicht im Formular als Auswahl dargestellt werden. Sonst stuende in der
    // Spalte weiterhin die ID statt des Namens.
    const fields = this.module().fields.filter((f) => !!RELATION_MAP[f.key]);
    fields.forEach((f) => {
      this.api.list(RELATION_MAP[f.key], '', 500).subscribe({
        next: (res) => { map[f.key] = res.items || []; this.relations.set({ ...map }); },
        error: () => { map[f.key] = []; this.relations.set({ ...map }); }
      });
    });
  }
  isCustomers(): boolean { return this.moduleKey() === 'customers'; }
  isVehicles(): boolean { return this.moduleKey() === 'vehicles'; }
  openVehicleDocs(row: GenericRecord, event?: Event): void {
    event?.stopPropagation();
    this.dialog.open(VehicleDocumentsDialogComponent, { width: 'min(640px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', data: { vehicle: row } });
  }
  openResourceDetail(row: GenericRecord, event?: Event): void {
    event?.stopPropagation();
    const ref = this.dialog.open(ResourceDetailDialogComponent, { width: 'min(620px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', data: { module: this.module(), record: row, canEdit: this.canWrite() } });
    ref.afterClosed().subscribe((action) => { if (action === 'edit') this.openEdit(row); });
  }
  syncLexwareCustomers(): void {
    if (this.lexwareSyncing()) return;
    this.lexwareSyncing.set(true);
    this.syncProcessed.set(0); this.syncCreated.set(0); this.syncUpdated.set(0); this.syncSuppliers.set(0); this.syncTotal.set(0); this.syncPage.set(0);
    this.runSyncChunk(0);
  }
  private runSyncChunk(page: number): void {
    this.syncPage.set(page + 1);
    this.api.lexwareSyncCustomersChunk(page).subscribe({
      next: (res) => {
        this.syncProcessed.update((v) => v + Number(res['processed'] || 0));
        this.syncCreated.update((v) => v + Number(res['created'] || 0));
        this.syncUpdated.update((v) => v + Number(res['updated'] || 0));
        this.syncSuppliers.update((v) => v + Number(res['suppliers_created'] || 0) + Number(res['suppliers_updated'] || 0));
        const total = Number(res['total_elements'] || 0);
        if (total > 0) this.syncTotal.set(total);
        if (res['has_more']) {
          this.runSyncChunk(Number(res['next_page'] ?? page + 1));
        } else {
          this.lexwareSyncing.set(false);
          this.snack.open(`Lexware fertig: ${this.syncCreated()} neu, ${this.syncUpdated()} aktualisiert, ${this.syncSuppliers()} Lieferanten`, 'OK', { duration: 4000 });
          this.pageIndex.set(0); this.load();
        }
      },
      error: () => { this.lexwareSyncing.set(false); this.snack.open('Lexware-Aktualisierung fehlgeschlagen. Bitte Lexware-Integration prüfen.', 'OK', { duration: 4000 }); }
    });
  }
  readonly items = signal<GenericRecord[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly query = signal('');
  readonly total = signal(0);
  readonly pageSize = signal(50);
  readonly pageIndex = signal(0);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  readonly rangeStart = computed(() => this.total() === 0 ? 0 : this.pageIndex() * this.pageSize() + 1);
  readonly rangeEnd = computed(() => Math.min(this.total(), (this.pageIndex() + 1) * this.pageSize()));
  readonly statusFilterValue = signal('');
  readonly relationFilterValue = signal('');
  hasStatusField(): boolean { return this.module().fields.some((f) => f.key === 'status'); }
  statusOptions(): string[] {
    const field = this.module().fields.find((f) => f.key === 'status');
    if (field?.options?.length) return field.options;
    return Array.from(new Set(this.items().map((r) => String(r['status'] ?? '')).filter(Boolean)));
  }
  // Pro Modul ein sinnvolles Relations-Filterfeld (z. B. Kolonne bei Mitarbeitern)
  filterRelationField(): ModuleField | null {
    const key = this.moduleKey();
    const wanted = key === 'crew_members' ? 'crew_id' : key === 'crews' ? 'foreman_user_id' : '';
    if (!wanted) return null;
    return this.module().fields.find((f) => f.key === wanted) ?? null;
  }
  relationsFor(key: string): GenericRecord[] { return this.relations()[key] ?? []; }
  relLabelText(rec: GenericRecord): string { return relationLabel(rec); }
  hasActiveFilter(): boolean { return !!this.statusFilterValue() || !!this.relationFilterValue(); }
  clearFilters(): void { this.statusFilterValue.set(''); this.relationFilterValue.set(''); }
  readonly filteredItems = computed(() => {
    const status = this.statusFilterValue();
    const relVal = this.relationFilterValue();
    const relField = this.filterRelationField()?.key;
    return this.items().filter((row) => {
      if (status && String(row['status'] ?? '') !== status) return false;
      if (relVal && relField && String(row[relField] ?? '') !== relVal) return false;
      return true;
    });
  });
  readonly displayedColumns = computed(() => {
    const moduleFields = this.module().fields.map((field) => field.key);
    // Kolonnen: Kolonne, Vorarbeiter, Kapazität
    if (this.moduleKey() === 'crews') {
      return ['name', 'foreman_user_id', 'capacity_m2_day', 'status'].filter((k) => moduleFields.includes(k));
    }
    // Mitarbeiter: Name, Kolonne, Rolle, Benutzer, Gültigkeit
    if (this.moduleKey() === 'crew_members') {
      return ['name', 'crew_id', 'role', 'user_id', 'valid_from', 'valid_to'].filter((k) => moduleFields.includes(k));
    }
    // Ressourcen (Fahrzeuge, Estrichpumpen, Maschinen): mehr Detailspalten zeigen.
    if (['vehicles', 'pumps', 'machines'].includes(this.moduleKey())) {
      const preferredRes = ['name', 'plate', 'type', 'make', 'model', 'manufacturer', 'serial_no', 'status', 'inspection_due', 'next_service'];
      const chosenRes = preferredRes.filter((key) => moduleFields.includes(key));
      return [...chosenRes, 'updated_at'].filter((v, i, a) => v !== 'id' && a.indexOf(v) === i).slice(0, 8);
    }
    // Kunden: Kundennummer, Name, Ort, E-Mail, Telefon
    if (this.moduleKey() === 'customers') {
      return ['customer_no', 'name', 'city', 'email', 'phone'].filter((k) => moduleFields.includes(k) || k === 'customer_no' || k === 'name');
    }
    const preferred = ['name', 'title', 'project_no', 'site_no', 'status', 'city', 'start_at', 'end_at'];
    const chosen = preferred.filter((key) => moduleFields.includes(key)).slice(0, 5);
    const fallback = moduleFields.slice(0, 4);
    return [...(chosen.length ? chosen : fallback), 'updated_at'].filter((value, index, array) => value !== 'id' && array.indexOf(value) === index).slice(0, 6);
  });
  isResourceModule(): boolean { return ['vehicles', 'pumps', 'machines'].includes(this.moduleKey()); }
  /** Aktionsspalte nur anzeigen, wenn der Benutzer dort auch wirklich etwas darf. */
  hasRowActions(): boolean { return this.canDeleteCurrent() || this.canWrite() || this.isResourceModule() || (this.isVehicles() && this.canManageVehicleDocs()); }
  /** Aktionsspalte steht immer GANZ RECHTS – Daten zuerst, Bedienung zuletzt. */
  readonly tableColumns = computed(() => this.hasRowActions() ? [...this.displayedColumns(), 'actions'] : this.displayedColumns());

  constructor() {
    this.route.data.subscribe((data) => {
      const key = String(data['moduleKey'] ?? ENTERPRISE_MODULES[0].key);
      this.moduleKey.set(key);
      this.load();
      this.loadRelations();
    });
  }

  load(): void {
    this.loading.set(true);
    const source = this.isCustomers() ? this.customerSource() : '';
    this.api.list(this.moduleKey(), this.query(), this.pageSize(), this.pageIndex() * this.pageSize(), source).subscribe({
      next: (response) => { this.items.set(response.items); this.total.set(response.total ?? response.items.length); this.loading.set(false); },
      error: () => { this.loading.set(false); this.snack.open('Laden fehlgeschlagen', 'OK', { duration: 3000 }); }
    });
  }
  setCustomerSource(source: '' | 'lexware' | 'manual'): void { this.customerSource.set(source); this.pageIndex.set(0); this.load(); }

  goToPage(index: number): void {
    const clamped = Math.min(Math.max(0, index), this.pageCount() - 1);
    if (clamped === this.pageIndex()) return;
    this.pageIndex.set(clamped);
    this.load();
  }
  nextPage(): void { this.goToPage(this.pageIndex() + 1); }
  prevPage(): void { this.goToPage(this.pageIndex() - 1); }
  onSearchChange(value: string): void { this.query.set(value); this.pageIndex.set(0); this.load(); }

  openCreate(): void {
    const ref = this.dialog.open(RecordDialogComponent, { width: 'min(760px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', data: { module: this.module(), record: null, relations: this.relations() } });
    ref.afterClosed().subscribe((payload: GenericRecord | undefined) => {
      if (!payload) return;
      this.saving.set(true);
      this.api.create(this.moduleKey(), payload).subscribe({
        next: (created) => {
          this.saving.set(false);
          this.load();
          // Nach dem Anlegen eines Kunden folgt fast immer die Bauakte.
          // Statt "Gespeichert" und Zurückspringen wird der Weg direkt
          // angeboten – mit dem Kunden bereits ausgewählt.
          if (this.moduleKey() === 'customers' && created?.['id']) {
            this.snack.open(`Kunde „${created['name'] || 'neu'}" angelegt.`, 'Bauakte anlegen', { duration: 7000 })
              .onAction().subscribe(() => {
                this.router.navigate(['/auftrag-wizard'], { queryParams: { customer_id: created['id'] } });
              });
            return;
          }
          this.snack.open('Gespeichert', 'OK', { duration: 2000 });
        },
        error: () => { this.saving.set(false); this.offline.enqueue({ moduleKey: this.moduleKey(), operation: 'create', payload }); this.snack.open('Server nicht erreichbar. Offline vorgemerkt.', 'OK', { duration: 3500 }); }
      });
    });
  }

  openEdit(row: GenericRecord, event?: Event): void {
    event?.stopPropagation();
    // Ohne Schreibrecht wird nur die Detailansicht geoeffnet – kein Bearbeiten-Dialog.
    if (!this.canWrite()) { this.openResourceDetail(row); return; }
    const ref = this.dialog.open(RecordDialogComponent, { width: 'min(760px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', data: { module: this.module(), record: row, relations: this.relations() } });
    ref.afterClosed().subscribe((payload: GenericRecord | undefined) => {
      if (!payload) return;
      this.api.update(this.moduleKey(), String(row['id']), payload).subscribe({
        next: () => { this.snack.open('Aktualisiert', 'OK', { duration: 2000 }); this.load(); },
        error: () => this.snack.open('Aktualisieren fehlgeschlagen', 'OK', { duration: 3000 })
      });
    });
  }

  /** Bauakten nur fuer Admins (Sonderweg mit Passwortbestaetigung),
   *  alle anderen Stammdaten mit regulaerem Loeschrecht des Moduls. */
  canDeleteCurrent(): boolean {
    if (this.moduleKey() === 'projects') return this.isAdmin();
    if (this.moduleKey() === 'audit_logs') return false;   // revisionssicher
    return this.auth.canDelete(this.module().permission);
  }
  isAdmin(): boolean { return this.auth.isAdmin(); }
  canWrite(): boolean { return this.auth.canWrite(this.module().permission); }
  canManageVehicleDocs(): boolean { return this.auth.canWrite('documents'); }
  /** Text fuer den Loeschen-Button, damit nicht ueberall \"Auftrag loeschen\" steht. */
  deleteLabel(): string { return this.moduleKey() === 'projects' ? 'Bauakte löschen' : 'Löschen'; }
  deleteRecord(row: GenericRecord, event: MouseEvent): void {
    event.preventDefault(); event.stopPropagation();
    if (!this.canDeleteCurrent()) return;
    const isProject = this.moduleKey() === 'projects';
    const title = String(row['name'] || row['title'] || row['plate'] || row['project_no'] || row['site_no'] || 'Datensatz');
    const label = isProject ? 'Bauakte' : this.module().label.replace(/e$/, '');
    const ref = this.dialog.open(ConfirmDialogComponent, {
      width: 'min(440px, 94vw)', panelClass: 'ew-dialog',
      data: {
        title: `${isProject ? 'Bauakte' : this.module().label} löschen`,
        // Der Text muss der Wirklichkeit entsprechen: Seit der Umstellung
        // wird endgültig gelöscht, nicht nur ausgeblendet.
        message: `Soll „${title}“ endgültig gelöscht werden? Der Datensatz und die zugehörigen Dateien werden dauerhaft entfernt und lassen sich nicht wiederherstellen.`,
        confirmText: 'Löschen', cancelText: 'Abbrechen', icon: 'delete', tone: 'warn'
      }
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (!confirmed) return;
      this.api.remove(this.moduleKey(), String(row['id'])).subscribe({
        next: () => { this.snack.open(`${label} gelöscht`, 'OK', { duration: 2200 }); this.load(); },
        error: () => this.snack.open(`${label} konnte nicht gelöscht werden`, 'OK', { duration: 3200 })
      });
    });
  }

  search(value: string): void { this.query.set(value); this.pageIndex.set(0); this.load(); }

  mobileColumns(): string[] {
    return this.displayedColumns().filter((column) => !['name','title','status'].includes(column)).slice(0, 4);
  }

  recordTitle(row: GenericRecord): string {
    return String(row['name'] || row['title'] || row['project_no'] || row['site_no'] || row['email'] || row['label'] || 'Datensatz');
  }

  recordSubtitle(row: GenericRecord): string {
    const parts = [row['city'], row['customer_name'], row['email'], row['phone'], row['updated_at']].filter(Boolean);
    return parts.length ? String(parts.slice(0, 2).join(' · ')) : this.module().label;
  }

  columnLabel(column: string): string {
    if (column === 'id') return 'ID';
    if (column === 'updated_at') return 'Aktualisiert';
    const label = this.module().fields.find((field) => field.key === column)?.label ?? column;
    // Verweisfelder zeigen den Namen, nicht die ID – dann darf auch die
    // Spaltenueberschrift nicht mehr \"…-ID\" heissen.
    if (RELATION_MAP[column]) return label.replace(/\s*[-–]?\s*ID$/i, '').trim() || label;
    return label;
  }

  display(row: GenericRecord, column: string): string {
    const value = row[column];
    if (value === null || value === undefined || value === '') return '—';
    if (typeof value === 'boolean') return value ? 'Ja' : 'Nein';
    if (column === 'status' || column === 'priority' || column === 'visibility' || column === 'event_type' || column === 'role') return this.valueLabel(String(value));
    // Datumsfelder im deutschen Format
    const field = this.module().fields.find((f) => f.key === column);
    if (field?.type === 'date' || field?.type === 'datetime' || column === 'updated_at' || column === 'created_at' || /(_at|_from|_to|_due|_date)$/.test(column)) {
      const d = new Date(String(value).replace(' ', 'T'));
      if (!Number.isNaN(d.getTime())) {
        const withTime = field?.type === 'datetime' || /_at$/.test(column);
        return new Intl.DateTimeFormat('de-DE', withTime ? { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' } : { day: '2-digit', month: '2-digit', year: 'numeric' }).format(d);
      }
    }
    // Relations (z. B. crew_id, foreman_user_id) als Name auflösen
    if (String(column).endsWith('_id')) return this.relationName(column, String(value));
    return String(value);
  }

  relationName(column: string, value: string): string {
    const target = RELATION_MAP[column];
    if (!target) return this.shortReference(value);
    const rel = (this.relations()[column] || []).find((r) => String(r['id']) === value);
    // Solange die Verweisliste noch laedt, lieber nichts als eine rohe ID zeigen.
    if (!rel) return (this.relations()[column] || []).length ? '—' : '…';
    const person = (rel['first_name'] || rel['last_name']) ? `${rel['first_name'] || ''} ${rel['last_name'] || ''}`.trim() : '';
    return String(rel['name'] || person || rel['email'] || rel['plate'] || rel['license_plate'] || rel['title'] || '—');
  }

  valueLabel(value: string): string {
    const map: Record<string,string> = {
      new:'Neu', planned:'Geplant', in_progress:'In Arbeit', delayed:'Verzögert', defect:'Defekt', completed:'Abgeschlossen', closed:'Geschlossen',
      open:'Offen', assigned:'Zugewiesen', done:'Erledigt', archived:'Archiviert',
      available:'Verfügbar', maintenance:'In Wartung', inactive:'Inaktiv', active:'Aktiv', repair:'In Reparatur', reserved:'Reserviert',
      low:'Niedrig', medium:'Normal', normal:'Normal', high:'Hoch', critical:'Kritisch', urgent:'Dringend',
      intern:'Intern', customer:'Kunde', kunde:'Kunde', subcontractor:'Subunternehmer', subunternehmer:'Subunternehmer',
      site_visit:'Erstbegehung', installation:'Ausführung', measurement:'Messung / QM', acceptance:'Abnahme', delivery:'Lieferung', meeting:'Besprechung', office:'Interner Termin',
      foreman:'Kolonnenführer', kolonnenfuehrer:'Kolonnenführer', worker:'Facharbeiter', helper:'Helfer', apprentice:'Auszubildender', driver:'Fahrer', member:'Mitglied'
    };
    return map[value] || value;
  }

  shortReference(value: string): string {
    if (!value) return '—';
    if (value.length > 12) return 'verknüpft';
    return value;
  }

  statusClass(row: GenericRecord, column: string): string {
    return column === 'status' ? `status-pill status-${String(row[column] ?? 'open')}` : '';
  }
}
