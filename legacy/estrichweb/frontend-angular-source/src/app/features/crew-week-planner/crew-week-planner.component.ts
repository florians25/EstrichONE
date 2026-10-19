import { Component, Inject, inject, signal } from '@angular/core';
import { jsPDF } from 'jspdf';
import { DatePipe, NgClass } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { forkJoin } from 'rxjs';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { AssignmentNoteDialogComponent } from '../../shared/assignment-note-dialog.component';

interface AssignmentDialogData {
  projects: GenericRecord[];
  sites: GenericRecord[];
  crews: GenericRecord[];
  vehicles: GenericRecord[];
  machines: GenericRecord[];
  initialDate: string;
  crew?: GenericRecord | null;
  assignment?: GenericRecord | null;
}

@Component({
  selector: 'ew-assignment-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title><mat-icon>groups</mat-icon>{{ data.assignment ? 'Einplanung bearbeiten' : 'Kolonne einplanen' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid dialog-form">
        <mat-form-field class="wide" appearance="outline">
          <mat-label>Auftrag / Projekt *</mat-label>
          <mat-select formControlName="project_id" (selectionChange)="projectChanged()" panelClass="ew-search-select-panel">
            <mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Auftrag suchen" [value]="projectSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="projectSearch.set($any($event.target).value)"></mat-option>
            @for (project of filteredProjects(); track project['id']) {
              <mat-option [value]="project['id']">{{ project['project_no'] || 'Auftrag' }} · {{ project['name'] }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field class="wide" appearance="outline">
          <mat-label>Baustelle *</mat-label>
          <mat-select formControlName="site_id" panelClass="ew-search-select-panel">
            <mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Baustelle suchen" [value]="siteSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="siteSearch.set($any($event.target).value)"></mat-option>
            @for (site of filteredSites(); track site['id']) {
              <mat-option [value]="site['id']">{{ site['site_no'] || 'Baustelle' }} · {{ site['name'] }} @if (site['city']) { · {{ site['city'] }} }</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Kolonne *</mat-label><mat-select formControlName="crew_id" panelClass="ew-search-select-panel"><mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Kolonne suchen" [value]="crewSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="crewSearch.set($any($event.target).value)"></mat-option>@for (crew of filteredCrews(); track crew['id']) { <mat-option [value]="crew['id']">{{ crew['name'] }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Fahrzeug</mat-label><mat-select formControlName="vehicle_id" panelClass="ew-search-select-panel"><mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Fahrzeug suchen" [value]="vehicleSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="vehicleSearch.set($any($event.target).value)"></mat-option><mat-option value="">Ohne Fahrzeug</mat-option>@for (vehicle of filteredVehicles(); track vehicle['id']) { <mat-option [value]="vehicle['id']">{{ vehicleLabel(vehicle) }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Maschine</mat-label><mat-select formControlName="machine_id" panelClass="ew-search-select-panel"><mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Maschine suchen" [value]="machineSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="machineSearch.set($any($event.target).value)"></mat-option><mat-option value="">Ohne Maschine</mat-option>@for (machine of filteredMachines(); track machine['id']) { <mat-option [value]="machine['id']">{{ machine['name'] }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Datum *</mat-label><input matInput type="date" formControlName="date"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Von</mat-label><input matInput type="time" formControlName="start_time"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Bis</mat-label><input matInput type="time" formControlName="end_time"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Status</mat-label><mat-select formControlName="status"><mat-option value="new">Neu</mat-option><mat-option value="planned">Geplant</mat-option><mat-option value="in_progress">In Arbeit</mat-option><mat-option value="delayed">Verzögert</mat-option><mat-option value="defect">Mangel</mat-option><mat-option value="completed">Abgeschlossen</mat-option></mat-select></mat-form-field>
        <mat-form-field class="wide" appearance="outline"><mat-label>Einplanungstitel *</mat-label><input matInput formControlName="title" placeholder="Estrich EG · 280 m²"></mat-form-field>
        <mat-form-field class="wide" appearance="outline"><mat-label>Notizen</mat-label><textarea matInput rows="4" formControlName="notes"></textarea></mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" [disabled]="form.invalid" (click)="save()"><mat-icon>save</mat-icon>{{ data.assignment ? 'Aktualisieren' : 'Einplanen' }}</button>
    </mat-dialog-actions>
  `
})
export class AssignmentDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<AssignmentDialogComponent>);
  readonly projectSearch = signal('');
  readonly siteSearch = signal('');
  readonly crewSearch = signal('');
  readonly vehicleSearch = signal('');
  readonly machineSearch = signal('');

  readonly form = this.fb.group({
    project_id: ['', Validators.required],
    site_id: ['', Validators.required],
    crew_id: ['', Validators.required],
    vehicle_id: [''],
    machine_id: [''],
    date: ['', Validators.required],
    start_time: ['07:00'],
    end_time: ['16:00'],
    status: ['planned'],
    title: ['', Validators.required],
    notes: ['']
  });

  constructor(@Inject(MAT_DIALOG_DATA) public data: AssignmentDialogData) {
    const assignment = data.assignment || null;
    if (assignment) {
      const start = this.toDate(assignment['start_at']);
      const end = this.toDate(assignment['end_at']);
      this.form.patchValue({
        project_id: String(assignment['project_id'] || ''),
        site_id: String(assignment['site_id'] || ''),
        crew_id: String(assignment['crew_id'] || data.crew?.['id'] || ''),
        vehicle_id: String(assignment['vehicle_id'] || ''),
        machine_id: String(assignment['machine_id'] || ''),
        date: this.isoDate(start),
        start_time: this.timePart(start, '07:00'),
        end_time: this.timePart(end, '16:00'),
        status: String(assignment['status'] || 'planned'),
        title: String(assignment['title'] || 'Estricharbeiten'),
        notes: String(assignment['notes'] || '')
      });
      const project = data.projects.find((item) => String(item['id']) === String(assignment['project_id']));
      const site = data.sites.find((item) => String(item['id']) === String(assignment['site_id']));
      if (project) this.projectSearch.set([project['project_no'], project['name']].filter(Boolean).join(' '));
      if (site) this.siteSearch.set([site['site_no'], site['name'], site['city']].filter(Boolean).join(' '));
      return;
    }
    const project = data.projects[0];
    const sites = data.sites.filter((site) => !project || site['project_id'] === project['id']);
    this.form.patchValue({
      project_id: project?.['id'] ?? '',
      site_id: sites[0]?.['id'] ?? data.sites[0]?.['id'] ?? '',
      crew_id: data.crew?.['id'] ?? data.crews[0]?.['id'] ?? '',
      date: data.initialDate,
      title: project ? `Estricharbeiten · ${project['name']}` : 'Estricharbeiten'
    });
  }

  filteredProjects(): GenericRecord[] { return this.filterRecords(this.data.projects, this.projectSearch(), ['project_no','name','city','zip']); }
  filteredSites(): GenericRecord[] {
    const projectId = this.form.value.project_id;
    const scoped = this.data.sites.filter((site) => !projectId || site['project_id'] === projectId);
    return this.filterRecords(scoped, this.siteSearch(), ['site_no','name','address','city','zip']);
  }
  filteredCrews(): GenericRecord[] { return this.filterRecords(this.data.crews, this.crewSearch(), ['name','code']); }
  filteredVehicles(): GenericRecord[] { return this.filterRecords(this.data.vehicles, this.vehicleSearch(), ['name','plate','license_plate','registration','type']); }
  vehicleLabel(vehicle: GenericRecord): string { const plate = vehicle['plate'] || vehicle['license_plate'] || vehicle['registration']; return plate ? `${vehicle['name'] || 'Fahrzeug'} · ${plate}` : (vehicle['name'] || 'Fahrzeug ohne Kennzeichen'); }
  filteredMachines(): GenericRecord[] { return this.filterRecords(this.data.machines, this.machineSearch(), ['name','inventory_no','type']); }
  private filterRecords(items: GenericRecord[], query: string, fields: string[]): GenericRecord[] {
    const needle = this.normalize(query);
    if (!needle) return items;
    return items.filter((item) => this.normalize(fields.map((field) => item[field] ?? '').join(' ')).includes(needle));
  }
  private normalize(value: unknown): string { return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }
  private toDate(value: unknown): Date { const date = new Date(String(value || new Date().toISOString()).replace(' ', 'T')); return Number.isNaN(date.getTime()) ? new Date() : date; }
  private isoDate(date: Date): string { return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`; }
  private timePart(date: Date, fallback: string): string { if (Number.isNaN(date.getTime())) return fallback; return `${String(date.getHours()).padStart(2,'0')}:${String(date.getMinutes()).padStart(2,'0')}`; }

  projectChanged(): void {
    const sites = this.filteredSites();
    const project = this.data.projects.find((item) => item['id'] === this.form.value.project_id);
    this.form.patchValue({ site_id: sites[0]?.['id'] ?? '', title: project ? `Estricharbeiten · ${project['name']}` : this.form.value.title });
  }

  save(): void {
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    const date = value.date || new Date().toISOString().slice(0, 10);
    this.ref.close({
      project_id: value.project_id,
      site_id: value.site_id,
      crew_id: value.crew_id,
      vehicle_id: value.vehicle_id || null,
      machine_id: value.machine_id || null,
      title: value.title,
      status: value.status,
      start_at: `${date} ${value.start_time || '07:00'}:00`,
      end_at: `${date} ${value.end_time || '16:00'}:00`,
      notes: value.notes
    });
  }
}

@Component({
  standalone: true,
  imports: [DatePipe, NgClass, DragDropModule, RouterLink, MatCardModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSnackBarModule, MatDialogModule, MatSlideToggleModule, MatTooltipModule, MatMenuModule],
  template: `
    <section class="crew-plan-header compact-crew-plan-header">
      <div class="crew-plan-title-row">
        <div><p class="eyebrow">Disposition</p><h1><mat-icon>groups</mat-icon>Kolonnenplanung</h1></div>
        <div class="head-actions crew-plan-actions">
          @if (canPlan) { <button mat-flat-button color="primary" (click)="openAssignmentDialog()"><mat-icon>add</mat-icon> Einplanung</button> }
          <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon> Neu laden</button>
          <button mat-stroked-button class="print-button" (click)="printCurrent()"><mat-icon>picture_as_pdf</mat-icon>{{ viewMode() === 'day' ? 'Tages-PDF' : (viewMode() === 'month' ? 'Monats-PDF' : 'Wochen-PDF') }}</button>
        </div>
      </div>
      <div class="crew-plan-control-row">
        <div class="plan-view-switch" role="group" aria-label="Ansicht">
          <button type="button" [class.active]="viewMode() === 'day'" (click)="setViewMode('day')">Tag</button>
          <button type="button" [class.active]="viewMode() === 'week'" (click)="setViewMode('week')">Woche</button>
          <button type="button" [class.active]="viewMode() === 'month'" (click)="setViewMode('month')">Monat</button>
        </div>
        <div class="plan-date-nav">
          <button mat-icon-button class="plan-date-arrow" (click)="shiftPeriod(-1)" [attr.aria-label]="viewMode() === 'day' ? 'Vortag' : 'Vorwoche'"><mat-icon>chevron_left</mat-icon></button>
          <div class="plan-date-center">
            <strong class="plan-date-main">{{ viewMode() === 'day' ? (currentDate() | date:'EEEE') : ('KW ' + isoWeek()) }}</strong>
            <span class="plan-date-sub">{{ viewMode() === 'day' ? (currentDate() | date:'dd.MM.yyyy') : weekRangeLabel() }}</span>
            <input type="date" class="plan-date-input" [value]="dateInputValue()" (change)="onDatePick($any($event.target).value)" aria-label="Datum wählen">
          </div>
          <button mat-icon-button class="plan-date-arrow" (click)="shiftPeriod(1)" [attr.aria-label]="viewMode() === 'day' ? 'Nächster Tag' : 'Nächste Woche'"><mat-icon>chevron_right</mat-icon></button>
          <button mat-stroked-button class="plan-today-btn" (click)="today()"><mat-icon>today</mat-icon>{{ viewMode() === 'day' ? 'Heute' : 'Diese Woche' }}</button>
        </div>
        @if (viewMode() === 'week') {
          <div class="week-mode-switcher" role="group" aria-label="Wochenansicht">
            <span [class.active]="workWeekOnly()">Arbeitswoche</span>
            <mat-slide-toggle class="week-mode-switch" [checked]="!workWeekOnly()" (change)="setWeekMode(!$event.checked)" aria-label="Standardwoche anzeigen"></mat-slide-toggle>
            <span [class.active]="!workWeekOnly()">Standardwoche</span>
          </div>
        }
      </div>
    </section>

    <section class="crew-toolbar pro-crew-toolbar compact-crew-toolbar filters-only-crew-toolbar">
      <mat-form-field appearance="outline" class="order-filter-field">
        <mat-label>Auftrag</mat-label>
        <mat-select [value]="projectFilter()" (selectionChange)="projectFilter.set($event.value)" panelClass="ew-search-select-panel">
          <mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Auftrag suchen" [value]="projectFilterSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="projectFilterSearch.set($any($event.target).value)"></mat-option>
          <mat-option value="">Alle Bauakten</mat-option>
          @for (project of filteredProjectOptions().slice(0, 50); track project['id']) { <mat-option [value]="project['id']">{{ project['project_no'] || 'Auftrag' }} · {{ project['name'] }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" class="crew-filter-field">
        <mat-label>Kolonne</mat-label>
        <mat-select [value]="crewFilter()" (selectionChange)="crewFilter.set($event.value)">
          <mat-option value="">Alle Kolonnen</mat-option>
          @for (crew of crews(); track crew['id']) { <mat-option [value]="crew['id']">{{ crew['name'] }}</mat-option> }
          <mat-option value="unassigned">Termine ohne Kolonne</mat-option>
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" class="status-filter-field">
        <mat-label>Status</mat-label>
        <mat-select [value]="statusFilter()" (selectionChange)="statusFilter.set($event.value)">
          <mat-option value="">Alle Status</mat-option>
          @for (status of statusOptions; track status.key) { <mat-option [value]="status.key">{{ status.label }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" class="crew-search-field">
        <mat-label>Suche</mat-label>
        <input matInput [value]="searchText()" (input)="searchText.set($any($event.target).value)" placeholder="Baustelle, Auftrag, Kolonne">
        <mat-icon matPrefix>search</mat-icon>
      </mat-form-field>
    </section>

    <mat-card class="square-card mobile-planner-notice">
      <mat-icon>phone_iphone</mat-icon>
      <div><h3>Planung auf größerem Bildschirm</h3></div>
      <button mat-flat-button color="primary" (click)="printPlan()"><mat-icon>picture_as_pdf</mat-icon>Wochen-PDF</button>
    </mat-card>

    <section class="mobile-crew-agenda">
      @for (day of weekDays(); track day.key) {
        <mat-card class="square-card mobile-day-card">
          <h3>{{ weekdayLabel(day.date) }} · {{ day.date | date:'dd.MM.yyyy' }}</h3>
          @for (assignment of assignmentsForDay(day.date); track assignment['id']) {
            <article><b>{{ assignment['title'] }}</b><span>{{ assignment['start_at'] | date:'HH:mm' }}–{{ assignment['end_at'] | date:'HH:mm' }} · {{ assignment['crew_name'] || 'ohne Kolonne' }}</span><small>{{ assignment['project_name'] || projectLabel(assignment['project_id']) }} · {{ assignment['site_name'] || siteLabel(assignment['site_id']) }}</small></article>
          } @empty { <p class="muted">Keine Termine.</p> }
        </mat-card>
      }
    </section>

    <div class="crew-plan-layout" cdkDropListGroup>
    <!-- Baustellen-Vorrat: schwebende Box unten rechts. Sie liegt ÜBER dem
         Plan, statt links Platz zu belegen – dadurch bleibt die volle Breite
         für die Planung, und der Vorrat ist trotzdem jederzeit erreichbar. -->
    <!-- Vollständig weggeklappt bleibt nur ein kleiner Reiter in der Ecke. -->
    @if (!stockOpen()) {
      <button type="button" class="stock-tab" (click)="stockOpen.set(true)" aria-label="Baustellen-Vorrat öffnen">
        <mat-icon>inventory_2</mat-icon>
        <span>Vorrat</span>
        @if (stockList().length) { <em>{{ stockList().length }}</em> }
      </button>
    }
    <aside class="site-stock site-stock-float" [class.is-hidden]="!stockOpen()">
      <header class="site-stock-head">
        <div><mat-icon>inventory_2</mat-icon><strong>Baustellen-Vorrat</strong><span class="site-stock-count">{{ stockList().length }}</span></div>
        <button type="button" class="stock-close" (click)="stockOpen.set(false)" aria-label="Vorrat zuklappen">
          <!-- Die runde Fläche liegt auf einem eigenen Element. Der Knopf
               selbst hat keinen Hintergrund – so kann ihn keine Regel für
               Breite oder Innenabstand zur Ellipse ziehen. -->
          <span class="stock-close-face"><mat-icon>close</mat-icon></span>
        </button>
      </header>

      <!-- Werkzeugzone bleibt stehen, nur die Liste scrollt. Bei 20 und mehr
           Baustellen wäre sonst die Suche beim Scrollen nicht mehr erreichbar. -->
      <div class="site-stock-tools">
        <div class="site-stock-summary">
          <button type="button" [class.on]="stockFilter() === 'free'" (click)="setStockFilter('free')"><b>{{ stockStats().free }}</b><small>frei</small></button>
          <button type="button" [class.on]="stockFilter() === 'planned'" (click)="setStockFilter('planned')"><b>{{ stockStats().planned }}</b><small>verplant</small></button>
          <button type="button" [class.on]="stockFilter() === 'all'" (click)="setStockFilter('all')"><b>{{ stockStats().total }}</b><small>alle</small></button>
        </div>
        <label class="site-stock-find">
          <mat-icon>search</mat-icon>
          <input type="search" [value]="stockSearch()" (input)="stockSearch.set($any($event.target).value)" placeholder="Name, Ort, Akte, Kunde">
          @if (stockSearch()) { <button type="button" (click)="stockSearch.set('')" aria-label="Suche leeren"><mat-icon>close</mat-icon></button> }
        </label>
        <div class="site-stock-sortrow">
          <span>{{ stockList().length }} von {{ stockStats().total }}</span>
          <button type="button" class="site-stock-sort" [matMenuTriggerFor]="sortMenu">
            <mat-icon>swap_vert</mat-icon>{{ stockSortLabel() }}
          </button>
          <mat-menu #sortMenu="matMenu" xPosition="before">
            <button mat-menu-item (click)="stockSort.set('name')">Name</button>
            <button mat-menu-item (click)="stockSort.set('city')">Ort</button>
            <button mat-menu-item (click)="stockSort.set('area')">Fläche</button>
            <button mat-menu-item (click)="stockSort.set('project')">Bauakte</button>
          </mat-menu>
        </div>
      </div>
      @if (true) {
        <div class="site-stock-body" cdkDropList [cdkDropListData]="stockList()" [cdkDropListConnectedTo]="[]">
          @for (site of stockList(); track site['id']) {
            <article class="site-stock-card" [class.is-planned]="stockIsPlanned(site)" cdkDrag [cdkDragData]="{ stockSite: site }"
                     [matTooltip]="stockTooltip(site)" matTooltipPosition="left"
                     (dblclick)="openAssignmentFromStock(site)">
              <span class="site-stock-state" [class.planned]="stockIsPlanned(site)"></span>
              <div class="site-stock-info">
                <strong>{{ site['name'] || 'Baustelle' }}</strong>
                <small>{{ siteAddrText(site) || stockProjectMeta(site) }}</small>
                @if (stockSpecLine(site)) { <small class="site-stock-spec">{{ stockSpecLine(site) }}</small> }
              </div>
              <button type="button" class="site-stock-go" (click)="openAssignmentFromStock(site); $event.stopPropagation()"
                      matTooltip="Einplanen" aria-label="Einplanen"><mat-icon>event_available</mat-icon></button>
            </article>
          } @empty { <p class="muted site-stock-empty">{{ stockShowAll() ? 'Keine passenden offenen Baustellen.' : 'Alle passenden Baustellen sind ' + stockPeriodLabel() + ' verplant.' }}</p> }
        </div>
      }
    </aside>

    <div class="crew-plan-main">
    <mat-card class="crew-week-card print-area">
      <div class="print-only print-title"><strong>Kolonnenplanung</strong><span>{{ viewMode() === 'day' ? (currentDate() | date:'dd.MM.yyyy') : (weekDays()[0].date | date:'dd.MM.yyyy') + ' – ' + (weekDays()[weekDays().length - 1].date | date:'dd.MM.yyyy') }}</span></div>

      @if (viewMode() === 'month') {
        <!-- Monatsplanung: Überblick über den ganzen Monat inkl. Auslastung
             und Ressourcen. Zum Einplanen dient weiterhin Woche bzw. Tag. -->
        <section class="cm-wrap">
          <header class="cm-head">
            <div class="cm-title">
              <h2>{{ monthLabel() }}</h2>
              <span>{{ monthAssignmentCount() }} Einsätze · {{ monthBusyDays() }} von {{ monthDays().length }} Tagen belegt</span>
            </div>
            <div class="cm-legend">
              <span><i class="cm-l low"></i>bis 50 %</span>
              <span><i class="cm-l mid"></i>bis 90 %</span>
              <span><i class="cm-l high"></i>voll</span>
              <span><i class="cm-l over"></i>überbucht</span>
            </div>
          </header>

          <!-- Kalenderraster -->
          <div class="cm-grid">
            @for (wd of monthWeekdayLabels; track wd) { <div class="cm-wdhead">{{ wd }}</div> }
            @for (cell of monthCells(); track cell.key) {
              @if (cell.date) {
                <button type="button" class="cm-day"
                        [class.is-today]="cell.isToday" [class.is-weekend]="cell.isWeekend"
                        [class.is-empty]="!cell.count"
                        [ngClass]="'load-' + cell.loadClass"
                        (click)="openDay(cell.date!)">
                  <span class="cm-daynum">{{ cell.day }}</span>
                  @if (cell.count) {
                    <span class="cm-bar"><i [style.width.%]="cell.loadPercent"></i></span>
                    <span class="cm-count">{{ cell.count }} Einsatz(e)</span>
                    <span class="cm-crews">{{ cell.crewCount }} / {{ crews().length }} Kolonnen</span>
                    @if (cell.inspections) { <span class="cm-tag">{{ cell.inspections }} Termin(e)</span> }
                  } @else {
                    <span class="cm-free">frei</span>
                  }
                </button>
              } @else { <span class="cm-day is-blank"></span> }
            }
          </div>

          <!-- Ressourcenverplanung des Monats -->
          <section class="cm-res">
            <h3>Ressourcen im Monat</h3>
            <div class="cm-res-grid">
              <article class="cm-res-card">
                <header><mat-icon>groups</mat-icon><b>Kolonnen</b></header>
                @for (row of monthResourceLoad('crew'); track row.id) {
                  <div class="cm-res-row">
                    <span class="cm-res-name">{{ row.name }}</span>
                    <span class="cm-res-bar"><i [style.width.%]="row.percent" [ngClass]="'load-' + row.loadClass"></i></span>
                    <span class="cm-res-val">{{ row.days }} Tg.</span>
                  </div>
                } @empty { <p class="cm-res-none">Keine Kolonnen hinterlegt.</p> }
              </article>

              <article class="cm-res-card">
                <header><mat-icon>local_shipping</mat-icon><b>Fahrzeuge</b></header>
                @for (row of monthResourceLoad('vehicle'); track row.id) {
                  <div class="cm-res-row">
                    <span class="cm-res-name">{{ row.name }}</span>
                    <span class="cm-res-bar"><i [style.width.%]="row.percent" [ngClass]="'load-' + row.loadClass"></i></span>
                    <span class="cm-res-val">{{ row.days }} Tg.</span>
                  </div>
                } @empty { <p class="cm-res-none">Keine Fahrzeuge verplant.</p> }
              </article>

              <article class="cm-res-card">
                <header><mat-icon>precision_manufacturing</mat-icon><b>Maschinen &amp; Pumpen</b></header>
                @for (row of monthResourceLoad('machine'); track row.id) {
                  <div class="cm-res-row">
                    <span class="cm-res-name">{{ row.name }}</span>
                    <span class="cm-res-bar"><i [style.width.%]="row.percent" [ngClass]="'load-' + row.loadClass"></i></span>
                    <span class="cm-res-val">{{ row.days }} Tg.</span>
                  </div>
                } @empty { <p class="cm-res-none">Keine Maschinen verplant.</p> }
              </article>
            </div>
            <p class="cm-res-hint">Ein Tag zählt als belegt, sobald mindestens ein Einsatz auf die Ressource entfällt. Doppelbelegungen sind rot markiert.</p>
          </section>
        </section>
      } @else if (viewMode() === 'week') {
        <section class="crew-week-grid compact-week-grid" [class.workweek-grid]="workWeekOnly()" [class.standardweek-grid]="!workWeekOnly()">
          <div class="corner-cell">Kolonne</div>
          @for (day of weekDays(); track day.key) {
            <div class="crew-day-head" [class.today]="isToday(day.date)"><strong>{{ weekdayLabel(day.date) }}</strong><span>{{ day.date | date:'dd.MM.' }}</span></div>
          }

          @for (crew of filteredCrewsWithUnassigned(); track crew['id']) {
            <div class="crew-row-head">
              <strong>{{ crew['name'] }}</strong>
              <small>{{ crew['capacity_m2_day'] ? crew['capacity_m2_day'] + ' m²/Tag' : 'Kapazität offen' }}</small>
              @if (crewMembersLabel(crew)) { <small class="crew-member-list">{{ crewMembersLabel(crew) }}</small> }
            </div>
            @for (day of weekDays(); track $index) {
              <div class="crew-day-cell" [class.has-entries]="assignmentsFor(crew, day.date).length" cdkDropList [cdkDropListData]="assignmentsFor(crew, day.date)" (cdkDropListDropped)="moveAssignment($event, crew, day.date)" (dblclick)="openAssignmentDialog(crew, day.date)">
                @if (assignmentsFor(crew, day.date).length > 1) { <span class="day-assignment-count">{{ assignmentsFor(crew, day.date).length }} Termine</span> }
                @for (assignment of assignmentsFor(crew, day.date); track assignment['id']) {
                  <article class="crew-assignment-card" [class.is-selected]="selectedAssignment()?.['id'] === assignment['id']" [class.has-note]="!!assignment['notes']" [ngClass]="'status-' + (assignment['status'] || 'planned')" cdkDrag (click)="openDetail(assignment); $event.stopPropagation()" (contextmenu)="openNote(assignment, $event)" [matTooltip]="assignment['notes'] ? 'Notiz: ' + assignment['notes'] : 'Rechtsklick für Notiz'">
                    @if (canDeletePlan) { <button type="button" mat-icon-button class="assignment-delete" (click)="deleteAssignment(assignment, $event)" aria-label="Einplanung löschen"><mat-icon>delete</mat-icon></button> }
                    <strong>{{ assignment['title'] }}</strong>
                    <span>{{ assignment['start_at'] | date:'HH:mm' }}–{{ assignment['end_at'] | date:'HH:mm' }}</span>
                    <!-- Nur Angaben zeigen, die nicht schon im Titel stehen.
                         Vorher standen Bauakte, Baustelle und Adresse immer
                         untereinander – heißen sie gleich, stand dasselbe
                         drei- bis viermal auf der Karte. -->
                    @for (line of cardLines(assignment); track line) {
                      <small class="site-address-line">{{ line }}</small>
                    }
                    @if (assignment['notes']) {
                      <small class="crew-card-note"><mat-icon>sticky_note_2</mat-icon>{{ assignment['notes'] }}</small>
                    }
                  </article>
                }
                <button class="cell-add" type="button" (click)="openAssignmentDialog(crew, day.date); $event.stopPropagation()" aria-label="Weitere Einplanung hinzufügen"><mat-icon>add</mat-icon></button>
              </div>
            }
          }
        </section>
      } @else {
        <section class="crew-timeline" [style.--ew-hours]="dayHours().length" [style.--ew-rowlabel.px]="220">
          <div class="crew-timeline-scroll">
            <div class="crew-timeline-grid" [style.--ew-col-min.px]="92">
              <div class="ctl-corner">Fahrzeuge / Kolonnen</div>
              @for (h of dayHours(); track h) {
                <div class="ctl-hour" [class.now-hour]="isCurrentHour(h)">{{ pad(h) }} Uhr</div>
              }

              @for (crew of filteredCrewsWithUnassigned(); track crew['id']) {
                <div class="ctl-rowhead">
                  <strong>{{ crew['name'] }}</strong>
                  @if (crew['capacity_m2_day']) { <small>{{ crew['capacity_m2_day'] }} m²/Tag</small> }
                  @if (crewMembersLabel(crew)) { <small class="ctl-members">{{ crewMembersLabel(crew) }}</small> }
                </div>
                <div class="ctl-lane" [attr.data-crew-id]="crew['id']" [style.grid-column]="'2 / span ' + dayHours().length"
                     [style.min-height.px]="laneHeight(crew)"
                     cdkDropList [cdkDropListData]="assignmentsFor(crew, currentDate())"
                     (cdkDropListDropped)="moveAssignment($event, crew, currentDate())"
                     (dblclick)="openAssignmentDialog(crew, currentDate())">
                  @for (h of dayHours(); track h) { <span class="ctl-gridline" [style.left.%]="hourLeft(h)"></span> }
                  @if (isToday(currentDate())) { <span class="ctl-nowline" [style.left.%]="nowLeft()"></span> }
                  @for (a of laidOut(crew); track a['id']) {
                    <article class="ctl-bar" [ngClass]="'status-' + (a['status'] || 'planned')"
                             [class.is-selected]="selectedAssignment()?.['id'] === a['id']"
                             [class.is-moving]="movingId() === a['id']"
                             [class.is-live]="movingId() === a['id'] || resizingId() === a['id']"
                             [style.left.%]="barLeft(a)" [style.width.%]="barWidth(a)"
                             [style.top.px]="8 + (+(a['_row'] || 0)) * 78" [style.height.px]="70"
                             [class.has-note]="!!a['notes']"
                             (mousedown)="startMove(a, $event)" (touchstart)="startMove(a, $event)"
                             (contextmenu)="openNote(a, $event)"
                             (click)="onBarClick(a, $event)">
                      <span class="ctl-resize ctl-resize-start" (mousedown)="startResize(a, 'start', $event)" (touchstart)="startResize(a, 'start', $event)" (click)="$event.stopPropagation()" aria-label="Beginn ändern"></span>
                      @if ((movingId() === a['id'] || resizingId() === a['id']) && liveTime()) {
                        <span class="ctl-livetime">{{ liveTime() }}</span>
                      }
                      <div class="ctl-bar-inner">
                        <strong>{{ a['title'] || projectLabel(a['project_id']) }}</strong>
                        <small>{{ a['site_name'] || siteLabel(a['site_id']) }}</small>
                        <small class="ctl-bar-addr">{{ assignmentSiteAddress(a) }}</small>
                        <em>{{ a['start_at'] | date:'HH:mm' }}–{{ a['end_at'] | date:'HH:mm' }}</em>
                        @if (a['notes']) { <span class="ctl-note-line">{{ a['notes'] }}</span> }
                      </div>
                      <button type="button" mat-icon-button class="assignment-delete ctl-bar-del" (mousedown)="$event.stopPropagation()" (touchstart)="$event.stopPropagation()" (click)="deleteAssignment(a, $event)" aria-label="Einplanung löschen"><mat-icon>delete</mat-icon></button>
                      <span class="ctl-resize ctl-resize-end" (mousedown)="startResize(a, 'end', $event)" (touchstart)="startResize(a, 'end', $event)" (click)="$event.stopPropagation()" aria-label="Ende ändern"></span>
                    </article>
                  }
                  <button class="ctl-lane-add" type="button" (click)="openAssignmentDialog(crew, currentDate()); $event.stopPropagation()" aria-label="Einplanen"><mat-icon>add</mat-icon></button>
                </div>
              }
            </div>
          </div>
        </section>
      }
    </mat-card>
    </div>
    </div>

    @if (selectedAssignment(); as a) {
      <div class="plan-detail-backdrop" (click)="closeDetail()"></div>
      <aside class="plan-detail-panel" role="dialog" aria-label="Auftragsdetails">
        <header class="plan-detail-head" [ngClass]="'status-' + (a['status'] || 'planned')">
          <div><span class="plan-detail-status">{{ statusLabel(a['status']) }}</span><h2>{{ a['title'] || projectLabel(a['project_id']) }}</h2></div>
          <button mat-icon-button (click)="closeDetail()" aria-label="Schließen"><mat-icon>close</mat-icon></button>
        </header>
        <div class="plan-detail-body">
          <div class="plan-detail-row"><mat-icon>schedule</mat-icon><div><small>Zeitraum</small><b>{{ a['start_at'] | date:'dd.MM.yyyy HH:mm' }} – {{ a['end_at'] | date:'HH:mm' }}</b></div></div>
          <div class="plan-detail-row"><mat-icon>place</mat-icon><div><small>Adresse</small><b>{{ assignmentSiteAddress(a) }}</b></div></div>
          <div class="plan-detail-row"><mat-icon>folder_shared</mat-icon><div><small>Bauakte</small><b>{{ detailProject()?.['project_no'] || '—' }} · {{ a['project_name'] || projectLabel(a['project_id']) }}</b></div></div>
          <div class="plan-detail-row"><mat-icon>person</mat-icon><div><small>Kunde</small><b>{{ detailProject()?.['customer_name'] || '—' }}</b></div></div>
          <div class="plan-detail-row"><mat-icon>business</mat-icon><div><small>Baustelle</small><b>{{ a['site_name'] || siteLabel(a['site_id']) }}</b></div></div>
          <div class="plan-detail-row"><mat-icon>groups</mat-icon><div><small>Kolonne</small><b>{{ a['crew_name'] || crewName(a['crew_id']) || 'nicht zugeordnet' }}</b></div></div>
          @if (detailSite()?.['screed_type'] || detailSite()?.['area_m2']) {
            <div class="plan-detail-row"><mat-icon>layers</mat-icon><div><small>Aufbau / Fläche</small><b>{{ detailSite()?.['screed_type'] || '—' }}{{ detailSite()?.['area_m2'] ? ' · ' + detailSite()?.['area_m2'] + ' m²' : '' }}</b></div></div>
          }
          @if (a['notes']) { <div class="plan-detail-note"><small>Notiz</small><p>{{ a['notes'] }}</p></div> }
        </div>
        <footer class="plan-detail-actions">
          <a mat-flat-button color="primary" [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: a['project_id'] }"><mat-icon>open_in_new</mat-icon>Bauakte öffnen</a>
          <button mat-stroked-button (click)="editAssignment(a)"><mat-icon>edit</mat-icon>Bearbeiten</button>
          <button mat-stroked-button class="detail-delete" (click)="deleteAssignment(a, $event)"><mat-icon>delete</mat-icon>Löschen</button>
        </footer>
      </aside>
    }

    <!-- Der schwebende Plus-Knopf unten rechts ist entfallen: Er lag über dem
         Baustellen-Vorrat und dupliziert nur die Schaltfläche „Einplanen“ in
         der Kopfzeile. -->
  `
})
export class CrewWeekPlannerComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  // Einplanen/Loeschen nur mit Planungsrecht – sonst reine Ansicht.
  readonly canPlan = this.auth.canWrite('planning');
  readonly canDeletePlan = this.auth.canDelete('planning');
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly currentDate = signal(new Date());
  readonly projects = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly crews = signal<GenericRecord[]>([]);
  readonly crewMembers = signal<GenericRecord[]>([]);
  readonly vehicles = signal<GenericRecord[]>([]);
  readonly machines = signal<GenericRecord[]>([]);
  readonly assignments = signal<GenericRecord[]>([]);
  readonly projectFilter = signal('');
  readonly projectFilterSearch = signal('');
  readonly crewFilter = signal('');
  readonly statusFilter = signal('');
  readonly searchText = signal('');
  readonly workWeekOnly = signal(true);
  readonly viewMode = signal<'day' | 'week' | 'month'>('week');
  readonly selectedAssignment = signal<GenericRecord | null>(null);
  readonly movingId = signal<string | null>(null);
  /** Kennung des Balkens, dessen Ränder gerade gezogen werden. */
  readonly resizingId = signal<string | null>(null);
  /** Zeitangabe, die WÄHREND des Ziehens am Balken mitläuft. */
  readonly liveTime = signal('');
  /** Minuten in \"HH:MM\" – für die Live-Anzeige. */
  private hhmm(totalMinutes: number): string {
    const m = Math.max(0, Math.round(totalMinutes));
    return `${String(Math.floor(m / 60) % 24).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
  }
  private setLiveTime(startMin: number, endMin: number): void {
    const dur = Math.max(0, endMin - startMin);
    const h = Math.floor(dur / 60); const min = dur % 60;
    const durLabel = min ? `${h}:${String(min).padStart(2, '0')} h` : `${h} h`;
    this.liveTime.set(`${this.hhmm(startMin)} – ${this.hhmm(endMin)} · ${durLabel}`);
  }
  private moveDidDrag = false;
  readonly stockOpen = signal(true);
  readonly stockShowAll = signal(false); // Altbestand: wird über stockFilter gesteuert
  readonly stockSearch = signal('');
  /** Filter des Vorrats: nur freie, nur verplante oder alle. */
  readonly stockFilter = signal<'free' | 'planned' | 'all'>('free');
  /** Sortierung der Liste – bei vielen Baustellen entscheidend. */
  readonly stockSort = signal<'name' | 'city' | 'area' | 'project'>('name');
  setStockFilter(value: 'free' | 'planned' | 'all'): void {
    this.stockFilter.set(value);
    this.stockShowAll.set(value !== 'free');
  }
  stockSortLabel(): string {
    return ({ name: 'Name', city: 'Ort', area: 'Fläche', project: 'Bauakte' } as Record<string, string>)[this.stockSort()];
  }
  // Konzept Baustellen-Vorrat:
  //  - Quelle sind aktive Baustellen (Projekt nicht abgeschlossen/storniert).
  //  - Standard: nur Baustellen, die im aktuell sichtbaren Zeitraum (Tag bzw. Woche)
  //    noch KEINER Kolonne zugeordnet sind -> verplante verschwinden aus dem Vorrat.
  //  - Umschaltbar auf "alle offenen", falls man bewusst doppelt verplanen will.
  private periodAssignments(): GenericRecord[] {
    const all = this.assignments();
    if (this.viewMode() === 'day') {
      const key = this.isoDate(this.currentDate());
      return all.filter((a) => this.isoDate(this.toDate(a['start_at'])) === key);
    }
    const days = this.weekDays().map((d) => this.isoDate(d.date));
    const set = new Set(days);
    return all.filter((a) => set.has(this.isoDate(this.toDate(a['start_at']))));
  }
  private activeProjectIds(): Set<string> {
    const inactive = new Set(['completed', 'closed', 'cancelled', 'archived']);
    return new Set(this.projects().filter((p) => !inactive.has(String(p['status'] || ''))).map((p) => String(p['id'])));
  }
  private stockBaseList(): GenericRecord[] {
    const activeProjects = this.activeProjectIds();
    return this.sites().filter((s) => {
      const pid = String(s['project_id'] || '');
      if (pid && !activeProjects.has(pid)) return false;
      const status = String(s['status'] || '').toLowerCase();
      return !['completed','closed','cancelled','archived'].includes(status);
    });
  }
  /**
   * Liste des Vorrats.
   *
   * Ausgelegt auf 20 und mehr Baustellen: Filter (frei/verplant/alle),
   * Volltextsuche und wählbare Sortierung. Die Obergrenze liegt bei 200
   * Einträgen – darüber hinaus grenzt die Suche sinnvoll ein, statt eine
   * unübersichtlich lange Liste zu bauen.
   */
  stockList(): GenericRecord[] {
    const planned = new Set(this.periodAssignments().map((a) => String(a['site_id'] || '')));
    const q = this.normalizePlanner(this.stockSearch());
    const filter = this.stockFilter();
    const sort = this.stockSort();

    return this.stockBaseList()
      .filter((s) => {
        const isPlanned = planned.has(String(s['id']));
        if (filter === 'free') return !isPlanned;
        if (filter === 'planned') return isPlanned;
        return true;
      })
      .filter((s) => {
        if (!q) return true;
        const hay = this.normalizePlanner([s['name'], s['site_no'], s['address'], s['zip'], s['city'], s['screed_type'], s['area_m2'], this.stockProjectMeta(s)].join(' '));
        return hay.includes(q);
      })
      .sort((a, b) => {
        // Freie Baustellen stehen immer vorn – sie sind der Grund für den Vorrat.
        const pa = planned.has(String(a['id'])) ? 1 : 0; const pb = planned.has(String(b['id'])) ? 1 : 0;
        if (pa !== pb) return pa - pb;
        if (sort === 'city') return String(a['city'] || '').localeCompare(String(b['city'] || ''), 'de');
        if (sort === 'area') return Number(b['area_m2'] || 0) - Number(a['area_m2'] || 0);
        if (sort === 'project') return this.stockProjectMeta(a).localeCompare(this.stockProjectMeta(b), 'de');
        return String(a['name'] || '').localeCompare(String(b['name'] || ''), 'de');
      })
      .slice(0, 200);
  }
  stockStats(): { total: number; free: number; planned: number } {
    const planned = new Set(this.periodAssignments().map((a) => String(a['site_id'] || '')));
    const total = this.stockBaseList().length;
    const plannedCount = this.stockBaseList().filter((s) => planned.has(String(s['id']))).length;
    return { total, planned: plannedCount, free: Math.max(0, total - plannedCount) };
  }
  /** Kurzzeile mit Bauakte, Fläche und Estrichart – spart eigene Zeilen. */
  stockSpecLine(site: GenericRecord): string {
    const parts: string[] = [];
    const meta = this.stockProjectMeta(site);
    if (meta) parts.push(meta);
    if (site['area_m2']) parts.push(`${site['area_m2']} m²`);
    if (site['screed_type']) parts.push(String(site['screed_type']));
    return parts.join(' · ');
  }
  /** Vollständige Angaben beim Überfahren, da die Karte gekürzt ist. */
  stockTooltip(site: GenericRecord): string {
    return [site['name'], this.siteAddrText(site), this.stockProjectMeta(site)]
      .map((v) => String(v ?? '').trim()).filter((v) => v.length > 0).join('\n');
  }
  stockIsPlanned(site: GenericRecord): boolean { return new Set(this.periodAssignments().map((a) => String(a['site_id'] || ''))).has(String(site['id'])); }
  stockPeriodLabel(): string { return this.viewMode() === 'day' ? 'für diesen Tag' : 'für diese Woche'; }
  /** Beschriftung der Kopfzeile je nach Ansicht. */
  viewLabel(): string { const m = this.viewMode(); return m === 'day' ? 'Tag' : m === 'month' ? 'Monat' : 'Woche'; }

  // ======================= Monatsplanung ==================================
  readonly monthWeekdayLabels = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];

  monthLabel(): string {
    return this.currentDate().toLocaleDateString('de-DE', { month: 'long', year: 'numeric' });
  }
  /** Alle Tage des angezeigten Monats. */
  monthDays(): Date[] {
    const base = this.currentDate();
    const year = base.getFullYear(); const month = base.getMonth();
    const count = new Date(year, month + 1, 0).getDate();
    return Array.from({ length: count }, (_, i) => new Date(year, month, i + 1, 12, 0, 0));
  }
  private assignmentsOnDay(iso: string): GenericRecord[] {
    return this.assignments().filter((a) => this.isoDate(this.toDate(a['start_at'])) === iso);
  }
  /**
   * Zellen des Kalenderrasters inklusive Auffüllern vor dem Monatsersten,
   * damit die Wochentage in den Spalten stimmen.
   */
  monthCells(): { key: string; date: Date | null; day: number; isToday: boolean; isWeekend: boolean; count: number; crewCount: number; inspections: number; loadPercent: number; loadClass: string }[] {
    const days = this.monthDays();
    if (!days.length) return [];
    const lead = (days[0].getDay() + 6) % 7;   // Montag = 0
    const cells: ReturnType<CrewWeekPlannerComponent['monthCells']> = [];
    for (let i = 0; i < lead; i++) cells.push({ key: `blank-${i}`, date: null, day: 0, isToday: false, isWeekend: false, count: 0, crewCount: 0, inspections: 0, loadPercent: 0, loadClass: 'none' });
    const crewTotal = Math.max(1, this.crews().length);
    for (const date of days) {
      const iso = this.isoDate(date);
      const items = this.assignmentsOnDay(iso);
      const crewIds = new Set(items.map((a) => String(a['crew_id'] ?? '')).filter((v) => v.length > 0));
      const inspections = items.filter((a) => /begehung|besichtigung|kontrolle|abnahme|aufma|messung|kundentermin/i.test(String(a['title'] || ''))).length;
      const percent = Math.min(140, Math.round((crewIds.size / crewTotal) * 100));
      cells.push({
        key: iso, date, day: date.getDate(),
        isToday: this.isToday(date),
        isWeekend: date.getDay() === 0 || date.getDay() === 6,
        count: items.length, crewCount: crewIds.size, inspections,
        loadPercent: Math.min(100, percent),
        loadClass: this.loadClassFor(percent, items.length)
      });
    }
    return cells;
  }
  private loadClassFor(percent: number, count: number): string {
    if (!count) return 'none';
    if (percent > 100) return 'over';
    if (percent >= 90) return 'high';
    if (percent >= 50) return 'mid';
    return 'low';
  }
  monthAssignmentCount(): number { return this.monthDays().reduce((sum, d) => sum + this.assignmentsOnDay(this.isoDate(d)).length, 0); }
  monthBusyDays(): number { return this.monthDays().filter((d) => this.assignmentsOnDay(this.isoDate(d)).length > 0).length; }
  /** Wechselt aus dem Monat in die Tagesansicht des gewählten Tages. */
  openDay(date: Date): void { this.currentDate.set(new Date(date)); this.setViewMode('day'); this.load(); }

  /**
   * Auslastung je Ressource im Monat: an wie vielen Arbeitstagen ist die
   * Kolonne, das Fahrzeug bzw. die Maschine verplant. Rot, sobald an einem
   * Tag mehrere Einsätze auf dieselbe Ressource entfallen (Doppelbelegung).
   */
  monthResourceLoad(kind: 'crew' | 'vehicle' | 'machine'): { id: string; name: string; days: number; percent: number; loadClass: string }[] {
    const field = kind === 'crew' ? 'crew_id' : kind === 'vehicle' ? 'vehicle_id' : 'machine_id';
    const nameField = kind === 'crew' ? 'crew_name' : kind === 'vehicle' ? 'vehicle_name' : 'machine_name';
    const source = kind === 'crew' ? this.crews() : kind === 'vehicle' ? this.vehicles() : this.machines();
    const days = this.monthDays();
    const totalDays = Math.max(1, days.filter((d) => d.getDay() !== 0 && d.getDay() !== 6).length);

    const rows = new Map<string, { id: string; name: string; days: Set<string>; doubled: boolean }>();
    for (const date of days) {
      const iso = this.isoDate(date);
      const perResource = new Map<string, number>();
      for (const a of this.assignmentsOnDay(iso)) {
        const id = String(a[field] ?? '');
        if (!id) continue;
        perResource.set(id, (perResource.get(id) || 0) + 1);
        const known = source.find((r) => String(r['id']) === id);
        const entry = rows.get(id) || { id, name: String(known?.['name'] || a[nameField] || 'Ohne Bezeichnung'), days: new Set<string>(), doubled: false };
        entry.days.add(iso);
        rows.set(id, entry);
      }
      perResource.forEach((count, id) => { if (count > 1) { const e = rows.get(id); if (e) e.doubled = true; } });
    }

    return Array.from(rows.values())
      .map((r) => {
        const percent = Math.min(100, Math.round((r.days.size / totalDays) * 100));
        return { id: r.id, name: r.name, days: r.days.size, percent, loadClass: r.doubled ? 'over' : this.loadClassFor(percent, r.days.size) };
      })
      .sort((a, b) => b.days - a.days);
  }
  projectForSite(site: GenericRecord): GenericRecord | undefined { return this.projects().find((p) => String(p['id']) === String(site['project_id'])); }
  projectNoForSite(site: GenericRecord): string { const p = this.projectForSite(site); return p ? String(p['project_no'] || p['name'] || '') : String(site['site_no'] || ''); }
  stockProjectMeta(site: GenericRecord): string { const p = this.projectForSite(site); return [p?.['project_no'] || site['site_no'], p?.['name'], p?.['customer_name']].filter(Boolean).join(' · ') || 'ohne Bauakte'; }
  siteAddrText(site: GenericRecord): string { return [site['address'], site['zip'], site['city']].filter(Boolean).join(' ') || '—'; }
  readonly statusOptions = [
    { key: 'new', label: 'Neu' }, { key: 'planned', label: 'Geplant' }, { key: 'in_progress', label: 'In Arbeit' },
    { key: 'delayed', label: 'Verzögert' }, { key: 'defect', label: 'Mangel' }, { key: 'completed', label: 'Abgeschlossen' }
  ];

  readonly companyName = signal('EstrichWeb');
  constructor() { this.load(); this.api.appSettings().subscribe({ next: (res) => { const n = res?.['company']?.['name']; if (n) this.companyName.set(String(n)); }, error: () => {} }); }

  load(): void {
    this.api.list('projects', '', 80).subscribe((response) => this.projects.set(response.items));
    this.api.list('construction_sites', '', 200).subscribe((response) => this.sites.set(response.items));
    this.api.list('crews', '', 100).subscribe((response) => this.crews.set(response.items));
    this.api.list('crew_members', '', 500).subscribe((response) => this.crewMembers.set(response.items));
    this.api.list('vehicles', '', 100).subscribe((response) => this.vehicles.set(response.items));
    this.api.list('machines', '', 100).subscribe((response) => this.machines.set(response.items));
    const days = this.weekDays();
    let from = days[0].key, to = days[days.length - 1].key;
    // In der Tagesansicht ist der Arbeitswoche/Standardwoche-Switch ohne Wirkung:
    // es wird immer genau der aktuelle Tag geladen.
    if (this.viewMode() === 'day') { from = this.isoDate(this.currentDate()); to = from; }
    // In der Monatsansicht den kompletten Monat laden.
    if (this.viewMode() === 'month') {
      const list = this.monthDays();
      if (list.length) { from = this.isoDate(list[0]); to = this.isoDate(list[list.length - 1]); }
    }
    this.api.planningAssignments(from, to).subscribe((response) => this.assignments.set(response.items));
  }

  /**
   * Notiz zu einer Einplanung – erreichbar per Rechtsklick auf die Karte
   * bzw. den Balken. Ohne Planungsrecht nur lesbar.
   */
  openNote(a: GenericRecord, event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    const ref = this.dialog.open(AssignmentNoteDialogComponent, {
      width: 'min(480px, 94vw)', panelClass: 'ew-dialog', autoFocus: false,
      data: {
        title: String(a['title'] || this.projectLabel(a['project_id']) || 'Einplanung'),
        subtitle: [a['site_name'] || this.siteLabel(a['site_id']), a['crew_name']].filter(Boolean).join(' · '),
        notes: String(a['notes'] || ''),
        readonly: !this.canPlan
      }
    });
    ref.afterClosed().subscribe((result: { notes: string } | undefined) => {
      if (!result) return;
      this.api.update('planning_assignments', String(a['id']), { notes: result.notes }).subscribe({
        next: () => {
          this.assignments.update((list) => list.map((x) => x['id'] === a['id'] ? { ...x, notes: result.notes } : x));
          this.snack.open(result.notes ? 'Notiz gespeichert.' : 'Notiz entfernt.', 'OK', { duration: 2200 });
        },
        error: () => this.snack.open('Notiz konnte nicht gespeichert werden.', 'OK', { duration: 3000 })
      });
    });
  }

  openAssignmentDialog(crew: GenericRecord | null = null, date: Date = this.currentDate()): void {
    const ref = this.dialog.open(AssignmentDialogComponent, {
      width: 'min(760px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog',
      data: { projects: this.projects(), sites: this.sites(), crews: this.crews(), vehicles: this.vehicles(), machines: this.machines(), initialDate: this.isoDate(date), crew }
    });
    ref.afterClosed().subscribe((payload?: GenericRecord) => {
      if (!payload) return;
      this.api.checkPlanningConflicts(payload).subscribe((check) => {
        if (Array.isArray(check['conflicts']) && check['conflicts'].length) this.snack.open('Achtung: Ressourcenkonflikt erkannt. Einplanung wird trotzdem gespeichert.', 'OK', { duration: 4500 });
        this.api.create('planning_assignments', payload).subscribe({
          next: () => { this.snack.open('Kolonne eingeplant', 'OK', { duration: 2200 }); this.load(); },
          error: () => this.snack.open('Einplanung konnte nicht gespeichert werden', 'OK', { duration: 3200 })
        });
      });
    });
  }

  openAssignmentFromStock(site: GenericRecord): void {
    const project = this.projectForSite(site);
    const projects = project ? [project, ...this.projects().filter((p) => p['id'] !== project['id'])] : this.projects();
    const sites = [site, ...this.sites().filter((s) => s['id'] !== site['id'])];
    const ref = this.dialog.open(AssignmentDialogComponent, {
      width: 'min(760px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog',
      data: { projects, sites, crews: this.crews(), vehicles: this.vehicles(), machines: this.machines(), initialDate: this.isoDate(this.currentDate()), crew: null }
    });
    ref.afterClosed().subscribe((payload?: GenericRecord) => {
      if (!payload) return;
      const prepared = { ...payload, project_id: site['project_id'] || payload['project_id'] || null, site_id: site['id'], title: payload['title'] || this.stockAssignmentTitle(site) };
      this.api.checkPlanningConflicts(prepared).subscribe((check) => {
        if (Array.isArray(check['conflicts']) && check['conflicts'].length) this.snack.open('Achtung: Ressourcenkonflikt erkannt. Einplanung wird trotzdem gespeichert.', 'OK', { duration: 4500 });
        this.api.create('planning_assignments', prepared).subscribe({
          next: () => { this.snack.open(`Baustelle ${project?.['project_no'] || ''} eingeplant`.trim(), 'OK', { duration: 2200 }); this.load(); },
          error: () => this.snack.open('Einplanung konnte nicht gespeichert werden', 'OK', { duration: 3200 })
        });
      });
    });
  }
  stockAssignmentTitle(site: GenericRecord): string {
    const project = this.projectForSite(site);
    return [project?.['project_no'], site['name'] || project?.['name'] || 'Baustelle'].filter(Boolean).join(' · ');
  }

  moveAssignment(event: CdkDragDrop<GenericRecord[]>, crew: GenericRecord, date: Date): void {
    // Drag aus dem Baustellen-Vorrat: neue Einplanung anlegen
    const dragData = event.item?.data as GenericRecord | undefined;
    if (dragData && dragData['stockSite']) {
      const site = dragData['stockSite'] as GenericRecord;
      const start = new Date(date); start.setHours(7, 0, 0, 0);
      const end = new Date(date); end.setHours(16, 0, 0, 0);
      const project = this.projects().find((p) => p['id'] === site['project_id']);
      const payload: GenericRecord = {
        crew_id: crew['id'] === 'unassigned' ? null : crew['id'],
        project_id: site['project_id'] || null,
        site_id: site['id'],
        title: this.stockAssignmentTitle(site),
        status: 'planned',
        start_at: this.sqlDateTime(start), end_at: this.sqlDateTime(end),
        // KEINE Adresse als Notiz mehr: Die Adresse steht ohnehin auf der
        // Karte. Als Notiz erschien sie ein zweites Mal – und belegte das
        // Notizfeld, das für echte Hinweise gedacht ist ("Schlüssel beim
        // Nachbarn", "Zufahrt eng").
        notes: null
      };
      this.withConflictCheck(
        this.checkPlanConflicts({ crewId: payload['crew_id'], siteId: site['id'], start, end }),
        () => this.api.create('planning_assignments', payload).subscribe({
          next: () => { this.snack.open('Baustelle eingeplant', 'OK', { duration: 2000 }); this.load(); },
          error: () => this.snack.open('Einplanung konnte nicht gespeichert werden', 'OK', { duration: 3200 })
        })
      );
      return;
    }
    const moved = event.previousContainer.data[event.previousIndex];
    if (!moved) return;
    const oldStart = this.toDate(moved['start_at']);
    const oldEnd = this.toDate(moved['end_at']);
    const durationMs = Math.max(oldEnd.getTime() - oldStart.getTime(), 60 * 60 * 1000);
    const start = new Date(date); start.setHours(oldStart.getHours(), oldStart.getMinutes(), 0, 0);
    const end = new Date(start.getTime() + durationMs);
    const payload = { crew_id: crew['id'] === 'unassigned' ? null : crew['id'], start_at: this.sqlDateTime(start), end_at: this.sqlDateTime(end) };
    const conflict = this.checkPlanConflicts({ crewId: payload.crew_id, siteId: moved['site_id'], start, end, ignoreId: moved['id'] });
    if (conflict.block) {
      // Überschneidungen werden gar nicht erst übernommen – auch nicht lokal.
      this.dialog.open(ConfirmDialogComponent, {
        width: 'min(460px, 96vw)', panelClass: 'ew-dialog',
        data: { title: 'Verschieben nicht möglich', message: conflict.block, confirmText: 'Verstanden', cancelText: '', icon: 'event_busy', tone: 'warn' }
      });
      this.load();
      return;
    }
    this.assignments.update((items) => items.map((item) => item['id'] === moved['id'] ? { ...item, ...payload, crew_name: crew['name'] } : item));
    this.api.update('planning_assignments', String(moved['id']), payload).subscribe({
      next: () => this.snack.open('Einplanung verschoben', 'OK', { duration: 1800 }),
      error: () => this.snack.open('Verschieben nur lokal erfolgt – Server nicht erreichbar', 'OK', { duration: 3200 })
    });
  }

  deleteAssignment(assignment: GenericRecord, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    const id = String(assignment['id'] ?? '');
    if (!id) return;
    const title = String(assignment['title'] || 'Einplanung');
    const ref = this.dialog.open(ConfirmDialogComponent, {
      width: 'min(440px, 94vw)',
      panelClass: 'ew-dialog',
      data: {
        title: 'Einplanung löschen',
        message: `Soll die Einplanung „${title}“ wirklich gelöscht werden?`,
        confirmText: 'Löschen',
        cancelText: 'Abbrechen',
        icon: 'delete',
        tone: 'warn'
      }
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (!confirmed) return;
      const previous = this.assignments();
      this.assignments.update((items) => items.filter((item) => item['id'] !== assignment['id']));
      this.api.remove('planning_assignments', id).subscribe({
        next: () => this.snack.open('Einplanung gelöscht', 'OK', { duration: 1800 }),
        error: () => { this.assignments.set(previous); this.snack.open('Einplanung konnte nicht gelöscht werden', 'OK', { duration: 3200 }); }
      });
    });
  }

  /** Anzahl Einsätze einer Kolonne in der angezeigten Woche (Arbeitsplan). */
  crewWeekCount(crew: GenericRecord): number {
    return this.weekDays().reduce((sum, day) => sum + this.assignmentsFor(crew, day.date).length, 0);
  }
  // ---- Konfliktprüfung ---------------------------------------------------
  /** Prüft, ob eine Einplanung mit dem bestehenden Plan kollidiert.
   *  `block` = zeitliche Überschneidung derselben Kolonne (nicht erlaubt),
   *  `warn`  = dieselbe Baustelle am selben Tag (nur Rückfrage). */
  private checkPlanConflicts(opts: { crewId: unknown; siteId: unknown; start: Date; end: Date; ignoreId?: unknown }): { block?: string; warn?: string } {
    const dayKey = this.isoDate(opts.start);
    const from = opts.start.getTime();
    const to = opts.end.getTime();
    const crewId = String(opts.crewId ?? '');
    const others = this.assignments().filter((a) =>
      String(a['id']) !== String(opts.ignoreId ?? '') &&
      this.isoDate(this.toDate(a['start_at'])) === dayKey);

    if (crewId && crewId !== 'unassigned') {
      const clash = others.find((a) => {
        if (String(a['crew_id'] ?? '') !== crewId) return false;
        const s = this.toDate(a['start_at']).getTime();
        const e = this.toDate(a['end_at']).getTime();
        return from < e && s < to;   // Anschlusstermine sind erlaubt
      });
      if (clash) {
        const t = `${this.toDate(clash['start_at']).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}–${this.toDate(clash['end_at']).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`;
        return { block: `Zeitliche Überschneidung: Diese Kolonne ist von ${t} bereits auf „${clash['site_name'] || clash['title'] || 'einer Baustelle'}" eingeteilt.` };
      }
    }

    const dup = others.find((a) => String(a['site_id'] ?? '') === String(opts.siteId ?? '') && String(opts.siteId ?? '') !== '');
    if (dup) {
      return { warn: `Diese Baustelle ist an diesem Tag bereits eingeplant (${dup['crew_name'] || 'andere Kolonne'}). Trotzdem zusätzlich einplanen?` };
    }
    return {};
  }
  /** Zeigt den passenden Dialog und ruft bei Freigabe `proceed` auf. */
  private withConflictCheck(conflict: { block?: string; warn?: string }, proceed: () => void): void {
    if (conflict.block) {
      this.dialog.open(ConfirmDialogComponent, {
        width: 'min(460px, 96vw)', panelClass: 'ew-dialog',
        data: { title: 'Einplanung nicht möglich', message: conflict.block, confirmText: 'Verstanden', cancelText: '', icon: 'event_busy', tone: 'warn' }
      });
      return;
    }
    if (conflict.warn) {
      this.dialog.open(ConfirmDialogComponent, {
        width: 'min(480px, 96vw)', panelClass: 'ew-dialog',
        data: { title: 'Baustelle bereits eingeplant', message: conflict.warn, confirmText: 'Trotzdem einplanen', cancelText: 'Abbrechen', icon: 'warning', tone: 'warn' }
      }).afterClosed().subscribe((ok) => { if (ok) proceed(); });
      return;
    }
    proceed();
  }

  assignmentsFor(crew: GenericRecord, date: Date): GenericRecord[] {
    const key = this.isoDate(date);
    const crewId = crew['id'] === 'unassigned' ? '' : crew['id'];
    return this.visibleAssignments()
      .filter((assignment) => (assignment['crew_id'] || '') === crewId && this.isoDate(this.toDate(assignment['start_at'])) === key)
      .sort((a, b) => String(a['start_at']).localeCompare(String(b['start_at'])));
  }

  // Überlappende Einplanungen in Unter-Reihen verteilen (nicht überlagern)
  laidOut(crew: GenericRecord): GenericRecord[] {
    const items = this.assignmentsFor(crew, this.currentDate());
    const rowEnds: number[] = []; // Endzeit (Minuten) je belegter Unterzeile
    return items.map((a) => {
      const start = this.dayMinutes(a['start_at']);
      const end = Math.max(start + 30, this.dayMinutes(a['end_at']));
      let row = rowEnds.findIndex((e) => start >= e);
      if (row === -1) { row = rowEnds.length; rowEnds.push(end); } else { rowEnds[row] = end; }
      return { ...a, _row: row };
    });
  }
  laneRows(crew: GenericRecord): number {
    const laid = this.laidOut(crew);
    return Math.max(1, laid.reduce((m, a) => Math.max(m, (Number(a['_row']) || 0) + 1), 1));
  }
  laneHeight(crew: GenericRecord): number { return 16 + this.laneRows(crew) * 78; }

  totalAssignments(): number { return this.visibleAssignments().length; }
  visibleAssignments(): GenericRecord[] {
    const project = this.projectFilter();
    const crew = this.crewFilter();
    const status = this.statusFilter();
    const q = this.normalizePlanner(this.searchText());
    return this.assignments()
      .filter((assignment) => !project || assignment['project_id'] === project)
      .filter((assignment) => !status || String(assignment['status'] || '') === status)
      .filter((assignment) => !crew || (crew === 'unassigned' ? !assignment['crew_id'] : assignment['crew_id'] === crew))
      .filter((assignment) => {
        if (!q) return true;
        const hay = this.normalizePlanner([assignment['title'], assignment['project_name'], this.projectLabel(assignment['project_id']), assignment['site_name'], this.siteLabel(assignment['site_id']), assignment['crew_name'], assignment['notes']].join(' '));
        return hay.includes(q);
      })
      .sort((a, b) => String(a['start_at']).localeCompare(String(b['start_at'])));
  }
  assignedCrewsCount(): number { return new Set(this.visibleAssignments().map((assignment) => assignment['crew_id']).filter(Boolean)).size; }
  unassignedCount(): number { return this.visibleAssignments().filter((assignment) => !assignment['crew_id']).length; }
  overloadedDaysCount(): number { return this.weekDays().filter((day) => this.assignmentsForDay(day.date).length > 1).length; }
  assignmentsForDay(date: Date): GenericRecord[] { const key = this.isoDate(date); return this.visibleAssignments().filter((assignment) => this.isoDate(this.toDate(assignment['start_at'])) === key); }
  statusLabel(status: string): string { return ({ new: 'Neu', planned: 'Geplant', in_progress: 'In Arbeit', delayed: 'Verzögert', defect: 'Mangel', completed: 'Abgeschlossen' } as Record<string,string>)[status] || status || 'Geplant'; }
  timeLabel(value: unknown): string { const d = this.toDate(value); return `${String(d.getHours()).padStart(2,'0')}:${String(d.getMinutes()).padStart(2,'0')}`; }
  crewsWithUnassigned(): GenericRecord[] { return [...this.crews(), { id: 'unassigned', name: 'Termine ohne Kolonne', capacity_m2_day: null, _isUnassigned: true }]; }
  filteredCrewsWithUnassigned(): GenericRecord[] {
    const selected = this.crewFilter();
    const rows = this.crewsWithUnassigned();
    return selected ? rows.filter((crew) => crew['id'] === selected) : rows;
  }
  weekDays(): { date: Date; key: string }[] {
    const base = new Date(this.currentDate());
    const start = new Date(base); start.setDate(base.getDate() - ((base.getDay() + 6) % 7));
    const length = this.workWeekOnly() ? 5 : 7;
    return Array.from({ length }, (_, index) => { const date = new Date(start); date.setDate(start.getDate() + index); return { date, key: this.isoDate(date) }; });
  }
  shiftWeek(delta: number): void { const next = new Date(this.currentDate()); next.setDate(next.getDate() + delta * 7); this.currentDate.set(next); this.load(); }
  shiftPeriod(delta: number): void {
    const next = new Date(this.currentDate());
    // Im Monat wird monatsweise geblättert, sonst tage- bzw. wochenweise.
    if (this.viewMode() === 'month') next.setMonth(next.getMonth() + delta);
    else next.setDate(next.getDate() + delta * (this.viewMode() === 'day' ? 1 : 7));
    this.currentDate.set(next); this.load();
  }
  dateInputValue(): string { return this.isoDate(this.currentDate()); }
  onDatePick(value: string): void { if (!value) return; const d = new Date(value + 'T12:00:00'); if (!Number.isNaN(d.getTime())) { this.currentDate.set(d); this.load(); } }
  isoWeek(): number { return this.calendarWeek(this.weekDays()[0].date); }
  today(): void { this.currentDate.set(new Date()); this.load(); }
  setWeekMode(workWeek: boolean): void { this.workWeekOnly.set(workWeek); this.load(); }
  setViewMode(mode: 'day' | 'week' | 'month'): void { this.viewMode.set(mode); this.closeDetail(); }

  // ---- Zeitachsen-Tagesansicht (Stundenraster wie professionelle Dispo-Tools) ----
  readonly dayStartHour = 5;   // 05 Uhr
  readonly dayEndHour = 21;    // 21 Uhr (exklusiv letzte Spalte = 20–21)
  /** Stundenspalten der Tagesansicht.
   *
   *  WICHTIG: Die Schleife lief frueher bis einschliesslich dayEndHour und
   *  erzeugte damit 17 Spalten, waehrend die Rasterlinien mit einer Spanne von
   *  16 Stunden rechnen (spanMin). Kopfzeile und Striche lagen dadurch um eine
   *  knappe Spaltenbreite auseinander. Jetzt entspricht jede Spalte genau einem
   *  Stundenblock (05–06, 06–07 … 20–21) und sitzt buendig auf der Linie. */
  dayHours(): number[] { const out: number[] = []; for (let h = this.dayStartHour; h < this.dayEndHour; h++) out.push(h); return out; }
  pad(h: number): string { return String(h).padStart(2, '0'); }
  private dayMinutes(value: unknown): number {
    const d = this.toDate(value);
    return d.getHours() * 60 + d.getMinutes();
  }
  private spanMin(): number { return (this.dayEndHour - this.dayStartHour) * 60; }
  hourLeft(h: number): number { return ((h - this.dayStartHour) * 60 / this.spanMin()) * 100; }
  nowLeft(): number { const now = new Date(); const m = now.getHours() * 60 + now.getMinutes(); return Math.max(0, Math.min(100, ((m - this.dayStartHour * 60) / this.spanMin()) * 100)); }
  isCurrentHour(h: number): boolean { return this.isToday(this.currentDate()) && new Date().getHours() === h; }
  statusRgb(status: string): [number, number, number] {
    const map: Record<string, [number, number, number]> = { new: [31,122,77], planned: [47,111,237], in_progress: [19,115,51], delayed: [176,96,0], defect: [180,35,24], completed: [71,85,105] };
    return map[status] || [47,111,237];
  }
  barLeft(a: GenericRecord): number {
    const start = this.dayMinutes(a['start_at']) - this.dayStartHour * 60;
    return Math.max(0, Math.min(100, (start / this.spanMin()) * 100));
  }
  barWidth(a: GenericRecord): number {
    const startAbs = this.dayMinutes(a['start_at']);
    const endAbs = Math.max(startAbs + 30, this.dayMinutes(a['end_at']));
    const left = Math.max(this.dayStartHour * 60, startAbs);
    const right = Math.min(this.dayEndHour * 60, endAbs);
    return Math.max(5, ((right - left) / this.spanMin()) * 100);
  }

  // Balken frei verschieben: horizontal = Uhrzeit (15-Min-Raster), vertikal = andere Kolonne.
  onBarClick(a: GenericRecord, event: Event): void {
    event.stopPropagation();
    if (this.moveDidDrag) { this.moveDidDrag = false; return; } // war ein Zieh-Vorgang, kein Klick
    this.openDetail(a);
  }
  startMove(a: GenericRecord, event: MouseEvent | TouchEvent): void {
    // Resize-Griffe und Löschbutton nicht abfangen
    const tgt = event.target as HTMLElement;
    if (tgt.closest('.ctl-resize') || tgt.closest('.ctl-bar-del')) return;
    event.preventDefault();
    const grid = (event.currentTarget as HTMLElement).closest('.crew-timeline-grid') as HTMLElement | null;
    const startLane = (event.currentTarget as HTMLElement).closest('.ctl-lane') as HTMLElement | null;
    if (!grid || !startLane) return;
    const barEl = event.currentTarget as HTMLElement;
    const totalMin = this.spanMin();
    const startRect = startLane.getBoundingClientRect();
    const durationMin = Math.max(30, this.dayMinutes(a['end_at']) - this.dayMinutes(a['start_at']));
    const clientX = (e: MouseEvent | TouchEvent) => 'touches' in e ? e.touches[0].clientX : (e as MouseEvent).clientX;
    const clientY = (e: MouseEvent | TouchEvent) => 'touches' in e ? e.touches[0].clientY : (e as MouseEvent).clientY;
    const downX = clientX(event); const downY = clientY(event);
    const origLeftPct = this.barLeft(a);
    this.moveDidDrag = false;
    this.movingId.set(String(a['id']));
    barEl.classList.add('is-moving');
    this.setLiveTime(this.dayMinutes(a['start_at']), this.dayMinutes(a['end_at']));

    let targetStartMin = this.dayMinutes(a['start_at']);
    let targetLane: HTMLElement = startLane;

    const move = (e: MouseEvent | TouchEvent) => {
      const dx = clientX(e) - downX; const dy = clientY(e) - downY;
      if (Math.abs(dx) > 4 || Math.abs(dy) > 4) this.moveDidDrag = true;
      // Horizontal -> neue Startzeit (15-Min-Raster)
      const pctDelta = (dx / startRect.width) * 100;
      let newLeftPct = Math.max(0, Math.min(100, origLeftPct + pctDelta));
      let newStartMin = this.dayStartHour * 60 + Math.round((newLeftPct / 100 * totalMin) / 15) * 15;
      newStartMin = Math.max(this.dayStartHour * 60, Math.min(this.dayEndHour * 60 - durationMin, newStartMin));
      targetStartMin = newStartMin;
      barEl.style.left = ((newStartMin - this.dayStartHour * 60) / totalMin * 100) + '%';
      // Zeiten laufen live mit, damit man beim Ziehen sieht, wo man landet.
      this.setLiveTime(newStartMin, newStartMin + durationMin);
      // Vertikal -> Lane unter dem Zeiger ermitteln und hervorheben
      const laneAtPoint = (document.elementFromPoint(clientX(e), clientY(e)) as HTMLElement | null)?.closest('.ctl-lane') as HTMLElement | null;
      grid.querySelectorAll('.ctl-lane.is-drop-target').forEach((el) => el.classList.remove('is-drop-target'));
      if (laneAtPoint) { targetLane = laneAtPoint; if (laneAtPoint !== startLane) laneAtPoint.classList.add('is-drop-target'); }
    };
    const end = () => {
      window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', end);
      window.removeEventListener('touchmove', move); window.removeEventListener('touchend', end);
      grid.querySelectorAll('.ctl-lane.is-drop-target').forEach((el) => el.classList.remove('is-drop-target'));
      barEl.classList.remove('is-moving'); this.movingId.set(null); this.liveTime.set('');
      if (!this.moveDidDrag) return; // reiner Klick -> onBarClick übernimmt
      const newCrewId = targetLane.getAttribute('data-crew-id') || '';
      const dayBase = new Date(this.currentDate()); dayBase.setHours(0, 0, 0, 0);
      const start = new Date(dayBase); start.setMinutes(targetStartMin);
      const endDt = new Date(start.getTime() + durationMin * 60000);
      const payload: GenericRecord = { start_at: this.sqlDateTime(start), end_at: this.sqlDateTime(endDt) };
      if (newCrewId && newCrewId !== String(a['crew_id'] ?? '') && newCrewId !== 'unassigned') payload['crew_id'] = newCrewId;
      if (newCrewId === 'unassigned') payload['crew_id'] = null;
      const crewName = this.crews().find((c) => c['id'] === payload['crew_id'])?.['name'];
      this.assignments.update((items) => items.map((it) => it['id'] === a['id'] ? { ...it, ...payload, crew_name: crewName ?? it['crew_name'] } : it));
      this.api.update('planning_assignments', String(a['id']), payload).subscribe({
        next: () => this.snack.open('Einplanung verschoben', 'OK', { duration: 1500 }),
        error: () => { this.snack.open('Verschieben nur lokal – Server nicht erreichbar', 'OK', { duration: 3000 }); this.load(); }
      });
    };
    window.addEventListener('mousemove', move); window.addEventListener('mouseup', end);
    window.addEventListener('touchmove', move, { passive: false }); window.addEventListener('touchend', end);
  }

  // Balken direkt im Plan kürzen/verlängern (Start- oder Endkante ziehen)
  startResize(a: GenericRecord, edge: 'start' | 'end', event: MouseEvent | TouchEvent): void {
    event.preventDefault(); event.stopPropagation();
    const lane = (event.target as HTMLElement).closest('.ctl-lane') as HTMLElement | null;
    if (!lane) return;
    const rect = lane.getBoundingClientRect();
    const totalMin = this.spanMin();
    const origStart = this.toDate(a['start_at']);
    const origEnd = this.toDate(a['end_at']);
    const dayBase = new Date(this.currentDate()); dayBase.setHours(0, 0, 0, 0);
    const clientX = (e: MouseEvent | TouchEvent) => 'touches' in e ? e.touches[0].clientX : (e as MouseEvent).clientX;
    const minuteAt = (x: number) => {
      const ratio = Math.max(0, Math.min(1, (x - rect.left) / rect.width));
      const raw = this.dayStartHour * 60 + ratio * totalMin;
      return Math.round(raw / 15) * 15; // 15-Minuten-Raster
    };
    const move = (e: MouseEvent | TouchEvent) => {
      const m = minuteAt(clientX(e));
      const el = lane.querySelector('.ctl-bar.is-resizing') as HTMLElement | null;
      if (!el) return;
      if (edge === 'start') {
        const endMin = origEnd.getHours() * 60 + origEnd.getMinutes();
        const startMin = Math.min(m, endMin - 30);
        el.style.left = ((startMin - this.dayStartHour * 60) / totalMin * 100) + '%';
        el.style.width = ((endMin - startMin) / totalMin * 100) + '%';
        (el as any)._newStart = startMin;
        this.setLiveTime(startMin, endMin);
      } else {
        const startMin = origStart.getHours() * 60 + origStart.getMinutes();
        const endMin = Math.max(m, startMin + 30);
        el.style.width = ((endMin - startMin) / totalMin * 100) + '%';
        (el as any)._newEnd = endMin;
        this.setLiveTime(startMin, endMin);
      }
    };
    const barEl = (event.target as HTMLElement).closest('.ctl-bar') as HTMLElement | null;
    if (barEl) barEl.classList.add('is-resizing');
    this.resizingId.set(String(a['id']));
    this.setLiveTime(this.dayMinutes(a['start_at']), this.dayMinutes(a['end_at']));
    const end = () => {
      window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', end);
      window.removeEventListener('touchmove', move); window.removeEventListener('touchend', end);
      const el = lane.querySelector('.ctl-bar.is-resizing') as any;
      if (el) {
        el.classList.remove('is-resizing');
        this.resizingId.set(null); this.liveTime.set('');
        const payload: GenericRecord = {};
        if (edge === 'start' && typeof el._newStart === 'number') { const d = new Date(dayBase); d.setMinutes(el._newStart); payload['start_at'] = this.sqlDateTime(d); }
        if (edge === 'end' && typeof el._newEnd === 'number') { const d = new Date(dayBase); d.setMinutes(el._newEnd); payload['end_at'] = this.sqlDateTime(d); }
        if (Object.keys(payload).length) {
          this.assignments.update((items) => items.map((it) => it['id'] === a['id'] ? { ...it, ...payload } : it));
          this.api.update('planning_assignments', String(a['id']), payload).subscribe({
            next: () => this.snack.open('Zeit angepasst', 'OK', { duration: 1500 }),
            error: () => this.snack.open('Anpassung nur lokal – Server nicht erreichbar', 'OK', { duration: 3000 })
          });
        }
      }
    };
    window.addEventListener('mousemove', move); window.addEventListener('mouseup', end);
    window.addEventListener('touchmove', move, { passive: false }); window.addEventListener('touchend', end);
  }

  // Auftragsdetails beim Anklicken einer Disposition (Seitenpanel wie professionelle Dispo-Tools)
  openDetail(assignment: GenericRecord): void { this.selectedAssignment.set(assignment); }
  closeDetail(): void { this.selectedAssignment.set(null); }
  detailProject(): GenericRecord | null { const a = this.selectedAssignment(); return a ? (this.projects().find((p) => p['id'] === a['project_id']) || null) : null; }
  detailSite(): GenericRecord | null { const a = this.selectedAssignment(); return a ? (this.sites().find((s) => s['id'] === a['site_id']) || null) : null; }
  crewName(crewId: unknown): string { return this.crews().find((c) => c['id'] === crewId)?.['name'] ?? ''; }
  editAssignment(assignment: GenericRecord): void {
    if (!assignment?.['id']) return;
    forkJoin({
      current: this.api.get('planning_assignments', String(assignment['id'])),
      projects: this.api.list('projects', '', 200),
      sites: this.api.list('construction_sites', '', 600),
      crews: this.api.list('crews', '', 200),
      vehicles: this.api.list('vehicles', '', 200),
      machines: this.api.list('machines', '', 200)
    }).subscribe({
      next: ({ current, projects, sites, crews, vehicles, machines }) => {
        const crew = (crews.items || []).find((c) => String(c['id']) === String(current['crew_id'])) || null;
        const ref = this.dialog.open(AssignmentDialogComponent, {
          width: 'min(760px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog',
          data: { projects: projects.items || [], sites: sites.items || [], crews: crews.items || [], vehicles: vehicles.items || [], machines: machines.items || [], initialDate: this.isoDate(this.toDate(current['start_at'])), crew, assignment: current }
        });
        ref.afterClosed().subscribe((payload?: GenericRecord) => {
          if (!payload) return;
          this.api.update('planning_assignments', String(current['id']), payload).subscribe({
            next: () => { this.snack.open('Einplanung aktualisiert', 'OK', { duration: 2200 }); this.closeDetail(); this.load(); },
            error: () => this.snack.open('Aktualisierung fehlgeschlagen', 'OK', { duration: 3200 })
          });
        });
      },
      error: () => this.snack.open('Einplanung konnte nicht aktuell geladen werden.', 'OK', { duration: 3200 })
    });
  }

  printCurrent(): void {
    const m = this.viewMode();
    if (m === 'day') { this.printDay(); return; }
    if (m === 'month') { this.printMonth(); return; }
    this.printPlan();
  }
  printDay(): void {
    const date = this.currentDate();
    const doc = new jsPDF({ unit: 'pt', format: 'a4', orientation: 'landscape' });
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();
    const M = 28; const W = pageW - M * 2;
    let y = M; let page = 1;
    const INK: [number,number,number] = [17,24,39];
    const printedBy = (this.auth.userName || '').trim();
    const footer = () => {
      doc.setDrawColor(0); doc.setLineWidth(0.4); doc.line(M, pageH - 34, pageW - M, pageH - 34);
      doc.setFont('helvetica','normal'); doc.setFontSize(7.5); doc.setTextColor(0);
      doc.text(`Kolonnen-Tagesplan · Seite ${page}`, M, pageH - 22);
      const stamp = `Gedruckt von ${printedBy || 'EstrichWeb'} am ${new Intl.DateTimeFormat('de-DE',{dateStyle:'medium',timeStyle:'short'}).format(new Date())}`;
      doc.text(stamp, pageW - M, pageH - 22, { align: 'right' });
    };

    // ---- Kopfband (Behörden-Stil, schwarz-weiß) ----
    const headH = 46;
    doc.setDrawColor(0); doc.setLineWidth(0.8); doc.rect(M, y, W, headH, 'S');
    doc.setFillColor(0,0,0); doc.rect(M, y, 6, headH, 'F');
    doc.setFont('helvetica','bold'); doc.setFontSize(15); doc.setTextColor(0);
    doc.text('KOLONNEN-TAGESPLAN', M + 16, y + 20);
    doc.setFont('helvetica','normal'); doc.setFontSize(9.5);
    doc.text(this.companyName(), M + 16, y + 36);
    doc.setFont('helvetica','bold'); doc.setFontSize(12);
    doc.text(new Intl.DateTimeFormat('de-DE',{weekday:'long',day:'2-digit',month:'2-digit',year:'numeric'}).format(date), pageW - M - 12, y + 20, { align: 'right' });
    // Statuslegende in Graustufen (Kürzel statt Farbe)
    doc.setFont('helvetica','normal'); doc.setFontSize(7.5); doc.setTextColor(0);
    const legend: [string,string][] = [['G = Geplant',''],['A = In Arbeit',''],['V = Verzögert',''],['M = Mangel',''],['F = Fertig','']];
    doc.text(legend.map((l) => l[0]).join('   ·   '), pageW - M - 12, y + 36, { align: 'right' });
    y += headH + 10;

    const statusLetter = (s: string): string => ({ planned:'G', in_progress:'A', delayed:'V', defect:'M', completed:'F', new:'G' } as Record<string,string>)[s] || 'G';
    const statusGray = (s: string): number => ({ planned:90, in_progress:60, delayed:120, defect:40, completed:165, new:90 } as Record<string,number>)[s] ?? 90;

    // ---- Zeitachsen-Geometrie ----
    const startH = this.dayStartHour, endH = this.dayEndHour;
    const labelW = 150;
    const axisX = M + labelW;
    const axisW = W - labelW;
    const spanMin = (endH - startH) * 60;
    const xForMin = (min: number) => axisX + (Math.max(0, Math.min(spanMin, min - startH * 60)) / spanMin) * axisW;
    const hourW = axisW / (endH - startH);

    const drawHeaderRow = () => {
      const hh = 20;
      doc.setFillColor(235,235,235); doc.rect(M, y, labelW, hh, 'F');
      doc.setDrawColor(0); doc.setLineWidth(0.5); doc.rect(M, y, labelW, hh, 'S');
      doc.setFont('helvetica','bold'); doc.setFontSize(8); doc.setTextColor(0);
      doc.text('KOLONNE', M + 8, y + 13);
      for (let h = startH; h < endH; h++) {
        const x = xForMin(h * 60);
        doc.setFillColor((h % 2) ? 245 : 235, (h % 2) ? 245 : 235, (h % 2) ? 245 : 235);
        doc.rect(x, y, hourW, hh, 'F');
        doc.setDrawColor(0); doc.setLineWidth(0.4); doc.rect(x, y, hourW, hh, 'S');
        doc.setFont('helvetica','bold'); doc.setFontSize(7.5); doc.setTextColor(0);
        doc.text(`${String(h).padStart(2,'0')}:00`, x + 3, y + 13);
      }
      y += hh;
    };
    const ensure = (h: number) => { if (y + h > pageH - 44) { footer(); doc.addPage(); page += 1; y = M; drawHeaderRow(); } };
    drawHeaderRow();

    const crews = this.filteredCrewsWithUnassigned();
    crews.forEach((crew) => {
      const laid = this.laidOut(crew);
      const rows = Math.max(1, laid.reduce((m, a) => Math.max(m, (Number(a['_row']) || 0) + 1), 1));
      const rowH = 34; const laneH = rows * rowH + 8;
      ensure(laneH);
      // Stundenraster
      for (let h = startH; h <= endH; h++) {
        const x = xForMin(h * 60);
        doc.setDrawColor(150,150,150); doc.setLineWidth(0.4); doc.line(x, y, x, y + laneH);
        if (h < endH) { const xh = xForMin(h * 60 + 30); doc.setDrawColor(220,220,220); doc.setLineWidth(0.25); doc.line(xh, y, xh, y + laneH); }
      }
      // Kolonnen-Label
      doc.setFillColor(244,244,244); doc.rect(M, y, labelW, laneH, 'F');
      doc.setDrawColor(0); doc.setLineWidth(0.5); doc.rect(M, y, labelW, laneH, 'S');
      doc.setFont('helvetica','bold'); doc.setFontSize(9); doc.setTextColor(0);
      doc.text((doc.splitTextToSize(String(crew['name'] || 'Kolonne'), labelW - 12) as string[]).slice(0,2), M + 8, y + 14);
      if (crew['capacity_m2_day']) { doc.setFont('helvetica','normal'); doc.setFontSize(7); doc.setTextColor(90); doc.text(`${crew['capacity_m2_day']} m²/Tag`, M + 8, y + laneH - 6); }
      doc.setTextColor(0);
      doc.setDrawColor(0); doc.setLineWidth(0.5); doc.rect(axisX, y, axisW, laneH, 'S');
      // Balken (weiß mit schwarzer Kante, Status als Kürzel + Graustufen-Kante links)
      laid.forEach((a) => {
        const sMin = this.dayMinutes(a['start_at']);
        const eMin = Math.max(sMin + 30, this.dayMinutes(a['end_at']));
        const bx = xForMin(sMin); const bw = Math.max(40, xForMin(eMin) - bx);
        const by = y + 4 + (Number(a['_row']) || 0) * rowH; const bh = rowH - 6;
        const st = String(a['status'] || 'planned'); const g = statusGray(st);
        doc.setFillColor(255,255,255); doc.setDrawColor(0); doc.setLineWidth(0.5);
        doc.rect(bx, by, bw, bh, 'FD');
        doc.setFillColor(g,g,g); doc.rect(bx, by, 4, bh, 'F'); // Status-Graustufe als Kante
        // Status-Kürzel oben rechts
        doc.setFont('helvetica','bold'); doc.setFontSize(7); doc.setTextColor(0);
        doc.text(statusLetter(st), bx + bw - 8, by + 8);
        // Inhalt
        const title = String(a['title'] || this.projectLabel(a['project_id']));
        const site = String(a['site_name'] || this.siteLabel(a['site_id']));
        const addr = this.assignmentSiteAddress(a);
        doc.setFont('helvetica','bold'); doc.setFontSize(7.2); doc.setTextColor(0);
        doc.text((doc.splitTextToSize(`${this.timeLabel(a['start_at'])}–${this.timeLabel(a['end_at'])} ${title}`, bw - 14) as string[]).slice(0,1), bx + 7, by + 9);
        doc.setFont('helvetica','normal'); doc.setFontSize(6.6);
        doc.text((doc.splitTextToSize(site, bw - 14) as string[]).slice(0,1), bx + 7, by + 17);
        if (addr && addr !== '—') doc.text((doc.splitTextToSize(addr, bw - 14) as string[]).slice(0,1), bx + 7, by + 25);
      });
      y += laneH;
    });
    footer();
    doc.save(`Kolonnen-Tagesplan_${this.isoDate(date)}.pdf`);
  }
  printCurrentLabel(): string {
    const m = this.viewMode();
    return m === 'day' ? 'Tages-PDF' : m === 'month' ? 'Monats-PDF' : 'Wochen-PDF';
  }

  /**
   * Wochen-PDF des Dispositionsplans.
   *
   * Gegenüber der früheren Fassung: Zeilenhöhe richtet sich nach dem vollsten
   * Tag (kein "+ 2 weitere" mehr), farbige Streifen je Einsatzart, Kopf- und
   * Summenzeile sowie eine Auslastungsspalte je Kolonne.
   */
  printPlan(): void {
    const days = this.weekDays();
    const kw = this.calendarWeek(days[0].date);
    const doc = new jsPDF({ unit: 'pt', format: 'a4', orientation: 'landscape' });
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();

    const M = 28;                       // Seitenrand
    const HEAD_H = 40;                  // Titelband
    const COL_H = 22;                   // Spaltenkopf
    const CREW_W = 118;                 // Kolonnenspalte
    const SUM_W = 44;                   // Summenspalte
    const PAD = 5;                      // Innenabstand der Zellen
    const LINE = 8.4;                   // Zeilenabstand im Eintrag
    const ITEM_GAP = 5;                 // Abstand zwischen zwei Einträgen
    const dayW = (pageW - M * 2 - CREW_W - SUM_W) / days.length;
    const FOOT_Y = pageH - 24;

    let page = 1;
    let y = 0;

    const setInk = (r: number, g: number, b: number) => doc.setTextColor(r, g, b);
    /** Text auf die verfügbare Breite kürzen – jsPDF bricht sonst nicht um. */
    const fit = (text: string, width: number): string => {
      let value = String(text ?? '').replace(/\s+/g, ' ').trim();
      if (!value) return '';
      if (doc.getTextWidth(value) <= width) return value;
      while (value.length > 1 && doc.getTextWidth(value + '…') > width) value = value.slice(0, -1);
      return value + '…';
    };
    const kindRgb = (a: GenericRecord): [number, number, number] => {
      const t = String(a['title'] || '').toLowerCase();
      if (/begehung|besichtigung|kontrolle|abnahme|aufma|messung|kundentermin/.test(t)) return [180, 83, 9];
      if (/estrich|arbeit/.test(t)) return [37, 87, 166];
      return [100, 116, 139];
    };
    /** Höhe eines Eintrags: Zeit+Titel, Baustelle, optional Adresse. */
    const itemHeight = (a: GenericRecord): number => (this.assignmentSiteAddress(a) ? 3 : 2) * LINE + ITEM_GAP;

    const footer = () => {
      doc.setDrawColor(226, 232, 240); doc.setLineWidth(0.5);
      doc.line(M, FOOT_Y - 10, pageW - M, FOOT_Y - 10);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); setInk(130, 130, 130);
      doc.text('Blau: Estricharbeiten   ·   Orange: Termin/Begehung   ·   Grau: Sonstiges', M, FOOT_Y);
      doc.text(`Seite ${page}`, pageW / 2, FOOT_Y, { align: 'center' });
      doc.text(`${(this.auth.userName || 'EstrichWeb').trim()} · ${new Intl.DateTimeFormat('de-DE', { dateStyle: 'short', timeStyle: 'short' }).format(new Date())}`,
               pageW - M, FOOT_Y, { align: 'right' });
      setInk(0, 0, 0);
    };

    const drawHead = () => {
      y = M;
      doc.setFillColor(20, 57, 111);
      doc.rect(M, y, pageW - M * 2, HEAD_H, 'F');
      doc.setFont('helvetica', 'bold'); doc.setFontSize(15); setInk(255, 255, 255);
      doc.text('Dispositionsplan', M + 14, y + 18);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(9);
      doc.text(fit(this.companyName() || 'EstrichWeb', 300), M + 14, y + 31);
      doc.setFont('helvetica', 'bold'); doc.setFontSize(13);
      doc.text(`KW ${kw}`, pageW - M - 14, y + 18, { align: 'right' });
      doc.setFont('helvetica', 'normal'); doc.setFontSize(9);
      doc.text(`${this.formatDate(days[0].date)} – ${this.formatDate(days[days.length - 1].date)}`, pageW - M - 14, y + 31, { align: 'right' });
      setInk(0, 0, 0);
      y += HEAD_H;

      // Spaltenkopf
      doc.setFillColor(238, 242, 247);
      doc.rect(M, y, pageW - M * 2, COL_H, 'F');
      doc.setFont('helvetica', 'bold'); doc.setFontSize(7.5); setInk(52, 64, 84);
      doc.text('KOLONNE', M + PAD, y + 14);
      const todayIso = this.isoDate(new Date());
      days.forEach((day, i) => {
        const x = M + CREW_W + i * dayW;
        if (day.key === todayIso) { doc.setFillColor(219, 231, 250); doc.rect(x, y, dayW, COL_H, 'F'); }
        doc.setFont('helvetica', 'bold'); doc.setFontSize(7.5); setInk(52, 64, 84);
        doc.text(`${this.weekdayLabel(day.date)} ${this.formatDate(day.date).slice(0, 5)}`, x + PAD, y + 14);
      });
      doc.text('EINS.', M + CREW_W + days.length * dayW + PAD, y + 14);
      doc.setDrawColor(203, 213, 225); doc.setLineWidth(0.6);
      doc.line(M, y + COL_H, pageW - M, y + COL_H);
      setInk(0, 0, 0);
      y += COL_H;
    };

    const nextPage = () => { footer(); doc.addPage(); page += 1; drawHead(); };

    drawHead();
    const crews = this.filteredCrewsWithUnassigned();
    let total = 0;

    crews.forEach((crew, ci) => {
      const perDay = days.map((d) => this.assignmentsFor(crew, d.date));
      const tallest = Math.max(...perDay.map((list) => list.reduce((sum, a) => sum + itemHeight(a), 0)), 0);
      const rowH = Math.max(40, tallest + PAD * 2);
      const count = perDay.reduce((sum, l) => sum + l.length, 0);
      total += count;

      if (y + rowH > FOOT_Y - 24) nextPage();

      // Zeilenhintergrund im Wechsel
      if (ci % 2 === 1) { doc.setFillColor(250, 251, 253); doc.rect(M, y, pageW - M * 2, rowH, 'F'); }

      // Kolonnenspalte
      doc.setFont('helvetica', 'bold'); doc.setFontSize(8.5); setInk(16, 24, 40);
      doc.text(fit(String(crew['name'] || 'Kolonne'), CREW_W - PAD * 2), M + PAD, y + 14);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(6.8); setInk(120, 128, 140);
      const members = this.crewMembersLabel(crew);
      if (members) doc.text(fit(members, CREW_W - PAD * 2), M + PAD, y + 24);
      if (crew['capacity_m2_day']) doc.text(`${crew['capacity_m2_day']} m²/Tag`, M + PAD, y + (members ? 33 : 24));
      setInk(0, 0, 0);

      // Tagesspalten
      perDay.forEach((items, i) => {
        const x = M + CREW_W + i * dayW;
        const innerW = dayW - PAD * 2 - 7;
        if (!items.length) {
          doc.setFont('helvetica', 'italic'); doc.setFontSize(7); setInk(190, 195, 202);
          doc.text('frei', x + PAD, y + 14); setInk(0, 0, 0);
          return;
        }
        let iy = y + PAD;
        items.forEach((a) => {
          const h = itemHeight(a) - ITEM_GAP;
          const [r, g, b] = kindRgb(a);
          doc.setFillColor(r, g, b);
          doc.rect(x + PAD, iy + 1.5, 2, h, 'F');
          const tx = x + PAD + 7;
          doc.setFont('helvetica', 'bold'); doc.setFontSize(7); setInk(16, 24, 40);
          doc.text(fit(`${this.timeLabel(a['start_at'])}–${this.timeLabel(a['end_at'])}  ${a['title'] || this.projectLabel(a['project_id'])}`, innerW), tx, iy + 7);
          doc.setFont('helvetica', 'normal'); doc.setFontSize(6.6); setInk(85, 95, 110);
          doc.text(fit(String(a['site_name'] || this.siteLabel(a['site_id'])), innerW), tx, iy + 7 + LINE);
          const addr = this.assignmentSiteAddress(a);
          if (addr && addr !== '—') {
            setInk(150, 158, 168);
            doc.text(fit(addr, innerW), tx, iy + 7 + LINE * 2);
          }
          setInk(0, 0, 0);
          iy += itemHeight(a);
        });
      });

      // Summenspalte
      const sx = M + CREW_W + days.length * dayW;
      doc.setFont('helvetica', 'bold'); doc.setFontSize(12); setInk(16, 24, 40);
      doc.text(String(count), sx + SUM_W / 2, y + 20, { align: 'center' });
      setInk(0, 0, 0);

      // Rasterlinien
      doc.setDrawColor(226, 232, 240); doc.setLineWidth(0.4);
      doc.line(M, y + rowH, pageW - M, y + rowH);
      doc.line(M + CREW_W, y, M + CREW_W, y + rowH);
      days.forEach((_, i) => {
        const x = M + CREW_W + (i + 1) * dayW;
        doc.line(x, y, x, y + rowH);
      });
      y += rowH;
    });

    // Abschluss
    if (y + 20 > FOOT_Y - 20) nextPage();
    doc.setFillColor(238, 242, 247);
    doc.rect(M, y, pageW - M * 2, 18, 'F');
    doc.setFont('helvetica', 'bold'); doc.setFontSize(7.5); setInk(52, 64, 84);
    doc.text(`${crews.length} Kolonnen  ·  ${total} Einsätze  ·  KW ${kw}`, M + PAD, y + 12);
    setInk(0, 0, 0);

    footer();
    doc.save(`Dispositionsplan_KW${kw}_${days[0].key}.pdf`);
  }

  /** Monats-PDF: Kalenderraster mit Auslastung plus Ressourcenübersicht. */
  printMonth(): void {
    const doc = new jsPDF({ unit: 'pt', format: 'a4', orientation: 'landscape' });
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();
    const M = 24;
    const cells = this.monthCells();

    doc.setFillColor(20, 57, 111);
    doc.rect(M, M, pageW - M * 2, 34, 'F');
    doc.setFont('helvetica', 'bold'); doc.setFontSize(14); doc.setTextColor(255);
    doc.text('Monatsplanung', M + 12, M + 15);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5);
    doc.text(this.companyName() || 'EstrichWeb', M + 12, M + 27);
    doc.setFont('helvetica', 'bold'); doc.setFontSize(12);
    doc.text(this.monthLabel(), pageW - M - 12, M + 15, { align: 'right' });
    doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5);
    doc.text(`${this.monthAssignmentCount()} Einsätze · ${this.monthBusyDays()} belegte Tage`, pageW - M - 12, M + 27, { align: 'right' });
    doc.setTextColor(0);

    let y = M + 46;
    const colW = (pageW - M * 2) / 7;
    doc.setFont('helvetica', 'bold'); doc.setFontSize(8); doc.setTextColor(90);
    this.monthWeekdayLabels.forEach((wd, i) => doc.text(wd, M + i * colW + 5, y));
    doc.setTextColor(0);
    y += 8;

    const rowH = 52;
    let col = 0;
    for (const cell of cells) {
      const x = M + col * colW;
      if (cell.date) {
        doc.setDrawColor(214); doc.setLineWidth(0.4);
        doc.rect(x, y, colW - 3, rowH, 'S');
        doc.setFont('helvetica', 'bold'); doc.setFontSize(9); doc.setTextColor(16, 24, 40);
        doc.text(String(cell.day), x + 5, y + 13);
        if (cell.count) {
          // Auslastungsbalken
          const barW = (colW - 13) * (cell.loadPercent / 100);
          const color: [number, number, number] = cell.loadClass === 'over' ? [201, 67, 58]
            : cell.loadClass === 'high' ? [29, 78, 216]
            : cell.loadClass === 'mid' ? [96, 165, 250] : [191, 219, 254];
          doc.setFillColor(238, 242, 246); doc.rect(x + 5, y + 18, colW - 13, 4, 'F');
          doc.setFillColor(...color); doc.rect(x + 5, y + 18, Math.max(1, barW), 4, 'F');
          doc.setFont('helvetica', 'normal'); doc.setFontSize(7); doc.setTextColor(60);
          doc.text(`${cell.count} Einsatz(e)`, x + 5, y + 32);
          doc.setTextColor(140); doc.setFontSize(6.4);
          doc.text(`${cell.crewCount} / ${this.crews().length} Kolonnen`, x + 5, y + 41);
          if (cell.inspections) doc.text(`${cell.inspections} Termin(e)`, x + 5, y + 48);
        } else {
          doc.setFont('helvetica', 'italic'); doc.setFontSize(7); doc.setTextColor(180);
          doc.text('frei', x + 5, y + 30);
        }
        doc.setTextColor(0);
      }
      col += 1;
      if (col === 7) { col = 0; y += rowH + 3; }
    }

    // Ressourcen
    if (col !== 0) y += rowH + 3;
    const blocks: { title: string; kind: 'crew' | 'vehicle' | 'machine' }[] = [
      { title: 'Kolonnen', kind: 'crew' },
      { title: 'Fahrzeuge', kind: 'vehicle' },
      { title: 'Maschinen & Pumpen', kind: 'machine' }
    ];
    const blockW = (pageW - M * 2) / 3;
    if (y + 90 > pageH - 40) { doc.addPage(); y = M; }
    doc.setFont('helvetica', 'bold'); doc.setFontSize(9); doc.setTextColor(45);
    doc.text('Ressourcen im Monat', M, y); y += 12;
    doc.setTextColor(0);

    blocks.forEach((block, bi) => {
      const bx = M + bi * blockW;
      let by = y;
      doc.setFont('helvetica', 'bold'); doc.setFontSize(8); doc.setTextColor(70);
      doc.text(block.title, bx, by); by += 10;
      doc.setFont('helvetica', 'normal'); doc.setFontSize(7); doc.setTextColor(0);
      const rows = this.monthResourceLoad(block.kind).slice(0, 8);
      if (!rows.length) { doc.setTextColor(160); doc.text('keine Einträge', bx, by); doc.setTextColor(0); return; }
      rows.forEach((row) => {
        doc.text((doc.splitTextToSize(row.name, blockW - 80) as string[])[0] || '', bx, by);
        doc.setFillColor(238, 242, 246); doc.rect(bx + blockW - 76, by - 5, 50, 4, 'F');
        const color: [number, number, number] = row.loadClass === 'over' ? [201, 67, 58]
          : row.loadClass === 'high' ? [29, 78, 216]
          : row.loadClass === 'mid' ? [96, 165, 250] : [191, 219, 254];
        doc.setFillColor(...color); doc.rect(bx + blockW - 76, by - 5, Math.max(1, 50 * row.percent / 100), 4, 'F');
        doc.text(`${row.days} Tg.`, bx + blockW - 20, by);
        by += 10;
      });
    });

    doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); doc.setTextColor(110);
    doc.text(`${this.auth.userName || 'EstrichWeb'} · ${new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date())}`,
             pageW - M, pageH - 20, { align: 'right' });
    doc.text('Rot = Doppelbelegung an mindestens einem Tag', M, pageH - 20);

    doc.save(`Monatsplanung_${this.isoDate(this.currentDate()).slice(0, 7)}.pdf`);
  }

  crewMembersLabel(crew: GenericRecord): string {
    if (!crew || crew['id'] === 'unassigned') return '';
    return this.crewMembers().filter((member) => member['crew_id'] === crew['id']).map((member) => String(member['name'] || member['user_name'] || '').trim()).filter(Boolean).slice(0, 5).join(', ');
  }

  calendarWeek(date: Date): number {
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    const day = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - day);
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
    return Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
  }
  formatDate(date: Date): string { return `${String(date.getDate()).padStart(2, '0')}.${String(date.getMonth() + 1).padStart(2, '0')}.${date.getFullYear()}`; }

  weekdayLabel(date: Date): string { return ['Mo','Di','Mi','Do','Fr','Sa','So'][(date.getDay() + 6) % 7]; }
  weekRangeLabel(): string {
    const days = this.weekDays();
    const first = days[0].date; const last = days[days.length - 1].date;
    const months = ['Jan','Feb','Mär','Apr','Mai','Jun','Jul','Aug','Sep','Okt','Nov','Dez'];
    return `${first.getDate()}. ${months[first.getMonth()]} – ${last.getDate()}. ${months[last.getMonth()]} ${last.getFullYear()}`;
  }
  isToday(date: Date): boolean { return this.isoDate(date) === this.isoDate(new Date()); }
  filteredProjectOptions(): GenericRecord[] {
    const needle = this.normalizePlanner(this.projectFilterSearch());
    if (!needle) return this.projects();
    return this.projects().filter((project) => this.normalizePlanner([project['project_no'], project['name'], project['city'], project['zip']].join(' ')).includes(needle));
  }
  normalizePlanner(value: unknown): string { return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }
  projectLabel(projectId: unknown): string { return this.projects().find((project) => project['id'] === projectId)?.['name'] ?? 'ohne Auftrag'; }
  siteLabel(siteId: unknown): string { return this.sites().find((site) => site['id'] === siteId)?.['name'] ?? 'Baustelle offen'; }
  /**
   * Zeilen einer Karte – ohne Wiederholungen.
   *
   * Der Titel lautet üblicherweise "2026-010 · Test BV 2" und enthält damit
   * bereits Aktennummer und Baustellenname. Bauakte, Baustelle und Adresse
   * wurden trotzdem eigenständig darunter gesetzt. Bei gleichlautenden Namen
   * stand dasselbe mehrfach auf einer ohnehin schmalen Karte.
   *
   * Hier wird jede Angabe nur übernommen, wenn sie noch nicht im Titel oder
   * in einer vorherigen Zeile vorkommt.
   */
  cardLines(assignment: GenericRecord): string[] {
    const title = String(assignment['title'] || '').toLowerCase();
    const seen = new Set<string>();
    const out: string[] = [];

    const add = (value: unknown) => {
      const text = String(value ?? '').trim();
      if (!text) return;
      const key = text.toLowerCase();
      if (seen.has(key)) return;
      // Steht die Angabe bereits im Titel? Dann ist sie überflüssig.
      if (title.includes(key)) { seen.add(key); return; }
      seen.add(key);
      out.push(text);
    };

    add(assignment['project_name'] || this.projectLabel(assignment['project_id']));
    add(assignment['site_name'] || this.siteLabel(assignment['site_id']));
    add(this.assignmentSiteAddress(assignment));
    // Auf der schmalen Wochenkarte reichen zwei Zeilen; mehr wird
    // abgeschnitten und hilft niemandem.
    return out.slice(0, 2);
  }

  assignmentSiteAddress(assignment: GenericRecord): string {
    const site = this.sites().find((item) => item['id'] === assignment['site_id']);
    const direct = [assignment['site_address'] || assignment['address'], assignment['site_zip'] || assignment['zip'], assignment['site_city'] || assignment['city']].filter(Boolean).join(' ');
    const fromSite = [site?.['address'], site?.['zip'], site?.['city']].filter(Boolean).join(' ');
    return direct || fromSite || '—';
  }

  private toDate(value: unknown): Date { return new Date(String(value ?? new Date().toISOString()).replace(' ', 'T')); }
  private isoDate(date: Date): string {
    // Use LOCAL date components (not toISOString, which is UTC). In CET/CEST the
    // UTC conversion rolled local midnight back a day, so planned assignments
    // never matched their day-column and appeared to vanish from the plan.
    const y = date.getFullYear(); const m = String(date.getMonth() + 1).padStart(2, '0'); const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }
  private sqlDateTime(date: Date): string { return `${this.isoDate(date)} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}:00`; }
}
