/** Zugriff auf den Pfad-Router von überall (Controller, Steuerelemente). */
import type { PathRouter, RouteMatch } from "./router";

let instance: PathRouter | null = null;

export function setRouter(router: PathRouter): void {
	instance = router;
}

export function navTo(path: string, replace = false): void {
	if (instance) {
		instance.navTo(path, replace);
	} else {
		window.location.assign(path);
	}
}

export function setQuery(values: Record<string, string | null | undefined>): void {
	instance?.setQuery(values);
}

export function currentRoute(): RouteMatch | null {
	return instance?.getCurrent() ?? null;
}
