/**
 * Module der Suite (Einstellungen › Module): welche Seiten, Rechte und Benachrichtigungen zu einem abschaltbaren Modul
 * gehören. Der Server liefert Zustand, Titel, Beschreibung und Abhängigkeiten (GET settings/modules); hier steht nur, wie
 * die Weboberfläche ein abgeschaltetes Modul ausblendet. Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */

/** Schalter je Modul (Schlüssel wie im Server, dazu „chat“ aus den Chat-Einstellungen). Fehlende Schlüssel gelten als an. */
export type ModuleStates = Record<string, boolean | undefined>;

/** Ein Modul, wie es der Server meldet (GET settings/modules). */
export interface ModuleInfo {
	key: string;
	area: string;
	title: string;
	description: string;
	enabled: boolean;
	core: boolean;
	requires: string[];
	requiredBy: string[];
}

/** Bereiche in Anzeigereihenfolge. */
export const MODULE_AREAS: { key: string; title: string; description: string }[] = [
	{ key: "office", title: "Büro", description: "Telefon, Anfragen und Überblick" },
	{ key: "planning", title: "Planung", description: "Disposition, Kolonnen, Geräte" },
	{ key: "material", title: "Material", description: "Materialstamm, Lieferanten, Bestellungen" },
	{ key: "hr", title: "Personal", description: "Mitarbeiter, Urlaub und Abwesenheit" },
	{ key: "site", title: "Baustelle und Dokumentation", description: "Was auf der Baustelle erfasst und dokumentiert wird" },
	{ key: "service", title: "Kommunikation und Service", description: "Chat, Helpdesk und Rückmeldungen" },
	{ key: "core", title: "Immer aktiv", description: "Kern von EstrichONE – nicht abschaltbar" },
];

/**
 * Seiten je Modul (Pfadanfänge). Längere Pfade gewinnen, z. B. /planung/geraete (Maschinen) vor /planung (Disposition).
 * Mit Query: nur, wenn der Parameter passt (Reiter einer Seite, die sonst zu einem anderen Modul gehört).
 */
interface PathRule { path: string; modules: string[]; query?: [string, string] }

const PATH_RULES: PathRule[] = [
	{ path: "/anrufe", modules: ["calls"] },
	{ path: "/anfragen", modules: ["calls"] },
	{ path: "/bauakten/karte", modules: ["map"] },
	{ path: "/planung/geraete", modules: ["machines"] },
	{ path: "/planung/kolonnen", modules: ["machines"], query: ["reiter", "geraete"] },
	{ path: "/planung", modules: ["planning"] },
	{ path: "/material", modules: ["materials"] },
	{ path: "/lieferanten", modules: ["materials"] },
	{ path: "/bestellungen", modules: ["orders"] },
	{ path: "/personal/urlaub", modules: ["absence"] },
	{ path: "/personal", modules: ["staff"] },
	{ path: "/system/aktenarchiv", modules: ["dossiers"] },
	{ path: "/it-center/helpdesk", modules: ["helpdesk"] },
	{ path: "/wissen", modules: ["helpdesk"] },
	{ path: "/it-center/meldungen", modules: ["feedback"] },
	{ path: "/meine-meldungen", modules: ["feedback"] },
	{ path: "/chat", modules: ["chat"] },
	{ path: "/dokumente", modules: ["approvals"], query: ["reiter", "freigaben"] },
];

/**
 * Rechte, die nur zu einem Modul gehören: Ist es aus, fehlen sie in der Oberfläche (Navigation, Kacheln, Reiter, Knöpfe
 * richten sich schon nach Rechten). „work.“ zählt erst, wenn Aufgaben und Mängel aus sind.
 */
const PERMISSION_PREFIXES: { modules: string[]; prefixes: string[] }[] = [
	{ modules: ["planning"], prefixes: ["planning."] },
	{ modules: ["staff"], prefixes: ["hr."] },
	{ modules: ["absence"], prefixes: ["absence."] },
	{ modules: ["materials"], prefixes: ["materials."] },
	{ modules: ["diary"], prefixes: ["diary."] },
	{ modules: ["approvals"], prefixes: ["documents.approve"] },
	{ modules: ["helpdesk"], prefixes: ["helpdesk."] },
	{ modules: ["feedback"], prefixes: ["feedback."] },
	{ modules: ["chat"], prefixes: ["chat."] },
	{ modules: ["tasks", "defects"], prefixes: ["work."] },
];

/** Eingeschaltet? Unbekannte bzw. fehlende Schlüssel gelten als an. */
export function moduleOn(states: ModuleStates, key: string): boolean {
	return states[key] !== false;
}

/** Ist mindestens eines der Module an? */
export function anyOn(states: ModuleStates, keys: string[]): boolean {
	return keys.some((k) => moduleOn(states, k));
}

const segmentMatch = (pathname: string, prefix: string) => pathname === prefix || pathname.startsWith(`${prefix}/`);

/** Module der Seite (z. B. „/planung/kolonnen?reiter=geraete“ → machines), sonst leer. Längster Pfad gewinnt. */
export function modulesOfPath(target: string): string[] {
	const url = new URL(target, "http://local");
	const rules = PATH_RULES.filter((r) => segmentMatch(url.pathname, r.path) && (!r.query || url.searchParams.get(r.query[0]) === r.query[1]))
		.sort((a, b) => Number(!!b.query) - Number(!!a.query) || b.path.length - a.path.length);
	return rules[0]?.modules ?? [];
}

/** Abgeschaltetes Modul, zu dem die Seite gehört (für „Modul deaktiviert“), sonst null. */
export function disabledModuleOfPath(states: ModuleStates, target: string): string | null {
	const modules = modulesOfPath(target);
	return modules.length > 0 && !anyOn(states, modules) ? modules[0] : null;
}

/** Ziel erreichbar (Kachel, Navigationseintrag, Karte, Suchtreffer)? */
export function pathEnabled(states: ModuleStates, target: string): boolean {
	return disabledModuleOfPath(states, target) === null;
}

/** Rechte ohne die der abgeschalteten Module. */
export function filterPermissions(states: ModuleStates, permissions: string[]): string[] {
	const hidden = PERMISSION_PREFIXES.filter((p) => !anyOn(states, p.modules)).flatMap((p) => p.prefixes);
	return permissions.filter((p) => !hidden.some((prefix) => p.startsWith(prefix)));
}

/**
 * Was beim Ausschalten zusätzlich ausgeht: alle eingeschalteten Module, die (auch über Zwischenstufen) davon abhängen.
 */
export function dependentsToDisable(modules: ModuleInfo[], key: string): ModuleInfo[] {
	const result: ModuleInfo[] = [];
	const queue = [key];
	while (queue.length) {
		const current = queue.shift()!;
		for (const m of modules.filter((x) => x.requires.includes(current) && x.enabled && !result.includes(x))) {
			result.push(m);
			queue.push(m.key);
		}
	}
	return result;
}

/** Was beim Einschalten zusätzlich angehen muss: ausgeschaltete Voraussetzungen (auch über Zwischenstufen). */
export function requirementsToEnable(modules: ModuleInfo[], key: string): ModuleInfo[] {
	const result: ModuleInfo[] = [];
	const queue = [key];
	while (queue.length) {
		const next = queue.shift();
		const current = modules.find((m) => m.key === next);
		for (const required of current?.requires ?? []) {
			const info = modules.find((m) => m.key === required);
			if (info && !info.enabled && !result.includes(info)) {
				result.push(info);
				queue.push(info.key);
			}
		}
	}
	return result;
}

/** Hinweis zu den Abhängigkeiten eines Moduls, z. B. „Benötigt: Personal · Wird benötigt von: Urlaub und Abwesenheit“. */
export function dependencyHint(modules: ModuleInfo[], module: ModuleInfo): string {
	const title = (key: string) => modules.find((m) => m.key === key)?.title ?? key;
	return [
		module.requires.length ? `Benötigt: ${module.requires.map(title).join(", ")}` : "",
		module.requiredBy.length ? `Wird benötigt von: ${module.requiredBy.map(title).join(", ")}` : "",
	].filter(Boolean).join(" · ");
}

/** Text für den Bestätigungsdialog beim Ausschalten. */
export function disableQuestion(module: ModuleInfo, dependents: ModuleInfo[]): string {
	const also = dependents.length
		? ` Damit werden auch ${dependents.map((d) => `„${d.title}“`).join(", ")} ausgeschaltet, weil ${dependents.length === 1 ? "es" : "sie"} „${module.title}“ benötig${dependents.length === 1 ? "t" : "en"}.`
		: "";
	return `„${module.title}“ ausschalten?${also} Das Modul verschwindet für alle Benutzer aus Web und Apps; Benachrichtigungen des Moduls werden nicht mehr verschickt. `
		+ "Vorhandene Daten bleiben erhalten und sind nach dem Einschalten wieder da.";
}
