import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import QRCode from 'qrcode';
import { firstValueFrom } from 'rxjs';
import { DeviceKind, DeviceResponse, EnrollmentCodeResponse } from '../core/api.models';
import { AuthService } from '../core/auth.service';
import { problemMessage } from '../core/problem';
import { DeviceStatusComponent, formatDateTime } from '../shared/status';

/** IT Center › Geräte (docs/reengineering/13, Basis): freigeben, sperren, entfernen, neues Gerät per QR-Code. */
@Component({
  selector: 'eo-devices',
  imports: [FormsModule, DeviceStatusComponent],
  template: `
    <div class="title">
      <h1>Geräte</h1>
      @if (canManage()) {
        <button class="button primary" type="button" (click)="openEnrollment()">Gerät einrichten</button>
      }
    </div>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    @if (pendingCount() > 0) {
      <div class="message hint" role="status">{{ pendingCount() }} {{ pendingCount() === 1 ? 'Gerät wartet' : 'Geräte warten' }} auf Freigabe.</div>
    }
    <table class="table">
      <thead>
        <tr><th scope="col">Gerät</th><th scope="col">Nutzung</th><th scope="col">Status</th><th scope="col">App / System</th>
          <th scope="col">Zuletzt gesehen</th><th scope="col"><span class="visually-hidden">Aktionen</span></th></tr>
      </thead>
      <tbody>
        @for (device of devices(); track device.id) {
          <tr>
            <td>{{ device.name }}<br /><small>{{ device.model ?? device.platform }}</small></td>
            <td>{{ device.kind === 'pool' ? 'Poolgerät' : 'Persönlich' }}</td>
            <td><eo-device-status [status]="device.status" /></td>
            <td><small>{{ device.appVersion ?? '–' }} · {{ device.osVersion ?? '–' }}</small></td>
            <td class="numeric">{{ date(device.lastSeenAt) }}</td>
            <td class="actions">
              @if (canManage()) {
                @switch (device.status) {
                  @case ('pending') {
                    <button class="button small primary" type="button" (click)="act(device, 'approve')">Freigeben</button>
                  }
                  @case ('active') {
                    <button class="button small" type="button" (click)="act(device, 'lock')">Sperren</button>
                  }
                  @case ('locked') {
                    <button class="button small" type="button" (click)="act(device, 'unlock')">Entsperren</button>
                  }
                }
                @if (device.status !== 'retired') {
                  <button class="button small danger" type="button" (click)="act(device, 'retire')">Entfernen</button>
                }
              }
            </td>
          </tr>
        } @empty {
          <tr><td colspan="6">Noch keine Geräte registriert.</td></tr>
        }
      </tbody>
    </table>

    <dialog #dialog aria-labelledby="enroll-title">
      <h2 id="enroll-title">Gerät einrichten</h2>
      @if (code(); as c) {
        <p>In der App „QR-Code scannen“ wählen. Der Code gilt bis {{ time(c.expiresAt) }} Uhr und nur einmal.</p>
        @if (qr()) {
          <img class="qr" [src]="qr()" alt="QR-Code zur Einrichtung" />
        } @else {
          <div class="message hint">Für einen QR-Code muss die öffentliche Serveradresse konfiguriert sein.</div>
        }
        <p>Code zum Abtippen: <code class="code" data-testid="enrollment-code">{{ c.code }}</code></p>
        <div class="actions"><button class="button" type="button" (click)="closeDialog()">Fertig</button></div>
      } @else {
        <form (ngSubmit)="createCode()">
          <fieldset class="field">
            <legend>Nutzung</legend>
            <label><input type="radio" name="kind" value="personal" [(ngModel)]="kind" /> Persönliches Gerät</label>
            <label><input type="radio" name="kind" value="pool" [(ngModel)]="kind" /> Poolgerät (mehrere Personen)</label>
          </fieldset>
          <div class="field">
            <label for="d-name">Gerätename (optional)</label>
            <input id="d-name" name="deviceName" placeholder="z. B. Fahrzeug 3" [(ngModel)]="deviceName" />
          </div>
          <p class="help">Ein Gerät, das mit diesem Code eingerichtet wird, ist sofort freigegeben.</p>
          <div class="actions">
            <button class="button primary" type="submit">Code erzeugen</button>
            <button class="button" type="button" (click)="closeDialog()">Abbrechen</button>
          </div>
        </form>
      }
    </dialog>
  `,
  styles: `
    .title { display: flex; justify-content: space-between; align-items: center; }
    small { color: var(--text-secondary); }
    .qr { width: 256px; height: 256px; display: block; margin: 8px auto 16px; background: #fff; padding: 8px; border-radius: 8px; }
    .code { font-size: 20px; letter-spacing: 0.08em; }
    fieldset { border: none; padding: 0; }
    fieldset label { display: block; padding: 4px 0; color: var(--text-primary); font-size: 15px; }
  `,
})
export class DevicesPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  protected readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly devices = signal<DeviceResponse[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly code = signal<EnrollmentCodeResponse | null>(null);
  protected readonly qr = signal<string | null>(null);
  protected readonly canManage = computed(() => this.auth.permissions().has('devices.manage'));
  protected readonly pendingCount = computed(() => this.devices().filter((d) => d.status === 'pending').length);
  protected readonly date = formatDateTime;
  protected kind: DeviceKind = 'personal';
  protected deviceName = '';

  constructor() {
    void this.reload();
  }

  protected async act(device: DeviceResponse, action: 'approve' | 'lock' | 'unlock' | 'retire'): Promise<void> {
    if (action === 'retire' && !confirm(`${device.name} entfernen? Alle Sitzungen des Geräts enden sofort; es muss neu eingerichtet werden.`)) {
      return;
    }
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post(`/api/v1/devices/${device.id}/${action}`, null));
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected openEnrollment(): void {
    this.code.set(null);
    this.qr.set(null);
    this.kind = 'personal';
    this.deviceName = '';
    this.dialog().nativeElement.showModal();
  }

  protected async createCode(): Promise<void> {
    try {
      const code = await firstValueFrom(this.http.post<EnrollmentCodeResponse>('/api/v1/devices/enrollment-codes',
        { kind: this.kind, deviceName: this.deviceName || null }));
      this.code.set(code);
      this.qr.set(code.link ? await QRCode.toDataURL(code.link, { width: 512, margin: 1, errorCorrectionLevel: 'M' }) : null);
    } catch (error) {
      this.error.set(problemMessage(error));
      this.closeDialog();
    }
  }

  protected closeDialog(): void {
    this.dialog().nativeElement.close();
    this.code.set(null);
    this.qr.set(null);
  }

  protected time(value: string): string {
    return new Intl.DateTimeFormat('de-DE', { timeStyle: 'short' }).format(new Date(value));
  }

  private async reload(): Promise<void> {
    try {
      this.devices.set(await firstValueFrom(this.http.get<DeviceResponse[]>('/api/v1/devices')));
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
