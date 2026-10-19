import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { jsPDF } from 'jspdf';
import { MatStepperModule } from '@angular/material/stepper';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { forkJoin } from 'rxjs';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MatStepperModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatChipsModule, MatSnackBarModule, MatProgressSpinnerModule],
  template: `
    <section class="page-head compact-head initial-inspection-head">
      <div>
        <p class="eyebrow">Erstbegehung · direkt mit Bauakte verknüpft</p>
        <h1><mat-icon>fact_check</mat-icon>{{ inspectionRound() >= 2 ? 'Zweitbegehungsprotokoll' : 'Erstbegehungsprotokoll' }}</h1>
        @if (inspectionRound() >= 2) { <p class="second-inspection-hint"><mat-icon>restart_alt</mat-icon>Wiederholte Begehung nach Ablehnung der Erstbegehung.</p> }
      </div>
      <div class="head-actions">
        <a mat-stroked-button [routerLink]="['/auftragsakte']" [queryParams]="{ project_id: contextForm.value.project_id }"><mat-icon>folder_open</mat-icon>Bauakte öffnen</a>
        <button mat-stroked-button (click)="printProtocol()"><mat-icon>picture_as_pdf</mat-icon>PDF erzeugen</button>
      </div>
    </section>

    <div class="wz2">
      <div class="wz2-steps">
        @for (t of stepTitles; track $index; let i = $index) {
          <button type="button" class="wz2-step" [class.active]="activeStep() === i" [class.done]="activeStep() > i" [disabled]="i > activeStep()" (click)="goStep(i)">
            <span class="wz2-step-num">@if (activeStep() > i) { <mat-icon>check</mat-icon> } @else { {{ i + 1 }} }</span>
            <span class="wz2-step-label">{{ t }}</span>
          </button>
        }
      </div>
      <div class="wz2-progress"><span [style.width.%]="((activeStep() + 1) / stepTitles.length) * 100"></span></div>

      <div class="wz2-panel">

        @if (activeStep() === 0) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>folder_open</mat-icon> Bauakte & Prüfer</h2>
            <form [formGroup]="contextForm" class="wz2-grid">
              <mat-form-field appearance="outline"><mat-label>Bauakte</mat-label><mat-select formControlName="project_id" panelClass="ew-search-select-panel"><mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Bauakte suchen" [value]="projectSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="projectSearch.set($any($event.target).value)"></mat-option>@for (project of filteredProjects(); track project['id']) { <mat-option [value]="project['id']">{{ project['project_no'] || 'Bauakte' }} · {{ project['name'] }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Baustelle</mat-label><mat-select formControlName="site_id" panelClass="ew-search-select-panel"><mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Baustelle suchen" [value]="siteSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="siteSearch.set($any($event.target).value)"></mat-option>@for (site of sitesForSelectedProject(); track site['id']) { <mat-option [value]="site['id']">{{ site['site_no'] || 'Baustelle' }} · {{ site['name'] }} @if (site['city']) { · {{ site['city'] }} }</mat-option> }</mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Prüfer</mat-label><mat-select formControlName="inspector_name" panelClass="ew-search-select-panel"><mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Mitarbeiter suchen" [value]="inspectorSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="inspectorSearch.set($any($event.target).value)"></mat-option>@for (user of filteredUsers(); track user['id']) { <mat-option [value]="user['name']">{{ user['name'] }} @if (user['email']) { · {{ user['email'] }} }</mat-option> }</mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Datum</mat-label><input matInput type="date" formControlName="inspection_date"></mat-form-field>
            </form>
            @if (selectedProject(); as project) {
              <div class="wz2-selected linked-bauakte-card"><div class="wz2-selected-icon"><mat-icon>folder_open</mat-icon></div><div class="wz2-selected-info"><strong>{{ project['project_no'] || 'Bauakte' }} · {{ project['name'] }}</strong><small>{{ project['address'] || selectedSite()?.['address'] || 'Adresse offen' }} {{ project['zip'] || selectedSite()?.['zip'] || '' }} {{ project['city'] || selectedSite()?.['city'] || '' }}</small></div></div>
            }
            <div class="wz2-actions">
              <button mat-flat-button color="primary" class="wz2-next" (click)="nextStep()">Weiter <mat-icon>arrow_forward</mat-icon></button>
            </div>
          </div>
        }

        @if (activeStep() === 1) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>checklist</mat-icon> Wesentliche Angaben</h2>
            <form [formGroup]="baseForm" class="wz2-grid">
              <mat-form-field appearance="outline"><mat-label>Estrichart</mat-label><mat-select formControlName="screed_type"><mat-option [value]="''">— bitte wählen —</mat-option>@for (st of screedTypes(); track st['id']) { <mat-option [value]="st['name']">{{ st['name'] }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Fußbodenheizung</mat-label><mat-select formControlName="floor_heating"><mat-option value="ja">Ja</mat-option><mat-option value="nein">Nein</mat-option><mat-option value="unbekannt">Unbekannt</mat-option></mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Genaue Fläche (m²)</mat-label><input matInput type="number" formControlName="exact_area_m2"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Untergrund</mat-label><mat-select formControlName="substrate_status"><mat-option value="ok">In Ordnung</mat-option><mat-option value="nacharbeit">Nacharbeit erforderlich</mat-option><mat-option value="unklar">Unklar</mat-option></mat-select></mat-form-field>
              <div class="wz2-checks wz2-wide"><mat-checkbox formControlName="height_checked">Meterriss / Höhen geprüft</mat-checkbox><mat-checkbox formControlName="access_ok">Baustelle zugänglich</mat-checkbox></div>
              <mat-form-field class="wz2-wide" appearance="outline"><mat-label>Bemerkungen / Mängel</mat-label><textarea matInput rows="4" formControlName="notes"></textarea></mat-form-field>
              <mat-form-field class="wz2-wide" appearance="outline"><mat-label>Notizen zur Baustelle</mat-label><textarea matInput rows="3" formControlName="site_notes"></textarea></mat-form-field>
            </form>
            <div class="wz2-actions">
              <button mat-stroked-button (click)="prevStep()"><mat-icon>arrow_back</mat-icon> Zurück</button>
              <button mat-flat-button color="primary" class="wz2-next" (click)="nextStep()">Weiter <mat-icon>arrow_forward</mat-icon></button>
            </div>
          </div>
        }

        @if (activeStep() === 2) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>thermostat</mat-icon> Messwerte</h2>
            <form [formGroup]="measureForm" class="wz2-grid">
              <mat-form-field appearance="outline"><mat-label>Temperatur °C</mat-label><input matInput type="number" formControlName="temperature_c"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Luftfeuchte %</mat-label><input matInput type="number" formControlName="humidity_percent"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Restfeuchte / CM</mat-label><input matInput type="number" formControlName="cm_percent"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Ebenheit / Höhenhinweis</mat-label><input matInput formControlName="level_note"></mat-form-field>
            </form>
            <div class="wz2-actions">
              <button mat-stroked-button (click)="prevStep()"><mat-icon>arrow_back</mat-icon> Zurück</button>
              <button mat-flat-button color="primary" class="wz2-next" (click)="nextStep()">Weiter <mat-icon>arrow_forward</mat-icon></button>
            </div>
          </div>
        }

        @if (activeStep() === 3) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>photo_camera</mat-icon> Fotos / Videos</h2>
            <section class="direct-capture-panel initial-media-groups">
              <input #meterPhotoInput type="file" hidden multiple accept="image/*" capture="environment" (change)="onMeterPhotos($event)">
              <input #mixingPhotoInput type="file" hidden multiple accept="image/*" capture="environment" (change)="onMixingPhotos($event)">
              <input #mixingVideoInput type="file" hidden multiple accept="video/*" capture="environment" (change)="onMixingVideos($event)">
              <div class="initial-media-row">
                <mat-icon>straighten</mat-icon>
                <div><h3>Meterriss</h3><p>{{ meterPhotos().length }} Foto(s)</p></div>
                <button mat-flat-button color="primary" type="button" (click)="meterPhotoInput.click()"><mat-icon>add_a_photo</mat-icon>Foto aufnehmen</button>
              </div>
              <div class="initial-media-row">
                <mat-icon>engineering</mat-icon>
                <div><h3>Mischplatz</h3><p>{{ mixingPhotos().length }} Foto(s) · {{ mixingVideos().length }} Video(s)</p></div>
                <button mat-stroked-button type="button" (click)="mixingPhotoInput.click()"><mat-icon>photo_camera</mat-icon>Foto</button>
                <button mat-stroked-button type="button" (click)="mixingVideoInput.click()"><mat-icon>videocam</mat-icon>Video</button>
              </div>
              <mat-chip-set class="media-chip-list">
                @for (photo of meterPhotos(); track photo.name; let i = $index) { <mat-chip (removed)="removeMeterPhoto(i)"><mat-icon>image</mat-icon>Meterriss · {{ photo.name }}<button matChipRemove><mat-icon>cancel</mat-icon></button></mat-chip> }
                @for (photo of mixingPhotos(); track photo.name; let i = $index) { <mat-chip (removed)="removeMixingPhoto(i)"><mat-icon>image</mat-icon>Mischplatz · {{ photo.name }}<button matChipRemove><mat-icon>cancel</mat-icon></button></mat-chip> }
                @for (video of mixingVideos(); track video.name) { <mat-chip><mat-icon>movie</mat-icon>Mischplatz · {{ video.name }}</mat-chip> }
              </mat-chip-set>
            </section>
            @if (photoPreviews().length) {
              <div class="photo-gallery-grid material-photo-grid inspection-photo-preview">
                @for (preview of photoPreviews(); track preview.name) { <figure><img [src]="preview.url" [alt]="preview.name"><figcaption>{{ preview.name }}</figcaption></figure> }
              </div>
            }
            <div class="wz2-actions">
              <button mat-stroked-button (click)="prevStep()"><mat-icon>arrow_back</mat-icon> Zurück</button>
              <button mat-flat-button color="primary" class="wz2-next" (click)="nextStep()">Weiter <mat-icon>arrow_forward</mat-icon></button>
            </div>
          </div>
        }

        @if (activeStep() === 4) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>assignment_turned_in</mat-icon> Freigabe</h2>
            <form [formGroup]="outcomeForm">
              <mat-form-field appearance="outline" class="wz2-wide"><mat-label>Begründung bei Ablehnung</mat-label><textarea matInput rows="2" formControlName="rejection_reason" placeholder="z. B. Untergrund nicht tragfähig, Nacharbeit erforderlich"></textarea></mat-form-field>
            </form>
            <div class="wz2-selected">
              <div class="wz2-selected-icon"><mat-icon>assignment_turned_in</mat-icon></div>
              <div class="wz2-selected-info"><strong>Protokoll speichern</strong><small>Bauakte, Baustelle, Prüfdaten, Notizen und Fotodokumentation werden übernommen.</small></div>
            </div>
            <!-- Abschlussleiste: eindeutige Haupt-/Nebenaktion statt gleichrangiger Buttons -->
            <div class="wz2-finalbar">
              @if (!canSaveInspection) { <p class="wz2-no-right"><mat-icon>lock</mat-icon>Ihnen fehlt die Berechtigung, Begehungen zu speichern. Bitte wenden Sie sich an die Administration.</p> }
              @if (saving()) { <div class="wz2-saving"><mat-progress-spinner mode="indeterminate" diameter="22"></mat-progress-spinner><span>Protokoll wird gespeichert …</span></div> }
              <div class="wz2-finalbar-primary">
                <button mat-flat-button color="primary" class="wz2-primary-save" [disabled]="saving() || !canSaveInspection" (click)="save('passed')">
                  <mat-icon>check_circle</mat-icon>
                  <span class="wz2-btn-text"><b>{{ inspectionRound() >= 2 ? 'Zweitbegehung freigeben' : 'Erstbegehung freigeben' }}</b><small>Protokoll speichern und Bauakte aktualisieren</small></span>
                </button>
                <button mat-stroked-button class="wz2-reject-save" [disabled]="saving() || !canSaveInspection" (click)="save('rejected')">
                  <mat-icon>cancel</mat-icon>
                  <span class="wz2-btn-text"><b>Ablehnen</b><small>Mit Begründung speichern</small></span>
                </button>
              </div>
              <div class="wz2-finalbar-secondary">
                <button mat-button (click)="prevStep()"><mat-icon>arrow_back</mat-icon>Zurück</button>
                <button mat-button (click)="printProtocol()"><mat-icon>picture_as_pdf</mat-icon>Vorschau als PDF</button>
              </div>
            </div>
          </div>
        }

      </div>
    </div>
  
`
})
export class InitialInspectionWizardComponent {

  private readonly auth = inject(AuthService);
  /** Erstbegehungen speichern setzt Schreibrecht voraus. */
  readonly canSaveInspection = this.auth.canWrite('inspections') || this.auth.canWrite('projects');  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  private readonly snack = inject(MatSnackBar);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly projects = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly users = signal<GenericRecord[]>([]);
  readonly screedTypes = signal<GenericRecord[]>([]);
  readonly saving = signal(false);
  readonly activeStep = signal(0);
  readonly stepTitles = ['Bauakte', 'Angaben', 'Messwerte', 'Fotos', 'Freigabe'];
  private scrollWizardTop(): void { try { window.scrollTo({ top: 0, behavior: 'smooth' }); } catch { /* ignore */ } }
  nextStep(): void {
    const s = this.activeStep();
    if (s === 0 && this.contextForm.invalid) {
      this.contextForm.markAllAsTouched();
      this.snack.open('Bitte Bauakte und Baustelle auswählen.', 'OK', { duration: 3000 });
      return;
    }
    this.activeStep.set(Math.min(this.stepTitles.length - 1, s + 1));
    this.scrollWizardTop();
  }
  prevStep(): void { this.activeStep.set(Math.max(0, this.activeStep() - 1)); this.scrollWizardTop(); }
  goStep(i: number): void { if (i <= this.activeStep()) { this.activeStep.set(i); this.scrollWizardTop(); } }
  readonly projectSearch = signal('');
  readonly siteSearch = signal('');
  readonly inspectorSearch = signal('');
  readonly meterPhotos = signal<File[]>([]);
  readonly mixingPhotos = signal<File[]>([]);
  readonly mixingVideos = signal<File[]>([]);
  readonly photoPreviews = signal<Array<{ name: string; url: string }>>([]);
  readonly photoRequirements = ['Fotos vom Meterriss', 'Fotos und/oder Video vom Mischplatz'];

  readonly contextForm = this.fb.group({ project_id: ['', Validators.required], site_id: ['', Validators.required], inspector_name: [''], inspection_date: [new Date().toISOString().slice(0, 10), Validators.required] });
  readonly baseForm = this.fb.group({ screed_type: [''], floor_heating: ['unbekannt'], exact_area_m2: [''], substrate_status: ['ok'], height_checked: [false], access_ok: [true], notes: [''], site_notes: [''] });
  readonly measureForm = this.fb.group({ temperature_c: [''], humidity_percent: [''], cm_percent: [''], level_note: [''], exact_area_m2: [''] });
  readonly outcomeForm = this.fb.group({ rejection_reason: [''] });
  readonly exportForm = this.fb.group({ include_context: [true], include_checklist: [true], include_measurements: [true], include_notes: [true], include_photos: [true], include_videos: [true] });

  readonly companyName = signal('EstrichWeb');
  readonly inspectionRound = signal(1);
  constructor() {
    this.api.appSettings().subscribe({ next: (res) => { const n = res?.['company']?.['name']; if (n) this.companyName.set(String(n)); }, error: () => {} });
    const queryProject = this.route.snapshot.queryParamMap.get('project_id') || this.route.snapshot.queryParamMap.get('project') || '';
    const querySite = this.route.snapshot.queryParamMap.get('site_id') || '';
    const secondParam = this.route.snapshot.queryParamMap.get('second') || '';
    if (secondParam) this.inspectionRound.set(2);
    const inspDateParam = this.route.snapshot.queryParamMap.get('inspection_date') || '';
    if (inspDateParam) this.contextForm.patchValue({ inspection_date: inspDateParam });
    forkJoin({ projects: this.api.list('projects', '', 500), sites: this.api.list('construction_sites', '', 1000), users: this.api.list('users', '', 500), screedTypes: this.api.list('screed_types', '', 250) }).subscribe(({ projects, sites, users, screedTypes }) => {
      this.projects.set(projects.items); this.sites.set(sites.items); this.users.set(users.items || []);
      const st = screedTypes.items || [];
      this.screedTypes.set(st.length ? st : this.defaultScreedTypes());
      const selectedProject = projects.items.find((p) => p['id'] === queryProject) || projects.items[0];
      if (selectedProject) this.contextForm.patchValue({ project_id: selectedProject['id'] });
      const firstSite = sites.items.find((site) => site['id'] === querySite) || sites.items.find((site) => site['project_id'] === selectedProject?.['id']) || sites.items[0];
      if (firstSite) { this.contextForm.patchValue({ site_id: firstSite['id'] }); this.baseForm.patchValue({ exact_area_m2: String(firstSite['area_m2'] || '') }); this.measureForm.patchValue({ exact_area_m2: String(firstSite['area_m2'] || '') }); }
      if (users.items?.[0] && !this.contextForm.value.inspector_name) this.contextForm.patchValue({ inspector_name: users.items[0]['name'] || '' });
    });
    this.contextForm.controls.site_id.valueChanges.subscribe((siteId) => { const site=this.sites().find((entry)=>entry['id']===siteId); if (site) { this.baseForm.patchValue({ exact_area_m2: String(site['area_m2'] || '') }); this.measureForm.patchValue({ exact_area_m2: String(site['area_m2'] || '') }); } });
    this.contextForm.controls.project_id.valueChanges.subscribe((projectId) => {
      const site = this.sites().find((entry) => entry['project_id'] === projectId);
      if (site) this.contextForm.patchValue({ site_id: site['id'] }, { emitEvent: false });
    });
  }

  selectedProject(): GenericRecord | null { return this.projects().find((p) => p['id'] === this.contextForm.value.project_id) || null; }
  selectedSite(): GenericRecord | null { return this.sites().find((s) => s['id'] === this.contextForm.value.site_id) || null; }
  private defaultScreedTypes(): GenericRecord[] {
    // Die 5 häufigsten Estricharten (DIN EN 13813) als Fallback, falls noch keine hinterlegt sind.
    return [
      { id: 'ct', name: 'Zementestrich (CT)' },
      { id: 'ca', name: 'Calciumsulfatestrich (CA)' },
      { id: 'caf', name: 'Calciumsulfat-Fließestrich (CAF)' },
      { id: 'as', name: 'Gussasphaltestrich (AS)' },
      { id: 'sr', name: 'Kunstharzestrich (SR)' }
    ];
  }
  filteredProjects(): GenericRecord[] { return this.filterRecords(this.projects(), this.projectSearch(), ['project_no','name','city','zip']); }
  sitesForSelectedProject(): GenericRecord[] { const projectId = this.contextForm.value.project_id; const scoped = this.sites().filter((site) => !projectId || site['project_id'] === projectId); return this.filterRecords(scoped, this.siteSearch(), ['site_no','name','address','city','zip']); }
  filteredUsers(): GenericRecord[] { return this.filterRecords(this.users(), this.inspectorSearch(), ['name','email','role_hint']); }
  filterRecords(items: GenericRecord[], query: string, fields: string[]): GenericRecord[] { const needle = this.normalize(query); if (!needle) return items; return items.filter((item) => this.normalize(fields.map((field) => item[field] ?? '').join(' ')).includes(needle)); }
  normalize(value: unknown): string { return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }

  onMeterPhotos(event: Event): void { this.setPhotoGroup(event, this.meterPhotos); }
  onMixingPhotos(event: Event): void { this.setPhotoGroup(event, this.mixingPhotos); }
  onMixingVideos(event: Event): void { this.mixingVideos.set(Array.from((event.target as HTMLInputElement).files || [])); }

  private setPhotoGroup(event: Event, target: { update(fn: (v: File[]) => File[]): void }): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files || []);
    if (files.length) {
      // Fotos anhängen statt ersetzen (mehrere Aufnahmen nacheinander möglich), Duplikate vermeiden
      target.update((existing) => {
        const seen = new Set(existing.map((f) => `${f.name}:${f.size}`));
        const added = files.filter((f) => !seen.has(`${f.name}:${f.size}`));
        return [...existing, ...added];
      });
    }
    input.value = '';
    this.refreshPhotoPreviews();
  }
  removeMeterPhoto(index: number): void { this.meterPhotos.update((l) => l.filter((_, i) => i !== index)); this.refreshPhotoPreviews(); }
  removeMixingPhoto(index: number): void { this.mixingPhotos.update((l) => l.filter((_, i) => i !== index)); this.refreshPhotoPreviews(); }
  allPhotos(): File[] { return [...this.meterPhotos(), ...this.mixingPhotos()]; }
  allVideos(): File[] { return [...this.mixingVideos()]; }
  refreshPhotoPreviews(): void {
    this.photoPreviews().forEach((item) => URL.revokeObjectURL(item.url));
    const previews = [
      ...this.meterPhotos().map((file) => ({ name: 'Meterriss · ' + file.name, url: URL.createObjectURL(file) })),
      ...this.mixingPhotos().map((file) => ({ name: 'Mischplatz · ' + file.name, url: URL.createObjectURL(file) }))
    ];
    this.photoPreviews.set(previews);
  }

  async printProtocol(): Promise<void> {
    const doc = new jsPDF({ unit: 'pt', format: 'a4' });
    const project = this.selectedProject();
    const site = this.selectedSite();
    const context = this.contextForm.getRawValue();
    const base = this.baseForm.getRawValue();
    const measure = this.measureForm.getRawValue();
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();
    const M = 34;
    const W = pageW - M * 2;
    let y = 36;
    let page = 1;
    const now = new Date();
    const clean = (value: unknown, fallback = '—') => {
      const raw = String(value ?? '').trim();
      return raw && raw !== 'null' && raw !== 'undefined' ? raw : fallback;
    };
    const footer = () => {
      doc.setDrawColor(110); doc.setLineWidth(0.35); doc.line(M, pageH - 30, pageW - M, pageH - 30);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); doc.setTextColor(70);
      doc.text(this.companyName() + ' · Erstbegehungsprotokoll', M, pageH - 14);
      doc.text(`Seite ${page}`, pageW - M, pageH - 14, { align: 'right' });
      doc.setTextColor(20);
    };
    const newPage = () => { footer(); doc.addPage(); page += 1; y = 36; };
    const ensure = (h: number) => { if (y + h > pageH - 46) newPage(); };
    const section = (title: string, subtitle = '', minBody = 60) => {
      ensure(46 + minBody);
      doc.setDrawColor(18); doc.setLineWidth(1.1); doc.rect(M, y, W, 38);
      doc.setFont('helvetica', 'bold'); doc.setFontSize(12); doc.text(title.toUpperCase(), M + 9, y + 16);
      if (subtitle) { doc.setFont('helvetica', 'normal'); doc.setFontSize(7.8); doc.text(subtitle, M + 9, y + 30); }
      y += 48;
    };
    const row = (items: Array<[string, unknown]>, h = 34) => {
      ensure(h + 2);
      const width = W / items.length;
      items.forEach(([label, value], i) => {
        const x = M + i * width;
        doc.setDrawColor(86); doc.setLineWidth(0.35); doc.rect(x, y, width, h);
        doc.setFont('helvetica', 'normal'); doc.setFontSize(6.8); doc.setTextColor(82); doc.text(label, x + 5, y + 9);
        doc.setFont('helvetica', 'bold'); doc.setFontSize(9.2); doc.setTextColor(12);
        doc.text(doc.splitTextToSize(clean(value), width - 10).slice(0, 2) as string[], x + 5, y + 22);
        doc.setTextColor(20);
      });
      y += h;
    };
    const memo = (label: string, value: unknown, h = 58) => {
      ensure(h + 4);
      doc.setDrawColor(86); doc.setLineWidth(0.35); doc.rect(M, y, W, h);
      doc.setFont('helvetica', 'bold'); doc.setFontSize(8.2); doc.text(label, M + 6, y + 12);
      doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5);
      doc.text(doc.splitTextToSize(clean(value), W - 14) as string[], M + 6, y + 27);
      y += h;
    };

    doc.setDrawColor(18); doc.setLineWidth(1.6); doc.rect(M, y, W, 90);
    doc.setFillColor(238, 238, 238); doc.rect(M, y, W, 28, 'F');
    doc.setFont('helvetica', 'bold'); doc.setFontSize(10); doc.text(this.companyName().toUpperCase(), M + 8, y + 18);
    doc.setFont('helvetica', 'bold'); doc.setFontSize(18); doc.text('ERSTBEGEHUNGSPROTOKOLL', M + 8, y + 56);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5); doc.text('Prüfung vor Ausführung · digitales Bauaktenformular', M + 8, y + 74);
    doc.setFont('helvetica', 'bold'); doc.setFontSize(8.5); doc.text(`Erstellt am ${now.toLocaleDateString('de-DE')}`, pageW - M - 8, y + 18, { align: 'right' });
    y += 108;

    section('I. Bauakte und Baustelle', 'Zuordnung der Erstbegehung', 112);
    row([['Bauakten-Nr.', project?.['project_no']], ['Bezeichnung', project?.['name']]], 36);
    row([['Baustelle', `${site?.['site_no'] || ''} ${site?.['name'] || ''}`.trim()], ['Kunde', project?.['customer_name'] || '—']], 36);
    row([['Adresse', site?.['address']], ['PLZ / Ort', [site?.['zip'], site?.['city']].filter(Boolean).join(' ')]], 36);
    row([['Datum', context.inspection_date], ['Prüfer', context.inspector_name]], 36);

    section('II. Wesentliche Angaben', 'Neues Kurzabfragekonzept', 134);
    row([['Estrichart', base.screed_type], ['Fußbodenheizung', this.yesNoSelect(base.floor_heating)], ['Genaue Fläche', base.exact_area_m2 ? `${base.exact_area_m2} m²` : '—']], 36);
    row([['Untergrund', this.substrateLabel(base.substrate_status)], ['Meterriss / Höhen geprüft', base.height_checked ? 'Ja' : 'Nein'], ['Baustelle zugänglich', base.access_ok ? 'Ja' : 'Nein']], 36);
    row([['Temperatur', measure.temperature_c ? `${measure.temperature_c} °C` : '—'], ['Luftfeuchte', measure.humidity_percent ? `${measure.humidity_percent} %` : '—'], ['CM / Feuchte', measure.cm_percent]], 36);
    memo('Bemerkungen / Mängel', base.notes, 52);
    memo('Notizen zur Baustelle', base.site_notes, 52);

    section('III. Medien / Anlagen', 'Erfasste Bilder und Videos zur Erstbegehung', 66);
    row([['Fotos Meterriss', `${this.meterPhotos().length}`], ['Fotos Mischplatz', `${this.mixingPhotos().length}`], ['Videos Mischplatz', `${this.mixingVideos().length}`]], 32);
    if (this.mixingVideos().length) memo('Videodateien', this.mixingVideos().map((file) => file.name).join(', '), 44);

    section('IV. Freigabe', 'Bestätigung der Angaben', 76);
    row([['Ergebnis', 'Bestanden / freigegeben'], ['Prüfer', context.inspector_name]], 34);
    ensure(72);
    y += 16;
    doc.setDrawColor(55); doc.line(M, y + 34, M + 210, y + 34); doc.line(pageW - M - 210, y + 34, pageW - M, y + 34);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(8); doc.text('Ort, Datum', M, y + 50); doc.text('Unterschrift Prüfer / Bauleitung', pageW - M - 210, y + 50);
    y += 76;

    if (this.allPhotos().length) {
      newPage();
      section('V. Fotodokumentation', 'Fotos werden ausschließlich in diesem Abschnitt ausgegeben', 240);
      const imgW = (W - 14) / 2;
      let col = 0;
      for (const file of this.allPhotos().slice(0, 24)) {
        if (col === 0) ensure(208);
        const x = M + col * (imgW + 14);
        const dataUrl = await this.fileToDataUrl(file);
        doc.setDrawColor(45); doc.rect(x, y, imgW, 168);
        try { doc.addImage(dataUrl, file.type.includes('png') ? 'PNG' : 'JPEG', x + 5, y + 5, imgW - 10, 158, undefined, 'SLOW'); }
        catch { doc.setFont('helvetica','normal'); doc.setFontSize(8); doc.text('Bild konnte nicht eingebettet werden.', x + 8, y + 24); }
        doc.setFont('helvetica','normal'); doc.setFontSize(7.2); doc.text(doc.splitTextToSize(file.name, imgW) as string[], x, y + 181);
        col += 1;
        if (col === 2) { col = 0; y += 205; }
      }
    }
    footer();
    doc.save(`Erstbegehung_${String(project?.['project_no'] || project?.['name'] || 'Bauakte').replace(/[^\w.-]+/g, '_')}.pdf`);
  }

  yesNoSelect(value: unknown): string { const raw = String(value ?? '').toLowerCase(); if (raw === 'ja') return 'Ja'; if (raw === 'nein') return 'Nein'; return 'Unbekannt'; }
  substrateLabel(value: unknown): string { return ({ ok: 'In Ordnung', nacharbeit: 'Nacharbeit erforderlich', unklar: 'Unklar' } as Record<string, string>)[String(value || '')] || String(value || '—'); }
  fileToDataUrl(file: File): Promise<string> { return new Promise((resolve, reject) => { const r = new FileReader(); r.onload = () => resolve(String(r.result)); r.onerror = reject; r.readAsDataURL(file); }); }

  async save(result: 'passed' | 'rejected' = 'passed'): Promise<void> {
    if (this.contextForm.invalid) { this.snack.open('Bitte Bauakte und Baustelle wählen', 'OK', { duration: 3000 }); return; }
    const reason = String(this.outcomeForm.value.rejection_reason || '').trim();
    if (result === 'rejected' && !reason) { this.snack.open('Bitte einen Ablehnungsgrund angeben', 'OK', { duration: 3000 }); return; }
    this.saving.set(true);
    const payload = { context: this.contextForm.getRawValue(), checklist: this.baseForm.getRawValue(), measurements: { ...this.measureForm.getRawValue(), exact_area_m2: this.baseForm.value.exact_area_m2 || this.measureForm.value.exact_area_m2 }, photo_requirements: this.photoRequirements, media_groups: { meter_photos: this.meterPhotos().map((f) => f.name), mixing_place_photos: this.mixingPhotos().map((f) => f.name), mixing_place_videos: this.mixingVideos().map((f) => f.name) }, export_options: this.exportForm.getRawValue(), result, rejection_reason: reason, inspection_round: this.inspectionRound(), is_second_inspection: this.inspectionRound() >= 2 };
    const compressedPhotos = await Promise.all(this.allPhotos().map((file) => this.compressImageFile(file)));
    this.api.saveInitialInspection(payload, compressedPhotos, this.allVideos()).subscribe({
      next: () => { this.updateSiteAreaFromInspection(); this.saving.set(false); this.snack.open(result === 'passed' ? 'Erstbegehung gespeichert und mit Bauakte verknüpft' : 'Erstbegehung als abgelehnt gespeichert', 'OK', { duration: 3000 }); this.router.navigate(['/auftragsakte'], { queryParams: { project_id: this.contextForm.value.project_id, tab: 'erstbegehung' } }); },
      error: () => { this.saving.set(false); this.snack.open('Speichern fehlgeschlagen. Bitte Login/API prüfen.', 'OK', { duration: 4000 }); }
    });
  }
  private async compressImageFile(file: File): Promise<File> {
    if (!file.type.startsWith('image/')) return file;
    const dataUrl = await this.fileToDataUrl(file);
    const img = await new Promise<HTMLImageElement>((resolve, reject) => {
      const image = new Image();
      image.onload = () => resolve(image);
      image.onerror = reject;
      image.src = dataUrl;
    });
    const maxSide = 1800;
    const scale = Math.min(1, maxSide / Math.max(img.width, img.height));
    if (scale >= 1 && file.size < 1_800_000) return file;
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(img.width * scale));
    canvas.height = Math.max(1, Math.round(img.height * scale));
    const ctx = canvas.getContext('2d');
    if (!ctx) return file;
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.82));
    if (!blob) return file;
    const name = file.name.replace(/\.(heic|heif|png|webp|jpg|jpeg)$/i, '') + '.jpg';
    return new File([blob], name, { type: 'image/jpeg', lastModified: Date.now() });
  }

  updateSiteAreaFromInspection(): void {
    const siteId = this.contextForm.value.site_id;
    const exact = this.baseForm.value.exact_area_m2 || this.measureForm.value.exact_area_m2;
    if (!siteId || exact === null || exact === undefined || String(exact).trim() === '') return;
    this.api.update('construction_sites', String(siteId), { area_m2: exact, screed_type: this.baseForm.value.screed_type || undefined, hinweise: this.baseForm.value.site_notes || undefined }).subscribe({ next: () => {}, error: () => {} });
  }

}
