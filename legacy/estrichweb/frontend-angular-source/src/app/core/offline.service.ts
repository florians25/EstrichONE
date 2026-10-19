import { Injectable, inject } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { ApiService, GenericRecord } from './api.service';

export interface OfflineOperation { id: string; moduleKey: string; operation: 'create' | 'update' | 'delete' | 'upload'; payload: GenericRecord; createdAt: string; }

@Injectable({ providedIn: 'root' })
export class OfflineService {
  private readonly api = inject(ApiService);
  private readonly storageKey = 'estrichweb.offlineQueue';
  readonly queue$ = new BehaviorSubject<OfflineOperation[]>(this.read());

  enqueue(operation: Omit<OfflineOperation, 'id' | 'createdAt'>): void {
    const next: OfflineOperation[] = [...this.read(), { ...operation, id: this.createId(), createdAt: new Date().toISOString() }];
    localStorage.setItem(this.storageKey, JSON.stringify(next));
    this.queue$.next(next);
  }

  private createId(): string {
    const bytes = new Uint8Array(16);
    const cryptoObject = globalThis.crypto as Crypto | undefined;
    if (cryptoObject?.getRandomValues) {
      cryptoObject.getRandomValues(bytes);
    } else {
      for (let i = 0; i < bytes.length; i += 1) bytes[i] = Math.floor(Math.random() * 256);
    }
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  clear(): void {
    localStorage.removeItem(this.storageKey);
    this.queue$.next([]);
  }

  sync() {
    return this.api.syncOffline(this.read()).subscribe({ next: () => this.clear() });
  }

  private read(): OfflineOperation[] {
    const raw = localStorage.getItem(this.storageKey);
    if (!raw) return [];
    try { return JSON.parse(raw) as OfflineOperation[]; } catch { return []; }
  }
}
