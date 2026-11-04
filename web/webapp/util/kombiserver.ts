/**
 * Kombiserver (Backup-Ziel + Archivserver auf einem Rechner, docs/backup.md „Kombiserver“) und „Abschließen & Archivieren“:
 * reine Logik ohne UI5 (mit Vitest geprüft). Gekoppelt wird über die bestehenden Endpunkte backup/targets und archive/pair.
 */
import type { HealthBanner } from "./health";

export const KOMBI_BACKUP_PORT = 8444;
export const KOMBI_ARCHIVE_PORT = 8443;

/** Hinweis in Web und Doku: bequem, aber kein zweiter Standort für das Archiv. */
export const KOMBI_LOCATION_HINT = "Backup und Archiv auf demselben Rechner ist bequem, aber kein zweiter Standort für das Archiv. "
	+ "Stellen Sie den Kombiserver räumlich getrennt vom EstrichONE-Server auf (anderes Gebäude, zuhause, Rechenzentrum).";

export function kombiCommand(version = "<version>"): string {
	return "sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v /opt/estrichone-kombi:/opt/estrichone-kombi "
		+ `ghcr.io/florians25/estrichone-installer:${version} backup-archiv`;
}

/**
 * „192.168.1.50“, „https://kombi.firma.de/“ oder „kombi.local:8444“ → Adressen beider Dienste (Ports aus den Feldern;
 * ein angehängter Port wird ignoriert, weil die Dienste verschiedene Ports haben). IPv6 in eckigen Klammern.
 */
export function kombiAddresses(input: string, backupPort = KOMBI_BACKUP_PORT, archivePort = KOMBI_ARCHIVE_PORT): { backup: string; archive: string } {
	const text = input.trim().replace(/^https?:\/\//i, "").replace(/\/.*$/, "");
	const host = text.startsWith("[") ? text.slice(0, text.indexOf("]") + 1) : text.replace(/:\d+$/, "");
	if (!host || /\s/.test(host) || (!host.startsWith("[") && !/^[A-Za-z0-9.-]+$/.test(host))) {
		throw new Error("Bitte die Adresse des Kombiservers angeben, z. B. 192.168.1.50 oder kombi.firma.de.");
	}
	for (const port of [backupPort, archivePort]) {
		if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error(`Ungültiger Port: ${port}`);
	}
	if (backupPort === archivePort) throw new Error("Backup-Ziel und Archiv laufen auf verschiedenen Ports (Standard 8444 und 8443).");
	return { backup: `${host}:${backupPort}`, archive: `${host}:${archivePort}` };
}

/** Gleiche Gegenstelle? (für „bereits gekoppelt“ beim Erweitern eines vorhandenen Backup-Ziels bzw. Archivs) */
export function sameEndpoint(a: string | null | undefined, b: string): boolean {
	if (!a) return false;
	const norm = (v: string) => v.trim().toLowerCase().replace(/^https?:\/\//, "").replace(/\/+$/, "");
	return norm(a) === norm(b);
}

export type KombiStepState = "paired" | "already" | "failed" | "skipped";
export interface KombiStep { service: "backup" | "archive"; state: KombiStepState; text: string }

/** Zusammenfassung nach dem Koppeln: ok = mindestens ein Dienst gekoppelt und keiner fehlgeschlagen. */
export function kombiSummary(steps: KombiStep[]): { ok: boolean; partial: boolean; text: string } {
	const failed = steps.filter((s) => s.state === "failed");
	const done = steps.filter((s) => s.state === "paired" || s.state === "already");
	return {
		ok: failed.length === 0 && done.length > 0,
		partial: failed.length > 0 && done.length > 0,
		text: steps.map((s) => `${s.service === "backup" ? "Backup-Ziel" : "Archivserver"}: ${s.text}`).join("\n"),
	};
}

/**
 * „Abschließen & Archivieren“: nicht eingerichtet → ausblenden; eingerichtet, aber nicht erreichbar → deaktiviert mit Hinweis.
 * Quelle ist das Statusbanner (für alle Angemeldeten lesbar), nicht die Archivverwaltung (nur Administratoren).
 */
export function completeArchiveOption(health: HealthBanner | null | undefined, canArchive: boolean): { visible: boolean; enabled: boolean; tooltip: string } {
	const archive = health?.archive;
	if (!canArchive || !archive?.configured) return { visible: false, enabled: false, tooltip: "" };
	if (archive.reachable === false) {
		return { visible: true, enabled: false, tooltip: "Archivserver gerade nicht erreichbar – Bauakte jetzt nur abschließen und später unter „Aktionen → Archivnachweis“ archivieren." };
	}
	return { visible: true, enabled: true, tooltip: "Bauakte abschließen und alle Dokumente sofort revisionssicher auf dem Archivserver ablegen." };
}

/** Fortschritt der Archivierung einer Bauakte aus den Archiveinträgen. */
export function archiveProgress(records: { status: string }[]): { total: number; archived: number; failed: number; done: boolean; text: string } {
	const total = records.length;
	const archived = records.filter((r) => r.status === "archived").length;
	const failed = records.filter((r) => r.status === "failed").length;
	const done = total > 0 && archived + failed === total;
	const text = total === 0 ? "Keine Dokumente zu archivieren."
		: done && failed === 0 ? `Alle ${total} Dokumente archiviert.`
			: done ? `${archived} von ${total} archiviert, ${failed} fehlgeschlagen – erneut versuchen unter „Aktionen → Archivnachweis“.`
				: `${archived} von ${total} Dokumenten archiviert …`;
	return { total, archived, failed, done, text };
}
