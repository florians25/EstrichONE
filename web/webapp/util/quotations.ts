/**
 * Artikel und Leistungen (Artikelabgleich mit Lexware Office), Angebote (Beta) und das Merkmal „Zur Abschlussprüfung“:
 * Typen der API und die Logik der Oberfläche (Summen, Texte, Zustände). Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */
import type { LexwareDeviationField } from "./lexware";

type State = "None" | "Success" | "Warning" | "Error" | "Information";

export type ArticleSync = "fromLexware" | "both" | "off";
export type CatalogKind = "product" | "service";

export interface CatalogItemSyncState { state: "local" | "synced" | "deviations" | "error" | "missing" | "archived" | "off" | string; syncedAt: string | null; deviations: LexwareDeviationField[]; error: string | null }
export interface CatalogItem {
	id: string; title: string; description: string | null; kind: CatalogKind; articleNumber: string | null; gtin: string | null; unit: string | null;
	netPrice: number; grossPrice: number | null; taxRate: number; materialId: string | null; materialName: string | null; isActive: boolean;
	origin: "estrichOne" | "lexware" | string; lexwareId: string | null; lexwareUrl: string | null; sync: CatalogItemSyncState; version: number;
}
export interface MaterialSummary { id: string; name: string; number: string | null; unit: string; isActive: boolean }
export interface CatalogList {
	items: CatalogItem[]; syncEnabled: boolean; direction: ArticleSync; canPush: boolean; lastSyncAt: string | null; lastSyncError: string | null; materials: MaterialSummary[];
}
export interface LexwareArticleSummary {
	id: string; title: string; description: string | null; type: string; articleNumber: string | null; unitName: string | null; netPrice: number | null;
	taxRate: number | null; archived: boolean; url: string;
}
export interface ArticleReconciliation {
	loadedAt: string;
	onlyLexware: { article: LexwareArticleSummary; candidates: { item: CatalogItem; reasons: string[] }[] }[];
	onlyEstrichOne: { item: CatalogItem; candidates: LexwareArticleSummary[] }[];
}
export interface ArticleSyncResult { imported: number; created: number; updated: number; deviations: number; duplicates: number; at: string }

export const articleSyncOptions: { key: ArticleSync; text: string }[] = [
	{ key: "fromLexware", text: "Aus Lexware Office nach EstrichONE (Vorgabe)" },
	{ key: "both", text: "Beide Richtungen" },
	{ key: "off", text: "Aus" },
];

export function articleSyncHelp(direction: string | null | undefined): string {
	switch (direction) {
		case "both": return "Artikel aus Lexware Office werden übernommen und Änderungen nachgezogen. Neue und geänderte Einträge aus EstrichONE werden in Lexware Office angelegt bzw. geändert. Gelöscht wird nie.";
		case "off": return "Es findet kein Artikelabgleich statt. „Artikel und Leistungen“ bleibt in EstrichONE nutzbar.";
		default: return "Artikel aus Lexware Office werden übernommen und Änderungen nachgezogen. In Lexware Office schreibt EstrichONE nichts; eigene Änderungen bleiben als Abweichung sichtbar.";
	}
}

/** Abgleichstand eines Eintrags als Anzeige. */
export function catalogSyncText(sync: CatalogItemSyncState | null | undefined): { text: string; state: State; icon: string } {
	switch (sync?.state) {
		case "synced": return { text: "Abgeglichen", state: "Success", icon: "sap-icon://sys-enter-2" };
		case "deviations": return { text: `Abweichungen (${sync.deviations.length})`, state: "Warning", icon: "sap-icon://compare" };
		case "error": return { text: "Fehler beim Abgleich", state: "Error", icon: "sap-icon://error" };
		case "missing": return { text: "In Lexware Office nicht mehr vorhanden", state: "Warning", icon: "sap-icon://warning" };
		case "archived": return { text: "In Lexware Office archiviert", state: "Warning", icon: "sap-icon://warning" };
		case "local": return { text: "Nur in EstrichONE", state: "None", icon: "sap-icon://product" };
		default: return { text: "Kein Abgleich", state: "None", icon: "" };
	}
}

export const kindText = (kind: string) => (kind === "product" ? "Material" : "Leistung");

export function syncResultText(r: ArticleSyncResult): string {
	const parts = [
		r.imported ? `${r.imported} übernommen` : "", r.created ? `${r.created} in Lexware Office angelegt` : "", r.updated ? `${r.updated} aktualisiert` : "",
		r.duplicates ? `${r.duplicates} mit möglichem Treffer (bitte entscheiden)` : "", r.deviations ? `${r.deviations} mit Abweichungen` : "",
	].filter(Boolean);
	return parts.length ? `Artikelabgleich abgeschlossen: ${parts.join(", ")}.` : "Artikelabgleich abgeschlossen. Es gab keine Änderungen.";
}

// ---------- Angebote ----------

export type QuotationState = "draft" | "sending" | "sent" | "failed";
export interface QuotationLine {
	kind: "item" | "text"; name: string; description?: string | null; catalogItemId?: string | null; quantity?: number | null; unit?: string | null;
	netPrice?: number | null; taxRate?: number | null; discountPercent?: number | null; lineNet?: number | null;
}
export interface QuotationTaxLine { rate: number; net: number; tax: number }
export interface QuotationTotals { net: number; tax: number; gross: number; taxLines: QuotationTaxLine[] }
export interface Quotation {
	id: string; contactId: string; customerName: string | null; customerLinked: boolean; bauakteId: string | null; bauakteNumber: string | null; bauakteTitle: string | null;
	title: string; introduction: string | null; remark: string | null; validUntil: string | null; taxType: TaxType;
	paymentConditionId: string | null; paymentLabel: string | null; paymentDuration: number | null; paymentDiscountPercent: number | null; paymentDiscountRange: number | null;
	state: QuotationState; editable: boolean; lexwareId: string | null; lexwareUrl: string | null; sentAt: string | null; sendError: string | null;
	lines: QuotationLine[]; totals: QuotationTotals; problems: string[]; version: number; updatedAt: string;
	sendErrorTechnical?: string | null; sendErrorFix?: string | null;
}
export interface QuotationSummary {
	id: string; title: string; contactId: string; customerName: string | null; bauakteId: string | null; state: QuotationState; net: number; gross: number;
	validUntil: string | null; lexwareUrl: string | null; updatedAt: string;
}
export interface PaymentCondition { id: string | null; label: string; duration: number | null; discountPercent: number | null; discountRange: number | null; isDefault: boolean }
export interface QuotationCatalogItem { id: string; title: string; description: string | null; kind: CatalogKind; articleNumber: string | null; unit: string | null; netPrice: number; taxRate: number; inLexware: boolean }
export interface TaxTypeOption { key: TaxType; text: string; allowed: boolean; hint: string | null }
export interface QuotationOptions {
	paymentConditions: PaymentCondition[]; paymentConditionsError: string | null; catalog: QuotationCatalogItem[]; taxRates: number[]; defaultValidDays: number;
	taxTypes?: TaxTypeOption[] | null; smallBusiness?: boolean;
}
export interface QuotationSuggestion { source: string; hint: string; name: string; description: string | null; quantity: number; unit: string; catalogItemId: string | null; netPrice: number | null; taxRate: number | null }
export interface QuotationSuggestions { lines: QuotationSuggestion[]; hints: string[] }

export const BETA_TEXT = "Beta: Angebote werden in EstrichONE vorbereitet und als Entwurf an Lexware Office gesendet. Nummer, Festschreiben und Versand erfolgen in Lexware Office.";

export type TaxType = "net" | "constructionService13b" | "vatfree" | "gross";

export const taxTypeOptions: TaxTypeOption[] = [
	{ key: "net", text: "Netto (Umsatzsteuer wird ausgewiesen)", allowed: true, hint: null },
	{ key: "constructionService13b", text: "Bauleistung nach § 13b UStG (Steuerschuld beim Leistungsempfänger)", allowed: true, hint: null },
	{ key: "vatfree", text: "Steuerfrei (zum Beispiel Kleinunternehmer nach § 19 UStG)", allowed: true, hint: null },
];

/** Steuerarten ohne ausgewiesene Umsatzsteuer: Jede Position trägt 0 Prozent. */
export const withoutTax = (taxType: string | null | undefined) => taxType === "constructionService13b" || taxType === "vatfree";

/** Bekannte Behebungen von Fehlern aus Lexware Office: Text der Schaltfläche und die Steuerart, die übernommen wird. */
export function fixAction(fix: string | null | undefined): { text: string; taxType: TaxType } | null {
	switch (fix) {
		case "taxTypeNet": return { text: "Steuerart Netto übernehmen und erneut senden", taxType: "net" };
		case "taxTypeVatfree": return { text: "Steuerart Steuerfrei übernehmen und erneut senden", taxType: "vatfree" };
		default: return null;
	}
}

/** Hinweis unter der Steuerart: warum die gewählte oder eine andere Steuerart nicht angeboten wird. */
export function taxTypeHint(options: TaxTypeOption[], selected: string): string {
	const current = options.find((o) => o.key === selected);
	if (current && !current.allowed) return current.hint ?? "";
	return options.filter((o) => !o.allowed && o.hint).map((o) => `${o.text.replace(/ \(.*\)$/, "")}: ${o.hint}`)[0] ?? "";
}

const round2 = (value: number) => Math.round((value + Number.EPSILON) * 100) / 100;

/** Positionsbetrag netto: Menge mal Einzelpreis abzüglich Rabatt (wie der Server). */
export function lineNet(line: Pick<QuotationLine, "quantity" | "netPrice" | "discountPercent">): number {
	const discount = Math.min(Math.max(Number(line.discountPercent ?? 0), 0), 100);
	return round2(Number(line.quantity ?? 0) * Number(line.netPrice ?? 0) * (1 - discount / 100));
}

/** Summen live: Steuer je Steuersatz auf die Summe der Positionen; bei § 13b keine Steuer. */
export function quotationTotals(lines: QuotationLine[], taxType: string): QuotationTotals {
	const groups = new Map<number, number>();
	for (const line of lines.filter((l) => l.kind === "item")) {
		const rate = withoutTax(taxType) ? 0 : Number(line.taxRate ?? 19);
		groups.set(rate, round2((groups.get(rate) ?? 0) + lineNet(line)));
	}
	const taxLines = [...groups.entries()].sort((a, b) => b[0] - a[0]).map(([rate, net]) => ({ rate, net, tax: round2(net * rate / 100) }));
	const net = round2(taxLines.reduce((s, t) => s + t.net, 0));
	const tax = round2(taxLines.reduce((s, t) => s + t.tax, 0));
	return { net, tax, gross: round2(net + tax), taxLines };
}

export function quotationStateText(state: string): { text: string; state: State } {
	switch (state) {
		case "sent": return { text: "An Lexware Office gesendet", state: "Success" };
		case "sending": return { text: "Wird gesendet", state: "Information" };
		case "failed": return { text: "Senden fehlgeschlagen", state: "Error" };
		default: return { text: "Entwurf in EstrichONE", state: "None" };
	}
}

/** Position aus einem Katalogeintrag. */
export function lineFromCatalog(item: QuotationCatalogItem, quantity = 1): QuotationLine {
	return { kind: "item", name: item.title, description: item.description, catalogItemId: item.id, quantity, unit: item.unit ?? "Stück", netPrice: item.netPrice, taxRate: item.taxRate, discountPercent: null };
}

export function lineFromSuggestion(s: QuotationSuggestion): QuotationLine {
	return { kind: "item", name: s.name, description: s.description, catalogItemId: s.catalogItemId, quantity: s.quantity, unit: s.unit, netPrice: s.netPrice ?? 0, taxRate: s.taxRate ?? 19, discountPercent: null };
}

/** Problemcode „Kunde nicht verknüpft“ erkannt? */
export const NOT_LINKED_CODE = "LEXWARE_CUSTOMER_NOT_LINKED";

// ---------- Zur Abschlussprüfung ----------

export const REVIEW_BADGE = "Zur Abschlussprüfung";

export function reviewHint(review: { reviewRequestedAt?: string | null; reviewReason?: string | null } | null | undefined): string {
	if (!review?.reviewRequestedAt) return "";
	return review.reviewReason || "Bitte prüfen und abschließen.";
}

export const statusRuleTexts = {
	quotation: "Angebot in Lexware Office angenommen: Eine Bauakte im Status „Anfrage“ wird auf „Geplant“ gesetzt. Andere Status bleiben unverändert.",
	invoices: "Alle zugeordneten Rechnungen bezahlt (mindestens eine Rechnung, keine offen oder überfällig, keine offene Gutschrift): Die Bauakte wird zur Abschlussprüfung gestellt. Abschließen bleibt eine bewusste Handlung.",
};

// ---------- Artikel und Leistungen: Liste, Preise ----------

/** Brutto aus netto und Steuersatz (auf Cent). */
export const grossOf = (net: number, rate: number) => Math.round(Number((net * (1 + rate / 100)).toFixed(6)) * 100) / 100;

/** Netto aus brutto und Steuersatz (vier Nachkommastellen, wie der Preis gespeichert wird). */
export const netOf = (gross: number, rate: number) => Math.round(gross / (1 + rate / 100) * 10000) / 10000;

export type PriceRounding = "cent" | "tenCent" | "euro";

/** Preis netto nach einer Änderung um Prozent, gerundet auf Cent, 10 Cent oder ganze Euro. */
export function changedPrice(net: number, percent: number, rounding: PriceRounding = "cent"): number {
	const raw = net * (1 + percent / 100);
	const step = rounding === "euro" ? 1 : rounding === "tenCent" ? 0.1 : 0.01;
	return Math.max(0, Math.round(Math.round(Number((raw / step).toFixed(6))) * step * 100) / 100);
}

export interface ArticleFilter { query?: string; kind?: string; unit?: string; sync?: string; active?: "all" | "active" | "inactive" }

export function filterArticles(items: CatalogItem[], f: ArticleFilter): CatalogItem[] {
	const q = (f.query ?? "").trim().toLowerCase();
	return items.filter((i) => (!f.kind || i.kind === f.kind) && (!f.unit || i.unit === f.unit) && (!f.sync || i.sync.state === f.sync)
		&& (f.active === "all" || (f.active === "inactive" ? !i.isActive : i.isActive))
		&& (!q || [i.title, i.articleNumber, i.description].some((v) => v?.toLowerCase().includes(q))));
}

export type ArticleSortKey = "articleNumber" | "title" | "kind" | "unit" | "netPrice" | "taxRate" | "grossPrice" | "sync";

export function sortArticles(items: CatalogItem[], key: ArticleSortKey, descending = false): CatalogItem[] {
	const value = (i: CatalogItem): string | number => {
		switch (key) {
			case "netPrice": return i.netPrice;
			case "taxRate": return i.taxRate;
			case "grossPrice": return grossOf(i.netPrice, i.taxRate);
			case "sync": return catalogSyncText(i.sync).text;
			case "kind": return kindText(i.kind);
			default: return (i[key] ?? "") as string;
		}
	};
	const sorted = [...items].sort((a, b) => {
		const x = value(a);
		const y = value(b);
		return typeof x === "number" && typeof y === "number" ? x - y : String(x).localeCompare(String(y), "de", { numeric: true });
	});
	return descending ? sorted.reverse() : sorted;
}
