/**
 * Statuszeile der Verwaltungsseiten: „Alles in Ordnung“ oder „Handlungsbedarf“ mit den offenen Punkten.
 * Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */

export type IssueLevel = "error" | "warning" | "info";
export interface Issue { text: string; level?: IssueLevel }

export interface AdminStatus {
	type: "Success" | "Warning" | "Error" | "Information";
	text: string;
	icon: string;
}

/**
 * Punkte ohne Text werden übersprungen (bequem für `cond && "Text"`). Fehler gehen vor Warnungen;
 * reine Hinweise (info) ergeben „Information“ ohne „Handlungsbedarf“.
 */
export function adminStatus(issues: (Issue | string | false | null | undefined)[], ok: string): AdminStatus {
	const list = issues.filter(Boolean).map((i) => (typeof i === "string" ? { text: i, level: "warning" as IssueLevel } : (i as Issue)))
		.filter((i) => i.text);
	const errors = list.filter((i) => i.level === "error");
	const warnings = list.filter((i) => !i.level || i.level === "warning");
	const infos = list.filter((i) => i.level === "info");
	if (errors.length || warnings.length) {
		const texts = [...errors, ...warnings].map((i) => i.text);
		return { type: errors.length ? "Error" : "Warning", icon: errors.length ? "sap-icon://error" : "sap-icon://alert",
			text: `Handlungsbedarf: ${texts.join(" · ")}` };
	}
	if (infos.length) return { type: "Information", icon: "sap-icon://hint", text: [ok, ...infos.map((i) => i.text)].filter(Boolean).join(" · ") };
	return { type: "Success", icon: "sap-icon://sys-enter-2", text: ok ? `Alles in Ordnung · ${ok}` : "Alles in Ordnung" };
}
