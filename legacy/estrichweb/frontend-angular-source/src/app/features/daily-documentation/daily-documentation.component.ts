import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TextFieldModule } from '@angular/cdk/text-field';
import { forkJoin } from 'rxjs';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

interface MediaPreview { name: string; originalSize: number; compressedSize: number; url: string; file: File; type: 'photo' | 'video'; }

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, MatSnackBarModule, MatTooltipModule, TextFieldModule],
  template: `
    <section class="page-head compact-page-title">
      <div class="page-head-with-back">
        @if (backProjectId()) { <button mat-stroked-button class="back-to-bauakte-btn" (click)="backToBauakte()"><mat-icon>arrow_back</mat-icon><span>Zurück zur Bauakte</span></button> }
        <h1>Tagesdokumentation</h1>
      </div>
      <div class="head-actions"><button mat-stroked-button (click)="printSheet()"><mat-icon>picture_as_pdf</mat-icon> PDF</button><button mat-stroked-button (click)="clearMedia()"><mat-icon>delete</mat-icon> Auswahl leeren</button></div>
    </section>

    <section class="daily-grid professional-grid">
      <mat-card class="daily-form-card square-card">
        <form [formGroup]="form" class="dd-form">
          <h3 class="dd-section"><span>1</span>Wo und wann</h3>
          <mat-form-field appearance="outline"><mat-label>Auftrag</mat-label><mat-select formControlName="project_id" panelClass="ew-search-select-panel"><mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Auftrag suchen" [value]="projectSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="projectSearch.set($any($event.target).value)"></mat-option>@for (project of filteredProjects(); track project['id']) { <mat-option [value]="project['id']">{{ project['project_no'] || 'Auftrag' }} · {{ project['name'] }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Baustelle</mat-label><mat-select formControlName="site_id" panelClass="ew-search-select-panel"><mat-option disabled class="select-search-option"><mat-icon>search</mat-icon><input matInput type="search" placeholder="Baustelle suchen" [value]="siteSearch()" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" (input)="siteSearch.set($any($event.target).value)"></mat-option>@for (site of filteredSites(); track site['id']) { <mat-option [value]="site['id']">{{ site['site_no'] || 'Baustelle' }} · {{ site['name'] }} @if (site['city']) { · {{ site['city'] }} }</mat-option> }</mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Datum</mat-label><input matInput type="date" formControlName="report_date"></mat-form-field>
          <mat-form-field appearance="outline" class="dd-weather">
            <mat-label>Wetter</mat-label>
            <input matInput formControlName="weather" placeholder="z. B. bewölkt, 14 °C">
            <button mat-icon-button matSuffix type="button" (click)="fetchWeather()"
                    [disabled]="weatherBusy()" matTooltip="Wetter vom Standort holen" aria-label="Wetter holen">
              <mat-icon>{{ weatherBusy() ? 'hourglass_top' : 'my_location' }}</mat-icon>
            </button>
          </mat-form-field>
          <h3 class="dd-section"><span>2</span>Wer war da</h3>
          <mat-form-field appearance="outline"><mat-label>Kolonne</mat-label><mat-select formControlName="crew_id"><mat-option value="">Keine Kolonne</mat-option>@for (crew of crews(); track crew['id']) { <mat-option [value]="crew['id']">{{ crew['name'] }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Mitarbeiter</mat-label><mat-select formControlName="user_id"><mat-option value="">Kein Mitarbeiter</mat-option>@for (user of users(); track user['id']) { <mat-option [value]="user['id']">{{ user['name'] }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Teamnotiz</mat-label><input matInput formControlName="crew_text" placeholder="z. B. 3 Mitarbeiter, Helfer kommt später"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Arbeitszeit</mat-label><input matInput formControlName="time_text"></mat-form-field>
          <h3 class="dd-section"><span>3</span>Was wurde gemacht</h3>
          <mat-form-field class="wide" appearance="outline"><mat-label>Ausgeführte Arbeiten</mat-label><textarea matInput rows="4" cdkTextareaAutosize cdkAutosizeMinRows="4" cdkAutosizeMaxRows="14" formControlName="work_done" placeholder="Kurz und sachlich – mehrere Zeilen sind üblich"></textarea></mat-form-field>
          <mat-form-field class="wide" appearance="outline"><mat-label>Hinweise / Behinderungen</mat-label><textarea matInput rows="3" cdkTextareaAutosize cdkAutosizeMinRows="3" cdkAutosizeMaxRows="10" formControlName="notes" placeholder="z. B. Zufahrt blockiert, Material verspätet, Vorleistung fehlte"></textarea></mat-form-field>
          <!-- Kundenportal ist derzeit deaktiviert – Freigabefeld ausgeblendet. -->
          <mat-form-field appearance="outline"><mat-label>Originalfoto zusätzlich speichern</mat-label><mat-select formControlName="store_original"><mat-option [value]="true">Ja</mat-option><mat-option [value]="false">Nein</mat-option></mat-select></mat-form-field>
        </form>

        <!-- Die Schnellauswahl ist auf Wunsch wieder entfallen. Das Wetter
             holt weiterhin der Knopf im Wetterfeld vom Standort. -->
        <h3 class="dd-section dd-section-plain"><span>4</span>Fotos</h3>
        <div class="upload-box square-upload-box">
          <input #photoInput type="file" accept="image/*" capture="environment" multiple hidden (change)="onPhotos($event)">
          <input #videoInput type="file" accept="video/mp4,video/webm,video/quicktime" multiple hidden (change)="onVideos($event)">
          @if (canWriteReport) { <button mat-flat-button color="primary" (click)="photoInput.click()"><mat-icon>add_a_photo</mat-icon> Fotos direkt aufnehmen / laden</button> }
          <button mat-stroked-button (click)="videoInput.click()"><mat-icon>videocam</mat-icon> Videos</button>
        </div>

        <div class="storage-hint compact-storage-hint">
          {{ photoCount() }} Fotos · {{ videoCount() }} Videos · {{ totalOriginalMb() }} MB ausgewählt · {{ totalCompressedMb() }} MB Foto-Upload
        </div>

        <div class="step-actions">
          @if (canWriteReport) { <button mat-flat-button color="primary" [disabled]="form.invalid || saving()" (click)="save()"><mat-icon>save</mat-icon> Speichern</button> }
        </div>
      </mat-card>

      <mat-card class="photo-preview-card square-card">
        <mat-card-title>Medien</mat-card-title>
        @if (media().length) {
          <div class="photo-grid media-preview-grid">
            @for (item of media(); track item.url) {
              <figure>
                @if (item.type === 'photo') { <img [src]="item.url" [alt]="item.name"> }
                @else { <video [src]="item.url" controls preload="metadata"></video> }
                <figcaption>{{ item.name }}<br><small>{{ mb(item.originalSize) }} MB</small></figcaption>
              </figure>
            }
          </div>
        } @else { <p class="muted">Keine Medien ausgewählt.</p> }
      </mat-card>
    </section>
  `
})
export class DailyDocumentationComponent {

  private readonly auth = inject(AuthService);
  /** Tagesberichte schreiben setzt das Berichtsrecht voraus. */
  readonly canWriteReport = this.auth.canWrite('reports');  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  private readonly snack = inject(MatSnackBar);

  // ---- Wetter vom Standort -------------------------------------------
  readonly weatherBusy = signal(false);
  /**
   * Wetter automatisch eintragen.
   *
   * Quelle ist Open-Meteo – frei nutzbar, ohne Zugangsschlüssel und ohne
   * Registrierung. Der Standort kommt vom Gerät; wird er verweigert, wird
   * ersatzweise die Adresse der gewählten Baustelle verwendet, sofern
   * Koordinaten hinterlegt sind. Schlägt beides fehl, bleibt das Feld
   * unberührt und es erscheint ein Hinweis – nie eine erfundene Angabe.
   */
  fetchWeather(): void {
    if (!navigator.geolocation) {
      this.snack.open('Dieses Gerät liefert keinen Standort. Bitte das Wetter von Hand eintragen.', 'OK', { duration: 4000 });
      return;
    }
    this.weatherBusy.set(true);
    navigator.geolocation.getCurrentPosition(
      (pos) => this.loadWeatherFor(pos.coords.latitude, pos.coords.longitude),
      () => {
        this.weatherBusy.set(false);
        this.snack.open('Standort nicht freigegeben. Bitte das Wetter von Hand eintragen.', 'OK', { duration: 4000 });
      },
      { timeout: 8000, maximumAge: 600000 }
    );
  }
  private loadWeatherFor(lat: number, lon: number): void {
    const url = `https://api.open-meteo.com/v1/forecast?latitude=${lat.toFixed(3)}&longitude=${lon.toFixed(3)}`
      + '&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&timezone=auto';
    fetch(url)
      .then((r) => r.json())
      .then((data) => {
        const c = data?.current;
        if (!c) throw new Error('keine Daten');
        const text = [
          this.weatherText(Number(c.weather_code)),
          `${Math.round(Number(c.temperature_2m))} °C`,
          c.relative_humidity_2m != null ? `${Math.round(Number(c.relative_humidity_2m))} % rF` : '',
          c.wind_speed_10m != null ? `Wind ${Math.round(Number(c.wind_speed_10m))} km/h` : ''
        ].filter((v) => v.length > 0).join(', ');
        this.form.patchValue({ weather: text });
        this.weatherBusy.set(false);
        this.snack.open('Wetter übernommen.', 'OK', { duration: 2000 });
      })
      .catch(() => {
        this.weatherBusy.set(false);
        this.snack.open('Wetterdienst nicht erreichbar. Bitte von Hand eintragen.', 'OK', { duration: 4000 });
      });
  }
  /** Wettercode nach WMO in Klartext – die für uns wichtigen Lagen. */
  private weatherText(code: number): string {
    if (code === 0) return 'klar';
    if (code <= 2) return 'leicht bewölkt';
    if (code === 3) return 'bedeckt';
    if (code <= 48) return 'Nebel';
    if (code <= 57) return 'Nieselregen';
    if (code <= 67) return 'Regen';
    if (code <= 77) return 'Schnee';
    if (code <= 82) return 'Regenschauer';
    if (code <= 86) return 'Schneeschauer';
    return 'Gewitter';
  }
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly projects = signal<GenericRecord[]>([]);
  readonly sites = signal<GenericRecord[]>([]);
  readonly crews = signal<GenericRecord[]>([]);
  readonly users = signal<GenericRecord[]>([]);
  readonly media = signal<MediaPreview[]>([]);
  readonly saving = signal(false);
  readonly projectSearch = signal('');
  readonly siteSearch = signal('');
  readonly backProjectId = signal('');
  backToBauakte(): void { this.router.navigate(['/auftragsakte'], { queryParams: { project_id: this.backProjectId() } }); }

  readonly form = this.fb.group({
    project_id: ['', Validators.required], site_id: ['', Validators.required], report_date: [new Date().toISOString().slice(0, 10), Validators.required],
    weather: [''], crew_id: [''], user_id: [''], crew_text: [''], time_text: ['07:00–16:00'], work_done: ['', Validators.required], notes: [''], customer_visible: [false], store_original: [false]
  });

  filteredProjects(): GenericRecord[] { return this.filterRecords(this.projects(), this.projectSearch(), ['project_no','name','city','zip']); }
  filteredSites(): GenericRecord[] { const projectId = this.form.value.project_id; const scoped = this.sites().filter((site) => !projectId || site['project_id'] === projectId); return this.filterRecords(scoped, this.siteSearch(), ['site_no','name','address','city','zip']); }
  filterRecords(items: GenericRecord[], query: string, fields: string[]): GenericRecord[] { const needle = this.normalize(query); if (!needle) return items; return items.filter((item) => this.normalize(fields.map((field) => item[field] ?? '').join(' ')).includes(needle)); }
  normalize(value: unknown): string { return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, ''); }

  constructor() {
    const queryProjectId = this.route.snapshot.queryParamMap.get('project_id') || '';
    const querySiteId = this.route.snapshot.queryParamMap.get('site_id') || '';
    this.backProjectId.set(queryProjectId);
    forkJoin({ projects: this.api.list('projects', '', 80), sites: this.api.list('construction_sites', '', 500), crews: this.api.list('crews', '', 200), users: this.api.list('users', '', 250) }).subscribe(({ projects, sites, crews, users }) => {
      this.projects.set(projects.items); this.sites.set(sites.items); this.crews.set(crews.items || []); this.users.set(users.items || []);
      const selectedProject = projects.items.find((project) => project['id'] === queryProjectId) || projects.items[0];
      if (selectedProject) this.form.patchValue({ project_id: selectedProject['id'] });
      const firstSite = sites.items.find((site) => site['id'] === querySiteId) || sites.items.find((site) => site['project_id'] === selectedProject?.['id']) || sites.items[0];
      if (firstSite) this.form.patchValue({ site_id: firstSite['id'] });
    });
    this.form.controls.project_id.valueChanges.subscribe((projectId) => { const first = this.sites().find((site) => site['project_id'] === projectId); if (first) this.form.patchValue({ site_id: first['id'] }, { emitEvent: false }); });
  }

  async onPhotos(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement; const items: MediaPreview[] = [];
    for (const file of Array.from(input.files || [])) { if (!file.type.startsWith('image/')) continue; const converted = await this.compressImage(file, 1600, .72); items.push({ name: file.name.replace(/\.[^.]+$/, '.jpg'), originalSize: file.size, compressedSize: converted.size, file: converted, url: URL.createObjectURL(converted), type: 'photo' }); }
    this.media.update((current) => [...current, ...items].slice(0, 120)); input.value = '';
  }

  onVideos(event: Event): void {
    const input = event.target as HTMLInputElement;
    const videos = Array.from(input.files || []).filter((file) => file.type.startsWith('video/')).map((file) => ({ name: file.name, originalSize: file.size, compressedSize: file.size, file, url: URL.createObjectURL(file), type: 'video' as const }));
    this.media.update((current) => [...current, ...videos].slice(0, 40)); input.value = '';
  }

  async compressImage(file: File, maxSide: number, quality: number): Promise<File> {
    const img = await new Promise<HTMLImageElement>((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = reject; image.src = URL.createObjectURL(file); });
    const ratio = Math.min(1, maxSide / Math.max(img.width, img.height)); const canvas = document.createElement('canvas'); canvas.width = Math.max(1, Math.round(img.width * ratio)); canvas.height = Math.max(1, Math.round(img.height * ratio));
    const ctx = canvas.getContext('2d'); if (!ctx) return file; ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
    const blob = await new Promise<Blob>((resolve) => canvas.toBlob((value) => resolve(value || file), 'image/jpeg', quality)); URL.revokeObjectURL(img.src);
    return new File([blob], file.name.replace(/\.[^.]+$/, '.jpg'), { type: 'image/jpeg', lastModified: Date.now() });
  }

  printSheet(): void { window.print(); }

  save(): void {
    if (this.form.invalid) return; this.saving.set(true);
    const raw = this.form.getRawValue();
    const crewLabel = this.crewName(raw.crew_id || '');
    const userLabel = this.userName(raw.user_id || '');
    const payload = { ...raw, crew_text: [crewLabel, userLabel, raw.crew_text].filter(Boolean).join(' · ') };
    this.api.saveDailyDocumentation(payload, this.media().filter((m) => m.type === 'photo').map((m) => m.file), this.media().filter((m) => m.type === 'video').map((m) => m.file)).subscribe({
      next: () => {
        this.saving.set(false);
        this.clearMedia();
        this.snack.open('Tagesdokumentation gespeichert und in der Bauakte verknüpft', 'OK', { duration: 2800 });
        this.router.navigate(['/auftragsakte'], { queryParams: { project_id: raw.project_id, tab: 'tagesuebersicht' } });
      },
      error: () => { this.saving.set(false); this.snack.open('Speichern fehlgeschlagen.', 'OK', { duration: 4000 }); }
    });
  }

  crewName(id: string): string { return this.crews().find((crew) => crew['id'] === id)?.['name'] || ''; }
  userName(id: string): string { return this.users().find((user) => user['id'] === id)?.['name'] || ''; }

  clearMedia(): void { this.media().forEach((item) => URL.revokeObjectURL(item.url)); this.media.set([]); }
  mb(bytes: number): string { return (bytes / 1024 / 1024).toFixed(2); }
  photoCount(): number { return this.media().filter((m) => m.type === 'photo').length; }
  videoCount(): number { return this.media().filter((m) => m.type === 'video').length; }
  totalOriginalMb(): string { return this.mb(this.media().reduce((sum, p) => sum + p.originalSize, 0)); }
  totalCompressedMb(): string { return this.mb(this.media().filter((m) => m.type === 'photo').reduce((sum, p) => sum + p.compressedSize, 0)); }
}
