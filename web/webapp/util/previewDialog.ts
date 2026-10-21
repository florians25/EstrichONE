/**
 * Große Vorschau (fast bildschirmfüllend) für Dokumente, Fotos und Videos: PDF im Rahmen derselben Herkunft,
 * Blättern durch die aktuelle Liste (auch mit Pfeiltasten), Herunterladen, neuer Tab, Details. Kopf mit den
 * Kerndaten der Bauakte.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Toolbar from "sap/m/Toolbar";
import ToolbarSpacer from "sap/m/ToolbarSpacer";
import Title from "sap/m/Title";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import HTML from "sap/ui/core/HTML";
import IllustratedMessage from "sap/m/IllustratedMessage";
import { akteHeader, type AkteContext } from "./akteHeader";
import { dateTime, fileSize } from "./format";
import { documentCategories } from "./labels";
import { session } from "./session";
import type { FileResponse } from "./models";

export type PreviewKind = "pdf" | "image" | "video" | "none";

export function previewKind(file: Pick<FileResponse, "contentType" | "originalName" | "state">): PreviewKind {
	if (file.state !== "stored") return "none";
	const type = file.contentType;
	return type === "application/pdf" || file.originalName.toLowerCase().endsWith(".pdf") ? "pdf"
		: type.startsWith("image/") ? "image" : type.startsWith("video/") ? "video" : "none";
}

const escape = (value: string) => value.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);

export interface PreviewOptions {
	files: FileResponse[];
	index: number;
	context: AkteContext | null;
	/** Detailseite (Fassungen, Freigaben) öffnen. */
	onDetails?: (file: FileResponse) => void;
}

export function openPreview(options: PreviewOptions): Dialog {
	let index = Math.max(0, Math.min(options.index, options.files.length - 1));
	const title = new Title({ level: "H4", wrapping: true });
	const meta = new Text().addStyleClass("eoMuted");
	const body = new HTML({ sanitizeContent: false, preferDOM: false });
	const none = new IllustratedMessage({
		illustrationType: "sapIllus-NoData", illustrationSize: "Spot", visible: false,
		title: "Keine Vorschau für diesen Dateityp", description: "Die Datei lässt sich herunterladen und mit dem passenden Programm öffnen.",
	});
	const prev = new Button({ icon: "sap-icon://navigation-left-arrow", tooltip: "Vorherige (Pfeil links)", press: () => show(index - 1) });
	const next = new Button({ icon: "sap-icon://navigation-right-arrow", tooltip: "Nächste (Pfeil rechts)", press: () => show(index + 1) });
	const position = new Text();
	const url = (f: FileResponse) => `/api/v1/files/${f.id}/content`;

	const show = (to: number) => {
		if (to < 0 || to >= options.files.length) return;
		index = to;
		const file = options.files[index];
		const kind = previewKind(file);
		title.setText(file.originalName);
		const place = file.kind === "document"
			? [documentCategories[(file.category ?? "other") as keyof typeof documentCategories]?.text, file.folder].filter(Boolean).join(" / ")
			: file.kind === "photo" ? "Foto" : "Video";
		meta.setText([place, fileSize(file.sizeBytes), `${dateTime(file.capturedAt ?? file.createdAt)} · ${session.name(file.uploadedByUserId)}`, file.caption].filter(Boolean).join(" · "));
		const label = escape(file.originalName);
		body.setContent(kind === "pdf" ? `<iframe class="eoPreviewLarge" title="Vorschau ${label}" src="${url(file)}"></iframe>`
			: kind === "image" ? `<div class="eoPreviewLarge eoPreviewCenter"><img class="eoPreviewMedia" alt="${label}" src="${url(file)}?variant=${file.variants === "ready" ? "display" : "original"}"></div>`
				: kind === "video" ? `<div class="eoPreviewLarge eoPreviewCenter"><video class="eoPreviewMedia" controls preload="metadata" src="${url(file)}"></video></div>`
					: "<div></div>");
		none.setVisible(kind === "none");
		prev.setEnabled(index > 0);
		next.setEnabled(index < options.files.length - 1);
		position.setText(options.files.length > 1 ? `${index + 1} von ${options.files.length}` : "");
	};

	const download = () => {
		const file = options.files[index];
		const a = document.createElement("a");
		a.href = url(file);
		a.download = file.originalName;
		a.click();
	};
	const keys = (event: KeyboardEvent) => {
		const tag = (event.target as HTMLElement | null)?.tagName;
		if (tag === "INPUT" || tag === "TEXTAREA") return;
		if (event.key === "ArrowLeft") show(index - 1);
		if (event.key === "ArrowRight") show(index + 1);
	};

	const dialog = new Dialog({
		title: "Vorschau", contentWidth: "96vw", contentHeight: "90vh", stretch: false, resizable: true, draggable: true,
		horizontalScrolling: false, verticalScrolling: false,
		customHeader: new Toolbar({ content: [
			new Title({ text: "Vorschau", level: "H2" }), new ToolbarSpacer(), position, prev, next,
			new Button({ text: "Herunterladen", icon: "sap-icon://download", press: download }),
			new Button({ text: "Neuer Tab", icon: "sap-icon://action", tooltip: "In neuem Tab öffnen", press: () => window.open(url(options.files[index]), "_blank", "noopener") }),
			...(options.onDetails ? [new Button({ text: "Details", icon: "sap-icon://detail-view", press: () => {
				const file = options.files[index];
				dialog.close();
				options.onDetails?.(file);
			} })] : []),
			new Button({ icon: "sap-icon://decline", tooltip: "Schließen", type: "Transparent", press: () => dialog.close() }),
		] }),
		content: [new VBox({ height: "100%", renderType: "Bare", items: [
			...akteHeader(options.context),
			new VBox({ items: [title, meta] }).addStyleClass("sapUiTinyMarginBottom"),
			body, none,
		] }).addStyleClass("eoPreviewBody")],
		afterOpen: () => document.addEventListener("keydown", keys),
		afterClose: () => {
			document.removeEventListener("keydown", keys);
			dialog.destroy();
		},
	});
	dialog.addStyleClass("eoPreviewDialog");
	show(index);
	dialog.open();
	return dialog;
}
