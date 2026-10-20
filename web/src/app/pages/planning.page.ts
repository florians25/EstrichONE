import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AssignmentView, BauakteListItem, BauaktePage, CrewResponse, DirectoryUser, PlanningRange, ResourceResponse } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { uuidv7 } from '../core/ids';
import { problemMessage } from '../core/problem';
import { addDays, isoDay, isoWeek, mondayOf, parseDay } from '../shared/days';

interface AssignmentForm {
  id: string;
  bauakteId: string | null;
  bauakteLabel: string;
  day: string;
  startTime: string;
  activity: string;
  crewId: string | null;
  userIds: Set<string>;
  resourceIds: Set<string>;
  notes: string;
}

const absenceNames: Record<string, string> = { vacation: 'Urlaub', sick: 'krank', training: 'Schulung', other: 'abwesend' };

/** Wochenplan (AP-19): Zeilen = Kolonnen, Spalten = Tage; Konflikte werden angezeigt, nicht verhindert. */
@Component({
  selector: 'eo-planning',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="title">
      <h1>Planung</h1>
      <div class="actions">
        <button class="button" type="button" (click)="shift(-7)" aria-label="Vorherige Woche">‹</button>
        <button class="button" type="button" (click)="week.set(mondayOf(today))">Heute</button>
        <button class="button" type="button" (click)="shift(7)" aria-label="Nächste Woche">›</button>
        <span class="week">KW {{ weekNumber() }} · {{ label(week()) }} – {{ label(days()[5]) }}</span>
      </div>
    </div>
    @if (error()) { <div class="message error" role="alert">{{ error() }}</div> }
    <div class="grid" role="table" aria-label="Wochenplan">
      <div class="row head" role="row">
        <div class="cell lane" role="columnheader">Kolonne</div>
        @for (d of days(); track d) {
          <div class="cell" role="columnheader" [class.today]="d === today">{{ weekday(d) }}<br /><small>{{ label(d) }}</small></div>
        }
      </div>
      <div class="row absences" role="row">
        <div class="cell lane" role="rowheader">Abwesend</div>
        @for (d of days(); track d) {
          <div class="cell" role="cell">
            @for (a of absentOn(d); track a.id) { <span class="absence">{{ a.userName }} · {{ absenceName(a.kind) }}</span> }
          </div>
        }
      </div>
      @for (lane of lanes(); track lane.id) {
        <div class="row" role="row">
          <div class="cell lane" role="rowheader">
            @if (lane.color) { <span class="swatch" [style.background]="lane.color" aria-hidden="true"></span> }
            {{ lane.name }}
          </div>
          @for (d of days(); track d) {
            <div class="cell" role="cell" [class.today]="d === today">
              @for (a of assignmentsFor(lane.id, d); track a.id) {
                <button type="button" class="card-item" [class.warn]="a.warnings.length > 0" (click)="open(a, null, d)"
                  [attr.aria-label]="a.activity + ', ' + a.bauakteTitle + (a.warnings.length ? ', ' + a.warnings.length + ' Hinweise' : '')">
                  <span class="time">{{ a.startTime ? a.startTime.slice(0, 5) : '' }}</span>
                  <strong>{{ a.bauakteNumber ?? '' }} {{ a.bauakteTitle }}</strong>
                  <span>{{ a.activity }}</span>
                  @if (a.resources.length) { <small>{{ resourceNames(a) }}</small> }
                  @if (!lane.id) { <small>{{ peopleNames(a) }}</small> }
                  @if (a.warnings.length) { <small class="warning">⚠ {{ a.warnings[0] }}@if (a.warnings.length > 1) { (+{{ a.warnings.length - 1 }}) }</small> }
                </button>
              }
              @if (canPlan()) {
                <button type="button" class="add" (click)="open(null, lane.id, d)" [attr.aria-label]="'Einsatz planen: ' + lane.name + ', ' + weekday(d) + ' ' + label(d)">+</button>
              }
            </div>
          }
        </div>
      }
    </div>

    <dialog #dialog aria-labelledby="assignment-title">
      <h2 id="assignment-title">{{ editing ? 'Einsatz bearbeiten' : 'Einsatz planen' }}</h2>
      <form (ngSubmit)="save()">
        <div class="field"><label for="a-bauakte">Bauakte</label>
          @if (form.bauakteId) {
            <div class="selected"><span>{{ form.bauakteLabel }}</span>
              <a [routerLink]="['/bauakten', form.bauakteId]" target="_blank">öffnen</a>
              @if (canPlan()) { <button class="button small" type="button" (click)="form.bauakteId = null">Ändern</button> }</div>
          } @else {
            <input id="a-bauakte" name="bauakteSearch" type="search" placeholder="Nummer oder Bauvorhaben" autocomplete="off"
              [ngModel]="bauakteQuery" [ngModelOptions]="{ standalone: true }" (ngModelChange)="searchBauakten($event)" />
            <ul class="results">
              @for (b of bauakteResults(); track b.id) {
                <li><button type="button" (click)="chooseBauakte(b)">{{ b.number ?? 'neu' }} · {{ b.title }} <small>{{ b.city ?? '' }}</small></button></li>
              }
            </ul>
          }
        </div>
        <div class="row2">
          <div class="field"><label for="a-day">Tag</label><input id="a-day" name="day" type="date" required [(ngModel)]="form.day" /></div>
          <div class="field"><label for="a-start">Beginn</label><input id="a-start" name="start" type="time" [(ngModel)]="form.startTime" /></div>
        </div>
        <div class="field"><label for="a-activity">Tätigkeit</label>
          <input id="a-activity" name="activity" required placeholder="z. B. Estrich einbringen EG" [(ngModel)]="form.activity" /></div>
        <div class="field"><label for="a-crew">Kolonne</label>
          <select id="a-crew" name="crew" [(ngModel)]="form.crewId">
            <option [ngValue]="null">keine</option>
            @for (c of activeCrews(); track c.id) { <option [ngValue]="c.id">{{ c.name }}</option> }
          </select></div>
        <fieldset class="field picks"><legend>Weitere Mitarbeiter</legend>
          @for (p of activePeople(); track p.id) {
            <label><input type="checkbox" [checked]="form.userIds.has(p.id)" (change)="toggle(form.userIds, p.id)" /> {{ p.displayName }}</label>
          }
        </fieldset>
        <fieldset class="field picks"><legend>Fahrzeuge und Geräte</legend>
          @for (r of activeResources(); track r.id) {
            <label><input type="checkbox" [checked]="form.resourceIds.has(r.id)" (change)="toggle(form.resourceIds, r.id)" /> {{ r.name }}</label>
          } @empty { <span class="muted">Noch keine Geräte angelegt.</span> }
        </fieldset>
        <div class="field"><label for="a-notes">Hinweise</label><textarea id="a-notes" name="notes" rows="2" [(ngModel)]="form.notes"></textarea></div>
        @if (warnings().length) {
          <div class="message hint" role="status"><ul>@for (w of warnings(); track w) { <li>{{ w }}</li> }</ul></div>
        }
        @if (dialogError()) { <div class="message error" role="alert">{{ dialogError() }}</div> }
        <div class="actions">
          @if (editing && canPlan()) { <button class="button danger" type="button" (click)="remove()">Löschen</button> }
          <button class="button" type="button" (click)="dialog.close()">Schließen</button>
          @if (canPlan()) {
            <button class="button primary" type="submit" [disabled]="!form.bauakteId || !form.activity.trim() || (!form.crewId && form.userIds.size === 0)">Speichern</button>
          }
        </div>
      </form>
    </dialog>
  `,
  styles: `
    .week { margin-left: 8px; font-weight: 600; font-variant-numeric: tabular-nums; }
    .grid { display: grid; border: 1px solid var(--line); border-radius: var(--radius-surface); background: var(--surface-raised); overflow-x: auto; }
    .row { display: grid; grid-template-columns: 160px repeat(6, minmax(150px, 1fr)); border-bottom: 1px solid var(--line); }
    .row:last-child { border-bottom: none; }
    .cell { padding: 6px; border-right: 1px solid var(--line); min-height: 64px; display: flex; flex-direction: column; gap: 4px; }
    .cell:last-child { border-right: none; }
    .head .cell { min-height: 0; font-weight: 600; font-size: 13px; }
    .head small { font-weight: 400; color: var(--text-secondary); }
    .lane { font-weight: 600; display: flex; flex-direction: row; align-items: center; gap: 6px; }
    .today { background: var(--info-bg); }
    .absences .cell { min-height: 0; }
    .absence { font-size: 12px; color: var(--hint); }
    .swatch { width: 10px; height: 10px; border-radius: 3px; flex: none; }
    .card-item { text-align: left; font: inherit; font-size: 13px; display: grid; gap: 1px; padding: 6px 8px; border: 1px solid var(--line); border-left: 3px solid var(--action-primary); border-radius: 6px; background: var(--surface); color: var(--text-primary); cursor: pointer; }
    .card-item.warn { border-left-color: var(--hint); }
    .card-item small { color: var(--text-secondary); }
    .card-item .warning { color: var(--hint); }
    .time { font-variant-numeric: tabular-nums; color: var(--text-secondary); font-size: 12px; }
    .add { align-self: flex-start; border: 1px dashed var(--line); background: none; color: var(--text-secondary); border-radius: 6px; width: 28px; height: 24px; cursor: pointer; }
    .add:hover { color: var(--action-primary); border-color: var(--action-primary); }
    .row2 { display: flex; gap: 12px; }
    .picks { border: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px 16px; flex-direction: row; }
    .picks legend { font-size: 13px; font-weight: 500; color: var(--text-secondary); margin-bottom: 4px; }
    .selected { display: flex; gap: 12px; align-items: center; min-height: 36px; }
    .results { list-style: none; padding: 0; margin: 4px 0 0; }
    .results button { width: 100%; text-align: left; background: none; border: 0; padding: 6px 8px; font: inherit; color: inherit; cursor: pointer; border-radius: 4px; }
    .results button:hover { background: var(--surface-alt); }
    .muted { color: var(--text-secondary); }
  `,
})
export class PlanningPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  protected readonly today = isoDay(new Date());
  protected readonly mondayOf = mondayOf;
  protected readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly week = signal(mondayOf(this.today));
  protected readonly days = computed(() => Array.from({ length: 6 }, (_, i) => addDays(this.week(), i)));
  protected readonly weekNumber = computed(() => isoWeek(this.week()));
  protected readonly range = signal<PlanningRange | null>(null);
  protected readonly crews = signal<CrewResponse[]>([]);
  protected readonly resources = signal<ResourceResponse[]>([]);
  protected readonly people = signal<DirectoryUser[]>([]);
  protected readonly activeCrews = computed(() => this.crews().filter((c) => c.isActive));
  protected readonly activeResources = computed(() => this.resources().filter((r) => r.isActive));
  protected readonly activePeople = computed(() => this.people().filter((p) => p.isActive));
  protected readonly lanes = computed(() => [
    ...this.activeCrews().map((c) => ({ id: c.id as string | null, name: c.name, color: c.color })),
    { id: null, name: 'Einzeln eingeplant', color: null },
  ]);
  protected readonly bauakteResults = signal<BauakteListItem[]>([]);
  protected readonly warnings = signal<string[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly dialogError = signal<string | null>(null);
  protected editing = false;
  protected bauakteQuery = '';
  protected form: AssignmentForm = this.empty(this.today, null);
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    void this.loadMasterData();
    effect(() => void this.loadRange(this.week()));
  }

  protected canPlan(): boolean {
    return this.auth.permissions().has('planning.manage');
  }

  private empty(day: string, crewId: string | null): AssignmentForm {
    return { id: uuidv7(), bauakteId: null, bauakteLabel: '', day, startTime: '07:00', activity: '', crewId, userIds: new Set(), resourceIds: new Set(), notes: '' };
  }

  private async loadMasterData(): Promise<void> {
    try {
      const [crews, resources, people] = await Promise.all([
        firstValueFrom(this.http.get<CrewResponse[]>('/api/v1/planning/crews')),
        firstValueFrom(this.http.get<ResourceResponse[]>('/api/v1/planning/resources')),
        firstValueFrom(this.http.get<DirectoryUser[]>('/api/v1/directory/users')),
      ]);
      this.crews.set(crews);
      this.resources.set(resources);
      this.people.set(people);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private async loadRange(monday: string): Promise<void> {
    try {
      this.range.set(await firstValueFrom(this.http.get<PlanningRange>('/api/v1/planning/range', { params: { from: monday, to: addDays(monday, 6) } })));
      this.error.set(null);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected shift(days: number): void {
    this.week.set(addDays(this.week(), days));
  }

  protected label(day: string): string {
    return parseDay(day).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' });
  }

  protected weekday(day: string): string {
    return parseDay(day).toLocaleDateString('de-DE', { weekday: 'long' });
  }

  protected absenceName(kind: string): string {
    return absenceNames[kind] ?? kind;
  }

  protected absentOn(day: string) {
    return (this.range()?.absences ?? []).filter((a) => a.from <= day && day <= a.to);
  }

  protected assignmentsFor(laneId: string | null, day: string): AssignmentView[] {
    return (this.range()?.assignments ?? []).filter((a) => a.day === day && (a.crewId ?? null) === laneId);
  }

  protected resourceNames(a: AssignmentView): string {
    return a.resources.map((r) => r.name).join(', ');
  }

  protected peopleNames(a: AssignmentView): string {
    return a.people.map((p) => p.name).join(', ');
  }

  protected toggle(set: Set<string>, id: string): void {
    if (set.has(id)) {
      set.delete(id);
    } else {
      set.add(id);
    }
  }

  protected open(assignment: AssignmentView | null, crewId: string | null, day: string): void {
    this.editing = assignment !== null;
    this.dialogError.set(null);
    this.bauakteQuery = '';
    this.bauakteResults.set([]);
    if (assignment) {
      const crewMembers = new Set(this.crews().find((c) => c.id === assignment.crewId)?.memberUserIds ?? []);
      this.form = {
        id: assignment.id, bauakteId: assignment.bauakteId, bauakteLabel: `${assignment.bauakteNumber ?? 'neu'} · ${assignment.bauakteTitle}`,
        day: assignment.day, startTime: assignment.startTime?.slice(0, 5) ?? '', activity: assignment.activity, crewId: assignment.crewId,
        userIds: new Set(assignment.people.map((p) => p.id).filter((id) => !crewMembers.has(id))),
        resourceIds: new Set(assignment.resources.map((r) => r.id)), notes: assignment.notes ?? '',
      };
      this.warnings.set(assignment.warnings);
    } else {
      this.form = this.empty(day, crewId);
      this.warnings.set([]);
    }
    this.dialog().nativeElement.showModal();
  }

  protected searchBauakten(value: string): void {
    this.bauakteQuery = value;
    clearTimeout(this.timer);
    this.timer = setTimeout(async () => {
      if (value.trim().length < 2) {
        this.bauakteResults.set([]);
        return;
      }
      const page = await firstValueFrom(this.http.get<BauaktePage>('/api/v1/bauakten', { params: { search: value.trim(), limit: 8 } }));
      this.bauakteResults.set(page.items);
    }, 200);
  }

  protected chooseBauakte(b: BauakteListItem): void {
    this.form.bauakteId = b.id;
    this.form.bauakteLabel = `${b.number ?? 'neu'} · ${b.title}`;
  }

  protected async save(): Promise<void> {
    const f = this.form;
    try {
      const saved = await firstValueFrom(this.http.put<AssignmentView>(`/api/v1/planning/assignments/${f.id}`, {
        bauakteId: f.bauakteId, day: f.day, startTime: f.startTime ? `${f.startTime}:00` : null, activity: f.activity.trim(), crewId: f.crewId,
        userIds: [...f.userIds], resourceIds: [...f.resourceIds], notes: f.notes || null,
      }));
      await this.loadRange(this.week());
      if (saved.warnings.length > 0) {
        // Hinweise sichtbar lassen; die Planung entscheidet, ob sie so bleibt.
        this.editing = true;
        this.warnings.set(saved.warnings);
      } else {
        this.dialog().nativeElement.close();
      }
    } catch (error) {
      this.dialogError.set(problemMessage(error));
    }
  }

  protected async remove(): Promise<void> {
    if (!confirm('Einsatz löschen?')) {
      return;
    }
    try {
      await firstValueFrom(this.http.delete(`/api/v1/planning/assignments/${this.form.id}`));
      this.dialog().nativeElement.close();
      await this.loadRange(this.week());
    } catch (error) {
      this.dialogError.set(problemMessage(error));
    }
  }
}
