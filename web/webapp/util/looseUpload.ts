/**
 * Datei ohne Bauakte hochladen (Personaldokumente, Anhänge im Helpdesk): ankündigen (Metadaten + SHA-256), dann den
 * Inhalt in Teilen zu 4 MB mit Content-Range senden – so bleibt jede Anfrage unter üblichen Proxy-Grenzen.
 * Jeder Teil wird bei Netzwerkfehlern bis zu dreimal wiederholt.
 */
import { csrfHeaders, post } from "./api";
import { uuidv7 } from "./ids";
import { sha256Hex } from "./sha256";

const CHUNK = 4 * 1024 * 1024;

const typeByExtension: Record<string, string> = {
	pdf: "application/pdf", jpg: "image/jpeg", jpeg: "image/jpeg", png: "image/png", heic: "image/heic", webp: "image/webp",
	doc: "application/msword", docx: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
	xls: "application/vnd.ms-excel", xlsx: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", txt: "text/plain",
	mp4: "video/mp4", mov: "video/quicktime",
};

export function contentTypeOf(file: File): string {
	return file.type || typeByExtension[file.name.split(".").pop()?.toLowerCase() ?? ""] || "application/octet-stream";
}

async function putPart(id: string, part: ArrayBuffer, start: number, total: number): Promise<void> {
	for (let attempt = 1; ; attempt++) {
		try {
			const response = await fetch(`/api/v1/files/${id}/content`, {
				method: "PUT", credentials: "same-origin",
				headers: {
					"Content-Type": "application/octet-stream", Accept: "application/json", ...csrfHeaders("PUT"),
					...(part.byteLength < total ? { "Content-Range": `bytes ${start}-${start + part.byteLength - 1}/${total}` } : {}),
				},
				body: part,
			});
			if (response.ok) return;
			let detail = "";
			try {
				const body = (await response.json()) as { detail?: string; title?: string };
				detail = body.detail ?? body.title ?? "";
			} catch {
				detail = "";
			}
			if (response.status === 413) throw new Error("Die Datei ist zu groß für den Server bzw. Proxy (413).");
			if (response.status < 500 || attempt >= 3) throw new Error(detail || `Hochladen fehlgeschlagen (Status ${response.status}).`);
		} catch (error) {
			if (!(error instanceof TypeError) || attempt >= 3) throw error instanceof TypeError ? new Error("Verbindung beim Hochladen unterbrochen – bitte erneut versuchen.") : error;
		}
		await new Promise((resolve) => setTimeout(resolve, 500 * 2 ** attempt));
	}
}

/** Lädt hoch und liefert die Datei-ID. */
export async function uploadLoose(file: File, onProgress?: (done: number, total: number) => void): Promise<string> {
	const bytes = await file.arrayBuffer();
	const contentType = contentTypeOf(file);
	const id = uuidv7();
	const kind = contentType.startsWith("image/") ? "photo" : contentType.startsWith("video/") ? "video" : "document";
	await post("files", { id, bauakteId: null, kind, originalName: file.name, contentType, sizeBytes: file.size, sha256: await sha256Hex(bytes) });
	if (bytes.byteLength <= CHUNK) {
		await putPart(id, bytes, 0, bytes.byteLength);
	} else {
		for (let start = 0; start < bytes.byteLength; start += CHUNK) {
			await putPart(id, bytes.slice(start, Math.min(start + CHUNK, bytes.byteLength)), start, bytes.byteLength);
			onProgress?.(Math.min(start + CHUNK, bytes.byteLength), bytes.byteLength);
		}
	}
	onProgress?.(bytes.byteLength, bytes.byteLength);
	return id;
}

export const fileUrl = (id: string, variant?: "thumb" | "display") => `/api/v1/files/${id}/content${variant ? `?variant=${variant}` : ""}`;
