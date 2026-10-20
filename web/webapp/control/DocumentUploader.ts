import Uploader from "sap/m/upload/Uploader";
import type UploadSetItem from "sap/m/upload/UploadSetItem";
import type { MetadataOptions } from "sap/ui/core/Element";
import { problemMessage } from "../util/api";
import { uploadFile, type UploadOptions } from "../util/upload";

/**
 * Uploader für sap.m.upload.UploadSet nach dem EstrichONE-Protokoll: ankündigen (SHA-256), dann Inhalt senden.
 * Ziel (Bauakte, Dokumentart, Ordner) liefert die Seite über `target` zum Zeitpunkt des Hochladens.
 * @namespace estrichone.control
 */
export default class DocumentUploader extends Uploader {
	static readonly metadata: MetadataOptions = {};

	/** Ziel des Uploads; von der Seite gesetzt. */
	target: () => Omit<UploadOptions, "onProgress" | "signal"> = () => ({ bauakteId: "" });
	/** Fehlermeldungen der letzten Dateien (für eine Zusammenfassung). */
	errors: string[] = [];
	private controllers = new Map<string, AbortController>();

	uploadItem(item: UploadSetItem): void {
		const file = item.getFileObject() as File;
		const abort = new AbortController();
		this.controllers.set(item.getId(), abort);
		this.fireUploadStarted({ item });
		uploadFile(file, {
			...this.target(),
			signal: abort.signal,
			onProgress: (loaded, total) => this.fireUploadProgressed({ item, loaded, total }),
		}).then((response) => {
			this.fireUploadCompleted({ item, responseXHR: { response: JSON.stringify(response), readyState: 4, status: 201, headers: {} } });
		}, (error: unknown) => {
			const message = `${file.name}: ${problemMessage(error)}`;
			this.errors.push(message);
			item.setVisibleEdit(false);
			this.fireUploadAborted({ item });
		}).finally(() => this.controllers.delete(item.getId()));
	}

	terminateItem(item: UploadSetItem): void {
		this.controllers.get(item.getId())?.abort();
	}
}
