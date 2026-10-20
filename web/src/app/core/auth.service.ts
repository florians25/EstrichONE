import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UserSummary, WebSessionResponse } from './api.models';

/**
 * Anmeldung im Browser: Die Tokens liegen ausschließlich in HttpOnly-Cookies (docs/reengineering/17).
 * Der Dienst kennt nur den angemeldeten Benutzer und seine Rechte.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly user = signal<UserSummary | null>(null);
  readonly permissions = signal<ReadonlySet<string>>(new Set());
  readonly mustChangePassword = signal(false);
  readonly isSignedIn = computed(() => this.user() !== null);

  /** Beim Start: besteht noch eine Sitzung (Cookie)? Der Interceptor erneuert sie bei Bedarf. */
  async restore(): Promise<void> {
    try {
      const user = await firstValueFrom(this.http.get<UserSummary>('/api/v1/auth/me'));
      this.user.set(user);
      await this.loadPermissions();
    } catch {
      this.clear();
    }
  }

  async signIn(login: string, password: string): Promise<void> {
    const session = await firstValueFrom(this.http.post<WebSessionResponse>('/api/v1/auth/web/login', { login, password }));
    this.user.set(session.user);
    this.mustChangePassword.set(session.mustChangePassword);
    await this.loadPermissions();
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/v1/auth/change-password', { currentPassword, newPassword }));
    this.mustChangePassword.set(false);
  }

  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/web/logout', null));
    } finally {
      this.clear();
    }
  }

  /** Erneuert die Sitzung (Refresh-Cookie). Wird vom Interceptor bei abgelaufenem Zugriffstoken genutzt. */
  refresh() {
    return this.http.post<WebSessionResponse>('/api/v1/auth/web/refresh', null);
  }

  has(permission: string): boolean {
    return this.permissions().has(permission);
  }

  clear(): void {
    this.user.set(null);
    this.permissions.set(new Set());
    this.mustChangePassword.set(false);
  }

  private async loadPermissions(): Promise<void> {
    const permissions = await firstValueFrom(this.http.get<string[]>('/api/v1/access/me/permissions'));
    this.permissions.set(new Set(permissions));
  }
}
