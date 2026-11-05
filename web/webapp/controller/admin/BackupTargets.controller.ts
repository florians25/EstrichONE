import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import { apiUrl, del, get, post, problemMessage } from "../../util/api";
import { dateTime, fileSize } from "../../util/format";
import { openForm } from "../../util/forms";
import type { ArchiveConnectionDto, ArchiveOverview, BackupMirrorDto, BackupMirrorsResponse, BackupTargetDto, CreateBackupMirrorResponse } from "../../util/infraModels";
import { KOMBI_ARCHIVE_PORT, KOMBI_BACKUP_PORT, KOMBI_LOCATION_HINT, kombiAddresses, kombiCommand, kombiSummary, sameEndpoint, type KombiStep } from "../../util/kombiserver";
import { session } from "../../util/session";
import { PeerUpdatePanel } from "../../util/peerUpdatePanel";
import type { PeerUpdateDto } from "../../util/peerUpdates";

/** Backup Center › Backup-Ziele: koppeln per Code, Status je Ziel, Notfallzugang. @namespace estrichone.controller.admin */
export default class BackupTargets extends BaseController {
	private peers!: PeerUpdatePanel;

	onInit(): void {
		this.local({ items: [], busy: false, mirrors: [], mirrorsAvailable: false, peers: PeerUpdatePanel.initial(),
			canKombi: session.has("settings.manage"), kombiCommand: kombiCommand(session.data.server?.serverVersion || undefined), kombiHint: KOMBI_LOCATION_HINT });
		this.peers = new PeerUpdatePanel({ model: () => this.model(), run: (w, s) => this.run(w, s), confirm: (t, o) => this.confirm(t, o) }, ["backup-target", "backup-mirror"]);
	}

	onExit(): void { this.peers.stop(); }

	onPeerUpdate(e: Event): void { void this.peers.update(this.peerRowOf(e)); }

	onPeerUpdateAll(): void { void this.peers.updateAll(); }

	onPeerAuto(e: Event): void { void this.peers.setAuto(this.peerRowOf(e), Boolean((e as Event<{ state: boolean }>).getParameter("state"))); }

	onPeerCommand(e: Event): void { this.peers.showCommand(this.peerRowOf(e)); }

	private peerRowOf(e: Event): PeerUpdateDto {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as PeerUpdateDto;
	}

	onRouteMatched(): void { void this.load(); }

	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		this.model().setProperty("/busy", true);
		void this.loadMirrors();
		void this.peers.load();
		const items = await this.run(() => get<BackupTargetDto[]>("backup/targets"));
		this.model().setProperty("/busy", false);
		const targets = items ?? [];
		this.statusLine([
			!targets.length && { text: "Noch kein Backup-Ziel gekoppelt – Sicherung liegt nur auf diesem Server", level: "warning" },
			...targets.filter((t) => !t.remote).map((t) => ({ text: `${t.name} nicht erreichbar`, level: "warning" as const })),
			...targets.filter((t) => t.lastRun?.status === "failed").map((t) => ({ text: `letzte Sicherung zu ${t.name} fehlgeschlagen`, level: "error" as const })),
			...targets.filter((t) => t.remote && !t.remote.appendOnly).map((t) => ({ text: `${t.name} erlaubt Löschen`, level: "warning" as const })),
		], `${targets.length} ${targets.length === 1 ? "Backup-Ziel" : "Backup-Ziele"} erreichbar und geschützt`);
		this.model().setProperty("/items", (items ?? []).map((t) => ({
			...t,
			paired: dateTime(t.pairedAt),
			runText: t.lastRun
				? `Letzte Sicherung dorthin: ${t.lastRun.status === "succeeded" ? "erfolgreich" : "fehlgeschlagen"} · ${dateTime(t.lastRun.at)}`
				: "Noch keine Sicherung dorthin (nächster Lauf des Backup-Dienstes, oder „Jetzt sichern“ unter Sicherung)",
			runState: !t.lastRun ? "Information" : t.lastRun.status === "succeeded" ? "Success" : "Error",
			remoteText: t.remote
				? `${t.remote.snapshots} Sicherungsstände · ${fileSize(t.remote.bytes)} belegt · ${t.remote.diskFreeBytes != null ? `${fileSize(t.remote.diskFreeBytes)} frei` : "Platz unbekannt"} · ${t.remote.appendOnly ? "nur anhängen" : "ACHTUNG: Löschen erlaubt"}`
				: "Ziel gerade nicht erreichbar",
			remoteState: !t.remote ? "Warning" : t.remote.appendOnly && t.remote.serverRunning ? "Success" : "Warning",
		})));
	}

	private async loadMirrors(): Promise<void> {
		const r = await this.run(() => get<BackupMirrorsResponse>("backup/mirrors"));
		this.model().setProperty("/mirrorsAvailable", r?.available ?? false);
		this.model().setProperty("/mirrors", (r?.mirrors ?? []).map((m) => ({
			...m,
			title: m.targetName ? `${m.name} (${m.targetName})` : m.name,
			stateText: !m.paired ? `Wartet auf Kopplung – Code gültig bis ${dateTime(m.codeExpiresAt)}`
				: m.reachable ? `Verbunden · zuletzt gemeldet ${dateTime(m.lastSeenAt)}` : `Nicht erreichbar – zuletzt gemeldet ${m.lastSeenAt ? dateTime(m.lastSeenAt) : "nie"}`,
			state: !m.paired ? "Information" : m.reachable && !m.lastReport?.error ? "Success" : "Error",
			detail: m.lastReport
				? `${m.lastReport.snapshots} Sicherungsstände · ${fileSize(m.lastReport.bytes)} gespiegelt${m.lastReport.diskFreeBytes != null ? ` · ${fileSize(m.lastReport.diskFreeBytes)} frei` : ""}${m.lastReport.error ? ` · Fehler: ${m.lastReport.error}` : ""}`
				: "Noch kein Abgleich",
		})));
	}

	async onCreateMirror(): Promise<void> {
		const result = await openForm<CreateBackupMirrorResponse>({
			title: "Backup-Ziel, das zieht", submitText: "Code erzeugen",
			fields: [
				{ key: "intro", type: "info", label: "Erzeugt einen Einmal-Code (24 Stunden gültig) und den Befehl für den Rechner zuhause." },
				{ key: "name", label: "Bezeichnung", placeholder: "z. B. NAS zuhause" },
			],
			submit: (v) => post<CreateBackupMirrorResponse>("backup/mirrors", { name: String(v.name ?? "").trim() || null }),
		});
		if (result) {
			MessageBox.information(`Auf dem Rechner zuhause (Linux mit Docker) einmal ausführen:\n\n${result.command}\n\nKopplungscode: ${result.code}\n\n`
				+ "Der Rechner holt danach stündlich neue Sicherungen und meldet sich hier.", { title: "Befehl für das Backup-Ziel" });
			void this.loadMirrors();
		}
	}

	async onRemoveMirror(e: Event): Promise<void> {
		const m = (e.getSource() as Control).getBindingContext("view")?.getObject() as BackupMirrorDto;
		if (!await this.confirm(`„${m.name}“ entfernen? Der Rechner kann danach nichts mehr abrufen; seine Kopie bleibt dort erhalten.`, { action: "Entfernen", danger: true })) return;
		await this.run(() => del(`backup/mirrors/${m.id}`), "Entfernt");
		void this.loadMirrors();
	}

	private row(e: Event): BackupTargetDto {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as BackupTargetDto;
	}

	async onPair(): Promise<void> {
		const result = await openForm<BackupTargetDto>({
			title: "Backup-Ziel koppeln", submitText: "Koppeln",
			fields: [
				{ key: "intro", type: "info", label: "Adresse und Kopplungscode zeigt der Zielrechner nach der Installation an (bzw. „sudo docker logs estrichone-backup-target“)." },
				{ key: "address", label: "Adresse", required: true, placeholder: "z. B. 192.168.1.60:8444 (wie vom Zielrechner angezeigt)" },
				{ key: "code", label: "Kopplungscode", required: true, placeholder: "ABCD-EFGH-JKLM", maxLength: 20 },
			],
			submit: (v) => post<BackupTargetDto>("backup/targets", { address: String(v.address).trim(), code: String(v.code).trim() }),
		});
		if (result) {
			MessageBox.success(`Gekoppelt mit „${result.name}“.\n\nFingerabdruck:\n${result.fingerprintDisplay}\n\nDie nächste Sicherung geht zusätzlich dorthin.${result.backupServiceConfigured ? "" : "\n\nHinweis: Der Backup-Dienst ist auf diesem Server nicht eingerichtet."}`,
				{ title: "Backup-Ziel verbunden" });
			void this.load();
		}
	}

	/**
	 * Kombiserver (Backup-Ziel + Archiv auf einem Rechner): EIN Code koppelt beide Dienste – nacheinander über die
	 * bestehenden Endpunkte. Bereits gekoppelte Dienste (vorhandenes Backup-Ziel bzw. Archiv erweitert) werden übersprungen.
	 */
	async onPairKombi(): Promise<void> {
		const result = await openForm<KombiStep[]>({
			title: "Kombiserver koppeln", submitText: "Beide koppeln", width: "40rem",
			fields: [
				{ key: "intro", type: "info", label: "Backup-Ziel und Archivserver auf einem Rechner, eingerichtet mit dem Installer-Modus „backup-archiv“. "
					+ "Adresse und Kopplungscode zeigt der Kombiserver nach der Installation an – der eine Code koppelt beide Dienste." },
				{ key: "hint", type: "info", label: KOMBI_LOCATION_HINT },
				{ key: "address", label: "Adresse des Kombiservers", required: true, placeholder: "z. B. 192.168.1.50 oder kombi.firma.de",
					validate: (v, all) => { try { kombiAddresses(String(v ?? ""), Number(all.backupPort), Number(all.archivePort)); return null; } catch (error) { return (error as Error).message; } } },
				{ key: "code", label: "Kopplungscode", required: true, placeholder: "ABCD-EFGH-JKLM-NPQR", maxLength: 24 },
				{ key: "ports", type: "section", label: "Ports (nur ändern, wenn bei der Installation geändert)" },
				{ key: "backupPort", label: "Port Backup-Ziel", type: "integer", min: 1, max: 65535, required: true },
				{ key: "archivePort", label: "Port Archivserver", type: "integer", min: 1, max: 65535, required: true },
			],
			initial: { backupPort: KOMBI_BACKUP_PORT, archivePort: KOMBI_ARCHIVE_PORT },
			submit: async (v) => {
				const addresses = kombiAddresses(String(v.address), Number(v.backupPort), Number(v.archivePort));
				const code = String(v.code).trim();
				const [targets, archive] = await Promise.all([
					get<BackupTargetDto[]>("backup/targets?live=false"), get<ArchiveOverview>("archive/status?live=false")]);
				const steps: KombiStep[] = [];
				if (targets.some((t) => sameEndpoint(t.address, addresses.backup))) {
					steps.push({ service: "backup", state: "already", text: "bereits gekoppelt" });
				} else {
					try {
						const t = await post<BackupTargetDto>("backup/targets", { address: addresses.backup, code });
						steps.push({ service: "backup", state: "paired", text: `gekoppelt mit „${t.name}“ (Fingerabdruck ${t.fingerprintDisplay})` });
					} catch (error) {
						steps.push({ service: "backup", state: "failed", text: problemMessage(error) });
					}
				}
				if (sameEndpoint(archive.connection?.address, addresses.archive)) {
					steps.push({ service: "archive", state: "already", text: "bereits gekoppelt" });
				} else {
					if (archive.connected && !await this.confirm(`Dieser Server ist bereits mit dem Archivserver „${archive.connection?.serverName}“ gekoppelt. `
						+ "Durch den Archivserver des Kombiservers ersetzen? Bereits archivierte Dokumente bleiben auf dem bisherigen Archivserver.", { action: "Ersetzen", danger: true })) {
						steps.push({ service: "archive", state: "skipped", text: "nicht gekoppelt (bisheriger Archivserver bleibt)" });
					} else {
						try {
							const a = await post<ArchiveConnectionDto>("archive/pair", { address: addresses.archive, code });
							steps.push({ service: "archive", state: "paired", text: `gekoppelt mit „${a.serverName}“ (Fingerabdruck ${a.fingerprintDisplay})` });
						} catch (error) {
							steps.push({ service: "archive", state: "failed", text: problemMessage(error) });
						}
					}
				}
				const summary = kombiSummary(steps);
				if (!summary.ok && !summary.partial) throw new Error(summary.text);
				return steps;
			},
		});
		if (result) {
			const summary = kombiSummary(result);
			const text = `${summary.text}\n\nFingerabdrücke mit der Anzeige des Kombiservers vergleichen. Die nächste Sicherung geht zusätzlich dorthin, `
				+ "Dokumente werden nach den Archivregeln archiviert.";
			if (summary.ok) MessageBox.success(text, { title: "Kombiserver verbunden" });
			else MessageBox.warning(`${text}\n\nDen fehlgeschlagenen Dienst einzeln koppeln (Code am Kombiserver neu: Installer mit -e ESTRICHONE_NEW_CODE=1).`, { title: "Kombiserver teilweise verbunden" });
			void this.load();
		}
	}

	/** Kombiserver hinter NAT (Topologie A): Backup-Ziel zieht; derselbe Code koppelt danach das Archiv. */
	async onCreateKombiMirror(): Promise<void> {
		const created = await openForm<CreateBackupMirrorResponse>({
			title: "Kombiserver, der zieht", submitText: "Code erzeugen", width: "40rem",
			fields: [
				{ key: "intro", type: "info", label: "Für einen Kombiserver ohne feste Adresse (zuhause hinter dem Router), wenn dieser Server öffentlich erreichbar ist: "
					+ "Das Backup-Ziel holt die Sicherungen selbst. Das Archiv muss für diesen Server erreichbar sein (Port 8443 weiterleiten oder Tunnel, z. B. WireGuard)." },
				{ key: "hint", type: "info", label: KOMBI_LOCATION_HINT },
				{ key: "name", label: "Bezeichnung", placeholder: "z. B. Kombiserver zuhause" },
			],
			submit: (v) => post<CreateBackupMirrorResponse>("backup/mirrors", { name: String(v.name ?? "").trim() || null, combined: true }),
		});
		if (!created) return;
		void this.loadMirrors();
		const archive = await openForm<ArchiveConnectionDto>({
			title: "Archiv des Kombiservers koppeln", submitText: "Archiv koppeln", width: "44rem",
			fields: [
				{ key: "step1", type: "info", label: `1. Auf dem Kombiserver (Linux mit Docker) einmal ausführen:\n${created.command}` },
				{ key: "step2", type: "info", label: "2. Sobald der Befehl fertig ist: Adresse, unter der dieser Server das Archiv erreicht (DynDNS mit weitergeleitetem Port 8443 bzw. Tunnel-Adresse), eingeben. Der Code ist derselbe." },
				{ key: "address", label: "Adresse des Archivs", required: true, placeholder: "z. B. kombi.dyndns.org:8443 oder 10.8.0.2:8443" },
				{ key: "code", label: "Kopplungscode", required: true, maxLength: 24 },
			],
			initial: { code: created.code },
			submit: (v) => post<ArchiveConnectionDto>("archive/pair", { address: String(v.address).trim(), code: String(v.code).trim() }),
		});
		MessageBox.information(archive
			? `Archiv gekoppelt mit „${archive.serverName}“ (Fingerabdruck ${archive.fingerprintDisplay}). Das Backup-Ziel meldet sich nach der Installation unter „Backup-Ziele, die ziehen“.`
			: `Archiv noch nicht gekoppelt. Später unter System → Archiv → „Archivserver koppeln“ mit dem Code ${created.code} (24 Stunden gültig) nachholen.`,
		{ title: "Kombiserver, der zieht" });
	}

	onRecovery(e: Event): void {
		window.open(apiUrl(`backup/targets/${this.row(e).id}/recovery`), "_blank", "noopener");
	}

	async onRemove(e: Event): Promise<void> {
		const t = this.row(e);
		if (!await this.confirm(`Backup-Ziel „${t.name}“ entfernen? Die Sicherungen auf dem Zielrechner bleiben dort erhalten.`, { action: "Entfernen", danger: true })) return;
		await this.run(() => del(`backup/targets/${t.id}`), "Entfernt");
		void this.load();
	}
}
