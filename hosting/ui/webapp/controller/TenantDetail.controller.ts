import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import Fragment from "sap/ui/core/Fragment";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import ObjectPageLayout from "sap/uxap/ObjectPageLayout";
import BaseController from "./BaseController";
import TenantFormDialog from "../util/TenantFormDialog";
import OperationLogDialog from "../util/OperationLogDialog";
import { del, get, post } from "../model/Api";
import { askPassword, download } from "../util/PasswordPrompt";

interface Detail {
	summary: { id: string; slug: string; companyName: string; openOperation?: { id: string } | null; state: string; version: string; planId: string; customerNumber?: string; contactName?: string; contactEmail?: string; domain: string };
	contactPhone?: string;
	notes?: string;
}

const CONFIRM: Record<string, [string, string]> = {
	stop: ["Instanz stoppen?", "Alle Dienste der Instanz werden angehalten. Die Kunden können EstrichONE bis zum Start nicht nutzen; die Sicherung ruht."],
	restart: ["Instanz neu starten?", "Alle Dienste werden neu gestartet (ca. 1 Minute nicht erreichbar)."],
	unlock: ["Sperre aufheben?", "Die Instanz ist danach wieder normal erreichbar."],
	provision: ["Erneut bereitstellen?", "Images werden geladen und die Instanz gestartet. Vorhandene Daten und Passwörter bleiben erhalten."]
};

const DONE: Record<string, string> = {
	start: "Start", stop: "Stopp", restart: "Neustart", unlock: "Entsperren", backup: "Sicherung", provision: "Bereitstellung",
	"initial-setup": "Ersteinrichtung", rollback: "Zurücknehmen", restore: "Wiederherstellung"
};

/**
 * Mandantendetail: Lebenszyklus, Status/Health, Ressourcen, Sicherung, Protokolle, Vorgänge, Ereignisse.
 * @namespace estrichone.hosting.controller
 */
export default class TenantDetailController extends BaseController {
	private readonly detail = new JSONModel({});
	private readonly logs = new JSONModel({ service: "api", lines: "200", level: "all", filter: "", text: "", count: 0, follow: false });
	private readonly events = new JSONModel({ items: [] });
	private readonly dlg = new JSONModel({});
	private readonly snapshots = new JSONModel({ items: [] });
	private readonly letter = new JSONModel({});
	private id = "";
	private timer?: ReturnType<typeof setInterval>;
	private logTimer?: ReturnType<typeof setInterval>;
	private readonly dialogs = new Map<string, Dialog>();
	private form?: TenantFormDialog;
	private operationLog?: OperationLogDialog;

	public onInit(): void {
		this.setModel(this.detail, "detail");
		this.setModel(this.logs, "logs");
		this.setModel(this.events, "events");
		this.setModel(this.dlg, "dlg");
		this.setModel(this.snapshots, "snapshots");
		this.setModel(this.letter, "letter");
		this.getRouter().getRoute("tenant")!.attachPatternMatched((event) => {
			this.id = (event.getParameter("arguments") as { id: string }).id;
			this.detail.setData({});
			this.logs.setProperty("/text", "");
			(this.byId("tenantObjectPage") as ObjectPageLayout).setSelectedSection(this.byId("secOverview")!.getId());
			void this.load();
			void this.loadLogs();
			clearInterval(this.timer);
			this.timer = setInterval(() => {
				if (this.isVisible()) {
					void this.load(true);
				}
			}, 5000);
		});
	}

	public onExit(): void {
		clearInterval(this.timer);
		clearInterval(this.logTimer);
	}

	public formatStorage(used?: number | null, limit?: number | null): string {
		return this.formatter.storageText(used, limit);
	}

	public formatMemory(used?: number | null, limit?: number | null): string {
		return used === null || used === undefined ? "–" : this.formatter.bytes(used) + (limit ? ` / ${this.formatter.bytes(limit)}` : "");
	}

	private async load(silent = false): Promise<void> {
		const action = async () => {
			const [detail, events, snapshots] = await Promise.all([
				get<Detail>(`/api/tenants/${this.id}`),
				get<{ items: unknown[] }>(`/api/audit?tenant=${this.id}&take=200`),
				get<{ items: unknown[] }>(`/api/tenants/${this.id}/snapshots`).catch(() => ({ items: [] }))
			]);
			this.detail.setData(detail);
			this.events.setData(events);
			this.snapshots.setData(snapshots);
		};
		if (silent) {
			await action().catch(() => undefined);
		} else {
			await this.run(action);
		}
	}

	public onBack(): void {
		this.navTo("tenants");
	}

	public async onRefreshStatus(): Promise<void> {
		await this.run(async () => {
			await post(`/api/tenants/${this.id}/refresh`);
			await this.load(true);
		}, this.byId("containerTable") as Control);
	}

	/** Einfache Aktionen (Start, Stopp, Neustart, Entsperren, Sichern, Bereitstellen) mit Rückfrage bei Störungen. */
	public onAction(event: Event): void {
		const action = (event.getSource() as Control).data("action") as string;
		const confirm = CONFIRM[action];
		if (confirm) {
			MessageBox.confirm(confirm[1], {
				title: confirm[0],
				emphasizedAction: MessageBox.Action.OK,
				onClose: (choice: string) => {
					if (choice === MessageBox.Action.OK) {
						void this.execute(action, {});
					}
				}
			});
		} else {
			void this.execute(action, {});
		}
	}

	private async execute(action: string, body: object): Promise<void> {
		const operation = await this.run(() => post<{ id: string }>(`/api/tenants/${this.id}/actions/${action}`, body));
		if (operation) {
			this.toast(`${DONE[action] ?? "Vorgang"} gestartet`);
			await this.load(true);
			await this.showOperation(operation.id);
		}
	}

	private async showOperation(operationId: string, onFinished?: () => void): Promise<void> {
		this.operationLog ??= new OperationLogDialog(this.getView()!);
		await this.operationLog.open(operationId, onFinished ?? (() => void this.load(true)));
	}

	public onOpenOperationLog(): void {
		const id = this.detail.getProperty("/summary/openOperation/id") as string | undefined;
		if (id) {
			void this.showOperation(id);
		}
	}

	public onOperation(event: Event): void {
		void this.showOperation((event.getSource() as Control).getBindingContext("detail")!.getProperty("id") as string);
	}

	private async dialog(name: string): Promise<Dialog> {
		let dialog = this.dialogs.get(name);
		if (!dialog) {
			dialog = (await Fragment.load({ id: this.getView()!.getId(), name: `estrichone.hosting.fragment.${name}`, controller: this })) as Dialog;
			this.getView()!.addDependent(dialog);
			this.dialogs.set(name, dialog);
		}
		return dialog;
	}

	public onDialogCancel(event: Event): void {
		let control = event.getSource() as Control | null;
		while (control && !(control instanceof Dialog)) {
			control = control.getParent() as Control | null;
		}
		(control as Dialog | null)?.close();
	}

	private markRequired(id: string): void {
		const field = this.byId(id) as unknown as { setValueState(state: string): void; setValueStateText(text: string): void; focus(): void };
		field.setValueState("Error");
		field.setValueStateText("Pflichtfeld");
		field.focus();
	}

	public async onLock(): Promise<void> {
		this.dlg.setData({ reason: "", stopInstance: false });
		(await this.dialog("LockDialog")).open();
	}

	public async onLockConfirm(): Promise<void> {
		const { reason, stopInstance } = this.dlg.getData();
		if (!reason?.trim()) {
			this.markRequired("lockReason");
			return;
		}
		(await this.dialog("LockDialog")).close();
		await this.execute("lock", { reason, stopInstance });
	}

	public async onUpdate(): Promise<void> {
		const versions = await this.run(() => get<{ versions: string[]; latest?: string; source: string; error?: string }>("/api/versions"));
		if (!versions) {
			return;
		}
		const current = this.detail.getProperty("/summary/version") as string;
		this.dlg.setData({ backupFirst: true, versions: versions.versions.filter((v) => v !== current), version: versions.latest !== current ? versions.latest ?? "" : "", source: versions.error ? `${versions.source} (${versions.error})` : versions.source });
		(await this.dialog("UpdateDialog")).open();
	}

	public async onUpdateConfirm(): Promise<void> {
		const version = ((this.dlg.getProperty("/version") as string) ?? "").trim();
		if (!version) {
			this.markRequired("updateVersion");
			return;
		}
		(await this.dialog("UpdateDialog")).close();
		await this.execute("update", { version, backupFirst: this.dlg.getProperty("/backupFirst") });
	}

	/** Löschen mit doppelter Bestätigung: Rückfrage, dann Kürzel eintippen (und optional Endsicherung). */
	public onDelete(): void {
		const company = this.detail.getProperty("/summary/companyName") as string;
		MessageBox.warning(`„${company}“ wirklich löschen? Im nächsten Schritt wird das Kürzel zur Bestätigung abgefragt.`, {
			title: "Mandant löschen",
			actions: [MessageBox.Action.YES, MessageBox.Action.CANCEL],
			emphasizedAction: MessageBox.Action.CANCEL,
			onClose: async (choice: string) => {
				if (choice === MessageBox.Action.YES) {
					this.dlg.setData({ confirmSlug: "", finalBackup: true });
					(await this.dialog("DeleteDialog")).open();
				}
			}
		});
	}

	public async onDeleteConfirm(): Promise<void> {
		const { confirmSlug, finalBackup } = this.dlg.getData();
		(await this.dialog("DeleteDialog")).close();
		const operation = await this.run(() => post<{ id: string }>(`/api/tenants/${this.id}/actions/delete`, { confirm: true, confirmSlug, finalBackup }));
		if (operation) {
			this.toast("Löschen gestartet");
			await this.showOperation(operation.id, () => {
				this.toast(finalBackup ? "Mandant gelöscht – Endsicherung liegt im Archiv" : "Mandant gelöscht");
				this.navTo(finalBackup ? "archives" : "tenants");
			});
		}
	}

	/** Letzte Aktualisierung zurücknehmen: vorherige Version und Datenstand der Vorab-Sicherung. */
	public onRollback(): void {
		const previous = this.detail.getProperty("/summary/previousVersion") as string;
		const snapshot = this.snapshots.getProperty("/rollbackSnapshot") as string | null;
		MessageBox.warning(
			`Zurück auf Version ${previous}${snapshot ? ` mit dem Datenstand der Vorab-Sicherung (${snapshot.slice(0, 8)}). Änderungen seit der Aktualisierung gehen verloren.` : ". Es gibt keine Vorab-Sicherung – die Daten bleiben unverändert (nur möglich, wenn die neue Version das Schema nicht geändert hat)."}`,
			{
				title: "Aktualisierung zurücknehmen",
				actions: ["Zurücknehmen", MessageBox.Action.CANCEL],
				emphasizedAction: "Zurücknehmen",
				onClose: (choice: string) => {
					if (choice === "Zurücknehmen") void this.execute("rollback", { confirm: true });
				}
			});
	}

	/** Wiederherstellen aus einer Sicherung (vorher wird der aktuelle Stand gesichert). */
	public onRestore(event: Event): void {
		const snapshot = (event.getSource() as Control).getBindingContext("snapshots")!.getObject() as { id: string; shortId: string; time: string };
		const when = this.formatter.dateTime(snapshot.time);
		MessageBox.warning(`Datenbank und Dateien auf den Stand vom ${when} (${snapshot.shortId}) zurücksetzen? Die Instanz ist dafür einige Minuten nicht erreichbar. Vorher wird der aktuelle Stand gesichert.`, {
			title: "Wiederherstellen",
			actions: ["Wiederherstellen", MessageBox.Action.CANCEL],
			emphasizedAction: MessageBox.Action.CANCEL,
			onClose: (choice: string) => {
				if (choice === "Wiederherstellen") void this.execute("restore", { snapshot: snapshot.id, confirm: true, backupFirst: true });
			}
		});
	}

	public async onExport(): Promise<void> {
		const password = await askPassword(this.getView()!, "Export herunterladen",
			"Der Export enthält das Sicherungs-Repository und den Schlüssel der Instanz (.env). Bitte nur verschlüsselt weitergeben.", "Herunterladen");
		if (!password) return;
		const result = await this.run(() => post<{ url: string; fileName: string }>(`/api/tenants/${this.id}/export`, { password }));
		if (result) {
			download(result.url, result.fileName);
			this.toast("Export wird heruntergeladen");
		}
	}

	public async onAccessLetter(): Promise<void> {
		const password = await askPassword(this.getView()!, "Zugangsbrief öffnen", "Der Zugangsbrief enthält das Startpasswort der ersten Admin-Person.", "Öffnen");
		if (!password) return;
		const letter = await this.run(() => post<object>(`/api/tenants/${this.id}/access-letter`, { password }));
		if (letter) {
			this.letter.setData(letter);
			(await this.dialog("AccessLetter")).open();
		}
	}

	public onPrintLetter(): void {
		document.body.classList.add("hostingPrintLetter");
		window.print();
		setTimeout(() => document.body.classList.remove("hostingPrintLetter"), 500);
	}

	public onCopyLetter(): void {
		const l = this.letter.getData() as { companyName: string; url: string; userName: string; initialPassword: string };
		const text = [`EstrichONE für ${l.companyName}`, `Adresse: ${l.url}`, `Benutzername: ${l.userName}`, `Startpasswort: ${l.initialPassword}`,
			"(Bitte nach der ersten Anmeldung ändern.)"].join("\n");
		void navigator.clipboard?.writeText(text).then(() => this.toast("Zugangsdaten kopiert"));
	}

	public onDiscardLetter(): void {
		MessageBox.confirm("Zugangsbrief verwerfen? Das Startpasswort ist danach nur noch beim Kunden bekannt.", {
			title: "Zugangsbrief verwerfen",
			onClose: async (choice: string) => {
				if (choice !== MessageBox.Action.OK) return;
				const done = await this.run(async () => {
					await del(`/api/tenants/${this.id}/access-letter`);
					return true;
				});
				if (done) {
					this.toast("Zugangsbrief verworfen");
					await this.load(true);
				}
			}
		});
	}

	/** Ankündigung oder Wartung nur für diese Instanz (Seite „Hinweise und Wartung“ mit Vorauswahl). */
	public onNotice(event: Event): void {
		this.getRouter().navTo("notices", { "?query": { tenant: this.id, kind: (event.getSource() as Control).data("kind") as string } });
	}

	public async onBackupKey(): Promise<void> {
		this.dlg.setData({ password: "", secret: "" });
		(await this.dialog("BackupKeyDialog")).open();
	}

	public async onBackupKeyConfirm(): Promise<void> {
		const result = await this.run(() => post<{ password: string }>(`/api/tenants/${this.id}/backup-password`, { password: this.dlg.getProperty("/password") }),
			this.dialogs.get("BackupKeyDialog"));
		this.dlg.setProperty("/password", "");
		if (result) {
			this.dlg.setProperty("/secret", result.password);
		}
	}

	public onCopyBackupKey(): void {
		void navigator.clipboard?.writeText(this.dlg.getProperty("/secret") as string).then(() => this.toast("Schlüssel kopiert"));
	}

	public onCopySetupCode(): void {
		void navigator.clipboard?.writeText(this.detail.getProperty("/status/setupCode") as string).then(() => this.toast("Einrichtungscode kopiert"));
	}

	public async onEdit(): Promise<void> {
		const data = this.detail.getData() as Detail;
		this.form ??= new TenantFormDialog(this.getView()!);
		const saved = await this.form.open({ ...data.summary, contactPhone: data.contactPhone, notes: data.notes, id: data.summary.id });
		if (saved) {
			this.toast("Gespeichert");
			await this.load();
		}
	}

	public async onLoadLogs(): Promise<void> {
		await this.loadLogs();
	}

	private async loadLogs(silent = false): Promise<void> {
		const { service, lines, level, filter } = this.logs.getData();
		const params = new URLSearchParams({ service, lines, level, filter: filter ?? "" });
		const action = async () => {
			const result = await get<{ lines: string[]; count: number }>(`/api/tenants/${this.id}/logs?${params}`);
			this.logs.setProperty("/text", result.lines.join("\n"));
			this.logs.setProperty("/count", result.count);
			const dom = (this.byId("logOutput") as Control).getDomRef()?.querySelector("textarea");
			if (dom) {
				setTimeout(() => (dom.scrollTop = dom.scrollHeight), 0);
			}
		};
		if (silent) {
			await action().catch(() => undefined);
		} else {
			await this.run(action, this.byId("logOutput") as Control);
		}
	}

	public onFollow(): void {
		clearInterval(this.logTimer);
		if (this.logs.getProperty("/follow")) {
			this.logTimer = setInterval(() => void this.loadLogs(true), 5000);
		}
	}

	public onDownloadLogs(): void {
		const { service, text } = this.logs.getData();
		const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
		const link = document.createElement("a");
		link.href = URL.createObjectURL(blob);
		link.download = `${this.detail.getProperty("/summary/slug")}-${service}-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-")}.log`;
		link.click();
		setTimeout(() => URL.revokeObjectURL(link.href), 1000);
	}
}
