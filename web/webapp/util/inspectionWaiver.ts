/**
 * „Erstbegehung nicht erforderlich“: Zustand und Anzeigetext. Ohne UI5-Abhängigkeit (Vitest).
 * Eine erfasste Erstbegehung hat Vorrang vor dem Vermerk.
 */
import { date } from "./format";

export type InitialInspectionState = "done" | "waived" | "open";

interface WaiverFields {
	initialInspectionWaived?: boolean;
	initialInspectionWaivedReason?: string | null;
	initialInspectionWaivedByName?: string | null;
	initialInspectionWaivedAt?: string | null;
}

export const WAIVE_BUTTON_TEXT = "Erstbegehung nicht erforderlich";

export function initialInspectionState(b: WaiverFields | null | undefined, inspections: { kind: string }[]): InitialInspectionState {
	if (inspections.some((i) => i.kind === "initialInspection")) return "done";
	return b?.initialInspectionWaived ? "waived" : "open";
}

/** „Keine Erstbegehung erforderlich (Name, Datum, Begründung)“; leere Angaben entfallen. */
export function waiverText(b: WaiverFields | null | undefined): string {
	if (!b?.initialInspectionWaived) return "";
	const parts = [
		b.initialInspectionWaivedByName?.trim(),
		b.initialInspectionWaivedAt ? date(b.initialInspectionWaivedAt) : "",
		b.initialInspectionWaivedReason?.trim(),
	].filter((p): p is string => !!p);
	return parts.length ? `Keine Erstbegehung erforderlich (${parts.join(", ")})` : "Keine Erstbegehung erforderlich";
}
