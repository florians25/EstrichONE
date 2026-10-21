/**
 * Hochladen wie die Apps: Datei ankündigen (Metadaten + SHA-256), dann den Inhalt senden; der Server prüft
 * Typ und Prüfsumme. Mit Fortschritt (XMLHttpRequest) und optional als neue Fassung eines Dokuments.
 */
import { ApiError, csrfHeaders, get, post, problemMessage, refreshSession } from "./api";
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

/** Größe der Teile: klein genug für übliche Proxy-Grenzen (Nginx Proxy Manager, Cloudflare: 100 MB, Nginx-Standard: 1 MB). */
export const CHUNK_SIZE = 4 * 1024 * 1024;
const MIN_CHUNK = 256 * 1024;
const MAX_ATTEMPTS = 6;

interface ChunkAnswer {
	status: number;
	body: { code?: string; detail?: string; title?: string; receivedBytes?: number } & Partial<FileResponse>;
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
let backoffBase = 1000;
/** Nur für Tests: Grundwartezeit der Wiederholungen. */
export function setUploadBackoff(ms: number): void {
	backoffBase = ms;
}

/** Ein Teil per XMLHttpRequest (wegen Fortschritt). Netzfehler → status 0. */
function sendChunk(id: string, bytes: ArrayBuffer, start: number, end: number, options: UploadOptions, onProgress: (loaded: number) => void): Promise<ChunkAnswer> {
	return new Promise((resolve, reject) => {
		const xhr = new XMLHttpRequest();
		xhr.open("PUT", `/api/v1/files/${id}/content`);
		xhr.withCredentials = true;
		xhr.timeout = 120_000;
		xhr.setRequestHeader("Content-Type", "application/octet-stream");
		xhr.setRequestHeader("Accept", "application/json");
		xhr.setRequestHeader("Content-Range", `bytes ${start}-${end - 1}/${bytes.byteLength}`);
		for (const [key, value] of Object.entries(csrfHeaders("PUT"))) xhr.setRequestHeader(key, value);
		xhr.upload.onprogress = (event) => onProgress(event.loaded);
		xhr.onload = () => {
			let body: ChunkAnswer["body"] = {};
			try {
				body = JSON.parse(xhr.responseText || "{}");
			} catch {
				body = {}; // z. B. HTML-Fehlerseite eines Proxys
			}
			resolve({ status: xhr.status, body });
		};
		xhr.onerror = () => resolve({ status: 0, body: {} });
		xhr.ontimeout = () => resolve({ status: 0, body: { code: "TIMEOUT" } });
		xhr.onabort = () => reject(new ApiError(0, "ABORTED", "Hochladen abgebrochen."));
		if (options.signal?.aborted) {
			reject(new ApiError(0, "ABORTED", "Hochladen abgebrochen."));
			return;
		}
		options.signal?.addEventListener("abort", () => xhr.abort(), { once: true });
		xhr.send(bytes.slice(start, end));
	});
}

async function receivedBytes(id: string): Promise<number | null> {
	try {
		return (await get<{ receivedBytes: number }>(`files/${id}/upload-status`)).receivedBytes;
	} catch {
		return null;
	}
}

/**
 * Inhalt in Teilen senden (Content-Range), jeden Teil bei Netzfehlern, 5xx, 408 und 429 mit wachsender Wartezeit
 * wiederholen und nach Abbrüchen an der Stelle fortsetzen, die der Server meldet. Lehnt ein vorgeschalteter Proxy
 * die Größe ab (413), werden die Teile kleiner.
 */
async function sendContent(id: string, bytes: ArrayBuffer, options: UploadOptions): Promise<FileResponse> {
	const total = bytes.byteLength;
	let offset = (await receivedBytes(id)) ?? 0;
	let chunk = CHUNK_SIZE;
	let failures = 0;
	let refreshed = false;
	for (;;) {
		const end = Math.min(offset + chunk, total);
		const answer = await sendChunk(id, bytes, offset, end, options, (loaded) => options.onProgress?.(offset + loaded, total));
		const { status, body } = answer;
		if (status === 200 || status === 201) {
			options.onProgress?.(total, total);
			return body as FileResponse;
		}
		if (status === 202) {
			offset = body.receivedBytes ?? end;
			failures = 0;
			options.onProgress?.(offset, total);
			continue;
		}
		if (status === 409 && body.code === "OFFSET_MISMATCH" && typeof body.receivedBytes === "number") {
			offset = body.receivedBytes;
			continue;
		}
		if (status === 401 && !refreshed) {
			refreshed = true;
			if (await refreshSession()) continue;
		}
		if (status === 413 && !body.code && chunk > MIN_CHUNK) {
			chunk = Math.max(MIN_CHUNK, Math.floor(chunk / 4));
			continue;
		}
		const transient = status === 0 || status === 408 || status === 429 || status === 502 || status === 503 || status === 504;
		if (transient && ++failures < MAX_ATTEMPTS) {
			await sleep(backoffBase * 2 ** (failures - 1));
			offset = (await receivedBytes(id)) ?? offset;
			continue;
		}
		throw uploadError(status, body);
	}
}

/** Verständliche Ursache statt „Server nicht erreichbar“. */
export function uploadError(status: number, body: ChunkAnswer["body"]): ApiError {
	if (body.detail || body.title) {
		return new ApiError(status, body.code, body.detail ?? body.title ?? "");
	}
	const message = status === 0
		? body.code === "TIMEOUT"
			? "Zeitüberschreitung beim Hochladen. Die Verbindung ist zu langsam oder ein vorgeschalteter Proxy bricht lange Anfragen ab (Zeitlimit erhöhen)."
			: typeof navigator !== "undefined" && navigator.onLine === false
				? "Keine Internetverbindung. Das Hochladen kann fortgesetzt werden, sobald die Verbindung wieder besteht."
				: "Die Verbindung wurde beim Hochladen wiederholt unterbrochen. Bitte Netzwerk und vorgeschalteten Proxy (Größen- und Zeitlimit) prüfen und erneut versuchen – bereits Übertragenes wird fortgesetzt."
		: status === 413
			? "Ein vorgeschalteter Proxy lässt die Anfrage nicht durch (HTTP 413, Größenlimit). Im Proxy z. B. „client_max_body_size 64m“ setzen."
			: status === 401
				? "Die Anmeldung ist abgelaufen. Bitte neu anmelden und erneut hochladen."
				: status >= 500
					? `Der Server konnte die Datei nicht annehmen (HTTP ${status}). Bitte später erneut versuchen.`
					: `Hochladen fehlgeschlagen (HTTP ${status}).`;
	return new ApiError(status, body.code ?? (status === 0 ? "NETWORK" : undefined), message);
}

/** Bereits angekündigte, noch nicht fertige Dateien dieser Sitzung (zum Fortsetzen bei erneutem Versuch). */
const pending = new Map<string, string>();
const fileKey = (file: File, options: UploadOptions) =>
	[options.bauakteId, file.name, file.size, file.lastModified, options.previousVersionId ?? ""].join("|");

export async function uploadFile(file: File, options: UploadOptions): Promise<FileResponse> {
	const bytes = await file.arrayBuffer();
	const key = fileKey(file, options);
	const known = pending.get(key);
	if (known) {
		try {
			const result = await sendContent(known, bytes, options);
			pending.delete(key);
			return result;
		} catch (error) {
			if (!(error instanceof ApiError) || error.status !== 404) throw error;
			pending.delete(key);
		}
	}
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
	pending.set(key, id);
	const result = await sendContent(id, bytes, options);
	pending.delete(key);
	return result;
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
