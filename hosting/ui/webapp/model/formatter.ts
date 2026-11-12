import { ValueState } from "sap/ui/core/library";

const dateTime = new Intl.DateTimeFormat("de-DE", { dateStyle: "medium", timeStyle: "short" });
const relative = new Intl.RelativeTimeFormat("de-DE", { numeric: "auto" });

const STATES: Record<string, [string, ValueState, string]> = {
	Provisioning: ["In Bereitstellung", ValueState.Information, "sap-icon://process"],
	Running: ["Aktiv", ValueState.Success, "sap-icon://status-positive"],
	Stopped: ["Angehalten", ValueState.None, "sap-icon://stop"],
	Locked: ["Gesperrt", ValueState.Warning, "sap-icon://locked"],
	Failed: ["Fehler", ValueState.Error, "sap-icon://error"],
	Deleting: ["Wird gelöscht", ValueState.Warning, "sap-icon://delete"],
	Deleted: ["Gelöscht", ValueState.None, "sap-icon://delete"]
};

const HEALTH: Record<string, [string, ValueState, string]> = {
	Healthy: ["In Ordnung", ValueState.Success, "sap-icon://message-success"],
	Degraded: ["Warnung", ValueState.Warning, "sap-icon://message-warning"],
	Unhealthy: ["Gestört", ValueState.Error, "sap-icon://message-error"],
	Stopped: ["Angehalten", ValueState.None, "sap-icon://stop"],
	Unknown: ["Unbekannt", ValueState.None, "sap-icon://question-mark"]
};

const OPERATION: Record<string, [string, ValueState]> = {
	Queued: ["Wartet", ValueState.Information],
	Running: ["Läuft", ValueState.Information],
	Succeeded: ["Erfolgreich", ValueState.Success],
	Failed: ["Fehlgeschlagen", ValueState.Error]
};

interface Certificate { state: string; daysLeft?: number | null; error?: string | null; notAfter?: string | null }

const CERTIFICATE: Record<string, [string, ValueState, string]> = {
	valid: ["Gültig", ValueState.Success, "sap-icon://shield"],
	expiring: ["Läuft bald ab", ValueState.Warning, "sap-icon://warning"],
	invalid: ["Ungültig", ValueState.Error, "sap-icon://error"],
	missing: ["Fehlt", ValueState.Error, "sap-icon://error"],
	off: ["Nur HTTP", ValueState.None, "sap-icon://unlocked"]
};

const SEVERITY: Record<string, [ValueState, string]> = {
	critical: [ValueState.Error, "sap-icon://message-error"],
	warning: [ValueState.Warning, "sap-icon://message-warning"],
	info: [ValueState.Information, "sap-icon://message-information"]
};

/** Update-Ablauf je Instanz (deploy/README.md): Phase mit Schritt und Farbe. */
const PHASES: Record<string, [string, ValueState]> = {
	announce: ["1/3 Ankündigung", ValueState.Information],
	update: ["2/3 Update", ValueState.Warning],
	finish: ["3/3 Abschluss", ValueState.Information],
	rollback: ["Rücknahme", ValueState.Warning],
	failed: ["Fehlgeschlagen", ValueState.Error],
	released: ["Freigegeben", ValueState.Success]
};

/** „2/3 Update · noch 0:42“ – Countdown bis zum Ende der Phase (Server liefert UTC ohne Zone oder ISO). */
function phaseText(phase?: string | null, until?: string | null, now = Date.now()): string {
	if (!phase) return "";
	const label = PHASES[phase]?.[0] ?? phase;
	if (!until || phase === "released") return label;
	const end = Date.parse(/[zZ]|[+-]\d\d:\d\d$/.test(until) ? until : `${until}Z`);
	const left = Math.ceil((end - now) / 1000);
	if (Number.isNaN(left) || left <= 0) return label;
	return `${label} · noch ${Math.floor(left / 60)}:${String(left % 60).padStart(2, "0")}`;
}

export default {
	phaseText: (phase?: string | null, until?: string | null) => phaseText(phase, until),
	phaseState: (phase?: string | null) => (phase ? PHASES[phase]?.[1] ?? ValueState.None : ValueState.None),
	certificateText: (c?: Certificate | null) =>
		!c ? "Noch nicht geprüft" : c.state === "valid" || c.state === "expiring" ? `${CERTIFICATE[c.state][0]} (${c.daysLeft ?? "?"} Tage)` : CERTIFICATE[c.state]?.[0] ?? c.state,
	certificateState: (c?: Certificate | null) => (!c ? ValueState.None : CERTIFICATE[c.state]?.[1] ?? ValueState.None),
	certificateIcon: (c?: Certificate | null) => (!c ? "" : CERTIFICATE[c.state]?.[2] ?? ""),
	/** Laufende Instanz: Gesundheitszustand; sonst Lebenszustand (angehalten, gesperrt …). */
	onlineText: (state?: string, health?: string) => (state === "Running" ? HEALTH[health ?? ""]?.[0] ?? "Prüfung …" : STATES[state ?? ""]?.[0] ?? ""),
	onlineState: (state?: string, health?: string) => (state === "Running" ? HEALTH[health ?? ""]?.[1] ?? ValueState.None : STATES[state ?? ""]?.[1] ?? ValueState.None),
	onlineIcon: (state?: string, health?: string) => (state === "Running" ? HEALTH[health ?? ""]?.[2] ?? "" : STATES[state ?? ""]?.[2] ?? ""),
	severityState: (severity?: string) => SEVERITY[severity ?? ""]?.[0] ?? ValueState.None,
	severityIcon: (severity?: string) => SEVERITY[severity ?? ""]?.[1] ?? "sap-icon://message-information",
	stateText: (state?: string) => (state ? STATES[state]?.[0] ?? state : ""),
	stateState: (state?: string) => (state ? STATES[state]?.[1] ?? ValueState.None : ValueState.None),
	stateIcon: (state?: string) => (state ? STATES[state]?.[2] ?? "" : ""),
	healthText: (health?: string) => (health ? HEALTH[health]?.[0] ?? health : ""),
	healthState: (health?: string) => (health ? HEALTH[health]?.[1] ?? ValueState.None : ValueState.None),
	healthIcon: (health?: string) => (health ? HEALTH[health]?.[2] ?? "" : ""),
	operationText: (status?: string) => (status ? OPERATION[status]?.[0] ?? status : ""),
	operationState: (status?: string) => (status ? OPERATION[status]?.[1] ?? ValueState.None : ValueState.None),
	containerState: (state?: string, health?: string | null) =>
		state !== "running" ? ValueState.None : health === "unhealthy" ? ValueState.Error : health === "starting" ? ValueState.Warning : ValueState.Success,
	containerText: (state?: string, health?: string | null) =>
		state === "running" ? (health === "healthy" ? "läuft (gesund)" : health === "unhealthy" ? "läuft (gestört)" : health === "starting" ? "startet" : "läuft") : state === "exited" ? "beendet" : state ?? "",

	bytes(value?: number | null): string {
		if (value === null || value === undefined) {
			return "–";
		}
		const units = ["B", "KB", "MB", "GB", "TB"];
		let size = value;
		let unit = 0;
		while (size >= 1024 && unit < units.length - 1) {
			size /= 1024;
			unit++;
		}
		return `${size.toLocaleString("de-DE", { maximumFractionDigits: size < 10 && unit > 0 ? 1 : 0 })} ${units[unit]}`;
	},
	gigabytes: (value?: number | null) =>
		((value ?? 0) / 1024 ** 3).toLocaleString("de-DE", { maximumFractionDigits: (value ?? 0) < 10 * 1024 ** 3 ? 2 : 0 }),
	percent: (value?: number | null) => (value === null || value === undefined ? "–" : `${value.toLocaleString("de-DE", { maximumFractionDigits: 1 })} %`),
	dateTime: (value?: string | null) => (value ? dateTime.format(new Date(value)) : "–"),
	ago(value?: string | null): string {
		if (!value) {
			return "–";
		}
		const seconds = (new Date(value).getTime() - Date.now()) / 1000;
		const abs = Math.abs(seconds);
		if (abs < 60) return relative.format(Math.round(seconds), "second");
		if (abs < 3600) return relative.format(Math.round(seconds / 60), "minute");
		if (abs < 86400) return relative.format(Math.round(seconds / 3600), "hour");
		return relative.format(Math.round(seconds / 86400), "day");
	},
	usagePercent(used?: number | null, limit?: number | null): number {
		if (!used || !limit) {
			return 0;
		}
		return Math.min(100, Math.round((used / limit) * 100));
	},
	usageState(used?: number | null, limit?: number | null): ValueState {
		if (!used || !limit) {
			return ValueState.None;
		}
		const ratio = used / limit;
		return ratio > 1 ? ValueState.Error : ratio > 0.85 ? ValueState.Warning : ValueState.Success;
	},
	storageText(used?: number | null, limit?: number | null): string {
		const u = used === null || used === undefined ? "–" : this.bytes(used);
		return limit ? `${u} von ${this.bytes(limit)}` : `${u} (unbegrenzt)`;
	},
	usersText: (active?: number | null, max?: number | null) =>
		`${active ?? "–"} ${max ? `von ${max}` : "(unbegrenzt)"}`,
	usersState: (active?: number | null, max?: number | null) =>
		!max || active === null || active === undefined ? ValueState.None : active > max ? ValueState.Error : active === max ? ValueState.Warning : ValueState.Success,
	backupText: (healthy?: boolean | null, last?: string | null) =>
		healthy === null || healthy === undefined ? "Noch kein Status" : healthy ? (last ? `Aktuell (${dateTime.format(new Date(last))})` : "Aktuell") : last ? `Veraltet (${dateTime.format(new Date(last))})` : "Keine Sicherung",
	backupState: (healthy?: boolean | null) => (healthy === null || healthy === undefined ? ValueState.None : healthy ? ValueState.Success : ValueState.Warning),
	backupEntryState: (status?: string) =>
		status === "succeeded" ? ValueState.Success : status === "failed" ? ValueState.Error : status === "running" ? ValueState.Information : ValueState.None,
	backupEntryText: (status?: string) =>
		({ succeeded: "Erfolgreich", failed: "Fehlgeschlagen", running: "Läuft" } as Record<string, string>)[status ?? ""] ?? (status ? status : "–"),
	versionState: (outdated?: boolean) => (outdated ? ValueState.Warning : ValueState.None),
	price: (value?: number | null) => (value === null || value === undefined ? "–" : `${value.toLocaleString("de-DE", { style: "currency", currency: "EUR" })} / Monat`),
	limit: (value?: number | null, unit = "") => (!value ? "unbegrenzt" : `${value.toLocaleString("de-DE")}${unit}`),
	initials: (name?: string | null) =>
		(name ?? "?").split(/[\s._-]+/).filter(Boolean).slice(0, 2).map((p) => p[0].toUpperCase()).join(""),
	not: (value: unknown) => !value,
	successText: (success?: boolean) => (success ? "Erfolgreich" : "Fehlgeschlagen"),
	successState: (success?: boolean) => (success ? ValueState.Success : ValueState.Error),
	/** Versionshinweise einer Suite-Version auf GitHub (Tag v<version>). */
	releaseNotesUrl: (version?: string | null) =>
		version ? `https://github.com/florians25/EstrichONE/releases/tag/v${encodeURIComponent(version.replace(/^v/, ""))}` : "",
	/** Bezugsquelle der App laut Suite (package = APK vom eigenen Server). */
	appSourceText: (app?: { downloadKind?: string | null; packageAvailable?: boolean; packageSize?: number | null } | null) => {
		if (!app) return "";
		switch (app.downloadKind) {
			case "package": return app.packageAvailable ? `APK vom eigenen Server${app.packageSize ? ` (${Math.round(app.packageSize / 1048576)} MB)` : ""}` : "APK fehlt auf dem Server";
			case "link": return "Download-Link";
			case "store": return "App Store / TestFlight / MDM";
			default: return "Keine Bezugsquelle eingestellt";
		}
	},
	modulesHeader: (modules?: { enabled: boolean; core?: boolean }[] | null) => {
		const switchable = (modules ?? []).filter((m) => !m.core);
		return switchable.length ? `Module (${switchable.filter((m) => m.enabled).length} von ${switchable.length} abschaltbaren eingeschaltet)` : "Module";
	},
	/** Herkunft der Modulliste: Schnittstelle der Suite oder Rückfall Datenbank (ältere Versionen). */
	modulesSourceText: (source?: string | null) =>
		source === "suite" ? "gemeldet von der Suite" : source === "database" ? "aus der Datenbank gelesen (ältere Suite-Version)" : "",
	appsShort: (latest?: string | null, minimum?: string | null) => (latest ? `${latest} (mind. ${minimum ?? "–"})` : "–")
};
