/** Zugriff auf die Konsolen-API: Sitzungscookie (HttpOnly) + CSRF-Token im Header für ändernde Aufrufe. */
export class ApiError extends Error {
	constructor(
		public readonly status: number,
		public readonly detail: string,
		public readonly fields: Record<string, string> = {},
		public readonly code?: string
	) {
		super(detail);
	}
}

let csrfToken: string | null = null;
let unauthorizedHandler: (() => void) | null = null;

export function setCsrfToken(token: string | null): void {
	csrfToken = token;
}

export function onUnauthorized(handler: () => void): void {
	unauthorizedHandler = handler;
}

export async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
	const headers: Record<string, string> = { Accept: "application/json" };
	if (body !== undefined) {
		headers["Content-Type"] = "application/json";
	}
	if (method !== "GET" && csrfToken) {
		headers["X-CSRF-Token"] = csrfToken;
	}
	let response: Response;
	try {
		response = await fetch(url, {
			method,
			headers,
			credentials: "same-origin",
			body: body === undefined ? undefined : JSON.stringify(body)
		});
	} catch {
		throw new ApiError(0, "Die Konsole ist nicht erreichbar. Bitte Verbindung prüfen.");
	}
	const text = await response.text();
	let data: unknown = undefined;
	if (text) {
		try {
			data = JSON.parse(text);
		} catch {
			data = text;
		}
	}
	if (!response.ok) {
		const problem = (data ?? {}) as { detail?: string; title?: string; fields?: Record<string, string>; code?: string };
		if (response.status === 401 && !url.startsWith("/api/auth/") && unauthorizedHandler) {
			unauthorizedHandler();
		}
		throw new ApiError(response.status, problem.detail ?? problem.title ?? `Fehler ${response.status}`, problem.fields ?? {}, problem.code);
	}
	return data as T;
}

export const get = <T>(url: string): Promise<T> => request<T>("GET", url);
export const post = <T>(url: string, body: unknown = {}): Promise<T> => request<T>("POST", url, body);
export const put = <T>(url: string, body: unknown): Promise<T> => request<T>("PUT", url, body);
export const del = <T>(url: string): Promise<T> => request<T>("DELETE", url);
