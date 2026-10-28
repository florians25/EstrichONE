import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import { apiUrl, del, get, post } from "../../util/api";
import { dateTime, fileSize } from "../../util/format";
import { openForm } from "../../util/forms";
import type { BackupMirrorDto, BackupMirrorsResponse, BackupTargetDto, CreateBackupMirrorResponse } from "../../util/infraModels";
import { PeerUpdatePanel } from "../../util/peerUpdatePanel";
import type { PeerUpdateDto } from "../../util/peerUpdates";

/** Backup Center › Backup-Ziele: koppeln per Code, Status je Ziel, Notfallzugang. @namespace estrichone.controller.admin */
export default class BackupTargets extends BaseController {
	private peers!: PeerUpdatePanel;

	onInit(): void {
		this.local({ items: [], busy: false, mirrors: [], mirrorsAvailable: false, peers: PeerUpdatePanel.initial() });
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
