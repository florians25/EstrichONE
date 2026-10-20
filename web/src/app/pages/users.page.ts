import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { RoleResponse, UserListItem, UserWithPassword } from '../core/api.models';
import { problemMessage } from '../core/problem';
import { formatDateTime } from '../shared/status';

interface UserForm {
  id: string | null;
  displayName: string;
  userName: string;
  email: string;
  phone: string;
  roleIds: Set<string>;
}

/** Benutzer verwalten (docs/reengineering/17: Einmalpasswort + Zwangswechsel, keine Passwörter per E-Mail). */
@Component({
  selector: 'eo-users',
  imports: [FormsModule],
  template: `
    <div class="title">
      <h1>Benutzer</h1>
      <button class="button primary" type="button" (click)="openCreate()">Benutzer anlegen</button>
    </div>
    @if (error()) {
      <div class="message error" role="alert">{{ error() }}</div>
    }
    @if (oneTime(); as result) {
      <div class="message success" role="status" data-testid="one-time-password">
        Einmalpasswort für <strong>{{ result.user.displayName }}</strong>:
        <code class="otp">{{ result.oneTimePassword }}</code>
        <button class="button small" type="button" (click)="copy(result.oneTimePassword)">Kopieren</button>
        <br />Es wird nur jetzt angezeigt. Bei der ersten Anmeldung muss ein eigenes Passwort gewählt werden.
      </div>
    }
    <table class="table">
      <thead>
        <tr><th scope="col">Name</th><th scope="col">Anmeldename</th><th scope="col">Rollen</th><th scope="col">Status</th>
          <th scope="col">Letzte Anmeldung</th><th scope="col"><span class="visually-hidden">Aktionen</span></th></tr>
      </thead>
      <tbody>
        @for (user of users(); track user.id) {
          <tr>
            <td>{{ user.displayName }}<br /><small>{{ user.email ?? '' }}</small></td>
            <td>{{ user.userName }}</td>
            <td>{{ roleNames(user) }}</td>
            <td>
              @if (user.isActive) {
                <span class="status success">Aktiv</span>
              } @else {
                <span class="status error">Gesperrt</span>
              }
            </td>
            <td class="numeric">{{ date(user.lastLoginAt) }}</td>
            <td class="actions">
              <button class="button small" type="button" (click)="openEdit(user)">Bearbeiten</button>
              <button class="button small" type="button" (click)="resetPassword(user)">Passwort zurücksetzen</button>
              @if (user.isActive) {
                <button class="button small danger" type="button" (click)="setActive(user, false)">Sperren</button>
              } @else {
                <button class="button small" type="button" (click)="setActive(user, true)">Entsperren</button>
              }
            </td>
          </tr>
        }
      </tbody>
    </table>

    <dialog #dialog aria-labelledby="user-dialog-title" (close)="form.set(null)">
      @if (form(); as f) {
        <form (ngSubmit)="save()">
          <h2 id="user-dialog-title">{{ f.id ? 'Benutzer bearbeiten' : 'Benutzer anlegen' }}</h2>
          <div class="field">
            <label for="u-name">Name</label>
            <input id="u-name" name="displayName" required [(ngModel)]="f.displayName" />
          </div>
          <div class="field">
            <label for="u-user">Anmeldename</label>
            <input id="u-user" name="userName" required [disabled]="!!f.id" [(ngModel)]="f.userName" />
            <span class="help">Für Mitarbeiter ohne E-Mail-Adresse, z. B. „m.huber“.</span>
          </div>
          <div class="field">
            <label for="u-email">E-Mail (optional)</label>
            <input id="u-email" name="email" type="email" [(ngModel)]="f.email" />
          </div>
          <div class="field">
            <label for="u-phone">Telefon (optional)</label>
            <input id="u-phone" name="phone" [(ngModel)]="f.phone" />
          </div>
          <fieldset class="field">
            <legend>Rollen</legend>
            @for (role of roles(); track role.id) {
              <label class="check">
                <input type="checkbox" [checked]="f.roleIds.has(role.id)" (change)="toggleRole(f, role.id)" />
                <span><strong>{{ role.name }}</strong><br /><small>{{ role.description }}</small></span>
              </label>
            }
          </fieldset>
          @if (dialogError()) {
            <div class="message error" role="alert">{{ dialogError() }}</div>
          }
          <div class="actions">
            <button class="button primary" type="submit" [disabled]="busy()">Speichern</button>
            <button class="button" type="button" (click)="dialog.close()">Abbrechen</button>
          </div>
        </form>
      }
    </dialog>
  `,
  styles: `
    .title { display: flex; justify-content: space-between; align-items: center; }
    .otp { font-size: 17px; padding: 2px 6px; background: var(--surface); border-radius: 4px; margin: 0 8px; }
    fieldset { border: 1px solid var(--line); border-radius: var(--radius-control); padding: 8px 12px; }
    .check { display: flex; gap: 8px; align-items: flex-start; padding: 6px 0; color: var(--text-primary) !important; font-size: 15px !important; font-weight: 400 !important; }
    small { color: var(--text-secondary); }
  `,
})
export class UsersPage {
  private readonly http = inject(HttpClient);
  protected readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly users = signal<UserListItem[]>([]);
  protected readonly roles = signal<RoleResponse[]>([]);
  protected readonly form = signal<UserForm | null>(null);
  protected readonly oneTime = signal<UserWithPassword | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly date = formatDateTime;
  private readonly roleById = computed(() => new Map(this.roles().map((role) => [role.id, role.name])));

  constructor() {
    void this.reload();
  }

  protected roleNames(user: UserListItem): string {
    return user.roleIds.map((id) => this.roleById().get(id) ?? '?').join(', ') || '–';
  }

  protected openCreate(): void {
    this.dialogError.set(null);
    this.form.set({ id: null, displayName: '', userName: '', email: '', phone: '', roleIds: new Set() });
    this.dialog().nativeElement.showModal();
  }

  protected openEdit(user: UserListItem): void {
    this.dialogError.set(null);
    this.form.set({ id: user.id, displayName: user.displayName, userName: user.userName, email: user.email ?? '',
      phone: user.phone ?? '', roleIds: new Set(user.roleIds) });
    this.dialog().nativeElement.showModal();
  }

  protected toggleRole(form: UserForm, roleId: string): void {
    if (form.roleIds.has(roleId)) {
      form.roleIds.delete(roleId);
    } else {
      form.roleIds.add(roleId);
    }
  }

  protected async save(): Promise<void> {
    const f = this.form();
    if (!f) {
      return;
    }
    this.busy.set(true);
    this.dialogError.set(null);
    const contact = { displayName: f.displayName, email: f.email || null, phone: f.phone || null };
    try {
      if (f.id) {
        await firstValueFrom(this.http.put(`/api/v1/users/${f.id}`, contact));
        await firstValueFrom(this.http.put(`/api/v1/access/users/${f.id}/roles`, { roleIds: [...f.roleIds] }));
      } else {
        const created = await firstValueFrom(this.http.post<UserWithPassword>('/api/v1/users',
          { ...contact, userName: f.userName, roleIds: [...f.roleIds] }));
        this.oneTime.set(created);
      }
      this.dialog().nativeElement.close();
      await this.reload();
    } catch (error) {
      this.dialogError.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async resetPassword(user: UserListItem): Promise<void> {
    if (!confirm(`Passwort von ${user.displayName} zurücksetzen? Alle Sitzungen der Person werden beendet.`)) {
      return;
    }
    await this.run(async () => this.oneTime.set(
      await firstValueFrom(this.http.post<UserWithPassword>(`/api/v1/users/${user.id}/reset-password`, null))));
  }

  protected async setActive(user: UserListItem, active: boolean): Promise<void> {
    if (!active && !confirm(`${user.displayName} sperren? Die Person wird sofort auf allen Geräten abgemeldet.`)) {
      return;
    }
    await this.run(() => firstValueFrom(this.http.post(`/api/v1/users/${user.id}/${active ? 'activate' : 'deactivate'}`, null)));
  }

  protected copy(text: string): void {
    void navigator.clipboard?.writeText(text);
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.error.set(null);
    try {
      await action();
      await this.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  private async reload(): Promise<void> {
    try {
      const [users, roles] = await Promise.all([
        firstValueFrom(this.http.get<UserListItem[]>('/api/v1/users')),
        firstValueFrom(this.http.get<RoleResponse[]>('/api/v1/access/roles')),
      ]);
      this.users.set(users);
      this.roles.set(roles);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }
}
