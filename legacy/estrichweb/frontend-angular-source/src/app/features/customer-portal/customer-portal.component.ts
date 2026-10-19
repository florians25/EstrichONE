import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { ApiService, GenericRecord } from '../../core/api.service';

@Component({
  standalone: true,
  imports: [DatePipe, MatCardModule, MatIconModule, MatButtonModule],
  template: `
    <div class="portal-shell">
      @if (loading()) {
        <div class="portal-loading"><mat-icon>hourglass_top</mat-icon><span>Portal wird geladen…</span></div>
      } @else if (error()) {
        <mat-card class="portal-card portal-error"><mat-icon>lock</mat-icon><h2>Kein Zugriff</h2><p>{{ error() }}</p></mat-card>
      } @else if (needsPin()) {
        <mat-card class="portal-card portal-pin-card">
          <div class="portal-brand portal-pin-brand"><mat-icon>lock</mat-icon><div><strong>{{ company()?.['name'] || 'Kundenportal' }}</strong><span>Geschützter Zugang</span></div></div>
          <h2 class="portal-pin-title">Bitte PIN eingeben</h2>
          <p class="portal-pin-hint">Den 5-stelligen PIN finden Sie auf Ihrem Zugangsschreiben.</p>
          <input class="portal-pin-input" inputmode="numeric" maxlength="5" [value]="pin()" (input)="onPin($event)" placeholder="•••••" autocomplete="one-time-code">
          @if (pinError()) { <p class="portal-pin-error">{{ pinError() }}</p> }
          <button mat-flat-button color="primary" class="portal-pin-btn" [disabled]="pin().length !== 5 || verifying()" (click)="verifyPin()"><mat-icon>login</mat-icon>Zugang öffnen</button>
        </mat-card>
      } @else {
        <header class="portal-head">
          <div class="portal-brand"><mat-icon>handshake</mat-icon><div><strong>{{ company()?.['name'] || 'Kundenportal' }}</strong><span>Kundenportal</span></div></div>
          <div class="portal-customer">Angemeldet als<br><b>{{ customer()?.['name'] }}</b></div>
        </header>

        <mat-card class="portal-card">
          <h2><mat-icon>folder_shared</mat-icon>Ihre Bauvorhaben</h2>
          @if (projects().length) {
            <div class="portal-projects">
              @for (p of projects(); track p['id']) {
                <article class="portal-project">
                  <div class="portal-project-main"><b>{{ p['name'] || 'Bauvorhaben' }}</b><small>{{ p['project_no'] }}</small></div>
                  @if (p['address'] || p['city']) { <div class="portal-project-addr"><mat-icon>location_on</mat-icon>{{ addrLine(p) }}</div> }
                  <div class="portal-project-meta">
                    <span class="portal-status">{{ statusLabel(p['status']) }}</span>
                    <small>{{ p['start_date'] ? (p['start_date'] | date:'dd.MM.yyyy') : '' }}@if (p['end_date']) { – {{ p['end_date'] | date:'dd.MM.yyyy' }} }</small>
                    @if (docCountFor(p['id']) > 0) { <small class="portal-project-docs"><mat-icon>description</mat-icon>{{ docCountFor(p['id']) }} Dokument(e)</small> }
                  </div>
                </article>
              }
            </div>
          } @else { <p class="portal-empty">Aktuell sind keine Bauvorhaben hinterlegt.</p> }
        </mat-card>

        <mat-card class="portal-card">
          <h2><mat-icon>description</mat-icon>Freigegebene Dokumente</h2>
          @if (documents().length) {
            <ul class="portal-docs">
              @for (d of documents(); track d['id']) {
                <li>
                  <mat-icon>insert_drive_file</mat-icon>
                  <span class="portal-doc-name">{{ d['name'] }}</span>
                  <small>{{ d['created_at'] | date:'dd.MM.yyyy' }}</small>
                  <span class="portal-doc-actions">
                    <button mat-icon-button type="button" aria-label="Öffnen" (click)="openDocument(d)"><mat-icon>open_in_new</mat-icon></button>
                    <button mat-icon-button type="button" aria-label="Herunterladen" (click)="openDocument(d, true)"><mat-icon>download</mat-icon></button>
                  </span>
                </li>
              }
            </ul>
          } @else { <p class="portal-empty">Es wurden noch keine Dokumente für Sie freigegeben.</p> }
        </mat-card>

        <footer class="portal-foot">{{ company()?.['name'] }} · Dieses Portal ist vertraulich und nur für Sie bestimmt.</footer>
      }
    </div>
  `
})
export class CustomerPortalComponent {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly needsPin = signal(false);
  readonly pin = signal('');
  readonly pinError = signal('');
  readonly verifying = signal(false);
  private token = '';
  readonly customer = signal<GenericRecord | null>(null);
  readonly company = signal<GenericRecord | null>(null);
  readonly projects = signal<GenericRecord[]>([]);
  readonly documents = signal<GenericRecord[]>([]);

  constructor() {
    this.token = this.route.snapshot.paramMap.get('token') || '';
    if (!this.token) { this.error.set('Ungültiger Zugangslink.'); this.loading.set(false); return; }
    this.api.customerPortalData(this.token).subscribe({
      next: (res) => { this.company.set(res['company'] || null); this.needsPin.set(!!res['requires_pin']); this.loading.set(false); },
      error: (err) => { this.error.set(err?.error?.error || 'Dieser Zugang ist ungültig oder wurde widerrufen.'); this.loading.set(false); }
    });
  }
  onPin(event: Event): void {
    const v = (event.target as HTMLInputElement).value.replace(/\D+/g, '').slice(0, 5);
    this.pin.set(v); this.pinError.set('');
  }
  verifyPin(): void {
    if (this.pin().length !== 5) return;
    this.verifying.set(true); this.pinError.set('');
    this.api.customerPortalVerify(this.token, this.pin()).subscribe({
      next: (res) => { this.customer.set(res['customer']); this.company.set(res['company']); this.projects.set(res['projects'] || []); this.documents.set(res['documents'] || []); this.needsPin.set(false); this.verifying.set(false); },
      error: (err) => { this.verifying.set(false); this.pinError.set(err?.error?.error || 'PIN ist nicht korrekt.'); }
    });
  }
  addrLine(p: GenericRecord): string { return [p['address'], p['city']].map((v) => String(v ?? '').trim()).filter((v) => v.length > 0).join(', '); }
  docCountFor(projectId: unknown): number {
    const pid = String(projectId ?? '');
    return this.documents().filter((d) => String(d['project_id'] ?? '') === pid).length;
  }
  statusLabel(s: unknown): string {
    const map: Record<string, string> = { new: 'Neu', planned: 'Geplant', in_progress: 'In Arbeit', delayed: 'Verzögert', defect: 'Mangel', completed: 'Abgeschlossen', closed: 'Geschlossen', cancelled: 'Storniert', open: 'Offen', active: 'Aktiv' };
    return map[String(s || '')] || String(s || '—');
  }
  openDocument(doc: GenericRecord, download = false): void {
    if (!this.pin()) return;
    const url = this.api.customerPortalDocumentUrl(this.token, String(doc['id']), this.pin(), download);
    window.open(url, '_blank');
  }
}
