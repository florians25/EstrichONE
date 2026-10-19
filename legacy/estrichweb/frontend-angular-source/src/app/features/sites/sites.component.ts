import { Component, inject } from '@angular/core';
import { AsyncPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { ApiService } from '../../core/api.service';

@Component({
  standalone: true,
  selector: 'ew-sites',
  imports: [AsyncPipe, MatCardModule, MatChipsModule],
  template: `
    <h1>Baustellenakte</h1>
    <section class="list">
      @for (site of sites$ | async; track site.id) {
        <mat-card>
          <mat-card-title>{{ site.name }}</mat-card-title>
          <mat-card-subtitle>{{ site.customer }} · {{ site.city }}</mat-card-subtitle>
          <mat-chip-set><mat-chip>{{ site.status }}</mat-chip></mat-chip-set>
          <p>Räume, Estrichaufbau, Dokumente, Fotos, Prüfungen, Termine und Mängel werden mandantengetrennt geführt.</p>
        </mat-card>
      }
    </section>
  `
})
export class SitesComponent {
  private readonly api = inject(ApiService);
  readonly sites$ = this.api.sites();
}
