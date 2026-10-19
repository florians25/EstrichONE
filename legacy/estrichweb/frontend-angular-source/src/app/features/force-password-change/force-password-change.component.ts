import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

@Component({
  standalone: true,
  imports: [FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSnackBarModule],
  template: `
    <div class="pw-change-screen">
      <mat-card class="pw-change-card">
        <div class="pw-change-head">
          <span class="pw-change-icon"><mat-icon>lock_reset</mat-icon></span>
          <h1>Passwort ändern</h1>
          <p>Aus Sicherheitsgründen musst du bei der ersten Anmeldung ein eigenes Passwort vergeben.</p>
        </div>
        <mat-form-field appearance="outline"><mat-label>Aktuelles Passwort</mat-label><input matInput [type]="show() ? 'text' : 'password'" [(ngModel)]="current" autocomplete="current-password"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Neues Passwort</mat-label><input matInput [type]="show() ? 'text' : 'password'" [(ngModel)]="next" autocomplete="new-password"><button mat-icon-button matSuffix type="button" (click)="show.set(!show())" tabindex="-1"><mat-icon>{{ show() ? 'visibility_off' : 'visibility' }}</mat-icon></button></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Neues Passwort wiederholen</mat-label><input matInput [type]="show() ? 'text' : 'password'" [(ngModel)]="confirm" autocomplete="new-password"></mat-form-field>
        <p class="pw-change-hint">Mindestens 8 Zeichen.</p>
        <button mat-flat-button color="primary" class="pw-change-submit" [disabled]="saving()" (click)="submit()"><mat-icon>check</mat-icon>Passwort speichern</button>
      </mat-card>
    </div>
  `
})
export class ForcePasswordChangeComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  current = ''; next = ''; confirm = '';
  readonly show = signal(false);
  readonly saving = signal(false);

  submit(): void {
    if (this.next.length < 8) { this.snack.open('Das neue Passwort muss mindestens 8 Zeichen haben.', 'OK', { duration: 3000 }); return; }
    if (this.next !== this.confirm) { this.snack.open('Die Passwörter stimmen nicht überein.', 'OK', { duration: 3000 }); return; }
    if (!this.current) { this.snack.open('Bitte das aktuelle Passwort eingeben.', 'OK', { duration: 3000 }); return; }
    this.saving.set(true);
    this.api.changeOwnPassword(this.current, this.next).subscribe({
      next: () => { this.saving.set(false); this.auth.clearMustChangePassword(); this.snack.open('Passwort geändert.', 'OK', { duration: 2500 }); void this.router.navigateByUrl('/'); },
      error: (err) => { this.saving.set(false); this.snack.open(err?.error?.error || 'Passwort konnte nicht geändert werden.', 'OK', { duration: 4000 }); }
    });
  }
}
