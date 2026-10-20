import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { problemMessage } from '../core/problem';

@Component({
  selector: 'eo-change-password',
  imports: [FormsModule],
  template: `
    <h1>Passwort ändern</h1>
    @if (auth.mustChangePassword()) {
      <div class="message hint" role="status">Sie haben ein Einmalpasswort erhalten. Bitte legen Sie jetzt ein eigenes Passwort fest.</div>
    }
    @if (done()) {
      <div class="message success" role="status">Das Passwort wurde geändert. Andere Sitzungen wurden beendet.</div>
    }
    <form (ngSubmit)="submit()">
      <div class="field">
        <label for="current">Bisheriges Passwort</label>
        <input id="current" name="current" type="password" autocomplete="current-password" required [(ngModel)]="current" />
      </div>
      <div class="field">
        <label for="new">Neues Passwort</label>
        <input id="new" name="new" type="password" autocomplete="new-password" required [(ngModel)]="next" />
        <span class="help">Mindestens 10 Zeichen; keine verbreiteten Passwörter und nicht der Anmeldename.</span>
      </div>
      <div class="field">
        <label for="repeat">Neues Passwort wiederholen</label>
        <input id="repeat" name="repeat" type="password" autocomplete="new-password" required [(ngModel)]="repeat" />
      </div>
      @if (error()) {
        <div class="message error" role="alert">{{ error() }}</div>
      }
      <button class="button primary" type="submit" [disabled]="busy()">Passwort ändern</button>
    </form>
  `,
})
export class ChangePasswordPage {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected current = '';
  protected next = '';
  protected repeat = '';
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);

  async submit(): Promise<void> {
    if (this.next !== this.repeat) {
      this.error.set('Die beiden neuen Passwörter stimmen nicht überein.');
      return;
    }
    const forced = this.auth.mustChangePassword();
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.changePassword(this.current, this.next);
      this.current = this.next = this.repeat = '';
      this.done.set(true);
      if (forced) {
        await this.router.navigateByUrl('/');
      }
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
