/**
 * Detailfenster für Fotos und Videos: Bild in voller Größe bzw. HTML5-Videoplayer, Vor/Zurück (auch Pfeiltasten),
 * Herunterladen, Schließen. Optional eine Seitenleiste (z. B. Metadaten und Beschriftung in der Bauakte).
 * Die Größe richtet sich nach dem Fenster (vw/vh), damit es auch bei 125/150 % Skalierung passt.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Toolbar from "sap/m/Toolbar";
import ToolbarSpacer from "sap/m/ToolbarSpacer";
import Title from "sap/m/Title";
import Text from "sap/m/Text";
import HBox from "sap/m/HBox";
import VBox from "sap/m/VBox";
import FlexItemData from "sap/m/FlexItemData";
import HTML from "sap/ui/core/HTML";
import type Control from "sap/ui/core/Control";
import { attachmentMedia, position, stageHtml, step, viewable, type MediaItem } from "./media";

export interface MediaViewerOptions {
	/** Überschrift des Fensters (Standard: „Foto“/„Video“ bzw. „Medien“). */
	title?: string;
	/** Seitenleiste rechts; wird bei jedem Wechsel mit dem aktuellen Eintrag aufgerufen. */
	side?: { controls: Control[]; update: (item: MediaItem, index: number) => void };
	/** Vor dem Schließen (z. B. Liste neu laden). */
	onClose?: () => void;
}

export interface MediaViewer {
	dialog: Dialog;
	/** Aktuellen Eintrag ersetzen (z. B. nach dem Speichern der Beschriftung). */
	replace: (item: MediaItem) => void;
	current: () => MediaItem;
}

export function openMediaViewer(items: MediaItem[], start: number, options: MediaViewerOptions = {}): MediaViewer | null {
	const list = [...items];
	if (list.length === 0) return null;
	let index = Math.max(0, Math.min(start, list.length - 1));

	const heading = new Title({ level: "H2", wrapping: false }).addStyleClass("eoMediaViewerTitle");
	const counter = new Text().addStyleClass("eoMediaViewerCounter eoTabular");
	const name = new Text({ wrapping: true }).addStyleClass("eoStrong");
	const meta = new Text({ wrapping: true }).addStyleClass("eoMuted");
	const stage = new HTML({ sanitizeContent: false, preferDOM: false });
	const many = list.length > 1;
	const prev = new Button({ icon: "sap-icon://navigation-left-arrow", text: "Zurück", tooltip: "Vorheriges (Pfeil links)", visible: many, press: () => go(-1) });
	const next = new Button({ icon: "sap-icon://navigation-right-arrow", text: "Weiter", tooltip: "Nächstes (Pfeil rechts)", iconFirst: false, visible: many,
		press: () => go(1) });

	const show = () => {
		const item = list[index];
		heading.setText(options.title ?? (item.kind === "video" ? "Video" : item.kind === "image" ? "Foto" : "Medien"));
		counter.setText(position(index, list.length));
		name.setText(item.caption || item.name);
		meta.setText([item.caption ? item.name : "", item.meta].filter(Boolean).join(" · "));
		meta.setVisible(!!meta.getText());
		stage.setContent(`<div class="eoMediaStage" data-kind="${item.kind}">${stageHtml(item)}</div>`);
		options.side?.update(item, index);
		// Nachbarbilder vorladen, damit das Blättern sofort reagiert.
		if (many) {
			for (const n of [step(index, 1, list.length), step(index, -1, list.length)]) {
				if (list[n].kind === "image") new window.Image().src = list[n].src;
			}
		}
	};
	const go = (delta: number) => {
		if (!many) return;
		index = step(index, delta, list.length);
		show();
	};
	// Fehlt die Vorschauvariante (noch), das Original zeigen.
	stage.attachEvent("afterRendering", () => {
		const img = stage.getDomRef()?.querySelector<HTMLImageElement>("img.eoMediaStageMedia");
		const fallback = list[index].fallback;
		if (img && fallback) img.addEventListener("error", () => { if (img.getAttribute("src") !== fallback) img.src = fallback; }, { once: true });
	});

	const download = () => {
		const item = list[index];
		const a = document.createElement("a");
		a.href = item.download;
		a.download = item.name;
		a.rel = "noopener";
		a.click();
	};
	const keys = (event: KeyboardEvent) => {
		const tag = (event.target as HTMLElement | null)?.tagName;
		if (tag === "INPUT" || tag === "TEXTAREA" || tag === "VIDEO") return;
		if (event.key === "ArrowLeft") go(-1);
		if (event.key === "ArrowRight") go(1);
	};

	const main = new VBox({
		renderType: "Bare",
		items: [stage, new VBox({ renderType: "Bare", items: [name, meta] }).addStyleClass("eoMediaViewerCaption")],
		layoutData: new FlexItemData({ growFactor: 1, minWidth: "0" }),
	}).addStyleClass("eoMediaViewerMain");
	const content: Control[] = [main];
	if (options.side) {
		content.push(new VBox({ items: options.side.controls, layoutData: new FlexItemData({ shrinkFactor: 0 }) }).addStyleClass("eoMediaViewerSide"));
	}

	const dialog = new Dialog({
		contentWidth: "94vw", contentHeight: "86vh", resizable: true, draggable: true,
		horizontalScrolling: false, verticalScrolling: false,
		customHeader: new Toolbar({ content: [heading, counter, new ToolbarSpacer(), prev, next,
			new Button({ text: "Herunterladen", icon: "sap-icon://download", press: download }),
			new Button({ text: "Schließen", icon: "sap-icon://decline", type: "Emphasized", press: () => dialog.close() }),
		] }),
		content: [new HBox({ renderType: "Bare", items: content }).addStyleClass("eoMediaViewerBody")],
		afterOpen: () => document.addEventListener("keydown", keys),
		afterClose: () => {
			document.removeEventListener("keydown", keys);
			options.onClose?.();
			dialog.destroy();
		},
	});
	dialog.addStyleClass("eoMediaViewer");
	dialog.addAriaLabelledBy(heading);
	show();
	dialog.open();
	return {
		dialog,
		replace: (item) => {
			list[index] = item;
			show();
		},
		current: () => list[index],
	};
}

/**
 * Anhang öffnen: Bilder und Videos im Detailfenster (Blättern über alle Medien derselben Liste),
 * andere Dateien wie bisher im neuen Tab.
 */
export function openAttachment(list: { id: string; name: string; contentType: string }[], id: string): void {
	const media = viewable(list.map(attachmentMedia));
	const index = media.findIndex((m) => m.id === id);
	if (index < 0) {
		window.open(`/api/v1/files/${id}/content`, "_blank", "noopener");
		return;
	}
	openMediaViewer(media, index);
}
