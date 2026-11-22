/**
 * Freitextfelder der Bauakte (Hinweise, Beschreibung, Zugang und Besonderheiten sowie mehrzeilige Felder aus dem
 * Formular-Designer) als gut lesbare Textblöcke: Zeilenumbrüche bleiben, Links, E-Mail-Adressen und Telefonnummern
 * werden anklickbar, lange Texte zeigen zuerst nur den Anfang. Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */
import type { FormSchema } from "./formSchema";
import { readValue } from "./formSchema";

export interface FreeTextBlock {
	/** siteNotes, description oder Schlüssel des Schema-Felds. */
	key: string;
	label: string;
	text: string;
	/** Sicheres HTML (alles escapt, nur a und br) in einem div.eoFreeTextBody für core:HTML. */
	html: string;
	/** Lang genug für „Mehr anzeigen“. */
	long: boolean;
	expanded: boolean;
	/** Hervorheben (Hinweise und Behinderungen). */
	important: boolean;
	/** Bauakte-Feld (siteNotes, description) oder Pfad im Formular-Schema. */
	source: "bauakte" | "schema";
	path: string;
	maxLength: number;
}

/** Ab so vielen Zeichen bzw. Zeilen wird zunächst gekürzt. */
export const LONG_CHARS = 320;
export const LONG_LINES = 5;

const escape = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");

// URL (http/https oder www.), E-Mail, Telefonnummer (mind. 6 Ziffern, beginnt mit + oder 0).
const TOKEN = /(https?:\/\/[^\s<>"']+|www\.[^\s<>"']+)|([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})|((?:\+|\b0)[0-9][0-9 /()-]{4,}[0-9])/g;

/** Telefonnummer für tel: (nur Ziffern und führendes +). */
export function telHref(raw: string): string {
	const plus = raw.trim().startsWith("+");
	// „+49 (0)171 …“: die (0) entfällt bei internationaler Schreibweise.
	return `tel:${plus ? "+" : ""}${(plus ? raw.replace(/\(0\)/g, "") : raw).replace(/\D/g, "")}`;
}

/** Freitext in sicheres HTML: escapen, Links erkennen, Zeilenumbrüche als br. */
export function freeTextHtml(text: string | null | undefined): string {
	if (!text) return "";
	const normalized = text.replace(/\r\n?/g, "\n");
	let out = "";
	let last = 0;
	for (const match of normalized.matchAll(TOKEN)) {
		const [whole, url, mail, phone] = match;
		const index = match.index ?? 0;
		out += escape(normalized.slice(last, index));
		if (url) {
			// Satzzeichen am Ende gehören nicht zum Link.
			const trimmed = url.replace(/[.,;:!?)]+$/, "");
			const href = trimmed.startsWith("www.") ? `https://${trimmed}` : trimmed;
			out += `<a href="${escape(href)}" target="_blank" rel="noopener noreferrer">${escape(trimmed)}</a>${escape(url.slice(trimmed.length))}`;
		} else if (mail) {
			out += `<a href="mailto:${escape(mail)}">${escape(mail)}</a>`;
		} else if (phone && phone.replace(/\D/g, "").length >= 6) {
			out += `<a href="${escape(telHref(phone))}">${escape(phone)}</a>`;
		} else {
			out += escape(whole);
		}
		last = index + whole.length;
	}
	out += escape(normalized.slice(last));
	return out.replace(/\n/g, "<br>");
}

export function isLong(text: string | null | undefined): boolean {
	if (!text) return false;
	return text.length > LONG_CHARS || text.split(/\r\n?|\n/).length > LONG_LINES;
}

/**
 * Textblöcke für die Übersicht: zuerst Hinweise und Behinderungen, dann Beschreibung, dann mehrzeilige Felder aus dem
 * Formular-Schema (z. B. „Zugang / Besonderheiten“). Hinweise und Beschreibung erscheinen immer (leer = „Nicht erfasst“),
 * Schema-Felder nur mit Inhalt, damit die Übersicht ruhig bleibt.
 */
export function freeTextBlocks(schema: FormSchema | null, b: { siteNotes?: string | null; description?: string | null } & Parameters<typeof readValue>[0]): FreeTextBlock[] {
	const block = (key: string, label: string, text: string | null | undefined, source: FreeTextBlock["source"], path: string, maxLength: number,
		important = false): FreeTextBlock => {
		const value = (text ?? "").trim();
		return { key, label, text: value, html: `<div class="eoFreeTextBody">${freeTextHtml(value)}</div>`, long: isLong(value), expanded: false, important, source, path, maxLength };
	};
	const out = [
		block("siteNotes", "Hinweise und Behinderungen", b?.siteNotes, "bauakte", "siteNotes", 2000, true),
		block("description", "Beschreibung", b?.description, "bauakte", "description", 4000),
	];
	for (const g of schema?.groups ?? []) {
		for (const f of g.fields) {
			if (f.type !== "textarea") continue;
			const value = readValue(b, f.path);
			if (typeof value !== "string" || !value.trim()) continue;
			out.push(block(f.key, f.label, value, "schema", f.path, f.builtin ? 1000 : 2000));
		}
	}
	return out;
}
