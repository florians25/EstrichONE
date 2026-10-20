import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Table from "sap/m/Table";
import type Tree from "sap/m/Tree";
import type UploadSet from "sap/m/upload/UploadSet";
import type UploadSetItem from "sap/m/upload/UploadSetItem";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import MessageStrip from "sap/m/MessageStrip";
import FileUploader from "sap/ui/unified/FileUploader";
import MessageBox from "sap/m/MessageBox";
import DocumentUploader from "../../../control/DocumentUploader";
import { del, get, problemMessage, put } from "../../../util/api";
import { approvalStatus, requestApproval, type ApprovalStatus } from "../../../util/approvals";
import { catalog, type FolderTemplate } from "../../../util/catalog";
import { akte } from "../../../util/akteStore";
import { documentRows, fileIcon, folderTree, inFolder, sortRows, type DocumentRow, type DocumentSort } from "../../../util/documents";
import { openForm, prompt } from "../../../util/forms";
import { documentCategories, options } from "../../../util/labels";
import type { DocumentCategory, FileResponse } from "../../../util/models";
import { navTo } from "../../../util/navigation";
import { uploadFile } from "../../../util/upload";

/**
 * Dokumente der Bauakte: Ablage nach Dokumentart und Unterordnern, Mehrfach-Upload per Ziehen (UploadSet),
 * Fassungen, Vorschau, Umbenennen, Verschieben, Löschen, ZIP-Download, Suche und Sortierung.
 * @namespace estrichone.controller.bauakte.sections
 */
export default class Documents extends SectionBase {
	protected watches = ["files"];
	private rows: DocumentRow[] = [];
	private extraFolders: string[] = [];
	/** Unterordner aus den Ordnervorlagen (Stammdaten), in jeder Bauakte angeboten. */
	private templateFolders: string[] = [];
	private uploader!: DocumentUploader;
	private batchTimer: ReturnType<typeof setTimeout> | undefined;

	protected init(): void {
		const model = new JSONModel({ tree: [], rows: [], selected: "", category: null, folder: null, query: "", sort: "newest", allVersions: false,
			selectedCount: 0, title: "", noData: "", targetText: "" });
		model.setSizeLimit(5000);
		this.v().setModel(model, "doc");
		this.uploader = new DocumentUploader();
		this.uploader.target = () => ({ bauakteId: this.bauakteId, category: (this.doc().getProperty("/category") as DocumentCategory | null) ?? "other",
			folder: this.doc().getProperty("/folder") as string | null });
		const set = this.v().byId("uploadSet") as UploadSet;
		set.setUploader(this.uploader);
		set.registerUploaderEvents(this.uploader);
		this.updateTarget();
	}

	private doc(): JSONModel {
		return this.v().getModel("doc") as JSONModel;
	}

	protected async load(): Promise<void> {
		const [files, approvals, templates] = await Promise.all([
			akte.files(this.bauakteId),
			get<{ items: { file: { id: string }; approvalStatus: ApprovalStatus | null; approvalAssignee: string | null }[] }>("documents", { bauakteId: this.bauakteId, limit: 1000 })
				.catch(() => ({ items: [] })),
			catalog<FolderTemplate>("folderTemplate"),
		]);
		this.templateFolders = templates.flatMap((t) => (t.content.folders ?? []).map((f) => `${t.content.category}/${f}`));
		const byId = new Map(approvals.items.map((i) => [i.file.id, i]));
		this.rows = documentRows(files).map((r) => {
			const a = byId.get(r.id);
			return {
				...r, icon: fileIcon(r.contentType, r.originalName), approvalStatus: a?.approvalStatus ?? null,
				approvalText: a?.approvalStatus ? `${approvalStatus[a.approvalStatus].text}${a.approvalStatus === "pending" && a.approvalAssignee ? ` (${a.approvalAssignee})` : ""}` : "",
				approvalState: a?.approvalStatus ? approvalStatus[a.approvalStatus].state : "None",
			};
		});
		this.refreshTree();
		this.refreshRows();
	}

	private refreshTree(): void {
		const tree = folderTree(this.rows, [...this.extraFolders, ...this.templateFolders]);
		this.doc().setProperty("/tree", [tree]);
		setTimeout(() => (this.v().byId("folderTree") as Tree).expandToLevel(2), 0);
	}

	refreshRows(): void {
		const m = this.doc();
		const category = m.getProperty("/category") as DocumentCategory | null;
		const folder = m.getProperty("/folder") as string | null;
		const query = String(m.getProperty("/query") ?? "").trim().toLowerCase();
		const all = m.getProperty("/allVersions") as boolean;
		const shown = sortRows(this.rows.filter((r) => (all || !r.superseded) && inFolder(r, category, folder)
			&& (!query || [r.originalName, r.caption, r.folder, documentCategories[r.categoryKey].text].some((v) => v?.toLowerCase().includes(query)))),
		m.getProperty("/sort") as DocumentSort);
		m.setProperty("/rows", shown);
		const place = category ? [documentCategories[category].text, folder].filter(Boolean).join(" / ") : "Alle Dokumente";
		m.setProperty("/title", `${place} (${shown.length})`);
		m.setProperty("/noData", query ? "Keine Treffer." : "Noch keine Dokumente in diesem Ordner. Dateien hierher ziehen oder „Hochladen“ wählen.");
		(this.v().byId("docTable") as Table).removeSelections(true);
		m.setProperty("/selectedCount", 0);
		this.updateTarget();
	}

	private updateTarget(): void {
		const m = this.doc();
		const category = (m.getProperty("/category") as DocumentCategory | null) ?? "other";
		const folder = m.getProperty("/folder") as string | null;
		m.setProperty("/targetText", `Neue Dateien werden abgelegt unter: ${[documentCategories[category].text, folder].filter(Boolean).join(" / ")}. `
			+ "Ordner links wählen, um woanders abzulegen. Mehrere Dateien gleichzeitig hierher ziehen.");
	}

	onFolder(event: Event): void {
		const item = (event as unknown as { getParameter(n: string): Control }).getParameter("listItem");
		const node = item.getBindingContext("doc")?.getObject() as { key: string; category: DocumentCategory | null; folder: string | null };
		const m = this.doc();
		m.setProperty("/selected", node.key);
		m.setProperty("/category", node.category);
		m.setProperty("/folder", node.folder);
		this.refreshRows();
	}

	onSearch(): void {
		this.refreshRows();
	}

	onSelection(): void {
		this.doc().setProperty("/selectedCount", (this.v().byId("docTable") as Table).getSelectedItems().length);
	}

	private selected(): DocumentRow[] {
		return (this.v().byId("docTable") as Table).getSelectedItems().map((i) => i.getBindingContext("doc")?.getObject() as DocumentRow);
	}

	private rowOf(event: Event): DocumentRow {
		let control = event.getSource() as Control | null;
		// Menüeinträge hängen nicht im Zeilenkontext: über den MenuButton zur Zeile.
		while (control && !control.getBindingContext("doc")) {
			control = (control.getParent() as Control | null) ?? null;
		}
		return control?.getBindingContext("doc")?.getObject() as DocumentRow;
	}

	async onNewFolder(): Promise<void> {
		const m = this.doc();
		const category = (m.getProperty("/category") as DocumentCategory | null) ?? "other";
		const parent = m.getProperty("/folder") as string | null;
		const name = await prompt("Neuer Unterordner", `Name des Ordners in „${[documentCategories[category].text, parent].filter(Boolean).join(" / ")}“`, {
			intro: "Ordner entstehen mit der ersten Datei darin. Neue Dateien landen nach dem Anlegen direkt hier.",
		});
		if (!name) return;
		const folder = [parent, name.replace(/\//g, "-").trim()].filter(Boolean).join("/");
		this.extraFolders.push(`${category}/${folder}`);
		m.setProperty("/category", category);
		m.setProperty("/folder", folder);
		m.setProperty("/selected", `${category}/${folder}`);
		this.refreshTree();
		this.refreshRows();
	}

	// Hochladen (UploadSet mit eigenem Uploader)

	onItemAdded(): void {
		this.uploader.errors = [];
	}

	onUploadCompleted(): void {
		this.scheduleRefresh();
	}

	onUploadTerminated(): void {
		this.scheduleRefresh();
	}

	/** Nach einem Stapel: Liste neu laden, fertige Einträge aus dem Upload-Bereich entfernen, Fehler zeigen. */
	private scheduleRefresh(): void {
		clearTimeout(this.batchTimer);
		this.batchTimer = setTimeout(() => {
			const set = this.v().byId("uploadSet") as UploadSet;
			const busy = set.getIncompleteItems().some((i: UploadSetItem) => i.getUploadState() === "Uploading" || i.getUploadState() === "Ready");
			if (busy) {
				this.scheduleRefresh();
				return;
			}
			const done = set.getItems().filter((i) => i.getUploadState() === "Complete");
			for (const item of done) set.removeItem(item);
			for (const item of set.getIncompleteItems().filter((i) => i.getUploadState() === "Error")) set.removeIncompleteItem(item);
			const errors = this.uploader.errors;
			this.uploader.errors = [];
			if (done.length) this.toast(`${done.length} ${done.length === 1 ? "Datei" : "Dateien"} hochgeladen.`);
			if (errors.length) MessageBox.error(errors.join("\n"), { title: "Nicht hochgeladen" });
			akte.invalidate(this.bauakteId, "files");
		}, 400);
	}

	// Aktionen je Dokument

	onPreview(event: Event): void {
		const row = this.rowOf(event);
		navTo(`/bauakten/${this.bauakteId}/datei/${row.id}?abschnitt=dokumente`);
	}

	async onRequestApproval(event: Event): Promise<void> {
		const row = this.rowOf(event);
		if (await requestApproval(row)) {
			this.toast("Freigabe angefordert");
			void this.load();
		}
	}

	onDownload(event: Event): void {
		const row = this.rowOf(event);
		const a = document.createElement("a");
		a.href = `/api/v1/files/${row.id}/content`;
		a.download = row.originalName;
		a.click();
	}

	private async details(row: FileResponse, changes: Partial<{ originalName: string; caption: string | null; category: string | null; folder: string | null }>): Promise<void> {
		await put(`files/${row.id}/details`, {
			originalName: row.originalName, caption: row.caption, category: row.category, folder: row.folder, roomId: row.roomId,
			workItemId: row.workItemId, ...changes,
		});
	}

	async onRename(event: Event): Promise<void> {
		const row = this.rowOf(event);
		const name = await openForm({
			title: "Umbenennen", submitText: "Umbenennen",
			initial: { name: row.originalName, caption: row.caption ?? "" },
			fields: [
				{ key: "name", label: "Dateiname", required: true, maxLength: 255 },
				{ key: "caption", label: "Beschreibung", maxLength: 500, placeholder: "optional, z. B. Stand Bauantrag" },
			],
			submit: async (v) => {
				const ext = row.originalName.includes(".") ? row.originalName.slice(row.originalName.lastIndexOf(".")) : "";
				let next = String(v.name);
				if (ext && !next.toLowerCase().endsWith(ext.toLowerCase())) next += ext;
				await this.details(row, { originalName: next, caption: (v.caption as string) || null });
				return next;
			},
		});
		if (name) {
			this.toast("Umbenannt");
			akte.invalidate(this.bauakteId, "files");
		}
	}

	private folderSuggestions(): string[] {
		return [...new Set([...this.rows.map((r) => r.folder).filter((f): f is string => !!f), ...[...this.extraFolders, ...this.templateFolders].map((e) => e.split("/").slice(1).join("/"))])].sort();
	}

	private async move(rows: DocumentRow[]): Promise<void> {
		const first = rows[0];
		const done = await openForm({
			title: rows.length === 1 ? `„${first.originalName}“ verschieben` : `${rows.length} Dokumente verschieben`, submitText: "Verschieben",
			initial: { category: first.categoryKey, folder: rows.length === 1 ? first.folder ?? "" : "" },
			fields: [
				{ key: "category", label: "Dokumentart", type: "select", required: true, options: options(documentCategories) },
				{ key: "folder", label: "Unterordner", placeholder: "leer = direkt in der Dokumentart, z. B. Pläne/EG", suggestions: this.folderSuggestions(), maxLength: 200 },
			],
			submit: async (v) => {
				for (const row of rows) {
					await this.details(row, { category: String(v.category), folder: (v.folder as string) || null });
				}
				return true;
			},
		});
		if (done) {
			this.toast(rows.length === 1 ? "Verschoben" : `${rows.length} Dokumente verschoben`);
			akte.invalidate(this.bauakteId, "files");
		}
	}

	onMove(event: Event): void {
		void this.move([this.rowOf(event)]);
	}

	onMoveSelected(): void {
		const rows = this.selected();
		if (rows.length) void this.move(rows);
	}

	private async remove(rows: DocumentRow[]): Promise<void> {
		const text = rows.length === 1 ? `„${rows[0].originalName}“ löschen?` : `${rows.length} Dokumente löschen?`;
		if (!(await this.confirm(`${text} Die Datei ist danach für alle nicht mehr sichtbar.`, { title: "Löschen", action: "Löschen", danger: true }))) return;
		const errors: string[] = [];
		for (const row of rows) {
			try {
				await del(`files/${row.id}`);
			} catch (error) {
				errors.push(`${row.originalName}: ${problemMessage(error)}`);
			}
		}
		if (errors.length) MessageBox.error(errors.join("\n"));
		else this.toast(rows.length === 1 ? "Gelöscht" : `${rows.length} Dokumente gelöscht`);
		akte.invalidate(this.bauakteId, "files");
	}

	onDelete(event: Event): void {
		void this.remove([this.rowOf(event)]);
	}

	onDeleteSelected(): void {
		const rows = this.selected();
		if (rows.length) void this.remove(rows);
	}

	onDownloadSelected(): void {
		const rows = this.selected().filter((r) => r.state === "stored");
		if (!rows.length) return;
		if (rows.length === 1) {
			const a = document.createElement("a");
			a.href = `/api/v1/files/${rows[0].id}/content`;
			a.download = rows[0].originalName;
			a.click();
			return;
		}
		window.location.href = `/api/v1/bauakten/${this.bauakteId}/files/archive?ids=${rows.map((r) => r.id).join(",")}`;
	}

	/** Neue Fassung: Datei wählen, als Nachfolger hochladen (gleiche Ablage). */
	onNewVersion(event: Event): void {
		const row = this.rowOf(event);
		const error = new MessageStrip({ type: "Error", showIcon: true, visible: false });
		const picker = new FileUploader({ buttonText: "Datei wählen …", width: "100%", sameFilenameAllowed: true, placeholder: "Neue Fassung auswählen" });
		const upload = new Button({
			text: "Als neue Fassung hochladen", type: "Emphasized",
			press: async () => {
				const input = picker.getDomRef()?.querySelector("input[type=file]") as HTMLInputElement | null;
				const file = input?.files?.[0];
				if (!file) {
					error.setText("Bitte eine Datei wählen.");
					error.setVisible(true);
					return;
				}
				dialog.setBusy(true);
				try {
					await uploadFile(file, { bauakteId: this.bauakteId, previousVersionId: row.id });
					dialog.close();
					this.toast("Neue Fassung hochgeladen");
					akte.invalidate(this.bauakteId, "files");
				} catch (e) {
					error.setText(problemMessage(e));
					error.setVisible(true);
				} finally {
					dialog.setBusy(false);
				}
			},
		});
		const dialog = new Dialog({
			title: "Neue Fassung hochladen", contentWidth: "30rem",
			content: [new VBox({ items: [
				new Text({ text: `Ersetzt „${row.originalName}“ (Fassung ${row.versionNumber}). Ältere Fassungen bleiben erhalten und sind über „Alle Fassungen“ sichtbar.` }),
				new Label({ text: "Datei", labelFor: picker }).addStyleClass("sapUiSmallMarginTop"), picker, error,
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: upload,
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		dialog.open();
	}
}
