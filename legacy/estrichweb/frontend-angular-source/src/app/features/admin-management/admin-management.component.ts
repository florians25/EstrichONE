import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule, ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService, GenericRecord } from '../../core/api.service';
import { UserCreateDialogComponent } from './user-create-dialog.component';
import { UserEditDialogComponent } from './user-edit-dialog.component';

@Component({
  standalone: true,
  imports: [DatePipe, NgClass, RouterLink, FormsModule, ReactiveFormsModule, MatCardModule, MatButtonModule, MatIconModule, MatTableModule, MatTabsModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatSelectModule, MatSnackBarModule, MatDialogModule, MatTooltipModule],
  template: `
    <section class="page-head compact-head admin-head">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Benutzer und Rollen</h1>
      </div>
      <div class="head-actions">
        <button mat-flat-button color="primary" (click)="openCreateUser()"><mat-icon>person_add</mat-icon> Benutzer anlegen</button>
        <button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon> Aktualisieren</button>
      </div>
    </section>

    <section class="admin-kpi-grid">
      <mat-card class="square-card"><span>Aktive Benutzer</span><b>{{ activeUsers().length }}</b></mat-card>
      <mat-card class="square-card"><span>Inaktiv</span><b>{{ inactiveUsers().length }}</b></mat-card>
      <mat-card class="square-card"><span>Rollen</span><b>{{ roles().length }}</b></mat-card>
      <mat-card class="square-card"><span>Aktive Geräte</span><b>{{ activeDevices().length }}</b></mat-card>
    </section>

    <mat-card class="admin-tabs-card square-card">
      <mat-tab-group animationDuration="0ms">
        <mat-tab label="Benutzer">
          <div class="admin-tab-toolbar user-role-toolbar">
            <mat-form-field appearance="outline" class="user-role-search"><mat-label>Benutzer suchen</mat-label><input matInput [(ngModel)]="userSearch" placeholder="Name oder E-Mail"><mat-icon matPrefix>search</mat-icon></mat-form-field>
            <button mat-flat-button color="primary" (click)="openCreateUser()"><mat-icon>person_add</mat-icon>Benutzer anlegen</button>
          </div>
          <div class="responsive-table-wrap">
            <table mat-table [dataSource]="filteredUsers()" class="data-table admin-user-role-table">
              <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>Name</th><td mat-cell *matCellDef="let row"><b>{{ row['name'] }}</b><small>{{ row['email'] }}</small></td></ng-container>
              <ng-container matColumnDef="roles"><th mat-header-cell *matHeaderCellDef>Rollen</th><td mat-cell *matCellDef="let row">
                <div class="assigned-role-chips">
                  @for (role of selectedRoleNames(row['id']); track role) { <span>{{ role }}</span> }
                  @empty { <em>Keine Rolle</em> }
                </div>
              </td></ng-container>
              <ng-container matColumnDef="two_factor_enabled"><th mat-header-cell *matHeaderCellDef>2FA</th><td mat-cell *matCellDef="let row">{{ row['two_factor_enabled'] ? 'aktiv' : 'aus' }}</td></ng-container>
              <ng-container matColumnDef="is_active"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let row"><span class="status-pill" [ngClass]="row['is_active'] ? 'status-active' : 'status-closed'">{{ row['is_active'] ? 'aktiv' : 'inaktiv' }}</span></td></ng-container>
              <ng-container matColumnDef="last_login_at"><th mat-header-cell *matHeaderCellDef>Letzter Login</th><td mat-cell *matCellDef="let row">{{ row['last_login_at'] | date:'dd.MM.yyyy HH:mm' }}</td></ng-container>
              <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef>Aktion</th><td mat-cell *matCellDef="let row">
                <div class="user-row-actions">
                  <button mat-stroked-button class="user-edit-btn" (click)="openEditUser(row)"><mat-icon>edit</mat-icon>Bearbeiten</button>
                  <button mat-stroked-button class="user-lock-btn" [class.is-locked]="!row['is_active']" (click)="toggleUserActive(row)">
                    <mat-icon>{{ row['is_active'] ? 'lock' : 'lock_open' }}</mat-icon>{{ row['is_active'] ? 'Sperren' : 'Entsperren' }}
                  </button>
                </div>
              </td></ng-container>
              <tr mat-header-row *matHeaderRowDef="userColumns"></tr><tr mat-row *matRowDef="let row; columns: userColumns"></tr>
            </table>
          </div>
        </mat-tab>
        <mat-tab label="Rollen &amp; Rechte">
          <section class="rr-layout">
            <!-- Linke Spalte: Rollen -->
            <aside class="rr-sidebar">
              <div class="rr-sidebar-head">
                <h3>Rollen</h3>
                <span class="rr-role-total">{{ rolesFull().length }}</span>
              </div>
              <div class="rr-new-role">
                <mat-form-field appearance="outline"><mat-label>Neue Rolle</mat-label><input matInput [(ngModel)]="newRoleName" placeholder="z. B. Bauleiter" (keyup.enter)="createRole()"></mat-form-field>
                <button mat-flat-button color="primary" [disabled]="!newRoleName.trim()" (click)="createRole()" aria-label="Rolle anlegen"><mat-icon>add</mat-icon></button>
              </div>
              <mat-form-field appearance="outline" class="rr-role-search"><mat-label>Rolle suchen</mat-label><input matInput [(ngModel)]="roleSearch"><mat-icon matPrefix>search</mat-icon></mat-form-field>
              <div class="rr-role-list">
                @for (role of filteredRoles(); track role['id']) {
                  <button type="button" class="rr-role-item" [class.active]="selectedRoleId() === role['id']" (click)="selectRole(role)">
                    <span class="rr-role-line">
                      <b>{{ role['name'] }}</b>
                      @if (role['is_system']) { <span class="rr-badge">System</span> }
                      @if (roleHasFullAccess(role)) { <span class="rr-badge rr-badge-full">Vollzugriff</span> }
                    </span>
                    <span class="rr-role-stats">
                      <span><mat-icon>key</mat-icon>{{ roleRightCount(role) }}</span>
                      <span><mat-icon>group</mat-icon>{{ role['user_count'] || 0 }}</span>
                    </span>
                  </button>
                } @empty { <p class="muted rr-empty-roles">Keine Rollen gefunden.</p> }
              </div>
            </aside>

            <!-- Rechte Spalte: Rechtematrix -->
            <div class="rr-matrix">
              @if (selectedRole(); as role) {
                <header class="rr-matrix-head">
                  <div class="rr-matrix-title">
                    <h3>{{ role['name'] }}@if (role['is_system']) { <span class="rr-badge">System</span> }</h3>
                    <span>
                      {{ permCount() }} von {{ totalPermSlots() }} Einzelrechten aktiv
                      @if (isDirty()) { · <em class="rr-dirty">ungespeicherte Änderungen</em> }
                    </span>
                  </div>
                  <div class="rr-matrix-tools">
                    <button mat-stroked-button (click)="resetPerms()" [disabled]="!isDirty()"><mat-icon>undo</mat-icon>Verwerfen</button>
                    <button mat-flat-button color="primary" (click)="saveRolePerms()" [disabled]="savingRole() || !isDirty()"><mat-icon>save</mat-icon>Speichern</button>
                  </div>
                </header>

                <!-- Vollzugriff: eigener, deutlich abgesetzter Schalter -->
                <div class="rr-superuser" [class.is-on]="hasWildcard()">
                  <mat-icon>{{ hasWildcard() ? 'verified_user' : 'shield' }}</mat-icon>
                  <div class="rr-superuser-text">
                    <b>Vollzugriff auf alle Bereiche</b>
                    <small>Hebt die Einzelrechte auf. Nur für Geschäftsführung und Administration vorgesehen.</small>
                  </div>
                  <mat-slide-toggle [checked]="hasWildcard()" (change)="toggleWildcard()"></mat-slide-toggle>
                </div>

                @if (hasWildcard()) {
                  <p class="rr-superuser-note"><mat-icon>info</mat-icon>Diese Rolle hat Vollzugriff. Die Einzelrechte unten sind deshalb wirkungslos – sie bleiben erhalten, falls der Vollzugriff später wieder entzogen wird.</p>
                }

                <!-- Werkzeugleiste: Suche und Vorlagen -->
                <div class="rr-toolbar" [class.is-muted]="hasWildcard()">
                  <mat-form-field appearance="outline" class="rr-perm-search"><mat-label>Bereich suchen</mat-label><input matInput [(ngModel)]="permSearch" placeholder="z. B. Fahrzeuge"><mat-icon matPrefix>search</mat-icon></mat-form-field>
                  <div class="rr-presets">
                    <span class="rr-presets-label">Vorlage anwenden:</span>
                    <button mat-stroked-button (click)="applyPreset('read')"><mat-icon>visibility</mat-icon>Nur ansehen</button>
                    <button mat-stroked-button (click)="applyPreset('edit')"><mat-icon>edit</mat-icon>Ansehen &amp; bearbeiten</button>
                    <button mat-stroked-button (click)="applyPreset('full')"><mat-icon>done_all</mat-icon>Alles erlauben</button>
                    <button mat-stroked-button (click)="applyPreset('none')"><mat-icon>remove_done</mat-icon>Nichts</button>
                  </div>
                </div>

                <!-- Legende der Rechtearten -->
                <div class="rr-legend">
                  @for (a of permActions; track a.key) {
                    <span class="rr-legend-item" [matTooltip]="a.hint"><mat-icon>{{ a.icon }}</mat-icon>{{ a.label }}</span>
                  }
                </div>

                <div class="rr-groups" [class.is-muted]="hasWildcard()">
                  @for (group of permGroups(); track group.category) {
                    <section class="rr-group">
                      <div class="rr-group-head">
                        <h4>{{ group.category }}</h4>
                        <div class="rr-group-actions">
                          <button mat-button (click)="setGroup(group, 'read')"><mat-icon>visibility</mat-icon>Nur ansehen</button>
                          <button mat-button (click)="setGroup(group, 'full')"><mat-icon>done_all</mat-icon>Alles</button>
                          <button mat-button (click)="setGroup(group, 'none')"><mat-icon>remove_done</mat-icon>Nichts</button>
                        </div>
                      </div>

                      <!-- Desktop: echte Matrix Bereich x Recht -->
                      <div class="rr-table-wrap">
                        <table class="rr-table">
                          <thead>
                            <tr>
                              <th class="rr-th-area">Bereich</th>
                              @for (a of permActions; track a.key) {
                                <th class="rr-th-act">
                                  <button type="button" class="rr-col-toggle" (click)="toggleColumn(group, a.key)" [matTooltip]="'Spalte ' + a.label + ' für alle Bereiche umschalten'">
                                    <mat-icon>{{ a.icon }}</mat-icon><span>{{ a.label }}</span>
                                  </button>
                                </th>
                              }
                              <th class="rr-th-all">Alle</th>
                            </tr>
                          </thead>
                          <tbody>
                            @for (mod of group.modules; track mod.key) {
                              <tr [class.row-on]="moduleSome(mod.key)">
                                <th scope="row" class="rr-area-name">
                                  <b>{{ mod.label }}</b>
                                  <small>{{ moduleSummary(mod.key) }}</small>
                                </th>
                                @for (a of permActions; track a.key) {
                                  <td class="rr-cell">
                                    @if (permExists(mod.key, a.key)) {
                                      <label class="rr-check" [matTooltip]="a.label + ' – ' + mod.label">
                                        <input type="checkbox" [checked]="hasPerm(mod.key, a.key)" (change)="togglePerm(mod.key, a.key)">
                                        <span class="rr-check-box"><mat-icon>check</mat-icon></span>
                                      </label>
                                    } @else { <span class="rr-na" matTooltip="Für diesen Bereich nicht vorgesehen">–</span> }
                                  </td>
                                }
                                <td class="rr-cell rr-cell-all">
                                  <mat-slide-toggle [checked]="moduleAll(mod.key)" (change)="toggleModule(mod.key)"></mat-slide-toggle>
                                </td>
                              </tr>
                            }
                          </tbody>
                        </table>
                      </div>

                      <!-- Handy: je Bereich eine Karte mit Schaltern -->
                      <div class="rr-mobile-list">
                        @for (mod of group.modules; track mod.key) {
                          <article class="rr-mobile-card">
                            <header>
                              <div><b>{{ mod.label }}</b><small>{{ moduleSummary(mod.key) }}</small></div>
                              <mat-slide-toggle [checked]="moduleAll(mod.key)" (change)="toggleModule(mod.key)"></mat-slide-toggle>
                            </header>
                            <div class="rr-mobile-acts">
                              @for (a of permActions; track a.key) {
                                @if (permExists(mod.key, a.key)) {
                                  <label class="rr-chip" [class.on]="hasPerm(mod.key, a.key)">
                                    <input type="checkbox" [checked]="hasPerm(mod.key, a.key)" (change)="togglePerm(mod.key, a.key)">
                                    <mat-icon>{{ a.icon }}</mat-icon><span>{{ a.label }}</span>
                                  </label>
                                }
                              }
                            </div>
                          </article>
                        }
                      </div>
                    </section>
                  } @empty { <p class="muted">Kein Bereich passt zu diesem Suchbegriff.</p> }
                </div>

                <!-- Klebende Speicherleiste, damit man nach dem Scrollen nicht suchen muss -->
                @if (isDirty()) {
                  <div class="rr-savebar">
                    <span><mat-icon>edit_note</mat-icon>{{ changeSummary() }}</span>
                    <span class="rr-savebar-actions">
                      <button mat-stroked-button (click)="resetPerms()">Verwerfen</button>
                      <button mat-flat-button color="primary" (click)="saveRolePerms()" [disabled]="savingRole()"><mat-icon>save</mat-icon>Speichern</button>
                    </span>
                  </div>
                }
              } @else {
                <div class="rr-placeholder"><mat-icon>admin_panel_settings</mat-icon><p>Bitte links eine Rolle wählen, um deren Rechte zu bearbeiten.</p></div>
              }
            </div>
          </section>
        </mat-tab>
        <mat-tab label="Geräte">
          <div class="responsive-table-wrap">
            <table mat-table [dataSource]="devices()" class="data-table">
              <ng-container matColumnDef="device_name"><th mat-header-cell *matHeaderCellDef>Gerät</th><td mat-cell *matCellDef="let row"><b>{{ row['device_name'] }}</b><small>{{ row['user_agent'] }}</small></td></ng-container>
              <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let row"><span class="status-pill" [ngClass]="'status-' + (row['status'] || 'open')">{{ row['status'] || '—' }}</span></td></ng-container>
              <ng-container matColumnDef="ip"><th mat-header-cell *matHeaderCellDef>IP</th><td mat-cell *matCellDef="let row">{{ row['ip'] || '—' }}</td></ng-container>
              <ng-container matColumnDef="last_seen_at"><th mat-header-cell *matHeaderCellDef>Zuletzt gesehen</th><td mat-cell *matCellDef="let row">{{ row['last_seen_at'] | date:'dd.MM.yyyy HH:mm' }}</td></ng-container>
              <tr mat-header-row *matHeaderRowDef="deviceColumns"></tr><tr mat-row *matRowDef="let row; columns: deviceColumns"></tr>
            </table>
          </div>
        </mat-tab>
        <mat-tab label="Microsoft SSO">
          <section class="admin-sso-grid">
            <mat-card class="square-card admin-panel-card">
              <h2>Microsoft Single Sign-On</h2>
              <form [formGroup]="ssoForm" class="form-grid one square-form">
                <mat-form-field appearance="outline"><mat-label>Microsoft Login aktivieren</mat-label><mat-select formControlName="enabled"><mat-option [value]="true">Ja</mat-option><mat-option [value]="false">Nein</mat-option></mat-select></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Microsoft Tenant-ID</mat-label><input matInput formControlName="provider_tenant_id" placeholder="common oder Tenant-ID"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Client-ID</mat-label><input matInput formControlName="client_id"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Client Secret neu setzen</mat-label><input matInput type="password" formControlName="client_secret" autocomplete="off"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Redirect-URI</mat-label><input matInput formControlName="redirect_uri"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Erlaubte E-Mail-Domains</mat-label><input matInput formControlName="allowed_domains" placeholder="firma.de; partner.de"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Neue Benutzer automatisch vorbereiten</mat-label><mat-select formControlName="auto_provision"><mat-option [value]="true">Ja</mat-option><mat-option [value]="false">Nein</mat-option></mat-select></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Standardrolle für neue Benutzer</mat-label><mat-select formControlName="default_role_id"><mat-option value="">Keine</mat-option>@for (role of roles(); track role['id']) { <mat-option [value]="role['id']">{{ role['name'] }}</mat-option> }</mat-select></mat-form-field>
              </form>
              <div class="button-row"><button mat-flat-button color="primary" (click)="saveSso()"><mat-icon>save</mat-icon> SSO speichern</button></div>
            </mat-card>
            <mat-card class="square-card admin-panel-card">
              <h2>Status</h2>
              <dl class="admin-kpi-list"><div><dt>Aktiv</dt><dd>{{ ssoConfig()?.['enabled'] ? 'ja' : 'nein' }}</dd></div><div><dt>Quelle</dt><dd>{{ ssoConfig()?.['source'] || '—' }}</dd></div><div><dt>Secret</dt><dd>{{ ssoConfig()?.['has_client_secret'] ? 'hinterlegt' : 'fehlt' }}</dd></div></dl>
            </mat-card>
          </section>
        </mat-tab>

      </mat-tab-group>
    </mat-card>
  `
})
export class AdminManagementComponent {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  openCreateUser(): void {
    const ref = this.dialog.open(UserCreateDialogComponent, { width: 'min(720px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', autoFocus: false, data: { roles: this.roles() } });
    ref.afterClosed().subscribe((res) => {
      if (res?.created) { this.snack.open('Benutzer angelegt.', 'OK', { duration: 2500 }); this.load(); }
    });
  }
  openEditUser(user: GenericRecord): void {
    const ref = this.dialog.open(UserEditDialogComponent, {
      width: 'min(720px, 96vw)', maxWidth: '96vw', panelClass: 'ew-dialog', autoFocus: false,
      data: { user, roles: this.roles(), assignedRoleIds: this.selectedRoles(String(user['id'])) }
    });
    ref.afterClosed().subscribe((res) => {
      if (res?.updated) {
        // Rollen-Map lokal aktualisieren, dann frisch laden.
        this.userRoleMap.update((map) => ({ ...map, [String(user['id'])]: (res.roleIds || []).map(String) }));
        this.snack.open('Benutzer gespeichert.', 'OK', { duration: 2500 });
        this.load();
      }
    });
  }
  private readonly fb = inject(FormBuilder);
  readonly users = signal<GenericRecord[]>([]); readonly roles = signal<GenericRecord[]>([]); readonly devices = signal<GenericRecord[]>([]); readonly audit = signal<GenericRecord[]>([]); readonly ssoConfig = signal<GenericRecord | null>(null);
  readonly userRoleMap = signal<Record<string, string[]>>({});
  userSearch = '';
  // Rollen & Rechte
  readonly rolesFull = signal<GenericRecord[]>([]);
  readonly allPermissions = signal<string[]>([]);
  readonly selectedRoleId = signal<string>('');
  permSearch = '';
  readonly editPerms = signal<Set<string>>(new Set());
  readonly originalPerms = signal<Set<string>>(new Set());
  readonly savingRole = signal(false);
  newRoleName = '';
  roleSearch = '';
  /** Die sechs Rechtearten, die die API je Bereich kennt. */
  readonly permActions = [
    { key: 'read',    label: 'Ansehen',    icon: 'visibility',   hint: 'Datensätze des Bereichs öffnen und lesen' },
    { key: 'write',   label: 'Bearbeiten', icon: 'edit',         hint: 'Anlegen und Ändern von Datensätzen' },
    { key: 'delete',  label: 'Löschen',    icon: 'delete',       hint: 'Datensätze löschen' },
    { key: 'export',  label: 'Export',     icon: 'download',     hint: 'PDF- und Listenexport' },
    { key: 'share',   label: 'Teilen',     icon: 'share',        hint: 'Weitergabe an Dritte, z. B. Freigaben' },
    { key: 'archive', label: 'Archiv',     icon: 'inventory_2',  hint: 'Archivieren und archivierte Daten einsehen' }
  ];
  /** Welche Rechtearten die jeweilige Vorlage setzt. */
  private readonly presets: Record<string, string[]> = {
    none: [],
    read: ['read', 'export'],
    edit: ['read', 'write', 'export'],
    full: ['read', 'write', 'delete', 'export', 'share', 'archive']
  };
  readonly selectedRole = computed(() => this.rolesFull().find((r) => r['id'] === this.selectedRoleId()) || null);
  readonly filteredRoles = computed(() => {
    const q = this.roleSearch.trim().toLowerCase();
    const list = this.rolesFull();
    if (!q) return list;
    return list.filter((r) => String(r['name'] || '').toLowerCase().includes(q));
  });
  // Module -> Kategorie (für gruppierte Matrix)
  private moduleCategory(key: string): string {
    const map: Record<string, string> = {
      projects: 'Aufträge & Baustellen', construction_sites: 'Aufträge & Baustellen', sites: 'Aufträge & Baustellen', inspections: 'Aufträge & Baustellen', acceptances: 'Aufträge & Baustellen', rooms: 'Aufträge & Baustellen', defects: 'Aufträge & Baustellen', quality: 'Aufträge & Baustellen', warranty: 'Aufträge & Baustellen',
      customers: 'Büro', contacts: 'Büro', documents: 'Büro', photos: 'Büro', reports: 'Büro', reportstemplates: 'Büro', tasks: 'Büro', notifications: 'Büro', calendar: 'Planung', planning: 'Planung', crews: 'Planung', crew_members: 'Planung',
      vehicles: 'Ressourcen', machines: 'Ressourcen', materials: 'Ressourcen', screed_types: 'Stammdaten', pumps: 'Ressourcen',
      users: 'Administration', roles: 'Administration', admin: 'Administration', integrations: 'Administration', security: 'Administration', devices: 'Administration', audit: 'Administration', dashboard: 'Administration'
    };
    return map[key] || 'Weitere';
  }
  private readonly categoryOrder = ['Aufträge & Baustellen', 'Büro', 'Planung', 'Ressourcen', 'Stammdaten', 'Administration', 'Weitere'];
  /** Bereiche, die derzeit nicht angeboten werden. Das Kundenportal ist
   *  abgeschaltet – dann darf man dafür auch keine Rechte mehr vergeben. */
  private readonly hiddenAreas = new Set(['portal', '*']);
  readonly permGroups = computed(() => {
    const set = new Set<string>();
    this.allPermissions().forEach((code) => { const m = String(code).split('.')[0]; if (m && !this.hiddenAreas.has(m)) set.add(m); });
    const q = this.permSearch.trim().toLowerCase();
    const mods = [...set]
      .map((key) => ({ key, label: this.moduleLabel(key), category: this.moduleCategory(key) }))
      .filter((item) => !q || item.key.toLowerCase().includes(q) || item.label.toLowerCase().includes(q))
      .sort((a, b) => a.label.localeCompare(b.label));
    const byCat = new Map<string, { key: string; label: string }[]>();
    mods.forEach((m) => { if (!byCat.has(m.category)) byCat.set(m.category, []); byCat.get(m.category)!.push({ key: m.key, label: m.label }); });
    return [...byCat.entries()]
      .map(([category, modules]) => ({ category, modules }))
      .sort((a, b) => this.categoryOrder.indexOf(a.category) - this.categoryOrder.indexOf(b.category));
  });
  readonly isDirty = computed(() => {
    const a = this.editPerms(); const b = this.originalPerms();
    if (a.size !== b.size) return true;
    for (const x of a) if (!b.has(x)) return true;
    return false;
  });
  readonly permCount = computed(() => [...this.editPerms()].filter((c) => c !== '*').length);
  totalPermSlots(): number { return this.selectablePermissions().length; }
  setAll(on: boolean): void { this.editPerms.set(on ? new Set(this.selectablePermissions()) : new Set()); }

  /** Alle vergebbaren Einzelrechte (ohne Wildcard und ohne versteckte Bereiche). */
  private selectablePermissions(): string[] {
    return this.allPermissions().filter((code) => !this.hiddenAreas.has(String(code).split('.')[0]));
  }

  // ---- Vollzugriff ('*') -------------------------------------------------
  hasWildcard(): boolean { return this.editPerms().has('*'); }
  toggleWildcard(): void {
    const s = new Set(this.editPerms());
    s.has('*') ? s.delete('*') : s.add('*');
    this.editPerms.set(s);
  }
  roleHasFullAccess(role: GenericRecord): boolean { return ((role['permissions'] || []) as string[]).includes('*'); }
  roleRightCount(role: GenericRecord): number {
    const list = ((role['permissions'] || []) as string[]).filter((c) => c !== '*');
    return list.length;
  }

  // ---- Vorlagen ----------------------------------------------------------
  /** Setzt eine Vorlage auf ALLE gerade sichtbaren Bereiche (Filter beachten). */
  applyPreset(preset: 'none' | 'read' | 'edit' | 'full'): void {
    const actions = this.presets[preset] || [];
    const s = new Set(this.editPerms());
    this.permGroups().forEach((group) => group.modules.forEach((mod) => {
      this.permActions.forEach((a) => {
        if (!this.permExists(mod.key, a.key)) return;
        const code = `${mod.key}.${a.key}`;
        actions.includes(a.key) ? s.add(code) : s.delete(code);
      });
    }));
    this.editPerms.set(s);
  }
  /** Vorlage nur für eine Kategorie. */
  setGroup(group: { modules: { key: string }[] }, preset: 'none' | 'read' | 'full'): void {
    const actions = this.presets[preset] || [];
    const s = new Set(this.editPerms());
    group.modules.forEach((mod) => this.permActions.forEach((a) => {
      if (!this.permExists(mod.key, a.key)) return;
      const code = `${mod.key}.${a.key}`;
      actions.includes(a.key) ? s.add(code) : s.delete(code);
    }));
    this.editPerms.set(s);
  }
  /** Eine ganze Spalte (z. B. \"Löschen\") für eine Kategorie umschalten. */
  toggleColumn(group: { modules: { key: string }[] }, action: string): void {
    const relevant = group.modules.filter((m) => this.permExists(m.key, action));
    const allOn = relevant.length > 0 && relevant.every((m) => this.editPerms().has(`${m.key}.${action}`));
    const s = new Set(this.editPerms());
    relevant.forEach((m) => { const code = `${m.key}.${action}`; allOn ? s.delete(code) : s.add(code); });
    this.editPerms.set(s);
  }

  // ---- Zusammenfassungen -------------------------------------------------
  /** Klartext, was in einem Bereich erlaubt ist. */
  moduleSummary(mod: string): string {
    const active = this.permActions.filter((a) => this.permExists(mod, a.key) && this.editPerms().has(`${mod}.${a.key}`));
    if (!active.length) return 'Kein Zugriff';
    const possible = this.permActions.filter((a) => this.permExists(mod, a.key));
    if (active.length === possible.length) return 'Voller Zugriff';
    return active.map((a) => a.label).join(', ');
  }
  /** Kurzfassung der ungespeicherten Änderungen für die Speicherleiste. */
  changeSummary(): string {
    const now = this.editPerms(); const before = this.originalPerms();
    let added = 0; let removed = 0;
    now.forEach((c) => { if (!before.has(c)) added += 1; });
    before.forEach((c) => { if (!now.has(c)) removed += 1; });
    const parts: string[] = [];
    if (added) parts.push(`${added} Recht${added === 1 ? '' : 'e'} ergänzt`);
    if (removed) parts.push(`${removed} Recht${removed === 1 ? '' : 'e'} entzogen`);
    return parts.join(' · ') || 'Änderungen vorhanden';
  }
  resetPerms(): void { this.editPerms.set(new Set(this.originalPerms())); }
  moduleSome(mod: string): boolean { return this.permActions.some((a) => this.permExists(mod, a.key) && this.editPerms().has(`${mod}.${a.key}`)); }
  groupAll(group: { modules: { key: string }[] }): boolean { return group.modules.every((m) => this.moduleAll(m.key)); }
  groupSome(group: { modules: { key: string }[] }): boolean { return group.modules.some((m) => this.moduleSome(m.key)); }
  toggleGroup(group: { modules: { key: string }[] }): void {
    const all = this.groupAll(group); const s = new Set(this.editPerms());
    group.modules.forEach((m) => this.permActions.forEach((a) => { if (this.permExists(m.key, a.key)) { const c = `${m.key}.${a.key}`; all ? s.delete(c) : s.add(c); } }));
    this.editPerms.set(s);
  }
  readonly savingUserId = signal<string>('');
  readonly userColumns = ['name', 'roles', 'two_factor_enabled', 'is_active', 'last_login_at', 'actions'];
  readonly deviceColumns = ['device_name', 'status', 'ip', 'last_seen_at'];
  readonly auditColumns = ['entity_type', 'action', 'entity_id', 'created_at'];
  readonly ssoForm = this.fb.nonNullable.group({ enabled: [false], provider_tenant_id: ['common'], client_id: [''], client_secret: [''], redirect_uri: [''], allowed_domains: [''], auto_provision: [false], default_role_id: [''] });
  readonly activeUsers = computed(() => this.users().filter((u) => !!u['is_active']));
  readonly inactiveUsers = computed(() => this.users().filter((u) => !u['is_active']));
  readonly activeDevices = computed(() => this.devices().filter((d) => String(d['status'] || '').toLowerCase() === 'active'));
  constructor() { this.load(); this.loadRoles(); }
  load(): void {
    forkJoin({ users: this.api.list('users', '', 500), roles: this.api.adminRoles(), devices: this.api.list('device_sessions', '', 500), userRoles: this.api.adminUserRoles(), sso: this.api.adminMicrosoftSso() }).subscribe({
      next: ({ users, roles, devices, userRoles, sso }) => { this.users.set(users.items); this.roles.set(roles.items || []); this.devices.set(devices.items); this.userRoleMap.set(userRoles['map'] || {}); this.ssoConfig.set(sso); this.ssoForm.patchValue({ enabled: !!sso['enabled'], provider_tenant_id: sso['provider_tenant_id'] || 'common', client_id: sso['client_id'] === 'configured' ? '' : (sso['client_id'] || ''), client_secret: '', redirect_uri: sso['redirect_uri'] || '', allowed_domains: sso['allowed_domains'] || '', auto_provision: !!sso['auto_provision'], default_role_id: sso['default_role_id'] || '' }); },
      error: () => this.snack.open('Admin-Daten konnten nicht geladen werden', 'OK', { duration: 3500 })
    });
  }
  selectedRoles(userId: string): string[] { return this.userRoleMap()[userId] || []; }
  toggleUserActive(user: GenericRecord): void {
    const next = !user['is_active'];
    this.api.adminUpdateUser(String(user['id']), { is_active: next }).subscribe({
      next: () => { this.users.update((list) => list.map((u) => u['id'] === user['id'] ? { ...u, is_active: next } : u)); this.snack.open(next ? 'Benutzer entsperrt.' : 'Benutzer gesperrt.', 'OK', { duration: 2200 }); },
      error: () => this.snack.open('Status konnte nicht geändert werden.', 'OK', { duration: 3000 })
    });
  }
  selectedRoleNames(userId: string): string[] {
    const ids = new Set(this.selectedRoles(userId));
    return this.roles().filter((role) => ids.has(String(role['id']))).map((role) => String(role['name']));
  }
  hasUserRole(userId: string, roleId: string): boolean { return this.selectedRoles(userId).map(String).includes(String(roleId)); }
  toggleUserRole(user: GenericRecord, roleId: string, checked: boolean): void {
    const current = new Set(this.selectedRoles(user['id']).map(String));
    checked ? current.add(String(roleId)) : current.delete(String(roleId));
    this.saveRoles(user, [...current]);
  }
  filteredUsers(): GenericRecord[] {
    const q = this.userSearch.trim().toLowerCase();
    if (!q) return this.users();
    return this.users().filter((user) => [user['name'], user['email'], this.selectedRoleNames(user['id']).join(' ')].join(' ').toLowerCase().includes(q));
  }

  loadRoles(selectId?: string): void {
    forkJoin({ roles: this.api.adminRoles(), perms: this.api.adminPermissions() }).subscribe({
      next: ({ roles, perms }) => {
        this.rolesFull.set(roles.items || []);
        this.allPermissions.set((perms.items || []).map((p) => String(p['code'])));
        const sel = selectId || this.selectedRoleId() || (roles.items || [])[0]?.['id'];
        const role = (roles.items || []).find((r) => r['id'] === sel);
        if (role) this.selectRole(role);
      },
      error: () => this.snack.open('Rollen konnten nicht geladen werden', 'OK', { duration: 3500 })
    });
  }
  selectRole(role: GenericRecord): void { this.selectedRoleId.set(String(role['id'])); const set = new Set((role['permissions'] || []) as string[]); this.editPerms.set(new Set(set)); this.originalPerms.set(new Set(set)); }
  permExists(mod: string, action: string): boolean { return this.allPermissions().includes(`${mod}.${action}`); }
  hasPerm(mod: string, action: string): boolean { return this.editPerms().has(`${mod}.${action}`); }
  togglePerm(mod: string, action: string): void { const code = `${mod}.${action}`; const s = new Set(this.editPerms()); s.has(code) ? s.delete(code) : s.add(code); this.editPerms.set(s); }
  moduleAll(mod: string): boolean { return this.permActions.every((a) => !this.permExists(mod, a.key) || this.editPerms().has(`${mod}.${a.key}`)); }
  toggleModule(mod: string): void { const all = this.moduleAll(mod); const s = new Set(this.editPerms()); this.permActions.forEach((a) => { if (this.permExists(mod, a.key)) { const c = `${mod}.${a.key}`; all ? s.delete(c) : s.add(c); } }); this.editPerms.set(s); }
  saveRolePerms(): void {
    const role = this.selectedRole(); if (!role) return;
    this.savingRole.set(true);
    this.api.adminSetRolePermissions(String(role['id']), [...this.editPerms()]).subscribe({
      next: () => { this.savingRole.set(false); this.originalPerms.set(new Set(this.editPerms())); this.snack.open('Rechte gespeichert', 'OK', { duration: 2000 }); this.loadRoles(String(role['id'])); },
      error: () => { this.savingRole.set(false); this.snack.open('Rechte konnten nicht gespeichert werden', 'OK', { duration: 3500 }); }
    });
  }
  createRole(): void {
    const name = this.newRoleName.trim(); if (!name) return;
    this.api.adminCreateRole({ name }).subscribe({
      next: (res) => { this.newRoleName = ''; this.snack.open('Rolle angelegt', 'OK', { duration: 2000 }); this.loadRoles(String(res['id'] || '')); this.load(); },
      error: () => this.snack.open('Rolle konnte nicht angelegt werden', 'OK', { duration: 3500 })
    });
  }
  moduleLabel(key: string): string {
    const map: Record<string, string> = {
      customers: 'Kunden', projects: 'Aufträge', construction_sites: 'Baustellen', sites: 'Baustellen', crews: 'Kolonnen', crew_members: 'Kolonnenmitglieder',
      vehicles: 'Fahrzeuge', machines: 'Maschinen', users: 'Benutzer', roles: 'Rollen', documents: 'Dokumente', photos: 'Fotos', reports: 'Berichte',
      tasks: 'Aufgaben', defects: 'Mängel', calendar: 'Kalender', planning: 'Planung', quality: 'Qualität / QM', inspections: 'Begehungen', contacts: 'Kontakte',
      screed_types: 'Estricharten', materials: 'Material', admin: 'Administration', audit: 'Audit', dashboard: 'Übersicht', integrations: 'Integrationen',
      security: 'Sicherheit', notifications: 'Benachrichtigungen', devices: 'Geräte', rooms: 'Räume', acceptances: 'Abnahmen', warranty: 'Gewährleistung', reportstemplates: 'Vorlagen'
    };
    return map[key] || (key.charAt(0).toUpperCase() + key.slice(1).replace(/_/g, ' '));
  }

  saveSso(): void {
    this.api.adminSaveMicrosoftSso(this.ssoForm.getRawValue()).subscribe({
      next: (config) => { this.ssoConfig.set(config); this.snack.open('Microsoft SSO gespeichert', 'OK', { duration: 2400 }); this.load(); },
      error: () => this.snack.open('Microsoft SSO konnte nicht gespeichert werden', 'OK', { duration: 3600 })
    });
  }

  saveRoles(user: GenericRecord, roleIds: string[]): void {
    const ids = [...new Set((roleIds || []).map(String).filter(Boolean))];
    this.savingUserId.set(user['id']);
    this.api.updateUserRoles(user['id'], ids).subscribe({
      next: (res) => { this.userRoleMap.update((map) => ({ ...map, [user['id']]: (res['role_ids'] || ids).map(String) })); this.savingUserId.set(''); this.snack.open('Rollen gespeichert', 'OK', { duration: 2000 }); this.loadRoles(); },
      error: () => { this.savingUserId.set(''); this.snack.open('Rollen konnten nicht gespeichert werden', 'OK', { duration: 3500 }); }
    });
  }
}
