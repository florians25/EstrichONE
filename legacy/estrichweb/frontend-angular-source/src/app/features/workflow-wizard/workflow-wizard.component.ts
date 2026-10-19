import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormsModule, FormArray, FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatStepperModule } from '@angular/material/stepper';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { OfflineService } from '../../core/offline.service';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, RouterLink, MatStepperModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, MatSnackBarModule, MatProgressBarModule, MatTooltipModule],
  template: `
    <section class="page-head compact-head work-head">
      <div>
        <p class="eyebrow">Bauakte anlegen</p>
        <h1>Neue Bauakte</h1>
      </div>
      <div class="head-actions">
        <a mat-stroked-button routerLink="/auftragssuche"><mat-icon>search</mat-icon> Auftrag suchen</a>
      </div>
    </section>

    <div class="wz2">
      <!-- Schrittanzeige -->
      <div class="wz2-steps">
        @for (t of mobileStepTitles; track $index; let i = $index) {
          <button type="button" class="wz2-step" [class.active]="activeStep() === i" [class.done]="activeStep() > i" [disabled]="i > activeStep()" (click)="goStep(i)">
            <span class="wz2-step-num">@if (activeStep() > i) { <mat-icon>check</mat-icon> } @else { {{ i + 1 }} }</span>
            <span class="wz2-step-label">{{ t }}</span>
          </button>
        }
      </div>
      <div class="wz2-progress"><span [style.width.%]="((activeStep() + 1) / mobileStepTitles.length) * 100"></span></div>

      <div class="wz2-panel">

        <!-- Schritt 1: Kunde -->
        @if (activeStep() === 0) {
          <div class="wz2-body">
            <div class="wz2-title-row">
              <h2 class="wz2-title"><mat-icon>person</mat-icon> Kunde auswählen</h2>
              @if (!newCustomerMode() && !selectedCustomer()) {
                <button mat-flat-button color="primary" type="button" class="wz2-title-newcust" (click)="toggleNewCustomer()"><mat-icon>person_add</mat-icon> Neuer Kunde</button>
              }
            </div>
            @if (!newCustomerMode()) {
              @if (selectedCustomer(); as customer) {
                <div class="wz2-selected">
                  <div class="wz2-selected-icon"><mat-icon>business</mat-icon></div>
                  <div class="wz2-selected-info">
                    <strong>{{ customer['name'] }}</strong>
                    <small>{{ customer['customer_no'] || 'ohne Kundennummer' }}@if (customer['city']) { · {{ customer['city'] }} }@if (customer['email']) { · {{ customer['email'] }} }</small>
                  </div>
                  <button mat-stroked-button type="button" (click)="clearCustomer()"><mat-icon>swap_horiz</mat-icon> Ändern</button>
                </div>
              } @else {
                <label class="wz2-flabel">Kunde aus der Kartei suchen</label>
                <div class="wz2-search">
                  <mat-icon>search</mat-icon>
                  <input type="search" placeholder="Name, Nummer, Ort oder E-Mail" [value]="customerSearch()" (input)="onCustomerSearch($any($event.target).value)" autocomplete="off">
                </div>
                @if (customersLoading()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }
                @if (!customersLoading() && customers().length) {
                  <div class="wz2-list-head"><mat-icon>groups</mat-icon><span>{{ customerSearch() ? filteredCustomers().length + ' Treffer' : (customers().length >= 500 ? '500+ Kunden – bitte suchen' : customers().length + ' Kunden') }}@if (!customerSearch() && customers().length > 25) { · oben tippen zum Suchen }</span></div>
                }
                <div class="wz2-picklist">
                  @for (customer of customerResults(); track customer['id']) {
                    <button type="button" class="wz2-pickitem" (click)="pickCustomer(customer)">
                      <mat-icon>person</mat-icon>
                      <span class="wz2-pickitem-txt"><b>{{ customer['name'] }}</b><small>{{ customerLabel(customer) }}</small></span>
                      <mat-icon class="wz2-pickitem-go">chevron_right</mat-icon>
                    </button>
                  }
                  @if (!filteredCustomers().length && !customersLoading()) {
                    <p class="wz2-empty">Kein passender Kunde. Legen Sie den Kunden direkt neu an.</p>
                  }
                </div>
              }
              <button mat-flat-button type="button" class="wz2-newcust" (click)="toggleNewCustomer()"><mat-icon>person_add</mat-icon> Neuen Kunden direkt anlegen</button>
            } @else {
              <div class="wz2-newcust-head"><b>Neuen Kunden anlegen</b><button mat-button type="button" (click)="toggleNewCustomer()"><mat-icon>arrow_back</mat-icon> Zurück zur Kartei</button></div>
              <form [formGroup]="newCustomerForm" class="wz2-grid">
                <mat-form-field appearance="outline" class="wz2-wide"><mat-label>Name / Firma *</mat-label><input matInput formControlName="name" (input)="onNewCustomerKey()" autocomplete="off"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Kundennummer (optional)</mat-label><input matInput formControlName="customer_no" autocomplete="off"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Telefon</mat-label><input matInput type="tel" formControlName="phone" (input)="onNewCustomerKey()" autocomplete="off"></mat-form-field>
                <mat-form-field appearance="outline" class="wz2-wide"><mat-label>E-Mail</mat-label><input matInput type="email" formControlName="email" (input)="onNewCustomerKey()" autocomplete="off"></mat-form-field>
                <mat-form-field appearance="outline" class="wz2-wide"><mat-label>Adresse</mat-label><input matInput formControlName="address" autocomplete="off"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>PLZ</mat-label><input matInput formControlName="zip" autocomplete="off"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Ort</mat-label><input matInput formControlName="city" autocomplete="off"></mat-form-field>
              </form>
              @if (dupeCandidates().length) {
                <div class="dupe-warning">
                  <p><mat-icon>warning_amber</mat-icon> Möglicherweise schon vorhanden – bestehenden Kunden verwenden?</p>
                  @for (c of dupeCandidates(); track c['id']) {
                    <button mat-stroked-button type="button" class="dupe-item" (click)="useExistingCustomer(c)"><mat-icon>person</mat-icon><span>{{ c['name'] }}@if (c['city']) { · {{ c['city'] }} }@if (c['customer_no']) { · Nr. {{ c['customer_no'] }} }@if (c['phone']) { · {{ c['phone'] }} }</span></button>
                  }
                </div>
              }
            }
            <div class="wz2-actions">
              <button mat-flat-button color="primary" class="wz2-next" (click)="nextStep()" [disabled]="!canProceedCustomer()">Weiter <mat-icon>arrow_forward</mat-icon></button>
            </div>
          </div>
        }

        <!-- Schritt 2: Bauakte -->
        @if (activeStep() === 1) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>folder_open</mat-icon> Bauakte erfassen</h2>
            <div class="capture-info">
              <mat-icon>event_available</mat-icon>
              <div class="capture-info-lines">
                <span>Erfasst am: <b>{{ todayLabel() }}</b></span>
                <span>Bauaktennummer wird automatisch vergeben.</span>
              </div>
            </div>
            <form [formGroup]="projectForm" class="wz2-grid">
              <mat-form-field appearance="outline" class="wz2-wide">
                <mat-label>Bauaktenname</mat-label>
                <input matInput formControlName="name" placeholder="BV Musterstraße 12">
                @if (autoName()) {
                  <button mat-icon-button matSuffix type="button" (click)="applyAutoName()"
                          [matTooltip]="'Vorschlag übernehmen: ' + autoName()" aria-label="Namensvorschlag übernehmen">
                    <mat-icon>auto_awesome</mat-icon>
                  </button>
                }
                <mat-hint>
                  @if (autoName()) {
                    <button type="button" class="wz2-autoname" (click)="applyAutoName()">
                      <mat-icon>auto_awesome</mat-icon>{{ autoName() }}
                    </button>
                  } @else {
                    Leer lassen: Der Name entsteht automatisch aus Adresse und Kunde.
                  }
                </mat-hint>
              </mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Status</mat-label><mat-select formControlName="status"><mat-option value="new">Neu</mat-option><mat-option value="planned">Geplant</mat-option><mat-option value="in_progress">In Arbeit</mat-option></mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Budget netto (€)</mat-label><input matInput type="number" formControlName="budget_net"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Erstbegehung geplant am</mat-label><input matInput type="date" formControlName="inspection_date"></mat-form-field>
              <mat-form-field appearance="outline" class="wz2-wide"><mat-label>Leistungsbeschreibung</mat-label><textarea matInput rows="3" formControlName="notes"></textarea></mat-form-field>
            </form>
            <div class="wz2-actions">
              <button mat-stroked-button (click)="prevStep()"><mat-icon>arrow_back</mat-icon> Zurück</button>
              <button mat-flat-button color="primary" class="wz2-next" (click)="nextStep()">Weiter <mat-icon>arrow_forward</mat-icon></button>
            </div>
          </div>
        }

        <!-- Schritt 3: Baustelle -->
        @if (activeStep() === 2) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>place</mat-icon> Adresse & Ausführung</h2>
            <div class="site-same-hint">
              <mat-slide-toggle [(ngModel)]="siteSameAsProject" [ngModelOptions]="{ standalone: true }">Baustelle entspricht der Bauakte (Name & Nummer übernehmen)</mat-slide-toggle>
            </div>
            <form [formGroup]="siteForm" class="wz2-grid">
              @if (!siteSameAsProject) {
                <mat-form-field appearance="outline"><mat-label>Objekt / Baustelle</mat-label><input matInput formControlName="name"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Baustellennummer</mat-label><input matInput formControlName="site_no"></mat-form-field>
              }
              <mat-form-field appearance="outline" class="wz2-wide">
                <mat-label>Baustellenadresse *</mat-label>
                <input matInput formControlName="address" placeholder="Straße und Hausnummer">
                @if (customerAddressAvailable()) {
                  <button mat-icon-button matSuffix type="button" (click)="adoptCustomerAddress()"
                          matTooltip="Adresse des Kunden übernehmen" aria-label="Kundenadresse übernehmen">
                    <mat-icon>content_copy</mat-icon>
                  </button>
                }
                <mat-hint>
                  @if (customerAddressAvailable()) {
                    <button type="button" class="wz2-adopt" (click)="adoptCustomerAddress()">
                      <mat-icon>content_copy</mat-icon>Wie beim Kunden: {{ customerAddressLabel() }}
                    </button>
                  } @else {
                    Für die Aufnahme vor Ort erforderlich – daraus entstehen Navigation und Bauaktenname.
                  }
                </mat-hint>
              </mat-form-field>
              <mat-form-field appearance="outline"><mat-label>PLZ</mat-label><input matInput formControlName="zip"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Ort</mat-label><input matInput formControlName="city"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Bauleiter</mat-label><input matInput formControlName="site_manager"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Telefon Baustelle</mat-label><input matInput type="tel" formControlName="phone"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Estrichart</mat-label><mat-select formControlName="screed_type">@for (type of screedTypes(); track type) { <mat-option [value]="type">{{ type }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Fläche m²</mat-label><input matInput type="number" formControlName="area_m2"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Ausführung geplant ab</mat-label><input matInput type="date" formControlName="planned_start"></mat-form-field>
            </form>
            <div class="wz2-actions">
              <button mat-stroked-button (click)="prevStep()"><mat-icon>arrow_back</mat-icon> Zurück</button>
              <button mat-flat-button color="primary" class="wz2-next" (click)="nextStep()">Weiter <mat-icon>arrow_forward</mat-icon></button>
            </div>
          </div>
        }

        <!-- Schritt 4: Räume -->
        @if (activeStep() === 3) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>meeting_room</mat-icon> Räume & Aufbau <span class="wz2-optional">optional</span></h2>
            <div class="rooms-toolbar">
              <button mat-stroked-button type="button" (click)="addRoom()"><mat-icon>add</mat-icon> Raum hinzufügen</button>
              @if (rooms.length) { <button mat-button type="button" class="skip-rooms-btn" (click)="skipRooms()"><mat-icon>skip_next</mat-icon> Räume überspringen</button> }
              <span>{{ rooms.length ? rooms.length + ' Räume vorbereitet' : 'Keine Räume – optional' }}</span>
            </div>
            @if (!rooms.length) {
              <div class="rooms-empty">
                <mat-icon>meeting_room</mat-icon>
                <p>Keine Räume erfasst. Das ist in Ordnung – Sie können Räume jederzeit später in der Bauakte ergänzen.</p>
                <button mat-stroked-button type="button" (click)="addRoom()"><mat-icon>add</mat-icon> Doch einen Raum erfassen</button>
              </div>
            }
            <div class="room-list">
              @for (room of rooms.controls; track $index; let i = $index) {
                <div [formGroup]="$any(room)" class="wz2-room">
                  <div class="room-head"><strong>Raum {{ i + 1 }}</strong><button mat-icon-button type="button" (click)="removeRoom(i)"><mat-icon>delete</mat-icon></button></div>
                  <div class="wz2-grid">
                    <mat-form-field appearance="outline"><mat-label>Raumname</mat-label><input matInput formControlName="name"></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Geschoss</mat-label><input matInput formControlName="floor"></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Fläche m²</mat-label><input matInput type="number" formControlName="area_m2"></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Aufbauhöhe mm</mat-label><input matInput type="number" formControlName="height_mm"></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Estrichart</mat-label><mat-select formControlName="screed_type">@for (type of screedTypes(); track type) { <mat-option [value]="type">{{ type }}</mat-option> }</mat-select></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Fußbodenheizung</mat-label><mat-select formControlName="heating"><mat-option [value]="true">Ja</mat-option><mat-option [value]="false">Nein</mat-option></mat-select></mat-form-field>
                  </div>
                </div>
              }
            </div>
            <div class="wz2-actions">
              <button mat-stroked-button (click)="prevStep()"><mat-icon>arrow_back</mat-icon> Zurück</button>
              <button mat-flat-button color="primary" class="wz2-next" (click)="nextStep()">Weiter <mat-icon>arrow_forward</mat-icon></button>
            </div>
          </div>
        }

        <!-- Abkürzung für die Aufnahme beim Kunden.
             Der Bauleiter steht vor Ort und soll nicht durch fünf Schritte
             blättern müssen. Sobald Kunde und Adresse stehen, lässt sich von
             JEDEM Schritt aus anlegen und direkt in die Erstbegehung wechseln.
             Alles Übrige kann später in Ruhe ergänzt werden. -->
        <!-- Aufnahme vor Ort: als Abfolge dargestellt, damit klar ist, was
             passiert. Vorher standen zwei ähnlich aussehende Kästen da und es
             war nicht erkennbar, dass der eine die Vorstufe des anderen ist. -->
        @if (activeStep() < 4 && customerChosen()) {
          <section class="wz2-onsite" [class.is-ready]="canQuickStart()">
            <header>
              <mat-icon>directions_car</mat-icon>
              <div>
                <b>Aufnahme vor Ort</b>
                <small>Bauakte anlegen und direkt mit der Erstbegehung weitermachen.</small>
              </div>
            </header>

            <ol class="wz2-onsite-list">
              <li class="done">
                <span class="wz2-onsite-mark"><mat-icon>check</mat-icon></span>
                <span class="wz2-onsite-text">Kunde steht fest<em>{{ customerNameLabel() }}</em></span>
              </li>

              <li [class.done]="canQuickStart()">
                <span class="wz2-onsite-mark">
                  @if (canQuickStart()) { <mat-icon>check</mat-icon> } @else { <span>2</span> }
                </span>
                <span class="wz2-onsite-text">
                  Baustellenadresse
                  @if (canQuickStart()) {
                    <em>{{ siteAddressLabel() }}</em>
                  } @else {
                    <em class="is-missing">wird für Navigation, Planung und den Aktennamen gebraucht</em>
                  }
                </span>
                @if (!canQuickStart() && customerAddressAvailable()) {
                  <button mat-stroked-button class="wz2-onsite-act" (click)="adoptCustomerAddress()">
                    <mat-icon>content_copy</mat-icon>Vom Kunden übernehmen
                  </button>
                }
              </li>

              <li [class.done]="false">
                <span class="wz2-onsite-mark"><span>3</span></span>
                <span class="wz2-onsite-text">Anlegen und begehen<em>{{ quickSummary() }}</em></span>
              </li>
            </ol>

            <button mat-flat-button color="primary" class="wz2-onsite-go"
                    (click)="submit(true)" [disabled]="saving() || !canCreateProject || !canQuickStart()">
              <mat-icon>fact_check</mat-icon>
              {{ canQuickStart() ? 'Anlegen und Erstbegehung starten' : 'Zuerst die Baustellenadresse eintragen' }}
            </button>
          </section>
        }

        <!-- Schritt 5: Speichern -->
        @if (activeStep() === 4) {
          <div class="wz2-body">
            <h2 class="wz2-title"><mat-icon>task_alt</mat-icon> Prüfen & speichern</h2>
            <div class="wz2-summary">
              <div class="wz2-sum-item"><span>Kunde</span><b>{{ selectedCustomer()?.['name'] || newCustomerForm.value.name || 'nicht gewählt' }}</b></div>
              <div class="wz2-sum-item"><span>Bauakte</span><b>{{ projectForm.value.name || '—' }}</b></div>
              <div class="wz2-sum-item"><span>Adresse</span><b>{{ siteForm.value.address || '—' }}{{ siteForm.value.city ? ', ' + siteForm.value.city : '' }}</b></div>
              <div class="wz2-sum-item"><span>Estrichart / Fläche</span><b>{{ siteForm.value.screed_type || '—' }} · {{ siteForm.value.area_m2 || 0 }} m²</b></div>
              <div class="wz2-sum-item"><span>Räume</span><b>{{ rooms.length }}</b></div>
            </div>
            @if (saving()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }
            <!-- Abschlussleiste: Hauptweg deutlich hervorgehoben, Nebenwege darunter -->
            <div class="wz2-finalbar">
              @if (!canCreateProject) { <p class="wz2-no-right"><mat-icon>lock</mat-icon>Ihnen fehlt die Berechtigung, Bauakten anzulegen. Bitte wenden Sie sich an die Administration.</p> }
              <div class="wz2-finalbar-primary">
                <button mat-flat-button color="primary" class="wz2-primary-save" (click)="submit(true)" [disabled]="saving() || !canCreateProject">
                  <mat-icon>fact_check</mat-icon>
                  <span class="wz2-btn-text"><b>Anlegen & Erstbegehung starten</b><small>Empfohlener Weg – Akte anlegen und direkt begehen</small></span>
                </button>
                <button mat-stroked-button (click)="submit(false)" [disabled]="saving() || !canCreateProject">
                  <mat-icon>save</mat-icon>
                  <span class="wz2-btn-text"><b>Nur Bauakte anlegen</b><small>Erstbegehung später durchführen</small></span>
                </button>
              </div>
              <div class="wz2-finalbar-secondary">
                <button mat-button (click)="prevStep()"><mat-icon>arrow_back</mat-icon>Zurück</button>
                <button mat-button (click)="saveOffline()"><mat-icon>cloud_off</mat-icon>Offline vormerken</button>
              </div>
            </div>
          </div>
        }

      </div>
    </div>
  
`
})
export class WorkflowWizardComponent implements OnInit {

  private readonly auth = inject(AuthService);
  /** Bauakten anlegen setzt Schreibrecht auf Bauakten voraus. */
  readonly canCreateProject = this.auth.canWrite('projects');  private readonly fb = inject(FormBuilder);
  private readonly api = inject(ApiService);
  private readonly offline = inject(OfflineService);
  private readonly snack = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private inquiryId = '';

  readonly activeStep = signal(0);
  readonly saving = signal(false);
  readonly customersLoading = signal(false);
  readonly customers = signal<GenericRecord[]>([]);
  readonly customerSearch = signal('');
  readonly selectedCustomerId = signal('');
  readonly selectedCustomer = computed<GenericRecord | null>(() => this.customers().find((customer) => String(customer['id']) === String(this.selectedCustomerId() || this.customerForm.controls.customer_id.value || '')) ?? null);
  readonly filteredCustomers = computed(() => this.filterRecords(this.customers(), this.customerSearch(), ['customer_no','name','city','zip','email','phone']));
  readonly screedTypes = signal<string[]>([]);
  readonly mobileStepTitles = ['Kunde auswählen', 'Bauakte erfassen', 'Adresse & Ausführung', 'Räume & Aufbau', 'Speichern'];

  readonly workflowForm = this.fb.group({
    customer: this.fb.group({
      customer_id: ['', Validators.required]
    }),
    project: this.fb.group({
      name: ['', Validators.required], project_no: [''], status: ['new'], budget_net: [null as number | null], start_date: [''], end_date: [''], inspection_date: [new Date().toISOString().slice(0, 10)], notes: ['']
    }),
    site: this.fb.group({
      name: [''], site_no: [''], address: [''], zip: [''], city: [''], site_manager: [''], phone: [''], screed_type: [''], area_m2: [null as number | null], planned_start: [''], planned_end: ['']
    }),
    rooms: this.fb.array([this.createRoomGroup()])
  });

  readonly customerForm = this.workflowForm.controls.customer;
  siteSameAsProject = true;
  readonly projectForm = this.workflowForm.controls.project;
  readonly siteForm = this.workflowForm.controls.site;
  get rooms(): FormArray { return this.workflowForm.controls.rooms; }

  // ----- Direkter Kunden-Anlage-Modus (ohne Kundennummer, mit Dublettensuche) -----
  readonly newCustomerMode = signal(false);
  readonly dupeCandidates = signal<GenericRecord[]>([]);
  readonly newCustomerForm = this.fb.group({
    name: ['', Validators.required], customer_no: [''], email: [''], phone: [''], address: [''], zip: [''], city: ['']
  });
  private dupeTimer: ReturnType<typeof setTimeout> | null = null;
  canProceedCustomer(): boolean { return this.newCustomerMode() ? this.newCustomerForm.valid : this.customerForm.valid; }
  todayLabel(): string { return new Date().toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }); }
  toggleNewCustomer(): void {
    const next = !this.newCustomerMode();
    this.newCustomerMode.set(next);
    this.dupeCandidates.set([]);
    const ctrl = this.customerForm.controls.customer_id;
    if (next) { ctrl.clearValidators(); ctrl.setValue('', { emitEvent: false }); this.selectedCustomerId.set(''); }
    else { ctrl.setValidators(Validators.required); }
    ctrl.updateValueAndValidity({ emitEvent: false });
  }
  onNewCustomerKey(): void {
    if (this.dupeTimer) clearTimeout(this.dupeTimer);
    const name = String(this.newCustomerForm.value.name || '').trim();
    const email = String(this.newCustomerForm.value.email || '').trim();
    const phone = String(this.newCustomerForm.value.phone || '').trim();
    const term = email || phone || name;
    if (term.length < 3) { this.dupeCandidates.set([]); return; }
    this.dupeTimer = setTimeout(() => {
      this.api.list('customers', term, 8).subscribe({
        next: (r) => {
          const needleName = name.toLowerCase();
          const list = (r.items || []).filter((c) => {
            const cn = String(c['name'] || '').toLowerCase();
            return (email && String(c['email'] || '').toLowerCase() === email.toLowerCase())
              || (phone && String(c['phone'] || '').replace(/\s/g, '') === phone.replace(/\s/g, ''))
              || (needleName && (cn.includes(needleName) || needleName.includes(cn)));
          });
          this.dupeCandidates.set(list.slice(0, 5));
        },
        error: () => this.dupeCandidates.set([])
      });
    }, 350);
  }
  useExistingCustomer(c: GenericRecord): void {
    this.newCustomerMode.set(false);
    this.dupeCandidates.set([]);
    const ctrl = this.customerForm.controls.customer_id;
    ctrl.setValidators(Validators.required);
    if (!this.customers().some((x) => String(x['id']) === String(c['id']))) this.customers.set([c, ...this.customers()]);
    this.selectCustomer(String(c['id']));
    ctrl.updateValueAndValidity({ emitEvent: false });
    this.snack.open(`Bestehender Kunde „${c['name']}" übernommen.`, 'OK', { duration: 2500 });
  }

  ngOnInit(): void {
    // Standardmäßig keinen Raum vorbelegen – Räume sind optional und werden bei Bedarf ergänzt.
    this.rooms.clear();
    this.customerForm.controls.customer_id.valueChanges.subscribe((value) => this.selectedCustomerId.set(String(value || '')));
    this.applyInquiryPrefill();
    this.loadCustomers();
    this.api.list('screed_types', '', 250).subscribe({
      next: (r) => this.screedTypes.set((r.items || []).map((s) => String(s['name'] || s['label'] || '').trim()).filter(Boolean)),
      error: () => this.screedTypes.set([])
    });
  }

  /**
   * Vorschlag für den Bauaktennamen.
   *
   * Regel: "BV" + Straße und Hausnummer, ersatzweise Kunde und Ort. Das
   * entspricht der üblichen Schreibweise im Betrieb ("BV Musterstraße 12")
   * und spart das Tippen bei jeder neuen Akte.
   */
  autoName(): string {
    const site = this.siteForm.value as Record<string, unknown>;
    const street = String(site['address'] || '').trim();
    const city = String(site['city'] || '').trim();
    const customer = String(this.selectedCustomer()?.['name'] || this.newCustomerForm.value.name || '').trim();

    let core = '';
    if (street) core = street;
    else if (customer && city) core = customer + ', ' + city;
    else if (customer) core = customer;
    else if (city) core = city;
    if (!core) return '';

    const suggestion = 'BV ' + core;
    const current = String(this.projectForm.value.name || '').trim();
    return current === suggestion ? '' : suggestion;
  }
  applyAutoName(): void {
    const name = this.autoName();
    if (!name) return;
    this.projectForm.patchValue({ name });
    if (!String(this.siteForm.value.name || '').trim()) this.siteForm.patchValue({ name });
  }
  /** Beim Speichern: leeres Namensfeld automatisch füllen. */
  private ensureProjectName(): void {
    if (String(this.projectForm.value.name || '').trim()) return;
    const fallback = this.autoName() || ('BV ' + new Date().toLocaleDateString('de-DE'));
    this.projectForm.patchValue({ name: fallback });
  }


  private applyInquiryPrefill(): void {
    const q = this.route.snapshot.queryParamMap;

    // Direkt aus der Kundenanlage gekommen: Kunden vorauswählen und, wenn
    // dort eine Adresse hinterlegt ist, gleich als Baustellenadresse
    // übernehmen. Das erspart das erneute Tippen derselben Angaben.
    const customerId = String(q.get('customer_id') || '').trim();
    if (customerId) {
      this.newCustomerMode.set(false);
      this.selectedCustomerId.set(customerId);
      this.customerForm.controls.customer_id.setValue(customerId, { emitEvent: false });
      // Die Kundenliste wird asynchron geladen – die Übernahme danach.
      setTimeout(() => {
        const c = this.selectedCustomer();
        if (!c) return;
        if (!String(this.siteForm.value.address || '').trim() && String(c['address'] || '').trim()) {
          this.adoptCustomerAddress();
        }
        this.ensureProjectName();
        this.activeStep.set(1);
      }, 350);
    }

    this.inquiryId = String(q.get('inquiry_id') || '').trim();
    if (!this.inquiryId) return;
    const name = String(q.get('inquiry_name') || '').trim();
    const phone = String(q.get('inquiry_phone') || '').trim();
    const company = String(q.get('inquiry_company') || '').trim();
    const subject = String(q.get('inquiry_subject') || '').trim();
    const notes = String(q.get('inquiry_notes') || '').trim();
    const title = subject || company || name || 'Anfrage';
    const noteLines = ['Aus Anfrage übernommen:', name ? `Kontakt: ${name}` : '', phone ? `Telefon: ${phone}` : '', company ? `Firma: ${company}` : '', subject ? `Anliegen: ${subject}` : '', notes].filter(Boolean);
    this.projectForm.patchValue({ name: title, notes: noteLines.join('\n') });
    this.siteForm.patchValue({ name: title, phone });
    this.snack.open('Anfrage wurde in den Bauakten-Wizard übernommen. Bitte Kunden auswählen und speichern.', 'OK', { duration: 4200 });
  }

  private custSearchTimer: ReturnType<typeof setTimeout> | null = null;
  onCustomerSearch(value: string): void {
    this.customerSearch.set(value);
    if (this.custSearchTimer) clearTimeout(this.custSearchTimer);
    this.custSearchTimer = setTimeout(() => {
      const term = value.trim();
      this.customersLoading.set(true);
      // Serverseitige Suche ueber ALLE Kunden (nicht nur die ersten 500)
      this.api.list('customers', term, 200).subscribe({
        next: (r) => { this.customers.set(r.items ?? []); this.customersLoading.set(false); },
        error: () => this.customersLoading.set(false)
      });
    }, 300);
  }

  loadCustomers(): void {
    this.customersLoading.set(true);
    this.api.list('customers', '', 500).subscribe({
      next: (response) => {
        this.customers.set(response.items ?? []);
        const currentCustomerId = this.customerForm.controls.customer_id.value;
        if (currentCustomerId) this.selectedCustomerId.set(String(currentCustomerId));
        this.customersLoading.set(false);
        if ((response.items ?? []).length === 0) this.snack.open('Keine Kunden in der Kundenkartei gefunden', 'OK', { duration: 4000 });
      },
      error: () => {
        this.customers.set([]);
        this.customersLoading.set(false);
        this.snack.open('Kundenkartei konnte nicht geladen werden', 'OK', { duration: 4000 });
      }
    });
  }

  selectCustomer(customerId: string): void {
    const value = String(customerId || '').trim();
    this.selectedCustomerId.set(value);
    this.customerForm.controls.customer_id.setValue(value, { emitEvent: false });
    this.customerForm.controls.customer_id.markAsDirty();
    this.customerForm.controls.customer_id.markAsTouched();
    this.customerForm.controls.customer_id.updateValueAndValidity({ emitEvent: false });
    this.customerForm.updateValueAndValidity({ emitEvent: false });
  }

  // ----- Eigener Stepper (ersetzt mat-stepper; sauber auf Handy + Desktop) -----
  customerResults(): GenericRecord[] { return this.filteredCustomers().slice(0, 500); }
  pickCustomer(c: GenericRecord): void { this.selectCustomer(String(c['id'])); this.customerSearch.set(''); }
  clearCustomer(): void {
    this.selectedCustomerId.set('');
    this.customerForm.controls.customer_id.setValue('', { emitEvent: false });
    this.customerForm.controls.customer_id.updateValueAndValidity({ emitEvent: false });
    this.customerSearch.set('');
  }
  private scrollWizardTop(): void { try { window.scrollTo({ top: 0, behavior: 'smooth' }); } catch { /* ignore */ } }
  /**
   * Reicht das Erfasste, um die Akte anzulegen?
   *
   * Bewusst niedrig angesetzt: Kunde (gewählt oder neu mit Namen) und eine
   * Adresse oder ein Bauaktenname. Alles Weitere – Estrichart, Fläche,
   * Termine – lässt sich nach der Begehung ergänzen. Beim Kunden zählt
   * Tempo, nicht Vollständigkeit.
   */
  /** Ist beim Kunden eine Adresse hinterlegt, die übernommen werden kann? */
  /** Steht ein Kunde fest? */
  customerChosen(): boolean {
    return this.newCustomerMode()
      ? !!String(this.newCustomerForm.value.name || '').trim()
      : !!this.selectedCustomerIdValue();
  }
  customerAddressAvailable(): boolean {
    return !!this.customerAddressParts().address;
  }
  private customerAddressParts(): { address: string; zip: string; city: string } {
    const c = this.newCustomerMode()
      ? (this.newCustomerForm.value as Record<string, unknown>)
      : (this.selectedCustomer() || {});
    return {
      address: String(c['address'] || '').trim(),
      zip: String(c['zip'] || '').trim(),
      city: String(c['city'] || '').trim()
    };
  }
  customerAddressLabel(): string {
    const p = this.customerAddressParts();
    return [p.address, [p.zip, p.city].filter(Boolean).join(' ')].filter(Boolean).join(', ');
  }
  /**
   * Adresse des Kunden als Baustellenadresse übernehmen.
   * Häufigster Fall bei Privatkunden: Die Baustelle IST die Kundenadresse.
   */
  adoptCustomerAddress(): void {
    const p = this.customerAddressParts();
    if (!p.address) return;
    this.siteForm.patchValue({ address: p.address, zip: p.zip, city: p.city });
    this.snack.open('Adresse des Kunden übernommen.', 'OK', { duration: 2200 });
  }

  canQuickStart(): boolean {
    const customerOk = this.newCustomerMode()
      ? !!String(this.newCustomerForm.value.name || '').trim()
      : !!this.selectedCustomerIdValue();
    if (!customerOk) return false;
    // Die Baustellenadresse ist bei der Aufnahme vor Ort zwingend: Ohne sie
    // gibt es keine Navigation, keine Zuordnung im Plan und keinen
    // brauchbaren Bauaktennamen.
    return !!String(this.siteForm.value.address || '').trim();
  }
  /** Name des gewählten bzw. neu erfassten Kunden. */
  customerNameLabel(): string {
    return (this.newCustomerMode()
      ? String(this.newCustomerForm.value.name || '')
      : String(this.selectedCustomer()?.['name'] || '')).trim() || 'Kunde';
  }
  /** Baustellenadresse in einer Zeile. */
  siteAddressLabel(): string {
    const v = this.siteForm.value as Record<string, unknown>;
    return [String(v['address'] || '').trim(),
            [String(v['zip'] || '').trim(), String(v['city'] || '').trim()].filter(Boolean).join(' ')]
      .filter(Boolean).join(', ');
  }
  /** Kurzfassung dessen, was gespeichert würde. */
  quickSummary(): string {
    const customer = this.newCustomerMode()
      ? String(this.newCustomerForm.value.name || '')
      : String(this.selectedCustomer()?.['name'] || '');
    const name = String(this.projectForm.value.name || '').trim() || this.autoName() || 'Bauakte';
    const parts = [name, customer].map((v) => v.trim()).filter((v) => v.length > 0);
    return `${parts.join(' · ')} – Rest später ergänzen`;
  }

  nextStep(): void {
    const s = this.activeStep();
    if (s === 0 && !this.canProceedCustomer()) {
      this.newCustomerForm.markAllAsTouched(); this.customerForm.markAllAsTouched();
      this.snack.open(this.newCustomerMode() ? 'Bitte mindestens den Kundennamen angeben.' : 'Bitte einen Kunden wählen oder neu anlegen.', 'OK', { duration: 3200 });
      return;
    }
    if (s === 1 && this.projectForm.controls.name.invalid) {
      // Erst versuchen, den Namen selbst zu bilden – nur wenn das nicht
      // gelingt (keine Adresse, kein Kunde), wird nachgefragt.
      this.ensureProjectName();
    }
    if (s === 1 && this.projectForm.controls.name.invalid) {
      this.projectForm.markAllAsTouched();
      this.snack.open('Bitte einen Bauaktennamen eingeben.', 'OK', { duration: 3000 });
      return;
    }
    this.activeStep.set(Math.min(this.mobileStepTitles.length - 1, s + 1));
    this.scrollWizardTop();
  }
  prevStep(): void { this.activeStep.set(Math.max(0, this.activeStep() - 1)); this.scrollWizardTop(); }
  goStep(i: number): void { if (i <= this.activeStep()) { this.activeStep.set(i); this.scrollWizardTop(); } }

  customerLabel(customer: GenericRecord): string {
    const number = customer['customer_no'] || 'Kunde';
    const name = customer['name'] || 'ohne Namen';
    const city = customer['city'] ? ` · ${customer['city']}` : '';
    return `${number} · ${name}${city}`;
  }

  filterRecords(items: GenericRecord[], query: string, fields: string[]): GenericRecord[] {
    const needle = this.normalize(query);
    if (!needle) return items;
    return items.filter((item) => this.normalize(fields.map((field) => item[field] ?? '').join(' ')).includes(needle));
  }

  normalize(value: unknown): string {
    return String(value ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
  }

  firstInvalidSection(): string {
    if (this.customerForm.invalid || !this.selectedCustomerIdValue()) return 'Bitte oben im Dropdown einen Kunden aus der Kundenkartei auswählen.';
    if (this.projectForm.invalid) return 'Bitte die Pflichtfelder im Abschnitt Auftrag ausfüllen.';
    if (this.siteForm.invalid) return 'Bitte die Pflichtfelder im Abschnitt Baustelle ausfüllen.';
    if (this.rooms.invalid) return 'Bitte die Pflichtfelder im Abschnitt Räume ausfüllen.';
    return 'Bitte alle Pflichtfelder prüfen.';
  }

  createRoomGroup() {
    return this.fb.group({ name: ['Wohnung / EG', Validators.required], floor: ['EG'], area_m2: [null as number | null], screed_type: [''], height_mm: [65], heating: [true], notes: [''] });
  }
  addRoom(): void { this.rooms.push(this.createRoomGroup()); }
  removeRoom(index: number): void { if (this.rooms.length > 0) this.rooms.removeAt(index); }
  skipRooms(): void { while (this.rooms.length > 0) this.rooms.removeAt(0); }

  selectedCustomerIdValue(): string {
    const fromSignal = String(this.selectedCustomerId() || '').trim();
    const fromControl = String(this.customerForm.controls.customer_id.value || '').trim();
    return fromSignal || fromControl;
  }

  payload(): GenericRecord {
    const raw = this.workflowForm.getRawValue();
    const newMode = this.newCustomerMode();
    const customerId = newMode ? '' : this.selectedCustomerIdValue();
    if (!newMode && !customerId) throw new Error('Kunde fehlt');
    const nc = this.newCustomerForm.getRawValue();
    const customer = newMode
      ? ({ name: nc.name || '', customer_no: nc.customer_no || '', email: nc.email || '', phone: nc.phone || '', address: nc.address || '', zip: nc.zip || '', city: nc.city || '' } as GenericRecord)
      : (this.selectedCustomer() ?? ({ id: customerId } as GenericRecord));
    const siteAddress = raw.site.address || customer['address'] || '';
    const siteName = (this.siteSameAsProject || !raw.site.name) ? (raw.project.name || raw.site.name || 'Baustelle') : raw.site.name;
    const siteNo = (this.siteSameAsProject || !raw.site.site_no) ? (raw.project.project_no || raw.site.site_no || '') : raw.site.site_no;
    return {
      inquiry_id: this.inquiryId || null,
      customer: {
        id: customerId,
        customer_id: customerId,
        create_new: newMode ? true : false,
        name: customer['name'] || '',
        customer_no: customer['customer_no'] || '',
        email: customer['email'] || '',
        phone: customer['phone'] || '',
        address: customer['address'] || '',
        zip: customer['zip'] || '',
        city: customer['city'] || ''
      },
      project: { ...raw.project, customer_id: customerId, address: siteAddress, zip: raw.site.zip || customer['zip'] || '', city: raw.site.city || customer['city'] || '' },
      site: { ...raw.site, name: siteName, site_no: siteNo, address: siteAddress, zip: raw.site.zip || customer['zip'] || '', city: raw.site.city || customer['city'] || '', status: 'planned' },
      rooms: raw.rooms
    };
  }

  submit(goToInspection = false): void {
    // Leeres Namensfeld automatisch füllen, bevor geprüft wird.
    this.ensureProjectName();
    const newMode = this.newCustomerMode();
    if (!newMode) this.selectedCustomerId.set(this.selectedCustomerIdValue());
    const customerOk = newMode ? this.newCustomerForm.valid : !!this.selectedCustomerIdValue();
    if (this.workflowForm.controls.project.invalid || !customerOk) {
      this.workflowForm.markAllAsTouched(); this.newCustomerForm.markAllAsTouched();
      this.snack.open(newMode && this.newCustomerForm.invalid ? 'Bitte mindestens den Kundennamen angeben.' : this.firstInvalidSection(), 'OK', { duration: 4200 });
      return;
    }
    this.saving.set(true);
    this.api.createCustomerProjectSite(this.payload()).subscribe({
      next: (response) => {
        this.saving.set(false);
        const created = response?.['created'] || response || {};
        const projectId = String(created['project_id'] || created['id'] || '').trim();
        const siteId = String(created['site_id'] || '').trim();
        const projectNo = String(created['project_no'] || '').trim();
        this.snack.open(projectNo ? `Bauakte ${projectNo} wurde angelegt` : 'Bauakte wurde angelegt', 'OK', { duration: 2500 });
        if (goToInspection && projectId) {
          const insp = String(this.projectForm.value.inspection_date || '').trim();
          this.router.navigate(['/erstbegehung-wizard'], { queryParams: { project_id: projectId, site_id: siteId, inspection_date: insp || null } });
        } else if (projectId) {
          this.router.navigate(['/auftragsakte'], { queryParams: { project_id: projectId } });
        } else {
          this.snack.open('Bauakte wurde gespeichert, konnte aber nicht automatisch geöffnet werden.', 'OK', { duration: 4000 });
        }
      },
      error: () => { this.saving.set(false); this.saveOffline(); this.snack.open('Server nicht erreichbar. Auftrag wurde offline vorgemerkt.', 'OK', { duration: 5000 }); }
    });
  }

  saveOffline(): void {
    this.selectedCustomerId.set(this.selectedCustomerIdValue());
    if (!this.selectedCustomerIdValue()) { this.snack.open('Offline-Vormerkung braucht einen Kunden aus der Kundenkartei', 'OK', { duration: 3000 }); return; }
    this.offline.enqueue({ moduleKey: 'workflow_customer_project_site', operation: 'create', payload: this.payload() });
    this.snack.open('Auftrag offline vorgemerkt', 'OK', { duration: 3000 });
  }
}
