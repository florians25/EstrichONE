import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BauakteListItem, BauaktePage } from '../core/api.models';
import { problemMessage } from '../core/problem';
import { BauakteStatusComponent, formatDateTime } from '../shared/status';

/** Bauakten ansehen (M2: Web-Basis; Anlegen und Bearbeiten im Büro folgen mit AP-18). */
@Component({
  selector: 'eo-bauakten',
  imports: [FormsModule, RouterLink, BauakteStatusComponent],
  template: `
    <h1>Bauakten</h1>
    <form class="search" role="search" (ngSubmit)="load(true)">
      <label class="visually-hidden" for="search">Suche</label>
      <input id="search" name="search" type="search" placeholder="Nummer, Bauvorhaben, Adresse" [(ngModel)]="search" />
      <button class="button" type="submit">Suchen</button>
    </form>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    <table class="table">
      <thead>
        <tr><th scope="col">Nummer</th><th scope="col">Bauvorhaben</th><th scope="col">Baustelle</th><th scope="col">Status</th><th scope="col">Geändert</th></tr>
      </thead>
      <tbody>
        @for (item of items(); track item.id) {
          <tr>
            <td class="numeric">{{ item.number ?? 'neu' }}</td>
            <td><a [routerLink]="['/bauakten', item.id]">{{ item.title }}</a></td>
            <td>{{ address(item) }}</td>
            <td><eo-bauakte-status [status]="item.status" /></td>
            <td class="numeric">{{ date(item.updatedAt) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="5">{{ loading() ? 'Wird geladen …' : 'Keine Bauakten gefunden.' }}</td></tr>
        }
      </tbody>
    </table>
    @if (nextCursor()) {
      <p><button class="button" type="button" (click)="load(false)" [disabled]="loading()">Weitere laden</button></p>
    }
  `,
  styles: `
    .search { display: flex; gap: 8px; margin-bottom: 16px; max-width: 560px; }
    .search input { flex: 1; font: inherit; min-height: 36px; padding: 6px 10px; border: 1px solid var(--border-control); border-radius: var(--radius-control); background: var(--surface); color: var(--text-primary); }
  `,
})
export class BauaktenPage {
  private readonly http = inject(HttpClient);

  protected search = '';
  protected readonly items = signal<BauakteListItem[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly date = formatDateTime;

  constructor() {
    void this.load(true);
  }

  async load(reset: boolean): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    let params = new HttpParams().set('limit', 50);
    if (this.search.trim()) {
      params = params.set('search', this.search.trim());
    }
    const cursor = this.nextCursor();
    if (!reset && cursor) {
      params = params.set('cursor', cursor);
    }
    try {
      const page = await firstValueFrom(this.http.get<BauaktePage>('/api/v1/bauakten', { params }));
      this.items.set(reset ? page.items : [...this.items(), ...page.items]);
      this.nextCursor.set(page.nextCursor);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected address(item: BauakteListItem): string {
    const place = [item.postalCode, item.city].filter(Boolean).join(' ');
    return [item.street, place].filter(Boolean).join(', ') || '–';
  }
}
