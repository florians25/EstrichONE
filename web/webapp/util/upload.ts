/**
 * Hochladen wie die Apps: Datei ankündigen (Metadaten + SHA-256), dann den Inhalt senden; der Server prüft
 * Typ und Prüfsumme. Mit Fortschritt (XMLHttpRequest) und optional als neue Fassung eines Dokuments.
 */
import { csrfHeaders, post, problemMessage, ApiError } from "./api";
import { uuidv7 } from "./ids";
import type { DocumentCategory, FileResponse } from "./models";
import { sha256Hex } from "./sha256";

export interface UploadOptions {
	bauakteId: string;
	category?: DocumentCategory | null;
	folder?: string | null;
	caption?: string | null;
	previousVersionId?: string | null;
	onProgress?: (loaded: number, total: number) => void;
	signal?: AbortSignal;
}

export function kindOf(contentType: string): "photo" | "video" | "document" {
	return contentType.startsWith("image/") ? "photo" : contentType.startsWith("video/") ? "video" : "document";
}

function sendContent(id: string, bytes: ArrayBuffer, options: UploadOptions): Promise<FileResponse> {
	return new Promise((resolve, reject) => {
		const xhr = new XMLHttpRequest();
		xhr.open("PUT", `/api/v1/files/${id}/content`);
		xhr.withCredentials = true;
		xhr.setRequestHeader("Content-Type", "application/octet-stream");
		xhr.setRequestHeader("Accept", "application/json");
		for (const [key, value] of Object.entries(csrfHeaders("PUT"))) xhr.setRequestHeader(key, value);
		xhr.upload.onprogress = (event) => options.onProgress?.(event.loaded, event.total || bytes.byteLength);
		xhr.onload = () => {
			let body: { code?: string; detail?: string; title?: string } & Partial<FileResponse> = {};
			try {
				body = JSON.parse(xhr.responseText || "{}");
			} catch {
				body = {};
			}
			if (xhr.status >= 200 && xhr.status < 300) {
				resolve(body as FileResponse);
			} else {
				reject(new ApiError(xhr.status, body.code, body.detail ?? body.title ?? `Hochladen fehlgeschlagen (Status ${xhr.status}).`));
			}
		};
		xhr.onerror = () => reject(new TypeError("Netzwerkfehler"));
		xhr.onabort = () => reject(new ApiError(0, "ABORTED", "Hochladen abgebrochen."));
		options.signal?.addEventListener("abort", () => xhr.abort());
		xhr.send(bytes);
	});
}

export async function uploadFile(file: File, options: UploadOptions): Promise<FileResponse> {
	const bytes = await file.arrayBuffer();
	const sha256 = await sha256Hex(bytes);
	const contentType = file.type || guessType(file.name);
	const kind = options.previousVersionId ? "document" : kindOf(contentType);
	const id = uuidv7();
	await post("files", {
		id, bauakteId: options.bauakteId, kind, originalName: file.name, contentType, sizeBytes: file.size, sha256,
		capturedAt: kind === "photo" ? new Date(file.lastModified || Date.now()).toISOString() : null,
		category: kind === "document" ? options.category ?? null : null,
		folder: kind === "document" ? options.folder ?? null : null,
		caption: options.caption ?? null,
		previousVersionId: options.previousVersionId ?? null,
	});
	return sendContent(id, bytes, options);
}

/** Dateityp aus der Endung, falls der Browser keinen meldet (z. B. HEIC, DWG). */
export function guessType(name: string): string {
	const ext = name.toLowerCase().split(".").pop() ?? "";
	const map: Record<string, string> = {
		pdf: "application/pdf", jpg: "image/jpeg", jpeg: "image/jpeg", png: "image/png", heic: "image/heic", webp: "image/webp",
		mp4: "video/mp4", mov: "video/quicktime", doc: "application/msword", docx: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
		xls: "application/vnd.ms-excel", xlsx: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", txt: "text/plain", csv: "text/csv",
	};
	return map[ext] ?? "application/octet-stream";
}

export interface BatchResult {
	done: FileResponse[];
	errors: string[];
}

/** Mehrere Dateien nacheinander; Fehler einzelner Dateien halten die anderen nicht auf. */
export async function uploadAll(files: File[], options: UploadOptions, onFile?: (index: number, total: number, name: string) => void): Promise<BatchResult> {
	const result: BatchResult = { done: [], errors: [] };
	for (const [index, file] of files.entries()) {
		onFile?.(index, files.length, file.name);
		try {
			result.done.push(await uploadFile(file, options));
		} catch (error) {
			result.errors.push(`${file.name}: ${problemMessage(error)}`);
		}
	}
	return result;
}
