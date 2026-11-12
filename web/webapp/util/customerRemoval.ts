/**
 * Kunden löschen, archivieren oder anonymisieren – die Entscheidungslogik ohne UI5 (Vitest). Gelöscht wird nur ein
 * Kunde ohne Bezüge (Bauakten, Projektbeteiligungen, Anrufe …); sonst bietet die Oberfläche Archivieren und – mit
 * eigenem Recht – Anonymisieren an. Bestätigt wird durch Eintippen der Kundennummer (ohne Nummer: des Namens).
 */
import type { CustomerReference, CustomerReferencesResponse } from "./models";

export type RemovalAction = "delete" | "archive" | "reactivate" | "anonymize";

export interface RemovalRights {
	/** contacts.delete */
	delete: boolean;
	/** contacts.edit (Archivieren und Reaktivieren) */
	edit: boolean;
	/** contacts.anonymize */
	anonymize: boolean;
}

/** „1 Bauakte, 2 Anrufe und Anfragen und 1 Projektbeteiligung“. */
export function describeReferences(references: Pick<CustomerReference, "count" | "label">[]): string {
	const parts = references.filter((r) => r.count > 0).map((r) => `${r.count} ${r.label}`);
	if (parts.length <= 1) return parts.join("");
	return `${parts.slice(0, -1).join(", ")} und ${parts[parts.length - 1]}`;
}

/** Eingetippte Bestätigung: Groß-/Kleinschreibung und Leerzeichen am Rand egal. */
export function confirmMatches(input: string | null | undefined, expected: string): boolean {
	const value = (input ?? "").trim().toLowerCase();
	return value.length > 0 && value === expected.trim().toLowerCase();
}

/** Welche Aktionen der Dialog anbietet; die erste ist die empfohlene. */
export function removalActions(refs: Pick<CustomerReferencesResponse, "deletable" | "archived" | "anonymized">, rights: RemovalRights): RemovalAction[] {
	const actions: RemovalAction[] = [];
	if (refs.deletable && rights.delete) actions.push("delete");
	if (!refs.archived && rights.edit) actions.push("archive");
	if (refs.archived && !refs.anonymized && rights.edit) actions.push("reactivate");
	if (!refs.deletable && !refs.anonymized && rights.anonymize) actions.push("anonymize");
	return actions;
}

/** Hinweis „Kunde kann nicht gelöscht werden …“ bzw. was beim Löschen entfernt wird. */
export function removalMessage(refs: Pick<CustomerReferencesResponse, "deletable" | "references" | "customerNumber" | "name">): string {
	const who = refs.customerNumber ? `${refs.name} (Kd.-Nr. ${refs.customerNumber})` : refs.name;
	if (refs.deletable) {
		return `${who} hat keine Bauakten, Anrufe oder andere verknüpfte Daten. Beim Löschen werden Stammdaten, Telefonnummern, `
			+ `E-Mail-Adressen, Notiz und Ansprechpartner endgültig entfernt.`
			+ (refs.customerNumber ? ` Die Kundennummer ${refs.customerNumber} wird nicht erneut vergeben.` : "");
	}
	return `${who} kann nicht gelöscht werden. Noch verknüpft: ${describeReferences(refs.references)}. `
		+ "Stattdessen kann der Kunde archiviert werden: Er bleibt mit allen Unterlagen erhalten, erscheint aber nicht mehr in Auswahllisten und Suche.";
}

export interface BulkOutcome { name: string; action: "deleted" | "archived" | "skipped" | "failed"; reason?: string }

/** Zusammenfassung nach der Mehrfachauswahl, z. B. „2 gelöscht, 1 archiviert, 1 übersprungen“. */
export function summarizeBulk(outcomes: BulkOutcome[]): string {
	const count = (a: BulkOutcome["action"]) => outcomes.filter((o) => o.action === a).length;
	const parts = [
		count("deleted") ? `${count("deleted")} gelöscht` : "",
		count("archived") ? `${count("archived")} archiviert` : "",
		count("skipped") ? `${count("skipped")} übersprungen` : "",
		count("failed") ? `${count("failed")} fehlgeschlagen` : "",
	].filter(Boolean);
	return parts.join(", ") || "Nichts geändert";
}

/** Kurzer Statustext für Liste und Kopf. */
export function customerState(c: { archivedAt?: string | null; anonymizedAt?: string | null }): { text: string; state: "None" | "Warning" | "Information" } {
	if (c.anonymizedAt) return { text: "Anonymisiert", state: "Warning" };
	if (c.archivedAt) return { text: "Archiviert", state: "Warning" };
	return { text: "", state: "None" };
}

/**
 * Beschriftung für einen Treffer der Dublettenprüfung: Archivierte Kunden lassen sich nicht zuordnen (der Server lehnt
 * mit CUSTOMER_ARCHIVED ab) – stattdessen „Reaktivieren und verwenden“.
 */
export function useCandidateText(contact: { archivedAt?: string | null; anonymizedAt?: string | null }, text: string): string {
	if (!contact.archivedAt || contact.anonymizedAt) return text;
	return text.replace(/^(Vorhandenen Kunden|Diesen Kunden) verwenden$/, "Reaktivieren und verwenden").replace(/^Verknüpfen$/, "Reaktivieren und verknüpfen");
}

/** Anonymisierte Kunden sind nicht mehr verwendbar (Treffer ohne Schaltfläche). */
export const candidateUsable = (contact: { anonymizedAt?: string | null }) => !contact.anonymizedAt;
