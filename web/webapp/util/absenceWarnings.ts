/**
 * Warnungen bei Krankmeldungen und Abwesenheiten (GET hr/absence-warnings): Typen und reine Hilfsfunktionen für Liste, Kennzahl
 * und Einstellungen – ohne UI5-Abhängigkeiten (Vitest). Gesundheitsbezogene Arten liefert der Server nur an die Personalverwaltung.
 */

export type WarningSeverity = "error" | "warning" | "info";

export interface AffectedAssignment {
	id: string; day: string; bauakteId: string; bauakteTitle: string; activity: string; crewId: string | null; crewName: string | null;
	present: number; minimumStaff: number | null; understaffed: boolean; text: string;
}

export interface AbsenceWarning {
	key: string; kind: string; severity: WarningSeverity; userId: string; userName: string; employeeId: string | null; date: string; title: string;
	text: string; absenceId: string | null; caseId: string | null; link: string | null; assignments: AffectedAssignment[];
}

export interface AbsenceWarningList { enabled: boolean; healthDetails: boolean; items: AbsenceWarning[]; acknowledged: number }

export interface AbsenceWarningSettings {
	enabled: boolean; planningImpact: boolean; planningImpactDays: number; certificate: boolean; certificateDueDay: number; continuedPay: boolean;
	bem: boolean; vacationOverlap: boolean; frequentShortThreshold: number; adjacent: boolean; notifyHr: boolean; notifyPlanners: boolean;
	notifyCrewLeaders: boolean;
}

export const kindLabels: Record<string, string> = {
	planning_impact: "Einsätze betroffen",
	certificate_missing: "AU-Nachweis fehlt",
	continued_pay: "Entgeltfortzahlung",
	bem: "BEM anbieten",
	vacation_overlap: "Überschneidung mit Urlaub",
	frequent_short: "Häufung kurzer Krankmeldungen",
	adjacent: "Angrenzend an freie Tage",
};

const kindIcons: Record<string, string> = {
	planning_impact: "sap-icon://shipping-status",
	certificate_missing: "sap-icon://document-text",
	continued_pay: "sap-icon://lateness",
	bem: "sap-icon://employee-approvals",
	vacation_overlap: "sap-icon://general-leave-request",
	frequent_short: "sap-icon://line-chart",
	adjacent: "sap-icon://calendar",
};

/** ValueState bzw. ObjectStatus-State zur Schwere. */
export function severityState(severity: WarningSeverity): "Error" | "Warning" | "Information" {
	return severity === "error" ? "Error" : severity === "warning" ? "Warning" : "Information";
}

export function severityText(severity: WarningSeverity): string {
	return severity === "error" ? "Dringend" : severity === "warning" ? "Warnung" : "Hinweis";
}

const isoDate = (d: string) => (/^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}.${d.slice(5, 7)}.${d.slice(0, 4)}` : d);

/** Zeile für die Warnungsliste (Web-Tabelle). */
export function warningRow(w: AbsenceWarning) {
	return {
		...w,
		state: severityState(w.severity),
		severityLabel: severityText(w.severity),
		kindLabel: kindLabels[w.kind] ?? w.kind,
		icon: kindIcons[w.kind] ?? "sap-icon://alert",
		dateText: isoDate(w.date),
		detailText: [w.text, ...w.assignments.map((a) => `• ${a.text}`)].join("\n"),
		canRetrieve: w.kind === "certificate_missing" && !!w.absenceId,
		canPlan: w.kind === "planning_impact" && !!w.link,
	};
}

/** Zählung für Kennzahl und MessageStrip. */
export function warningCounts(items: AbsenceWarning[]): { total: number; errors: number; warnings: number; infos: number; planning: number } {
	return {
		total: items.length,
		errors: items.filter((w) => w.severity === "error").length,
		warnings: items.filter((w) => w.severity === "warning").length,
		infos: items.filter((w) => w.severity === "info").length,
		planning: items.filter((w) => w.kind === "planning_impact").length,
	};
}

/** Text des MessageStrip über der Liste (leer, wenn nichts anliegt). */
export function warningSummary(list: Pick<AbsenceWarningList, "enabled" | "items">): { text: string; type: "Error" | "Warning" | "Information" | "Success" } {
	if (!list.enabled) return { text: "Warnungen sind in den Einstellungen abgeschaltet.", type: "Information" };
	const c = warningCounts(list.items);
	if (c.total === 0) return { text: "Keine offenen Warnungen.", type: "Success" };
	const parts = [c.errors ? `${c.errors} dringend` : null, c.warnings ? `${c.warnings} ${c.warnings === 1 ? "Warnung" : "Warnungen"}` : null,
		c.infos ? `${c.infos} ${c.infos === 1 ? "Hinweis" : "Hinweise"}` : null].filter(Boolean);
	const planning = c.planning ? ` ${c.planning === 1 ? "1 Abwesenheit betrifft" : `${c.planning} Abwesenheiten betreffen`} geplante Einsätze.` : "";
	return { text: `${parts.join(", ")}.${planning}`, type: c.errors ? "Error" : c.warnings ? "Warning" : "Information" };
}

/** Kennzahl „Abwesenheitswarnungen“ auf der Startseite. */
export function warningKpi(list: AbsenceWarningList, hr: boolean): { value: string; infoState: "Error" | "Warning" | "None"; to: string } | null {
	if (!list.enabled) return null;
	const c = warningCounts(list.items);
	const first = list.items.find((w) => w.kind === "planning_impact");
	return {
		value: String(c.total), infoState: c.errors ? "Error" : c.total ? "Warning" : "None",
		to: hr ? "/personal/urlaub?reiter=krank" : first?.link ?? "/planung",
	};
}

/** Stand des AU-Nachweises je Krankmeldung (vom Server berechnet). */
export function certificateStatus(status: string | undefined, dueOn: string | null | undefined): { text: string; state: "Success" | "Warning" | "None" } {
	switch (status) {
		case "attached": return { text: "vorhanden", state: "Success" };
		case "retrieved": return { text: "eAU abgerufen", state: "Success" };
		case "later": return { text: dueOn ? `ab ${isoDate(dueOn)}` : "noch nicht fällig", state: "None" };
		case "notRequired": return { text: "nicht erforderlich", state: "None" };
		default: return { text: "fehlt", state: "Warning" };
	}
}

/** Kurzbeschreibung der Einstellungen für die Verwaltung. */
export function settingsSummary(s: AbsenceWarningSettings): string {
	if (!s.enabled) return "Abgeschaltet.";
	const on = [
		s.planningImpact ? `Einsätze (${s.planningImpactDays} Tage Vorschau)` : null,
		s.certificate ? `AU ab ${s.certificateDueDay}. Kalendertag` : null,
		s.continuedPay ? "Entgeltfortzahlung" : null,
		s.bem ? "BEM" : null,
		s.vacationOverlap ? "Urlaubsüberschneidung" : null,
		s.frequentShortThreshold > 0 ? `Häufung ab ${s.frequentShortThreshold}` : null,
		s.adjacent ? "angrenzende freie Tage" : null,
	].filter(Boolean);
	const to = [s.notifyHr ? "Personal" : null, s.notifyPlanners ? "Disposition" : null, s.notifyCrewLeaders ? "Kolonnenführer" : null].filter(Boolean);
	return `Aktiv: ${on.length ? on.join(", ") : "keine Regel"}. Benachrichtigung: ${to.length ? to.join(", ") : "niemand"}.`;
}
