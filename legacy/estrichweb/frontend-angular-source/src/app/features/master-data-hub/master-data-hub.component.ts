import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

interface MasterLink { label: string; route: string; icon: string; text: string; }
interface MasterGroup { title: string; text: string; links: MasterLink[]; }

@Component({
  standalone: true,
  imports: [RouterLink, MatCardModule, MatButtonModule, MatIconModule],
  template: `
    <section class="page-head compact-head">
      <div>
        <p class="eyebrow">Verwaltung · Stammdaten sauber gebündelt</p>
        <h1><mat-icon>inventory_2</mat-icon>Stammdaten</h1>
        <p>Alle Grunddaten liegen gesammelt hier. Im Tagesgeschäft bleiben Aufträge, Kalender und Kolonnen im Vordergrund.</p>
      </div>
    </section>

    <section class="master-grid">
      @for (group of groups; track group.title) {
        <mat-card class="master-group-card">
          <h2>{{ group.title }}</h2>
          <p>{{ group.text }}</p>
          <div class="master-link-list">
            @for (link of group.links; track link.route) {
              <a [routerLink]="link.route" class="master-link">
                <mat-icon>{{ link.icon }}</mat-icon>
                <span><b>{{ link.label }}</b><small>{{ link.text }}</small></span>
              </a>
            }
          </div>
        </mat-card>
      }
    </section>
  `
})
export class MasterDataHubComponent {
  readonly groups: MasterGroup[] = [
    { title: 'Kunden & Kontakte', text: 'Adressdaten für Büro, Angebote und Baustellenkommunikation.', links: [
      { label: 'Kunden', route: '/kunden', icon: 'business', text: 'Auftraggeber, GU, Privatkunden' },
      { label: 'Ansprechpartner', route: '/ansprechpartner', icon: 'contacts', text: 'Bauleiter, Verwaltung, Rechnungsstellen' },
      { label: 'Lieferanten', route: '/lieferanten', icon: 'local_shipping', text: 'Materiallieferanten, Kontakte, Bestelladressen' }
    ]},
    { title: 'Kolonnen & Ressourcen', text: 'Alles, was in der Kolonnenplanung verwendet wird.', links: [
      { label: 'Kolonnen', route: '/kolonnen', icon: 'groups', text: 'Teams, Kapazitäten, Status' },
      { label: 'Mitarbeiter', route: '/kolonnen-mitarbeiter', icon: 'badge', text: 'Zuordnung zu Kolonnen' },
      { label: 'Fahrzeuge', route: '/fahrzeuge', icon: 'local_shipping', text: 'Transporter, LKW, Pumpenfahrzeuge' },
      { label: 'Maschinen', route: '/maschinen', icon: 'precision_manufacturing', text: 'Pumpen, Laser, Geräte' }
    ]},
    { title: 'Estrich-Fachwerte', text: 'Fachliche Standards und Aufbau-Komponenten.', links: [
      { label: 'Estricharten', route: '/estricharten', icon: 'layers', text: 'Zement, CA, Heizestrich usw.' },
      { label: 'Estrichaufbau-Komponenten', route: '/estrichkomponenten', icon: 'view_in_ar', text: 'Dämmung, Folie, Randdämmstreifen' },
      { label: 'Prüfvorlagen', route: '/erstbegehung-vorlagen', icon: 'fact_check', text: 'Checklisten und Pflichtfelder' }
    ]},
    { title: 'System', text: 'Nur für berechtigte Verwaltungskräfte und Admins.', links: [
      { label: 'Admin-Verwaltung', route: '/admin-verwaltung', icon: 'manage_accounts', text: 'Benutzer, Rollen, Geräte' },
      { label: 'Benutzer', route: '/benutzer', icon: 'person', text: 'Login, Rollen, Status' },
      { label: 'Rollen', route: '/rollen', icon: 'admin_panel_settings', text: 'Rechtepakete' },
      { label: 'Audit Log', route: '/audit-log', icon: 'history', text: 'Revisionssichere Historie' },
      { label: 'Geräte-Sessions', route: '/geraete', icon: 'devices', text: 'Mobile Geräte und Sitzungen' },
      { label: 'Lexware Office', route: '/lexware-integration', icon: 'sync_alt', text: 'Kunden und Aufträge anbinden' }
    ]}
  ];
}
