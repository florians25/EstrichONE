import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { downloadMaterialOrderPdf, MaterialOrderDialogComponent, materialOrderStatusLabel, MATERIAL_ORDER_STATUSES } from './material-order-dialog.component';

/**
 * Buero -> Materialbestellungen.
 *
 * Aufbau bewusst identisch zu den uebrigen Uebersichtsseiten (Bauaktensuche,
 * Stammdaten): Seitenkopf, KPI-Kacheln, Karte mit Filterleiste, Tabelle am
 * Desktop und Karten am Handy. Aktionen erscheinen nur mit passendem Recht.
 */
@Component({
  standalone: true,
  imports: [DatePipe, NgClass, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatTableModule, MatMenuModule, MatTooltipModule, MatProgressSpinnerModule, MatDialogModule, MatSnackBarModule],
  template: `
    <section class="page-head compact-head material-orders-head">
      <div>
        <p class="eyebrow">Büro · Material</p>
        <h1><mat-icon>inventory_2</mat-icon>Materialbestellungen</h1>
      </div>
      <div class="head-actions">
        <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon>Aktualisieren</button>
      </div>
    </section>

    <section class="material-kpi-row">
      <button type="button" class="material-kpi" [class.is-active]="statusFilter === 'to_order'" (click)="setStatus('to_order')">
        <span class="material-kpi-icon kpi-open"><mat-icon>pending_actions</mat-icon></span>
        <span class="material-kpi-text"><b>{{ countFor('to_order') }}</b><small>Zu bestellen</small></span>
      </button>
      <button type="button" class="material-kpi" [class.is-active]="statusFilter === 'ordered'" (click)="setStatus('ordered')">
        <span class="material-kpi-icon kpi-ordered"><mat-icon>local_shipping</mat-icon></span>
        <span class="material-kpi-text"><b>{{ countFor('ordered') }}</b><small>Bestellt</small></span>
      </button>
      <button type="button" class="material-kpi" [class.is-active]="statusFilter === 'done'" (click)="setStatus('done')">
        <span class="material-kpi-icon kpi-done"><mat-icon>check_circle</mat-icon></span>
        <span class="material-kpi-text"><b>{{ countFor('done') }}</b><small>Erledigt</small></span>
      </button>
      <button type="button" class="material-kpi" [class.is-active]="statusFilter === 'cancelled'" (click)="setStatus('cancelled')">
        <span class="material-kpi-icon kpi-cancelled"><mat-icon>do_not_disturb_on</mat-icon></span>
        <span class="material-kpi-text"><b>{{ countFor('cancelled') }}</b><small>Abgesagt</small></span>
      </button>
    </section>

    <mat-card class="table-card module-list-card material-orders-card">
      <div class="table-tools">
        <mat-form-field appearance="outline" class="material-main-search">
          <mat-label>Suche</mat-label>
          <input matInput [(ngModel)]="search" autocomplete="off" placeholder="Bestellnr., Bauakte, Lieferant, Adresse">
          <mat-icon matPrefix>search</mat-icon>
        </mat-form-field>
        <mat-form-field appearance="outline" class="table-filter-field">
          <mat-label>Status</mat-label>
          <mat-select [(ngModel)]="statusFilter" (selectionChange)="load()">
            <mat-option value="">Alle</mat-option>
            @for (s of statuses; track s.value) { <mat-option [value]="s.value">{{ s.label }}</mat-option> }
          </mat-select>
        </mat-form-field>
        @if (hasActiveFilter()) { <button mat-stroked-button (click)="clearFilters()"><mat-icon>backspace</mat-icon>Filter zurücksetzen</button> }
        @if (loading()) { <mat-spinner diameter="28"></mat-spinner> }
        <span class="table-count">Gesamt: <b>{{ filtered().length }}</b></span>
      </div>

      <div class="responsive-table-wrap material-desktop-table">
        <table mat-table [dataSource]="filtered()" class="data-table pro-data-table material-orders-table">
          <ng-container matColumnDef="order">
            <th mat-header-cell *matHeaderCellDef>Bestellung</th>
            <td mat-cell *matCellDef="let order" data-label="Bestellung"><b>{{ order['order_no'] || 'Bestellung' }}</b><small>{{ order['created_at'] | date:'dd.MM.yyyy' }}</small></td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <td mat-cell *matCellDef="let order" data-label="Status"><span class="status-pill" [ngClass]="'material-status-' + (order['status'] || 'to_order')">{{ statusLabel(order['status']) }}</span></td>
          </ng-container>
          <ng-container matColumnDef="project">
            <th mat-header-cell *matHeaderCellDef>Bauakte</th>
            <td mat-cell *matCellDef="let order" data-label="Bauakte"><b>{{ projectLine(order) }}</b><small>{{ siteLine(order) || '—' }}</small></td>
          </ng-container>
          <ng-container matColumnDef="supplier">
            <th mat-header-cell *matHeaderCellDef>Lieferant</th>
            <td mat-cell *matCellDef="let order" data-label="Lieferant">{{ order['supplier_name'] || 'Ohne Lieferant' }}</td>
          </ng-container>
          <ng-container matColumnDef="delivery">
            <th mat-header-cell *matHeaderCellDef>Lieferwunsch</th>
            <td mat-cell *matCellDef="let order" data-label="Lieferwunsch">{{ order['requested_delivery_date'] ? (order['requested_delivery_date'] | date:'dd.MM.yyyy') : '—' }}</td>
          </ng-container>
          <ng-container matColumnDef="items">
            <th mat-header-cell *matHeaderCellDef>Positionen</th>
            <td mat-cell *matCellDef="let order" data-label="Positionen"><b>{{ itemCount(order) }}</b><small [matTooltip]="itemPreview(order)">{{ itemPreview(order) }}</small></td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef>Aktion</th>
            <td mat-cell *matCellDef="let order" data-label="Aktion">
              <div class="row-action-group">
                @if (canWrite) {
                  <button mat-stroked-button [matMenuTriggerFor]="statusMenu"><mat-icon>flag</mat-icon>Status</button>
                  <mat-menu #statusMenu="matMenu" xPosition="before">
                    @for (s of statuses; track s.value) {
                      <button mat-menu-item (click)="setOrderStatus(order, s.value)">
                        <mat-icon>{{ order['status'] === s.value ? 'check' : 'radio_button_unchecked' }}</mat-icon><span>{{ s.label }}</span>
                      </button>
                    }
                  </mat-menu>
                  <button mat-stroked-button (click)="openEdit(order)"><mat-icon>edit</mat-icon>Bearbeiten</button>
                }
                <button mat-stroked-button (click)="download(order)"><mat-icon>picture_as_pdf</mat-icon>PDF</button>
                @if (order['project_id']) { <a mat-stroked-button [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: order['project_id'], tab: 'material' }"><mat-icon>folder_open</mat-icon>Bauakte</a> }
                @if (canDelete) { <button mat-stroked-button class="square-delete-text" (click)="remove(order)"><mat-icon>delete</mat-icon>Löschen</button> }
              </div>
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
      </div>

      <!-- Handy: Karten statt breiter Tabelle -->
      <div class="material-mobile-list">
        @for (order of filtered(); track order['id']) {
          <article class="material-mobile-card">
            <header>
              <div>
                <b>{{ order['order_no'] || 'Bestellung' }}</b>
                <span>{{ projectLine(order) }}</span>
              </div>
              <span class="status-pill" [ngClass]="'material-status-' + (order['status'] || 'to_order')">{{ statusLabel(order['status']) }}</span>
            </header>
            <dl>
              <div><dt>Lieferant</dt><dd>{{ order['supplier_name'] || 'Ohne Lieferant' }}</dd></div>
              <div><dt>Lieferwunsch</dt><dd>{{ order['requested_delivery_date'] ? (order['requested_delivery_date'] | date:'dd.MM.yyyy') : '—' }}</dd></div>
              <div><dt>Positionen</dt><dd>{{ itemCount(order) }}</dd></div>
            </dl>
            @if (order['delivery_address']) { <p class="material-address"><mat-icon>place</mat-icon><span>{{ order['delivery_address'] }}</span></p> }
            <div class="material-mobile-actions">
              @if (canWrite) { <button mat-stroked-button (click)="openEdit(order)"><mat-icon>edit</mat-icon>Bearbeiten</button> }
              <button mat-stroked-button (click)="download(order)"><mat-icon>picture_as_pdf</mat-icon>PDF</button>
              @if (order['project_id']) { <a mat-stroked-button [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: order['project_id'], tab: 'material' }"><mat-icon>folder_open</mat-icon>Bauakte</a> }
              @if (canDelete) { <button mat-stroked-button class="square-delete-text" (click)="remove(order)"><mat-icon>delete</mat-icon>Löschen</button> }
            </div>
          </article>
        }
      </div>

      @if (!filtered().length && !loading()) {
        <div class="empty material-empty"><mat-icon>inventory_2</mat-icon><b>Keine Materialbestellungen gefunden</b><span>Bestellungen werden direkt aus der Bauakte heraus angelegt.</span></div>
      }
    </mat-card>
  `})
export class MaterialOrdersComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  readonly orders = signal<GenericRecord[]>([]);
  readonly suppliers = signal<GenericRecord[]>([]);
  readonly summary = signal<GenericRecord>({});
  readonly companyInfo = signal<GenericRecord>({});
  readonly loading = signal(false);
  readonly statuses = MATERIAL_ORDER_STATUSES;
  search = '';
  statusFilter = '';

  readonly canWrite = this.auth.canWrite('materials');
  readonly canDelete = this.auth.canDelete('materials');
  readonly columns = ['order', 'status', 'project', 'supplier', 'delivery', 'items', 'actions'];

  readonly filtered = computed(() => {
    const q = this.search.trim().toLowerCase();
    const list = this.orders();
    if (!q) return list;
    return list.filter((o) => [o['order_no'], o['project_no'], o['project_name'], o['site_name'], o['supplier_name'], o['delivery_address'], o['notes']].join(' ').toLowerCase().includes(q));
  });

  constructor() { this.load(); this.api.appSettings().subscribe({ next: (res) => this.companyInfo.set((res?.['company'] as GenericRecord) || {}), error: () => {} }); }

  load(): void {
    this.loading.set(true);
    forkJoin({ orders: this.api.materialOrders(300, this.statusFilter), suppliers: this.api.list('suppliers', '', 300), summary: this.api.materialOrdersSummary() }).subscribe({
      next: ({ orders, suppliers, summary }) => {
        this.orders.set(orders.items || []);
        this.suppliers.set(suppliers.items || []);
        this.summary.set(summary || orders.summary || {});
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.snack.open('Materialbestellungen konnten nicht geladen werden.', 'OK', { duration: 3000 }); }
    });
  }

  setStatus(status: string): void { this.statusFilter = this.statusFilter === status ? '' : status; this.load(); }
  hasActiveFilter(): boolean { return !!this.statusFilter || !!this.search.trim(); }
  clearFilters(): void { this.statusFilter = ''; this.search = ''; this.load(); }

  /** Kennzahl je Status: bevorzugt die Server-Zusammenfassung, sonst gezaehlt. */
  countFor(status: string): number {
    const stats = (this.summary()['stats'] as GenericRecord) || this.summary();
    const fromServer = stats?.[status];
    if (fromServer !== undefined && fromServer !== null) return Number(fromServer);
    if (status === 'to_order') {
      const open = this.summary()['open_count'] ?? this.summary()['open'];
      if (open !== undefined && open !== null) return Number(open);
    }
    return this.orders().filter((o) => String(o['status'] || 'to_order') === status).length;
  }

  statusLabel(status: unknown): string { return materialOrderStatusLabel(status); }
  projectLine(order: GenericRecord): string { return [order['project_no'], order['project_name']].filter((v) => !!v).join(' · ') || 'Ohne Bauakte'; }
  siteLine(order: GenericRecord): string { return [order['site_no'], order['site_name']].filter((v) => !!v).join(' · '); }
  itemPreview(order: GenericRecord): string {
    const items = Array.isArray(order['items']) ? order['items'] as GenericRecord[] : [];
    return items.length ? items.map((i) => `${i['quantity'] || ''} ${i['unit'] || ''} ${i['description'] || ''}`.trim()).filter(Boolean).slice(0, 3).join(' · ') : 'Keine Positionen';
  }
  itemCount(order: GenericRecord): number { return Array.isArray(order['items']) ? (order['items'] as GenericRecord[]).length : 0; }

  openEdit(order: GenericRecord): void {
    if (!this.canWrite) return;
    const ref = this.dialog.open(MaterialOrderDialogComponent, { width: 'min(880px, 96vw)', maxWidth: '96vw', autoFocus: false, panelClass: ['ew-dialog', 'material-order-dialog'], data: { order, suppliers: this.suppliers(), sites: [] } });
    ref.afterClosed().subscribe((payload) => {
      if (!payload) return;
      this.api.updateMaterialOrder(String(order['id']), payload).subscribe({
        next: () => { this.load(); this.snack.open('Materialbestellung gespeichert.', 'OK', { duration: 2200 }); },
        error: () => this.snack.open('Speichern fehlgeschlagen.', 'OK', { duration: 3000 })
      });
    });
  }

  setOrderStatus(order: GenericRecord, status: string): void {
    if (!this.canWrite || String(order['status']) === status) return;
    this.api.updateMaterialOrderStatus(String(order['id']), status).subscribe({
      next: (updated) => {
        this.orders.update((l) => l.map((x) => x['id'] === updated['id'] ? updated : x));
        // Kennzahlen neu holen, aber nicht die ganze Liste – spart einen Roundtrip.
        this.api.invalidateCache('material-orders-summary');
        this.api.materialOrdersSummary().subscribe({ next: (res) => this.summary.set(res || {}), error: () => {} });
      },
      error: () => this.snack.open('Status konnte nicht geändert werden.', 'OK', { duration: 3000 })
    });
  }

  download(order: GenericRecord): void { downloadMaterialOrderPdf(order, null, this.companyInfo()); }

  remove(order: GenericRecord): void {
    if (!this.canDelete) return;
    const ref = this.dialog.open(ConfirmDialogComponent, { width: 'min(460px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', data: { title: 'Materialbestellung löschen?', message: `Die Bestellung ${order['order_no'] || ''} wird gelöscht.`, confirmText: 'Löschen', cancelText: 'Abbrechen', icon: 'delete', tone: 'warn' } });
    ref.afterClosed().subscribe((ok) => {
      if (!ok) return;
      this.api.deleteMaterialOrder(String(order['id'])).subscribe({
        next: () => { this.orders.update((l) => l.filter((x) => x['id'] !== order['id'])); this.load(); this.snack.open('Materialbestellung gelöscht.', 'OK', { duration: 2200 }); },
        error: () => this.snack.open('Löschen fehlgeschlagen.', 'OK', { duration: 3000 })
      });
    });
  }
}
