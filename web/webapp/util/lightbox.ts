/**
 * Großansicht für Fotos: Vor/Zurück (Pfeiltasten), Metadaten (Aufnahme, Ersteller, Raum, Mangel), Beschriftung
 * bearbeiten und Original herunterladen.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import FlexBox from "sap/m/FlexBox";
import Image from "sap/m/Image";
import Text from "sap/m/Text";
import Label from "sap/m/Label";
import Input from "sap/m/Input";
import Link from "sap/m/Link";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import Title from "sap/m/Title";
import FlexItemData from "sap/m/FlexItemData";
import MessageToast from "sap/m/MessageToast";
import { problemMessage, put } from "./api";
import { dateTime, fileSize } from "./format";
import type { FileResponse } from "./models";
import { session } from "./session";

export interface LightboxOptions {
	roomName?: (id: string | null) => string | null;
	workName?: (id: string | null) => string | null;
	/** Nach dem Speichern der Beschriftung (Liste neu laden). */
	onChanged?: (file: FileResponse) => void;
}

const url = (f: FileResponse, variant: "display" | "thumb" | "original") =>
	`/api/v1/files/${f.id}/content${variant === "original" ? "" : `?variant=${f.variants === "ready" ? variant : "original"}`}`;

export function openLightbox(photos: FileResponse[], start: number, options: LightboxOptions = {}): void {
	const list = photos.filter((p) => p.state === "stored");
	if (list.length === 0) return;
	let index = Math.max(0, Math.min(start, list.length - 1));
	// Ein Bild statt sap.m.Carousel: das Carousel schob die Bilder im Dialog aus dem sichtbaren Bereich (schwarze Fläche).
	const image = new Image({ decorative: false, densityAware: false }).addStyleClass("eoLightboxImage");
	const stage = new FlexBox({ items: [image], justifyContent: "Center", alignItems: "Center", height: "72vh", width: "100%" })
		.addStyleClass("eoLightboxStage");
	const go = (to: number) => {
		index = (to + list.length) % list.length;
		show();
	};
	const title = new Title({ level: "H2", wrapping: true });
	const captured = new Text();
	const author = new Text();
	const relation = new Text();
	const size = new Text();
	const caption = new Input({ width: "100%", placeholder: "z. B. Randdämmung Bad OG", maxLength: 500, editable: session.has("files.upload") });
	const saveCaption = new Button({
		text: "Beschriftung speichern", icon: "sap-icon://save", visible: session.has("files.upload"),
		press: async () => {
			const file = list[index];
			try {
				const updated = await put<FileResponse>(`files/${file.id}/details`, {
					originalName: file.originalName, caption: caption.getValue().trim() || null, category: file.category, folder: file.folder,
					roomId: file.roomId, workItemId: file.workItemId,
				});
				list[index] = updated;
				MessageToast.show("Beschriftung gespeichert");
				options.onChanged?.(updated);
				show();
			} catch (error) {
				MessageToast.show(problemMessage(error));
			}
		},
	});
	const download = new Link({ text: "Original herunterladen", icon: "sap-icon://download" });
	const counter = new Text();

	const show = () => {
		const f = list[index];
		title.setText(f.caption || f.originalName);
		captured.setText(`Aufnahme: ${dateTime(f.capturedAt ?? f.createdAt)}`);
		author.setText(`Von: ${session.name(f.uploadedByUserId)} · hochgeladen ${dateTime(f.createdAt)}`);
		const parts = [options.roomName?.(f.roomId), options.workName?.(f.workItemId)].filter(Boolean);
		relation.setText(parts.length ? `Bezug: ${parts.join(" · ")}` : "Bezug: –");
		size.setText(`${f.originalName} · ${fileSize(f.sizeBytes)}`);
		caption.setValue(f.caption ?? "");
		download.setHref(`${url(f, "original")}${url(f, "original").includes("?") ? "&" : "?"}download=1`);
		download.data("name", f.originalName);
		counter.setText(`${index + 1} von ${list.length}`);
		image.setSrc(url(f, "display"));
		image.setAlt(f.caption ?? `Foto vom ${dateTime(f.capturedAt ?? f.createdAt)}`);
		// Nachbarbilder vorladen, damit das Blättern sofort reagiert.
		for (const n of [index + 1, index - 1]) if (list.length > 1) new window.Image().src = url(list[(n + list.length) % list.length], "display");
	};
	download.attachPress(() => {
		const a = document.createElement("a");
		a.href = `/api/v1/files/${list[index].id}/content`;
		a.download = list[index].originalName;
		a.click();
	});

	const side = new VBox({
		width: "20rem",
		items: [title, counter, captured, author, relation, size,
			new Label({ text: "Beschriftung", labelFor: caption }).addStyleClass("sapUiSmallMarginTop"), caption, saveCaption, download],
		layoutData: new FlexItemData({ shrinkFactor: 0 }),
	}).addStyleClass("sapUiSmallMargin");
	const dialog = new Dialog({
		title: "Fotos",
		contentWidth: "92vw",
		contentHeight: "82vh",
		resizable: true,
		horizontalScrolling: false,
		content: [new HBox({ items: [new VBox({ items: [stage], layoutData: new FlexItemData({ growFactor: 1 }) }), side], wrap: "NoWrap" })],
		buttons: [
			new Button({ icon: "sap-icon://navigation-left-arrow", tooltip: "Vorheriges Foto", text: "Zurück", press: () => go(index - 1), enabled: list.length > 1 }),
			new Button({ icon: "sap-icon://navigation-right-arrow", tooltip: "Nächstes Foto", text: "Weiter", press: () => go(index + 1), enabled: list.length > 1 }),
			new Button({ text: "Schließen", type: "Emphasized", press: () => dialog.close() }),
		],
		afterClose: () => dialog.destroy(),
	});
	dialog.addStyleClass("eoLightbox");
	dialog.attachBrowserEvent("keydown", (event: KeyboardEvent) => {
		if ((event.target as HTMLElement).tagName === "INPUT") return;
		if (event.key === "ArrowRight") go(index + 1);
		if (event.key === "ArrowLeft") go(index - 1);
	});
	show();
	dialog.open();
}
