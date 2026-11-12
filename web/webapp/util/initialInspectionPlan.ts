/**
 * Reiter „Erstbegehung“ der Dokumentation: Status (offen, geplant, durchgeführt, nicht erforderlich) und Termin
 * aus Einsätzen (Terminart Erstbegehung) und Kalenderterminen (Baustellentermin „Erstbegehung: …“).
 * Ohne UI5-Abhängigkeit (Vitest).
 */
import { initialInspectionState } from "./inspectionWaiver";
import { time, weekdayDate } from "./format";

export type InitialInspectionStatus = "open" | "planned" | "done" | "waived";

/** Ein geplanter Termin der Erstbegehung, gleich ob aus der Einsatzplanung oder dem Kalender. */
export interface InitialAppointment {
	/** Tag „JJJJ-MM-TT“ (Ortszeit). */
	day: string;
	/** Beginn „HH:mm“ oder leer (ganztägig/ohne Uhrzeit). */
	start: string;
	end: string;
	/** Kolonne bzw. Personen, kommagetrennt. */
	who: string;
	source: "assignment" | "calendar";
	id: string;
}

interface AssignmentLike {
	id: string; kind: string; day: string; startTime: string | null; endTime: string | null; crewName: string | null; people: { name: string }[];
}

interface EventLike {
	eventId: string; kind: string; title: string; start: string; end: string; allDay: boolean; bauakteId: string | null;
	people?: { displayName?: string; name?: string }[]; participants?: { displayName?: string; name?: string }[];
}

const pad = (n: number) => String(n).padStart(2, "0");
const localDay = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const localTime = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;

/** Termine der Erstbegehung einer Bauakte, nach Zeit sortiert. */
export function initialAppointments(bauakteId: string, assignments: AssignmentLike[], events: EventLike[]): InitialAppointment[] {
	const fromPlanning = assignments.filter((a) => a.kind === "initialInspection").map((a): InitialAppointment => ({
		day: a.day, start: time(a.startTime), end: time(a.endTime), source: "assignment", id: a.id,
		who: [a.crewName, ...a.people.map((p) => p.name)].filter((v, i, all): v is string => !!v && all.indexOf(v) === i).join(", "),
	}));
	const fromCalendar = events
		.filter((e) => e.bauakteId === bauakteId && /erstbegehung/i.test(e.title))
		.map((e): InitialAppointment => {
			const start = new Date(e.start);
			const end = new Date(e.end);
			const people = [...(e.participants ?? []), ...(e.people ?? [])].map((p) => p.displayName ?? p.name ?? "").filter(Boolean);
			return {
				day: e.allDay ? e.start.slice(0, 10) : localDay(start), start: e.allDay ? "" : localTime(start), end: e.allDay ? "" : localTime(end),
				who: [...new Set(people)].join(", "), source: "calendar", id: e.eventId,
			};
		});
	return [...fromPlanning, ...fromCalendar].sort((a, b) => (a.day + a.start).localeCompare(b.day + b.start));
}

/** Nächster Termin ab heute, sonst der letzte vergangene (falls die Begehung noch nicht erfasst ist). */
export function relevantAppointment(list: InitialAppointment[], today: string): InitialAppointment | null {
	return list.find((a) => a.day >= today) ?? list[list.length - 1] ?? null;
}

/** „Do., 19.11.2026, 08:00–10:00 · Kolonne Nord“ */
export function appointmentText(a: InitialAppointment | null): string {
	if (!a) return "";
	const span = a.start ? `, ${a.start}${a.end && a.end !== a.start ? `–${a.end}` : ""}` : "";
	return `${weekdayDate(a.day)}${span}${a.who ? ` · ${a.who}` : ""}`;
}

/** Status der Erstbegehung: erfasst vor Vermerk vor Termin. */
export function initialStatus(
	b: Parameters<typeof initialInspectionState>[0], inspections: { kind: string }[], appointment: InitialAppointment | null,
): InitialInspectionStatus {
	const state = initialInspectionState(b, inspections);
	if (state !== "open") return state;
	return appointment ? "planned" : "open";
}

export const initialStatusView: Record<InitialInspectionStatus, { text: string; state: "None" | "Success" | "Information" | "Warning"; icon: string }> = {
	open: { text: "Offen", state: "Warning", icon: "sap-icon://pending" },
	planned: { text: "Geplant", state: "Information", icon: "sap-icon://appointment-2" },
	done: { text: "Durchgeführt", state: "Success", icon: "sap-icon://accept" },
	waived: { text: "Nicht erforderlich", state: "None", icon: "sap-icon://decline" },
};

/** Dokumente der Erstbegehung: vom Server abgelegte Protokoll-PDFs liegen im Ordner „Erstbegehung“. */
export function isInitialInspectionDocument(f: { kind: string; folder: string | null; state: string }): boolean {
	return f.kind === "document" && f.state === "stored" && !!f.folder && /(^|\/)Erstbegehung$/i.test(f.folder.trim());
}
