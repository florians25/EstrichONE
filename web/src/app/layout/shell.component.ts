import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';

interface NavItem {
  label: string;
  link: string;
  permission?: string;
}

/** Rahmen: linke Navigation (232 px), Kopfzeile mit angemeldeter Person (05, Abschnitt 4). */
@Component({
  selector: 'eo-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <a class="skip" href="#inhalt">Zum Inhalt springen</a>
    <div class="layout">
      <nav aria-label="Hauptnavigation">
        <a class="brand" routerLink="/bauakten">
          <span class="mark" aria-hidden="true"><i></i><i></i><i></i></span>
          EstrichONE
        </a>
        <ul>
          @for (item of visibleItems(); track item.link) {
            <li><a [routerLink]="item.link" routerLinkActive="active" ariaCurrentWhenActive="page">{{ item.label }}</a></li>
          }
        </ul>
      </nav>
      <div class="main">
        <header>
          <span class="user">{{ auth.user()?.displayName }}</span>
          <a routerLink="/konto/passwort">Passwort ändern</a>
          <button class="button small" type="button" (click)="signOut()">Abmelden</button>
        </header>
        <main id="inhalt" tabindex="-1">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
  styles: `
    .skip { position: absolute; left: -9999px; }
    .skip:focus { left: 16px; top: 8px; z-index: 10; background: var(--surface); padding: 8px; }
    .layout { display: grid; grid-template-columns: var(--nav-width) 1fr; min-height: 100vh; }
    nav { background: var(--brand-navy); color: #fff; padding: 16px 12px; }
    .brand { display: flex; align-items: center; gap: 10px; color: #fff; font-weight: 600; font-size: 17px; text-decoration: none; padding: 4px 8px 20px; }
    .mark { display: inline-grid; gap: 3px; }
    .mark i { display: block; height: 5px; background: #fff; border-radius: 2px; width: 18px; }
    .mark i:nth-child(2) { width: 14px; }
    .mark i:nth-child(3) { background: var(--brand-blue); }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 2px; }
    nav ul a { display: block; color: #e5e7eb; text-decoration: none; padding: 8px 10px; border-radius: var(--radius-control); }
    nav ul a:hover { background: rgb(255 255 255 / 0.08); color: #fff; }
    nav ul a.active { background: rgb(255 255 255 / 0.16); color: #fff; font-weight: 600; }
    .main { display: flex; flex-direction: column; min-width: 0; }
    header { display: flex; align-items: center; justify-content: flex-end; gap: 16px; padding: 10px 24px; background: var(--surface); border-bottom: 1px solid var(--line); }
    .user { font-weight: 600; margin-right: auto; }
    main { padding: 24px; max-width: 1280px; width: 100%; }
    @media (max-width: 760px) {
      .layout { grid-template-columns: 1fr; }
      nav ul { grid-auto-flow: column; overflow-x: auto; }
    }
  `,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly items: NavItem[] = [
    { label: 'Bauakten', link: '/bauakten', permission: 'bauakten.read' },
    { label: 'Benutzer', link: '/verwaltung/benutzer', permission: 'users.manage' },
    { label: 'Geräte', link: '/it-center/geraete', permission: 'devices.read' },
    { label: 'Helpdesk', link: '/it-center/helpdesk', permission: 'helpdesk.agent' },
    { label: 'Sicherung', link: '/system/sicherung', permission: 'backup.manage' },
  ];

  protected readonly visibleItems = computed(() => {
    const permissions = this.auth.permissions();
    return this.items.filter((item) => !item.permission || permissions.has(item.permission));
  });

  async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/anmelden']);
  }
}
