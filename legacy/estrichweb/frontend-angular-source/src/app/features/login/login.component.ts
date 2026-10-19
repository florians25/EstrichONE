import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../../core/auth.service';
import { ApiService } from '../../core/api.service';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <div class="lg-page">
      <!-- Zentrierte Karte über flächigem Hintergrund in den Unternehmens-
           farben. Die geteilte Fassung wirkte zerrissen; eine ruhige Fläche
           mit einer klaren Karte in der Mitte ist das, was man aus
           Fachanwendungen kennt. -->
      <main class="lg-panel">
      <div class="lg-card">
        @if (loading()) { <mat-progress-bar mode="indeterminate" class="lg-bar"></mat-progress-bar> }

        <header class="lg-id">
          <span class="lg-mark">EW</span>
          <b class="lg-company">{{ companyName() || 'EstrichWeb' }}</b>
          <!-- Das System heißt EstrichWeb. In Runde 62 hatte ich hier
               "Bauaktenplattform" eingesetzt – das ist eine Beschreibung,
               kein Name, und hat den Produktnamen verdrängt. -->
          <small class="lg-product">EstrichWeb</small>
        </header>

        <h1 class="lg-h1">Anmelden</h1>

        @if (loginNotice(); as n) {
          <div class="lg-note" [class]="'lg-note-' + (n.type || 'info')">
            @if (n.title) { <b>{{ n.title }}</b> }
            @if (n.text) { <span>{{ n.text }}</span> }
          </div>
        }

        <!-- Schnellanmeldung: nur wenn freigeschaltet UND dieses Gerät
             hinterlegt ist. Sonst ist der Bereich gar nicht vorhanden. -->
        @if (quickAvailable() && !quickHidden()) {
          <section class="lg-quick">
            <div class="lg-quick-user">
              <span class="lg-quick-avatar">{{ quickInitials() }}</span>
              <div>
                <b>{{ quickUser() }}</b>
                <small>{{ quickDevice() || 'Dieses Gerät' }}</small>
              </div>
            </div>

            <label class="lg-quick-label" for="lg-pin">PIN eingeben</label>
            <div class="lg-pin-row">
              <input id="lg-pin" class="lg-pin" type="password" inputmode="numeric" pattern="[0-9]*"
                     maxlength="6" autocomplete="off" [(ngModel)]="pin" [ngModelOptions]="{ standalone: true }"
                     (ngModelChange)="onPinChange()" [disabled]="loading()"
                     placeholder="••••••" aria-label="PIN">
              <button type="button" class="lg-pin-go" (click)="submitPin()"
                      [disabled]="pin.length !== 6 || loading()" aria-label="Mit PIN anmelden">
                <mat-icon>arrow_forward</mat-icon>
              </button>
            </div>

            @if (quickError()) { <p class="lg-quick-error"><mat-icon>error_outline</mat-icon>{{ quickError() }}</p> }

            <button type="button" class="lg-quick-switch" (click)="quickHidden.set(true)">
              Mit E-Mail und Passwort anmelden
            </button>
          </section>
        }

        <form [formGroup]="form" (ngSubmit)="submit()" class="lg-form" autocomplete="on" novalidate
              [class.is-secondary]="quickAvailable() && !quickHidden()">
          <mat-form-field appearance="outline" class="lg-field">
            <mat-label>E-Mail-Adresse</mat-label>
            <input matInput type="email" formControlName="email" autocomplete="username" inputmode="email"
                   autocapitalize="off" autocorrect="off" spellcheck="false" enterkeyhint="next">
            <mat-icon matPrefix>mail_outline</mat-icon>
            @if (emailInvalid()) { <mat-error>Bitte eine gültige E-Mail-Adresse eingeben.</mat-error> }
          </mat-form-field>

          <mat-form-field appearance="outline" class="lg-field">
            <mat-label>Passwort</mat-label>
            <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password"
                   autocomplete="current-password" enterkeyhint="go"
                   (keyup)="checkCapsLock($event)" (keydown)="checkCapsLock($event)">
            <mat-icon matPrefix>lock_outline</mat-icon>
            <button mat-icon-button matSuffix type="button" tabindex="-1" (click)="showPassword.set(!showPassword())"
                    [attr.aria-label]="showPassword() ? 'Passwort verbergen' : 'Passwort anzeigen'">
              <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
            </button>
            @if (capsLock()) { <mat-hint class="lg-caps"><mat-icon>keyboard_capslock</mat-icon>Die Feststelltaste ist aktiv.</mat-hint> }
          </mat-form-field>

          <!-- Kennung merken: spart auf dem Baustellenhandy das Tippen der
               langen Adresse. Gespeichert wird NUR die E-Mail, nie das
               Passwort – dafür ist der Passwortspeicher des Browsers da. -->
          <label class="lg-remember">
            <input type="checkbox" [checked]="rememberEmail()" (change)="toggleRemember()">
            <span>E-Mail-Adresse merken</span>
          </label>

          @if (error()) {
            <p class="lg-error" [class.is-locked]="locked()" role="alert">
              <mat-icon>{{ locked() ? 'lock_clock' : 'error_outline' }}</mat-icon>
              <span>{{ error() }}</span>
            </p>
          }

          <button mat-flat-button color="primary" class="lg-submit" type="submit" [disabled]="form.invalid || loading() || locked()">
            <mat-icon>login</mat-icon>{{ loading() ? 'Anmeldung läuft …' : 'Anmelden' }}
          </button>
        </form>

        <p class="lg-help">Zugangsdaten vergessen oder Konto gesperrt? Bitte an die Administration in Ihrem Betrieb wenden.</p>
        <footer class="lg-foot">
          <span class="lg-secure"><mat-icon>lock</mat-icon>Verschlüsselte Verbindung</span>
          <span class="lg-ver">EstrichWeb {{ appVersion() }}</span>
        </footer>
      </div>
      </main>
    </div>
  `,
  styles: [`
    /* Anmeldung mit Angular-Material-Bausteinen. Die globale Regel
       .mat-mdc-form-field { width: 100% } passt hier sogar, weil die Felder
       ohnehin die volle Breite der Karte einnehmen sollen. */
    :host { display: block; }
    .lg-page {
      min-height: 100vh; min-height: 100dvh;
      background: #f4f6f9;
      display: flex; justify-content: center; align-items: flex-start;
      padding: clamp(24px, 8vh, 88px) 20px 40px;
    }
    .lg-card {
      width: 100%; max-width: 404px;
      padding: 34px 32px 24px !important;
      border-radius: 12px !important;
      position: relative; overflow: hidden;
      background: #fff !important;
      border-color: #e3e8ef !important;
    }
    /* Material lässt unter jedem Feld Platz für Hinweistexte. Ohne Hinweis
       ist der Abstand hier unnötig groß. */
    .lg-form .mat-mdc-form-field-subscript-wrapper { min-height: 0; }
    .lg-form .mat-mdc-form-field { margin-bottom: 4px; }
    .lg-bar { position: absolute; top: 0; left: 0; right: 0; }

    .lg-id { display: flex; align-items: center; gap: 11px; margin-bottom: 28px; }
    .lg-mark {
      flex: 0 0 auto; width: 36px; height: 36px; border-radius: 7px;
      display: inline-grid; place-items: center;
      background: #14396f; color: #fff; font-size: .82rem; font-weight: 700; letter-spacing: .04em;
    }
    .lg-id-tx { display: flex; flex-direction: column; min-width: 0; line-height: 1.25; }
    .lg-id-tx b { font-size: .95rem; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lg-id-tx small { font-size: .76rem; color: #7a8699; }

    .lg-h1 { margin: 0; font-size: 1.44rem; font-weight: 600; letter-spacing: -.012em; color: #101828; }
    .lg-sub { margin: 6px 0 22px; font-size: .87rem; color: #667085; line-height: 1.5; }

    .lg-note {
      margin: 0 0 18px; padding: 11px 13px; border-radius: 6px;
      border: 1px solid #d6dce5; background: #f6f8fa;
      font-size: .84rem; line-height: 1.5; color: #344054;
    }
    .lg-note b { display: block; font-weight: 600; margin-bottom: 2px; }
    .lg-note-warning { border-color: #f0d3a8; background: #fdf8f0; color: #7a4a12; }
    .lg-note-critical { border-color: #eec4c0; background: #fdf5f4; color: #8a2a22; }
    .lg-note-success { border-color: #bfe0c8; background: #f4faf6; color: #226138; }

    .lg-form { display: block; }
    .lg-field { width: 100%; }
    .lg-field .mat-icon[matPrefix] { margin-right: 8px; color: #98a2b3 !important; }
    .lg-caps { display: inline-flex !important; align-items: center; gap: 5px; color: #8a5a12 !important; }
    .lg-caps .mat-icon { font-size: 15px !important; width: 15px !important; height: 15px !important; color: #8a5a12 !important; }

    .lg-error {
      display: flex; align-items: flex-start; gap: 8px;
      margin: 2px 0 16px; padding: 10px 13px;
      border: 1px solid #eec4c0; border-left: 3px solid #c9433a; border-radius: 6px;
      background: #fdf5f4; color: #8a2a22; font-size: .84rem; line-height: 1.5;
    }
    .lg-error .mat-icon { flex: 0 0 auto; color: #c9433a !important; margin-top: 1px; }

    .lg-submit {
      width: 100%; height: 46px !important;
      font-size: .94rem !important; font-weight: 600 !important;
      margin-top: 4px;
    }

    .lg-help {
      margin: 22px 0 0; padding-top: 18px;
      border-top: 1px solid #e6eaf0;
      font-size: .8rem; line-height: 1.55; color: #667085;
    }
    .lg-foot {
      display: flex; align-items: center; justify-content: space-between; gap: 10px;
      margin: 18px 0 0; padding-top: 14px; border-top: 1px solid #eef1f5;
      font-size: .73rem; color: #98a2b3;
    }
    .lg-secure { display: inline-flex; align-items: center; gap: 5px; }
    .lg-secure .mat-icon { font-size: 14px !important; width: 14px !important; height: 14px !important; color: #98a2b3 !important; }
    .lg-ver { font-variant-numeric: tabular-nums; }

    @media (max-width: 520px) {
      .lg-page { padding: 0; background: #fff; align-items: stretch; }
      .lg-card {
        max-width: none; min-height: 100dvh;
        border: 0 !important; border-radius: 0 !important; box-shadow: none !important;
        padding: 26px 20px 32px !important;
      }
      .lg-id { margin-bottom: 22px; }
      .lg-h1 { font-size: 1.34rem; }
      .lg-submit { height: 50px !important; }
    }
    @media (max-height: 620px) {
      .lg-page { padding-top: 20px; }
      .lg-id { margin-bottom: 18px; }
      .lg-sub { margin-bottom: 16px; }
    }
  `]
})
export class LoginComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ApiService);
  readonly loading = signal(false);
  readonly error = signal('');

  // ---- Schnellanmeldung mit PIN ----------------------------------------
  /** Steht für dieses Gerät eine PIN-Anmeldung bereit? */
  readonly quickAvailable = signal(false);
  readonly quickHidden = signal(false);
  readonly quickUser = signal('');
  readonly quickDevice = signal('');
  readonly quickError = signal('');
  pin = '';

  /** Kennung dieses Geräts – bleibt im Browser und wird nie angezeigt. */
  private deviceId(): string {
    try {
      let id = localStorage.getItem('ew.device.id');
      if (!id) {
        id = (crypto.randomUUID?.() || String(Date.now()) + Math.random().toString(36).slice(2)).replace(/-/g, '');
        localStorage.setItem('ew.device.id', id);
      }
      return id;
    } catch { return ''; }
  }
  quickInitials(): string {
    const n = (this.quickUser() || '?').trim().split(/\s+/);
    return ((n[0]?.[0] || '') + (n[1]?.[0] || '')).toUpperCase() || '?';
  }
  private checkQuickLogin(): void {
    const device = this.deviceId();
    if (!device) return;
    this.api.quickLoginStatus(device).subscribe({
      next: (r) => {
        if (!r?.['available']) return;
        this.quickAvailable.set(true);
        this.quickUser.set(String(r['user_name'] || ''));
        this.quickDevice.set(String(r['device_label'] || ''));
        setTimeout(() => (document.getElementById('lg-pin') as HTMLInputElement | null)?.focus(), 120);
      },
      error: () => {}
    });
  }
  onPinChange(): void {
    // Nur Ziffern zulassen und bei sechs Stellen selbst absenden – so tippt
    // man die PIN und es geht weiter, ohne einen Knopf zu suchen.
    this.pin = (this.pin || '').replace(/\D/g, '').slice(0, 6);
    this.quickError.set('');
    if (this.pin.length === 6) this.submitPin();
  }
  submitPin(): void {
    if (this.pin.length !== 6 || this.loading()) return;
    this.loading.set(true);
    this.quickError.set('');
    this.auth.quickLogin(this.deviceId(), this.pin).subscribe({
      next: (response) => { this.finishLogin(response); },
      error: (err) => {
        this.loading.set(false);
        this.pin = '';
        const msg = err?.error?.error || err?.error?.message || '';
        this.quickError.set(msg || 'Die PIN ist nicht korrekt.');
        // Bei Sperre auf die volle Anmeldung umschalten.
        if (err?.status === 423) { this.quickAvailable.set(false); this.quickHidden.set(true); }
      }
    });
  }
  /** Ist die Anmeldung gerade gesperrt? Dann bleibt der Knopf inaktiv. */
  readonly locked = signal(false);
  /** E-Mail merken – nur die Kennung, niemals das Passwort. */
  readonly rememberEmail = signal(false);
  private static readonly REMEMBER_KEY = 'ew.login.email';

  toggleRemember(): void {
    const next = !this.rememberEmail();
    this.rememberEmail.set(next);
    try {
      if (next) localStorage.setItem(LoginComponent.REMEMBER_KEY, String(this.form.value.email || ''));
      else localStorage.removeItem(LoginComponent.REMEMBER_KEY);
    } catch { /* Speicher nicht verfügbar */ }
  }
  private restoreEmail(): void {
    try {
      const saved = localStorage.getItem(LoginComponent.REMEMBER_KEY);
      if (saved) { this.form.patchValue({ email: saved }); this.rememberEmail.set(true); }
    } catch { /* egal */ }
  }
  readonly showPassword = signal(false);
  readonly loginNotice = signal<{ type?: string; title?: string; text?: string } | null>(null);
  /** Firmenname aus den öffentlichen App-Infos, falls hinterlegt. */
  readonly companyName = signal('');
  readonly appVersion = signal('2.0.0');
  /** Hinweis auf aktive Feststelltaste – häufigste Ursache für Fehlanmeldungen. */
  readonly capsLock = signal(false);
  readonly form = this.fb.nonNullable.group({ email: ['', [Validators.required, Validators.email]], password: ['', Validators.required] });

  noticeIcon(type?: string): string { return ({ success: 'check_circle', warning: 'warning', critical: 'error' } as Record<string, string>)[String(type)] || 'info'; }

  /** Fehlermeldung erst zeigen, wenn das Feld auch berührt wurde. */
  emailInvalid(): boolean {
    const c = this.form.controls.email;
    return c.invalid && (c.touched || c.dirty);
  }
  checkCapsLock(event: Event): void {
    const ke = event as KeyboardEvent;
    if (typeof ke.getModifierState !== 'function') return;
    this.capsLock.set(ke.getModifierState('CapsLock'));
  }

  ngOnInit(): void {
    this.restoreEmail();
    this.checkQuickLogin();
    this.api.publicAppInfo().subscribe({
      next: (info) => {
        const n = info?.['login_message'];
        if (n && n.enabled && (n.title || n.text)) this.loginNotice.set(n);
        const company = info?.['company'] as Record<string, unknown> | undefined;
        if (company?.['name']) this.companyName.set(String(company['name']));
        if (info?.['version']) this.appVersion.set(String(info['version']));
      },
      error: () => {}
    });
    if (this.auth.isAuthenticated()) this.redirectAfterLogin();
  }

  submit(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    this.error.set('');
    this.form.disable({ emitEvent: false });
    this.auth.login(this.form.controls.email.value, this.form.controls.password.value).subscribe({
      next: (response) => {
        this.form.enable({ emitEvent: false });
        // Gemerkte Kennung aktualisieren – erst nach erfolgreicher Anmeldung,
        // damit keine Tippfehler gespeichert werden.
        if (this.rememberEmail()) {
          try { localStorage.setItem(LoginComponent.REMEMBER_KEY, String(this.form.value.email || '')); } catch { /* egal */ }
        }
        this.finishLogin(response);
      },
      error: (err) => {
        this.form.enable({ emitEvent: false });
        this.loading.set(false);
        this.error.set(this.messageFor(err));
        // Bei einer Sperre bleibt der Anmeldeknopf inaktiv, bis die Wartezeit
        // abgelaufen ist – sonst probiert man weiter und wird nur neu gesperrt.
        if (err?.status === 429) {
          this.locked.set(true);
          const minutes = Number(err?.error?.retry_after_minutes ?? 15);
          setTimeout(() => { this.locked.set(false); this.error.set(''); }, Math.max(1, minutes) * 60000);
        }
      }
    });
  }

  /** Für den Anwender verständliche Meldung statt roher Serverantwort. */
  private messageFor(err: { status?: number; error?: { message?: string; error?: string } }): string {
    const fromServer = err?.error?.message || err?.error?.error;
    switch (err?.status) {
      case 0: return 'Keine Verbindung zum Server. Bitte Internetverbindung prüfen und erneut versuchen.';
      // Die Serverantwort nennt gegebenenfalls die verbleibenden Versuche.
      case 401: return fromServer || 'E-Mail oder Passwort ist nicht korrekt.';
      case 403: return fromServer || 'Dieses Benutzerkonto ist gesperrt. Bitte an die Administration wenden.';
      case 429: return fromServer || 'Zu viele Anmeldeversuche. Bitte einige Minuten warten und erneut versuchen.';
      case 500: case 502: case 503: case 504:
        return 'Der Server ist derzeit nicht erreichbar. Bitte später erneut versuchen.';
      default: return fromServer || 'Anmeldung fehlgeschlagen. Bitte Zugangsdaten prüfen.';
    }
  }

  private finishLogin(_response: unknown): void {
    this.loading.set(false);
    this.redirectAfterLogin();
  }

  private redirectAfterLogin(): void {
    // Erzwungener Passwortwechsel bei Erstanmeldung: direkt zur Wechsel-Seite.
    if (this.auth.mustChangePassword) { void this.router.navigateByUrl('/passwort-aendern'); return; }
    const target = this.route.snapshot.queryParamMap.get('returnUrl') || '/';
    const safeTarget = target.startsWith('/login') ? '/' : target;
    void this.router.navigateByUrl(safeTarget);
  }
}
