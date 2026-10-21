import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import { apiUrl, del, get, post } from "../../util/api";
import { dateTime, fileSize } from "../../util/format";
import { openForm } from "../../util/forms";
import type { BackupTargetDto } from "../../util/infraModels";

/** Backup Center › Backup-Ziele: koppeln per Code, Status je Ziel, Notfallzugang. @namespace estrichone.controller.admin */
export default class BackupTargets extends BaseController {
	onInit(): void {
		this.local({ items: [], busy: false });
	}

	onRouteMatched(): void { void this.load(); }

	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		this.model().setProperty("/busy", true);
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
