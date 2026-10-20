import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AbsenceKind, AbsenceView, CrewResponse, DirectoryUser, PlanningRange, ResourceKind, ResourceResponse } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { uuidv7 } from '../core/ids';
import { problemMessage } from '../core/problem';
import { addDays, isoDay, parseDay } from '../shared/days';

type Tab = 'crews' | 'resources' | 'absences';

/** Stammdaten der Planung: Kolonnen, Fahrzeuge und Geräte, Abwesenheiten. */
@Component({
  selector: 'eo-resources',
  imports: [FormsModule],
  template: `
    <h1>Ressourcen</h1>
    <div class="tabs" role="tablist" aria-label="Ressourcen">
      <button type="button" role="tab" class="tab" [class.active]="tab() === 'crews'" [attr.aria-selected]="tab() === 'crews'" (click)="tab.set('crews')">Kolonnen</button>
      <button type="button" role="tab" class="tab" [class.active]="tab() === 'resources'" [attr.aria-selected]="tab() === 'resources'" (click)="tab.set('resources')">Fahrzeuge und Geräte</button>
      <button type="button" role="tab" class="tab" [class.active]="tab() === 'absences'" [attr.aria-selected]="tab() === 'absences'" (click)="tab.set('absences')">Abwesenheiten</button>
    </div>
    @if (error()) { <div class="message error" role="alert">{{ error() }}</div> }

    @switch (tab()) {
      @case ('crews') {
        @if (canManage()) {
          <form class="card inline" (ngSubmit)="saveCrew()">
            <input name="crewName" aria-label="Name der Kolonne" placeholder="Name der Kolonne" required [(ngModel)]="crewForm.name" />
            <select name="leader" aria-label="Kolonnenführer" [(ngModel)]="crewForm.leaderUserId">
              <option [ngValue]="null">Kolonnenführer …</option>
              @for (p of activePeople(); track p.id) { <option [ngValue]="p.id">{{ p.displayName }}</option> }
            </select>
            <input name="color" type="color" aria-label="Farbe" [(ngModel)]="crewForm.color" />
            <button class="button primary" type="submit" [disabled]="!crewForm.name.trim()">{{ crewForm.id ? 'Speichern' : 'Kolonne anlegen' }}</button>
            @if (crewForm.id) { <button class="button" type="button" (click)="resetCrew()">Abbrechen</button> }
          </form>
          @if (crewForm.id) {
            <fieldset class="card picks"><legend>Mitglieder von {{ crewForm.name }}</legend>
              @for (p of activePeople(); track p.id) {
                <label><input type="checkbox" [checked]="crewForm.members.has(p.id)" (change)="toggleMember(p.id)" /> {{ p.displayName }}</label>
              }
            </fieldset>
          }
        }
        <table class="table">
          <thead><tr><th scope="col">Kolonne</th><th scope="col">Kolonnenführer</th><th scope="col">Mitglieder</th><th scope="col">Status</th><th scope="col"><span class="visually-hidden">Aktionen</span></th></tr></thead>
          <tbody>
            @for (c of crews(); track c.id) {
              <tr>
                <td>@if (c.color) { <span class="swatch" [style.background]="c.color" aria-hidden="true"></span> }{{ c.name }}</td>
                <td>{{ name(c.leaderUserId) }}</td>
                <td>{{ memberNames(c) }}</td>
                <td>@if (c.isActive) { <span class="status success">Aktiv</span> } @else { <span class="status neutral">Inaktiv</span> }</td>
                <td class="actions">@if (canManage()) {
                  <button class="button small" type="button" (click)="editCrew(c)">Bearbeiten</button>
                  <button class="button small" type="button" (click)="toggleCrewActive(c)">{{ c.isActive ? 'Deaktivieren' : 'Aktivieren' }}</button> }</td>
              </tr>
            } @empty { <tr><td colspan="5">Noch keine Kolonnen.</td></tr> }
          </tbody>
        </table>
      }
      @case ('resources') {
        @if (canManage()) {
          <form class="card inline" (ngSubmit)="saveResource()">
            <select name="kind" aria-label="Art" [(ngModel)]="resourceForm.kind">
              <option value="vehicle">Fahrzeug</option><option value="pump">Pumpe</option><option value="machine">Maschine</option>
            </select>
            <input name="resName" aria-label="Bezeichnung" placeholder="Bezeichnung, z. B. Mixokret 2" required [(ngModel)]="resourceForm.name" />
            <input name="identifier" aria-label="Kennzeichen oder Seriennummer" placeholder="Kennzeichen / Seriennummer" [(ngModel)]="resourceForm.identifier" />
            <button class="button primary" type="submit" [disabled]="!resourceForm.name.trim()">{{ resourceForm.id ? 'Speichern' : 'Hinzufügen' }}</button>
          </form>
        }
        <table class="table">
          <thead><tr><th scope="col">Bezeichnung</th><th scope="col">Art</th><th scope="col">Kennzeichen / Nr.</th><th scope="col">Status</th><th scope="col"><span class="visually-hidden">Aktionen</span></th></tr></thead>
          <tbody>
            @for (r of resources(); track r.id) {
              <tr>
                <td>{{ r.name }}</td><td>{{ kindName[r.kind] }}</td><td>{{ r.identifier ?? '–' }}</td>
                <td>@if (r.isActive) { <span class="status success">Verfügbar</span> } @else { <span class="status neutral">Außer Betrieb</span> }</td>
                <td class="actions">@if (canManage()) {
                  <button class="button small" type="button" (click)="editResource(r)">Bearbeiten</button>
                  <button class="button small" type="button" (click)="toggleResourceActive(r)">{{ r.isActive ? 'Außer Betrieb' : 'Wieder verfügbar' }}</button> }</td>
              </tr>
            } @empty { <tr><td colspan="5">Noch keine Fahrzeuge oder Geräte.</td></tr> }
          </tbody>
        </table>
      }
      @case ('absences') {
        @if (canManage()) {
          <form class="card inline" (ngSubmit)="saveAbsence()">
            <select name="user" aria-label="Mitarbeiter" required [(ngModel)]="absenceForm.userId">
              <option [ngValue]="null">Mitarbeiter …</option>
              @for (p of activePeople(); track p.id) { <option [ngValue]="p.id">{{ p.displayName }}</option> }
            </select>
            <select name="absenceKind" aria-label="Art" [(ngModel)]="absenceForm.kind">
              <option value="vacation">Urlaub</option><option value="sick">Krank</option><option value="training">Schulung</option><option value="other">Sonstiges</option>
            </select>
            <input name="from" type="date" aria-label="Von" required [(ngModel)]="absenceForm.from" />
            <input name="to" type="date" aria-label="Bis" required [(ngModel)]="absenceForm.to" />
            <button class="button primary" type="submit" [disabled]="!absenceForm.userId">Eintragen</button>
          </form>
        }
        <p class="muted">Abwesenheiten der nächsten 60 Tage.</p>
        <table class="table">
          <thead><tr><th scope="col">Mitarbeiter</th><th scope="col">Art</th><th scope="col">Zeitraum</th><th scope="col"><span class="visually-hidden">Aktionen</span></th></tr></thead>
          <tbody>
            @for (a of absences(); track a.id) {
              <tr>
                <td>{{ a.userName }}</td><td>{{ absenceName[a.kind] }}</td><td class="numeric">{{ day(a.from) }} – {{ day(a.to) }}</td>
                <td class="actions">@if (canManage()) { <button class="button small danger" type="button" (click)="removeAbsence(a)">Entfernen</button> }</td>
              </tr>
            } @empty { <tr><td colspan="4">Keine Abwesenheiten eingetragen.</td></tr> }
          </tbody>
        </table>
      }
    }
  `,
  styles: `
    .tabs { display: flex; gap: 4px; flex-wrap: wrap; margin-bottom: 16px; border-bottom: 1px solid var(--line); }
    .tab { background: none; border: 0; border-bottom: 2px solid transparent; padding: 8px 12px; font: inherit; color: var(--text-secondary); cursor: pointer; }
    .tab.active { color: var(--text-primary); border-bottom-color: var(--action-primary); font-weight: 600; }
    .inline { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-bottom: 16px; }
    .inline input, .inline select { font: inherit; min-height: 36px; padding: 6px 10px; border: 1px solid var(--border-control); border-radius: var(--radius-control); background: var(--surface); color: var(--text-primary); }
    .inline input[type='color'] { padding: 2px; width: 44px; }
    .picks { display: flex; flex-wrap: wrap; gap: 6px 16px; margin-bottom: 16px; }
    .swatch { display: inline-block; width: 10px; height: 10px; border-radius: 3px; margin-right: 6px; }
    .muted { color: var(--text-secondary); }
  `,
})
export class ResourcesPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  protected readonly tab = signal<Tab>('crews');
  protected readonly crews = signal<CrewResponse[]>([]);
  protected readonly resources = signal<ResourceResponse[]>([]);
  protected readonly absences = signal<AbsenceView[]>([]);
  protected readonly people = signal<DirectoryUser[]>([]);
  protected readonly activePeople = computed(() => this.people().filter((p) => p.isActive));
  protected readonly error = signal<string | null>(null);
  protected readonly kindName: Record<ResourceKind, string> = { vehicle: 'Fahrzeug', pump: 'Pumpe', machine: 'Maschine' };
  protected readonly absenceName: Record<AbsenceKind, string> = { vacation: 'Urlaub', sick: 'Krank', training: 'Schulung', other: 'Sonstiges' };
  protected crewForm = { id: null as string | null, name: '', leaderUserId: null as string | null, color: '#0062CC', members: new Set<string>() };
  protected resourceForm = { id: null as string | null, kind: 'pump' as ResourceKind, name: '', identifier: '', isActive: true };
  protected absenceForm = { userId: null as string | null, kind: 'vacation' as AbsenceKind, from: isoDay(new Date()), to: isoDay(new Date()) };

  constructor() {
    void this.load();
  }

  protected canManage(): boolean {
    return this.auth.permissions().has('planning.manage');
  }

  private async load(): Promise<void> {
    try {
      const today = isoDay(new Date());
      const [crews, resources, people, range] = await Promise.all([
        firstValueFrom(this.http.get<CrewResponse[]>('/api/v1/planning/crews')),
        firstValueFrom(this.http.get<ResourceResponse[]>('/api/v1/planning/resources')),
        firstValueFrom(this.http.get<DirectoryUser[]>('/api/v1/directory/users')),
        firstValueFrom(this.http.get<PlanningRange>('/api/v1/planning/range', { params: { from: today, to: addDays(today, 60) } })),
      ]);
      this.crews.set(crews);
      this.resources.set(resources);
      this.people.set(people);
      this.absences.set(range.absences);
      this.error.set(null);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected name(id: string | null): string {
    return (id && this.people().find((p) => p.id === id)?.displayName) || '–';
  }

  protected memberNames(c: CrewResponse): string {
    return c.memberUserIds.map((id) => this.name(id)).join(', ') || '–';
  }

  protected day(value: string): string {
    return parseDay(value).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  protected editCrew(c: CrewResponse): void {
    this.crewForm = { id: c.id, name: c.name, leaderUserId: c.leaderUserId, color: c.color ?? '#0062CC', members: new Set(c.memberUserIds) };
  }

  protected resetCrew(): void {
    this.crewForm = { id: null, name: '', leaderUserId: null, color: '#0062CC', members: new Set() };
  }

  protected toggleMember(id: string): void {
    if (this.crewForm.members.has(id)) {
      this.crewForm.members.delete(id);
    } else {
      this.crewForm.members.add(id);
    }
  }

  protected async saveCrew(isActive = true, crew?: CrewResponse): Promise<void> {
    const f = crew
      ? { id: crew.id, name: crew.name, leaderUserId: crew.leaderUserId, color: crew.color ?? '#0062CC', members: new Set(crew.memberUserIds) }
      : this.crewForm;
    const id = f.id ?? uuidv7();
    await this.run(this.http.put(`/api/v1/planning/crews/${id}`, {
      name: f.name.trim(), leaderUserId: f.leaderUserId, memberUserIds: [...f.members], color: f.color, isActive,
      sortOrder: crew?.sortOrder ?? this.crews().findIndex((c) => c.id === id) + 1,
    }));
    if (!crew && !this.error()) {
      // Nach dem Anlegen gleich die Mitglieder auswählen können.
      this.crewForm = { ...f, id };
    }
  }

  protected toggleCrewActive(c: CrewResponse): Promise<void> {
    return this.saveCrew(!c.isActive, c);
  }

  protected editResource(r: ResourceResponse): void {
    this.resourceForm = { id: r.id, kind: r.kind, name: r.name, identifier: r.identifier ?? '', isActive: r.isActive };
  }

  protected async saveResource(): Promise<void> {
    const f = this.resourceForm;
    await this.run(this.http.put(`/api/v1/planning/resources/${f.id ?? uuidv7()}`, {
      kind: f.kind, name: f.name.trim(), identifier: f.identifier || null, isActive: f.isActive,
    }));
    if (!this.error()) {
      this.resourceForm = { id: null, kind: f.kind, name: '', identifier: '', isActive: true };
    }
  }

  protected toggleResourceActive(r: ResourceResponse): Promise<void> {
    return this.run(this.http.put(`/api/v1/planning/resources/${r.id}`, { kind: r.kind, name: r.name, identifier: r.identifier, notes: r.notes, isActive: !r.isActive }));
  }

  protected async saveAbsence(): Promise<void> {
    const f = this.absenceForm;
    await this.run(this.http.put(`/api/v1/planning/absences/${uuidv7()}`, { userId: f.userId, kind: f.kind, from: f.from, to: f.to }));
  }

  protected removeAbsence(a: AbsenceView): Promise<void> {
    return this.run(this.http.delete(`/api/v1/planning/absences/${a.id}`));
  }

  private async run(request: import('rxjs').Observable<unknown>): Promise<void> {
    try {
      await firstValueFrom(request);
      await this.load();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
