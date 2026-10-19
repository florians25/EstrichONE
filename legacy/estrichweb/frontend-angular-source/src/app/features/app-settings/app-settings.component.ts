import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

interface Announcement { enabled: boolean; type: string; title: string; text: string; }
interface CompanyData { name: string; short: string; phone: string; email: string; website: string; street: string; zip: string; city: string; vat: string; ceo: string; footer: string; }
interface EmailConfig { enabled: boolean; host: string; port: number; secure: string; user: string; pass: string; from_email: string; from_name: string; has_password?: boolean; }

@Component({
  standalone: true,
  imports: [FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, MatTabsModule, MatSnackBarModule],
  template: `
    <section class="page-head compact-head">
      <div><p class="eyebrow">Verwaltung · Einstellungen</p><h1><mat-icon>settings</mat-icon>Firmendaten & Entwickleroptionen</h1></div>
      <div class="head-actions"><button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon>Neu laden</button></div>
    </section>

    <mat-tab-group class="settings-tabs" mat-stretch-tabs="false" animationDuration="0ms">
      <!-- Firmenstammdaten -->
      <mat-tab label="Firmenstammdaten">
        <mat-card class="square-card settings-card">
          <div class="card-headline"><h3>Firmenstammdaten</h3><span>Erscheint u. a. in PDF-Ausdrucken</span></div>
          <div class="settings-grid">
            <mat-form-field appearance="outline" class="wide"><mat-label>Firmenname</mat-label><input matInput [(ngModel)]="company.name"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Kürzel (Logo-Text)</mat-label><input matInput [(ngModel)]="company.short" maxlength="4"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Telefon</mat-label><input matInput [(ngModel)]="company.phone"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>E-Mail</mat-label><input matInput [(ngModel)]="company.email"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Website</mat-label><input matInput [(ngModel)]="company.website"></mat-form-field>
            <mat-form-field appearance="outline" class="wide"><mat-label>Straße & Nr.</mat-label><input matInput [(ngModel)]="company.street"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>PLZ</mat-label><input matInput [(ngModel)]="company.zip"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Ort</mat-label><input matInput [(ngModel)]="company.city"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>USt-IdNr.</mat-label><input matInput [(ngModel)]="company.vat"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Geschäftsführer</mat-label><input matInput [(ngModel)]="company.ceo"></mat-form-field>
            <mat-form-field appearance="outline" class="wide"><mat-label>Fußzeile für Dokumente</mat-label><input matInput [(ngModel)]="company.footer"></mat-form-field>
          </div>
          <div class="settings-actions">@if (canWrite) { <button mat-flat-button color="primary" (click)="save('company', company)"><mat-icon>save</mat-icon>Firmendaten speichern</button> }</div>
        </mat-card>
      </mat-tab>

      <!-- Entwickleroptionen -->
      <mat-tab label="Entwickleroptionen">
        <mat-card class="square-card settings-card">
          <div class="card-headline"><h3>Meldung auf dem Dashboard</h3><span>Wird allen Nutzern nach dem Login angezeigt</span></div>
          <div class="settings-toggle-row"><mat-slide-toggle [(ngModel)]="dashboardMessage.enabled">Meldung anzeigen</mat-slide-toggle></div>
          <div class="settings-grid">
            <mat-form-field appearance="outline"><mat-label>Typ</mat-label><mat-select [(ngModel)]="dashboardMessage.type"><mat-option value="info">Info</mat-option><mat-option value="success">Erfolg</mat-option><mat-option value="warning">Warnung</mat-option><mat-option value="critical">Wichtig</mat-option></mat-select></mat-form-field>
            <mat-form-field appearance="outline" class="wide"><mat-label>Titel</mat-label><input matInput [(ngModel)]="dashboardMessage.title"></mat-form-field>
            <mat-form-field appearance="outline" class="full"><mat-label>Text</mat-label><textarea matInput rows="3" [(ngModel)]="dashboardMessage.text"></textarea></mat-form-field>
          </div>
          <div class="settings-actions">@if (canWrite) { <button mat-flat-button color="primary" (click)="save('dashboard_message', dashboardMessage)"><mat-icon>save</mat-icon>Dashboard-Meldung speichern</button> }</div>
        </mat-card>

        <mat-card class="square-card settings-card">
          <div class="card-headline"><h3>Meldung auf der Loginseite</h3><span>Vor dem Login sichtbar (z. B. Wartungshinweis)</span></div>
          <div class="settings-toggle-row"><mat-slide-toggle [(ngModel)]="loginMessage.enabled">Meldung anzeigen</mat-slide-toggle></div>
          <div class="settings-grid">
            <mat-form-field appearance="outline"><mat-label>Typ</mat-label><mat-select [(ngModel)]="loginMessage.type"><mat-option value="info">Info</mat-option><mat-option value="success">Erfolg</mat-option><mat-option value="warning">Warnung</mat-option><mat-option value="critical">Wichtig</mat-option></mat-select></mat-form-field>
            <mat-form-field appearance="outline" class="wide"><mat-label>Titel</mat-label><input matInput [(ngModel)]="loginMessage.title"></mat-form-field>
            <mat-form-field appearance="outline" class="full"><mat-label>Text</mat-label><textarea matInput rows="3" [(ngModel)]="loginMessage.text"></textarea></mat-form-field>
          </div>
          <div class="settings-actions">@if (canWrite) { <button mat-flat-button color="primary" (click)="save('login_message', loginMessage)"><mat-icon>save</mat-icon>Login-Meldung speichern</button> }</div>
        </mat-card>

        <!-- Schnellanmeldung: bewusst standardmäßig AUS. Wer sie einschaltet,
             soll wissen, was er tut – deshalb steht die Abwägung dabei. -->
        <mat-card class="square-card settings-card">
          <div class="card-headline">
            <h3>Schnellanmeldung mit PIN</h3>
            <span>Für Bürorechner und feste Baustellen-Tablets</span>
          </div>

          <div class="settings-toggle-row">
            <mat-slide-toggle [(ngModel)]="quickLogin.enabled">Schnellanmeldung erlauben</mat-slide-toggle>
          </div>

          <div class="settings-explain">
            <p><b>So funktioniert es:</b> Nach einer vollständigen Anmeldung kann sich
            jeder Mitarbeiter im eigenen Profil für <b>dieses Gerät</b> eine
            6-stellige PIN einrichten. Danach genügt auf diesem Gerät die PIN.</p>

            <p><b>Was das bedeutet:</b> Die PIN gilt <b>nur zusammen mit diesem Gerät</b>.
            Wer die PIN kennt, kommt von einem anderen Gerät aus nicht hinein. Nach
            fünf Fehlversuchen wird die Schnellanmeldung gesperrt und die volle
            Anmeldung ist wieder nötig. Ohne Nutzung läuft die Freigabe nach
            90 Tagen ab.</p>

            <p class="settings-warn">
              <mat-icon>warning</mat-icon>
              <span>Sechs Ziffern sind schwächer als ein Passwort. Auf Geräten, die
              mehrere Personen unbeaufsichtigt nutzen, sollte die Schnellanmeldung
              ausgeschaltet bleiben.</span>
            </p>
          </div>

          <div class="settings-actions">
            @if (canWrite) {
              <button mat-flat-button color="primary" (click)="save('quick_login', quickLogin)"><mat-icon>save</mat-icon>Schnellanmeldung speichern</button>
            }
          </div>
        </mat-card>
      </mat-tab>

      <!-- E-Mail / Benachrichtigungen -->
      <mat-tab label="E-Mail">
        <mat-card class="square-card settings-card">
          <div class="card-headline"><h3>E-Mail-Versand (SMTP)</h3><span>Für Zuweisungs- und Benachrichtigungs-E-Mails an Mitarbeiter</span></div>
          <div class="settings-toggle-row"><mat-slide-toggle [(ngModel)]="email.enabled">E-Mail-Versand aktivieren</mat-slide-toggle></div>
          <div class="settings-grid">
            <mat-form-field appearance="outline" class="wide"><mat-label>SMTP-Server (Host)</mat-label><input matInput [(ngModel)]="email.host" placeholder="z. B. smtp.ionos.de"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Port</mat-label><input matInput type="number" [(ngModel)]="email.port" placeholder="587"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Verschlüsselung</mat-label><mat-select [(ngModel)]="email.secure"><mat-option value="tls">STARTTLS (587)</mat-option><mat-option value="ssl">SSL/TLS (465)</mat-option><mat-option value="none">Keine</mat-option></mat-select></mat-form-field>
            <mat-form-field appearance="outline" class="wide"><mat-label>Benutzername</mat-label><input matInput [(ngModel)]="email.user" autocomplete="off"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>{{ email.has_password ? 'Passwort (gespeichert – leer lassen zum Behalten)' : 'Passwort' }}</mat-label><input matInput type="password" [(ngModel)]="email.pass" autocomplete="new-password"></mat-form-field>
            <mat-form-field appearance="outline" class="wide"><mat-label>Absender-Adresse (From)</mat-label><input matInput [(ngModel)]="email.from_email" placeholder="noreply@firma.de"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Absender-Name</mat-label><input matInput [(ngModel)]="email.from_name" placeholder="EstrichWeb"></mat-form-field>
          </div>
          <div class="settings-actions">
            @if (canWrite) { <button mat-flat-button color="primary" (click)="saveEmail()"><mat-icon>save</mat-icon>E-Mail-Einstellungen speichern</button> }
            <button mat-stroked-button (click)="sendTest()" [disabled]="testing"><mat-icon>send</mat-icon>Test-E-Mail senden</button>
          </div>
          <p class="settings-hint">Tipp: Bei IONOS/Strato meist <b>STARTTLS</b> über Port <b>587</b>. Speichern Sie zuerst, dann „Test-E-Mail senden" – die Test-Mail geht an Ihre eigene Adresse.</p>
        </mat-card>
      </mat-tab>

      <!-- Was gibt es Neues wird statisch aus api/content/whats-new.txt ausgeliefert.
           Die Bearbeitung in der Oberfläche ist absichtlich entfernt. -->
    </mat-tab-group>
  `
})
export class AppSettingsComponent {
  private readonly auth = inject(AuthService);
  /** Ohne Schreibrecht sind die Einstellungen nur einsehbar. */
  readonly canWrite = this.auth.canWrite('settings');
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);

  company: CompanyData = { name: '', short: '', phone: '', email: '', website: '', street: '', zip: '', city: '', vat: '', ceo: '', footer: '' };
  dashboardMessage: Announcement = { enabled: false, type: 'info', title: '', text: '' };
  loginMessage: Announcement = { enabled: false, type: 'info', title: '', text: '' };
  /** Schnellanmeldung – standardmäßig aus. */
  quickLogin: { enabled: boolean } = { enabled: false };
  email: EmailConfig = { enabled: false, host: '', port: 587, secure: 'tls', user: '', pass: '', from_email: '', from_name: 'EstrichWeb', has_password: false };
  testing = false;
  constructor() { this.load(); this.loadEmail(); }

  load(): void {
    this.api.appSettings().subscribe({
      next: (res) => {
        this.company = { ...this.company, ...(res['company'] || {}) };
        this.dashboardMessage = { ...this.dashboardMessage, ...(res['dashboard_message'] || {}) };
        this.loginMessage = { ...this.loginMessage, ...(res['login_message'] || {}) };
        this.quickLogin = { ...this.quickLogin, ...(res['quick_login'] || {}) };
      },
      error: () => this.snack.open('Einstellungen konnten nicht geladen werden', 'OK', { duration: 3000 })
    });
  }

  loadEmail(): void {
    this.api.getEmailSettings().subscribe({
      next: (res) => { const s = (res['settings'] || {}) as Partial<EmailConfig>; this.email = { ...this.email, ...s, pass: '' }; },
      error: () => {}
    });
  }

  saveEmail(): void {
    this.api.saveEmailSettings(this.email).subscribe({
      next: () => { this.snack.open('E-Mail-Einstellungen gespeichert', 'OK', { duration: 2000 }); this.loadEmail(); },
      error: () => this.snack.open('Speichern fehlgeschlagen', 'OK', { duration: 3000 })
    });
  }

  sendTest(): void {
    this.testing = true;
    this.api.testEmail().subscribe({
      next: (res) => { this.testing = false; this.snack.open('Test-E-Mail gesendet an ' + (res['to'] || ''), 'OK', { duration: 4000 }); },
      error: (e) => { this.testing = false; this.snack.open('Test fehlgeschlagen: ' + (e?.error?.error || 'unbekannt'), 'OK', { duration: 5000 }); }
    });
  }

  save(key: string, value: unknown): void {
    this.api.saveAppSetting(key, value).subscribe({
      next: () => this.snack.open('Gespeichert', 'OK', { duration: 2000 }),
      error: () => this.snack.open('Speichern fehlgeschlagen', 'OK', { duration: 3000 })
    });
  }

}
