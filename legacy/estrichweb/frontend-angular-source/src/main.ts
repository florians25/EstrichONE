import { isDevMode, LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, Routes, withInMemoryScrolling } from '@angular/router';
import { provideStore } from '@ngrx/store';
import { provideServiceWorker } from '@angular/service-worker';
import { AppComponent } from './app/app.component';
import { authInterceptor } from './app/core/auth.interceptor';
import { authGuard, permissionGuard } from './app/core/auth.guard';
import { ENTERPRISE_MODULES } from './app/core/module-registry';
import { appReducer } from './app/store/app.reducer';

// Alte generische Benutzer-/Rollen-Seiten sind abgeschaltet – diese laufen
// jetzt ausschließlich über die neue Verwaltung (/admin-verwaltung).
const LEGACY_ADMIN_KEYS = ['users', 'roles'];
const moduleRoutes: Routes = ENTERPRISE_MODULES
  .filter((module) => !LEGACY_ADMIN_KEYS.includes(module.key))
  .map((module) => ({
    path: module.route,
    canActivate: module.permission && module.permission !== 'projects' ? [authGuard, permissionGuard(module.permission)] : [authGuard],
    loadComponent: () => import('./app/features/module-page/module-page.component').then((m) => m.ModulePageComponent),
    data: { moduleKey: module.key }
  }));

const routes: Routes = [
  // Kundenportal ist derzeit deaktiviert. Alte Links laufen auf die Startseite,
  // damit gespeicherte Lesezeichen keinen Fehler erzeugen.
  { path: 'kundenportal', redirectTo: '', pathMatch: 'full' },
  { path: 'kundenportal/:token', redirectTo: '', pathMatch: 'full' },
  { path: 'kundenportal-verwaltung', redirectTo: '', pathMatch: 'full' },
  { path: 'login', loadComponent: () => import('./app/features/login/login.component').then((m) => m.LoginComponent) },
  { path: '', canActivate: [authGuard], loadComponent: () => import('./app/features/dashboard/dashboard.component').then((m) => m.DashboardComponent) },
  { path: 'auftrag-wizard', canActivate: [authGuard], loadComponent: () => import('./app/features/workflow-wizard/workflow-wizard.component').then((m) => m.WorkflowWizardComponent) },
  { path: 'auftragssuche', canActivate: [authGuard], loadComponent: () => import('./app/features/order-search/order-search.component').then((m) => m.OrderSearchComponent) },
  { path: 'auftragsakte', canActivate: [authGuard], loadComponent: () => import('./app/features/order-file/order-file.component').then((m) => m.OrderFileComponent) },
  { path: 'erstbegehung-wizard', canActivate: [authGuard], loadComponent: () => import('./app/features/initial-inspection-wizard/initial-inspection-wizard.component').then((m) => m.InitialInspectionWizardComponent) },
  { path: 'tagesdokumentation', canActivate: [authGuard], loadComponent: () => import('./app/features/daily-documentation/daily-documentation.component').then((m) => m.DailyDocumentationComponent) },
  { path: 'plantafel-board', redirectTo: 'kolonnen-wochenplaner', pathMatch: 'full' },
  // Arbeitsplan: bewusst NUR authGuard – jeder angemeldete Mitarbeiter darf
  // sehen, wer wann wo eingeteilt ist. Geändert wird im Kolonnenplan.
  { path: 'arbeitsplan', canActivate: [authGuard], loadComponent: () => import('./app/features/work-plan/work-plan.component').then((m) => m.WorkPlanComponent) },
  { path: 'kolonnen-wochenplaner', canActivate: [authGuard, permissionGuard('planning')], loadComponent: () => import('./app/features/crew-week-planner/crew-week-planner.component').then((m) => m.CrewWeekPlannerComponent) },
  { path: 'tageseinteilung', canActivate: [authGuard, permissionGuard('planning')], loadComponent: () => import('./app/features/daily-dispatch/daily-dispatch.component').then((m) => m.DailyDispatchComponent) },
  // Eigener Kalender im Stil von Google Kalender – bewusst nur authGuard,
  // damit jeder Mitarbeiter seine Termine sehen kann.
  { path: 'kalender', canActivate: [authGuard], loadComponent: () => import('./app/features/calendar-gcal/gcal.component').then((m) => m.GcalComponent) },
  { path: 'kalender-board', canActivate: [authGuard, permissionGuard('planning')], loadComponent: () => import('./app/features/calendar/calendar.component').then((m) => m.CalendarComponent) },
  { path: 'stammdaten', canActivate: [authGuard, permissionGuard('screed')], loadComponent: () => import('./app/features/master-data-hub/master-data-hub.component').then((m) => m.MasterDataHubComponent) },
  { path: 'lexware-integration', canActivate: [authGuard, permissionGuard('integrations')], loadComponent: () => import('./app/features/lexware-integration/lexware-integration.component').then((m) => m.LexwareIntegrationComponent) },
  { path: 'admin-verwaltung', canActivate: [authGuard, permissionGuard('users')], loadComponent: () => import('./app/features/admin-management/admin-management.component').then((m) => m.AdminManagementComponent) },
  { path: 'anrufe', canActivate: [authGuard, permissionGuard('customers')], loadComponent: () => import('./app/features/calls/calls.component').then((m) => m.CallsComponent) },
  { path: 'anfragen', canActivate: [authGuard, permissionGuard('customers')], loadComponent: () => import('./app/features/calls/calls.component').then((m) => m.CallsComponent), data: { callKind: 'inquiry' } },
  { path: 'anfragen/:id', canActivate: [authGuard, permissionGuard('customers')], loadComponent: () => import('./app/features/calls/inquiry-detail.component').then((m) => m.InquiryDetailComponent) },
  { path: 'materialbestellungen', canActivate: [authGuard, permissionGuard('materials')], loadComponent: () => import('./app/features/material-orders/material-orders.component').then((m) => m.MaterialOrdersComponent) },
  { path: 'firmen-einstellungen', canActivate: [authGuard, permissionGuard('settings')], loadComponent: () => import('./app/features/app-settings/app-settings.component').then((m) => m.AppSettingsComponent) },
  { path: 'profil', canActivate: [authGuard], loadComponent: () => import('./app/features/profile/profile.component').then((m) => m.ProfileComponent) },
  { path: 'passwort-aendern', canActivate: [authGuard], loadComponent: () => import('./app/features/force-password-change/force-password-change.component').then((m) => m.ForcePasswordChangeComponent) },
  { path: 'offline-center', canActivate: [authGuard], loadComponent: () => import('./app/features/offline/offline-center.component').then((m) => m.OfflineCenterComponent) },
  { path: 'benutzer', redirectTo: 'admin-verwaltung', pathMatch: 'full' },
  { path: 'rollen', redirectTo: 'admin-verwaltung', pathMatch: 'full' },
  ...moduleRoutes,
  { path: '**', redirectTo: '' }
];

registerLocaleData(localeDe);

bootstrapApplication(AppComponent, {
  providers: [
    { provide: LOCALE_ID, useValue: 'de-DE' },
    provideAnimations(),
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideStore({ app: appReducer }),
    provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), registrationStrategy: 'registerWhenStable:30000' })
  ]
}).catch((error) => console.error(error));
