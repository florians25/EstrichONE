import { Component, inject, signal, computed, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService, GenericRecord } from '../../core/api.service';

interface CallForm { kind: 'call' | 'inquiry'; caller_name: string; caller_phone: string; caller_email: string; company: string; customer_id: string; direction: string; source: string; priority: string; subject: string; notes: string; project_id: string; follow_up: boolean; area_m2: number | null; screed_type: string; desired_start: string; due_date: string; next_step: string; }

@Component({
  standalone: true,
  selector: 'ew-call-form-dialog',
  imports: [FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatAutocompleteModule, MatSlideToggleModule, MatDialogModule, MatSnackBarModule],
  template: `
    <h2 mat-dialog-title class="cf-title"><span class="cf-title-icon"><mat-icon>{{ isInquiry ? 'confirmation_number' : (isEdit ? 'edit_note' : 'add_call') }}</mat-icon></span>{{ dialogTitle() }}</h2>
    <mat-dialog-content class="cf-dialog">
      @if (!isEdit && !isInquiry) {
        <p class="cf-live"><span class="cf-live-dot"></span>Gespräch läuft – {{ clock() }}</p>
      }
      <h3 class="cf-section">Anrufer</h3>
      <div class="cf-grid">
        <mat-form-field appearance="outline"><mat-label>Name</mat-label><input matInput [(ngModel)]="form.caller_name" placeholder="Anrufer"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Telefon</mat-label><input matInput [(ngModel)]="form.caller_phone" placeholder="z. B. 0221 123456"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>E-Mail</mat-label><input matInput [(ngModel)]="form.caller_email" placeholder="mail@example.de"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Firma (optional)</mat-label><input matInput [(ngModel)]="form.company"></mat-form-field>
        @if (!isInquiry) {
          <h3 class="cf-section cf-section-full">Gespräch</h3>
          <mat-form-field appearance="outline"><mat-label>Richtung</mat-label>
            <mat-select [(ngModel)]="form.direction"><mat-option value="inbound">Eingehend</mat-option><mat-option value="outbound">Ausgehend</mat-option></mat-select>
          </mat-form-field>
          <!-- Kundensuche wie in der Schnellerfassung: schlichtes Feld mit
               Trefferliste. Die Material-Autovervollständigung hat hier
               Auswahl und Anzeige durcheinandergebracht; ein eigenes Feld
               ist berechenbarer und durchsucht auch Telefonnummern. -->
          <div class="cf-span2 cf-cust">
            <mat-form-field appearance="outline" class="cf-cust-field">
              <mat-label>Kunde verknüpfen (optional)</mat-label>
              <input type="text" matInput [(ngModel)]="customerInput" (ngModelChange)="onCustomerQuery($event)"
                     placeholder="Name, Firma, Telefon oder Ort" autocomplete="off">
              <mat-icon matPrefix>person_search</mat-icon>
              @if (form.customer_id) {
                <button mat-icon-button matSuffix type="button" (click)="clearCustomer()"
                        matTooltip="Verknüpfung entfernen" aria-label="Verknüpfung entfernen"><mat-icon>link_off</mat-icon></button>
              }
            </mat-form-field>

            @if (customerHits().length && !form.customer_id) {
              <ul class="cf-hits">
                @for (c of customerHits(); track c['id']) {
                  <li><button type="button" (click)="pickCustomer(c)">
                    <b>{{ c['name'] || c['company'] }}</b>
                    @if (c['phone'] || c['mobile']) { <span>{{ c['phone'] || c['mobile'] }}</span> }
                    @if (c['city']) { <small>{{ c['city'] }}</small> }
                  </button></li>
                }
              </ul>
            }
            @if (selectedCustomer(); as c) {
              <p class="cf-linked">
                <mat-icon>how_to_reg</mat-icon>
                <span>Verknüpft mit <b>{{ c['name'] || c['company'] }}</b></span>
              </p>
            }
          </div>
          @if (selectedCustomer()) {
            <div class="cf-span2 cf-linked-customer">
              <mat-icon>badge</mat-icon>
              <div><strong>{{ selectedCustomer()?.['name'] || selectedCustomer()?.['company'] || 'Kunde' }}</strong><span>{{ customerDetailLine(selectedCustomer()) }}</span></div>
              <button mat-icon-button type="button" (click)="clearCustomer()" aria-label="Kunde entfernen"><mat-icon>close</mat-icon></button>
            </div>
          }
        }
        <mat-form-field appearance="outline" class="cf-span2"><mat-label>{{ isInquiry ? 'Anliegen' : 'Betreff' }}</mat-label><input matInput [(ngModel)]="form.subject" [placeholder]="isInquiry ? 'Worum geht es bei der Anfrage?' : 'Worum ging es?'"></mat-form-field>
        @if (!isInquiry) {
        <mat-form-field appearance="outline" class="cf-span2"><mat-label>Auftrag verknüpfen (optional)</mat-label>
          <input type="text" matInput [matAutocomplete]="auto" [ngModel]="projectQuery()" (ngModelChange)="onProjectQuery($event)" [disabled]="isEdit && !!data.call?.['project_id']" placeholder="Auftrag suchen oder leer lassen">
          <mat-icon matPrefix>search</mat-icon>
          @if (projectQuery()) { <button mat-icon-button matSuffix type="button" (click)="clearProject()" aria-label="Verknüpfung entfernen"><mat-icon>close</mat-icon></button> }
          <mat-autocomplete #auto="matAutocomplete" (optionSelected)="onProjectPick($event)" [displayWith]="displayProject">
            @for (p of filteredProjects(); track p['id']) { <mat-option [value]="p">{{ projectLabel(p) }}</mat-option> }
            @if (!filteredProjects().length) { <mat-option [disabled]="true">Kein Auftrag gefunden</mat-option> }
          </mat-autocomplete>
        </mat-form-field>
        }
        @if (isInquiry) {
          <mat-form-field appearance="outline"><mat-label>Quelle</mat-label><mat-select [(ngModel)]="form.source"><mat-option value="phone">Telefon</mat-option><mat-option value="email">E-Mail</mat-option><mat-option value="website">Website</mat-option><mat-option value="recommendation">Empfehlung</mat-option><mat-option value="other">Sonstiges</mat-option></mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Priorität</mat-label><mat-select [(ngModel)]="form.priority"><mat-option value="low">Niedrig</mat-option><mat-option value="normal">Normal</mat-option><mat-option value="urgent">Dringend</mat-option></mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Fläche m²</mat-label><input matInput type="number" [(ngModel)]="form.area_m2"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Estrichart</mat-label><mat-select [(ngModel)]="form.screed_type"><mat-option value="">— bitte wählen —</mat-option>@for (type of screedTypeOptions(); track type) { <mat-option [value]="type">{{ type }}</mat-option> }@if (screedTypeMissing()) { <mat-option [value]="form.screed_type">{{ form.screed_type }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Wunschstart</mat-label><input matInput type="date" [(ngModel)]="form.desired_start"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Wiedervorlage</mat-label><input matInput type="date" [(ngModel)]="form.due_date"></mat-form-field>
          <mat-form-field appearance="outline" class="cf-span2"><mat-label>Nächster Schritt</mat-label><input matInput [(ngModel)]="form.next_step" placeholder="z. B. Rückruf, Angebot vorbereiten"></mat-form-field>
        }
        <mat-form-field appearance="outline" class="cf-span2">
          <mat-label>{{ isInquiry ? 'Ticket-Notiz' : 'Notiz' }}</mat-label>
          <textarea matInput rows="4" [(ngModel)]="form.notes"
                    placeholder="Worum geht es? Was ist zu tun?"
                    (keydown.control.enter)="save()"></textarea>
        </mat-form-field>
        <!-- Stichworte wie in der Schnellerfassung – ein Tipp statt Tippen. -->
        <div class="cf-chips cf-span2">
          @for (t of noteTemplates; track t) {
            <button type="button" class="cf-chip" (click)="appendNote(t)">{{ t }}</button>
          }
        </div>
      </div>
      <div class="cf-foot">
        <mat-slide-toggle [(ngModel)]="form.follow_up">Wiedervorlage nötig</mat-slide-toggle>
        @if (isInquiry) { <span class="cf-hint"><mat-icon>info</mat-icon>Anfrage bleibt ein Ticket, bis daraus eine Bauakte entsteht oder sie abgebrochen wird.</span> }
        @if (!isInquiry && form.project_id && !isEdit) { <span class="cf-hint"><mat-icon>link</mat-icon>Wird als Notiz im Auftrag gespeichert</span> }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-stroked-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" [disabled]="saving()" (click)="save()"><mat-icon>save</mat-icon>{{ isEdit ? 'Speichern' : (isInquiry ? 'Anfrage speichern' : 'Anruf speichern') }}</button>
    </mat-dialog-actions>
  `
})
export class CallFormDialogComponent {
  /** Häufige Stichworte – identisch zur Schnellerfassung. */
  readonly noteTemplates = ['Rückruf gewünscht', 'Termin vereinbaren', 'Angebot erbeten', 'Nachfrage zum Auftrag', 'Reklamation', 'Nicht erreicht'];
  appendNote(text: string): void {
    const current = String(this.form.notes || '').trim();
    this.form.notes = current ? `${current}\n${text}` : text;
  }
  /** Mitlaufende Gesprächsdauer – dieselbe Anzeige wie in der Liste. */
  readonly clock = signal('00:00');
  private readonly startedAt = Date.now();
  private readonly timer = setInterval(() => {
    const sec = Math.floor((Date.now() - this.startedAt) / 1000);
    this.clock.set(`${String(Math.floor(sec / 60)).padStart(2, '0')}:${String(sec % 60).padStart(2, '0')}`);
  }, 1000);
  ngOnDestroy(): void { clearInterval(this.timer); }

  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  readonly saving = signal(false);
  readonly projects = signal<GenericRecord[]>([]);
  readonly customers = signal<GenericRecord[]>([]);
  readonly screedTypes = signal<GenericRecord[]>([]);
  readonly projectQuery = signal('');
  readonly customerQuery = signal('');
  isEdit = false;
  readonly isInquiry: boolean;
  form: CallForm = { kind: 'call', caller_name: '', caller_phone: '', caller_email: '', company: '', customer_id: '', direction: 'inbound', source: 'phone', priority: 'normal', subject: '', notes: '', project_id: '', follow_up: false, area_m2: null, screed_type: '', desired_start: '', due_date: '', next_step: '' };

  readonly filteredProjects = computed(() => {
    const q = this.projectQuery().trim().toLowerCase();
    const list = this.projects();
    if (!q) return list.slice(0, 50);
    return list.filter((p) => this.projectLabel(p).toLowerCase().includes(q)).slice(0, 50);
  });

  readonly filteredCustomers = computed(() => {
    const q = this.customerQuery().trim().toLowerCase();
    const list = this.customers();
    if (!q) return list.slice(0, 50);
    return list.filter((c) => this.customerLabel(c).toLowerCase().includes(q)).slice(0, 50);
  });

  readonly selectedCustomer = computed(() => this.customers().find((c) => String(c['id']) === String(this.form.customer_id)) || null);

  constructor(private ref: MatDialogRef<CallFormDialogComponent>, @Inject(MAT_DIALOG_DATA) public data: { call?: GenericRecord; projects?: GenericRecord[]; kind?: 'call' | 'inquiry' }) {
    this.isInquiry = String(data?.kind || data?.call?.['kind'] || 'call') === 'inquiry';
    this.form.kind = this.isInquiry ? 'inquiry' : 'call';
    if (data?.projects?.length) this.projects.set(data.projects);
    else this.api.list('projects', '', 300).subscribe({ next: (r) => this.projects.set(r.items || []), error: () => {} });
    if (!this.isInquiry) this.api.list('customers', '', 2000).subscribe({ next: (r) => { this.customers.set(r.items || []); this.syncCustomerQuery(); }, error: () => {} });
    if (this.isInquiry) this.api.list('screed_types', '', 250).subscribe({ next: (r) => this.screedTypes.set(r.items || []), error: () => this.screedTypes.set([]) });
    if (data?.call) {
      this.isEdit = true; const c = data.call;
      this.form = {
        kind: this.isInquiry ? 'inquiry' : 'call',
        caller_name: String(c['caller_name'] || ''), caller_phone: String(c['caller_phone'] || ''), caller_email: String(c['caller_email'] || ''), company: String(c['company'] || ''), customer_id: String(c['customer_id'] || ''),
        direction: String(c['direction'] || 'inbound'), source: String(c['source'] || 'phone'), priority: String(c['priority'] || 'normal'), subject: String(c['subject'] || ''), notes: String(c['notes'] || ''),
        project_id: this.isInquiry ? '' : String(c['project_id'] || ''), follow_up: !!c['follow_up'], area_m2: c['area_m2'] == null ? null : Number(c['area_m2']), screed_type: String(c['screed_type'] || ''), desired_start: String(c['desired_start'] || '').slice(0,10), due_date: String(c['due_date'] || '').slice(0,10), next_step: String(c['next_step'] || '')
      };
      // Vorhandene Verknüpfung im Suchfeld anzeigen
      const linked = (data.projects || []).find((p) => String(p['id']) === this.form.project_id);
      if (linked) this.projectQuery.set(this.projectLabel(linked));
      this.syncCustomerQuery();
    }
  }
  dialogTitle(): string {
    if (this.isEdit) return this.isInquiry ? 'Anfrage bearbeiten' : 'Anruf bearbeiten';
    return this.isInquiry ? 'Anfrage erfassen' : 'Anruf erfassen';
  }
  projectLabel(p: GenericRecord): string { return [p['project_no'], p['name']].filter(Boolean).join(' · ') || String(p['id']); }
  displayProject = (p: GenericRecord | string): string => { if (!p) return ''; if (typeof p === 'string') return p; return this.projectLabel(p); };
  customerLabel(c: GenericRecord): string { return [c['name'] || c['company'], c['contact_person'], c['city']].filter(Boolean).join(' · ') || String(c['id']); }
  displayCustomer = (c: GenericRecord | string): string => { if (!c) return ''; if (typeof c === 'string') return c; return this.customerLabel(c); };
  customerDetailLine(c: GenericRecord | null): string { if (!c) return ''; const contact = [c['contact_person'], c['phone'], c['email']].filter(Boolean).join(' · '); const address = [c['address'], [c['zip'], c['city']].filter(Boolean).join(' ')].filter(Boolean).join(', '); return [contact, address].filter(Boolean).join(' | ') || 'Keine weiteren Kundendaten hinterlegt'; }
  syncCustomerQuery(): void {
    const c = this.customers().find((x) => String(x['id']) === String(this.form.customer_id));
    if (c) this.customerInput = this.customerLabel(c);
  }
  screedTypeOptions(): string[] { const fromDb = this.screedTypes().map((s) => String(s['name'] || s['label'] || '').trim()).filter(Boolean); return fromDb; }
  screedTypeMissing(): boolean { const v = String(this.form.screed_type || '').trim(); return !!v && !this.screedTypeOptions().includes(v); }
  onProjectQuery(v: string): void {
    // Freitext: solange nichts ausgewählt ist, Verknüpfung leeren
    if (typeof v === 'string') { this.projectQuery.set(v); if (!v.trim()) this.form.project_id = ''; }
  }
  onProjectPick(ev: MatAutocompleteSelectedEvent): void {
    const p = ev.option.value as GenericRecord;
    this.form.project_id = String(p['id']);
    this.projectQuery.set(this.projectLabel(p));
  }
  clearProject(): void { this.form.project_id = ''; this.projectQuery.set(''); }
  /** Eingabefeld der Kundensuche (freier Text). */
  customerInput = '';
  readonly customerHits = signal<GenericRecord[]>([]);

  /** Vereinheitlichen: Umlaute, Groß-/Kleinschreibung, Telefonzeichen. */
  private normCust(value: unknown): string {
    return String(value ?? '')
      .toLowerCase()
      .replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss')
      .replace(/[\s()/.\-]/g, '');
  }
  onCustomerQuery(v: string): void {
    this.customerInput = v ?? '';
    if (this.form.customer_id) { this.form.customer_id = ''; }
    const q = this.normCust(v);
    if (q.length < 2) { this.customerHits.set([]); return; }
    this.customerHits.set(this.customers().filter((c) =>
      ['name', 'company', 'contact_person', 'phone', 'mobile', 'email', 'city', 'customer_no']
        .some((k) => this.normCust(c[k]).includes(q))
    ).slice(0, 6));
  }
  /** Kunden übernehmen und leere Felder aus den Stammdaten vorbelegen. */
  pickCustomer(c: GenericRecord): void {
    this.form.customer_id = String(c['id']);
    this.customerInput = this.customerLabel(c);
    this.customerHits.set([]);
    if (!this.form.caller_name.trim()) this.form.caller_name = String(c['contact_person'] || c['name'] || '');
    if (!this.form.caller_phone.trim()) this.form.caller_phone = String(c['phone'] || c['mobile'] || '');
    if (!this.form.caller_email.trim()) this.form.caller_email = String(c['email'] || '');
    if (!this.form.company.trim()) this.form.company = String(c['name'] || c['company'] || '');
  }
  clearCustomer(): void { this.form.customer_id = ''; this.customerInput = ''; this.customerHits.set([]); }
  save(): void {
    if (!this.form.caller_name.trim() && !this.form.caller_phone.trim() && !this.form.caller_email.trim()) { this.snack.open('Bitte Name, Telefon oder E-Mail angeben.', 'OK', { duration: 2800 }); return; }
    this.saving.set(true);
    if (this.isEdit && this.data.call) {
      // Auftragsverknüpfung wird beim Bearbeiten nicht verändert (Notiz bleibt bestehen).
      const payload = { kind: this.form.kind, caller_name: this.form.caller_name, caller_phone: this.form.caller_phone, caller_email: this.form.caller_email, company: this.form.company, customer_id: this.isInquiry ? '' : this.form.customer_id, direction: this.form.direction, source: this.form.source, priority: this.form.priority, subject: this.form.subject, notes: this.form.notes, follow_up: this.form.follow_up, area_m2: this.form.area_m2, screed_type: this.form.screed_type, desired_start: this.form.desired_start, due_date: this.form.due_date, next_step: this.form.next_step };
      this.api.updateCall(String(this.data.call['id']), payload).subscribe({
        next: (updated) => { this.saving.set(false); this.ref.close({ updated: true, call: updated }); },
        error: (err) => { this.saving.set(false); this.snack.open(err?.error?.error || 'Konnte nicht gespeichert werden.', 'OK', { duration: 3500 }); }
      });
    } else {
      const payload = { ...this.form, customer_id: this.isInquiry ? '' : this.form.customer_id, project_id: this.isInquiry ? '' : this.form.project_id };
      this.api.createCall(payload).subscribe({
        next: (created) => { this.saving.set(false); this.ref.close({ created: true, call: created }); },
        error: (err) => { this.saving.set(false); this.snack.open(err?.error?.error || 'Konnte nicht gespeichert werden.', 'OK', { duration: 3500 }); }
      });
    }
  }
}
