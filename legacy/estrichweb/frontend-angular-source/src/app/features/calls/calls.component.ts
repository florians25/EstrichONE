import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { jsPDF } from 'jspdf';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Inject } from '@angular/core';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { CallFormDialogComponent } from './call-form-dialog.component';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';

@Component({
  standalone: true,
  selector: 'ew-call-detail-dialog',
  imports: [DatePipe, MatButtonModule, MatIconModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title class="call-detail-title">
      <span class="call-dir-icon" [class.out]="data.call['direction']==='outbound'">
        <mat-icon>{{ data.isInquiry ? 'confirmation_number' : (data.call['direction']==='outbound' ? 'call_made' : 'call_received') }}</mat-icon>
      </span>{{ data.isInquiry ? 'Anfrage-Details' : 'Anruf-Details' }}
    </h2>
    <mat-dialog-content class="call-detail-body">
      <dl class="call-detail-list">
        <div><dt>Name</dt><dd>{{ data.call['caller_name'] || '—' }}</dd></div>
        <div><dt>Telefon</dt><dd>{{ data.call['caller_phone'] || '—' }}</dd></div>
        <div><dt>Firma</dt><dd>{{ data.call['company'] || '—' }}</dd></div>
        @if (!data.isInquiry) { <div><dt>Richtung</dt><dd>{{ data.call['direction']==='outbound' ? 'Ausgehend' : 'Eingehend' }}</dd></div> }
        @if (data.isInquiry) { <div><dt>Status</dt><dd>{{ data.statusLabel }}</dd></div> }
        <div><dt>Zeitpunkt</dt><dd>{{ data.call['called_at'] | date:'dd.MM.yyyy HH:mm' }}</dd></div>
        <div><dt>Bearbeiter</dt><dd>{{ data.call['handled_by'] || '—' }}</dd></div>
        @if (!data.isInquiry) { <div><dt>Auftrag</dt><dd>{{ data.projectLabel || (data.call['project_id'] ? data.call['project_id'] : 'Ohne Verknüpfung') }}</dd></div> }
        @if (data.isInquiry && data.call['converted_project_id']) { <div><dt>Bauakte</dt><dd>{{ data.projectLabel || data.call['converted_project_id'] }}</dd></div> }
        <div><dt>Wiedervorlage</dt><dd>{{ data.call['follow_up'] ? 'Ja' : 'Nein' }}</dd></div>
      </dl>
      <div class="call-detail-section"><h4>{{ data.isInquiry ? 'Anliegen' : 'Betreff' }}</h4><p>{{ data.call['subject'] || '—' }}</p></div>
      <div class="call-detail-section"><h4>{{ data.isInquiry ? 'Ticket-Notiz' : 'Notiz' }}</h4><p class="call-detail-notes">{{ data.call['notes'] || '—' }}</p></div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-stroked-button mat-dialog-close>Schließen</button>
      <button mat-flat-button color="primary" (click)="download()"><mat-icon>picture_as_pdf</mat-icon>PDF herunterladen</button>
    </mat-dialog-actions>
  `
})
export class CallDetailDialogComponent {
  constructor(private ref: MatDialogRef<CallDetailDialogComponent>, @Inject(MAT_DIALOG_DATA) public data: { call: GenericRecord; projectLabel: string; company: string; isInquiry: boolean; statusLabel: string }) {}
  download(): void {
    const c = this.data.call;
    const doc = new jsPDF({ unit: 'mm', format: 'a4' });
    const left = 22, right = 20, PW = 210; const cw = PW - left - right;
    doc.setFont('helvetica', 'bold'); doc.setFontSize(16); doc.text(this.data.isInquiry ? 'ANFRAGE-TICKET' : 'GESPRÄCHSNOTIZ', left, 22);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(9.5); doc.text(this.data.company || '', left, 30);
    doc.setDrawColor(0); doc.setLineWidth(0.6); doc.line(left, 34, PW - right, 34);
    let y = 46;
    const row = (k: string, v: string) => { doc.setFont('helvetica', 'bold'); doc.setFontSize(10); doc.text(k, left, y); doc.setFont('helvetica', 'normal'); const lines = doc.splitTextToSize(v || '—', cw - 45) as string[]; doc.text(lines, left + 45, y); y += Math.max(7, lines.length * 5.6); };
    row('Datum/Zeit:', new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(String(c['called_at'] || Date.now()))));
    if (this.data.isInquiry) row('Status:', this.data.statusLabel); else row('Richtung:', c['direction'] === 'outbound' ? 'Ausgehender Anruf' : 'Eingehender Anruf');
    row('Name:', String(c['caller_name'] || '—'));
    row('Telefon:', String(c['caller_phone'] || '—'));
    row('Firma:', String(c['company'] || '—'));
    if (!this.data.isInquiry) row('Auftrag:', this.data.projectLabel || (c['project_id'] ? String(c['project_id']) : 'Ohne Verknüpfung'));
    if (this.data.isInquiry && c['converted_project_id']) row('Bauakte:', this.data.projectLabel || String(c['converted_project_id']));
    row('Bearbeiter:', String(c['handled_by'] || '—'));
    row('Wiedervorlage:', c['follow_up'] ? 'Ja' : 'Nein');
    y += 2; doc.setDrawColor(210); doc.setLineWidth(0.3); doc.line(left, y, PW - right, y); y += 8;
    doc.setFont('helvetica', 'bold'); doc.setFontSize(11); doc.text(this.data.isInquiry ? 'Anliegen' : 'Betreff', left, y); y += 6;
    doc.setFont('helvetica', 'normal'); doc.setFontSize(10); doc.text(doc.splitTextToSize(String(c['subject'] || '—'), cw) as string[], left, y);
    y += 14;
    doc.setFont('helvetica', 'bold'); doc.setFontSize(11); doc.text(this.data.isInquiry ? 'Ticket-Notiz' : 'Gesprächsnotiz', left, y); y += 6;
    doc.setFont('helvetica', 'normal'); doc.setFontSize(10); doc.text(doc.splitTextToSize(String(c['notes'] || '—'), cw) as string[], left, y);
    const safe = String(c['caller_name'] || (this.data.isInquiry ? 'Anfrage' : 'Anruf')).replace(/[^a-z0-9]+/gi, '_');
    doc.save(`${this.data.isInquiry ? 'Anfrage' : 'Gespraechsnotiz'}_${safe}.pdf`);
  }
}

@Component({
  standalone: true,
  imports: [DatePipe, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatDialogModule, MatSnackBarModule, MatTooltipModule],
  template: `
    <section class="page-head compact-head">
      <div><p class="eyebrow">Büro</p><h1>{{ isInquiry() ? 'Anfragen' : 'Anrufe' }}</h1></div>
      <div class="head-actions">
        @if (canWrite && !isInquiry()) { <button mat-flat-button color="primary" class="quick-open" (click)="quickOpen()"><mat-icon>bolt</mat-icon>Schnellerfassung</button> }
        @if (canWrite) { <button mat-stroked-button (click)="openNew()"><mat-icon>{{ isInquiry() ? 'confirmation_number' : 'add_call' }}</mat-icon>{{ isInquiry() ? 'Anfrage erfassen' : 'Anruf mit allen Feldern' }}</button> }
        <button mat-stroked-button (click)="exportList()" [disabled]="!filtered().length"><mat-icon>picture_as_pdf</mat-icon>Liste als PDF</button>
        <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon>Aktualisieren</button>
      </div>
    </section>

    <!-- Schnellerfassung: gedacht für das Gespräch am Telefon. Ein Feld für
         Namen/Nummer, eines für die Notiz, Speichern mit Strg+Enter. Alles
         Weitere kann nachträglich ergänzt werden. -->
    @if (quickMode() && canWrite && !isInquiry()) {
      <mat-card class="square-card quick-card">
        <div class="quick-head">
          <span class="quick-live"><span class="quick-dot"></span>Gespräch läuft – {{ quickClock() }}</span>
          <span class="quick-spacer"></span>
          <button mat-icon-button (click)="quickClose()" matTooltip="Schnellerfassung schließen" aria-label="Schließen"><mat-icon>close</mat-icon></button>
        </div>

        <div class="quick-grid">
          <div class="quick-who">
            <mat-form-field appearance="outline" class="quick-who-field">
              <mat-label>Wer ruft an?</mat-label>
              <input matInput [(ngModel)]="quickWho" (ngModelChange)="onWhoChange()"
                     placeholder="Name, Firma oder Telefonnummer" autocomplete="off"
                     (keydown.control.enter)="quickSave()">
              <mat-icon matPrefix>person_search</mat-icon>
              @if (matchedCustomer(); as c) {
                <button mat-icon-button matSuffix type="button" (click)="clearCustomer()"
                        matTooltip="Zuordnung aufheben" aria-label="Zuordnung aufheben"><mat-icon>link_off</mat-icon></button>
              }
            </mat-form-field>

            <!-- Trefferliste über ALLE Kunden: Name, Firma, Telefon, Mobil, Ort.
                 Vorher gab es gar keine Suche – der Name musste getippt werden
                 und der Anruf blieb ohne Kundenbezug. -->
            @if (customerHits().length && !matchedCustomer()) {
              <ul class="quick-hits">
                @for (c of customerHits(); track c['id']) {
                  <li><button type="button" (click)="pickCustomer(c)">
                    <b>{{ c['name'] }}</b>
                    @if (c['phone'] || c['mobile']) { <span>{{ c['phone'] || c['mobile'] }}</span> }
                    @if (c['city']) { <small>{{ c['city'] }}</small> }
                  </button></li>
                }
              </ul>
            }
            @if (matchedCustomer(); as c) {
              <p class="quick-linked">
                <mat-icon>how_to_reg</mat-icon>
                <span>Zugeordnet: <b>{{ c['name'] }}</b>@if (c['city']) { <span> · {{ c['city'] }}</span> }</span>
                @if (openProjectsFor(c); as n) { <em>{{ n }} offene Bauakte(n)</em> }
              </p>
            }
          </div>

          <div class="quick-dir" role="group" aria-label="Richtung">
            <button type="button" [class.on]="quickDirection() === 'inbound'" (click)="quickDirection.set('inbound')">
              <mat-icon>call_received</mat-icon>Eingehend
            </button>
            <button type="button" [class.on]="quickDirection() === 'outbound'" (click)="quickDirection.set('outbound')">
              <mat-icon>call_made</mat-icon>Ausgehend
            </button>
          </div>
        </div>

        <mat-form-field appearance="outline" class="quick-note">
          <mat-label>Notiz – einfach mitschreiben</mat-label>
          <textarea matInput rows="4" [(ngModel)]="quickNote" #quickNoteField
                    (keydown.control.enter)="quickSave()"
                    placeholder="Worum geht es? Was ist zu tun?"></textarea>
          <mat-hint>Strg + Enter speichert. Alles Weitere lässt sich später ergänzen.</mat-hint>
        </mat-form-field>

        <div class="quick-chips">
          @for (t of quickTemplates; track t) {
            <button type="button" class="quick-chip" (click)="quickAppend(t)">{{ t }}</button>
          }
        </div>

        <div class="quick-actions">
          <label class="quick-follow">
            <input type="checkbox" [checked]="quickFollowUp()" (change)="quickFollowUp.set(!quickFollowUp())">
            <span>Wiedervorlage setzen</span>
          </label>
          <span class="quick-spacer"></span>
          <button mat-stroked-button (click)="quickSaveAndOpen()" [disabled]="!quickWho.trim() && !quickNote.trim()">
            <mat-icon>open_in_new</mat-icon>Speichern &amp; ergänzen
          </button>
          <button mat-flat-button color="primary" (click)="quickSave()" [disabled]="!quickWho.trim() && !quickNote.trim()">
            <mat-icon>save</mat-icon>Speichern
          </button>
        </div>
      </mat-card>
    }

    <mat-card class="square-card call-list-card" [class.inquiry-list-card]="isInquiry()">
      <div class="call-list-head">
        <mat-form-field appearance="outline" class="call-search"><mat-label>Suchen</mat-label><input matInput [(ngModel)]="search" placeholder="Name, Telefon, Betreff"><mat-icon matPrefix>search</mat-icon></mat-form-field>
        <button mat-button class="call-filter-toggle" [class.active]="onlyFollowUp()" (click)="onlyFollowUp.set(!onlyFollowUp())"><mat-icon>flag</mat-icon>Nur Wiedervorlagen</button>
        @if (isInquiry()) { <button mat-button class="call-filter-toggle" [class.active]="hideClosedInquiries()" (click)="hideClosedInquiries.set(!hideClosedInquiries())"><mat-icon>filter_alt</mat-icon>Nur offene</button> }
        <span class="call-count">{{ filtered().length }} {{ isInquiry() ? 'Anfragen' : 'Anrufe' }}</span>
      </div>
      <div class="call-list">
        @for (c of filtered(); track c['id']) {
          <article class="call-row" [class.followup]="c['follow_up']" [class.inquiry-row]="isInquiry()" [class.closed-inquiry]="isInquiry() && c['inquiry_status'] !== 'open'" (click)="openDetail(c)">
            <span class="call-dir" [class.out]="c['direction']==='outbound'"><mat-icon>{{ isInquiry() ? 'confirmation_number' : (c['direction']==='outbound' ? 'call_made' : 'call_received') }}</mat-icon></span>
            <div class="call-row-main">
              <strong>{{ c['caller_name'] || c['caller_phone'] || 'Unbekannt' }}</strong>
              <small>{{ c['subject'] || 'Ohne Betreff' }}</small>
              @if (isInquiry() && c['company']) { <small class="call-company-line">{{ c['company'] }}</small> }
            </div>
            <div class="call-row-meta">
              @if (!isInquiry() && c['project_id']) { <span class="call-link-chip"><mat-icon>link</mat-icon>{{ shortProject(c['project_id']) }}</span> }
              @if (isInquiry()) { <span class="call-followup-chip inquiry-status" [class.done]="c['inquiry_status']==='converted'" [class.cancelled]="c['inquiry_status']==='cancelled'">{{ inquiryStatusLabel(c) }}</span> }
              @if (c['follow_up']) { <span class="call-followup-chip">Wiedervorlage</span> }
              <small>{{ c['called_at'] | date:'dd.MM.yy HH:mm' }}</small>
            </div>
            <div class="call-row-actions">
              @if (isInquiry() && c['inquiry_status'] === 'open') {
                @if (canCreateProject) { <button mat-icon-button (click)="convertInquiry(c); $event.stopPropagation()" aria-label="Bauakte anlegen"><mat-icon>folder_open</mat-icon></button> }
                <button mat-icon-button (click)="cancelInquiry(c); $event.stopPropagation()" aria-label="Anfrage abbrechen"><mat-icon>cancel</mat-icon></button>
              }
              @if (!isInquiry()) { <button mat-icon-button (click)="createInquiryFromCall(c); $event.stopPropagation()" aria-label="Aus Anruf Anfrage erstellen"><mat-icon>confirmation_number</mat-icon></button> }
              <button mat-icon-button (click)="openEdit(c); $event.stopPropagation()" aria-label="Bearbeiten"><mat-icon>edit</mat-icon></button>
              <button mat-icon-button (click)="openDetail(c); $event.stopPropagation()" aria-label="Details"><mat-icon>visibility</mat-icon></button>
              @if (canDelete) { <button mat-icon-button (click)="remove(c); $event.stopPropagation()" aria-label="Löschen"><mat-icon>delete</mat-icon></button> }
            </div>
          </article>
        } @empty { <p class="call-empty">{{ isInquiry() ? 'Noch keine Anfragen erfasst.' : 'Noch keine Anrufe erfasst.' }}</p> }
      </div>
    </mat-card>
  `
})
export class CallsComponent {

  private readonly auth = inject(AuthService);
  // Ohne Schreib-/Loeschrecht werden die Aktionen gar nicht angeboten.
  readonly canWrite = this.auth.canWrite('customers');
  readonly canDelete = this.auth.canDelete('customers');
  readonly canCreateProject = this.auth.canWrite('projects');  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly calls = signal<GenericRecord[]>([]);
  /** Alle Kunden – Grundlage der Suche in der Schnellerfassung. */
  readonly customers = signal<GenericRecord[]>([]);
  readonly projectsForCalls = signal<GenericRecord[]>([]);
  /** Der zugeordnete Kunde, sofern gewählt. */
  readonly matchedCustomer = signal<GenericRecord | null>(null);
  readonly customerHits = signal<GenericRecord[]>([]);

  /** Kundenliste laden – einmalig beim Öffnen der Seite. */
  private loadCustomersForSearch(): void {
    this.api.list('customers', '', 1000).subscribe({
      next: (r) => this.customers.set(r.items || []),
      error: () => this.customers.set([])
    });
    this.api.list('projects', '', 500).subscribe({
      next: (r) => this.projectsForCalls.set(r.items || []),
      error: () => this.projectsForCalls.set([])
    });
  }
  /** Vereinheitlichen: Umlaute, Groß-/Kleinschreibung, Telefonzeichen. */
  private norm(value: unknown): string {
    return String(value ?? '')
      .toLowerCase()
      .replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss')
      .replace(/[\s()/.\-]/g, '');
  }
  /**
   * Suche über ALLE Kunden.
   *
   * Gesucht wird in Name, Firma, Telefon, Mobil, Ort und Kundennummer.
   * Telefonnummern werden dabei von Leerzeichen, Schrägstrichen und
   * Klammern befreit – "0221 / 12 34 56" findet man damit auch als
   * "022112".
   */
  onWhoChange(): void {
    this.matchedCustomer.set(null);
    const q = this.norm(this.quickWho);
    if (q.length < 2) { this.customerHits.set([]); return; }
    const hits = this.customers().filter((c) =>
      ['name', 'company', 'phone', 'mobile', 'email', 'city', 'customer_no']
        .some((k) => this.norm(c[k]).includes(q))
    ).slice(0, 6);
    this.customerHits.set(hits);
  }
  pickCustomer(c: GenericRecord): void {
    this.matchedCustomer.set(c);
    this.customerHits.set([]);
    this.quickWho = String(c['name'] || '');
  }
  clearCustomer(): void { this.matchedCustomer.set(null); this.customerHits.set([]); }
  /** Wie viele Bauakten des Kunden sind noch offen? Hilft beim Gespräch. */
  openProjectsFor(c: GenericRecord): number {
    const closed = ['completed', 'closed', 'cancelled', 'abgeschlossen', 'storniert'];
    return this.projectsForCalls().filter((p) =>
      String(p['customer_id'] || '') === String(c['id'])
      && !closed.includes(String(p['status'] || '').toLowerCase())).length;
  }
  readonly projects = signal<GenericRecord[]>([]);
  readonly company = signal('');
  readonly onlyFollowUp = signal(false);
  readonly hideClosedInquiries = signal(true);
  readonly kind = signal<'call' | 'inquiry'>('call');
  search = '';

  readonly isInquiry = computed(() => this.kind() === 'inquiry');

  readonly filtered = computed(() => {
    const q = this.search.trim().toLowerCase();
    let list = this.calls();
    if (this.onlyFollowUp()) list = list.filter((c) => !!c['follow_up']);
    if (this.isInquiry() && this.hideClosedInquiries()) list = list.filter((c) => ['open','in_progress'].includes(String(c['inquiry_status'] || 'open')));
    if (!q) return list;
    return list.filter((c) => [c['caller_name'], c['caller_phone'], c['subject'], c['company'], c['notes']].join(' ').toLowerCase().includes(q));
  });

  constructor() {
    this.kind.set(this.route.snapshot.data['callKind'] === 'inquiry' ? 'inquiry' : 'call');
    this.load();
    this.loadCustomersForSearch();
    this.api.publicAppInfo().subscribe({ next: (info) => { const n = info?.['company']?.['name']; if (n) this.company.set(String(n)); }, error: () => {} });
  }
  load(): void {
    forkJoin({ calls: this.api.listCalls(this.kind()), projects: this.api.list('projects', '', 300) }).subscribe({
      next: ({ calls, projects }) => { this.calls.set(calls.items || []); this.projects.set(projects.items || []); },
      error: () => this.snack.open(this.isInquiry() ? 'Anfragen konnten nicht geladen werden.' : 'Anrufe konnten nicht geladen werden.', 'OK', { duration: 3000 })
    });
  }
  projectLabel(p: GenericRecord): string { return [p['project_no'], p['name']].filter(Boolean).join(' · ') || String(p['id']); }
  shortProject(id: unknown): string { const p = this.projects().find((x) => String(x['id']) === String(id)); return p ? String(p['project_no'] || p['name'] || '').slice(0, 24) : 'Auftrag'; }
  inquiryStatusLabel(c: GenericRecord): string { return ({ open: 'Offen', in_progress: 'In Bearbeitung', cancelled: 'Abgebrochen', converted: 'Bauakte erstellt' } as Record<string, string>)[String(c['inquiry_status'] || 'open')] || 'Offen'; }

  openNew(): void {
    const ref = this.dialog.open(CallFormDialogComponent, { width: 'min(640px, 96vw)', maxWidth: '96vw', autoFocus: false, data: { projects: this.projects(), kind: this.kind() } });
    ref.afterClosed().subscribe((res) => {
      if (res?.created) { this.calls.update((l) => [res.call, ...l]); this.snack.open(this.isInquiry() ? 'Anfrage gespeichert.' : (res.call['project_id'] ? 'Anruf gespeichert + Notiz im Auftrag abgelegt.' : 'Anruf gespeichert.'), 'OK', { duration: 3000 }); }
    });
  }
  // ---- Schnellerfassung -------------------------------------------------
  /**
   * Erfassung während des Gesprächs.
   *
   * Gedanke dahinter: Wer den Kunden am Apparat hat, kann nicht erst ein
   * Formular mit zehn Feldern ausfüllen. Zwei Felder genügen – Name bzw.
   * Nummer und die Notiz. Richtung, Zeitpunkt und Bearbeiter setzt die
   * Anwendung selbst; alles Weitere lässt sich danach in Ruhe ergänzen.
   */
  readonly quickMode = signal(false);
  readonly quickDirection = signal<'inbound' | 'outbound'>('inbound');
  readonly quickFollowUp = signal(false);
  readonly quickStarted = signal<Date | null>(null);
  readonly quickClock = signal('00:00');
  quickWho = '';
  quickNote = '';
  private quickTimer: ReturnType<typeof setInterval> | null = null;

  /** Häufige Stichworte – ein Klick hängt sie an die Notiz an. */
  readonly quickTemplates = [
    'Rückruf gewünscht',
    'Termin vereinbaren',
    'Angebot erbeten',
    'Nachfrage zum Auftrag',
    'Reklamation',
    'Nicht erreicht'
  ];

  quickOpen(): void {
    this.quickMode.set(true);
    this.quickStarted.set(new Date());
    this.quickClock.set('00:00');
    this.quickTimer = setInterval(() => {
      const start = this.quickStarted();
      if (!start) return;
      const sec = Math.floor((Date.now() - start.getTime()) / 1000);
      this.quickClock.set(`${String(Math.floor(sec / 60)).padStart(2, '0')}:${String(sec % 60).padStart(2, '0')}`);
    }, 1000);
    setTimeout(() => (document.querySelector('.quick-who input') as HTMLInputElement | null)?.focus(), 60);
  }
  quickClose(): void {
    this.quickMode.set(false);
    if (this.quickTimer) { clearInterval(this.quickTimer); this.quickTimer = null; }
    this.quickWho = ''; this.quickNote = '';
    this.clearCustomer();
    this.quickFollowUp.set(false); this.quickStarted.set(null);
  }
  quickAppend(text: string): void {
    this.quickNote = this.quickNote.trim() ? `${this.quickNote.trim()}\n${text}` : text;
  }
  /** Name und Telefonnummer aus einer Eingabe trennen. */
  private splitWho(value: string): { name: string; phone: string } {
    const raw = value.trim();
    if (!raw) return { name: '', phone: '' };
    // Überwiegend Ziffern, Plus, Schrägstrich, Leerzeichen -> Telefonnummer
    if (/^[+0-9 ()/.-]{5,}$/.test(raw)) return { name: '', phone: raw };
    const match = raw.match(/^(.*?)[,;]?\s*([+0-9][0-9 ()/.-]{4,})$/);
    if (match) return { name: match[1].trim(), phone: match[2].trim() };
    return { name: raw, phone: '' };
  }
  private quickPayload(): GenericRecord {
    const { name, phone } = this.splitWho(this.quickWho);
    const started = this.quickStarted() || new Date();
    const duration = Math.max(0, Math.round((Date.now() - started.getTime()) / 60000));
    const notes = [this.quickNote.trim(), duration >= 1 ? `Gesprächsdauer: ca. ${duration} Min.` : '']
      .filter(Boolean).join('\n\n');
    const linked = this.matchedCustomer();
    return {
      // `kind` ausdrücklich mitgeben. Fehlte es, hat die API einen leeren
      // Wert in eine Pflichtspalte geschrieben – das war die Ursache des
      // Serverfehlers. Der Fehler ist serverseitig behoben; hier steht es
      // zusätzlich, damit der Aufruf aus sich heraus vollständig ist.
      kind: 'call',
      // Bei zugeordnetem Kunden dessen Stammdaten verwenden – der Anruf
      // hängt dann an der Kundenakte und nicht nur an einem Namen.
      customer_id: linked?.['id'] ?? null,
      caller_name: linked ? String(linked['name'] || name) : name,
      caller_phone: phone || String(linked?.['phone'] || linked?.['mobile'] || ''),
      direction: this.quickDirection(),
      called_at: this.sqlNow(started),
      subject: this.quickSubject(),
      notes,
      follow_up: this.quickFollowUp() ? 1 : 0,
      handled_by: this.auth.userName || ''
    };
  }
  /** Betreff aus der ersten Notizzeile ableiten. */
  private quickSubject(): string {
    const first = this.quickNote.split('\n').map((l) => l.trim()).find((l) => l.length > 0) || '';
    if (!first) return this.quickDirection() === 'outbound' ? 'Ausgehender Anruf' : 'Eingehender Anruf';
    return first.length > 80 ? first.slice(0, 77) + '…' : first;
  }
  private sqlNow(d: Date): string {
    const p = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:00`;
  }
  quickSave(): void {
    if (!this.quickWho.trim() && !this.quickNote.trim()) return;
    const payload = this.quickPayload();
    this.api.createCall(payload).subscribe({
      next: (call) => {
        this.calls.update((l) => [call, ...l]);
        this.snack.open('Anruf gespeichert.', 'OK', { duration: 2200 });
        // Bereit für das nächste Gespräch – die Leiste bleibt offen.
        this.quickWho = ''; this.quickNote = '';
        this.clearCustomer();
        this.quickFollowUp.set(false);
        this.quickStarted.set(new Date());
        setTimeout(() => (document.querySelector('.quick-who input') as HTMLInputElement | null)?.focus(), 40);
      },
      error: () => this.snack.open('Anruf konnte nicht gespeichert werden.', 'OK', { duration: 3200 })
    });
  }
  /** Speichern und sofort im vollen Formular weiterbearbeiten. */
  quickSaveAndOpen(): void {
    if (!this.quickWho.trim() && !this.quickNote.trim()) return;
    const payload = this.quickPayload();
    this.api.createCall(payload).subscribe({
      next: (call) => {
        this.calls.update((l) => [call, ...l]);
        this.quickClose();
        this.openEdit(call);
      },
      error: () => this.snack.open('Anruf konnte nicht gespeichert werden.', 'OK', { duration: 3200 })
    });
  }

  openEdit(call: GenericRecord): void {
    const ref = this.dialog.open(CallFormDialogComponent, { width: 'min(640px, 96vw)', maxWidth: '96vw', autoFocus: false, data: { call, projects: this.projects(), kind: this.kind() } });
    ref.afterClosed().subscribe((res) => {
      if (res?.updated) { this.calls.update((l) => l.map((c) => c['id'] === res.call['id'] ? res.call : c)); this.snack.open(this.isInquiry() ? 'Anfrage aktualisiert.' : 'Anruf aktualisiert.', 'OK', { duration: 2200 }); }
    });
  }
  openDetail(call: GenericRecord): void {
    if (this.isInquiry()) { void this.router.navigate(['/anfragen', call['id']]); return; }
    const linkedId = call['converted_project_id'] || call['project_id'];
    const p = this.projects().find((x) => String(x['id']) === String(linkedId));
    this.dialog.open(CallDetailDialogComponent, { width: 'min(560px, 96vw)', maxWidth: '96vw', autoFocus: false, data: { call, projectLabel: p ? this.projectLabel(p) : '', company: this.company(), isInquiry: this.isInquiry(), statusLabel: this.inquiryStatusLabel(call) } });
  }
  remove(call: GenericRecord): void {
    const ref = this.dialog.open(ConfirmDialogComponent, { width: 'min(460px, 96vw)', maxWidth: '96vw', data: { title: this.isInquiry() ? 'Anfrage löschen?' : 'Anruf löschen?', message: this.isInquiry() ? 'Diese Anfrage wirklich löschen?' : 'Diesen Anruf wirklich löschen?', confirmText: 'Löschen', icon: 'delete', tone: 'warn' } });
    ref.afterClosed().subscribe((ok) => { if (!ok) return; this.api.deleteCall(String(call['id'])).subscribe({
      next: () => { this.calls.update((l) => l.filter((c) => c['id'] !== call['id'])); this.snack.open(this.isInquiry() ? 'Anfrage gelöscht.' : 'Anruf gelöscht.', 'OK', { duration: 2000 }); },
      error: () => this.snack.open('Löschen fehlgeschlagen.', 'OK', { duration: 3000 })
    }); });
  }
  cancelInquiry(call: GenericRecord): void {
    const ref = this.dialog.open(ConfirmDialogComponent, { width: 'min(460px, 96vw)', maxWidth: '96vw', data: { title: 'Anfrage abbrechen?', message: 'Diese Anfrage abbrechen? Sie bleibt dokumentiert, wird aber nicht mehr als offen geführt.', confirmText: 'Abbrechen', icon: 'cancel', tone: 'warn' } });
    ref.afterClosed().subscribe((ok) => { if (!ok) return; this.api.cancelInquiry(String(call['id'])).subscribe({
      next: (updated) => { this.calls.update((l) => l.map((c) => c['id'] === updated['id'] ? updated : c)); this.snack.open('Anfrage abgebrochen.', 'OK', { duration: 2200 }); },
      error: () => this.snack.open('Anfrage konnte nicht abgebrochen werden.', 'OK', { duration: 3200 })
    }); });
  }
  createInquiryFromCall(call: GenericRecord): void {
    const ref = this.dialog.open(ConfirmDialogComponent, { width: 'min(460px, 96vw)', maxWidth: '96vw', data: { title: 'Aus Anruf Anfrage erstellen?', message: 'Aus diesem Anruf wird ein neues Anfrage-Ticket erstellt. Der Anruf bleibt erhalten.', confirmText: 'Anfrage erstellen', icon: 'confirmation_number', tone: 'primary' } });
    ref.afterClosed().subscribe((ok) => { if (!ok) return; this.api.createInquiryFromCall(String(call['id'])).subscribe({ next: (created) => { this.snack.open('Anfrage erstellt.', 'OK', { duration: 2200 }); void this.router.navigate(['/anfragen', created['id']]); }, error: () => this.snack.open('Anfrage konnte nicht erstellt werden.', 'OK', { duration: 3000 }) }); });
  }
  convertInquiry(call: GenericRecord): void {
    void this.router.navigate(['/auftrag-wizard'], { queryParams: {
      inquiry_id: call['id'],
      inquiry_name: call['caller_name'] || '',
      inquiry_phone: call['caller_phone'] || '',
      inquiry_company: call['company'] || '',
      inquiry_subject: call['subject'] || '',
      inquiry_notes: call['notes'] || ''
    }});
  }
  exportList(): void {
    const rows = this.filtered();
    const doc = new jsPDF({ unit: 'mm', format: 'a4' });
    const PW = 210, PH = 297, left = 14, right = 14; const W = PW - left - right;
    const printedBy = this.company() || 'EstrichWeb';
    let page = 1;
    const C = { date: { x: left, w: 30 }, who: { x: left + 30, w: 56 }, subj: { x: left + 86, w: 56 }, proj: { x: left + 142, w: 32 }, flag: { x: left + 174, w: W - 174 } };
    const drawHeader = (): number => {
      let y = 16;
      doc.setDrawColor(0); doc.setLineWidth(0.8); doc.rect(left, y, W, 18, 'S');
      doc.setFillColor(0, 0, 0); doc.rect(left, y, 5, 18, 'F');
      doc.setFont('helvetica', 'bold'); doc.setFontSize(14); doc.setTextColor(0);
      doc.text(this.isInquiry() ? 'ANFRAGENLISTE' : 'ANRUFLISTE', left + 10, y + 8);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5);
      doc.text(this.company() || '', left + 10, y + 14);
      doc.text(`Stand: ${new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date())}`, PW - right - 3, y + 8, { align: 'right' });
      if (this.onlyFollowUp()) doc.text('Filter: nur Wiedervorlagen', PW - right - 3, y + 14, { align: 'right' });
      y += 24;
      doc.setFillColor(235, 235, 235); doc.rect(left, y - 5, W, 8, 'F');
      doc.setDrawColor(0); doc.setLineWidth(0.4); doc.rect(left, y - 5, W, 8, 'S');
      doc.setFont('helvetica', 'bold'); doc.setFontSize(8); doc.setTextColor(0);
      doc.text('DATUM', C.date.x + 1, y); doc.text('NAME / TELEFON', C.who.x + 1, y);
      doc.text(this.isInquiry() ? 'ANLIEGEN' : 'BETREFF', C.subj.x + 1, y); doc.text(this.isInquiry() ? 'STATUS' : 'AUFTRAG', C.proj.x + 1, y);
      doc.text('W.V.', C.flag.x + 1, y);
      return y + 6;
    };
    const footer = () => {
      doc.setDrawColor(0); doc.setLineWidth(0.3); doc.line(left, PH - 16, PW - right, PH - 16);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); doc.setTextColor(80);
      doc.text(`${rows.length} Einträge · W.V. = Wiedervorlage`, left, PH - 10);
      doc.text(`Seite ${page}`, PW / 2, PH - 10, { align: 'center' });
      doc.text(`Gedruckt: ${printedBy}`, PW - right, PH - 10, { align: 'right' });
      doc.setTextColor(0);
    };
    let y = drawHeader();
    const rowH = 9;
    rows.forEach((c, i) => {
      if (y + rowH > PH - 20) { footer(); doc.addPage(); page += 1; y = drawHeader(); }
      if (i % 2 === 1) { doc.setFillColor(248, 248, 248); doc.rect(left, y - 4.5, W, rowH, 'F'); }
      const dir = this.isInquiry() ? this.inquiryStatusLabel(c) : (c['direction'] === 'outbound' ? 'Ausgehend' : 'Eingehend');
      const dt = c['called_at'] ? new Intl.DateTimeFormat('de-DE', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(String(c['called_at']))) : '—';
      const name = String(c['caller_name'] || '—');
      const phone = String(c['caller_phone'] || '');
      const subj = String(c['subject'] || '—');
      const proj = this.isInquiry() ? this.inquiryStatusLabel(c) : (c['project_id'] ? this.shortProject(c['project_id']) : '–');
      doc.setFont('helvetica', 'normal'); doc.setFontSize(8); doc.setTextColor(0);
      doc.text(dt, C.date.x + 1, y);
      doc.setFont('helvetica', 'bold'); doc.text((doc.splitTextToSize(name, C.who.w - 2) as string[]).slice(0, 1), C.who.x + 1, y);
      doc.setFont('helvetica', 'normal'); doc.text((doc.splitTextToSize(subj, C.subj.w - 2) as string[]).slice(0, 1), C.subj.x + 1, y);
      doc.text((doc.splitTextToSize(proj, C.proj.w - 2) as string[]).slice(0, 1), C.proj.x + 1, y);
      if (c['follow_up']) { doc.setFont('helvetica', 'bold'); doc.text('JA', C.flag.x + 1, y); doc.setFont('helvetica', 'normal'); }
      doc.setFontSize(6.6); doc.setTextColor(110);
      doc.text(dir, C.date.x + 1, y + 3.6);
      if (phone) doc.text((doc.splitTextToSize(phone, C.who.w - 2) as string[]).slice(0, 1), C.who.x + 1, y + 3.6);
      doc.setTextColor(0);
      y += rowH;
      doc.setDrawColor(225); doc.setLineWidth(0.2); doc.line(left, y - 4.5, PW - right, y - 4.5);
    });
    if (!rows.length) { doc.setFont('helvetica', 'italic'); doc.setFontSize(9); doc.setTextColor(120); doc.text('Keine Einträge.', left, y + 4); doc.setTextColor(0); }
    footer();
    doc.save(`${this.isInquiry() ? 'Anfragenliste' : 'Anrufliste'}_${new Date().toISOString().slice(0, 10)}.pdf`);
  }
}
