/**
 * Modul „Eingangsbelege“: Typen der API und die Logik der Übersicht, des Belegeingangs und der Kosten der Bauakte
 * (Status, Filter, Summen als Textzeile, Prüfung der Datei vor dem Hochladen, Darstellung der E-Rechnung). Ohne
 * UI5-Abhängigkeit (mit Vitest geprüft).
 */
import { money } from "./format";

type State = "None" | "Success" | "Warning" | "Error" | "Information";

export type PurchaseType = "purchaseinvoice" | "purchasecreditnote";

export interface PurchaseItem {
	lexwareId: string; type: PurchaseType; typeName: string; number: string | null; voucherDate: string | null; dueDate: string | null; gross: number | null;
	openAmount: number | null; status: string; contactName: string | null; lexwareContactId: string | null; supplierId: string | null; supplierName: string | null;
	supplierLinkedByHand: boolean; bauakteId: string | null; bauakteNumber: string | null; bauakteTitle: string | null; lexwareUrl: string | null;
}
export interface PurchaseDocumentItem {
	id: string; fileName: string; contentType: string; sizeBytes: number; format: string; formatName: string; supplierName: string | null; invoiceNumber: string | null;
	invoiceDate: string | null; grossAmount: number | null; bauakteId: string | null; bauakteNumber: string | null; bauakteTitle: string | null;
	state: "uploaded" | "sending" | "sent" | "failed"; sendError: string | null; uploadedAt: string; uploadedBy: string | null; sentAt: string | null;
	problemCount: number; source: string;
}
export interface EInvoiceLine { name: string; quantity: number | null; unit: string | null; netPrice: number | null; lineNet: number | null; taxRate: number | null }
export interface EInvoiceData {
	format: string; creditNote: boolean; number: string | null; issueDate: string | null; dueDate: string | null; sellerName: string | null; sellerVatId: string | null;
	sellerStreet: string | null; sellerPostalCode: string | null; sellerCity: string | null; buyerName: string | null; buyerReference: string | null; currency: string;
	netTotal: number | null; taxTotal: number | null; grossTotal: number | null; duePayable: number | null; lines: EInvoiceLine[]; problems: string[];
}
export interface PurchaseDocument { document: PurchaseDocumentItem; note: string | null; data: EInvoiceData; canEdit: boolean; duplicate?: boolean }
export interface SupplierSummary { id: string; name: string; number: string | null }
export interface PurchaseList {
	items: PurchaseItem[]; documents: PurchaseDocumentItem[]; suppliers: SupplierSummary[]; canEdit: boolean; loadedAt: string; lexwareError: string | null;
}
export interface BauakteCosts {
	bauakteId: string; revenue: number; costs: number; difference: number; text: string; vouchers: PurchaseItem[]; documents: PurchaseDocumentItem[]; canEdit: boolean;
}

export const maxUploadBytes = 5 * 1024 * 1024;
const allowedExtensions = ["pdf", "jpg", "jpeg", "png", "xml"];

/** Status eines Eingangsbelegs aus Lexware Office. Unbekannte Werte neutral. */
export function purchaseStatus(status: string): { text: string; state: State } {
	switch (status) {
		case "open": return { text: "Offen", state: "Information" };
		case "overdue": return { text: "Überfällig", state: "Error" };
		case "paid": case "paidoff": return { text: "Bezahlt", state: "Success" };
		case "voided": return { text: "Storniert", state: "None" };
		case "draft": return { text: "Entwurf", state: "None" };
		case "unchecked": return { text: "Zu prüfen", state: "Warning" };
		default: return { text: status ? status.charAt(0).toUpperCase() + status.slice(1) : "Unbekannt", state: "None" };
	}
}

/** Stand eines hochgeladenen Belegs. */
export function documentState(d: Pick<PurchaseDocumentItem, "state" | "problemCount" | "format">): { text: string; state: State } {
	switch (d.state) {
		case "sent": return { text: "An Lexware Office übergeben", state: "Success" };
		case "sending": return { text: "Wird übergeben", state: "Information" };
		case "failed": return { text: "Übergabe fehlgeschlagen", state: "Error" };
		default: return d.problemCount > 0 ? { text: "Hochgeladen, Angaben prüfen", state: "Warning" } : { text: "Hochgeladen", state: "None" };
	}
}

/** Prüfung vor dem Hochladen (der Server prüft den Inhalt noch einmal). Liefert die Meldung oder null. */
export function uploadProblem(file: { name: string; size: number }): string | null {
	const extension = file.name.includes(".") ? file.name.split(".").pop()!.toLowerCase() : "";
	if (!allowedExtensions.includes(extension)) return `„${file.name}“: Laden Sie den Beleg als PDF, Foto (JPEG oder PNG) oder XRechnung (XML) hoch.`;
	if (file.size === 0) return `„${file.name}“ ist leer.`;
	if (file.size > maxUploadBytes) return `„${file.name}“ ist größer als 5 MB. Verkleinern Sie das Foto oder das PDF.`;
	return null;
}

export interface PurchaseFilter { tab: "lexware" | "inbox"; query: string; supplier: string; bauakte: string; status: string }

const matches = (query: string, ...values: (string | null | undefined)[]) => {
	const q = query.trim().toLowerCase();
	return !q || values.some((v) => v?.toLowerCase().includes(q));
};

export function filterPurchases(items: PurchaseItem[], filter: PurchaseFilter): PurchaseItem[] {
	return items.filter((i) => matches(filter.query, i.number, i.contactName, i.supplierName, i.bauakteNumber, i.bauakteTitle))
		.filter((i) => !filter.supplier || (i.lexwareContactId ?? i.contactName) === filter.supplier)
		.filter((i) => !filter.bauakte || (filter.bauakte === "none" ? !i.bauakteId : i.bauakteId === filter.bauakte))
		.filter((i) => filter.status === "all" || !filter.status || (filter.status === "open" ? ["open", "overdue"].includes(i.status) : i.status === filter.status));
}

export function filterDocuments(items: PurchaseDocumentItem[], filter: PurchaseFilter): PurchaseDocumentItem[] {
	return items.filter((d) => matches(filter.query, d.fileName, d.supplierName, d.invoiceNumber, d.bauakteNumber, d.bauakteTitle))
		.filter((d) => !filter.bauakte || (filter.bauakte === "none" ? !d.bauakteId : d.bauakteId === filter.bauakte));
}

/** Summen der Auswahl als Textzeile: Gutschriften zählen negativ, stornierte nicht; offen nur bei offenen und überfälligen Belegen. */
export function purchaseSummary(items: PurchaseItem[]): string {
	if (!items.length) return "";
	const counted = items.filter((i) => i.status !== "voided");
	const sign = (i: PurchaseItem) => (i.type === "purchasecreditnote" ? -1 : 1);
	const gross = counted.reduce((s, i) => s + sign(i) * (i.gross ?? 0), 0);
	const open = counted.filter((i) => ["open", "overdue"].includes(i.status)).reduce((s, i) => s + sign(i) * (i.openAmount ?? 0), 0);
	const unassigned = items.filter((i) => !i.bauakteId).length;
	return `${items.length} ${items.length === 1 ? "Beleg" : "Belege"}, brutto ${money(gross)}, davon offen ${money(open)}`
		+ (unassigned ? `, ${unassigned} ohne Bauakte.` : ".");
}

/** Lieferant als Text: Name aus Lexware Office, dazu der verknüpfte Lieferant des Materialstamms, wenn er anders heißt. */
export function supplierText(i: Pick<PurchaseItem, "contactName" | "supplierName">): string {
	const name = i.contactName ?? "Ohne Lieferant";
	return i.supplierName && i.supplierName !== name ? `${name} (Lieferant ${i.supplierName})` : name;
}

/** Positionen der E-Rechnung für die Tabelle. */
export function lineRows(data: EInvoiceData): { name: string; quantity: string; price: string; net: string; tax: string }[] {
	return data.lines.map((l) => ({
		name: l.name, quantity: l.quantity === null ? "" : `${l.quantity.toLocaleString("de-DE")} ${unitName(l.unit)}`.trim(),
		price: l.netPrice === null ? "" : money(l.netPrice), net: l.lineNet === null ? "" : money(l.lineNet), tax: l.taxRate === null ? "" : `${l.taxRate.toLocaleString("de-DE")} %`,
	}));
}

/** Einheiten der E-Rechnung (UN/ECE Recommendation 20) als Text; unbekannte Codes bleiben stehen. */
export function unitName(code: string | null): string {
	const known: Record<string, string> = {
		C62: "Stück", H87: "Stück", XPP: "Stück", MTK: "m²", MTR: "m", MTQ: "m³", TNE: "t", KGM: "kg", HUR: "Std.", DAY: "Tage", LS: "pauschal", LTR: "l",
		XBG: "Sack", XPA: "Paket", XPX: "Palette", SET: "Satz",
	};
	return code ? known[code] ?? code : "";
}

/** Kopfdaten der E-Rechnung als Zeilen (Bezeichnung, Wert), leere Angaben entfallen. */
export function headerFacts(data: EInvoiceData): { label: string; value: string }[] {
	const seller = [data.sellerStreet, [data.sellerPostalCode, data.sellerCity].filter(Boolean).join(" ")].filter(Boolean).join(", ");
	const facts: [string, string | null][] = [
		["Rechnungssteller", data.sellerName], ["Anschrift", seller || null], ["USt-IdNr.", data.sellerVatId], ["Rechnungsempfänger", data.buyerName],
		["Leitweg-ID bzw. Käuferreferenz", data.buyerReference], ["Rechnungsnummer", data.number],
		["Rechnungsdatum", data.issueDate ? new Date(`${data.issueDate}T00:00:00`).toLocaleDateString("de-DE") : null],
		["Fällig am", data.dueDate ? new Date(`${data.dueDate}T00:00:00`).toLocaleDateString("de-DE") : null],
	];
	return facts.filter(([, v]) => v).map(([label, value]) => ({ label, value: value! }));
}

/** Summen der E-Rechnung als Textzeile. */
export function totalsText(data: EInvoiceData): string {
	const parts: string[] = [];
	if (data.netTotal !== null) parts.push(`Netto ${money(data.netTotal)}`);
	if (data.taxTotal !== null) parts.push(`Umsatzsteuer ${money(data.taxTotal)}`);
	if (data.grossTotal !== null) parts.push(`Brutto ${money(data.grossTotal)}`);
	if (data.duePayable !== null && data.duePayable !== data.grossTotal) parts.push(`Zahlbetrag ${money(data.duePayable)}`);
	return parts.join(", ");
}
