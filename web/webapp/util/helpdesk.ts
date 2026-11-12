/**
 * Helpdesk für die IT: Typen der Service-Desk-Erweiterung (Ansichten, SLA, Gruppen, Regeln, Wissensdatenbank) und reine
 * Hilfsfunktionen für Liste, Ampel und Antworten. Ohne UI5 – siehe test/helpdesk.test.ts.
 */
import type { TicketCategory, TicketDetail, TicketPriority, TicketStatus, TicketSummary } from "./models";

export type SlaStateKey = "met" | "breached" | "paused" | "overdue" | "dueSoon" | "ok";

export interface HdSummary extends TicketSummary {
	hasUnread?: boolean;
	groupId?: string | null;
	groupName?: string | null;
	subcategoryId?: string | null;
	subcategoryName?: string | null;
	firstResponseAt?: string | null;
	responseDueAt?: string | null;
	resolutionDueAt?: string | null;
	slaState?: SlaStateKey | null;
	dueAt?: string | null;
	dueKind?: "response" | "resolution" | null;
	mergedIntoTicketId?: string | null;
	rating?: number | null;
	resolvedAt?: string | null;
	channel?: TicketChannel;
	createdByUserId?: string | null;
	createdByName?: string | null;
}

/** Eingangskanal: selbst gemeldet oder von der IT aufgenommen (Telefon, E-Mail, vor Ort). */
export type TicketChannel = "selfService" | "phone" | "email" | "inPerson";

export const ticketChannels: Record<TicketChannel, { text: string; icon: string }> = {
	selfService: { text: "Selbst gemeldet", icon: "sap-icon://person-placeholder" },
	phone: { text: "Telefon", icon: "sap-icon://call" },
	email: { text: "E-Mail", icon: "sap-icon://email" },
	inPerson: { text: "Vor Ort", icon: "sap-icon://customer" },
};

/** Seite der Arbeitsliste (GET /tickets/page). */
export interface TicketPage { items: HdSummary[]; total: number; offset: number; limit: number }

/** Person für „Meldung aufnehmen“ (GET /helpdesk/reporters). */
export interface HelpdeskReporter { id: string; displayName: string; userName: string; email: string | null; phone: string | null }

export interface HelpdeskHoliday { date: string; name: string; isStatutory: boolean }
export interface HelpdeskHolidays {
	year: number; state: string | null; stateName: string | null; days: HelpdeskHoliday[]; states: { code: string; name: string }[];
}

export interface HdTimelineItem {
	id: string; at: string; kind: string; actorUserId: string | null; actorName: string | null; summary: string; text: string | null;
	fromValue: string | null; toValue: string | null; isInternal: boolean; isReporter: boolean; attachmentFileIds: string[];
	mentionUserIds?: string[] | null;
}

/**
 * Zusatztext unter einem Ereignis im Verlauf (Lösung beim Status „Gelöst“/„Geschlossen“, Kommentar der Bewertung).
 * Der Server liefert die Lösung des Tickets an jedem dieser Status-Ereignisse; gezeigt wird sie nur am letzten, und nur,
 * wenn sie etwas sagt (keine Zeile aus reinem Satzzeichen wie „,“).
 */
export function timelineEventTexts(items: HdTimelineItem[]): Map<string, string> {
	const meaningful = (t: string | null) => !!t && /[\p{L}\p{N}]/u.test(t);
	const out = new Map<string, string>();
	let lastResolution: HdTimelineItem | undefined;
	for (const i of items) {
		if (i.kind === "status" && meaningful(i.text) && (!lastResolution || i.at >= lastResolution.at)) lastResolution = i;
		if (i.kind === "rating" && meaningful(i.text)) out.set(i.id, i.text!.trim());
	}
	if (lastResolution) out.set(lastResolution.id, lastResolution.text!.trim());
	return out;
}

export interface HdLink { ticketId: string; number: string; title: string; status: TicketStatus; kind: "related" | "duplicate" }
export interface HdArticleRef { id: string; title: string; isPublished: boolean }

export interface HdDetail extends Omit<TicketDetail, "summary"> {
	summary: HdSummary;
	timeline?: HdTimelineItem[] | null;
	links?: HdLink[] | null;
	articles?: HdArticleRef[] | null;
	mergedIntoNumber?: string | null;
	rating?: number | null;
	ratingComment?: string | null;
	ratedAt?: string | null;
	canRate?: boolean;
	totalMinutes?: number | null;
	watcherNames?: string[] | null;
}

export interface HelpdeskStats {
	open: number; unassigned: number; overdue: number; waiting: number; mine: number; blocked: number; createdToday: number; createdThisWeek: number;
	resolvedThisWeek: number; avgFirstResponseMinutes: number | null; avgResolutionHours: number | null; slaMetPercent: number | null;
	satisfactionAverage: number | null; ratings: number;
	byCategory: { category: TicketCategory; open: number; total30Days: number }[];
	byAgent: { userId: string; name: string; open: number; overdue: number }[];
	trend: { day: string; created: number; resolved: number }[];
	/** „business“: Reaktions- und Lösungszeit in Geschäftszeit (ohne Nächte, Wochenende, Feiertage, Rückfrage). */
	timeBasis?: string;
	/** Melder hat geantwortet, die IT ist am Zug. */
	waitingForSupport?: number;
}

export interface ReporterDevice {
	id: string; name: string; platform: "ios" | "android" | "windows" | "other"; kind: string; status: string; model: string | null; osVersion: string | null;
	appVersion: string | null; updateState: "current" | "recommended" | "required" | "newerThanServer" | null; latestVersion: string | null;
	lastSeenAt: string | null; isTicketDevice: boolean;
}

export interface KnowledgeArticleSummary {
	id: string; title: string; category: TicketCategory | null; keywords: string | null; isPublished: boolean; excerpt: string; views: number;
	helpfulCount: number; updatedAt: string;
}

export interface KnowledgeArticle {
	id: string; title: string; body: string; category: TicketCategory | null; keywords: string | null; isPublished: boolean; authorName: string | null;
	updatedByName: string | null; views: number; helpfulCount: number; createdAt: string; updatedAt: string; version: number;
}

export interface TicketContext {
	reporter: { id: string; displayName: string; userName: string; email: string | null; phone: string | null; isActive: boolean; lastLoginAt: string | null } | null;
	devices: ReporterDevice[];
	otherTickets: HdSummary[];
	suggestedArticles: KnowledgeArticleSummary[];
}

export interface SlaTarget { priority: TicketPriority; responseHours: number; resolutionHours: number; aroundTheClock: boolean }
export interface HelpdeskSettings {
	targets: SlaTarget[]; hours: { start: string; end: string; days: number[] }; holidays: string[]; slaEnabled: boolean; ratingEnabled: boolean;
	timeTrackingEnabled: boolean; notifyAgentsOnNew: boolean; suggestArticles: boolean; autoCloseDays: number;
	/** Bundesland für gesetzliche Feiertage (BW … TH) oder null. */
	state?: string | null;
}
export interface HelpdeskGroup { id: string; name: string; description: string | null; memberUserIds: string[]; isActive: boolean; sortOrder: number; version: number }
export interface TicketSubcategory { id: string; category: TicketCategory; name: string; isActive: boolean; sortOrder: number; version: number }
export type AssignmentStrategy = "groupOnly" | "fixed" | "leastBusy";
export interface HelpdeskRule {
	id: string; name: string; category: TicketCategory | null; subcategoryId: string | null; keyword: string | null; groupId: string | null;
	strategy: AssignmentStrategy; assigneeUserId: string | null; priority: TicketPriority | null; isActive: boolean; sortOrder: number; version: number;
}
export interface CannedResponse { id: string; name: string; text: string; category: TicketCategory | null; isPersonal: boolean; sortOrder: number; version: number }
export interface HelpdeskAgent { id: string; displayName: string; openTickets: number }
export interface HelpdeskOptions {
	categories: { key: TicketCategory; name: string; subcategories: TicketSubcategory[] }[];
	ratingEnabled: boolean; suggestArticles: boolean; timeTrackingEnabled: boolean; slaEnabled: boolean;
}
export interface TimeEntry { id: string; userId: string; userName: string | null; minutes: number; workDate: string; note: string | null; createdAt: string }

/** Gespeicherte Ansichten der Arbeitsliste (Schlüssel = Filter der API). */
export const queueViews: { key: string; text: string; empty: string }[] = [
	{ key: "mine", text: "Meine", empty: "Ihnen ist gerade kein offenes Ticket zugewiesen." },
	{ key: "unassigned", text: "Nicht zugewiesen", empty: "Alle Tickets haben eine zuständige Person." },
	{ key: "overdue", text: "Überfällig", empty: "Kein Ticket hat sein Ziel überschritten." },
	{ key: "support", text: "Warten auf Support", empty: "Kein Melder wartet auf eine Antwort der IT." },
	{ key: "waiting", text: "Warten auf Anwenderinfo", empty: "Keine offenen Rückfragen an Melder." },
	{ key: "open", text: "Alle offenen", empty: "Keine offenen Tickets." },
	{ key: "myGroups", text: "Meine Gruppen", empty: "Keine offenen Tickets in Ihren Gruppen." },
	{ key: "done", text: "Erledigt", empty: "Noch keine gelösten Tickets." },
	{ key: "all", text: "Alle", empty: "Noch keine Tickets." },
];

export const slaStates: Record<SlaStateKey, { text: string; tone: "Success" | "Warning" | "Error" | "Information" | "None"; icon: string }> = {
	met: { text: "im Ziel erledigt", tone: "Success", icon: "sap-icon://accept" },
	breached: { text: "nach Ziel erledigt", tone: "Warning", icon: "sap-icon://warning2" },
	paused: { text: "angehalten (Rückfrage)", tone: "Information", icon: "sap-icon://pause" },
	overdue: { text: "überfällig", tone: "Error", icon: "sap-icon://alert" },
	dueSoon: { text: "bald fällig", tone: "Warning", icon: "sap-icon://lateness" },
	ok: { text: "im Plan", tone: "None", icon: "sap-icon://history" },
};

export const strategies: Record<AssignmentStrategy, string> = {
	groupOnly: "Nur Gruppe setzen", fixed: "Feste Person", leastBusy: "Wenigste offene Tickets in der Gruppe",
};

export const updateStates: Record<string, { text: string; tone: "Success" | "Warning" | "Error" | "Information" | "None" }> = {
	current: { text: "aktuell", tone: "Success" }, recommended: { text: "Update verfügbar", tone: "Warning" },
	required: { text: "Update erforderlich", tone: "Error" }, newerThanServer: { text: "neuer als Server", tone: "Information" },
};

const priorityRank: Record<string, number> = { urgent: 0, high: 1, normal: 2, low: 3 };
const statusRank: Record<string, number> = { new: 0, inProgress: 1, waitingForSupport: 2, waitingForReporter: 3, resolved: 4, closed: 5 };

/** Dauer in Worten: „45 min“, „3 h 20 min“, „2 T 4 h“. */
export function duration(ms: number): string {
	const minutes = Math.max(0, Math.round(Math.abs(ms) / 60_000));
	if (minutes < 60) return `${minutes} min`;
	const hours = Math.floor(minutes / 60);
	if (hours < 24) return minutes % 60 ? `${hours} h ${minutes % 60} min` : `${hours} h`;
	const days = Math.floor(hours / 24);
	return hours % 24 ? `${days} T ${hours % 24} h` : `${days} T`;
}

/** Minuten als „1:05 h“ bzw. „45 min“ (Zeiterfassung). */
export function formatMinutes(minutes: number | null | undefined): string {
	const m = Math.max(0, Math.round(minutes ?? 0));
	return m < 60 ? `${m} min` : `${Math.floor(m / 60)}:${String(m % 60).padStart(2, "0")} h`;
}

/** Eingabe der Zeiterfassung: „45“, „1:30“, „1,5“, „1,5 h“, „90 min“ → Minuten (null bei Unsinn). */
export function parseMinutes(input: string): number | null {
	const text = input.trim().toLowerCase().replace(/\s+/g, "");
	let m: RegExpMatchArray | null;
	if ((m = text.match(/^(\d{1,2}):(\d{2})h?$/))) return Number(m[1]) * 60 + Number(m[2]);
	if ((m = text.match(/^(\d+(?:[.,]\d+)?)(h|std|stunden?)$/))) return Math.round(Number(m[1].replace(",", ".")) * 60);
	if ((m = text.match(/^(\d+)(min|m)?$/))) return Number(m[1]);
	if ((m = text.match(/^(\d+[.,]\d+)$/))) return Math.round(Number(m[1].replace(",", ".")) * 60);
	return null;
}

const clock = (d: Date): string => d.toLocaleString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });

/** Ampel für Liste und Kopf: Text, Farbe, Symbol (Server liefert Zustand und nächstes Ziel). */
export function slaInfo(t: Pick<HdSummary, "slaState" | "dueAt" | "dueKind">, now = Date.now()): { text: string; tone: string; icon: string; key: string } {
	const key = t.slaState ?? "";
	const base = slaStates[key as SlaStateKey];
	if (!base) return { text: "–", tone: "None", icon: "", key };
	const due = t.dueAt ? new Date(t.dueAt).getTime() : null;
	const what = t.dueKind === "response" ? "Antwort" : "Lösung";
	let text = base.text;
	if (key === "overdue" && due) text = `${what} überfällig seit ${duration(now - due)}`;
	else if (key === "dueSoon" && due) text = `${what} fällig in ${duration(due - now)}`;
	else if (key === "ok" && due) text = `${what} bis ${clock(new Date(due))}`;
	return { text, tone: base.tone, icon: base.icon, key };
}

export type SortColumn = "number" | "title" | "reporter" | "category" | "priority" | "status" | "assignee" | "due" | "activity" | "waiting";

/** Spaltensortierung der Arbeitsliste (auf- oder absteigend, Gleichstand nach Nummer). */
export function compareTickets(a: HdSummary, b: HdSummary, column: SortColumn, descending = false): number {
	const text = (x: string | null | undefined, y: string | null | undefined) => (x ?? "￿").localeCompare(y ?? "￿", "de", { numeric: true, sensitivity: "base" });
	const time = (x: string | null | undefined, y: string | null | undefined) =>
		(x ? new Date(x).getTime() : Number.MAX_SAFE_INTEGER) - (y ? new Date(y).getTime() : Number.MAX_SAFE_INTEGER);
	let result: number;
	switch (column) {
		case "number": result = text(a.number, b.number); break;
		case "title": result = text(a.title, b.title); break;
		case "reporter": result = text(a.reporterDisplayName, b.reporterDisplayName); break;
		case "category": result = text(a.category, b.category); break;
		case "priority": result = (priorityRank[a.priority] ?? 9) - (priorityRank[b.priority] ?? 9); break;
		case "status": result = (statusRank[a.status] ?? 9) - (statusRank[b.status] ?? 9); break;
		case "assignee": result = text(a.assigneeName, b.assigneeName); break;
		case "due": result = time(a.dueAt, b.dueAt); break;
		case "activity": result = time(b.lastActivityAt, a.lastActivityAt); break;
		default: result = Number(!!b.reporterBlocked) - Number(!!a.reporterBlocked) || time(a.lastActivityAt, b.lastActivityAt);
	}
	if (descending) result = -result;
	return result || text(b.number, a.number);
}

/** Textbaustein mit Platzhaltern füllen: {Melder}, {Vorname}, {Ticket}, {Bearbeiter}, {Titel}. */
export function fillPlaceholders(text: string, values: { reporter?: string | null; ticket?: string; agent?: string | null; title?: string }): string {
	const reporter = (values.reporter ?? "").replace(/ \(laut Eingabe\)$/, "").trim();
	const first = reporter.split(/\s+/)[0] ?? "";
	return text
		.replace(/\{Melder\}/g, reporter || "zusammen")
		.replace(/\{Vorname\}/g, first || "zusammen")
		.replace(/\{Ticket\}/g, values.ticket ?? "")
		.replace(/\{Titel\}/g, values.title ?? "")
		.replace(/\{Bearbeiter\}/g, values.agent ?? "Ihre IT");
}

/** Erwähnte Bearbeiter im Text („@Anna Berger“ oder „@Anna“), längste Namen zuerst. */
export function mentionedAgents(text: string, agents: { id: string; displayName: string }[]): string[] {
	const lower = text.toLowerCase();
	const found = new Set<string>();
	for (const agent of [...agents].sort((a, b) => b.displayName.length - a.displayName.length)) {
		const full = `@${agent.displayName.toLowerCase()}`;
		const first = `@${agent.displayName.split(/\s+/)[0].toLowerCase()}`;
		const hit = (needle: string) => {
			const at = lower.indexOf(needle);
			return at >= 0 && !/[\p{L}\d]/u.test(lower.charAt(at + needle.length));
		};
		if (hit(full) || hit(first)) found.add(agent.id);
	}
	return [...found];
}

/**
 * Erwähnung beim Tippen: Steht vor der Schreibmarke „@“ plus Anfang eines Namens (am Zeilenanfang oder nach Leerzeichen,
 * höchstens zwei Wörter, keine Zeilenumbrüche), liefert die Funktion Position des „@“ und den bisher getippten Text.
 */
export function mentionQuery(text: string, caret: number): { start: number; query: string } | null {
	const before = text.slice(0, Math.max(0, caret));
	const at = before.lastIndexOf("@");
	if (at < 0) return null;
	if (at > 0 && !/[\s(„"'\[]/u.test(before.charAt(at - 1))) return null;
	const query = before.slice(at + 1);
	if (query.length > 40 || /[\n\r]/.test(query) || /\s{2}/.test(query) || query.split(" ").length > 2 || /^\s/.test(query)) return null;
	if (!/^[\p{L}\d .'-]*$/u.test(query)) return null;
	return { start: at, query };
}

/** Passende Bearbeiter zu einem angefangenen Namen (Vor- oder Nachname beginnt damit, bzw. ganzer Name), höchstens <limit>. */
export function mentionCandidates<T extends { id: string; displayName: string }>(query: string, agents: T[], limit = 6): T[] {
	const q = query.trim().toLowerCase();
	const words = (name: string) => name.toLowerCase().split(/[\s-]+/);
	return agents
		.filter((a) => !q || a.displayName.toLowerCase().startsWith(q) || words(a.displayName).some((w) => w.startsWith(q)))
		.sort((a, b) => Number(!a.displayName.toLowerCase().startsWith(q)) - Number(!b.displayName.toLowerCase().startsWith(q))
			|| a.displayName.localeCompare(b.displayName, "de"))
		.slice(0, limit);
}

/** Angefangene Erwähnung durch „@Vollständiger Name “ ersetzen; liefert neuen Text und Position der Schreibmarke. */
export function insertMention(text: string, start: number, caret: number, name: string): { text: string; caret: number } {
	const rest = text.slice(caret);
	const inserted = `@${name}${rest.startsWith(" ") ? "" : " "}`;
	return { text: text.slice(0, start) + inserted + rest, caret: start + inserted.length + (rest.startsWith(" ") ? 1 : 0) };
}

/** Zähler unter der Arbeitsliste: „100 von 742 Tickets“ bzw. „12 Tickets“. */
export function pageText(loaded: number, total: number, unread = 0): string {
	const noun = (n: number) => (n === 1 ? "Ticket" : "Tickets");
	const base = total > loaded ? `${loaded.toLocaleString("de-DE")} von ${total.toLocaleString("de-DE")} ${noun(total)}` : `${loaded} ${noun(loaded)}`;
	return `${base}${unread ? ` · ${unread} mit neuer Antwort` : ""}`;
}

/** Kennzahlen als Kacheln (Schlüssel = Ansicht, in die ein Klick springt). */
export function kpiTiles(s: HelpdeskStats): { key: string; label: string; value: string; unit: string; color: "Good" | "Critical" | "Error" | "Neutral"; footer: string }[] {
	const hours = (m: number | null) => (m == null ? "–" : m < 60 ? String(Math.round(m)) : (m / 60).toLocaleString("de-DE", { maximumFractionDigits: 1 }));
	return [
		{ key: "open", label: "Offen", value: String(s.open), unit: "", color: "Neutral", footer: s.blocked ? `${s.blocked} können nicht arbeiten` : `${s.createdToday} heute neu` },
		{ key: "unassigned", label: "Nicht zugewiesen", value: String(s.unassigned), unit: "", color: s.unassigned ? "Critical" : "Good", footer: "warten auf Übernahme" },
		{ key: "overdue", label: "Überfällig", value: String(s.overdue), unit: "", color: s.overdue ? "Error" : "Good", footer: "Ziel überschritten" },
		{ key: "support", label: "Warten auf Support", value: String(s.waitingForSupport ?? 0), unit: "", color: s.waitingForSupport ? "Critical" : "Good",
			footer: "Melder hat geantwortet" },
		{ key: "waiting", label: "Anwenderinfo", value: String(s.waiting), unit: "", color: "Neutral", footer: "warten auf Melder" },
		{ key: "mine", label: "Mir zugewiesen", value: String(s.mine), unit: "", color: "Neutral", footer: "offen" },
		{ key: "done", label: "Gelöst diese Woche", value: String(s.resolvedThisWeek), unit: "", color: "Good", footer: `${s.createdThisWeek} neu diese Woche` },
		{ key: "", label: "Ø erste Antwort", value: hours(s.avgFirstResponseMinutes), unit: s.avgFirstResponseMinutes != null && s.avgFirstResponseMinutes < 60 ? "min" : "h",
			color: "Neutral", footer: `${s.timeBasis === "business" ? "Geschäftszeit · " : ""}${s.slaMetPercent == null ? "letzte 30 Tage" : `${s.slaMetPercent.toLocaleString("de-DE")} % im Ziel gelöst`}` },
		{ key: "", label: "Ø Lösungszeit", value: s.avgResolutionHours == null ? "–" : s.avgResolutionHours.toLocaleString("de-DE", { maximumFractionDigits: 1 }),
			unit: s.avgResolutionHours == null ? "" : "h", color: "Neutral", footer: s.timeBasis === "business" ? "Geschäftszeit ohne Rückfrage" : "letzte 30 Tage" },
		{ key: "", label: "Zufriedenheit", value: s.satisfactionAverage == null ? "–" : s.satisfactionAverage.toLocaleString("de-DE", { maximumFractionDigits: 1 }),
			unit: s.satisfactionAverage == null ? "" : "von 5", color: s.satisfactionAverage != null && s.satisfactionAverage < 3.5 ? "Critical" : "Neutral",
			footer: s.ratings ? `${s.ratings} Bewertungen (30 Tage)` : "noch keine Bewertung" },
	];
}

/** Zeile „Modell und System“ im Kontext: „iPhone 15 · iOS 18.1“ (App-Version steht in eigener Spalte). */
export function deviceLine(d: ReporterDevice): string {
	const platform = d.platform === "ios" ? "iOS" : d.platform === "android" ? "Android" : d.platform === "windows" ? "Windows" : "";
	return [d.model, d.osVersion ? `${platform} ${d.osVersion}`.trim() : platform].filter(Boolean).join(" · ");
}

/** Verlauf für den Prüfpfad: alle Ereignisse, neueste zuerst, mit Art. */
export function auditRows(timeline: HdTimelineItem[]): { at: string; who: string; what: string; detail: string; internal: boolean }[] {
	const kinds: Record<string, string> = {
		created: "Gemeldet", comment: "Antwort", note: "Interne Notiz", status: "Status", assignee: "Zuweisung", priority: "Priorität", category: "Kategorie",
		subcategory: "Unterkategorie", group: "Gruppe", rule: "Regel", merged: "Zusammengeführt", mergedFrom: "Zusammengeführt", link: "Verknüpfung",
		unlink: "Verknüpfung", article: "Wissensdatenbank", rating: "Bewertung", time: "Zeiterfassung", watcher: "Beobachter",
	};
	return [...timeline].sort((a, b) => b.at.localeCompare(a.at)).map((i) => ({
		at: i.at, who: i.actorName ?? "System", what: kinds[i.kind] ?? i.kind,
		detail: i.kind === "comment" || i.kind === "note" || i.kind === "created" ? (i.text ?? "").slice(0, 160) : i.summary, internal: i.isInternal,
	}));
}

/** Feiertage aus Zeilen „24.12.2026“ oder „2026-12-24“ (Kommentar nach dem Datum erlaubt) → ISO-Daten, sortiert, ohne Doppelte. */
export function parseHolidays(text: string): { dates: string[]; invalid: string[] } {
	const dates = new Set<string>();
	const invalid: string[] = [];
	for (const raw of text.split(/[\n,;]+/)) {
		const line = raw.trim();
		if (!line) continue;
		let m = line.match(/^(\d{1,2})\.(\d{1,2})\.(\d{4})/);
		let iso = m ? `${m[3]}-${m[2].padStart(2, "0")}-${m[1].padStart(2, "0")}` : null;
		if (!iso && (m = line.match(/^(\d{4})-(\d{2})-(\d{2})/))) iso = `${m[1]}-${m[2]}-${m[3]}`;
		const valid = iso && !Number.isNaN(Date.parse(`${iso}T00:00:00Z`)) && new Date(`${iso}T00:00:00Z`).toISOString().startsWith(iso);
		if (valid && iso) dates.add(iso);
		else invalid.push(line);
	}
	return { dates: [...dates].sort(), invalid };
}

export function formatHolidays(dates: string[]): string {
	return dates.map((d) => d.split("-").reverse().join(".")).join("\n");
}

/** Bedingung und Wirkung einer Zuweisungsregel in einem Satz. */
export function describeRule(rule: HelpdeskRule, names: { categories: Record<string, string>; subcategories: Map<string, string>; groups: Map<string, string>; people: Map<string, string> }): { when: string; then: string } {
	const parts = [rule.category ? `Kategorie „${names.categories[rule.category] ?? rule.category}“` : null,
		rule.subcategoryId ? `Unterkategorie „${names.subcategories.get(rule.subcategoryId) ?? "gelöscht"}“` : null,
		rule.keyword ? `Text enthält „${rule.keyword}“` : null].filter(Boolean);
	const group = rule.groupId ? names.groups.get(rule.groupId) ?? "gelöschte Gruppe" : null;
	const then = [group ? `Gruppe ${group}` : null,
		rule.strategy === "fixed" ? `an ${names.people.get(rule.assigneeUserId ?? "") ?? "unbekannt"}` : rule.strategy === "leastBusy" ? "an das Mitglied mit den wenigsten offenen Tickets" : null,
		rule.priority ? `Priorität ${({ low: "niedrig", normal: "normal", high: "hoch", urgent: "dringend" } as Record<string, string>)[rule.priority]}` : null].filter(Boolean);
	return { when: parts.length ? parts.join(" und ") : "jedes neue Ticket", then: then.join(", ") || "keine Änderung" };
}

const escapeHtml = (s: string): string => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

/**
 * Artikeltext als einfaches HTML für sap.m.FormattedText: Absätze, Überschriften („# “, „## “), Aufzählungen („- “, „* “),
 * nummerierte Schritte („1. “) und **fett**. Alles andere wird maskiert – kein HTML aus dem Artikel.
 */
export function articleHtml(body: string): string {
	const out: string[] = [];
	let list: "ul" | "ol" | null = null;
	const close = () => { if (list) { out.push(`</${list}>`); list = null; } };
	const inline = (s: string) => escapeHtml(s).replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
	for (const raw of body.replace(/\r\n/g, "\n").split("\n")) {
		const line = raw.trimEnd();
		let m: RegExpMatchArray | null;
		if (!line.trim()) { close(); continue; }
		if ((m = line.match(/^#{1,3}\s+(.*)$/))) { close(); out.push(`<h4>${inline(m[1])}</h4>`); continue; }
		if ((m = line.match(/^\s*[-*•]\s+(.*)$/))) {
			if (list !== "ul") { close(); out.push("<ul>"); list = "ul"; }
			out.push(`<li>${inline(m[1])}</li>`);
			continue;
		}
		if ((m = line.match(/^\s*\d+[.)]\s+(.*)$/))) {
			if (list !== "ol") { close(); out.push("<ol>"); list = "ol"; }
			out.push(`<li>${inline(m[1])}</li>`);
			continue;
		}
		close();
		out.push(`<p>${inline(line)}</p>`);
	}
	close();
	return out.join("");
}
