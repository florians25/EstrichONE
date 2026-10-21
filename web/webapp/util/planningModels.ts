/** Typen für Teamplaner, „Wer fährt wohin“, Terminkalender und Personalstamm (contracts/openapi/v1.json). */
import type { AbsenceKind, AbsenceView, AssignmentKind } from "./models";
import { navTo } from "./navigation";
import { api, ApiError } from "./api";

export interface PersonRef { id: string; name: string }
export interface CrewRef { id: string; name: string; color: string | null }
export interface ResourceRef { id: string; name: string; kind: "vehicle" | "pump" | "machine" }

// ---- Kalender -----------------------------------------------------------------------------------------------------

export type CalendarKind = "shared" | "personal";
export type CalendarEventKind =
	| "appointment" | "meeting" | "customerAppointment" | "siteVisit" | "delivery" | "vacation" | "training" | "private" | "other";
export type RecurrenceFrequency = "none" | "daily" | "weekly" | "monthly" | "yearly";
export type Weekday = "sunday" | "monday" | "tuesday" | "wednesday" | "thursday" | "friday" | "saturday";
export type CalendarEditScope = "series" | "occurrence" | "following";

export interface CalendarResponse {
	id: string; name: string; description: string | null; color: string; kind: CalendarKind; ownerUserId: string | null;
	sortOrder: number; isActive: boolean; canEdit: boolean; version: number;
}

export interface RecurrenceRule { frequency: RecurrenceFrequency; interval: number; weekdays: Weekday[] | null; until: string | null; count: number | null }

export interface CalendarOccurrence {
	eventId: string; occurrenceDate: string | null; start: string; end: string; allDay: boolean; title: string; location: string | null;
	description: string | null; kind: CalendarEventKind; color: string | null; calendarId: string; calendarName: string; calendarColor: string;
	isRecurring: boolean; canEdit: boolean; participants: PersonRef[]; crews: CrewRef[]; people: PersonRef[];
	bauakteId: string | null; bauakteNumber: string | null; bauakteTitle: string | null; version: number;
}

export interface CalendarEventDetail {
	id: string; calendarId: string; title: string; location: string | null; description: string | null; kind: CalendarEventKind; color: string | null;
	allDay: boolean; start: string; end: string; participantUserIds: string[]; crewIds: string[]; bauakteId: string | null; recurrence: RecurrenceRule;
	exceptionDates: string[]; seriesId: string | null; canEdit: boolean; version: number;
}

// ---- Wer fährt wohin --------------------------------------------------------------------------------------------

export interface DispatchJob {
	assignmentId: string; kind: AssignmentKind; activity: string; startTime: string | null; endTime: string | null; crewId: string | null;
	crewName: string | null; crewColor: string | null; notes: string | null;
}
export interface DispatchSite {
	bauakteId: string; number: string | null; title: string; address: string | null; latitude: number | null; longitude: number | null;
	siteContactName: string | null; siteContactPhone: string | null; startTime: string | null; jobs: DispatchJob[]; people: PersonRef[];
	vehicles: ResourceRef[]; warnings: string[]; navigationUrl: string | null; crews: DispatchSiteCrew[];
}
export interface DispatchSiteCrew { id: string; name: string; color: string | null; assignmentId: string; activity: string; startTime: string | null }
export interface DispatchCrew {
	id: string; name: string; color: string | null; leaderName: string | null; memberCount: number; vehicles: ResourceRef[]; siteIds: string[];
	absentMembers: { id: string; name: string; note: string }[];
}
export interface DispatchPerson { id: string; name: string; crewId: string | null; crewName: string | null; crewColor: string | null; siteIds: string[]; note: string | null }
export interface DispatchVehicle { id: string; name: string; kind: ResourceRef["kind"]; identifier: string | null; siteIds: string[] }
export interface DispatchAppointment { eventId: string; title: string; location: string | null; start: string; end: string; allDay: boolean; people: PersonRef[] }
export interface DispatchBoard {
	day: string; sites: DispatchSite[]; people: DispatchPerson[]; vehicles: DispatchVehicle[]; appointments: DispatchAppointment[];
	mySiteIds: string[]; canPlan: boolean; crews: DispatchCrew[];
}

// ---- Personal -----------------------------------------------------------------------------------------------------

export type EmploymentType = "fullTime" | "partTime" | "miniJob" | "apprentice" | "temporary" | "freelancer";
export type QualificationKind = "driverLicense" | "certificate" | "training" | "medical" | "other";
export type EmployeeDocumentCategory = "contract" | "certificate" | "driverLicense" | "medical" | "training" | "other";

export interface EmployeeListItem {
	id: string; personnelNumber: string | null; displayName: string; firstName: string; lastName: string; jobTitle: string | null;
	employmentType: EmploymentType; phone: string | null; mobile: string | null; email: string | null; city: string | null; userId: string | null;
	userName: string | null; isActive: boolean; entryDate: string | null; exitDate: string | null; crews: CrewRef[]; hourlyRate: number | null;
	vacationRemaining: number; absentToday: AbsenceKind | null; expiringQualifications: number; nextExpiry: string | null; version: number;
}
export interface QualificationView {
	id: string; kind: QualificationKind; name: string; number: string | null; issuedOn: string | null; expiresOn: string | null; notes: string | null;
	expired: boolean; expiresSoon: boolean;
}
export interface EquipmentView { id: string; resourceId: string | null; name: string; serialNumber: string | null; issuedOn: string | null; returnedOn: string | null; notes: string | null }
export interface EmployeeDocumentView {
	id: string; fileId: string; category: EmployeeDocumentCategory; title: string; fileName: string | null; contentType: string | null; sizeBytes: number;
	validUntil: string | null; addedAt: string;
}
export interface VacationSummary { year: number; entitlement: number; carryOver: number; taken: number; planned: number; remaining: number; sickDays: number; requested: number }
export interface EmployeeDetail {
	id: string; personnelNumber: string | null; firstName: string; lastName: string; displayName: string; jobTitle: string | null;
	employmentType: EmploymentType; userId: string | null; userName: string | null; email: string | null; phone: string | null; mobile: string | null;
	street: string | null; postalCode: string | null; city: string | null; birthDate: string | null; emergencyContactName: string | null;
	emergencyContactPhone: string | null; emergencyContactRelation: string | null; entryDate: string | null; exitDate: string | null;
	workTimeModel: string | null; weeklyHours: number | null; workDays: Weekday[]; hourlyRate: number | null; vacationDaysPerYear: number;
	vacationCarryOver: number; notes: string | null; isActive: boolean; crews: CrewRef[]; qualifications: QualificationView[]; equipment: EquipmentView[];
	documents: EmployeeDocumentView[]; absences: AbsenceView[]; vacation: VacationSummary; canManage: boolean; probationEnd: string | null; version: number;
}
export interface ExpiringQualification { employeeId: string; employeeName: string; qualification: QualificationView }

// ---- Helpdesk-Verlauf ---------------------------------------------------------------------------------------------

export interface TicketTimelineItem {
	id: string; at: string; kind: string; actorUserId: string | null; actorName: string | null; summary: string; text: string | null;
	fromValue: string | null; toValue: string | null; isInternal: boolean; isReporter: boolean; attachmentFileIds: string[];
}

// ---- Bezeichnungen --------------------------------------------------------------------------------------------------

export const calendarEventKinds: Record<CalendarEventKind, { text: string; color: string; icon: string }> = {
	appointment: { text: "Termin", color: "#0064D9", icon: "sap-icon://appointment" },
	meeting: { text: "Besprechung", color: "#7800A4", icon: "sap-icon://meeting-room" },
	customerAppointment: { text: "Kundentermin", color: "#C0399F", icon: "sap-icon://customer" },
	siteVisit: { text: "Baustellenbesuch", color: "#0B2D5B", icon: "sap-icon://building" },
	delivery: { text: "Lieferung", color: "#5D6D7E", icon: "sap-icon://shipping-status" },
	vacation: { text: "Urlaub / frei", color: "#188918", icon: "sap-icon://general-leave-request" },
	training: { text: "Schulung", color: "#00838F", icon: "sap-icon://learning-assistant" },
	private: { text: "Privat", color: "#8D6E00", icon: "sap-icon://private" },
	other: { text: "Sonstiges", color: "#94A3B8", icon: "sap-icon://overflow" },
};

export const weekdays: { key: Weekday; text: string; short: string }[] = [
	{ key: "monday", text: "Montag", short: "Mo" }, { key: "tuesday", text: "Dienstag", short: "Di" }, { key: "wednesday", text: "Mittwoch", short: "Mi" },
	{ key: "thursday", text: "Donnerstag", short: "Do" }, { key: "friday", text: "Freitag", short: "Fr" }, { key: "saturday", text: "Samstag", short: "Sa" },
	{ key: "sunday", text: "Sonntag", short: "So" },
];
export const weekdayOf = (d: Date): Weekday => (["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"] as Weekday[])[d.getDay()];

export const frequencies: Record<RecurrenceFrequency, string> = {
	none: "Keine Wiederholung", daily: "Täglich", weekly: "Wöchentlich", monthly: "Monatlich", yearly: "Jährlich",
};

export const employmentTypes: Record<EmploymentType, string> = {
	fullTime: "Vollzeit", partTime: "Teilzeit", miniJob: "Minijob", apprentice: "Auszubildende/r", temporary: "Aushilfe / Saison", freelancer: "Freie Mitarbeit",
};
export const qualificationKinds: Record<QualificationKind, string> = {
	driverLicense: "Führerscheinklasse", certificate: "Zertifikat / Schein", training: "Schulung / Unterweisung", medical: "Arbeitsmedizin (G-Untersuchung)",
	other: "Sonstiges",
};
export const documentCategoriesStaff: Record<EmployeeDocumentCategory, string> = {
	contract: "Vertrag", certificate: "Zeugnis / Nachweis", driverLicense: "Führerschein", medical: "Arbeitsmedizin", training: "Schulung", other: "Sonstiges",
};
/** Übliche Führerscheinklassen im Estrichbau. */
export const licenseClasses = ["B", "BE", "C1", "C1E", "C", "CE", "L", "T", "Kran", "Stapler"];

/** Lokale Ortszeit als ISO ohne Zeitzone („2026-11-04T08:00:00“) – so speichert der Server Termine. */
export function localIso(d: Date): string {
	const p = (n: number) => String(n).padStart(2, "0");
	return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:00`;
}
export function parseLocal(value: string): Date {
	const [date, time = "00:00:00"] = value.split("T");
	const [y, m, d] = date.split("-").map(Number);
	const [hh, mm] = time.split(":").map(Number);
	return new Date(y, m - 1, d, hh || 0, mm || 0);
}

/** Beginn/Ende eines Einsatzes für die Zeitachse; ohne Uhrzeit 7–16 Uhr, ohne Ende zwei Stunden. */
export function assignmentSpan(a: { day: string; startTime: string | null; endTime: string | null }): { start: Date; end: Date; timed: boolean } {
	const start = parseLocal(`${a.day}T${a.startTime ?? "07:00:00"}`);
	const end = a.endTime ? parseLocal(`${a.day}T${a.endTime}`) : a.startTime ? new Date(start.getTime() + 2 * 3600_000) : parseLocal(`${a.day}T16:00:00`);
	return { start, end, timed: !!a.startTime };
}

/** Query der aktuellen Seite ändern und die Seite neu laden lassen (Deep-Link: Ansicht, Datum, Reiter). */
export function navQuery(values: Record<string, string | null | undefined>): void {
	const query = new URLSearchParams(window.location.search);
	for (const [key, value] of Object.entries(values)) {
		if (value) query.set(key, value);
		else query.delete(key);
	}
	const text = query.toString();
	navTo(`${window.location.pathname}${text ? `?${text}` : ""}`);
}

/**
 * Datei (PDF) des Servers herunterladen: per fetch mit der Sitzung (abgelaufene Anmeldung wird still erneuert) und
 * mit dem Dateinamen des Servers; Fehler kommen als verständliche Meldung statt als gespeicherte Fehlerseite.
 */
export async function download(url: string): Promise<void> {
	let response = await fetch(url, { credentials: "same-origin" });
	if (response.status === 401) {
		await api("auth/me", { quiet: true }).catch(() => undefined);
		response = await fetch(url, { credentials: "same-origin" });
	}
	if (!response.ok) {
		let message = `Download fehlgeschlagen (Status ${response.status}).`;
		try {
			const problem = (await response.json()) as { detail?: string; title?: string };
			message = problem.detail ?? problem.title ?? message;
		} catch {
			// kein JSON
		}
		throw new ApiError(response.status, undefined, message);
	}
	const disposition = response.headers.get("Content-Disposition") ?? "";
	const star = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
	const plain = /filename="?([^";]+)"?/i.exec(disposition)?.[1];
	const name = star ? decodeURIComponent(star) : plain ?? "download.pdf";
	const href = URL.createObjectURL(await response.blob());
	const a = document.createElement("a");
	a.href = href;
	a.download = name;
	document.body.append(a);
	a.click();
	a.remove();
	setTimeout(() => URL.revokeObjectURL(href), 60_000);
}
