import { Routes } from '@angular/router';
import { permissionGuard, signedInGuard } from './core/guards';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  { path: 'anmelden', title: 'Anmelden · EstrichONE', loadComponent: () => import('./pages/login.page').then((m) => m.LoginPage) },
  { path: 'einrichtung', title: 'Einrichtung · EstrichONE', loadComponent: () => import('./pages/setup.page').then((m) => m.SetupPage) },
  {
    path: '',
    component: ShellComponent,
    canActivate: [signedInGuard],
    canActivateChild: [signedInGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'bauakten' },
      {
        path: 'bauakten',
        title: 'Bauakten · EstrichONE',
        loadComponent: () => import('./pages/bauakten.page').then((m) => m.BauaktenPage),
      },
      {
        path: 'bauakten/:id',
        title: 'Bauakte · EstrichONE',
        loadComponent: () => import('./pages/bauakte-detail.page').then((m) => m.BauakteDetailPage),
      },
      {
        path: 'kunden',
        title: 'Kunden · EstrichONE',
        canActivate: [permissionGuard('contacts.read')],
        loadComponent: () => import('./pages/customers.page').then((m) => m.CustomersPage),
      },
      {
        path: 'verwaltung/benutzer',
        title: 'Benutzer · EstrichONE',
        canActivate: [permissionGuard('users.manage')],
        loadComponent: () => import('./pages/users.page').then((m) => m.UsersPage),
      },
      {
        path: 'it-center/geraete',
        title: 'Geräte · EstrichONE',
        canActivate: [permissionGuard('devices.read')],
        loadComponent: () => import('./pages/devices.page').then((m) => m.DevicesPage),
      },
      {
        path: 'it-center/helpdesk',
        title: 'Helpdesk · EstrichONE',
        canActivate: [permissionGuard('helpdesk.agent')],
        loadComponent: () => import('./pages/helpdesk.page').then((m) => m.HelpdeskPage),
      },
      {
        path: 'it-center/helpdesk/:id',
        title: 'Ticket · EstrichONE',
        canActivate: [permissionGuard('helpdesk.agent')],
        loadComponent: () => import('./pages/ticket.page').then((m) => m.TicketPage),
      },
      {
        path: 'system/sicherung',
        title: 'Sicherung · EstrichONE',
        canActivate: [permissionGuard('backup.manage')],
        loadComponent: () => import('./pages/backup.page').then((m) => m.BackupPage),
      },
      {
        path: 'konto/passwort',
        title: 'Passwort ändern · EstrichONE',
        loadComponent: () => import('./pages/change-password.page').then((m) => m.ChangePasswordPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
