import { DatePipe, NgClass } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { forkJoin } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { AssignmentDialogComponent } from '../crew-week-planner/crew-week-planner.component';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';

type BoardMode = 'dispatcher' | 'ressourcen' | 'konflikte' | 'mobil';

@Component({
  standalone: true,
  imports: [DatePipe, NgClass, DragDropModule, MatCardModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatInputModule, MatChipsModule, MatTooltipModule, MatDialogModule, MatSnackBarModule],
  template: `
    <section class="page-head compact-head plantafel-hero">
      <div>
        <p class="eyebrow">Disposition · Plantafel · Ressourcensteuerung</p>
        <h1><mat-icon>view_timeline</mat-icon>Dispatcher-Plantafel</h1>
      </div>
      <div class="head-actions">
        @if (canPlan) { <button mat-flat-button color="primary" (click)="openAssignment()"><mat-icon>add</mat-icon>Einplanung</button> }
        <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon>Aktualisieren</button>
        <button mat-stroked-button (click)="printBoard()"><mat-icon>picture_as_pdf</mat-icon>PDF</button>
      </div>
    </section>

    <section class="plantafel-toolbar">
      <div class="plantafel-mode-switch" role="group" aria-label="Plantafel Ansicht">
        <button mat-stroked-button [class.active]="mode() === 'dispatcher'" (click)="mode.set('dispatcher')"><mat-icon>dashboard_customize</mat-icon>Dispatcher</button>
        <button mat-stroked-button [class.active]="mode() === 'ressourcen'" (click)="mode.set('ressourcen')"><mat-icon>groups</mat-icon>Ressourcen</button>
        <button mat-stroked-button [class.active]="mode() === 'konflikte'" (click)="mode.set('konflikte')"><mat-icon>warning</mat-icon>Konflikte</button>
        <button mat-stroked-button [class.active]="mode() === 'mobil'" (click)="mode.set('mobil')"><mat-icon>phone_iphone</mat-icon>Mobil</button>
      </div>
      <span class="plantafel-date-nav">
        <button mat-stroked-button (click)="shiftWeek(-1)"><mat-icon>chevron_left</mat-icon>Vorwoche</button>
        <button mat-stroked-button (click)="today()">Heute</button>
        <button mat-stroked-button (click)="shiftWeek(1)">Nächste Woche<mat-icon>chevron_right</mat-icon></button>
      </span>
      <strong class="week-label">{{ weekRangeLabel() }}</strong>
    </section>

    <section class="plantafel-filterbar">
      <mat-form-field appearance="outline">
        <mat-label>Bauakte filtern</mat-label>
        <mat-select [value]="projectFilter()" (selectionChange)="projectFilter.set($event.value)">
          <mat-option value="">Alle Bauakten</mat-option>
          @for (project of projects(); track project['id']) {
            <mat-option [value]="project['id']">{{ project['project_no'] || 'Auftrag' }} · {{ project['name'] }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Kolonne filtern</mat-label>
        <mat-select [value]="crewFilter()" (selectionChange)="crewFilter.set($event.value)">
          <mat-option value="">Alle Kolonnen</mat-option>
          @for (crew of crews(); track crew['id']) { <mat-option [value]="crew['id']">{{ crew['name'] }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Status</mat-label>
        <mat-select [value]="statusFilter()" (selectionChange)="statusFilter.set($event.value)">
          <mat-option value="">Alle Status</mat-option>
          @for (status of statusOptions; track status.key) { <mat-option [value]="status.key">{{ status.label }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" class="plantafel-search-field">
        <mat-label>Suchen</mat-label>
        <input matInput [value]="search()" (input)="search.set($any($event.target).value)" placeholder="Baustelle, Auftrag, Notiz">
      </mat-form-field>
    </section>

    <section class="plantafel-kpis">
      <mat-card class="square-card plantafel-kpi"><mat-icon>event_available</mat-icon><b>{{ visibleAssignments().length }}</b><span>Termine</span></mat-card>
      <mat-card class="square-card plantafel-kpi"><mat-icon>groups</mat-icon><b>{{ activeCrewCount() }}</b><span>aktive Kolonnen</span></mat-card>
      <mat-card class="square-card plantafel-kpi"><mat-icon>priority_high</mat-icon><b>{{ backlog().length }}</b><span>ungeplant</span></mat-card>
      <mat-card class="square-card plantafel-kpi"><mat-icon>warning</mat-icon><b>{{ conflictList().length }}</b><span>Konflikte</span></mat-card>
      <mat-card class="square-card plantafel-kpi"><mat-icon>local_shipping</mat-icon><b>{{ vehicleUsageCount() }}</b><span>Fahrzeuge</span></mat-card>
    </section>

    @if (mode() === 'mobil') {
      <mat-card class="square-card mobile-plantafel-hint">
        <mat-icon>phone_iphone</mat-icon>
        <div><h3>Mobile Disposition</h3></div>
        <button mat-flat-button color="primary" (click)="printBoard()"><mat-icon>picture_as_pdf</mat-icon>Plan als PDF</button>
      </mat-card>
      <section class="plantafel-mobile-days">
        @for (day of weekDays(); track day.key) {
          <mat-card class="square-card plantafel-mobile-day">
            <h3>{{ weekdayLabel(day.date) }} · {{ day.date | date:'dd.MM.yyyy' }}</h3>
            @for (assignment of assignmentsForDay(day.date); track assignment['id']) {
              <article class="plantafel-mobile-item">
                <b>{{ assignment['title'] }}</b>
                <span>{{ timeLabel(assignment['start_at']) }}–{{ timeLabel(assignment['end_at']) }} · {{ crewLabel(assignment['crew_id']) }}</span>
                <small>{{ projectLabel(assignment['project_id']) }} · {{ siteLabel(assignment['site_id']) }}</small>
              </article>
            } @empty { <p class="muted">Keine Termine.</p> }
          </mat-card>
        }
      </section>
    } @else {
      <section class="plantafel-workspace" [ngClass]="'mode-' + mode()">
        <mat-card class="square-card plantafel-backlog-card">
          <div class="card-headline"><h3>Rückstand / ungeplant</h3><span>{{ backlog().length }} offen</span></div>
          <div class="plantafel-backlog-list" cdkDropList [cdkDropListData]="backlog()" (cdkDropListDropped)="dropToBacklog($event)">
            @for (item of backlog(); track item['id']) { <article class="plantafel-task-card" cdkDrag>{{ assignmentCard(item) }}</article> }
            @empty { <div class="empty-pane"><mat-icon>task_alt</mat-icon><span>Keine ungeplanten Einsätze</span></div> }
          </div>
        </mat-card>

        <mat-card class="square-card plantafel-main-card">
          @if (mode() === 'konflikte') {
            <div class="card-headline"><h3>Konflikt- und Qualitätscheck</h3><span>{{ conflictList().length }} Hinweis(e)</span></div>
            <section class="plantafel-conflict-list">
              @for (conflict of conflictList(); track conflict.key) {
                <article class="plantafel-conflict-card" [ngClass]="conflict.severity">
                  <mat-icon>{{ conflict.icon }}</mat-icon>
                  <div><b>{{ conflict.title }}</b><span>{{ conflict.detail }}</span></div>
                  <button mat-stroked-button (click)="focusDay(conflict.date)">Tag anzeigen</button>
                </article>
              } @empty { <div class="empty-pane"><mat-icon>verified</mat-icon><span>Keine sichtbaren Konflikte in dieser Woche.</span></div> }
            </section>
          } @else {
            <div class="card-headline plantafel-grid-headline"><h3>{{ mode() === 'ressourcen' ? 'Ressourcenmatrix' : 'Wochen-Dispatcher' }}</h3><span>{{ weekRangeLabel() }}</span></div>
            <section class="plantafel-grid" cdkDropListGroup>
              <div class="plantafel-corner">{{ mode() === 'ressourcen' ? 'Ressource' : 'Kolonne' }}</div>
              @for (day of weekDays(); track day.key) {
                <div class="plantafel-day-head" [class.today]="isToday(day.date)"><b>{{ weekdayLabel(day.date) }}</b><span>{{ day.date | date:'dd.MM.' }}</span><small>{{ assignmentsForDay(day.date).length }} Termin(e)</small></div>
              }
              @for (resource of resourcesForMode(); track resource['id']) {
                <div class="plantafel-resource-head">
                  <b>{{ resource['name'] }}</b>
                  <small>{{ resourceSubtitle(resource) }}</small>
                  <button mat-stroked-button (click)="openAssignment(resource)"><mat-icon>add</mat-icon>Termin</button>
                </div>
                @for (day of weekDays(); track day.key) {
                  <div class="plantafel-cell" cdkDropList [cdkDropListData]="assignmentsForResourceDay(resource, day.date)" (cdkDropListDropped)="dropToCell($event, resource, day.date)" (dblclick)="openAssignment(resource, day.date)">
                    @for (assignment of assignmentsForResourceDay(resource, day.date); track assignment['id']) {
                      <article class="plantafel-assignment-card" [ngClass]="'status-' + (assignment['status'] || 'planned')" cdkDrag (dblclick)="editAssignment(assignment, $event)">
                        <header><b>{{ assignment['title'] }}</b><span class="plantafel-card-actions">@if (canPlan) { <button type="button" mat-icon-button class="edit-inline-btn" (click)="editAssignment(assignment, $event)" matTooltip="Einplanung bearbeiten"><mat-icon>edit</mat-icon></button> }@if (canDeletePlan) { <button type="button" mat-icon-button class="delete-inline-btn square-delete" (click)="deleteAssignment(assignment, $event)" matTooltip="Einplanung löschen"><mat-icon>delete</mat-icon></button> }</span></header>
                        <span>{{ timeLabel(assignment['start_at']) }}–{{ timeLabel(assignment['end_at']) }}</span>
                        <small>{{ projectLabel(assignment['project_id']) }}</small>
                        <small>{{ siteLabel(assignment['site_id']) }}</small>
                        @if (mode() === 'ressourcen') { <em>{{ crewLabel(assignment['crew_id']) }}</em> }
                      </article>
                    }
                    <button type="button" class="plantafel-cell-add" (click)="openAssignment(resource, day.date); $event.stopPropagation()"><mat-icon>add</mat-icon></button>
                  </div>
                }
              }
            </section>
          }
        </mat-card>

        <mat-card class="square-card plantafel-side-card">
          <div class="card-headline"><h3>Tageslast</h3><span>Kapazität</span></div>
          @for (day of weekDays(); track day.key) {
            <div class="day-load-row">
              <span>{{ weekdayLabel(day.date) }}</span>
              <strong>{{ assignmentsForDay(day.date).length }}</strong>
              <meter min="0" max="8" [value]="Math.min(assignmentsForDay(day.date).length, 8)"></meter>
            </div>
          }
          <hr>
          <h3>Schnellaktionen</h3>
          @if (canPlan) { <button mat-stroked-button (click)="openAssignment()"><mat-icon>add</mat-icon>Einplanung anlegen</button> }
          <button mat-stroked-button (click)="mode.set('konflikte')"><mat-icon>rule</mat-icon>Konflikte prüfen</button>
          <button mat-stroked-button (click)="printBoard()"><mat-icon>print</mat-icon>Plan drucken</button>
        </mat-card>
      </section>
    }
  `
})
export class PlanningBoardComponent {

  private readonly auth = inject(AuthService);
  readonly canPlan = this.auth.canWrite('planning');
  readonly canDeletePlan = this.auth.canDelete('planning');  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  readonly Math = Math;

  readonly mode = signal<BoardMode>('dispatcher');
  readonly currentDate = signal(new Date());
  readonly projectFilter = signal('');
  readonly crewFilter = signal('');
  readonly statusFilter = signal('');
  readonly search = signal('');

  readonly projects = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly crews = signal<GenericRecord[]>([]);
  readonly vehicles = signal<GenericRecord[]>([]);
  readonly machines = signal<GenericRecord[]>([]);
  readonly assignments = signal<GenericRecord[]>([]);

  readonly statusOptions = [
    { key: 'new', label: 'Neu' }, { key: 'planned', label: 'Geplant' }, { key: 'in_progress', label: 'In Arbeit' },
    { key: 'delayed', label: 'Verzögert' }, { key: 'defect', label: 'Mangel' }, { key: 'completed', label: 'Abgeschlossen' }
  ];

  readonly visibleAssignments = computed(() => {
    const q = this.normalize(this.search());
    return this.assignments()
      .filter((a) => !this.projectFilter() || a['project_id'] === this.projectFilter())
      .filter((a) => !this.crewFilter() || a['crew_id'] === this.crewFilter())
      .filter((a) => !this.statusFilter() || String(a['status'] || 'planned') === this.statusFilter())
      .filter((a) => !q || this.normalize([a['title'], a['notes'], a['project_name'], this.projectLabel(a['project_id']), a['site_name'], this.siteLabel(a['site_id']), this.crewLabel(a['crew_id'])].join(' ')).includes(q))
      .sort((a, b) => String(a['start_at']).localeCompare(String(b['start_at'])));
  });
  readonly backlog = computed(() => this.visibleAssignments().filter((a) => !a['crew_id'] || !a['start_at']));
  readonly conflictList = computed(() => this.buildConflicts());

  constructor() { this.load(); }

  load(): void {
    const days = this.weekDays();
    forkJoin({
      projects: this.api.list('projects', '', 150),
      sites: this.api.list('construction_sites', '', 500),
      crews: this.api.list('crews', '', 150),
      vehicles: this.api.list('vehicles', '', 150),
      machines: this.api.list('machines', '', 150),
      assignments: this.api.planningAssignments(days[0].key, days[6].key)
    }).subscribe({
      next: (res) => {
        this.projects.set(res.projects.items || []); this.sites.set(res.sites.items || []); this.crews.set(res.crews.items || []);
        this.vehicles.set(res.vehicles.items || []); this.machines.set(res.machines.items || []); this.assignments.set(res.assignments.items || []);
      },
      error: () => this.snack.open('Plantafel konnte nicht geladen werden.', 'OK', { duration: 3000 })
    });
  }

  openAssignment(resource?: GenericRecord, date?: Date): void {
    const ref = this.dialog.open(AssignmentDialogComponent, {
      width: 'min(760px, 96vw)', panelClass: 'ew-dialog',
      data: { projects: this.projects(), sites: this.sites(), crews: this.crews(), vehicles: this.vehicles(), machines: this.machines(), initialDate: this.isoDate(date || new Date()), crew: this.resourceToCrew(resource) }
    });
    ref.afterClosed().subscribe((payload) => {
      if (!payload) return;
      this.api.create('planning_assignments', payload).subscribe({
        next: () => { this.snack.open('Einplanung gespeichert', 'OK', { duration: 1800 }); this.load(); },
        error: () => this.snack.open('Einplanung konnte nicht gespeichert werden', 'OK', { duration: 3000 })
      });
    });
  }

  editAssignment(assignment: GenericRecord, event?: Event): void {
    event?.stopPropagation();
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
            next: () => { this.snack.open('Einplanung aktualisiert', 'OK', { duration: 1800 }); this.load(); },
            error: () => this.snack.open('Einplanung konnte nicht aktualisiert werden', 'OK', { duration: 3000 })
          });
        });
      },
      error: () => this.snack.open('Einplanung konnte nicht aktuell geladen werden.', 'OK', { duration: 3000 })
    });
  }

  deleteAssignment(assignment: GenericRecord, event?: Event): void {
    event?.stopPropagation();
    this.dialog.open(ConfirmDialogComponent, { width: '420px', panelClass: 'ew-dialog', data: { title: 'Einplanung löschen', message: `Soll „${assignment['title'] || 'Einplanung'}“ wirklich gelöscht werden?`, confirmLabel: 'Löschen', icon: 'delete' } })
      .afterClosed().subscribe((ok) => {
        if (!ok) return;
        const previous = this.assignments(); this.assignments.set(previous.filter((a) => a['id'] !== assignment['id']));
        this.api.remove('planning_assignments', String(assignment['id'])).subscribe({
          next: () => this.snack.open('Einplanung gelöscht', 'OK', { duration: 1800 }),
          error: () => { this.assignments.set(previous); this.snack.open('Einplanung konnte nicht gelöscht werden', 'OK', { duration: 3000 }); }
        });
      });
  }

  dropToBacklog(event: CdkDragDrop<GenericRecord[]>): void {
    const moved = event.previousContainer.data[event.previousIndex];
    if (!moved?.['id']) return;
    this.patchAssignment(moved, { crew_id: null, vehicle_id: null, machine_id: null, status: 'new' });
  }

  dropToCell(event: CdkDragDrop<GenericRecord[]>, resource: GenericRecord, date: Date): void {
    const moved = event.previousContainer.data[event.previousIndex];
    if (!moved?.['id']) return;
    const start = this.withDate(moved['start_at'], date, '07:00');
    const end = this.withDate(moved['end_at'], date, '16:00');
    const patch: GenericRecord = { start_at: this.sqlDateTime(start), end_at: this.sqlDateTime(end), status: moved['status'] || 'planned' };
    if (this.mode() === 'ressourcen') {
      if (resource['resourceType'] === 'vehicle') patch['vehicle_id'] = resource['id'];
      else if (resource['resourceType'] === 'machine') patch['machine_id'] = resource['id'];
      else patch['crew_id'] = resource['id'];
    } else {
      patch['crew_id'] = resource['id'];
    }
    // No-op erkennen: gleiche Ressource UND gleicher Tag -> nichts tun (verhindert Flackern)
    const sameDay = this.isoDate(this.toDate(moved['start_at'])) === this.isoDate(date);
    const sameResource =
      (resource['resourceType'] === 'vehicle' && moved['vehicle_id'] === resource['id']) ||
      (resource['resourceType'] === 'machine' && moved['machine_id'] === resource['id']) ||
      ((resource['resourceType'] === 'crew' || this.mode() !== 'ressourcen') && moved['crew_id'] === resource['id']);
    if (sameDay && sameResource) return;
    this.patchAssignment(moved, patch);
  }

  private patchAssignment(assignment: GenericRecord, patch: GenericRecord): void {
    const previous = this.assignments();
    const next = { ...assignment, ...patch };
    this.assignments.set(previous.map((a) => a['id'] === assignment['id'] ? next : a));
    this.api.update('planning_assignments', String(assignment['id']), patch).subscribe({
      error: () => { this.assignments.set(previous); this.snack.open('Änderung konnte nicht gespeichert werden', 'OK', { duration: 3000 }); }
    });
  }

  resourcesForMode(): GenericRecord[] {
    if (this.mode() === 'ressourcen') {
      return [
        ...this.crews().map((r) => ({ ...r, resourceType: 'crew' })),
        ...this.vehicles().map((r) => ({ ...r, resourceType: 'vehicle', name: r['name'] || r['plate'] || 'Fahrzeug' })),
        ...this.machines().map((r) => ({ ...r, resourceType: 'machine', name: r['name'] || 'Maschine' }))
      ];
    }
    return this.crews().filter((crew) => !this.crewFilter() || crew['id'] === this.crewFilter());
  }

  assignmentsForResourceDay(resource: GenericRecord, date: Date): GenericRecord[] {
    const key = this.isoDate(date);
    return this.visibleAssignments().filter((a) => {
      if (this.isoDate(this.toDate(a['start_at'])) !== key) return false;
      if (this.mode() === 'ressourcen') {
        if (resource['resourceType'] === 'vehicle') return a['vehicle_id'] === resource['id'];
        if (resource['resourceType'] === 'machine') return a['machine_id'] === resource['id'];
      }
      return a['crew_id'] === resource['id'];
    });
  }
  assignmentsForDay(date: Date): GenericRecord[] { const key = this.isoDate(date); return this.visibleAssignments().filter((a) => this.isoDate(this.toDate(a['start_at'])) === key); }
  activeCrewCount(): number { return new Set(this.visibleAssignments().map((a) => a['crew_id']).filter(Boolean)).size; }
  vehicleUsageCount(): number { return new Set(this.visibleAssignments().map((a) => a['vehicle_id']).filter(Boolean)).size; }

  private buildConflicts(): Array<{ key: string; icon: string; title: string; detail: string; severity: string; date: Date }> {
    const conflicts: Array<{ key: string; icon: string; title: string; detail: string; severity: string; date: Date }> = [];
    for (const day of this.weekDays()) {
      for (const crew of this.crews()) {
        const list = this.visibleAssignments().filter((a) => a['crew_id'] === crew['id'] && this.isoDate(this.toDate(a['start_at'])) === day.key);
        if (list.length > 1) conflicts.push({ key: `${day.key}-${crew['id']}-crew`, icon: 'groups', title: 'Mehrfachbelegung Kolonne', detail: `${crew['name']} hat ${list.length} Termine am ${day.date.toLocaleDateString('de-DE')}.`, severity: 'warn', date: day.date });
      }
      for (const vehicle of this.vehicles()) {
        const list = this.visibleAssignments().filter((a) => a['vehicle_id'] === vehicle['id'] && this.isoDate(this.toDate(a['start_at'])) === day.key);
        if (list.length > 1) conflicts.push({ key: `${day.key}-${vehicle['id']}-vehicle`, icon: 'local_shipping', title: 'Fahrzeug doppelt verplant', detail: `${vehicle['name'] || vehicle['plate']} ist ${list.length}× belegt.`, severity: 'critical', date: day.date });
      }
    }
    this.backlog().forEach((a) => conflicts.push({ key: `${a['id']}-backlog`, icon: 'priority_high', title: 'Ungeplante Einplanung', detail: `${a['title'] || 'Einplanung'} hat keine Kolonne oder keinen Termin.`, severity: 'info', date: this.toDate(a['start_at']) }));
    return conflicts;
  }

  focusDay(date: Date): void { this.currentDate.set(date); this.mode.set('dispatcher'); }
  shiftWeek(delta: number): void { const next = new Date(this.currentDate()); next.setDate(next.getDate() + delta * 7); this.currentDate.set(next); this.load(); }
  today(): void { this.currentDate.set(new Date()); this.load(); }
  printBoard(): void { window.print(); }
  weekDays(): { date: Date; key: string }[] { const base = new Date(this.currentDate()); const start = new Date(base); start.setDate(base.getDate() - ((base.getDay() + 6) % 7)); return Array.from({ length: 7 }, (_, i) => { const date = new Date(start); date.setDate(start.getDate() + i); return { date, key: this.isoDate(date) }; }); }
  weekRangeLabel(): string { const days = this.weekDays(); return `${days[0].date.toLocaleDateString('de-DE')} – ${days[6].date.toLocaleDateString('de-DE')}`; }
  weekdayLabel(date: Date): string { return ['Mo','Di','Mi','Do','Fr','Sa','So'][(date.getDay() + 6) % 7]; }
  isToday(date: Date): boolean { return this.isoDate(date) === this.isoDate(new Date()); }
  timeLabel(value: unknown): string { const d = this.toDate(value); return `${String(d.getHours()).padStart(2,'0')}:${String(d.getMinutes()).padStart(2,'0')}`; }
  statusLabel(status: string): string { return this.statusOptions.find((s) => s.key === status)?.label || status || 'Geplant'; }
  assignmentCard(a: GenericRecord): string { return `${a['title'] || 'Einplanung'} · ${this.projectLabel(a['project_id'])} · ${this.siteLabel(a['site_id'])}`; }
  projectLabel(id: unknown): string { return this.projects().find((p) => p['id'] === id)?.['name'] || 'ohne Auftrag'; }
  siteLabel(id: unknown): string { return this.sites().find((s) => s['id'] === id)?.['name'] || 'Baustelle offen'; }
  crewLabel(id: unknown): string { return this.crews().find((c) => c['id'] === id)?.['name'] || 'ohne Kolonne'; }
  resourceSubtitle(r: GenericRecord): string { if (r['resourceType'] === 'vehicle') return r['plate'] || r['license_plate'] || r['registration'] || 'Fahrzeug'; if (r['resourceType'] === 'machine') return r['type'] || r['serial_no'] || 'Maschine'; return r['capacity_m2_day'] ? `${r['capacity_m2_day']} m²/Tag` : 'Kolonne'; }
  private resourceToCrew(resource?: GenericRecord): GenericRecord | null { if (!resource || resource['resourceType'] === 'vehicle' || resource['resourceType'] === 'machine') return null; return resource; }
  private normalize(value: unknown): string { return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }
  private toDate(value: unknown): Date { const date = new Date(String(value || new Date().toISOString()).replace(' ', 'T')); return isNaN(date.getTime()) ? new Date() : date; }
  private withDate(value: unknown, date: Date, fallbackTime: string): Date { const parsed = this.toDate(value); const [h, m] = fallbackTime.split(':').map(Number); const next = new Date(date); next.setHours(isNaN(parsed.getHours()) ? h : parsed.getHours(), isNaN(parsed.getMinutes()) ? m : parsed.getMinutes(), 0, 0); return next; }
  private isoDate(date: Date): string { return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`; }
  private sqlDateTime(date: Date): string { return `${this.isoDate(date)} ${String(date.getHours()).padStart(2,'0')}:${String(date.getMinutes()).padStart(2,'0')}:00`; }
}
