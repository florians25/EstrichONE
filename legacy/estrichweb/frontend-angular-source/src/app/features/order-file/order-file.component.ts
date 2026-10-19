import { Component, Inject, OnDestroy, computed, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { DatePipe, NgClass } from '@angular/common';
import { FormBuilder, FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { jsPDF } from 'jspdf';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatMenuModule } from '@angular/material/menu';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TextFieldModule } from '@angular/cdk/text-field';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { RoomDialogComponent } from './room-dialog.component';
import { downloadMaterialOrderPdf, MaterialOrderDialogComponent, materialOrderStatusLabel } from '../material-orders/material-order-dialog.component';


interface AppointmentDialogData { project: GenericRecord; sites: GenericRecord[]; }

@Component({
  selector: 'ew-order-appointment-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule],
  template: `
    <h2 mat-dialog-title><mat-icon>event</mat-icon>Termin zur Akte</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid dialog-form">
        <mat-form-field class="wide" appearance="outline"><mat-label>Titel</mat-label><input matInput formControlName="title"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Baustelle</mat-label><mat-select formControlName="site_id"><mat-option value="">Ohne Baustelle</mat-option>@for (site of data.sites; track site['id']) { <mat-option [value]="site['id']">{{ site['site_no'] || 'Baustelle' }} · {{ site['name'] }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Datum</mat-label><input matInput type="date" formControlName="date"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Ganztägig</mat-label><mat-select formControlName="all_day"><mat-option [value]="true">Ja</mat-option><mat-option [value]="false">Nein</mat-option></mat-select></mat-form-field>
        @if (!form.value.all_day) {
          <mat-form-field appearance="outline"><mat-label>Von</mat-label><input matInput type="time" formControlName="start_time"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Bis</mat-label><input matInput type="time" formControlName="end_time"></mat-form-field>
        }
        <mat-form-field appearance="outline"><mat-label>Kategorie</mat-label><mat-select formControlName="event_type"><mat-option value="site_visit">Erstbegehung</mat-option><mat-option value="installation">Ausführung</mat-option><mat-option value="measurement">Messung / QM</mat-option><mat-option value="acceptance">Abnahme</mat-option><mat-option value="delivery">Lieferung</mat-option><mat-option value="meeting">Besprechung</mat-option><mat-option value="office">Interner Termin</mat-option></mat-select></mat-form-field>
        <mat-form-field class="wide" appearance="outline"><mat-label>Notiz</mat-label><textarea matInput rows="3" formControlName="description"></textarea></mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Abbrechen</button><button mat-flat-button color="primary" [disabled]="form.invalid" (click)="save()"><mat-icon>save</mat-icon>Speichern</button></mat-dialog-actions>
  `
})
export class OrderAppointmentDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<OrderAppointmentDialogComponent>);
  readonly form = this.fb.group({ title: ['Termin', Validators.required], site_id: [''], date: [new Date().toISOString().slice(0,10), Validators.required], all_day: [true], start_time: ['07:00'], end_time: ['16:00'], event_type: ['installation'], description: [''] });
  constructor(@Inject(MAT_DIALOG_DATA) public data: AppointmentDialogData) {}
  save(): void {
    const v=this.form.getRawValue(); const date=v.date || new Date().toISOString().slice(0,10);
    this.ref.close({ project_id: this.data.project['id'], site_id: v.site_id || null, title: v.title, description: v.description, start_at: v.all_day ? `${date} 00:00:00` : `${date} ${v.start_time || '07:00'}:00`, end_at: v.all_day ? `${date} 23:59:00` : `${date} ${v.end_time || '16:00'}:00`, all_day: v.all_day, event_type: v.event_type, visibility: 'intern', reminder_minutes: 1440, color: this.colorFor(v.event_type || 'installation'), external_provider: 'none' });
  }
  colorFor(type: string): string { return ({ installation: '#188038', measurement: '#f9ab00', acceptance: '#9334e6', site_visit: '#1a73e8', delivery: '#b06000', meeting: '#0097a7', office: '#5f6368' } as Record<string,string>)[type] || '#1a73e8'; }
}


interface OrderUploadDialogData { project: GenericRecord; sites: GenericRecord[]; }

@Component({
  selector: 'ew-order-upload-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title><mat-icon>upload_file</mat-icon>Dateien zur Bauakte hinzufügen</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid dialog-form">
        <mat-form-field class="wide" appearance="outline">
          <mat-label>Baustelle / Akte *</mat-label>
          <mat-select formControlName="site_id" panelClass="ew-search-select-panel mobile-picker-panel">
            <mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Bauakte suchen" [value]="siteSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="siteSearch.set($any($event.target).value)"></mat-option>
            @for (site of filteredEffectiveSites(); track site['id']) {
              <mat-option [value]="site['id']"><span class="mobile-option-main">{{ site['site_no'] || 'Akte' }} · {{ site['name'] }}</span>@if (site['city']) { <small class="mobile-option-sub">{{ site['address'] || '' }} {{ site['zip'] || '' }} {{ site['city'] }}</small> }</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Kategorie</mat-label>
          <mat-select formControlName="category">
            <mat-option value="bauakte">Bauakte</mat-option>
            <mat-option value="vordruck">Vordruck / Plan</mat-option>
            <mat-option value="qm">QM / Prüfung</mat-option>
            <mat-option value="foto">Foto</mat-option>
            <mat-option value="video">Video</mat-option>
            <mat-option value="angebot">Angebot</mat-option>
            <mat-option value="rechnung">Rechnung</mat-option>
            <mat-option value="kundendokument">Kundendokument</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Bezeichnung</mat-label><input matInput formControlName="name" placeholder="z. B. Fotos Untergrund"></mat-form-field>
        <input #fileInput type="file" multiple hidden accept=".pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png,.webp,.heic,.mp4,.webm,.mov,video/*,image/*" (change)="onFiles($event)">
        <button mat-stroked-button type="button" class="wide" (click)="fileInput.click()"><mat-icon>attach_file</mat-icon>Dateien auswählen</button>
        @if (files().length) {
          <div class="selected-upload-list wide">
            @for (file of files(); track file.name) { <span>{{ file.name }} · {{ mb(file.size) }}</span> }
          </div>
        }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Abbrechen</button><button mat-flat-button color="primary" [disabled]="form.invalid || !files().length" (click)="save()"><mat-icon>upload</mat-icon>Hochladen</button></mat-dialog-actions>
  `
})
export class OrderUploadDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<OrderUploadDialogComponent>);
  readonly files = signal<File[]>([]);
  readonly siteSearch = signal('');
  readonly form = this.fb.group({ site_id: ['__auto__', Validators.required], category: ['bauakte'], name: [''] });
  constructor(@Inject(MAT_DIALOG_DATA) public data: OrderUploadDialogData) { if (data.sites[0]) this.form.patchValue({ site_id: data.sites[0]['id'] }); }
  effectiveSites(): GenericRecord[] {
    return this.data.sites.length ? this.data.sites : [{ id: '__auto__', site_no: 'Akte', name: this.data.project['name'] || 'Aktuelle Bauakte' }];
  }
  filteredEffectiveSites(): GenericRecord[] {
    const needle = String(this.siteSearch() || '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    if (!needle) return this.effectiveSites();
    return this.effectiveSites().filter((site) => String([site['site_no'], site['name'], site['address'], site['zip'], site['city']].join(' ')).toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').includes(needle));
  }
  onFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const selected = Array.from(input.files || []);
    this.files.set(selected);
    if (selected[0]) this.form.patchValue({ category: this.detectCategory(selected[0]) });
  }
  detectCategory(file: File): string {
    const name = file.name.toLowerCase();
    const mime = file.type.toLowerCase();
    if (mime.startsWith('image/')) return 'foto';
    if (mime.startsWith('video/')) return 'video';
    if (/angebot|offerte|quote/.test(name)) return 'angebot';
    if (/rechnung|invoice/.test(name)) return 'rechnung';
    if (/protokoll|pruef|prüf|qm|cm|mess|abnahme|erstbegehung/.test(name)) return 'qm';
    if (/plan|grundriss|zeichnung|skizze|vordruck/.test(name)) return 'vordruck';
    if (/kunde|vertrag|auftrag|beleg/.test(name)) return 'kundendokument';
    return 'bauakte';
  }
  mb(bytes: number): string { return bytes > 1024*1024 ? (bytes/1024/1024).toFixed(1)+' MB' : Math.max(1, Math.round(bytes/1024))+' KB'; }
  save(): void { this.ref.close({ ...this.form.getRawValue(), files: this.files() }); }
}

interface OrderAssignmentDialogData { project: GenericRecord; sites: GenericRecord[]; users: GenericRecord[]; }
@Component({
  selector: 'ew-order-assignment-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title><mat-icon>assignment_ind</mat-icon>Bauakte zuweisen</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid dialog-form">
        <mat-form-field class="wide" appearance="outline"><mat-label>Bauakte</mat-label><input matInput [value]="data.project['project_no'] + ' · ' + data.project['name']" disabled></mat-form-field>
        <mat-form-field class="wide" appearance="outline">
          <mat-label>Mitarbeiter *</mat-label>
          <mat-select formControlName="assigned_user_id" panelClass="ew-search-select-panel">
            @for (user of data.users; track user['id']) { <mat-option [value]="user['id']">{{ user['name'] }} @if (user['email']) { · {{ user['email'] }} }</mat-option> }
          </mat-select>
        </mat-form-field>
        <mat-form-field class="wide" appearance="outline"><mat-label>Notiz (optional)</mat-label><input matInput formControlName="note"></mat-form-field>      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Abbrechen</button><button mat-flat-button color="primary" [disabled]="form.invalid" (click)="save()"><mat-icon>person_add</mat-icon>Zuweisen</button></mat-dialog-actions>
  `
})
export class OrderAssignmentDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<OrderAssignmentDialogComponent>);
  readonly form = this.fb.group({ assigned_user_id: ['', Validators.required], note: [''] });
  constructor(@Inject(MAT_DIALOG_DATA) public data: OrderAssignmentDialogData) {}
  save(): void { this.ref.close(this.form.getRawValue()); }
}

type DailyEntry = { record: GenericRecord; dateValue: string; title: string; subtitle: string; typeLabel: string; type: 'photo' | 'video' | 'document' | 'report' };
type DailyGroup = { dateValue: string; dateLabel: string; entries: DailyEntry[]; photoCount: number; videoCount: number; docCount: number; reportCount: number };
type AttachmentEntry = { record: GenericRecord; moduleKey: 'documents' | 'photos'; type: 'document' | 'photo' | 'video'; typeLabel: string; icon: string; title: string; subtitle: string; category: string; createdAt: string; size: string };

interface ConfirmDialogData { title: string; message: string; confirmLabel: string; icon?: string; }

@Component({
  selector: 'ew-order-confirm-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title><mat-icon>{{ data.icon || 'help' }}</mat-icon>{{ data.title }}</h2>
    <mat-dialog-content><p>{{ data.message }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" class="confirm-accept" [mat-dialog-close]="true"><mat-icon>{{ data.icon || 'check' }}</mat-icon>{{ data.confirmLabel }}</button>
    </mat-dialog-actions>
  `
})
export class OrderConfirmDialogComponent {
  constructor(@Inject(MAT_DIALOG_DATA) public data: ConfirmDialogData) {}
}

interface PasswordConfirmData { title: string; message: string; confirmLabel: string; }
@Component({
  selector: 'ew-password-confirm-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title><mat-icon>warning</mat-icon>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
      <form [formGroup]="form" class="form-grid one dialog-form">
        <mat-form-field appearance="outline"><mat-label>Passwort bestätigen</mat-label><input matInput type="password" formControlName="password" autocomplete="current-password"></mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button class="danger-action" [disabled]="form.invalid" (click)="save()"><mat-icon>gpp_bad</mat-icon>{{ data.confirmLabel }}</button>
    </mat-dialog-actions>
  `
})
export class OrderPasswordConfirmDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<OrderPasswordConfirmDialogComponent>);
  readonly form = this.fb.group({ password: ['', Validators.required] });
  constructor(@Inject(MAT_DIALOG_DATA) public data: PasswordConfirmData) {}
  save(): void { this.ref.close(this.form.getRawValue().password); }
}

interface CancelDialogData { projectLabel: string; }
@Component({
  selector: 'ew-order-cancel-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title class="cancel-dialog-title"><mat-icon>gpp_bad</mat-icon>Bauakte stornieren</h2>
    <mat-dialog-content>
      <p class="cancel-lead">Die Bauakte <strong>{{ data.projectLabel }}</strong> wird storniert.</p>
      <div class="cancel-consequences">
        <mat-icon>info</mat-icon>
        <ul>
          <li>Verknüpfte Baustellen werden ebenfalls storniert und aus der aktiven Planung/Tageseinteilung entfernt.</li>
          <li>Die Akte bleibt zur Dokumentation erhalten und kann später wieder aktiviert werden.</li>
        </ul>
      </div>
      <form [formGroup]="form" class="cancel-form">
        <mat-form-field appearance="outline"><mat-label>Stornogrund</mat-label>
          <mat-select formControlName="reason">
            @for (r of reasons; track r) { <mat-option [value]="r">{{ r }}</mat-option> }
          </mat-select>
        </mat-form-field>
        @if (form.value.reason === 'Sonstiger Grund') {
          <mat-form-field appearance="outline"><mat-label>Grund beschreiben</mat-label><textarea matInput rows="2" formControlName="reason_text" placeholder="Kurze Begründung"></textarea></mat-form-field>
        } @else {
          <mat-form-field appearance="outline"><mat-label>Notiz (optional)</mat-label><textarea matInput rows="2" formControlName="reason_text" placeholder="Ergänzende Notiz zur Stornierung"></textarea></mat-form-field>
        }
        <mat-form-field appearance="outline"><mat-label>Passwort bestätigen</mat-label><input matInput type="password" formControlName="password" autocomplete="current-password"></mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button class="danger-action" [disabled]="!canConfirm()" (click)="confirm()"><mat-icon>gpp_bad</mat-icon>Stornieren</button>
    </mat-dialog-actions>
  `
})
export class OrderCancelDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<OrderCancelDialogComponent>);
  readonly reasons = ['Auftrag zurückgezogen', 'Kunde storniert', 'Doppelt angelegt', 'Angebot nicht angenommen', 'Termin/Verzögerung', 'Sonstiger Grund'];
  readonly form = this.fb.group({ reason: ['Auftrag zurückgezogen', Validators.required], reason_text: [''], password: ['', Validators.required] });
  constructor(@Inject(MAT_DIALOG_DATA) public data: CancelDialogData) {}
  canConfirm(): boolean {
    const v = this.form.getRawValue();
    if (!v.password) return false;
    if (v.reason === 'Sonstiger Grund' && !String(v.reason_text || '').trim()) return false;
    return true;
  }
  private fullReason(): string {
    const v = this.form.getRawValue();
    const base = v.reason === 'Sonstiger Grund' ? '' : String(v.reason || '');
    const extra = String(v.reason_text || '').trim();
    return [base, extra].filter(Boolean).join(' – ') || String(v.reason || '');
  }
  confirm(): void { if (!this.canConfirm()) return; this.ref.close({ password: this.form.getRawValue().password, reason: this.fullReason() }); }
}



@Component({
  standalone: true,
  imports: [DatePipe, NgClass, ReactiveFormsModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatSelectModule, MatTabsModule, MatTableModule, MatSnackBarModule, MatDialogModule, MatInputModule, MatSlideToggleModule, MatMenuModule, MatChipsModule, MatTooltipModule, MatCheckboxModule, MatProgressBarModule, MatProgressSpinnerModule, TextFieldModule],
  template: `
    <section class="page-head compact-page-title bauakte-head bauakte-redesign-head">
      <div class="bauakte-head-identity">
        <p class="eyebrow">Bauakte</p>
        @if (selectedProject(); as project) {
          <h1>{{ project['name'] || 'Bauakte' }}</h1>
          <p class="bauakte-head-sub">
            <span class="soft-pill">{{ project['project_no'] || 'ohne Nummer' }}</span>
            <span class="bauakte-head-customer">{{ project['customer_name'] || customerName(project['customer_id']) }}</span>
            @if (projectAddress(); as addr) { <span class="bauakte-head-addr"><mat-icon>location_on</mat-icon>{{ addr }}</span> }
            @if (customerPhone(); as phone) {
              <a class="bauakte-head-act" [href]="'tel:' + phone" matTooltip="Kunden anrufen">
                <mat-icon>call</mat-icon><span class="bha-text">Anrufen</span>
              </a>
            }
            @if (mapsUrl(); as url) {
              <a class="bauakte-head-act" [href]="url" target="_blank" rel="noopener" matTooltip="Navigation zur Baustelle">
                <mat-icon>directions</mat-icon><span class="bha-text">Route</span>
              </a>
            }
            @if (nextAppointmentLabel(); as next) {
              <button type="button" class="bauakte-head-next" (click)="activeTab.set(6)" matTooltip="Termine dieser Bauakte öffnen">
                <mat-icon>event</mat-icon>{{ next }}@if (nextAppointmentCrew(); as crew) { <span>· {{ crew }}</span> }
              </button>
            }
          </p>
          @if (!hasInitialInspection()) {
            <button type="button" class="inspection-warn-badge" (click)="activeTab.set(7)" aria-label="Erstbegehung öffnen">
              <mat-icon>warning_amber</mat-icon><span>Erstbegehung noch nicht ausgeführt</span><mat-icon class="iwb-go">chevron_right</mat-icon>
            </button>
          }
        } @else {
          <h1>Bauakte</h1>
        }
      </div>
      <div class="head-actions bauakte-head-actions">
        @if (selectedProject(); as project) {
          @if (canEditProject) {
            <button mat-flat-button class="bauakte-status-trigger" [ngClass]="'status-trigger-' + (project['status'] || 'new')" [matMenuTriggerFor]="statusMenu" aria-label="Status der Bauakte setzen">
              <mat-icon>flag</mat-icon><span class="status-dot"></span>{{ statusLabel(project['status']) }}<mat-icon class="status-caret">expand_more</mat-icon>
            </button>
          } @else {
            <span class="bauakte-status-readonly" [ngClass]="'status-trigger-' + (project['status'] || 'new')"><mat-icon>flag</mat-icon>{{ statusLabel(project['status']) }}</span>
          }
          <mat-menu #statusMenu="matMenu" class="bauakte-status-menu" xPosition="before">
            <div class="bauakte-status-menu-head">Status setzen</div>
            @for (step of statusSteps; track step.key) {
              <button mat-menu-item (click)="setStatus(project, step.key)">
                <span class="status-dot" [ngClass]="'status-dot-' + step.key"></span>
                <span class="status-menu-label">{{ step.label }}</span>
                @if ((project['status'] || 'new') === step.key) { <mat-icon class="status-check">check</mat-icon> }
              </button>
            }
          </mat-menu>
          @if (!isCrew && canEditProject && (project['status'] || 'new') !== 'completed') {
            <button mat-flat-button class="bauakte-complete-btn" (click)="completeOrder(project)"><mat-icon>check_circle</mat-icon> Abschließen</button>
          }
          @if (!isCrew && canEditProject) {
            <button mat-stroked-button class="bauakte-assign-btn" (click)="openAssignment()"><mat-icon>assignment_ind</mat-icon> {{ project['assigned_user_name'] || userName(project['assigned_user_id']) ? 'Zugewiesen: ' + (project['assigned_user_name'] || userName(project['assigned_user_id'])) : 'Mitarbeiter zuweisen' }}</button>
          } @else {
            <span class="bauakte-assign-readonly"><mat-icon>assignment_ind</mat-icon> {{ project['assigned_user_name'] || userName(project['assigned_user_id']) || 'Nicht zugewiesen' }}</span>
          }
          <button mat-stroked-button class="bauakte-actions-trigger" [matMenuTriggerFor]="actionsMenu" aria-label="Weitere Aktionen"><mat-icon>more_horiz</mat-icon><span class="actions-trigger-label">Aktionen</span></button>
          <mat-menu #actionsMenu="matMenu" class="bauakte-actions-menu" xPosition="before">
            @if (!isCrew && canPlanAppointments) { <button mat-menu-item (click)="openAppointment()"><mat-icon>event</mat-icon><span>Termin anlegen</span></button> }
            @if (canUploadFiles) { <button mat-menu-item (click)="openUpload()"><mat-icon>upload_file</mat-icon><span>Datei hochladen</span></button> }
            @if (!isCrew && canEditProject) { <button mat-menu-item (click)="openAssignment()"><mat-icon>assignment_ind</mat-icon><span>Mitarbeiter zuweisen</span></button> }
            <button mat-menu-item (click)="printFile()"><mat-icon>picture_as_pdf</mat-icon><span>PDF herunterladen</span></button>
            @if (!isCrew && canDestroyProject) {
              <button mat-menu-item class="menu-danger" (click)="cancelOrder(project)"><mat-icon>gpp_bad</mat-icon><span>Bauakte stornieren</span></button>
              <button mat-menu-item class="menu-danger" (click)="deleteOrder(project)"><mat-icon>delete_forever</mat-icon><span>Bauakte löschen</span></button>
            }
          </mat-menu>
        }
      </div>
    </section>

    @if (loading()) {
      <div class="bauakte-loading-shell">
        <mat-progress-bar mode="indeterminate"></mat-progress-bar>
        <div class="bauakte-loading-hint"><mat-icon>folder_open</mat-icon><span>Bauakte wird geladen …</span></div>
      </div>
    }

    @if (!loading() && selectedProject(); as project) {
      <main class="bauakte-rework-shell order-file-page">
        @if (isClosedProject()) {
          <div class="bauakte-closed-banner" role="status">
            <mat-icon>lock</mat-icon>
            <div>
              <b>Diese Bauakte ist {{ closedProjectLabel() }}.</b>
              <span>Sie sehen den vollständigen Stand der Akte. Änderungen sind nur nach erneutem Öffnen über den Status sinnvoll.</span>
            </div>
          </div>
        }
        @if (contextLoading()) { <mat-progress-bar class="bauakte-context-bar" mode="indeterminate"></mat-progress-bar> }
        @if (presence().length) {
          <mat-card class="square-card active-user-strip-card"><mat-icon>visibility</mat-icon><b>Aktuell in dieser Akte aktiv:</b>@for (user of presence(); track user['id']) { <span>{{ user['user_name'] }}{{ user['site_name'] ? ' · ' + user['site_name'] : '' }}</span> }</mat-card>
        }

        <!-- Angeheftete Anmerkungen ("Post-it").
             Sie stehen über allen Reitern, damit Wichtiges auf der Baustelle
             und im Büro sofort ins Auge fällt – ohne einen Reiter zu öffnen.
             Es sind gewöhnliche Notizen mit gesetztem Merkmal; die Liste im
             Reiter "Notizen" bleibt unverändert. -->
        <!-- Prominente Hinweise zur Baustelle: angeheftete Anmerkung und
             fachliche Warnungen stehen gemeinsam ganz oben. Vorher war nur
             die Anmerkung sichtbar; Dinge wie eine fehlende Erstbegehung
             oder überfällige Termine musste man sich zusammensuchen. -->
        @if (siteAlerts().length) {
          <section class="site-alerts" aria-label="Hinweise zur Baustelle">
            @for (a of siteAlerts(); track a.text) {
              <button type="button" class="site-alert" [ngClass]="'lv-' + a.level" (click)="activeTab.set(a.tab)">
                <mat-icon>{{ a.icon }}</mat-icon>
                <span>{{ a.text }}</span>
                <mat-icon class="site-alert-go">chevron_right</mat-icon>
              </button>
            }
          </section>
        }

        @if (pinnedNotes().length) {
          <section class="postits" aria-label="Angeheftete Anmerkungen">
            @for (n of pinnedNotes(); track n['id']) {
              <article class="postit" [ngClass]="'c-' + noteColor(n)" [class.is-due]="noteOverdue(n)">
                <p class="postit-body">{{ n['body'] }}</p>
                <footer class="postit-foot">
                  <span class="postit-meta">
                    {{ n['author_name'] || 'Unbekannt' }} · {{ n['created_at'] | date:'dd.MM.' }}
                    @if (noteDue(n); as due) { <b class="postit-due"><mat-icon>event</mat-icon>{{ due }}</b> }
                  </span>
                  @if (canEditProject) {
                    <span class="postit-actions">
                      <button type="button" [matMenuTriggerFor]="colorMenu" matTooltip="Farbe" aria-label="Farbe wählen"><mat-icon>palette</mat-icon></button>
                      <mat-menu #colorMenu="matMenu" xPosition="before">
                        @for (c of noteColors; track c.key) {
                          <button mat-menu-item (click)="setNoteColor(n, c.key)">
                            <span class="postit-swatch" [ngClass]="'c-' + c.key"></span><span>{{ c.label }}</span>
                          </button>
                        }
                      </mat-menu>
                      <button type="button" (click)="unpinNote(n)" matTooltip="Anheftung lösen" aria-label="Lösen"><mat-icon>push_pin</mat-icon></button>
                    </span>
                  }
                </footer>
              </article>
            }
          </section>
        }

        <nav class="mobile-bauakte-command-grid" aria-label="Schnellzugriff Bauakte">
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 0" (click)="activeTab.set(0)"><mat-icon>home_work</mat-icon><span>Übersicht</span></button>
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 1" (click)="activeTab.set(1)"><mat-icon>contacts</mat-icon><span>Kontakte</span></button>
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 2" (click)="activeTab.set(2)"><mat-icon>forum</mat-icon><span>Notizen</span>@if (pinnedNotes().length) { <em class="bauakte-cmd-badge is-pin"><mat-icon>push_pin</mat-icon></em> }</button>
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 3" (click)="activeTab.set(3)"><mat-icon>photo_library</mat-icon><span>Fotos</span>@if (photos().length) { <em class="bauakte-cmd-badge">{{ photos().length }}</em> }</button>
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 4" (click)="activeTab.set(4)"><mat-icon>movie</mat-icon><span>Videos</span>@if (videos().length) { <em class="bauakte-cmd-badge">{{ videos().length }}</em> }</button>
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 5" (click)="activeTab.set(5)"><mat-icon>folder</mat-icon><span>Dokumente</span>@if (documents().length) { <em class="bauakte-cmd-badge">{{ documents().length }}</em> }</button>
          <a class="bauakte-cmd" [routerLink]="['/tagesdokumentation']" [queryParams]="{ project_id: project['id'], site_id: primarySite()?.['id'] }"><mat-icon>edit_note</mat-icon><span>Tagesdoku</span></a>
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 7" (click)="activeTab.set(7)"><mat-icon>fact_check</mat-icon><span>Erstbegehung</span></button>
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 8" (click)="onTabChange(8)"><mat-icon>meeting_room</mat-icon><span>Räume</span>@if (rooms().length) { <em class="bauakte-cmd-badge">{{ rooms().length }}</em> }</button>
          <button type="button" class="bauakte-cmd" [class.is-active]="activeTab() === 9" (click)="activeTab.set(9)"><mat-icon>inventory_2</mat-icon><span>Material</span>@if (materialOrders().length) { <em class="bauakte-cmd-badge" [class.is-open]="openMaterialCount() > 0" [matTooltip]="openMaterialCount() ? openMaterialCount() + ' noch offen' : 'alle geliefert'">{{ materialOrders().length }}</em> }</button>
        </nav>

        <mat-tab-group class="bauakte-tabs bauakte-rework-tabs"
                       (touchstart)="onTabSwipeStart($event)" (touchend)="onTabSwipeEnd($event)" mat-stretch-tabs="false" animationDuration="0ms" [selectedIndex]="activeTab()" (selectedIndexChange)="onTabChange($event)">          <mat-tab label="Baustellenübersicht">
            <section class="bauakte-summary" aria-label="Eckdaten der Bauakte">
              <div class="summary-primary">
                <div class="summary-line">
                  <mat-icon>tag</mat-icon>
                  <div><small>Bauakte</small><b>{{ project['project_no'] || '—' }}</b></div>
                </div>
                <div class="summary-line">
                  <mat-icon>person</mat-icon>
                  <div><small>Kunde</small><b>{{ project['customer_name'] || customerName(project['customer_id']) }}</b></div>
                </div>
                <div class="summary-line">
                  <mat-icon>location_on</mat-icon>
                  <div><small>Baustelle</small><b>{{ projectAddress() }}</b></div>
                </div>
              </div>
              <div class="summary-metrics">
                <div class="metric"><small>Aufbau</small><b>{{ primarySite()?.['screed_type'] || primarySite()?.['insulation_type'] || '—' }}</b></div>
                <div class="metric"><small>Fläche</small><b>{{ primarySite()?.['area_m2'] ? (primarySite()?.['area_m2'] + ' m²') : '—' }}</b></div>
                <div class="metric"><small>Zugewiesen</small><b>{{ project['assigned_user_name'] || userName(project['assigned_user_id']) || 'offen' }}</b></div>
              </div>
            </section>
            <section class="bauakte-overview-grid">
              <mat-card class="square-card overview-card image-card gallery-card">
                <div class="card-headline"><h3>Objektbilder</h3><span>{{ photos().length }} Foto(s)</span></div>
                @if (photos().length) {
                  <div class="gallery-blur-wrap">
                    <img class="gallery-blur-img" [src]="photoUrl(photos()[0])" alt="Vorschau Baustellenfotos" aria-hidden="true">
                    <div class="gallery-blur-overlay">
                      <mat-icon>photo_library</mat-icon>
                      <b>{{ photos().length }} {{ photos().length === 1 ? 'Foto' : 'Fotos' }} hinterlegt</b>
                      <button type="button" mat-flat-button color="primary" (click)="activeTab.set(3)"><mat-icon>collections</mat-icon>Foto-Galerie öffnen</button>
                    </div>
                  </div>
                } @else {
                  <div class="empty-pane"><mat-icon>photo_camera</mat-icon><span>Noch kein Bild hochgeladen</span>@if (canUploadFiles) { <button mat-stroked-button (click)="openUpload('media')"><mat-icon>add_a_photo</mat-icon>Foto hinzufügen</button> }</div>
                }
              </mat-card>

              <mat-card class="square-card overview-card map-card">
                <div class="card-headline"><h3>Standort</h3><span>{{ primarySite()?.['zip'] || '' }} {{ primarySite()?.['city'] || '' }}</span>@if (mapUrl()) { <button mat-icon-button class="map-enlarge-btn" (click)="mapEnlarged.set(true)" aria-label="Karte vergrößern"><mat-icon>open_in_full</mat-icon></button> }</div>
                @if (mapUrl()) { <iframe [src]="mapUrl()" title="Standortkarte" loading="lazy"></iframe> } @else { <div class="empty-pane"><mat-icon>map</mat-icon><span>Keine Adresse hinterlegt</span></div> }
              </mat-card>

              <mat-card class="square-card overview-card summary-card details-card">
                <div class="card-headline">
                  <h3>Baustellendaten</h3>
                  @if (!detailsEdit()) {
                    @if (canEditProject) { <button mat-stroked-button (click)="startEditDetails()"><mat-icon>edit</mat-icon>Bearbeiten</button> }
                  } @else {
                    <span class="details-edit-actions"><button mat-button (click)="cancelEditDetails()">Abbrechen</button><button mat-flat-button color="primary" (click)="saveDetails(project)"><mat-icon>save</mat-icon>Speichern</button></span>
                  }
                </div>
                <form [formGroup]="detailsForm" class="details-form-grid structured-site-details readonly-details-grid">
                  <div class="details-section-title wide"><mat-icon>assignment</mat-icon>Basisdaten</div>
                  <mat-form-field appearance="outline"><mat-label>Bauakte</mat-label><input matInput [value]="project['project_no'] || '—'" readonly></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Kunde</mat-label><input matInput [value]="project['customer_name'] || customerName(project['customer_id'])" readonly></mat-form-field>
                  @if (detailsEdit()) {
                    <mat-form-field appearance="outline" class="date-pair-field"><mat-label>Beginn</mat-label><input matInput type="date" formControlName="start_date"></mat-form-field>
                    <mat-form-field appearance="outline" class="date-pair-field"><mat-label>Ende</mat-label><input matInput type="date" formControlName="end_date"></mat-form-field>
                  } @else {
                    <mat-form-field appearance="outline" class="date-pair-field"><mat-label>Beginn</mat-label><input matInput [value]="fmtDate(project['start_date'])" readonly></mat-form-field>
                    <mat-form-field appearance="outline" class="date-pair-field"><mat-label>Ende</mat-label><input matInput [value]="fmtDate(project['end_date'])" readonly></mat-form-field>
                  }
                  <mat-form-field appearance="outline"><mat-label>Status</mat-label><input matInput [value]="statusLabel(project['status'])" readonly></mat-form-field>
                  <div class="details-section-title wide"><mat-icon>place</mat-icon>Standort</div>
                  <mat-form-field appearance="outline" class="wide"><mat-label>Adresse</mat-label><input matInput formControlName="address" [readonly]="!detailsEdit()"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>PLZ</mat-label><input matInput formControlName="zip" [readonly]="!detailsEdit()"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Ort</mat-label><input matInput formControlName="city" [readonly]="!detailsEdit()"></mat-form-field>
                  <div class="details-section-title wide"><mat-icon>construction</mat-icon>Ausführung</div>
                  @if (detailsEdit()) {
                    <mat-form-field appearance="outline"><mat-label>Aufbau / Estrichart</mat-label>
                      <mat-select formControlName="screed_type">
                        <mat-option [value]="''">— bitte wählen —</mat-option>
                        @for (st of screedTypes(); track st['id']) { <mat-option [value]="st['name']">{{ st['name'] }}</mat-option> }
                        @if (screedTypeMissing()) { <mat-option [value]="detailsForm.get('screed_type')?.value">{{ detailsForm.get('screed_type')?.value }}</mat-option> }
                      </mat-select>
                    </mat-form-field>
                  } @else {
                    <mat-form-field appearance="outline"><mat-label>Aufbau / Estrichart</mat-label><input matInput formControlName="screed_type" [readonly]="true"></mat-form-field>
                  }
                  <mat-form-field appearance="outline"><mat-label>Fläche (m²)</mat-label><input matInput type="number" formControlName="area_m2" [readonly]="!detailsEdit()"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Telefon</mat-label><input matInput formControlName="phone" [readonly]="!detailsEdit()"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>E-Mail Baustelle</mat-label><input matInput type="email" formControlName="site_email" [readonly]="!detailsEdit()" placeholder="E-Mailadresse"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Ansprechpartner vor Ort</mat-label><input matInput formControlName="site_manager" [readonly]="!detailsEdit()"></mat-form-field>
                  <div class="details-section-title wide"><mat-icon>notes</mat-icon>Hinweise</div>
                  <mat-form-field appearance="outline" class="wide"><mat-label>Notiz</mat-label><textarea matInput rows="2" formControlName="notes" [readonly]="!detailsEdit()"></textarea></mat-form-field>
                  <mat-form-field appearance="outline" class="wide prominent-hinweis-field"><mat-label>Prominente Hinweise zur Baustelle</mat-label><textarea matInput rows="3" formControlName="hinweise" [readonly]="!detailsEdit()" placeholder="z. B. Zugang, Lagerfläche, Ansprechpartner, Risiken, Terminbesonderheiten"></textarea></mat-form-field>
                </form>
                @if (!primarySite() && detailsEdit()) { <p class="muted details-hint">Hinweis: Adress-/Aufbaudaten werden gespeichert, sobald eine Baustelle in der Akte existiert (wird beim ersten Upload automatisch angelegt).</p> }
              </mat-card>

              <mat-card class="square-card overview-card assignment-summary-card">
                <div class="card-headline"><h3>Zuweisung</h3>@if (!isCrew && canEditProject) { <button mat-stroked-button (click)="openAssignment()"><mat-icon>assignment_ind</mat-icon>Ändern</button> }</div>
                <div class="assignment-summary">
                  <mat-icon>badge</mat-icon>
                  <div>
                    <small>Zugewiesener Mitarbeiter</small>
                    <b>{{ project['assigned_user_name'] || userName(project['assigned_user_id']) || 'nicht zugewiesen' }}</b>
                    @if (project['assigned_user_id']) { <span>{{ assignedUserEmail(project['assigned_user_id']) }}</span> }
                  </div>
                </div>
              </mat-card>
            </section>
          </mat-tab>


          <mat-tab label="Ansprechpartner und Kontakte">
            <section class="contacts-grid material-contact-grid">
              <mat-card class="square-card contact-card wide-contact-card editable-contact-card">
                <div class="card-headline"><h3>Kunde</h3><span>Stammdaten</span></div>
                <form [formGroup]="contactForm" class="details-form-grid readonly-details-grid">
                  <mat-form-field appearance="outline"><mat-label>Name</mat-label><input matInput [value]="customerRecord()?.['name'] || project['customer_name'] || customerName(project['customer_id'])" readonly></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Ansprechpartner</mat-label><input matInput formControlName="contact_person" readonly></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>E-Mail</mat-label><input matInput formControlName="email" readonly></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Telefon</mat-label><input matInput formControlName="phone" readonly></mat-form-field>
                  <mat-form-field appearance="outline" class="wide"><mat-label>Adresse</mat-label><input matInput [value]="customerRecord()?.['address'] || '—'" readonly></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>PLZ</mat-label><input matInput [value]="customerRecord()?.['zip'] || '—'" readonly></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Ort</mat-label><input matInput [value]="customerRecord()?.['city'] || '—'" readonly></mat-form-field>
                </form>
              </mat-card>

              <mat-card class="square-card contact-card wide-contact-card editable-contact-card">
                <div class="card-headline"><h3>Baustellenkontakt</h3>@if (canEditProject) { @if (!contactEdit()) { <button mat-stroked-button (click)="startEditContacts()"><mat-icon>edit</mat-icon>Bearbeiten</button> } @else { <span class="details-edit-actions"><button mat-button (click)="cancelEditContacts()">Abbrechen</button><button mat-flat-button color="primary" (click)="saveContacts()"><mat-icon>save</mat-icon>Speichern</button></span> } }</div>
                <form [formGroup]="contactForm" class="details-form-grid readonly-details-grid">
                  <mat-form-field appearance="outline"><mat-label>Ansprechpartner vor Ort</mat-label><input matInput formControlName="site_manager" [readonly]="!contactEdit()"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Telefon</mat-label><input matInput formControlName="site_phone" [readonly]="!contactEdit()"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>E-Mail Baustellenkontakt</mat-label><input matInput formControlName="site_email" [readonly]="!contactEdit()"></mat-form-field>
                  <mat-form-field appearance="outline" class="wide"><mat-label>Adresse</mat-label><input matInput formControlName="site_address" [readonly]="!contactEdit()"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>PLZ</mat-label><input matInput formControlName="site_zip" [readonly]="!contactEdit()"></mat-form-field>
                  <mat-form-field appearance="outline"><mat-label>Ort</mat-label><input matInput formControlName="site_city" [readonly]="!contactEdit()"></mat-form-field>
                  <mat-form-field appearance="outline" class="wide"><mat-label>Hinweise</mat-label><textarea matInput rows="3" formControlName="hints" [readonly]="!contactEdit()"></textarea></mat-form-field>
                </form>
              </mat-card>

              <mat-card class="square-card contact-card team-card">
                <div class="card-headline"><h3>Zugewiesene Mitarbeiter</h3><span>{{ project['assigned_user_name'] || userName(project['assigned_user_id']) || 'nicht zugewiesen' }}</span></div>
                <div class="simple-list team-list">
                  @for (user of uniqueAssignedUsers(); track user['id']) {
                    <article><b>{{ user['name'] }}</b><span>{{ user['email'] || 'ohne E-Mail' }}</span><small>{{ userRoleLabel(user) }}</small></article>
                  } @empty { <p class="muted">Noch keine Mitarbeiter zugewiesen.</p> }
                </div>
              </mat-card>

              <mat-card class="square-card contact-card share-card">
                <div class="card-headline"><h3>Freigaben / externe Kontakte</h3></div>
                <div class="simple-list share-list">
                  @for (share of shares(); track share['id']) {
                    <article><b>{{ share['email'] || share['recipient_email'] || 'Freigabe' }}</b><span>{{ share['document_name'] || share['name'] || 'Dokumentfreigabe' }}</span><small>{{ share['created_at'] | date:'dd.MM.yyyy HH:mm' }}</small></article>
                  } @empty { <p class="muted">Keine Freigaben vorhanden.</p> }
                </div>
              </mat-card>
            </section>
          </mat-tab>

          <mat-tab label="Notizen">
            <section class="notes-tab">
              <mat-card class="square-card notes-card">
                <div class="card-headline"><h3>Notizen & Verlauf</h3><span>Chat-Verlauf inkl. Systemänderungen</span></div>

                <div class="notes-timeline">
                  @for (n of notes(); track n['id']) {
                    @if (n['entry_type'] === 'note') {
                      <article class="note-entry note-manual" [class.is-pinned]="isPinned(n)">
                        <div class="note-avatar">{{ noteInitials(n) }}</div>
                        <div class="note-bubble">
                          <header><b>{{ n['author_name'] || 'Mitarbeiter' }}</b><time>{{ n['created_at'] | date:'dd.MM.yyyy HH:mm' }}</time></header>
                          @if (n['body']) { <p>{{ n['body'] }}</p> }
                          @if (n['document_id']) {
                            @if (n['document_available']) {
                              <button type="button" class="note-attachment" (click)="openNoteDocument(n)"><mat-icon>description</mat-icon><span>{{ n['document_name'] || 'Dokument öffnen' }}</span></button>
                            } @else {
                              <div class="note-attachment note-attachment-deleted" title="Das angehängte Dokument wurde inzwischen gelöscht"><mat-icon>delete_outline</mat-icon><span>Angehängtes Dokument wurde gelöscht</span></div>
                            }
                          }
                          @if (canWriteNotes) {
                            <span class="note-actions">
                              <button mat-icon-button class="note-pin" [class.on]="isPinned(n)" (click)="togglePin(n)"
                                      [matTooltip]="isPinned(n) ? 'Anheftung lösen' : 'Oben anheften'" aria-label="Anheften"><mat-icon>push_pin</mat-icon></button>
                              <button mat-icon-button class="note-delete" (click)="deleteNote(n)" matTooltip="Notiz löschen" aria-label="Notiz löschen"><mat-icon>delete_outline</mat-icon></button>
                            </span>
                          }
                        </div>
                      </article>
                    } @else if (n['entry_type'] === 'call') {
                      <!-- Anruf als eigene Karte: Wer, worum, wann – auf einen
                           Blick. Vorher stand alles als Textblock mit
                           Zeilenumbrüchen in einer gewöhnlichen Notiz. -->
                      <article class="note-entry note-call" [ngClass]="'dir-' + callDirection(n)">
                        <span class="note-call-icon">
                          <mat-icon>{{ callDirection(n) === 'outbound' ? 'call_made' : 'call_received' }}</mat-icon>
                        </span>
                        <div class="note-call-main">
                          <div class="note-call-head">
                            <b>{{ callCaller(n) || 'Anruf' }}</b>
                            @if (callPhone(n); as ph) {
                              <a class="note-call-phone" [href]="'tel:' + ph" (click)="$event.stopPropagation()">{{ ph }}</a>
                            }
                            <span class="note-call-dir">{{ callDirection(n) === 'outbound' ? 'ausgehend' : 'eingehend' }}</span>
                            <time>{{ n['created_at'] | date:'dd.MM.yyyy HH:mm' }}</time>
                          </div>
                          @if (callSubject(n); as sub) { <p class="note-call-subject">{{ sub }}</p> }
                          @if (callText(n); as txt) { <p class="note-call-text">{{ txt }}</p> }
                          <div class="note-call-foot">
                            <span class="note-call-who"><mat-icon>badge</mat-icon>{{ n['author_name'] || 'Unbekannt' }}</span>
                            @if (callFollowUp(n)) { <span class="note-call-flag"><mat-icon>flag</mat-icon>Wiedervorlage</span> }
                          </div>
                        </div>
                      </article>
                    } @else {
                      <article class="note-entry note-system">
                        <span class="note-system-icon"><mat-icon>{{ noteSystemIcon(n) }}</mat-icon></span>
                        <div class="note-system-text"><span>{{ n['body'] }}</span><time>{{ n['created_at'] | date:'dd.MM.yyyy HH:mm' }}</time></div>
                      </article>
                    }
                  } @empty {
                    <div class="empty-pane"><mat-icon>forum</mat-icon><span>Noch keine Notizen oder Ereignisse.</span></div>
                  }
                </div>

                <div class="note-compose">
                  @if (noteFile(); as f) { <div class="note-file-chip"><mat-icon>description</mat-icon><span>{{ f.name }}</span><button mat-icon-button (click)="noteFile.set(null)" aria-label="Anhang entfernen"><mat-icon>close</mat-icon></button></div> }
                  <div class="note-compose-row">
                    <input #noteFileInput type="file" hidden (change)="onNoteFile($event)">
                    <mat-form-field appearance="outline" class="note-input">
                      <mat-label>Notiz schreiben…</mat-label>
                      <!-- Mehrzeilig: Enter macht einen Zeilenumbruch, gesendet
                           wird mit Strg+Enter oder über den Knopf. Vorher hat
                           Enter sofort abgeschickt – längere Notizen waren so
                           gar nicht möglich. -->
                      <textarea matInput rows="3" cdkTextareaAutosize cdkAutosizeMinRows="3" cdkAutosizeMaxRows="12"
                                [(ngModel)]="noteDraft"
                                (keydown.control.enter)="sendNote()"
                                (keydown.meta.enter)="sendNote()"
                                placeholder="Mehrere Zeilen möglich – Strg + Enter sendet"></textarea>
                    </mat-form-field>
                  </div>

                    <!-- Werkzeugzeile: Anhang und Anheften links, Senden rechts.
                         Vorher lagen alle vier Elemente in einer Reihe mit dem
                         Textfeld – am Handy war jedes davon zu klein. -->
                    <div class="note-compose-tools">
                      <button mat-icon-button class="note-attach" (click)="noteFileInput.click()" matTooltip="Dokument anhängen" aria-label="Dokument anhängen"><mat-icon>attach_file</mat-icon></button>
                      <button mat-icon-button class="note-pin-draft" [class.on]="notePinDraft()" (click)="notePinDraft.set(!notePinDraft())"
                              [matTooltip]="notePinDraft() ? 'Wird oben angeheftet' : 'Oben anheften'" aria-label="Anheften"><mat-icon>push_pin</mat-icon></button>
                      @if (notePinDraft()) {
                        <button mat-icon-button class="note-color-draft" [matMenuTriggerFor]="draftColors" matTooltip="Farbe" aria-label="Farbe">
                          <span class="postit-swatch" [ngClass]="'c-' + noteColorDraft()"></span>
                        </button>
                        <mat-menu #draftColors="matMenu" xPosition="before">
                          @for (c of noteColors; track c.key) {
                            <button mat-menu-item (click)="noteColorDraft.set(c.key)">
                              <span class="postit-swatch" [ngClass]="'c-' + c.key"></span><span>{{ c.label }}</span>
                            </button>
                          }
                        </mat-menu>
                      }
                      <span class="note-tools-spacer"></span>
                      <button mat-flat-button color="primary" class="note-send" [disabled]="noteSending() || (!noteDraft.trim() && !noteFile())" (click)="sendNote()">
                        <mat-icon>send</mat-icon>Senden
                      </button>
                    </div>
                  <p class="note-compose-hint">Ein angehängtes Dokument erscheint auch im Reiter „Dokumente".</p>
                </div>
              </mat-card>
            </section>
          </mat-tab>

                  <mat-tab label="Fotos">
            <section class="bauakte-fotos-shell">
              <div class="bauakte-fotos-bar">
                <div class="bauakte-fotos-count"><mat-icon>photo_library</mat-icon><b>{{ photos().length }}</b><span>{{ photos().length === 1 ? 'Foto' : 'Fotos' }} in dieser Bauakte</span></div>
                <div class="bauakte-fotos-actions">
                  @if (canUploadFiles) { <button mat-flat-button color="primary" (click)="openPhotoUpload()"><mat-icon>add_a_photo</mat-icon>Foto aufnehmen / hochladen</button> }
                </div>
              </div>
              @if (photos().length) {
                <div class="bauakte-photo-grid">
                  @for (photo of photos(); track photo['id']; let i = $index) {
                    <div class="bauakte-photo-tile-wrap">
                      <button type="button" class="bauakte-photo-tile" (click)="openLightbox(photo, i)">
                        <img [src]="photoUrl(photo, 'thumb')" loading="lazy" alt="Baustellenfoto">
                        @if (photo['caption']) { <span class="bauakte-photo-cap">{{ photo['caption'] }}</span> }
                      </button>
                      @if (!isCrew) {
                        <div class="bauakte-photo-tile-actions">
                          <button mat-mini-fab class="photo-tile-del" (click)="deleteAttachment('photos', photo, $event); $event.stopPropagation()" aria-label="Bild löschen"><mat-icon>delete</mat-icon></button>
                        </div>
                      }
                    </div>
                  }
                </div>
              } @else {
                <div class="empty-pane bauakte-fotos-empty">
                  <mat-icon>photo_camera</mat-icon>
                  <span>Noch keine Fotos zu dieser Baustelle erfasst.</span>
                  @if (canUploadFiles) { <button mat-flat-button color="primary" (click)="openPhotoUpload()"><mat-icon>add_a_photo</mat-icon>Erstes Foto hinzufügen</button> }
                </div>
              }
            </section>
          </mat-tab>

          <mat-tab label="Videos">
            <section class="bauakte-fotos-shell">
              <div class="bauakte-fotos-bar">
                <div class="bauakte-fotos-count"><mat-icon>movie</mat-icon><b>{{ videos().length }}</b><span>{{ videos().length === 1 ? 'Video' : 'Videos' }} in dieser Bauakte</span></div>
                <div class="bauakte-fotos-actions">
                  @if (canUploadFiles) { <button mat-flat-button color="primary" (click)="openVideoUpload()"><mat-icon>videocam</mat-icon>Video hochladen</button> }
                </div>
              </div>
              @if (videos().length) {
                <div class="bauakte-video-grid">
                  @for (video of videos(); track video['id']) {
                    <article class="bauakte-video-card">
                      <video class="bauakte-video-el" [src]="api.documentViewUrl(video['id'])" controls playsinline preload="metadata" controlslist="nodownload" disablepictureinpicture></video>
                      <div class="bauakte-video-meta">
                        <b>{{ video['name'] || video['original_name'] || 'Video' }}</b>
                        <small>{{ (video['created_at'] || video['updated_at']) | date:'dd.MM.yyyy HH:mm' }} · {{ fileSize(video['size_bytes']) }}</small>
                      </div>
                      <div class="bauakte-video-actions">
                        @if (canShareFiles) { <button mat-stroked-button class="share-btn" (click)="shareFile(video, $event)"><mat-icon>ios_share</mat-icon> Senden</button> }
                        @if (!isCrew) {
                          @if (canDeleteFiles) { <button mat-icon-button class="bauakte-video-del" (click)="deleteAttachment('documents', video, $event)" aria-label="Video löschen"><mat-icon>delete</mat-icon></button> }
                        }
                      </div>
                    </article>
                  }
                </div>
              } @else {
                <div class="empty-pane bauakte-fotos-empty">
                  <mat-icon>movie</mat-icon>
                  <span>Noch keine Videos zu dieser Baustelle erfasst.</span>
                  @if (canUploadFiles) { <button mat-flat-button color="primary" (click)="openVideoUpload()"><mat-icon>videocam</mat-icon>Erstes Video hochladen</button> }
                </div>
              }
            </section>
          </mat-tab>

          <mat-tab label="Dokumente">
            <!-- Bewusst klein und zurückhaltend: Der Hinweis muss dastehen,
                 soll aber nicht bei jedem Blick ins Auge springen. -->
            <p class="doc-notice">
              <mat-icon>warning</mat-icon>
              <span>Keine revisionssichere Ablage. Bitte alle Unterlagen zusätzlich im
              <b>Büro-Ordner</b> auf dem NAS führen.</span>
            </p>
            <section class="documents-redesign-shell">
              <div class="tab-action-row material-attachment-actions documents-redesign-actions">
                @if (canUploadFiles) { <button mat-flat-button color="primary" (click)="openUpload('document')"><mat-icon>upload_file</mat-icon>Dokument hochladen</button> }
              </div>

              <section class="documents-desktop-split">
                <mat-card class="square-card documents-table-card">
                  <div class="documents-table-head"><h3>Dokumente</h3><span>{{ documentFiles().length }}</span></div>
                  @if (documentEntries().length) {
                    <div class="responsive-table-wrap slim-table documents-table-wrap documents-desktop-table">
                      <table mat-table [dataSource]="documentEntries()" class="data-table pro-data-table documents-data-table">
                        <ng-container matColumnDef="open"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let item"><button mat-stroked-button class="open-row-button" (click)="openAttachment(item); $event.stopPropagation()"><mat-icon>visibility</mat-icon>Öffnen</button></td></ng-container>
                        <ng-container matColumnDef="type"><th mat-header-cell *matHeaderCellDef>Typ</th><td mat-cell *matCellDef="let item"><mat-icon>{{ item.icon }}</mat-icon><span>{{ item.typeLabel }}</span></td></ng-container>
                        <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>Name</th><td mat-cell *matCellDef="let item"><b>{{ item.title }}</b><small>{{ item.subtitle }}</small></td></ng-container>
                        <ng-container matColumnDef="category"><th mat-header-cell *matHeaderCellDef>Kategorie</th><td mat-cell *matCellDef="let item">{{ item.category }}</td></ng-container>
                        <ng-container matColumnDef="created"><th mat-header-cell *matHeaderCellDef>Datum</th><td mat-cell *matCellDef="let item">{{ item.createdAt | date:'dd.MM.yyyy HH:mm' }}</td></ng-container>
                        <ng-container matColumnDef="size"><th mat-header-cell *matHeaderCellDef>Größe</th><td mat-cell *matCellDef="let item">{{ item.size }}</td></ng-container>
                        <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let item"><button mat-icon-button class="share-inline-btn" (click)="shareFile(item.record, $event)" title="Senden" aria-label="Senden"><mat-icon>ios_share</mat-icon></button>@if (!isCrew && canDeleteFiles) {<button mat-icon-button class="delete-inline-btn" (click)="deleteAttachment(item.moduleKey, item.record, $event)" aria-label="Datei löschen"><mat-icon>delete</mat-icon></button>}</td></ng-container>
                        <tr mat-header-row *matHeaderRowDef="unifiedAttachmentColumns"></tr>
                        <tr mat-row *matRowDef="let row; columns: unifiedAttachmentColumns" class="clickable-row" [class.is-active]="isAttachmentSelected(row)" (click)="openAttachment(row)"></tr>
                      </table>
                    </div>

                    <!-- Handy: Karten statt breiter Tabelle, direkt öffnen -->
                    <div class="documents-mobile-list">
                      @for (item of documentEntries(); track item.record['id']) {
                        <article class="doc-mobile-card">
                          <!-- Der Zeilenkörper öffnet, die Aktionen liegen in
                               einer eigenen Zeile darunter. Vorher standen
                               Name und drei Schaltflächen nebeneinander –
                               der Name war dadurch abgeschnitten und die
                               Liste musste waagerecht scrollen. -->
                          <button type="button" class="doc-open" (click)="openAttachmentExternal(item)">
                            <span class="doc-mobile-icon"><mat-icon>{{ item.icon }}</mat-icon></span>
                            <span class="doc-mobile-main">
                              <b>{{ item.title }}</b>
                              <small>{{ item.category }} · {{ item.createdAt | date:'dd.MM.yyyy' }} · {{ item.size }}</small>
                            </span>
                            <mat-icon class="doc-mobile-chevron">open_in_new</mat-icon>
                          </button>
                          @if (canShareFiles || (!isCrew && canDeleteFiles)) {
                            <div class="doc-mobile-actions">
                              @if (canShareFiles) {
                                <button type="button" class="doc-act" (click)="shareFile(item.record, $event)">
                                  <mat-icon>ios_share</mat-icon>Senden
                                </button>
                              }
                              @if (!isCrew && canDeleteFiles) {
                                <button type="button" class="doc-act doc-act-del" (click)="deleteAttachment(item.moduleKey, item.record, $event)">
                                  <mat-icon>delete_outline</mat-icon>Löschen
                                </button>
                              }
                            </div>
                          }
                        </article>
                      }
                    </div>
                  } @else {
                    <div class="empty-pane documents-empty-pane"><mat-icon>folder_open</mat-icon><span>Noch keine Dokumente abgelegt.</span></div>
                  }
                </mat-card>

                <!-- Am Handy wird die Vorschau zur Vollbildebene. Sie ist dort
                     nur sichtbar, wenn wirklich etwas gewählt ist, und hat eine
                     eigene Kopfzeile mit Schließen-Knopf. Vorher gab es keinen
                     Weg zurück aus der PDF-Ansicht. -->
                <mat-card class="square-card viewer-card attachment-viewer-card documents-preview-card"
                          [class.is-open]="hasSelection()"
                          [class.is-fullscreen]="viewerFullscreen()">
                  <div class="card-headline">
                    <button type="button" class="doc-back" (click)="clearSelection()" aria-label="Zurück zur Liste">
                      <mat-icon>arrow_back</mat-icon><span>Dokumente</span>
                    </button>
                    @if (viewerFullscreen()) {
                      <button type="button" class="doc-back doc-back-exit" (click)="viewerFullscreen.set(false)" aria-label="Vollbild verlassen">
                        <mat-icon>close_fullscreen</mat-icon><span>Vollbild verlassen</span>
                      </button>
                    }
                    <h3>Vorschau</h3>
                    <span class="viewer-actions">
                      @if (hasSelection()) {
                        <button mat-stroked-button (click)="openSelectedLarge()"><mat-icon>open_in_full</mat-icon>Groß öffnen</button>
                        <button mat-stroked-button (click)="downloadSelected()"><mat-icon>download</mat-icon>Herunterladen</button>
                      }
                    </span>
                  </div>

                  <!-- Handy: feste Leiste am unteren Rand. Senden zum Weiterleiten
                       oder Drucken über das Gerät, Schließen als eindeutiger Weg
                       zurück – beides mit dem Daumen erreichbar. -->
                  @if (hasSelection()) {
                    <div class="doc-mobile-bar">
                      @if (canShareFiles && selectedDoc()) {
                        <button type="button" class="doc-bar-btn" (click)="shareFile(selectedDoc()!, $event)">
                          <mat-icon>ios_share</mat-icon>Senden
                        </button>
                      }
                      <button type="button" class="doc-bar-btn" (click)="downloadSelected()">
                        <mat-icon>download</mat-icon>Speichern
                      </button>
                      <button type="button" class="doc-bar-btn doc-bar-close" (click)="clearSelection()">
                        <mat-icon>close</mat-icon>Schließen
                      </button>
                    </div>
                  }
                  <div class="viewer-panel standalone-viewer documents-preview-panel">
                    @if (photoViewerUrl()) { <img class="document-image-preview" [src]="photoViewerUrl()" alt="Bildansicht"> }
                    @else if (rawViewerUrl()) {
                      @switch (selectedKind()) {
                        @case ('pdf') { <iframe [src]="safeViewerUrl()" title="PDF-Vorschau"></iframe> }
                        @case ('video') { <video [src]="rawViewerUrl()" controls preload="metadata"></video> }
                        @case ('image') { <img class="document-image-preview" [src]="rawViewerUrl()" alt="Bildansicht"> }
                        @case ('word') { @if (wordLoading()) { <div class="word-preview-loading"><mat-spinner diameter="22"></mat-spinner><span>Word-Vorschau wird erstellt…</span></div> } @else if (wordHtml()) { <div class="word-preview" [innerHTML]="wordHtml()"></div> } @else { <div class="preview-fallback"><mat-icon>description</mat-icon><b>{{ selectedName() }}</b><span>Für dieses Word-Dokument konnte keine Vorschau erstellt werden.</span><div class="preview-fallback-actions"><button mat-flat-button color="primary" (click)="openSelectedLarge()"><mat-icon>open_in_new</mat-icon>In neuem Tab öffnen</button><button mat-stroked-button (click)="downloadSelected()"><mat-icon>download</mat-icon>Herunterladen</button></div></div> } }
                        @default { <div class="preview-fallback"><mat-icon>{{ selectedIsExcel() ? 'table_chart' : 'insert_drive_file' }}</mat-icon><b>{{ selectedName() }}</b><span>{{ selectedKindLabel() }} – dieses Format kann der Browser nicht direkt anzeigen.</span><div class="preview-fallback-actions"><button mat-flat-button color="primary" (click)="openSelectedLarge()"><mat-icon>open_in_new</mat-icon>In neuem Tab öffnen</button><button mat-stroked-button (click)="downloadSelected()"><mat-icon>download</mat-icon>Herunterladen</button></div></div> }
                      }
                    }
                    @else { <div class="empty-viewer"><mat-icon>folder_open</mat-icon><b>Keine Datei ausgewählt</b></div> }
                  </div>
                </mat-card>
              </section>
            </section>
          </mat-tab>

          <mat-tab label="Tagesübersicht">
            <section class="tagesuebersicht-shell">
              <div class="tab-action-row">
                @if (canWriteReports) { <a mat-flat-button color="primary" routerLink="/tagesdokumentation" [queryParams]="{ project_id: project['id'], site_id: primarySite()?.['id'] }"><mat-icon>edit_note</mat-icon>Tagesbericht erfassen</a> }
                @if (canUploadFiles) { <button mat-stroked-button (click)="openUpload('media')"><mat-icon>add_a_photo</mat-icon>Fotos / Videos hinzufügen</button> }
              </div>

              <section class="daily-report-layout tagesuebersicht-layout">
                <mat-card class="square-card report-list-card tagesuebersicht-timeline-card">
                  <div class="card-headline"><h3>Tageschronik</h3><span>Nach Tagen gruppiert · neueste zuerst</span></div>
                  <div class="tagesuebersicht-timeline">
                    @for (day of dailyGroups(); track day.dateValue) {
                      <section class="tag-group">
                        <header class="tag-group-head">
                          <div class="tag-group-date">
                            <span class="tag-group-weekday">{{ day.dateValue | date:'EEEE' }}</span>
                            <strong>{{ day.dateValue | date:'dd.MM.yyyy' }}</strong>
                          </div>
                          <span class="tag-group-count">
                            @if (day.photoCount) { <span><mat-icon>photo_camera</mat-icon>{{ day.photoCount }}</span> }
                            @if (day.videoCount) { <span><mat-icon>movie</mat-icon>{{ day.videoCount }}</span> }
                            @if (day.docCount) { <span><mat-icon>description</mat-icon>{{ day.docCount }}</span> }
                            @if (day.reportCount) { <span><mat-icon>edit_note</mat-icon>{{ day.reportCount }}</span> }
                          </span>
                        </header>
                        <div class="tag-group-entries">
                          @for (entry of day.entries; track entry.record['id']) {
                            <button type="button" class="report-entry" [class.is-active]="selectedDoc()?.['id'] === entry.record['id']" (click)="openDailyEntry(entry)">
                              <span class="report-entry-icon"><mat-icon>{{ entry.type === 'photo' ? 'image' : entry.type === 'video' ? 'movie' : entry.type === 'report' ? 'edit_note' : 'description' }}</mat-icon></span>
                              <span class="report-entry-main"><b>{{ entry.title }}</b><small>{{ entry.subtitle }}</small></span>
                              <span class="report-entry-type">{{ entry.typeLabel }}</span>
                            </button>
                          }
                        </div>
                      </section>
                    } @empty {
                      <div class="empty-pane"><mat-icon>fact_check</mat-icon><span>Noch keine Tagesberichte oder Medien vorhanden.</span></div>
                    }
                  </div>
                </mat-card>
                <mat-card class="square-card viewer-card">
                  <div class="card-headline"><h3>Vorschau</h3><span class="viewer-actions">@if (hasSelection()) { <button mat-stroked-button (click)="openSelectedLarge()"><mat-icon>open_in_full</mat-icon>Groß öffnen</button><button mat-stroked-button (click)="downloadSelected()"><mat-icon>download</mat-icon>Herunterladen</button> }</span></div>
                  <div class="viewer-panel standalone-viewer">
                    @if (selectedReport(); as report) { <div class="daily-report-preview professional-daily-text"><header><span>Tagesbericht</span><h3>{{ reportDate(report) }}</h3></header><dl><div><dt>Kolonne / Mitarbeiter</dt><dd>{{ reportField(report, 'crew_text') || '—' }}</dd></div><div><dt>Arbeitszeit</dt><dd>{{ reportField(report, 'time_text') || '—' }}</dd></div><div><dt>Wetter</dt><dd>{{ reportField(report, 'weather') || '—' }}</dd></div></dl><section><h4>Ausgeführte Arbeiten</h4><p>{{ reportField(report, 'work_done') || '—' }}</p></section><section><h4>Hinweise / Behinderungen</h4><p>{{ reportField(report, 'notes') || '—' }}</p></section></div> }
                    @else if (photoViewerUrl()) { <img class="document-image-preview" [src]="photoViewerUrl()" alt="Vorschau"> }
                    @else if (rawViewerUrl()) {
                      @switch (selectedKind()) {
                        @case ('pdf') { <iframe [src]="safeViewerUrl()" title="PDF-Vorschau"></iframe> }
                        @case ('video') { <video [src]="rawViewerUrl()" controls preload="metadata"></video> }
                        @case ('image') { <img class="document-image-preview" [src]="rawViewerUrl()" alt="Bildvorschau"> }
                        @case ('word') { @if (wordLoading()) { <div class="word-preview-loading"><mat-spinner diameter="22"></mat-spinner><span>Word-Vorschau wird erstellt…</span></div> } @else if (wordHtml()) { <div class="word-preview" [innerHTML]="wordHtml()"></div> } @else { <div class="preview-fallback"><mat-icon>description</mat-icon><b>{{ selectedName() }}</b><span>Für dieses Word-Dokument konnte keine Vorschau erstellt werden.</span><div class="preview-fallback-actions"><button mat-flat-button color="primary" (click)="openSelectedLarge()"><mat-icon>open_in_new</mat-icon>In neuem Tab öffnen</button><button mat-stroked-button (click)="downloadSelected()"><mat-icon>download</mat-icon>Herunterladen</button></div></div> } }
                        @default { <div class="preview-fallback"><mat-icon>{{ selectedIsExcel() ? 'table_chart' : 'insert_drive_file' }}</mat-icon><b>{{ selectedName() }}</b><span>{{ selectedKindLabel() }} – dieses Format kann der Browser nicht direkt anzeigen.</span><div class="preview-fallback-actions"><button mat-flat-button color="primary" (click)="openSelectedLarge()"><mat-icon>open_in_new</mat-icon>In neuem Tab öffnen</button><button mat-stroked-button (click)="downloadSelected()"><mat-icon>download</mat-icon>Herunterladen</button></div></div> }
                      }
                    }
                    @else { <div class="empty-viewer"><mat-icon>folder_open</mat-icon><b>Eintrag auswählen</b></div> }
                  </div>
                </mat-card>
              </section>
            </section>
          </mat-tab>

          <mat-tab label="Erstbegehung">
            <section class="initial-inspection-tab">
              @if (initialInspection(); as inspection) {
                <mat-card class="square-card initial-inspection-card inspection-detail-card">
                  <div class="card-headline inspection-detail-headline">
                    <div><h3>Erstbegehungsprotokoll</h3><span>Erfasst am {{ inspection['created_at'] | date:'dd.MM.yyyy HH:mm' }}</span></div>
                    <div class="inspection-detail-actions">
                      <button mat-stroked-button (click)="printInspectionPdf(inspection)"><mat-icon>picture_as_pdf</mat-icon>PDF</button>
                      @if (canWriteInspections) { <a mat-flat-button color="primary" routerLink="/erstbegehung-wizard" [queryParams]="{ project_id: project['id'], site_id: primarySite()?.['id'] }"><mat-icon>edit</mat-icon>Bearbeiten</a> }
                      @if (isInspectionRejected(inspection)) {
                        @if (canWriteInspections) { <a mat-flat-button class="second-inspection-btn" routerLink="/erstbegehung-wizard" [queryParams]="{ project_id: project['id'], site_id: primarySite()?.['id'], second: 2 }"><mat-icon>restart_alt</mat-icon>Zweitbegehung durchführen</a> }
                      }
                    </div>
                  </div>

                  <section class="inspection-detail-section">
                    <h4>Wesentliche Angaben</h4>
                    <div class="inspection-detail-grid">
                      <div class="insp-field"><small>Ergebnis</small><b>{{ inspectionResultLabel(inspection) }}</b></div>
                      <div class="insp-field"><small>Status</small><b>{{ inspectionStatusLabel(inspection['status']) }}</b></div>
                      <div class="insp-field"><small>Prüfer</small><b>{{ inspection['signed_by'] || inspectionField(inspection, 'context.inspector_name') || '—' }}</b></div>
                      <div class="insp-field"><small>Datum</small><b>{{ (inspectionField(inspection, 'context.inspection_date') || inspection['signed_at'] || inspection['created_at']) | date:'dd.MM.yyyy' }}</b></div>
                    </div>
                  </section>

                  <section class="inspection-detail-section">
                    <h4>Untergrund & Aufbau</h4>
                    <div class="inspection-detail-grid">
                      <div class="insp-field"><small>Estrichart</small><b>{{ inspectionField(inspection, 'checklist.screed_type') || primarySite()?.['screed_type'] || '—' }}</b></div>
                      <div class="insp-field"><small>Fläche</small><b>{{ inspectionField(inspection, 'checklist.exact_area_m2') || inspectionField(inspection, 'measurements.exact_area_m2') || primarySite()?.['area_m2'] || '—' }} m²</b></div>
                      <div class="insp-field"><small>Fußbodenheizung</small><b>{{ yesNoLabel(inspectionField(inspection, 'checklist.floor_heating') ?? inspectionField(inspection, 'checklist.underfloor_heating')) }}</b></div>
                      <div class="insp-field"><small>Untergrund</small><b>{{ substrateStatusLabel(inspectionField(inspection, 'checklist.substrate_status')) }}</b></div>
                      <div class="insp-field"><small>Meterriss / Höhen geprüft</small><b>{{ yesNoLabel(inspectionField(inspection, 'checklist.height_checked')) }}</b></div>
                      <div class="insp-field"><small>Baustelle zugänglich</small><b>{{ yesNoLabel(inspectionField(inspection, 'checklist.access_ok')) }}</b></div>
                    </div>
                  </section>

                  <section class="inspection-detail-section">
                    <h4>Messwerte</h4>
                    <div class="inspection-detail-grid">
                      <div class="insp-field"><small>CM / Feuchte</small><b>{{ inspectionField(inspection, 'measurements.cm_percent') || '—' }}</b></div>
                      <div class="insp-field"><small>Temperatur</small><b>{{ inspectionField(inspection, 'measurements.temperature_c') ? inspectionField(inspection, 'measurements.temperature_c') + ' °C' : '—' }}</b></div>
                      <div class="insp-field"><small>Luftfeuchte</small><b>{{ inspectionField(inspection, 'measurements.humidity_percent') ? inspectionField(inspection, 'measurements.humidity_percent') + ' %' : '—' }}</b></div>
                      <div class="insp-field"><small>Ebenheit / Hinweis</small><b>{{ inspectionField(inspection, 'measurements.level_note') || '—' }}</b></div>
                    </div>
                  </section>

                  <section class="inspection-detail-section">
                    <h4>Bemerkungen</h4>
                    <div class="inspection-detail-notes">
                      <div class="insp-note"><small>Bemerkungen / Mängel</small><p>{{ inspectionField(inspection, 'checklist.notes') || inspectionField(inspection, 'notes') || '—' }}</p></div>
                      <div class="insp-note"><small>Notizen zur Baustelle</small><p>{{ inspection['notes'] || inspectionField(inspection, 'checklist.site_notes') || inspectionField(inspection, 'site_notes') || inspectionField(inspection, 'rejection_reason') || '—' }}</p></div>
                    </div>
                  </section>

                  @if (inspectionPhotoIds(inspection).length) {
                    <section class="inspection-detail-section">
                      <h4>Fotodokumentation ({{ inspectionPhotoIds(inspection).length }})</h4>
                      <div class="inspection-media-strip">
                        @for (id of inspectionPhotoIds(inspection); track id) {
                          <button type="button" (click)="openLightboxById(id)"><img [src]="photoUrlById(id, 'thumb')" alt="Erstbegehungsfoto"></button>
                        }
                      </div>
                    </section>
                  }
                </mat-card>
              } @else {
                <mat-card class="square-card initial-inspection-empty">
                  <mat-icon>fact_check</mat-icon>
                  <h3>Erstbegehung noch nicht ausgeführt</h3>
                  <div class="inspection-empty-actions">@if (canWriteInspections) { <a mat-flat-button color="primary" routerLink="/erstbegehung-wizard" [queryParams]="{ project_id: project['id'], site_id: primarySite()?.['id'] }"><mat-icon>play_arrow</mat-icon>Erstbegehung ausführen</a> }@if (canWriteInspections) { <button mat-stroked-button (click)="markInitialInspectionNotNeeded()"><mat-icon>do_not_disturb_on</mat-icon>Keine Erstbegehung notwendig</button> }</div>
                </mat-card>
              }
            </section>
          </mat-tab>

          <!-- ===================================================================
               Räume und Flächen
               Die fachliche Grundlage jeder Estrichbaustelle: Was wird wo
               eingebaut, mit welcher Art, welcher Dicke, wie weit ist es.
               Die Tabelle rooms gab es bereits als Stammdatenmodul – in der
               Bauakte war sie nicht erreichbar. Hier ist sie am Ort des
               Geschehens: am Handy als Liste zum Antippen, im Büro als
               Aufstellung mit Summen.
               =================================================================== -->
          <mat-tab label="Räume &amp; Flächen">
            <section class="rooms-tab">
              <mat-card class="square-card rooms-card">
                <div class="rooms-head">
                  <div class="rooms-sum">
                    <span><b>{{ roomsTotalArea() }}</b><small>m² in Räumen</small></span>
                    <span><b>{{ rooms().length }}</b><small>Räume</small></span>
                    <span><b>{{ roomsDoneArea() }}</b><small>m² fertig</small></span>
                    <span><b>{{ roomsProgress() }} %</b><small>Fortschritt</small></span>
                  </div>
                  @if (canEditProject) {
                    <button mat-flat-button color="primary" (click)="openRoom(null)"><mat-icon>add</mat-icon>Raum anlegen</button>
                  }
                </div>

                @if (roomsProgress() > 0) {
                  <div class="rooms-bar"><i [style.width.%]="roomsProgress()"></i></div>
                }

                <!-- Abgleich mit der Baustellenfläche. Beide Wege sind zulässig:
                     Wer ohne Räume arbeitet, pflegt nur die Gesamtfläche. -->
                @if (areaMismatch(); as diff) {
                  <p class="rooms-hint">
                    <mat-icon>info</mat-icon>
                    <span>Für die Baustelle sind <b>{{ siteAreaLabel() }} m²</b> hinterlegt,
                    die Räume ergeben <b>{{ roomsTotalArea() }} m²</b> ({{ diff }}). Das ist
                    kein Fehler – ergänzen Sie Räume nur, wenn es Ihnen hilft.</span>
                  </p>
                }

                <!-- Aufteilung nach Estrichart – die Zahl, die für Material
                     und Angebot gebraucht wird. -->
                @if (roomsByType().length > 1) {
                  <div class="rooms-types">
                    @for (t of roomsByType(); track t.type) {
                      <span class="rooms-type"><b>{{ t.area }} m²</b>{{ t.type }}</span>
                    }
                  </div>
                }

                @if (rooms().length) {
                  <!-- Nach Geschossen gegliedert. Eine durchlaufende Liste
                       wird ab etwa acht Räumen unübersichtlich; auf der
                       Baustelle arbeitet man ohnehin geschossweise. -->
                  @for (group of roomGroups(); track group.floor) {
                    <div class="rooms-group">
                      <h4 class="rooms-group-head">
                        <mat-icon>stairs</mat-icon>
                        <span>{{ group.floor }}</span>
                        <em>{{ group.rooms.length }} Räume · {{ group.area }} m²</em>
                      </h4>
                  <div class="rooms-list">
                    @for (r of group.rooms; track r['id']) {
                      <article class="room" [ngClass]="'st-' + roomStatusKey(r)"
                               (click)="canEditProject ? openRoom(r) : null"
                               [class.is-clickable]="canEditProject">
                        <span class="room-rail"></span>
                        <div class="room-main">
                          <div class="room-line1">
                            <b>{{ r['name'] || 'Raum' }}</b>
                            @if (r['floor']) { <span class="room-floor">{{ r['floor'] }}</span> }
                            @if (r['heating']) { <span class="room-heat" matTooltip="Heizestrich"><mat-icon>whatshot</mat-icon></span> }
                          </div>
                          <div class="room-line2">
                            @if (r['area_m2']) { <span><mat-icon>square_foot</mat-icon>{{ r['area_m2'] }} m²</span> }
                            @if (r['screed_type']) { <span><mat-icon>layers</mat-icon>{{ r['screed_type'] }}</span> }
                            @if (r['height_mm']) { <span><mat-icon>height</mat-icon>{{ r['height_mm'] }} mm</span> }
                          </div>
                          @if (r['notes']) { <p class="room-note">{{ r['notes'] }}</p> }
                        </div>
                        <span class="room-status">{{ roomStatusLabel(r) }}</span>
                        @if (canEditProject) {
                          <span class="room-actions" (click)="$event.stopPropagation()">
                            <button mat-icon-button [matMenuTriggerFor]="roomStatusMenu" matTooltip="Stand ändern" aria-label="Stand"><mat-icon>flag</mat-icon></button>
                            <mat-menu #roomStatusMenu="matMenu" xPosition="before">
                              @for (st of roomStates; track st.key) {
                                <button mat-menu-item (click)="setRoomStatus(r, st.key)"><span class="room-dot" [ngClass]="'st-' + st.key"></span><span>{{ st.label }}</span></button>
                              }
                            </mat-menu>
                            <button mat-icon-button (click)="deleteRoom(r)" matTooltip="Raum löschen" aria-label="Löschen"><mat-icon>delete_outline</mat-icon></button>
                          </span>
                        }
                      </article>
                    }
                  </div>
                    </div>
                  }
                } @else {
                  <div class="rooms-empty">
                    <mat-icon>meeting_room</mat-icon>
                    <p>
                      <b>Freiwillig.</b> Die Bauakte funktioniert vollständig ohne Räume –
                      Fotos, Dokumente, Termine, Material und Erstbegehung bleiben unberührt.
                    </p>
                    <p class="rooms-empty-hint">
                      Wer die Räume erfasst, bekommt zusätzlich: Flächensumme je Estrichart,
                      Fortschritt in Prozent und eine Grundlage fürs Aufmaß. Für eine kleine
                      Baustelle mit einer Fläche lohnt sich das meist nicht.
                    </p>
                    @if (canEditProject) { <button mat-stroked-button (click)="openRoom(null)"><mat-icon>add</mat-icon>Raum anlegen</button> }
                  </div>
                }
              </mat-card>
            </section>
          </mat-tab>

          <mat-tab label="Material">
            <section class="material-tab">
              <mat-card class="square-card material-tab-card">
                <div class="card-headline"><div><h3>Material in Bauakte</h3><span>Bestellungen mit Status und Lieferant</span></div>@if (canOrderMaterial) { <button mat-flat-button color="primary" (click)="openMaterialOrder()"><mat-icon>add</mat-icon>Materialbestellung</button> }</div>
                <div class="material-order-list">
                  @for (order of materialOrders(); track order['id']) {
                    <article class="material-order-card">
                      <header><div><strong>{{ order['order_no'] || 'Bestellung' }}</strong><span>{{ order['supplier_name'] || 'Ohne Lieferant' }}</span></div><em class="material-status">{{ materialStatusLabel(order['status']) }}</em></header>
                      @if (order['requested_delivery_date']) { <p><mat-icon>event</mat-icon>Lieferwunsch: {{ order['requested_delivery_date'] | date:'dd.MM.yyyy' }}</p> }
                      @if (order['delivery_address']) { <p><mat-icon>place</mat-icon>{{ order['delivery_address'] }}</p> }
                      <div class="material-items-preview">{{ materialItemPreview(order) }}</div>
                      <div class="material-actions">
                        @if (canOrderMaterial) { <button mat-stroked-button (click)="openMaterialOrder(order)"><mat-icon>edit</mat-icon>Bearbeiten</button> }
                        <button mat-stroked-button (click)="downloadMaterialPdf(order)"><mat-icon>picture_as_pdf</mat-icon>PDF</button>
                        @if (canDeleteMaterial) { <button mat-icon-button color="warn" (click)="deleteMaterialOrder(order)" aria-label="Materialbestellung löschen"><mat-icon>delete</mat-icon></button> }
                      </div>
                    </article>
                  } @empty {
                    <p class="call-empty">Noch keine Materialbestellungen in dieser Bauakte.</p>
                  }
                </div>
              </mat-card>
            </section>
          </mat-tab>


</mat-tab-group>

        <!-- ===================================================================
             Dokumentansicht am Handy – bewusst AUSSERHALB der Reiter.
             Die Material-Reiter setzen auf ihren Inhaltsbereich ein
             ein transform. Ein Element mit position: fixed bezieht sich dann
             nicht mehr auf den Bildschirm, sondern auf diesen Bereich – die
             Vorschau blieb deshalb in der Seite stehen statt sie zu füllen.
             Hier, außerhalb der Reiter, gibt es keinen solchen Elternteil.
             =================================================================== -->
        <!-- Nur wenn ein Dokument AUSDRÜCKLICH angetippt wurde.
             Vorher hing die Ansicht an hasSelection() – dieses Merkmal wird
             aber auch von anderen Abläufen gesetzt (Foto wählen, Anhang aus
             einer Notiz, Vorauswahl beim Laden der Akte). Dadurch ging die
             Vollbildansicht bei Aktionen auf, die damit nichts zu tun hatten. -->
        @if (docSheetOpen() && hasSelection()) {
          <div class="doc-sheet" role="dialog" aria-modal="true" aria-label="Dokumentansicht">
            <header class="doc-sheet-head">
              <button type="button" class="doc-sheet-back" (click)="clearSelection()">
                <mat-icon>arrow_back</mat-icon><span>Dokumente</span>
              </button>
              <span class="doc-sheet-title">{{ selectedDocName() }}</span>
            </header>

            <div class="doc-sheet-body">
              @switch (viewerKind()) {
                @case ('pdf') { <iframe [src]="safeViewerUrl()" title="Dokumentansicht"></iframe> }
                @case ('image') { <img [src]="photoViewerUrl() || rawViewerUrl()" alt="Vorschau"> }
                @case ('video') { <video [src]="rawViewerUrl()" controls playsinline></video> }
                @default {
                  <div class="doc-sheet-none">
                    <mat-icon>description</mat-icon>
                    <p>Diese Datei lässt sich nicht direkt anzeigen.</p>
                    <button mat-flat-button color="primary" (click)="downloadSelected()">
                      <mat-icon>download</mat-icon>Öffnen bzw. speichern
                    </button>
                  </div>
                }
              }
            </div>

            <footer class="doc-sheet-bar">
              @if (canShareFiles && selectedDoc()) {
                <button type="button" (click)="shareFile(selectedDoc()!, $event)"><mat-icon>ios_share</mat-icon>Senden</button>
              }
              <button type="button" (click)="downloadSelected()"><mat-icon>download</mat-icon>Speichern</button>
              <button type="button" class="doc-sheet-close" (click)="clearSelection()"><mat-icon>close</mat-icon>Schließen</button>
            </footer>
          </div>
        }
      </main>
    } @else if (!loading()) {
      <mat-card class="square-card bauakte-empty-card"><mat-icon>folder_off</mat-icon><p class="muted">Keine Bauakte gefunden.</p></mat-card>
    }

    @if (selectedProject()) {
      <div class="bauakte-fab-wrap">
        @if (fabOpen()) {
          <button type="button" class="bauakte-fab-action" (click)="openPhotoUpload(); fabOpen.set(false)"><mat-icon>add_a_photo</mat-icon><span>Foto</span></button>
          <button type="button" class="bauakte-fab-action" (click)="openVideoUpload(); fabOpen.set(false)"><mat-icon>videocam</mat-icon><span>Video</span></button>
          <a class="bauakte-fab-action" [routerLink]="['/tagesdokumentation']" [queryParams]="{ project_id: selectedProject()?.['id'], site_id: primarySite()?.['id'] }" (click)="fabOpen.set(false)"><mat-icon>edit_note</mat-icon><span>Tagesdoku</span></a>
        }
        <button type="button" class="bauakte-fab" [class.is-open]="fabOpen()" (click)="fabOpen.set(!fabOpen())" aria-label="Aufnehmen / Hinzufügen"><mat-icon>{{ fabOpen() ? 'close' : 'add' }}</mat-icon></button>
      </div>
    }

    @if (mapEnlarged() && mapUrl()) {
      <div class="map-overlay" (click)="mapEnlarged.set(false)">
        <div class="map-overlay-inner" (click)="$event.stopPropagation()">
          <header class="map-overlay-head">
            <div><strong>Standort</strong><span>{{ siteAddressLine() }}</span></div>
            <button mat-icon-button (click)="mapEnlarged.set(false)" aria-label="Schließen"><mat-icon>close</mat-icon></button>
          </header>
          <iframe [src]="mapUrlLarge()" title="Standortkarte groß" loading="lazy"></iframe>
        </div>
      </div>
    }

    @if (lightboxUrl()) {
      <div class="bauakte-lightbox" [class.is-single]="lightboxList().length <= 1" (click)="closeLightbox()" (touchstart)="onLightboxTouchStart($event)" (touchend)="onLightboxTouchEnd($event)">

        <!-- Kopfleiste: Bildinfo links, Schließen rechts -->
        <header class="lb-topbar" (click)="$event.stopPropagation()">
          <div class="lb-title">
            <b>{{ lightboxCaption() }}</b>
            <small>{{ lightboxMeta() }}</small>
          </div>
          @if (lightboxList().length > 1) {
            <span class="lb-counter">{{ lightboxIndex() + 1 }} / {{ lightboxList().length }}</span>
          }
          <button mat-icon-button class="lb-close" (click)="closeLightbox(); $event.stopPropagation()" aria-label="Schließen"><mat-icon>close</mat-icon></button>
        </header>

        @if (lightboxList().length > 1) {
          <button mat-icon-button class="bauakte-lightbox-nav prev" (click)="lightboxPrev($event)" aria-label="Vorheriges Foto"><mat-icon>chevron_left</mat-icon></button>
          <button mat-icon-button class="bauakte-lightbox-nav next" (click)="lightboxNext($event)" aria-label="Nächstes Foto"><mat-icon>chevron_right</mat-icon></button>
        }

        <div class="lb-stage" (click)="closeLightbox()">
          <img class="lb-image" [src]="lightboxUrl()" alt="Foto" (click)="$event.stopPropagation()">
        </div>

        <!-- Aktionsleiste unten: beschriftet statt nur Symbole, am Handy gut erreichbar -->
        <footer class="lb-actionbar" (click)="$event.stopPropagation()">
          <button type="button" class="lb-action" (click)="openLightboxExternal()">
            <mat-icon>open_in_full</mat-icon><span>Original</span>
          </button>
          <button type="button" class="lb-action" (click)="downloadLightbox()">
            <mat-icon>download</mat-icon><span>Speichern</span>
          </button>
          @if (canShareLightbox()) {
            <button type="button" class="lb-action" (click)="shareLightbox()">
              <mat-icon>share</mat-icon><span>Teilen</span>
            </button>
          }
          @if (!isCrew && canDeleteFiles) {
            <button type="button" class="lb-action lb-action-danger" (click)="deleteCurrentLightbox($event)">
              <mat-icon>delete</mat-icon><span>Löschen</span>
            </button>
          }
        </footer>

        <!-- Streifen mit allen Bildern: schnelles Springen ohne Wischen -->
        @if (lightboxList().length > 1) {
          <div class="lb-strip" (click)="$event.stopPropagation()">
            @for (photo of lightboxList(); track photo['id']; let i = $index) {
              <button type="button" class="lb-thumb" [class.is-active]="i === lightboxIndex()" (click)="lightboxGoTo(i)" [attr.aria-label]="'Bild ' + (i + 1)">
                <img [src]="photoUrl(photo, 'thumb')" loading="lazy" alt="">
              </button>
            }
          </div>
        }
      </div>
    }
  `
})
export class OrderFileComponent implements OnDestroy {
  readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  get isCrew(): boolean { return this.auth.isCrewMember(); }
  // Rechte der Bauakte. Ohne Recht wird der jeweilige Button gar nicht erst
  // angeboten (statt ihn anzuzeigen und beim Klick abzulehnen).
  get canEditProject(): boolean { return this.auth.canWrite('projects'); }
  get canUploadFiles(): boolean { return this.auth.canWrite('documents'); }
  get canDeleteFiles(): boolean { return this.auth.canDelete('documents'); }
  get canPlanAppointments(): boolean { return this.auth.canWrite('planning'); }
  get canDestroyProject(): boolean { return this.auth.isAdmin(); }
  // ===================== Räume und Flächen ===============================
  /**
   * Raumaufstellung einer Baustelle.
   *
   * Fachlicher Kern jeder Estrichbaustelle: welcher Raum, welche Fläche,
   * welche Estrichart, welche Dicke, wie weit. Daraus ergeben sich
   * Materialmenge, Angebotsprüfung und Fortschritt – ohne dass jemand
   * zusätzlich rechnen muss.
   *
   * Die Tabelle `rooms` war bereits vorhanden (Stammdatenmodul), nur nicht
   * in der Bauakte erreichbar. Es gibt daher keine Schemaänderung.
   */
  readonly rooms = signal<GenericRecord[]>([]);
  readonly roomStates = [
    { key: 'open',      label: 'Offen' },
    { key: 'prepared',  label: 'Vorbereitet' },
    { key: 'done',      label: 'Eingebaut' },
    { key: 'checked',   label: 'Geprüft / CM' },
    { key: 'defect',    label: 'Mangel' }
  ];
  /** Status auf einen bekannten Schlüssel abbilden. */
  roomStatusKey(r: GenericRecord): string {
    const raw = String(r['status'] || '').toLowerCase().trim();
    const map: Record<string, string> = {
      '': 'open', 'offen': 'open', 'open': 'open',
      'vorbereitet': 'prepared', 'prepared': 'prepared',
      'eingebaut': 'done', 'done': 'done', 'fertig': 'done',
      'geprüft': 'checked', 'geprueft': 'checked', 'checked': 'checked',
      'mangel': 'defect', 'defect': 'defect'
    };
    return map[raw] || 'open';
  }
  roomStatusLabel(r: GenericRecord): string {
    return this.roomStates.find((s) => s.key === this.roomStatusKey(r))?.label || 'Offen';
  }
  private roomArea(r: GenericRecord): number { return Number(r['area_m2'] || 0); }
  /** Gesamtfläche, auf eine Nachkommastelle gerundet. */
  roomsTotalArea(): string {
    const sum = this.rooms().reduce((a, r) => a + this.roomArea(r), 0);
    return this.formatArea(sum);
  }
  /** Fläche, die bereits eingebaut oder geprüft ist. */
  roomsDoneArea(): string {
    const sum = this.rooms()
      .filter((r) => ['done', 'checked'].includes(this.roomStatusKey(r)))
      .reduce((a, r) => a + this.roomArea(r), 0);
    return this.formatArea(sum);
  }
  roomsProgress(): number {
    const total = this.rooms().reduce((a, r) => a + this.roomArea(r), 0);
    if (!total) return 0;
    const done = this.rooms()
      .filter((r) => ['done', 'checked'].includes(this.roomStatusKey(r)))
      .reduce((a, r) => a + this.roomArea(r), 0);
    return Math.round((done / total) * 100);
  }
  /** Flächen je Estrichart – die Zahl für Material und Angebot. */
  roomsByType(): { type: string; area: string }[] {
    const map = new Map<string, number>();
    for (const r of this.rooms()) {
      const type = String(r['screed_type'] || 'ohne Angabe');
      map.set(type, (map.get(type) || 0) + this.roomArea(r));
    }
    return Array.from(map.entries())
      .sort((a, b) => b[1] - a[1])
      .map(([type, area]) => ({ type, area: this.formatArea(area) }));
  }
  private formatArea(value: number): string {
    return (Math.round(value * 10) / 10).toLocaleString('de-DE', { maximumFractionDigits: 1 });
  }

  /** Hinterlegte Gesamtfläche der Baustelle. */
  siteAreaLabel(): string {
    const site = this.primarySite();
    return this.formatArea(Number(site?.['area_m2'] || 0));
  }
  /**
   * Abweichung zwischen Baustellenfläche und Summe der Räume.
   * Wird nur gezeigt, wenn beides gepflegt ist und sich um mehr als 5 %
   * unterscheidet – als Hinweis, nicht als Fehler.
   */
  areaMismatch(): string {
    const site = Number(this.primarySite()?.['area_m2'] || 0);
    const rooms = this.rooms().reduce((a, r) => a + Number(r['area_m2'] || 0), 0);
    if (!site || !rooms) return '';
    const diff = rooms - site;
    if (Math.abs(diff) / site < 0.05) return '';
    return `${diff > 0 ? '+' : ''}${this.formatArea(diff)} m²`;
  }

  private roomsLoaded = false;
  /**
   * Räume nach Geschoss gruppieren.
   *
   * Eine durchlaufende Liste wird ab etwa acht Räumen unübersichtlich – auf
   * der Baustelle arbeitet man ohnehin geschossweise. Räume ohne Angabe
   * landen in einer eigenen Gruppe am Ende.
   */
  roomGroups(): { floor: string; rooms: GenericRecord[]; area: string }[] {
    const map = new Map<string, GenericRecord[]>();
    for (const r of this.rooms()) {
      const floor = String(r['floor'] || '').trim() || 'Ohne Geschossangabe';
      if (!map.has(floor)) map.set(floor, []);
      map.get(floor)!.push(r);
    }
    // Übliche Reihenfolge im Bau: Keller, Erdgeschoss, Obergeschosse, Dach.
    const order = ['kg', 'ug', 'keller', 'eg', 'erdgeschoss', '1.og', 'og', '2.og', '3.og', 'dg', 'dach'];
    const rank = (floor: string) => {
      const key = floor.toLowerCase().replace(/\s/g, '');
      const i = order.findIndex((o) => key.startsWith(o));
      return i < 0 ? (floor === 'Ohne Geschossangabe' ? 999 : 500) : i;
    };
    return Array.from(map.entries())
      .sort((a, b) => rank(a[0]) - rank(b[0]) || a[0].localeCompare(b[0], 'de'))
      .map(([floor, list]) => ({
        floor,
        rooms: list,
        area: this.formatArea(list.reduce((sum, r) => sum + Number(r['area_m2'] || 0), 0))
      }));
  }

  loadRooms(): void {
    const site = this.primarySite();
    if (!site) { this.rooms.set([]); return; }
    this.api.list('rooms', '', 300).subscribe({
      next: (r) => { this.roomsLoaded = true; this.rooms.set((r.items || [])
        .filter((x) => String(x['site_id'] || '') === String(site['id']))
        .sort((a, b) => String(a['floor'] || '').localeCompare(String(b['floor'] || ''), 'de')
          || String(a['name'] || '').localeCompare(String(b['name'] || ''), 'de'))); },
      error: () => { this.roomsLoaded = true; this.rooms.set([]); }
    });
  }
  setRoomStatus(r: GenericRecord, status: string): void {
    this.api.update('rooms', String(r['id']), { status }).subscribe({
      next: () => { this.rooms.update((l) => l.map((x) => x['id'] === r['id'] ? { ...x, status } : x)); },
      error: () => this.snack.open('Stand konnte nicht gespeichert werden.', 'OK', { duration: 3000 })
    });
  }
  deleteRoom(r: GenericRecord): void {
    this.dialog.open(ConfirmDialogComponent, {
      width: 'min(440px, 94vw)', panelClass: 'ew-dialog',
      data: { title: 'Raum löschen?', message: `„${r['name'] || 'Raum'}" wird aus der Aufstellung entfernt.`, confirmText: 'Löschen', cancelText: 'Abbrechen', icon: 'delete', tone: 'warn' }
    }).afterClosed().subscribe((ok) => {
      if (!ok) return;
      this.api.remove('rooms', String(r['id'])).subscribe({
        next: () => { this.snack.open('Raum gelöscht.', 'OK', { duration: 2000 }); this.loadRooms(); },
        error: () => this.snack.open('Raum konnte nicht gelöscht werden.', 'OK', { duration: 3000 })
      });
    });
  }
  openRoom(r: GenericRecord | null): void {
    const site = this.primarySite();
    if (!site) { this.snack.open('Für diese Bauakte ist keine Baustelle hinterlegt.', 'OK', { duration: 3200 }); return; }
    this.dialog.open(RoomDialogComponent, {
      width: 'min(560px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', autoFocus: false,
      data: { room: r, siteId: String(site['id']), screedTypes: this.screedTypes(), states: this.roomStates }
    }).afterClosed().subscribe((payload: GenericRecord | undefined) => {
      if (!payload) return;
      const request = r?.['id']
        ? this.api.update('rooms', String(r['id']), payload)
        : this.api.create('rooms', payload);
      request.subscribe({
        next: () => { this.snack.open('Raum gespeichert.', 'OK', { duration: 2000 }); this.loadRooms(); },
        error: () => this.snack.open('Raum konnte nicht gespeichert werden.', 'OK', { duration: 3000 })
      });
    });
  }

  /**
   * Vorschau schließen und zur Liste zurück.
   * Am Handy liegt die Vorschau als Vollbildebene über der Liste – ohne
   * diesen Weg kam man aus einer PDF-Ansicht nicht mehr heraus.
   */
  /** Eigene Vollbildanzeige – nur in der installierten App gebraucht. */
  readonly viewerFullscreen = signal(false);

  /**
   * Ist die Vollbildansicht ausdrücklich geöffnet?
   *
   * Wird ausschließlich beim Antippen eines Dokuments oder Fotos in der
   * Liste gesetzt – nicht durch Nebenwirkungen anderer Abläufe.
   */
  readonly docSheetOpen = signal(false);

  /** Art der gewählten Datei – bestimmt, womit sie angezeigt wird. */
  viewerKind(): 'pdf' | 'image' | 'video' | 'other' {
    if (this.photoViewerUrl()) return 'image';
    const doc = this.selectedDoc();
    const name = String(doc?.['name'] || doc?.['file_name'] || '').toLowerCase();
    const mime = String(doc?.['mime_type'] || '').toLowerCase();
    if (mime.includes('pdf') || name.endsWith('.pdf')) return 'pdf';
    if (mime.startsWith('image/') || /\.(png|jpe?g|gif|webp|heic)$/.test(name)) return 'image';
    if (mime.startsWith('video/') || /\.(mp4|mov|webm|m4v)$/.test(name)) return 'video';
    return 'other';
  }
  selectedDocName(): string {
    const doc = this.selectedDoc();
    return String(doc?.['name'] || doc?.['file_name'] || 'Dokument');
  }

  clearSelection(): void {
    this.docSheetOpen.set(false);
    this.viewerFullscreen.set(false);
    this.selectedDoc.set(null);
    this.rawViewerUrl.set('');
    this.safeViewerUrl.set(null);
    this.photoViewerUrl.set('');
  }

  /**
   * Hinweise zur Baustelle, die ganz oben stehen sollen.
   *
   * Bewusst knapp: Nur Dinge, die den Ablauf blockieren oder erfahrungsgemäß
   * vergessen werden. Ist alles in Ordnung, erscheint der Bereich gar nicht –
   * eine Akte ohne Auffälligkeiten sieht aus wie zuvor.
   */
  siteAlerts(): { level: string; icon: string; text: string; tab: number }[] {
    const out: { level: string; icon: string; text: string; tab: number }[] = [];
    const p = this.selectedProject();
    if (!p) return out;

    // 1. Termin heute oder morgen – das Dringendste zuerst.
    const next = this.projectAppointments()
      .filter((a) => new Date(String(a['start_at'] || '').replace(' ', 'T')) >= new Date())
      .sort((a, b) => String(a['start_at'] || '').localeCompare(String(b['start_at'] || '')))[0];
    if (next) {
      const d = new Date(String(next['start_at']).replace(' ', 'T'));
      const days = Math.round((d.getTime() - Date.now()) / 86400000);
      if (days <= 1) {
        const when = days <= 0 ? 'heute' : 'morgen';
        const time = d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
        const crew = String(next['crew_name'] || '');
        out.push({ level: 'go', icon: 'event_available', tab: 6,
                   text: `Einsatz ${when} um ${time}${crew ? ' · ' + crew : ''}` });
      }
    }

    // 2. Erstbegehung fehlt – ohne sie fehlen die fachlichen Grundlagen.
    if (!this.inspections().length && !p['initial_inspection_done']) {
      out.push({ level: 'warn', icon: 'fact_check', tab: 7, text: 'Erstbegehung steht noch aus' });
    }

    // 3. Material bestellt, aber noch nicht geliefert.
    const open = this.openMaterialCount();
    if (open > 0) {
      out.push({ level: 'info', icon: 'inventory_2', tab: 9,
                 text: `${open} Materialbestellung${open > 1 ? 'en' : ''} noch offen` });
    }

    // 4. Keine Adresse – dann gibt es keine Navigation und keine Planung.
    const site = this.primarySite();
    if (site && !String(site['address'] || '').trim()) {
      out.push({ level: 'warn', icon: 'wrong_location', tab: 0, text: 'Für die Baustelle ist keine Adresse hinterlegt' });
    }

    return out.slice(0, 3);
  }

  /** Telefonnummer des Kunden – für den Direktanruf von der Baustelle. */
  customerPhone(): string {
    const project = this.selectedProject();
    if (!project) return '';
    const direct = String(project['customer_phone'] || project['contact_phone'] || '').trim();
    if (direct) return direct.replace(/\s+/g, '');
    const customer = this.customers().find((c) => String(c['id']) === String(project['customer_id'] ?? ''));
    const phone = String(customer?.['phone'] || customer?.['mobile'] || '').trim();
    return phone ? phone.replace(/\s+/g, '') : '';
  }
  /** Navigationsziel aus der Baustellenadresse. */
  mapsUrl(): string {
    const addr = this.projectAddress();
    return addr ? 'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(addr) : '';
  }

  // ---- Wischen zwischen den Reitern (nur Fingerbedienung) --------------
  private tabSwipeX = 0;
  private tabSwipeY = 0;
  /** Anzahl der Reiter – Grenze beim Wischen. */
  private readonly tabCount = 11;
  onTabSwipeStart(event: TouchEvent): void {
    if (event.touches.length !== 1) return;
    this.tabSwipeX = event.touches[0].clientX;
    this.tabSwipeY = event.touches[0].clientY;
  }
  onTabSwipeEnd(event: TouchEvent): void {
    if (!this.tabSwipeX) return;
    const t = event.changedTouches[0];
    const dx = t.clientX - this.tabSwipeX;
    const dy = t.clientY - this.tabSwipeY;
    this.tabSwipeX = 0;
    // Nur deutliche waagerechte Bewegungen zählen, sonst kollidiert es mit
    // dem Scrollen in Listen und dem Blättern in Bildern.
    if (Math.abs(dx) < 80 || Math.abs(dx) < Math.abs(dy) * 2) return;
    const next = this.activeTab() + (dx < 0 ? 1 : -1);
    if (next < 0 || next >= this.tabCount) return;
    this.activeTab.set(next);
  }

  /** Termine dieser Bauakte – Grundlage für die Terminangabe im Kopf. */
  readonly projectAppointments = signal<GenericRecord[]>([]);
  private loadProjectAppointments(projectId: string): void {
    const from = new Date(); from.setMonth(from.getMonth() - 2);
    const to = new Date(); to.setMonth(to.getMonth() + 6);
    const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    this.api.planningAssignments(iso(from), iso(to)).subscribe({
      next: (r) => this.projectAppointments.set((r.items || []).filter((a) => String(a['project_id'] || '') === projectId)),
      error: () => this.projectAppointments.set([])
    });
  }
  private nextAppointment(): GenericRecord | undefined {
    const now = new Date();
    return this.projectAppointments()
      .filter((a) => new Date(String(a['start_at'] || '').replace(' ', 'T')) >= now)
      .sort((a, b) => String(a['start_at'] || '').localeCompare(String(b['start_at'] || '')))[0];
  }
  /**
   * Nächster Termin als Kurzangabe für den vorhandenen Kopfbereich.
   * Das ist die Angabe, die beim Öffnen einer Akte am häufigsten gesucht
   * wird – sie steht jetzt direkt neben Nummer, Kunde und Adresse.
   */
  nextAppointmentLabel(): string {
    const next = this.nextAppointment();
    if (!next) return '';
    const d = new Date(String(next['start_at']).replace(' ', 'T'));
    const days = Math.round((d.getTime() - Date.now()) / 86400000);
    const time = d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
    if (days <= 0) return `heute ${time}`;
    if (days === 1) return `morgen ${time}`;
    return `${d.toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit' })} ${time}`;
  }
  nextAppointmentCrew(): string { return String(this.nextAppointment()?.['crew_name'] || ''); }
  /** Materialbestellungen, die noch nicht geliefert sind. */
  openMaterialCount(): number {
    return this.materialOrders().filter((o) => !['delivered', 'geliefert', 'done', 'closed'].includes(String(o['status'] || '').toLowerCase())).length;
  }

  /**
   * Notizen darf jeder erfassen, der die Bauakte sehen darf.
   * Der Austausch zwischen Baustelle und Büro soll an keiner Rechtevergabe
   * scheitern – an der Akte selbst ändert eine Notiz nichts.
   */
  readonly canWriteNotes = true;

  get canWriteReports(): boolean { return this.auth.canWrite('reports'); }
  get canWriteInspections(): boolean { return this.auth.canWrite('inspections') || this.auth.canWrite('projects'); }
  get canOrderMaterial(): boolean { return this.auth.canWrite('materials') || this.auth.canWrite('projects'); }
  get canDeleteMaterial(): boolean { return this.auth.canDelete('materials') || this.auth.isAdmin(); }
  get canShareFiles(): boolean { return this.auth.can('documents.share') || this.auth.canWrite('documents'); }
  private readonly sanitizer = inject(DomSanitizer);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);
  readonly detailsEdit = signal(false);
  readonly activeTab = signal(0);
  // Beim Tabwechsel die Vorschau sauber zurücksetzen, damit nicht z. B. ein
  // Foto im Dokumente-Tab hängen bleibt. Anschließend passende Vorschau wählen.
  onTabChange(index: number): void {
    // Beim Wechsel des Reiters darf keine Vollbildansicht offen bleiben.
    this.docSheetOpen.set(false);
    this.activeTab.set(index);
    // Räume erst beim ersten Öffnen des Reiters holen (Index 8).
    if (index === 8 && !this.roomsLoaded) this.loadRooms();
    this.resetViewer();
    if (index === 2) { const first = this.photos()[0]; if (first) this.openPhoto(first); }       // Fotos
    else if (index === 3) { const first = this.videos()[0]; if (first) this.openDocument(first); } // Videos
    else if (index === 4) { const first = this.documentEntries()[0]; if (first) this.openAttachment(first); } // Dokumente
  }
  resetViewer(): void {
    this.selectedDoc.set(null);
    this.selectedReport.set(null);
    this.rawViewerUrl.set('');
    this.safeViewerUrl.set(null);
    this.photoViewerUrl.set('');
  }
  readonly detailsForm = this.fb.group({ start_date: [''], end_date: [''], address: [''], zip: [''], city: [''], screed_type: [''], area_m2: [null as number | null], phone: [''], site_manager: [''], site_email: [''], notes: [''], hinweise: [''] });
  screedTypeMissing(): boolean {
    const v = String(this.detailsForm.get('screed_type')?.value || '').trim();
    if (!v) return false;
    return !this.screedTypes().some((st) => String(st['name']) === v);
  }
  readonly contactEdit = signal(false);
  readonly contactForm = this.fb.group({ contact_person: [''], email: [''], phone: [''], site_manager: [''], site_phone: [''], site_email: [''], site_address: [''], site_zip: [''], site_city: [''], hints: [''] });
  private presenceTimer: number | undefined;
  readonly projectControl = new FormControl<string>('', { nonNullable: true });
  readonly orderSearch = signal('');
  readonly projects = signal<GenericRecord[]>([]); readonly customers = signal<GenericRecord[]>([]); readonly users = signal<GenericRecord[]>([]); readonly screedTypes = signal<GenericRecord[]>([]); readonly suppliers = signal<GenericRecord[]>([]); readonly sites = signal<GenericRecord[]>([]); readonly documents = signal<GenericRecord[]>([]); readonly photos = signal<GenericRecord[]>([]); readonly reports = signal<GenericRecord[]>([]); readonly inspections = signal<GenericRecord[]>([]); readonly quality = signal<GenericRecord[]>([]); readonly shares = signal<GenericRecord[]>([]); readonly tasks = signal<GenericRecord[]>([]); readonly history = signal<GenericRecord[]>([]); readonly presence = signal<GenericRecord[]>([]); readonly materialOrders = signal<GenericRecord[]>([]);
  readonly loading = signal(true);
  readonly contextLoading = signal(false);
  readonly inspectionPdfOptions = signal({ includeChecklist: true, includeMeasurements: true, includeNotes: true, includePhotos: true });
  readonly rawViewerUrl = signal<string>(''); readonly safeViewerUrl = signal<SafeResourceUrl | null>(null); readonly mapUrl = computed<SafeResourceUrl | null>(() => { const site=this.primarySite(); if(!site) return null; const address=[site['address'], site['zip'], site['city']].filter(Boolean).join(' '); return address ? this.sanitizer.bypassSecurityTrustResourceUrl('https://maps.google.com/maps?q=' + encodeURIComponent(address) + '&t=&z=15&ie=UTF8&iwloc=&output=embed') : null; }); readonly photoViewerUrl = signal<string>(''); readonly selectedDoc = signal<GenericRecord | null>(null);
  readonly mapEnlarged = signal(false);
  readonly wordHtml = signal<string>('');
  readonly wordLoading = signal(false);
  siteAddressLine(): string { const s = this.primarySite(); if (!s) return ''; return [s['address'], [s['zip'], s['city']].filter(Boolean).join(' ')].filter(Boolean).join(', '); }
  mapUrlLarge(): SafeResourceUrl | null {
    const site = this.primarySite(); if (!site) return null;
    const address = [site['address'], site['zip'], site['city']].filter(Boolean).join(' ');
    return address ? this.sanitizer.bypassSecurityTrustResourceUrl('https://maps.google.com/maps?q=' + encodeURIComponent(address) + '&t=&z=16&ie=UTF8&iwloc=&output=embed') : null;
  }
  /** Beschriftung des gerade angezeigten Bildes. */
  lightboxCaption(): string {
    const photo = this.lightboxList()[this.lightboxIndex()];
    return String(photo?.['caption'] || photo?.['original_name'] || photo?.['name'] || 'Foto');
  }
  /** Zusatzangaben (Datum, Raum) unter der Beschriftung. */
  lightboxMeta(): string {
    const photo = this.lightboxList()[this.lightboxIndex()];
    if (!photo) return '';
    const parts: string[] = [];
    const taken = photo['taken_at'] || photo['created_at'];
    if (taken) {
      const d = new Date(String(taken).replace(' ', 'T'));
      if (!isNaN(d.getTime())) parts.push(d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }) + ', ' + d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' }) + ' Uhr');
    }
    if (photo['room_name']) parts.push(String(photo['room_name']));
    return parts.join(' · ');
  }
  /** Direkt zu einem Bild des Streifens springen. */
  lightboxGoTo(index: number): void {
    const list = this.lightboxList();
    if (index < 0 || index >= list.length) return;
    this.lightboxIndex.set(index);
    this.lightboxUrl.set(this.photoUrl(list[index], 'main'));
  }
  /** Original in neuem Tab oeffnen. */
  openLightboxExternal(): void { const url = this.lightboxUrl(); if (url) window.open(url, '_blank', 'noopener'); }
  /** Bild herunterladen. */
  downloadLightbox(): void {
    const url = this.lightboxUrl(); if (!url) return;
    const a = document.createElement('a');
    a.href = url;
    a.download = this.lightboxCaption().replace(/[^\w.\- ]+/g, '_') || 'foto.jpg';
    document.body.appendChild(a); a.click(); a.remove();
  }
  /** Teilen ueber das Betriebssystem – nur wo der Browser das unterstuetzt. */
  canShareLightbox(): boolean { return typeof navigator !== 'undefined' && !!(navigator as any).share; }
  shareLightbox(): void {
    const url = this.lightboxUrl(); if (!url) return;
    const share = (navigator as any).share;
    if (!share) return;
    share.call(navigator, { title: this.lightboxCaption(), url: new URL(url, window.location.origin).toString() }).catch(() => {});
  }
  readonly lightboxUrl = signal<string>('');
  readonly lightboxList = signal<GenericRecord[]>([]);
  readonly lightboxIndex = signal<number>(0);
  private lightboxTouchX = 0;
  readonly selectedReport = signal<GenericRecord | null>(null);
  readonly companyInfo = signal<GenericRecord>({});
  readonly fabOpen = signal(false);
  readonly notes = signal<GenericRecord[]>([]);
  readonly noteFile = signal<File | null>(null);
  readonly noteSending = signal(false);
  noteDraft = '';
  noteInitials(n: GenericRecord): string { const name = String(n['author_name'] || '?').trim(); const parts = name.split(/\s+/); return ((parts[0]?.[0] || '') + (parts[1]?.[0] || '')).toUpperCase() || '?'; }
  // ---- Anrufnotizen -----------------------------------------------------
  /**
   * Angaben eines Anrufs aus der Notiz lesen.
   *
   * Neue Anrufe bringen die Einzelangaben in `meta_json` mit. Ältere Einträge
   * haben nur den Textblock – dafür gibt es je Feld einen Rückfall, damit
   * auch bereits erfasste Anrufe ordentlich dargestellt werden.
   */
  private callMeta(n: GenericRecord): Record<string, unknown> {
    const raw = n['meta_json'];
    if (!raw) return {};
    if (typeof raw === 'object') return raw as Record<string, unknown>;
    try { return JSON.parse(String(raw)) as Record<string, unknown>; } catch { return {}; }
  }
  callDirection(n: GenericRecord): string {
    const m = this.callMeta(n);
    if (m['direction']) return String(m['direction']);
    return String(n['body'] || '').includes('Ausgehend') ? 'outbound' : 'inbound';
  }
  callCaller(n: GenericRecord): string {
    const m = this.callMeta(n);
    if (m['caller']) return String(m['caller']);
    // Rückfall: aus "📞 Eingehender Anruf – Name (0221…)" den Namen holen.
    const match = String(n['body'] || '').match(/Anruf\s*–\s*([^(\n]+)/);
    return match ? match[1].trim() : '';
  }
  callPhone(n: GenericRecord): string {
    const m = this.callMeta(n);
    if (m['phone']) return String(m['phone']);
    const match = String(n['body'] || '').match(/\(([+0-9][0-9\s()/.-]{4,})\)/);
    return match ? match[1].trim() : '';
  }
  callSubject(n: GenericRecord): string {
    const m = this.callMeta(n);
    if (m['subject']) return String(m['subject']);
    const match = String(n['body'] || '').match(/Betreff:\s*(.+)/);
    return match ? match[1].trim() : '';
  }
  callText(n: GenericRecord): string {
    const m = this.callMeta(n);
    if (m['text']) return String(m['text']);
    // Rückfall: alles nach der Kopfzeile bzw. nach dem Betreff.
    const lines = String(n['body'] || '').split('\n').slice(1)
      .filter((l) => !l.startsWith('Betreff:'));
    return lines.join('\n').trim();
  }
  callFollowUp(n: GenericRecord): boolean { return !!Number(this.callMeta(n)['follow_up'] || 0); }

  noteSystemIcon(n: GenericRecord): string { const t = String(n['entry_type'] || ''); if (t === 'assignment') return 'person'; if (t === 'status') return 'flag'; return 'info'; }
  // ---- Angeheftete Anmerkungen ("Post-it") ------------------------------
  /** Wählbare Farben. Rot ist bewusst dabei – für das, was nicht übersehen
   *  werden darf. */
  readonly noteColors = [
    { key: 'yellow', label: 'Gelb – Hinweis' },
    { key: 'orange', label: 'Orange – beachten' },
    { key: 'red',    label: 'Rot – wichtig' },
    { key: 'green',  label: 'Grün – erledigt/Info' },
    { key: 'blue',   label: 'Blau – Verwaltung' }
  ];
  /** Zusatzangaben einer Notiz aus meta_json lesen. */
  private noteMeta(n: GenericRecord): Record<string, unknown> {
    const raw = n['meta_json'];
    if (!raw) return {};
    if (typeof raw === 'object') return raw as Record<string, unknown>;
    try { return JSON.parse(String(raw)) as Record<string, unknown>; } catch { return {}; }
  }
  isPinned(n: GenericRecord): boolean { return !!Number(this.noteMeta(n)['pinned'] || 0); }
  noteColor(n: GenericRecord): string { return String(this.noteMeta(n)['color'] || 'yellow'); }
  /** Fälligkeit als Kurzangabe, sofern gesetzt. */
  noteDue(n: GenericRecord): string {
    const raw = String(this.noteMeta(n)['due_at'] || '');
    if (!raw) return '';
    const d = new Date(raw.replace(' ', 'T'));
    if (isNaN(d.getTime())) return '';
    return d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' });
  }
  noteOverdue(n: GenericRecord): boolean {
    const raw = String(this.noteMeta(n)['due_at'] || '');
    if (!raw) return false;
    const d = new Date(raw.replace(' ', 'T'));
    return !isNaN(d.getTime()) && d.getTime() < Date.now();
  }
  /** Angeheftete Anmerkungen, neueste zuerst. */
  /** Die angeheftete Anmerkung – höchstens eine. */
  pinnedNotes(): GenericRecord[] {
    return this.notes()
      .filter((n) => String(n['entry_type'] || 'note') === 'note' && this.isPinned(n))
      .sort((a, b) => String(b['created_at'] || '').localeCompare(String(a['created_at'] || '')))
      .slice(0, 1);
  }
  private saveNoteMeta(n: GenericRecord, payload: GenericRecord): void {
    const project = this.selectedProject(); if (!project) return;
    this.api.updateProjectNote(String(project['id']), String(n['id']), payload).subscribe({
      next: () => this.loadNotes(),
      error: () => this.snack.open('Anmerkung konnte nicht geändert werden.', 'OK', { duration: 3000 })
    });
  }
  /**
   * Anheften – es ist immer nur EINE Anmerkung angeheftet.
   *
   * Ein Stapel bunter Zettel über der Akte wäre schnell genauso unübersichtlich
   * wie gar keiner. Wer eine neue Anmerkung anheftet, löst die bisherige
   * automatisch; sie bleibt als gewöhnliche Notiz erhalten.
   */
  togglePin(n: GenericRecord): void {
    const next = !this.isPinned(n);
    if (!next) {
      this.saveNoteMeta(n, { pinned: 0 });
      this.snack.open('Anheftung gelöst.', 'OK', { duration: 2000 });
      return;
    }
    const previous = this.notes().find((x) => this.isPinned(x) && String(x['id']) !== String(n['id']));
    const project = this.selectedProject(); if (!project) return;
    const pinNew = () => this.api.updateProjectNote(String(project['id']), String(n['id']), { pinned: 1 }).subscribe({
      next: () => { this.loadNotes(); this.snack.open(previous ? 'Angeheftet – die bisherige Anmerkung wurde gelöst.' : 'Anmerkung oben angeheftet.', 'OK', { duration: 2600 }); },
      error: () => this.snack.open('Anmerkung konnte nicht angeheftet werden.', 'OK', { duration: 3000 })
    });
    if (previous) {
      this.api.updateProjectNote(String(project['id']), String(previous['id']), { pinned: 0 }).subscribe({
        next: () => pinNew(), error: () => pinNew()
      });
    } else { pinNew(); }
  }
  unpinNote(n: GenericRecord): void { this.saveNoteMeta(n, { pinned: 0 }); }
  setNoteColor(n: GenericRecord, color: string): void { this.saveNoteMeta(n, { color }); }

  loadNotes(): void {
    const project = this.selectedProject(); if (!project) return;
    this.api.projectNotes(String(project['id'])).subscribe({ next: (res) => this.notes.set(res.items || []), error: () => {} });
  }
  onNoteFile(event: Event): void { const input = event.target as HTMLInputElement; const f = input.files?.[0]; if (f) this.noteFile.set(f); input.value = ''; }
  /** Beim Verfassen: gleich oben anheften? */
  readonly notePinDraft = signal(false);
  readonly noteColorDraft = signal('yellow');

  sendNote(): void {
    const project = this.selectedProject(); if (!project) return;
    const body = this.noteDraft.trim(); const file = this.noteFile();
    if (!body && !file) return;
    this.noteSending.set(true);
    const extra = this.notePinDraft() ? { pinned: 1, color: this.noteColorDraft() } : undefined;
    // Nur eine Anmerkung bleibt angeheftet – die bisherige wird gelöst.
    if (this.notePinDraft()) {
      const previous = this.notes().find((x) => this.isPinned(x));
      if (previous) this.api.updateProjectNote(String(project['id']), String(previous['id']), { pinned: 0 }).subscribe({ next: () => {}, error: () => {} });
    }
    this.api.addProjectNote(String(project['id']), body, file, extra).subscribe({
      next: () => { this.noteDraft = ''; this.noteFile.set(null); this.noteSending.set(false); this.notePinDraft.set(false); this.loadNotes(); if (file) this.refreshProjectContext(); },
      error: () => { this.noteSending.set(false); this.snack.open('Notiz konnte nicht gespeichert werden', 'OK', { duration: 3000 }); }
    });
  }
  deleteNote(n: GenericRecord): void {
    const project = this.selectedProject(); if (!project) return;
    const hasDoc = !!n['document_id'] && !!n['document_available'];
    const message = hasDoc
      ? 'Notiz wirklich löschen? Das hier angehängte Dokument bleibt weiterhin im Reiter „Dokumente" erhalten.'
      : 'Diese Notiz wirklich löschen?';
    const ref = this.dialog.open(OrderConfirmDialogComponent, { width: 'min(440px,94vw)', panelClass: 'ew-dialog', data: { title: 'Notiz löschen', message, confirmLabel: 'Löschen', icon: 'delete' } });
    ref.afterClosed().subscribe((ok) => {
      if (!ok) return;
      this.api.deleteProjectNote(String(project['id']), String(n['id'])).subscribe({
        next: () => { this.loadNotes(); this.snack.open(hasDoc ? 'Notiz gelöscht – das Dokument bleibt unter „Dokumente".' : 'Notiz gelöscht.', 'OK', { duration: 2800 }); },
        error: () => this.snack.open('Notiz konnte nicht gelöscht werden.', 'OK', { duration: 3000 })
      });
    });
  }
  openNoteDocument(n: GenericRecord): void {
    if (!n['document_available']) { this.snack.open('Dieses Dokument wurde gelöscht und ist nicht mehr verfügbar.', 'OK', { duration: 3000 }); return; }
    const id = String(n['document_id'] || ''); if (id) window.open(this.api.documentViewUrl(id), '_blank', 'noopener');
  }
  companyName(): string { return String(this.companyInfo()['name'] || 'EstrichWeb'); }
  companyNameUpper(): string { return this.companyName().toUpperCase(); }
  readonly siteColumns = ['site_no','name','address','screed','status'];
  readonly attachmentColumns = ['icon','name','category','size','created','actions'];
  readonly unifiedAttachmentColumns = ['open','type','name','category','created','size','actions'];
  readonly screedColumns = ['name','norm','thickness','drying'];
  readonly statusSteps = [{ key: 'new', label: 'Neu' },{ key: 'planned', label: 'Geplant' },{ key: 'in_progress', label: 'In Arbeit' },{ key: 'completed', label: 'Abgeschlossen' }];
  isActiveProject(project: GenericRecord): boolean { return !['completed','closed','cancelled','archived','deleted'].includes(String(project['status'] || 'new')); }
  readonly activeProjects = computed(() => this.projects().filter((p) => this.isActiveProject(p)));
  readonly filteredActiveProjects = computed(() => {
    const needle = this.normalize(this.orderSearch());
    if (!needle) return this.selectableProjects();
    return this.selectableProjects().filter((project) => this.normalize([project['project_no'], project['name'], project['city'], project['zip'], this.customerName(project['customer_id'])].join(' ')).includes(needle));
  });
  /** Auswahlliste: aktive Bauakten + die aktuell geoeffnete Akte, auch wenn diese
   *  bereits abgeschlossen/storniert/archiviert ist. */
  readonly selectableProjects = computed(() => {
    const list = [...this.activeProjects()];
    const currentId = String(this.projectControl.value || '');
    if (currentId && !list.some((p) => String(p['id']) === currentId)) {
      const current = this.projects().find((p) => String(p['id']) === currentId);
      if (current) list.unshift(current);
    }
    return list;
  });
  /** Wichtig: Eine ausdruecklich gewaehlte Bauakte wird IMMER geoeffnet – auch
   *  wenn sie abgeschlossen, storniert oder archiviert ist. Frueher wurde in
   *  diesem Fall stillschweigend auf die erste aktive Akte umgeschaltet. */
  readonly selectedProject = computed(() => {
    const wantedId = String(this.projectControl.value || '');
    const current = wantedId ? this.projects().find((p) => String(p['id']) === wantedId) : undefined;
    if (current) return current;
    return this.activeProjects()[0] || this.projects()[0];
  });
  isClosedProject(): boolean { const p = this.selectedProject(); return !!p && !this.isActiveProject(p); }
  closedProjectLabel(): string {
    const status = String(this.selectedProject()?.['status'] || '');
    const map: Record<string, string> = { completed: 'abgeschlossen', closed: 'geschlossen', cancelled: 'storniert', archived: 'archiviert', deleted: 'gelöscht' };
    return map[status] || 'abgeschlossen';
  }
  readonly primarySite = computed(() => this.sites()[0]);
  readonly initialInspection = computed(() => {
    const matches = this.inspections().filter((item) => String(item['result_json'] || '').includes('initial_site_inspection'));
    const pool = matches.length ? matches : this.inspections();
    // Neueste Begehung zuerst (damit eine Zweitbegehung die Erstbegehung ersetzt in der Anzeige)
    return [...pool].sort((a, b) => String(b['created_at'] || '').localeCompare(String(a['created_at'] || '')))[0] || null;
  });
  hasInitialInspection(): boolean { return !!this.initialInspection(); }
  startInitialInspection(project: GenericRecord): void {
    this.router.navigate(['/erstbegehung-wizard'], { queryParams: { project_id: project['id'], site_id: this.primarySite()?.['id'] } });
  }
  readonly videos = computed(() => this.documents().filter((d) => String(d['mime_type'] || '').startsWith('video/') || d['category'] === 'daily_video' || d['category'] === 'video'))
  readonly documentFiles = computed(() => this.documents().filter((d) => !String(d['mime_type'] || '').startsWith('video/') && d['category'] !== 'daily_video' && d['category'] !== 'video'));
  readonly combinedAttachments = computed<AttachmentEntry[]>(() => {
    const docs = this.documentFiles().map((doc): AttachmentEntry => ({ record: doc, moduleKey: 'documents', type: 'document', typeLabel: 'Dokument', icon: this.docIcon(doc), title: String(doc['name'] || doc['original_name'] || 'Dokument'), subtitle: String(doc['original_name'] || doc['mime_type'] || ''), category: this.documentCategoryLabel(String(doc['category'] || '')), createdAt: String(doc['created_at'] || doc['updated_at'] || ''), size: this.fileSize(Number(doc['size_bytes'] || 0)) }));
    const photos = this.photos().map((photo): AttachmentEntry => ({ record: photo, moduleKey: 'photos', type: 'photo', typeLabel: 'Bild', icon: 'image', title: String(photo['caption'] || 'Bild'), subtitle: this.projectAddress(), category: 'Bild', createdAt: String(photo['taken_at'] || photo['created_at'] || ''), size: this.fileSize(Number(photo['original_size_bytes'] || photo['size_bytes'] || 0)) }));
    const videos = this.videos().map((video): AttachmentEntry => ({ record: video, moduleKey: 'documents', type: 'video', typeLabel: 'Video', icon: 'movie', title: String(video['name'] || video['original_name'] || 'Video'), subtitle: String(video['mime_type'] || ''), category: this.documentCategoryLabel(String(video['category'] || 'video')), createdAt: String(video['created_at'] || video['updated_at'] || ''), size: this.fileSize(Number(video['size_bytes'] || 0)) }));
    return [...docs, ...photos, ...videos].sort((a, b) => String(b.createdAt || '').localeCompare(String(a.createdAt || '')));
  });

  constructor() {
    this.activeTab.set(this.tabIndexFromQuery(this.route.snapshot.queryParamMap.get('tab') || ''));
    this.load(); this.projectControl.valueChanges.subscribe(() => this.refreshProjectContext());
    this.api.appSettings().subscribe({ next: (res) => { if (res?.['company']) this.companyInfo.set(res['company']); }, error: () => {} });
  }
  ngOnDestroy(): void { if (this.presenceTimer) window.clearInterval(this.presenceTimer); }

  load(): void {
    this.loading.set(true);
    // Bewusst kleine Limits: Shared-Hosting vertraegt nur wenige gleichzeitige
    // PHP-Prozesse. Eine per Link angeforderte (auch alte oder abgeschlossene)
    // Bauakte wird unten gezielt einzeln nachgeladen – dafuer ist kein grosses
    // Limit noetig.
    forkJoin({ projects: this.api.list('projects','',150), customers: this.api.list('customers','',250), users: this.api.list('users','',250), screedTypes: this.api.list('screed_types','',250), suppliers: this.api.list('suppliers','',250) }).subscribe({
      next: ({projects, customers, users, screedTypes, suppliers}) => {
        this.projects.set(projects.items); this.customers.set(customers.items); this.users.set(users.items || []); this.screedTypes.set(screedTypes.items || []); this.suppliers.set(suppliers.items || []);
        this.loading.set(false);
        const queryId = this.route.snapshot.queryParamMap.get('project_id') || '';
        if (queryId) { this.ensureProjectLoaded(queryId); return; }
        const first = this.activeProjects()[0]?.['id'] || projects.items[0]?.['id'] || '';
        if (first) this.projectControl.setValue(String(first));
      },
      error: () => { this.loading.set(false); this.snack.open('Bauakten konnten nicht geladen werden.', 'OK', { duration: 3000 }); }
    });
  }

  /** Stellt sicher, dass die per Link angeforderte Bauakte wirklich geoeffnet wird –
   *  notfalls wird sie einzeln nachgeladen (z. B. sehr alte oder abgeschlossene Akten). */
  private ensureProjectLoaded(projectId: string): void {
    const known = this.projects().some((p) => String(p['id']) === projectId);
    if (known) { this.projectControl.setValue(projectId); return; }
    this.api.get('projects', projectId).subscribe({
      next: (project) => {
        if (project && project['id']) {
          this.projects.update((list) => [project, ...list.filter((p) => String(p['id']) !== projectId)]);
          this.projectControl.setValue(projectId);
        } else { this.fallbackProject(); }
      },
      error: () => { this.snack.open('Diese Bauakte konnte nicht geladen werden.', 'OK', { duration: 3500 }); this.fallbackProject(); }
    });
  }
  private fallbackProject(): void {
    const first = this.activeProjects()[0]?.['id'] || this.projects()[0]?.['id'] || '';
    if (first) this.projectControl.setValue(String(first));
  }

  selectProject(projectId: string): void { this.projectControl.setValue(String(projectId || '')); }

  refreshProjectContext(): void {
    const project = this.selectedProject(); if(!project) return;
    this.contextLoading.set(true);
    this.api.siteFile(project['id']).subscribe({ next: (file) => { const sites = file['sites'] || []; const documents = file['documents'] || []; const photos = file['photos'] || []; this.sites.set(sites); this.documents.set(documents); this.photos.set(this.dedupeRecords(photos)); this.reports.set(file['reports'] || []); this.inspections.set(file['inspections'] || []); this.quality.set(file['quality'] || []); this.shares.set(file['shares'] || []); this.tasks.set(file['tasks'] || []); this.presence.set(file['presence'] || []); this.selectedReport.set(null); this.patchDetailsForm(); this.patchContactForm(); if (photos[0]) this.openPhoto(photos[0]); else if (documents[0]) this.openDocument(documents[0]); else { this.selectedDoc.set(null); this.rawViewerUrl.set(''); this.safeViewerUrl.set(null); this.photoViewerUrl.set(''); } this.contextLoading.set(false); }, error: () => { this.contextLoading.set(false); this.snack.open('Bauakte konnte nicht geladen werden.', 'OK', { duration: 3000 }); } });
    this.history.set([]);
    this.loadNotes();
    this.loadMaterialOrders();
    this.loadProjectAppointments(String(project['id']));
    // Räume werden erst beim Öffnen des Reiters geladen (siehe onTabChange).
    // Das spart bei jedem Aufruf einer Bauakte einen Serveraufruf – die
    // Aufstellung wird von den wenigsten sofort gebraucht.
    this.rooms.set([]);
    this.roomsLoaded = false;
    this.pingPresence(project['id']); if (this.presenceTimer) window.clearInterval(this.presenceTimer); this.presenceTimer = window.setInterval(() => this.pingPresence(project['id']), 60000);
  }
  dedupeRecords(items: GenericRecord[]): GenericRecord[] {
    const seen = new Set<string>();
    return (items || []).filter((item) => { const key = String(item['id'] || item['file_path'] || item['thumb_path'] || item['url'] || JSON.stringify(item)); if (seen.has(key)) return false; seen.add(key); return true; });
  }
  loadMaterialOrders(): void { const project = this.selectedProject(); if (!project) return; this.api.projectMaterialOrders(String(project['id'])).subscribe({ next: (res) => this.materialOrders.set(res.items || []), error: () => this.materialOrders.set([]) }); }
  materialStatusLabel(status: unknown): string { return materialOrderStatusLabel(status); }
  materialItemPreview(order: GenericRecord): string { const items = Array.isArray(order['items']) ? order['items'] as GenericRecord[] : []; return items.length ? items.map((i) => `${i['quantity'] || ''} ${i['unit'] || ''} ${i['description'] || ''}`.trim()).filter(Boolean).join(' · ') : 'Keine Positionen'; }
  openMaterialOrder(order: GenericRecord | null = null): void { const project = this.selectedProject(); if (!project) return; const ref = this.dialog.open(MaterialOrderDialogComponent, { width: 'min(880px, 96vw)', maxWidth: '96vw', autoFocus: false, panelClass: ['ew-dialog', 'material-order-dialog'], data: { project, sites: this.sites(), suppliers: this.suppliers(), order } }); ref.afterClosed().subscribe((payload) => { if (!payload) return; const request = order ? this.api.updateMaterialOrder(String(order['id']), payload) : this.api.createProjectMaterialOrder(String(project['id']), payload); request.subscribe({ next: () => { this.loadMaterialOrders(); this.snack.open('Materialbestellung gespeichert.', 'OK', { duration: 2200 }); }, error: () => this.snack.open('Materialbestellung konnte nicht gespeichert werden.', 'OK', { duration: 3000 }) }); }); }
  downloadMaterialPdf(order: GenericRecord): void { downloadMaterialOrderPdf(order, this.selectedProject(), this.companyInfo()); }
  deleteMaterialOrder(order: GenericRecord): void { const ref = this.dialog.open(ConfirmDialogComponent, { width: 'min(460px, 96vw)', maxWidth: '96vw', data: { title: 'Materialbestellung löschen?', message: `Die Bestellung ${order['order_no'] || ''} wird gelöscht.`, confirmText: 'Löschen', icon: 'delete', tone: 'warn' } }); ref.afterClosed().subscribe((ok) => { if (!ok) return; this.api.deleteMaterialOrder(String(order['id'])).subscribe({ next: () => { this.loadMaterialOrders(); this.snack.open('Materialbestellung gelöscht.', 'OK', { duration: 2200 }); }, error: () => this.snack.open('Löschen fehlgeschlagen.', 'OK', { duration: 3000 }) }); }); }
  markInitialInspectionNotNeeded(): void { const project = this.selectedProject(); if (!project) return; const payload: GenericRecord = { project_id: project['id'], site_id: this.primarySite()?.['id'] || null, result: 'not_required', status: 'completed', result_json: { type: 'initial_site_inspection', result: 'not_required', notes: 'Keine Erstbegehung notwendig' }, notes: 'Keine Erstbegehung notwendig' }; this.api.saveInitialInspection(payload).subscribe({ next: () => { this.snack.open('Erstbegehung als nicht notwendig markiert.', 'OK', { duration: 2400 }); this.refreshProjectContext(); }, error: () => this.snack.open('Status konnte nicht gespeichert werden.', 'OK', { duration: 3000 }) }); }
  pingPresence(projectId: string): void { this.api.pingProjectPresence(projectId).subscribe({ next: () => {}, error: () => {} }); this.api.projectPresence(projectId).subscribe((res) => this.presence.set(res['items'] || [])); }

  setStatus(project: GenericRecord, status: string): void { this.api.update('projects', project['id'], { status }).subscribe({ next: (updated) => { this.projects.update((items) => items.map((item) => item['id'] === updated['id'] ? { ...item, ...updated } : item)); this.snack.open('Status gespeichert', 'OK', { duration: 1800 }); this.loadNotes(); }, error: () => this.snack.open('Status konnte nicht gespeichert werden.', 'OK', { duration: 3000 }) }); }
  completeOrder(project: GenericRecord): void {
    const ref = this.dialog.open(OrderConfirmDialogComponent, { width: 'min(460px,94vw)', panelClass: 'ew-dialog', data: { title: 'Bauakte abschließen', message: `Soll die Bauakte „${project['name'] || project['project_no'] || ''}" wirklich abgeschlossen werden? Der Status wird auf „Abgeschlossen" gesetzt.`, confirmLabel: 'Abschließen', icon: 'check_circle' } });
    ref.afterClosed().subscribe((ok) => {
      if (!ok) return;
      this.api.update('projects', project['id'], { status: 'completed' }).subscribe({
        next: () => {
          this.snack.open('Bauakte erfolgreich abgeschlossen.', 'OK', { duration: 3500 });
          void this.router.navigate(['/auftragssuche']);
        },
        error: () => this.snack.open('Bauakte konnte nicht abgeschlossen werden.', 'OK', { duration: 3500 })
      });
    });
  }
  cancelOrder(project: GenericRecord): void {
    const label = `${project['project_no'] || ''} ${project['name'] || ''}`.trim() || 'Bauakte';
    const ref = this.dialog.open(OrderCancelDialogComponent, {
      width: 'min(520px,94vw)',
      panelClass: 'ew-dialog',
      autoFocus: false,
      data: { projectLabel: label }
    });
    ref.afterClosed().subscribe((result: { password: string; reason: string } | undefined) => {
      if (!result || !result.password) return;
      this.api.cancelProject(String(project['id']), String(result.password), String(result.reason || '')).subscribe({
        next: (updated) => {
          let nextId = '';
          this.projects.update((items) => {
            const updatedItems = items.map((item) => item['id'] === project['id'] ? { ...item, ...updated, status: updated['status'] || 'cancelled', cancel_reason: result.reason || item['cancel_reason'] } : item);
            nextId = updatedItems.find((item) => item['id'] !== project['id'] && this.isActiveProject(item))?.['id'] || '';
            return updatedItems;
          });
          this.snack.open('Bauakte storniert' + (result.reason ? ' · Grund: ' + result.reason : ''), 'OK', { duration: 3200 });
          this.openProjectAfterDestructiveAction(nextId);
        },
        error: (err) => this.snack.open(err?.error?.error || 'Bauakte konnte nicht storniert werden.', 'OK', { duration: 3600 })
      });
    });
  }
  deleteOrder(project: GenericRecord): void {
    const label = `${project['project_no'] || ''} ${project['name'] || ''}`.trim();
    const ref = this.dialog.open(OrderPasswordConfirmDialogComponent, {
      width: 'min(540px,94vw)',
      panelClass: 'ew-dialog',
      data: {
        title: 'Bauakte endgültig löschen',
        message: `Achtung: Die Bauakte „${label || 'ohne Nummer'}" wird gelöscht. Verknüpfte Baustellen, Notizen, Dokumente, Fotos/Videos und die zugehörigen gespeicherten Dateien werden ebenfalls entfernt. Diese Aktion ist nur für Führungspositionen erlaubt und muss mit Passwort bestätigt werden.`,
        confirmLabel: 'Endgültig löschen'
      }
    });
    ref.afterClosed().subscribe((password) => {
      if (!password) return;
      this.api.deleteProject(String(project['id']), String(password)).subscribe({
        next: () => {
          this.projects.update((items) => items.filter((item) => item['id'] !== project['id']));
          this.resetViewer(); this.sites.set([]); this.documents.set([]); this.photos.set([]); this.reports.set([]); this.inspections.set([]); this.quality.set([]); this.shares.set([]); this.tasks.set([]); this.presence.set([]);
          this.snack.open('Bauakte gelöscht', 'OK', { duration: 2600 });
          void this.router.navigate(['/auftragssuche']);
        },
        error: (err) => this.snack.open(err?.error?.error || 'Bauakte konnte nicht gelöscht werden.', 'OK', { duration: 4200 })
      });
    });
  }
  openProjectAfterDestructiveAction(projectId: string): void {
    this.resetViewer(); this.sites.set([]); this.documents.set([]); this.photos.set([]); this.reports.set([]); this.inspections.set([]); this.quality.set([]); this.shares.set([]); this.tasks.set([]); this.presence.set([]);
    this.projectControl.setValue(projectId || '');
    if (projectId) void this.router.navigate(['/auftragsakte'], { queryParams: { project_id: projectId } });
    else void this.router.navigate(['/auftragsakte']);
  }
  patchDetailsForm(): void {
    const p = this.selectedProject(); const s = this.primarySite();
    this.detailsForm.reset({
      start_date: this.toIso(p?.['start_date']), end_date: this.toIso(p?.['end_date']),
      address: s?.['address'] || '', zip: s?.['zip'] || '', city: s?.['city'] || '',
      screed_type: s?.['screed_type'] || s?.['insulation_type'] || '', area_m2: s?.['area_m2'] ?? null,
      phone: s?.['phone'] || '', site_email: s?.['site_email'] || s?.['email'] || '', site_manager: s?.['site_manager'] || '', notes: p?.['notes'] || '', hinweise: s?.['hinweise'] || p?.['hinweise'] || ''
    });
    this.detailsEdit.set(false);
  }
  toIso(value: unknown): string { const raw = String(value ?? '').trim(); if (!raw || raw.startsWith('0000-00-00')) return ''; return raw.slice(0, 10); }
  startEditDetails(): void { this.detailsEdit.set(true); }
  cancelEditDetails(): void { this.patchDetailsForm(); }
  saveDetails(project: GenericRecord): void {
    const v = this.detailsForm.getRawValue();
    this.api.update('projects', project['id'], { start_date: v.start_date || null, end_date: v.end_date || null, notes: v.notes || null, hinweise: v.hinweise || null }).subscribe({
      next: (updated) => { this.projects.update((items) => items.map((it) => it['id'] === project['id'] ? { ...it, ...updated, start_date: v.start_date, end_date: v.end_date, notes: v.notes, hinweise: v.hinweise } : it)); },
      error: () => this.snack.open('Bauaktendaten konnten nicht gespeichert werden', 'OK', { duration: 3000 })
    });
    const site = this.primarySite();
    if (site) {
      this.api.update('construction_sites', site['id'], { address: v.address, zip: v.zip, city: v.city, screed_type: v.screed_type, area_m2: v.area_m2, phone: v.phone, site_email: v.site_email, site_manager: v.site_manager, notes: v.notes, hinweise: v.hinweise }).subscribe({
        next: () => { this.snack.open('Baustellendaten gespeichert', 'OK', { duration: 2000 }); this.refreshProjectContext(); },
        error: () => this.snack.open('Baustellendaten konnten nicht gespeichert werden', 'OK', { duration: 3000 })
      });
    } else {
      this.snack.open('Bauaktendaten gespeichert', 'OK', { duration: 2000 });
    }
    this.detailsEdit.set(false);
  }
  patchContactForm(): void {
    const customer = this.customerRecord(); const site = this.primarySite(); const project = this.selectedProject();
    this.contactForm.reset({
      contact_person: customer?.['contact_person'] || customer?.['name'] || '',
      email: customer?.['email'] || '',
      phone: customer?.['phone'] || '',
      site_manager: site?.['site_manager'] || '',
      site_phone: site?.['phone'] || '',
      site_email: site?.['site_email'] || site?.['email'] || '',
      site_address: site?.['address'] || '',
      site_zip: site?.['zip'] || '',
      site_city: site?.['city'] || '',
      hints: site?.['hinweise'] || project?.['hinweise'] || site?.['notes'] || project?.['notes'] || ''
    });
    this.contactEdit.set(false);
  }
  startEditContacts(): void { this.contactEdit.set(true); }
  cancelEditContacts(): void { this.patchContactForm(); }
  saveContacts(): void {
    const v = this.contactForm.getRawValue(); const customer = this.customerRecord(); const site = this.primarySite();
    let pending = 0; const done = () => { pending -= 1; if (pending <= 0) { this.snack.open('Kontaktdaten gespeichert', 'OK', { duration: 2000 }); this.refreshProjectContext(); } };
    if (site?.['id']) { pending += 1; this.api.update('construction_sites', site['id'], { site_manager: v.site_manager, phone: v.site_phone, site_email: v.site_email, address: v.site_address, zip: v.site_zip, city: v.site_city, hinweise: v.hints }).subscribe({ next: done, error: () => { pending -= 1; this.snack.open('Baustellenkontakt konnte nicht gespeichert werden', 'OK', { duration: 3200 }); } }); }
    this.contactEdit.set(false); if (!pending) this.snack.open('Keine Kontaktdaten zum Speichern vorhanden', 'OK', { duration: 2200 });
  }
  coverPhoto(): GenericRecord | null { return this.photos()[0] || null; }
  coverPhotoUrl(): string { const p = this.coverPhoto(); return p ? this.api.photoViewUrl(String(p['id']), 'main') : ''; }
  openCover(): void { const url = this.coverPhotoUrl(); if (url) this.lightboxUrl.set(url); }
  hasSelection(): boolean { return !!this.photoViewerUrl() || !!this.rawViewerUrl() || !!this.selectedReport(); }
  openSelectedLarge(): void {
    if (this.photoViewerUrl()) { this.lightboxUrl.set(this.photoViewerUrl()); return; }
    if (this.rawViewerUrl()) {
      // In der installierten App würde ein neues Fenster ohne Bedienleiste
      // aufgehen. Dort stattdessen die eigene Vollbildebene nutzen.
      if (this.isStandaloneApp()) { this.viewerFullscreen.set(true); return; }
      window.open(this.rawViewerUrl(), '_blank');
    }
  }
  /**
   * Herunterladen über einen unsichtbaren Verweis statt über ein neues
   * Fenster. In der installierten App bliebe ein Fenster ohne Bedienleiste
   * stehen; ein Verweis mit `download` löst dagegen den Speichern-Dialog des
   * Geräts aus und lässt die App im Vordergrund.
   */
  private triggerDownload(url: string, filename?: string): void {
    const a = document.createElement('a');
    a.href = url;
    a.rel = 'noopener';
    if (filename) a.download = filename;
    if (!this.isStandaloneApp()) a.target = '_blank';
    document.body.appendChild(a);
    a.click();
    a.remove();
  }
  downloadSelected(): void {
    const doc = this.selectedDoc();
    if (doc) { this.triggerDownload(this.api.documentDownloadUrl(String(doc['id'])), String(doc['name'] || doc['file_name'] || '')); return; }
    if (this.photoViewerUrl()) { this.triggerDownload(this.photoViewerUrl()); }
  }
  openDocument(doc: GenericRecord): void {
    const url = this.api.documentViewUrl(doc['id']); this.selectedDoc.set(doc); this.rawViewerUrl.set(url); this.safeViewerUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url)); this.photoViewerUrl.set(''); this.selectedReport.set(null);
    this.wordHtml.set(''); this.wordLoading.set(false);
    if (this.isWordDoc(doc)) this.loadWordPreview(doc, url);
  }
  isPortalVisible(doc: GenericRecord): boolean { return Number(doc['customer_visible']) === 1 || doc['customer_visible'] === true; }
  togglePortalVisibility(doc: GenericRecord, ev?: Event): void {
    ev?.stopPropagation();
    const next = !this.isPortalVisible(doc);
    this.api.setDocumentPortalVisibility(String(doc['id']), next).subscribe({
      next: () => {
        this.documents.update((list) => list.map((d) => d['id'] === doc['id'] ? { ...d, customer_visible: next ? 1 : 0 } : d));
        this.snack.open(next ? 'Dokument im Kundenportal freigegeben.' : 'Dokument aus dem Kundenportal entfernt.', 'OK', { duration: 2500 });
      },
      error: () => this.snack.open('Freigabe konnte nicht geändert werden.', 'OK', { duration: 3000 })
    });
  }

  /** Echtes Senden: teilt die Datei (Bild/Video/PDF) über das System-Teilen-Menü
   *  (WhatsApp, E-Mail, AirDrop …). Fallback: Datei herunterladen. */
  async shareFile(doc: GenericRecord, ev?: Event): Promise<void> {
    ev?.stopPropagation();
    const url = this.api.documentViewUrl(doc['id']);
    const filename = String(doc['original_name'] || doc['name'] || 'datei');
    const title = filename;
    try {
      const nav = navigator as Navigator & { canShare?: (d: { files?: File[] }) => boolean };
      const response = await fetch(url);
      if (response.ok && typeof nav.share === 'function') {
        const blob = await response.blob();
        const type = blob.type || String(doc['mime_type'] || 'application/octet-stream');
        const file = new File([blob], filename, { type });
        if (typeof nav.canShare === 'function' && nav.canShare({ files: [file] })) {
          await nav.share({ files: [file], title });
          return;
        }
        // Datei-Sharing nicht unterstützt -> wenigstens Titel/URL teilen
        try { await nav.share({ title, text: filename }); return; } catch { /* weiter zum Download */ }
      }
      // Fallback: herunterladen (dann kann der Nutzer es selbst versenden)
      const dlBlob = response.ok ? await response.blob() : null;
      const dlUrl = dlBlob ? URL.createObjectURL(dlBlob) : url;
      const a = document.createElement('a');
      a.href = dlUrl; a.download = filename; a.rel = 'noopener';
      document.body.appendChild(a); a.click(); a.remove();
      if (dlBlob) setTimeout(() => URL.revokeObjectURL(dlUrl), 60000);
      this.snack.open('Datei wird heruntergeladen – Sie können sie nun versenden.', 'OK', { duration: 3000 });
    } catch (_e) {
      this.snack.open('Senden nicht möglich. Bitte Datei manuell herunterladen.', 'OK', { duration: 3500 });
    }
  }

  isWordDoc(doc: GenericRecord | null): boolean { return this.docKind(doc) === 'word'; }
  selectedIsWord(): boolean { return this.isWordDoc(this.selectedDoc()); }
  private loadWordPreview(doc: GenericRecord, url: string): void {
    // .doc (altes Binärformat) kann mammoth nicht zuverlässig -> Hinweis + Download.
    const name = String(doc['original_name'] || doc['name'] || '').toLowerCase();
    if (name.endsWith('.doc') && !name.endsWith('.docx')) { this.wordHtml.set('<p class="word-doc-fallback">Vorschau für das alte .doc-Format wird nicht unterstützt. Bitte herunterladen oder als .docx speichern.</p>'); return; }
    this.wordLoading.set(true);
    fetch(url, { credentials: 'include' })
      .then((r) => r.arrayBuffer())
      .then((buf) => import('mammoth/mammoth.browser' as any).then((m: any) => (m.default || m).convertToHtml({ arrayBuffer: buf })))
      .then((res: { value: string }) => { this.wordHtml.set(res?.value || '<p>Keine Inhalte gefunden.</p>'); this.wordLoading.set(false); })
      .catch(() => { this.wordHtml.set(''); this.wordLoading.set(false); this.snack.open('Word-Vorschau konnte nicht erzeugt werden – bitte herunterladen.', 'OK', { duration: 3500 }); });
  }
  openPhoto(photo: GenericRecord): void { this.photoViewerUrl.set(this.api.photoViewUrl(photo['id'], 'main')); this.rawViewerUrl.set(''); this.safeViewerUrl.set(null); this.selectedReport.set(null); }
  detectDocumentCategory(file: File, selected = ''): string {
    const name = String(file.name || '').toLowerCase();
    const mime = String(file.type || '').toLowerCase();
    if (mime.startsWith('image/')) return 'foto';
    if (mime.startsWith('video/')) return 'video';
    if (/angebot|offerte|quote/.test(name)) return 'angebot';
    if (/rechnung|invoice/.test(name)) return 'rechnung';
    if (/protokoll|pruef|prüf|qm|cm|mess|abnahme|erstbegehung/.test(name)) return 'qm';
    if (/plan|grundriss|zeichnung|skizze|vordruck/.test(name)) return 'vordruck';
    if (/kunde|vertrag|auftrag|beleg/.test(name)) return 'kundendokument';
    return selected || 'bauakte';
  }

  openUpload(mode: 'document' | 'media' = 'document'): void {
    const project = this.selectedProject();
    if (!project) { this.snack.open('Bitte zuerst Bauakte wählen', 'OK', { duration: 2500 }); return; }
    const ref = this.dialog.open(OrderUploadDialogComponent, { width: 'min(760px,96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', data: { project, sites: this.sites() } });
    ref.afterClosed().subscribe((payload?: GenericRecord & { files: File[] }) => {
      if (!payload?.files?.length) return;
      let done = 0;
      const siteId = payload['site_id'] === '__auto__' ? null : payload['site_id'];
      payload.files.forEach((file) => {
        const category = mode === 'media' ? (file.type.startsWith('video/') ? 'video' : 'foto') : this.detectDocumentCategory(file, String(payload['category'] || ''));
        this.api.uploadDocument(file, { site_id: siteId, project_id: project['id'], category, name: payload['name'] || file.name }).subscribe({
          next: () => { done += 1; if (done === payload.files.length) { this.snack.open('Dateien zur Bauakte gespeichert', 'OK', { duration: 2600 }); this.refreshProjectContext(); } },
          error: () => this.snack.open('Upload fehlgeschlagen: ' + file.name, 'OK', { duration: 3600 })
        });
      });
    });
  }

  // Direkter Foto-Upload aus dem Foto-Tab (nur Bilder; landen serverseitig in der Galerie).
  private uploadFilesToBauakte(files: File[], categoryFor: (f: File) => string, doneLabel: string): void {
    const project = this.selectedProject();
    if (!project || !files.length) return;
    const siteId = this.primarySite()?.['id'] ?? null;
    let done = 0;
    files.forEach((file) => {
      this.api.uploadDocument(file, { site_id: siteId, project_id: project['id'], category: categoryFor(file), name: file.name }).subscribe({
        next: () => { done += 1; if (done === files.length) { this.snack.open(doneLabel, 'OK', { duration: 2400 }); this.refreshProjectContext(); } },
        error: () => this.snack.open('Upload fehlgeschlagen: ' + file.name, 'OK', { duration: 3600 })
      });
    });
  }
  // Öffnet einen Datei-/Kamera-Dialog zuverlässig auch auf iOS/Android.
  // Wichtig: das <input> MUSS im DOM hängen, sonst ignoriert Safari input.click().
  private pickFiles(accept: string, handler: (files: File[]) => void): void {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = accept;
    input.multiple = true;
    // Kein "capture" erzwingen -> Nutzer kann Kamera ODER Galerie wählen (robuster auf Mobilgeräten).
    input.style.position = 'fixed';
    input.style.left = '-10000px';
    input.style.top = '0';
    input.style.width = '1px';
    input.style.height = '1px';
    input.style.opacity = '0';
    let cleaned = false;
    const cleanup = () => { if (cleaned) return; cleaned = true; try { input.remove(); } catch { /* ignore */ } };
    input.onchange = () => { const files = Array.from(input.files || []); handler(files); cleanup(); };
    // Falls der Nutzer den Dialog abbricht: nach Rückkehr des Fokus aufräumen.
    window.addEventListener('focus', () => setTimeout(cleanup, 1500), { once: true });
    document.body.appendChild(input);
    input.click();
  }
  openPhotoUpload(): void {
    const project = this.selectedProject();
    if (!project) { this.snack.open('Bitte zuerst Bauakte wählen', 'OK', { duration: 2500 }); return; }
    this.pickFiles('image/*', (files) => {
      const imgs = files.filter((f) => f.type.startsWith('image/') || /\.(jpe?g|png|webp|heic|heif)$/i.test(f.name));
      if (imgs.length) this.uploadFilesToBauakte(imgs, () => 'foto', imgs.length === 1 ? 'Foto gespeichert' : 'Fotos gespeichert');
    });
  }
  openVideoUpload(): void {
    const project = this.selectedProject();
    if (!project) { this.snack.open('Bitte zuerst Bauakte wählen', 'OK', { duration: 2500 }); return; }
    this.pickFiles('video/*', (files) => {
      const vids = files.filter((f) => f.type.startsWith('video/') || /\.(mp4|webm|mov|m4v|3gp)$/i.test(f.name));
      if (vids.length) this.uploadFilesToBauakte(vids, () => 'video', vids.length === 1 ? 'Video gespeichert' : 'Videos gespeichert');
    });
  }
  // Nur echte Dokumente (keine Bilder, keine Videos) für den Dokumente-Tab.
  documentEntries(): AttachmentEntry[] { return this.combinedAttachments().filter((entry) => entry.type === 'document'); }

  openAssignment(area: string = 'bauakte'): void {
    const project = this.selectedProject();
    if (!project) { this.snack.open('Bitte zuerst Bauakte wählen', 'OK', { duration: 2500 }); return; }
    const ref = this.dialog.open(OrderAssignmentDialogComponent, { width: 'min(760px,96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', data: { project, sites: this.sites(), users: this.users() } });
    ref.afterClosed().subscribe((payload?: GenericRecord) => {
      if (!payload) return;
      this.api.assignProject(project['id'], payload['assigned_user_id']).subscribe({
        next: (res) => { this.projects.update((items) => items.map((it) => it['id'] === project['id'] ? { ...it, assigned_user_id: res['assigned_user_id'], assigned_user_name: res['assigned_user_name'] } : it)); this.snack.open('Bauakte zugewiesen', 'OK', { duration: 2000 }); },
        error: () => this.snack.open('Zuweisung konnte nicht gespeichert werden', 'OK', { duration: 3200 })
      });
    });
  }

  openAppointment(): void { const project=this.selectedProject(); if(!project){ this.snack.open('Bitte zuerst Bauakte wählen','OK',{duration:2500}); return; } const ref=this.dialog.open(OrderAppointmentDialogComponent,{width:'min(720px,96vw)', maxWidth:'96vw', panelClass:'ew-dialog', data:{project, sites:this.sites()}}); ref.afterClosed().subscribe((payload?: GenericRecord)=>{ if(!payload) return; this.api.create('calendar_events', payload).subscribe({next:()=>this.snack.open('Termin gespeichert','OK',{duration:2200}), error:()=>this.snack.open('Termin konnte nicht gespeichert werden','OK',{duration:3200})}); }); }
  async printFile(): Promise<void> {
    const p = this.selectedProject();
    if (!p) return;
    const site = this.primarySite();
    const customer = this.customerRecord();
    const docs = this.documentFiles();
    const photos = this.photos();
    const reports = this.dailyEntries().filter((entry) => entry.type === 'report');
    const inspection = this.initialInspection();
    const inspectionData = inspection ? this.inspectionData(inspection) : null;
    const checklist = (inspectionData?.['checklist'] || {}) as GenericRecord;
    const measurements = (inspectionData?.['measurements'] || {}) as GenericRecord;
    const doc = new jsPDF({ unit: 'pt', format: 'a4' });
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();
    const M = 38;
    const W = pageW - M * 2;
    let y = 42;
    let page = 1;
    const now = new Date();
    const INK: [number, number, number] = [0, 0, 0];
    const BAR: [number, number, number] = [226, 226, 226];   // helles Grau für Abschnittsbalken
    const ZEBRA: [number, number, number] = [244, 244, 244];
    const clean = (value: unknown, fallback = '—') => {
      const raw = String(value ?? '').trim();
      if (!raw || raw === 'null' || raw === 'undefined') return fallback;
      const normalized = raw.replace(' ', 'T');
      if (/^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2})?)?/.test(normalized)) {
        const date = new Date(normalized);
        if (!Number.isNaN(date.getTime())) {
          const hasTime = /T\d{2}:\d{2}/.test(normalized);
          return hasTime
            ? new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }).format(date)
            : new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(date);
        }
      }
      return raw;
    };
    const footer = () => {
      doc.setDrawColor(...INK); doc.setLineWidth(0.5); doc.line(M, pageH - 38, pageW - M, pageH - 38);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); doc.setTextColor(...INK);
      doc.text(this.companyName() + ' · Bauaktenformular', M, pageH - 24);
      doc.text(`Seite ${page}`, pageW - M, pageH - 24, { align: 'right' });
    };
    const newPage = () => { footer(); doc.addPage(); page += 1; y = 42; };
    const ensure = (h: number) => { if (y + h > pageH - 60) newPage(); };
    const titleBox = (title: string, _subtitle = '', minBodyHeight = 58) => {
      // Abschnittsueberschrift + erster Inhaltsblock bleiben zusammen auf einer Seite.
      if (y > 54) y += 14;
      const needed = Math.max(210, 50 + minBodyHeight);
      if (y + needed > pageH - 60) newPage();
      const barH = 28;
      doc.setFillColor(226, 226, 226); doc.rect(M, y, W, barH, 'F');          // grauer Balken
      doc.setDrawColor(0, 0, 0); doc.setLineWidth(0.9); doc.rect(M, y, W, barH, 'S');
      doc.setFillColor(0, 0, 0); doc.rect(M, y, 6, barH, 'F');                 // schwarzer Akzent links
      doc.setFont('helvetica', 'bold'); doc.setFontSize(11.5); doc.setTextColor(0, 0, 0);
      doc.text(title.toUpperCase(), M + 14, y + 19);
      y += barH + 12;
    };
    const field = (label: string, value: unknown, x: number, width: number, h = 28) => {
      doc.setDrawColor(...INK); doc.setLineWidth(0.4); doc.rect(x, y, width, h);
      doc.setFont('helvetica', 'bold'); doc.setFontSize(6.6); doc.setTextColor(...INK); doc.text(label.toUpperCase(), x + 5, y + 8.5);
      doc.setFont('helvetica', 'bold'); doc.setFontSize(9); doc.setTextColor(...INK);
      const lines = doc.splitTextToSize(clean(value), width - 10) as string[];
      doc.text(lines.slice(0, Math.max(1, Math.floor((h - 13) / 10))), x + 5, y + 20);
    };
    const row = (items: Array<[string, unknown]>, h = 28) => {
      ensure(h + 2);
      const width = W / items.length;
      items.forEach(([label, value], i) => field(label, value, M + i * width, width, h));
      y += h;
    };
    const memoLine = (label: string, value: unknown, h = 52) => {
      ensure(h + 4);
      doc.setDrawColor(...INK); doc.setLineWidth(0.4); doc.rect(M, y, W, h);
      doc.setFillColor(...BAR); doc.rect(M, y, W, 15, 'F'); doc.setDrawColor(...INK); doc.line(M, y + 15, M + W, y + 15);
      doc.setFont('helvetica', 'bold'); doc.setFontSize(8); doc.setTextColor(...INK); doc.text(label.toUpperCase(), M + 6, y + 10.5);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(8.4); doc.setTextColor(...INK);
      doc.text(doc.splitTextToSize(clean(value), W - 14) as string[], M + 6, y + 27);
      y += h;
    };
    const table = (headers: string[], rows: unknown[][], widths: number[], maxRows = 10) => {
      const rowH = 20;
      ensure(rowH * (Math.min(rows.length, maxRows) + 1) + 8);
      // Kopfzeile: grauer Hintergrund, dann Rahmen + Text
      let x = M;
      doc.setFillColor(226, 226, 226);
      headers.forEach((_, i) => { doc.rect(x, y, widths[i], rowH, 'F'); x += widths[i]; });
      x = M;
      doc.setDrawColor(0, 0, 0); doc.setLineWidth(0.5);
      doc.setFont('helvetica', 'bold'); doc.setFontSize(7.5); doc.setTextColor(0, 0, 0);
      headers.forEach((header, i) => { doc.rect(x, y, widths[i], rowH, 'S'); doc.text(String(header).toUpperCase(), x + 4, y + 13); x += widths[i]; });
      y += rowH;
      // Datenzeilen: Zebra-Füllung getrennt von Rahmen + Text (vermeidet FD-Statusfehler)
      doc.setFont('helvetica', 'normal'); doc.setFontSize(7.2);
      rows.slice(0, maxRows).forEach((r, ri) => {
        x = M;
        if (ri % 2) { doc.setFillColor(244, 244, 244); r.forEach((_, i) => { doc.rect(x, y, widths[i], rowH, 'F'); x += widths[i]; }); }
        x = M;
        doc.setDrawColor(0, 0, 0); doc.setLineWidth(0.4); doc.setTextColor(0, 0, 0);
        r.forEach((cell, i) => {
          doc.rect(x, y, widths[i], rowH, 'S');
          const txt = doc.splitTextToSize(clean(cell), Math.max(10, widths[i] - 8)) as string[];
          if (txt.length) doc.text(txt.slice(0, 1), x + 4, y + 13);
          x += widths[i];
        });
        y += rowH;
      });
      doc.setTextColor(0, 0, 0);
    };

    // ---- Kopf: klar gegliederter Formularkopf (Behörden-Stil) ----
    const headH = 96;
    doc.setDrawColor(...INK); doc.setLineWidth(1.6); doc.rect(M, y, W, headH);
    // Obere Leiste: Firma + Dokumenttyp-Stempel rechts
    doc.setFillColor(...BAR); doc.rect(M, y, W, 24, 'F'); doc.setDrawColor(...INK); doc.setLineWidth(0.6); doc.line(M, y + 24, M + W, y + 24);
    doc.setFont('helvetica', 'bold'); doc.setFontSize(10.5); doc.setTextColor(...INK); doc.text(this.companyNameUpper(), M + 10, y + 16);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); doc.text('Formular F-01 · Bauakte', pageW - M - 10, y + 16, { align: 'right' });
    // Linke Zone: großer Titel + Untertitel
    doc.setFont('helvetica', 'bold'); doc.setFontSize(24); doc.text('BAUAKTE', M + 10, y + 56);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5); doc.text('Offizieller Export der digitalen Bauakte', M + 10, y + 74);
    doc.setFontSize(8); doc.text(doc.splitTextToSize(clean(p['name']), W * 0.5).slice(0, 1) as string[], M + 10, y + 87);
    // Rechte Zone: Infobox mit Nr./Status/Datum
    const boxW = 188; const boxX = M + W - boxW;
    doc.setDrawColor(...INK); doc.setLineWidth(0.6); doc.line(boxX, y + 24, boxX, y + headH);
    const infoRow = (label: string, value: string, yy: number, strong = false) => {
      doc.setFont('helvetica', 'normal'); doc.setFontSize(7); doc.setTextColor(90); doc.text(label.toUpperCase(), boxX + 10, yy);
      doc.setFont('helvetica', 'bold'); doc.setFontSize(strong ? 12 : 9.5); doc.setTextColor(...INK); doc.text(value, boxX + 10, yy + 11);
    };
    infoRow('Bauakten-Nr.', clean(p['project_no']), y + 34, true);
    infoRow('Status', this.statusLabel(String(p['status'] || '')).toUpperCase(), y + 60);
    infoRow('Erstellt am', now.toLocaleDateString('de-DE'), y + 82);
    doc.setTextColor(...INK);
    y += headH + 16;

    titleBox('I. Allgemeine Angaben', 'Bauakte / Auftraggeber / Baustelle', 140);
    row([['Bauakten-Nr.', p['project_no']], ['Bezeichnung', p['name']]], 34);
    row([['Kunde', customer?.['name'] || p['customer_name'] || this.customerName(p['customer_id'])], ['Status', this.statusLabel(String(p['status'] || ''))]], 34);
    row([['Adresse Baustelle', site?.['address']], ['PLZ / Ort', [site?.['zip'], site?.['city']].filter(Boolean).join(' ')]], 34);
    row([['Beginn', this.fmtDate(p['start_date'])], ['Ende', this.fmtDate(p['end_date'])], ['Fläche', site?.['area_m2'] ? `${site?.['area_m2']} m²` : '—']], 32);
    row([['Estrichart', site?.['screed_type']], ['Fußbodenheizung', clean(checklist['underfloor_heating'], '—')], ['Zuständig', p['assigned_user_name'] || this.userName(p['assigned_user_id'])]], 32);
    memoLine('Hinweise zur Baustelle', site?.['hinweise'] || p['hinweise'] || site?.['notes'] || p['notes'], 56);

    titleBox('II. Erstbegehung', inspection ? 'Prüfung vor Ausführung' : 'Keine Erstbegehung hinterlegt', inspection ? 150 : 70);
    if (inspection && inspectionData) {
      row([['Datum', inspection['signed_at'] || inspection['created_at']], ['Prüfer', inspection['signed_by'] || inspectionData['context']?.['inspector_name']], ['Ergebnis', this.inspectionResultLabel(inspection)]], 32);
      row([['Estrichart', checklist['screed_type'] || site?.['screed_type']], ['Untergrund', checklist['substrate_status'] || checklist['notes']], ['Meterriss', clean(checklist['meter_mark'], '—')]], 32);
      row([['Temperatur', measurements['temperature_c'] ? `${measurements['temperature_c']} °C` : '—'], ['Luftfeuchte', measurements['humidity_percent'] ? `${measurements['humidity_percent']} %` : '—'], ['CM / Feuchte', measurements['cm_percent'] || '—']], 32);
      memoLine('Notizen Erstbegehung', inspection['notes'] || inspectionData['site_notes'] || checklist['site_notes'] || checklist['notes'], 54);
    } else {
      memoLine('Erstbegehung', 'In dieser Bauakte ist noch keine Erstbegehung hinterlegt.', 38);
    }

    titleBox('III. Tagesdokumentation / Qualität', 'Auszüge aus der Akte', reports.length ? 68 : 54);
    const reportRows = reports.slice(0, 8).map((entry) => [this.fmtDate(entry.dateValue), entry.title, entry.subtitle]);
    if (reportRows.length) table(['Datum', 'Eintrag', 'Details'], reportRows, [78, 150, W - 228], 8); else memoLine('Tagesdokumentation', 'Keine Tagesdokumentation hinterlegt.', 34);

    titleBox('IV. Anlagenverzeichnis', 'Dokumente und Dateien', 78);
    row([['Dokumente', String(docs.length)], ['Fotos', String(photos.length)], ['Tagesberichte', String(reports.length)]], 30);
    const fileRows = docs.slice(0, 12).map((file) => [clean(file['name'] || file['original_name'], 'Datei'), this.documentCategoryLabel(String(file['category'] || '')), this.fileSize(Number(file['size_bytes'] || 0)), this.fmtDate(file['created_at'])]);
    if (fileRows.length) table(['Datei', 'Kategorie', 'Größe', 'Datum'], fileRows, [245, 110, 70, W - 425], 12);

    titleBox('V. Kontakt / Freigabe', 'Kontaktangaben und Bestätigung', 104);
    row([['Kundenkontakt', customer?.['contact_person'] || customer?.['name']], ['Telefon', customer?.['phone']], ['E-Mail', customer?.['email']]], 32);
    row([['Baustellenkontakt', site?.['site_manager']], ['Telefon Baustelle', site?.['phone']], ['E-Mail Baustelle', site?.['site_email'] || site?.['email']]], 32);
    ensure(82); y += 18;
    doc.setDrawColor(40); doc.line(M, y + 36, M + 210, y + 36); doc.line(pageW - M - 210, y + 36, pageW - M, y + 36);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(8); doc.text('Ort, Datum', M, y + 51); doc.text('Unterschrift / Freigabe', pageW - M - 210, y + 51);
    y += 72;

    if (photos.length) {
      newPage();
      titleBox('VI. Fotodokumentation', 'Fotos werden ausschließlich in diesem Abschnitt ausgegeben', 224);
      const imgW = (W - 14) / 2;
      let col = 0;
      for (const photo of photos.slice(0, 18)) {
        if (col === 0) ensure(280);
        const x = M + col * (imgW + 14);
        const used = await this.addPhotoToPdf(doc, this.photoUrl(photo, 'original'), x, y, imgW, 235);
        doc.setFont('helvetica', 'normal'); doc.setFontSize(7.2);
        doc.text(doc.splitTextToSize(clean(photo['caption'] || photo['taken_at'] || photo['created_at'], 'Foto'), imgW) as string[], x, y + (used || 235) + 10);
        col += 1;
        if (col === 2) { col = 0; y += 268; }
      }
    }
    footer();
    this.savePdf(doc, `Bauakte_${String(p['project_no'] || p['name'] || 'akte').replace(/[^\w.-]+/g, '_')}.pdf`);
  }

  private savePdf(doc: jsPDF, filename: string): void {
    try {
      // Auf dem Handy oeffnen viele Browser das PDF zuverlaessiger ueber eine Blob-URL.
      const isMobile = typeof navigator !== 'undefined' && /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent || '');
      if (isMobile) {
        const blob = doc.output('blob');
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = filename; a.rel = 'noopener';
        document.body.appendChild(a); a.click(); a.remove();
        // Fallback: in neuem Tab oeffnen, falls Download blockiert wird.
        setTimeout(() => { try { window.open(url, '_blank'); } catch { /* ignore */ } setTimeout(() => URL.revokeObjectURL(url), 60000); }, 250);
        return;
      }
      doc.save(filename);
    } catch (_e) {
      try { doc.save(filename); } catch { /* ignore */ }
    }
  }
  private async addPhotoToPdf(doc: jsPDF, url: string, x: number, y: number, maxWidth: number, maxHeight: number): Promise<number> {
    try {
      const response = await fetch(url);
      if (!response.ok) return 0;
      const blob = await response.blob();
      const dataUrl = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result || ''));
        reader.onerror = () => reject(reader.error || new Error('Bild konnte nicht gelesen werden'));
        reader.readAsDataURL(blob);
      });
      // Natürliche Bildmaße ermitteln, damit das Seitenverhältnis erhalten bleibt (keine Verzerrung).
      const dims = await new Promise<{ w: number; h: number }>((resolve) => {
        const img = new Image();
        img.onload = () => resolve({ w: img.naturalWidth || 4, h: img.naturalHeight || 3 });
        img.onerror = () => resolve({ w: 4, h: 3 });
        img.src = dataUrl;
      });
      const format = blob.type.includes('png') ? 'PNG' : 'JPEG';
      const innerW = maxWidth - 12, innerH = maxHeight - 12;
      const scale = Math.min(innerW / dims.w, innerH / dims.h);
      const drawW = Math.max(1, dims.w * scale);
      const drawH = Math.max(1, dims.h * scale);
      const offX = x + 6 + (innerW - drawW) / 2;
      const offY = y + 6 + (innerH - drawH) / 2;
      doc.setDrawColor(208, 213, 221); doc.setFillColor(248, 250, 252);
      doc.rect(x, y, maxWidth, maxHeight, 'FD');
      doc.addImage(dataUrl, format, offX, offY, drawW, drawH, undefined, 'SLOW');
      return maxHeight;
    } catch (_err) {
      return 0;
    }
  }

  /** Dateiendung eines Dokuments (klein geschrieben, ohne Punkt). */
  docExtension(doc: GenericRecord | null): string {
    const name = String(doc?.['original_name'] || doc?.['name'] || doc?.['storage_path'] || '');
    const match = name.toLowerCase().match(/\.([a-z0-9]{1,5})$/);
    return match ? match[1] : '';
  }
  /** Art der Datei. Der gespeicherte MIME-Typ ist bei Handy-Uploads oft leer
   *  oder generisch, deshalb entscheidet zusaetzlich die Dateiendung. */
  docKind(doc: GenericRecord | null): 'pdf' | 'image' | 'video' | 'word' | 'excel' | 'other' {
    if (!doc) return 'other';
    const mime = String(doc['mime_type'] || '').toLowerCase();
    const ext = this.docExtension(doc);
    if (mime.includes('pdf') || ext === 'pdf') return 'pdf';
    if (mime.startsWith('video/') || ['mp4', 'webm', 'mov', 'm4v'].includes(ext)) return 'video';
    if (mime.startsWith('image/') || ['jpg', 'jpeg', 'png', 'webp', 'gif', 'heic'].includes(ext)) return 'image';
    if (mime.includes('wordprocessing') || mime.includes('msword') || ['doc', 'docx'].includes(ext)) return 'word';
    if (mime.includes('spreadsheet') || mime.includes('ms-excel') || ['xls', 'xlsx', 'csv'].includes(ext)) return 'excel';
    return 'other';
  }
  selectedKind(): 'pdf' | 'image' | 'video' | 'word' | 'excel' | 'other' { return this.docKind(this.selectedDoc()); }
  selectedIsPdf(): boolean { return this.selectedKind() === 'pdf'; }
  selectedIsVideo(): boolean { return this.selectedKind() === 'video'; }
  selectedIsImage(): boolean { return this.selectedKind() === 'image'; }
  selectedIsExcel(): boolean { return this.selectedKind() === 'excel'; }
  selectedName(): string { const d = this.selectedDoc(); return String(d?.['name'] || d?.['original_name'] || 'Dokument'); }
  selectedKindLabel(): string {
    const map: Record<string, string> = { pdf: 'PDF-Dokument', image: 'Bild', video: 'Video', word: 'Word-Dokument', excel: 'Tabelle', other: 'Datei' };
    return map[this.selectedKind()] || 'Datei';
  }
  docIcon(doc: GenericRecord): string { const mime = String(doc['mime_type'] || ''); if(mime.includes('pdf')) return 'picture_as_pdf'; if(mime.startsWith('video/')) return 'movie'; if(mime.startsWith('image/')) return 'image'; return 'description'; }
  normalize(value: unknown): string { return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }
  customerName(id: string): string { return this.customers().find((customer) => customer['id'] === id)?.['name'] || '—'; }
  userName(id: string): string { return this.users().find((user) => user['id'] === id)?.['name'] || 'nicht zugewiesen'; }
  assignedUserEmail(id: string): string { return this.users().find((user) => user['id'] === id)?.['email'] || ''; }
  areaLabel(area: string): string { return ({ bauakte:'Bauakte', termin:'Termin', qm:'QM', dokumentation:'Dokumentation', mangel:'Mangel' } as Record<string,string>)[area] || 'Bauakte'; }
  priorityLabel(priority: string): string { return ({ low:'Niedrig', normal:'Normal', high:'Hoch', urgent:'Dringend' } as Record<string,string>)[priority] || priority || 'Normal'; }
  openQualityCount(): number { return this.quality().filter((q) => !['approved','closed','archived'].includes(String(q['status'] || ''))).length; }
  statusIndex(status: string): number { return Math.max(0, this.statusSteps.findIndex((step) => step.key === status)); }
  statusLabel(status: string): string { return ({new:'Neu',planned:'Geplant',in_progress:'In Arbeit',completed:'Abgeschlossen',cancelled:'Storniert',closed:'Geschlossen',open:'Offen'} as Record<string,string>)[status] || status || 'Offen'; }
  historyIcon(action: string): string { if(action === 'create') return 'add_circle'; if(action === 'update') return 'edit'; if(action === 'delete') return 'delete'; return 'history'; }
  bauakteTools(): Array<{ key: string; label: string; icon: string; description: string }> {
    return [
      { key: 'bautagebuch', label: 'Bautagebuch', icon: 'edit_note', description: 'Tagesberichte mit Fotos, Videos, Wetter, Mitarbeitern und Notizen.' },
      { key: 'zeiterfassung', label: 'Zeiterfassung', icon: 'schedule', description: 'Arbeitszeiten, Pausen, Fahrtzeiten und Kolonnenbezug dokumentieren.' },
      { key: 'chat', label: 'Baustellenkommunikation', icon: 'forum', description: 'Rückfragen, Abstimmungen und Entscheidungen direkt in der Akte sammeln.' },
      { key: 'material', label: 'Material & Maschinen', icon: 'inventory_2', description: 'Materialverbrauch, Maschinen und Fahrzeuge zur Ausführung festhalten.' },
      { key: 'formulare', label: 'Formulare & Abnahmen', icon: 'fact_check', description: 'Erstbegehung, QM, Abnahme und individuelle Prüfprotokolle führen.' },
      { key: 'dateien', label: 'Dateianlagen', icon: 'folder', description: 'Dokumente, Bilder und Videos strukturiert ablegen und anzeigen.' },
      { key: 'lexware', label: 'Lexware Office', icon: 'sync', description: 'Kunden, Belege und Auftragsdaten mit Lexware Office abgleichen.' }
    ];
  }

  runTool(key: string): void {
    if (key === 'bautagebuch') { window.location.href = '/tagesdokumentation?project_id=' + encodeURIComponent(String(this.selectedProject()?.['id'] || '')); return; }
    if (key === 'formulare') { window.location.href = '/erstbegehung-wizard?project_id=' + encodeURIComponent(String(this.selectedProject()?.['id'] || '')); return; }
    if (key === 'dateien') { this.openUpload('document'); return; }
    if (key === 'aufgaben' || key === 'chat' || key === 'zeiterfassung' || key === 'material') { this.openAssignment(key === 'material' ? 'dokumentation' : 'bauakte'); return; }
    if (key === 'lexware') { window.location.href = '/lexware-integration'; return; }
  }

  fileSize(bytes: number): string { if(!bytes) return '—'; if(bytes > 1024*1024) return (bytes/1024/1024).toFixed(1)+' MB'; return Math.round(bytes/1024)+' KB'; }
  customerRecord(): GenericRecord | null { const project = this.selectedProject(); return this.customers().find((customer) => customer['id'] === project?.['customer_id']) || null; }
  projectAddress(): string { const site = this.primarySite(); if (!site) return 'Adresse noch nicht hinterlegt'; return [site['address'], site['zip'], site['city']].filter(Boolean).join(', '); }
  photoUrl(photo: GenericRecord, variant: 'main' | 'thumb' | 'original' = 'main'): string { return this.api.photoViewUrl(photo['id'], variant); }
  photoUrlById(id: string, variant: 'main' | 'thumb' | 'original' = 'main'): string { return this.api.photoViewUrl(id, variant); }
  openLightbox(photo: GenericRecord, index?: number): void {
    const list = this.photos();
    const idx = typeof index === 'number' ? index : Math.max(0, list.findIndex((p) => p['id'] === photo['id']));
    this.lightboxList.set(list.length ? list : [photo]);
    this.lightboxIndex.set(idx < 0 ? 0 : idx);
    this.lightboxUrl.set(this.api.photoViewUrl(photo['id'], 'main'));
  }
  /** Foto der Erstbegehung groß öffnen – mit Blättern durch alle Fotos. */
  openLightboxById(id: unknown): void {
    const key = String(id ?? '');
    if (!key) return;
    const list = this.photos();
    const idx = list.findIndex((p) => String(p['id']) === key);
    this.lightboxList.set(idx >= 0 ? list : []);
    this.lightboxIndex.set(idx >= 0 ? idx : 0);
    this.lightboxUrl.set(this.api.photoViewUrl(key, 'main'));
  }
  closeLightbox(): void { this.lightboxUrl.set(''); this.lightboxList.set([]); this.lightboxIndex.set(0); }
  private currentLightboxPhoto(): GenericRecord | null { const list = this.lightboxList(); const idx = this.lightboxIndex(); return list[idx] || null; }
  shareCurrentLightbox(ev?: Event): void { ev?.stopPropagation(); const p = this.currentLightboxPhoto(); if (p) void this.shareFile(p); }
  deleteCurrentLightbox(ev?: Event): void {
    ev?.stopPropagation();
    const p = this.currentLightboxPhoto(); if (!p) return;
    const id = String(p['id']);
    // Lightbox schließen und über den bestehenden Lösch-Dialog löschen.
    this.closeLightbox();
    this.deleteAttachment('photos', p);
    // Sofort optimistisch aus der Liste nehmen, falls bestätigt (deleteAttachment aktualisiert ohnehin).
    void id;
  }
  private showLightboxAt(index: number): void {
    const list = this.lightboxList();
    if (!list.length) return;
    const wrapped = (index + list.length) % list.length;
    this.lightboxIndex.set(wrapped);
    this.lightboxUrl.set(this.api.photoViewUrl(list[wrapped]['id'], 'main'));
  }
  lightboxNext(event?: Event): void { event?.stopPropagation(); this.showLightboxAt(this.lightboxIndex() + 1); }
  lightboxPrev(event?: Event): void { event?.stopPropagation(); this.showLightboxAt(this.lightboxIndex() - 1); }
  onLightboxTouchStart(event: TouchEvent): void { this.lightboxTouchX = event.changedTouches[0]?.clientX ?? 0; }
  onLightboxTouchEnd(event: TouchEvent): void {
    if (this.lightboxList().length < 2) return;
    const dx = (event.changedTouches[0]?.clientX ?? 0) - this.lightboxTouchX;
    if (Math.abs(dx) < 45) return;
    if (dx < 0) this.lightboxNext(); else this.lightboxPrev();
  }
  fmtDate(value: unknown): string {
    const raw = String(value ?? '').trim();
    if (!raw || raw.startsWith('0000-00-00') || raw === '0000-00-00 00:00:00') return '—';
    const date = new Date(raw.replace(' ', 'T'));
    if (isNaN(date.getTime())) return '—';
    return date.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }
  documentCategoryLabel(category: string): string { return ({ bauakte: 'Bauakte', vordruck: 'Vordruck / Plan', qm: 'QM / Prüfung', foto: 'Foto', video: 'Video', angebot: 'Angebot', rechnung: 'Rechnung', kundendokument: 'Kundendokument', daily_video: 'Tagesvideo', daily_photo: 'Tagesfoto' } as Record<string,string>)[category] || category || 'Dokument'; }
  latestHistory(): GenericRecord[] { return [...this.history()].slice(0, 10); }
  dailyEntries(): DailyEntry[] {
    const reportEntries = this.reports().map((report) => ({ record: report, dateValue: String(this.reportField(report, 'report_date') || report['created_at'] || report['updated_at'] || ''), title: report['title'] || 'Tagesbericht', subtitle: [this.reportField(report, 'crew_text'), this.reportField(report, 'work_done')].filter(Boolean).join(' · ') || 'Tagesdokumentation', typeLabel: 'Tagesbericht', type: 'report' as const }));
    const photos = this.photos().map((photo) => ({ record: photo, dateValue: String(photo['taken_at'] || photo['created_at'] || ''), title: photo['caption'] || 'Foto', subtitle: this.projectAddress(), typeLabel: 'Foto', type: 'photo' as const }));
    const videos = this.videos().map((video) => ({ record: video, dateValue: String(video['created_at'] || video['updated_at'] || ''), title: video['name'] || video['original_name'] || 'Video', subtitle: this.documentCategoryLabel(String(video['category'] || 'video')), typeLabel: 'Video', type: 'video' as const }));
    const docs = this.documentFiles().filter((doc) => String(doc['category'] || '').includes('daily') || String(doc['name'] || '').toLowerCase().includes('tages')).map((doc) => ({ record: doc, dateValue: String(doc['created_at'] || doc['updated_at'] || ''), title: doc['name'] || doc['original_name'] || 'Dokument', subtitle: this.documentCategoryLabel(String(doc['category'] || '')), typeLabel: 'Dokument', type: 'document' as const }));
    return [...reportEntries, ...photos, ...videos, ...docs].sort((a, b) => String(b.dateValue || '').localeCompare(String(a.dateValue || '')));
  }
  openDailyEntry(entry: DailyEntry): void { if (entry.type === 'photo') this.openPhoto(entry.record); else if (entry.type === 'report') { this.selectedReport.set(entry.record); this.selectedDoc.set(null); this.rawViewerUrl.set(''); this.safeViewerUrl.set(null); this.photoViewerUrl.set(''); } else this.openDocument(entry.record); }
  openAttachment(entry: AttachmentEntry): void { if (entry.type === 'photo') this.openPhoto(entry.record); else this.openDocument(entry.record); }
  /**
   * Läuft die Anwendung als installierte App (ohne Adress- und Tableiste)?
   *
   * Genau dort lag das Problem: `window.open` öffnet in der installierten
   * App ein Fenster OHNE Bedienleiste. Aus einem so geöffneten PDF gibt es
   * keinen Weg zurück – kein Zurück-Knopf, keine Tabs. Der Nutzer sitzt fest
   * und muss die App beenden.
   */
  private isStandaloneApp(): boolean {
    try {
      return window.matchMedia('(display-mode: standalone)').matches
        || window.matchMedia('(display-mode: fullscreen)').matches
        || (window.navigator as unknown as { standalone?: boolean }).standalone === true;
    } catch { return false; }
  }

  /**
   * Anhang öffnen.
   *
   * In der installierten App bleibt die Anzeige INNERHALB der Anwendung –
   * die eigene Vollbildvorschau hat eine Kopfzeile mit "← Dokumente".
   * Im normalen Browser wird weiterhin ein Tab geöffnet, dort ist der Weg
   * zurück über die Tableiste vorhanden.
   */
  openAttachmentExternal(entry: AttachmentEntry): void {
    const id = String(entry.record['id'] ?? '');
    if (!id) return;
    // Am Handy IMMER innerhalb der Anwendung anzeigen – unabhängig davon, ob
    // sie im Browser oder installiert läuft. Ein neuer Tab ist dort
    // unpraktisch (Wechsel über die Tabübersicht), ein Download unnötig.
    if (this.isStandaloneApp() || window.innerWidth < 900) {
      this.openAttachment(entry);
      this.docSheetOpen.set(true);   // ausdrücklicher Aufruf
      return;
    }
    const url = entry.type === 'photo' ? this.api.photoViewUrl(id, 'main') : this.api.documentViewUrl(id);
    window.open(url, '_blank', 'noopener');
  }
  isAttachmentSelected(entry: AttachmentEntry): boolean { return entry.type === 'photo' ? this.photoViewerUrl() === this.photoUrl(entry.record, 'main') : this.selectedDoc()?.['id'] === entry.record['id']; }
  dailyGroups(): DailyGroup[] {
    const groups = new Map<string, DailyGroup>();
    for (const entry of this.dailyEntries()) {
      const day = String(entry.dateValue || '').slice(0, 10) || 'ohne-datum';
      let group = groups.get(day);
      if (!group) { group = { dateValue: day === 'ohne-datum' ? '' : day, dateLabel: day, entries: [], photoCount: 0, videoCount: 0, docCount: 0, reportCount: 0 }; groups.set(day, group); }
      group.entries.push(entry);
      if (entry.type === 'photo') group.photoCount += 1; else if (entry.type === 'video') group.videoCount += 1; else if (entry.type === 'report') group.reportCount += 1; else group.docCount += 1;
    }
    return [...groups.values()].sort((a, b) => String(b.dateLabel).localeCompare(String(a.dateLabel)));
  }
  tabIndexFromQuery(tab: string): number { return ({ kontakte: 1, fotos: 2, videos: 3, dokumente: 4, tagesuebersicht: 5, erstbegehung: 6, material: 7, notizen: 8 } as Record<string, number>)[tab] ?? 0; }
  reportContent(report: GenericRecord): GenericRecord { const raw = report['content'] || report['content_json'] || '{}'; if (typeof raw === 'object' && raw) return raw; try { const parsed = JSON.parse(String(raw || '{}')); return parsed && typeof parsed === 'object' ? parsed : {}; } catch (_err) { return {}; } }
  reportField(report: GenericRecord, key: string): string { const content = this.reportContent(report); return String(content[key] ?? report[key] ?? '').trim(); }
  reportDate(report: GenericRecord): string { return this.fmtDate(this.reportField(report, 'report_date') || report['created_at']); }
  inspectionData(inspection: GenericRecord): GenericRecord { const raw = inspection['result_json'] || '{}'; if (typeof raw === 'object' && raw) return raw; try { const parsed = JSON.parse(String(raw || '{}')); return parsed && typeof parsed === 'object' ? parsed : {}; } catch (_err) { return {}; } }
  inspectionField(inspection: GenericRecord, path: string): string { const data = this.inspectionData(inspection); const value = path.split('.').reduce<any>((acc, part) => acc && typeof acc === 'object' ? acc[part] : undefined, data); return String(value ?? '').trim(); }
  inspectionStatusLabel(status: string): string { return ({ completed: 'Abgeschlossen', rejected: 'Abgelehnt', draft: 'Entwurf', open: 'Offen' } as Record<string,string>)[status] || status || 'Offen'; }
  inspectionResultLabel(inspection: GenericRecord): string { return ({ passed: 'Bestanden / freigegeben', rejected: 'Abgelehnt', draft: 'Entwurf' } as Record<string,string>)[this.inspectionField(inspection, 'result')] || this.inspectionStatusLabel(inspection['status']); }
  isInspectionRejected(inspection: GenericRecord): boolean { return this.inspectionField(inspection, 'result') === 'rejected'; }
  yesNoLabel(value: unknown): string { const raw = String(value ?? '').trim().toLowerCase(); if (!raw) return '—'; if (['true','1','ja','yes','y'].includes(raw)) return 'Ja'; if (['false','0','nein','no','n'].includes(raw)) return 'Nein'; return String(value); }
  substrateStatusLabel(value: unknown): string { const raw = String(value ?? '').trim().toLowerCase(); if (!raw) return '—'; return ({ ok: 'In Ordnung', nacharbeit: 'Nacharbeit erforderlich', unklar: 'Unklar' } as Record<string, string>)[raw] || String(value); }
  inspectionPhotoIds(inspection: GenericRecord): string[] { const data = this.inspectionData(inspection); const ids = Array.isArray(data['photos']) ? data['photos'].filter(Boolean).map(String) : []; return Array.from(new Set(ids)); }
  setInspectionPdfOption(key: 'includeChecklist' | 'includeMeasurements' | 'includeNotes' | 'includePhotos', value: boolean): void { this.inspectionPdfOptions.update((current) => ({ ...current, [key]: value })); }
  async printInspectionPdf(inspection: GenericRecord): Promise<void> {
    const project = this.selectedProject();
    const site = this.primarySite();
    const data = this.inspectionData(inspection);
    const checklist = (data['checklist'] || {}) as GenericRecord;
    const measurements = (data['measurements'] || {}) as GenericRecord;
    const context = (data['context'] || {}) as GenericRecord;
    const photoIds = Array.isArray(data['photos']) ? data['photos'].filter(Boolean).map(String) : [];
    const doc = new jsPDF({ unit: 'pt', format: 'a4' });
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();
    const M = 34;
    const W = pageW - M * 2;
    let y = 36;
    let page = 1;
    const now = new Date();
    const INK: [number, number, number] = [0, 0, 0];
    const BAR: [number, number, number] = [226, 226, 226];
    const clean = (value: unknown, fallback = '—') => {
      const raw = String(value ?? '').trim();
      if (!raw || raw === 'null' || raw === 'undefined') return fallback;
      const normalized = raw.replace(' ', 'T');
      if (/^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2})?)?/.test(normalized)) {
        const date = new Date(normalized);
        if (!Number.isNaN(date.getTime())) {
          const hasTime = /T\d{2}:\d{2}/.test(normalized);
          return hasTime
            ? new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }).format(date)
            : new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(date);
        }
      }
      return raw;
    };
    const footer = () => {
      doc.setDrawColor(...INK); doc.setLineWidth(0.5); doc.line(M, pageH - 38, pageW - M, pageH - 38);
      doc.setFont('helvetica','normal'); doc.setFontSize(7.5); doc.setTextColor(...INK);
      doc.text(this.companyName() + ' · Erstbegehungsprotokoll', M, pageH - 24);
      doc.text(`Seite ${page}`, pageW - M, pageH - 24, { align: 'right' });
    };
    const newPage = () => { footer(); doc.addPage(); page += 1; y = 36; };
    const ensure = (h: number) => { if (y + h > pageH - 60) newPage(); };
    const section = (title: string, _subtitle = '', minBody = 60) => {
      // Abschnittsueberschrift und der erste Protokollblock bleiben zusammen.
      if (y > 48) y += 14;
      const needed = Math.max(210, 46 + minBody);
      if (y + needed > pageH - 60) newPage();
      const barH = 30;
      doc.setFillColor(226, 226, 226); doc.rect(M, y, W, barH, 'F');
      doc.setDrawColor(0, 0, 0); doc.setLineWidth(0.9); doc.rect(M, y, W, barH, 'S');
      doc.setFillColor(0, 0, 0); doc.rect(M, y, 6, barH, 'F');
      doc.setFont('helvetica','bold'); doc.setFontSize(11.5); doc.setTextColor(0, 0, 0);
      doc.text(title.toUpperCase(), M + 14, y + 20);
      y += barH + 12;
    };
    const row = (items: Array<[string, unknown]>, h = 34) => {
      ensure(h + 2);
      const width = W / items.length;
      items.forEach(([label, value], i) => {
        const x = M + i * width;
        doc.setDrawColor(...INK); doc.setLineWidth(0.4); doc.rect(x, y, width, h);
        doc.setFont('helvetica','bold'); doc.setFontSize(6.7); doc.setTextColor(...INK); doc.text(label.toUpperCase(), x + 5, y + 9.5);
        doc.setFont('helvetica','bold'); doc.setFontSize(9.2); doc.setTextColor(...INK);
        doc.text(doc.splitTextToSize(clean(value), width - 10).slice(0, 2) as string[], x + 5, y + 23);
      });
      y += h;
    };
    const memo = (label: string, value: unknown, h = 58) => {
      ensure(h + 4);
      doc.setDrawColor(...INK); doc.setLineWidth(0.4); doc.rect(M, y, W, h);
      doc.setFillColor(...BAR); doc.rect(M, y, W, 15, 'F'); doc.setDrawColor(...INK); doc.line(M, y + 15, M + W, y + 15);
      doc.setFont('helvetica','bold'); doc.setFontSize(8.2); doc.setTextColor(...INK); doc.text(label.toUpperCase(), M + 6, y + 11);
      doc.setFont('helvetica','normal'); doc.setFontSize(8.5); doc.setTextColor(...INK);
      doc.text(doc.splitTextToSize(clean(value), W - 14) as string[], M + 6, y + 28);
      y += h;
    };
    const yesNoSelect = (value: unknown) => { const raw = String(value ?? '').toLowerCase(); if (raw === 'ja') return 'Ja'; if (raw === 'nein') return 'Nein'; if (raw === 'unbekannt') return 'Unbekannt'; return this.yesNoLabel(value); };
    const substrateLabel = (value: unknown) => ({ ok: 'In Ordnung', nacharbeit: 'Nacharbeit erforderlich', unklar: 'Unklar' } as Record<string, string>)[String(value || '')] || clean(value);

    // ---- Kopf: klar gegliederter Formularkopf ----
    const headH = 96;
    doc.setDrawColor(...INK); doc.setLineWidth(1.6); doc.rect(M, y, W, headH);
    doc.setFillColor(...BAR); doc.rect(M, y, W, 24, 'F'); doc.setDrawColor(...INK); doc.setLineWidth(0.6); doc.line(M, y + 24, M + W, y + 24);
    doc.setFont('helvetica','bold'); doc.setFontSize(10.5); doc.setTextColor(...INK); doc.text(this.companyNameUpper(), M + 10, y + 16);
    doc.setFont('helvetica','normal'); doc.setFontSize(7.5); doc.text('Formular F-02 · Erstbegehung', pageW - M - 10, y + 16, { align: 'right' });
    doc.setFont('helvetica','bold'); doc.setFontSize(19); doc.text('ERSTBEGEHUNGSPROTOKOLL', M + 10, y + 54);
    doc.setFont('helvetica','normal'); doc.setFontSize(8.5); doc.text('Prüfung vor Ausführung · digitales Bauaktenformular', M + 10, y + 72);
    doc.setFontSize(8); doc.text(doc.splitTextToSize(clean(project?.['name']), W * 0.5).slice(0, 1) as string[], M + 10, y + 85);
    const boxW = 188; const boxX = M + W - boxW;
    doc.setDrawColor(...INK); doc.setLineWidth(0.6); doc.line(boxX, y + 24, boxX, y + headH);
    const infoRow = (label: string, value: string, yy: number, strong = false) => {
      doc.setFont('helvetica','normal'); doc.setFontSize(7); doc.setTextColor(90); doc.text(label.toUpperCase(), boxX + 10, yy);
      doc.setFont('helvetica','bold'); doc.setFontSize(strong ? 12 : 9.5); doc.setTextColor(...INK); doc.text(value, boxX + 10, yy + 11);
    };
    infoRow('Bauakten-Nr.', clean(project?.['project_no']), y + 34, true);
    infoRow('Ergebnis', this.inspectionResultLabel(inspection).toUpperCase(), y + 60);
    infoRow('Erstellt am', now.toLocaleDateString('de-DE'), y + 82);
    doc.setTextColor(...INK);
    y += headH + 16;

    section('I. Bauakte und Baustelle', 'Zuordnung der Erstbegehung', 112);
    row([['Bauakten-Nr.', project?.['project_no']], ['Bezeichnung', project?.['name']]], 36);
    row([['Baustelle', `${site?.['site_no'] || ''} ${site?.['name'] || ''}`.trim()], ['Kunde', project?.['customer_name'] || this.customerName(project?.['customer_id'])]], 36);
    row([['Adresse', site?.['address']], ['PLZ / Ort', [site?.['zip'], site?.['city']].filter(Boolean).join(' ')]], 36);
    row([['Datum', context['inspection_date'] || inspection['created_at']], ['Prüfer', inspection['signed_by'] || context['inspector_name']]], 36);

    section('II. Wesentliche Angaben', 'Kurzabfrage vor Ausführung', 134);
    row([['Ergebnis', this.inspectionResultLabel(inspection)], ['Status', this.inspectionStatusLabel(inspection['status'])], ['Fläche', checklist['exact_area_m2'] || measurements['exact_area_m2'] || site?.['area_m2'] ? `${checklist['exact_area_m2'] || measurements['exact_area_m2'] || site?.['area_m2']} m²` : '—']], 36);
    row([['Estrichart', checklist['screed_type'] || site?.['screed_type']], ['Fußbodenheizung', yesNoSelect(checklist['floor_heating'] || checklist['underfloor_heating'])], ['Untergrund', substrateLabel(checklist['substrate_status'])]], 36);
    row([['Meterriss / Höhen geprüft', checklist['height_checked'] ? 'Ja' : 'Nein'], ['Baustelle zugänglich', checklist['access_ok'] ? 'Ja' : 'Nein'], ['CM / Feuchte', measurements['cm_percent']]], 36);
    row([['Temperatur', measurements['temperature_c'] ? `${measurements['temperature_c']} °C` : '—'], ['Luftfeuchte', measurements['humidity_percent'] ? `${measurements['humidity_percent']} %` : '—'], ['Ebenheit / Hinweis', measurements['level_note']]], 36);
    memo('Bemerkungen / Mängel', checklist['notes'] || data['notes'], 52);
    memo('Notizen zur Baustelle', checklist['site_notes'] || data['site_notes'] || inspection['notes'], 52);

    section('III. Medien / Anlagen', 'Erfasste Bilder und Videos zur Erstbegehung', 66);
    const groups = (data['media_groups'] || {}) as GenericRecord;
    row([['Fotos Meterriss', Array.isArray(groups['meter_photos']) ? groups['meter_photos'].length : '—'], ['Fotos Mischplatz', Array.isArray(groups['mixing_place_photos']) ? groups['mixing_place_photos'].length : '—'], ['Videos Mischplatz', Array.isArray(groups['mixing_place_videos']) ? groups['mixing_place_videos'].length : '—']], 32);
    if (Array.isArray(groups['mixing_place_videos']) && groups['mixing_place_videos'].length) memo('Videodateien', groups['mixing_place_videos'].join(', '), 44);

    section('IV. Freigabe', 'Bestätigung der Angaben', 76);
    row([['Geprüft durch', inspection['signed_by'] || context['inspector_name']], ['Datum Freigabe', inspection['signed_at'] || context['inspection_date'] || inspection['created_at']]], 34);
    ensure(72); y += 16;
    doc.setDrawColor(55); doc.line(M, y + 34, M + 210, y + 34); doc.line(pageW - M - 210, y + 34, pageW - M, y + 34);
    doc.setFont('helvetica','normal'); doc.setFontSize(8); doc.text('Ort, Datum', M, y + 50); doc.text('Unterschrift Prüfer / Bauleitung', pageW - M - 210, y + 50);
    y += 76;

    if (photoIds.length) {
      newPage();
      section('V. Fotodokumentation', 'Fotos werden ausschließlich in diesem Abschnitt ausgegeben', 240);
      const imgW = (W - 14) / 2;
      let col = 0;
      for (const id of photoIds.slice(0, 30)) {
        if (col === 0) ensure(280);
        const x = M + col * (imgW + 14);
        const used = await this.addPhotoToPdf(doc, this.photoUrl({ id } as GenericRecord, 'main'), x, y, imgW, 235);
        doc.setFont('helvetica','normal'); doc.setFontSize(7.2); doc.text('Erstbegehungsfoto', x, y + (used || 235) + 12);
        col += 1;
        if (col === 2) { col = 0; y += 268; }
      }
    }
    footer();
    this.savePdf(doc, `Erstbegehung_${String(project?.['project_no'] || project?.['name'] || 'Bauakte').replace(/[^\w.-]+/g, '_')}.pdf`);
  }

  deleteAttachment(moduleKey: 'documents' | 'photos', item: GenericRecord, event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    const title = String(item['name'] || item['original_name'] || item['caption'] || 'Datei');
    const label = moduleKey === 'photos' ? 'Bild' : 'Dokument';
    const ref = this.dialog.open(ConfirmDialogComponent, {
      width: 'min(440px, 94vw)',
      panelClass: 'ew-dialog',
      data: {
        title: `${label} löschen`,
        message: `${label} „${title}“ wirklich dauerhaft aus der Bauakte löschen?`,
        confirmText: 'Löschen',
        cancelText: 'Abbrechen',
        icon: 'delete',
        tone: 'warn'
      }
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (!confirmed) return;
      this.api.remove(moduleKey, String(item['id'])).subscribe({
        next: () => {
          this.snack.open(moduleKey === 'photos' ? 'Bild gelöscht' : 'Dokument gelöscht', 'OK', { duration: 1800 });
          this.selectedDoc.set(null); this.selectedReport.set(null); this.rawViewerUrl.set(''); this.safeViewerUrl.set(null); this.photoViewerUrl.set('');
          this.refreshProjectContext();
        },
        error: () => this.snack.open('Löschen fehlgeschlagen', 'OK', { duration: 3200 })
      });
    });
  }
  uniqueAssignedUsers(): GenericRecord[] { const ids = [...new Set(this.tasks().map((task) => task['assigned_user_id']).filter(Boolean))]; return ids.map((id) => this.users().find((user) => user['id'] === id)).filter(Boolean) as GenericRecord[]; }
  userRoleLabel(user: GenericRecord): string { return user['role_name'] || user['role'] || user['position'] || 'Mitarbeiter'; }
}
