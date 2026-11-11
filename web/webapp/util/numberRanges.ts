/**
 * Nummernkreise: Vorschau und Prüfung der Muster (wie im Server), Feld „Nummer“ für Stammdaten-Dialoge.
 * Muster: {JJJJ} Jahr, {JJ} Jahr zweistellig, {N…} laufende Nummer mit Mindeststellen; sonst Buchstaben, Ziffern und - / . _
 */
import type { NumberRangeResponse } from "./models";

/** Beispielnummer zu einem Muster. */
export function formatNumber(pattern: string, number: number, year = new Date().getFullYear()): string {
	return pattern.replace(/\{JJJJ\}/g, String(year)).replace(/\{JJ\}/g, String(year).slice(-2))
		.replace(/\{(N+)\}/g, (_m, n: string) => String(number).padStart(n.length, "0"));
}

export function isYearly(pattern: string): boolean {
	return pattern.includes("{JJ");
}

/** Höchster Zählerwert, der in die Stellen passt (z. B. 9999 bei „60{NNNN}“). */
export function capacity(pattern: string): number {
	const digits = Math.min(...[...pattern.matchAll(/\{(N+)\}/g)].map((m) => m[1].length), 9);
	return 10 ** digits - 1;
}

/** Fehlertext zum Muster oder null (gleiche Regeln wie der Server). */
export function patternError(pattern: string, maxLength = 30): string | null {
	const p = pattern.trim();
	if (!p) return "Bitte ein Muster angeben, z. B. 60{NNNN}.";
	if (p.length > maxLength) return `Das Muster darf höchstens ${maxLength} Zeichen lang sein.`;
	const counters = [...p.matchAll(/\{(N+)\}/g)];
	if (counters.length !== 1) return "Das Muster braucht genau eine laufende Nummer {N…}, z. B. {NNNN}.";
	if (counters[0][1].length > 9) return "Die laufende Nummer darf höchstens 9 Stellen haben.";
	const rest = p.replace(/\{JJJJ\}/g, "").replace(/\{JJ\}/g, "").replace(/\{N+\}/g, "");
	return /^[A-Za-z0-9\-/._]*$/.test(rest) ? null : "Erlaubt sind {JJJJ}, {JJ}, {N…} sowie Buchstaben, Ziffern und - / . _";
}

export interface RangeInput { pattern: string; rangeStart: number; rangeEnd: number | null; nextNumber: number | null }

/** Prüft Muster, Bereich und nächste Nummer; Fehlertext oder null. */
export function rangeError(r: RangeInput, maxLength = 30): string | null {
	const error = patternError(r.pattern, maxLength);
	if (error) return error;
	const cap = capacity(r.pattern);
	const yearly = isYearly(r.pattern);
	if (!Number.isInteger(r.rangeStart) || r.rangeStart < 1 || (!yearly && r.rangeStart > cap)) return `Der Bereich muss zwischen 1 und ${cap} beginnen.`;
	if (r.rangeEnd !== null && (!Number.isInteger(r.rangeEnd) || r.rangeEnd < r.rangeStart)) return "Das Ende des Bereichs muss größer oder gleich dem Anfang sein.";
	if (!yearly && r.rangeEnd !== null && r.rangeEnd > cap) return `Ohne Jahr im Muster muss der Bereich in die Stellen passen (Ende höchstens ${cap}).`;
	if (r.nextNumber !== null && (!Number.isInteger(r.nextNumber) || r.nextNumber < r.rangeStart || (r.rangeEnd !== null && r.nextNumber > r.rangeEnd))) {
		return "Die nächste Nummer muss im Bereich liegen.";
	}
	return null;
}

export type RangeLevel = "ok" | "warning" | "error";

/** Ampel „verbleibend“: ausgeschöpft = Fehler, unter 10 % bzw. höchstens 100 frei = Warnung. */
export function remainingLevel(r: Pick<NumberRangeResponse, "remaining" | "rangeStart" | "rangeEnd">): RangeLevel {
	if (r.remaining === null || r.rangeEnd === null) return "ok";
	if (r.remaining <= 0) return "error";
	const size = r.rangeEnd - r.rangeStart + 1;
	return r.remaining <= Math.max(10, Math.min(100, Math.ceil(size * 0.1))) ? "warning" : "ok";
}

export function remainingText(r: Pick<NumberRangeResponse, "remaining" | "yearly">): string {
	if (r.remaining === null) return r.yearly ? "unbegrenzt (je Jahr neu)" : "unbegrenzt";
	if (r.remaining <= 0) return "ausgeschöpft";
	return `${r.remaining.toLocaleString("de-DE")} frei`;
}

export function rangeText(r: Pick<NumberRangeResponse, "rangeStart" | "rangeEnd" | "yearly">): string {
	return `${r.rangeStart.toLocaleString("de-DE")} bis ${r.rangeEnd === null ? "∞" : r.rangeEnd.toLocaleString("de-DE")}${r.yearly ? " je Jahr" : ""}`;
}
