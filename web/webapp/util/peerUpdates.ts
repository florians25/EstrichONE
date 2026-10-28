/** Fernaktualisierung der Zusatzserver (Backup-Ziele, Ziele, die ziehen, Archivserver) – docs/backup.md. */
import { dateTime } from "./format";

export type PeerKind = "backup-target" | "backup-mirror" | "archive";
export type PeerUpdateStatus = "none" | "requested" | "delivered" | "running" | "succeeded" | "rolled_back" | "failed";

export interface PeerUpdateDto {
	key: string; kind: PeerKind; name: string; installedVersion: string | null; availableVersion: string; updateAvailable: boolean;
	updaterAvailable: boolean; updaterVersion: string | null; reachable: boolean; lastCheckedAt: string | null; status: PeerUpdateStatus;
	targetVersion: string | null; previousVersion: string | null; message: string | null; requestedAt: string | null; finishedAt: string | null;
	autoUpdate: boolean; localCommand: string;
}
export interface PeerUpdatesResponse { currentVersion: string; items: PeerUpdateDto[] }
export interface PeerUpdateAllResponse { requested: number; items: PeerUpdateDto[] }

const kindText: Record<PeerKind, string> = { "backup-target": "Backup-Ziel", "backup-mirror": "Backup-Ziel, das zieht", archive: "Archivserver" };

export const activeStatus = (s: PeerUpdateStatus): boolean => s === "requested" || s === "delivered" || s === "running";

/** Anzeige eines Eintrags: Version, Zustand (ObjectStatus), Hinweis, ob „Aktualisieren“ möglich ist. */
export function peerRow(p: PeerUpdateDto): Record<string, unknown> {
	const active = activeStatus(p.status);
	let stateText: string;
	let state: string;
	if (active) {
		stateText = p.status === "requested"
			? (p.kind === "backup-mirror" ? `Wartet auf Abholung (meldet sich alle 2 Minuten) – Version ${p.targetVersion}` : `Beauftragt – Version ${p.targetVersion}`)
			: `Wird aktualisiert auf ${p.targetVersion} …`;
		state = "Information";
	} else if (p.status === "failed" && p.finishedAt) {
		stateText = `Aktualisierung fehlgeschlagen (${dateTime(p.finishedAt)}): ${p.message ?? ""}`;
		state = "Error";
	} else if (p.status === "rolled_back") {
		stateText = `Zurückgerollt (${dateTime(p.finishedAt)}): ${p.message ?? `Version ${p.targetVersion} wurde nicht gesund.`}`;
		state = "Warning";
	} else if (p.updateAvailable) {
		stateText = `Version ${p.availableVersion} verfügbar`;
		state = "Warning";
	} else if (!p.installedVersion) {
		stateText = "Version noch unbekannt";
		state = "None";
	} else {
		stateText = p.status === "succeeded" ? `Aktuell – aktualisiert ${dateTime(p.finishedAt)}` : "Aktuell";
		state = "Success";
	}
	return {
		...p,
		kindText: kindText[p.kind] ?? p.kind,
		versionText: `Installiert ${p.installedVersion ?? "?"} · verfügbar ${p.availableVersion}`,
		stateText, state, active,
		canUpdate: p.updateAvailable && p.updaterAvailable && !active,
		needsLocal: p.updateAvailable && !p.updaterAvailable && !active,
		reachText: p.kind === "backup-mirror" ? (p.lastCheckedAt ? `zuletzt gemeldet ${dateTime(p.lastCheckedAt)}` : "noch nicht gemeldet")
			: p.reachable ? "Verbindung ok" : "zurzeit keine Verbindung",
	};
}
