/**
 * Zugriff auf die EstrichONE-API v1. Tokens liegen ausschließlich in HttpOnly-Cookies; der CSRF-Token aus dem
 * Cookie XSRF-TOKEN geht im Header X-XSRF-TOKEN mit (Doppel-Absendung). Ein abgelaufenes Zugriffstoken wird einmal
 * erneuert und die Anfrage wiederholt. Fehler kommen als RFC 9457 mit stabilem `code`.
 *
 * Bewusst ohne UI5-Abhängigkeit, damit die Logik mit Vitest geprüft werden kann.
 */

export interface Problem {
	status?: number;
	title?: string;
	detail?: string;
	code?: string;
	errors?: Record<string, string[]>;
	endsAt?: string;
	/** Wartung: <c>maintenance</c>, <c>restore</c> oder <c>update</c> (Update-Ablauf, util/updateView.ts). */
	reason?: string;
	phase?: string;
}

/** Fehler einer API-Anfrage mit verständlicher Meldung. */
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

/**
 * Reaktion auf eine beendete Sitzung. Liefert `true`, wenn der Benutzer sich (z. B. im Anmeldedialog) erneut
 * angemeldet hat – dann wird die Anfrage wiederholt, ohne dass Eingaben verloren gehen.
 */
type Listener = (reason: "expired" | "disabled") => boolean | Promise<boolean> | void;
let sessionEnded: Listener = () => undefined;
let refreshing: Promise<boolean> | null = null;

let maintenanceHit: (problem: Problem | undefined) => void = () => undefined;

/** Wartungsmodus: jede 503-Antwort mit Code MAINTENANCE meldet sich hier (Wartungsseite). */
export function onMaintenance(listener: (problem: Problem | undefined) => void): void {
	maintenanceHit = listener;
}

let updateHint: (phase: string) => void = () => undefined;

/** Update-Ablauf: Jede Antwort mit Kopfzeile X-EstrichONE-Update meldet die Phase (Countdown sofort zeigen). */
export function onUpdateHint(listener: (phase: string) => void): void {
	updateHint = listener;
}

let unreachableHit: () => void = () => undefined;

/** Server nicht erreichbar (Netzwerkfehler, 502/504, 503 ohne Wartung): Störungsseite prüft und zeigt sich ggf. */
export function onUnreachable(listener: () => void): void {
	unreachableHit = listener;
}

let passwordChangeHit: () => void = () => undefined;

/**
 * Einmalpasswort noch aktiv: Der Server lehnt alles außer Passwortwechsel, Abmelden und Status mit 403
 * PASSWORD_CHANGE_REQUIRED ab – die Oberfläche führt dann zum Passwortwechsel (auch nach einem Neuladen).
 */
export function onPasswordChangeRequired(listener: () => void): void {
	passwordChangeHit = listener;
}

/** Wird vom Anmeldezustand gesetzt: was passiert, wenn die Sitzung endet. */
export function onSessionEnded(listener: Listener): void {
	sessionEnded = listener;
}

function xsrfToken(): string | undefined {
	return document.cookie.split("; ").find((c) => c.startsWith("XSRF-TOKEN="))?.split("=")[1];
}

export function csrfHeaders(method: string): Record<string, string> {
	const token = xsrfToken();
	return token && !["GET", "HEAD", "OPTIONS"].includes(method) ? { "X-XSRF-TOKEN": decodeURIComponent(token) } : {};
}

/** Die Anfrage hat den Server nicht erreicht (kein Netz, Proxy bricht ab, Zeitüberschreitung). */
export class NetworkError extends Error {
	constructor(message = "Der Server ist nicht erreichbar.") {
		super(message);
	}
}

/** Verständliche Meldung zu jedem Fehler (für MessageBox und Formulare). */
export function problemMessage(error: unknown): string {
	if (error instanceof ApiError) {
		return error.message;
	}
	if (error instanceof NetworkError || (error instanceof TypeError && /fetch|network|load failed/i.test(error.message))) {
		return typeof navigator !== "undefined" && navigator.onLine === false
			? "Keine Internetverbindung. Bitte die Verbindung prüfen und erneut versuchen."
			: error instanceof NetworkError && error.message !== "Der Server ist nicht erreichbar."
				? error.message
				: "Der Server ist nicht erreichbar. Bitte die Verbindung prüfen und erneut versuchen.";
	}
	const detail = error instanceof Error && error.message ? ` (${error.message})` : "";
	return `Ein unerwarteter Fehler ist aufgetreten${detail}. Bitte die Seite neu laden und erneut versuchen.`;
}

const englishTitles = new Set(["Bad Request", "Unauthorized", "Forbidden", "Not Found", "Method Not Allowed", "Conflict", "Gone",
	"Payload Too Large", "Request Entity Too Large", "Unsupported Media Type", "Unprocessable Entity", "Too Many Requests", "Internal Server Error",
	"Bad Gateway", "Service Unavailable", "Gateway Timeout", "An error occurred while processing your request.",
	"One or more validation errors occurred."]);
const statusTexts: Record<number, string> = {
	400: "Die Eingaben sind unvollständig oder ungültig.", 403: "Dafür fehlt die Berechtigung.",
	404: "Nicht gefunden. Der Eintrag wurde gelöscht oder die Adresse ist nicht mehr gültig.",
	409: "Der Eintrag wurde inzwischen geändert. Bitte neu laden und erneut versuchen.", 413: "Die Datei ist zu groß.",
	429: "Zu viele Anfragen in kurzer Zeit. Bitte kurz warten und erneut versuchen.",
	500: "Auf dem Server ist ein Fehler aufgetreten. Bitte später erneut versuchen.",
	503: "Der Server ist gerade nicht verfügbar (z. B. Wartung). Bitte später erneut versuchen.",
};

/** Antwort eines vorgeschalteten Proxys (Cloudflare, Caddy, Nginx), nicht der API: Zeitüberschreitung oder abgebrochene Antwort. */
export const gatewayMessage = "Der Server hat nicht rechtzeitig oder nur unvollständig geantwortet – bitte erneut versuchen. "
	+ "Bleibt es dabei, stehen die Einzelheiten im Server-Log (docker compose logs api).";

/**
 * Stammt die Fehlerantwort nicht von EstrichONE? Eigene Probleme (API und Caddy) tragen immer einen `code`; Proxys wie Cloudflare
 * liefern bei 5xx eigene Seiten oder JSON mit englischem Text („The origin web server returned an invalid or incomplete response …“),
 * die nicht in der Oberfläche erscheinen sollen.
 */
export function isForeignProblem(status: number, problem: { code?: string } | undefined): boolean {
	return status >= 500 && !problem?.code;
}

/** Deutsche Meldung zu einem Status ohne eigene Problembeschreibung. */
export function statusMessage(status: number): string {
	if (status === 502 || status === 504 || (status >= 520 && status <= 530)) return gatewayMessage;
	return statusTexts[status] ?? `Die Anfrage ist fehlgeschlagen (Status ${status}).`;
}

async function toError(response: Response): Promise<ApiError> {
	let problem: Problem | undefined;
	try {
		const body: unknown = await response.json();
		problem = body && typeof body === "object" ? (body as Problem) : undefined;
	} catch {
		problem = undefined;
	}
	if (isForeignProblem(response.status, problem)) {
		return new ApiError(response.status, undefined, statusMessage(response.status), undefined);
	}
	const firstValidation = problem?.errors ? Object.values(problem.errors).flat()[0] : undefined;
	// Standardtitel von ASP.NET („Not Found“ …) ohne eigene Meldung: deutscher Text statt englischem Statusnamen.
	const title = problem?.title && !englishTitles.has(problem.title) ? problem.title : undefined;
	const message = firstValidation ?? problem?.detail ?? title ?? statusMessage(response.status);
	return new ApiError(response.status, problem?.code, message, problem);
}

/**
 * Sitzung einmal erneuern – auch bei parallelen Anfragen nur einmal. 409 heißt: ein anderer Tab hat gerade erneuert,
 * die Cookies sind schon neu; die Anfrage kann einfach wiederholt werden.
 */
export function refreshSession(): Promise<boolean> {
	// Ohne CSRF-Cookie gab es keine Sitzung (oder sie wurde beendet): nicht unnötig anfragen.
	if (!xsrfToken()) return Promise.resolve(false);
	refreshing ??= fetch("/api/v1/auth/web/refresh", { method: "POST", credentials: "same-origin", headers: csrfHeaders("POST") })
		.then((r) => r.ok || r.status === 409)
		.catch(() => false)
		.finally(() => {
			refreshing = null;
		});
	return refreshing;
}

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Lesende Anfragen überstehen kurze Aussetzer (Netz, Neustart des Servers, 502/503/504 vom Proxy) mit zwei
 * Wiederholungen; schreibende Anfragen werden nie automatisch wiederholt.
 */
async function sendWithRetry(send: () => Promise<Response>, method: string): Promise<Response> {
	const idempotent = method === "GET" || method === "HEAD";
	for (let attempt = 0; ; attempt++) {
		try {
			const response = await send();
			const transient = [502, 503, 504].includes(response.status) && response.headers.get("Content-Type")?.includes("problem") !== true;
			if (!idempotent || !transient || attempt >= 2) return response;
		} catch (error) {
			if (!idempotent || attempt >= 2) throw error;
		}
		await wait(retryDelay * (attempt + 1));
	}
}

let retryDelay = 800;
/** Nur für Tests: Wartezeit zwischen Wiederholungen. */
export function setRetryDelay(ms: number): void {
	retryDelay = ms;
}

export type Query = Record<string, string | number | boolean | null | undefined>;

export interface RequestOptions {
	method?: string;
	body?: unknown;
	query?: Query;
	headers?: Record<string, string>;
	/** Rohdaten (z. B. Dateiinhalt) statt JSON. */
	raw?: BodyInit;
	/** 401 nicht als beendete Sitzung melden (Prüfung beim Start, ob überhaupt angemeldet). */
	quiet?: boolean;
}

/** Vollständige URL einer API-Ressource (auch für Links und Bilder). */
export function apiUrl(path: string, query: Query = {}): string {
	const url = new URL(path.startsWith("/") ? path : `/api/v1/${path}`, window.location.origin);
	for (const [key, value] of Object.entries(query)) {
		if (value !== null && value !== undefined && value !== "") {
			url.searchParams.set(key, String(value));
		}
	}
	return url.pathname + url.search;
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
	const method = options.method ?? "GET";
	const url = apiUrl(path, options.query);
	const send = () =>
		fetch(url, {
			method,
			credentials: "same-origin",
			headers: {
				Accept: "application/json",
				...(options.body !== undefined ? { "Content-Type": "application/json" } : {}),
				...(options.raw !== undefined ? { "Content-Type": "application/octet-stream" } : {}),
				...csrfHeaders(method),
				...options.headers,
			},
			body: options.raw ?? (options.body !== undefined ? JSON.stringify(options.body) : undefined),
		}).catch(() => {
			throw new NetworkError();
		});

	let response: Response;
	try {
		response = await sendWithRetry(send, method);
	} catch (error) {
		if (error instanceof NetworkError) unreachableHit();
		throw error;
	}
	if ([502, 504].includes(response.status) || (response.status === 503 && response.headers.get("Content-Type")?.includes("problem") !== true)) {
		unreachableHit();
	}
	const phase = response.headers?.get?.("X-EstrichONE-Update");
	if (phase) updateHint(phase);
	if (response.status === 401 && !url.startsWith("/api/v1/auth/web/")) {
		let error = await toError(response);
		// Abgelaufenes oder (nach Ablauf des Cookies) fehlendes Zugriffstoken: still erneuern und wiederholen.
		if (error.code !== "USER_DISABLED" && (await refreshSession())) {
			response = await send();
			if (response.status === 401) error = await toError(response);
		}
		if (response.status === 401) {
			if (options.quiet || !(await sessionEnded(error.code === "USER_DISABLED" ? "disabled" : "expired"))) {
				throw error;
			}
			response = await send();
		}
	}
	if (!response.ok) {
		const error = await toError(response);
		if (response.status === 503 && error.code === "MAINTENANCE") {
			maintenanceHit(error.problem);
		}
		if (response.status === 403 && error.code === "PASSWORD_CHANGE_REQUIRED") {
			passwordChangeHit();
		}
		throw error;
	}
	if (response.status === 204 || response.headers.get("Content-Length") === "0") {
		return undefined as T;
	}
	const type = response.headers.get("Content-Type") ?? "";
	return (type.includes("json") ? await response.json() : undefined) as T;
}

export const get = <T>(path: string, query?: Query) => api<T>(path, { query });
export const post = <T>(path: string, body?: unknown) => api<T>(path, { method: "POST", body: body ?? null });
export const put = <T>(path: string, body: unknown, headers?: Record<string, string>) => api<T>(path, { method: "PUT", body, headers });
export const patch = <T>(path: string, body: unknown) => api<T>(path, { method: "PATCH", body });
export const del = <T>(path: string) => api<T>(path, { method: "DELETE" });

export interface SyncResult {
	opId: string;
	status: "applied" | "duplicate" | "conflict" | "rejected";
	code?: string | null;
	message?: string | null;
	entity?: unknown;
}

/**
 * Erfassen über den Sync wie die Apps (Bautagebuch, Prüfungen): eine Logik für alle Plattformen.
 * Abgelehnte Operationen werden als Fehler mit der Meldung des Servers geworfen.
 */
export async function syncPush(type: string, entityId: string, payload: unknown, opId: string): Promise<SyncResult> {
	const result = await post<{ results: SyncResult[] }>("sync/push", {
		operations: [{ opId, type, entityId, baseVersion: null, payload }],
	});
	const first = result.results[0];
	if (!first || first.status === "rejected" || first.status === "conflict") {
		throw new ApiError(422, first?.code ?? "SYNC_REJECTED", first?.message ?? "Die Änderung wurde vom Server abgelehnt.");
	}
	return first;
}

/** POST mit JSON, Antwort als Datei (z. B. PDF); Fehler wie bei api(). */
export async function postForBlob(path: string, body: unknown): Promise<Blob> {
	const send = () => fetch(apiUrl(path), {
		method: "POST", credentials: "same-origin",
		headers: { "Content-Type": "application/json", Accept: "application/pdf, application/problem+json", ...csrfHeaders("POST") },
		body: JSON.stringify(body),
	}).catch(() => {
		throw new NetworkError();
	});
	let response = await send();
	if (response.status === 401 && (await refreshSession())) response = await send();
	if (!response.ok) throw await toError(response);
	return response.blob();
}
