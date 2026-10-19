import { Component, inject } from '@angular/core';
import { AsyncPipe, JsonPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { OfflineService } from '../../core/offline.service';

@Component({
  standalone: true,
  imports: [AsyncPipe, JsonPipe, MatCardModule, MatButtonModule, MatIconModule],
  template: `
    <section class="page-head"><div><p class="eyebrow">PWA · Offline Queue · automatische Synchronisation</p><h1><mat-icon>cloud_sync</mat-icon>Offline Sync Center</h1><p>Baustellen, Aufgaben, Checklisten, Fotos, Erstbegehungen und Tagesberichte können offline vorgemerkt und später synchronisiert werden.</p></div><button mat-flat-button color="primary" (click)="offline.sync()"><mat-icon>sync</mat-icon> Synchronisieren</button></section>
    <section class="list">
      @for (item of offline.queue$ | async; track item.id) {
        <mat-card><mat-card-title>{{ item.operation }} · {{ item.moduleKey }}</mat-card-title><mat-card-subtitle>{{ item.createdAt }}</mat-card-subtitle><pre>{{ item.payload | json }}</pre></mat-card>
      } @empty { <mat-card><mat-card-title>Keine Offline-Vorgänge</mat-card-title><p>Die PWA ist bereit für Baustellenbetrieb mit späterer Synchronisation.</p></mat-card> }
    </section>
  `
})
export class OfflineCenterComponent { readonly offline = inject(OfflineService); }
