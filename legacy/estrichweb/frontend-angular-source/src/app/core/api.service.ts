import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, from, map, of, switchMap, tap } from 'rxjs';
import { finalize, shareReplay } from 'rxjs/operators';
import { EnterpriseModule } from './module-registry';

export type GenericRecord = Record<string, any>;

export interface ApiListResponse { items: GenericRecord[]; total?: number; module?: string; }
export interface DashboardStats { projects: number; sites: number; open_tasks: number; open_defects: number; documents: number; assignments_today: number; overdue: number; quality_open: number; }
export interface DashboardResponse { stats: DashboardStats; today: GenericRecord[]; my_today?: GenericRecord[]; modules: { key: string; label: string; count: number }[]; }
export interface LoginResponse { token: string; refresh_token?: string; user: { id: string; name: string; email: string; tenant_id: string; tenant_slug: string; tenant_name: string; permissions: string[]; must_change_password?: boolean }; }

@Injectable({ providedIn: 'root' })
export class ApiService {
  // ---------------------------------------------------------------------
  // Kurzzeit-Zwischenspeicher fuer haeufig wiederholte Lese-Endpunkte.
  //
  // Firmendaten und die Material-Zusammenfassung werden von mehreren
  // Komponenten und zusaetzlich bei jedem Seitenwechsel abgefragt. Auf
  // Shared-Hosting summiert sich das zu unnoetiger Last (bis hin zu 503).
  // Gleichzeitige Abfragen teilen sich hier eine laufende Anfrage, und das
  // Ergebnis bleibt fuer kurze Zeit gueltig.
  // ---------------------------------------------------------------------
  private readonly cacheValues = new Map<string, { at: number; value: any }>();
  private readonly cacheInFlight = new Map<string, Observable<any>>();

  private cachedGet<T>(key: string, ttlMs: number, request: () => Observable<T>): Observable<T> {
    const cached = this.cacheValues.get(key);
    if (cached && Date.now() - cached.at < ttlMs) return of(cached.value as T);
    const running = this.cacheInFlight.get(key);
    if (running) return running as Observable<T>;
    const shared = request().pipe(
      tap((value) => this.cacheValues.set(key, { at: Date.now(), value })),
      finalize(() => this.cacheInFlight.delete(key)),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    this.cacheInFlight.set(key, shared);
    return shared;
  }

  /** Zwischenspeicher gezielt leeren, z. B. nach dem Speichern von Einstellungen. */
  invalidateCache(prefix = ''): void {
    if (!prefix) { this.cacheValues.clear(); this.cacheInFlight.clear(); return; }
    Array.from(this.cacheValues.keys()).filter((k) => k.startsWith(prefix)).forEach((k) => this.cacheValues.delete(k));
    Array.from(this.cacheInFlight.keys()).filter((k) => k.startsWith(prefix)).forEach((k) => this.cacheInFlight.delete(k));
  }

  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  login(email: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.baseUrl}/auth/login`, { email, password });
  }

  refreshToken(refreshToken: string): Observable<{ token: string }> {
    return this.http.post<{ token: string }>(`${this.baseUrl}/auth/refresh`, { refresh_token: refreshToken });
  }

  microsoftSsoStatus(): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/auth/microsoft/status`);
  }

  microsoftSsoComplete(ticket: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.baseUrl}/auth/microsoft/complete`, { ticket });
  }

  dashboard(): Observable<DashboardResponse> {
    return this.http.get<DashboardResponse>(`${this.baseUrl}/dashboard`);
  }

  modules(): Observable<EnterpriseModule[]> {
    return this.http.get<{items: EnterpriseModule[]}>(`${this.baseUrl}/modules`).pipe(map((response) => response.items));
  }

  list(moduleKey: string, query = '', limit = 250, offset = 0, source = ''): Observable<ApiListResponse> {
    let params = new HttpParams().set('limit', String(limit)).set('offset', String(offset));
    if (query) params = params.set('q', query);
    if (source) params = params.set('source', source);
    return this.http.get<ApiListResponse>(`${this.baseUrl}/module/${moduleKey}`, { params });
  }

  sites(): Observable<GenericRecord[]> {
    return this.list('construction_sites', '', 250).pipe(map((response) => response.items || []));
  }

  get(moduleKey: string, id: string): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/module/${moduleKey}/${id}`);
  }

  createCustomerProjectSite(payload: GenericRecord): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/workflow/customer-project-site`, payload);
  }

  create(moduleKey: string, payload: GenericRecord): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/module/${moduleKey}`, payload);
  }

  update(moduleKey: string, id: string, payload: GenericRecord): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/module/${moduleKey}/${id}`, payload);
  }

  remove(moduleKey: string, id: string): Observable<{ ok: boolean }> {
    return this.http.delete<{ ok: boolean }>(`${this.baseUrl}/module/${moduleKey}/${id}`);
  }

  planningAssignments(from?: string, to?: string): Observable<ApiListResponse> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<ApiListResponse>(`${this.baseUrl}/planning/assignments`, { params });
  }

  checkPlanningConflicts(payload: GenericRecord): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/planning/assignments/check-conflicts`, payload);
  }

  uploadDocument(file: File, meta: GenericRecord): Observable<GenericRecord> {
    return from(this.compressImageFile(file)).pipe(switchMap((prepared) => {
      const body = new FormData();
      body.append('file', prepared, prepared.name);
      Object.entries(meta).forEach(([key, value]) => body.append(key, String(value ?? '')));
      return this.http.post<GenericRecord>(`${this.baseUrl}/documents/upload`, body);
    }));
  }

  saveDailyDocumentation(meta: GenericRecord, photos: File[], videos: File[] = []): Observable<GenericRecord> {
    return from(Promise.all(photos.map((file) => this.compressImageFile(file)))).pipe(switchMap((preparedPhotos) => {
      const body = new FormData();
      Object.entries(meta).forEach(([key, value]) => body.append(key, String(value ?? '')));
      preparedPhotos.forEach((file) => body.append('photos[]', file, file.name));
      videos.forEach((file) => body.append('videos[]', file, file.name));
      return this.http.post<GenericRecord>(`${this.baseUrl}/daily-documentation`, body);
    }));
  }

  saveInitialInspection(payload: GenericRecord, photos: File[] = [], videos: File[] = []): Observable<GenericRecord> {
    if (!photos.length && !videos.length) return this.http.post<GenericRecord>(`${this.baseUrl}/inspections/initial`, payload);
    return from(Promise.all(photos.map((file) => this.compressImageFile(file)))).pipe(switchMap((preparedPhotos) => {
      const body = new FormData();
      body.append('payload', JSON.stringify(payload));
      preparedPhotos.forEach((file) => body.append('photos[]', file, file.name));
      videos.forEach((file) => body.append('videos[]', file, file.name));
      return this.http.post<GenericRecord>(`${this.baseUrl}/inspections/initial`, body);
    }));
  }

  private webpSupported: boolean | null = null;
  private canEncodeWebp(): boolean {
    if (this.webpSupported !== null) return this.webpSupported;
    try {
      const c = document.createElement('canvas'); c.width = 1; c.height = 1;
      this.webpSupported = c.toDataURL('image/webp').startsWith('data:image/webp');
    } catch { this.webpSupported = false; }
    return this.webpSupported;
  }

  private async compressImageFile(file: File): Promise<File> {
    if (!file.type.startsWith('image/')) return file;
    try {
      const dataUrl = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result || ''));
        reader.onerror = () => reject(reader.error || new Error('Datei konnte nicht gelesen werden'));
        reader.readAsDataURL(file);
      });
      const img = await new Promise<HTMLImageElement>((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = reject;
        image.src = dataUrl;
      });
      const maxSide = 1800;
      const scale = Math.min(1, maxSide / Math.max(img.width, img.height));
      // Bereits klein genug? Dann nur durchreichen (spart Rechenzeit).
      if (scale >= 1 && file.size < 900_000) return file;
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round(img.width * scale));
      canvas.height = Math.max(1, Math.round(img.height * scale));
      const ctx = canvas.getContext('2d');
      if (!ctx) return file;
      ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
      // WebP ist bei gleicher Qualität deutlich kleiner; sonst JPEG als Fallback.
      const useWebp = this.canEncodeWebp();
      const mime = useWebp ? 'image/webp' : 'image/jpeg';
      const quality = useWebp ? 0.8 : 0.82;
      const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, mime, quality));
      if (!blob) return file;
      // Nur nehmen, wenn tatsächlich kleiner als das Original.
      if (blob.size >= file.size && scale >= 1) return file;
      const ext = useWebp ? '.webp' : '.jpg';
      const name = file.name.replace(/\.(heic|heif|png|webp|jpg|jpeg)$/i, '') + ext;
      return new File([blob], name, { type: mime, lastModified: Date.now() });
    } catch (_err) {
      return file;
    }
  }


  lexwareStatus(): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/integrations/lexware/config`);
  }

  lexwareSaveConfig(payload: GenericRecord): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/integrations/lexware/config`, payload);
  }

  lexwareTest(): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/integrations/lexware/test`, {});
  }

  lexwareSyncCustomers(): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/integrations/lexware/sync-customers`, {});
  }
  lexwareSyncCustomersChunk(page: number): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/integrations/lexware/sync-customers-chunk`, { page });
  }

  lexwareSyncProjects(): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/integrations/lexware/sync-projects`, {});
  }

  lexwareSyncDocuments(): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/integrations/lexware/sync-documents`, {});
  }

  lexwareAutoSync(force = false): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/integrations/lexware/auto-sync`, { force });
  }

  lexwareSyncedRecords(type = ''): Observable<ApiListResponse> {
    let params = new HttpParams();
    if (type) params = params.set('type', type);
    return this.http.get<ApiListResponse>(`${this.baseUrl}/integrations/lexware/synced-records`, { params });
  }

  lexwareQuotations(): Observable<ApiListResponse> {
    return this.http.get<ApiListResponse>(`${this.baseUrl}/integrations/lexware/quotations`);
  }

  lexwareLinkQuotation(projectId: string, quotationId: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/integrations/lexware/link-quotation`, { project_id: projectId, quotation_id: quotationId });
  }

  lexwareLink(payload: GenericRecord): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/integrations/lexware/link`, payload);
  }

  projectHistory(projectId: string): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/projects/${projectId}/history`);
  }

  projectPresence(projectId: string): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/projects/${projectId}/presence`);
  }

  pingProjectPresence(projectId: string, siteId?: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/projects/${projectId}/presence`, { site_id: siteId || null, screen: 'auftragsakte' });
  }


  adminMicrosoftSso(): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/admin/sso/microsoft`);
  }

  adminSaveMicrosoftSso(payload: GenericRecord): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/admin/sso/microsoft`, payload);
  }

  lexwareContacts(query = ''): Observable<GenericRecord> {
    let params = new HttpParams().set('size', '25');
    if (query) params = params.set('q', query);
    return this.http.get<GenericRecord>(`${this.baseUrl}/integrations/lexware/contacts`, { params });
  }

  adminUserRoles(): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/admin/user-roles`);
  }

  updateUserRoles(userId: string, roleIds: string[]): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/admin/users/${userId}/roles`, { role_ids: roleIds });
  }

  adminCreateUser(payload: { name: string; email: string; password: string; is_active: boolean; role_ids: string[]; must_change_password?: boolean }): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/admin/users`, payload);
  }
  adminUpdateUser(userId: string, payload: GenericRecord): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/admin/users/${userId}`, payload);
  }
  changeOwnPassword(currentPassword: string, newPassword: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/me/change-password`, { current_password: currentPassword, new_password: newPassword });
  }
  customerPortalStatus(customerId: string): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/admin/customers/${customerId}/portal`);
  }
  customerPortalGenerate(customerId: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/admin/customers/${customerId}/portal`, {});
  }
  customerPortalRevoke(customerId: string): Observable<GenericRecord> {
    return this.http.delete<GenericRecord>(`${this.baseUrl}/admin/customers/${customerId}/portal`);
  }
  customerPortalData(token: string): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/public/portal/${token}`);
  }
  customerPortalVerify(token: string, pin: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/public/portal/${token}/verify`, { pin });
  }
  customerPortalDocumentUrl(token: string, documentId: string, pin: string, download = false): string {
    const params = new URLSearchParams({ pin });
    if (download) params.set('download', '1');
    return `${this.baseUrl}/public/portal/${token}/document/${documentId}?${params.toString()}`;
  }
  listCalls(kind = ''): Observable<ApiListResponse> {
    let params = new HttpParams();
    if (kind) params = params.set('kind', kind);
    return this.http.get<ApiListResponse>(`${this.baseUrl}/calls`, { params });
  }
  /** Anmeldung mit Gerätekennung und PIN. */
  quickLoginSubmit(deviceId: string, pin: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.baseUrl}/auth/quick/login`, { device_id: deviceId, pin });
  }
  /** Steht auf diesem Gerät eine PIN-Anmeldung bereit? */
  quickLoginStatus(deviceId: string): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/auth/quick/status`, { params: { device_id: deviceId } });
  }
  /** Schnellanmeldung für dieses Gerät einrichten. */
  setQuickLogin(deviceId: string, pin: string, label: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/me/quick-login`, { device_id: deviceId, pin, device_label: label });
  }
  /** Schnellanmeldung aufheben – für dieses oder alle Geräte. */
  removeQuickLogin(deviceId?: string): Observable<GenericRecord> {
    const params: Record<string, string> = deviceId ? { device_id: deviceId } : {};
    return this.http.delete<GenericRecord>(`${this.baseUrl}/me/quick-login`, { params });
  }
  /** Eigene Geräte mit Schnellanmeldung. */
  quickLoginDevices(): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/me/quick-login`);
  }

  /** Eigene Kontodaten für die Profilseite. */
  me(): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/me`);
  }
  /** Eigenes Passwort ändern. */
  createCall(payload: GenericRecord): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/calls`, payload);
  }
  updateCall(id: string, payload: GenericRecord): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/calls/${id}`, payload);
  }
  deleteCall(id: string): Observable<GenericRecord> {
    return this.http.delete<GenericRecord>(`${this.baseUrl}/calls/${id}`);
  }
  cancelInquiry(id: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/inquiries/${id}/cancel`, {});
  }

  getCall(id: string): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/calls/${id}`);
  }
  createInquiryFromCall(id: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/calls/${id}/create-inquiry`, {});
  }
  updateInquiryStatus(id: string, status: 'open' | 'in_progress' | 'converted' | 'cancelled'): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/calls/${id}`, { inquiry_status: status });
  }

  materialOrders(limit = 300, status = '', q = ''): Observable<ApiListResponse & { summary?: GenericRecord }> {
    let params = new HttpParams().set('limit', String(limit));
    if (status) params = params.set('status', status);
    if (q) params = params.set('q', q);
    return this.http.get<ApiListResponse & { summary?: GenericRecord }>(`${this.baseUrl}/material-orders`, { params });
  }
  materialOrdersSummary(): Observable<GenericRecord> {
    return this.cachedGet('material-orders-summary', 30000, () => this.http.get<GenericRecord>(`${this.baseUrl}/material-orders/summary`));
  }
  projectMaterialOrders(projectId: string): Observable<{ items: GenericRecord[] }> {
    return this.http.get<{ items: GenericRecord[] }>(`${this.baseUrl}/projects/${projectId}/material-orders`);
  }
  createProjectMaterialOrder(projectId: string, payload: GenericRecord): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/projects/${projectId}/material-orders`, payload);
  }
  updateMaterialOrder(id: string, payload: GenericRecord): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/material-orders/${id}`, payload);
  }
  updateMaterialOrderStatus(id: string, status: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/material-orders/${id}/status`, { status });
  }
  deleteMaterialOrder(id: string): Observable<GenericRecord> {
    return this.http.delete<GenericRecord>(`${this.baseUrl}/material-orders/${id}`);
  }

  adminRoles(): Observable<ApiListResponse> {
    return this.http.get<ApiListResponse>(`${this.baseUrl}/admin/roles`);
  }
  adminPermissions(): Observable<ApiListResponse> {
    return this.http.get<ApiListResponse>(`${this.baseUrl}/admin/permissions`);
  }
  adminCreateRole(payload: GenericRecord): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/admin/roles`, payload);
  }
  adminSetRolePermissions(roleId: string, permissions: string[]): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/admin/roles/${roleId}/permissions`, { permissions });
  }

  siteFile(projectId: string): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/projects/${projectId}/file`);
  }

  documentViewUrl(id: string): string {
    const token = encodeURIComponent(localStorage.getItem('estrichweb.jwt') || '');
    return `${this.baseUrl}/documents/${id}/view?access_token=${token}&v=${Date.now()}`;
  }
  documentDownloadUrl(id: string): string {
    return `${this.documentViewUrl(id)}&download=1`;
  }
  assignProject(projectId: string, userId: string | null): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/projects/${projectId}/assignee`, { assigned_user_id: userId });
  }

  cancelProject(projectId: string, password: string, reason: string = ''): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/projects/${projectId}/cancel`, { password, reason });
  }
  deleteProject(projectId: string, password: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/projects/${projectId}/delete`, { password });
  }

  photoViewUrl(id: string, variant = 'main'): string {
    return `${this.baseUrl}/photos/${id}/view?variant=${encodeURIComponent(variant)}&access_token=${encodeURIComponent(localStorage.getItem('estrichweb.jwt') || '')}`;
  }

  shareDocument(documentId: string, email: string, canDownload = false): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/documents/${documentId}/shares`, { email, can_download: canDownload });
  }

  syncOffline(operations: GenericRecord[]): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/offline/sync`, { operations });
  }

  // ---- Notizen / Verlauf je Bauakte ----
  projectNotes(projectId: string): Observable<{ items: GenericRecord[] }> {
    return this.http.get<{ items: GenericRecord[] }>(`${this.baseUrl}/projects/${projectId}/notes`);
  }
  /** Anmerkung anheften, umfärben, mit Fälligkeit versehen oder Text ändern. */
  updateProjectNote(projectId: string, noteId: string, payload: GenericRecord): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/projects/${projectId}/notes/${noteId}`, payload);
  }
  addProjectNote(projectId: string, body: string, file?: File | null, extra?: GenericRecord): Observable<GenericRecord> {
    if (file) {
      return from(this.compressImageFile(file)).pipe(switchMap((prepared) => {
        const form = new FormData();
        form.append('body', body);
        form.append('file', prepared, prepared.name);
        Object.entries(extra || {}).forEach(([k, v]) => form.append(k, String(v ?? '')));
        return this.http.post<GenericRecord>(`${this.baseUrl}/projects/${projectId}/notes`, form);
      }));
    }
    return this.http.post<GenericRecord>(`${this.baseUrl}/projects/${projectId}/notes`, { body, ...(extra || {}) });
  }
  deleteProjectNote(projectId: string, noteId: string): Observable<GenericRecord> {
    return this.http.delete<GenericRecord>(`${this.baseUrl}/projects/${projectId}/notes/${noteId}`);
  }

  // ---- App-/Firmeneinstellungen, Meldungen, Changelog ----
  publicAppInfo(): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/public/app-info`);
  }
  appSettings(): Observable<GenericRecord> {
    return this.cachedGet('settings-app', 300000, () => this.http.get<GenericRecord>(`${this.baseUrl}/settings/app`));
  }
  saveAppSetting(key: string, value: unknown): Observable<GenericRecord> {
    // Nach dem Speichern muss der Zwischenspeicher weg, sonst zeigt die
    // Oberflaeche noch die alten Firmendaten.
    return this.http.put<GenericRecord>(`${this.baseUrl}/settings/app/${key}`, { value }).pipe(tap(() => this.invalidateCache('settings-app')));
  }
  getEmailSettings(): Observable<GenericRecord> {
    return this.http.get<GenericRecord>(`${this.baseUrl}/admin/email-settings`);
  }
  saveEmailSettings(value: unknown): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/settings/app/email`, { value });
  }
  testEmail(to?: string): Observable<GenericRecord> {
    return this.http.post<GenericRecord>(`${this.baseUrl}/admin/email-settings/test`, to ? { to } : {});
  }
  setDocumentPortalVisibility(documentId: string, visible: boolean): Observable<GenericRecord> {
    return this.http.put<GenericRecord>(`${this.baseUrl}/documents/${documentId}/portal-visibility`, { customer_visible: visible });
  }

  // ---- Fahrzeug-Dokumente ----
  vehicleDocuments(vehicleId: string): Observable<{ items: GenericRecord[] }> {
    return this.http.get<{ items: GenericRecord[] }>(`${this.baseUrl}/vehicles/${vehicleId}/documents`);
  }
  uploadVehicleDocument(vehicleId: string, file: File, category = 'fahrzeug'): Observable<GenericRecord> {
    return from(this.compressImageFile(file)).pipe(switchMap((prepared) => {
      const body = new FormData();
      body.append('file', prepared, prepared.name);
      body.append('category', category);
      body.append('name', prepared.name);
      return this.http.post<GenericRecord>(`${this.baseUrl}/vehicles/${vehicleId}/documents`, body);
    }));
  }
  deleteVehicleDocument(vehicleId: string, docId: string): Observable<GenericRecord> {
    return this.http.delete<GenericRecord>(`${this.baseUrl}/vehicles/${vehicleId}/documents/${docId}`);
  }
}
