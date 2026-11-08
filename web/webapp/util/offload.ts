/** Speicher-Auslagerung (docs/speicher.md): Typen der API und Anzeigewerte, ohne UI5-Abhängigkeit (mit Vitest geprüft). */
import { dateTime, fileSize } from "./format";

export type OffloadState = "pending" | "offloaded" | "recalled" | "missing";
export type StatusLevel = "ok" | "warning" | "critical";

export interface OffloadConnectionDto {
	address: string; serverName: string; serverVersion: string | null; caFingerprint: string; fingerprintDisplay: string;
	pairedAt: string | null; lastContactAt: string | null; lastError: string | null; reachable: boolean;
}
export interface OffloadSettingsDto {
	autoWhenLow: boolean; autoByRules: boolean; thresholdPercent: number; thresholdFreeGb: number | null; minAgeMonths: number;
	onlyCompletedBauakten: boolean; includePhotos: boolean; includeVideos: boolean; includeDocuments: boolean; requireBackup: boolean; cacheLimitMb: number;
}
export interface OffloadServerStatus {
	name: string; version: string; diskFreeBytes: number | null; diskTotalBytes: number | null;
	offload: { objects: number; bytes: number; trashObjects: number; trashBytes: number; trashDays: number;
		lastVerification: { at: string; ok: boolean; objectsChecked: number; bytesChecked: number; errors: string[] } | null } | null;
}
export interface OffloadJobStatus {
	running: boolean; kind: "offload" | "recall" | null; total: number; processed: number; failed: number; bytes: number;
	startedAt: string | null; finishedAt: string | null; message: string | null;
}
export interface OffloadOverview {
	status: { level: StatusLevel; text: string };
	paired: boolean;
	connection: OffloadConnectionDto | null;
	server: OffloadServerStatus | null;
	settings: OffloadSettingsDto;
	disk: { freeBytes: number | null; totalBytes: number | null; freePercent: number | null; low: boolean; bytesToFree: number };
	counts: { offloadedFiles: number; offloadedBlobs: number; offloadedBytes: number; pendingBlobs: number; pendingBytes: number; missingBlobs: number;
		candidateFiles: number; candidateBytes: number; cacheBytes: number };
	backupAvailable: boolean;
	lastCopy: { requestId: string; status: string; message: string | null; at: string; files: number | null } | null;
	job: OffloadJobStatus;
	lastRunAt: string | null;
	lastRunMessage: string | null;
}
export interface OffloadedBauakteDto { bauakteId: string; title: string; files: number; bytes: number }
export interface OffloadedFileDto {
	fileId: string; name: string; kind: "photo" | "video" | "document"; bauakteId: string | null; bauakteTitle: string | null; sizeBytes: number;
	state: OffloadState; uploadedAt: string; offloadedAt: string | null; backedUpAt: string | null; lastError: string | null;
}
export interface OffloadedFilesPage { items: OffloadedFileDto[]; total: number }
export interface OffloadAccepted { message: string; requestId?: string | null }

const levelState: Record<StatusLevel, string> = { ok: "Success", warning: "Warning", critical: "Error" };
const stripType: Record<StatusLevel, string> = { ok: "Success", warning: "Warning", critical: "Error" };

/** Belegung in Prozent (0–100) für ProgressIndicator; null bei unbekannter Größe. */
export function usedPercent(free: number | null | undefined, total: number | null | undefined): number | null {
	if (free == null || !total) return null;
	return Math.max(0, Math.min(100, Math.round((1 - free / total) * 1000) / 10));
}

/** Anzeigewerte der Seite aus der Übersicht. */
export function offloadView(o: OffloadOverview): Record<string, unknown> {
	const used = usedPercent(o.disk.freeBytes, o.disk.totalBytes);
	const storageUsed = usedPercent(o.server?.diskFreeBytes, o.server?.diskTotalBytes);
	const threshold = o.settings.thresholdPercent;
	const job = o.job;
	const v = o.server?.offload?.lastVerification ?? null;
	return {
		statusType: stripType[o.status.level],
		statusState: levelState[o.status.level],
		statusText: o.status.text,
		serverUsed: used ?? 0,
		serverUsedLabel: `${(used ?? 0).toLocaleString("de-DE", { maximumFractionDigits: 1 })} %`,
		serverUsedText: o.disk.totalBytes ? `${fileSize(o.disk.totalBytes - (o.disk.freeBytes ?? 0))} von ${fileSize(o.disk.totalBytes)} belegt · ${fileSize(o.disk.freeBytes ?? 0)} frei (${(o.disk.freePercent ?? 0).toLocaleString("de-DE", { maximumFractionDigits: 1 })} %)` : "Unbekannt",
		serverState: o.disk.low ? "Warning" : (used ?? 0) > 100 - threshold - 5 ? "Information" : "Success",
		thresholdText: `Knapp unter ${threshold} % frei${o.settings.thresholdFreeGb ? ` oder unter ${o.settings.thresholdFreeGb} GB` : ""}`
			+ (o.disk.low && o.disk.bytesToFree > 0 ? ` · zum Ziel fehlen ${fileSize(o.disk.bytesToFree)}` : ""),
		storageUsed: storageUsed ?? 0,
		storageUsedLabel: `${(storageUsed ?? 0).toLocaleString("de-DE", { maximumFractionDigits: 1 })} %`,
		storageUsedText: o.server && o.server.diskTotalBytes ? `${fileSize(o.server.diskTotalBytes - (o.server.diskFreeBytes ?? 0))} von ${fileSize(o.server.diskTotalBytes)} belegt` : o.paired ? "Nicht erreichbar" : "Kein Storage gekoppelt",
		storageObjects: o.server?.offload ? `${o.server.offload.objects} Objekte (${fileSize(o.server.offload.bytes)} verschlüsselt)`
			+ (o.server.offload.trashObjects ? ` · Papierkorb ${o.server.offload.trashObjects} (${o.server.offload.trashDays} Tage)` : "") : "–",
		verification: v ? (v.ok ? `In Ordnung · ${dateTime(v.at)} · ${v.objectsChecked} Objekte` : `${v.errors.length} Abweichung(en) · ${dateTime(v.at)}`) : "Noch nicht geprüft",
		verificationState: v ? (v.ok ? "Success" : "Error") : "None",
		offloaded: `${o.counts.offloadedFiles} Dateien · ${fileSize(o.counts.offloadedBytes)}`,
		pending: o.counts.pendingBlobs ? `${o.counts.pendingBlobs} übertragen und geprüft (${fileSize(o.counts.pendingBytes)}), Sicherungskopie ausstehend` : "",
		candidates: `${o.counts.candidateFiles} Dateien (${fileSize(o.counts.candidateBytes)}) entsprechen den Regeln`,
		cache: fileSize(o.counts.cacheBytes),
		contact: o.connection ? (o.connection.reachable ? `Erreichbar · ${dateTime(o.connection.lastContactAt)}` : `Nicht erreichbar · zuletzt ${dateTime(o.connection.lastContactAt)}`) : "",
		contactState: o.connection?.reachable ? "Success" : "Warning",
		backup: !o.settings.requireBackup ? "Aus: Originale liegen nach dem Auslagern nur auf dem Storage (nicht empfohlen)"
			: !o.backupAvailable ? "Kein Backup-Dienst eingerichtet; es wird nichts lokal gelöscht"
			: o.lastCopy ? `${copyText(o.lastCopy.status)} · ${dateTime(o.lastCopy.at)}${o.lastCopy.message ? ` · ${o.lastCopy.message}` : ""}`
			: "Bereit: jedes Original wird vor dem lokalen Löschen zusätzlich gesichert",
		backupState: !o.settings.requireBackup || !o.backupAvailable || o.lastCopy?.status === "failed" ? "Warning" : "Success",
		jobRunning: job.running,
		jobPercent: job.total ? Math.round((job.processed / job.total) * 100) : 0,
		jobText: job.running
			? `${job.kind === "recall" ? "Rückholen" : "Auslagern"}: ${job.processed} von ${job.total}${job.failed ? `, ${job.failed} fehlgeschlagen` : ""} · ${fileSize(job.bytes)}`
			: job.message ? `${job.message}${job.finishedAt ? ` (${dateTime(job.finishedAt)})` : ""}` : o.lastRunMessage ? `${o.lastRunMessage} (${dateTime(o.lastRunAt)})` : "Noch kein Lauf",
		canRun: o.paired && !job.running,
		hasMissing: o.counts.missingBlobs > 0,
	};
}

function copyText(status: string): string {
	switch (status) {
		case "succeeded": return "Letzte Sicherungskopie erfolgreich";
		case "failed": return "Letzte Sicherungskopie fehlgeschlagen";
		case "running": return "Sicherungskopie läuft";
		default: return "Sicherungskopie angefordert (Backup-Dienst holt sie binnen einer Minute ab)";
	}
}

const stateText: Record<OffloadState, { text: string; state: string }> = {
	pending: { text: "Wartet auf Sicherungskopie", state: "Information" },
	offloaded: { text: "Auf dem Storage", state: "Success" },
	recalled: { text: "Wieder lokal", state: "None" },
	missing: { text: "Fehlt, aus Sicherung zurückholen", state: "Error" },
};
const kindText = { photo: "Foto", video: "Video", document: "Dokument" } as const;

export function offloadFileRow(f: OffloadedFileDto): Record<string, unknown> {
	return {
		...f,
		size: fileSize(f.sizeBytes),
		kindText: kindText[f.kind] ?? f.kind,
		stateText: stateText[f.state]?.text ?? f.state,
		stateState: stateText[f.state]?.state ?? "None",
		when: f.offloadedAt ? `ausgelagert ${dateTime(f.offloadedAt)}` : `übertragen ${dateTime(f.uploadedAt)}`,
		canRecall: f.state === "offloaded" || f.state === "missing" || f.state === "pending",
	};
}

export interface StorageHint { type: "Information" | "Error"; text: string }

/**
 * Hinweis beim Öffnen einer Datei, deren Original ausgelagert ist („wird vom Storage geladen“) bzw. klare Meldung, wenn der
 * Storage nicht erreichbar ist. Der Abruf des ersten Bytes lädt das Original zugleich in den Zwischenspeicher des Servers.
 */
export async function storageHint(fileId: string, fetcher: typeof fetch = fetch): Promise<StorageHint | null> {
	try {
		const where = await fetcher(`/api/v1/files/offload/locations?ids=${encodeURIComponent(fileId)}`, { credentials: "same-origin" });
		if (!where.ok) return null;
		const list = (await where.json()) as { fileId: string; state: OffloadState }[];
		const entry = list.find((l) => l.fileId === fileId);
		if (!entry) return null;
		if (entry.state === "missing") {
			return { type: "Error", text: "Das Original fehlt auf dem Storage. Bitte die Verwaltung informieren (Speicher-Auslagerung › Aus Sicherung zurückholen)." };
		}
		const probe = await fetcher(`/api/v1/files/${encodeURIComponent(fileId)}/content`, { credentials: "same-origin", headers: { Range: "bytes=0-0" } });
		if (probe.status === 503) {
			const problem = (await probe.json().catch(() => null)) as { detail?: string } | null;
			return { type: "Error", text: problem?.detail ?? "Der Storage ist derzeit nicht erreichbar. Es gehen keine Daten verloren. Bitte später erneut versuchen." };
		}
		return { type: "Information", text: "Das Original wird vom Storage geladen (Speicher-Auslagerung). Dies kann einen Moment dauern." };
	} catch {
		return null;
	}
}
