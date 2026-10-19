import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ApiService, GenericRecord } from '../../core/api.service';

@Component({
  standalone: true,
  selector: 'ew-tasks',
  imports: [DatePipe, NgClass, FormsModule, RouterLink, MatCardModule, MatChipsModule, MatButtonModule, MatIconModule, MatTableModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <section class="page-head compact-head tasks-head">
      <div>
        <p class="eyebrow">Baustellensteuerung</p>
        <h1><mat-icon>task_alt</mat-icon>Aufgaben und Mängel</h1>
        <p>Alle automatisch und manuell erzeugten Aufgaben mit Bauaktenbezug, Baustelle, Zuständigkeit und Fälligkeit.</p>
      </div>
      <div class="head-actions">
        <a mat-flat-button color="primary" routerLink="/erstbegehung-wizard"><mat-icon>fact_check</mat-icon>Erstbegehung</a>
        <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon>Aktualisieren</button>
      </div>
    </section>

    <section class="admin-kpi-grid tasks-kpi-grid">
      <mat-card class="square-card"><span>Offen</span><b>{{ openTasks().length }}</b></mat-card>
      <mat-card class="square-card"><span>Überfällig</span><b>{{ overdueTasks().length }}</b></mat-card>
      <mat-card class="square-card"><span>Automatisch</span><b>{{ automaticTasks().length }}</b></mat-card>
      <mat-card class="square-card"><span>Erledigt</span><b>{{ doneTasks().length }}</b></mat-card>
    </section>

    <mat-card class="search-console task-filter-card">
      <div class="search-row">
        <mat-form-field appearance="outline" class="main-search"><mat-label>Aufgabe, Bauakte, Baustelle suchen</mat-label><input matInput [(ngModel)]="queryText" autocomplete="off"><mat-icon matPrefix>search</mat-icon></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Status</mat-label><mat-select [(ngModel)]="statusFilter"><mat-option value="open">Offen</mat-option><mat-option value="all">Alle</mat-option><mat-option value="done">Erledigt</mat-option></mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Quelle</mat-label><mat-select [(ngModel)]="sourceFilter"><mat-option value="all">Alle Quellen</mat-option><mat-option value="auto">Automatisch</mat-option><mat-option value="manual">Manuell</mat-option></mat-select></mat-form-field>
      </div>
    </mat-card>

    <mat-card class="table-card module-list-card tasks-table-card">
      <div class="responsive-table-wrap desktop-table-only">
        <table mat-table [dataSource]="filteredTasks()" class="data-table pro-data-table">
          <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Aufgabe</th><td mat-cell *matCellDef="let row"><b>{{ row['title'] }}</b><small>{{ row['description'] || '—' }}</small></td></ng-container>
          <ng-container matColumnDef="akte"><th mat-header-cell *matHeaderCellDef>Bauakte</th><td mat-cell *matCellDef="let row"><a [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: rowProjectId(row) }">{{ projectName(rowProjectId(row)) }}</a><small>{{ siteName(row['site_id']) }}</small></td></ng-container>
          <ng-container matColumnDef="priority"><th mat-header-cell *matHeaderCellDef>Priorität</th><td mat-cell *matCellDef="let row"><span class="status-pill" [ngClass]="'priority-' + (row['priority'] || 'normal')">{{ priorityLabel(row['priority']) }}</span></td></ng-container>
          <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let row"><span class="status-pill" [ngClass]="'status-' + (row['status'] || 'open')">{{ statusLabel(row['status']) }}</span></td></ng-container>
          <ng-container matColumnDef="due"><th mat-header-cell *matHeaderCellDef>Fällig</th><td mat-cell *matCellDef="let row">{{ fmtDate(row['due_date']) }}</td></ng-container>
          <ng-container matColumnDef="assigned"><th mat-header-cell *matHeaderCellDef>Zuständig</th><td mat-cell *matCellDef="let row">{{ userName(row['assigned_user_id']) }}</td></ng-container>
          <ng-container matColumnDef="source"><th mat-header-cell *matHeaderCellDef>Quelle</th><td mat-cell *matCellDef="let row"><mat-chip-set><mat-chip>{{ isAutomatic(row) ? 'Automatisch' : 'Manuell' }}</mat-chip></mat-chip-set></td></ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
      </div>
      <section class="mobile-record-list">
        @for (task of filteredTasks(); track task['id']) {
          <article class="mobile-record-card">
            <header><div><strong>{{ task['title'] }}</strong><span>{{ projectName(rowProjectId(task)) }} · {{ siteName(task['site_id']) }}</span></div><span class="status-pill" [ngClass]="'status-' + (task['status'] || 'open')">{{ statusLabel(task['status']) }}</span></header>
            <dl><div><dt>Fällig</dt><dd>{{ fmtDate(task['due_date']) }}</dd></div><div><dt>Priorität</dt><dd>{{ priorityLabel(task['priority']) }}</dd></div><div><dt>Zuständig</dt><dd>{{ userName(task['assigned_user_id']) }}</dd></div></dl>
            <a mat-stroked-button [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: rowProjectId(task) }"><mat-icon>folder_open</mat-icon>Bauakte öffnen</a>
          </article>
        } @empty { <div class="empty-state"><mat-icon>task_alt</mat-icon><b>Keine Aufgaben gefunden.</b></div> }
      </section>
    </mat-card>
  `
})
export class TasksComponent {
  private readonly api = inject(ApiService);
  readonly tasks = signal<GenericRecord[]>([]);
  readonly projects = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly users = signal<GenericRecord[]>([]);
  queryText = '';
  statusFilter: 'open' | 'all' | 'done' = 'open';
  sourceFilter: 'all' | 'auto' | 'manual' = 'all';
  readonly columns = ['title','akte','priority','status','due','assigned','source'];

  readonly openTasks = computed(() => this.tasks().filter((t) => !['done','closed','archived'].includes(String(t['status'] || '').toLowerCase())));
  readonly doneTasks = computed(() => this.tasks().filter((t) => ['done','closed','archived'].includes(String(t['status'] || '').toLowerCase())));
  readonly automaticTasks = computed(() => this.tasks().filter((t) => this.isAutomatic(t)));
  readonly overdueTasks = computed(() => this.openTasks().filter((t) => this.isOverdue(t['due_date'])));
  readonly filteredTasks = computed(() => {
    const q = this.normalize(this.queryText);
    return this.tasks().filter((task) => {
      const done = ['done','closed','archived'].includes(String(task['status'] || '').toLowerCase());
      if (this.statusFilter === 'open' && done) return false;
      if (this.statusFilter === 'done' && !done) return false;
      const auto = this.isAutomatic(task);
      if (this.sourceFilter === 'auto' && !auto) return false;
      if (this.sourceFilter === 'manual' && auto) return false;
      const projectId = this.rowProjectId(task);
      const hay = this.normalize([task['title'], task['description'], this.projectName(projectId), this.siteName(task['site_id']), this.userName(task['assigned_user_id'])].join(' '));
      return !q || hay.includes(q);
    }).sort((a, b) => String(a['due_date'] || '9999-12-31').localeCompare(String(b['due_date'] || '9999-12-31')));
  });

  constructor() { this.load(); }
  load(): void {
    forkJoin({ tasks: this.api.list('tasks', '', 1000), projects: this.api.list('projects', '', 1000), sites: this.api.list('construction_sites', '', 1000), users: this.api.list('users', '', 1000) }).subscribe(({tasks, projects, sites, users}) => {
      this.tasks.set(tasks.items || []); this.projects.set(projects.items || []); this.sites.set(sites.items || []); this.users.set(users.items || []);
    });
  }
  rowProjectId(row: GenericRecord): string { return row['project_id'] || this.sites().find((s) => s['id'] === row['site_id'])?.['project_id'] || ''; }
  projectName(id: string): string { const p=this.projects().find((item) => item['id'] === id); return p ? `${p['project_no'] || 'Akte'} · ${p['name']}` : '—'; }
  siteName(id: string): string { const s=this.sites().find((item) => item['id'] === id); return s ? `${s['site_no'] || 'Baustelle'} · ${s['name']}` : '—'; }
  userName(id: string): string { return this.users().find((u) => u['id'] === id)?.['name'] || 'nicht zugewiesen'; }
  isAutomatic(task: GenericRecord): boolean { return /automatisch|erstbegehung|wizard|auftragserfassung/i.test(String(task['description'] || '') + ' ' + String(task['title'] || '')); }
  isOverdue(value: unknown): boolean { const raw=String(value || '').slice(0,10); return !!raw && raw !== '0000-00-00' && raw < new Date().toISOString().slice(0,10); }
  fmtDate(value: unknown): string { const raw=String(value || '').slice(0,10); return !raw || raw === '0000-00-00' ? '—' : raw.split('-').reverse().join('.'); }
  normalize(value: unknown): string { return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }
  priorityLabel(priority: string): string { return ({ low:'Niedrig', normal:'Normal', medium:'Mittel', high:'Hoch', urgent:'Dringend' } as Record<string,string>)[priority] || priority || 'Normal'; }
  statusLabel(status: string): string { return ({ open:'Offen', in_progress:'In Arbeit', done:'Erledigt', closed:'Geschlossen', archived:'Archiv' } as Record<string,string>)[status] || status || 'Offen'; }
}
