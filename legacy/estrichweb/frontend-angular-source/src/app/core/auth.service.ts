import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { ApiService, LoginResponse } from './api.service';

const SESSION_VERSION = '2026-05-30-gf-login-media-v1';
const STORAGE_KEYS = ['estrichweb.jwt', 'estrichweb.refresh', 'estrichweb.tenantId', 'estrichweb.tenantSlug', 'estrichweb.user', 'estrichweb.mustChangePw'];

function decodeJwtPayload(token: string): any | null {
  try {
    const part = token.split('.')[1];
    if (!part) return null;
    const normalized = part.replace(/-/g, '+').replace(/_/g, '/');
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
    return JSON.parse(atob(padded));
  } catch {
    return null;
  }
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly loggedIn = signal(this.hasUsableToken());

  constructor() {
    // Alte Builds konnten abgelaufene/inkompatible Tokens im Browser behalten.
    // Nach einem Deployment wird die Sitzung einmal sauber zurückgesetzt, damit
    // keine 401-Kaskaden beim Laden von Dashboard/Projektlisten entstehen.
    const knownVersion = localStorage.getItem('estrichweb.sessionVersion');
    if (knownVersion !== SESSION_VERSION) {
      this.clearSession(false);
      localStorage.setItem('estrichweb.sessionVersion', SESSION_VERSION);
    }
    this.loggedIn.set(this.hasUsableToken());
  }

  login(email: string, password: string) {
    return this.api.login(email, password).pipe(tap((response: LoginResponse) => this.storeLoginResponse(response)));
  }

  get refreshTokenValue(): string { return localStorage.getItem('estrichweb.refresh') || ''; }
  hasRefreshToken(): boolean { return !!this.refreshTokenValue; }
  setAccessToken(token: string): void { localStorage.setItem('estrichweb.jwt', token); this.loggedIn.set(true); }

  // Access-Token über das Refresh-Token erneuern (Session verlängern statt abmelden)
  refreshSession() {
    return this.api.refreshToken(this.refreshTokenValue).pipe(tap((res: { token: string }) => { if (res?.token) this.setAccessToken(res.token); }));
  }

  /**
   * Anmeldung mit Gerätekennung und PIN.
   * Die Antwort ist dieselbe wie bei der vollständigen Anmeldung, deshalb
   * läuft sie durch dieselbe Weiterverarbeitung.
   */
  quickLogin(deviceId: string, pin: string) {
    return this.api.quickLoginSubmit(deviceId, pin).pipe(tap((response: LoginResponse) => this.storeLoginResponse(response)));
  }

  completeMicrosoftSso(ticket: string) {
    return this.api.microsoftSsoComplete(ticket).pipe(tap((response: LoginResponse) => this.storeLoginResponse(response)));
  }

  microsoftSsoStatus() {
    return this.api.microsoftSsoStatus();
  }

  storeLoginResponse(response: LoginResponse): void {
    localStorage.setItem('estrichweb.jwt', response.token);
    if (response.refresh_token) localStorage.setItem('estrichweb.refresh', response.refresh_token);
    localStorage.setItem('estrichweb.tenantId', response.user.tenant_id);
    localStorage.setItem('estrichweb.tenantSlug', response.user.tenant_slug);
    localStorage.setItem('estrichweb.user', JSON.stringify(response.user));
    localStorage.setItem('estrichweb.sessionVersion', SESSION_VERSION);
    localStorage.setItem('estrichweb.mustChangePw', response.user.must_change_password ? '1' : '0');
    this.loggedIn.set(true);
  }

  get mustChangePassword(): boolean { return localStorage.getItem('estrichweb.mustChangePw') === '1'; }
  clearMustChangePassword(): void { localStorage.setItem('estrichweb.mustChangePw', '0'); }

  logout(): void {
    this.clearSession();
    void this.router.navigate(['/login']);
  }

  clearSession(removeVersion = false): void {
    STORAGE_KEYS.forEach((key) => localStorage.removeItem(key));
    if (removeVersion) localStorage.removeItem('estrichweb.sessionVersion');
    this.loggedIn.set(false);
  }

  isAuthenticated(): boolean {
    const valid = this.hasUsableToken();
    // Nur abräumen, wenn auch kein Refresh-Token mehr da ist (sonst kann verlängert werden).
    if (!valid && !this.hasRefreshToken()) this.clearSession(false);
    return valid;
  }

  get token(): string { return localStorage.getItem('estrichweb.jwt') || ''; }

  get userId(): string {
    const raw = localStorage.getItem('estrichweb.user');
    if (!raw) return '';
    try { return JSON.parse(raw).id as string; } catch { return ''; }
  }

  get userName(): string {
    const raw = localStorage.getItem('estrichweb.user');
    if (!raw) return '';
    try { return JSON.parse(raw).name as string; } catch { return ''; }
  }

  get tenantName(): string {
    const raw = localStorage.getItem('estrichweb.user');
    if (!raw) return '';
    try { return JSON.parse(raw).tenant_name as string; } catch { return ''; }
  }

  get userEmail(): string {
    const raw = localStorage.getItem('estrichweb.user');
    if (!raw) return '';
    try { return JSON.parse(raw).email as string || ''; } catch { return ''; }
  }

  get permissions(): string[] {
    const raw = localStorage.getItem('estrichweb.user');
    if (!raw) return [];
    try { return JSON.parse(raw).permissions as string[] || []; } catch { return []; }
  }

  get roleCodes(): string[] {
    const raw = localStorage.getItem('estrichweb.user');
    if (!raw) return [];
    try { return (JSON.parse(raw).role_codes as string[]) || []; } catch { return []; }
  }

  /** Kolonnenmitglied: nur Lesen, außer Tagesdokumentation und Fotos/Videos. */
  isCrewMember(): boolean {
    if (this.permissions.includes('*')) return false;
    return this.roleCodes.includes('kolonnenmitglied');
  }

  // Bereiche, die JEDER Mitarbeiter immer sehen/nutzen darf (Bauakten-Kernfunktionen).
  private static readonly ALWAYS_ALLOWED = new Set(['projects', 'dashboard', 'profile']);
  private static readonly WRITE_ACTIONS = new Set(['write', 'delete', 'export', 'archive', 'share', 'create', 'update']);
  private static readonly CREW_WRITE_AREAS = new Set(['reports', 'photos', 'documents']);

  /** Prüft, ob der aktuelle Benutzer ein Recht hat. Wildcard '*' = Vollzugriff.
   *  'projects.*' (Bauakten) ist für alle freigegeben. */
  can(permission: string): boolean {
    if (!permission) return true;
    const perms = this.permissions;
    if (perms.includes('*')) return true;
    const parts = permission.split('.');
    const area = parts[0];
    const action = parts[1] || 'read';
    // Kolonnenmitglied: Schreiben nur in reports/photos/documents, sonst nur Lesen.
    if (this.isCrewMember()) {
      if (!AuthService.WRITE_ACTIONS.has(action)) return true;
      return AuthService.CREW_WRITE_AREAS.has(area);
    }
    if (AuthService.ALWAYS_ALLOWED.has(area)) return true;
    if (perms.includes(permission)) return true;
    // Bereichs-Wildcard, z. B. 'customers' deckt 'customers.read'/'customers.write'
    if (perms.includes(`${area}.*`) || perms.includes(area)) return true;
    return false;
  }
  // ---------------------------------------------------------------------
  // Komfort-Prüfungen. Die Regeln sind exakt deckungsgleich mit
  // require_permission() in der API. Damit gilt: Wo die API ablehnen würde,
  // wird in der Oberfläche gar kein Button mehr angeboten.
  // ---------------------------------------------------------------------
  /** Schreibrecht in einem Bereich (z. B. 'customers'). */
  canWrite(area: string): boolean { return this.can(`${area}.write`); }
  /** Löschrecht in einem Bereich. */
  canDelete(area: string): boolean { return this.can(`${area}.delete`); }
  /** Leserecht in einem Bereich. */
  canRead(area: string): boolean { return this.can(`${area}.read`); }
  /** Exportrecht (PDF/Listen) in einem Bereich. */
  canExport(area: string): boolean { return this.can(`${area}.export`); }
  /** true, wenn der Benutzer administrative Rechte besitzt. */
  isAdmin(): boolean {
    const perms = this.permissions;
    return perms.includes('*') || perms.includes('admin') || perms.includes('admin.*') || perms.includes('admin.write') || perms.includes('admin.delete');
  }

  /** true, wenn der Benutzer in einem Bereich mindestens ein Recht hat (für Menü-Sichtbarkeit). */
  canAccessArea(area: string): boolean {
    if (!area) return true;
    const perms = this.permissions;
    if (perms.includes('*')) return true;
    if (AuthService.ALWAYS_ALLOWED.has(area)) return true;
    return perms.some((p) => p === area || p.startsWith(`${area}.`));
  }

  private hasUsableToken(): boolean {
    const token = localStorage.getItem('estrichweb.jwt');
    if (!token || token.split('.').length !== 3) return false;
    const payload = decodeJwtPayload(token);
    if (!payload || !payload.exp || !payload.sub || !payload.tenant_id) return false;
    return payload.exp * 1000 > Date.now() + 15000;
  }
}
