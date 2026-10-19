import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { jsPDF } from 'jspdf';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

interface PlanDay { iso: string; date: Date; label: string; short: string; dayNum: string; month: string; isToday: boolean; isWeekend: boolean; }

/**
 * Arbeitsplan (Dienstplan) – reine Ansichtsseite, ohne Planungsrechte
 * erreichbar. Jeder angemeldete Mitarbeiter sieht, wer wann wo eingeteilt ist.
 *
 * WICHTIG zur Umsetzung: Diese Seite verwendet bewusst KEINE
 * `mat-form-field`-Bausteine. In styles.css gilt global
 * `.mat-mdc-form-field { width: 100% }` – dadurch nimmt jedes Filterfeld die
 * volle Breite ein und die Werkzeugleiste bricht auseinander. Mit eigenen
 * Bedienelementen bleibt die Anordnung unter Kontrolle.
 */
@Component({
  selector: 'ew-work-plan',
  standalone: true,
  imports: [DatePipe, NgClass, FormsModule, RouterLink, MatIconModule, MatTooltipModule, MatSnackBarModule],
  template: `
    <section class="wp">

      <!-- ===== Kopfbereich ===== -->
      <header class="wp-hero wp-hero-plain">
        <div class="wp-hero-text">
          <p class="wp-eyebrow">Planung</p>
          <h1>Arbeitsplan</h1>
          <p class="wp-hero-sub">KW {{ isoWeek() }} · {{ rangeLabel() }}</p>
        </div>
        <div class="wp-hero-actions">
          <button type="button" class="wp-btn" (click)="load()"><mat-icon>refresh</mat-icon><span>Aktualisieren</span></button>
          <button type="button" class="wp-btn" (click)="exportPdf()"><mat-icon>picture_as_pdf</mat-icon><span>PDF</span></button>
          <button type="button" class="wp-btn" (click)="print()"><mat-icon>print</mat-icon><span>Drucken</span></button>
        </div>
      </header>

      <!-- ===== Wochennavigation (klebt am oberen Rand) ===== -->
      <nav class="wp-weekbar">
        <div class="wp-weekbar-nav">
          <button type="button" class="wp-step" (click)="shiftWeek(-1)" aria-label="Vorwoche"><mat-icon>chevron_left</mat-icon></button>
          <button type="button" class="wp-step" (click)="shiftWeek(1)" aria-label="Nächste Woche"><mat-icon>chevron_right</mat-icon></button>
          <button type="button" class="wp-btn today" [class.is-current]="isCurrentWeek()" (click)="thisWeek()">Heute</button>
        </div>
        <div class="wp-weekbar-mid">
          <b>KW {{ isoWeek() }}</b>
          <span>{{ rangeLabel() }}</span>
        </div>
        <div class="wp-weekbar-sum">
          <span><b>{{ totalCount() }}</b>Einsätze</span>
          <span><b>{{ busyCrewCount() }}/{{ crews().length }}</b>Kolonnen</span>
        </div>
      </nav>

      <!-- ===== Filterleiste: eine Zeile, eigene Bedienelemente ===== -->
      <div class="wp-filters">
        <label class="wp-field wp-field-search">
          <mat-icon>search</mat-icon>
          <input type="search" [(ngModel)]="search" placeholder="Kolonne, Baustelle oder Ort suchen">
          @if (search) { <button type="button" class="wp-clear" (click)="search = ''" aria-label="Suche leeren"><mat-icon>close</mat-icon></button> }
        </label>
        <label class="wp-field wp-field-select">
          <select [(ngModel)]="scope">
            <option value="all">Alle Kolonnen</option>
            <option value="mine">Nur meine Einsätze</option>
            <option value="busy">Nur belegte Kolonnen</option>
          </select>
          <mat-icon>expand_more</mat-icon>
        </label>
        <label class="wp-field wp-field-select">
          <select [(ngModel)]="workWeekOnly">
            <option [ngValue]="true">Mo – Fr</option>
            <option [ngValue]="false">Mo – So</option>
          </select>
          <mat-icon>expand_more</mat-icon>
        </label>
        <div class="wp-switch" role="group" aria-label="Darstellung">
          <button type="button" [class.on]="view === 'timeline'" (click)="view = 'timeline'"><mat-icon>view_timeline</mat-icon><span>Tag</span></button>
          <button type="button" [class.on]="view === 'agenda'" (click)="view = 'agenda'"><mat-icon>view_agenda</mat-icon><span>Agenda</span></button>
        </div>
      </div>

      @if (loading()) { <div class="wp-loading"><span></span></div> }

      <!-- ===== Tagesansicht mit Stundenachse (wie eine Plantafel) ===== -->
      @if (view === 'timeline') {
        <div class="wp-tl">
          <div class="wp-tl-daybar">
            @for (day of days(); track day.iso) {
              <button type="button" class="wp-tl-day" [class.on]="timelineDay() === day.iso" [class.is-today]="day.isToday" (click)="timelineDay.set(day.iso)">
                <small>{{ day.short }}</small><b>{{ day.dayNum }}</b>
                @if (dayEntries(day).length) { <em>{{ dayEntries(day).length }}</em> }
              </button>
            }
          </div>

          <div class="wp-tl-wrap">
            <div class="wp-tl-inner">
              <!-- Kopfzeile mit Stunden -->
              <div class="wp-tl-head">
                <div class="wp-tl-corner">Kolonne</div>
                <div class="wp-tl-hours">
                  @for (h of hours(); track h) { <span class="wp-tl-hour">{{ h }}:00</span> }
                </div>
              </div>

              @for (crew of visibleCrews(); track crew['id']) {
                <div class="wp-tl-row">
                  <div class="wp-tl-name">
                    <span class="wp-tl-dot" [style.background]="crewColor(crew)"></span>
                    <span class="wp-tl-nametx">
                      <b>{{ crew['name'] }}</b>
                      <small>{{ timelineLoadLabel(crew) }}</small>
                    </span>
                  </div>
                  <div class="wp-tl-lane">
                    @for (h of hours(); track h) { <i class="wp-tl-line"></i> }
                    @if (isTodayTimeline()) { <i class="wp-tl-now" [style.left.%]="nowPercent()"></i> }
                    @for (a of timelineEntries(crew); track a['id']) {
                      <button type="button" class="wp-tl-bar" [ngClass]="'k-' + kindOf(a)"
                              [style.left.%]="barLeft(a)" [style.width.%]="barWidth(a)"
                              (click)="openDetail(a)" [matTooltip]="tooltipFor(a)">
                        <b>{{ a['title'] || 'Einsatz' }}</b>
                        <small>{{ a['site_name'] || a['project_name'] || '' }}</small>
                      </button>
                    }
                    @if (!timelineEntries(crew).length) { <span class="wp-tl-free">frei</span> }
                  </div>
                </div>
              }
              @if (!visibleCrews().length) { <p class="wp-tl-empty">Keine Kolonnen für diese Auswahl.</p> }
            </div>
          </div>
        </div>
      }

      <!-- ===== Agenda: Karten je Tag (Desktop wählbar, Handy immer) ===== -->
      <div class="wp-agenda" [class.chosen]="view === 'agenda'">
        @for (day of visibleDays(); track day.iso) {
          <section class="wp-day" [class.is-today]="day.isToday" [class.is-empty]="!dayEntries(day).length">
            <header class="wp-day-head">
              <span class="wp-day-date"><b>{{ day.dayNum }}</b><small>{{ day.month }}</small></span>
              <span class="wp-day-tx">
                <b>{{ day.label }}</b>
                <small>{{ dayEntries(day).length }} Einsatz(e) · {{ dayCrewCount(day) }} Kolonne(n)</small>
              </span>
              @if (day.isToday) { <span class="wp-tag">heute</span> }
            </header>

            @if (dayEntries(day).length) {
              <div class="wp-cards">
                @for (a of dayEntries(day); track a['id']) {
                  <article class="wp-card" [ngClass]="'k-' + kindOf(a)" role="button" tabindex="0"
                           (click)="openDetail(a)" (keydown.enter)="openDetail(a)">
                    <span class="wp-card-rail"></span>
                    <div class="wp-card-in">
                      <div class="wp-card-row1">
                        <span class="wp-chip-time"><mat-icon>schedule</mat-icon>{{ timeLabel(a) }}</span>
                        <span class="wp-chip-kind">{{ kindLabel(a) }}</span>
                      </div>
                      <b class="wp-card-h">{{ a['title'] || 'Einsatz' }}</b>
                      <span class="wp-card-l"><mat-icon>apartment</mat-icon>{{ a['site_name'] || a['project_name'] || 'Baustelle' }}</span>
                      @if (addressOf(a)) { <span class="wp-card-l dim"><mat-icon>place</mat-icon>{{ addressOf(a) }}</span> }
                      <span class="wp-card-l dim"><mat-icon>groups</mat-icon>{{ crewName(a['crew_id']) }}</span>
                    </div>
                    <div class="wp-card-side" (click)="$event.stopPropagation()">
                      @if (mapsUrl(a)) { <a class="wp-ic-btn" [href]="mapsUrl(a)" target="_blank" rel="noopener" matTooltip="Navigation" aria-label="Navigation"><mat-icon>directions</mat-icon></a> }
                      @if (a['project_id']) { <a class="wp-ic-btn" [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: a['project_id'] }" matTooltip="Bauakte" aria-label="Bauakte"><mat-icon>folder_open</mat-icon></a> }
                    </div>
                  </article>
                }
              </div>
            } @else {
              <p class="wp-day-free"><mat-icon>event_available</mat-icon>Keine Einsätze geplant.</p>
            }
          </section>
        }
      </div>

      <!-- ===== Detailansicht ===== -->
      @if (detail(); as d) {
        <div class="wp-modal" (click)="closeDetail()">
          <aside class="wp-sheet" (click)="$event.stopPropagation()" role="dialog" aria-modal="true">
            <span class="wp-sheet-grab"></span>
            <header class="wp-sheet-head" [ngClass]="'k-' + kindOf(d)">
              <div>
                <small>{{ kindLabel(d) }}</small>
                <b>{{ d['title'] || 'Einsatz' }}</b>
              </div>
              <button type="button" class="wp-sheet-x" (click)="closeDetail()" aria-label="Schließen"><mat-icon>close</mat-icon></button>
            </header>
            <dl class="wp-sheet-list">
              <div><dt><mat-icon>event</mat-icon>Termin</dt><dd>{{ dateLabel(d) }}<br>{{ timeLabel(d) }}</dd></div>
              <div><dt><mat-icon>groups</mat-icon>Kolonne</dt><dd>{{ crewName(d['crew_id']) }}</dd></div>
              <div><dt><mat-icon>apartment</mat-icon>Baustelle</dt><dd>{{ d['site_name'] || d['project_name'] || '—' }}</dd></div>
              <div><dt><mat-icon>place</mat-icon>Adresse</dt><dd>{{ addressOf(d) || '—' }}</dd></div>
              @if (d['notes']) { <div><dt><mat-icon>notes</mat-icon>Notiz</dt><dd>{{ d['notes'] }}</dd></div> }
            </dl>
            <div class="wp-sheet-actions">
              @if (mapsUrl(d)) { <a class="wp-btn primary wide" [href]="mapsUrl(d)" target="_blank" rel="noopener"><mat-icon>directions</mat-icon><span>Navigation starten</span></a> }
              @if (d['project_id']) { <a class="wp-btn wide" [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: d['project_id'] }" (click)="closeDetail()"><mat-icon>folder_open</mat-icon><span>Bauakte öffnen</span></a> }
            </div>
          </aside>
        </div>
      }

      <!-- Nur für den Ausdruck -->
      <div class="wp-printhead"><b>Arbeitsplan KW {{ isoWeek() }}</b><span>{{ rangeLabel() }}</span></div>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .wp { max-width: 1500px; margin: 0 auto; padding: 4px 4px 96px; color: #0f172a; }

    /* ---------- Kopf ---------- */
    /* Kopfbereich im Systemdesign: helle Karte wie auf den übrigen Seiten. */
    .wp-hero {
      display: flex; align-items: center; justify-content: space-between; gap: 16px; flex-wrap: wrap;
      background: #fff; border: 1px solid #e2e8f0; border-radius: 14px;
      padding: 16px 18px; margin-bottom: 10px;
    }
    .wp-eyebrow { margin: 0; font-size: .7rem; font-weight: 800; letter-spacing: .12em; text-transform: uppercase; color: #64748b; }
    .wp-hero h1 { margin: 2px 0 0; font-size: 1.5rem; line-height: 1.15; color: #0f172a; }
    .wp-hero-sub { margin: 3px 0 0; color: #64748b; font-size: .86rem; }
    .wp-hero-actions { display: flex; gap: 8px; flex-wrap: wrap; }

    /* ---------- Schaltflächen ---------- */
    .wp-btn {
      display: inline-flex; align-items: center; justify-content: center; gap: 6px;
      min-height: 42px; padding: 0 14px; border-radius: 10px; cursor: pointer;
      border: 1px solid #cbd5e1; background: #fff; color: #334155;
      font: inherit; font-size: .86rem; font-weight: 700; white-space: nowrap;
    }
    .wp-btn:hover { border-color: #94a3b8; background: #f8fafc; }
    .wp-btn mat-icon { font-size: 18px !important; width: 18px !important; height: 18px !important; }
    .wp-btn.primary { background: #2457a6; border-color: #2457a6; color: #fff; }
    .wp-btn.primary mat-icon { color: #fff !important; }
    .wp-btn.wide { width: 100%; min-height: 50px; }
    .wp-btn.today.is-current { background: #eaf1ff; border-color: #93c5fd; color: #1d4ed8; }

    /* ---------- Wochenleiste ---------- */
    .wp-weekbar {
      position: sticky; top: 6px; z-index: 20;
      display: flex; align-items: center; gap: 8px;
      background: #fff; border: 1px solid #e2e8f0; border-radius: 12px;
      padding: 8px 10px; margin-bottom: 10px;
      box-shadow: 0 2px 10px rgba(15,23,42,.05);
    }
    .wp-step {
      flex: 0 0 auto; width: 42px; height: 42px; border-radius: 10px;
      border: 1px solid #cbd5e1; background: #fff; cursor: pointer;
      display: inline-grid; place-items: center; color: #334155;
    }
    .wp-step:hover { border-color: #94a3b8; background: #f8fafc; }
    .wp-weekbar-mid { flex: 1 1 auto; display: flex; flex-direction: column; align-items: center; min-width: 0; }
    .wp-weekbar-mid b { font-size: 1rem; }
    .wp-weekbar-mid span { font-size: .78rem; color: #64748b; }

    /* ---------- Filter (eigene Bedienelemente, kein mat-form-field) ---------- */
    .wp-filters { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 12px; }
    .wp-field {
      display: inline-flex; align-items: center; gap: 8px;
      height: 44px; padding: 0 12px; border: 1px solid #cbd5e1; border-radius: 10px; background: #fff;
    }
    .wp-field mat-icon { color: #64748b !important; font-size: 19px !important; width: 19px !important; height: 19px !important; flex: 0 0 auto; }
    .wp-field:focus-within { border-color: #2457a6; box-shadow: 0 0 0 1px #2457a6; }
    .wp-field-search { flex: 1 1 300px; min-width: 220px; }
    .wp-field-search input { flex: 1 1 auto; min-width: 0; border: 0; outline: none; font: inherit; font-size: .92rem; background: transparent; color: #0f172a; }
    .wp-clear { border: 0; background: none; cursor: pointer; display: inline-grid; place-items: center; padding: 0; }
    .wp-field-select { flex: 0 0 auto; padding-right: 8px; }
    .wp-field-select select {
      border: 0; outline: none; background: transparent; font: inherit; font-size: .9rem; font-weight: 600;
      color: #0f172a; cursor: pointer; padding-right: 2px; appearance: none; min-width: 130px;
    }
    .wp-switch { display: inline-flex; border: 1px solid #cbd5e1; border-radius: 10px; overflow: hidden; background: #fff; margin-left: auto; }
    .wp-switch button {
      display: inline-flex; align-items: center; gap: 6px; border: 0; background: transparent;
      padding: 0 14px; height: 42px; cursor: pointer; font: inherit; font-size: .84rem; font-weight: 700; color: #475569;
    }
    .wp-switch button + button { border-left: 1px solid #e2e8f0; }
    .wp-switch button.on { background: #2457a6; color: #fff; }
    .wp-switch button.on mat-icon { color: #fff !important; }
    .wp-switch mat-icon { font-size: 18px !important; width: 18px !important; height: 18px !important; }

    .wp-loading { height: 3px; background: #e2e8f0; border-radius: 3px; overflow: hidden; margin-bottom: 10px; }
    .wp-loading span { display: block; width: 40%; height: 100%; background: #2457a6; animation: wpslide 1.1s infinite ease-in-out; }
    @keyframes wpslide { 0% { transform: translateX(-100%); } 100% { transform: translateX(300%); } }

    /* ---------- Raster ---------- */
    .wp-gridwrap { overflow-x: auto; max-width: 100%; border: 1px solid #e2e8f0; border-radius: 14px; background: #fff; }
    /* WICHTIG: kein table-layout: fixed. Damit wurden alle Spalten gleich
       breit verteilt und die Mindestbreite der Kolonnenspalte ignoriert –
       der Name blieb als \"Ko…\" übrig. */
    .wp-grid { width: 100%; border-collapse: collapse; font-size: .84rem; }
    .wp-grid thead th { position: sticky; top: 0; z-index: 2; background: #f6f8fb; border-bottom: 1px solid #e2e8f0; padding: 10px 8px; font-weight: 700; color: #334155; }
    .g-crew { text-align: left; width: 252px; min-width: 252px; position: sticky; left: 0; z-index: 3; background: #f6f8fb; }
    .g-day { width: 168px; min-width: 168px; text-align: center; }
    .g-day b { display: block; }
    .g-day span { font-weight: 500; color: #64748b; font-size: .75rem; }
    .g-day.is-today { background: #eaf1ff; color: #1d4ed8; }
    .g-day.is-dim, .g-cell.is-dim { opacity: .4; }
    .g-sum { width: 58px; min-width: 58px; }
    /* Kolonnenzelle als Raster – so kann der Text nicht mehr unter den
       Farbstreifen rutschen (war im Screenshot als Überlappung sichtbar). */
    /* Erste Spalte: Farbstreifen, Kürzelfeld, Name, Besetzung, Auslastung.
       Als Raster mit fester Spaltenaufteilung – so kann nichts überlappen
       und lange Namen werden sauber gekürzt. */
    .g-crewcell {
      width: 252px; min-width: 252px;
      position: sticky; left: 0; z-index: 1; background: #fff;
      text-align: left; vertical-align: middle;
      padding: 9px 12px; border-right: 1px solid #e2e8f0; border-bottom: 1px solid #eef2f6;
      display: grid !important; grid-template-columns: 5px 34px minmax(0, 1fr); gap: 9px; align-items: center;
    }
    .g-crewdot { width: 5px; align-self: stretch; min-height: 40px; border-radius: 3px; }
    .g-avatar {
      width: 34px; height: 34px; border-radius: 9px;
      display: inline-grid; place-items: center;
      color: #fff; font-size: .76rem; font-weight: 800; letter-spacing: .02em;
    }
    .g-crewtx { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .g-crewtx b { color: #0f172a; font-size: .88rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .g-members { color: #94a3b8; font-size: .72rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .g-crewmeta { display: flex; align-items: center; gap: 5px; margin-top: 2px; flex-wrap: nowrap; min-width: 0; }
    .g-crewmeta .g-chip { flex: 0 0 auto; }
    .g-chip { font-size: .66rem; font-weight: 800; border-radius: 999px; padding: 2px 7px; background: #eef2f6; color: #475467; white-space: nowrap; }
    .g-chip.w-low { background: #eff6ff; color: #1d4ed8; }
    .g-chip.w-good { background: #ecfdf3; color: #15803d; }
    .g-chip.w-full { background: #fef3f2; color: #b42318; }
    .g-chip.ghost { background: transparent; color: #98a2b3; padding-left: 0; }
    .g-loadbar { display: block; height: 3px; border-radius: 2px; background: #eef2f6; overflow: hidden; margin-top: 3px; }
    .g-loadbar i { display: block; height: 100%; border-radius: 2px; background: #cbd5e1; }
    .g-loadbar i.w-low { background: #93c5fd; }
    .g-loadbar i.w-good { background: #34a853; }
    .g-loadbar i.w-full { background: #d92d20; }

    .g-free { color: #cbd5e1; font-size: .78rem; font-style: italic; }
    .g-time { display: block; font-size: .71rem; font-weight: 800; color: #475569; }
    .g-sumcell { text-align: center; font-weight: 800; color: #334155; border-bottom: 1px solid #eef2f6; }
    .g-none { text-align: center; color: #94a3b8; padding: 28px; }
    .wp-hero-text { min-width: 0; }

    /* ---------- Tagesansicht mit Stundenachse ---------- */
    .wp-tl { margin-bottom: 4px; }
    .wp-tl-daybar { display: flex; gap: 6px; margin-bottom: 10px; flex-wrap: wrap; }
    .wp-tl-day {
      position: relative; display: flex; flex-direction: column; align-items: center; gap: 1px;
      min-width: 66px; padding: 7px 10px; border: 1px solid #e2e8f0; border-radius: 10px;
      background: #fff; cursor: pointer; font: inherit;
    }
    .wp-tl-day small { font-size: .68rem; text-transform: uppercase; letter-spacing: .04em; color: #64748b; }
    .wp-tl-day b { font-size: 1.02rem; color: #0f172a; line-height: 1.1; }
    .wp-tl-day em {
      position: absolute; top: 3px; right: 5px; font-style: normal; font-size: .62rem; font-weight: 800;
      color: #fff; background: #15803d; border-radius: 8px; min-width: 15px; height: 15px; line-height: 15px; padding: 0 3px;
    }
    .wp-tl-day.is-today { border-color: #93c5fd; }
    .wp-tl-day.on { background: #14396f; border-color: #14396f; }
    .wp-tl-day.on small, .wp-tl-day.on b { color: #fff; }

    .wp-tl-wrap { overflow-x: auto; max-width: 100%; border: 1px solid #e2e8f0; border-radius: 14px; background: #fff; }
    .wp-tl-inner { min-width: 980px; }
    .wp-tl-head {
      display: grid; grid-template-columns: 252px minmax(0, 1fr);
      background: #f6f8fb; border-bottom: 1px solid #e2e8f0;
      position: sticky; top: 0; z-index: 2;
    }
    .wp-tl-corner { padding: 9px 12px; font-weight: 700; font-size: .8rem; color: #334155; border-right: 1px solid #e2e8f0; }
    /* Genauso viele Spalten wie Stunden – die Beschriftung sitzt damit
       buendig auf ihrer Linie. */
    .wp-tl-hours { display: grid; grid-auto-flow: column; grid-auto-columns: minmax(0, 1fr); }
    .wp-tl-hour {
      padding: 9px 0 9px 5px; font-size: .7rem; font-weight: 700; color: #64748b;
      border-left: 1px solid #e2e8f0; white-space: nowrap; overflow: hidden;
    }
    .wp-tl-hour:first-child { border-left: 0; }
    .wp-tl-row { display: grid; grid-template-columns: 252px minmax(0, 1fr); border-bottom: 1px solid #eef2f6; }
    .wp-tl-row:last-child { border-bottom: 0; }
    /* Zwei Elemente: Farbstreifen und Textblock. (Eine dritte Spalte hatte
       den Namen auf wenige Zeichen zusammengedrückt.) */
    .wp-tl-name {
      display: grid; grid-template-columns: 5px minmax(0, 1fr); gap: 10px; align-items: center;
      padding: 8px 12px; border-right: 1px solid #e2e8f0; min-width: 0;
    }
    .wp-tl-dot { width: 5px; align-self: stretch; min-height: 30px; border-radius: 3px; }
    .wp-tl-nametx { display: flex; flex-direction: column; min-width: 0; }
    .wp-tl-nametx b { font-size: .86rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .wp-tl-nametx small { font-size: .71rem; color: #64748b; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .wp-tl-lane {
      position: relative; min-height: 58px;
      display: grid; grid-auto-flow: column; grid-auto-columns: minmax(0, 1fr);
    }
    .wp-tl-line { border-left: 1px solid #f1f5f9; }
    .wp-tl-line:first-child { border-left: 0; }
    .wp-tl-now { position: absolute; top: 0; bottom: 0; width: 2px; background: #dc2626; z-index: 3; }
    .wp-tl-now::before { content: ''; position: absolute; top: -3px; left: -3px; width: 8px; height: 8px; border-radius: 50%; background: #dc2626; }
    .wp-tl-bar {
      position: absolute; top: 7px; bottom: 7px; z-index: 2;
      display: flex; flex-direction: column; justify-content: center; gap: 1px;
      border: 1px solid #b8d0f5; border-left: 3px solid #2f6fed; background: #eaf2ff;
      border-radius: 7px; padding: 4px 9px; cursor: pointer; overflow: hidden; text-align: left; font: inherit;
    }
    .wp-tl-bar:hover { background: #dfeaff; border-color: #7fa9ec; }
    .wp-tl-bar b { font-size: .79rem; color: #0f172a; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .wp-tl-bar small { font-size: .7rem; color: #475569; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .wp-tl-bar.k-inspection { background: #fff5ea; border-color: #f3d3ae; border-left-color: #b45309; }
    .wp-tl-bar.k-other { background: #f4f6f9; border-color: #dfe5ec; border-left-color: #64748b; }
    .wp-tl-free { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); color: #cbd5e1; font-size: .78rem; font-style: italic; }
    .wp-tl-empty { padding: 26px; text-align: center; color: #94a3b8; }

    /* ---------- Dienstplan-Feinheiten (Wochenraster) ---------- */
    .g-day em { display: block; font-style: normal; font-size: .68rem; font-weight: 500; color: #98a2b3; margin-top: 2px; }
    .g-dayload { display: block; height: 3px; border-radius: 2px; background: #eef2f6; overflow: hidden; margin: 4px 6px 0; }
    .g-dayload i { display: block; height: 100%; background: #2457a6; border-radius: 2px; }
    .g-item-top { display: flex; align-items: center; justify-content: space-between; gap: 6px; }
    .g-kind { font-size: .62rem; font-weight: 800; text-transform: uppercase; letter-spacing: .04em; color: #667085; background: #eef2f6; border-radius: 999px; padding: 1px 6px; }
    .g-item.k-inspection .g-kind { background: #fff3e6; color: #9a3412; }

    /* ---------- Gewählte Tage heller (Runde 39) ----------
       Das dunkle Schiefergrau (#334155) wirkte wie „gesperrt" statt wie
       „ausgewählt". Ausgewählt heißt jetzt: heller blauer Grund mit
       kräftiger Schrift – deutlich, aber nicht schwer. */
    .wp-tl-day.on {
      background: #e8f0fe !important;
      border-color: #1a73e8 !important;
      box-shadow: inset 0 0 0 1px #1a73e8;
    }
    .wp-tl-day.on small { color: #1a73e8 !important; }
    .wp-tl-day.on b { color: #14396f !important; }
    .wp-tl-day.on em { background: #1a73e8 !important; }

    /* Datumsfeld der Agenda: heller Grund, Monat vollständig lesbar */
    .wp-day.is-today .wp-day-date { background: #e8f0fe !important; }
    .wp-day.is-today .wp-day-date b { color: #14396f !important; }
    .wp-day.is-today .wp-day-date small { color: #1a73e8 !important; }
    .wp-day-date {
      width: 46px !important; height: auto !important;
      min-height: 46px; padding: 5px 2px;
      overflow: visible;
    }
    .wp-day-date b { font-size: 1.14rem; line-height: 1.1; }
    .wp-day-date small {
      font-size: .6rem !important; letter-spacing: .02em;
      white-space: nowrap; overflow: visible;
    }
    .wp-switch button.on { background: #1a73e8 !important; }

    /* ---------- Weitere Beruhigung (Runde 35) ----------
       Kopfzeile schlanker, Wochenleiste dreigeteilt: Navigation links,
       Zeitraum mittig, Kennzahlen rechts. Farbe nur noch dort, wo sie
       etwas bedeutet – Einsatzart und Auslastung. */
    .wp-weekbar {
      display: grid !important;
      grid-template-columns: auto minmax(0, 1fr) auto;
      align-items: center; gap: 12px;
      padding: 7px 12px;
    }
    .wp-weekbar-nav { display: flex; align-items: center; gap: 6px; }
    .wp-weekbar-mid { text-align: center; }
    .wp-weekbar-mid b { font-size: .95rem; font-weight: 600; }
    .wp-weekbar-mid span { display: block; font-size: .76rem; color: #667085; }
    .wp-weekbar-sum { display: flex; gap: 16px; }
    .wp-weekbar-sum span {
      display: flex; flex-direction: column; align-items: flex-end;
      font-size: .68rem; color: #667085; line-height: 1.2;
    }
    .wp-weekbar-sum b { font-size: .95rem; font-weight: 700; color: #101828; }
    .wp-btn.today { min-height: 36px; padding: 0 12px; font-size: .82rem; }
    .wp-step { width: 36px; height: 36px; }

    /* Kopfbereich schlanker */
    .wp-hero-plain { padding: 12px 16px; }
    .wp-hero-plain h1 { font-size: 1.2rem; }
    .wp-hero-sub { font-size: .8rem; }

    /* Filterleiste ruhiger */
    .wp-field { height: 40px; }
    .wp-switch button { height: 38px; font-size: .8rem; }

    /* Tagesansicht: feinere Linien, weniger Fläche */
    .wp-tl-wrap { border-color: #e4e7ea; }
    .wp-tl-head { background: #fafbfc; }
    .wp-tl-hour { font-size: .68rem; }
    .wp-tl-bar { border-width: 1px; border-left-width: 3px; }
    .wp-tl-bar b { font-weight: 600; }

    /* Agenda-Karten flacher */
    .wp-card { border-color: #e4e7ea; }
    .wp-card-rail { flex-basis: 3px; }
    .wp-chip-kind { font-size: .64rem; padding: 2px 7px; }

    @media (max-width: 860px) {
      .wp-weekbar { grid-template-columns: auto minmax(0, 1fr); }
      .wp-weekbar-sum { display: none; }
    }

    /* ---------- Sachlichere Gestaltung (Rückbau) ----------
       Der Plan wirkte durch Farbverläufe, Pillenformen, Schatten und viele
       farbige Chips zu verspielt. Für ein Werkzeug, das täglich benutzt wird,
       zählt Ruhe: gedeckte Farben, klare Kanten, wenig Rundung. */
    .wp-hero-plain { border-radius: 6px; padding: 14px 16px; }
    .wp-hero-plain h1 { font-size: 1.32rem; font-weight: 600; }
    .wp-eyebrow { letter-spacing: .08em; }

    .wp-btn { border-radius: 4px; font-weight: 600; }
    .wp-step { border-radius: 4px; }
    .wp-weekbar { border-radius: 6px; box-shadow: none; }
    .wp-switch { border-radius: 4px; }
    .wp-switch button.on { background: #334155; }
    .wp-field { border-radius: 4px; }

    /* Karten: flach, mit klarer Kante statt Schatten und starker Rundung */
    .wp-day { border-radius: 6px; box-shadow: none !important; }
    .wp-day.is-today { border-color: #94a3b8; box-shadow: none !important; }
    .wp-card { border-radius: 5px; }
    .wp-card:hover { border-color: #94a3b8; box-shadow: none; transform: none; }
    .wp-card-rail { flex-basis: 4px; }
    .wp-day-date { border-radius: 5px; background: #eef1f5; }
    .wp-day.is-today .wp-day-date { background: #334155; }
    .wp-tag { border-radius: 4px; background: #eef1f5; color: #334155; text-transform: none; font-weight: 600; }
    .wp-chip-kind { border-radius: 4px; text-transform: none; letter-spacing: 0; }
    .wp-ic-btn { border-radius: 4px; }

    /* Tagesansicht: ruhigere Farbgebung */
    .wp-tl-day { border-radius: 4px; }
    .wp-tl-day.on { background: #334155; border-color: #334155; }
    .wp-tl-day em { background: #475569; border-radius: 3px; }
    .wp-tl-wrap { border-radius: 6px; }
    .wp-tl-bar { border-radius: 4px; }
    .wp-tl-dot { border-radius: 2px; }
    .wp-tl-nametx b { font-weight: 600; }

    /* Detailfenster nüchtern */
    .wp-sheet { border-radius: 8px; }
    .wp-sheet-head { background: #f1f5f9; }
    .wp-sheet-head.k-inspection { background: #f5f0e9; }
    .wp-sheet-head small { letter-spacing: .05em; }
    .wp-btn.primary { background: #334155; border-color: #334155; }

    /* ---------- Agenda: Lesbarkeit ----------
       Die hellgrauen Töne (#94a3b8) auf weißem Grund lagen unter dem
       empfohlenen Kontrast. Angehoben auf mindestens #64748b, Kleintext
       zusätzlich etwas größer. */
    .wp-card-l.dim { color: #64748b !important; font-size: .8rem !important; }
    .wp-card-l mat-icon { color: #64748b !important; }
    .wp-day-tx small { color: #475467 !important; }
    .wp-day-free { color: #64748b !important; }
    .wp-chip-kind { color: #344054 !important; background: #e8ecf1 !important; }
    .wp-card.k-inspection .wp-chip-kind { background: #fdebd8 !important; color: #7a3d0a !important; }
    .wp-chip-time { color: #101828 !important; }
    .wp-chip-time mat-icon { color: #475467 !important; }
    .wp-tl-nametx small { color: #475467 !important; }
    .wp-tl-free, .wp-day-date small { color: #64748b !important; }

    /* Symbole in allen Schaltflächen der Seite exakt mittig */
    .wp-ic-btn, .wp-step, .wp-tl-day, .wp-btn, .wp-switch button {
      display: inline-flex !important;
      align-items: center !important;
      justify-content: center !important;
    }
    .wp-ic-btn mat-icon, .wp-step mat-icon {
      display: block !important;
      font-size: 20px !important; width: 20px !important; height: 20px !important;
      line-height: 20px !important; margin: 0 !important;
    }
    .wp-btn mat-icon, .wp-switch button mat-icon {
      display: block !important;
      font-size: 18px !important; width: 18px !important; height: 18px !important;
      line-height: 18px !important; margin: 0 !important;
    }
    .wp-card-l mat-icon, .wp-chip-time mat-icon {
      display: block !important; line-height: 14px !important;
    }

    /* ---------- Agenda ---------- */
    .wp-agenda { display: none; }
    .wp-agenda.chosen { display: block; }
    .wp-day { border: 1px solid #e2e8f0; border-radius: 14px; padding: 14px; margin-bottom: 12px; background: #fff; }
    .wp-day.is-today { border-color: #93c5fd; box-shadow: 0 0 0 1px #dbeafe inset; }
    .wp-day-head { display: flex; align-items: center; gap: 12px; margin-bottom: 10px; }
    .wp-day-date { display: flex; flex-direction: column; align-items: center; justify-content: center; width: 48px; height: 48px; border-radius: 12px; background: #f1f5f9; flex: 0 0 auto; }
    .wp-day.is-today .wp-day-date { background: #2457a6; }
    .wp-day.is-today .wp-day-date b, .wp-day.is-today .wp-day-date small { color: #fff; }
    .wp-day-date b { font-size: 1.2rem; line-height: 1; }
    .wp-day-date small { font-size: .66rem; text-transform: uppercase; color: #64748b; }
    .wp-day-tx { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; }
    .wp-day-tx b { font-size: .96rem; }
    .wp-day-tx small { color: #64748b; font-size: .8rem; }
    .wp-tag { background: #eaf1ff; color: #1d4ed8; border-radius: 999px; padding: 3px 10px; font-size: .7rem; font-weight: 800; text-transform: uppercase; flex: 0 0 auto; }
    .wp-day-free { display: flex; align-items: center; gap: 8px; color: #94a3b8; font-size: .88rem; margin: 2px 0 0; }

    .wp-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 10px; }
    /* Zeitspalte links macht die Abfolge des Tages auf einen Blick lesbar. */
    .wp-card-time-col { display: none; }
    .wp-card { display: flex; background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; cursor: pointer; transition: border-color .12s, box-shadow .12s, transform .12s; }
    .wp-card:hover { border-color: #93c5fd; box-shadow: 0 6px 18px rgba(36,87,166,.13); transform: translateY(-1px); }
    .wp-card:focus-visible { outline: 2px solid #2457a6; outline-offset: 2px; }
    .wp-card-rail { flex: 0 0 5px; background: #2f6fed; }
    .wp-card.k-inspection .wp-card-rail { background: #b45309; }
    .wp-card.k-other .wp-card-rail { background: #64748b; }
    .wp-card-in { flex: 1 1 auto; min-width: 0; padding: 11px 13px; display: flex; flex-direction: column; gap: 3px; }
    .wp-card-row1 { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 1px; }
    .wp-chip-time { display: inline-flex; align-items: center; gap: 4px; font-size: .76rem; font-weight: 800; color: #334155; }
    .wp-chip-time mat-icon { font-size: 14px !important; width: 14px !important; height: 14px !important; color: #64748b !important; }
    .wp-chip-kind { font-size: .66rem; font-weight: 800; text-transform: uppercase; letter-spacing: .04em; color: #475569; background: #f1f5f9; border-radius: 999px; padding: 3px 9px; }
    .wp-card.k-inspection .wp-chip-kind { background: #fff3e6; color: #9a3412; }
    .wp-card-h { font-size: .98rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .wp-card-l { display: inline-flex; align-items: center; gap: 5px; font-size: .82rem; color: #475569; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .wp-card-l.dim { color: #94a3b8; font-size: .78rem; }
    .wp-card-l mat-icon { font-size: 14px !important; width: 14px !important; height: 14px !important; flex: 0 0 auto; }
    .wp-card-side { display: flex; flex-direction: column; justify-content: center; gap: 5px; padding: 8px; border-left: 1px solid #f1f5f9; flex: 0 0 auto; }
    .wp-ic-btn { display: inline-flex; align-items: center; justify-content: center; width: 38px; height: 38px; border-radius: 9px; border: 1px solid #e2e8f0; color: #2457a6; background: #fff; }
    .wp-ic-btn:hover { border-color: #93c5fd; background: #f5f9ff; }

    /* ---------- Detailansicht ---------- */
    /* Die Überlagerung beginnt erst hinter der Seitenleiste, damit die
       Detailansicht mittig im Inhaltsbereich sitzt und die Navigation
       bedienbar bleibt. Die Breite liefert die Anwendungsschale als
       CSS-Variable (--ew-sidebar-width). */
    .wp-modal {
      position: fixed; top: 0; right: 0; bottom: 0;
      left: var(--ew-sidebar-width, 0px);
      z-index: 1100; background: rgba(15,23,42,.5);
      display: grid; place-items: center; padding: 16px;
    }
    .wp-sheet { width: min(470px, 96vw); max-height: 88vh; overflow-y: auto; background: #fff; border-radius: 16px; box-shadow: 0 20px 56px rgba(15,23,42,.34); }
    .wp-sheet-grab { display: none; }
    .wp-sheet-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; padding: 16px 18px; background: #eef4ff; }
    .wp-sheet-head.k-inspection { background: #fff4e8; }
    .wp-sheet-head.k-other { background: #f1f5f9; }
    .wp-sheet-head small { display: block; font-size: .7rem; font-weight: 800; text-transform: uppercase; letter-spacing: .05em; color: #64748b; }
    .wp-sheet-head b { font-size: 1.1rem; }
    .wp-sheet-x { border: 0; background: rgba(255,255,255,.8); border-radius: 9px; width: 36px; height: 36px; display: inline-grid; place-items: center; cursor: pointer; color: #475569; flex: 0 0 auto; }
    .wp-sheet-list { margin: 0; padding: 14px 18px; display: flex; flex-direction: column; gap: 12px; }
    .wp-sheet-list > div { display: grid; grid-template-columns: 132px 1fr; gap: 10px; align-items: start; }
    .wp-sheet-list dt { display: inline-flex; align-items: center; gap: 6px; color: #64748b; font-size: .82rem; }
    .wp-sheet-list dt mat-icon { font-size: 16px !important; width: 16px !important; height: 16px !important; }
    .wp-sheet-list dd { margin: 0; font-weight: 600; font-size: .9rem; }
    .wp-sheet-actions { display: flex; flex-direction: column; gap: 8px; padding: 4px 18px 18px; }

    .wp-printhead { display: none; }

    /* ================= Mobil ================= */
    @media (max-width: 960px) {
      .wp { padding: 4px 10px 100px; }
      .wp-hero { padding: 16px; border-radius: 14px; }
      .wp-hero h1 { font-size: 1.4rem; }
      .wp-hero-actions { width: 100%; }
      .wp-hero-actions .wp-btn { flex: 1 1 0; }
      /* Das Raster ist auf schmalen Geräten nicht bedienbar – immer Agenda. */
      .wp-tl { display: none !important; }
      .wp-agenda { display: block !important; }
      .wp-switch { display: none; }
      .wp-filters { gap: 6px; }
      .wp-field-search { flex: 1 1 100%; }
      .wp-field-select { flex: 1 1 calc(50% - 3px); }
      .wp-field-select select { min-width: 0; width: 100%; }
      /* Agenda am Handy: Zeitleiste links, Karte rechts – wie ein Tagesplan.
         Die Karten sind flacher, weil Adresse und Kolonne zusammenrücken. */
      .wp-cards { grid-template-columns: 1fr; gap: 8px; }
      .wp-card { border-radius: 10px; }
      .wp-card-rail { flex-basis: 4px; }
      .wp-card-in { padding: 10px 11px; gap: 2px; }
      .wp-card-row1 { margin-bottom: 2px; }
      .wp-chip-time { font-size: .8rem; }
      .wp-card-h { font-size: .94rem; white-space: normal; }
      .wp-card-l { font-size: .8rem; }
      .wp-card-side { flex-direction: row; align-items: center; padding: 6px 8px; border-left: 0; border-top: 0; }
      .wp-ic-btn { width: 42px; height: 42px; }
      /* Tageskarte kompakter, damit mehr Einsätze ohne Scrollen sichtbar sind. */
      .wp-day { padding: 12px 12px 14px; border-radius: 12px; margin-bottom: 10px; }
      .wp-day-head { gap: 10px; margin-bottom: 8px; }
      .wp-day-date { width: 42px; height: 42px; border-radius: 10px; }
      .wp-day-date b { font-size: 1.06rem; }
      .wp-day-tx b { font-size: .9rem; }
      .wp-day-tx small { font-size: .76rem; }
      /* Tage ohne Einsätze schrumpfen auf eine Zeile zusammen. */
      .wp-day.is-empty { padding: 10px 12px; opacity: .72; }
      .wp-day.is-empty .wp-day-head { margin-bottom: 0; }
      .wp-day.is-empty .wp-day-free { display: none; }

      /* Detailansicht als Blatt von unten – daumenfreundlich */
      .wp-modal { place-items: end center; padding: 0; }
      .wp-sheet {
        width: 100%; max-width: none; max-height: 90vh;
        border-radius: 18px 18px 0 0; padding-bottom: env(safe-area-inset-bottom);
        animation: wpsheet .18s ease-out;
      }
      .wp-sheet-grab { display: block; width: 42px; height: 4px; border-radius: 3px; background: #cbd5e1; margin: 8px auto 0; }
      .wp-sheet-list > div { grid-template-columns: 1fr; gap: 2px; }
      @keyframes wpsheet { from { transform: translateY(14%); } to { transform: translateY(0); } }
    }
    @media (max-width: 420px) {
    }

    /* ================= Druck ================= */
    @media print {
      .wp-hero, .wp-weekbar, .wp-filters, .wp-modal, .wp-loading { display: none !important; }
      .wp-printhead { display: flex !important; justify-content: space-between; align-items: baseline; margin-bottom: 10px; font-size: 13pt; }
      .wp-tl { display: block !important; }
      .wp-tl-wrap { overflow: visible !important; border: 0; }
      .wp-grid { font-size: 9pt; }
      .g-item { break-inside: avoid; }
    }
  `]
})
export class WorkPlanComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly snack = inject(MatSnackBar);

  readonly crews = signal<GenericRecord[]>([]);
  readonly members = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly assignments = signal<GenericRecord[]>([]);
  readonly loading = signal(false);
  readonly anchor = signal(new Date());
  readonly detail = signal<GenericRecord | null>(null);
  /** Optionaler Tagesfokus über die Tagesleiste. */
  readonly focusDay = signal<string>('');
  search = '';
  scope: 'all' | 'mine' | 'busy' = 'all';
  workWeekOnly = true;
  /** Tagesansicht ist die Standardlösung – sie zeigt den Arbeitstag am genauesten. */
  /** Nur noch Tag und Agenda – die Wochenplanung liegt in der Kolonnenplanung. */
  view: 'timeline' | 'agenda' = 'timeline';
  /** Angezeigter Tag in der Stundenansicht. */
  readonly timelineDay = signal<string>('');
  /** Dargestellter Zeitraum der Stundenachse. */
  readonly startHour = 5;
  readonly endHour = 21;

  constructor() { this.load(); }

  // ---- Zeitraum ----------------------------------------------------------
  private iso(d: Date): string {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }
  private monday(): Date {
    const d = new Date(this.anchor());
    d.setDate(d.getDate() - ((d.getDay() + 6) % 7));
    d.setHours(12, 0, 0, 0);
    return d;
  }
  readonly days = computed<PlanDay[]>(() => {
    const start = this.monday();
    const today = this.iso(new Date());
    return Array.from({ length: this.workWeekOnly ? 5 : 7 }, (_, i) => {
      const date = new Date(start); date.setDate(start.getDate() + i);
      return {
        iso: this.iso(date), date,
        label: date.toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: '2-digit' }),
        short: date.toLocaleDateString('de-DE', { weekday: 'short' }),
        dayNum: String(date.getDate()).padStart(2, '0'),
        month: date.toLocaleDateString('de-DE', { month: 'short' }),
        isToday: this.iso(date) === today,
        isWeekend: i >= 5
      };
    });
  });
  /** In der Agenda kann auf einen Tag eingegrenzt werden. */
  visibleDays(): PlanDay[] {
    const focus = this.focusDay();
    return focus ? this.days().filter((d) => d.iso === focus) : this.days();
  }
  toggleFocus(iso: string): void { this.focusDay.set(this.focusDay() === iso ? '' : iso); }
  isoWeek(): number {
    const d = new Date(this.monday()); d.setHours(0, 0, 0, 0); d.setDate(d.getDate() + 3);
    const first = new Date(d.getFullYear(), 0, 4);
    return 1 + Math.round(((d.getTime() - first.getTime()) / 86400000 - 3 + ((first.getDay() + 6) % 7)) / 7);
  }
  rangeLabel(): string {
    const list = this.days();
    if (!list.length) return '';
    const fmt = (d: Date) => d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
    return `${fmt(list[0].date)} – ${fmt(list[list.length - 1].date)}`;
  }
  isCurrentWeek(): boolean {
    const now = new Date(); const m = new Date(now); m.setDate(now.getDate() - ((now.getDay() + 6) % 7));
    return this.iso(m) === this.iso(this.monday());
  }
  shiftWeek(delta: number): void { const d = new Date(this.anchor()); d.setDate(d.getDate() + delta * 7); this.anchor.set(d); this.focusDay.set(''); this.loadAssignments(); }
  thisWeek(): void { this.anchor.set(new Date()); this.focusDay.set(''); this.loadAssignments(); }

  // ---- Daten -------------------------------------------------------------
  load(): void {
    this.loading.set(true);
    forkJoin({
      crews: this.api.list('crews', '', 200),
      members: this.api.list('crew_members', '', 400),
      sites: this.api.list('construction_sites', '', 400)
    }).subscribe({
      next: ({ crews, members, sites }) => {
        this.crews.set(crews.items || []);
        this.members.set(members.items || []);
        this.sites.set(sites.items || []);
        this.loadAssignments();
      },
      error: () => { this.loading.set(false); this.snack.open('Arbeitsplan konnte nicht geladen werden.', 'OK', { duration: 3000 }); }
    });
  }
  private loadAssignments(): void {
    const list = this.days();
    if (!list.length) return;
    this.loading.set(true);
    this.api.planningAssignments(list[0].iso, list[list.length - 1].iso).subscribe({
      next: (res) => { this.assignments.set(res.items || []); this.loading.set(false); },
      error: () => { this.assignments.set([]); this.loading.set(false); }
    });
  }

  // ---- Darstellung -------------------------------------------------------
  kindOf(a: GenericRecord): 'work' | 'inspection' | 'other' {
    const title = String(a['title'] || '').toLowerCase();
    if (/begehung|besichtigung|abnahme/.test(title)) return 'inspection';
    if (/estrich|arbeit/.test(title)) return 'work';
    return 'other';
  }
  kindLabel(a: GenericRecord): string {
    const map: Record<string, string> = { work: 'Estrich', inspection: 'Begehung', other: 'Sonstiges' };
    return map[this.kindOf(a)] || 'Einsatz';
  }
  timeLabel(a: GenericRecord): string {
    const fmt = (v: unknown) => { const m = String(v ?? '').match(/(\d{2}):(\d{2})/); return m ? `${m[1]}:${m[2]}` : ''; };
    const from = fmt(a['start_at']); const to = fmt(a['end_at']);
    return from && to ? `${from}–${to}` : (from || 'ganztägig');
  }
  dateLabel(a: GenericRecord): string {
    const raw = String(a['start_at'] || '').slice(0, 10);
    if (!raw) return '—';
    const d = new Date(raw + 'T12:00:00');
    return isNaN(d.getTime()) ? raw : d.toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' });
  }
  crewName(id: unknown): string {
    return String(this.crews().find((c) => String(c['id']) === String(id ?? ''))?.['name'] || 'Ohne Kolonne');
  }
  crewColor(crew: GenericRecord): string { return String(crew['color'] || '#2f6fed'); }
  memberLabel(crew: GenericRecord): string {
    const names = this.members().filter((m) => String(m['crew_id']) === String(crew['id']))
      .map((m) => String(m['user_name'] || m['name'] || '').trim()).filter((v) => v.length > 0);
    return names.join(', ');
  }
  addressOf(a: GenericRecord): string {
    const site = this.sites().find((s) => String(s['id']) === String(a['site_id']));
    if (!site) return '';
    return [site['address'], site['city']].map((v) => String(v ?? '').trim()).filter((v) => v.length > 0).join(', ');
  }
  mapsUrl(a: GenericRecord): string {
    const addr = this.addressOf(a);
    return addr ? 'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(addr) : '';
  }
  tooltipFor(a: GenericRecord): string {
    return [a['title'], a['site_name'] || a['project_name'], this.addressOf(a), this.timeLabel(a)]
      .map((v) => String(v ?? '').trim()).filter((v) => v.length > 0).join('\n');
  }
  openDetail(a: GenericRecord): void { this.detail.set(a); }
  closeDetail(): void { this.detail.set(null); }

  // ---- Auswertung --------------------------------------------------------
  private matches(a: GenericRecord, crew: GenericRecord): boolean {
    const q = this.search.trim().toLowerCase();
    if (!q) return true;
    return [crew['name'], a['title'], a['site_name'], a['project_name'], this.addressOf(a)].join(' ').toLowerCase().includes(q);
  }
  entriesFor(crew: GenericRecord, day: PlanDay): GenericRecord[] {
    return this.assignments()
      .filter((a) => String(a['crew_id'] ?? '') === String(crew['id']))
      .filter((a) => String(a['start_at'] || '').slice(0, 10) === day.iso)
      .filter((a) => this.matches(a, crew))
      .sort((a, b) => String(a['start_at'] || '').localeCompare(String(b['start_at'] || '')));
  }
  dayEntries(day: PlanDay): GenericRecord[] { return this.visibleCrews().flatMap((crew) => this.entriesFor(crew, day)); }
  weekCount(crew: GenericRecord): number { return this.days().reduce((s, d) => s + this.entriesFor(crew, d).length, 0); }
  dayCrewCount(day: PlanDay): number { return new Set(this.dayEntries(day).map((a) => String(a['crew_id'] ?? ''))).size; }
  /** Auslastungsbalken der Tagesleiste: Anteil der belegten Kolonnen. */
  loadPercent(day: PlanDay): number {
    const total = this.visibleCrews().length || 1;
    return Math.min(100, Math.round((this.dayCrewCount(day) / total) * 100));
  }
  totalCount(): number { return this.days().reduce((s, d) => s + this.dayEntries(d).length, 0); }
  busyCrewCount(): number { return this.crews().filter((c) => this.weekCount(c) > 0).length; }
  inspectionCount(): number { return this.days().reduce((s, d) => s + this.dayEntries(d).filter((a) => this.kindOf(a) === 'inspection').length, 0); }
  freeSlotCount(): number { return this.visibleCrews().reduce((s, c) => s + this.days().filter((d) => !this.entriesFor(c, d).length).length, 0); }

  private myCrewIds(): Set<string> {
    const userId = String(this.auth.userId || '');
    return new Set(this.members().filter((m) => String(m['user_id']) === userId).map((m) => String(m['crew_id'])));
  }
  visibleCrews(): GenericRecord[] {
    let list = this.crews();
    if (this.scope === 'mine') { const mine = this.myCrewIds(); list = list.filter((c) => mine.has(String(c['id']))); }
    if (this.scope === 'busy') list = list.filter((c) => this.weekCount(c) > 0);
    return list;
  }

  // ---- Tagesansicht mit Stundenachse ------------------------------------
  /** Stundenspalten. Die Schleife laeuft bis VOR endHour, damit jede Spalte
   *  genau einen Stundenblock abbildet und buendig auf ihrer Linie sitzt. */
  hours(): number[] { const out: number[] = []; for (let h = this.startHour; h < this.endHour; h++) out.push(h); return out; }
  private spanMinutes(): number { return (this.endHour - this.startHour) * 60; }
  private minutesOf(value: unknown): number {
    const m = String(value ?? '').match(/(\d{2}):(\d{2})/);
    return m ? Number(m[1]) * 60 + Number(m[2]) : this.startHour * 60;
  }
  /** Gewaehlter Tag – standardmaessig heute, sonst der erste Tag der Woche. */
  private activeDayIso(): string {
    const chosen = this.timelineDay();
    const list = this.days();
    if (chosen && list.some((d) => d.iso === chosen)) return chosen;
    return (list.find((d) => d.isToday) || list[0])?.iso || '';
  }
  isTodayTimeline(): boolean {
    const iso = this.activeDayIso();
    return !!this.days().find((d) => d.iso === iso && d.isToday);
  }
  timelineEntries(crew: GenericRecord): GenericRecord[] {
    const iso = this.activeDayIso();
    const day = this.days().find((d) => d.iso === iso);
    return day ? this.entriesFor(crew, day) : [];
  }
  barLeft(a: GenericRecord): number {
    const start = this.minutesOf(a['start_at']) - this.startHour * 60;
    return Math.max(0, Math.min(100, (start / this.spanMinutes()) * 100));
  }
  barWidth(a: GenericRecord): number {
    const start = Math.max(this.minutesOf(a['start_at']), this.startHour * 60);
    const end = Math.min(this.minutesOf(a['end_at']), this.endHour * 60);
    const width = ((end - start) / this.spanMinutes()) * 100;
    // Sehr kurze Termine (z. B. eine Kontrolle) bleiben lesbar.
    return Math.max(3.5, Math.min(100 - this.barLeft(a), width));
  }
  /** Markierung der aktuellen Uhrzeit. */
  nowPercent(): number {
    const now = new Date();
    const minutes = now.getHours() * 60 + now.getMinutes() - this.startHour * 60;
    return Math.max(0, Math.min(100, (minutes / this.spanMinutes()) * 100));
  }
  /** Belegte Stunden einer Kolonne am gewaehlten Tag. */
  timelineLoadLabel(crew: GenericRecord): string {
    const entries = this.timelineEntries(crew);
    if (!entries.length) return 'nicht eingeteilt';
    const minutes = entries.reduce((sum, a) => sum + Math.max(0, this.minutesOf(a['end_at']) - this.minutesOf(a['start_at'])), 0);
    const hours = Math.round((minutes / 60) * 10) / 10;
    return `${entries.length} Einsatz(e) · ${String(hours).replace('.', ',')} h`;
  }

  /** Kurzform der Art für die Kennzeichnung im Dienstplan. */
  kindShort(a: GenericRecord): string {
    const map: Record<string, string> = { work: 'Estrich', inspection: 'Termin', other: 'Sonst.' };
    return map[this.kindOf(a)] || '';
  }
  /** Anteil der belegten Kolonnen an einem Tag – für den Balken im Kopf. */
  dayLoadPercent(day: PlanDay): number {
    const total = Math.max(1, this.visibleCrews().length);
    return Math.min(100, Math.round((this.dayCrewCount(day) / total) * 100));
  }
  /** Geplante Stunden einer Kolonne in der angezeigten Woche. */
  weekHours(crew: GenericRecord): string {
    const minutes = this.days().reduce((sum, day) => sum + this.entriesFor(crew, day)
      .reduce((m, a) => m + Math.max(0, this.minutesOf(a['end_at']) - this.minutesOf(a['start_at'])), 0), 0);
    const hours = Math.round((minutes / 60) * 10) / 10;
    return String(hours).replace('.', ',');
  }

  print(): void { window.print(); }

  /** Arbeitsplan als PDF-Datei speichern (Querformat, eine Zeile je Kolonne). */
  exportPdf(): void {
    const doc = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' });
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();
    const margin = 10;
    const days = this.days();
    const crews = this.visibleCrews();

    // Kopf
    doc.setFont('helvetica', 'bold'); doc.setFontSize(14);
    doc.text(`Arbeitsplan KW ${this.isoWeek()}`, margin, margin + 5);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.setTextColor(90);
    doc.text(this.rangeLabel(), margin, margin + 11);
    doc.text(`Stand: ${new Date().toLocaleDateString('de-DE')} ${new Date().toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`,
             pageW - margin, margin + 11, { align: 'right' });
    doc.setTextColor(0);

    const nameW = 42;
    const colW = (pageW - margin * 2 - nameW) / Math.max(1, days.length);
    let y = margin + 17;

    // Spaltenkoepfe
    doc.setFillColor(243, 246, 250);
    doc.rect(margin, y, pageW - margin * 2, 8, 'F');
    doc.setFont('helvetica', 'bold'); doc.setFontSize(8);
    doc.text('Kolonne', margin + 2, y + 5.4);
    days.forEach((day, i) => {
      doc.text(`${day.short} ${day.date.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' })}`,
               margin + nameW + i * colW + 2, y + 5.4);
    });
    y += 8;

    const lineH = 3.6;
    doc.setFontSize(7);

    for (const crew of crews) {
      // Zeilenhoehe aus der vollsten Spalte ableiten
      const perDay = days.map((d) => this.entriesFor(crew, d));
      const maxItems = Math.max(1, ...perDay.map((list) => list.length));
      const rowH = Math.max(11, maxItems * (lineH * 3) + 3);

      if (y + rowH > pageH - margin - 6) {
        doc.addPage(); y = margin;
        doc.setFillColor(243, 246, 250);
        doc.rect(margin, y, pageW - margin * 2, 8, 'F');
        doc.setFont('helvetica', 'bold'); doc.setFontSize(8);
        doc.text('Kolonne', margin + 2, y + 5.4);
        days.forEach((day, i) => doc.text(`${day.short} ${day.date.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' })}`,
                                          margin + nameW + i * colW + 2, y + 5.4));
        y += 8; doc.setFontSize(7);
      }

      // Kolonnenname
      doc.setFont('helvetica', 'bold'); doc.setFontSize(8);
      doc.text(this.trimText(doc, String(crew['name'] || ''), nameW - 4), margin + 2, y + 5);
      const members = this.memberLabel(crew);
      if (members) {
        doc.setFont('helvetica', 'normal'); doc.setFontSize(6.5); doc.setTextColor(120);
        doc.text(this.trimText(doc, members, nameW - 4), margin + 2, y + 8.6);
        doc.setTextColor(0);
      }

      // Eintraege je Tag
      doc.setFontSize(7);
      perDay.forEach((list, i) => {
        const x = margin + nameW + i * colW;
        if (!list.length) {
          doc.setFont('helvetica', 'italic'); doc.setTextColor(170);
          doc.text('frei', x + 2, y + 5);
          doc.setTextColor(0); return;
        }
        let ty = y + 4;
        list.forEach((a) => {
          const kind = this.kindOf(a);
          // Farbstreifen links am Eintrag
          if (kind === 'inspection') doc.setFillColor(180, 83, 9);
          else if (kind === 'other') doc.setFillColor(100, 116, 139);
          else doc.setFillColor(47, 111, 237);
          doc.rect(x + 1, ty - 2.6, 0.9, lineH * 2.6, 'F');

          doc.setFont('helvetica', 'bold');
          doc.text(this.trimText(doc, this.timeLabel(a), colW - 6), x + 3.5, ty);
          doc.setFont('helvetica', 'normal');
          doc.text(this.trimText(doc, String(a['title'] || 'Einsatz'), colW - 6), x + 3.5, ty + lineH);
          doc.setTextColor(110);
          doc.text(this.trimText(doc, String(a['site_name'] || a['project_name'] || ''), colW - 6), x + 3.5, ty + lineH * 2);
          doc.setTextColor(0);
          ty += lineH * 3;
        });
      });

      // Trennlinien
      doc.setDrawColor(226, 232, 240); doc.setLineWidth(0.2);
      doc.line(margin, y + rowH, pageW - margin, y + rowH);
      days.forEach((_, i) => {
        const x = margin + nameW + i * colW;
        doc.line(x, y, x, y + rowH);
      });
      y += rowH;
    }

    // Fusszeile auf allen Seiten
    const pages = doc.getNumberOfPages();
    for (let i = 1; i <= pages; i++) {
      doc.setPage(i);
      doc.setFontSize(7); doc.setTextColor(150);
      doc.text('EstrichWeb · Arbeitsplan', margin, pageH - 5);
      doc.text(`Seite ${i} von ${pages}`, pageW - margin, pageH - 5, { align: 'right' });
    }

    doc.save(`Arbeitsplan_KW${this.isoWeek()}_${this.days()[0]?.iso || ''}.pdf`);
  }

  /** Text auf die verfuegbare Breite kuerzen, damit nichts ueberlaeuft. */
  private trimText(doc: jsPDF, text: string, maxWidth: number): string {
    let value = String(text ?? '').trim();
    if (!value) return '';
    while (value.length > 1 && doc.getTextWidth(value) > maxWidth) value = value.slice(0, -1);
    return doc.getTextWidth(String(text)) > maxWidth ? value.slice(0, -1) + '…' : value;
  }
}
