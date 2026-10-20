/**
 * Pfad-Router mit echter Adresse (History API) statt „#/…“: /anmelden, /bauakten/<id> usw. sind Deep-Links,
 * die Caddy (try_files … /index.html) und der Entwicklungsserver auf index.html abbilden.
 */

export interface RouteDef {
	name: string;
	/** Muster mit Platzhaltern, z. B. „/bauakten/:id“. */
	pattern: string;
	/** XML-View (Name ohne Präfix „estrichone.view.“). */
	view: string;
	/** Seite ohne Anmeldung (Anmelden, Einrichtung). */
	public?: boolean;
	/** Ohne Programmrahmen (z. B. Druckansicht). */
	bare?: boolean;
	permission?: string;
	/** Eintrag der Seitennavigation, der hervorgehoben wird. */
	nav?: string;
	/** Titel für Browser-Reiter und Kopfzeile (i18n-Schlüssel). */
	title?: string;
}

export interface RouteMatch {
	route: RouteDef;
	params: Record<string, string>;
	query: URLSearchParams;
	path: string;
}

function compile(pattern: string): { regex: RegExp; keys: string[] } {
	const keys: string[] = [];
	const source = pattern.replace(/\/:([a-zA-Z]+)/g, (_, key: string) => {
		keys.push(key);
		return "/([^/]+)";
	});
	return { regex: new RegExp(`^${source}/?$`), keys };
}

export function matchRoute(routes: RouteDef[], path: string, search = ""): RouteMatch | null {
	for (const route of routes) {
		const { regex, keys } = compile(route.pattern);
		const found = regex.exec(path);
		if (found) {
			const params: Record<string, string> = {};
			keys.forEach((key, index) => {
				params[key] = decodeURIComponent(found[index + 1]);
			});
			return { route, params, query: new URLSearchParams(search), path };
		}
	}
	return null;
}

export type MatchedHandler = (match: RouteMatch | null) => void;

export class PathRouter {
	private current: RouteMatch | null = null;

	constructor(private readonly routes: RouteDef[], private readonly handler: MatchedHandler) {}

	start(): void {
		window.addEventListener("popstate", () => this.dispatch());
		// Interne Links (<a href="/…">) ohne Neuladen; Strg/Umschalt/Mittelklick öffnen weiter einen neuen Tab.
		document.addEventListener("click", (event) => {
			if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
			const anchor = (event.target as HTMLElement | null)?.closest?.("a");
			const href = anchor?.getAttribute("href");
			if (!anchor || !href || !href.startsWith("/") || href.startsWith("//") || href.startsWith("/api/") || anchor.target === "_blank"
				|| anchor.hasAttribute("download")) {
				return;
			}
			event.preventDefault();
			this.navTo(href);
		});
		this.dispatch();
	}

	getCurrent(): RouteMatch | null {
		return this.current;
	}

	/** Zu einem Pfad (mit Query) wechseln. */
	navTo(path: string, replace = false): void {
		const target = new URL(path, window.location.origin);
		const same = target.pathname + target.search === window.location.pathname + window.location.search;
		if (replace || same) {
			window.history.replaceState(null, "", target.pathname + target.search);
		} else {
			window.history.pushState(null, "", target.pathname + target.search);
		}
		this.dispatch();
	}

	/** Nur die Query ändern (Reiter, Filter) – ohne neuen Verlaufseintrag und ohne neue Seite. */
	setQuery(values: Record<string, string | null | undefined>): void {
		const query = new URLSearchParams(window.location.search);
		for (const [key, value] of Object.entries(values)) {
			if (value) query.set(key, value);
			else query.delete(key);
		}
		const text = query.toString();
		window.history.replaceState(null, "", window.location.pathname + (text ? `?${text}` : ""));
		if (this.current) {
			this.current = { ...this.current, query };
		}
	}

	private dispatch(): void {
		this.current = matchRoute(this.routes, window.location.pathname, window.location.search);
		this.handler(this.current);
	}
}
