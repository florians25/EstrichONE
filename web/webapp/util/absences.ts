/**
 * Abwesenheiten: Typen der Personal-API und reine Hilfsfunktionen (Texte für Konto und Anträge, Zeiträume, Farben),
 * ohne UI5-Abhängigkeiten, damit sie mit Vitest prüfbar sind.
 */
import type { VacationGridEntry } from "../control/VacationGrid";

export interface AbsenceTypeView {
	id: string; key: string; name: string; code: string; color: string; kind: string; requiresApproval: boolean; countsAsVacation: boolean;
	requestable: boolean; isPaid: boolean; isActive: boolean; sortOrder: number; isSystem: boolean; isSickness: boolean;
}

export interface TypeDays { typeId: string; name: string; color: string; days: number }

export interface VacationAccount {
	year: number; entitlement: number; carryOver: number; taken: number; planned: number; remaining: number; sickDays: number; requested: number;
	annualEntitlement: number; adjustment: number; carryOverExpiresOn: string | null; carryOverExpired: number; carryOverExpiring: number;
	remainingAfterRequests: number; byType: TypeDays[] | null; note: string | null;
}

export interface RequestView {
	id: string; userId: string; userName: string; from: string; to: string; kind: string; note: string | null; status: string; workingDays: number;
	requestedByName: string | null; decidedByName: string | null; decisionComment: string | null; warnings: string[]; remainingAfter: number | null;
	canDecide: boolean; canCancel: boolean; typeId: string | null; typeName: string | null; typeColor: string | null; halfDayStart: boolean; halfDayEnd: boolean;
	substituteUserId: string | null; substituteName: string | null; replacesRequestId: string | null; canChange: boolean; countsAsVacation: boolean;
}

export interface ClosureView { id: string; name: string; from: string; to: string; typeId: string; workingDays: number; employees: number }

export interface MyAbsence {
	id: string; from: string; to: string; typeId: string; typeName: string; color: string; kind: string; halfDayStart: boolean; halfDayEnd: boolean;
	workingDays: number; requestId: string | null;
}

export interface MyVacation {
	employeeId: string | null; vacation: VacationAccount | null; requests: RequestView[]; holidays: { date: string; name: string }[]; year: number;
	types: AbsenceTypeView[] | null; closures: ClosureView[] | null; absences: MyAbsence[] | null; canApprove: boolean; pendingApprovals: number;
}

export interface PlanRow {
	employeeId: string | null; userId: string; name: string; crews: { name: string }[]; entries: (VacationGridEntry & { typeId: string | null })[];
	vacationRemaining: number | null;
}

export interface Plan {
	from: string; to: string; state: string; holidays: { date: string; name: string }[]; rows: PlanRow[]; types: AbsenceTypeView[] | null;
	closures: ClosureView[] | null; crewMinimums: { crewId: string; name: string; minimumStaff: number; members: number }[] | null;
}

export interface Preview { workingDays: number; warnings: string[]; remainingAfter: number | null; requiresApproval: boolean; holidays: { date: string; name: string }[] }

export interface AccountRow {
	employeeId: string; userId: string | null; name: string; personnelNumber: string | null; crews: { name: string }[]; entryDate: string | null;
	exitDate: string | null; vacation: VacationAccount;
	account: { entitlement: number | null; carryOver: number | null; carryOverExpiresOn: string | null; adjustment: number; note: string | null } | null;
}

export interface Settings {
	state: string; stateName: string; states: { key: string; name: string }[]; maxCrewAbsencePercent: number; remindBirthdays: boolean; remindAnniversaries: boolean;
	carryOverExpiryMonth: number; carryOverExpiryDay: number; prorateEntitlement: boolean; accountingStartYear: number; crewLeadersApprove: boolean;
}

/** Farbauswahl für Abwesenheitsarten (gut unterscheidbar, auch in hellem und dunklem Theme). */
export const typeColors = [
	{ key: "#43A047", text: "Grün" }, { key: "#7CB342", text: "Hellgrün" }, { key: "#1E88E5", text: "Blau" }, { key: "#00ACC1", text: "Türkis" },
	{ key: "#3949AB", text: "Indigo" }, { key: "#8E24AA", text: "Violett" }, { key: "#F06292", text: "Rosa" }, { key: "#E53935", text: "Rot" },
	{ key: "#FB8C00", text: "Orange" }, { key: "#FDD835", text: "Gelb" }, { key: "#6D4C41", text: "Braun" }, { key: "#9E9E9E", text: "Grau" },
];

const de = (d: string) => `${d.slice(8, 10)}.${d.slice(5, 7)}.${d.slice(0, 4)}`;

/** Zahl mit höchstens einer Nachkommastelle (deutsch), leer als Gedankenstrich. */
export function num(v: number | null | undefined): string {
	return v == null ? "–" : v.toLocaleString("de-DE", { maximumFractionDigits: 1 });
}

/** Zeitraum „12.03.2027“ oder „12.03. – 14.03.2027“, mit Hinweis auf halbe Tage. */
export function range(from: string, to: string, halfDayStart = false, halfDayEnd = false): string {
	const text = from === to ? de(from) : `${from.slice(0, 4) === to.slice(0, 4) ? de(from).slice(0, 6) : de(from)} – ${de(to)}`;
	const half = from === to ? (halfDayStart || halfDayEnd ? " (halber Tag)" : "")
		: [halfDayStart ? "erster Tag halb" : null, halfDayEnd ? "letzter Tag halb" : null].filter(Boolean).join(", ");
	return half && from !== to ? `${text} (${half})` : `${text}${half}`;
}

export function escapeHtml(value: string): string {
	return value.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c] ?? c);
}

/** Farbpunkt als HTML (nur gültige #RRGGBB-Farben). */
export function colorDot(color: string | null | undefined): string {
	const safe = color && /^#[0-9a-f]{6}$/i.test(color) ? color : "#9E9E9E";
	return `<span class="eoVacTypeDot" style="background:${safe}"></span>`;
}

/** Regeln einer Art in Kurzform. */
export function typeRules(t: Pick<AbsenceTypeView, "requiresApproval" | "countsAsVacation" | "requestable" | "isPaid" | "isSickness">): string {
	if (t.isSickness) return "Krankheit, vom Büro erfasst, vertraulich";
	return [t.requiresApproval ? "genehmigungspflichtig" : "ohne Genehmigung", t.countsAsVacation ? "zählt als Urlaub" : null,
		t.requestable ? null : "nur Büro", t.isPaid ? null : "unbezahlt"].filter(Boolean).join(", ");
}

export const statusTexts: Record<string, { text: string; state: string }> = {
	pending: { text: "Beantragt", state: "Warning" }, approved: { text: "Genehmigt", state: "Success" }, rejected: { text: "Abgelehnt", state: "Error" },
	cancelled: { text: "Zurückgezogen", state: "None" },
};

/** Zeile für Antragslisten (eigene Übersicht und Genehmigung). */
export function requestRow(r: RequestView, own: boolean) {
	const status = r.status === "cancelled" && r.decisionComment === "Durch Änderungsantrag ersetzt." ? { text: "Ersetzt", state: "None" }
		: r.status === "cancelled" && r.decidedByName && r.decidedByName !== r.userName && !own ? { text: "Storniert", state: "None" }
		: statusTexts[r.status] ?? { text: r.status, state: "None" };
	return {
		...r,
		rangeText: range(r.from, r.to, r.halfDayStart, r.halfDayEnd),
		typeText: r.typeName ?? "Abwesenheit",
		dot: colorDot(r.typeColor),
		statusText: status.text,
		statusState: status.state,
		workingDaysText: num(r.workingDays),
		remainingAfterText: num(r.remainingAfter),
		changeText: r.replacesRequestId && r.status === "pending" ? "Änderungsantrag" : "",
		detailText: [r.substituteName ? `Vertretung: ${r.substituteName}` : null, r.decisionComment, r.note].filter(Boolean).join(" · "),
		canEdit: own && r.status === "pending",
	};
}

/** Texte für das Urlaubskonto (eigene Übersicht und Kontenliste). */
export function accountTexts(v: VacationAccount) {
	const expires = v.carryOverExpiresOn ? de(v.carryOverExpiresOn) : null;
	return {
		entitlement: num(v.entitlement),
		entitlementInfo: v.entitlement !== v.annualEntitlement ? `Jahresanspruch ${num(v.annualEntitlement)}` : "",
		carryOver: num(v.carryOver),
		carryInfo: v.carryOverExpired > 0 ? `${num(v.carryOverExpired)} verfallen am ${expires}` : v.carryOverExpiring > 0 ? `${num(v.carryOverExpiring)} verfallen am ${expires}` : "",
		adjustment: v.adjustment === 0 ? "–" : `${v.adjustment > 0 ? "+" : ""}${num(v.adjustment)}`,
		taken: num(v.taken),
		planned: num(v.planned),
		requested: num(v.requested),
		remaining: num(v.remaining),
		afterRequests: v.requested > 0 ? `nach Anträgen ${num(v.remainingAfterRequests)}` : "",
		expiringText: v.carryOverExpiring > 0
			? `${num(v.carryOverExpiring)} ${v.carryOverExpiring === 1 ? "Tag" : "Tage"} Resturlaub aus dem Vorjahr ${v.carryOverExpiring === 1 ? "verfällt" : "verfallen"} nach dem ${expires}, wenn sie bis dahin nicht genommen oder geplant sind.`
			: "",
	};
}

/** Anteil eines Tages (halber erster/letzter Tag). */
export function dayFraction(entry: Pick<VacationGridEntry, "from" | "to" | "halfDayStart" | "halfDayEnd">, day: string): number {
	return (day === entry.from && entry.halfDayStart) || (day === entry.to && entry.halfDayEnd) ? 0.5 : 1;
}

/** Hintergrund aus der Farbe der Art (deckend für genehmigt, heller für beantragt); halbe Tage diagonal geteilt. */
export function cellBackground(color: string, pending: boolean, half: boolean): string {
	const fill = `${color}${pending ? "40" : "80"}`;
	return half ? `linear-gradient(135deg, ${fill} 50%, transparent 50%)` : fill;
}

