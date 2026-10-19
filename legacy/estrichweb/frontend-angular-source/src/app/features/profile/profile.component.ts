import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../core/auth.service';
import { ApiService, GenericRecord } from '../../core/api.service';
import { PasswordChangeDialogComponent } from './password-change-dialog.component';
import { QuickPinDialogComponent } from './quick-pin-dialog.component';
import { WhatsNewDialogComponent } from '../whats-new/whats-new-dialog.component';

@Component({
  standalone: true,
  imports: [MatCardModule, MatButtonModule, MatIconModule, MatDialogModule, MatSnackBarModule],
  template: `
    <section class="page-head compact-head profile-head">
      <div class="profile-id">
        <span class="profile-avatar">{{ initials() }}</span>
        <div><p class="eyebrow">Mein Profil</p><h1>{{ auth.userName || 'Benutzer' }}</h1><span class="profile-sub">{{ auth.userEmail }}</span></div>
      </div>
    </section>

    <div class="profile-layout">
      <!-- Konto -->
      <mat-card class="square-card profile-card">
        <div class="card-headline"><h3>Konto</h3></div>
        <dl class="profile-detail-list">
          <div><dt>Name</dt><dd>{{ auth.userName || '—' }}</dd></div>
          <div><dt>E-Mail</dt><dd>{{ auth.userEmail || '—' }}</dd></div>
          <div><dt>Firma</dt><dd>{{ auth.tenantName || '—' }}</dd></div>
          <div><dt>Rolle</dt><dd>{{ roleLabel() }}</dd></div>
          @if (lastLogin()) { <div><dt>Letzte Anmeldung</dt><dd>{{ lastLogin() }}</dd></div> }
        </dl>
      </mat-card>

      <!-- Sicherheit: das, was man am eigenen Konto wirklich tun kann -->
      <mat-card class="square-card profile-card">
        <div class="card-headline"><h3>Sicherheit</h3></div>
        <dl class="profile-detail-list">
          <div><dt>Passwort</dt><dd>{{ passwordAgeLabel() }}</dd></div>
          <div>
            <dt>Zwei-Faktor</dt>
            <dd>
              <span class="profile-flag" [class.on]="twoFactor()">
                <mat-icon>{{ twoFactor() ? 'verified_user' : 'shield' }}</mat-icon>
                {{ twoFactor() ? 'aktiv' : 'nicht eingerichtet' }}
              </span>
            </dd>
          </div>
        </dl>
        <div class="profile-actions">
          <button mat-flat-button color="primary" (click)="changePassword()"><mat-icon>key</mat-icon>Passwort ändern</button>
        </div>

        <!-- Schnellanmeldung: nur sichtbar, wenn der Betrieb sie freigeschaltet hat -->
        @if (quickEnabled()) {
          <div class="profile-quick">
            <h4><mat-icon>pin</mat-icon>Schnellanmeldung auf diesem Gerät</h4>
            @if (quickHere()) {
              <p class="profile-quick-on">
                <mat-icon>check_circle</mat-icon>
                <span>Eingerichtet. Beim nächsten Anmelden genügt Ihre 6-stellige PIN.</span>
              </p>
              <div class="profile-actions">
                <button mat-stroked-button (click)="setupQuick()"><mat-icon>edit</mat-icon>PIN ändern</button>
                <button mat-stroked-button class="profile-logout" (click)="removeQuick(false)"><mat-icon>link_off</mat-icon>Auf diesem Gerät aufheben</button>
              </div>
            } @else {
              <p class="profile-hint">
                Nach der Einrichtung genügt auf <b>diesem Gerät</b> eine 6-stellige PIN.
                Die PIN gilt nur zusammen mit diesem Gerät – auf einem anderen Rechner
                nützt sie niemandem. Nach fünf Fehlversuchen ist wieder die vollständige
                Anmeldung nötig.
              </p>
              <div class="profile-actions">
                <button mat-flat-button color="primary" (click)="setupQuick()"><mat-icon>add</mat-icon>PIN einrichten</button>
              </div>
            }
            @if (quickDevices().length > 1) {
              <p class="profile-hint">
                Für Ihr Konto sind {{ quickDevices().length }} Geräte hinterlegt.
                <button type="button" class="profile-link" (click)="removeQuick(true)">Alle aufheben</button>
              </p>
            }
          </div>
        }
        <p class="profile-hint">
          Melden Sie sich nach dem Ändern auf allen Geräten neu an. Bei Verdacht auf
          einen unbefugten Zugriff bitte umgehend die Administration verständigen.
        </p>
      </mat-card>

      <!-- Berechtigungen: nachvollziehbar statt einer bloßen Zahl -->
      <mat-card class="square-card profile-card profile-card-wide">
        <div class="card-headline">
          <h3>Meine Berechtigungen</h3>
          <span>{{ permissionSummary() }}</span>
        </div>
        @if (isAdmin()) {
          <p class="profile-admin"><mat-icon>admin_panel_settings</mat-icon>Administrator – alle Bereiche und Aktionen freigeschaltet.</p>
        } @else if (permissionGroups().length) {
          <div class="profile-perms">
            @for (g of permissionGroups(); track g.area) {
              <div class="profile-perm">
                <b>{{ g.label }}</b>
                <span>
                  @for (a of g.actions; track a) { <em>{{ a }}</em> }
                </span>
              </div>
            }
          </div>
        } @else {
          <p class="profile-hint">Für dieses Konto sind keine besonderen Berechtigungen hinterlegt.</p>
        }
      </mat-card>

      <!-- Anwendung -->
      <mat-card class="square-card profile-card profile-card-wide">
        <div class="card-headline"><h3>Anwendung</h3></div>
        <dl class="profile-detail-list profile-detail-inline">
          <div><dt>Version</dt><dd>{{ appVersion() }}</dd></div>
          <div><dt>Installiert als App</dt><dd>{{ isStandalone() ? 'ja' : 'nein' }}</dd></div>
          <div><dt>Verbindung</dt><dd>{{ online() ? 'online' : 'offline' }}</dd></div>
        </dl>
        <div class="profile-actions">
          <button mat-stroked-button (click)="openWhatsNew()"><mat-icon>auto_awesome</mat-icon>Was gibt es Neues</button>
          <button mat-stroked-button (click)="reloadApp()"><mat-icon>refresh</mat-icon>Anwendung neu laden</button>
          <button mat-stroked-button class="profile-logout" (click)="auth.logout()"><mat-icon>logout</mat-icon>Abmelden</button>
        </div>
      </mat-card>
    </div>
  `
})
export class ProfileComponent {
  readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);

  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);

  readonly profile = signal<GenericRecord | null>(null);
  readonly online = signal(navigator.onLine);

  constructor() {
    // Zusatzangaben zum eigenen Konto laden (letzte Anmeldung, 2FA, Rolle).
    this.api.me().subscribe({ next: (r) => this.profile.set(r), error: () => {} });
    this.loadQuick();
    window.addEventListener('online', () => this.online.set(true));
    window.addEventListener('offline', () => this.online.set(false));
  }

  initials(): string { const n = (this.auth.userName || '?').trim().split(/\s+/); return ((n[0]?.[0] || '') + (n[1]?.[0] || '')).toUpperCase() || '?'; }
  isAdmin(): boolean { return (this.auth.permissions || []).includes('*'); }
  permissionSummary(): string {
    const p = this.auth.permissions || [];
    if (p.includes('*')) return 'Administrator';
    return p.length ? `${p.length} Einzelrechte` : 'Standardrechte';
  }
  roleLabel(): string {
    const r = String(this.profile()?.['role_name'] || this.profile()?.['role'] || '').trim();
    if (r) return r;
    return this.isAdmin() ? 'Administrator' : 'Mitarbeiter';
  }
  lastLogin(): string {
    const raw = String(this.profile()?.['last_login_at'] || '');
    if (!raw) return '';
    const d = new Date(raw.replace(' ', 'T'));
    return isNaN(d.getTime()) ? '' : d.toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' });
  }
  twoFactor(): boolean { return !!Number(this.profile()?.['two_factor_enabled'] || 0); }
  /** Wie alt ist das Passwort? Ein Hinweis, keine Vorschrift. */
  passwordAgeLabel(): string {
    const raw = String(this.profile()?.['password_changed_at'] || '');
    if (!raw) return 'Änderungszeitpunkt nicht hinterlegt';
    const d = new Date(raw.replace(' ', 'T'));
    if (isNaN(d.getTime())) return 'Änderungszeitpunkt nicht hinterlegt';
    const days = Math.floor((Date.now() - d.getTime()) / 86400000);
    if (days <= 0) return 'heute geändert';
    if (days === 1) return 'gestern geändert';
    if (days < 60) return `vor ${days} Tagen geändert`;
    return `zuletzt geändert am ${d.toLocaleDateString('de-DE')}`;
  }

  /**
   * Berechtigungen nach Bereich gruppieren.
   * Aus "projects.write" wird "Bauakten – ändern". Eine bloße Zahl sagt
   * niemandem, was er darf.
   */
  permissionGroups(): { area: string; label: string; actions: string[] }[] {
    const areaNames: Record<string, string> = {
      projects: 'Bauakten', customers: 'Kunden', planning: 'Planung',
      materials: 'Material', documents: 'Dokumente', reports: 'Berichte',
      vehicles: 'Fahrzeuge', crews: 'Kolonnen', users: 'Benutzer',
      inspections: 'Begehungen', calendar: 'Kalender', sites: 'Baustellen'
    };
    const actionNames: Record<string, string> = {
      read: 'sehen', write: 'ändern', delete: 'löschen', export: 'exportieren'
    };
    const map = new Map<string, Set<string>>();
    for (const perm of this.auth.permissions || []) {
      const [area, action] = String(perm).split('.');
      if (!area || !action) continue;
      if (!map.has(area)) map.set(area, new Set());
      map.get(area)!.add(actionNames[action] || action);
    }
    return Array.from(map.entries())
      .map(([area, actions]) => ({ area, label: areaNames[area] || area, actions: Array.from(actions) }))
      .sort((a, b) => a.label.localeCompare(b.label, 'de'));
  }

  appVersion(): string { return String(this.profile()?.['app_version'] || '—'); }
  isStandalone(): boolean {
    try { return window.matchMedia('(display-mode: standalone)').matches; } catch { return false; }
  }
  changePassword(): void {
    this.dialog.open(PasswordChangeDialogComponent, {
      width: 'min(480px, 94vw)', panelClass: 'ew-dialog', autoFocus: false, data: {}
    }).afterClosed().subscribe((ok) => {
      if (ok) this.snack.open('Passwort geändert. Bitte auf anderen Geräten neu anmelden.', 'OK', { duration: 5000 });
    });
  }
  reloadApp(): void { location.reload(); }

  // ---- Schnellanmeldung -------------------------------------------------
  readonly quickEnabled = signal(false);
  readonly quickDevices = signal<GenericRecord[]>([]);

  /** Kennung dieses Geräts – dieselbe wie in der Anmeldemaske. */
  private deviceId(): string {
    try {
      let id = localStorage.getItem('ew.device.id');
      if (!id) {
        id = (crypto.randomUUID?.() || String(Date.now()) + Math.random().toString(36).slice(2)).replace(/-/g, '');
        localStorage.setItem('ew.device.id', id);
      }
      return id;
    } catch { return ''; }
  }
  /** Ist gerade DIESES Gerät hinterlegt? */
  quickHere(): boolean {
    const id = this.deviceId();
    return this.quickDevices().some((d) => String(d['device_id']) === id);
  }
  private loadQuick(): void {
    this.api.quickLoginDevices().subscribe({
      next: (r) => {
        this.quickEnabled.set(!!r?.['enabled']);
        this.quickDevices.set((r?.['items'] as GenericRecord[]) || []);
      },
      error: () => this.quickEnabled.set(false)
    });
  }
  /** Sprechender Gerätename, damit man Einträge später zuordnen kann. */
  private deviceLabel(): string {
    const ua = navigator.userAgent;
    const os = /Windows/.test(ua) ? 'Windows-PC'
      : /Android/.test(ua) ? 'Android-Gerät'
      : /iPhone/.test(ua) ? 'iPhone'
      : /iPad/.test(ua) ? 'iPad'
      : /Macintosh/.test(ua) ? 'Mac' : 'Gerät';
    return `${os} · ${new Date().toLocaleDateString('de-DE')}`;
  }
  setupQuick(): void {
    this.dialog.open(QuickPinDialogComponent, {
      width: 'min(420px, 94vw)', panelClass: 'ew-dialog', autoFocus: false, data: {}
    }).afterClosed().subscribe((pin: string | undefined) => {
      if (!pin) return;
      this.api.setQuickLogin(this.deviceId(), pin, this.deviceLabel()).subscribe({
        next: () => { this.snack.open('Schnellanmeldung eingerichtet.', 'OK', { duration: 3000 }); this.loadQuick(); },
        error: (err) => this.snack.open(err?.error?.error || 'Die PIN konnte nicht gespeichert werden.', 'OK', { duration: 4000 })
      });
    });
  }
  removeQuick(all: boolean): void {
    this.api.removeQuickLogin(all ? undefined : this.deviceId()).subscribe({
      next: () => { this.snack.open(all ? 'Auf allen Geräten aufgehoben.' : 'Auf diesem Gerät aufgehoben.', 'OK', { duration: 3000 }); this.loadQuick(); },
      error: () => this.snack.open('Die Schnellanmeldung konnte nicht aufgehoben werden.', 'OK', { duration: 3500 })
    });
  }
  openWhatsNew(): void {
    this.dialog.open(WhatsNewDialogComponent, { width: 'min(640px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', autoFocus: false, data: {} });
  }
}
