import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Calendar, EventInput } from '@fullcalendar/core';
import deLocale from '@fullcalendar/core/locales/de';
import dayGridPlugin from '@fullcalendar/daygrid';
import timeGridPlugin from '@fullcalendar/timegrid';
import listPlugin from '@fullcalendar/list';
import interactionPlugin from '@fullcalendar/interaction';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { CalendarCategory, CalendarCategoryDialogComponent, DEFAULT_CATEGORIES } from '../calendar/calendar.component';
import { GcalEventDialogComponent, GcalEventResult } from './gcal-event-dialog.component';

type GView = 'timeGridDay' | 'timeGridThreeDay' | 'timeGridWeek' | 'dayGridMonth' | 'listWeek' | 'listMonth';

/**
 * Kalender auf Basis von FullCalendar.
 *
 * WARUM EINE BIBLIOTHEK:
 * Die vorherige Fassung war handgebaut – Ziehen, Überlappungsberechnung,
 * Rasterhöhen und Zeitzonen waren selbst implementiert. Genau dort sind
 * wiederholt Fehler entstanden (Rasterversatz durch box-sizing, unterdrückte
 * Klicks durch preventDefault, unleserliche Beschriftung durch eine
 * Overlay-Ebene). FullCalendar löst diese Punkte seit Jahren erprobt und
 * verhält sich beim Ziehen, Ändern der Dauer und bei parallelen Terminen wie
 * der Google Kalender.
 *
 * Die Umgebung drumherum (Seitenleiste mit Mini-Monat und Kalenderliste,
 * Kopfleiste, Termindialog, Detailfenster) bleibt eigener Code, damit die
 * Anbindung an Bauakten, Baustellen und die Planung erhalten bleibt.
 */
@Component({
  selector: 'ew-gcal',
  standalone: true,
  imports: [FormsModule, RouterLink, MatIconModule, MatMenuModule, MatTooltipModule, MatDialogModule, MatSnackBarModule],
  template: `
    <div class="gc gc-dense">
      <!-- ===== Kopfleiste ===== -->
      <header class="gc-top">
        <button type="button" class="gc-icon" (click)="toggleSidebar()" matTooltip="Seitenleiste" aria-label="Seitenleiste">
          <mat-icon>menu</mat-icon>
        </button>
        <span class="gc-brand"><mat-icon>calendar_month</mat-icon><b>Kalender</b></span>

        <button type="button" class="gc-today" (click)="today()">Heute</button>
        <button type="button" class="gc-icon" (click)="prev()" matTooltip="Zurück" aria-label="Zurück"><mat-icon>chevron_left</mat-icon></button>
        <button type="button" class="gc-icon" (click)="next()" matTooltip="Vor" aria-label="Vor"><mat-icon>chevron_right</mat-icon></button>
        <h1 class="gc-title">{{ title() }}</h1>

        <span class="gc-spacer"></span>

        <label class="gc-search">
          <mat-icon>search</mat-icon>
          <input type="search" [(ngModel)]="search" (ngModelChange)="refreshEvents()" placeholder="Termine durchsuchen">
        </label>


        <button type="button" class="gc-viewbtn" [matMenuTriggerFor]="viewMenu">
          {{ viewLabel() }}<mat-icon>arrow_drop_down</mat-icon>
        </button>
        <mat-menu #viewMenu="matMenu" xPosition="before">
          @if (isPhone()) {
            <button mat-menu-item (click)="setView('listWeek')">Termine der Woche</button>
            <button mat-menu-item (click)="setView('listMonth')">Termine des Monats</button>
            <button mat-menu-item (click)="setView('timeGridDay')">Tagesraster</button>
            <button mat-menu-item (click)="setView('dayGridMonth')">Monatsübersicht</button>
          } @else {
            <button mat-menu-item (click)="setView('timeGridDay')">Tag</button>
            <button mat-menu-item (click)="setView('timeGridThreeDay')">3 Tage</button>
            <button mat-menu-item (click)="setView('timeGridWeek')">Woche</button>
            <button mat-menu-item (click)="setView('dayGridMonth')">Monat</button>
            <button mat-menu-item (click)="setView('listWeek')">Terminübersicht</button>
          }
        </mat-menu>
      </header>

      <div class="gc-body">
        <!-- ===== Seitenleiste ===== -->
        @if (sidebarOpen()) {
          <aside class="gc-side">
            @if (canWrite) {
              <button type="button" class="gc-create" (click)="createEvent()">
                <mat-icon>add</mat-icon><span>Erstellen</span>
              </button>
            }

            <section class="gc-mini">
              <header>
                <b>{{ miniTitle() }}</b>
                <span>
                  <button type="button" class="gc-mini-nav" (click)="shiftMini(-1)" aria-label="Vormonat"><mat-icon>chevron_left</mat-icon></button>
                  <button type="button" class="gc-mini-nav" (click)="shiftMini(1)" aria-label="Nächster Monat"><mat-icon>chevron_right</mat-icon></button>
                </span>
              </header>
              <div class="gc-mini-grid">
                @for (w of miniWeekdays; track $index) { <span class="gc-mini-wd">{{ w }}</span> }
                @for (d of miniDays(); track d.iso) {
                  <button type="button" class="gc-mini-day"
                          [class.other]="!d.inMonth" [class.today]="d.isToday" [class.sel]="d.iso === selectedIso()"
                          [class.has]="d.count > 0"
                          (click)="goTo(d.date)">
                    <!-- Die farbige Fläche liegt auf einem eigenen Element.
                         Der Button selbst hat keinen Hintergrund – so kann
                         keine Button-Regel (Mindestbreite, Innenabstand) die
                         Kreisform zu einer Ellipse ziehen. -->
                    <span class="gc-mini-face">{{ d.day }}</span>
                  </button>
                }
              </div>
            </section>

            <section class="gc-cals">
              <h3>
                Meine Kalender
                @if (canWrite) {
                  <button type="button" class="gc-addcal" (click)="addCategory()" matTooltip="Eigenen Kalender anlegen" aria-label="Kalender anlegen"><mat-icon>add</mat-icon></button>
                }
              </h3>
              @for (c of categories(); track c.code) {
                <div class="gc-cal-row">
                  <label class="gc-cal">
                    <input type="checkbox" [checked]="isActive(c.code)" (change)="toggleCategory(c.code)">
                    <span class="gc-box" [style.background]="isActive(c.code) ? c.color : 'transparent'" [style.border-color]="c.color">
                      <mat-icon>check</mat-icon>
                    </span>
                    <span class="gc-cal-name">{{ c.label }}</span>
                  </label>
                  @if (canWrite && !isSystemCategory(c)) {
                    <button type="button" class="gc-cal-more" [matMenuTriggerFor]="calMenu"
                            (click)="$event.preventDefault(); $event.stopPropagation()"
                            [attr.aria-label]="'Kalender ' + c.label + ' bearbeiten'">
                      <mat-icon>more_vert</mat-icon>
                    </button>
                    <mat-menu #calMenu="matMenu" xPosition="before">
                      <button mat-menu-item (click)="editCategory(c)"><mat-icon>edit</mat-icon><span>Bearbeiten</span></button>
                      <button mat-menu-item (click)="deleteCategory(c)"><mat-icon>delete</mat-icon><span>Löschen</span></button>
                    </mat-menu>
                  }
                </div>
              }
            </section>
          </aside>
        }

        <!-- ===== Kalender ===== -->
        <main class="gc-main">
          <!-- Desktop: FullCalendar. Am Handy bleibt es im Dokument (es liefert
               weiterhin Zeitraum und Titel), wird aber ausgeblendet. -->
          <div #host class="gc-host"></div>

        </main>
      </div>

      @if (canWrite && isPhone()) {
        <button type="button" class="gc-fab" (click)="createEvent()" aria-label="Termin erstellen"><mat-icon>add</mat-icon></button>
      }

      <!-- ===== Detailfenster ===== -->
      @if (detail(); as d) {
        <div class="gc-pop-backdrop" (click)="closeDetail()">
          <aside class="gc-pop" (click)="$event.stopPropagation()" role="dialog" aria-modal="true">
            <header class="gc-pop-head">
              <span class="gc-spacer"></span>
              @if (!d['_locked'] && canWrite) {
                <button type="button" class="gc-icon" (click)="editDetail()" matTooltip="Bearbeiten" aria-label="Bearbeiten"><mat-icon>edit</mat-icon></button>
              }
              @if (!d['_locked'] && canDelete) {
                <button type="button" class="gc-icon" (click)="deleteDetail()" matTooltip="Löschen" aria-label="Löschen"><mat-icon>delete</mat-icon></button>
              }
              <button type="button" class="gc-icon" (click)="closeDetail()" matTooltip="Schließen" aria-label="Schließen"><mat-icon>close</mat-icon></button>
            </header>
            <div class="gc-pop-body">
              <div class="gc-pop-title">
                <span class="gc-pop-dot" [style.background]="colorOf(d)"></span>
                <div>
                  <b>{{ d['title'] || 'Termin' }}</b>
                  <span>{{ detailWhen(d) }}</span>
                </div>
              </div>
              @if (d['_locked']) {
                <p class="gc-pop-locked"><mat-icon>lock</mat-icon>Termin aus der Planung – Änderungen erfolgen dort.</p>
              }
              @if (d['location']) { <p class="gc-pop-row"><mat-icon>place</mat-icon><span>{{ d['location'] }}</span></p> }
              @if (assigneeName(d)) { <p class="gc-pop-row"><mat-icon>person</mat-icon><span>{{ assigneeName(d) }}</span></p> }
              <p class="gc-pop-row"><mat-icon>label</mat-icon><span>{{ categoryLabel(d) }}</span></p>
              @if (d['recurrence_rule']) { <p class="gc-pop-row"><mat-icon>repeat</mat-icon><span>{{ recurrenceLabel(d) }}</span></p> }
              @if (d['description'] || d['notes']) { <p class="gc-pop-row"><mat-icon>notes</mat-icon><span class="gc-pop-desc">{{ d['description'] || d['notes'] }}</span></p> }
              @if (d['project_id']) {
                <a class="gc-pop-link" [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: d['project_id'] }" (click)="closeDetail()">
                  <mat-icon>folder_open</mat-icon><span>{{ projectLabel(d['project_id']) }}</span><mat-icon class="gc-pop-go">chevron_right</mat-icon>
                </a>
              }
            </div>
          </aside>
        </div>
      }
    </div>
  `,
  styleUrl: './gcal.component.css'
})
export class GcalComponent implements AfterViewInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  @ViewChild('host') hostRef?: ElementRef<HTMLDivElement>;
  private calendar?: Calendar;

  readonly canWrite = this.auth.canWrite('planning');
  readonly canDelete = this.auth.canDelete('planning');

  readonly events = signal<GenericRecord[]>([]);
  readonly planningEvents = signal<GenericRecord[]>([]);
  readonly projects = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly users = signal<GenericRecord[]>([]);
  readonly categories = signal<CalendarCategory[]>(DEFAULT_CATEGORIES);
  readonly hiddenCategories = signal<Set<string>>(new Set());
  readonly detail = signal<GenericRecord | null>(null);
  readonly title = signal('');
  readonly selectedIso = signal(this.iso(new Date()));
  readonly miniAnchor = signal(new Date());
  readonly sidebarOpen = signal(window.innerWidth >= 1100);
  readonly isPhone = signal(window.innerWidth < 760);

  /** Der Kalender nutzt nur noch die gedrängte Zeilenhöhe – sie zeigt den
   *  ganzen Arbeitstag ohne Scrollen. Eine Wahlmöglichkeit gab es kurzzeitig,
   *  sie hat sich als überflüssig erwiesen. */

  /**
   * Zeilenhöhe umstellen.
   *
   * `compact` zeigt eine Stunde auf rund 34px – damit ist der ganze
   * Arbeitstag ohne Scrollen sichtbar. `wide` gibt jedem Termin mehr Platz
   * für Titel und Ort. Umgesetzt über eine Klasse am Gerüst, damit
   * FullCalendar nicht neu aufgebaut werden muss.
   */
  /**
   * Höhe einer halben Stunde in Pixeln.
   *
   * Als Variable direkt am Element gesetzt – dadurch kann keine
   * Regelreihenfolge dazwischenkommen. Genau das war zuvor das Problem:
   * Es gab zwei Regelsätze für dieselbe Sache, und je nach Ladereihenfolge
   * hat immer der gedrängte gewonnen.
   */
  // Am Handy ist die Terminübersicht die brauchbarste Ansicht – ein
  // Stundenraster ist auf dieser Breite nicht bedienbar.
  readonly view = signal<GView>(window.innerWidth < 760 ? 'listWeek' : 'timeGridWeek');
  search = '';

  readonly miniWeekdays = ['M', 'D', 'M', 'D', 'F', 'S', 'S'];

  static readonly PLANNING_CATEGORY: CalendarCategory =
    { code: '__planning__', label: 'Termine ohne Kolonne', color: '#b45309', icon: 'fact_check' };

  // ---- Aufbau -----------------------------------------------------------
  ngAfterViewInit(): void {
    const el = this.hostRef?.nativeElement;
    if (!el) return;

    this.calendar = new Calendar(el, {
      plugins: [dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin],
      locale: deLocale,
      initialView: this.view(),
      headerToolbar: false,                 // eigene Kopfleiste
      // Eigene Ansicht: drei Tage. Auf dem Handy ist die volle Woche mit
      // rund 40px je Spalte unbrauchbar, ein einzelner Tag gibt zu wenig
      // Überblick. Drei Tage sind der brauchbare Mittelweg.
      views: {
        // Monatsansicht: Termine als farbige Blöcke statt als Punkt mit
        // Text. Nur so ist die Kategoriefarbe auf einen Blick erkennbar.
        dayGridMonth: { dayMaxEvents: 4 },
        listWeek: { listDayFormat: { weekday: 'long', day: 'numeric', month: 'long' } },
        listMonth: { listDayFormat: { weekday: 'short', day: 'numeric', month: 'short' } },
        timeGridThreeDay: {
          type: 'timeGrid',
          duration: { days: 3 },
          buttonText: '3 Tage',
          dayHeaderFormat: { weekday: 'short', day: 'numeric', omitCommas: true }
        }
      },
      // Am Handy wächst der Kalender mit seinem Inhalt und die SEITE scrollt.
      // Mit '100%' braucht FullCalendar einen Elternteil mit fester Höhe –
      // die gab es am Handy nicht, das Raster war deshalb null Pixel hoch
      // und die Terminübersicht blieb leer.
      height: window.innerWidth < 760 ? 'auto' : '100%',
      expandRows: true,
      nowIndicator: true,
      // Halbe Stunden als Raster; die Beschriftung nur zur vollen Stunde.
      // Beim Ziehen wird auf 15 Minuten eingerastet (snapDuration).
      slotDuration: '00:30:00',
      slotLabelInterval: '01:00:00',
      // Ab wann ein Termin als "flach" gilt und Zeit/Titel nebeneinander
      // rückt. Vorgabe sind 30px – dadurch galt schon ein Ein-Stunden-Termin
      // als knapp und der Ort entfiel. 46px passt zu drei Zeilen.
      eventShortHeight: 46,
      snapDuration: '00:15:00',
      slotMinTime: '05:00:00',
      slotMaxTime: '22:00:00',
      scrollTime: '07:00:00',
      firstDay: 1,
      weekNumbers: true,
      weekText: 'KW',
      dayMaxEvents: true,
      // Termine IMMER als farbiger Block darstellen.
      // Vorgabe von FullCalendar ist 'auto': In der Monatsansicht werden
      // zeitgebundene Termine damit als kleiner Punkt mit schwarzem Text
      // gezeichnet – die Kategoriefarbe ging dort unter. Die Einstellung nur
      // in der Monatsansicht zu setzen hat nicht durchgegriffen, deshalb
      // steht sie jetzt global. Auf die übrigen Ansichten wirkt sie sich
      // nicht aus, dort sind Termine ohnehin Blöcke.
      eventDisplay: 'block',
      editable: this.canWrite,
      eventStartEditable: this.canWrite,
      eventDurationEditable: this.canWrite,
      selectable: this.canWrite,
      selectMirror: true,
      eventTimeFormat: { hour: '2-digit', minute: '2-digit', hour12: false, separator: '–' },
      // Eigene Darstellung: Zeit, Titel und – wenn hinterlegt – der Ort.
      eventContent: (arg) => {
        const location = String((arg.event.extendedProps as Record<string, unknown>)?.['location'] || '');
        const wrap = document.createElement('div');
        wrap.className = 'gc-ev';
        if (arg.timeText) {
          const time = document.createElement('span');
          time.className = 'gc-ev-time';
          time.textContent = arg.timeText;
          wrap.appendChild(time);
        }
        const title = document.createElement('span');
        title.className = 'gc-ev-title';
        title.textContent = arg.event.title;
        wrap.appendChild(title);
        // Ort nur unterdrücken, wenn er wörtlich im Titel steckt – sonst
        // stünde bei Planungsterminen der Baustellenname doppelt.
        const redundant = !!location && arg.event.title.toLowerCase().trim() === location.toLowerCase().trim();
        if (location && !redundant) {
          const place = document.createElement('span');
          place.className = 'gc-ev-place';
          place.textContent = location;
          wrap.appendChild(place);
        }
        // Zuständige Person – erscheint in der Terminübersicht und in
        // luftiger Zeilenhöhe auch im Raster.
        const assignee = String((arg.event.extendedProps as Record<string, unknown>)?.['assignee'] || '');
        if (assignee) {
          const who = document.createElement('span');
          who.className = 'gc-ev-who';
          who.textContent = assignee;
          wrap.appendChild(who);
        }
        // Kalendername – wird nur in der Terminübersicht eingeblendet.
        const category = String((arg.event.extendedProps as Record<string, unknown>)?.['category'] || '');
        if (category) {
          const cat = document.createElement('span');
          cat.className = 'gc-ev-cat';
          cat.textContent = category;
          wrap.appendChild(cat);
        }
        return { domNodes: [wrap] };
      },
      slotLabelFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
      buttonText: { today: 'Heute' },
      allDayText: 'Ganztägig',
      noEventsText: 'Keine Termine in diesem Zeitraum',
      listDayFormat: { weekday: 'long', day: 'numeric', month: 'long' },
      listDaySideFormat: false,

      // Auswahl im Raster legt einen Termin an
      select: (info) => {
        if (!this.canWrite) return;
        this.openDialog(null, info.start, info.start.getHours(), info.end);
        this.calendar?.unselect();
      },
      // Die Listenansicht faerbt nur den Punkt. Fuer die Kartenoptik am Handy
      // wird die Farbe zusaetzlich als CSS-Variable an der Zeile hinterlegt.
      eventDidMount: (info) => {
        const bg = String(info.event.backgroundColor || '#1a73e8');
        info.el.style.setProperty('--ev', bg);
        info.el.style.setProperty('--ink', this.textColorFor(bg));
        if (info.event.extendedProps?.['locked']) info.el.classList.add('fc-locked');
      },
      eventClick: (info) => {
        const record = this.recordFor(info.event.id);
        if (record) this.detail.set(record);
      },
      eventDrop: (info) => {
        const record = this.recordFor(info.event.id);
        if (!record || record['_locked']) { info.revert(); return; }
        this.persistTimes(record, info.event.start, info.event.end, () => info.revert());
      },
      eventResize: (info) => {
        const record = this.recordFor(info.event.id);
        if (!record || record['_locked']) { info.revert(); return; }
        this.persistTimes(record, info.event.start, info.event.end, () => info.revert());
      },
      datesSet: (info) => {
        this.title.set(info.view.title);
        this.selectedIso.set(this.iso(info.view.currentStart));
        this.miniAnchor.set(new Date(info.view.currentStart));
        this.loadPlanningAppointments(info.start, info.end);
      }
    });

    this.calendar.render();
    this.load();
    window.addEventListener('resize', this.onResize);
  }
  ngOnDestroy(): void {
    window.removeEventListener('resize', this.onResize);
    this.calendar?.destroy();
  }
  private readonly onResize = (): void => {
    // Auch beim Ändern der Fensterbreite neu rechnen lassen.
    setTimeout(() => this.calendar?.updateSize(), 0);
    const phone = window.innerWidth < 760;
    if (phone === this.isPhone()) return;
    this.isPhone.set(phone);
    // Beim Wechsel der Gerätebreite auf die jeweils brauchbare Ansicht.
    // Höhenverhalten mitziehen: am Handy mitwachsend, am Desktop fest.
    this.calendar?.setOption('height', phone ? 'auto' : '100%');
    if (phone && this.view().startsWith('timeGrid')) { this.setView('listWeek'); return; }
    if (!phone && this.view().startsWith('list')) { this.setView('timeGridWeek'); return; }
    // Wochenansicht ist am Handy nicht lesbar, Dreitagesansicht am Desktop
    // zu wenig – beim Wechsel der Gerätebreite passend umschalten.
    if (phone && this.view() === 'timeGridWeek') this.setView('timeGridThreeDay');
    if (!phone && this.view() === 'timeGridThreeDay') this.setView('timeGridWeek');
  };

  // ---- Steuerung --------------------------------------------------------
  /**
   * Seitenleiste umschalten.
   *
   * FullCalendar berechnet Spaltenbreiten einmalig und merkt sich das
   * Ergebnis. Klappt die Seitenleiste weg, wird der Bereich zwar breiter,
   * das Raster behält aber die alte Breite – rechts bleibt eine leere
   * Fläche. `updateSize()` nach dem Umschalten rechnet neu.
   */
  toggleSidebar(): void {
    this.sidebarOpen.set(!this.sidebarOpen());
    setTimeout(() => this.calendar?.updateSize(), 0);
  }
  today(): void { this.calendar?.today(); }
  prev(): void { this.calendar?.prev(); }
  next(): void { this.calendar?.next(); }
  goTo(date: Date): void { this.calendar?.gotoDate(date); }
  setView(v: GView): void { this.view.set(v); this.calendar?.changeView(v); }
  viewLabel(): string {
    const labels: Record<GView, string> = {
      timeGridDay: 'Tag', timeGridThreeDay: '3 Tage', timeGridWeek: 'Woche',
      dayGridMonth: 'Monat', listWeek: 'Woche', listMonth: 'Monat'
    };
    return labels[this.view()] || 'Ansicht';
  }

  // ---- Daten ------------------------------------------------------------
  private iso(d: Date): string {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }
  private load(): void {
    forkJoin({
      events: this.api.list('calendar_events', '', 1000),
      projects: this.api.list('projects', '', 300),
      sites: this.api.list('construction_sites', '', 300),
      cats: this.api.list('calendar_categories', '', 200),
      users: this.api.list('users', '', 300)
    }).subscribe({
      next: ({ events, projects, sites, cats, users }) => {
        this.users.set(users.items || []);
        this.events.set(events.items || []);
        this.projects.set(projects.items || []);
        this.sites.set(sites.items || []);
        const list = (cats.items || []) as unknown as CalendarCategory[];
        this.categories.set(this.mergeCategories(list));
        this.refreshEvents();
      },
      error: () => this.snack.open('Kalender konnte nicht geladen werden.', 'OK', { duration: 3000 })
    });
  }
  private reload(): void {
    this.api.list('calendar_events', '', 1000).subscribe({
      next: (r) => { this.events.set(r.items || []); this.refreshEvents(); },
      error: () => {}
    });
  }
  /** Termine ohne Kolonne aus der Planung – schreibgeschützt. */
  private loadPlanningAppointments(from: Date, to: Date): void {
    this.api.planningAssignments(this.iso(from), this.iso(to)).subscribe({
      next: (res) => {
        this.planningEvents.set((res.items || [])
          .filter((a) => !a['crew_id'])
          .map((a) => ({
            id: `plan-${a['id']}`,
            title: String(a['title'] || 'Termin'),
            start_at: a['start_at'], end_at: a['end_at'], all_day: 0,
            event_type: GcalComponent.PLANNING_CATEGORY.code,
            color: GcalComponent.PLANNING_CATEGORY.color,
            location: a['site_name'] || a['project_name'] || '',
            notes: a['notes'] || '', project_id: a['project_id'] || null,
            _locked: true
          } as GenericRecord)));
        this.refreshEvents();
      },
      error: () => this.planningEvents.set([])
    });
  }

  private allRecords(): GenericRecord[] { return [...this.events(), ...this.planningEvents()]; }
  private recordFor(id: string): GenericRecord | undefined {
    return this.allRecords().find((e) => String(e['id']) === String(id));
  }

  /** Termine an FullCalendar übergeben (Filter: Kategorie und Suche). */
  refreshEvents(): void {
    if (!this.calendar) return;
    const hidden = this.hiddenCategories();
    const q = this.search.trim().toLowerCase();
    const items: EventInput[] = this.allRecords()
      .filter((e) => !hidden.has(String(e['event_type'] || '')))
      .filter((e) => !q || [e['title'], e['location'], e['notes'], e['description']].join(' ').toLowerCase().includes(q))
      .map((e) => {
        const bg = this.colorOf(e);
        return {
          id: String(e['id']),
          title: String(e['title'] || 'Termin'),
          // Der Ort steht als Zusatzangabe zur Verfuegung und wird unter dem
          // Titel gezeigt, sofern der Block hoch genug ist.
          extendedProps: {
            assignee: this.assigneeName(e),
            location: String(e['location'] || ''),
            locked: !!e['_locked'],
            category: this.categoryLabel(e)
          },
          start: String(e['start_at'] || '').replace(' ', 'T'),
          end: e['end_at'] ? String(e['end_at']).replace(' ', 'T') : undefined,
          allDay: !!e['all_day'],
          backgroundColor: bg,
          borderColor: bg,
          textColor: this.textColorFor(bg),
          editable: this.canWrite && !e['_locked'],
          classNames: e['_locked'] ? ['fc-locked'] : []
        } as EventInput;
      });
    this.calendar.removeAllEvents();
    this.calendar.addEventSource(items);
  }

  private persistTimes(record: GenericRecord, start: Date | null, end: Date | null, revert: () => void): void {
    if (!start) { revert(); return; }
    const fmt = (d: Date) => `${this.iso(d)} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}:00`;
    const payload: GenericRecord = { start_at: fmt(start), end_at: fmt(end || start) };
    this.api.update('calendar_events', String(record['id']), payload).subscribe({
      next: () => {
        this.events.update((list) => list.map((x) => String(x['id']) === String(record['id']) ? { ...x, ...payload } : x));
        this.snack.open('Termin verschoben.', 'OK', { duration: 1800 });
      },
      error: () => { this.snack.open('Verschieben fehlgeschlagen.', 'OK', { duration: 3000 }); revert(); }
    });
  }

  timeRange(e: GenericRecord): string {
    const fmt = (v: unknown) => { const m = String(v ?? '').match(/(\d{2}):(\d{2})/); return m ? `${m[1]}:${m[2]}` : ''; };
    const a = fmt(e['start_at']); const b = fmt(e['end_at']);
    return b ? `${a} – ${b}` : a;
  }

  // ---- Kategorien -------------------------------------------------------
  isActive(code: string): boolean { return !this.hiddenCategories().has(code); }
  toggleCategory(code: string): void {
    const set = new Set(this.hiddenCategories());
    set.has(code) ? set.delete(code) : set.add(code);
    this.hiddenCategories.set(set);
    this.refreshEvents();
  }
  colorOf(e: GenericRecord): string {
    if (e['color']) return String(e['color']);
    return this.categories().find((c) => c.code === String(e['event_type'] || ''))?.color || '#1a73e8';
  }
  categoryLabel(e: GenericRecord): string {
    return this.categories().find((c) => c.code === String(e['event_type'] || ''))?.label || 'Termin';
  }
  /** Lesbare Schriftfarbe zur Hintergrundfarbe (WCAG-Helligkeit). */
  textColorFor(background: string): string {
    const hex = String(background || '').replace('#', '').trim();
    if (hex.length !== 3 && hex.length !== 6) return '#ffffff';
    const full = hex.length === 3 ? hex.split('').map((c) => c + c).join('') : hex;
    const ch = (v: number) => { const c = v / 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
    const l = 0.2126 * ch(parseInt(full.slice(0, 2), 16)) + 0.7152 * ch(parseInt(full.slice(2, 4), 16)) + 0.0722 * ch(parseInt(full.slice(4, 6), 16));
    return (1.05 / (l + 0.05)) >= ((l + 0.05) / 0.10) ? '#ffffff' : '#202124';
  }

  // ---- Mini-Monat -------------------------------------------------------
  miniTitle(): string { return this.miniAnchor().toLocaleDateString('de-DE', { month: 'long', year: 'numeric' }); }
  shiftMini(delta: number): void { const d = new Date(this.miniAnchor()); d.setMonth(d.getMonth() + delta); this.miniAnchor.set(d); }
  readonly miniDays = computed(() => {
    const base = this.miniAnchor();
    const first = new Date(base.getFullYear(), base.getMonth(), 1, 12);
    const start = new Date(first); start.setDate(first.getDate() - ((first.getDay() + 6) % 7));
    const today = this.iso(new Date());
    const counts = new Map<string, number>();
    this.allRecords().forEach((e) => {
      const key = String(e['start_at'] || '').slice(0, 10);
      counts.set(key, (counts.get(key) || 0) + 1);
    });
    return Array.from({ length: 42 }, (_, i) => {
      const d = new Date(start); d.setDate(start.getDate() + i);
      const iso = this.iso(d);
      return { iso, date: d, day: d.getDate(), inMonth: d.getMonth() === base.getMonth(), isToday: iso === today, count: counts.get(iso) || 0 };
    });
  });

  // ---- Detailfenster ----------------------------------------------------
  closeDetail(): void { this.detail.set(null); }
  editDetail(): void { const d = this.detail(); this.closeDetail(); if (d) this.openDialog(d, new Date(String(d['start_at']).replace(' ', 'T')), null); }
  deleteDetail(): void {
    const d = this.detail(); if (!d) return;
    this.closeDetail();
    this.dialog.open(ConfirmDialogComponent, {
      width: 'min(440px, 94vw)', panelClass: 'ew-dialog',
      data: { title: 'Termin löschen?', message: `„${d['title'] || 'Termin'}" wird entfernt.`, confirmText: 'Löschen', cancelText: 'Abbrechen', icon: 'delete', tone: 'warn' }
    }).afterClosed().subscribe((ok) => {
      if (!ok) return;
      this.api.remove('calendar_events', String(d['id'])).subscribe({
        next: () => { this.snack.open('Termin gelöscht.', 'OK', { duration: 2200 }); this.reload(); },
        error: () => this.snack.open('Termin konnte nicht gelöscht werden.', 'OK', { duration: 3000 })
      });
    });
  }
  detailWhen(e: GenericRecord): string {
    const d = new Date(String(e['start_at'] || '').replace(' ', 'T'));
    const day = isNaN(d.getTime()) ? '' : d.toLocaleDateString('de-DE', { weekday: 'long', day: 'numeric', month: 'long' });
    if (e['all_day']) return `${day} · ganztägig`;
    const fmt = (v: unknown) => { const m = String(v ?? '').match(/(\d{2}):(\d{2})/); return m ? `${m[1]}:${m[2]}` : ''; };
    return `${day} · ${fmt(e['start_at'])} – ${fmt(e['end_at'])}`;
  }
  recurrenceLabel(e: GenericRecord): string {
    const rule = String(e['recurrence_rule'] || '');
    const map: Record<string, string> = { DAILY: 'Täglich', WEEKLY: 'Wöchentlich', BIWEEKLY: 'Alle 2 Wochen', MONTHLY: 'Monatlich' };
    const count = rule.match(/COUNT=(\d+)/)?.[1];
    return `${map[rule.split(';')[0]] || 'Serie'}${count ? ` · ${count} Termine` : ''}`;
  }
  /** Name der zuständigen Person zu einem Termin. */
  assigneeName(e: GenericRecord): string {
    const id = String(e['assigned_user_id'] || '');
    if (!id) return '';
    const u = this.users().find((x) => String(x['id']) === id);
    if (!u) return '';
    const name = [u['first_name'], u['last_name']].map((v) => String(v ?? '').trim()).filter(Boolean).join(' ');
    return name || String(u['name'] || u['email'] || '');
  }
  projectLabel(id: unknown): string {
    const p = this.projects().find((x) => String(x['id']) === String(id ?? ''));
    return p ? [p['project_no'], p['name']].filter(Boolean).join(' · ') : 'Bauakte öffnen';
  }

  // ---- Anlegen und Bearbeiten -------------------------------------------
  createEvent(): void {
    const start = this.calendar?.getDate() || new Date();
    this.openDialog(null, start, 9, null);
  }
  private openDialog(event: GenericRecord | null, date: Date, hour: number | null, end?: Date | null): void {
    const ref = this.dialog.open(GcalEventDialogComponent, {
      width: 'min(560px, 96vw)', maxWidth: '96vw', panelClass: ['ew-dialog', 'gcal-dialog'], autoFocus: false,
      data: {
        event,
        categories: this.categories().filter((c) => c.code !== GcalComponent.PLANNING_CATEGORY.code),
        projects: this.projects(), sites: this.sites(), users: this.users(),
        date: this.iso(date),
        hour: hour ?? date.getHours(),
        canDelete: this.canDelete
      }
    });
    ref.afterClosed().subscribe((result: GcalEventResult | undefined) => {
      if (!result) return;
      if (result.deleted && event?.['id']) { this.detail.set(event); this.deleteDetail(); return; }
      if (!result.payload) return;
      if (event?.['id']) {
        this.api.update('calendar_events', String(event['id']), result.payload).subscribe({
          next: () => { this.snack.open('Termin gespeichert.', 'OK', { duration: 2000 }); this.reload(); },
          error: () => this.snack.open('Termin konnte nicht gespeichert werden.', 'OK', { duration: 3000 })
        });
        return;
      }
      this.createSeries(result);
    });
  }
  /** Serientermine werden als eigenständige Einträge geschrieben. */
  private createSeries(result: GcalEventResult): void {
    const base = result.payload as GenericRecord;
    const repeat = result.repeat;
    if (!repeat) {
      this.api.create('calendar_events', base).subscribe({
        next: () => { this.snack.open('Termin angelegt.', 'OK', { duration: 2000 }); this.reload(); },
        error: () => this.snack.open('Termin konnte nicht angelegt werden.', 'OK', { duration: 3000 })
      });
      return;
    }
    const seriesId = 'ser-' + Date.now().toString(36);
    const rule = `${repeat.freq.toUpperCase()};COUNT=${repeat.count}`;
    const shift = (value: string, index: number): string => {
      const d = new Date(String(value).replace(' ', 'T'));
      if (repeat.freq === 'daily') d.setDate(d.getDate() + index);
      else if (repeat.freq === 'weekly') d.setDate(d.getDate() + index * 7);
      else if (repeat.freq === 'biweekly') d.setDate(d.getDate() + index * 14);
      else d.setMonth(d.getMonth() + index);
      const p = (n: number) => String(n).padStart(2, '0');
      return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:00`;
    };
    forkJoin(Array.from({ length: repeat.count }, (_, i) => this.api.create('calendar_events', {
      ...base, start_at: shift(String(base['start_at']), i), end_at: shift(String(base['end_at']), i),
      recurrence_rule: rule, external_id: seriesId
    }))).subscribe({
      next: () => { this.snack.open(`Serie mit ${repeat.count} Terminen angelegt.`, 'OK', { duration: 2800 }); this.reload(); },
      error: () => { this.snack.open('Die Serie konnte nicht vollständig angelegt werden.', 'OK', { duration: 3400 }); this.reload(); }
    });
  }
  /**
   * Kategorien zusammenführen.
   *
   * FRÜHERER FEHLER: Es hieß `list.length ? list : DEFAULT_CATEGORIES` – die
   * Grundkategorien erschienen also nur, solange die Datenbank leer war.
   * Sobald jemand eine eigene anlegte, lieferte die API genau diese eine und
   * alle Vorgaben verschwanden.
   *
   * Jetzt gilt: gespeicherte Kategorien zuerst, danach die Vorgaben, deren
   * Code noch nicht vergeben ist.
   */
  private mergeCategories(list: CalendarCategory[]): CalendarCategory[] {
    const known = new Set(list.map((c) => String(c.code)));
    const defaults = DEFAULT_CATEGORIES.filter((c) => !known.has(String(c.code)));
    return [...list, ...defaults, GcalComponent.PLANNING_CATEGORY];
  }

  /** Die Sammelkategorie der Planung ist nicht bearbeitbar. */
  isSystemCategory(c: CalendarCategory): boolean {
    return c.code === GcalComponent.PLANNING_CATEGORY.code;
  }
  /** Vorhandene Kategorien neu laden. */
  private reloadCategories(): void {
    this.api.list('calendar_categories', '', 200).subscribe({
      next: (r) => {
        const list = (r.items || []) as unknown as CalendarCategory[];
        this.categories.set(this.mergeCategories(list));
        this.refreshEvents();
      },
      error: () => {}
    });
  }
  /** Kennung des Datensatzes zu einer Kategorie ermitteln. */
  private categoryId(c: CalendarCategory): string {
    return String((c as unknown as GenericRecord)['id'] || '');
  }
  editCategory(c: CalendarCategory): void {
    if (!this.canWrite) return;
    const id = this.categoryId(c);
    if (!id) {
      // Vorgabe ohne eigenen Datensatz: beim Bearbeiten als eigene Kategorie
      // übernehmen, damit sie danach ganz normal änderbar ist.
      this.adoptDefault(c);
      return;
    }
    this.dialog.open(CalendarCategoryDialogComponent, {
      width: 'min(460px, 94vw)', panelClass: 'ew-dialog', autoFocus: false,
      data: { category: c }
    }).afterClosed().subscribe((payload: GenericRecord | undefined) => {
      if (!payload) return;
      this.api.update('calendar_categories', id, payload).subscribe({
        next: () => { this.snack.open('Kalender gespeichert.', 'OK', { duration: 2200 }); this.reloadCategories(); },
        error: () => this.snack.open('Kalender konnte nicht gespeichert werden.', 'OK', { duration: 3000 })
      });
    });
  }
  /**
   * Eine Grundkategorie in einen echten Datensatz überführen.
   * Die Vorgaben stehen im Code, nicht in der Datenbank – beim ersten
   * Bearbeiten werden sie angelegt.
   */
  private adoptDefault(c: CalendarCategory): void {
    this.dialog.open(CalendarCategoryDialogComponent, {
      width: 'min(460px, 94vw)', panelClass: 'ew-dialog', autoFocus: false,
      data: { category: c }
    }).afterClosed().subscribe((payload: GenericRecord | undefined) => {
      if (!payload) return;
      this.api.create('calendar_categories', payload).subscribe({
        next: () => { this.snack.open('Kalender gespeichert.', 'OK', { duration: 2200 }); this.reloadCategories(); },
        error: () => this.snack.open('Kalender konnte nicht gespeichert werden.', 'OK', { duration: 3000 })
      });
    });
  }

  deleteCategory(c: CalendarCategory): void {
    if (!this.canWrite) return;
    const id = this.categoryId(c);
    if (!id) {
      this.snack.open('Grundkategorien lassen sich nicht löschen. Bearbeiten und umbenennen ist möglich.', 'OK', { duration: 4200 });
      return;
    }
    // Wie viele Termine hängen daran? Das gehört in die Rückfrage.
    const used = this.events().filter((e) => String(e['event_type'] || '') === c.code).length;
    const message = used
      ? `„${c.label}" wird entfernt. ${used} Termin(e) verlieren damit ihre Zuordnung – sie bleiben erhalten und erscheinen ohne Farbe.`
      : `„${c.label}" wird entfernt.`;
    this.dialog.open(ConfirmDialogComponent, {
      width: 'min(480px, 94vw)', panelClass: 'ew-dialog',
      data: { title: 'Kalender löschen?', message, confirmText: 'Löschen', cancelText: 'Abbrechen', icon: 'delete', tone: 'warn' }
    }).afterClosed().subscribe((ok) => {
      if (!ok) return;
      this.api.remove('calendar_categories', id).subscribe({
        next: () => { this.snack.open('Kalender gelöscht.', 'OK', { duration: 2200 }); this.reloadCategories(); },
        error: () => this.snack.open('Kalender konnte nicht gelöscht werden.', 'OK', { duration: 3000 })
      });
    });
  }

  addCategory(): void {
    if (!this.canWrite) return;
    this.dialog.open(CalendarCategoryDialogComponent, { width: 'min(460px, 94vw)', panelClass: 'ew-dialog', autoFocus: false, data: {} })
      .afterClosed().subscribe((payload: GenericRecord | undefined) => {
        if (!payload) return;
        this.api.create('calendar_categories', payload).subscribe({
          next: () => { this.snack.open('Kalender angelegt.', 'OK', { duration: 2200 }); this.reloadCategories(); },
          error: () => this.snack.open('Kalender konnte nicht angelegt werden.', 'OK', { duration: 3000 })
        });
      });
  }
}
