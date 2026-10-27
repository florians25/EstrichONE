/** Systemstatus Backup-/Archivserver (B5): Bannertexte, ohne UI5-Abhängigkeit (mit Vitest geprüft). */
/** Systemstatus für alle Clients (Paket Backup/Archiv). Felder fehlen, solange nichts eingerichtet ist. */
export interface HealthPart {
	configured?: boolean;
	reachable?: boolean;
	lastSuccessAt?: string | null;
	severity?: string | null;
}

export interface HealthBanner {
	/** docs/backup.md: genau diese Banner anzeigen (critical rot, warning gelb, Text unverändert). */
	banners?: { key: string; severity: string; text: string }[];
	backup?: HealthPart | null;
	backupTarget?: HealthPart | null;
	archive?: HealthPart | null;
}

export interface HealthStrip { key: string; type: "Error" | "Warning" | "Information"; text: string; icon: string }

/** Anzuzeigende Banner: vom Server vorgegeben; sonst aus den Einzelwerten abgeleitet. */
export function healthStrips(health: HealthBanner | null | undefined, now = Date.now()): HealthStrip[] {
	const icon = (key: string) => (key === "archive" ? "sap-icon://attachment-zip-file" : "sap-icon://upload-to-cloud");
	if (health?.banners) {
		return health.banners.filter((b) => b.text).map((b) => ({
			key: b.key, text: b.text, icon: icon(b.key),
			type: b.severity === "critical" || b.severity === "error" ? "Error" : b.severity === "warning" ? "Warning" : "Information",
		}));
	}
	const t = healthTexts(health, now);
	return [
		...(t.backup ? [{ key: "backup", type: "Error" as const, text: t.backup, icon: icon("backup") }] : []),
		...(t.archive ? [{ key: "archive", type: "Warning" as const, text: t.archive, icon: icon("archive") }] : []),
	];
}

const stamp = (value: string | null | undefined) =>
	value ? new Intl.DateTimeFormat("de-DE", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(new Date(value)) + " Uhr" : "unbekannt";

/** Texte aus den Anforderungen (B5) – identisch auf allen Systemen. */
export function healthTexts(health: HealthBanner | null | undefined, now = Date.now()): { backup: string; archive: string } {
	const b = health?.backup ?? health?.backupTarget;
	const a = health?.archive;
	const stale = (value: string | null | undefined) => !value || now - new Date(value).getTime() > 26 * 3600_000;
	const backupDown = !!b?.configured && (b.reachable === false || b.severity === "critical" || b.severity === "error" || stale(b.lastSuccessAt));
	const archiveDown = !!a?.configured && (a.reachable === false || a.severity === "critical" || a.severity === "error" || a.severity === "warning");
	return {
		backup: backupDown ? `ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup ${stamp(b?.lastSuccessAt)}). Bitte geben Sie umgehend Ihrem Admin Bescheid.` : "",
		archive: archiveDown ? `Hinweis: Archiv-Server ausgefallen oder nicht erreichbar (Letzte Archivierung ${stamp(a?.lastSuccessAt)}). Bitte informieren Sie Ihren Admin.` : "",
	};
}

