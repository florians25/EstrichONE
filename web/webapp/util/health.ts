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
		backup: backupDown ? `Backup-Ziel nicht erreichbar. Letzte erfolgreiche Sicherung: ${stamp(b?.lastSuccessAt)}. Bitte informieren Sie umgehend die Administration.` : "",
		archive: archiveDown ? `Archivserver nicht erreichbar. Letzte Archivierung: ${stamp(a?.lastSuccessAt)}. Bitte informieren Sie die Administration.` : "",
	};
}


/** Antwort von GET /system/address (nur Administratoren). */
export interface AddressCheck { configuredUrl: string | null; requestUrl: string; matches: boolean; https: boolean }

/**
 * Hinweis für Administratoren: Verbindung unverschlüsselt (Vorrang) bzw. eingestellte Adresse (ESTRICHONE_DOMAIN)
 * weicht von der aufgerufenen ab. Die Anmeldung klappt trotzdem; Links in E-Mails, QR-Codes und Pushnachrichten
 * zeigen aber auf die eingestellte Adresse.
 */
export function addressStrip(check: AddressCheck | null | undefined, origin: string): HealthStrip | null {
	if (!check) return null;
	if (!check.https && origin.startsWith("http:") && !/^http:\/\/(localhost|127\.|\[::1\])/.test(origin)) {
		return {
			key: "address", type: "Warning", icon: "sap-icon://locked",
			text: `Unverschlüsselte Verbindung: EstrichONE wird über ${origin} aufgerufen. Passwörter und Daten gehen unverschlüsselt `
				+ "durchs Netz. Bitte über die HTTPS-Adresse (z. B. Nginx Proxy Manager) aufrufen und den Port nur für den Proxy freigeben.",
		};
	}
	if (!check.matches && check.configuredUrl) {
		const host = new URL(origin).hostname;
		return {
			key: "address", type: "Warning", icon: "sap-icon://chain-link",
			text: `Konfigurierte Adresse weicht ab: Der Server ist auf ${check.configuredUrl} eingestellt, aufgerufen wird ${origin}. `
				+ "Links in E-Mails, QR-Codes und Pushnachrichten zeigen auf die eingestellte Adresse. Bitte den Installer erneut mit "
				+ `-e ESTRICHONE_DOMAIN=${host} ausführen (bzw. ESTRICHONE_DOMAIN und ESTRICHONE_PUBLIC_URL in der .env anpassen).`,
		};
	}
	return null;
}
