/**
 * Lexware Office: Typen der API und die Logik der Oberfläche (Art, Status und Summen der Belege, Texte der Dublettenprüfung
 * und der Abgleichübersicht). Rechnungen und Angebote entstehen ausschließlich in Lexware Office; EstrichONE zeigt sie nur an
 * und ordnet sie Bauakten zu. Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */

export interface LexwareStatus {
	enabled: boolean;
	configured: boolean;
	canRead: boolean;
	canAssign: boolean;
	canManage: boolean;
	/** Vorgabe für „Mit Lexware Office abgleichen“ bei neuen Kunden (Modul an und Einstellung gesetzt). */
	syncDefault?: boolean;
}

export interface LexwareSettings {
	moduleEnabled: boolean;
	hasApiKey: boolean;
	baseUrl: string;
	customBaseUrl: boolean;
	organizationName: string | null;
	lastTestAt: string | null;
	lastTestOk: boolean | null;
	lastTestError: string | null;
	linkedCustomers: number;
	linkedInvoices: number;
	keyHelpUrl: string;
	version: number;
	newCustomersSync: boolean;
}

export interface LexwareTestResponse { ok: boolean; organizationName: string | null; error: string | null; at: string }

export interface LexwareContactSummary {
	id: string;
	name: string;
	isCompany: boolean;
	customerNumber: string | null;
	street: string | null;
	postalCode: string | null;
	city: string | null;
	email: string | null;
	phone: string | null;
	url: string;
	linkedContactId: string | null;
	linkedContactName: string | null;
}

export interface LexwareCustomerSummary {
	id: string;
	name: string;
	isCompany: boolean;
	customerNumber: string | null;
	street: string | null;
	postalCode: string | null;
	city: string | null;
	email: string | null;
	phone: string | null;
	archived: boolean;
	linkedLexwareContactId: string | null;
	syncEnabled?: boolean;
}

export interface Match { score: number; reasons: string[]; strong: boolean }
export interface LexwareContactCandidate extends Match { contact: LexwareContactSummary }
export interface LexwareCustomerCandidate extends Match { customer: LexwareCustomerSummary }

export type LexwareLinkOrigin = "linked" | "createdInLexware" | "importedFromLexware";
export interface LexwareContactLink { lexwareContactId: string; name: string | null; number: string | null; url: string; origin: LexwareLinkOrigin; linkedAt: string }
export interface CustomerLexware { contactId: string; link: LexwareContactLink | null; canEdit: boolean; syncEnabled: boolean }
export interface LexwareCandidates { candidates: LexwareContactCandidate[]; loadedAt: string | null }
export interface CreateLexwareContactResponse { created: boolean; link: LexwareContactLink | null; candidates: LexwareContactCandidate[] }
export interface ImportLexwareContactResponse { imported: boolean; contactId: string | null; customerNumber: string | null; candidates: LexwareCustomerCandidate[] }
export interface LexwareContactList { items: LexwareContactSummary[]; page: number; pageSize: number; total: number; loadedAt: string }

export interface LexwareLinkedPair { customer: LexwareCustomerSummary; lexware: LexwareContactSummary | null; missingInLexware: boolean }
export interface LexwareSuggestion extends Match { customer: LexwareCustomerSummary; lexware: LexwareContactSummary; unique: boolean }
export interface LexwareInternalDuplicate {
	system: "estrichOne" | "lexware";
	firstId: string; firstName: string; firstNumber: string | null;
	secondId: string; secondName: string; secondNumber: string | null;
	score: number; reasons: string[];
}
export interface LexwareReconciliation {
	loadedAt: string;
	linked: number; onlyEstrichOne: number; onlyLexware: number; possibleDuplicates: number; uniqueMatches: number;
	linkedItems: LexwareLinkedPair[]; onlyEstrichOneItems: LexwareCustomerSummary[]; onlyLexwareItems: LexwareContactSummary[];
	suggestions: LexwareSuggestion[]; internalDuplicates: LexwareInternalDuplicate[]; truncated: boolean;
	/** Kunden, die bewusst nur in EstrichONE bleiben („Mit Lexware Office abgleichen“ aus). */
	excluded: number; excludedItems: LexwareCustomerSummary[];
}

export type VoucherType = "invoice" | "quotation" | "creditnote" | "orderconfirmation";
export type VoucherStatus = "draft" | "open" | "overdue" | "paid" | "paidoff" | "voided" | "accepted" | "rejected";

/** Beleg aus Lexware Office, der einer Bauakte zugeordnet ist. */
export interface LexwareVoucher {
	id: string; lexwareId: string; type: VoucherType | string; number: string | null; title: string | null; voucherDate: string | null; dueDate: string | null;
	netAmount: number | null; grossAmount: number | null; currency: string; status: VoucherStatus | string; contactName: string | null; url: string;
	syncedAt: string | null; syncError: string | null;
}
export interface LexwareBauakteCustomer { id: string; name: string; lexwareContactId: string | null; syncEnabled: boolean }
export interface BauakteLexware {
	bauakteId: string; configured: boolean; canAssign: boolean; customer: LexwareBauakteCustomer | null; vouchers: LexwareVoucher[]; syncError: string | null;
}
/** Beleg aus der Belegliste von Lexware Office (Kunde, Zuordnungsdialog). */
export interface LexwareVoucherItem {
	lexwareId: string; type: VoucherType | string; number: string | null; voucherDate: string | null; dueDate: string | null; grossAmount: number | null;
	currency: string; status: VoucherStatus | string; contactName: string | null; url: string; assignedBauakteId: string | null; assignedBauakte: string | null;
	suggested: boolean;
}
export interface CustomerVouchers { linked: boolean; vouchers: LexwareVoucherItem[]; loadedAt: string | null }
export interface BauakteVoucherCandidates { customerLinked: boolean; vouchers: LexwareVoucherItem[] }

type State = "None" | "Success" | "Warning" | "Error" | "Information";

const voucherTypes: Record<string, string> = { invoice: "Rechnung", quotation: "Angebot", creditnote: "Gutschrift", orderconfirmation: "Auftragsbestätigung" };

/** Belegart als Text. */
export function voucherTypeText(type: string | null | undefined): string {
	return voucherTypes[type ?? ""] ?? "Beleg";
}

/** Status eines Belegs als Text und Farbe: Entwurf, offen, überfällig, bezahlt, storniert; bei Angeboten auch angenommen und abgelehnt. */
export function voucherStatus(status: string | null | undefined): { text: string; state: State; icon: string } {
	switch (status) {
		case "draft": return { text: "Entwurf", state: "None", icon: "sap-icon://edit" };
		case "open": return { text: "Offen", state: "Information", icon: "sap-icon://pending" };
		case "overdue": return { text: "Überfällig", state: "Error", icon: "sap-icon://alert" };
		case "paid": case "paidoff": return { text: "Bezahlt", state: "Success", icon: "sap-icon://sys-enter-2" };
		case "accepted": return { text: "Angenommen", state: "Success", icon: "sap-icon://accept" };
		case "rejected": return { text: "Abgelehnt", state: "Error", icon: "sap-icon://decline" };
		case "voided": return { text: "Storniert", state: "Warning", icon: "sap-icon://cancel" };
		default: return { text: "Unbekannt", state: "None", icon: "sap-icon://question-mark" };
	}
}

/** Zweites Datum eines Belegs: bei Rechnungen die Fälligkeit, bei Angeboten „gültig bis“. */
export function dueLabel(type: string | null | undefined): string {
	return type === "quotation" ? "gültig bis" : "fällig";
}

/** Betrag in Euro (deutsche Schreibweise); leer, wenn Lexware noch keinen Betrag kennt. */
export function euro(value: number | null | undefined, currency = "EUR"): string {
	if (value === null || value === undefined || Number.isNaN(value)) return "";
	return value.toLocaleString("de-DE", { style: "currency", currency: currency || "EUR" });
}

/** Belegnummer; Entwürfe haben in Lexware noch keine. */
export function voucherNumber(v: { number: string | null; status: string }): string {
	return v.number || (v.status === "draft" ? "wird automatisch vergeben" : "ohne Nummer");
}

/**
 * Summen einer Belegliste als dezenter Text: Rechnungen offen, davon überfällig, bezahlt; Angebote nach Stand. Entwürfe und
 * stornierte Belege zählen nicht in die Beträge.
 */
export function voucherSummary(vouchers: { type: string; status: string; grossAmount: number | null }[]): string {
	const of = (type: string, statuses: string[]) => vouchers.filter((v) => v.type === type && statuses.includes(v.status));
	const sum = (list: { grossAmount: number | null }[]) => list.reduce((total, v) => total + (v.grossAmount ?? 0), 0);
	const parts: string[] = [];
	const invoices = vouchers.filter((v) => v.type === "invoice");
	if (invoices.length > 0) {
		const open = of("invoice", ["open", "overdue"]);
		const overdue = of("invoice", ["overdue"]);
		const paid = of("invoice", ["paid", "paidoff"]);
		const drafts = of("invoice", ["draft"]);
		const details = [
			open.length > 0 ? `offen ${euro(sum(open))}` : "", overdue.length > 0 ? `davon überfällig ${euro(sum(overdue))}` : "",
			paid.length > 0 ? `bezahlt ${euro(sum(paid))}` : "", drafts.length > 0 ? `${drafts.length} im Entwurf` : "",
		].filter(Boolean);
		parts.push(`${invoices.length} ${invoices.length === 1 ? "Rechnung" : "Rechnungen"}${details.length ? `: ${details.join(", ")}` : ""}`);
	}
	const quotations = vouchers.filter((v) => v.type === "quotation");
	if (quotations.length > 0) {
		const details = [
			of("quotation", ["open"]).length > 0 ? `${of("quotation", ["open"]).length} offen` : "",
			of("quotation", ["accepted"]).length > 0 ? `angenommen ${euro(sum(of("quotation", ["accepted"])))}` : "",
			of("quotation", ["rejected"]).length > 0 ? `${of("quotation", ["rejected"]).length} abgelehnt` : "",
		].filter(Boolean);
		parts.push(`${quotations.length} ${quotations.length === 1 ? "Angebot" : "Angebote"}${details.length ? `: ${details.join(", ")}` : ""}`);
	}
	const others = vouchers.length - invoices.length - quotations.length;
	if (others > 0) parts.push(`${others} ${others === 1 ? "weiterer Beleg" : "weitere Belege"}`);
	return parts.join(". ");
}

/** Hinweis im Zuordnungsdialog: schon einer Bauakte zugeordnet (dieser oder einer anderen). */
export function assignedText(item: Pick<LexwareVoucherItem, "assignedBauakteId" | "assignedBauakte">, bauakteId: string): string {
	if (!item.assignedBauakteId) return "";
	return item.assignedBauakteId === bauakteId ? "Dieser Bauakte bereits zugeordnet" : `Zugeordnet: Bauakte ${item.assignedBauakte ?? ""}`.trim();
}

/** Fehlertext einer fehlgeschlagenen Abfrage für die Anzeige in einer Tabelle. */
export function problemText(error: unknown): string {
	const problem = (error as { problem?: { detail?: string; title?: string } } | null)?.problem;
	return problem?.detail ?? problem?.title ?? (error instanceof Error ? error.message : "Die Belege konnten nicht geladen werden.");
}

/** Anschrift in einer Zeile. */
export function placeText(c: { street?: string | null; postalCode?: string | null; city?: string | null }): string {
	return [c.street, [c.postalCode, c.city].filter(Boolean).join(" ")].filter(Boolean).join(", ");
}

/** Übereinstimmung als Text und Farbe: eindeutiges Merkmal, wahrscheinlich, möglich; ohne Gründe nur Suchtreffer. */
export function matchLabel(match: Match): { text: string; state: State } {
	if (match.reasons.length === 0) return { text: "Suchtreffer", state: "None" };
	const reasons = match.reasons.join(", ");
	if (match.strong) return { text: reasons, state: "Error" };
	return { text: reasons, state: match.score >= 70 ? "Warning" : "Information" };
}

const origins: Record<LexwareLinkOrigin, string> = {
	linked: "verknüpft", createdInLexware: "aus EstrichONE in Lexware Office angelegt", importedFromLexware: "aus Lexware Office übernommen",
};

export const SYNC_LABEL = "Mit Lexware Office abgleichen";
export const SYNC_HELP = "Aus: Der Kunde bleibt nur in EstrichONE und wird weder mit Lexware Office verknüpft noch dort angelegt.";

/** Schalter im Kunden-Formular nur mit eingeschaltetem Modul. */
export function syncFieldVisible(modules: Record<string, boolean | undefined>): boolean {
	return modules.lexware === true;
}

/** Vorbelegung des Schalters: beim Bearbeiten der Stand des Kunden, beim Anlegen die Vorgabe aus den Einstellungen. */
export function syncInitial(contact: { lexwareSync?: boolean } | null, status: Pick<LexwareStatus, "syncDefault"> | null | undefined): boolean {
	return contact ? contact.lexwareSync === true : status?.syncDefault === true;
}

/** Stand des Abgleichs in der Kundenansicht. */
export function syncStateText(syncEnabled: boolean, link: LexwareContactLink | null): { text: string; state: State; hint: string } {
	if (syncEnabled) return { text: "Abgleich eingeschaltet", state: link ? "Success" : "Information", hint: "" };
	return link
		? { text: "Abgleich ausgeschaltet", state: "Warning",
			hint: "Der Abgleich ist ausgeschaltet, die Verknüpfung besteht aber noch. Der Kontakt und seine Rechnungen liegen weiter in Lexware Office. Lösen Sie die Verknüpfung, wenn der Kunde ganz in EstrichONE bleiben soll." }
		: { text: "Nur in EstrichONE", state: "None", hint: "Dieser Kunde bleibt nur in EstrichONE. Schalten Sie den Abgleich ein, um ihn mit Lexware Office zu verknüpfen." };
}

/** Kurztext zur Verknüpfung eines Kunden. */
export function linkText(link: LexwareContactLink | null): string {
	if (!link) return "Nicht mit Lexware Office verknüpft";
	return [link.name ?? "Lexware-Kontakt", link.number ? `Kundennummer ${link.number}` : "", origins[link.origin] ?? ""].filter(Boolean).join(", ");
}

/** Frage vor dem bewussten Neuanlegen trotz möglicher Dubletten. */
export function createAnywayQuestion(name: string, count: number, target: "lexware" | "estrichOne"): string {
	const where = target === "lexware" ? "in Lexware Office" : "in EstrichONE";
	return `Die Prüfung hat ${count === 1 ? "einen möglichen doppelten Eintrag" : `${count} mögliche doppelte Einträge`} ${where} gefunden. `
		+ `Möchten Sie „${name}“ trotzdem neu anlegen? Doppelte Einträge lassen sich später nur von Hand zusammenführen.`;
}

/** Zähler der Abgleichübersicht als Kacheln. */
export function reconciliationFigures(r: LexwareReconciliation | null): { key: string; label: string; value: string; state: State; hint: string }[] {
	if (!r) return [];
	return [
		{ key: "linked", label: "Verknüpft", value: String(r.linked), state: "Success", hint: "Kunde und Lexware-Kontakt gehören zusammen" },
		{ key: "suggestions", label: "Mögliche Dubletten", value: String(r.possibleDuplicates), state: r.possibleDuplicates > 0 ? "Warning" : "None",
			hint: r.uniqueMatches > 0 ? `${r.uniqueMatches} davon eindeutig` : "Vor dem Anlegen prüfen" },
		{ key: "onlyEstrichOne", label: "Nur in EstrichONE", value: String(r.onlyEstrichOne), state: "None", hint: "Abgleich an, noch kein Kontakt in Lexware Office" },
		{ key: "excluded", label: "Nur in EstrichONE (bewusst)", value: String(r.excluded ?? 0), state: "None", hint: "Abgleich für diese Kunden ausgeschaltet" },
		{ key: "onlyLexware", label: "Nur in Lexware", value: String(r.onlyLexware), state: "None", hint: "Noch kein Kunde in EstrichONE" },
	];
}

/** Eindeutige Treffer für die Massenaktion „Eindeutige Treffer verknüpfen“. */
export function uniquePairs(r: LexwareReconciliation | null): { contactId: string; lexwareContactId: string }[] {
	return (r?.suggestions ?? []).filter((s) => s.unique).map((s) => ({ contactId: s.customer.id, lexwareContactId: s.lexware.id }));
}

/** Ergebnis des letzten Verbindungstests als Text. */
export function lastTestText(s: Pick<LexwareSettings, "lastTestAt" | "lastTestOk" | "lastTestError" | "organizationName">, at: (iso: string) => string): string {
	if (!s.lastTestAt) return "";
	if (s.lastTestOk) return `Letzter Verbindungstest am ${at(s.lastTestAt)} erfolgreich${s.organizationName ? `: ${s.organizationName}` : "."}`;
	return `Letzter Verbindungstest am ${at(s.lastTestAt)} fehlgeschlagen: ${s.lastTestError ?? "ohne Angabe"}`;
}

/** Abschnitte in Kunde und Bauakte nur mit eingeschaltetem Modul und Leserecht. */
export function lexwareVisible(modules: Record<string, boolean | undefined>, permissions: Record<string, boolean | undefined>): boolean {
	return modules.lexware === true && permissions["invoices.read"] === true;
}
