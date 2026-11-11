/**
 * Reine Hilfsfunktionen des Anlage-Assistenten (ohne UI5, mit Vitest geprüft): Aufteilung des Formular-Schemas,
 * Beteiligte und Termin der Erstbegehung.
 */
import { fieldsOf, type FormSchema } from "./formSchema";

/** Lokale Zeit ohne Zeitzone (wie der Terminkalender: yyyy-MM-ddTHH:mm:00). */
const iso = (d: Date): string => {
	const p = (n: number) => String(n).padStart(2, "0");
	return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:00`;
};

/** Eckdaten zum Objekt und Estrich, die der Assistent im Schritt „Bauvorhaben“ zeigt. */
const objectFields = ["objectType", "floors", "totalArea", "screedType", "nominalThicknessMm", "underfloorHeating", "heatingType"];
/** Ausführung: gewünschter Zeitraum und Priorität. */
const scheduleFields = ["plannedStart", "plannedEnd", "priority"];

/**
 * Formular-Schema aufteilen: Eckdaten (Objekt, Estrich; dazu alle Pflichtfelder), Ausführung (Zeitraum, Priorität)
 * und weitere Kennwerte (alles Übrige, in den Gruppen der Stammdaten).
 */
export function splitSchema(schema: FormSchema): { primary: FormSchema; schedule: FormSchema; more: FormSchema } {
	const all = fieldsOf(schema);
	const pick = (keys: string[]) => keys.map((k) => all.find((f) => f.key === k)).filter((f) => !!f);
	const scheduleList = pick(scheduleFields);
	const primaryFields = [...pick(objectFields), ...all.filter((f) => f.required && !objectFields.includes(f.key) && !scheduleFields.includes(f.key))];
	const used = new Set([...primaryFields, ...scheduleList].map((f) => f!.key));
	type Fields = FormSchema["groups"][number]["fields"];
	return {
		primary: { ...schema, groups: primaryFields.length ? [{ key: "eckdaten", label: "Eckdaten", fields: primaryFields as Fields }] : [] },
		schedule: { ...schema, groups: scheduleList.length ? [{ key: "ausfuehrung", label: "Ausführung", fields: scheduleList as Fields }] : [] },
		more: { ...schema, groups: schema.groups.map((g) => ({ ...g, fields: g.fields.filter((f) => !used.has(f.key)) })).filter((g) => g.fields.length > 0) },
	};
}

/** Angaben zu einem Beteiligten (Bauleitung, Architekt) aus dem Assistenten. */
export interface PartyDraft { name: string; company: string; phone: string; email: string }

/** Anfrage für PUT bauakten/{id}/parties/{partyId}; `null`, wenn nichts erfasst ist. Ohne Namen gilt die Firma als Name. */
export function partyBody(role: string, d: PartyDraft): Record<string, unknown> | null {
	const t = (v: string) => v.trim() || null;
	const name = t(d.name) ?? t(d.company);
	if (!name) return null;
	return { role, contactId: null, name, company: t(d.company), phone: t(d.phone), email: t(d.email), note: null, version: null };
}

/** Wahl zur Erstbegehung im Assistenten. */
export type InspectionChoice = "later" | "appointment" | "now" | "waived";
export const inspectionChoices: { key: InspectionChoice; text: string }[] = [
	{ key: "later", text: "Später planen" },
	{ key: "appointment", text: "Termin für die Erstbegehung eintragen" },
	{ key: "now", text: "Direkt im Anschluss durchführen (z. B. jetzt beim Kunden vor Ort)" },
	{ key: "waived", text: "Erstbegehung nicht erforderlich" },
];

/** Beginn und Ende eines Termins aus Datum (yyyy-MM-dd), Uhrzeit (HH:mm) und Dauer in Minuten (lokale Zeit). */
export function appointmentSpan(day: string, time: string, minutes: number): { start: string; end: string } | null {
	const d = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
	const t = /^(\d{1,2}):(\d{2})$/.exec(time);
	if (!d || !t) return null;
	const start = new Date(Number(d[1]), Number(d[2]) - 1, Number(d[3]), Number(t[1]), Number(t[2]));
	return { start: iso(start), end: iso(new Date(start.getTime() + minutes * 60_000)) };
}
