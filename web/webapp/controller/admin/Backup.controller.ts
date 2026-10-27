import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import { apiUrl, get, post, put } from "../../util/api";
import { dateTime, fileSize } from "../../util/format";
import { openForm } from "../../util/forms";
import type { BackupRunInfo, BackupStatusResponse } from "../../util/models";
import type { BackupPreferences, BackupSnapshotsResponse, RestoreProgress } from "../../util/infraModels";

const info = (r: BackupRunInfo | null) => (r
	? { text: `${r.status === "succeeded" ? "Erfolgreich" : r.status === "running" ? "Läuft" : "Fehlgeschlagen"} · ${dateTime(r.at)} · ${r.message}`,
		state: r.status === "succeeded" ? "Success" : r.status === "running" ? "Information" : "Error" }
	: { text: "Noch nie", state: "None" });

const age = (iso: string) => {
	const hours = Math.round((Date.now() - new Date(iso).getTime()) / 3_600_000);
	return hours < 1 ? "gerade eben" : hours < 48 ? `vor ${hours} Stunden` : `vor ${Math.round(hours / 24)} Tagen`;
};

const restoreView = (p: RestoreProgress | null) => {
	if (!p) return { active: false, text: "", type: "Information", percent: 0, phase: "" };
	const active = p.scope === "full" && ["requested", "running", "restored"].includes(p.status);
	const what = p.scope === "full" ? "Wiederherstellung des Systems" : "Wiederherstellung einer Datei";
	const text = active ? `${what} aus Sicherung ${p.snapshot.slice(0, 8)} läuft – EstrichONE ist für alle Benutzer gesperrt. ${p.message}`
		: p.status === "completed" ? `${what} abgeschlossen (${dateTime(p.at)}): ${p.message}`
		: p.status === "failed" ? `${what} fehlgeschlagen (${dateTime(p.at)}): ${p.message}`
		: `${what}: ${p.message}`;
	return { active, text, type: active ? "Information" : p.status === "completed" ? "Success" : p.status === "failed" ? "Error" : "Information", percent: p.percent, phase: `${p.phase} · ${p.percent} %` };
};

/**
 * Backup Center: Zustand und Verlauf, Liste der Sicherungen mit Wiederherstellen (ganzes System oder eine Datei),
 * Zeitplan/Aufbewahrung/Meldungen, Notfallblatt und Befehl für die Neuinstallation. Aktualisiert sich alle 15 Sekunden,
 * während einer Wiederherstellung alle 3 Sekunden. @namespace estrichone.controller.admin
 */
export default class Backup extends BaseController {
	private timer: ReturnType<typeof setTimeout> | undefined;
	private snapshots: BackupSnapshotsResponse | null = null;

	onInit(): void {
		this.local({ s: null, tab: "status", source: "primary", repositories: [], snapshots: [], snapshotCount: 0, p: null, restore: restoreView(null), restoreCommand: "", planText: "" });
	}

	onRouteMatched(): void {
		void this.load(true);
	}

	private schedule(): void {
		clearTimeout(this.timer);
		this.timer = setTimeout(() => {
			if (this.v().getDomRef()) void this.load(false);
		}, this.model().getProperty("/restore/active") ? 3_000 : 15_000);
	}

	private async load(first: boolean): Promise<void> {
		try {
			const restore = await get<RestoreProgress | null>("backup/restore").catch(() => null);
			this.model().setProperty("/restore", restoreView(restore ?? null));
			if (restore && restoreView(restore).active) { this.schedule(); return; }
			const s = await get<BackupStatusResponse>("backup/status");
			this.model().setProperty("/s", s);
			this.model().setProperty("/run", info(s.lastRun));
			this.model().setProperty("/success", info(s.lastSuccess));
			this.model().setProperty("/check", info(s.lastCheck));
			this.model().setProperty("/test", info(s.lastRestoreTest));
			if (first || !this.model().getProperty("/p")) await this.loadSettings();
			this.snapshots = await get<BackupSnapshotsResponse>("backup/snapshots");
			this.model().setProperty("/repositories", this.snapshots.repositories.map((r) => ({ key: r.key, label: r.label || r.key })));
			this.showSnapshots();
			if (first) {
				const server = await get<{ serverVersion: string }>("server-info");
				this.model().setProperty("/restoreCommand", "sudo mkdir -p /opt/estrichone && sudo docker run --rm -it -v /var/run/docker.sock:/var/run/docker.sock "
					+ `-v /opt/estrichone:/opt/estrichone -v /mnt:/mnt ghcr.io/florians25/estrichone-installer:${server.serverVersion.split("+")[0]} restore`);
			}
			this.schedule();
		} catch (error) {
			this.showError(error);
		}
	}

	private async loadSettings(): Promise<void> {
		const p = await get<BackupPreferences>("backup/settings");
		this.model().setProperty("/p", { ...p, incrementalKey: String(p.incrementalEveryHours) });
		this.model().setProperty("/planText", `Vollsicherung täglich um ${p.dailyTime} Uhr${p.incrementalEveryHours > 0 ? `, inkrementell alle ${p.incrementalEveryHours} h` : ""} · `
			+ `Aufbewahrung ${p.keepDaily} täglich / ${p.keepWeekly} wöchentlich / ${p.keepMonthly} monatlich / ${p.keepYearly} jährlich`);
	}

	private showSnapshots(): void {
		const key = this.model().getProperty("/source") as string;
		const repo = this.snapshots?.repositories.find((r) => r.key === key) ?? this.snapshots?.repositories[0];
		if (repo && repo.key !== key) this.model().setProperty("/source", repo.key);
		const list = (repo?.snapshots ?? []).map((s) => ({
			...s, when: dateTime(s.time), age: age(s.time), files: s.files != null ? s.files.toLocaleString("de-DE") : "–",
			size: s.bytes != null ? `${fileSize(s.bytes)} · ${s.addedBytes != null ? `+${fileSize(s.addedBytes)}` : "–"}` : "–",
		}));
		this.model().setProperty("/snapshots", list);
		this.model().setProperty("/snapshotCount", String(list.length));
	}

	onSource(): void { this.showSnapshots(); }

	async onRun(): Promise<void> {
		await this.run(() => post("backup/run"), "Sicherung angefordert – startet innerhalb von 30 Sekunden");
		void this.load(false);
	}

	async onSaveSettings(): Promise<void> {
		const p = this.model().getProperty("/p") as BackupPreferences & { incrementalKey: string };
		const body: BackupPreferences = {
			dailyTime: p.dailyTime, incrementalEveryHours: Number(p.incrementalKey), keepDaily: Number(p.keepDaily), keepWeekly: Number(p.keepWeekly),
			keepMonthly: Number(p.keepMonthly), keepYearly: Number(p.keepYearly), notifyAdmins: p.notifyAdmins, notifyOnSuccess: p.notifyOnSuccess,
			weeklyRestoreTest: p.weeklyRestoreTest,
		};
		if (await this.run(() => put("backup/settings", body), "Zeitplan gespeichert – gilt ab dem nächsten Takt des Backup-Dienstes") !== undefined) await this.loadSettings();
	}

	private row(e: Event): { id: string; shortId: string; when: string } {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as { id: string; shortId: string; when: string };
	}

	async onRestoreFull(e: Event): Promise<void> {
		const snap = this.row(e);
		const result = await openForm<unknown>({
			title: "System wiederherstellen", submitText: "Wiederherstellen",
			fields: [
				{ key: "warn", type: "info", label: `Alle Daten werden auf den Stand vom ${snap.when} zurückgesetzt: Datenbank, Fotos, Dokumente, Videos und Einstellungen. `
					+ "Änderungen danach gehen verloren (die bisherige Datenbank bleibt als Reserve auf dem Server). Während der Wiederherstellung ist EstrichONE "
					+ "für alle gesperrt (Wartungsmodus); die Apps behalten ungesendete Eingaben und gleichen danach neu ab. Alle müssen sich neu anmelden." },
				{ key: "confirmation", label: "Zur Bestätigung WIEDERHERSTELLEN eingeben", required: true, placeholder: "WIEDERHERSTELLEN" },
			],
			submit: (v) => post("backup/restore", { snapshot: snap.id, source: this.model().getProperty("/source"), scope: "full", confirmation: String(v.confirmation).trim() }),
		});
		if (result) {
			this.toast("Wiederherstellung angefordert");
			void this.load(false);
		}
	}

	async onRestoreFile(e: Event): Promise<void> {
		const snap = this.row(e);
		const result = await openForm<unknown>({
			title: "Einzelne Datei wiederherstellen", submitText: "Datei zurückholen",
			fields: [
				{ key: "hint", type: "info", label: `Holt eine Datei (Original und Vorschaubilder) aus der Sicherung vom ${snap.when} zurück, z. B. wenn ein Foto beschädigt ist oder fehlt. `
					+ "Die Datei-ID steht in der Bauakte unter Dokumente → Details. Alles andere bleibt unverändert, niemand wird gesperrt." },
				{ key: "fileId", label: "Datei-ID", required: true, placeholder: "z. B. 01a15700-0000-7000-8000-00000000f11e" },
			],
			submit: (v) => post("backup/restore", { snapshot: snap.id, source: this.model().getProperty("/source"), scope: "file", fileId: String(v.fileId).trim() }),
		});
		if (result) {
			MessageBox.information("Die Datei wird innerhalb von 30 Sekunden zurückgeholt. Das Ergebnis erscheint oben auf dieser Seite.", { title: "Angefordert" });
			void this.load(false);
		}
	}

	onEmergencySheet(): void {
		window.open(apiUrl("backup/emergency-sheet"), "_blank", "noopener");
	}

	async onCopyCommand(): Promise<void> {
		await navigator.clipboard.writeText(this.model().getProperty("/restoreCommand") as string).catch(() => undefined);
		this.toast("Befehl kopiert");
	}
}
