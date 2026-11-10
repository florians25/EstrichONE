/** „Erstbegehung nicht erforderlich“ setzen (mit kurzer Bestätigung, Begründung optional) oder aufheben. */
import EventBus from "sap/ui/core/EventBus";
import { put } from "./api";
import { akte } from "./akteStore";
import { openForm } from "./forms";
import type { BauakteDetail } from "./models";
import { WAIVE_BUTTON_TEXT } from "./inspectionWaiver";

export async function setInitialInspectionWaiver(bauakteId: string, waived: boolean, reason?: string | null): Promise<BauakteDetail> {
	const updated = await put<BauakteDetail>(`bauakten/${bauakteId}/initial-inspection-waiver`, { waived, reason: reason?.trim() || null });
	EventBus.getInstance().publish("bauakte", "updated", { bauakte: updated });
	akte.invalidate(bauakteId, "inspections");
	return updated;
}

/** Bestätigungsdialog; liefert die geänderte Bauakte oder null bei Abbruch. */
export function askWaiveInitialInspection(bauakteId: string): Promise<BauakteDetail | null> {
	return openForm<BauakteDetail>({
		title: WAIVE_BUTTON_TEXT, submitText: "Bestätigen", width: "32rem",
		intro: "Für diese Bauakte wird keine Erstbegehung durchgeführt. Sie wird danach nicht mehr als offen angezeigt. Der Vermerk lässt sich jederzeit rückgängig machen.",
		fields: [{ key: "reason", label: "Begründung (optional)", type: "textarea", rows: 3, maxLength: 500, placeholder: "z. B. Bestandsgebäude, Untergrund bekannt" }],
		submit: (v) => setInitialInspectionWaiver(bauakteId, true, v.reason ? String(v.reason) : null),
	});
}
