import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { problemMessage } from '../core/problem';

/** Ersteinrichtung mit dem Einmalcode aus dem Server-Protokoll (docs/reengineering/17, „Erster Administrator“). */
@Component({
  selector: 'eo-setup',
  imports: [FormsModule],
  template: `
    <div class="page">
      <form class="card" (ngSubmit)="submit()">
        <h1>EstrichONE einrichten</h1>
        <p>Den Einrichtungscode zeigt der Server beim ersten Start in seinem Protokoll an
          (<code>docker compose logs api</code>).</p>
        <div class="field">
          <label for="code">Einrichtungscode</label>
          <input id="code" name="code" required autocomplete="off" [(ngModel)]="setupCode" />
        </div>
        <div class="field">
          <label for="company">Firmenname</label>
          <input id="company" name="company" required [(ngModel)]="companyName" />
        </div>
        <h2>Erster Administrator</h2>
        <div class="field">
          <label for="name">Name</label>
          <input id="name" name="name" required autocomplete="name" [(ngModel)]="displayName" />
        </div>
        <div class="field">
          <label for="user">Anmeldename</label>
          <input id="user" name="user" required autocomplete="username" [(ngModel)]="userName" />
        </div>
        <div class="field">
          <label for="email">E-Mail (optional)</label>
          <input id="email" name="email" type="email" autocomplete="email" [(ngModel)]="email" />
        </div>
        <div class="field">
          <label for="pw">Passwort</label>
          <input id="pw" name="pw" type="password" required autocomplete="new-password" [(ngModel)]="password" />
          <span class="help">Mindestens 10 Zeichen.</span>
        </div>
        @if (error()) {
          <div class="message error" role="alert">{{ error() }}</div>
        }
        <button class="button primary" type="submit" [disabled]="busy()">Einrichten und anmelden</button>
      </form>
    </div>
  `,
  styles: `
    .page { min-height: 100vh; display: grid; place-items: center; padding: 16px; background: var(--brand-navy); }
    .card { width: 100%; max-width: 480px; }
    .button { width: 100%; min-height: 44px; }
  `,
})
export class SetupPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected setupCode = '';
  protected companyName = '';
  protected displayName = '';
  protected userName = '';
  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post('/api/v1/setup', {
        setupCode: this.setupCode.trim(),
        companyName: this.companyName,
        administrator: { displayName: this.displayName, userName: this.userName, email: this.email || null, password: this.password },
      }));
      await this.auth.signIn(this.userName, this.password);
      await this.router.navigateByUrl('/');
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
