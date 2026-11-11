/**
 * App-Updates (Verwaltung › App-Updates, docs/app-updates.md): Typen der API und Aufbereitung ohne UI5-Abhängigkeit
 * (mit Vitest geprüft).
 */

export type AppUpdateState = "current" | "recommended" | "required" | "newerThanServer";

export interface AppUpdateSettings {
	recommendUpdates: boolean;
	enforceMinimum: boolean;
	minimumVersion: string | null;
	message: string | null;
	iosUpdateUrl: string | null;
	iosLatestVersion: string | null;
	androidUpdateUrl: string | null;
}

export interface AppDownload {
	kind: "package" | "link" | "store" | "none";
	url: string | null;
	sha256?: string | null;
	size?: number | null;
	versionCode?: number | null;
	signerSha256?: string | null;
	releaseSigned?: boolean;
}

export interface PlatformUpdateInfo {
	platform: "android" | "ios";
	latestVersion: string | null;
	minimumVersion: string;
	download: AppDownload;
}

export interface AppPackageInfo {
	version: string;
	versionCode: number;
	size: number;
	sha256: string;
	releaseSigned: boolean;
	signerSha256: string | null;
	valid: boolean;
	error: string | null;
}

export interface AppUpdateDevice {
	id: string;
	name: string;
	platform: "ios" | "android";
	kind: "personal" | "pool";
	status: "pending" | "active" | "locked" | "retired";
	model: string | null;
	appVersion: string | null;
	state: AppUpdateState | null;
	assignedUserName: string | null;
	lastSeenAt: string | null;
}

export interface AppUpdateOverview {
	serverVersion: string;
	settings: AppUpdateSettings;
	android: PlatformUpdateInfo;
	ios: PlatformUpdateInfo;
	androidPackage: AppPackageInfo | null;
	devices: AppUpdateDevice[];
	counts: { total: number; current: number; recommended: number; required: number; newerThanServer: number; unknown: number };
}

export const defaultAppUpdateSettings: AppUpdateSettings = {
	recommendUpdates: true, enforceMinimum: false, minimumVersion: null, message: null, iosUpdateUrl: null, iosLatestVersion: null, androidUpdateUrl: null,
};

/** Text und Farbe (UI5 ValueState) je Stand. */
export function stateText(state: AppUpdateState | null): { text: string; state: "Success" | "Warning" | "Error" | "Information" | "None" } {
	switch (state) {
		case "current": return { text: "Aktuell", state: "Success" };
		case "recommended": return { text: "Update verfügbar", state: "Warning" };
		case "required": return { text: "Gesperrt – Update nötig", state: "Error" };
		case "newerThanServer": return { text: "Neuer als der Server", state: "Information" };
		default: return { text: "Version unbekannt", state: "None" };
	}
}

/** Gültige Versionsnummer wie auf dem Server (1 bis 3 Zahlen, optional Vorabkennung). */
export function isVersion(value: string | null | undefined): boolean {
	if (!value?.trim()) return false;
	return /^v?\d{1,6}(\.\d{1,6}){0,2}(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+.*)?$/.test(value.trim());
}

/** Vergleich nach Semantic Versioning (wie VersionComparer auf dem Server). */
export function compareVersions(a: string, b: string): number {
	const split = (v: string) => {
		const text = v.trim().replace(/^v/, "").split("+")[0];
		const dash = text.indexOf("-");
		const core = (dash < 0 ? text : text.slice(0, dash)).split(".").map((n) => Number.parseInt(n, 10) || 0);
		while (core.length < 3) core.push(0);
		return { core, pre: dash < 0 ? [] : text.slice(dash + 1).split(".") };
	};
	const x = split(a), y = split(b);
	for (let i = 0; i < 3; i++) if (x.core[i] !== y.core[i]) return Math.sign(x.core[i] - y.core[i]);
	if (!x.pre.length || !y.pre.length) return x.pre.length === y.pre.length ? 0 : x.pre.length ? -1 : 1;
	for (let i = 0; i < Math.min(x.pre.length, y.pre.length); i++) {
		const na = /^\d+$/.test(x.pre[i]), nb = /^\d+$/.test(y.pre[i]);
		const r = na && nb ? Math.sign(Number(x.pre[i]) - Number(y.pre[i])) : na !== nb ? (na ? -1 : 1) : x.pre[i] < y.pre[i] ? -1 : x.pre[i] > y.pre[i] ? 1 : 0;
		if (r) return r;
	}
	return Math.sign(x.pre.length - y.pre.length);
}

/** Eingaben prüfen; leere Liste = in Ordnung. Leere Texte werden zu null. */
export function validateSettings(s: AppUpdateSettings): { errors: string[]; clean: AppUpdateSettings } {
	const text = (v: string | null) => (v ?? "").trim() || null;
	const clean: AppUpdateSettings = {
		recommendUpdates: !!s.recommendUpdates, enforceMinimum: !!s.enforceMinimum,
		minimumVersion: text(s.minimumVersion), message: text(s.message), iosUpdateUrl: text(s.iosUpdateUrl),
		iosLatestVersion: text(s.iosLatestVersion), androidUpdateUrl: text(s.androidUpdateUrl),
	};
	const errors: string[] = [];
	if (clean.minimumVersion && !isVersion(clean.minimumVersion)) errors.push("Mindestversion: bitte als Nummer angeben, z. B. 1.0.12.");
	if (clean.iosLatestVersion && !isVersion(clean.iosLatestVersion)) errors.push("Verfügbare iOS-Version: bitte als Nummer angeben, z. B. 1.0.12.");
	for (const [label, url] of [["iOS-Link", clean.iosUpdateUrl], ["Android-Link", clean.androidUpdateUrl]] as const) {
		if (url && !/^https:\/\/[^\s]+$/i.test(url)) errors.push(`${label}: nur https-Adressen sind erlaubt.`);
	}
	if ((clean.message?.length ?? 0) > 500) errors.push("Hinweis: höchstens 500 Zeichen.");
	return { errors, clean };
}

export interface StatusIssue { text: string; level: "error" | "warning" | "info" }

/** Statuszeile der Seite: was die Verwaltung tun sollte. */
export function overviewIssues(o: AppUpdateOverview): StatusIssue[] {
	const issues: StatusIssue[] = [];
	const p = o.androidPackage;
	if (p && !p.valid) issues.push({ text: `Android-Paket wird nicht ausgeliefert: ${p.error ?? "ungültig"}`, level: "error" });
	else if (!p && o.android.download.kind !== "link") issues.push({ text: "Kein Android-Paket auf dem Server", level: "warning" });
	else if (p && !p.releaseSigned) issues.push({ text: "Android-Paket ist eine Testversion (Debug-Signatur) – Updates nur mit Release-Schlüssel zuverlässig", level: "warning" });
	if (o.counts.required > 0) issues.push({ text: `${o.counts.required} ${o.counts.required === 1 ? "Gerät ist" : "Geräte sind"} gesperrt, bis die App aktualisiert ist`, level: "error" });
	if (o.counts.recommended > 0) issues.push({ text: `${o.counts.recommended} ${o.counts.recommended === 1 ? "Gerät hat" : "Geräte haben"} eine ältere App`, level: "warning" });
	if (o.counts.newerThanServer > 0) issues.push({ text: `${o.counts.newerThanServer} ${o.counts.newerThanServer === 1 ? "App ist" : "Apps sind"} neuer als der Server – bitte den Server aktualisieren (sudo /opt/estrichone/update.sh)`, level: "info" });
	if (o.settings.enforceMinimum && !o.settings.iosUpdateUrl && o.devices.some((d) => d.platform === "ios"))
		issues.push({ text: "Mindestversion erzwungen, aber kein Link für iOS hinterlegt (App Store, TestFlight oder MDM)", level: "warning" });
	return issues;
}

/** Wie viele Geräte eine Mindestversion sofort sperren würde (Vorschau beim Einschalten). */
export function wouldBlock(devices: AppUpdateDevice[], minimum: string, platform?: "ios" | "android"): number {
	return devices.filter((d) => (!platform || d.platform === platform) && d.status === "active" && d.appVersion && isVersion(d.appVersion)
		&& compareVersions(d.appVersion, minimum) < 0).length;
}

/** Kurzform der Prüfsumme für die Anzeige. */
export const shortHash = (hash: string | null | undefined) => (hash ? `${hash.slice(0, 12)}…${hash.slice(-6)}` : "–");

/** Reihenfolge: gesperrt, Update verfügbar, neuer als der Server, unbekannt, aktuell; innerhalb nach Name. */
export function sortDevices(devices: AppUpdateDevice[]): AppUpdateDevice[] {
	const rank: Record<string, number> = { required: 0, recommended: 1, newerThanServer: 2, null: 3, current: 4 };
	return [...devices].sort((a, b) => (rank[String(a.state)] - rank[String(b.state)]) || a.name.localeCompare(b.name, "de"));
}
