/**
 * Großansicht für Fotos: Vor/Zurück (Pfeiltasten), Metadaten (Aufnahme, Ersteller, Raum, Mangel), Beschriftung
 * bearbeiten und Original herunterladen.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Carousel from "sap/m/Carousel";
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
	const carousel = new Carousel({
		height: "72vh", width: "100%", loop: true, showPageIndicator: list.length > 1, pageIndicatorPlacement: "Bottom",
		pages: list.map((p) => new Image({ src: url(p, "display"), alt: p.caption ?? `Foto vom ${dateTime(p.capturedAt ?? p.createdAt)}`,
			decorative: false, lazyLoading: true, densityAware: false }).addStyleClass("eoLightboxImage")),
	});
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
	};
	download.attachPress(() => {
		const a = document.createElement("a");
		a.href = `/api/v1/files/${list[index].id}/content`;
		a.download = list[index].originalName;
		a.click();
	});
	carousel.attachPageChanged((event) => {
		const id = event.getParameter("newActivePageId");
		index = Math.max(0, carousel.getPages().findIndex((p) => p.getId() === id));
		show();
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
		content: [new HBox({ items: [new VBox({ items: [carousel], layoutData: new FlexItemData({ growFactor: 1 }) }), side], wrap: "NoWrap" })],
		buttons: [
			new Button({ icon: "sap-icon://navigation-left-arrow", tooltip: "Vorheriges Foto", text: "Zurück", press: () => carousel.previous() }),
			new Button({ icon: "sap-icon://navigation-right-arrow", tooltip: "Nächstes Foto", text: "Weiter", press: () => carousel.next() }),
			new Button({ text: "Schließen", type: "Emphasized", press: () => dialog.close() }),
		],
		afterClose: () => dialog.destroy(),
	});
	dialog.addStyleClass("eoLightbox");
	dialog.attachBrowserEvent("keydown", (event: KeyboardEvent) => {
		if ((event.target as HTMLElement).tagName === "INPUT") return;
		if (event.key === "ArrowRight") carousel.next();
		if (event.key === "ArrowLeft") carousel.previous();
	});
	carousel.setActivePage(carousel.getPages()[index]);
	show();
	dialog.open();
}
