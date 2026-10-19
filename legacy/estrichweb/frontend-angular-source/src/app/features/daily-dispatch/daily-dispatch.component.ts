import { Component, computed, inject, signal, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { forkJoin } from 'rxjs';
import { jsPDF } from 'jspdf';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { AssignmentNoteDialogComponent } from '../../shared/assignment-note-dialog.component';

interface AssignDialogData {
  crewName: string;
  dateLabel: string;
  sites: GenericRecord[];
  defaultFrom: string;
  defaultTo: string;
  /** true = Dialog plant eine Begehung ohne Kolonne. */
  visitMode?: boolean;
  /** Auswahl der zuständigen Person im Begehungsmodus. */
  users?: GenericRecord[];
}

@Component({
  selector: 'ew-dispatch-assign-dialog',
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="asg-head">
      <div class="asg-head-main">
        <span class="asg-crew"><mat-icon>groups</mat-icon>{{ data.crewName }}</span>
        <span class="asg-date">{{ data.dateLabel }}</span>
      </div>
      <button mat-icon-button mat-dialog-close aria-label="Schließen"><mat-icon>close</mat-icon></button>
    </div>

    <div class="asg-controls">
      <!-- Einsatzart: nicht nur Kolonnenarbeit, auch Begehungen planbar -->
      @if (visibleKinds().length > 1) {
      <div class="asg-kinds" role="group" aria-label="Art des Einsatzes">
        @for (k of visibleKinds(); track k.value) {
          <button type="button" class="asg-kind" [class.on]="kind === k.value" (click)="kind = k.value">
            <mat-icon>{{ k.icon }}</mat-icon><span>{{ k.label }}</span>
          </button>
        }
      </div>
      }
      <div class="asg-search">
        <mat-icon>search</mat-icon>
        <input type="text" [(ngModel)]="search" placeholder="Baustelle suchen (Name, Ort, Nr.)" autocomplete="off" autofocus>
        @if (search) { <button type="button" class="asg-clear" (click)="search=''" aria-label="Leeren"><mat-icon>close</mat-icon></button> }
      </div>
      <div class="asg-times">
        <label>Von<input type="time" [(ngModel)]="from"></label>
        <label>Bis<input type="time" [(ngModel)]="to"></label>
      </div>
      @if (data.visitMode && (data.users || []).length) {
        <label class="asg-owner">Zuständig
          <select [(ngModel)]="userId">
            <option value="">— offen —</option>
            @for (u of data.users; track u['id']) { <option [value]="u['id']">{{ u['name'] || u['email'] }}</option> }
          </select>
        </label>
      }
    </div>

    <div class="asg-list" mat-dialog-content>
      @for (s of filtered(); track s['id']) {
        <button type="button" class="asg-item" (click)="pick(s)">
          <span class="asg-item-icon"><mat-icon>add_location_alt</mat-icon></span>
          <span class="asg-item-main">
            <b>{{ s['name'] || s['site_no'] || 'Baustelle' }}</b>
            <small>{{ addr(s) || 'Keine Adresse hinterlegt' }}</small>
          </span>
          <mat-icon class="asg-item-go">chevron_right</mat-icon>
        </button>
      }
      @if (!filtered().length) {
        <div class="asg-empty"><mat-icon>search_off</mat-icon><p>Keine passende aktive Baustelle gefunden.</p></div>
      }
    </div>

    <div class="asg-foot">
      <span>{{ filtered().length }} Baustelle(n)</span>
      <button mat-stroked-button mat-dialog-close>Abbrechen</button>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .asg-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 8px; padding: 16px 18px 6px; }
    .asg-head-main { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .asg-crew { display: flex; align-items: center; gap: 8px; font-size: 1.18rem; font-weight: 800; color: #0f172a; }
    .asg-crew mat-icon { color: var(--accent, #2457a6); }
    .asg-date { color: #64748b; font-size: 0.86rem; font-weight: 600; }
    .asg-controls { padding: 6px 18px 12px; display: flex; flex-direction: column; gap: 10px; border-bottom: 1px solid #eef2f7; }
    .asg-search { display: flex; align-items: center; gap: 8px; border: 1.5px solid #cbd5e1; border-radius: 12px; padding: 0 12px; height: 48px; background: #f8fafc; }
    .asg-search:focus-within { border-color: var(--accent, #2457a6); background: #fff; }
    .asg-search mat-icon { color: #64748b; flex: 0 0 auto; }
    .asg-search input { flex: 1 1 auto; border: none; outline: none; background: transparent; font: inherit; font-size: 1rem; min-width: 0; }
    .asg-clear { border: none; background: transparent; cursor: pointer; color: #94a3b8; display: inline-flex; }
    .asg-clear mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .asg-kinds { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 6px; }
    .asg-kind { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px; padding: 8px 4px; min-height: 58px; border: 1.5px solid #cbd5e1; border-radius: 12px; background: #fff; cursor: pointer; color: #475569; font: inherit; font-size: .74rem; font-weight: 700; text-align: center; line-height: 1.15; }
    .asg-kind mat-icon { color: #64748b; }
    .asg-kind:hover { border-color: #94a3b8; }
    .asg-kind.on { border-color: var(--accent, #2457a6); background: #eef3fb; color: var(--accent, #2457a6); }
    .asg-kind.on mat-icon { color: var(--accent, #2457a6); }
    .asg-times { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .asg-times label { display: flex; flex-direction: column; gap: 3px; font-size: 0.78rem; font-weight: 700; color: #475569; text-transform: uppercase; letter-spacing: .03em; }
    .asg-times input { border: 1.5px solid #cbd5e1; border-radius: 10px; height: 44px; padding: 0 10px; font: inherit; font-size: 1rem; background: #fff; }
    .asg-times input:focus { border-color: var(--accent, #2457a6); outline: none; }
    .asg-owner { display: flex; flex-direction: column; gap: 3px; font-size: 0.78rem; font-weight: 700; color: #475569; text-transform: uppercase; letter-spacing: .03em; }
    .asg-owner select { border: 1.5px solid #cbd5e1; border-radius: 10px; height: 44px; padding: 0 10px; font: inherit; font-size: 1rem; background: #fff; text-transform: none; font-weight: 500; color: #0f172a; }
    .asg-list.mat-mdc-dialog-content { max-height: min(52vh, 460px); padding: 10px 14px; margin: 0; display: flex; flex-direction: column; gap: 8px; }
    .asg-item { display: flex; align-items: center; gap: 12px; width: 100%; text-align: left; background: #fff; border: 1px solid #e2e8f0; border-radius: 14px; padding: 12px 14px; cursor: pointer; min-height: 64px; transition: border-color .12s ease, background .12s ease, box-shadow .12s ease; }
    .asg-item:hover { border-color: var(--accent, #2457a6); background: #f5f8fd; box-shadow: 0 4px 14px rgba(36,87,166,.12); }
    .asg-item-icon { flex: 0 0 auto; width: 42px; height: 42px; border-radius: 12px; display: grid; place-items: center; background: #eef3fb; color: var(--accent, #2457a6); }
    .asg-item-main { flex: 1 1 auto; display: flex; flex-direction: column; min-width: 0; }
    .asg-item-main b { color: #0f172a; font-weight: 700; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .asg-item-main small { color: #64748b; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .asg-item-go { color: #cbd5e1; flex: 0 0 auto; }
    .asg-item:hover .asg-item-go { color: var(--accent, #2457a6); }
    .asg-empty { text-align: center; color: #94a3b8; padding: 30px 12px; }
    .asg-empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: .6; }
    .asg-foot { display: flex; align-items: center; justify-content: space-between; padding: 10px 18px 16px; border-top: 1px solid #eef2f7; color: #64748b; font-size: 0.85rem; font-weight: 600; }
    @media (max-width: 700px) {
      .asg-item { min-height: 68px; }
      .asg-list.mat-mdc-dialog-content { max-height: 60vh; }
    }
  `]
})
export class DispatchAssignDialogComponent {
  private readonly ref = inject(MatDialogRef<DispatchAssignDialogComponent>);
  search = '';
  from: string;
  to: string;
  /** Art des Einsatzes. Begehungen dauern typischerweise nur kurz,
   *  darum bekommt jede Art eine eigene Vorgabezeit. */
  kind = 'work';
  /** Zuständige Person im Begehungsmodus. */
  userId = '';
  /** Terminarten. `crew` = für Kolonnen wählbar, `visit` = im Begehungsfeld. */
  readonly kinds = [
    { value: 'work' as const,              label: 'Estrich',       icon: 'construction',      title: 'Estricharbeiten',  hours: 9, crew: true,  visit: false },
    { value: 'first_inspection' as const,  label: 'Erstbegehung',  icon: 'fact_check',        title: 'Erstbegehung',     hours: 1, crew: false, visit: true },
    { value: 'second_inspection' as const, label: 'Zweitbegehung', icon: 'checklist',         title: 'Zweitbegehung',    hours: 1, crew: false, visit: true },
    { value: 'control' as const,           label: 'Kontrolle',     icon: 'visibility',        title: 'Kontrolle',        hours: 1, crew: false, visit: true },
    { value: 'handover' as const,          label: 'Abnahme',       icon: 'task_alt',          title: 'Abnahme',          hours: 2, crew: false, visit: true },
    { value: 'measurement' as const,       label: 'Aufmaß',        icon: 'straighten',        title: 'Aufmaß',           hours: 1, crew: false, visit: true },
    { value: 'moisture' as const,          label: 'CM-Messung',    icon: 'water_drop',        title: 'CM-Messung',       hours: 1, crew: false, visit: true },
    { value: 'customer' as const,          label: 'Kundentermin',  icon: 'handshake',         title: 'Kundentermin',     hours: 1, crew: false, visit: true },
    { value: 'other' as const,             label: 'Sonstiges',     icon: 'more_horiz',        title: 'Termin',           hours: 2, crew: true,  visit: true }
  ];
  constructor(@Inject(MAT_DIALOG_DATA) public data: AssignDialogData) {
    this.from = data.defaultFrom;
    this.to = data.defaultTo;
    // Im Begehungsmodus ist die Estrichvariante nicht sinnvoll.
    if (data.visitMode) this.kind = 'first_inspection';
  }
  /** Kolonnen erhalten ausschliesslich Arbeitspakete. Begehungen werden im
   *  eigenen Bereich „Begehungen & Besichtigungen" geplant und sind hier
   *  bewusst nicht mehr waehlbar. */
  visibleKinds() { return this.kinds.filter((k) => this.data.visitMode ? k.visit : k.crew); }
  private kindDef() { return this.kinds.find((k) => k.value === this.kind) || this.kinds[0]; }
  addr(s: GenericRecord): string {
    return [s['address'], s['city']].map((v) => String(v ?? '').trim()).filter((v) => v.length > 0).join(', ');
  }
  filtered(): GenericRecord[] {
    const q = this.search.trim().toLowerCase();
    if (!q) return this.data.sites.slice(0, 80);
    return this.data.sites.filter((s) => ['name', 'site_no', 'address', 'city'].map((k) => String(s[k] ?? '').toLowerCase()).some((v) => v.includes(q))).slice(0, 80);
  }
  pick(site: GenericRecord): void {
    const def = this.kindDef();
    let to = (this.to || '16:00').slice(0, 5);
    // Begehungen und kurze Termine: Ende automatisch passend setzen, sofern
    // der Anwender die Zeiten nicht selbst angepasst hat.
    if (this.kind !== 'work' && to === this.data.defaultTo) {
      const [h, m] = (this.from || '07:00').split(':').map(Number);
      const end = new Date(2000, 0, 1, h || 7, m || 0);
      end.setHours(end.getHours() + def.hours);
      to = `${String(end.getHours()).padStart(2, '0')}:${String(end.getMinutes()).padStart(2, '0')}`;
    }
    this.ref.close({ site, from: (this.from || '07:00').slice(0, 5), to, kind: this.kind, title: def.title, userId: this.userId });
  }
}

@Component({
  selector: 'ew-daily-dispatch',
  standalone: true,
  imports: [FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, MatSnackBarModule, MatDialogModule, MatTooltipModule],
  template: `
    <section class="dispatch-page">
      <header class="dispatch-head">
        <div class="dispatch-head-text">
          <p class="eyebrow">Tageseinteilung</p>
          <h1><mat-icon>route</mat-icon>Wer fährt wohin?</h1>
        </div>
        <button mat-flat-button color="primary" class="dispatch-pdf" [disabled]="!assignments().length" (click)="exportPdf()">
          <mat-icon>picture_as_pdf</mat-icon>PDF erzeugen
        </button>
      </header>

      <div class="dispatch-controls">
        <div class="dispatch-datebar">
          <button type="button" class="dispatch-arrow" (click)="shiftDay(-1)" aria-label="Tag zurück"><mat-icon>chevron_left</mat-icon></button>
          <label class="dispatch-date">
            <input type="date" [value]="date()" (change)="setDate($any($event.target).value)">
            <span class="dispatch-dayname">{{ dayLabel() }}</span>
          </label>
          <button type="button" class="dispatch-arrow" (click)="shiftDay(1)" aria-label="Tag vor"><mat-icon>chevron_right</mat-icon></button>
        </div>
        <div class="dispatch-quickdays" role="group" aria-label="Schnellauswahl Tag">
          <button type="button" class="qd-btn" [class.qd-active]="isToday()" [attr.aria-pressed]="isToday()" (click)="goToday()"><mat-icon>today</mat-icon><span>Heute</span></button>
          <button type="button" class="qd-btn" [class.qd-active]="isTomorrow()" [attr.aria-pressed]="isTomorrow()" (click)="goTomorrow()"><mat-icon>wb_twilight</mat-icon><span>Morgen</span></button>
        </div>

        @if (loading()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }

        @if (crews().length && !loading()) {
          <div class="dispatch-summary">
            <mat-icon>groups</mat-icon>
            <span><b>{{ crewsWithWork() }}</b> von <b>{{ crews().length }}</b> Kolonnen eingeteilt · <b>{{ assignments().length }}</b> Baustelle(n)</span>
          </div>
        }
      </div>

      @if (!crews().length && !loading()) {
        <div class="dispatch-empty"><mat-icon>groups</mat-icon><p>Keine Kolonnen vorhanden. Bitte zuerst unter Stammdaten Kolonnen anlegen.</p></div>
      }

      <!-- Begehungen und Besichtigungen: unabhaengig von einer Kolonne planbar,
           weil sie meist ein einzelner Mitarbeiter uebernimmt. -->
      <mat-card class="dispatch-visits" [class.has-work]="visits().length">
        <div class="dispatch-visits-head">
          <div class="dispatch-visits-title">
            <span class="dispatch-visits-icon"><mat-icon>fact_check</mat-icon></span>
            <div>
              <b>Begehungen, Kontrollen &amp; Abnahmen</b>
              <small>{{ visits().length ? visits().length + ' Termin(e) an diesem Tag' : 'Für diesen Tag ist nichts geplant' }}</small>
            </div>
          </div>
          @if (canPlan) {
            <button mat-flat-button color="primary" class="dispatch-visit-add" (click)="openVisit()"><mat-icon>add</mat-icon>Termin planen</button>
          }
        </div>

        @if (visits().length) {
          <div class="dispatch-visits-list">
            @for (v of visits(); track v['id']) {
              <article class="dispatch-visit">
                <span class="dispatch-visit-ic"><mat-icon>{{ visitIcon(v) }}</mat-icon></span>
                <span class="dispatch-visit-time">{{ timeLabel(v) }}</span>
                <div class="dispatch-visit-main">
                  <b>{{ v['title'] || 'Begehung' }}</b>
                  <small>{{ v['site_name'] || v['project_name'] || 'Baustelle' }}</small>
                  @if (siteAddress(v)) { <small class="dispatch-visit-addr">{{ siteAddress(v) }}</small> }
                  @if (visitOwner(v)) { <small class="dispatch-visit-owner"><mat-icon>person</mat-icon>{{ visitOwner(v) }}</small> }
                </div>
                <div class="dispatch-visit-actions">
                  @if (mapsUrl(v)) { <a mat-icon-button [href]="mapsUrl(v)" target="_blank" rel="noopener" matTooltip="Navigation"><mat-icon>directions</mat-icon></a> }
                  <button mat-icon-button class="dispatch-note" [class.has-note]="!!v['notes']" (click)="openNote(v)" [matTooltip]="v['notes'] ? v['notes'] : 'Notiz hinterlegen'" aria-label="Notiz"><mat-icon>sticky_note_2</mat-icon></button>
                  @if (canDeletePlan) { <button mat-icon-button class="dispatch-del" (click)="removeAssignment(v)" matTooltip="Termin entfernen"><mat-icon>close</mat-icon></button> }
                </div>
              </article>
            }
          </div>
        }
      </mat-card>

      <div class="dispatch-crews">
        @for (crew of crews(); track crew['id']) {
          <mat-card class="dispatch-crew-card" [class.has-work]="assignmentsForCrew(crew['id']).length">
            <div class="dispatch-crew-head">
              <div class="dispatch-crew-name"><mat-icon>groups</mat-icon><b>{{ crew['name'] }}</b></div>
              <span class="dispatch-crew-count">{{ assignmentsForCrew(crew['id']).length }} Baustelle(n)</span>
            </div>
            <div class="dispatch-crew-body">
              @for (a of assignmentsForCrew(crew['id']); track a['id']) {
                <div class="dispatch-assignment">
                  <span class="dispatch-assignment-icon"><mat-icon>place</mat-icon></span>
                  <div class="dispatch-assignment-main">
                    <b>{{ a['site_name'] || a['project_name'] || 'Baustelle' }}</b>
                    @if (isInspection(a)) { <span class="dispatch-kind-tag"><mat-icon>fact_check</mat-icon>{{ a['title'] }}</span> }
                    <small><mat-icon>schedule</mat-icon>{{ assignmentTime(a) }}@if (siteAddress(a)) { <span class="dispatch-dot">·</span>{{ siteAddress(a) }} }</small>
                  </div>
                  <span class="dispatch-actions">
                    @if (mapsUrl(a)) { <a mat-icon-button class="dispatch-nav" [href]="mapsUrl(a)" target="_blank" rel="noopener" matTooltip="Navigation" aria-label="Navigation" (click)="$event.stopPropagation()"><mat-icon>directions</mat-icon></a> }
                    <button mat-icon-button class="dispatch-note" [class.has-note]="!!a['notes']" (click)="openNote(a)" [matTooltip]="a['notes'] ? a['notes'] : 'Notiz hinterlegen'" aria-label="Notiz"><mat-icon>sticky_note_2</mat-icon></button>
                    @if (canDeletePlan) { <button mat-icon-button class="dispatch-del" (click)="removeAssignment(a)" matTooltip="Einteilung entfernen" aria-label="Entfernen"><mat-icon>close</mat-icon></button> }
                  </span>
                </div>
              }
              @if (!assignmentsForCrew(crew['id']).length) {
                <p class="dispatch-none"><mat-icon>event_available</mat-icon>Noch nichts eingeplant.</p>
              }
              @if (canPlan) { <button mat-flat-button color="primary" class="dispatch-add" (click)="openAssign(crew)"><mat-icon>add</mat-icon>Baustelle zuweisen</button> }
            </div>
          </mat-card>
        }
      </div>

      @if (crews().length) {
        <div class="dispatch-footer">
          <a mat-stroked-button routerLink="/kolonnen-wochenplaner"><mat-icon>view_week</mat-icon>Zum Wochenplaner</a>
        </div>
      }
    </section>
  `,
  styles: [`
    .dispatch-page { max-width: 1080px; margin: 0 auto; padding: 8px 16px 96px; }
    .dispatch-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .dispatch-head-text { min-width: 0; }
    .dispatch-head .eyebrow { color: #64748b; margin: 0; font-size: 0.8rem; text-transform: uppercase; letter-spacing: .05em; font-weight: 700; }
    .dispatch-head h1 { display: flex; align-items: center; gap: 8px; font-size: 1.45rem; margin: 2px 0 14px; color: #0f172a; }
    .dispatch-head h1 mat-icon { color: var(--accent, #2457a6); }
    .dispatch-pdf { margin-bottom: 14px; font-weight: 700; }

    /* Steuerung und Kolonnen teilen sich dieselbe Breite -> Datumsbox und Karten
       sind links/rechts bündig (keine schmale, zentrierte Steuerspalte mehr). */
    .dispatch-controls { width: 100%; }
    .dispatch-datebar { display: flex; align-items: center; gap: 8px; background: #fff; border: 1px solid #e2e8f0; border-radius: 16px; padding: 8px 10px; position: sticky; top: 6px; z-index: 5; box-shadow: 0 2px 10px rgba(15,23,42,.05); }
    .dispatch-arrow { display: inline-flex; align-items: center; justify-content: center; width: 44px; height: 44px; border-radius: 12px; border: 1px solid #e2e8f0; background: #f8fafc; color: var(--accent, #2457a6); cursor: pointer; flex: 0 0 auto; transition: background .12s ease; }
    .dispatch-arrow:hover { background: #eef2f8; }
    .dispatch-arrow mat-icon { font-size: 26px; width: 26px; height: 26px; }
    .dispatch-date { flex: 1 1 auto; display: flex; flex-direction: column; align-items: center; cursor: pointer; }
    .dispatch-date input { border: none; font: inherit; font-size: 17px; font-weight: 800; text-align: center; background: transparent; color: #0f172a; cursor: pointer; }
    .dispatch-dayname { color: #475569; font-size: 0.82rem; font-weight: 600; }

    .dispatch-quickdays { display: flex; gap: 8px; margin-top: 10px; }
    .qd-btn { flex: 1 1 50%; min-height: 46px; display: inline-flex; align-items: center; justify-content: center; gap: 8px; border: 1.5px solid #cbd5e1; border-radius: 12px; background: #fff; color: #1e293b; font-size: 0.98rem; font-weight: 700; cursor: pointer; transition: background .12s ease, border-color .12s ease, color .12s ease; -webkit-tap-highlight-color: transparent; }
    .qd-btn mat-icon { font-size: 20px; width: 20px; height: 20px; color: var(--accent, #2457a6); }
    .qd-btn:hover { border-color: #94a3b8; background: #f8fafc; }
    .qd-btn.qd-active { background: var(--accent, #2457a6); border-color: var(--accent, #2457a6); color: #fff; box-shadow: 0 4px 14px rgba(36,87,166,.28); }
    .qd-btn.qd-active mat-icon, .qd-btn.qd-active span { color: #fff; }

    .dispatch-summary { display: flex; align-items: center; gap: 8px; margin-top: 12px; padding: 11px 14px; background: #eef3fb; border: 1px solid #cdddf4; border-radius: 12px; color: #1d4b8f; font-size: 0.92rem; font-weight: 600; }
    .dispatch-summary mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .dispatch-summary b { font-weight: 800; }

    .dispatch-visits { margin-top: 16px; padding: 14px; border-radius: 12px; border: 1px solid var(--line, #e2e8f0); }
    .dispatch-visits.has-work { border-color: #fed7aa; }
    .dispatch-visits-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .dispatch-visits-title { display: flex; align-items: center; gap: 10px; min-width: 0; }
    .dispatch-visits-icon { display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px; border-radius: 10px; background: #fff7ed; flex: 0 0 auto; }
    .dispatch-visits-icon mat-icon { color: #b45309; }
    .dispatch-visits-title b { display: block; font-size: 1.02rem; }
    .dispatch-visits-title small { color: #64748b; font-size: .82rem; }
    .dispatch-visits-list { display: grid; grid-template-columns: 1fr; gap: 8px; margin-top: 12px; }
    @media (min-width: 900px) { .dispatch-visits-list { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (min-width: 1400px) { .dispatch-visits-list { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
    .dispatch-visit {
      display: grid; grid-template-columns: 32px 62px minmax(0, 1fr);
      grid-template-areas: 'ic time main' 'acts acts acts';
      gap: 6px 10px; align-items: start;
      padding: 10px 12px 8px; background: #fffaf5; border: 1px solid #fde7cf; border-radius: 10px;
    }
    .dispatch-visit-ic { grid-area: ic; align-self: center; }
    .dispatch-visit-time { grid-area: time; align-self: center; }
    .dispatch-visit-main { grid-area: main; }
    .dispatch-visit-actions {
      grid-area: acts; justify-content: flex-end;
      padding-top: 6px; margin-top: 2px; border-top: 1px solid #fde7cf;
    }
    .dispatch-visit-main b { white-space: normal !important; line-height: 1.3; }
    .dispatch-visit-main small { white-space: normal !important; }
    .dispatch-visit-ic { display: inline-grid; place-items: center; width: 32px; height: 32px; border-radius: 8px; background: #fff2e2; flex: 0 0 auto; }
    .dispatch-visit-ic mat-icon { color: #b45309 !important; font-size: 18px !important; width: 18px !important; height: 18px !important; }
    .dispatch-visit-time { flex: 0 0 auto; min-width: 62px; text-align: center; font-size: .74rem; font-weight: 800; color: #9a3412; background: #fff2e2; border-radius: 6px; padding: 5px 6px; }
    .dispatch-visit-main { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; }
    .dispatch-visit-main b { font-size: .95rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dispatch-visit-main small { color: #64748b; font-size: .78rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dispatch-visit-addr { color: #94a3b8 !important; }
    .dispatch-visit-owner { display: inline-flex !important; align-items: center; gap: 4px; color: #475569 !important; }
    .dispatch-visit-owner mat-icon { font-size: 13px !important; }
    .dispatch-visit-actions { display: flex; align-items: center; gap: 2px; flex: 0 0 auto; }
    @media (max-width: 700px) {
      .dispatch-visit-add { width: 100%; min-height: 48px; }
      .dispatch-visits-head { flex-direction: column; align-items: stretch; }
    }
    /* Kolonnenraster.
       Vorher feste 2/3/4 Spalten je Fensterbreite – bei nur zwei Kolonnen
       blieben die Karten schmal, obwohl rechts viel Platz frei war. Der Text
       brach mehrfach um und die Symbolknöpfe rutschten in eine zweite Zeile.
       Mit auto-fit fuellen wenige Kolonnen die Breite aus; viele Kolonnen
       werden weiterhin umbrochen. */
    .dispatch-crews {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
      gap: 14px; margin-top: 16px; align-items: start;
    }
    @media (min-width: 1600px) { .dispatch-page { max-width: 1500px; } }

    .dispatch-crew-card {
      border-radius: 18px; padding: 0; overflow: hidden;
      border: 1px solid #e2e8f0; box-shadow: 0 1px 4px rgba(15,23,42,.05);
      /* Die Einteilung darin richtet sich nach der Breite DIESER Karte,
         nicht nach der Fensterbreite – nur so stimmt die Aufteilung, egal
         wie viele Kolonnen nebeneinander stehen. */
      container-type: inline-size;
    }
    .dispatch-crew-card.has-work { border-color: #cdddf4; }
    .dispatch-crew-head { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 13px 16px; background: var(--accent, #2457a6); color: #fff; }
    .dispatch-crew-name { display: flex; align-items: center; gap: 9px; font-size: 1.08rem; font-weight: 800; min-width: 0; }
    .dispatch-crew-name b { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dispatch-crew-name mat-icon { color: #fff; flex: 0 0 auto; }
    .dispatch-crew-count { color: #fff; background: rgba(255,255,255,.20); border-radius: 999px; padding: 3px 11px; font-size: 0.8rem; font-weight: 700; white-space: nowrap; flex: 0 0 auto; }
    .dispatch-crew-body { padding: 12px 14px 14px; }

    .dispatch-assignment { display: flex; align-items: center; gap: 10px; padding: 10px 10px; background: #f8fafc; border: 1px solid #e6edf5; border-radius: 12px; margin-bottom: 8px; }
    .dispatch-assignment-icon { flex: 0 0 auto; width: 36px; height: 36px; border-radius: 10px; display: grid; place-items: center; background: #eef3fb; color: var(--accent, #2457a6); }
    .dispatch-assignment-main { flex: 1 1 auto; display: flex; flex-direction: column; min-width: 0; gap: 2px; }
    .dispatch-assignment-main b { color: #0f172a; font-weight: 700; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dispatch-assignment-main small { color: #475569; font-weight: 500; display: flex; align-items: center; gap: 5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dispatch-assignment-main small mat-icon { font-size: 15px; width: 15px; height: 15px; color: #94a3b8; flex: 0 0 auto; }
    .dispatch-dot { color: #cbd5e1; }
    .dispatch-nav { color: var(--accent, #2457a6); flex: 0 0 auto; }
    .dispatch-del { color: #b42318; flex: 0 0 auto; }
    /* Aktionen als eigene Gruppe – vorher drueckten drei Symbolknoepfe den
       Text so weit zusammen, dass nur noch "Test ..." uebrig blieb. */
    /* Die drei Knöpfe bleiben IMMER in einer Reihe. Ohne nowrap sind sie
       bei knapper Breite umgebrochen – im Screenshot standen Navigation
       oben und Notiz/Löschen darunter. */
    .dispatch-actions {
      display: inline-flex; align-items: center;
      flex-wrap: nowrap !important; white-space: nowrap;
      gap: 2px; flex: 0 0 auto; margin-left: 4px;
    }
    .dispatch-actions > * { flex: none; }
    .dispatch-assignment { align-items: center; gap: 10px; padding: 10px 10px 10px 12px; }
    .dispatch-assignment-main { flex: 1 1 auto; min-width: 0; }
    .dispatch-assignment-main b { display: block; font-size: .92rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dispatch-assignment-main small { display: flex; align-items: center; gap: 4px; min-width: 0; }
    .dispatch-assignment-main small > :not(mat-icon) { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    /* Einteilung als Raster statt Flex-Zeile.
       Vorher standen Text und drei Symbolknöpfe in EINER Zeile – bei zwei
       Kolonnenkarten nebeneinander blieb für den Baustellennamen kaum Platz,
       die Knöpfe haben ihn faktisch verdeckt. Jetzt bekommt der Text die
       volle Breite, die Aktionen sitzen in einer eigenen Zeile darunter.
       Dadurch ist die Karte etwas höher, aber vollständig lesbar. */
    .dispatch-assignment {
      display: grid !important;
      grid-template-columns: 36px minmax(0, 1fr);
      grid-template-areas:
        'icon main'
        'acts acts';
      gap: 8px 10px;
      align-items: start !important;
      padding: 11px 12px 9px !important;
    }
    .dispatch-assignment-icon { grid-area: icon; align-self: center; }
    .dispatch-assignment-main { grid-area: main; }
    .dispatch-actions {
      grid-area: acts;
      display: flex; align-items: center; justify-content: flex-end; gap: 4px;
      margin: 2px 0 0 !important;
      padding-top: 7px;
      border-top: 1px solid #e6edf5;
    }
    /* Name darf jetzt umbrechen statt abgeschnitten zu werden. */
    .dispatch-assignment-main b {
      white-space: normal !important;
      overflow: visible !important;
      line-height: 1.3;
      word-break: break-word;
    }
    .dispatch-assignment-main small { white-space: normal !important; align-items: flex-start; gap: 5px; }
    .dispatch-dot { opacity: .5; }

    /* Ist die Karte breit genug, stehen Symbol, Text und Aktionen in EINER
       Zeile – so wie es sich für eine Einteilung gehört. Gemessen wird die
       Breite der Kolonnenkarte, nicht die des Fensters. */
    @container (min-width: 330px) {
      .dispatch-assignment {
        grid-template-columns: 34px minmax(0, 1fr) auto;
        grid-template-areas: 'icon main acts';
        align-items: center !important;
        padding: 10px 12px !important;
      }
      .dispatch-actions {
        border-top: 0;
        padding-top: 0;
        margin: 0 !important;
        align-self: center;
        flex-wrap: nowrap !important;
      }
      .dispatch-assignment-main b { white-space: nowrap !important; overflow: hidden !important; text-overflow: ellipsis; }
      .dispatch-assignment-main small { white-space: nowrap !important; overflow: hidden; text-overflow: ellipsis; align-items: center; }
    }
    .dispatch-assignment-main small mat-icon { margin-top: 1px; }
    .dispatch-note.mat-mdc-icon-button { width: 32px !important; height: 32px !important; flex: 0 0 auto; }
    .dispatch-nav.mat-mdc-icon-button, .dispatch-del.mat-mdc-icon-button { width: 32px !important; height: 32px !important; }
    .dispatch-actions .mat-icon { font-size: 18px !important; width: 18px !important; height: 18px !important; }
    .dispatch-note mat-icon { color: #94a3b8 !important; }
    .dispatch-note.has-note mat-icon { color: #b45309 !important; }
    .dispatch-notetext { display: inline-flex !important; align-items: flex-start; gap: 4px; color: #9a3412 !important; white-space: normal !important; }
    .dispatch-notetext mat-icon { font-size: 13px !important; width: 13px !important; height: 13px !important; flex: 0 0 auto; margin-top: 2px; }
    .dispatch-none { display: flex; align-items: center; justify-content: center; gap: 8px; color: #94a3b8; font-size: 0.9rem; margin: 4px 0 12px; padding: 10px; border: 1px dashed #e2e8f0; border-radius: 10px; }
    .dispatch-none mat-icon { font-size: 20px; width: 20px; height: 20px; }

    .dispatch-add { width: 100%; min-height: 48px; font-weight: 700; margin-top: 2px; }
    .dispatch-empty { text-align: center; color: #64748b; padding: 34px 12px; }
    .dispatch-empty mat-icon { font-size: 44px; width: 44px; height: 44px; opacity: .5; }
    .dispatch-footer { margin-top: 22px; text-align: center; }
    @media (max-width: 700px) {
      .dispatch-page { padding: 6px 10px 100px; }
      .dispatch-head { align-items: center; }
      .dispatch-pdf { width: 100%; min-height: 46px; order: 3; margin-top: 4px; }
      .dispatch-crew-body { padding: 14px; }
      .dispatch-assignment { padding: 12px; }
      .dispatch-assignment-main b { font-size: 1.02rem; }
      .dispatch-add { min-height: 52px; font-size: 1rem; }
      .dispatch-datebar { padding: 9px 10px; }
      .dispatch-date input { font-size: 17px; }
      .qd-btn { min-height: 50px; font-size: 1rem; }
    }
  `]
})
export class DailyDispatchComponent {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly auth = inject(AuthService);
  /** Ohne Planungsrecht ist die Tageseinteilung eine reine Ansicht. */
  readonly canPlan = this.auth.canWrite('planning');
  readonly canDeletePlan = this.auth.canDelete('planning');

  readonly date = signal(this.tomorrowIso());
  readonly crews = signal<GenericRecord[]>([]);
  readonly users = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly projects = signal<GenericRecord[]>([]);
  readonly assignments = signal<GenericRecord[]>([]);
  readonly loading = signal(false);
  readonly startTime = signal('07:00');
  readonly endTime = signal('16:00');
  private companyName = 'EstrichWeb';

  readonly dayLabel = computed(() => {
    const d = new Date(this.date() + 'T00:00:00');
    return d.toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' });
  });

  constructor() {
    this.loading.set(true);
    this.api.appSettings().subscribe({ next: (res) => { const n = res?.['company']?.['name']; if (n) this.companyName = String(n); }, error: () => {} });
    forkJoin({
      crews: this.api.list('crews', '', 200),
      sites: this.api.list('construction_sites', '', 500),
      projects: this.api.list('projects', '', 500),
      users: this.api.list('users', '', 250)
    }).subscribe({
      next: ({ crews, sites, projects, users }) => {
        this.crews.set(crews.items || []);
        this.sites.set(sites.items || []);
        this.projects.set(projects.items || []);
        this.users.set(users.items || []);
        this.loadAssignments();
      },
      error: () => { this.loading.set(false); this.snack.open('Daten konnten nicht geladen werden.', 'OK', { duration: 3000 }); }
    });
  }

  private localIso(d: Date): string {
    const y = d.getFullYear(); const m = String(d.getMonth() + 1).padStart(2, '0'); const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }
  private tomorrowIso(): string { const d = new Date(); d.setDate(d.getDate() + 1); return this.localIso(d); }
  private todayIso(): string { return this.localIso(new Date()); }
  setDate(value: string): void { if (value) { this.date.set(value); this.loadAssignments(); } }
  goTomorrow(): void { this.date.set(this.tomorrowIso()); this.loadAssignments(); }
  goToday(): void { this.date.set(this.todayIso()); this.loadAssignments(); }
  shiftDay(delta: number): void { const d = new Date(this.date() + 'T12:00:00'); d.setDate(d.getDate() + delta); this.date.set(this.localIso(d)); this.loadAssignments(); }
  isToday(): boolean { return this.date() === this.todayIso(); }
  isTomorrow(): boolean { return this.date() === this.tomorrowIso(); }

  loadAssignments(): void {
    this.loading.set(true);
    this.api.planningAssignments(this.date(), this.date()).subscribe({
      next: (r) => { this.assignments.set(r.items || []); this.loading.set(false); },
      error: () => { this.assignments.set([]); this.loading.set(false); }
    });
  }

  assignmentsForCrew(crewId: unknown): GenericRecord[] {
    const id = String(crewId ?? '');
    return this.assignments().filter((a) => !this.isInspection(a))
      .filter((a) => String(a['crew_id'] ?? '') === id);
  }
  crewsWithWork(): number {
    const ids = new Set(this.assignments().map((a) => String(a['crew_id'] ?? '')));
    return this.crews().filter((c) => ids.has(String(c['id']))).length;
  }
  /** Zeitspanne eines Termins in Kurzform. */
  timeLabel(a: GenericRecord): string {
    const fmt = (v: unknown) => { const m = String(v ?? '').match(/(\d{2}):(\d{2})/); return m ? `${m[1]}:${m[2]}` : ''; };
    const from = fmt(a['start_at']); const to = fmt(a['end_at']);
    if (from && to) return `${from}–${to}`;
    return from || 'ganztägig';
  }
  mapsUrl(a: GenericRecord): string {
    const addr = this.siteAddress(a);
    return addr ? 'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(addr) : '';
  }
  siteAddress(a: GenericRecord): string {
    const site = this.sites().find((s) => String(s['id']) === String(a['site_id']));
    if (!site) return '';
    return [site['address'], site['city']].map((v) => String(v ?? '').trim()).filter((v) => v.length > 0).join(', ');
  }
  private static readonly INACTIVE_STATUS = ['completed', 'closed', 'done', 'abgeschlossen', 'cancelled', 'canceled', 'storniert', 'archived', 'archiviert', 'inactive'];
  private isInactiveStatus(v: unknown): boolean { return DailyDispatchComponent.INACTIVE_STATUS.includes(String(v ?? '').toLowerCase()); }
  private isActiveSite(s: GenericRecord): boolean {
    if (this.isInactiveStatus(s['status'])) return false;
    if (s['is_active'] !== undefined && Number(s['is_active']) === 0) return false;
    if (s['archived_at'] || s['completed_at']) return false;
    const proj = this.projects().find((p) => String(p['id']) === String(s['project_id']));
    if (proj && (this.isInactiveStatus(proj['status']) || proj['completed_at'] || proj['archived_at'])) return false;
    return true;
  }
  private activeSites(): GenericRecord[] {
    return this.sites().filter((s) => this.isActiveSite(s)).sort((a, b) => String(a['name'] ?? '').localeCompare(String(b['name'] ?? ''), 'de'));
  }

  assignmentTime(a: GenericRecord): string {
    const fmt = (v: unknown) => { const s = String(v ?? ''); const m = s.match(/(\d{2}):(\d{2})/); return m ? `${m[1]}:${m[2]}` : ''; };
    const from = fmt(a['start_at']); const to = fmt(a['end_at']);
    if (from && to) return `${from}–${to} Uhr`;
    if (from) return `ab ${from} Uhr`;
    return 'ganztägig';
  }

  /** true, wenn die Einplanung eine Begehung und keine Estricharbeit ist. */
  isInspection(a: GenericRecord): boolean {
    // Alle Termine ausser Estricharbeiten gehoeren in das Begehungsfeld.
    return /begehung|besichtigung|kontrolle|abnahme|aufma|cm-messung|kundentermin/i.test(String(a['title'] || ''));
  }
  /** Symbol passend zur Terminart – für die Liste der Begehungen. */
  visitIcon(a: GenericRecord): string {
    const t = String(a['title'] || '').toLowerCase();
    if (t.includes('kontrolle')) return 'visibility';
    if (t.includes('abnahme')) return 'task_alt';
    if (t.includes('aufma')) return 'straighten';
    if (t.includes('cm-messung') || t.includes('messung')) return 'water_drop';
    if (t.includes('kundentermin')) return 'handshake';
    if (t.includes('zweitbegehung')) return 'checklist';
    return 'fact_check';
  }
  // ---- Begehungen & Besichtigungen ---------------------------------------
  /** Termine ohne Kolonne bzw. mit Begehungs-Titel. */
  readonly visits = computed(() => this.assignments()
    .filter((a) => this.isInspection(a) || !a['crew_id'])
    .sort((a, b) => String(a['start_at'] || '').localeCompare(String(b['start_at'] || ''))));
  /** Name des zuständigen Mitarbeiters, falls hinterlegt. */
  visitOwner(a: GenericRecord): string {
    const id = String(a['assigned_user_id'] || '');
    if (!id) return '';
    const user = this.users().find((u) => String(u['id']) === id);
    return String(user?.['name'] || user?.['email'] || '');
  }
  /** Begehung planen – Baustelle, Art, Zeit und zuständige Person. */
  openVisit(): void {
    if (!this.canPlan) return;
    const ref = this.dialog.open(DispatchAssignDialogComponent, {
      panelClass: 'ew-dialog',
      width: 'min(560px, 96vw)',
      autoFocus: false,
      data: {
        crewName: 'Termin ohne Kolonne',
        dateLabel: this.dayLabel(),
        sites: this.activeSites(),
        defaultFrom: '09:00',
        defaultTo: '10:00',
        visitMode: true,
        users: this.users()
      } as AssignDialogData
    });
    ref.afterClosed().subscribe((result: { site: GenericRecord; from: string; to: string; kind?: string; title?: string; userId?: string } | undefined) => {
      if (!result || !result.site) return;
      const save = () => {
        const date = this.date();
        const payload: GenericRecord = {
          project_id: result.site['project_id'] || null,
          site_id: result.site['id'],
          crew_id: null,
          assigned_user_id: result.userId || null,
          title: result.title || 'Erstbegehung',
          status: 'planned',
          start_at: `${date} ${result.from}:00`,
          end_at: `${date} ${result.to}:00`,
          notes: `Art: ${result.title || 'Begehung'}`
        };
        this.api.create('planning_assignments', payload).subscribe({
          next: () => { this.snack.open(`${result.title || 'Termin'} eingeplant.`, 'OK', { duration: 2500 }); this.loadAssignments(); },
          error: () => this.snack.open('Einplanen fehlgeschlagen.', 'OK', { duration: 3000 })
        });
      };
      // Bei Begehungen gibt es keine Kolonne – geprüft wird nur die
      // Mehrfachplanung derselben Baustelle am selben Tag.
      const conflict = this.checkConflicts({ siteId: String(result.site['id']), from: result.from, to: result.to });
      if (conflict.warn) {
        this.dialog.open(ConfirmDialogComponent, {
          width: 'min(480px, 96vw)', panelClass: 'ew-dialog',
          data: { title: 'Baustelle bereits eingeplant', message: conflict.warn, confirmText: 'Trotzdem einplanen', cancelText: 'Abbrechen', icon: 'warning', tone: 'warn' }
        }).afterClosed().subscribe((ok) => { if (ok) save(); });
        return;
      }
      save();
    });
  }

  // ---- Konfliktprüfung ---------------------------------------------------
  /** Minuten seit Mitternacht aus einem Zeitstempel oder \"HH:MM\". */
  private toMinutes(value: unknown): number {
    const m = String(value ?? '').match(/(\d{2}):(\d{2})/);
    return m ? Number(m[1]) * 60 + Number(m[2]) : -1;
  }
  /** true, wenn sich zwei Zeitspannen echt überschneiden (Anschluss ist erlaubt). */
  private overlaps(aFrom: number, aTo: number, bFrom: number, bTo: number): boolean {
    if (aFrom < 0 || bFrom < 0) return false;
    return aFrom < bTo && bFrom < aTo;
  }
  /**
   * Prüft eine geplante Einteilung gegen den bestehenden Tag.
   * Liefert `block` (nicht erlaubt) oder `warn` (nur Hinweis).
   */
  private checkConflicts(opts: { crewId?: string; siteId: string; from: string; to: string; ignoreId?: string }): { block?: string; warn?: string } {
    const from = this.toMinutes(opts.from);
    const to = this.toMinutes(opts.to);
    const sameDay = this.assignments().filter((a) => String(a['id']) !== String(opts.ignoreId ?? ''));

    // a) Überschneidung innerhalb derselben Kolonne ist nicht erlaubt.
    if (opts.crewId) {
      const clash = sameDay.find((a) =>
        String(a['crew_id'] ?? '') === String(opts.crewId) &&
        this.overlaps(from, to, this.toMinutes(a['start_at']), this.toMinutes(a['end_at'])));
      if (clash) {
        return { block: `Zeitliche Überschneidung: Diese Kolonne ist von ${this.timeLabel(clash)} bereits auf „${clash['site_name'] || clash['title'] || 'einer Baustelle'}" eingeteilt.` };
      }
    }

    // b) Dieselbe Baustelle am selben Tag doppelt ist erlaubt, aber auffällig.
    const dup = sameDay.find((a) => String(a['site_id'] ?? '') === String(opts.siteId));
    if (dup) {
      const who = dup['crew_id'] ? this.crewName(dup['crew_id']) : 'ein Begehungstermin';
      return { warn: `Diese Baustelle ist heute bereits eingeplant (${who}, ${this.timeLabel(dup)}). Trotzdem zusätzlich einplanen?` };
    }
    return {};
  }
  /** Name einer Kolonne für Meldungstexte. */
  crewName(id: unknown): string {
    return String(this.crews().find((c) => String(c['id']) === String(id ?? ''))?.['name'] || 'eine Kolonne');
  }

  /** Bemerkung zu einer Einteilung erfassen – ohne den vollen Dialog. */
  openNote(a: GenericRecord): void {
    const ref = this.dialog.open(AssignmentNoteDialogComponent, {
      width: 'min(480px, 94vw)', panelClass: 'ew-dialog', autoFocus: false,
      data: {
        title: String(a['title'] || 'Einteilung'),
        subtitle: [a['site_name'] || a['project_name'], this.crewName(a['crew_id'])].filter(Boolean).join(' · '),
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

  openAssign(crew: GenericRecord): void {
    const ref = this.dialog.open(DispatchAssignDialogComponent, {
      panelClass: 'ew-dialog',
      width: 'min(560px, 96vw)',
      autoFocus: false,
      data: {
        crewName: String(crew['name'] || 'Kolonne'),
        dateLabel: this.dayLabel(),
        sites: this.activeSites(),
        defaultFrom: this.startTime(),
        defaultTo: this.endTime()
      } as AssignDialogData
    });
    ref.afterClosed().subscribe((result: { site: GenericRecord; from: string; to: string; kind?: string; title?: string } | undefined) => {
      if (!result || !result.site) return;
      this.startTime.set(result.from); this.endTime.set(result.to);

      const conflict = this.checkConflicts({ crewId: String(crew['id']), siteId: String(result.site['id']), from: result.from, to: result.to });
      if (conflict.block) {
        // Überschneidungen innerhalb einer Kolonne werden nicht angelegt.
        this.dialog.open(ConfirmDialogComponent, {
          width: 'min(460px, 96vw)', panelClass: 'ew-dialog',
          data: { title: 'Einteilung nicht möglich', message: conflict.block, confirmText: 'Verstanden', cancelText: '', icon: 'event_busy', tone: 'warn' }
        });
        return;
      }
      if (conflict.warn) {
        // Doppelte Baustelle ist erlaubt – aber nur nach Rückfrage.
        this.dialog.open(ConfirmDialogComponent, {
          width: 'min(480px, 96vw)', panelClass: 'ew-dialog',
          data: { title: 'Baustelle bereits eingeplant', message: conflict.warn, confirmText: 'Trotzdem einplanen', cancelText: 'Abbrechen', icon: 'warning', tone: 'warn' }
        }).afterClosed().subscribe((ok) => {
          if (ok) this.assign(crew, result.site, result.from, result.to, result.title || 'Estricharbeiten', result.kind || 'work');
        });
        return;
      }
      this.assign(crew, result.site, result.from, result.to, result.title || 'Estricharbeiten', result.kind || 'work');
    });
  }

  private assign(crew: GenericRecord, site: GenericRecord, from: string, to: string, title = 'Estricharbeiten', kind = 'work'): void {
    const date = this.date();
    const payload: GenericRecord = {
      project_id: site['project_id'] || null,
      site_id: site['id'],
      crew_id: crew['id'],
      title,
      status: 'planned',
      start_at: `${date} ${from}:00`,
      end_at: `${date} ${to}:00`,
      // Art im Notizfeld hinterlegen, damit die Einplanung auch ohne
      // Datenbankerweiterung eindeutig zuzuordnen ist.
      notes: kind === 'work' ? '' : `Art: ${title}`
    };
    this.api.create('planning_assignments', payload).subscribe({
      next: () => { this.snack.open(`„${site['name'] || 'Baustelle'}" für ${crew['name']} eingeplant.`, 'OK', { duration: 2500 }); this.loadAssignments(); },
      error: () => this.snack.open('Einplanen fehlgeschlagen.', 'OK', { duration: 3000 })
    });
  }

  removeAssignment(a: GenericRecord): void {
    this.api.remove('planning_assignments', String(a['id'])).subscribe({
      next: () => { this.assignments.update((list) => list.filter((x) => x['id'] !== a['id'])); this.snack.open('Einplanung entfernt.', 'OK', { duration: 2000 }); },
      error: () => this.snack.open('Entfernen fehlgeschlagen.', 'OK', { duration: 3000 })
    });
  }

  exportPdf(): void {
    const doc = new jsPDF({ unit: 'pt', format: 'a4' });
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();
    const M = 42;
    const contentW = pageW - 2 * M;
    // Spaltenraster (Zeit | Baustelle | Adresse)
    const cZeit = M;
    const cBaustelle = M + 84;
    const cAdresse = M + 84 + 226;
    const wZeit = 84, wBaustelle = 226, wAdresse = contentW - 84 - 226;
    let y = 0;

    const line = (x1: number, yy: number, x2: number, w = 0.6, g = 0) => {
      doc.setDrawColor(g, g, g); doc.setLineWidth(w); doc.line(x1, yy, x2, yy);
    };
    const rectBox = (x: number, yy: number, w: number, h: number, lw = 0.8, g = 0) => {
      doc.setDrawColor(g, g, g); doc.setLineWidth(lw); doc.rect(x, yy, w, h);
    };

    const drawMasthead = (): number => {
      // Rahmen um den Kopf – formell, wie ein Behördenformular
      const top = M;
      rectBox(M, top, contentW, 58, 1, 0);
      // Firmenname
      doc.setTextColor(0, 0, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(13);
      doc.text(this.companyName.toUpperCase(), M + 12, top + 22);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5); doc.setTextColor(60, 60, 60);
      doc.text('Estrichbau · Disposition', M + 12, top + 37);
      // Dokumenttitel rechts
      doc.setTextColor(0, 0, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(15);
      doc.text('DISPOSITIONSPLAN', M + contentW - 12, top + 24, { align: 'right' });
      doc.setFont('helvetica', 'normal'); doc.setFontSize(9);
      doc.text('Tageseinteilung', M + contentW - 12, top + 40, { align: 'right' });
      // Trennlinie in der Mitte des Kopfes (vertikal)
      doc.setLineWidth(0.4); doc.setDrawColor(150, 150, 150);
      return top + 58;
    };

    const drawMetaBar = (yy: number): number => {
      const h = 30;
      rectBox(M, yy, contentW, h, 0.8, 0);
      // Vier Felder – Termine ohne Kolonne gehören mit in die Kennzahlen.
      const quarter = contentW / 4;
      doc.setLineWidth(0.5); doc.setDrawColor(120, 120, 120);
      doc.line(M + quarter, yy, M + quarter, yy + h);
      doc.line(M + 2 * quarter, yy, M + 2 * quarter, yy + h);
      doc.line(M + 3 * quarter, yy, M + 3 * quarter, yy + h);
      const cap = (label: string, value: string, x: number) => {
        doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); doc.setTextColor(90, 90, 90);
        doc.text(label.toUpperCase(), x + 8, yy + 12);
        doc.setFont('helvetica', 'bold'); doc.setFontSize(10); doc.setTextColor(0, 0, 0);
        doc.text(value, x + 8, yy + 24);
      };
      const crewItems = this.assignments().filter((a) => !this.isInspection(a) && a['crew_id']).length;
      cap('Datum', this.dayLabel(), M);
      cap('Kolonnen im Einsatz', `${this.crewsWithWork()} von ${this.crews().length}`, M + quarter);
      cap('Baustellen', String(crewItems), M + 2 * quarter);
      cap('Termine', String(this.visits().length), M + 3 * quarter);
      return yy + h;
    };

    const drawTableHeader = (yy: number): number => {
      const h = 20;
      doc.setFillColor(30, 30, 30); doc.rect(M, yy, contentW, h, 'F');
      doc.setTextColor(255, 255, 255); doc.setFont('helvetica', 'bold'); doc.setFontSize(8.5);
      doc.text('ZEIT', cZeit + 8, yy + 13);
      doc.text('BAUSTELLE', cBaustelle + 6, yy + 13);
      doc.text('ADRESSE', cAdresse + 6, yy + 13);
      return yy + h;
    };

    const runningHeader = (): number => {
      // Auf Folgeseiten: kompakter Kopf
      let yy = M;
      doc.setTextColor(0, 0, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(10);
      doc.text(this.companyName.toUpperCase() + ' — DISPOSITIONSPLAN', M, yy + 6);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.setTextColor(80, 80, 80);
      doc.text(this.dayLabel(), M + contentW, yy + 6, { align: 'right' });
      line(M, yy + 12, M + contentW, 0.8, 0);
      return yy + 24;
    };

    const ensure = (needed: number, withTableHeader: boolean) => {
      if (y + needed > pageH - 70) {
        doc.addPage();
        y = runningHeader();
        if (withTableHeader) y = drawTableHeader(y);
      }
    };

    // Erste Seite
    y = drawMasthead();
    y += 10;
    y = drawMetaBar(y);
    y += 16;

    const crewsSorted = [...this.crews()].sort((a, b) => String(a['name'] ?? '').localeCompare(String(b['name'] ?? ''), 'de'));

    for (const crew of crewsSorted) {
      const items = this.assignmentsForCrew(crew['id']).slice().sort((a, b) => String(a['start_at'] ?? '').localeCompare(String(b['start_at'] ?? '')));
      ensure(52, false);
      // Kolonnen-Titel
      doc.setFillColor(225, 225, 225); doc.rect(M, y, contentW, 20, 'F');
      rectBox(M, y, contentW, 20, 0.6, 120);
      doc.setTextColor(0, 0, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(10.5);
      doc.text(String(crew['name'] || 'Kolonne').toUpperCase(), M + 8, y + 14);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.setTextColor(70, 70, 70);
      doc.text(`${items.length} Baustelle(n)`, M + contentW - 8, y + 14, { align: 'right' });
      y += 20;

      y = drawTableHeader(y);

      if (!items.length) {
        const h = 22;
        rectBox(M, y, contentW, h, 0.5, 170);
        doc.setTextColor(120, 120, 120); doc.setFont('helvetica', 'italic'); doc.setFontSize(9.5);
        doc.text('— keine Einteilung für diesen Tag —', M + contentW / 2, y + 14, { align: 'center' });
        doc.setFont('helvetica', 'normal');
        y += h + 12;
        continue;
      }

      let idx = 0;
      for (const a of items) {
        const name = String(a['site_name'] || a['project_name'] || 'Baustelle');
        const time = this.assignmentTime(a);
        const addr = this.siteAddress(a) || '—';
        const note = String(a['notes'] || '').trim();
        const nameLines = doc.splitTextToSize(name, wBaustelle - 12) as string[];
        const addrLines = doc.splitTextToSize(addr, wAdresse - 12) as string[];
        // Notizen laufen über die volle Breite unterhalb der Spalten.
        const noteLines = note ? (doc.splitTextToSize('Notiz: ' + note, contentW - wZeit - 20) as string[]) : [];
        const rows = Math.max(nameLines.length, addrLines.length, 1);
        const h = 16 + (rows - 1) * 11 + (noteLines.length ? noteLines.length * 10 + 3 : 0);
        ensure(h, true);
        // Zebra-Streifen in Graustufen für Lesbarkeit
        if (idx % 2 === 1) { doc.setFillColor(244, 244, 244); doc.rect(M, y, contentW, h, 'F'); }
        // Zellenrahmen
        rectBox(M, y, contentW, h, 0.4, 150);
        doc.setLineWidth(0.3); doc.setDrawColor(190, 190, 190);
        doc.line(cBaustelle, y, cBaustelle, y + h);
        doc.line(cAdresse, y, cAdresse, y + h);
        // Inhalte
        doc.setTextColor(0, 0, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(9);
        doc.text(time, cZeit + 8, y + 11);
        doc.setFont('helvetica', 'bold'); doc.setFontSize(9.5);
        doc.text(nameLines, cBaustelle + 6, y + 11);
        doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.setTextColor(60, 60, 60);
        doc.text(addrLines, cAdresse + 6, y + 11);
        if (noteLines.length) {
          const ny = y + 11 + (rows - 1) * 11 + 12;
          doc.setFont('helvetica', 'italic'); doc.setFontSize(8); doc.setTextColor(110, 90, 40);
          doc.text(noteLines, cBaustelle + 6, ny);
          doc.setFont('helvetica', 'normal'); doc.setTextColor(0, 0, 0);
        }
        y += h;
        idx++;
      }
      y += 16;
    }

    // ---- Termine ohne Kolonne (Begehungen, Kontrollen, Abnahmen) ----------
    const visitItems = this.visits();
    if (visitItems.length) {
      ensure(72, false);
      doc.setFillColor(225, 225, 225); doc.rect(M, y, contentW, 20, 'F');
      rectBox(M, y, contentW, 20, 0.6, 120);
      doc.setTextColor(0, 0, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(10.5);
      doc.text('TERMINE OHNE KOLONNE', M + 8, y + 14);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.setTextColor(70, 70, 70);
      doc.text(`${visitItems.length} Termin(e)`, M + contentW - 8, y + 14, { align: 'right' });
      y += 20;

      // Eigene Kopfzeile: statt Adresse steht hier die zuständige Person
      const visitHeader = (yy: number): number => {
        const h = 20;
        doc.setFillColor(30, 30, 30); doc.rect(M, yy, contentW, h, 'F');
        doc.setTextColor(255, 255, 255); doc.setFont('helvetica', 'bold'); doc.setFontSize(8.5);
        doc.text('ZEIT', cZeit + 8, yy + 13);
        doc.text('TERMIN / BAUSTELLE', cBaustelle + 6, yy + 13);
        doc.text('ZUSTÄNDIG', cAdresse + 6, yy + 13);
        return yy + h;
      };
      y = visitHeader(y);

      let vidx = 0;
      for (const v of visitItems) {
        const art = String(v['title'] || 'Termin');
        const place = String(v['site_name'] || v['project_name'] || 'Baustelle');
        const owner = this.visitOwner(v) || 'offen';
        const note = String(v['notes'] || '').replace(/^Art:.*$/m, '').trim();
        const nameLines = doc.splitTextToSize(`${art} — ${place}`, wBaustelle - 12) as string[];
        const addr = this.siteAddress(v);
        const ownerLines = doc.splitTextToSize(owner, wAdresse - 12) as string[];
        const noteLines = note ? (doc.splitTextToSize('Notiz: ' + note, contentW - wZeit - 20) as string[]) : [];
        const rows = Math.max(nameLines.length, ownerLines.length, 1);
        const h = 16 + (rows - 1) * 11 + (addr ? 10 : 0) + (noteLines.length ? noteLines.length * 10 + 3 : 0);
        ensure(h, false);
        if (vidx % 2 === 1) { doc.setFillColor(244, 244, 244); doc.rect(M, y, contentW, h, 'F'); }
        rectBox(M, y, contentW, h, 0.4, 150);
        doc.setLineWidth(0.3); doc.setDrawColor(190, 190, 190);
        doc.line(cBaustelle, y, cBaustelle, y + h);
        doc.line(cAdresse, y, cAdresse, y + h);
        doc.setTextColor(0, 0, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(9);
        doc.text(this.timeLabel(v), cZeit + 8, y + 11);
        doc.setFontSize(9.5);
        doc.text(nameLines, cBaustelle + 6, y + 11);
        doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.setTextColor(60, 60, 60);
        doc.text(ownerLines, cAdresse + 6, y + 11);
        let ny = y + 11 + (rows - 1) * 11;
        if (addr) {
          ny += 10;
          doc.setFontSize(8); doc.setTextColor(110, 110, 110);
          doc.text(doc.splitTextToSize(addr, wBaustelle - 12) as string[], cBaustelle + 6, ny);
        }
        if (noteLines.length) {
          ny += 13;
          doc.setFont('helvetica', 'italic'); doc.setFontSize(8); doc.setTextColor(110, 90, 40);
          doc.text(noteLines, cBaustelle + 6, ny);
          doc.setFont('helvetica', 'normal');
        }
        doc.setTextColor(0, 0, 0);
        y += h;
        vidx++;
      }
      y += 16;
    }

    // Unterschriftenblock
    ensure(70, false);
    y += 6;
    const sigW = (contentW - 24) / 2;
    doc.setDrawColor(0, 0, 0); doc.setLineWidth(0.5);
    doc.line(M, y + 30, M + sigW, y + 30);
    doc.line(M + sigW + 24, y + 30, M + contentW, y + 30);
    doc.setTextColor(90, 90, 90); doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5);
    doc.text('Datum, Unterschrift Bauleitung / Disposition', M, y + 42);
    doc.text('Kenntnisnahme Kolonnenführer', M + sigW + 24, y + 42);

    // Fußzeile auf allen Seiten
    const pages = doc.getNumberOfPages();
    for (let i = 1; i <= pages; i++) {
      doc.setPage(i);
      line(M, pageH - 42, M + contentW, 0.6, 0);
      doc.setTextColor(90, 90, 90); doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5);
      doc.text(`${this.companyName} · Dispositionsplan · erstellt am ${new Date().toLocaleString('de-DE')}`, M, pageH - 28);
      doc.text(`Seite ${i} von ${pages}`, M + contentW, pageH - 28, { align: 'right' });
    }

    doc.save(`Dispositionsplan_${this.date()}.pdf`);
  }
}
