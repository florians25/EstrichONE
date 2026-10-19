import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { jsPDF } from 'jspdf';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';

@Component({
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSnackBarModule, MatDialogModule],
  template: `
    <section class="inquiry-ticket-page">
      <section class="inquiry-ticket-hero">
        <div class="ticket-hero-icon"><mat-icon>confirmation_number</mat-icon></div>
        <div class="ticket-hero-main">
          <p class="eyebrow">Büro · Anfrage-Ticket</p>
          <h1>{{ form.value.subject || inquiry()['subject'] || 'Neue Anfrage' }}</h1>
          <div class="ticket-hero-meta">
            <span><mat-icon>person</mat-icon>{{ primaryContact() }}</span>
            @if (contactLine() !== '—') { <span><mat-icon>contact_phone</mat-icon>{{ contactLine() }}</span> }
            @if (form.value.due_date) { <span><mat-icon>event</mat-icon>Wiedervorlage {{ form.value.due_date | date:'dd.MM.yyyy' }}</span> }
          </div>
        </div>
        <div class="ticket-hero-actions">
          <span class="ticket-status-pill" [class.warn]="currentStatus()==='cancelled'" [class.done]="currentStatus()==='converted'">{{ statusLabel(currentStatus()) }}</span>
          <a mat-stroked-button routerLink="/anfragen"><mat-icon>arrow_back</mat-icon>Zurück</a>
          <button mat-stroked-button type="button" (click)="downloadPdf()"><mat-icon>picture_as_pdf</mat-icon>PDF</button>
        </div>
      </section>

      <mat-card class="square-card ticket-status-card">
        <div class="ticket-status-head">
          <div><strong>Status</strong><span>Bearbeitungsstand direkt ändern</span></div>
          <button mat-button type="button" (click)="load()"><mat-icon>refresh</mat-icon>Aktualisieren</button>
        </div>
        <div class="ticket-status-flow" role="list" aria-label="Anfragestatus">
          @for (step of steps; track step.value; let idx = $index) {
            <button type="button" role="listitem" [class.active]="isStepActive(step.value)" [class.current]="currentStatus() === step.value" [class.cancelled]="step.value === 'cancelled'" (click)="setStatus(step.value)">
              <span class="step-index">{{ idx + 1 }}</span>
              <span class="step-copy"><strong>{{ step.label }}</strong><small>{{ step.hint }}</small></span>
              <mat-icon>{{ step.icon }}</mat-icon>
            </button>
          }
        </div>
      </mat-card>

      <form [formGroup]="form" class="ticket-layout">
        <main class="ticket-main-column">
          <mat-card class="square-card ticket-section-card ticket-section-contact">
            <div class="ticket-section-title"><mat-icon>contact_mail</mat-icon><div><h2>Kontakt</h2><p>Wer hat angefragt und wie erreichen wir die Person?</p></div></div>
            <div class="ticket-form-grid compact">
              <mat-form-field appearance="outline"><mat-label>Name</mat-label><input matInput formControlName="caller_name" autocomplete="name"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Firma</mat-label><input matInput formControlName="company" autocomplete="organization"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Telefon</mat-label><input matInput formControlName="caller_phone" autocomplete="tel"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>E-Mail</mat-label><input matInput formControlName="caller_email" autocomplete="email"></mat-form-field>
              <mat-form-field class="wide" appearance="outline"><mat-label>Straße / Objektadresse</mat-label><input matInput formControlName="address" autocomplete="street-address"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>PLZ</mat-label><input matInput formControlName="zip" autocomplete="postal-code"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Ort</mat-label><input matInput formControlName="city" autocomplete="address-level2"></mat-form-field>
            </div>
          </mat-card>

          <mat-card class="square-card ticket-section-card">
            <div class="ticket-section-title"><mat-icon>home_work</mat-icon><div><h2>Bedarf & Objekt</h2><p>Kurz erfassen, worum es geht und welche Eckdaten bekannt sind.</p></div></div>
            <div class="ticket-form-grid compact">
              <mat-form-field class="wide" appearance="outline"><mat-label>Anliegen / Betreff</mat-label><input matInput formControlName="subject"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Fläche m²</mat-label><input matInput type="number" formControlName="area_m2"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Estrichart</mat-label><mat-select formControlName="screed_type"><mat-option value="">— bitte wählen —</mat-option>@for (type of screedTypeOptions(); track type) { <mat-option [value]="type">{{ type }}</mat-option> }@if (screedTypeMissing()) { <mat-option [value]="form.value.screed_type">{{ form.value.screed_type }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Wunschstart</mat-label><input matInput type="date" formControlName="desired_start"></mat-form-field>
              <mat-form-field class="wide" appearance="outline"><mat-label>Notizen / Gesprächsinhalt</mat-label><textarea matInput rows="4" formControlName="notes"></textarea></mat-form-field>
            </div>
          </mat-card>

          <mat-card class="square-card ticket-section-card">
            <div class="ticket-section-title"><mat-icon>task_alt</mat-icon><div><h2>Bearbeitung</h2><p>Priorität, Quelle und nächster Schritt für die Wiedervorlage.</p></div></div>
            <div class="ticket-form-grid compact">
              <mat-form-field appearance="outline"><mat-label>Quelle</mat-label><input matInput formControlName="source" placeholder="z. B. Telefon, Website, Empfehlung"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Priorität</mat-label><mat-select formControlName="priority"><mat-option value="low">Niedrig</mat-option><mat-option value="normal">Normal</mat-option><mat-option value="high">Hoch</mat-option><mat-option value="urgent">Dringend</mat-option></mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Wiedervorlage</mat-label><input matInput type="date" formControlName="due_date"></mat-form-field>
              <mat-form-field class="wide" appearance="outline"><mat-label>Nächster Schritt</mat-label><input matInput formControlName="next_step" placeholder="z. B. Angebot vorbereiten, Rückruf, Bauakte anlegen"></mat-form-field>
            </div>
          </mat-card>
        </main>

        <aside class="ticket-side-column">
          <mat-card class="square-card ticket-action-panel">
            <h2>Schnellaktionen</h2>
            @if (canWrite) { <button mat-flat-button color="primary" type="button" [disabled]="saving()" (click)="save()"><mat-icon>save</mat-icon>{{ saving() ? 'Speichert…' : 'Anfrage speichern' }}</button> }
            @if (currentStatus() !== 'in_progress') { <button mat-stroked-button type="button" (click)="takeInProgress()"><mat-icon>pending_actions</mat-icon>In Bearbeitung nehmen</button> }
            <button mat-stroked-button type="button" (click)="convertToProject()"><mat-icon>folder_open</mat-icon>Bauakte erstellen</button>
            <button mat-stroked-button type="button" (click)="downloadPdf()"><mat-icon>picture_as_pdf</mat-icon>Anfrage-PDF</button>
            <button mat-stroked-button color="warn" type="button" (click)="cancel()"><mat-icon>cancel</mat-icon>Anfrage abbrechen</button>
            @if (inquiry()['converted_project_id']) { <a mat-stroked-button [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: inquiry()['converted_project_id'] }"><mat-icon>open_in_new</mat-icon>Erstellte Bauakte öffnen</a> }
          </mat-card>

          <mat-card class="square-card ticket-facts-card">
            <h2>Ticketdaten</h2>
            <dl>
              <div><dt>Status</dt><dd>{{ statusLabel(currentStatus()) }}</dd></div>
              <div><dt>Kontakt</dt><dd>{{ primaryContact() }}</dd></div>
              <div><dt>Priorität</dt><dd>{{ priorityLabel(form.value.priority || inquiry()['priority']) }}</dd></div>
              <div><dt>Nächster Schritt</dt><dd>{{ form.value.next_step || 'nicht gesetzt' }}</dd></div>
              <div><dt>Adresse</dt><dd>{{ addressLine() }}</dd></div>
            </dl>
          </mat-card>
        </aside>
      </form>
    </section>
  `
})
export class InquiryDetailComponent {

  private readonly auth = inject(AuthService);
  readonly canWrite = this.auth.canWrite('customers');
  readonly canDelete = this.auth.canDelete('customers');
  readonly canCreateProject = this.auth.canWrite('projects');  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);
  readonly inquiry = signal<GenericRecord>({});
  readonly saving = signal(false);
  readonly screedTypes = signal<GenericRecord[]>([]);
  readonly steps = [
    { value: 'open', label: 'Offen', hint: 'neu erfasst', icon: 'radio_button_unchecked' },
    { value: 'in_progress', label: 'In Bearbeitung', hint: 'wird geklärt', icon: 'pending_actions' },
    { value: 'converted', label: 'Bauakte erstellt', hint: 'übernommen', icon: 'folder_open' },
    { value: 'cancelled', label: 'Abgebrochen', hint: 'nicht weiterführen', icon: 'cancel' }
  ];
  readonly form = this.fb.group({ caller_name: [''], caller_phone: [''], caller_email: [''], company: [''], address: [''], zip: [''], city: [''], subject: [''], notes: [''], source: [''], priority: ['normal'], desired_start: [''], area_m2: [null as number | null], screed_type: [''], next_step: [''], due_date: [''] });

  constructor() { this.loadMasterData(); this.load(); }

  id(): string { return this.route.snapshot.paramMap.get('id') || ''; }
  currentStatus(): string { return String(this.inquiry()['inquiry_status'] || 'open'); }
  loadMasterData(): void { this.api.list('screed_types', '', 250).subscribe({ next: (r) => this.screedTypes.set(r.items || []), error: () => this.screedTypes.set([]) }); }
  screedTypeOptions(): string[] { const fromDb = this.screedTypes().map((s) => String(s['name'] || s['label'] || '').trim()).filter(Boolean); return fromDb; }
  screedTypeMissing(): boolean { const v = String(this.form.value.screed_type || '').trim(); return !!v && !this.screedTypeOptions().includes(v); }
  statusLabel(status: string): string { return ({ open: 'Offen', in_progress: 'In Bearbeitung', converted: 'Bauakte erstellt', cancelled: 'Abgebrochen' } as Record<string,string>)[status] || status; }
  priorityLabel(priority: unknown): string { return ({ low: 'Niedrig', normal: 'Normal', high: 'Hoch', urgent: 'Dringend' } as Record<string,string>)[String(priority || 'normal')] || String(priority || 'Normal'); }
  primaryContact(): string { return String(this.form.value.caller_name || this.inquiry()['caller_name'] || this.form.value.company || this.inquiry()['company'] || 'Kontakt offen'); }
  contactLine(): string { return [this.form.value.caller_phone || this.inquiry()['caller_phone'], this.form.value.caller_email || this.inquiry()['caller_email']].filter(Boolean).join(' · ') || '—'; }
  addressLine(): string { return [this.form.value.address || this.inquiry()['address'], this.form.value.zip || this.inquiry()['zip'], this.form.value.city || this.inquiry()['city']].filter(Boolean).join(' ') || 'nicht erfasst'; }
  isStepActive(value: string): boolean { const order = ['open','in_progress','converted','cancelled']; const current = this.currentStatus(); if (current === 'cancelled') return value === 'cancelled'; if (current === 'converted') return value === 'converted' || value === 'open' || value === 'in_progress'; return order.indexOf(value) <= order.indexOf(current); }

  load(): void {
    const id = this.id(); if (!id) return;
    this.api.getCall(id).subscribe({
      next: (row) => {
        this.inquiry.set(row || {});
        this.form.patchValue({
          caller_name: String(row['caller_name'] || ''), caller_phone: String(row['caller_phone'] || ''), caller_email: String(row['caller_email'] || ''), company: String(row['company'] || ''),
          address: String(row['address'] || ''), zip: String(row['zip'] || ''), city: String(row['city'] || ''), subject: String(row['subject'] || ''), notes: String(row['notes'] || ''),
          source: String(row['source'] || ''), priority: String(row['priority'] || 'normal'), desired_start: String(row['desired_start'] || '').slice(0,10),
          area_m2: row['area_m2'] == null ? null : Number(row['area_m2']), screed_type: String(row['screed_type'] || ''), next_step: String(row['next_step'] || ''), due_date: String(row['due_date'] || '').slice(0,10)
        });
      },
      error: () => this.snack.open('Anfrage konnte nicht geladen werden.', 'OK', { duration: 3000 })
    });
  }

  save(): void {
    if (this.saving()) return;
    this.saving.set(true);
    const payload = { ...this.form.getRawValue(), kind: 'inquiry' };
    this.api.updateCall(this.id(), payload).subscribe({
      next: (row) => { this.inquiry.set(row); this.saving.set(false); this.snack.open('Anfrage gespeichert.', 'OK', { duration: 2200 }); },
      error: () => { this.saving.set(false); this.snack.open('Speichern fehlgeschlagen. Bitte Pflichtfelder und Datenbankstand prüfen.', 'OK', { duration: 4200 }); }
    });
  }

  setStatus(status: string): void {
    if (!['open','in_progress','converted','cancelled'].includes(status)) return;
    this.api.updateInquiryStatus(this.id(), status as 'open' | 'in_progress' | 'converted' | 'cancelled').subscribe({
      next: (row) => { this.inquiry.set(row); this.snack.open('Status aktualisiert.', 'OK', { duration: 1800 }); },
      error: () => this.snack.open('Status konnte nicht geändert werden.', 'OK', { duration: 3000 })
    });
  }
  takeInProgress(): void { this.setStatus('in_progress'); }
  convertToProject(): void { const c: GenericRecord = { ...this.inquiry(), ...this.form.getRawValue() }; void this.router.navigate(['/auftrag-wizard'], { queryParams: { inquiry_id: c['id'], inquiry_name: c['caller_name'] || '', inquiry_phone: c['caller_phone'] || '', inquiry_email: c['caller_email'] || '', inquiry_company: c['company'] || '', inquiry_subject: c['subject'] || '', inquiry_notes: c['notes'] || '' } }); }
  cancel(): void { const ref = this.dialog.open(ConfirmDialogComponent, { width: 'min(460px, 96vw)', maxWidth: '96vw', data: { title: 'Anfrage abbrechen?', message: 'Die Anfrage bleibt dokumentiert, wird aber als abgebrochen markiert.', confirmText: 'Abbrechen', icon: 'cancel', tone: 'warn' } }); ref.afterClosed().subscribe((ok) => { if (ok) this.setStatus('cancelled'); }); }

  downloadPdf(): void {
    const data: GenericRecord = { ...this.inquiry(), ...this.form.getRawValue() };
    const doc = new jsPDF({ unit: 'mm', format: 'a4' });
    const left = 25, right = 20, pageW = 210, pageH = 297, usable = pageW - left - right;
    let y = 18;
    const line = (label: string, value: unknown) => {
      const text = String(value || '').trim(); if (!text) return;
      doc.setFont('helvetica','bold'); doc.setFontSize(8.5); doc.text(label, left, y);
      doc.setFont('helvetica','normal'); doc.setFontSize(9.5);
      const lines = doc.splitTextToSize(text, usable - 46) as string[];
      doc.text(lines, left + 46, y); y += Math.max(6, lines.length * 4.5);
    };
    const box = (title: string) => { y += 4; doc.setFillColor(248,250,252); doc.rect(left, y - 5, usable, 8, 'F'); doc.setFont('helvetica','bold'); doc.setFontSize(10); doc.text(title, left + 2, y); y += 10; };
    const footer = () => { doc.setDrawColor(210); doc.line(left, pageH - 20, pageW - right, pageH - 20); doc.setFont('helvetica','normal'); doc.setFontSize(7.5); doc.setTextColor(95); doc.text('Anfrageausgabe · EstrichWeb', left, pageH - 14); doc.text(`Seite ${doc.getNumberOfPages()}`, pageW - right, pageH - 14, { align: 'right' }); doc.setTextColor(0); };
    doc.setFont('helvetica','normal'); doc.setFontSize(7.5); doc.setTextColor(90); doc.text('EstrichWeb · Anfrage', left, y); doc.setTextColor(0);
    y = 42;
    doc.setFont('helvetica','bold'); doc.setFontSize(18); doc.text('Anfrage-Ticket', left, y);
    doc.setFont('helvetica','normal'); doc.setFontSize(10); doc.text(new Intl.DateTimeFormat('de-DE').format(new Date()), pageW - right, y, { align: 'right' });
    y += 10;
    doc.setFontSize(10); doc.text(String(data['subject'] || 'Nicht-Kunden-Anfrage'), left, y); y += 8;
    doc.setFillColor(235,244,255); doc.roundedRect(left, y, usable, 13, 2, 2, 'F');
    doc.setFont('helvetica','bold'); doc.setFontSize(10); doc.text(`Status: ${this.statusLabel(String(data['inquiry_status'] || 'open'))}`, left + 4, y + 8); y += 20;
    box('Kontakt');
    line('Name', data['caller_name']); line('Firma', data['company']); line('Telefon', data['caller_phone']); line('E-Mail', data['caller_email']); line('Adresse', [data['address'], data['zip'], data['city']].filter(Boolean).join(' '));
    box('Bedarf und Objekt');
    line('Anliegen', data['subject']); line('Fläche', data['area_m2'] ? `${data['area_m2']} m²` : ''); line('Estrichart', data['screed_type']); line('Wunschstart', data['desired_start']);
    if (y > 225) { footer(); doc.addPage(); y = 25; }
    box('Bearbeitung');
    line('Quelle', data['source']); line('Priorität', this.priorityLabel(data['priority'])); line('Fällig am', data['due_date']); line('Nächster Schritt', data['next_step']);
    box('Notizen');
    const notes = String(data['notes'] || 'Keine Notizen erfasst.');
    doc.setFont('helvetica','normal'); doc.setFontSize(9.5); doc.text(doc.splitTextToSize(notes, usable) as string[], left, y);
    footer();
    const name = String(data['subject'] || data['caller_name'] || 'Anfrage').replace(/[^a-z0-9äöüß._-]+/gi, '_').replace(/^_+|_+$/g, '') || 'Anfrage';
    doc.save(`${name}.pdf`);
  }
}
