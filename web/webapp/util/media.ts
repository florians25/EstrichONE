/**
 * Medien (Fotos, Videos) einheitlich darstellen: kleine Vorschaukacheln in Listen und Verläufen, die große Ansicht
 * öffnet sich im Detailfenster (util/mediaViewer). Ohne UI5-Abhängigkeit – auch in Vitest geprüft.
 */

export type MediaKind = "image" | "video" | "other";

/** Ein Eintrag für Kachel und Detailfenster. */
export interface MediaItem {
	id: string;
	name: string;
	kind: MediaKind;
	/** Volle Ansicht (Bild in großer Variante bzw. Videodatei). */
	src: string;
	/** Kleines Vorschaubild; bei Videos die Videodatei selbst (das erste Bild dient als Vorschau). */
	thumb: string;
	/** Ersatz, falls die Vorschauvariante (noch) fehlt: das Original. */
	fallback?: string;
	/** Adresse zum Herunterladen des Originals (über ein Link-Element mit download-Attribut). */
	download: string;
	/** Zusatzzeile im Detailfenster (Größe, Datum, Ersteller …). */
	meta?: string;
	/** Beschriftung, falls vorhanden. */
	caption?: string | null;
}

/** Art aus Inhaltstyp und Dateiname (HEIC/HEIF zeigt nicht jeder Browser an, daher nur mit Server-Vorschau als Bild). */
export function mediaKind(contentType: string | null | undefined, name = ""): MediaKind {
	const type = (contentType ?? "").toLowerCase();
	const ext = name.toLowerCase().split(".").pop() ?? "";
	if (type.startsWith("image/") || ["jpg", "jpeg", "png", "webp", "gif", "heic", "heif"].includes(ext)) return "image";
	if (type.startsWith("video/") || ["mp4", "mov", "m4v", "webm"].includes(ext)) return "video";
	return "other";
}

/** Felder einer Datei aus der Ablage (FileResponse), die für die Anzeige gebraucht werden. */
export interface StoredFile {
	id: string;
	originalName: string;
	contentType: string;
	variants: string;
	state?: string;
	caption?: string | null;
}

const content = (id: string) => `/api/v1/files/${id}/content`;

/** Adresse einer Bildvariante; ohne fertige Varianten das Original. */
export function fileVariantUrl(file: Pick<StoredFile, "id" | "variants">, variant: "thumb" | "display" | "large"): string {
	return `${content(file.id)}?variant=${file.variants === "ready" ? variant : "original"}`;
}

/** Datei der Ablage (Bauakte, Meldungen, Helpdesk) als Medieneintrag. */
export function fileMedia(file: StoredFile, meta?: string): MediaItem {
	const kind = mediaKind(file.contentType, file.originalName);
	return {
		id: file.id, name: file.originalName, kind, caption: file.caption ?? null, meta,
		src: kind === "image" ? fileVariantUrl(file, "large") : content(file.id),
		thumb: kind === "image" ? fileVariantUrl(file, "thumb") : content(file.id),
		fallback: kind === "image" ? content(file.id) : undefined,
		download: content(file.id),
	};
}

/** Anhang mit Kennung, Name und Inhaltstyp (Meldungen, Helpdesk), Vorschau über die Server-Varianten. */
export function attachmentMedia(a: { id: string; name: string; contentType: string }): MediaItem {
	return fileMedia({ id: a.id, originalName: a.name, contentType: a.contentType, variants: "ready" });
}

/** Nur die anzeigbaren Medien (Bilder, Videos) – Dokumente öffnen weiter wie gehabt. */
export function viewable<T extends { kind: MediaKind }>(items: T[]): T[] {
	return items.filter((i) => i.kind !== "other");
}

/** Nächster Index beim Blättern, am Ende wieder von vorn. */
export function step(index: number, delta: number, length: number): number {
	if (length <= 0) return 0;
	return (((index + delta) % length) + length) % length;
}

/** Startindex für eine Kennung (unbekannt = erster Eintrag). */
export function indexOf(items: { id: string }[], id: string | null | undefined): number {
	return Math.max(0, items.findIndex((i) => i.id === id));
}

/** „3 von 7“ – leer bei nur einem Medium. */
export function position(index: number, length: number): string {
	return length > 1 ? `${index + 1} von ${length}` : "";
}

export const escapeHtml = (value: string): string => value.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);

/** Inhalt einer Vorschaukachel (Bild bzw. stummes Video mit Abspiel-Symbol), z. B. für Chat-Verläufe. */
export function thumbInnerHtml(item: Pick<MediaItem, "kind" | "thumb" | "name">): string {
	const name = escapeHtml(item.name);
	if (item.kind === "video") {
		// #t=0.1: der Browser zeigt das erste Bild als Standbild, ohne das Video zu laden.
		return `<video class="eoMediaThumbMedia" muted playsinline preload="metadata" src="${escapeHtml(item.thumb)}#t=0.1" aria-hidden="true"></video>`
			+ '<span class="eoMediaThumbPlay" aria-hidden="true"></span>';
	}
	if (item.kind === "image") return `<img class="eoMediaThumbMedia" loading="lazy" src="${escapeHtml(item.thumb)}" alt="${name}">`;
	return `<span class="eoMediaThumbName">${name}</span>`;
}

/** Inhalt des Detailfensters: Bild in voller Größe bzw. HTML5-Videoplayer mit Bedienelementen. */
export function stageHtml(item: Pick<MediaItem, "kind" | "src" | "name">): string {
	const name = escapeHtml(item.name);
	if (item.kind === "video") {
		return `<video class="eoMediaStageMedia" controls playsinline preload="metadata" src="${escapeHtml(item.src)}" aria-label="${name}"></video>`;
	}
	if (item.kind === "image") return `<img class="eoMediaStageMedia" src="${escapeHtml(item.src)}" alt="${name}">`;
	return `<div class="eoMediaStageNone">Für „${name}“ gibt es keine Vorschau.</div>`;
}

/** Zeile für Anhänge in Meldungen und Helpdesk (Kachel mit Bild, Video-Standbild oder Dokumentsymbol). */
export interface AttachmentView {
	id: string;
	name: string;
	contentType: string;
	kind: MediaKind;
	thumb: string;
}

export function attachmentView(a: { id: string; name: string; contentType: string }): AttachmentView {
	const item = attachmentMedia(a);
	return { id: a.id, name: a.name, contentType: a.contentType, kind: item.kind, thumb: item.kind === "other" ? "" : item.thumb };
}
