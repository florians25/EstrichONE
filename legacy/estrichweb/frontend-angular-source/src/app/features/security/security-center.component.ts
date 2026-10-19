import { Component } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';

@Component({
  standalone: true,
  imports: [MatCardModule, MatIconModule, MatChipsModule],
  template: `
    <section class="page-head"><div><p class="eyebrow">DSGVO · Audit · Rollen · Device Management</p><h1><mat-icon>security</mat-icon>Security Center</h1><p>Übersicht über Sicherheitsfunktionen, die API-seitig vorbereitet und mandantenfähig umgesetzt sind.</p></div></section>
    <section class="grid cards">
      @for (item of checks; track item.title) {
        <mat-card><mat-card-title><mat-icon>{{ item.icon }}</mat-icon>{{ item.title }}</mat-card-title><p>{{ item.text }}</p><mat-chip-set>@for (tag of item.tags; track tag) { <mat-chip>{{ tag }}</mat-chip> }</mat-chip-set></mat-card>
      }
    </section>
  `
})
export class SecurityCenterComponent {
  readonly checks = [
    { icon: 'vpn_key', title: 'Authentifizierung', text: 'JWT, Refresh Tokens, Passwortregeln, Session-/Geräteverwaltung.', tags: ['JWT', 'Refresh', '2FA vorbereitet'] },
    { icon: 'policy', title: 'Rechte', text: 'Granulare Rechte bis Modul-/Datensatzebene mit Rollenmatrix.', tags: ['Lesen', 'Schreiben', 'Export', 'Archiv'] },
    { icon: 'history', title: 'Audit Log', text: 'Nicht löschbare Änderungsprotokolle mit Vorher/Nachher, IP und Gerät.', tags: ['Revision', 'DSGVO', 'Nachweis'] },
    { icon: 'shield', title: 'Schutz', text: 'Prepared Statements, Security Header, CSRF-/Rate-Limit-Grundlagen, Upload-Prüfungen.', tags: ['XSS', 'SQLi', 'HSTS-ready'] }
  ];
}
