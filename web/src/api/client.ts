import type { Problem } from './models';

/** Fehler einer API-Anfrage mit verständlicher Meldung (RFC 9457, stabiler `code`). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
    readonly problem?: Problem,
  ) {
    super(message);
  }
}

type Listener = (reason: 'expired' | 'disabled') => void;
let sessionEnded: Listener = () => undefined;
let refreshing: Promise<boolean> | null = null;

/** Wird vom Anmeldezustand gesetzt: Was passiert, wenn die Sitzung endet. */
export function onSessionEnded(listener: Listener): void {
  sessionEnded = listener;
}

function xsrfToken(): string | undefined {
  return document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='))?.split('=')[1];
}

export function problemMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message;
  }
  if (error instanceof TypeError) {
    return 'Der Server ist nicht erreichbar. Bitte die Verbindung prüfen und erneut versuchen.';
  }
  return 'Ein unerwarteter Fehler ist aufgetreten.';
}

async function toError(response: Response): Promise<ApiError> {
  let problem: Problem | undefined;
  try {
    problem = (await response.json()) as Problem;
  } catch {
    problem = undefined;
  }
  const firstValidation = problem?.errors ? Object.values(problem.errors).flat()[0] : undefined;
  const message = firstValidation ?? problem?.detail ?? problem?.title ?? `Die Anfrage ist fehlgeschlagen (Status ${response.status}).`;
  return new ApiError(response.status, problem?.code, message, problem);
}

/** Sitzung einmal erneuern – auch bei parallelen Anfragen nur einmal (Refresh-Cookie, HttpOnly). */
function refresh(): Promise<boolean> {
  refreshing ??= fetch('/api/v1/auth/web/refresh', { method: 'POST', credentials: 'same-origin', headers: csrfHeaders('POST') })
    .then((r) => r.ok)
    .catch(() => false)
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

function csrfHeaders(method: string): Record<string, string> {
  const token = xsrfToken();
  return token && !['GET', 'HEAD', 'OPTIONS'].includes(method) ? { 'X-XSRF-TOKEN': decodeURIComponent(token) } : {};
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  query?: Record<string, string | number | boolean | null | undefined>;
  headers?: Record<string, string>;
  /** Rohdaten (z. B. Dateiinhalt) statt JSON. */
  raw?: BodyInit;
  /** 401 nicht als beendete Sitzung melden (Prüfung beim Start, ob überhaupt angemeldet). */
  quiet?: boolean;
}

/**
 * Anfrage an die EstrichONE-API. Tokens liegen nur in HttpOnly-Cookies; CSRF-Schutz über X-XSRF-TOKEN.
 * Abgelaufenes Zugriffstoken: einmal erneuern und wiederholen.
 */
export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? 'GET';
  const url = new URL(path.startsWith('/') ? path : `/api/v1/${path}`, window.location.origin);
  for (const [key, value] of Object.entries(options.query ?? {})) {
    if (value !== null && value !== undefined && value !== '') {
      url.searchParams.set(key, String(value));
    }
  }
  const send = () =>
    fetch(url, {
      method,
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(options.raw !== undefined ? { 'Content-Type': 'application/octet-stream' } : {}),
        ...csrfHeaders(method),
        ...options.headers,
      },
      body: options.raw ?? (options.body !== undefined ? JSON.stringify(options.body) : undefined),
    });

  let response = await send();
  if (response.status === 401 && !url.pathname.startsWith('/api/v1/auth/web/')) {
    const error = await toError(response);
    if (error.code === 'ACCESS_TOKEN_EXPIRED' && (await refresh())) {
      response = await send();
    } else {
      if (!options.quiet) {
        sessionEnded(error.code === 'USER_DISABLED' ? 'disabled' : 'expired');
      }
      throw error;
    }
  }
  if (!response.ok) {
    throw await toError(response);
  }
  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T;
  }
  const type = response.headers.get('Content-Type') ?? '';
  return (type.includes('json') ? await response.json() : undefined) as T;
}

export const get = <T>(path: string, query?: RequestOptions['query']) => api<T>(path, { query });
export const post = <T>(path: string, body?: unknown) => api<T>(path, { method: 'POST', body: body ?? null });
export const put = <T>(path: string, body: unknown, headers?: Record<string, string>) => api<T>(path, { method: 'PUT', body, headers });
export const patch = <T>(path: string, body: unknown) => api<T>(path, { method: 'PATCH', body });
export const del = <T>(path: string) => api<T>(path, { method: 'DELETE' });
