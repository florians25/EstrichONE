import { Component, Inject, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { NgClass } from '@angular/common';
import { GenericRecord } from '../../core/api.service';

export interface RoomDialogData {
  room: GenericRecord | null;
  siteId: string;
  screedTypes: GenericRecord[];
  states: { key: string; label: string }[];
}

/**
 * Raum einer Baustelle erfassen.
 *
 * Bewusst knapp gehalten: Name, Geschoss, Fläche, Estrichart, Dicke,
 * Heizestrich, Stand, Bemerkung. Das ist genau das, was für Materialmenge,
 * Aufmaß und Fortschritt gebraucht wird – mehr Felder würden die Erfassung
 * auf der Baustelle nur ausbremsen.
 *
 * Die Fläche lässt sich alternativ aus Länge × Breite rechnen; das spart den
 * Taschenrechner beim Aufmaß vor Ort.
 */
@Component({
  standalone: true,
  imports: [NgClass, FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule],
  template: `
    <h2 mat-dialog-title class="rd-title">
      <mat-icon>meeting_room</mat-icon>{{ data.room ? 'Raum bearbeiten' : 'Raum anlegen' }}
    </h2>

    <mat-dialog-content class="rd-body">
      <!-- Aufbau in vier Schritten, jeweils volle Breite.
           Vorher lag alles auf einem 12-spaltigen Raster – bei schmalen
           Dialogen ragten die Einheiten (m², mm) über den Rand hinaus. -->

      <!-- 1. Raum -->
      <div class="rd-pair">
        <mat-form-field appearance="outline">
          <mat-label>Raum *</mat-label>
          <input matInput [(ngModel)]="name" placeholder="z. B. Wohnzimmer">
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Geschoss</mat-label>
          <input matInput [(ngModel)]="floor" placeholder="EG, OG, KG">
        </mat-form-field>
      </div>

      <!-- 2. Fläche -->
      <section class="rd-sec">
        <h3><mat-icon>square_foot</mat-icon>Fläche</h3>
        <mat-form-field appearance="outline" class="rd-full">
          <mat-label>Fläche in m²</mat-label>
          <input matInput type="number" inputmode="decimal" step="0.1" [(ngModel)]="area">
        </mat-form-field>

        <button type="button" class="rd-toggle-calc" (click)="showCalc.set(!showCalc())">
          <mat-icon>{{ showCalc() ? 'expand_less' : 'calculate' }}</mat-icon>
          {{ showCalc() ? 'Rechenhilfe ausblenden' : 'Aus Länge × Breite berechnen' }}
        </button>

        @if (showCalc()) {
          <div class="rd-pair rd-calc">
            <mat-form-field appearance="outline">
              <mat-label>Länge in m</mat-label>
              <input matInput type="number" inputmode="decimal" step="0.01" [(ngModel)]="len" (ngModelChange)="calcArea()">
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Breite in m</mat-label>
              <input matInput type="number" inputmode="decimal" step="0.01" [(ngModel)]="wid" (ngModelChange)="calcArea()">
            </mat-form-field>
          </div>
          @if (area) { <p class="rd-calc-result">ergibt <b>{{ area }} m²</b></p> }
        }
      </section>

      <!-- 3. Aufbau -->
      <section class="rd-sec">
        <h3><mat-icon>layers</mat-icon>Aufbau</h3>
        <div class="rd-pair">
          <mat-form-field appearance="outline">
            <mat-label>Estrichart</mat-label>
            <mat-select [(ngModel)]="screedType">
              <mat-option value="">nicht festgelegt</mat-option>
              @for (t of data.screedTypes; track t['id']) {
                <mat-option [value]="t['name']">{{ t['name'] }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Dicke in mm</mat-label>
            <input matInput type="number" inputmode="numeric" [(ngModel)]="height">
          </mat-form-field>
        </div>

        <label class="rd-heat">
          <mat-slide-toggle [(ngModel)]="heating"></mat-slide-toggle>
          <span><b>Heizestrich</b><small>Fußbodenheizung im Aufbau</small></span>
        </label>
      </section>

      <!-- 4. Stand -->
      <section class="rd-sec">
        <h3><mat-icon>flag</mat-icon>Stand</h3>
        <div class="rd-states" role="group" aria-label="Stand">
          @for (st of data.states; track st.key) {
            <button type="button" class="rd-state" [class.on]="status === st.key"
                    [ngClass]="'st-' + st.key" (click)="status = st.key">
              <span class="rd-state-dot"></span>{{ st.label }}
            </button>
          }
        </div>

        <mat-form-field appearance="outline" class="rd-full rd-note">
          <mat-label>Bemerkung</mat-label>
          <textarea matInput rows="2" [(ngModel)]="notes"
                    placeholder="z. B. Untergrund uneben, Randdämmstreifen bauseits"></textarea>
        </mat-form-field>
      </section>

      @if (error()) { <p class="rd-error"><mat-icon>error_outline</mat-icon>{{ error() }}</p> }
    </mat-dialog-content>

    <mat-dialog-actions class="rd-actions">
      <button mat-button mat-dialog-close>Abbrechen</button>
      <button mat-flat-button color="primary" (click)="save()"><mat-icon>save</mat-icon>Speichern</button>
    </mat-dialog-actions>
  `,
  styles: [`
    /* Grundsatz: Alles liegt in Blöcken voller Breite oder in Zweiergruppen.
       Kein mehrspaltiges Raster mehr – dort ragten die Einheiten (m², mm)
       bei schmalen Dialogen über den Rand hinaus. Einheiten stehen jetzt in
       der Feldbeschriftung ("Fläche in m²"), das spart die Suffix-Elemente
       ganz und ist auch für Vorlesehilfen eindeutig. */
    .rd-title { display: flex; align-items: center; gap: 9px; font-weight: 600; }
    .rd-title .mat-icon { color: #2457a6 !important; }

    .rd-body {
      padding: 4px 24px 8px !important;
      width: min(560px, 92vw);
      box-sizing: border-box;
      overflow-x: hidden;
    }

    /* Zwei Felder nebeneinander, darunter umbrechend. */
    .rd-pair { display: grid; grid-template-columns: 1fr 1fr; gap: 0 14px; }
    .rd-full { width: 100%; }

    /* Abschnitte mit Überschrift und feiner Linie. */
    .rd-sec { border-top: 1px solid #eef1f5; padding-top: 14px; margin-top: 2px; }
    .rd-sec h3 {
      display: flex; align-items: center; gap: 7px; margin: 0 0 12px;
      font-size: .76rem; font-weight: 700;
      text-transform: uppercase; letter-spacing: .06em; color: #667085;
    }
    .rd-sec h3::after { content: ''; flex: 1 1 auto; height: 1px; background: #eef1f5; }
    .rd-sec h3 .mat-icon {
      font-size: 16px !important; width: 16px !important; height: 16px !important;
      color: #98a2b3 !important;
    }

    /* Rechenhilfe ist eingeklappt – sie wird selten gebraucht und hat den
       Dialog sonst unnötig aufgebläht. */
    .rd-toggle-calc {
      display: inline-flex; align-items: center; gap: 6px;
      border: 0; background: none; cursor: pointer; padding: 0 0 12px;
      font: inherit; font-size: .8rem; font-weight: 600; color: #2457a6;
    }
    .rd-toggle-calc .mat-icon {
      font-size: 17px !important; width: 17px !important; height: 17px !important;
      color: #2457a6 !important;
    }
    .rd-toggle-calc:hover { text-decoration: underline; }
    .rd-calc-result {
      margin: -6px 0 12px; font-size: .82rem; color: #667085;
    }
    .rd-calc-result b { color: #101828; font-variant-numeric: tabular-nums; }

    .rd-heat {
      display: flex; align-items: center; gap: 13px;
      border: 1px solid var(--line, #e2e8f0); border-radius: 10px;
      padding: 12px 14px; cursor: pointer; background: #fbfcfe;
      margin-bottom: 4px;
    }
    .rd-heat:hover { border-color: #aab4c2; }
    .rd-heat > span { display: flex; flex-direction: column; gap: 1px; }
    .rd-heat b { font-size: .88rem; color: #101828; }
    .rd-heat small { font-size: .76rem; color: #667085; }

    /* Stand als Marken – ein Tipp statt Auswahlliste. */
    .rd-states { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
    .rd-state {
      display: inline-flex; align-items: center; gap: 8px;
      border: 1px solid var(--line, #e2e8f0); border-radius: 999px;
      background: #fff; padding: 9px 15px; cursor: pointer;
      font: inherit; font-size: .84rem; color: #475467;
    }
    .rd-state:hover { border-color: #94a3b8; background: #fbfcfe; }
    .rd-state-dot {
      width: 9px; height: 9px; min-width: 9px; aspect-ratio: 1 / 1;
      border-radius: 50%; background: #cbd5e1; flex: none;
    }
    .rd-state.st-prepared .rd-state-dot { background: #60a5fa; }
    .rd-state.st-done .rd-state-dot { background: #15803d; }
    .rd-state.st-checked .rd-state-dot { background: #0f766e; }
    .rd-state.st-defect .rd-state-dot { background: #b42318; }
    .rd-state.on { border-color: #14396f; background: #eef3fb; color: #14396f; font-weight: 600; }

    .rd-note textarea { resize: none; line-height: 1.5; }

    .rd-error { display: flex; align-items: center; gap: 7px; margin: 10px 0 0; color: #b42318; font-size: .85rem; }
    .rd-error .mat-icon { color: #b42318 !important; font-size: 17px !important; width: 17px !important; height: 17px !important; }

    .rd-actions { padding: 8px 20px 14px !important; gap: 8px; justify-content: flex-end; }

    /* Der leere Hilfstextbereich unter jedem Feld kostet je 20px Höhe. */
    .rd-body .mat-mdc-form-field-subscript-wrapper { display: none; }
    .rd-body .mat-mdc-form-field { margin-bottom: 12px; width: 100%; }
    /* Zahlenfelder ohne Pfeilchen – auf der Baustelle tippt man Zahlen. */
    .rd-body input[type=number]::-webkit-outer-spin-button,
    .rd-body input[type=number]::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
    .rd-body input[type=number] { -moz-appearance: textfield; }

    @media (max-width: 620px) {
      .rd-body { width: auto; padding: 4px 16px 8px !important; }
      .rd-pair { grid-template-columns: 1fr; gap: 0; }
      .rd-state { padding: 11px 16px; font-size: .88rem; }
      .rd-actions { padding: 8px 14px calc(14px + env(safe-area-inset-bottom)) !important; }
      .rd-actions button { flex: 1 1 auto; min-height: 48px; }
      .rd-body input, .rd-body textarea { font-size: 16px !important; }
    }
  `]
})
export class RoomDialogComponent {
  private readonly ref = inject(MatDialogRef<RoomDialogComponent>);
  readonly error = signal('');
  /** Rechenhilfe ist eingeklappt – sie wird nur beim Aufmaß gebraucht. */
  readonly showCalc = signal(false);

  name = '';
  floor = '';
  area: number | null = null;
  len: number | null = null;
  wid: number | null = null;
  screedType = '';
  height: number | null = null;
  heating = false;
  status = 'open';
  notes = '';

  constructor(@Inject(MAT_DIALOG_DATA) public data: RoomDialogData) {
    const r = data.room;
    if (!r) return;
    this.name = String(r['name'] || '');
    this.floor = String(r['floor'] || '');
    this.area = r['area_m2'] != null ? Number(r['area_m2']) : null;
    this.screedType = String(r['screed_type'] || '');
    this.height = r['height_mm'] != null ? Number(r['height_mm']) : null;
    this.heating = !!Number(r['heating'] || 0);
    this.status = String(r['status'] || 'open');
    this.notes = String(r['notes'] || '');
  }

  /** Fläche aus Länge und Breite – spart den Taschenrechner beim Aufmaß. */
  calcArea(): void {
    const l = Number(this.len || 0);
    const w = Number(this.wid || 0);
    if (l > 0 && w > 0) this.area = Math.round(l * w * 100) / 100;
  }

  save(): void {
    if (!this.name.trim()) { this.error.set('Bitte einen Raumnamen eingeben.'); return; }
    this.ref.close({
      site_id: this.data.siteId,
      name: this.name.trim(),
      floor: this.floor.trim(),
      area_m2: this.area ?? null,
      screed_type: this.screedType || null,
      height_mm: this.height ?? null,
      heating: this.heating ? 1 : 0,
      status: this.status,
      notes: this.notes.trim()
    } as GenericRecord);
  }
}
