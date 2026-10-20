import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IsActiveMatchOptions, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ServerInfo } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { IconComponent } from '../shared/icon.component';

interface NavItem {
  label: string;
  link: string;
  icon: string;
  permission?: string;
  queryParams?: Record<string, string>;
  /** Nur bei genau dieser Adresse hervorheben (Anfragen zusätzlich am Statusfilter). */
  exact?: boolean;
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

/**
 * Programmrahmen im Stil klassischer Handwerkersoftware (Streit V.1 als Vorbild): Kopfleiste in Navy mit Suche,
 * Navigationsbaum nach Fachgruppen, Statusleiste mit Server und Benutzer.
 */
@Component({
  selector: 'eo-shell',
  imports: [FormsModule, RouterOutlet, RouterLink, RouterLinkActive, IconComponent],
  template: `
    <a class="skip" href="#inhalt">Zum Inhalt springen</a>
    <div class="frame" [class.compact]="compact()">
      <header class="appbar">
        <button type="button" class="icon-button" (click)="compact.set(!compact())" [attr.aria-label]="compact() ? 'Navigation ausklappen' : 'Navigation einklappen'"
          [attr.aria-expanded]="!compact()"><eo-icon name="menu" [size]="20" /></button>
        <a class="brand" routerLink="/start">
          <span class="mark" aria-hidden="true"><i></i><i></i><i></i></span>
          <span>EstrichONE</span>
        </a>
        @if (server(); as s) { <span class="company">{{ s.serverName }}</span> }
        <form class="search" role="search" (ngSubmit)="search()">
          <eo-icon name="search" [size]="16" />
          <label class="visually-hidden" for="global-search">Bauakten suchen</label>
          <input id="global-search" name="q" type="search" placeholder="Bauakte suchen: Nummer, Bauvorhaben, Ort …" [(ngModel)]="query" />
        </form>
        <div class="user">
          <button type="button" class="user-button" (click)="menuOpen.set(!menuOpen())" [attr.aria-expanded]="menuOpen()" aria-haspopup="menu">
            <span class="avatar" aria-hidden="true">{{ initials() }}</span>
            <span class="name">{{ auth.user()?.displayName }}</span>
            <eo-icon name="chevron-down" [size]="14" />
          </button>
          @if (menuOpen()) {
            <div class="menu" role="menu" (click)="menuOpen.set(false)">
              <a role="menuitem" routerLink="/konto/passwort"><eo-icon name="key" [size]="16" /> Passwort ändern</a>
              <button role="menuitem" type="button" (click)="signOut()"><eo-icon name="log-out" [size]="16" /> Abmelden</button>
            </div>
          }
        </div>
      </header>

      <nav aria-label="Hauptnavigation">
        @for (group of visibleGroups(); track group.label) {
          <div class="group">
            @if (group.label) {
              <button type="button" class="group-head" (click)="toggle(group.label)" [attr.aria-expanded]="!closed().has(group.label)">
                <eo-icon [name]="closed().has(group.label) ? 'chevron-right' : 'chevron-down'" [size]="14" />
                <span>{{ group.label }}</span>
              </button>
            }
            @if (!closed().has(group.label)) {
              <ul>
                @for (item of group.items; track item.label) {
                  <li>
                    <a [routerLink]="item.link" [queryParams]="item.queryParams ?? null" routerLinkActive="active"
                      [routerLinkActiveOptions]="matchOptions(item)" ariaCurrentWhenActive="page" [attr.title]="compact() ? item.label : null">
                      <eo-icon [name]="item.icon" [size]="18" />
                      <span class="label">{{ item.label }}</span>
                    </a>
                  </li>
                }
              </ul>
            }
          </div>
        }
      </nav>

      <main id="inhalt" tabindex="-1">
        <router-outlet />
      </main>

      <footer class="statusbar">
        <span><span class="dot" aria-hidden="true"></span> Verbunden mit {{ server()?.serverName ?? 'Server' }}</span>
        <span class="sep" aria-hidden="true"></span>
        <span>Server {{ server()?.serverVersion ?? '–' }}</span>
        <span class="grow"></span>
        <span>{{ auth.user()?.displayName }}</span>
        <span class="sep" aria-hidden="true"></span>
        <span>{{ today }}</span>
      </footer>
    </div>
  `,
  styles: `
    .skip { position: absolute; left: -9999px; }
    .skip:focus { left: 16px; top: 8px; z-index: 10; background: var(--surface); padding: 8px; }
    .frame { display: grid; grid-template-columns: var(--nav-width) minmax(0, 1fr); grid-template-rows: 44px minmax(0, 1fr) 24px; height: 100vh; }
    .frame.compact { grid-template-columns: 52px minmax(0, 1fr); }
    .appbar { grid-column: 1 / -1; display: flex; align-items: center; gap: 12px; padding: 0 12px 0 6px; background: var(--brand-navy); color: #fff; }
    .icon-button { display: inline-grid; place-items: center; width: 36px; height: 36px; border: 0; border-radius: 3px; background: none; color: #fff; cursor: pointer; }
    .icon-button:hover { background: rgb(255 255 255 / 0.12); }
    .brand { display: flex; align-items: center; gap: 9px; color: #fff; font-weight: 700; font-size: 16px; text-decoration: none; letter-spacing: 0.01em; }
    .mark { display: inline-grid; gap: 3px; }
    .mark i { display: block; height: 4px; background: #fff; border-radius: 1px; width: 18px; }
    .mark i:nth-child(2) { width: 13px; }
    .mark i:nth-child(3) { background: var(--brand-blue); }
    .company { padding-left: 12px; border-left: 1px solid rgb(255 255 255 / 0.3); color: #dbe5f3; font-size: 13px; white-space: nowrap; }
    .search { flex: 1; max-width: 520px; margin: 0 auto; display: flex; align-items: center; gap: 8px; height: 30px; padding: 0 10px;
      background: rgb(255 255 255 / 0.12); border: 1px solid rgb(255 255 255 / 0.2); border-radius: 3px; color: #dbe5f3; }
    .search:focus-within { background: #fff; color: var(--text-secondary); }
    .search input { flex: 1; min-width: 0; border: 0; background: none; color: inherit; font: inherit; outline: none; }
    .search input::placeholder { color: #c3d0e2; }
    .search:focus-within input { color: #1f2937; }
    .user { position: relative; }
    .user-button { display: flex; align-items: center; gap: 8px; height: 34px; padding: 0 8px; border: 0; border-radius: 3px; background: none; color: #fff; font: inherit; cursor: pointer; }
    .user-button:hover { background: rgb(255 255 255 / 0.12); }
    .avatar { display: inline-grid; place-items: center; width: 26px; height: 26px; border-radius: 50%; background: var(--brand-blue); font-size: 11px; font-weight: 700; }
    .menu { position: absolute; right: 0; top: 40px; z-index: 20; min-width: 200px; padding: 4px; background: var(--surface-raised); color: var(--text-primary);
      border: 1px solid var(--line-strong); border-radius: 4px; box-shadow: 0 8px 24px rgb(0 0 0 / 0.18); }
    .menu a, .menu button { display: flex; align-items: center; gap: 8px; width: 100%; padding: 7px 10px; border: 0; border-radius: 3px; background: none;
      color: inherit; font: inherit; text-decoration: none; cursor: pointer; text-align: left; }
    .menu a:hover, .menu button:hover { background: var(--grid-hover); }
    nav { background: var(--surface); border-right: 1px solid var(--line); overflow-y: auto; padding: 8px 6px; }
    .group + .group { margin-top: 6px; }
    .group-head { display: flex; align-items: center; gap: 4px; width: 100%; padding: 6px 6px 4px; border: 0; background: none; font: inherit;
      font-size: 11px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; color: var(--text-tertiary); cursor: pointer; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 1px; }
    nav a { display: flex; align-items: center; gap: 10px; padding: 6px 10px 6px 12px; border-left: 3px solid transparent; border-radius: 0 3px 3px 0;
      color: var(--text-primary); text-decoration: none; white-space: nowrap; }
    nav a eo-icon { color: var(--brand-navy); }
    nav a:hover { background: var(--grid-hover); }
    nav a.active { background: var(--grid-selected); border-left-color: var(--brand-navy); font-weight: 600; }
    @media (prefers-color-scheme: dark) { nav a eo-icon { color: var(--link); } }
    .compact nav { padding: 8px 4px; }
    .compact .group-head, .compact .label, .compact .company { display: none; }
    .compact nav a { justify-content: center; padding: 8px 0; }
    main { overflow: auto; padding: 16px 20px 24px; min-width: 0; }
    .statusbar { grid-column: 1 / -1; display: flex; align-items: center; gap: 10px; padding: 0 12px; font-size: 12px; color: var(--text-secondary);
      background: var(--grid-head); border-top: 1px solid var(--line); white-space: nowrap; overflow: hidden; }
    .statusbar .grow { flex: 1; }
    .statusbar .sep { width: 1px; height: 14px; background: var(--line-strong); }
    .dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: var(--success); margin-right: 4px; }
    @media (max-width: 760px) {
      .frame, .frame.compact { grid-template-columns: 1fr; grid-template-rows: 44px auto minmax(0, 1fr) 24px; }
      nav { border-right: 0; border-bottom: 1px solid var(--line); }
      nav ul { grid-auto-flow: column; overflow-x: auto; }
      .group-head, .company, .name { display: none; }
    }
  `,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  protected readonly server = signal<ServerInfo | null>(null);
  protected readonly compact = signal(false);
  protected readonly menuOpen = signal(false);
  protected readonly closed = signal<ReadonlySet<string>>(new Set());
  protected readonly today = new Date().toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' });
  protected query = '';

  private readonly groups: NavGroup[] = [
    { label: '', items: [{ label: 'Übersicht', link: '/start', icon: 'overview' }] },
    {
      label: 'Auftragsbearbeitung',
      items: [
        { label: 'Anfragen', link: '/bauakten', icon: 'inbox', permission: 'bauakten.read', queryParams: { status: 'inquiry' }, exact: true },
        { label: 'Bauakten', link: '/bauakten', icon: 'bauakte', permission: 'bauakten.read' },
        { label: 'Planung', link: '/planung', icon: 'calendar', permission: 'planning.read', exact: true },
      ],
    },
    {
      label: 'Stammdaten',
      items: [
        { label: 'Kunden', link: '/kunden', icon: 'customers', permission: 'contacts.read' },
        { label: 'Kolonnen und Geräte', link: '/planung/ressourcen', icon: 'truck', permission: 'planning.read' },
      ],
    },
    {
      label: 'IT Center',
      items: [
        { label: 'Benutzer', link: '/verwaltung/benutzer', icon: 'users', permission: 'users.manage' },
        { label: 'Geräte', link: '/it-center/geraete', icon: 'device', permission: 'devices.read' },
        { label: 'Helpdesk', link: '/it-center/helpdesk', icon: 'helpdesk', permission: 'helpdesk.agent' },
      ],
    },
    { label: 'System', items: [{ label: 'Sicherung', link: '/system/sicherung', icon: 'backup', permission: 'backup.manage' }] },
  ];

  protected readonly visibleGroups = computed(() => {
    const permissions = this.auth.permissions();
    return this.groups
      .map((g) => ({ ...g, items: g.items.filter((item) => !item.permission || permissions.has(item.permission)) }))
      .filter((g) => g.items.length > 0);
  });

  protected readonly initials = computed(() =>
    (this.auth.user()?.displayName ?? '?').split(' ').filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join(''),
  );

  constructor() {
    void firstValueFrom(this.http.get<ServerInfo>('/api/v1/server-info')).then((info) => this.server.set(info), () => undefined);
  }

  protected matchOptions(item: NavItem): IsActiveMatchOptions {
    return {
      paths: item.exact ? 'exact' : 'subset',
      queryParams: item.queryParams ? 'subset' : 'ignored',
      fragment: 'ignored',
      matrixParams: 'ignored',
    };
  }

  protected toggle(group: string): void {
    const next = new Set(this.closed());
    if (next.has(group)) {
      next.delete(group);
    } else {
      next.add(group);
    }
    this.closed.set(next);
  }

  protected search(): void {
    const q = this.query.trim();
    void this.router.navigate(['/bauakten'], { queryParams: q ? { search: q } : {} });
  }

  async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/anmelden']);
  }
}
