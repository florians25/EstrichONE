import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

interface BauakteRow {
  project: GenericRecord;
  firstSite: GenericRecord | null;
  sites: GenericRecord[];
  customer: GenericRecord | null;
}

@Component({
  standalone: true,
  imports: [DatePipe, NgClass, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatTableModule, MatProgressSpinnerModule],
  template: `
    <section class="page-head compact-head order-search-head bauakten-table-head">
      <div>
        <p class="eyebrow">Bauaktensuche</p>
        <h1><mat-icon>folder_open</mat-icon>Bauaktensuche</h1>
      </div>
      <div class="head-actions">
        @if (canCreateProject) { <a mat-flat-button color="primary" routerLink="/auftrag-wizard"><mat-icon>add</mat-icon>Neuer Auftrag</a> }
        <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon>Aktualisieren</button>
      </div>
    </section>

    <mat-card class="table-card module-list-card bauakten-search-card">
      <div class="table-tools bauakten-filterbar">
        <mat-form-field appearance="outline" class="bauakten-main-search">
          <mat-label>Suche</mat-label>
          <input matInput [ngModel]="queryText()" (ngModelChange)="queryText.set($event)" autocomplete="off" placeholder="Auftrag, Kunde, Baustelle, Ort, Adresse">
          <mat-icon matPrefix>search</mat-icon>
        </mat-form-field>
        <mat-form-field appearance="outline" class="bauakten-status-filter">
          <mat-label>Status</mat-label>
          <mat-select [ngModel]="statusFilter()" (ngModelChange)="statusFilter.set($event)">
            <mat-option value="active">Aktive</mat-option>
            <mat-option value="all">Alle</mat-option>
            <mat-option value="new">Neu</mat-option>
            <mat-option value="planned">Geplant</mat-option>
            <mat-option value="in_progress">In Arbeit</mat-option>
            <mat-option value="delayed">Verzögert</mat-option>
            <mat-option value="defect">Mangel</mat-option>
            <mat-option value="completed">Abgeschlossen</mat-option>
            <mat-option value="archived">Archiv</mat-option>
          </mat-select>
        </mat-form-field>
        <button mat-stroked-button type="button" (click)="clearSearch()"><mat-icon>backspace</mat-icon>Zurücksetzen</button>
        @if (loading()) { <mat-spinner diameter="28"></mat-spinner> }
      </div>

      <div class="bauakten-count-line"><b>{{ rows().length }}</b><span>Bauakten</span></div>

      <div class="responsive-table-wrap bauakten-table-wrap bauakten-desktop-table">
        <table mat-table [dataSource]="rows()" class="data-table pro-data-table bauakten-data-table">
          <ng-container matColumnDef="open"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let row"><button mat-stroked-button class="open-row-button" (click)="openProject(row.project['id']); $event.stopPropagation()"><mat-icon>folder_open</mat-icon>Öffnen</button></td></ng-container>
          <ng-container matColumnDef="project"><th mat-header-cell *matHeaderCellDef>Auftrag</th><td mat-cell *matCellDef="let row"><b>{{ row.project['project_no'] || '—' }} · {{ row.project['name'] }}</b></td></ng-container>
          <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let row"><span class="status-pill" [ngClass]="'status-' + (row.project['status'] || 'open')">{{ statusLabel(row.project['status']) }}</span></td></ng-container>
          <ng-container matColumnDef="customer"><th mat-header-cell *matHeaderCellDef>Kunde</th><td mat-cell *matCellDef="let row"><b>{{ row.customer?.['name'] || '—' }}</b><small>{{ row.customer?.['phone'] || row.customer?.['email'] || '—' }}</small></td></ng-container>
          <ng-container matColumnDef="site"><th mat-header-cell *matHeaderCellDef>Baustelle</th><td mat-cell *matCellDef="let row"><b>{{ siteSummary(row) }}</b><small>{{ row.firstSite?.['screed_type'] || row.project['screed_type'] || '—' }} · {{ row.firstSite?.['area_m2'] || '—' }} m²</small></td></ng-container>
          <ng-container matColumnDef="address"><th mat-header-cell *matHeaderCellDef>Adresse</th><td mat-cell *matCellDef="let row">{{ addressSummary(row) }}</td></ng-container>
          <ng-container matColumnDef="assigned"><th mat-header-cell *matHeaderCellDef>Zuständig</th><td mat-cell *matCellDef="let row">{{ row.project['assigned_user_name'] || row.firstSite?.['site_manager'] || '—' }}</td></ng-container>
          <ng-container matColumnDef="updated"><th mat-header-cell *matHeaderCellDef>Aktualisiert</th><td mat-cell *matCellDef="let row">{{ row.project['updated_at'] | date:'dd.MM.yyyy' }}</td></ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns" class="clickable-row" (click)="openProject(row.project['id'])"></tr>
        </table>
      </div>

      <!-- Handy: saubere Kartenliste statt zerbrochener Tabelle -->
      <div class="bauakten-mobile-list">
        @for (row of rows(); track row.project['id']) {
          <article class="bauakte-mobile-card" (click)="openProject(row.project['id'])">
            <div class="bauakte-mobile-top">
              <b>{{ row.project['project_no'] || '—' }} · {{ row.project['name'] }}</b>
              <span class="status-pill" [ngClass]="'status-' + (row.project['status'] || 'open')">{{ statusLabel(row.project['status']) }}</span>
            </div>
            <div class="bauakte-mobile-meta">
              @if (row.customer?.['name']) { <span><mat-icon>person</mat-icon>{{ row.customer?.['name'] }}</span> }
              @if (addressSummary(row) !== '—') { <span><mat-icon>place</mat-icon>{{ addressSummary(row) }}</span> }
              @if (siteSummary(row) !== '—') { <span><mat-icon>business</mat-icon>{{ siteSummary(row) }}</span> }
            </div>
            <button mat-stroked-button class="bauakte-mobile-open" (click)="openProject(row.project['id']); $event.stopPropagation()"><mat-icon>folder_open</mat-icon>Öffnen</button>
          </article>
        }
      </div>

      @if (!rows().length && !loading()) { <div class="empty bauakten-empty"><mat-icon>search_off</mat-icon><b>Keine Bauakte gefunden</b></div> }
    </mat-card>
  `
})
export class OrderSearchComponent {

  private readonly auth = inject(AuthService);
  readonly canCreateProject = this.auth.canWrite('projects');  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly loading = signal(true);
  readonly projects = signal<GenericRecord[]>([]);
  readonly customers = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly queryText = signal('');
  readonly statusFilter = signal('active');
  readonly columns = ['open', 'project', 'status', 'customer', 'site', 'address', 'assigned', 'updated'];

  readonly rows = computed<BauakteRow[]>(() => {
    const q = this.normalize(this.queryText());
    return this.projects()
      .map((project) => {
        const sites = this.sitesFor(project['id']);
        const firstSite = sites[0] || null;
        const customer = this.customers().find((c) => c['id'] === project['customer_id']) || null;
        return { project, sites, firstSite, customer };
      })
      .filter((row) => this.statusMatches(row.project['status']))
      .filter((row) => {
        if (!q) return true;
        const siteText = row.sites.map((s) => [s['site_no'], s['name'], s['address'], s['zip'], s['city'], s['screed_type'], s['site_manager']].join(' ')).join(' ');
        const hay = this.normalize([row.project['project_no'], row.project['name'], row.project['address'], row.project['zip'], row.project['city'], row.project['status'], row.project['assigned_user_name'], row.customer?.['name'], row.customer?.['email'], row.customer?.['phone'], siteText].join(' '));
        return hay.includes(q);
      })
      .sort((a, b) => String(b.project['updated_at'] || b.project['created_at'] || '').localeCompare(String(a.project['updated_at'] || a.project['created_at'] || '')));
  });

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    forkJoin({ projects: this.api.list('projects', '', 1000), customers: this.api.list('customers', '', 1000), sites: this.api.list('construction_sites', '', 2000) })
      .subscribe({
        next: ({ projects, customers, sites }) => { this.projects.set(projects.items || []); this.customers.set(customers.items || []); this.sites.set(sites.items || []); this.loading.set(false); },
        error: () => { this.loading.set(false); }
      });
  }

  applySearch(): void { }
  clearSearch(): void { this.queryText.set(''); this.statusFilter.set('active'); }
  openProject(id: string): void { if (id) void this.router.navigate(['/auftragsakte'], { queryParams: { project_id: id } }); }
  statusMatches(status: string): boolean {
    const normalized = String(status || '').toLowerCase();
    const filter = this.statusFilter();
    if (filter === 'all') return true;
    if (filter === 'active') return !['completed', 'abgeschlossen', 'archived', 'closed'].includes(normalized);
    return normalized === filter;
  }
  normalize(value: string): string { return String(value || '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }
  sitesFor(projectId: string): GenericRecord[] { return this.sites().filter((site) => site['project_id'] === projectId); }
  siteSummary(row: BauakteRow): string {
    if (!row.sites.length) return '—';
    const first = row.firstSite;
    const label = `${first?.['site_no'] || 'Baustelle'} · ${first?.['name'] || ''}`.trim();
    return row.sites.length > 1 ? `${label} +${row.sites.length - 1}` : label;
  }
  addressSummary(row: BauakteRow): string {
    const source = row.firstSite || row.project;
    return [source?.['address'], source?.['zip'], source?.['city']].filter(Boolean).join(', ') || '—';
  }
  statusLabel(status: string): string {
    const map: Record<string,string> = { new: 'Neu', planned: 'Geplant', in_progress: 'In Arbeit', delayed: 'Verzögert', defect: 'Mangel', completed: 'Abgeschlossen', archived: 'Archiv', closed: 'Geschlossen' };
    return map[String(status || '')] || status || 'Offen';
  }
}
