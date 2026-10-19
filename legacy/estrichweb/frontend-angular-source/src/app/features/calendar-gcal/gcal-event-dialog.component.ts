import { Component, Inject, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { GenericRecord } from '../../core/api.service';
import { CalendarCategory } from '../calendar/calendar.component';

export interface GcalEventDialogData {
  event: GenericRecord | null;
  categories: CalendarCategory[];
  projects: GenericRecord[];
  sites: GenericRecord[];
  users: GenericRecord[];
  date: string;       // YYYY-MM-DD
  hour: number | null;
  canDelete: boolean;
}

export interface GcalEventResult {
  payload?: GenericRecord;
  deleted?: boolean;
  /** Wiederholung: erzeugt zusätzliche Termine (siehe Komponente). */
  repeat?: { freq: 'daily' | 'weekly' | 'biweekly' | 'monthly'; count: number };
}

/**
 * Termindialog im Stil von Google Kalender.
 *
 * Aufbau bewusst wie dort: großes Titelfeld ohne Beschriftung, darunter die
 * Zeitangaben, dann die Zusatzfelder mit führenden Symbolen. Wer den Google
 * Kalender kennt, findet sich sofort zurecht.
 */
@Component({
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule],
  template: `
    <div class="ge">
      <header class="ge-head">
        <span class="ge-head-title">{{ data.event ? 'Termin bearbeiten' : 'Neuer Termin' }}</span>
        <span class="ge-spacer"></span>
        @if (data.event && data.canDelete) {
          <button mat-icon-button (click)="remove()" matTooltip="Termin löschen" aria-label="Löschen"><mat-icon>delete</mat-icon></button>
        }
        <button mat-icon-button mat-dialog-close matTooltip="Schließen" aria-label="Schließen"><mat-icon>close</mat-icon></button>
      </header>

      <div class="ge-body">
        <!-- Titel: groß und ohne Label, wie bei Google -->
        <input class="ge-title" [(ngModel)]="title" placeholder="Titel hinzufügen" autocomplete="off" #titleField>

        <!-- Zeit -->
        <div class="ge-row">
          <mat-icon class="ge-ic">schedule</mat-icon>
          <div class="ge-times">
            <div class="ge-timeline-row">
              <input type="date" class="ge-inp" [(ngModel)]="startDate">
              @if (!allDay) {
                <input type="time" class="ge-inp ge-inp-time" [(ngModel)]="startTime" step="900">
                <span class="ge-dash">–</span>
                <input type="time" class="ge-inp ge-inp-time" [(ngModel)]="endTime" step="900">
              }
              <span class="ge-dur">{{ durationLabel() }}</span>
            </div>
            <label class="ge-check">
              <input type="checkbox" [(ngModel)]="allDay"><span>Ganztägig</span>
            </label>
          </div>
        </div>

        <!-- Wiederholung -->
        <div class="ge-row">
          <mat-icon class="ge-ic">repeat</mat-icon>
          <div class="ge-repeat">
            <button type="button" class="ge-pick" [matMenuTriggerFor]="repeatMenu">
              {{ repeatLabel() }}<mat-icon>arrow_drop_down</mat-icon>
            </button>
            <mat-menu #repeatMenu="matMenu">
              <button mat-menu-item (click)="repeat = 'none'">Wiederholt sich nicht</button>
              <button mat-menu-item (click)="repeat = 'daily'">Täglich</button>
              <button mat-menu-item (click)="repeat = 'weekly'">Wöchentlich</button>
              <button mat-menu-item (click)="repeat = 'biweekly'">Alle 2 Wochen</button>
              <button mat-menu-item (click)="repeat = 'monthly'">Monatlich</button>
            </mat-menu>
            @if (repeat !== 'none') {
              <span class="ge-repeat-count">
                <input type="number" class="ge-inp ge-inp-num" min="2" max="60" [(ngModel)]="repeatCount">
                <small>Termine insgesamt</small>
              </span>
            }
          </div>
        </div>

        <!-- Kalender / Kategorie mit Farbe -->
        <div class="ge-row">
          <mat-icon class="ge-ic">label</mat-icon>
          <div class="ge-linkcol">
            <!-- Auswahlliste statt Marken: Bei vielen eigenen Kalendern war die
                 Markenreihe mehrere Zeilen hoch und hat den Dialog gesprengt. -->
            <div class="ge-catwrap">
              <span class="ge-catdot" [style.background]="categoryColor()"></span>
              <select class="ge-inp ge-sel ge-catsel" [(ngModel)]="eventType">
                @for (c of data.categories; track c.code) {
                  <option [value]="c.code">{{ c.label }}</option>
                }
              </select>
            </div>
          </div>
        </div>

        <!-- Bauakte -->
        <div class="ge-row">
          <mat-icon class="ge-ic">folder_open</mat-icon>
          <div class="ge-linkcol">
            <!-- Suchbare Auswahl: Bei vielen Bauakten ist eine reine
                 Auswahlliste unbrauchbar. -->
            <div class="ge-combo">
              @if (projectId) {
                <div class="ge-chosen">
                  <mat-icon>check_circle</mat-icon>
                  <span>{{ projectLabel(projectId) }}</span>
                  <button type="button" (click)="clearProject()" aria-label="Verknüpfung entfernen"><mat-icon>close</mat-icon></button>
                </div>
              } @else {
                <input class="ge-inp ge-wide" [(ngModel)]="projectSearch" placeholder="Suche: Bauakte, Kunde, Ort oder Nummer" autocomplete="off">
                @if (projectSearch.trim()) {
                  <ul class="ge-list">
                    @for (p of filteredProjects(); track p['id']) {
                      <li><button type="button" (click)="pickProject(p)">
                        <b>{{ p['project_no'] || 'Bauakte' }}</b><span>{{ p['name'] }}</span>
                        @if (p['customer_name']) { <small class="ge-cust"><mat-icon>person</mat-icon>{{ p['customer_name'] }}</small> }
                        @if (p['city']) { <small>{{ p['city'] }}</small> }
                      </button></li>
                    } @empty { <li class="ge-none">Keine Bauakte gefunden.</li> }
                  </ul>
                }
              }
            </div>

            @if (projectId && siteOptions().length) {
              <div class="ge-combo">
                @if (siteId) {
                  <div class="ge-chosen">
                    <mat-icon>apartment</mat-icon>
                    <span>{{ siteLabel(siteId) }}@if (siteOptions().length === 1) { <em class="ge-auto">einzige Baustelle</em> }</span>
                    <button type="button" (click)="siteId = ''" aria-label="Baustelle entfernen"><mat-icon>close</mat-icon></button>
                  </div>
                } @else {
                  <input class="ge-inp ge-wide" [(ngModel)]="siteSearch" placeholder="Baustelle suchen (optional)" autocomplete="off">
                  <ul class="ge-list">
                    @for (s of filteredSites(); track s['id']) {
                      <li><button type="button" (click)="pickSite(s)">
                        <b>{{ s['site_no'] || 'Baustelle' }}</b><span>{{ s['name'] }}</span>
                        @if (s['city']) { <small>{{ s['city'] }}</small> }
                      </button></li>
                    } @empty { <li class="ge-none">Keine Baustelle gefunden.</li> }
                  </ul>
                }
              </div>
            }
          </div>
        </div>

        <!-- Zuständigkeit -->
        <div class="ge-row">
          <mat-icon class="ge-ic">person</mat-icon>
          <div class="ge-linkcol">
            <select class="ge-inp ge-sel" [(ngModel)]="assignedUserId">
              <option value="">Niemand zugewiesen</option>
              @for (u of data.users; track u['id']) {
                <option [value]="u['id']">{{ userLabel(u) }}</option>
              }
            </select>
            <span class="ge-hintline">Wer den Termin wahrnimmt. Erscheint auf dem Termin und im Detailfenster.</span>
          </div>
        </div>

        <!-- Ort -->
        <div class="ge-row">
          <mat-icon class="ge-ic">place</mat-icon>
          <input class="ge-inp ge-wide" [(ngModel)]="location" placeholder="Ort hinzufügen" autocomplete="off">
        </div>

        <!-- Beschreibung -->
        <div class="ge-row">
          <mat-icon class="ge-ic">notes</mat-icon>
          <textarea class="ge-inp ge-wide ge-area" rows="3" [(ngModel)]="description" placeholder="Beschreibung hinzufügen"></textarea>
        </div>

        @if (error()) { <p class="ge-error"><mat-icon>error_outline</mat-icon>{{ error() }}</p> }
      </div>

      <footer class="ge-foot">
        <button mat-button mat-dialog-close>Abbrechen</button>
        <button mat-flat-button color="primary" (click)="save()">Speichern</button>
      </footer>
    </div>
  `,
  styles: [`
    .ge { display: flex; flex-direction: column; max-height: 86vh; }
    .ge-head { display: flex; align-items: center; gap: 4px; padding: 8px 10px 8px 20px; border-bottom: 1px solid #e8eaed; }
    .ge-head-title { font-size: .88rem; color: #5f6368; }
    .ge-spacer { flex: 1 1 auto; }

    .ge-body { padding: 14px 20px 18px; overflow-y: auto; }
    .ge-title {
      display: block; width: 100%; box-sizing: border-box;
      border: 0; border-bottom: 2px solid #e8eaed; outline: none;
      background: transparent; font: inherit; font-size: 1.4rem; color: #3c4043;
      padding: 6px 0 8px; margin-bottom: 18px;
    }
    .ge-title:focus { border-bottom-color: #1a73e8; }
    .ge-title::placeholder { color: #9aa0a6; }

    .ge-row { display: grid; grid-template-columns: 28px minmax(0, 1fr); gap: 14px; align-items: start; margin-bottom: 14px; }
    .ge-ic { color: #5f6368 !important; margin-top: 7px; }

    .ge-inp {
      border: 1px solid #dadce0; border-radius: 4px; background: #fff;
      font: inherit; font-size: .88rem; color: #3c4043;
      height: 36px; padding: 0 10px; box-sizing: border-box;
    }
    .ge-inp:focus { outline: none; border-color: #1a73e8; box-shadow: 0 0 0 1px #1a73e8; }
    .ge-inp-time { width: 104px; }
    .ge-inp-num { width: 74px; }
    .ge-wide { width: 100%; }
    .ge-area { height: auto; padding: 8px 10px; resize: vertical; font-family: inherit; }
    .ge-sel { width: 100%; cursor: pointer; }

    .ge-times { display: flex; flex-direction: column; gap: 8px; }
    .ge-timeline-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .ge-dash { color: #5f6368; }
    .ge-dur { color: #70757a; font-size: .8rem; }
    .ge-check { display: inline-flex; align-items: center; gap: 8px; font-size: .86rem; color: #3c4043; cursor: pointer; }

    .ge-repeat { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .ge-pick {
      display: inline-flex; align-items: center; gap: 2px;
      height: 36px; padding: 0 6px 0 12px;
      border: 1px solid #dadce0; border-radius: 4px; background: #fff;
      font: inherit; font-size: .88rem; color: #3c4043; cursor: pointer;
    }
    .ge-pick:hover { background: #f1f3f4; }
    .ge-repeat-count { display: inline-flex; align-items: center; gap: 8px; }
    .ge-repeat-count small { color: #70757a; font-size: .8rem; }

    /* Kategorie als Auswahlliste mit Farbpunkt davor. */
    .ge-catwrap { position: relative; display: flex; align-items: center; }
    .ge-catdot {
      position: absolute; left: 12px; z-index: 1;
      width: 11px; height: 11px; min-width: 11px; aspect-ratio: 1 / 1;
      border-radius: 50%; pointer-events: none;
    }
    .ge-catsel { padding-left: 32px !important; width: 100%; }
    .ge-sel {
      appearance: none; -webkit-appearance: none;
      background-image: linear-gradient(45deg, transparent 50%, #70757a 50%), linear-gradient(135deg, #70757a 50%, transparent 50%);
      background-position: calc(100% - 17px) 50%, calc(100% - 12px) 50%;
      background-size: 5px 5px, 5px 5px;
      background-repeat: no-repeat;
      padding-right: 32px !important;
    }
    .ge-cust { display: inline-flex; align-items: center; gap: 3px; }
    .ge-cust .mat-icon { font-size: 12px !important; width: 12px !important; height: 12px !important; }
    .ge-cats { display: flex; flex-wrap: wrap; gap: 6px; }
    .ge-cat {
      display: inline-flex; align-items: center; gap: 7px;
      border: 1px solid #dadce0; border-radius: 18px;
      background: #fff; padding: 6px 13px; cursor: pointer;
      font: inherit; font-size: .82rem; color: #3c4043;
    }
    .ge-cat i { width: 11px; height: 11px; border-radius: 50%; flex: 0 0 auto; }
    .ge-cat:hover { background: #f1f3f4; }
    .ge-cat.on { border-color: var(--c); box-shadow: 0 0 0 1px var(--c); background: #f8fbff; font-weight: 500; }

    .ge-linkcol { display: flex; flex-direction: column; gap: 10px; }
    .ge-hintline { font-size: .76rem; color: #70757a; line-height: 1.4; }
    .ge-auto {
      margin-left: 7px; font-style: normal; font-size: .7rem;
      color: #1a73e8; background: #e8f0fe; border-radius: 999px; padding: 1px 8px;
    }
    .ge-combo { position: relative; }
    .ge-chosen {
      display: flex; align-items: center; gap: 8px;
      border: 1px solid #dadce0; border-radius: 4px;
      padding: 7px 8px 7px 11px; background: #f8fbff;
    }
    .ge-chosen > span { flex: 1 1 auto; min-width: 0; font-size: .86rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ge-chosen > .mat-icon { color: #1a73e8 !important; font-size: 18px !important; width: 18px !important; height: 18px !important; flex: none; }
    .ge-chosen button {
      border: 0; background: transparent; cursor: pointer; flex: none;
      width: 26px; height: 26px; min-width: 26px; aspect-ratio: 1 / 1; border-radius: 50%;
      display: inline-grid; place-items: center; color: #5f6368;
    }
    .ge-chosen button:hover { background: #e8eaed; }
    .ge-chosen button .mat-icon { font-size: 16px !important; width: 16px !important; height: 16px !important; color: #5f6368 !important; }
    .ge-list {
      list-style: none; margin: 4px 0 0; padding: 4px;
      border: 1px solid #dadce0; border-radius: 6px; background: #fff;
      max-height: 210px; overflow-y: auto;
      box-shadow: 0 2px 8px rgba(60,64,67,.14);
    }
    .ge-list button {
      display: flex; align-items: baseline; gap: 8px; width: 100%;
      border: 0; background: transparent; cursor: pointer;
      padding: 8px 10px; border-radius: 4px; font: inherit; text-align: left;
    }
    .ge-list button:hover { background: #f1f3f4; }
    .ge-list b { font-size: .8rem; color: #1a73e8; flex: none; }
    .ge-list span { font-size: .84rem; color: #3c4043; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ge-list small { margin-left: auto; font-size: .76rem; color: #80868b; flex: none; }
    .ge-none { padding: 10px; color: #80868b; font-size: .82rem; }

    .ge-error {
      display: flex; align-items: center; gap: 7px; margin: 4px 0 0;
      color: #c5221f; font-size: .84rem;
    }
    .ge-error .mat-icon { color: #c5221f !important; font-size: 17px !important; width: 17px !important; height: 17px !important; }

    .ge-foot { display: flex; justify-content: flex-end; gap: 8px; padding: 10px 16px 14px; border-top: 1px solid #e8eaed; }

    /* ---------------------------------------------------------------
       Handy: Der Dialog füllt den Bildschirm. Kopf- und Fußzeile bleiben
       stehen, nur der Inhalt scrollt – so ist "Speichern" immer erreichbar,
       auch wenn die Tastatur offen ist.
       --------------------------------------------------------------- */
    @media (max-width: 600px) {
      .ge { max-height: none; height: 100%; }
      .ge-head {
        position: sticky; top: 0; z-index: 3;
        background: #fff; padding: 10px 8px 10px 16px;
      }
      .ge-head-title { font-size: .92rem; font-weight: 600; color: #202124; }
      .ge-body { padding: 14px 16px 20px; }

      /* Titel groß und prominent – das Wichtigste zuerst. */
      .ge-title { font-size: 1.24rem; padding: 8px 0 10px; margin-bottom: 20px; }

      /* Zeile: Symbol schmaler, Felder breiter */
      .ge-row { grid-template-columns: 22px minmax(0, 1fr); gap: 12px; margin-bottom: 18px; }
      .ge-ic { margin-top: 10px; }

      /* 16px Schriftgröße verhindert das automatische Zoomen auf iOS. */
      .ge-inp { height: 46px; font-size: 16px; border-radius: 8px; }
      .ge-area { height: auto; }

      /* Datum über die volle Breite, Von/Bis darunter nebeneinander */
      .ge-times { gap: 10px; }
      .ge-timeline-row { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
      .ge-timeline-row input[type="date"] { grid-column: 1 / -1; width: 100%; }
      .ge-inp-time { width: 100%; }
      .ge-dash { display: none; }
      .ge-dur { grid-column: 1 / -1; font-size: .82rem; }
      .ge-check { min-height: 44px; }

      /* Auswahlliste statt Marken – spart am Handy die meiste Höhe. */
      .ge-catsel { height: 48px; font-size: 16px; }
      .ge-catdot { left: 13px; width: 12px; height: 12px; min-width: 12px; }

      .ge-pick { height: 46px; width: 100%; justify-content: space-between; }
      .ge-repeat { flex-direction: column; align-items: stretch; gap: 10px; }
      .ge-repeat-count { justify-content: space-between; }
      .ge-inp-num { width: 96px; }

      .ge-list { max-height: 46vh; }
      .ge-list button { padding: 12px 10px; }
      .ge-chosen { padding: 9px 8px 9px 12px; min-height: 46px; }

      /* Fußzeile bleibt unten stehen */
      .ge-foot {
        position: sticky; bottom: 0; z-index: 3;
        background: #fff;
        padding: 10px 16px calc(12px + env(safe-area-inset-bottom));
        box-shadow: 0 -1px 0 #e8eaed;
      }
      .ge-foot button { flex: 1 1 auto; min-height: 48px; font-size: .94rem; }
    }
  `]
})
export class GcalEventDialogComponent {
  private readonly ref = inject(MatDialogRef<GcalEventDialogComponent>);

  title = '';
  startDate = '';
  startTime = '09:00';
  endTime = '10:00';
  allDay = false;
  eventType = '';
  projectId = '';
  siteId = '';
  location = '';
  assignedUserId = '';
  description = '';
  repeat: 'none' | 'daily' | 'weekly' | 'biweekly' | 'monthly' = 'none';
  repeatCount = 4;
  readonly error = signal('');

  constructor(@Inject(MAT_DIALOG_DATA) public data: GcalEventDialogData) {
    const e = data.event;
    if (e) {
      this.title = String(e['title'] || '');
      this.startDate = String(e['start_at'] || '').slice(0, 10);
      this.startTime = this.hhmm(e['start_at']) || '09:00';
      this.endTime = this.hhmm(e['end_at']) || '10:00';
      this.allDay = !!e['all_day'];
      this.eventType = String(e['event_type'] || data.categories[0]?.code || '');
      this.projectId = String(e['project_id'] || '');
      this.siteId = String(e['site_id'] || '');
      this.location = String(e['location'] || '');
      this.assignedUserId = String(e['assigned_user_id'] || '');
      this.description = String(e['description'] || e['notes'] || '');
    } else {
      this.startDate = data.date;
      const h = data.hour ?? 9;
      this.startTime = `${String(h).padStart(2, '0')}:00`;
      this.endTime = `${String(Math.min(23, h + 1)).padStart(2, '0')}:00`;
      this.eventType = data.categories[0]?.code || '';
    }
    // Auch beim Öffnen: Gibt es zur Bauakte nur eine Baustelle, gleich setzen.
    if (this.projectId && !this.siteId) this.autoPickSingleSite();
  }

  private hhmm(value: unknown): string {
    const m = String(value ?? '').match(/(\d{2}):(\d{2})/);
    return m ? `${m[1]}:${m[2]}` : '';
  }
  private minutes(hhmm: string): number {
    const m = hhmm.match(/(\d{2}):(\d{2})/);
    return m ? Number(m[1]) * 60 + Number(m[2]) : 0;
  }
  durationLabel(): string {
    if (this.allDay) return 'ganztägig';
    const d = this.minutes(this.endTime) - this.minutes(this.startTime);
    if (d <= 0) return '';
    const h = Math.floor(d / 60); const min = d % 60;
    return h ? (min ? `${h} Std. ${min} Min.` : `${h} Std.`) : `${min} Min.`;
  }
  repeatLabel(): string {
    return ({ none: 'Wiederholt sich nicht', daily: 'Täglich', weekly: 'Wöchentlich', biweekly: 'Alle 2 Wochen', monthly: 'Monatlich' } as Record<string, string>)[this.repeat];
  }
  projectSearch = '';
  siteSearch = '';

  readonly siteOptions = computed(() => this.data.sites.filter((s) => String(s['project_id']) === String(this.projectId)));

  private matches(record: Record<string, unknown>, keys: string[], needle: string): boolean {
    const q = needle.trim().toLowerCase();
    if (!q) return true;
    return keys.map((k) => String(record[k] ?? '').toLowerCase()).some((v) => v.includes(q));
  }
  /** Höchstens 8 Treffer – die Liste soll den Dialog nicht sprengen. */
  /**
   * Bauakten durchsuchen – jetzt auch über den Kundennamen.
   *
   * In der Praxis weiß man oft den Kunden, nicht die Aktennummer
   * ("Meier" statt "2026-010"). Der Kundenname stand zwar am Datensatz,
   * wurde aber nicht durchsucht.
   */
  filteredProjects() {
    return this.data.projects
      .filter((p) => this.matches(p, ['project_no', 'name', 'city', 'zip', 'customer_name', 'customer', 'address'], this.projectSearch))
      .slice(0, 10);
  }
  /** Farbe der gewählten Kategorie – für den Punkt vor der Auswahlliste. */
  categoryColor(): string {
    return this.data.categories.find((c) => c.code === this.eventType)?.color || '#1a73e8';
  }
  filteredSites() {
    return this.siteOptions()
      .filter((s) => this.matches(s, ['site_no', 'name', 'city', 'address'], this.siteSearch))
      .slice(0, 8);
  }
  projectLabel(id: string): string {
    const p = this.data.projects.find((x) => String(x['id']) === String(id));
    return p ? [p['project_no'], p['name']].filter(Boolean).join(' · ') : 'Bauakte';
  }
  /** Anzeigename eines Mitarbeiters. */
  userLabel(u: Record<string, unknown>): string {
    const name = [u['first_name'], u['last_name']].map((v) => String(v ?? '').trim()).filter(Boolean).join(' ');
    return name || String(u['name'] || u['email'] || 'Mitarbeiter');
  }
  siteLabel(id: string): string {
    const s = this.data.sites.find((x) => String(x['id']) === String(id));
    return s ? [s['site_no'], s['name']].filter(Boolean).join(' · ') : 'Baustelle';
  }
  pickProject(p: Record<string, unknown>): void {
    this.projectId = String(p['id']);
    this.projectSearch = '';
    this.siteId = '';
    this.autoPickSingleSite();
  }
  /**
   * Hat die gewählte Bauakte genau EINE Baustelle, wird sie sofort
   * übernommen – ein zusätzlicher Klick ohne Auswahlmöglichkeit ist
   * unnötig. Ist ein Ort hinterlegt und das Ortsfeld noch leer, wird auch
   * dieser gleich eingetragen.
   */
  private autoPickSingleSite(): void {
    const options = this.siteOptions();
    if (options.length !== 1) return;
    const only = options[0];
    this.siteId = String(only['id']);
    if (!this.location.trim()) this.location = this.addressOf(only);
  }
  /** Adresse einer Baustelle als Ortsangabe. */
  private addressOf(site: Record<string, unknown>): string {
    return [site['address'], site['zip'], site['city']]
      .map((v) => String(v ?? '').trim())
      .filter((v) => v.length > 0)
      .join(' ');
  }
  /** Wird eine Baustelle gewählt, den Ort übernehmen, falls noch leer. */
  pickSite(site: Record<string, unknown>): void {
    this.siteId = String(site['id']);
    if (!this.location.trim()) this.location = this.addressOf(site);
  }
  clearProject(): void { this.projectId = ''; this.siteId = ''; this.projectSearch = ''; }
  onProjectChange(): void { this.siteId = ''; }

  remove(): void { this.ref.close({ deleted: true } as GcalEventResult); }

  save(): void {
    if (!this.title.trim()) { this.error.set('Bitte einen Titel eingeben.'); return; }
    if (!this.startDate) { this.error.set('Bitte ein Datum wählen.'); return; }
    if (!this.allDay && this.minutes(this.endTime) <= this.minutes(this.startTime)) {
      this.error.set('Das Ende muss nach dem Beginn liegen.'); return;
    }
    const payload: GenericRecord = {
      title: this.title.trim(),
      start_at: this.allDay ? `${this.startDate} 00:00:00` : `${this.startDate} ${this.startTime}:00`,
      end_at: this.allDay ? `${this.startDate} 23:59:00` : `${this.startDate} ${this.endTime}:00`,
      all_day: this.allDay ? 1 : 0,
      event_type: this.eventType,
      project_id: this.projectId || null,
      site_id: this.siteId || null,
      location: this.location.trim(),
      assigned_user_id: this.assignedUserId || null,
      description: this.description.trim()
    };
    const result: GcalEventResult = { payload };
    if (!this.data.event && this.repeat !== 'none') {
      result.repeat = { freq: this.repeat, count: Math.max(2, Math.min(60, Number(this.repeatCount) || 2)) };
    }
    this.ref.close(result);
  }
}
