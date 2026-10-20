import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AssignmentView, BackupStatusResponse, BauaktePage, PlanningRange, TicketSummary } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { isoDay } from '../shared/days';
import { IconComponent } from '../shared/icon.component';
import { formatDateTime } from '../shared/status';

interface Tile {
  label: string;
  icon: string;
  link: string;
  queryParams?: Record<string, string>;
  permission?: string;
}

/** Startseite: Programmkacheln nach Fachgruppen (wie im Desktop-Programm) und rechts der Tagesüberblick. */
@Component({
  selector: 'eo-start',
  imports: [RouterLink, IconComponent],
  template: `
    <div class="layout">
      <section class="launchpad" aria-label="Programme">
        @for (group of groups(); track group.label) {
          <div class="group">
            <h2>{{ group.label }}</h2>
            <ul>
              @for (tile of group.tiles; track tile.label) {
                <li>
                  <a class="tile" [routerLink]="tile.link" [queryParams]="tile.queryParams ?? null">
                    <span class="tile-icon"><eo-icon [name]="tile.icon" [size]="30" [stroke]="1.4" /></span>
                    <span class="tile-label">{{ tile.label }}</span>
                  </a>
                </li>
              }
            </ul>
          </div>
        }
      </section>

      <aside class="today" aria-label="Heute">
        <div class="pane">
          <div class="pane-title">Heute · {{ todayLabel }}</div>
          @if (has('planning.read')) {
            <h3>Einsätze</h3>
            @for (a of assignments(); track a.id) {
              <a class="assignment" [routerLink]="['/bauakten', a.bauakteId]">
                <span class="time">{{ a.startTime ? a.startTime.slice(0, 5) : '–' }}</span>
                <span><strong>{{ a.bauakteTitle }}</strong><br /><small>{{ a.activity }}@if (a.crewName) { · {{ a.crewName }} }</small></span>
                @if (a.warnings.length) { <span class="warn" [attr.title]="a.warnings.join(' ')"><eo-icon name="warning" [size]="16" /></span> }
              </a>
            } @empty {
              <p class="muted">Heute ist nichts eingeplant.</p>
            }
          }
          <div class="figures">
            @if (has('bauakten.read')) {
              <a class="figure" routerLink="/bauakten" [queryParams]="{ status: 'inquiry' }">
                <strong>{{ inquiries() ?? '–' }}</strong><span>offene Anfragen</span>
              </a>
            }
            @if (has('helpdesk.agent')) {
              <a class="figure" routerLink="/it-center/helpdesk">
                <strong>{{ tickets() ?? '–' }}</strong><span>offene Tickets</span>
                @if (blocked()) { <small class="alert">{{ blocked() }} können nicht arbeiten</small> }
              </a>
            }
          </div>
        </div>
        @if (has('backup.manage') && backup(); as b) {
          <div class="pane">
            <div class="pane-title">Sicherung</div>
            @if (b.lastSuccess) {
              <p><span class="status" [class.success]="!stale()" [class.hint]="stale()">{{ stale() ? 'Veraltet' : 'Aktuell' }}</span>
                Letzte erfolgreiche Sicherung: {{ date(b.lastSuccess.at) }}</p>
            } @else {
              <p><span class="status error">Keine Sicherung</span> Bisher wurde keine Sicherung abgeschlossen.</p>
            }
            <a routerLink="/system/sicherung">Zur Sicherung</a>
          </div>
        }
      </aside>
    </div>
  `,
  styles: `
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 20px; align-items: start; }
    @media (max-width: 1100px) { .layout { grid-template-columns: 1fr; } }
    .launchpad { display: grid; grid-template-columns: repeat(auto-fill, minmax(330px, 1fr)); gap: 8px 28px; }
    .group h2 { margin: 4px 0 8px; font-size: 13px; font-weight: 700; color: var(--brand-navy); padding-bottom: 4px; border-bottom: 2px solid var(--brand-navy); }
    @media (prefers-color-scheme: dark) { .group h2 { color: var(--text-primary); border-color: var(--link); } }
    .group ul { list-style: none; margin: 0 0 16px; padding: 0; display: flex; flex-wrap: wrap; gap: 8px; }
    .tile { display: flex; flex-direction: column; align-items: center; gap: 6px; width: 96px; padding: 12px 4px 8px; text-decoration: none; color: var(--text-primary);
      background: var(--surface); border: 1px solid var(--line); border-radius: 4px; box-shadow: var(--shadow-pane); }
    .tile:hover { border-color: var(--brand-navy); background: var(--grid-hover); }
    .tile-icon { color: var(--brand-navy); display: grid; place-items: center; height: 34px; }
    @media (prefers-color-scheme: dark) { .tile-icon { color: var(--link); } }
    .tile-label { font-size: 12px; line-height: 15px; text-align: center; }
    .today { display: grid; gap: 12px; }
    .today h3 { margin-top: 0; }
    .assignment { display: grid; grid-template-columns: 44px 1fr auto; gap: 8px; align-items: start; padding: 6px 4px; border-bottom: 1px solid var(--line);
      color: var(--text-primary); text-decoration: none; }
    .assignment:hover { background: var(--grid-hover); }
    .time { font-variant-numeric: tabular-nums; font-weight: 600; color: var(--brand-navy); }
    .warn { color: var(--hint); }
    .figures { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 12px; }
    .figure { display: grid; padding: 10px; border: 1px solid var(--line); border-radius: 4px; text-decoration: none; color: var(--text-primary); background: var(--surface-alt); }
    .figure strong { font-size: 24px; line-height: 30px; color: var(--brand-navy); }
    @media (prefers-color-scheme: dark) { .figure strong, .time { color: var(--link); } }
    .figure span { font-size: 12px; color: var(--text-secondary); }
    .alert { color: var(--error); font-weight: 600; }
    .muted { color: var(--text-secondary); }
  `,
})
export class StartPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  protected readonly todayLabel = new Date().toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: 'long' });
  protected readonly date = formatDateTime;
  protected readonly assignments = signal<AssignmentView[]>([]);
  protected readonly inquiries = signal<string | null>(null);
  protected readonly tickets = signal<number | null>(null);
  protected readonly blocked = signal(0);
  protected readonly backup = signal<BackupStatusResponse | null>(null);
  protected readonly stale = computed(() => {
    const last = this.backup()?.lastSuccess?.at;
    return !last || Date.now() - new Date(last).getTime() > 36 * 3600 * 1000;
  });

  private readonly allGroups: { label: string; tiles: Tile[] }[] = [
    {
      label: 'Auftragsbearbeitung',
      tiles: [
        { label: 'Anfragen', icon: 'inbox', link: '/bauakten', queryParams: { status: 'inquiry' }, permission: 'bauakten.read' },
        { label: 'Bauakten', icon: 'bauakte', link: '/bauakten', permission: 'bauakten.read' },
        { label: 'Neue Bauakte', icon: 'folder-plus', link: '/bauakten', queryParams: { neu: 'bauakte' }, permission: 'bauakten.create' },
        { label: 'Neue Anfrage', icon: 'inbox', link: '/bauakten', queryParams: { neu: 'anfrage' }, permission: 'bauakten.create' },
      ],
    },
    {
      label: 'Planung',
      tiles: [
        { label: 'Wochenplan', icon: 'calendar', link: '/planung', permission: 'planning.read' },
        { label: 'Kolonnen', icon: 'crew', link: '/planung/ressourcen', queryParams: { reiter: 'kolonnen' }, permission: 'planning.read' },
        { label: 'Fahrzeuge und Geräte', icon: 'truck', link: '/planung/ressourcen', queryParams: { reiter: 'geraete' }, permission: 'planning.read' },
        { label: 'Abwesenheiten', icon: 'absence', link: '/planung/ressourcen', queryParams: { reiter: 'abwesenheiten' }, permission: 'planning.read' },
      ],
    },
    {
      label: 'Stammdaten',
      tiles: [{ label: 'Kunden', icon: 'customers', link: '/kunden', permission: 'contacts.read' }],
    },
    {
      label: 'IT Center',
      tiles: [
        { label: 'Benutzer', icon: 'users', link: '/verwaltung/benutzer', permission: 'users.manage' },
        { label: 'Geräte', icon: 'device', link: '/it-center/geraete', permission: 'devices.read' },
        { label: 'Helpdesk', icon: 'helpdesk', link: '/it-center/helpdesk', permission: 'helpdesk.agent' },
      ],
    },
    {
      label: 'System',
      tiles: [
        { label: 'Sicherung', icon: 'backup', link: '/system/sicherung', permission: 'backup.manage' },
        { label: 'Passwort ändern', icon: 'key', link: '/konto/passwort' },
      ],
    },
  ];

  protected readonly groups = computed(() => {
    const permissions = this.auth.permissions();
    return this.allGroups
      .map((g) => ({ ...g, tiles: g.tiles.filter((t) => !t.permission || permissions.has(t.permission)) }))
      .filter((g) => g.tiles.length > 0);
  });

  constructor() {
    void this.load();
  }

  protected has(permission: string): boolean {
    return this.auth.permissions().has(permission);
  }

  private async load(): Promise<void> {
    const today = isoDay(new Date());
    // Jede Kachel des Überblicks lädt für sich; ein fehlendes Recht oder ein Fehler lässt die anderen stehen.
    if (this.has('planning.read')) {
      void firstValueFrom(this.http.get<PlanningRange>('/api/v1/planning/range', { params: { from: today, to: today } }))
        .then((r) => this.assignments.set(r.assignments), () => undefined);
    }
    if (this.has('bauakten.read')) {
      void firstValueFrom(this.http.get<BauaktePage>('/api/v1/bauakten', { params: { status: 'inquiry', limit: 100 } }))
        .then((p) => this.inquiries.set(p.nextCursor ? '100+' : String(p.items.length)), () => undefined);
    }
    if (this.has('helpdesk.agent')) {
      void firstValueFrom(this.http.get<TicketSummary[]>('/api/v1/tickets', { params: { filter: 'open' } })).then((t) => {
        this.tickets.set(t.length);
        this.blocked.set(t.filter((x) => x.reporterBlocked).length);
      }, () => undefined);
    }
    if (this.has('backup.manage')) {
      void firstValueFrom(this.http.get<BackupStatusResponse>('/api/v1/backup/status')).then((b) => this.backup.set(b), () => undefined);
    }
  }
}
