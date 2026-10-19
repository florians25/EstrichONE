import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';

/**
 * Eigenes Passwort ändern.
 *
 * Bewusst mit Rückmeldung zur Güte: Ein Balken zeigt, wie widerstandsfähig
 * das gewählte Passwort ist. Erzwungen wird nur die Mindestlänge von acht
 * Zeichen (Vorgabe des Servers) – alles Weitere ist ein Hinweis, keine Hürde.
 */
@Component({
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title class="pw-title"><mat-icon>key</mat-icon>Passwort ändern</h2>

    <mat-dialog-content class="pw-body">
      <mat-form-field appearance="outline" class="pw-field">
        <mat-label>Aktuelles Passwort</mat-label>
        <input matInput [type]="show() ? 'text' : 'password'" [(ngModel)]="current" autocomplete="current-password">
      </mat-form-field>

      <mat-form-field appearance="outline" class="pw-field">
        <mat-label>Neues Passwort</mat-label>
        <input matInput [type]="show() ? 'text' : 'password'" [(ngModel)]="next"
               (ngModelChange)="rate()" autocomplete="new-password">
        <button mat-icon-button matSuffix type="button" tabindex="-1" (click)="show.set(!show())"
                [attr.aria-label]="show() ? 'Passwörter verbergen' : 'Passwörter anzeigen'">
          <mat-icon>{{ show() ? 'visibility_off' : 'visibility' }}</mat-icon>
        </button>
      </mat-form-field>

      @if (next) {
        <div class="pw-meter">
          <div class="pw-meter-bar"><i [style.width.%]="score() * 25" [class]="'s' + score()"></i></div>
          <span>{{ scoreLabel() }}</span>
        </div>
        <ul class="pw-rules">
          <li [class.ok]="next.length >= 12">mindestens 12 Zeichen</li>
          <li [class.ok]="hasMixedCase()">Groß- und Kleinbuchstaben</li>
          <li [class.ok]="hasDigit()">mindestens eine Ziffer</li>
          <li [class.ok]="hasSpecial()">ein Sonderzeichen</li>
        </ul>
      }

      <mat-form-field appearance="outline" class="pw-field">
        <mat-label>Neues Passwort wiederholen</mat-label>
        <input matInput [type]="show() ? 'text' : 'password'" [(ngModel)]="repeat" autocomplete="new-password">
      </mat-form-field>

      @if (error()) { <p class="pw-error"><mat-icon>error_outline</mat-icon>{{ error() }}</p> }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="busy()">Abbrechen</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="busy() || !canSave()">
        <mat-icon>save</mat-icon>Ändern
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .pw-title { display: flex; align-items: center; gap: 9px; font-weight: 600; }
    .pw-title .mat-icon { color: #2457a6 !important; }
    .pw-body { padding-top: 6px !important; width: min(460px, 90vw); }
    .pw-field { width: 100%; }
    .pw-body .mat-mdc-form-field-subscript-wrapper { display: none; }
    .pw-body .mat-mdc-form-field { margin-bottom: 12px; }

    .pw-meter { display: flex; align-items: center; gap: 10px; margin: -4px 0 8px; }
    .pw-meter-bar { flex: 1 1 auto; height: 5px; border-radius: 3px; background: #eef2f6; overflow: hidden; }
    .pw-meter-bar i { display: block; height: 100%; border-radius: 3px; transition: width .2s ease; }
    .pw-meter-bar i.s0, .pw-meter-bar i.s1 { background: #b42318; }
    .pw-meter-bar i.s2 { background: #b45309; }
    .pw-meter-bar i.s3 { background: #2457a6; }
    .pw-meter-bar i.s4 { background: #15803d; }
    .pw-meter span { font-size: .76rem; color: #667085; white-space: nowrap; }

    .pw-rules { list-style: none; margin: 0 0 14px; padding: 0; display: grid; grid-template-columns: 1fr 1fr; gap: 3px 12px; }
    .pw-rules li { font-size: .76rem; color: #98a2b3; padding-left: 16px; position: relative; }
    .pw-rules li::before { content: '○'; position: absolute; left: 0; }
    .pw-rules li.ok { color: #15803d; }
    .pw-rules li.ok::before { content: '✓'; }

    .pw-error { display: flex; align-items: center; gap: 7px; margin: 0; color: #b42318; font-size: .84rem; }
    .pw-error .mat-icon { color: #b42318 !important; font-size: 17px !important; width: 17px !important; height: 17px !important; }

    @media (max-width: 620px) {
      .pw-body { width: auto; }
      .pw-rules { grid-template-columns: 1fr; }
      .pw-body input { font-size: 16px !important; }
    }
  `]
})
export class PasswordChangeDialogComponent {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<PasswordChangeDialogComponent>);

  current = '';
  next = '';
  repeat = '';
  readonly show = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly score = signal(0);

  hasMixedCase(): boolean { return /[a-zäöüß]/.test(this.next) && /[A-ZÄÖÜ]/.test(this.next); }
  hasDigit(): boolean { return /\d/.test(this.next); }
  hasSpecial(): boolean { return /[^A-Za-z0-9äöüÄÖÜß]/.test(this.next); }

  /** Grobe Einschätzung – Länge zählt am meisten, wie in der Praxis auch. */
  rate(): void {
    let s = 0;
    if (this.next.length >= 12) s++;
    if (this.hasMixedCase()) s++;
    if (this.hasDigit()) s++;
    if (this.hasSpecial()) s++;
    if (this.next.length >= 16) s = Math.max(s, 3);
    this.score.set(Math.min(4, s));
  }
  scoreLabel(): string {
    return ['sehr schwach', 'schwach', 'mittel', 'gut', 'stark'][this.score()] || '';
  }
  canSave(): boolean {
    return this.current.length > 0 && this.next.length >= 8 && this.next === this.repeat;
  }

  save(): void {
    this.error.set('');
    if (this.next !== this.repeat) { this.error.set('Die beiden neuen Passwörter stimmen nicht überein.'); return; }
    if (this.next.length < 8) { this.error.set('Das neue Passwort muss mindestens 8 Zeichen haben.'); return; }
    if (this.next === this.current) { this.error.set('Das neue Passwort muss sich vom bisherigen unterscheiden.'); return; }

    this.busy.set(true);
    this.api.changeOwnPassword(this.current, this.next).subscribe({
      next: () => { this.busy.set(false); this.ref.close(true); },
      error: (err) => {
        this.busy.set(false);
        const msg = err?.error?.error || err?.error?.message || '';
        this.error.set(err?.status === 403
          ? 'Das aktuelle Passwort ist nicht korrekt.'
          : (msg || 'Das Passwort konnte nicht geändert werden.'));
      }
    });
  }
}
