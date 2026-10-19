import { Component, computed, inject, signal } from '@angular/core';
import { AsyncPipe, DatePipe, NgClass } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { of, catchError } from 'rxjs';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

interface Kpi { label: string; key: string; icon: string; tone: string; route: string; }

@Component({
  standalone: true,
  imports: [AsyncPipe, DatePipe, NgClass, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    @if (dashboardNotice(); as n) {
      <div class="app-announce app-announce-dash" [class]="'app-announce-' + (n.type || 'info')">
        <mat-icon>{{ noticeIcon(n.type) }}</mat-icon>
        <div>@if (n.title) { <b>{{ n.title }}</b> }@if (n.text) { <span>{{ n.text }}</span> }</div>
      </div>
    }
    <section class="dash-hero">
      <div class="dash-hero-copy">
        <p class="eyebrow">{{ today | date:'EEEE, dd. MMMM yyyy' }}</p>
        <h1>{{ greeting() }}, {{ firstName() }}</h1>
        <p class="dash-hero-sub">Dein Überblick für heute – Bauakten, Einsätze und offene Punkte auf einen Blick.</p>
      </div>
      <div class="dash-hero-actions">
        @if (canCreateProject) { <a mat-flat-button color="primary" routerLink="/auftrag-wizard"><mat-icon>add</mat-icon> Neue Bauakte</a> }
        <a mat-stroked-button routerLink="/auftragssuche"><mat-icon>search</mat-icon> Bauakte suchen</a>
        <a mat-stroked-button routerLink="/kalender"><mat-icon>event</mat-icon> Kalender</a>
      </div>
    </section>

    @if (dashboard$ | async; as dashboard) {
      <section class="dash-kpi-grid">
        @for (kpi of kpis; track kpi.key) {
          <a class="dash-kpi-card" [class]="'tone-' + kpi.tone" [routerLink]="kpi.route">
            <span class="dash-kpi-icon"><mat-icon>{{ kpi.icon }}</mat-icon></span>
            <span class="dash-kpi-value">{{ $any(dashboard.stats)[kpi.key] ?? 0 }}</span>
            <span class="dash-kpi-label">{{ kpi.label }}</span>
          </a>
        }
      </section>

      @if ((dashboard.my_today && dashboard.my_today.length) || isCrew()) {
        <section class="dash-mywork">
          <mat-card class="dash-card dash-mywork-card">
            <div class="dash-card-head"><h2><mat-icon>engineering</mat-icon> Meine Baustellen &amp; Termine heute</h2></div>
            @if (dashboard.my_today && dashboard.my_today.length) {
              <div class="dash-mywork-list">
                @for (item of dashboard.my_today; track item['id']) {
                  <article class="dash-mywork-row" [class.is-linked]="projectIdOf(item)" [ngClass]="'k-' + todayKind(item)" [class.is-solo]="isSoloAppointment(item)">
                    <span class="dash-mywork-time">{{ (item['start_at'] | date:'HH:mm') }}<span class="mw-dash">–</span>{{ (item['end_at'] | date:'HH:mm') }}</span>
                    <div class="dash-mywork-main">
                      <div class="dash-mywork-line1">
                        @if (projectIdOf(item); as pid) {
                          <a class="dash-open-link" [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: pid }">
                            <b>{{ item['site_name'] || item['project_name'] || 'Baustelle' }}</b>
                          </a>
                        } @else {
                          <b>{{ item['site_name'] || item['project_name'] || 'Baustelle' }}</b>
                        }
                        @if (isSoloAppointment(item)) { <span class="dash-solo-tag">{{ item['title'] || 'Termin' }}</span> }
                        @if (isRunning(item)) { <span class="dash-now-tag">läuft</span> }
                      </div>
                      <small>{{ myWorkAddress(item) || (item['project_name'] || '') }}</small>
                      <small class="dash-mywork-who">
                        @if (item['crew_name']) { <mat-icon>groups</mat-icon>{{ item['crew_name'] }} }
                        @else { <mat-icon>person</mat-icon>Ihnen persönlich zugewiesen }
                      </small>
                    </div>
                    <span class="dash-row-actions">
                      @if (mapsUrl(item)) { <a mat-icon-button [href]="mapsUrl(item)" target="_blank" rel="noopener" matTooltip="Navigation starten" aria-label="Navigation starten"><mat-icon>directions</mat-icon></a> }
                      @if (projectIdOf(item); as pid) {
                        <a mat-stroked-button class="dash-open-btn" [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: pid }"><mat-icon>folder_open</mat-icon>Bauakte</a>
                      }
                    </span>
                  </article>
                }
              </div>
            } @else {
              <p class="dash-mywork-empty"><mat-icon>event_available</mat-icon> Für heute sind Ihnen keine Baustellen und Termine zugewiesen.</p>
            }
          </mat-card>
        </section>
      }

      <section class="dash-main-grid">
        <mat-card class="dash-card dash-today-card">
          <div class="dash-card-head"><h2><mat-icon>today</mat-icon> Heutige Einsätze</h2>@if (canPlan) { <a mat-button routerLink="/kolonnen-wochenplaner">Kolonnenplanung</a> } @else { <a mat-button routerLink="/arbeitsplan">Arbeitsplan</a> }</div>
          @if (dashboard.today.length) {
            <p class="dash-today-sum">{{ dashboard.today.length }} Einsatz(e) · {{ todayCrewCount(dashboard.today) }} Kolonne(n)</p>
            <ol class="dash-today-list">
              @for (item of dashboard.today; track item['id']) {
                <li class="dash-today-item" [ngClass]="'k-' + todayKind(item)" [class.is-now]="isRunning(item)" [class.is-done]="isPast(item)">
                  <span class="dash-today-when">
                    <b>{{ (item['start_at'] | date:'HH:mm') || '—' }}</b>
                    @if (item['end_at']) { <small>{{ item['end_at'] | date:'HH:mm' }}</small> }
                  </span>
                  <span class="dash-today-track"><i></i></span>
                  <div class="dash-today-body">
                    <div class="dash-today-line1">
                      @if (projectIdOf(item); as pid) {
                        <a class="dash-open-link" [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: pid }"><b>{{ item['title'] || item['project_name'] || 'Einsatz' }}</b></a>
                      } @else {
                        <b>{{ item['title'] || item['project_name'] || 'Einsatz' }}</b>
                      }
                      @if (isRunning(item)) { <span class="dash-now-tag">läuft</span> }
                    </div>
                    <span class="dash-today-site"><mat-icon>apartment</mat-icon>{{ item['site_name'] || item['project_name'] || 'Baustelle' }}</span>
                    <span class="dash-today-meta">
                      <span class="dash-today-crew"><mat-icon>groups</mat-icon>{{ item['crew_name'] || 'Kolonne offen' }}</span>
                      @if (todayAddress(item)) { <span class="dash-today-addr"><mat-icon>place</mat-icon>{{ todayAddress(item) }}</span> }
                    </span>
                  </div>
                  <span class="dash-today-actions">
                    @if (todayMapsUrl(item)) { <a mat-icon-button [href]="todayMapsUrl(item)" target="_blank" rel="noopener" matTooltip="Navigation" aria-label="Navigation"><mat-icon>directions</mat-icon></a> }
                    @if (projectIdOf(item); as pid) {
                      <a mat-icon-button class="dash-today-open" [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: pid }" matTooltip="Bauakte öffnen" aria-label="Bauakte öffnen"><mat-icon>folder_open</mat-icon></a>
                    }
                  </span>
                </li>
              }
            </ol>
          } @else {
            <div class="dash-empty"><mat-icon>event_available</mat-icon><span>Für heute sind keine Einsätze geplant.</span>@if (canPlan) { <a mat-stroked-button routerLink="/kolonnen-wochenplaner"><mat-icon>add</mat-icon>Einsatz planen</a> }</div>
          }
        </mat-card>

        <!-- Kalendertermine des Tages – nur für Personen mit Kalenderzugriff -->
        @if (canSeeCalendar) {
          <mat-card class="dash-card dash-cal-card">
            <div class="dash-card-head">
              <h2><mat-icon>calendar_month</mat-icon> Termine heute</h2>
              <a mat-button routerLink="/kalender">Kalender</a>
            </div>
            @if (todayEvents().length) {
              <ol class="dash-cal-list">
                @for (e of todayEvents(); track e['id']) {
                  <li class="dash-cal-item" [class.is-now]="eventRunning(e)" [class.is-done]="eventPast(e)">
                    <span class="dash-cal-bar" [style.background]="eventColor(e)"></span>
                    <span class="dash-cal-time">{{ e['all_day'] ? 'ganztägig' : eventTime(e) }}</span>
                    <span class="dash-cal-main">
                      <b>{{ e['title'] || 'Termin' }}</b>
                      @if (e['location']) { <small><mat-icon>place</mat-icon>{{ e['location'] }}</small> }
                    </span>
                    @if (eventRunning(e)) { <span class="dash-now-tag">läuft</span> }
                  </li>
                }
              </ol>
            } @else {
              <div class="dash-empty"><mat-icon>event_available</mat-icon><span>Für heute sind keine Termine eingetragen.</span></div>
            }
          </mat-card>
        }

        <mat-card class="dash-card dash-quick-card">
          <div class="dash-card-head"><h2><mat-icon>bolt</mat-icon> Schnellzugriff</h2></div>
          <div class="dash-quick-grid">
            <!-- Tagesdoku und Erstbegehung sind entfallen: Beide beginnen
                 sinnvollerweise IN einer Bauakte, nicht aus dem Dashboard
                 heraus – dort fehlt der Bezug und man muss die Akte danach
                 ohnehin erst auswählen. -->
            <a class="dash-quick-tile" routerLink="/auftragsakte"><mat-icon>folder_open</mat-icon><span>Bauakte öffnen</span></a>
            <a class="dash-quick-tile" routerLink="/auftrag-wizard"><mat-icon>create_new_folder</mat-icon><span>Neue Bauakte</span></a>
            <a class="dash-quick-tile" routerLink="/kalender"><mat-icon>calendar_month</mat-icon><span>Kalender</span></a>
            <a class="dash-quick-tile" routerLink="/kunden"><mat-icon>business</mat-icon><span>Kunden</span></a>
            <a class="dash-quick-tile" routerLink="/fahrzeuge"><mat-icon>local_shipping</mat-icon><span>Fahrzeuge</span></a>
          </div>
        </mat-card>
      </section>
    } @else {
      <mat-card class="dash-card"><p class="muted">Übersicht wird geladen…</p></mat-card>
    }
  `
})
export class DashboardComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  /** Rechte für die Schnellaktionen der Übersicht. */
  readonly canCreateProject = this.auth.canWrite('projects');
  readonly canPlan = this.auth.canWrite('planning');
  /** Kalenderzugriff: Wer den Kalender sehen darf, sieht die Termine
   *  auch auf der Tagesübersicht. */
  readonly canSeeCalendar = this.auth.canRead('calendar') || this.auth.canRead('planning');
  readonly calendarEvents = signal<GenericRecord[]>([]);
  readonly calendarCategories = signal<GenericRecord[]>([]);

  /** Termine des heutigen Tages, nach Uhrzeit sortiert. */
  readonly todayEvents = computed(() => {
    const today = new Date();
    const iso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
    return this.calendarEvents()
      .filter((e) => String(e['start_at'] || '').slice(0, 10) === iso)
      .sort((a, b) => String(a['start_at'] || '').localeCompare(String(b['start_at'] || '')));
  });
  eventColor(e: GenericRecord): string {
    if (e['color']) return String(e['color']);
    const cat = this.calendarCategories().find((c) => String(c['code']) === String(e['event_type'] || ''));
    return String(cat?.['color'] || '#1a73e8');
  }
  eventTime(e: GenericRecord): string {
    const fmt = (v: unknown) => { const m = String(v ?? '').match(/(\d{2}):(\d{2})/); return m ? `${m[1]}:${m[2]}` : ''; };
    const a = fmt(e['start_at']); const b = fmt(e['end_at']);
    return b ? `${a}–${b}` : a;
  }
  private eventMinutes(v: unknown): number {
    const m = String(v ?? '').match(/(\d{2}):(\d{2})/);
    return m ? Number(m[1]) * 60 + Number(m[2]) : -1;
  }
  eventRunning(e: GenericRecord): boolean {
    if (e['all_day']) return true;
    const now = new Date().getHours() * 60 + new Date().getMinutes();
    const a = this.eventMinutes(e['start_at']); const b = this.eventMinutes(e['end_at']);
    return a >= 0 && now >= a && (b < 0 || now < b);
  }
  eventPast(e: GenericRecord): boolean {
    if (e['all_day']) return false;
    const b = this.eventMinutes(e['end_at']);
    return b >= 0 && (new Date().getHours() * 60 + new Date().getMinutes()) >= b;
  }
  private loadCalendar(): void {
    if (!this.canSeeCalendar) return;
    this.api.list('calendar_events', '', 400).subscribe({ next: (r) => this.calendarEvents.set(r.items || []), error: () => {} });
    this.api.list('calendar_categories', '', 100).subscribe({ next: (r) => this.calendarCategories.set(r.items || []), error: () => {} });
  }
  isCrew(): boolean { return this.auth.isCrewMember() || this.auth.roleCodes.includes('kolonnenfuehrer'); }
  /** Bauakten-Kennung eines Eintrags – je nach Endpunkt unterschiedlich benannt. */
  /** Art des Einsatzes für die farbliche Kennzeichnung. */
  todayKind(item: GenericRecord): 'work' | 'inspection' | 'other' {
    const title = String(item['title'] || '').toLowerCase();
    if (/begehung|besichtigung|kontrolle|abnahme|aufma|messung|kundentermin/.test(title)) return 'inspection';
    if (/estrich|arbeit/.test(title)) return 'work';
    return 'other';
  }
  private minutesOf(value: unknown): number {
    const m = String(value ?? '').match(/(\d{2}):(\d{2})/);
    return m ? Number(m[1]) * 60 + Number(m[2]) : -1;
  }
  private nowMinutes(): number { const d = new Date(); return d.getHours() * 60 + d.getMinutes(); }
  /** Läuft der Einsatz gerade? Hebt die aktuelle Zeile hervor. */
  isRunning(item: GenericRecord): boolean {
    const from = this.minutesOf(item['start_at']); const to = this.minutesOf(item['end_at']);
    if (from < 0) return false;
    const now = this.nowMinutes();
    return now >= from && (to < 0 || now < to);
  }
  /** Bereits beendete Einsätze werden gedämpft dargestellt. */
  isPast(item: GenericRecord): boolean {
    const to = this.minutesOf(item['end_at']);
    return to >= 0 && this.nowMinutes() >= to;
  }
  /** Termin ohne Kolonne – der Person persönlich zugewiesen (z. B. Begehung). */
  isSoloAppointment(item: GenericRecord): boolean {
    return !item['crew_id'] || this.todayKind(item) === 'inspection';
  }
  todayCrewCount(list: GenericRecord[]): number {
    return new Set((list || []).map((i) => String(i['crew_id'] ?? ''))).size;
  }
  todayAddress(item: GenericRecord): string {
    return [item['site_address'], item['site_city']].map((v) => String(v ?? '').trim()).filter((v) => v.length > 0).join(', ');
  }
  todayMapsUrl(item: GenericRecord): string {
    const addr = this.todayAddress(item);
    return addr ? 'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(addr) : '';
  }
  projectIdOf(item: GenericRecord): string {
    // resolved_project_id kommt aus der API und beruecksichtigt, dass eine
    // Einplanung die Bauakte auch nur ueber die Baustelle kennen kann.
    return String(item['resolved_project_id'] || item['project_id'] || '');
  }
  myWorkAddress(item: GenericRecord): string {
    return [item['site_address'], item['site_city']].map((v) => String(v ?? '').trim()).filter((v) => v.length > 0).join(', ');
  }
  mapsUrl(item: GenericRecord): string {
    const addr = this.myWorkAddress(item);
    return addr ? 'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(addr) : '';
  }
  /** Der Kalender läuft vorübergehend über Google Kalender. */
  readonly googleCalendarUrl = 'https://calendar.google.com/calendar/r';
  readonly today = new Date();
  readonly dashboard$ = this.api.dashboard().pipe(catchError(() => of(null as any)));
  readonly dashboardNotice = signal<{ type?: string; title?: string; text?: string } | null>(null);
  noticeIcon(type?: string): string { return ({ success: 'check_circle', warning: 'warning', critical: 'error' } as Record<string, string>)[String(type)] || 'info'; }
  constructor() {
    this.loadCalendar();
    this.api.appSettings().subscribe({
      next: (res) => { const n = res?.['dashboard_message']; if (n && n.enabled && (n.title || n.text)) this.dashboardNotice.set(n); },
      error: () => {}
    });
  }
  readonly kpis: Kpi[] = [
    { label: 'aktive Bauakten', key: 'projects', icon: 'assignment', tone: 'blue', route: '/auftragssuche' },
    { label: 'aktive Baustellen', key: 'sites', icon: 'apartment', tone: 'slate', route: '/auftragssuche' },
    { label: 'heute eingeplant', key: 'assignments_today', icon: 'event', tone: 'green', route: '/kolonnen-wochenplaner' },
  ];
  firstName(): string { return (this.auth.userName || 'Team').split(/\s+/)[0]; }
  greeting(): string { const h = new Date().getHours(); if (h < 11) return 'Guten Morgen'; if (h < 18) return 'Guten Tag'; return 'Guten Abend'; }
}
