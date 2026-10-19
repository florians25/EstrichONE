import { Component, Inject, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { jsPDF } from 'jspdf';
import { ApiService, GenericRecord } from '../../core/api.service';

@Component({
  standalone: true,
  selector: 'ew-user-create-dialog',
  imports: [FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatCheckboxModule, MatDialogModule, MatSnackBarModule],
  template: `
    <h2 mat-dialog-title class="ud-title"><span class="ud-title-icon"><mat-icon>person_add</mat-icon></span>Neuen Benutzer anlegen</h2>
    <mat-dialog-content class="ud-dialog">
      @if (!created()) {
        <div class="ud-body">
          <section class="ud-section">
            <h4 class="ud-section-title"><mat-icon>badge</mat-icon>Stammdaten</h4>
            <div class="ud-field-grid">
              <mat-form-field appearance="outline" class="full"><mat-label>Name</mat-label><input matInput [(ngModel)]="name" autocomplete="off"></mat-form-field>
              <mat-form-field appearance="outline" class="full"><mat-label>E-Mail</mat-label><input matInput type="email" [(ngModel)]="email" autocomplete="off"></mat-form-field>
              <mat-form-field appearance="outline" class="full"><mat-label>Passwort</mat-label><input matInput [type]="showPw() ? 'text' : 'password'" [(ngModel)]="password" autocomplete="new-password"><button mat-icon-button matSuffix type="button" (click)="showPw.set(!showPw())" tabindex="-1"><mat-icon>{{ showPw() ? 'visibility_off' : 'visibility' }}</mat-icon></button></mat-form-field>
              <button mat-stroked-button class="ud-genpw" type="button" (click)="generatePassword()"><mat-icon>autorenew</mat-icon>Sicheres Passwort erzeugen</button>
            </div>
            <div class="ud-toggles">
              <mat-slide-toggle [(ngModel)]="active">Benutzer ist aktiv</mat-slide-toggle>
              <mat-slide-toggle [(ngModel)]="mustChange">Passwort bei erster Anmeldung ändern</mat-slide-toggle>
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
      } @else {
        <div class="ud-success">
          <div class="ud-success-icon"><mat-icon>check_circle</mat-icon></div>
          <h3>Benutzer angelegt</h3>
          <p>Drucken Sie die Zugangskarte aus und übergeben Sie sie dem neuen Mitarbeiter. Das Passwort wird hier nur einmal angezeigt.</p>
          <div class="ud-cred-card">
            <div><span>Name</span><b>{{ name }}</b></div>
            <div><span>Anmeldeseite</span><b>{{ loginUrl() }}</b></div>
            <div><span>Benutzername</span><b>{{ email }}</b></div>
            <div><span>Passwort</span><b class="ud-cred-pw">{{ password }}</b></div>
          </div>
          <button mat-flat-button color="primary" class="ud-print-btn" (click)="printAccessCard()"><mat-icon>print</mat-icon>Zugangskarte drucken (PDF)</button>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end" class="ud-actions">
      @if (!created()) {
        <button mat-stroked-button mat-dialog-close>Abbrechen</button>
        <button mat-flat-button color="primary" [disabled]="saving()" (click)="save()"><mat-icon>save</mat-icon>Benutzer anlegen</button>
      } @else {
        <button mat-stroked-button (click)="printAccessCard()"><mat-icon>print</mat-icon>Zugangskarte</button>
        <button mat-flat-button color="primary" (click)="finish()"><mat-icon>done</mat-icon>Fertig</button>
      }
    </mat-dialog-actions>
  `
})
export class UserCreateDialogComponent {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  name = ''; email = ''; password = ''; active = true; mustChange = true;
  readonly showPw = signal(false);
  readonly saving = signal(false);
  readonly created = signal(false);
  readonly companyName = signal('EstrichWeb');
  private roleIds = new Set<string>();

  constructor(private ref: MatDialogRef<UserCreateDialogComponent>, @Inject(MAT_DIALOG_DATA) public data: { roles: GenericRecord[] }) {
    this.api.appSettings().subscribe({ next: (res) => { const n = res?.['company']?.['name']; if (n) this.companyName.set(String(n)); }, error: () => {} });
  }

  loginUrl(): string { try { return window.location.origin + '/login'; } catch { return '/login'; } }
  isChecked(id: string): boolean { return this.roleIds.has(String(id)); }
  roleCount(): number { return this.roleIds.size; }
  toggle(id: string, checked: boolean): void { if (checked) this.roleIds.add(String(id)); else this.roleIds.delete(String(id)); }
  generatePassword(): void {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#';
    let pw = ''; for (let i = 0; i < 12; i++) pw += chars[Math.floor(Math.random() * chars.length)];
    this.password = pw; this.showPw.set(true);
  }
  finish(): void { this.ref.close({ created: true }); }
  save(): void {
    const name = this.name.trim(); const email = this.email.trim().toLowerCase();
    if (!name || !email) { this.snack.open('Name und E-Mail sind erforderlich.', 'OK', { duration: 3000 }); return; }
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) { this.snack.open('Bitte eine gültige E-Mail eingeben.', 'OK', { duration: 3000 }); return; }
    if (this.password.length < 8) { this.snack.open('Passwort muss mindestens 8 Zeichen haben.', 'OK', { duration: 3000 }); return; }
    this.name = name; this.email = email;
    this.saving.set(true);
    this.api.adminCreateUser({ name, email, password: this.password, is_active: this.active, role_ids: Array.from(this.roleIds), must_change_password: this.mustChange }).subscribe({
      next: () => { this.saving.set(false); this.created.set(true); this.snack.open('Benutzer angelegt – Zugangskarte kann gedruckt werden.', 'OK', { duration: 3000 }); },
      error: (err) => { this.saving.set(false); this.snack.open(err?.error?.error || 'Benutzer konnte nicht angelegt werden.', 'OK', { duration: 4000 }); }
    });
  }

  printAccessCard(): void {
    const doc = new jsPDF({ unit: 'mm', format: 'a4' });
    const PW = 210, left = 25, right = 20;
    const company = this.companyName();
    const today = new Date().toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });

    // Kopf
    doc.setFont('times', 'bold'); doc.setFontSize(18);
    doc.text(company.toUpperCase(), left, 24);
    doc.setFont('times', 'normal'); doc.setFontSize(9);
    doc.text('Zugangskarte · ' + today, PW - right, 20, { align: 'right' });
    doc.setLineWidth(0.6); doc.setDrawColor(0); doc.line(left, 28, PW - right, 28);

    // Titel
    doc.setFont('times', 'bold'); doc.setFontSize(15);
    doc.text('Ihre Zugangsdaten für EstrichWeb', left, 46);
    doc.setFont('times', 'normal'); doc.setFontSize(11);
    const intro = doc.splitTextToSize('Willkommen! Mit den folgenden Daten melden Sie sich in der EstrichWeb-App an. Bitte bewahren Sie diese Karte sicher auf und geben Sie die Daten nicht weiter.', PW - left - right) as string[];
    doc.text(intro, left, 56);

    // Datenbox
    let y = 56 + intro.length * 6 + 6;
    const boxX = left, boxW = PW - left - right, boxH = 62;
    doc.setDrawColor(150); doc.setFillColor(248, 250, 252); doc.setLineWidth(0.4);
    doc.rect(boxX, y, boxW, boxH, 'FD');
    const row = (label: string, value: string, ry: number, mono = false) => {
      doc.setFont('times', 'bold'); doc.setFontSize(10); doc.setTextColor(90, 90, 90);
      doc.text(label, boxX + 8, ry);
      doc.setFont(mono ? 'courier' : 'times', mono ? 'bold' : 'normal'); doc.setFontSize(mono ? 14 : 12); doc.setTextColor(0, 0, 0);
      doc.text(String(value || '—'), boxX + 8, ry + 6.5);
    };
    row('NAME', this.name, y + 11);
    row('ANMELDESEITE', this.loginUrl(), y + 27);
    row('BENUTZERNAME (E-MAIL)', this.email, y + 43);
    // Passwort separat, rechte Spalte
    doc.setFont('times', 'bold'); doc.setFontSize(10); doc.setTextColor(90, 90, 90);
    doc.text('PASSWORT', boxX + boxW / 2 + 4, y + 43);
    doc.setFont('courier', 'bold'); doc.setFontSize(14); doc.setTextColor(0, 0, 0);
    doc.text(String(this.password || '—'), boxX + boxW / 2 + 4, y + 49.5);

    y += boxH + 12;
    // Hinweise
    doc.setFont('times', 'normal'); doc.setFontSize(10.5); doc.setTextColor(0, 0, 0);
    const hints: string[] = [];
    if (this.mustChange) hints.push('• Beim ersten Login werden Sie aufgefordert, ein eigenes Passwort zu vergeben.');
    hints.push('• Öffnen Sie die Anmeldeseite im Browser oder über die installierte App.');
    hints.push('• Behandeln Sie diese Zugangsdaten vertraulich. Bei Verlust wenden Sie sich an Ihren Administrator.');
    let hy = y;
    hints.forEach((h) => { const lines = doc.splitTextToSize(h, PW - left - right) as string[]; doc.text(lines, left, hy); hy += lines.length * 5.5 + 1; });

    // Fuß
    doc.setFont('times', 'normal'); doc.setFontSize(8); doc.setTextColor(120, 120, 120);
    doc.text(company + ' · Zugangskarte vom ' + today, PW / 2, 285, { align: 'center' });

    const safe = (this.name || 'benutzer').replace(/[^\w.-]+/g, '_');
    const filename = `Zugangskarte_${safe}.pdf`;
    try {
      const isMobile = typeof navigator !== 'undefined' && /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent || '');
      if (isMobile) {
        const blob = doc.output('blob'); const url = URL.createObjectURL(blob);
        const a = document.createElement('a'); a.href = url; a.download = filename; a.rel = 'noopener';
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => { try { window.open(url, '_blank'); } catch { /* ignore */ } setTimeout(() => URL.revokeObjectURL(url), 60000); }, 250);
      } else { doc.save(filename); }
    } catch { try { doc.save(filename); } catch { /* ignore */ } }
  }
}
