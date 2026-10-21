/**
 * Hochladen im Modaldialog (Dokumente, Fotos/Videos): Ziel wählen (Dokumentart, Unterordner), Dateien ziehen oder
 * auswählen, Fortschritt je Datei, Fehler mit Ursache und „Erneut versuchen“ (setzt fort, wo die Übertragung abbrach).
 * Kopf mit Kerndaten der Bauakte, damit klar ist, wohin hochgeladen wird.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Label from "sap/m/Label";
import Input from "sap/m/Input";
import Select from "sap/m/Select";
import List from "sap/m/List";
import CustomListItem from "sap/m/CustomListItem";
import HBox from "sap/m/HBox";
import VBox from "sap/m/VBox";
import Text from "sap/m/Text";
import ProgressIndicator from "sap/m/ProgressIndicator";
import MessageStrip from "sap/m/MessageStrip";
import Item from "sap/ui/core/Item";
import Icon from "sap/ui/core/Icon";
import HTML from "sap/ui/core/HTML";
import SuggestionItem from "sap/m/SuggestionItem";
import JSONModel from "sap/ui/model/json/JSONModel";
import { problemMessage } from "./api";
import { akteHeader, type AkteContext } from "./akteHeader";
import { fileSize } from "./format";
import { fileIcon } from "./documents";
import { documentCategories, options } from "./labels";
import type { DocumentCategory } from "./models";
import { kindOf, guessType, uploadFile } from "./upload";

export interface UploadDialogOptions {
	title: string;
	bauakteId: string;
	context: AkteContext | null;
	/** Dokumente (mit Ablage) oder Fotos/Videos. */
	kind: "documents" | "media";
	category?: DocumentCategory | null;
	folder?: string | null;
	folderSuggestions?: string[];
	/** Bereits gezogene Dateien. */
	files?: File[];
	/** Nach jedem abgeschlossenen Stapel (Anzahl erfolgreich). */
	onUploaded: (count: number) => void;
}

type RowState = "waiting" | "uploading" | "done" | "error";

interface Row {
	key: number;
	name: string;
	sizeText: string;
	icon: string;
	state: RowState;
	percent: number;
	statusText: string;
	error: string;
}

let counter = 0;

const stateText: Record<RowState, string> = { waiting: "Bereit", uploading: "Wird hochgeladen …", done: "Hochgeladen", error: "Fehler" };

export function openUploadDialog(options_: UploadDialogOptions): Dialog {
	const id = `upl${++counter}`;
	const files = new Map<number, File>();
	let next = 0;
	let running = false;
	const model = new JSONModel({ rows: [] as Row[], waiting: 0, failed: 0, running: false });

	const rows = () => model.getProperty("/rows") as Row[];
	const recount = () => {
		model.setProperty("/waiting", rows().filter((r) => r.state === "waiting").length);
		model.setProperty("/failed", rows().filter((r) => r.state === "error").length);
		model.setProperty("/running", running);
	};
	const accept = options_.kind === "media" ? "image/*,video/*,.heic,.heif" : "";
	const rejected = new MessageStrip({ type: "Warning", showIcon: true, visible: false });
	const add = (list: File[]) => {
		const skipped: string[] = [];
		for (const file of list) {
			const type = file.type || guessType(file.name);
			if (options_.kind === "media" && kindOf(type) === "document") {
				skipped.push(file.name);
				continue;
			}
			const key = next++;
			files.set(key, file);
			rows().push({ key, name: file.name, sizeText: fileSize(file.size), icon: fileIcon(type, file.name), state: "waiting", percent: 0, statusText: stateText.waiting, error: "" });
		}
		rejected.setText(skipped.length ? `Keine Fotos oder Videos, nicht übernommen: ${skipped.join(", ")}` : "");
		rejected.setVisible(skipped.length > 0);
		model.refresh(true);
		recount();
	};

	// Ziel
	const category = new Select(`${id}-category`, {
		width: "100%", selectedKey: options_.category ?? "other",
		items: options(documentCategories).map((o) => new Item({ key: o.key, text: o.text })),
	});
	const folder = new Input(`${id}-folder`, {
		width: "100%", value: options_.folder ?? "", placeholder: "leer = direkt in der Dokumentart, z. B. Pläne/EG", maxLength: 200,
		showSuggestion: true, suggestionItems: (options_.folderSuggestions ?? []).map((f) => new SuggestionItem({ text: f })),
	});
	const caption = new Input(`${id}-caption`, {
		width: "100%", maxLength: 500,
		placeholder: options_.kind === "media" ? "optional, für alle gewählten Fotos – z. B. Fußbodenheizung vor dem Einbringen" : "optional, z. B. Stand Bauantrag",
	});
	const target = options_.kind === "documents"
		? [
			new HBox({ wrap: "Wrap", width: "100%", items: [
				new VBox({ width: "14rem", items: [new Label({ text: "Dokumentart", labelFor: category, required: true }), category] }).addStyleClass("sapUiSmallMarginEnd"),
				new VBox({ width: "18rem", items: [new Label({ text: "Unterordner", labelFor: folder }), folder] }),
			] }),
			new Label({ text: "Beschreibung", labelFor: caption }).addStyleClass("sapUiTinyMarginTop"), caption,
		]
		: [new Label({ text: "Beschriftung", labelFor: caption }), caption];

	// Ablagefläche mit Dateiauswahl (natives Eingabefeld, CSP-tauglich ohne Inline-Skript)
	const input = document.createElement("input");
	input.type = "file";
	input.multiple = true;
	if (accept) input.accept = accept;
	input.className = "eoFileInput";
	input.setAttribute("aria-label", "Dateien auswählen");
	input.addEventListener("change", () => {
		add([...(input.files ?? [])]);
		input.value = "";
	});
	const zone = new HTML(`${id}-zone`, {
		content: `<div class="eoDropZone" role="group" aria-label="Dateien hierher ziehen"><span class="eoDropText">${options_.kind === "media" ? "Fotos und Videos" : "Dateien"} hierher ziehen</span></div>`,
		afterRendering: () => {
			const dom = zone.getDomRef() as HTMLElement | null;
			if (!dom || dom.dataset.ready) return;
			dom.dataset.ready = "1";
			dom.appendChild(input);
			dom.addEventListener("dragover", (event) => {
				event.preventDefault();
				dom.classList.add("eoDropActive");
			});
			dom.addEventListener("dragleave", () => dom.classList.remove("eoDropActive"));
			dom.addEventListener("drop", (event) => {
				event.preventDefault();
				dom.classList.remove("eoDropActive");
				add([...(event.dataTransfer?.files ?? [])]);
			});
		},
	});
	const choose = new Button(`${id}-choose`, { text: "Dateien auswählen …", icon: "sap-icon://browse-folder", press: () => input.click() });

	const list = new List(`${id}-list`, {
		noDataText: "Noch keine Dateien gewählt.", showSeparators: "Inner",
		items: {
			path: "/rows",
			template: new CustomListItem({
				content: [new HBox({
					alignItems: "Center", justifyContent: "SpaceBetween", width: "100%",
					items: [
						new HBox({ alignItems: "Center", items: [
							new Icon({ src: "{icon}", size: "1.25rem", decorative: true }).addStyleClass("sapUiSmallMarginEnd"),
							new VBox({ items: [
								new Text({ text: "{name}", wrapping: true }),
								new Text({ text: "{sizeText} · {statusText}" }).addStyleClass("eoMuted"),
								new Text({ text: "{error}", visible: "{= !!${error} }", wrapping: true }).addStyleClass("eoErrorText"),
							] }),
						] }),
						new HBox({ alignItems: "Center", items: [
							new ProgressIndicator({ percentValue: "{percent}", displayValue: "{percent} %", width: "8rem", visible: "{= ${state} === 'uploading' || ${state} === 'done' }",
								state: "{= ${state} === 'done' ? 'Success' : 'Information' }" }),
							new Icon({ src: "sap-icon://message-error", color: "Negative", visible: "{= ${state} === 'error' }", tooltip: "Fehler" }),
							new Button({ icon: "sap-icon://decline", type: "Transparent", tooltip: "Entfernen", visible: "{= ${state} === 'waiting' || ${state} === 'error' }",
								press: (event) => {
									const row = (event.getSource() as unknown as Button).getBindingContext()?.getObject() as Row;
									files.delete(row.key);
									model.setProperty("/rows", rows().filter((r) => r.key !== row.key));
									recount();
								} }),
						] }),
					],
				}).addStyleClass("sapUiTinyMarginTopBottom")],
			}),
		},
	});

	const summary = new MessageStrip({ type: "Error", showIcon: true, visible: false });
	const set = (row: Row, patch: Partial<Row>) => {
		Object.assign(row, patch);
		model.refresh(true);
	};

	const start = async () => {
		if (running) return;
		running = true;
		recount();
		summary.setVisible(false);
		const target_ = {
			bauakteId: options_.bauakteId,
			category: options_.kind === "documents" ? (category.getSelectedKey() as DocumentCategory) : null,
			folder: options_.kind === "documents" ? folder.getValue().trim().replace(/^\/+|\/+$/g, "") || null : null,
			caption: caption.getValue().trim() || null,
		};
		let ok = 0;
		for (const row of rows().filter((r) => r.state === "waiting" || r.state === "error")) {
			const file = files.get(row.key);
			if (!file) continue;
			set(row, { state: "uploading", percent: 0, statusText: stateText.uploading, error: "" });
			try {
				await uploadFile(file, { ...target_, onProgress: (loaded, total) => set(row, { percent: total ? Math.min(99, Math.round((loaded / total) * 100)) : 0 }) });
				set(row, { state: "done", percent: 100, statusText: stateText.done });
				files.delete(row.key);
				ok++;
			} catch (error) {
				set(row, { state: "error", statusText: stateText.error, error: problemMessage(error) });
			}
		}
		running = false;
		recount();
		if (ok) options_.onUploaded(ok);
		if (!dialog.isOpen()) {
			dialog.destroy();
			return;
		}
		const failed = rows().filter((r) => r.state === "error").length;
		if (failed) {
			summary.setText(`${failed} ${failed === 1 ? "Datei wurde" : "Dateien wurden"} nicht hochgeladen. „Erneut versuchen“ setzt fort, wo die Übertragung abbrach.`);
			summary.setVisible(true);
		} else if (ok) {
			dialog.close();
		}
	};

	const uploadButton = new Button(`${id}-start`, {
		text: "{= ${/failed} > 0 && ${/waiting} === 0 ? 'Erneut versuchen' : 'Hochladen' }", type: "Emphasized", icon: "sap-icon://upload",
		enabled: "{= !${/running} && (${/waiting} > 0 || ${/failed} > 0) }",
		press: () => void start(),
	});
	const dialog = new Dialog(id, {
		title: options_.title, contentWidth: "44rem", resizable: true, draggable: true, verticalScrolling: true,
		content: [new VBox({ items: [
			...akteHeader(options_.context),
			...target,
			new VBox({ items: [zone, choose] }).addStyleClass("sapUiSmallMarginTop eoDropBox"),
			rejected, list, summary,
		] }).addStyleClass("sapUiSmallMargin")],
		beginButton: uploadButton,
		endButton: new Button({ text: "Schließen", press: () => dialog.close() }),
		afterClose: () => {
			if (!running) dialog.destroy();
		},
	});
	dialog.setModel(model);
	if (options_.files?.length) add(options_.files);
	dialog.open();
	return dialog;
}
