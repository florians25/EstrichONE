import { Component, Inject, inject, signal } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

type CalendarView = 'Tag' | 'Woche' | 'Monat' | 'Agenda';

export interface CalendarCategory {
  id?: string;
  code: string;
  label: string;
  color: string;
  icon: string;
  is_active?: number | boolean;
  sort_order?: number;
}

export const DEFAULT_CATEGORIES: CalendarCategory[] = [
  { code: 'site_visit', label: 'Erstbegehung', color: '#1a73e8', icon: 'location_on', sort_order: 10 },
  { code: 'installation', label: 'Ausführung', color: '#188038', icon: 'construction', sort_order: 20 },
  { code: 'measurement', label: 'Messung / QM', color: '#f9ab00', icon: 'science', sort_order: 30 },
  { code: 'acceptance', label: 'Abnahme', color: '#9334e6', icon: 'fact_check', sort_order: 40 },
  { code: 'delivery', label: 'Lieferung', color: '#b06000', icon: 'local_shipping', sort_order: 50 },
  { code: 'meeting', label: 'Besprechung', color: '#0097a7', icon: 'groups', sort_order: 60 },
  { code: 'office', label: 'Interner Termin', color: '#5f6368', icon: 'business_center', sort_order: 70 }
];

export interface EventDialogData {
  projects: GenericRecord[];
  sites: GenericRecord[];
  categories: CalendarCategory[];
  initialDate: string;
  initialHour?: number | null;
  event?: GenericRecord | null;
}

interface CategoryDialogData {
  category?: CalendarCategory | null;
}

@Component({
  selector: 'ew-event-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, NgClass, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule],
  template: `
    <h2 mat-dialog-title><mat-icon>event</mat-icon>{{ data.event ? 'Termin bearbeiten' : 'Termin anlegen' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid dialog-form calendar-event-dialog-grid">
        <mat-form-field class="wide" appearance="outline">
          <mat-label>Auftrag / Projekt</mat-label>
          <mat-select formControlName="project_id" (selectionChange)="projectChanged()" panelClass="ew-search-select-panel">
            <mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Auftrag suchen" [value]="projectSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="projectSearch.set($any($event.target).value)"></mat-option>
            <mat-option value="">Ohne Auftragsbezug</mat-option>
            @for (project of filteredProjects(); track project['id']) {
              <mat-option [value]="project['id']">{{ project['project_no'] || 'Auftrag' }} · {{ project['name'] }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field class="wide" appearance="outline">
          <mat-label>Baustelle</mat-label>
          <mat-select formControlName="site_id" panelClass="ew-search-select-panel">
            <mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Baustelle suchen" [value]="siteSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="siteSearch.set($any($event.target).value)"></mat-option>
            <mat-option value="">Ohne Baustellenbezug</mat-option>
            @for (site of filteredSites(); track site['id']) {
              <mat-option [value]="site['id']">{{ site['site_no'] || 'Baustelle' }} · {{ site['name'] }} @if (site['city']) { · {{ site['city'] }} }</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field class="wide" appearance="outline"><mat-label>Titel *</mat-label><input matInput formControlName="title" placeholder="Terminbezeichnung"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Datum *</mat-label><input matInput type="date" formControlName="date"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Ganztägig</mat-label><mat-select formControlName="all_day"><mat-option [value]="true">Ja</mat-option><mat-option [value]="false">Nein</mat-option></mat-select></mat-form-field>

        @if (!form.value.all_day) {
          <mat-form-field appearance="outline"><mat-label>Von</mat-label><input matInput type="time" formControlName="start_time"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Bis</mat-label><input matInput type="time" formControlName="end_time"></mat-form-field>
        }

        <mat-form-field appearance="outline">
          <mat-label>Kategorie / Kalender</mat-label>
          <mat-select formControlName="event_type">
            @for (category of data.categories; track category.code) {
              <mat-option [value]="category.code"><span class="select-color-dot" [style.background]="category.color"></span>{{ category.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Sichtbarkeit</mat-label>
          <mat-select formControlName="visibility">
            <mat-option value="intern">Intern</mat-option>
            <mat-option value="kunde">Kunde</mat-option>
            <mat-option value="subunternehmer">Subunternehmer</mat-option>
            <mat-option value="öffentlich">Öffentlich</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Erinnerung</mat-label>
          <mat-select formControlName="reminder_minutes">
            <mat-option [value]="0">Keine</mat-option>
            <mat-option [value]="15">15 Minuten vorher</mat-option>
            <mat-option [value]="60">1 Stunde vorher</mat-option>
            <mat-option [value]="1440">1 Tag vorher</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field class="wide" appearance="outline"><mat-label>Beschreibung</mat-label><textarea matInput rows="4" formControlName="description"></textarea></mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end" class="event-dialog-actions">
      @if (data.event) { <button mat-button class="danger-soft" (click)="delete()"><mat-icon>delete</mat-icon>Löschen</button> }
      <span class="spacer"></span>
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="save()"><mat-icon>save</mat-icon> Speichern</button>
    </mat-dialog-actions>
  `
})
export class EventDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<EventDialogComponent>);
  readonly projectSearch = signal('');
  readonly siteSearch = signal('');

  readonly form = this.fb.group({
    project_id: [''],
    site_id: [''],
    title: ['', Validators.required],
    date: ['', Validators.required],
    all_day: [false],
    start_time: ['07:00'],
    end_time: ['16:00'],
    event_type: ['installation'],
    visibility: ['intern'],
    reminder_minutes: [1440],
    description: ['']
  });

  constructor(@Inject(MAT_DIALOG_DATA) public data: EventDialogData) {
    const event = data.event;
    const start = event?.['start_at'] ? new Date(String(event['start_at']).replace(' ', 'T')) : null;
    const end = event?.['end_at'] ? new Date(String(event['end_at']).replace(' ', 'T')) : null;
    const hour = data.initialHour ?? 7;
    this.form.patchValue({
      project_id: event?.['project_id'] ?? '',
      site_id: event?.['site_id'] ?? '',
      title: event?.['title'] ?? 'Neuer Termin',
      date: start ? this.isoDate(start) : data.initialDate,
      all_day: Boolean(Number(event?.['all_day'] ?? 0)),
      start_time: start ? this.hm(start) : `${String(hour).padStart(2, '0')}:00`,
      end_time: end ? this.hm(end) : `${String(Math.min(hour + 1, 18)).padStart(2, '0')}:00`,
      event_type: event?.['event_type'] ?? data.categories[0]?.code ?? 'installation',
      visibility: event?.['visibility'] ?? 'intern',
      reminder_minutes: Number(event?.['reminder_minutes'] ?? 1440),
      description: event?.['description'] ?? ''
    });
  }

  filteredProjects(): GenericRecord[] {
    return this.filterRecords(this.data.projects, this.projectSearch(), ['project_no','name','city','zip']);
  }

  filteredSites(): GenericRecord[] {
    const projectId = this.form.value.project_id;
    const scoped = this.data.sites.filter((site) => !projectId || site['project_id'] === projectId);
    return this.filterRecords(scoped, this.siteSearch(), ['site_no','name','address','city','zip']);
  }

  private filterRecords(items: GenericRecord[], query: string, fields: string[]): GenericRecord[] {
    const needle = this.normalize(query);
    if (!needle) return items;
    return items.filter((item) => this.normalize(fields.map((field) => item[field] ?? '').join(' ')).includes(needle));
  }

  private normalize(value: unknown): string {
    return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
  }

  projectChanged(): void {
    const sites = this.filteredSites();
    if (!sites.some((site) => site['id'] === this.form.value.site_id)) {
      this.form.patchValue({ site_id: sites[0]?.['id'] ?? '' });
    }
  }

  save(): void {
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    const date = value.date || this.isoDate(new Date());
    const start = value.all_day ? `${date} 00:00:00` : `${date} ${value.start_time || '07:00'}:00`;
    const end = value.all_day ? `${date} 23:59:00` : `${date} ${value.end_time || '16:00'}:00`;
    const category = this.data.categories.find((item) => item.code === value.event_type) ?? this.data.categories[0];
    this.ref.close({
      action: 'save',
      payload: {
        project_id: value.project_id || null,
        site_id: value.site_id || null,
        title: value.title,
        description: value.description,
        start_at: start,
        end_at: end,
        all_day: value.all_day,
        event_type: value.event_type,
        visibility: value.visibility,
        reminder_minutes: value.reminder_minutes,
        color: category?.color || '#1a73e8',
        external_provider: 'none'
      }
    });
  }

  delete(): void { this.ref.close({ action: 'delete' }); }
  private isoDate(date: Date): string { return date.toISOString().slice(0, 10); }
  private hm(date: Date): string { return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`; }
}

@Component({
  selector: 'ew-calendar-category-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title><mat-icon>palette</mat-icon>{{ data.category ? 'Kategorie bearbeiten' : 'Neue Kalender-Kategorie' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid dialog-form">
        <mat-form-field appearance="outline"><mat-label>Name *</mat-label><input matInput formControlName="label" placeholder="z. B. Estrich einbringen"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Interner Code *</mat-label><input matInput formControlName="code" placeholder="z. B. estrich_einbau"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Farbe</mat-label><input matInput type="color" formControlName="color"></mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Icon</mat-label>
          <mat-select formControlName="icon">
            <mat-option value="event">Termin</mat-option>
            <mat-option value="construction">Ausführung</mat-option>
            <mat-option value="groups">Team</mat-option>
            <mat-option value="science">QM</mat-option>
            <mat-option value="local_shipping">Lieferung</mat-option>
            <mat-option value="fact_check">Abnahme</mat-option>
            <mat-option value="warning">Wichtig</mat-option>
          </mat-select>
        </mat-form-field>
      </form>
      <p class="dialog-help-line"><mat-icon>info</mat-icon>Die Kategorie steht danach direkt im Termin-Dialog und in der Kalender-Legende zur Verfügung.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="save()"><mat-icon>save</mat-icon>Speichern</button>
    </mat-dialog-actions>
  `
})
export class CalendarCategoryDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<CalendarCategoryDialogComponent>);
  readonly form = this.fb.group({
    label: ['', Validators.required],
    code: ['', Validators.required],
    color: ['#1a73e8', Validators.required],
    icon: ['event']
  });

  constructor(@Inject(MAT_DIALOG_DATA) public data: CategoryDialogData) {
    if (data.category) {
      this.form.patchValue({
        label: data.category.label,
        code: data.category.code,
        color: data.category.color,
        icon: data.category.icon || 'event'
      });
    }
    this.form.controls.label.valueChanges.subscribe((value) => {
      if (this.data.category || this.form.controls.code.dirty) return;
      this.form.controls.code.setValue(this.slug(value || ''), { emitEvent: false });
    });
  }

  save(): void {
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    this.ref.close({
      code: this.slug(value.code || value.label || 'termin'),
      label: value.label,
      color: value.color || '#1a73e8',
      icon: value.icon || 'event',
      is_active: 1,
      sort_order: 100
    });
  }

  private slug(value: string): string {
    return String(value || 'termin').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '') || 'termin';
  }
}

@Component({
  standalone: true,
  imports: [DatePipe, NgClass, MatCardModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSnackBarModule, MatDialogModule, MatChipsModule, MatTooltipModule],
  template: `
    <section class="google-shell calendar-pro-shell">
      <header class="google-calendar-topbar calendar-pro-topbar">
        <button mat-flat-button color="primary" class="create-btn" (click)="openEventDialog()"><mat-icon>add</mat-icon>Erstellen</button>
        <button mat-stroked-button (click)="today()">Heute</button>
        <button mat-icon-button (click)="shift(-1)" aria-label="Zurück"><mat-icon>chevron_left</mat-icon></button>
        <button mat-icon-button (click)="shift(1)" aria-label="Weiter"><mat-icon>chevron_right</mat-icon></button>
        <h1>{{ titleRange() }}</h1>
        <span class="spacer"></span>
        <button mat-stroked-button (click)="printCalendar()"><mat-icon>picture_as_pdf</mat-icon>PDF</button>
        <div class="view-switch google-view-switch">
          @for (view of views; track view) { <button mat-stroked-button [class.active]="view === activeView()" (click)="activeView.set(view)">{{ view }}</button> }
        </div>
      </header>

      <section class="google-calendar-layout calendar-pro-layout">
        <aside class="calendar-sidebar-card calendar-pro-sidebar">
          <button mat-flat-button color="primary" class="mobile-create-wide" (click)="openEventDialog()"><mat-icon>add</mat-icon>Termin anlegen</button>
          <div class="mini-month">
            <div class="mini-head"><button mat-icon-button (click)="shift(-1)"><mat-icon>chevron_left</mat-icon></button><strong>{{ currentDate() | date:'MMMM y' }}</strong><button mat-icon-button (click)="shift(1)"><mat-icon>chevron_right</mat-icon></button></div>
            <div class="mini-grid">
              @for (dayName of dayNames; track dayName) { <span class="mini-dow">{{ dayName }}</span> }
              @for (day of monthDays(); track day.key) {
                <button class="mini-day" [class.outside]="!day.inMonth" [class.today]="isToday(day.date)" [class.selected]="sameDate(day.date, currentDate())" (click)="pickDate(day.date)">{{ day.date.getDate() }}</button>
              }
            </div>
          </div>

          <mat-form-field appearance="outline">
            <mat-label>Projektfilter</mat-label>
            <mat-select [value]="projectFilter()" (selectionChange)="projectFilter.set($event.value)" panelClass="ew-search-select-panel">
              <mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Auftrag suchen" [value]="sidebarProjectSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="sidebarProjectSearch.set($any($event.target).value)"></mat-option>
              <mat-option value="">Alle Projekte</mat-option>
              @for (project of sidebarProjects(); track project['id']) { <mat-option [value]="project['id']">{{ project['project_no'] || 'Auftrag' }} · {{ project['name'] }}</mat-option> }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Kategorie</mat-label>
            <mat-select [value]="categoryFilter()" (selectionChange)="categoryFilter.set($event.value)">
              <mat-option value="">Alle Kategorien</mat-option>
              @for (category of categories(); track category.code) { <mat-option [value]="category.code"><span class="select-color-dot" [style.background]="category.color"></span>{{ category.label }}</mat-option> }
            </mat-select>
          </mat-form-field>

          <div class="calendar-list-section">
            <h3>Auftragskalender</h3>
            @for (project of projects().slice(0, 8); track project['id']) {
              <button class="project-filter-row" [class.active]="projectFilter() === project['id']" (click)="projectFilter.set(projectFilter() === project['id'] ? '' : project['id'])"><span></span>{{ project['project_no'] || 'Auftrag' }} · {{ project['name'] }}</button>
            }
          </div>

          <div class="calendar-legend calendar-category-admin">
            <div class="legend-title-row"><h3>Kategorien & Farben</h3><button mat-icon-button matTooltip="Kategorie anlegen" (click)="openCategoryDialog()"><mat-icon>add</mat-icon></button></div>
            @for (category of categories(); track category.code) {
              <button class="legend-row category-row" [class.active]="categoryFilter() === category.code" (click)="categoryFilter.set(categoryFilter() === category.code ? '' : category.code)">
                <span class="legend-dot" [style.background]="category.color"></span><mat-icon>{{ category.icon || 'event' }}</mat-icon>{{ category.label }}
              </button>
            }
          </div>
        </aside>

        <mat-card class="google-calendar-card calendar-pro-card">
          @if (activeView() === 'Monat') {
            <section class="month-grid google-calendar google-month-pro">
              @for (dayName of dayNames; track dayName) { <div class="dow">{{ dayName }}</div> }
              @for (day of monthDays(); track day.key) {
                <div class="month-cell" [ngClass]="{ outside: !day.inMonth, today: isToday(day.date) }" (click)="openEventDialog(day.date)">
                  <span class="date-number">{{ day.date.getDate() }}</span>
                  @for (event of eventsForDate(day.date).slice(0, 5); track event['id']) {
                    <article class="event-chip calendar-color-chip" [style.border-left-color]="categoryColor(event)" [style.background]="softColor(event)" [style.color]="categoryColor(event)" (click)="openEventDialog(day.date, null, event); $event.stopPropagation()"><span class="event-dot" [style.background]="categoryColor(event)"></span><span>{{ event['title'] }}</span></article>
                  }
                  @if (eventsForDate(day.date).length > 5) { <small class="more-chip">+{{ eventsForDate(day.date).length - 5 }} weitere</small> }
                </div>
              }
            </section>
          }

          @if (activeView() === 'Woche') {
            <section class="week-calendar google-week google-week-pro">
              <div class="week-header-spacer"></div>
              @for (day of weekDays(); track day.key) { <div class="week-day-head" [ngClass]="{today: isToday(day.date)}"><b>{{ weekdayLabel(day.date) }}</b><span>{{ day.date | date:'dd' }}</span></div> }
              <div class="all-day-label">Ganztägig</div>
              @for (day of weekDays(); track day.key) {
                <div class="all-day-cell" (click)="openEventDialog(day.date)">
                  @for (event of allDayEvents(day.date); track event['id']) { <article class="all-day-event" [style.background]="softColor(event)" [style.color]="categoryColor(event)" (click)="openEventDialog(day.date, null, event); $event.stopPropagation()">{{ event['title'] }}</article> }
                </div>
              }
              @for (hour of hours; track hour) {
                <div class="hour-label">{{ hour }}:00</div>
                @for (day of weekDays(); track day.key) {
                  <div class="hour-cell parallel-hour-cell" (click)="openEventDialog(day.date, hour)">
                    <div class="parallel-stack" [style.grid-template-columns]="parallelGrid(day.date, hour)">
                      @for (item of timedEventColumns(day.date, hour); track item.event['id']) {
                        <article class="timed-event timed-event-pro" [style.border-left-color]="categoryColor(item.event)" [style.background]="softColor(item.event)" [style.color]="categoryColor(item.event)" (click)="openEventDialog(day.date, hour, item.event); $event.stopPropagation()">
                          <b>{{ item.event['title'] }}</b>
                          <small>{{ item.event['start_at'] | date:'HH:mm' }}–{{ item.event['end_at'] | date:'HH:mm' }} · {{ referenceLabel(item.event) }}</small>
                        </article>
                      }
                    </div>
                  </div>
                }
              }
            </section>
          }

          @if (activeView() === 'Tag') {
            <section class="day-calendar google-day google-day-pro">
              <h2>{{ currentDate() | date:'EEEE, dd. MMMM y' }}</h2>
              <div class="day-all-day"><strong>Ganztägig</strong><div>@for (event of allDayEvents(currentDate()); track event['id']) { <article class="all-day-event" [style.background]="softColor(event)" [style.color]="categoryColor(event)" (click)="openEventDialog(currentDate(), null, event)">{{ event['title'] }}</article> }</div></div>
              @for (hour of hours; track hour) {
                <div class="day-hour day-hour-pro" (click)="openEventDialog(currentDate(), hour)"><span>{{ hour }}:00</span><div class="parallel-stack" [style.grid-template-columns]="parallelGrid(currentDate(), hour)">@for (item of timedEventColumns(currentDate(), hour); track item.event['id']) { <article class="timed-event timed-event-pro" [style.border-left-color]="categoryColor(item.event)" [style.background]="softColor(item.event)" [style.color]="categoryColor(item.event)" (click)="openEventDialog(currentDate(), hour, item.event); $event.stopPropagation()"><b>{{ item.event['title'] }}</b><small>{{ referenceLabel(item.event) }} · {{ categoryLabel(item.event['event_type']) }}</small></article> }</div></div>
              }
            </section>
          }

          @if (activeView() === 'Agenda') {
            <section class="agenda-list google-agenda google-agenda-pro">
              @for (event of sortedEvents(); track event['id']) {
                <article class="agenda-item agenda-item-pro" [style.border-left-color]="categoryColor(event)" (click)="openEventDialog(toDate(event['start_at']), null, event)">
                  <mat-icon [style.color]="categoryColor(event)">{{ eventIcon(event) }}</mat-icon>
                  <div><b>{{ event['title'] }}</b><small>{{ agendaTime(event) }} · {{ referenceLabel(event) }} · {{ categoryLabel(event['event_type']) }}</small></div>
                </article>
              } @empty {
                <div class="empty-state"><mat-icon>event_busy</mat-icon><h3>Keine Termine im gewählten Filter</h3><p>Lege über „Erstellen“ einen neuen Termin oder eine neue Kategorie an.</p></div>
              }
            </section>
          }
        </mat-card>
      </section>
    </section>

    <button class="mobile-fab" mat-fab color="primary" (click)="openEventDialog()"><mat-icon>add</mat-icon></button>
  `
})
export class CalendarComponent {

  private readonly auth = inject(AuthService);
  /** Ohne Planungsrecht ist der Kalender eine reine Ansicht. */
  readonly canPlan = this.auth.canWrite('planning');
  readonly canDeletePlan = this.auth.canDelete('planning');  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  readonly views: CalendarView[] = ['Tag', 'Woche', 'Monat', 'Agenda'];
  readonly activeView = signal<CalendarView>('Woche');
  readonly currentDate = signal(new Date());
  readonly events = signal<GenericRecord[]>([]);
  readonly projects = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly categories = signal<CalendarCategory[]>(DEFAULT_CATEGORIES);
  readonly projectFilter = signal('');
  readonly categoryFilter = signal('');
  readonly sidebarProjectSearch = signal('');
  readonly hours = Array.from({ length: 16 }, (_, i) => i + 6);
  readonly dayNames = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];

  constructor() { this.load(); }

  load(): void {
    this.api.list('projects', '', 120).subscribe({ next: (response) => this.projects.set(response.items), error: () => this.projects.set([]) });
    this.api.list('construction_sites', '', 500).subscribe({ next: (response) => this.sites.set(response.items), error: () => this.sites.set([]) });
    this.api.list('calendar_categories', '', 200).subscribe({
      next: (response) => {
        const merged = this.mergeCategories(response.items as CalendarCategory[]);
        this.categories.set(merged);
        this.refreshEvents();
      },
      error: () => { this.categories.set(DEFAULT_CATEGORIES); this.refreshEvents(); }
    });
  }

  refreshEvents(): void {
    this.api.list('calendar_events', '', 1000).subscribe({
      next: (response) => this.events.set(this.normalize(response.items)),
      error: () => this.snack.open('Kalendertermine konnten nicht geladen werden', 'OK', { duration: 3200 })
    });
  }

  openEventDialog(date = this.currentDate(), hour: number | null = null, event: GenericRecord | null = null): void {
    const ref = this.dialog.open(EventDialogComponent, {
      width: 'min(780px, 96vw)',
      maxWidth: '96vw',
      panelClass: 'ew-dialog',
      data: { projects: this.projects(), sites: this.sites(), categories: this.categories(), initialDate: this.isoDate(date), initialHour: hour, event }
    });
    ref.afterClosed().subscribe((result?: { action: string; payload?: GenericRecord }) => {
      if (!result) return;
      if (result.action === 'delete' && event?.['id']) {
        this.api.remove('calendar_events', String(event['id'])).subscribe({
          next: () => { this.snack.open('Termin gelöscht', 'OK', { duration: 2200 }); this.refreshEvents(); },
          error: () => this.snack.open('Termin konnte nicht gelöscht werden', 'OK', { duration: 3200 })
        });
        return;
      }
      const payload = result.payload;
      if (!payload) return;
      const request = event?.['id'] ? this.api.update('calendar_events', String(event['id']), payload) : this.api.create('calendar_events', payload);
      request.subscribe({
        next: () => { this.snack.open('Termin gespeichert', 'OK', { duration: 2200 }); this.refreshEvents(); },
        error: () => this.snack.open('Termin konnte nicht gespeichert werden', 'OK', { duration: 3200 })
      });
    });
  }

  openCategoryDialog(category: CalendarCategory | null = null): void {
    const ref = this.dialog.open(CalendarCategoryDialogComponent, { width: 'min(560px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', data: { category } });
    ref.afterClosed().subscribe((payload?: CalendarCategory) => {
      if (!payload) return;
      const request = category?.id ? this.api.update('calendar_categories', category.id, payload) : this.api.create('calendar_categories', payload);
      request.subscribe({
        next: () => { this.snack.open('Kategorie gespeichert', 'OK', { duration: 2200 }); this.load(); },
        error: () => this.snack.open('Kategorie konnte nicht gespeichert werden. Bitte SQL/API-Hotfix hochladen.', 'OK', { duration: 4200 })
      });
    });
  }

  printCalendar(): void { window.print(); }
  today(): void { this.currentDate.set(new Date()); }
  shift(delta: number): void {
    const base = new Date(this.currentDate());
    if (this.activeView() === 'Monat') base.setMonth(base.getMonth() + delta);
    else if (this.activeView() === 'Tag') base.setDate(base.getDate() + delta);
    else base.setDate(base.getDate() + delta * 7);
    this.currentDate.set(base);
  }
  pickDate(date: Date): void { this.currentDate.set(date); this.activeView.set('Tag'); }
  titleRange(): string {
    if (this.activeView() === 'Tag') return this.currentDate().toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' });
    if (this.activeView() === 'Woche') {
      const days = this.weekDays();
      const first = days[0].date; const last = days[6].date;
      return `${first.getDate()}. ${this.monthLabel(first)} – ${last.getDate()}. ${this.monthLabel(last)} ${last.getFullYear()}`;
    }
    if (this.activeView() === 'Agenda') return 'Agenda · ' + this.monthLabel(this.currentDate()) + ' ' + this.currentDate().getFullYear();
    return `${this.monthLabel(this.currentDate())} ${this.currentDate().getFullYear()}`;
  }

  monthDays(): { date: Date; key: string; inMonth: boolean }[] {
    const base = this.currentDate();
    const first = new Date(base.getFullYear(), base.getMonth(), 1);
    const start = new Date(first);
    start.setDate(first.getDate() - ((first.getDay() + 6) % 7));
    return Array.from({ length: 42 }, (_, index) => {
      const date = new Date(start); date.setDate(start.getDate() + index);
      return { date, key: this.isoDate(date), inMonth: date.getMonth() === base.getMonth() };
    });
  }

  weekDays(): { date: Date; key: string }[] {
    const base = new Date(this.currentDate());
    const start = new Date(base);
    start.setDate(base.getDate() - ((base.getDay() + 6) % 7));
    return Array.from({ length: 7 }, (_, index) => { const date = new Date(start); date.setDate(start.getDate() + index); return { date, key: this.isoDate(date) }; });
  }

  eventsForDate(date: Date): GenericRecord[] {
    const key = this.isoDate(date);
    return this.filteredEvents().filter((event) => this.isoDate(this.toDate(event['start_at'])) === key).sort((a, b) => String(a['start_at']).localeCompare(String(b['start_at'])));
  }

  allDayEvents(date: Date): GenericRecord[] { return this.eventsForDate(date).filter((event) => Boolean(Number(event['all_day']))); }
  timedEvents(date: Date, hour: number): GenericRecord[] {
    return this.eventsForDate(date).filter((event) => !Boolean(Number(event['all_day'])) && this.toDate(event['start_at']).getHours() === hour);
  }
  timedEventColumns(date: Date, hour: number): { event: GenericRecord; index: number; total: number }[] {
    const items = this.timedEvents(date, hour);
    return items.map((event, index) => ({ event, index, total: items.length }));
  }
  parallelGrid(date: Date, hour: number): string {
    const count = Math.max(1, this.timedEvents(date, hour).length);
    return `repeat(${count}, minmax(0, 1fr))`;
  }
  sortedEvents(): GenericRecord[] { return [...this.filteredEvents()].sort((a, b) => String(a['start_at']).localeCompare(String(b['start_at']))); }
  filteredEvents(): GenericRecord[] {
    return this.events().filter((event) => {
      if (this.projectFilter() && event['project_id'] !== this.projectFilter()) return false;
      if (this.categoryFilter() && event['event_type'] !== this.categoryFilter()) return false;
      return true;
    });
  }
  sidebarProjects(): GenericRecord[] { return this.filterMainRecords(this.projects(), this.sidebarProjectSearch(), ['project_no','name','city','zip']); }
  filterMainRecords(items: GenericRecord[], query: string, fields: string[]): GenericRecord[] {
    const needle = this.normalizeMain(query);
    if (!needle) return items;
    return items.filter((item) => this.normalizeMain(fields.map((field) => item[field] ?? '').join(' ')).includes(needle));
  }
  normalizeMain(value: unknown): string { return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }
  isToday(date: Date): boolean { return this.isoDate(date) === this.isoDate(new Date()); }
  sameDate(a: Date, b: Date): boolean { return this.isoDate(a) === this.isoDate(b); }
  toDate(value: unknown): Date { return new Date(String(value ?? new Date().toISOString()).replace(' ', 'T')); }
  projectLabel(projectId: unknown): string { return this.projects().find((project) => project['id'] === projectId)?.['name'] ?? 'Allgemeiner Termin'; }
  siteLabel(siteId: unknown): string { return this.sites().find((site) => site['id'] === siteId)?.['name'] ?? ''; }
  referenceLabel(event: GenericRecord): string {
    return this.siteLabel(event['site_id']) || this.projectLabel(event['project_id']);
  }
  weekdayLabel(date: Date): string { return this.dayNames[(date.getDay() + 6) % 7]; }
  monthLabel(date: Date): string { return ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'][date.getMonth()]; }
  categoryLabel(type: unknown): string { return this.categories().find((item) => item.code === type)?.label || 'Termin'; }
  categoryColor(event: GenericRecord): string { return String(event['color'] || this.categories().find((item) => item.code === event['event_type'])?.color || '#1a73e8'); }
  softColor(event: GenericRecord): string { return this.hexToRgba(this.categoryColor(event), 0.12); }
  eventIcon(event: GenericRecord): string { return this.categories().find((item) => item.code === event['event_type'])?.icon || 'event'; }
  agendaTime(event: GenericRecord): string {
    const start = this.toDate(event['start_at']); const end = this.toDate(event['end_at']);
    if (Boolean(Number(event['all_day']))) return `${start.toLocaleDateString('de-DE')} · ganztägig`;
    return `${start.toLocaleDateString('de-DE')} · ${start.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}–${end.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`;
  }
  private normalize(items: GenericRecord[]): GenericRecord[] { return items.map((item) => ({ ...item, project_id: item['project_id'] ?? this.sites().find((site) => site['id'] === item['site_id'])?.['project_id'] ?? '' })); }
  private mergeCategories(rows: CalendarCategory[]): CalendarCategory[] {
    const custom = (rows || []).filter((row) => row && String(row.code || '').trim()).map((row) => ({ ...row, is_active: Number(row.is_active ?? 1), sort_order: Number(row.sort_order ?? 100) }));
    const map = new Map<string, CalendarCategory>();
    for (const item of DEFAULT_CATEGORIES) map.set(item.code, item);
    for (const item of custom) map.set(item.code, item);
    return [...map.values()].filter((item) => Number(item.is_active ?? 1) === 1).sort((a, b) => Number(a.sort_order ?? 100) - Number(b.sort_order ?? 100) || a.label.localeCompare(b.label));
  }
  private hexToRgba(hex: string, alpha: number): string {
    const clean = hex.replace('#', '');
    if (clean.length !== 6) return 'rgba(26,115,232,.12)';
    const r = parseInt(clean.slice(0, 2), 16); const g = parseInt(clean.slice(2, 4), 16); const b = parseInt(clean.slice(4, 6), 16);
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
  }
  private isoDate(date: Date): string { return date.toISOString().slice(0, 10); }
}
