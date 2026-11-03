import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import BaseController from "./BaseController";
import { del, get, post } from "../model/Api";
import { askPassword, download } from "../util/PasswordPrompt";

interface Archive { name: string; slug: string; archivedAt?: string; sizeBytes: number; hasKey: boolean }

/**
 * Archiv der Endsicherungen gelöschter Mandanten: herunterladen (nach Passwortabfrage) und endgültig löschen.
 * @namespace estrichone.hosting.controller
 */
export default class ArchivesController extends BaseController {
	private readonly archives = new JSONModel({ items: [] });

	public onInit(): void {
		this.setModel(this.archives, "archives");
		this.getRouter().getRoute("archives")!.attachPatternMatched(() => void this.load());
	}

	private async load(): Promise<void> {
		await this.run(async () => this.archives.setProperty("/items", await get<Archive[]>("/api/archives")), this.byId("archiveTable") as Control);
	}

	public onRefresh(): void {
		void this.load();
	}

	private archiveOf(event: Event): Archive {
		return (event.getSource() as Control).getBindingContext("archives")!.getObject() as Archive;
	}

	public async onDownload(event: Event): Promise<void> {
		const archive = this.archiveOf(event);
		const password = await askPassword(this.getView()!, "Endsicherung herunterladen",
			`„${archive.name}“ enthält alle Daten und den Schlüssel der Instanz. Bitte nur verschlüsselt weitergeben.`, "Herunterladen");
		if (!password) return;
		const result = await this.run(() => post<{ url: string; fileName: string }>(`/api/archives/${encodeURIComponent(archive.name)}/export`, { password }));
		if (result) {
			download(result.url, result.fileName);
			this.toast("Download gestartet");
		}
	}

	/** Endgültig löschen: Name zur Bestätigung eintippen. */
	public onDelete(event: Event): void {
		const archive = this.archiveOf(event);
		const input = new Input({ placeholder: archive.name, width: "100%" });
		const dialog: Dialog = new Dialog({
			title: "Endsicherung endgültig löschen",
			type: "Message",
			state: "Error",
			content: new VBox({ items: [new Text({ text: `Die Endsicherung „${archive.name}“ wird unwiderruflich gelöscht. Zur Bestätigung den Namen eingeben:` }), input] }),
			beginButton: new Button({
				text: "Endgültig löschen",
				type: "Reject",
				press: async () => {
					if (input.getValue() !== archive.name) {
						input.setValueState("Error");
						input.setValueStateText("Name stimmt nicht überein.");
						return;
					}
					dialog.close();
					const done = await this.run(async () => {
						await del(`/api/archives/${encodeURIComponent(archive.name)}?confirm=${encodeURIComponent(archive.name)}`);
						return true;
					});
					if (done) {
						this.toast("Endsicherung gelöscht");
						await this.load();
					}
				}
			}),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy()
		});
		this.getView()!.addDependent(dialog);
		dialog.open();
	}
}
