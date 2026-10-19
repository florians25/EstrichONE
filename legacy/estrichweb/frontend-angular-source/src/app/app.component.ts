import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgClass } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { AuthService } from './core/auth.service';
import { ApiService } from './core/api.service';
import { WhatsNewDialogComponent } from './features/whats-new/whats-new-dialog.component';
import { CallFormDialogComponent } from './features/calls/call-form-dialog.component';

interface PrimaryNavItem { route: string; label: string; icon: string; exact?: boolean; perm?: string; /** Vorübergehend: Punkt öffnet eine externe Adresse statt einer App-Route. */ external?: string; }
interface NavGroup { key: string; title: string; icon: string; items: PrimaryNavItem[]; }

@Component({
  selector: 'ew-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgClass, MatToolbarModule, MatButtonModule, MatIconModule, MatSidenavModule, MatExpansionModule, MatListModule, MatDividerModule, MatMenuModule, MatTooltipModule, MatDialogModule, MatSnackBarModule],
  template: `
    @if (isChromeless()) {
      <router-outlet />
    } @else {
      <mat-sidenav-container class="app-frame" autosize [ngClass]="{ 'is-mobile': isMobile() }" [style.--ew-sidebar-width]="sidebarWidth()">
        <mat-sidenav [mode]="isMobile() ? 'over' : 'side'" [opened]="isMobile() ? navOpen() : true" class="nav professional-material-sidebar" [class.is-rail]="!isMobile() && !navOpen()" [fixedInViewport]="isMobile()">
          @if (!isMobile() && !navOpen()) {
            <nav class="sidebar-icon-rail" aria-label="Kompaktnavigation">
              <button mat-icon-button (click)="toggleNav()" aria-label="Navigation ausklappen"><mat-icon>menu_open</mat-icon></button>
              @for (item of railItems; track item.route) {
                @if (item.external) {
                  <a mat-icon-button [href]="item.external" target="_blank" rel="noopener" [attr.aria-label]="item.label" [matTooltip]="item.label + ' (Google Kalender)'" matTooltipPosition="right">
                    <mat-icon>{{ item.icon }}</mat-icon>
                  </a>
                } @else {
                  <a mat-icon-button [routerLink]="item.route" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: !!item.exact }" [attr.aria-label]="item.label" [matTooltip]="item.label" matTooltipPosition="right">
                    <mat-icon>{{ item.icon }}</mat-icon>
                  </a>
                }
              }
            </nav>
          } @else {
            <div class="brand-block pro-brand">
              <div class="brand-logo">EW</div>
              <div class="brand-copy">
                <strong>EstrichWeb</strong>
                <small>{{ auth.tenantName || 'Mandant' }}</small>
              </div>
            </div>

            <div class="sidebar-command-row">
              @if (canCreateProject) { <a mat-flat-button color="primary" routerLink="/auftrag-wizard" (click)="closeOnMobile()"><mat-icon>add</mat-icon> Neue Akte</a> }
              <a mat-stroked-button routerLink="/auftragssuche" (click)="closeOnMobile()"><mat-icon>search</mat-icon> Suche</a>
            </div>

            <mat-accordion class="sidebar-accordion" multi>
              @for (group of visibleGroups(); track group.key) {
                <mat-expansion-panel [expanded]="isExpanded(group.key)" (opened)="setExpanded(group.key, true)" (closed)="setExpanded(group.key, false)" class="sidebar-panel">
                  <mat-expansion-panel-header>
                    <mat-panel-title><mat-icon>{{ group.icon }}</mat-icon>{{ group.title }}</mat-panel-title>
                  </mat-expansion-panel-header>
                  <mat-nav-list class="sidebar-list">
                    @for (item of group.items; track item.route) {
                      @if (item.external) {
                        <a mat-list-item [href]="item.external" target="_blank" rel="noopener" (click)="closeOnMobile()">
                          <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
                          <span matListItemTitle class="nav-title-row">
                            <span class="nav-label">{{ item.label }}</span>
                            <mat-icon class="nav-ext-icon">open_in_new</mat-icon>
                          </span>
                        </a>
                      } @else {
                        <a mat-list-item [routerLink]="item.route" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: !!item.exact }" (click)="closeOnMobile()">
                          <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
                          <span matListItemTitle class="nav-title-row">
                            <span class="nav-label">{{ item.label }}</span>
                            @if (item.route === '/materialbestellungen' && materialOpenCount() > 0) { <span class="nav-badge">{{ materialOpenCount() }}</span> }
                          </span>
                        </a>
                      }
                    }
                  </mat-nav-list>
                </mat-expansion-panel>
              }
            </mat-accordion>

          }
        </mat-sidenav>

        <mat-sidenav-content class="content-pane">
          <mat-toolbar class="topbar office-topbar">
            <button mat-icon-button (click)="toggleNav()" aria-label="Navigation"><mat-icon>menu</mat-icon></button>
            <span class="title">{{ pageTitle() }}</span>
            <span class="spacer"></span>
            @if (canCreateProject) { <a mat-flat-button color="primary" routerLink="/auftrag-wizard" class="top-action"><mat-icon>add</mat-icon> Neue Akte</a> }
            <button mat-stroked-button class="top-action top-action-call" (click)="openCallDialog()"><mat-icon>add_call</mat-icon> Telefon</button>
            <button mat-button class="account-trigger" [matMenuTriggerFor]="accountMenu" aria-label="Benutzermenü">
              <span class="account-avatar">{{ initials() }}</span>
              <span class="account-name">{{ auth.userName || 'Benutzer' }}</span>
              <mat-icon class="account-caret">expand_more</mat-icon>
            </button>
            <mat-menu #accountMenu="matMenu" xPosition="before" class="account-menu">
              <a class="account-menu-head account-menu-head-link" routerLink="/profil">
                <span class="account-avatar lg">{{ initials() }}</span>
                <div><strong>{{ auth.userName || 'Benutzer' }}</strong><small>{{ auth.userEmail }}</small><small>{{ auth.tenantName }}</small></div>
              </a>
              <!-- Entfallen, weil doppelt: "Mein Profil" (der Kopfbereich
                   darüber führt bereits dorthin), "Stammdaten" und "Kunden"
                   (beide stehen in der Hauptnavigation). Das Menü enthält
                   jetzt nur noch, was es NUR hier gibt. -->
              <button mat-menu-item (click)="openWhatsNew()"><mat-icon>auto_awesome</mat-icon>Was gibt es Neues</button>
              @if (auth.isAdmin()) {
                <a mat-menu-item routerLink="/admin-verwaltung"><mat-icon>manage_accounts</mat-icon>Benutzer &amp; Rollen</a>
                <a mat-menu-item routerLink="/firmen-einstellungen"><mat-icon>settings</mat-icon>Firmendaten &amp; Optionen</a>
              }
              <button mat-menu-item class="account-logout" (click)="auth.logout()"><mat-icon>logout</mat-icon>Abmelden</button>
            </mat-menu>
          </mat-toolbar>

          <main class="shell"><router-outlet /></main>

          <nav class="bottom-nav" aria-label="Mobile Hauptnavigation">
            <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"><mat-icon>today</mat-icon><span>Heute</span></a>
            <a routerLink="/auftragssuche" routerLinkActive="active"><mat-icon>search</mat-icon><span>Suche</span></a>
            <a routerLink="/kalender" routerLinkActive="active"><mat-icon>event</mat-icon><span>Kalender</span></a>
            <a routerLink="/arbeitsplan" routerLinkActive="active"><mat-icon>event_note</mat-icon><span>Plan</span></a>
            <a routerLink="/stammdaten" routerLinkActive="active"><mat-icon>storage</mat-icon><span>Stamm</span></a>
          </nav>
        </mat-sidenav-content>
      </mat-sidenav-container>
    }
  `
})
export class AppComponent {
  /** Ohne Schreibrecht auf Bauakten wird gar kein Anlegen-Knopf angeboten. */
  get canCreateProject(): boolean { return this.auth.canWrite('projects'); }
  /** Der Kalender läuft vorübergehend über Google Kalender. */
  readonly googleCalendarUrl = 'https://calendar.google.com/calendar/r';
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  openWhatsNew(): void {
    this.dialog.open(WhatsNewDialogComponent, { width: 'min(640px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', autoFocus: false, data: {} });
  }
  openCallDialog(): void {
    const ref = this.dialog.open(CallFormDialogComponent, { width: 'min(640px, 96vw)', maxWidth: '96vw', autoFocus: false, data: {} });
    ref.afterClosed().subscribe((res) => {
      if (res?.created) this.snack.open(res.call?.['project_id'] ? 'Anruf gespeichert + Notiz im Auftrag abgelegt.' : 'Anruf gespeichert.', 'OK', { duration: 3000 });
    });
  }
  // Getrennte Zustände: Am Desktop entscheidet der Benutzer zwischen voller
  // Leiste und schmaler Symbolleiste; am Handy ist es ein Überlagerungsmenü.
  // Frueher war beides EIN Signal – beim Verkleinern und wieder Vergroessern
  // ging die Einstellung verloren und die Leiste sprang oder verschwand.
  private readonly desktopNavExpanded = signal(this.restoreNavPreference());
  private readonly mobileNavOpen = signal(false);
  readonly width = signal(window.innerWidth);
  /** Sichtbarer Zustand – je nach Gerät der passende Wert. */
  readonly navOpen = computed(() => this.isMobile() ? this.mobileNavOpen() : this.desktopNavExpanded());

  /** Einstellung der Seitenleiste über Sitzungen hinweg merken. */
  private restoreNavPreference(): boolean {
    try {
      const stored = localStorage.getItem('ew.nav.expanded');
      if (stored === '0') return false;
      if (stored === '1') return true;
    } catch { /* Speicher nicht verfügbar – Standard verwenden */ }
    return window.innerWidth >= 1180;
  }
  private storeNavPreference(value: boolean): void {
    try { localStorage.setItem('ew.nav.expanded', value ? '1' : '0'); } catch { /* egal */ }
  }
  readonly currentUrl = signal(this.router.url);
  readonly expanded = signal<Record<string, boolean>>({ orders: true, planning: true, office: true, resources: false, site: false, admin: false });
  readonly materialOpenCount = signal(0);


  readonly railItems: PrimaryNavItem[] = [
    { route: '/', label: 'Tagesübersicht', icon: 'today', exact: true },
    { route: '/arbeitsplan', label: 'Arbeitsplan', icon: 'event_note' },
    { route: '/auftragssuche', label: 'Bauaktensuche', icon: 'manage_search' },
    { route: '/kalender', label: 'Kalender', icon: 'calendar_month' },
    { route: '/kolonnen-wochenplaner', label: 'Kolonnenplanung', icon: 'groups' },
    { route: '/admin-verwaltung', label: 'Benutzer und Rollen', icon: 'manage_accounts' }
  ];

  // Erstbegehung und Tagesdokumentation sind bewusst NICHT im Menü:
  // Beide setzen eine Bauakte voraus und werden ausschließlich von dort
  // aufgerufen. Die Routen bleiben erhalten, damit die Verweise aus der
  // Bauakte weiterhin funktionieren.
  readonly navGroups: NavGroup[] = [
    { key: 'orders', title: 'Bauakten', icon: 'assignment', items: [
      { route: '/', label: 'Tagesübersicht', icon: 'today', exact: true, perm: 'projects' },
      { route: '/auftragssuche', label: 'Bauaktensuche', icon: 'manage_search', perm: 'projects' },
      { route: '/auftrag-wizard', label: 'Neue Bauakte', icon: 'add_box', perm: 'projects.write' }
    ]},
    { key: 'planning', title: 'Planung', icon: 'event_note', items: [
      { route: '/kalender', label: 'Kalender', icon: 'calendar_month' },
      { route: '/kalender-google', label: 'Kalender (Google)', icon: 'open_in_new', external: 'https://calendar.google.com/calendar/r' },
      { route: '/arbeitsplan', label: 'Arbeitsplan', icon: 'event_note' },
      { route: '/kolonnen-wochenplaner', label: 'Kolonnenplanung', icon: 'groups', perm: 'planning' },
      { route: '/tageseinteilung', label: 'Tageseinteilung', icon: 'route', perm: 'planning' }
    ]},
    { key: 'office', title: 'Büro', icon: 'business_center', items: [
      { route: '/kunden', label: 'Kunden', icon: 'business', perm: 'customers' },
      { route: '/anrufe', label: 'Anrufe', icon: 'call', perm: 'customers' },
      { route: '/anfragen', label: 'Anfragen', icon: 'confirmation_number', perm: 'customers' },
      { route: '/materialbestellungen', label: 'Materialbestellungen', icon: 'inventory_2', perm: 'materials.read' },
      { route: '/stammdaten', label: 'Stammdaten', icon: 'storage', perm: 'screed' }
    ]},
    { key: 'resources', title: 'Ressourcen', icon: 'inventory_2', items: [
      { route: '/kolonnen', label: 'Kolonnen', icon: 'groups', perm: 'crews' },
      { route: '/kolonnen-mitarbeiter', label: 'Mitarbeiter', icon: 'badge', perm: 'crews' },
      { route: '/fahrzeuge', label: 'Fahrzeuge', icon: 'local_shipping', perm: 'vehicles' },
      { route: '/estrichpumpen', label: 'Estrichpumpen', icon: 'water_pump', perm: 'pumps' },
      { route: '/maschinen', label: 'Maschinen', icon: 'precision_manufacturing', perm: 'machines' }
    ]},
    { key: 'site', title: 'Baustelle', icon: 'apartment', items: [
    ]},
    { key: 'admin', title: 'Administration', icon: 'admin_panel_settings', items: [
      { route: '/admin-verwaltung', label: 'Benutzer und Rollen', icon: 'manage_accounts', perm: 'users' },
      { route: '/firmen-einstellungen', label: 'Firmendaten & Optionen', icon: 'settings', perm: 'settings' },
      { route: '/lexware-integration', label: 'Lexware Office', icon: 'sync_alt', perm: 'integrations' }
    ]}
  ];

  // Menü nach Rechten filtern: Items ohne Berechtigung ausblenden, leere Gruppen weglassen.
  /**
   * Sichtbarkeit eines Menüpunkts.
   *
   * Enthaelt `perm` einen Punkt (z. B. 'projects.write'), wird das Recht
   * EXAKT geprueft. Ohne Punkt genuegt irgendein Recht im Bereich.
   *
   * Wichtig: `canAccessArea('projects')` liefert fuer jeden Mitarbeiter true,
   * weil Bauakten in der API als immer lesbar gelten. Ein Punkt wie
   * \"Neue Bauakte\" darf sich darauf nicht stuetzen – sonst erscheint er
   * auch ohne Schreibrecht.
   */
  private maySee(perm?: string): boolean {
    if (!perm) return true;
    return perm.includes('.') ? this.auth.can(perm) : this.auth.canAccessArea(perm);
  }
  visibleGroups(): NavGroup[] {
    return this.navGroups
      .map((g) => ({ ...g, items: g.items.filter((it) => this.maySee(it.perm)) }))
      .filter((g) => g.items.length > 0);
  }

  constructor() {
    this.router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) { this.currentUrl.set(event.urlAfterRedirects); this.loadMaterialOpenCount(); }
    });
    this.startLexwareBackgroundSync();
    this.loadMaterialOpenCount();
  }

  private lastMaterialCountAt = 0;
  private loadMaterialOpenCount(): void {
    if (!this.auth.token) return;
    // Bei jedem Seitenwechsel neu zu laden erzeugt auf Shared-Hosting unnoetige
    // Last. Hoechstens alle 30 Sekunden – der Wert im Menue ist eine Kennzahl,
    // keine Echtzeitanzeige. (Der Zwischenspeicher im ApiService greift zusaetzlich.)
    const now = Date.now();
    if (now - this.lastMaterialCountAt < 30000) return;
    this.lastMaterialCountAt = now;
    this.api.materialOrdersSummary().subscribe({ next: (res) => this.materialOpenCount.set(Number(res?.['open_count'] ?? res?.['open'] ?? res?.['stats']?.['to_order'] ?? 0)), error: () => void 0 });
  }

  private startLexwareBackgroundSync(): void {
    const run = () => {
      if (this.isChromeless() || !this.auth.token) return;
      const perms = this.auth.permissions || [];
      if (!perms.includes('*') && !perms.includes('integrations.write')) return;
      const last = Number(localStorage.getItem('estrichweb.lexware.lastAutoSync') || '0');
      if (Date.now() - last < 4 * 60 * 1000) return;
      localStorage.setItem('estrichweb.lexware.lastAutoSync', String(Date.now()));
      this.api.lexwareAutoSync(false).subscribe({ error: () => void 0 });
    };
    window.setInterval(run, 60 * 1000);
    window.setTimeout(run, 8000);
  }

  @HostListener('window:resize') onResize(): void {
    const width = window.innerWidth;
    this.width.set(width);
    // Beim Wechsel auf Desktop nur das Überlagerungsmenü schliessen. Die
    // Desktop-Einstellung bleibt unberuehrt – die Leiste verschwindet nicht
    // mehr und springt auch nicht in einen anderen Zustand.
    if (width >= 980 && this.mobileNavOpen()) this.mobileNavOpen.set(false);
  }

  toggleNav(): void {
    if (this.isMobile()) { this.mobileNavOpen.update((v) => !v); return; }
    this.desktopNavExpanded.update((v) => { this.storeNavPreference(!v); return !v; });
  }
  closeOnMobile(): void { if (this.isMobile()) this.mobileNavOpen.set(false); }
  isMobile(): boolean { return this.width() < 980; }
  /** Tatsächliche Breite der Seitenleiste – Überlagerungen (z. B. die
   *  Detailansicht im Arbeitsplan) richten sich danach aus, statt die
   *  Leiste zu verdecken. */
  sidebarWidth(): string {
    if (this.isMobile() || this.isLoginRoute() || this.isPortalRoute()) return '0px';
    return this.desktopNavExpanded() ? '292px' : '64px';
  }
  isLoginRoute(): boolean { return this.currentUrl().startsWith('/login'); }
  isPortalRoute(): boolean { return this.currentUrl().startsWith('/kundenportal/'); }
  isChromeless(): boolean { return this.isLoginRoute() || this.isPortalRoute(); }
  isExpanded(key: string): boolean { return !!this.expanded()[key]; }
  setExpanded(key: string, value: boolean): void { this.expanded.update((state) => ({ ...state, [key]: value })); }

  pageTitle(): string {
    const url = this.currentUrl();
    const item = this.navGroups.flatMap((group) => group.items).find((nav) => nav.route === url || (nav.route !== '/' && url.startsWith(nav.route)));
    return item?.label ?? 'EstrichWeb';
  }

  initials(): string {
    const name = (this.auth.userName || '').trim();
    if (!name) return 'EW';
    const parts = name.split(/\s+/).filter(Boolean);
    const letters = parts.length >= 2 ? parts[0][0] + parts[parts.length - 1][0] : name.slice(0, 2);
    return letters.toUpperCase();
  }
}
