import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { RoomOverview, RoomResponse, RoomStatus, ScreedTypeResponse } from '../../core/api.models';
import { uuidv7 } from '../../core/ids';
import { problemMessage } from '../../core/problem';

interface RoomForm {
  id: string;
  sectionId: string | null;
  name: string;
  floor: string;
  area: number | null;
  screedTypeId: string | null;
  thickness: number | null;
  insulation: number | null;
  heated: boolean;
  status: RoomStatus;
  notes: string;
}

/** Räume und Flächen mit Estrichaufbau, gruppiert nach Bereich, mit Summen. */
@Component({
  selector: 'eo-bauakte-rooms',
  imports: [FormsModule],
  template: `
    @if (error()) { <div class="message error" role="alert">{{ error() }}</div> }
    <div class="summary card">
      <div><span class="label">Fläche gesamt</span><strong>{{ area(overview()?.totalAreaSquareMeters ?? 0) }}</strong></div>
      <div><span class="label">davon beheizt</span><strong>{{ area(overview()?.heatedAreaSquareMeters ?? 0) }}</strong></div>
      <div><span class="label">Räume</span><strong>{{ overview()?.rooms?.length ?? 0 }}</strong></div>
      @if (canEdit()) {
        <div class="actions push">
          <button class="button" type="button" (click)="addSection()">Bereich hinzufügen</button>
          <button class="button primary" type="button" (click)="open(null)">Raum hinzufügen</button>
        </div>
      }
    </div>
    @if (sectionEdit) {
      <form class="card section-form" (ngSubmit)="saveSection()">
        <label for="s-name">{{ sectionEdit.isNew ? 'Neuer Bereich' : 'Bereich umbenennen' }}</label>
        <input id="s-name" name="sectionName" required placeholder="z. B. Haus A, OG, Wohnung 3" [(ngModel)]="sectionEdit.name" />
        <button class="button primary" type="submit" [disabled]="!sectionEdit.name.trim()">Speichern</button>
        <button class="button" type="button" (click)="sectionEdit = null">Abbrechen</button>
      </form>
    }
    @for (group of groups(); track group.id) {
      <section>
        <div class="group-head">
          <h3>{{ group.name }}</h3>
          @if (group.id && canEdit()) {
            <button class="button small" type="button" (click)="renameSection(group.id, group.name)">Umbenennen</button>
            <button class="button small danger" type="button" (click)="removeSection(group.id)">Bereich entfernen</button>
          }
        </div>
        <table class="table">
          <thead><tr><th scope="col">Raum</th><th scope="col">Geschoss</th><th scope="col" class="numeric">Fläche</th><th scope="col">Estrich</th>
            <th scope="col" class="numeric">Dicke</th><th scope="col">Heizung</th><th scope="col">Status</th><th scope="col"><span class="visually-hidden">Aktionen</span></th></tr></thead>
          <tbody>
            @for (r of group.rooms; track r.id) {
              <tr>
                <td>{{ r.name }}</td>
                <td>{{ r.floor ?? '–' }}</td>
                <td class="numeric">{{ r.areaSquareMeters === null ? '–' : area(r.areaSquareMeters) }}</td>
                <td>{{ r.screedTypeName ?? '–' }}</td>
                <td class="numeric">{{ r.thicknessMillimeters === null ? '–' : r.thicknessMillimeters + ' mm' }}</td>
                <td>{{ r.underfloorHeating ? 'Ja' : 'Nein' }}</td>
                <td><span class="status {{ statusTone[r.status] }}">{{ statusText[r.status] }}</span></td>
                <td class="actions">@if (canEdit()) {
                  <button class="button small" type="button" (click)="open(r)">Bearbeiten</button>
                  <button class="button small danger" type="button" (click)="remove(r)">Entfernen</button> }</td>
              </tr>
            } @empty {
              <tr><td colspan="8">Noch keine Räume.</td></tr>
            }
          </tbody>
        </table>
      </section>
    }

    <dialog #dialog aria-labelledby="room-title">
      <h2 id="room-title">{{ editing ? 'Raum bearbeiten' : 'Raum hinzufügen' }}</h2>
      <form (ngSubmit)="save()">
        <div class="row">
          <div class="field grow"><label for="r-name">Raum</label><input id="r-name" name="name" required [(ngModel)]="form.name" /></div>
          <div class="field"><label for="r-floor">Geschoss</label><input id="r-floor" name="floor" placeholder="EG" [(ngModel)]="form.floor" /></div>
        </div>
        @if ((overview()?.sections?.length ?? 0) > 0) {
          <div class="field"><label for="r-section">Bereich</label>
            <select id="r-section" name="section" [(ngModel)]="form.sectionId">
              <option [ngValue]="null">ohne Bereich</option>
              @for (s of overview()!.sections; track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
            </select></div>
        }
        <div class="row">
          <div class="field"><label for="r-area">Fläche (m²)</label><input id="r-area" name="area" type="number" min="0" step="0.01" [(ngModel)]="form.area" /></div>
          <div class="field"><label for="r-status">Status</label>
            <select id="r-status" name="status" [(ngModel)]="form.status">
              <option value="planned">Geplant</option><option value="laid">Eingebracht</option><option value="ready">Belegreif</option>
            </select></div>
        </div>
        <div class="field"><label for="r-type">Estrichart</label>
          <select id="r-type" name="type" [(ngModel)]="form.screedTypeId">
            <option [ngValue]="null">noch offen</option>
            @for (t of types(); track t.id) { <option [ngValue]="t.id">{{ t.name }}{{ t.designation ? ' (' + t.designation + ')' : '' }}</option> }
          </select></div>
        <div class="row">
          <div class="field"><label for="r-thick">Dicke (mm)</label><input id="r-thick" name="thickness" type="number" min="10" max="300" [(ngModel)]="form.thickness" /></div>
          <div class="field"><label for="r-ins">Dämmung (mm)</label><input id="r-ins" name="insulation" type="number" min="0" max="500" [(ngModel)]="form.insulation" /></div>
        </div>
        <label class="check"><input type="checkbox" name="heated" [(ngModel)]="form.heated" /> Fußbodenheizung</label>
        <div class="field"><label for="r-notes">Hinweise</label><textarea id="r-notes" name="notes" rows="2" [(ngModel)]="form.notes"></textarea></div>
        @if (dialogError()) { <div class="message error" role="alert">{{ dialogError() }}</div> }
        <div class="actions">
          <button class="button" type="button" (click)="dialog.close()">Abbrechen</button>
          <button class="button primary" type="submit" [disabled]="!form.name.trim()">Speichern</button>
        </div>
      </form>
    </dialog>
  `,
  styles: `
    .summary { display: flex; gap: 32px; align-items: center; margin-bottom: 16px; flex-wrap: wrap; }
    .summary .label { display: block; font-size: 12px; color: var(--text-secondary); }
    .summary strong { font-size: 20px; font-variant-numeric: tabular-nums; }
    .push { margin-left: auto; }
    .group-head { display: flex; align-items: center; gap: 8px; margin-top: 16px; }
    .group-head h3 { margin: 8px 8px 8px 0; }
    .row { display: flex; gap: 12px; }
    .grow { flex: 1; }
    .check { display: flex; gap: 8px; align-items: center; margin-bottom: 16px; }
    .section-form { display: flex; gap: 8px; align-items: center; margin-bottom: 16px; max-width: 720px; }
    .section-form input { flex: 1; font: inherit; min-height: 36px; padding: 6px 10px; border: 1px solid var(--border-control); border-radius: var(--radius-control); background: var(--surface); color: var(--text-primary); }
  `,
})
export class BauakteRoomsComponent {
  private readonly http = inject(HttpClient);

  readonly bauakteId = input.required<string>();
  readonly canEdit = input(false);
  protected readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly overview = signal<RoomOverview | null>(null);
  protected readonly types = signal<ScreedTypeResponse[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly statusText: Record<RoomStatus, string> = { planned: 'Geplant', laid: 'Eingebracht', ready: 'Belegreif' };
  protected readonly statusTone: Record<RoomStatus, string> = { planned: 'info', laid: 'hint', ready: 'success' };
  protected editing = false;
  protected sectionEdit: { id: string; name: string; isNew: boolean } | null = null;
  protected form: RoomForm = this.empty();

  protected readonly groups = computed(() => {
    const o = this.overview();
    if (!o) {
      return [];
    }
    const groups = o.sections.map((s) => ({ id: s.id as string | null, name: s.name, rooms: o.rooms.filter((r) => r.sectionId === s.id) }));
    const loose = o.rooms.filter((r) => !r.sectionId);
    if (loose.length > 0 || groups.length === 0) {
      groups.push({ id: null, name: groups.length === 0 ? 'Räume' : 'Ohne Bereich', rooms: loose });
    }
    return groups;
  });

  constructor() {
    effect(() => void this.load(this.bauakteId()));
  }

  private empty(): RoomForm {
    return { id: uuidv7(), sectionId: null, name: '', floor: '', area: null, screedTypeId: null, thickness: null, insulation: null, heated: false, status: 'planned', notes: '' };
  }

  private async load(id: string): Promise<void> {
    try {
      const [overview, types] = await Promise.all([
        firstValueFrom(this.http.get<RoomOverview>(`/api/v1/bauakten/${id}/rooms`)),
        firstValueFrom(this.http.get<ScreedTypeResponse[]>('/api/v1/screed-types')),
      ]);
      this.overview.set(overview);
      this.types.set(types.filter((t) => t.isActive));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected area(value: number): string {
    return `${value.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} m²`;
  }

  protected open(room: RoomResponse | null): void {
    this.editing = room !== null;
    this.dialogError.set(null);
    this.form = room
      ? {
          id: room.id, sectionId: room.sectionId, name: room.name, floor: room.floor ?? '', area: room.areaSquareMeters, screedTypeId: room.screedTypeId,
          thickness: room.thicknessMillimeters, insulation: room.insulationMillimeters, heated: room.underfloorHeating, status: room.status,
          notes: room.notes ?? '',
        }
      : this.empty();
    this.dialog().nativeElement.showModal();
  }

  protected async save(): Promise<void> {
    const f = this.form;
    try {
      this.overview.set(await firstValueFrom(this.http.put<RoomOverview>(`/api/v1/bauakten/${this.bauakteId()}/rooms/${f.id}`, {
        id: f.id, sectionId: f.sectionId, name: f.name.trim(), floor: f.floor || null, areaSquareMeters: f.area, screedTypeId: f.screedTypeId,
        thicknessMillimeters: f.thickness, insulationMillimeters: f.insulation, underfloorHeating: f.heated, status: f.status, notes: f.notes || null,
      })));
      this.dialog().nativeElement.close();
    } catch (error) {
      this.dialogError.set(problemMessage(error));
    }
  }

  protected async remove(room: RoomResponse): Promise<void> {
    if (!confirm(`Raum „${room.name}“ entfernen?`)) {
      return;
    }
    await this.run(firstValueFrom(this.http.delete<RoomOverview>(`/api/v1/bauakten/${this.bauakteId()}/rooms/${room.id}`)));
  }

  protected addSection(): void {
    this.sectionEdit = { id: uuidv7(), name: '', isNew: true };
  }

  protected renameSection(id: string, current: string): void {
    this.sectionEdit = { id, name: current, isNew: false };
  }

  protected async saveSection(): Promise<void> {
    const edit = this.sectionEdit;
    if (!edit?.name.trim()) {
      return;
    }
    await this.run(firstValueFrom(this.http.put<RoomOverview>(`/api/v1/bauakten/${this.bauakteId()}/sections/${edit.id}`, { id: edit.id, name: edit.name.trim() })));
    if (!this.error()) {
      this.sectionEdit = null;
    }
  }

  protected async removeSection(id: string): Promise<void> {
    if (confirm('Bereich entfernen? Die Räume bleiben erhalten.')) {
      await this.run(firstValueFrom(this.http.delete<RoomOverview>(`/api/v1/bauakten/${this.bauakteId()}/sections/${id}`)));
    }
  }

  private async run(request: Promise<RoomOverview>): Promise<void> {
    try {
      this.overview.set(await request);
      this.error.set(null);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
