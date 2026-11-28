/**
 * Belegeingang: Dateien nacheinander hochladen (Inhalt als Rohdaten, der Server liest E-Rechnungen und erkennt doppelte
 * Dateien) und das Ergebnis als eine Meldung zusammenfassen. Gemeinsam für die Seite „Eingangsbelege“ und die Bauakte.
 */
import { api, ApiError } from "./api";
import { uploadProblem, type PurchaseDocument } from "./purchases";

export interface UploadResult { uploaded: PurchaseDocument[]; duplicates: number; problems: string[] }

export async function uploadPurchases(files: File[], bauakteId: string | null): Promise<UploadResult> {
	const result: UploadResult = { uploaded: [], duplicates: 0, problems: [] };
	for (const file of files) {
		const problem = uploadProblem(file);
		if (problem) {
			result.problems.push(problem);
			continue;
		}
		try {
			const doc = await api<PurchaseDocument>("purchases/documents", { method: "POST", raw: file, query: { fileName: file.name, bauakteId, source: "web" } });
			if (doc.duplicate) result.duplicates++;
			else result.uploaded.push(doc);
		} catch (error) {
			result.problems.push(`„${file.name}“: ${error instanceof ApiError ? error.message : String(error)}`);
		}
	}
	return result;
}

/** Zusammenfassung für die Meldung nach dem Hochladen. */
export function uploadText(r: UploadResult): string {
	const parts: string[] = [];
	if (r.uploaded.length) parts.push(`${r.uploaded.length} ${r.uploaded.length === 1 ? "Beleg" : "Belege"} hochgeladen.`);
	if (r.duplicates) parts.push(`${r.duplicates} ${r.duplicates === 1 ? "Datei war" : "Dateien waren"} schon vorhanden.`);
	const check = r.uploaded.filter((d) => d.data.problems.length).length;
	if (check) parts.push(`Bei ${check} ${check === 1 ? "Beleg fehlen" : "Belegen fehlen"} Pflichtangaben; bitte prüfen.`);
	return parts.join(" ");
}

/** Dateien direkt auf ein Element ziehen. */
export function attachDrop(dom: HTMLElement, enabled: () => boolean, onFiles: (files: File[]) => void): void {
	const hasFiles = (event: DragEvent) => [...(event.dataTransfer?.types ?? [])].includes("Files");
	dom.addEventListener("dragover", (event) => {
		if (!hasFiles(event) || !enabled()) return;
		event.preventDefault();
		dom.classList.add("eoDropActive");
	});
	dom.addEventListener("dragleave", () => dom.classList.remove("eoDropActive"));
	dom.addEventListener("drop", (event) => {
		if (!hasFiles(event) || !enabled()) return;
		event.preventDefault();
		dom.classList.remove("eoDropActive");
		onFiles([...(event.dataTransfer?.files ?? [])]);
	});
}
