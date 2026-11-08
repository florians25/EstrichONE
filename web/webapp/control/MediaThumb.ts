import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";

/**
 * Kleine, einheitliche Vorschaukachel für Fotos und Videos (feste Größe, Bild beschnitten, Video mit Abspiel-Symbol).
 * Ein Klick löst „press“ aus – die große Ansicht öffnet das Detailfenster (util/mediaViewer).
 * Größe über die Stilklasse (Standard 6 rem, „eoMediaThumbS“ 4,5 rem, „eoMediaThumbFill“ füllt die Rasterzelle).
 * @namespace estrichone.control
 */
export default class MediaThumb extends Control {
	static readonly metadata: MetadataOptions = {
		properties: {
			/** Vorschaubild bzw. bei Videos die Videodatei (das erste Bild dient als Standbild). */
			src: { type: "string", defaultValue: "" },
			/** "image", "video" oder "other" (Dokument: nur Symbol). */
			kind: { type: "string", defaultValue: "image" },
			/** Name für Screenreader und Tooltip. */
			alt: { type: "string", defaultValue: "" },
		},
		events: { press: {} },
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: MediaThumb): void {
			const kind = String(control.getProperty("kind") || "image");
			const alt = String(control.getProperty("alt") || "");
			const src = String(control.getProperty("src") || "");
			rm.openStart("button", control);
			rm.attr("type", "button");
			rm.class("eoMediaThumb");
			rm.attr("data-kind", kind);
			rm.attr("title", alt);
			rm.attr("aria-label", `${kind === "video" ? "Video" : kind === "image" ? "Foto" : "Datei"} öffnen: ${alt}`);
			rm.openEnd();
			if (kind === "video" && src) {
				rm.openStart("video").class("eoMediaThumbMedia").attr("muted", "muted").attr("playsinline", "playsinline")
					.attr("preload", "metadata").attr("src", `${src}#t=0.1`).attr("aria-hidden", "true").openEnd().close("video");
				rm.openStart("span").class("eoMediaThumbPlay").attr("aria-hidden", "true").openEnd().close("span");
			} else if (kind === "image" && src) {
				rm.voidStart("img").class("eoMediaThumbMedia").attr("loading", "lazy").attr("src", src).attr("alt", alt).voidEnd();
			} else {
				rm.icon("sap-icon://document", ["eoMediaThumbIcon"], { "aria-hidden": "true" });
			}
			rm.close("button");
		},
	};

	onclick(event: MouseEvent): void {
		// Nur mit eigenem Handler den Klick abfangen – sonst reagiert die umgebende Listenzeile (Auswahl, Öffnen).
		if (!this.hasListeners("press")) return;
		event.stopPropagation();
		this.fireEvent("press");
	}
}
