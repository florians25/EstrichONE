/**
 * Modul „Verkauf“: Typen der API und die Logik des Belegeditors und der Übersicht (Summen, Zwischensummen, Abzüge,
 * Status, Textzeilen). Ohne UI5-Abhängigkeit (mit Vitest geprüft). Maßgeblich rechnet Lexware Office; die Summen hier
 * sind die Vorschau und folgen derselben Regel wie der Server (SalesRules).
 */
import type { PaymentCondition, TaxType, TaxTypeOption } from "./quotations";

type State = "None" | "Success" | "Warning" | "Error" | "Information";

export type SalesType = "quotation" | "orderconfirmation" | "invoice" | "creditnote";
export type LineKind = "item" | "text" | "subtotal" | "deduction";

export interface SalesLine {
	kind: LineKind; name: string; description?: string | null; catalogItemId?: string | null; quantity?: number | null; unit?: string | null;
	netPrice?: number | null; taxRate?: number | null; discountPercent?: number | null; lineNet?: number | null; deductionVoucherId?: string | null;
}
export interface SalesTaxLine { rate: number; net: number; tax: number }
export interface SalesTotals { net: number; tax: number; gross: number; taxLines: SalesTaxLine[] }
export interface SalesChainItem {
	id: string | null; lexwareId: string | null; type: SalesType; typeName: string; number: string | null; date: string | null; status: string; gross: number | null;
	current: boolean; lexwareUrl: string | null;
}
export interface SalesVoucher {
	id: string; type: SalesType; typeName: string; purpose: "standard" | "downPayment" | "final"; contactId: string; customerName: string | null;
	customerNumber: string | null; customerAddress: string | null; customerLinked: boolean; bauakteId: string | null; bauakteNumber: string | null;
	bauakteTitle: string | null; title: string; introduction: string | null; remark: string | null; voucherDate: string | null; validUntil: string | null;
	serviceDate: string | null; serviceEndDate: string | null; taxType: TaxType; paymentConditionId: string | null; paymentLabel: string | null;
	paymentDuration: number | null; paymentDiscountPercent: number | null; paymentDiscountRange: number | null; state: "draft" | "sending" | "sent" | "failed";
	status: string; editable: boolean; canEdit: boolean; canFinalize: boolean; lexwareId: string | null; lexwareUrl: string | null; lexwareNumber: string | null;
	finalized: boolean; sentAt: string | null; sendError: string | null; sendErrorTechnical: string | null; sendErrorFix: string | null; precedingId: string | null;
	downPaymentPercent: number | null; openAmount: number | null; paymentStatus: string | null; lines: SalesLine[]; totals: SalesTotals; problems: string[];
	chain: SalesChainItem[]; followers: SalesType[]; version: number; updatedAt: string;
}
export interface SalesListItem {
	id: string | null; lexwareId: string | null; type: SalesType; typeName: string; number: string | null; title: string | null; contactId: string | null;
	customerName: string | null; bauakteId: string | null; bauakteNumber: string | null; bauakteTitle: string | null; voucherDate: string | null; dueDate: string | null;
	net: number | null; gross: number | null; openAmount: number | null; status: string; lexwareUrl: string | null;
}
export interface SalesList { items: SalesListItem[]; loadedAt: string; lexwareError: string | null }
export interface SalesCatalogItem { id: string; title: string; description: string | null; kind: "product" | "service"; articleNumber: string | null; unit: string | null; netPrice: number; taxRate: number; inLexware: boolean }
export interface SalesTypeAccess { type: SalesType; name: string; enabled: boolean; canRead: boolean; canEdit: boolean; canFinalize: boolean }
export interface SalesOptions {
	paymentConditions: PaymentCondition[]; paymentConditionsError: string | null; catalog: SalesCatalogItem[]; taxRates: number[]; defaultValidDays: number;
	taxTypes: TaxTypeOption[]; smallBusiness: boolean; types: SalesTypeAccess[]; finalizeModule: boolean; paymentsModule: boolean;
}
export interface SalesSuggestion { source: string; hint: string; name: string; description: string | null; quantity: number; unit: string; catalogItemId: string | null; netPrice: number | null; taxRate: number | null; selected: boolean }
export interface SalesSuggestions { lines: SalesSuggestion[]; hints: string[]; serviceDate: string | null; serviceEndDate: string | null }
export interface SalesPlan {
	bauakteId: string; customerLinked: boolean; customerId: string | null; customerName: string | null; orderNet: number | null; orderGross: number | null;
	orderSource: string | null; orderVoucherId: string | null; downPaymentsNet: number; downPaymentCount: number; invoicedNet: number; paidGross: number;
	openGross: number; notBilledNet: number | null; progressPercent: number | null; suggestedPercent: number | null; serviceDate: string | null;
	serviceEndDate: string | null; finalInvoiceExists: boolean; text: string; vouchers: SalesListItem[]; hints: string[];
}

export const salesTypes: { key: SalesType; text: string; plural: string; module: string }[] = [
	{ key: "quotation", text: "Angebot", plural: "Angebote", module: "quotations" },
	{ key: "orderconfirmation", text: "Auftragsbestätigung", plural: "Auftragsbestätigungen", module: "salesOrderConfirmations" },
	{ key: "invoice", text: "Rechnung", plural: "Rechnungen", module: "salesInvoices" },
	{ key: "creditnote", text: "Gutschrift", plural: "Gutschriften", module: "salesCreditNotes" },
];

export const typeName = (type: string) => salesTypes.find((t) => t.key === type)?.text ?? "Beleg";

const round2 = (value: number) => Math.round((value + Number.EPSILON) * 100) / 100;

/** Positionsbetrag netto; Abzüge zählen mit Menge 1 und ohne Rabatt (negativer Preis). */
export function lineNet(line: Pick<SalesLine, "kind" | "quantity" | "netPrice" | "discountPercent">): number {
	if (line.kind === "deduction") return round2(Number(line.netPrice ?? 0));
	if (line.kind !== "item") return 0;
	const discount = Math.min(Math.max(Number(line.discountPercent ?? 0), 0), 100);
	return round2(Number(line.quantity ?? 0) * Number(line.netPrice ?? 0) * (1 - discount / 100));
}

/** Zwischensumme netto der Positionen seit der letzten Zwischensumme bis vor <index>. */
export function subtotal(lines: SalesLine[], index: number): number {
	let sum = 0;
	for (let i = index - 1; i >= 0 && lines[i].kind !== "subtotal"; i--) sum += lineNet(lines[i]);
	return round2(sum);
}

export const withoutTax = (taxType: string | null | undefined) => taxType === "constructionService13b" || taxType === "vatfree";

/** Summen: Steuer je Steuersatz auf die Summe der Positionen (Abzüge eingeschlossen); bei § 13b und steuerfrei keine Steuer. */
export function totals(lines: SalesLine[], taxType: string): SalesTotals {
	const groups = new Map<number, number>();
	for (const line of lines.filter((l) => l.kind === "item" || l.kind === "deduction")) {
		const rate = withoutTax(taxType) ? 0 : Number(line.taxRate ?? 19);
		groups.set(rate, round2((groups.get(rate) ?? 0) + lineNet(line)));
	}
	const taxLines = [...groups.entries()].sort((a, b) => b[0] - a[0]).map(([rate, net]) => ({ rate, net, tax: round2(net * rate / 100) }));
	const net = round2(taxLines.reduce((s, t) => s + t.net, 0));
	const tax = round2(taxLines.reduce((s, t) => s + t.tax, 0));
	return { net, tax, gross: round2(net + tax), taxLines };
}

/** Status als Text und Farbe, wie in Lexware Office; „Erstellt“ bei Auftragsbestätigungen ohne Zahlungsbedeutung. */
export function statusText(status: string, type?: string): { text: string; state: State } {
	if (type === "orderconfirmation" && (status === "open" || status === "paid" || status === "paidoff" || status === "overdue")) return { text: "Erstellt", state: "None" };
	switch (status) {
		case "local": return { text: "In EstrichONE", state: "None" };
		case "failed": return { text: "Senden fehlgeschlagen", state: "Error" };
		case "sending": return { text: "Wird gesendet", state: "Information" };
		case "draft": return { text: "Entwurf", state: "None" };
		case "open": return { text: "Offen", state: "Information" };
		case "overdue": return { text: "Überfällig", state: "Error" };
		case "paid": case "paidoff": return { text: "Bezahlt", state: "Success" };
		case "accepted": return { text: "Angenommen", state: "Success" };
		case "rejected": return { text: "Abgelehnt", state: "Warning" };
		case "voided": return { text: "Storniert", state: "Warning" };
		default: return { text: "Unbekannt", state: "None" };
	}
}

/** Reiter der Übersicht: Status je Belegart wie in Lexware Office. */
export const statusTabs: { key: string; text: string; statuses: string[] | null }[] = [
	{ key: "all", text: "Alle", statuses: null },
	{ key: "draft", text: "Entwurf", statuses: ["local", "failed", "sending", "draft"] },
	{ key: "open", text: "Offen", statuses: ["open", "overdue"] },
	{ key: "overdue", text: "Überfällig", statuses: ["overdue"] },
	{ key: "paid", text: "Bezahlt", statuses: ["paid", "paidoff"] },
	{ key: "accepted", text: "Angenommen", statuses: ["accepted"] },
	{ key: "rejected", text: "Abgelehnt", statuses: ["rejected"] },
];

export interface SalesFilter { type?: string; status?: string; query?: string; customer?: string; bauakte?: string }

export function filterItems(items: SalesListItem[], f: SalesFilter): SalesListItem[] {
	const tab = statusTabs.find((t) => t.key === (f.status ?? "all"));
	const q = (f.query ?? "").trim().toLowerCase();
	return items.filter((i) => (!f.type || f.type === "all" || i.type === f.type)
		&& (!tab?.statuses || tab.statuses.includes(i.status))
		&& (!f.customer || i.contactId === f.customer || i.customerName === f.customer)
		&& (!f.bauakte || i.bauakteId === f.bauakte)
		&& (!q || [i.number, i.title, i.customerName, i.bauakteNumber, i.bauakteTitle, i.typeName].some((v) => (v ?? "").toLowerCase().includes(q))));
}

const euro = (v: number) => v.toLocaleString("de-DE", { style: "currency", currency: "EUR" });

/** Summen der Auswahl als Textzeile (keine Kacheln): Anzahl, netto, brutto, offen. */
export function selectionText(items: SalesListItem[]): string {
	if (!items.length) return "Keine Belege in dieser Auswahl.";
	const net = items.reduce((s, i) => s + (i.net ?? 0), 0);
	const gross = items.reduce((s, i) => s + (i.gross ?? 0), 0);
	const open = items.reduce((s, i) => s + (i.openAmount ?? 0), 0);
	return `${items.length} ${items.length === 1 ? "Beleg" : "Belege"}, netto ${euro(net)}, brutto ${euro(gross)}${open > 0 ? `, davon offen ${euro(open)}` : ""}.`;
}

/** Nummer in der Anzeige: in Lexware Office vergeben, sonst „wird automatisch vergeben“. */
export const numberText = (number: string | null | undefined) => number || "wird automatisch vergeben";

/** Fortschritt des Rechnungsplans für den schlichten Balken: berechnet (Abschläge und Rechnungen) zur Auftragssumme. */
export function planProgress(plan: Pick<SalesPlan, "orderNet" | "invoicedNet" | "paidGross" | "orderGross">): { billed: number; paid: number } {
	if (!plan.orderNet || plan.orderNet <= 0) return { billed: 0, paid: 0 };
	const billed = Math.max(0, Math.min(100, Math.round(plan.invoicedNet / plan.orderNet * 100)));
	const paid = plan.orderGross ? Math.max(0, Math.min(100, Math.round(plan.paidGross / plan.orderGross * 100))) : 0;
	return { billed, paid };
}

/** Leistungszeitraum als Text. */
export function periodText(start: string | null, end: string | null, format: (iso: string) => string): string {
	if (!start) return "";
	return end && end !== start ? `${format(start)} bis ${format(end)}` : format(start);
}

/** Position aus einem Eintrag in „Artikel und Leistungen“. */
export function lineFromCatalog(item: SalesCatalogItem, quantity = 1): SalesLine {
	return { kind: "item", name: item.title, description: item.description, catalogItemId: item.id, quantity, unit: item.unit ?? "Stück", netPrice: item.netPrice, taxRate: item.taxRate };
}

export function lineFromSuggestion(s: SalesSuggestion): SalesLine {
	return { kind: "item", name: s.name, description: s.description, catalogItemId: s.catalogItemId, quantity: s.quantity, unit: s.unit, netPrice: s.netPrice ?? 0, taxRate: s.taxRate ?? 19 };
}

/** Vorschlagsliste der Eingabezeile: Artikel und Leistungen, deren Bezeichnung oder Nummer den Suchtext enthält. */
export function searchCatalog(catalog: SalesCatalogItem[], text: string, limit = 12): SalesCatalogItem[] {
	const q = text.trim().toLowerCase();
	if (!q) return catalog.slice(0, limit);
	return catalog.filter((c) => c.title.toLowerCase().includes(q) || (c.articleNumber ?? "").toLowerCase().includes(q)).slice(0, limit);
}

/** Titel einer neuen Belegart beim Umwandeln, z. B. „In Rechnung umwandeln“. */
export function convertText(type: SalesType): string {
	return type === "invoice" ? "In Rechnung umwandeln" : type === "orderconfirmation" ? "In Auftragsbestätigung umwandeln" : `In ${typeName(type)} umwandeln`;
}
