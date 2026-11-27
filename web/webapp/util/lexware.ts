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
	/** Richtung des Änderungsabgleichs verknüpfter Kunden. */
	changeSync: ChangeSync;
	lastChangeSyncAt: string | null;
	lastChangeSyncError: string | null;
	customersWithDeviations: number;
	/** Neue Kunden ohne Treffer automatisch in Lexware Office anlegen. */
	autoCreate: boolean;
}

/** Stand der automatischen Anlage eines Kunden in Lexware Office. */
export interface LexwareCreationState { state: "pending" | "duplicates" | "failed" | "manual" | string; error: string | null; at: string | null }

export type ChangeSync = "both" | "toLexware" | "fromLexware" | "off";
export type DeviationSource = "estrichOne" | "lexware";
/** Abweichung in einem abgeglichenen Feld mit den Werten beider Seiten. */
export interface LexwareDeviationField { field: string; label: string; estrichOne: string | null; lexware: string | null }
export type SyncStateKey = "synced" | "deviations" | "pending" | "error" | "missing" | "archived" | "disabled" | "paused" | "off";
/** Stand des Änderungsabgleichs einer Verknüpfung. */
export interface LexwareSyncState { state: SyncStateKey | string; syncedAt: string | null; deviations: LexwareDeviationField[]; error: string | null }
export interface LexwareDeviationItem {
	customer: LexwareCustomerSummary; lexwareContactId: string; lexwareName: string | null; lexwareUrl: string; fields: LexwareDeviationField[]; syncedAt: string | null;
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
	/** Stand der automatischen Anlage: pending, duplicates, failed, manual. */
	creation?: string | null;
	creationError?: string | null;
}

export interface Match { score: number; reasons: string[]; strong: boolean }
export interface LexwareContactCandidate extends Match { contact: LexwareContactSummary }
export interface LexwareCustomerCandidate extends Match { customer: LexwareCustomerSummary }

export type LexwareLinkOrigin = "linked" | "createdInLexware" | "importedFromLexware";
export interface LexwareContactLink { lexwareContactId: string; name: string | null; number: string | null; url: string; origin: LexwareLinkOrigin; linkedAt: string }
export interface CustomerLexware { contactId: string; link: LexwareContactLink | null; canEdit: boolean; syncEnabled: boolean; sync?: LexwareSyncState | null; creation?: LexwareCreationState | null }
export interface LexwareCandidates { candidates: LexwareContactCandidate[]; loadedAt: string | null }
export interface CreateLexwareContactResponse { created: boolean; link: LexwareContactLink | null; candidates: LexwareContactCandidate[] }
export interface ImportLexwareContactResponse { imported: boolean; contactId: string | null; customerNumber: string | null; candidates: LexwareCustomerCandidate[] }
export interface LexwareContactList { items: LexwareContactSummary[]; page: number; pageSize: number; total: number; loadedAt: string }

export interface LexwareLinkedPair { customer: LexwareCustomerSummary; lexware: LexwareContactSummary | null; missingInLexware: boolean; sync?: LexwareSyncState | null }
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
	/** Verknüpfte Kunden mit offenen Abweichungen (beide Seiten haben ein Feld unterschiedlich). */
	deviations?: number; deviationItems?: LexwareDeviationItem[];
	changeSync?: ChangeSync; changeSyncError?: string | null;
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
	/** Verknüpfter Lexware-Kontakt des Kunden (aus dem zuletzt abgeglichenen Stand); fehlt ohne Verknüpfung. */
	contact?: LexwareBauakteContact | null;
	/** Summen der zugeordneten Belege als Textzeile. */
	summary?: string | null;
}
export interface LexwareBauakteContact {
	lexwareContactId: string; name: string | null; number: string | null; url: string; street: string | null; postalCode: string | null; city: string | null;
	email: string | null; differingFields: string[]; sync: LexwareSyncState;
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
		{ key: "deviations", label: "Abweichungen", value: String(r.deviations ?? 0), state: (r.deviations ?? 0) > 0 ? "Warning" : "None",
			hint: r.changeSync === "off" ? "Änderungsabgleich ausgeschaltet" : "Unterschiedliche Angaben, Sie entscheiden" },
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

// ---------- Änderungsabgleich verknüpfter Kunden ----------

/** Richtungen des Änderungsabgleichs für die Auswahl in den Einstellungen. */
export const changeSyncOptions: { key: ChangeSync; text: string }[] = [
	{ key: "both", text: "Beide Richtungen" },
	{ key: "toLexware", text: "Nur von EstrichONE nach Lexware Office" },
	{ key: "fromLexware", text: "Nur von Lexware Office nach EstrichONE" },
	{ key: "off", text: "Aus" },
];

/** Erläuterung zur gewählten Richtung. */
export function changeSyncHelp(direction: ChangeSync | string | null | undefined): string {
	switch (direction) {
		case "toLexware": return "Änderungen an verknüpften Kunden in EstrichONE gehen nach Lexware Office. Änderungen aus Lexware Office erscheinen als Abweichung und werden nicht von selbst übernommen.";
		case "fromLexware": return "Änderungen am Kontakt in Lexware Office kommen nach EstrichONE. Änderungen aus EstrichONE erscheinen als Abweichung und werden nicht von selbst übertragen.";
		case "off": return "Spätere Änderungen werden nicht abgeglichen. Verknüpfungen und Belege bleiben unverändert sichtbar.";
		default: return "Ändert nur eine Seite ein Feld, wird die Änderung auf der anderen Seite übernommen. Ändern beide Seiten dasselbe Feld unterschiedlich, entscheiden Sie im Kundenabgleich.";
	}
}

/** Stand des letzten vollständigen Änderungsabgleichs für die Einstellungen. */
export function changeSyncStand(s: Pick<LexwareSettings, "changeSync" | "lastChangeSyncAt" | "lastChangeSyncError">, at: (iso: string) => string): { text: string; state: State } {
	if (s.changeSync === "off") return { text: "", state: "None" };
	if (s.lastChangeSyncError) return { text: `Der letzte Änderungsabgleich ist fehlgeschlagen: ${s.lastChangeSyncError}`, state: "Error" };
	return s.lastChangeSyncAt ? { text: `Letzter vollständiger Änderungsabgleich: ${at(s.lastChangeSyncAt)}`, state: "None" }
		: { text: "Der erste Änderungsabgleich läuft in Kürze im Hintergrund.", state: "None" };
}

/** Stand des Änderungsabgleichs als Text und Farbe (Kunde, Abgleichübersicht). */
export function changeSyncState(sync: LexwareSyncState | null | undefined, at: (iso: string) => string): { text: string; state: State; icon: string; hint: string } {
	if (!sync) return { text: "", state: "None", icon: "", hint: "" };
	const last = sync.syncedAt ? `Letzter Abgleich: ${at(sync.syncedAt)}` : "Noch nicht abgeglichen";
	const count = sync.deviations.length;
	switch (sync.state) {
		case "synced": return { text: "Abgeglichen", state: "Success", icon: "sap-icon://sys-enter-2", hint: last };
		case "deviations": return { text: count === 1 ? "1 Abweichung offen" : `${count} Abweichungen offen`, state: "Warning", icon: "sap-icon://alert",
			hint: `${last}. Beide Seiten haben unterschiedliche Angaben. Es wurde nichts überschrieben.` };
		case "pending": return { text: "Abgleich steht aus", state: "Information", icon: "sap-icon://pending", hint: `${last}. Der Abgleich läuft in Kürze im Hintergrund.` };
		case "error": return { text: "Abgleich fehlgeschlagen", state: "Error", icon: "sap-icon://error", hint: `${last}. ${sync.error ?? ""}`.trim() };
		case "missing": return { text: "Kontakt in Lexware Office nicht mehr vorhanden", state: "Error", icon: "sap-icon://alert",
			hint: "Der verknüpfte Kontakt wurde in Lexware Office gelöscht. Lösen Sie die Verknüpfung und verknüpfen Sie den Kunden bei Bedarf neu." };
		case "archived": return { text: "Kontakt in Lexware Office archiviert", state: "Warning", icon: "sap-icon://alert",
			hint: "Der verknüpfte Kontakt ist in Lexware Office archiviert. Solange er archiviert ist, wird nichts abgeglichen." };
		case "paused": return { text: "Abgleich ruht", state: "None", icon: "", hint: "Archivierte Kunden werden nicht abgeglichen. Der Kontakt in Lexware Office bleibt unverändert." };
		case "off": return { text: "Änderungsabgleich ausgeschaltet", state: "None", icon: "", hint: "Der Änderungsabgleich ist in den Einstellungen von Lexware Office ausgeschaltet." };
		default: return { text: "", state: "None", icon: "", hint: "" };
	}
}

/** Wert einer Abweichung für die Anzeige. */
export function deviationValue(value: string | null | undefined): string {
	return value?.trim() ? value : "(leer)";
}

/** Abweichungen in einer Zeile, z. B. „Straße: Altweg 1 / Neuweg 2“. */
export function deviationSummary(fields: LexwareDeviationField[]): string {
	return fields.map((f) => `${f.label}: ${deviationValue(f.estrichOne)} / ${deviationValue(f.lexware)}`).join("; ");
}

/** Auswahl im Dialog als Anfrage: nur entschiedene Felder; ohne Entscheidung nichts. */
export function resolveRequest(choices: Record<string, DeviationSource | "open" | "" | undefined>): { fields: Record<string, DeviationSource> } | null {
	const fields = Object.fromEntries(Object.entries(choices).filter(([, source]) => source === "estrichOne" || source === "lexware")) as Record<string, DeviationSource>;
	return Object.keys(fields).length > 0 ? { fields } : null;
}

// ---------- Beleg als PDF ----------

/** Für Entwürfe stellt Lexware Office kein PDF bereit. */
export function pdfAvailable(voucher: { status: string }): boolean {
	return voucher.status !== "draft";
}

export const PDF_DRAFT_HINT = "Für Entwürfe stellt Lexware Office kein PDF bereit.";

/** Adresse des Beleg-PDFs: am Kunden über die Lexware-ID, an der Bauakte über die Zuordnung. */
export function voucherPdfPath(owner: { customerId: string; lexwareId: string } | { bauakteId: string; linkId: string }): string {
	return "customerId" in owner
		? `lexware/customers/${owner.customerId}/vouchers/${encodeURIComponent(owner.lexwareId)}/document`
		: `bauakten/${owner.bauakteId}/lexware/vouchers/${owner.linkId}/document`;
}

/** Titel der PDF-Anzeige, z. B. „Rechnung RE1042“. */
export function voucherTitle(v: { type: string; number: string | null }): string {
	return v.number ? `${voucherTypeText(v.type)} ${v.number}` : voucherTypeText(v.type);
}

// ---------- Lexware Office in der Übersicht der Bauakte ----------

export interface BauakteLexwareBlock {
	/** Block zeigen: Kunde verknüpft. */
	linked: boolean;
	/** Dezenter Hinweis statt Block: Kunde vorhanden, nicht verknüpft, Nutzer darf verknüpfen. */
	unlinkedHint: boolean;
	customerId: string;
	name: string; number: string; address: string; addressDiffers: boolean; email: string; emailDiffers: boolean; nameDiffers: boolean;
	stateText: string; stateState: State; stateHint: string; summary: string; url: string;
}

const DIFFERS = "weicht von EstrichONE ab";
export const differsText = (differs: boolean) => (differs ? DIFFERS : "");

/** Kompakter Block „Lexware Office“ in der Übersicht der Bauakte (nur Anzeige, ohne Aufruf bei Lexware Office). */
export function bauakteLexwareBlock(data: BauakteLexware | null, at: (iso: string) => string): BauakteLexwareBlock {
	const empty: BauakteLexwareBlock = {
		linked: false, unlinkedHint: false, customerId: data?.customer?.id ?? "", name: "", number: "", address: "", addressDiffers: false, email: "", emailDiffers: false,
		nameDiffers: false, stateText: "", stateState: "None", stateHint: "", summary: "", url: "",
	};
	const c = data?.contact;
	if (!data || !c) return { ...empty, unlinkedHint: !!data?.customer && data.configured && data.canAssign };
	const differs = (...fields: string[]) => fields.some((f) => c.differingFields.includes(f));
	const state = changeSyncState(c.sync, at);
	const quiet = !state.text && c.sync.state === "disabled";
	return {
		...empty, linked: true, name: c.name ?? "Lexware-Kontakt", number: c.number ?? "", address: placeText(c), addressDiffers: differs("street", "postalCode", "city"),
		email: c.email ?? "", emailDiffers: differs("email"), nameDiffers: differs("name"),
		stateText: quiet ? "Abgleich ausgeschaltet" : state.text, stateState: state.state,
		stateHint: c.sync.state === "synced" || c.sync.state === "deviations" ? (c.sync.syncedAt ? `Zuletzt abgeglichen: ${at(c.sync.syncedAt)}` : "") : state.hint,
		summary: data.summary || voucherSummary(data.vouchers) || "Dieser Bauakte ist noch kein Beleg zugeordnet.", url: c.url,
	};
}

// ---------- Prüfung beim Anlegen eines Kunden ----------

export interface LexwareContactCheck { checked: boolean; message: string | null; candidates: LexwareContactCandidate[]; canImport: boolean }

export const LEXWARE_EXISTS_WARNING = "Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden.";
export const LEXWARE_CHECK_UNAVAILABLE = "Die Prüfung in Lexware Office ist derzeit nicht möglich.";

/** Die Prüfung läuft mit eingeschaltetem Modul und hinterlegtem Schlüssel, unabhängig vom Schalter „Mit Lexware Office abgleichen“. */
export function lexwareCheckActive(modules: Record<string, boolean | undefined>, status: Pick<LexwareStatus, "configured"> | null | undefined): boolean {
	return modules.lexware === true && status?.configured === true;
}

/** Eingaben des Dialogs „Neuer Kunde“ als Anfrage; ohne Name und E-Mail-Adresse wird nicht geprüft. */
export function lexwareCheckRequest(draft: { kind: string; name: string; street?: string | null; postalCode?: string | null; city?: string | null; phones?: string[]; emails?: string[]; vatId?: string | null }):
Record<string, string | null> | null {
	const text = (value: string | null | undefined) => value?.trim() || null;
	const name = text(draft.name);
	const email = text(draft.emails?.[0]);
	if ((name?.length ?? 0) < 3 && !email) return null;
	return { name, kind: draft.kind === "company" ? "company" : "person", email, phone: text(draft.phones?.[0]), street: text(draft.street),
		postalCode: text(draft.postalCode), city: text(draft.city), vatId: text(draft.vatId) };
}

/** Warnung über den Treffern, in Einzahl und Mehrzahl. */
export function lexwareCheckText(check: LexwareContactCheck | null): { warning: string; note: string } {
	if (!check) return { warning: "", note: "" };
	if (!check.checked) return { warning: "", note: check.message ? `${check.message} Der Kunde lässt sich trotzdem anlegen und erscheint später im Kundenabgleich.` : "" };
	const n = check.candidates.length;
	return { warning: n === 0 ? "" : n === 1 ? LEXWARE_EXISTS_WARNING : `${LEXWARE_EXISTS_WARNING} Es gibt ${n} passende Kontakte.`, note: "" };
}

/** Zeile zu einem Treffer: Kundennummer und Anschrift. */
export function lexwareCandidateLine(c: LexwareContactSummary): string {
	return [c.customerNumber ? `Kundennummer ${c.customerNumber}` : "ohne Kundennummer", placeText(c), c.email].filter(Boolean).join(", ");
}

/** Was mit einem Treffer möglich ist: übernehmen, auf den vorhandenen Kunden verweisen oder nur warnen. */
export function lexwareCandidateAction(c: LexwareContactSummary, canImport: boolean): "import" | "linked" | "none" {
	return c.linkedContactId ? "linked" : canImport ? "import" : "none";
}

// ---------- Automatische Anlage in Lexware Office ----------

/** Stand der automatischen Anlage als Text und Farbe (Kunde, Liste „Nur in EstrichONE“). */
export function creationStateText(state: string | null | undefined, error?: string | null): { text: string; state: State; hint: string; retry: boolean } {
	switch (state) {
		case "pending": return { text: "Anlage in Lexware Office ausstehend", state: "Information", retry: false,
			hint: "Der Kunde wird in Kürze im Hintergrund in Lexware Office angelegt, wenn es dort keinen ähnlichen Kontakt gibt." };
		case "duplicates": return { text: "Nicht automatisch angelegt: ähnlicher Kontakt in Lexware Office", state: "Warning", retry: false,
			hint: "In Lexware Office gibt es einen ähnlichen Kontakt. Prüfen Sie die Treffer und verknüpfen Sie den Kunden oder legen Sie ihn bewusst neu an." };
		case "failed": return { text: "Anlage in Lexware Office fehlgeschlagen", state: "Error", retry: true,
			hint: `${error ?? ""} EstrichONE versucht es später erneut.`.trim() };
		default: return { text: "", state: "None", hint: "", retry: false };
	}
}

/** Rückfrage der Massenaktion „Abgleich einschalten“ mit der Zahl der Kunden, die dadurch in Lexware Office neu angelegt würden. */
export function enableSyncQuestion(preview: { changed: number; wouldCreate: number; withMatches: number }): string {
	const who = preview.changed === 1 ? "den ausgewählten Kunden" : `${preview.changed} ausgewählte Kunden`;
	const created = preview.wouldCreate === 0 ? "Dadurch wird in Lexware Office kein Kunde neu angelegt."
		: preview.wouldCreate === 1 ? "Dadurch wird 1 Kunde automatisch in Lexware Office neu angelegt, weil es dort keinen ähnlichen Kontakt gibt."
			: `Dadurch werden ${preview.wouldCreate} Kunden automatisch in Lexware Office neu angelegt, weil es dort keinen ähnlichen Kontakt gibt.`;
	const matches = preview.withMatches === 0 ? ""
		: preview.withMatches === 1 ? " 1 Kunde hat dort einen ähnlichen Kontakt und erscheint unter „Mögliche Dubletten“."
			: ` ${preview.withMatches} Kunden haben dort einen ähnlichen Kontakt und erscheinen unter „Mögliche Dubletten“.`;
	return `Abgleich mit Lexware Office für ${who} einschalten? ${created}${matches}`;
}
