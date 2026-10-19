import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/**
 * PIN für die Schnellanmeldung festlegen.
 *
 * Zwei Eingaben zur Sicherheit gegen Vertipper. Zu einfache Folgen werden
 * abgewiesen – eine PIN wie 123456 wäre keine Sicherung, und der Server
 * lehnt sie ohnehin ab. Der Hinweis kommt hier schon vor dem Absenden.
 */
@Component({
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title class="qp-title"><mat-icon>pin</mat-icon>PIN festlegen</h2>

    <mat-dialog-content class="qp-body">
      <p class="qp-lead">
        Sechs Ziffern. Sie gelten <b>nur auf diesem Gerät</b> – auf einem anderen
        Rechner nützt die PIN niemandem.
      </p>

      <label class="qp-label" for="qp1">Neue PIN</label>
      <input id="qp1" class="qp-input" type="password" inputmode="numeric" maxlength="6"
             [(ngModel)]="pin" (ngModelChange)="onChange()" autocomplete="off" placeholder="••••••">

      <label class="qp-label" for="qp2">PIN wiederholen</label>
      <input id="qp2" class="qp-input" type="password" inputmode="numeric" maxlength="6"
             [(ngModel)]="repeat" (ngModelChange)="onChange()" autocomplete="off" placeholder="••••••">

      @if (error()) { <p class="qp-error"><mat-icon>error_outline</mat-icon>{{ error() }}</p> }

      <p class="qp-note">
        <mat-icon>info</mat-icon>
        <span>Nach fünf Fehlversuchen wird die Schnellanmeldung gesperrt und die
        vollständige Anmeldung ist wieder nötig.</span>
      </p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="!valid()">
        <mat-icon>save</mat-icon>PIN speichern
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .qp-title { display: flex; align-items: center; gap: 9px; font-weight: 600; }
    .qp-title .mat-icon { color: #2457a6 !important; }
    .qp-body { padding-top: 4px !important; width: min(400px, 90vw); }
    .qp-lead { margin: 0 0 18px; font-size: .85rem; line-height: 1.55; color: #667085; }
    .qp-lead b { color: #101828; }
    .qp-label {
      display: block; margin-bottom: 6px;
      font-size: .74rem; font-weight: 600; letter-spacing: .05em;
      text-transform: uppercase; color: #667085;
    }
    .qp-input {
      width: 100%; height: 52px; margin-bottom: 16px; padding: 0 16px;
      border: 1px solid #cfd9e6; border-radius: 9px; background: #fff;
      font: inherit; font-size: 1.4rem; font-weight: 600;
      letter-spacing: .45em; text-align: center; box-sizing: border-box;
    }
    .qp-input:focus { outline: none; border-color: #14396f; box-shadow: 0 0 0 3px rgba(20,57,111,.1); }
    .qp-input::placeholder { letter-spacing: .3em; color: #cbd5e1; font-weight: 400; }

    .qp-error { display: flex; align-items: center; gap: 7px; margin: -6px 0 14px; font-size: .84rem; color: #b42318; }
    .qp-error .mat-icon { font-size: 17px !important; width: 17px !important; height: 17px !important; color: #b42318 !important; }

    .qp-note {
      display: flex; align-items: flex-start; gap: 8px;
      margin: 0; padding: 10px 12px;
      border-radius: 8px; background: #f8fafc;
      font-size: .78rem; line-height: 1.5; color: #667085;
    }
    .qp-note .mat-icon { flex: none; font-size: 16px !important; width: 16px !important; height: 16px !important; color: #98a2b3 !important; }

    @media (max-width: 620px) {
      .qp-body { width: auto; }
      .qp-input { height: 56px; font-size: 16px !important; font-size: 1.4rem !important; }
    }
  `]
})
export class QuickPinDialogComponent {
  private readonly ref = inject(MatDialogRef<QuickPinDialogComponent>);

  pin = '';
  repeat = '';
  readonly error = signal('');

  /** Folgen wie 123456 oder 000000 sind keine Sicherung. */
  private tooSimple(pin: string): boolean {
    if (/^(\d)\1{5}$/.test(pin)) return true;
    return ['123456', '654321', '111111', '000000', '123123', '112233'].includes(pin);
  }
  onChange(): void {
    this.pin = (this.pin || '').replace(/\D/g, '').slice(0, 6);
    this.repeat = (this.repeat || '').replace(/\D/g, '').slice(0, 6);
    this.error.set('');
  }
  valid(): boolean { return this.pin.length === 6 && this.repeat.length === 6; }

  save(): void {
    if (this.pin !== this.repeat) { this.error.set('Die beiden Eingaben stimmen nicht überein.'); return; }
    if (this.tooSimple(this.pin)) { this.error.set('Diese PIN ist zu einfach. Bitte eine andere wählen.'); return; }
    this.ref.close(this.pin);
  }
}
