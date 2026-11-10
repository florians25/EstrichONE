/**
 * Reine Logik für Kundenanlage, Bauaktenanlage und Kundenakte (ohne UI5, mit Vitest geprüft):
 * Dublettenabfragen aus den Formularwerten bilden und Treffer zusammenführen, Adressen vergleichen und übernehmen,
 * Kennzahlen der Kundenakte aufbereiten.
 */
import type { ContactResponse } from "./models";

export interface DuplicateCandidate {
	contact: ContactResponse;
	score: number;
	reasons: string[];
}

export interface CustomerDraft {
	name: string;
	street?: string | null;
	postalCode?: string | null;
	city?: string | null;
	phones?: string[];
	emails?: string[];
}

export interface Address {
	street?: string | null;
	postalCode?: string | null;
	city?: string | null;
}

export type DuplicateQuery = Record<string, string | null>;

const clean = (v: string | null | undefined) => (v ?? "").trim();

/** Kleinbuchstaben, Umlaute ausgeschrieben, Satzzeichen entfernt, „str.“ → „strasse“ (wie die Dublettenprüfung des Servers). */
export function normalize(value: string | null | undefined): string {
	const text = clean(value).toLowerCase().replace(/ä/g, "ae").replace(/ö/g, "oe").replace(/ü/g, "ue").replace(/ß/g, "ss")
		.normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, " ").trim();
	return text.split(" ").filter(Boolean)
		.map((t) => (t === "str" ? "strasse" : t.endsWith("str") && t.length > 4 ? `${t.slice(0, -3)}strasse` : t))
		.join(" ");
}

/** Ziffern einer Telefonnummer, Ländervorwahl +49/0049 als 0; unter sechs Ziffern ohne Aussagekraft. */
export function phoneDigits(value: string | null | undefined): string | null {
	const raw = clean(value);
	let digits = raw.replace(/\D/g, "");
	if (digits.startsWith("0049")) digits = `0${digits.slice(4)}`;
	else if (raw.startsWith("+") && digits.startsWith("49")) digits = `0${digits.slice(2)}`;
	return digits.length >= 6 ? digits : null;
}

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Abfragen für `contacts/duplicates`: je Telefonnummer und E-Mail-Adresse eine, mindestens eine. Liefert eine leere Liste,
 * solange nichts Aussagekräftiges eingegeben ist (Name unter drei Zeichen, keine Nummer, E-Mail oder vollständige Anschrift).
 */
export function duplicateQueries(draft: CustomerDraft): DuplicateQuery[] {
	const name = clean(draft.name);
	const phones = (draft.phones ?? []).map(clean).filter((p) => phoneDigits(p));
	const emails = (draft.emails ?? []).map(clean).filter((e) => emailPattern.test(e));
	const street = clean(draft.street) || null;
	const postalCode = clean(draft.postalCode) || null;
	const meaningful = normalize(name).replace(/ /g, "").length >= 3 || phones.length > 0 || emails.length > 0 || (!!street && !!postalCode);
	if (!meaningful) return [];
	const base = { name: name || null, street, postalCode, city: clean(draft.city) || null };
	const count = Math.max(1, phones.length, emails.length);
	return Array.from({ length: count }, (_, i) => ({ ...base, phone: phones[i] ?? null, email: emails[i] ?? null }));
}

/** Schlüssel einer Abfrage, um unveränderte Eingaben nicht erneut zu prüfen. */
export function draftKey(draft: CustomerDraft): string {
	return JSON.stringify(duplicateQueries(draft));
}

/** Treffer mehrerer Abfragen zusammenführen: je Kunde der höchste Wert und alle Gründe, bester Treffer zuerst. */
export function mergeCandidates(lists: DuplicateCandidate[][], exclude?: string | null): DuplicateCandidate[] {
	const byId = new Map<string, DuplicateCandidate>();
	for (const d of lists.flat()) {
		if (d.contact.id === exclude) continue;
		const known = byId.get(d.contact.id);
		if (!known) {
			byId.set(d.contact.id, { ...d, reasons: [...d.reasons] });
			continue;
		}
		known.score = Math.max(known.score, d.score);
		for (const r of d.reasons) if (!known.reasons.includes(r)) known.reasons.push(r);
	}
	return [...byId.values()].sort((a, b) => b.score - a.score || a.contact.name.localeCompare(b.contact.name, "de"));
}

/** Stärke eines Treffers für die Anzeige. */
export function candidateState(score: number): "Error" | "Warning" {
	return score >= 60 ? "Error" : "Warning";
}

/** Eine Zeile mit Anschrift und Erreichbarkeit eines Treffers. */
export function candidateLine(c: ContactResponse): string {
	return [c.customerNumber ? `Kd.-Nr. ${c.customerNumber}` : null, addressLine(c), c.phone, c.email].filter(Boolean).join(" · ") || "Keine weiteren Angaben";
}

export function addressLine(a: Address | null | undefined): string {
	if (!a) return "";
	return [clean(a.street), [clean(a.postalCode), clean(a.city)].filter(Boolean).join(" ")].filter(Boolean).join(", ");
}

export function hasAddress(a: Address | null | undefined): boolean {
	return !!a && (!!clean(a.street) || !!clean(a.city) || !!clean(a.postalCode));
}

/** Baustellenadresse „wie Kundenanschrift“: Straße, PLZ, Ort des Kunden. */
export function siteFromCustomer(customer: Address | null | undefined): Required<Address> {
	return { street: clean(customer?.street) || null, postalCode: clean(customer?.postalCode) || null, city: clean(customer?.city) || null };
}

/** Gleiche Adresse: gleiche Straße mit Hausnummer (normalisiert) und gleiche PLZ, ohne PLZ der gleiche Ort. */
export function sameAddress(a: Address | null | undefined, b: Address | null | undefined): boolean {
	const sa = normalize(a?.street);
	if (!sa || sa !== normalize(b?.street)) return false;
	const pa = clean(a?.postalCode);
	const pb = clean(b?.postalCode);
	if (pa && pb) return pa === pb;
	return !!normalize(a?.city) && normalize(a?.city) === normalize(b?.city);
}

/** Bauakten an derselben Adresse (für den Hinweis bei der Anlage). */
export function atSameAddress<T extends Address & { id: string }>(list: T[], address: Address, excludeId?: string | null): T[] {
	return list.filter((b) => b.id !== excludeId && sameAddress(b, address));
}

/** Routenplanung zur Anschrift (Kartendienst im Browser bzw. in der Karten-App). */
export function routeUrl(a: Address | null | undefined): string | null {
	const line = addressLine(a);
	return line ? `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(line)}` : null;
}

/** Prüfungen der Kundenanlage; liefert je Feld eine Meldung. */
export function validateCustomer(v: { kind: string; name: string; postalCode?: string | null; phones?: string[]; emails?: string[]; personEmail?: string | null }):
	Record<string, string> {
	const errors: Record<string, string> = {};
	if (!clean(v.name)) errors.name = v.kind === "company" ? "Bitte den Firmennamen angeben." : "Bitte den Namen angeben.";
	const postal = clean(v.postalCode);
	if (postal && /^\d+$/.test(postal) && (postal.length < 4 || postal.length > 5)) errors.postalCode = "Bitte eine Postleitzahl mit 4 oder 5 Ziffern angeben.";
	const badPhone = (v.phones ?? []).map(clean).find((p) => p && (!/^[0-9 +()/.-]+$/.test(p) || p.replace(/\D/g, "").length < 6));
	if (badPhone) errors.phones = `Bitte eine gültige Telefonnummer angeben („${badPhone}“).`;
	const badMail = (v.emails ?? []).map(clean).find((e) => e && !emailPattern.test(e));
	if (badMail) errors.emails = `Bitte eine gültige E-Mail-Adresse angeben („${badMail}“).`;
	if (clean(v.personEmail) && !emailPattern.test(clean(v.personEmail))) errors.personEmail = "Bitte eine gültige E-Mail-Adresse angeben.";
	return errors;
}

export interface KeyFigures {
	bauaktenTotal: number;
	bauaktenActive: number;
	inquiries: number;
	completed: number;
	totalArea: number;
	lastActivity: string | null;
	openTasks: number | null;
	openDefects: number | null;
	nextPlannedStart: string | null;
}

/** Bauakten nach Status filtern (Kundenakte): alle, laufende, Anfragen, abgeschlossene/stornierte. */
export function filterByStatus<T extends { status: string }>(list: T[], filter: string): T[] {
	switch (filter) {
		case "active": return list.filter((b) => !["inquiry", "completed", "cancelled"].includes(b.status));
		case "inquiry": return list.filter((b) => b.status === "inquiry");
		case "old": return list.filter((b) => b.status === "completed" || b.status === "cancelled");
		default: return list;
	}
}

/** Neuere von zwei ISO-Zeitangaben. */
export function latest(...values: (string | null | undefined)[]): string | null {
	return values.filter((v): v is string => !!v).sort().at(-1) ?? null;
}
