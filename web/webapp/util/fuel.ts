/**
 * Tankbelege (Modul „Tankbelege“): Bezeichnungen, Status, Filter, Kennzahlen und Auswertung je Fahrzeug.
 * Ohne UI5-Abhängigkeit – mit Vitest geprüft.
 */
import type { Tone } from "./labels";

export type FuelType = "diesel" | "petrol" | "adBlue" | "other";
export type FuelPaymentMethod = "fuelCard" | "companyCard" | "private";
export type FuelReceiptStatus = "new" | "checked" | "settled" | "rejected";
export type FuelDecision = "check" | "reject" | "settle" | "reopen";

export interface FuelWarning {
	code: "odometer_backwards" | "consumption_unusual" | "duplicate" | "exceeds_tank" | "missing_image" | string;
	text: string;
}

export interface FuelReceiptResponse {
	id: string;
	number: string;
	vehicleId: string;
	vehicleName: string;
	vehicleIdentifier: string | null;
	userId: string;
	userName: string;
	date: string;
	odometerKm: number | null;
	liters: number;
	amountGross: number;
	pricePerLiter: number;
	fuelType: FuelType;
	paymentMethod: FuelPaymentMethod;
	station: string | null;
	notes: string | null;
	imageFileId: string | null;
	imageStored: boolean;
	status: FuelReceiptStatus;
	statusText: string;
	rejectionReason: string | null;
	checkedByName: string | null;
	checkedAt: string | null;
	settledByName: string | null;
	settledAt: string | null;
	distanceKm: number | null;
	consumptionPer100Km: number | null;
	warnings: FuelWarning[];
	source: string;
	canEdit: boolean;
	createdAt: string;
	version: number;
}

export interface FuelMonthStatistics {
	month: string;
	receipts: number;
	liters: number;
	adBlueLiters: number;
	cost: number;
	distanceKm: number;
	averageConsumption: number | null;
}

export interface FuelVehicleStatistics {
	vehicleId: string;
	vehicleName: string;
	identifier: string | null;
	receipts: number;
	liters: number;
	adBlueLiters: number;
	cost: number;
	distanceKm: number;
	averageConsumption: number | null;
	lastOdometerKm: number | null;
	months: FuelMonthStatistics[];
}

export const fuelTypes: Record<FuelType, string> = { diesel: "Diesel", petrol: "Benzin", adBlue: "AdBlue", other: "Sonstiges" };

export const paymentMethods: Record<FuelPaymentMethod, string> = {
	fuelCard: "Tankkarte", companyCard: "Firmenkarte", private: "Privat ausgelegt",
};

/** Status mit Ton für ObjectStatus; „Erstattet“ (privat) bzw. „Gebucht“ (Karte). */
export function fuelStatus(status: FuelReceiptStatus, payment: FuelPaymentMethod): { text: string; tone: Tone } {
	switch (status) {
		case "new": return { text: "Neu", tone: "Information" };
		case "checked": return { text: "Geprüft", tone: "Success" };
		case "rejected": return { text: "Abgelehnt", tone: "Error" };
		default: return { text: payment === "private" ? "Erstattet" : "Gebucht", tone: "None" };
	}
}

/** Filter der Seite → Abfrage (leere Werte entfallen). */
export interface FuelFilter {
	from: string | null;
	to: string | null;
	vehicleId: string;
	userId: string;
	status: string;
	paymentMethod: string;
	warningsOnly: boolean;
}

export function fuelQuery(filter: FuelFilter): Record<string, string | boolean | undefined> {
	return {
		from: filter.from || undefined, to: filter.to || undefined, vehicleId: filter.vehicleId || undefined, userId: filter.userId || undefined,
		status: filter.status || undefined, paymentMethod: filter.paymentMethod || undefined, warningsOnly: filter.warningsOnly || undefined,
	};
}

/** Standardzeitraum: laufender und voriger Monat (Belege kommen oft verspätet). */
export function defaultRange(today: string): { from: string; to: string } {
	const [y, m] = today.split("-").map(Number);
	const first = new Date(Date.UTC(y, m - 2, 1));
	return { from: first.toISOString().slice(0, 10), to: today };
}

/** Kennzahlen über die angezeigten Belege. */
export function fuelKpis(items: FuelReceiptResponse[]): { open: number; flagged: number; refundOpen: number; refundAmount: number; cost: number; liters: number } {
	const counted = items.filter((r) => r.status !== "rejected");
	const refunds = items.filter((r) => r.paymentMethod === "private" && (r.status === "new" || r.status === "checked"));
	return {
		open: items.filter((r) => r.status === "new").length,
		flagged: items.filter((r) => r.warnings.some((w) => w.code !== "missing_image")).length,
		refundOpen: refunds.length,
		refundAmount: round2(refunds.reduce((sum, r) => sum + r.amountGross, 0)),
		cost: round2(counted.reduce((sum, r) => sum + r.amountGross, 0)),
		liters: round2(counted.filter((r) => r.fuelType !== "adBlue").reduce((sum, r) => sum + r.liters, 0)),
	};
}

/** Welche Entscheidungen für die gewählten Belege möglich sind (alle müssen passen). */
export function allowedDecisions(items: Pick<FuelReceiptResponse, "status">[]): FuelDecision[] {
	if (items.length === 0) return [];
	const all = (ok: (s: FuelReceiptStatus) => boolean) => items.every((r) => ok(r.status));
	const result: FuelDecision[] = [];
	if (all((s) => s === "new" || s === "rejected")) result.push("check");
	if (all((s) => s === "new" || s === "checked")) result.push("reject");
	if (all((s) => s === "checked")) result.push("settle");
	if (all((s) => s === "checked" || s === "rejected")) result.push("reopen");
	return result;
}

export const decisionTexts: Record<FuelDecision, string> = {
	check: "Freigeben", reject: "Ablehnen", settle: "Erstattet/Gebucht", reopen: "Zurücknehmen",
};

/** Text zur Erledigung: „Erstattet“ nur bei privat ausgelegten Belegen, sonst „Gebucht“. */
export function settleText(items: Pick<FuelReceiptResponse, "paymentMethod">[]): string {
	if (items.length > 0 && items.every((r) => r.paymentMethod === "private")) return "Als erstattet markieren";
	if (items.length > 0 && items.every((r) => r.paymentMethod !== "private")) return "Als gebucht markieren";
	return "Als erstattet bzw. gebucht markieren";
}

const number = (value: number, digits = 2) => value.toLocaleString("de-DE", { maximumFractionDigits: digits });

export const liters = (value: number | null | undefined): string => value === null || value === undefined ? "–" : `${number(value)} l`;
export const km = (value: number | null | undefined): string => value === null || value === undefined ? "–" : `${value.toLocaleString("de-DE")} km`;
export const consumption = (value: number | null | undefined): string =>
	value === null || value === undefined ? "–" : `${value.toLocaleString("de-DE", { minimumFractionDigits: 1, maximumFractionDigits: 1 })} l/100 km`;
export const pricePerLiter = (value: number): string => `${value.toLocaleString("de-DE", { minimumFractionDigits: 3, maximumFractionDigits: 3 })} €/l`;

const monthNames = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];
export function monthText(month: string): string {
	const [y, m] = month.split("-").map(Number);
	return `${monthNames[m - 1] ?? month} ${y}`;
}

/** Zeilen der Auswertung (Monate je Fahrzeug, gruppiert nach Fahrzeug, neueste zuerst). */
export function statisticRows(stats: FuelVehicleStatistics[]): Array<FuelMonthStatistics & { vehicle: string; monthText: string }> {
	return stats.flatMap((s) => [...s.months].reverse().map((m) => ({
		...m, vehicle: s.identifier ? `${s.vehicleName} (${s.identifier})` : s.vehicleName, monthText: monthText(m.month),
	})));
}

/** Kurztext der Hinweise für die Liste („2 Hinweise“), ohne den reinen Foto-Hinweis zu überbetonen. */
export function warningSummary(warnings: FuelWarning[]): { text: string; tone: Tone } {
	const relevant = warnings.filter((w) => w.code !== "missing_image");
	if (relevant.length === 0) return warnings.length ? { text: "Ohne Foto", tone: "Warning" } : { text: "", tone: "None" };
	return { text: relevant.length === 1 ? "1 Hinweis" : `${relevant.length} Hinweise`, tone: relevant.some((w) => w.code === "duplicate" || w.code === "odometer_backwards") ? "Error" : "Warning" };
}

function round2(value: number): number {
	return Math.round(value * 100) / 100;
}
