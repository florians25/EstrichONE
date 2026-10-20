/**
 * Anzeigeformate (de-DE). Ohne UI5-Abhängigkeit – auch in Vitest geprüft. Datumswerte ohne Uhrzeit kommen vom
 * Server als „JJJJ-MM-TT“ (DateOnly) und werden in lokaler Zeit gelesen.
 */
import { parseDay } from "./days";

const dash = "–";
const euro = new Intl.NumberFormat("de-DE", { style: "currency", currency: "EUR" });
const amount = new Intl.NumberFormat("de-DE", { maximumFractionDigits: 3 });

export function dateTime(value: string | null | undefined): string {
	return value ? new Intl.DateTimeFormat("de-DE", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value)) : dash;
}

export function date(value: string | null | undefined): string {
	if (!value) return dash;
	const d = /^\d{4}-\d{2}-\d{2}$/.test(value) ? parseDay(value) : new Date(value);
	return d.toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit", year: "numeric" });
}

export function weekdayDate(value: string | null | undefined): string {
	return value ? parseDay(value).toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit", year: "numeric" }) : dash;
}

export function longDay(value: string | null | undefined): string {
	return value ? parseDay(value).toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long", year: "numeric" }) : dash;
}

export function time(value: string | null | undefined): string {
	return value ? value.slice(0, 5) : "";
}

export function timeRange(start: string | null | undefined, end: string | null | undefined): string {
	return start ? `${time(start)}${end ? `–${time(end)}` : ""}` : "";
}

export function fileSize(bytes: number): string {
	return bytes < 1024 * 1024
		? `${Math.max(1, Math.round(bytes / 1024))} KB`
		: `${(bytes / 1024 / 1024).toLocaleString("de-DE", { maximumFractionDigits: 1 })} MB`;
}

export function area(value: number | null | undefined): string {
	return value === null || value === undefined ? dash : `${value.toLocaleString("de-DE", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} m²`;
}

export function money(value: number | null | undefined): string {
	return value === null || value === undefined ? dash : euro.format(value);
}

export function quantity(value: number): string {
	return amount.format(value);
}

export function cmNumber(value: number | null | undefined): string {
	return value === null || value === undefined ? dash : value.toLocaleString("de-DE", { minimumFractionDigits: 1, maximumFractionDigits: 2 });
}

export function address(b: { street?: string | null; postalCode?: string | null; city?: string | null }): string {
	const place = [b.postalCode, b.city].filter(Boolean).join(" ");
	return [b.street, place].filter(Boolean).join(", ") || dash;
}

/** „seit 3 Std.“ – wie lange ein Ticket schon wartet. */
export function waitingSince(value: string, now = Date.now()): string {
	const minutes = Math.max(0, Math.round((now - new Date(value).getTime()) / 60000));
	if (minutes < 60) {
		return `${minutes} Min.`;
	}
	const hours = Math.round(minutes / 60);
	return hours < 48 ? `${hours} Std.` : `${Math.round(hours / 24)} Tage`;
}

/** Initialen für Avatare: „Anna Admin“ → „AA“. */
export function initials(name: string | null | undefined): string {
	return (name ?? "?").split(/\s+/).filter(Boolean).map((p) => p[0]).slice(0, 2).join("").toUpperCase();
}

export const weatherNames: Record<string, string> = { sunny: "sonnig", cloudy: "bewölkt", rain: "Regen", snow: "Schnee", frost: "Frost", wind: "Wind" };

export function weatherLine(entry: { weather: string | null; temperatureCelsius: number | null; workersPresent: number | null }): string {
	return [
		entry.weather ? weatherNames[entry.weather] : null,
		entry.temperatureCelsius !== null ? `${entry.temperatureCelsius} °C` : null,
		entry.workersPresent !== null ? `${entry.workersPresent} anwesend` : null,
	].filter(Boolean).join(" · ");
}

/** Ein deutscher Zahlenwert aus einem Eingabefeld („42,5“ → 42.5); leer → null. */
export function parseNumber(value: string | null | undefined): number | null {
	const raw = (value ?? "").trim();
	const text = raw.includes(",") ? raw.replace(/\./g, "").replace(",", ".") : raw;
	if (!text) return null;
	const n = Number(text);
	return Number.isFinite(n) ? n : NaN;
}

/** Dateiname für Downloads und Archive ohne problematische Zeichen. */
export function safeFileName(name: string): string {
	return name.replace(/[\\/:*?"<>|]+/g, "_").trim() || "Datei";
}
