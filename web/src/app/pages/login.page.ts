import { HttpClient } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ServerInfo } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { problemMessage } from '../core/problem';

@Component({
  selector: 'eo-login',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="page">
      <form class="card" (ngSubmit)="submit()" aria-labelledby="login-title">
        <h1 id="login-title">Anmelden</h1>
        @if (serverName()) {
          <p class="server">{{ serverName() }}</p>
        }
        @if (grund() === 'abgelaufen') {
          <div class="message hint" role="status">Die Sitzung ist abgelaufen. Bitte erneut anmelden.</div>
        }
        @if (grund() === 'gesperrt') {
          <div class="message error" role="alert">Das Benutzerkonto ist gesperrt. Bitte an die Administration wenden.</div>
        }
        @if (setupRequired()) {
          <div class="message hint" role="status">
            EstrichONE ist noch nicht eingerichtet. <a routerLink="/einrichtung">Jetzt einrichten</a>
          </div>
        }
        <div class="field">
          <label for="login">Anmeldename oder E-Mail</label>
          <input id="login" name="login" autocomplete="username" required [(ngModel)]="login" />
        </div>
        <div class="field">
          <label for="password">Passwort</label>
          <input id="password" name="password" type="password" autocomplete="current-password" required [(ngModel)]="password" />
        </div>
        @if (error()) {
          <div class="message error" role="alert">{{ error() }}</div>
        }
        <button class="button primary" type="submit" [disabled]="busy() || !login || !password">
          {{ busy() ? 'Anmeldung läuft …' : 'Anmelden' }}
        </button>
        <p class="help">Passwort vergessen? Ihre Administration kann es zurücksetzen.</p>
      </form>
    </div>
  `,
  styles: `
    .page { min-height: 100vh; display: grid; place-items: center; padding: 16px; background: var(--brand-navy); }
    .card { width: 100%; max-width: 400px; }
    .server { color: var(--text-secondary); margin-top: -8px; }
    .button { width: 100%; min-height: 44px; }
    .help { font-size: 13px; color: var(--text-tertiary); margin: 16px 0 0; }
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  readonly grund = input<string>();
  readonly ziel = input<string>();

  protected login = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverName = signal<string | null>(null);
  protected readonly setupRequired = signal(false);

  constructor() {
    void firstValueFrom(this.http.get<ServerInfo>('/api/v1/server-info'))
      .then((info) => {
        this.serverName.set(info.serverName);
        this.setupRequired.set(info.setupRequired);
      })
      .catch(() => undefined);
  }

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.signIn(this.login, this.password);
      const target = this.auth.mustChangePassword() ? '/konto/passwort' : (this.ziel() ?? '/');
      await this.router.navigateByUrl(target);
    } catch (error) {
      this.password = '';
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
