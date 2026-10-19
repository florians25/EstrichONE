import { Component, Inject, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService, GenericRecord } from '../../core/api.service';

interface EditData { user: GenericRecord; roles: GenericRecord[]; assignedRoleIds: string[]; }

@Component({
  standalone: true,
  selector: 'ew-user-edit-dialog',
  imports: [FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatCheckboxModule, MatDialogModule, MatSnackBarModule],
  template: `
    <h2 mat-dialog-title class="ud-title"><span class="ud-title-icon"><mat-icon>manage_accounts</mat-icon></span>Benutzer bearbeiten</h2>
    <mat-dialog-content class="ud-dialog">
      <div class="ud-body">
        <section class="ud-section">
          <h4 class="ud-section-title"><mat-icon>badge</mat-icon>Stammdaten</h4>
          <div class="ud-field-grid">
            <mat-form-field appearance="outline" class="full"><mat-label>Name</mat-label><input matInput [(ngModel)]="name"></mat-form-field>
            <mat-form-field appearance="outline" class="full"><mat-label>E-Mail (nicht änderbar)</mat-label><input matInput [value]="data.user['email']" disabled></mat-form-field>
            <mat-form-field appearance="outline" class="full"><mat-label>Neues Passwort (optional)</mat-label><input matInput [type]="showPw() ? 'text' : 'password'" [(ngModel)]="password" autocomplete="new-password" placeholder="leer = unverändert"><button mat-icon-button matSuffix type="button" (click)="showPw.set(!showPw())" tabindex="-1"><mat-icon>{{ showPw() ? 'visibility_off' : 'visibility' }}</mat-icon></button></mat-form-field>
            <button mat-stroked-button class="ud-genpw" type="button" (click)="generatePassword()"><mat-icon>autorenew</mat-icon>Sicheres Passwort erzeugen</button>
          </div>
          <div class="ud-toggles">
            <mat-slide-toggle [(ngModel)]="active">Benutzer ist aktiv</mat-slide-toggle>
          </div>
        </section>

        <section class="ud-section ud-section-roles">
          <h4 class="ud-section-title"><mat-icon>shield_person</mat-icon>Rollen<span class="ud-role-count">{{ roleCount() }}</span></h4>
          @if (!data.roles?.length) { <p class="muted">Keine Rollen vorhanden.</p> }
          <div class="ud-role-list">
            @for (role of data.roles; track role['id']) {
              <label class="ud-role" [class.checked]="isChecked(role['id'])">
                <mat-checkbox [checked]="isChecked(role['id'])" (change)="toggle(role['id'], $any($event).checked)"></mat-checkbox>
                <span class="ud-role-name">{{ role['name'] }}</span>
                @if (role['is_system']) { <span class="ud-role-badge">System</span> }
              </label>
            }
          </div>
        </section>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end" class="ud-actions">
      <button mat-stroked-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" [disabled]="saving()" (click)="save()"><mat-icon>save</mat-icon>Änderungen speichern</button>
    </mat-dialog-actions>
  `
})
export class UserEditDialogComponent {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  name = ''; password = ''; active = true;
  readonly showPw = signal(false);
  readonly saving = signal(false);
  private roleIds = new Set<string>();

  constructor(private ref: MatDialogRef<UserEditDialogComponent>, @Inject(MAT_DIALOG_DATA) public data: EditData) {
    this.name = String(data.user['name'] || '');
    this.active = !!data.user['is_active'];
    (data.assignedRoleIds || []).forEach((id) => this.roleIds.add(String(id)));
  }

  isChecked(id: string): boolean { return this.roleIds.has(String(id)); }
  roleCount(): number { return this.roleIds.size; }
  toggle(id: string, checked: boolean): void { if (checked) this.roleIds.add(String(id)); else this.roleIds.delete(String(id)); }
  generatePassword(): void {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#';
    let pw = ''; for (let i = 0; i < 12; i++) pw += chars[Math.floor(Math.random() * chars.length)];
    this.password = pw; this.showPw.set(true);
  }
  save(): void {
    const name = this.name.trim();
    if (!name) { this.snack.open('Name darf nicht leer sein.', 'OK', { duration: 3000 }); return; }
    if (this.password && this.password.length < 8) { this.snack.open('Passwort muss mindestens 8 Zeichen haben.', 'OK', { duration: 3000 }); return; }
    this.saving.set(true);
    const payload: GenericRecord = { name, is_active: this.active };
    if (this.password) payload['password'] = this.password;
    const id = String(this.data.user['id']);
    forkJoin({
      user: this.api.adminUpdateUser(id, payload),
      roles: this.api.updateUserRoles(id, Array.from(this.roleIds))
    }).subscribe({
      next: () => { this.saving.set(false); this.ref.close({ updated: true, roleIds: Array.from(this.roleIds) }); },
      error: (err) => { this.saving.set(false); this.snack.open(err?.error?.error || 'Benutzer konnte nicht gespeichert werden.', 'OK', { duration: 4000 }); }
    });
  }
}
