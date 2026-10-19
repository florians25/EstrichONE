import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { Observable, catchError, switchMap, throwError, of, from, timer } from 'rxjs';
import { finalize, retry, shareReplay } from 'rxjs/operators';

const STORAGE_KEYS = ['estrichweb.jwt', 'estrichweb.refresh', 'estrichweb.tenantId', 'estrichweb.tenantSlug', 'estrichweb.user'];

function decodeJwtPayload(token: string): any | null {
  try {
    const part = token.split('.')[1];
    if (!part) return null;
    const normalized = part.replace(/-/g, '+').replace(/_/g, '/');
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
    return JSON.parse(atob(padded));
  } catch {
    return null;
  }
}

function tokenExpiredOrInvalid(token: string): boolean {
  const payload = decodeJwtPayload(token);
  return !payload || !payload.exp || !payload.sub || !payload.tenant_id || payload.exp * 1000 <= Date.now() + 15000;
}

function clearSession(): void {
  STORAGE_KEYS.forEach((key) => localStorage.removeItem(key));
}

function isAuthLogin(url: string): boolean { return url.includes('/auth/login'); }
function isAuthRefresh(url: string): boolean { return url.includes('/auth/refresh'); }
function isApiRequest(url: string): boolean { return url.startsWith('/api') || url.includes('/api/'); }
// Öffentliche Endpunkte (kein Login nötig) – Interceptor darf hier NICHT auf /login umleiten.
function isPublicEndpoint(url: string): boolean {
  return url.includes('/public/portal/') || url.includes('/public/app-info') || url.includes('/auth/');
}
function preservesUnsavedWork(url: string): boolean {
  return url.includes('/daily-documentation') || url.includes('/documents/upload') || url.includes('/inspections/initial') || url.includes('/notes');
}

// ---------------------------------------------------------------------------
// Gleichzeitige Anfragen begrenzen.
//
// Auf Shared-Hosting (STRATO u. a.) steht nur eine kleine Zahl paralleler
// PHP-Prozesse zur Verfuegung. Beim Seitenaufbau feuert die Anwendung aber
// mehrere forkJoin-Buendel gleichzeitig ab (Bauakte, Dashboard, Sidebar-Badge).
// Sind alle Prozesse belegt, antwortet der Webserver fuer JEDE weitere Anfrage
// sofort mit 503 – genau das Fehlerbild "503 in 0 ms auf allen Endpunkten".
// Deshalb werden API-Anfragen hier auf wenige gleichzeitige begrenzt und der
// Rest kurz in eine Warteschlange gestellt.
// ---------------------------------------------------------------------------
const MAX_PARALLEL_API_REQUESTS = 4;
let activeRequests = 0;
const waitingRequests: Array<() => void> = [];

function acquireSlot(): Promise<void> {
  if (activeRequests < MAX_PARALLEL_API_REQUESTS) { activeRequests += 1; return Promise.resolve(); }
  return new Promise<void>((resolve) => waitingRequests.push(() => { activeRequests += 1; resolve(); }));
}
function releaseSlot(): void {
  activeRequests = Math.max(0, activeRequests - 1);
  const next = waitingRequests.shift();
  if (next) next();
}

// Anmeldung/Refresh duerfen NIE in der Warteschlange haengen, sonst kann sich
// ein Refresh, der innerhalb einer laufenden Anfrage startet, selbst blockieren.
function bypassesQueue(url: string): boolean {
  return url.includes('/auth/') || url.includes('/documents/') || url.includes('/photos/');
}

// Nur lesende Anfragen duerfen wiederholt werden – niemals Schreibvorgaenge.
function isRetryableRequest(req: HttpRequest<unknown>): boolean {
  return req.method === 'GET' && !isAuthLogin(req.url) && !isAuthRefresh(req.url);
}

// Geteilter, laufender Refresh-Aufruf, damit parallele 401s nur EINEN Refresh ausloesen.
let refreshInFlight: Observable<string> | null = null;

// Drosselung für die "keine Verbindung"-Meldung, damit nicht bei jedem abgebrochenen
// Request eine Snackbar erscheint.
let lastConnErrorAt = 0;

function doRefresh(http: HttpClient): Observable<string> {
  if (refreshInFlight) return refreshInFlight;
  const refreshToken = localStorage.getItem('estrichweb.refresh') || '';
  if (!refreshToken) return throwError(() => new Error('no refresh token'));
  refreshInFlight = http.post<{ token: string }>('/api/auth/refresh', { refresh_token: refreshToken }).pipe(
    switchMap((res) => {
      const token = res?.token || '';
      if (!token) return throwError(() => new Error('refresh failed'));
      localStorage.setItem('estrichweb.jwt', token);
      return of(token);
    }),
    shareReplay(1)
  );
  refreshInFlight.subscribe({ error: () => { refreshInFlight = null; }, complete: () => { setTimeout(() => (refreshInFlight = null), 0); } });
  return refreshInFlight;
}

function applyHeaders(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  const tenantId = localStorage.getItem('estrichweb.tenantId');
  const tenantSlug = localStorage.getItem('estrichweb.tenantSlug');
  let headers = req.headers.set('X-Requested-With', 'XMLHttpRequest');
  if (token) {
    headers = headers.set('Authorization', `Bearer ${token}`);
    headers = headers.set('X-Authorization', `Bearer ${token}`);
    headers = headers.set('X-EW-Authorization', token);
  }
  if (tenantId) headers = headers.set('X-Tenant-Id', tenantId);
  if (tenantSlug) headers = headers.set('X-Tenant-Slug', tenantSlug);
  return req.clone({ headers });
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const http = inject(HttpClient);
  const snack = inject(MatSnackBar);
  const token = localStorage.getItem('estrichweb.jwt') || '';
  const hasRefresh = !!(localStorage.getItem('estrichweb.refresh') || '');

  if (isAuthLogin(req.url) || isAuthRefresh(req.url)) {
    return next(applyHeaders(req, token));
  }

  // Öffentliche Endpunkte (Kundenportal etc.) ohne Token durchlassen – kein Redirect auf /login.
  if (isPublicEndpoint(req.url)) {
    return next(req.clone({ headers: req.headers.set('X-Requested-With', 'XMLHttpRequest') }));
  }

  // Access-Token abgelaufen? -> erst Refresh versuchen, sonst abmelden.
  if (isApiRequest(req.url) && (!token || tokenExpiredOrInvalid(token))) {
    if (hasRefresh) {
      return doRefresh(http).pipe(
        switchMap((fresh) => next(applyHeaders(req, fresh))),
        catchError(() => { clearSession(); void router.navigate(['/login']); return throwError(() => new HttpErrorResponse({ status: 401 })); })
      );
    }
    clearSession();
    void router.navigate(['/login']);
    return throwError(() => new HttpErrorResponse({ status: 401 }));
  }

  // Anfrage durch die Warteschlange schicken und bei Ueberlast (503/429)
  // gestaffelt wiederholen, statt sie dem Benutzer als Fehler zu zeigen.
  const send = (): Observable<any> => {
    if (bypassesQueue(req.url)) return next(applyHeaders(req, token));
    return from(acquireSlot()).pipe(
      switchMap(() => next(applyHeaders(req, token))),
      finalize(() => releaseSlot())
    );
  };

  return send().pipe(
    retry({
      count: isRetryableRequest(req) ? 3 : 0,
      delay: (error: HttpErrorResponse, attempt: number) => {
        // Nur bei Ueberlast/Wartung erneut versuchen: 503 (nicht verfuegbar),
        // 429 (zu viele Anfragen), 502/504 (Gateway ueberlastet).
        if (![429, 502, 503, 504].includes(error?.status)) return throwError(() => error);
        // 400 ms, 900 ms, 1900 ms – mit etwas Streuung gegen Gleichtakt.
        const wait = 400 * Math.pow(2, attempt - 1) + Math.random() * 250;
        return timer(wait);
      }
    }),
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !isAuthLogin(req.url) && !isAuthRefresh(req.url)) {
        if (hasRefresh) {
          return doRefresh(http).pipe(
            switchMap((fresh) => next(applyHeaders(req, fresh))),
            catchError(() => {
              if (!preservesUnsavedWork(req.url)) { clearSession(); void router.navigate(['/login']); }
              return throwError(() => error);
            })
          );
        }
        if (!preservesUnsavedWork(req.url)) { clearSession(); void router.navigate(['/login']); }
      }
      if (error.status === 403 && !isPublicEndpoint(req.url)) {
        const body: any = error.error;
        let msg = 'Keine Berechtigung: Für diese Aktion fehlt Ihnen die Freigabe.';
        if (typeof body === 'string' && body.trim() && !body.trim().startsWith('{')) msg = body;
        else if (body && typeof body.error === 'string') msg = body.error;
        else if (body && typeof body.message === 'string') msg = body.message;
        // Per Mikrotask anzeigen, damit die klare Meldung eine evtl. generische
        // Komponenten-Meldung überschreibt (Snackbar zeigt nur eine zugleich).
        setTimeout(() => snack.open(msg, 'OK', { duration: 6000 }), 0);
      } else if (error.status === 0 && !isPublicEndpoint(req.url)) {
        // status 0 entsteht nicht nur bei echtem Verbindungsverlust, sondern auch bei
        // abgebrochenen/überholten Requests (Navigation, switchMap-Wechsel, Zerstörung der
        // Komponente). Diese dürfen KEINE "Verbindung verloren"-Meldung auslösen.
        const raw: any = error.error;
        const aborted = typeof ProgressEvent !== 'undefined' && raw instanceof ProgressEvent && raw.type !== 'error';
        const offline = typeof navigator !== 'undefined' && 'onLine' in navigator ? navigator.onLine === false : false;
        const now = Date.now();
        // Nur melden, wenn wirklich offline ODER wenn seit der letzten Meldung genug Zeit
        // vergangen ist (echter, dauerhafter Serverausfall) – und nie bei Abbrüchen.
        if (!aborted && (offline || now - lastConnErrorAt > 20000)) {
          lastConnErrorAt = now;
          const msg = offline
            ? 'Keine Internetverbindung. Sobald Sie wieder online sind, funktioniert alles normal.'
            : 'Server derzeit nicht erreichbar. Bitte in Kürze erneut versuchen.';
          setTimeout(() => snack.open(msg, 'OK', { duration: 5000 }), 0);
        }
      } else if ([429, 502, 503, 504].includes(error.status)) {
        // Nach mehreren vergeblichen Versuchen: Ueberlast klar benennen.
        const now = Date.now();
        if (now - lastConnErrorAt > 20000) {
          lastConnErrorAt = now;
          setTimeout(() => snack.open('Der Server ist gerade ausgelastet. Bitte einen Moment warten und erneut versuchen.', 'OK', { duration: 6000 }), 0);
        }
      } else if (error.status >= 500) {
        const body: any = error.error;
        const detail = (body && typeof body.error === 'string') ? body.error : '';
        setTimeout(() => snack.open('Serverfehler' + (detail ? ': ' + detail : '. Bitte später erneut versuchen.'), 'OK', { duration: 6000 }), 0);
      } else if ((error.status === 422 || error.status === 400 || error.status === 409) && !isPublicEndpoint(req.url)) {
        const body: any = error.error;
        const detail = (typeof body === 'string' && body.trim() && !body.trim().startsWith('{')) ? body : ((body && typeof body.error === 'string') ? body.error : '');
        if (detail) setTimeout(() => snack.open(detail, 'OK', { duration: 6000 }), 0);
      }
      return throwError(() => error);
    })
  );
};
