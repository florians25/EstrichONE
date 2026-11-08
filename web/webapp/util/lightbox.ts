/**
 * Großansicht für Fotos und Videos der Bauakte im einheitlichen Detailfenster (util/mediaViewer): Vor/Zurück
 * (Pfeiltasten), Metadaten (Aufnahme, Ersteller, Raum, Mangel), Beschriftung bearbeiten, Original herunterladen.
 */
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import Label from "sap/m/Label";
import Input from "sap/m/Input";
import MessageToast from "sap/m/MessageToast";
import { problemMessage, put } from "./api";
import { dateTime, fileSize } from "./format";
import { fileMedia, type MediaItem } from "./media";
import { openMediaViewer, type MediaViewer } from "./mediaViewer";
import type { FileResponse } from "./models";
import { session } from "./session";

export interface LightboxOptions {
	roomName?: (id: string | null) => string | null;
	workName?: (id: string | null) => string | null;
	/** Nach dem Speichern der Beschriftung (Liste neu laden). */
	onChanged?: (file: FileResponse) => void;
	/** Überschrift (Standard „Fotos“). */
	title?: string;
}

const metaOf = (f: FileResponse) => `${fileSize(f.sizeBytes)} · ${dateTime(f.capturedAt ?? f.createdAt)}`;

export function openLightbox(files: FileResponse[], start: number, options: LightboxOptions = {}): MediaViewer | null {
	const startId = files[start]?.id;
	const list = files.filter((p) => p.state === "stored");
	if (list.length === 0) return null;
	const byId = new Map(list.map((f) => [f.id, f]));
	let currentId = "";

	const captured = new Text();
	const author = new Text();
	const relation = new Text();
	const size = new Text();
	const caption = new Input({ width: "100%", placeholder: "z. B. Randdämmung Bad OG", maxLength: 500, editable: session.has("files.upload") });
	const saveCaption = new Button({
		text: "Beschriftung speichern", icon: "sap-icon://save", visible: session.has("files.upload"),
		press: async () => {
			const file = byId.get(currentId);
			if (!file) return;
			try {
				const updated = await put<FileResponse>(`files/${file.id}/details`, {
					originalName: file.originalName, caption: caption.getValue().trim() || null, category: file.category, folder: file.folder,
					roomId: file.roomId, workItemId: file.workItemId,
				});
				byId.set(updated.id, updated);
				MessageToast.show("Beschriftung gespeichert");
				options.onChanged?.(updated);
				viewer?.replace(fileMedia(updated, metaOf(updated)));
			} catch (error) {
				MessageToast.show(problemMessage(error));
			}
		},
	});
	const update = (item: MediaItem) => {
		const f = byId.get(item.id)!;
		currentId = f.id;
		captured.setText(`Aufnahme: ${dateTime(f.capturedAt ?? f.createdAt)}`);
		author.setText(`Von: ${session.name(f.uploadedByUserId)} · hochgeladen ${dateTime(f.createdAt)}`);
		const parts = [options.roomName?.(f.roomId), options.workName?.(f.workItemId)].filter(Boolean);
		relation.setText(parts.length ? `Bezug: ${parts.join(" · ")}` : "Bezug: –");
		size.setText(`${f.originalName} · ${fileSize(f.sizeBytes)}`);
		caption.setValue(f.caption ?? "");
	};
	const items = list.map((f) => fileMedia(f, metaOf(f)));
	const viewer: MediaViewer | null = openMediaViewer(items, Math.max(0, list.findIndex((f) => f.id === startId)), {
		title: options.title ?? "Fotos",
		side: {
			controls: [captured, author, relation, size,
				new Label({ text: "Beschriftung", labelFor: caption }).addStyleClass("sapUiSmallMarginTop"), caption, saveCaption],
			update,
		},
	});
	return viewer;
}
