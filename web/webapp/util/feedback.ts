/**
 * Fehler melden und Funktionen wünschen: Typen, Bezeichnungen, Bereiche, technische Angaben, Pflichtfelder und
 * Zusammenfassung des geführten Formulars sowie Filter der Listen. Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */
import type { Tone } from "./labels";

export type FeedbackKind = "bug" | "feature";
export type FeedbackStatus = "new" | "inReview" | "planned" | "inProgress" | "done" | "rejected" | "duplicate";
export type FeedbackPriority = "low" | "normal" | "high" | "urgent";
export type FeedbackFrequency = "once" | "sometimes" | "always";
export type FeedbackImpact = "low" | "medium" | "high";

/** Automatisch mitgeschickte technische Angaben. */
export interface FeedbackContext {
	page: string | null;
	pageTitle: string | null;
	browser: string | null;
	userAgent: string | null;
	platform: string | null;
	screen: string | null;
	viewport: string | null;
	pixelRatio: string | null;
	appVersion: string | null;
	language: string | null;
	timeZone: string | null;
}

export interface FeedbackAttachment { id: string; name: string; contentType: string; sizeBytes: number }

export interface FeedbackSummary {
	id: string;
	number: string;
	kind: FeedbackKind;
	title: string;
	area: string;
	status: FeedbackStatus;
	priority: FeedbackPriority;
	impact: FeedbackImpact;
	reporterUserId: string;
	reporterName: string | null;
	assigneeUserId: string | null;
	assigneeName: string | null;
	duplicateOfId: string | null;
	duplicateOfNumber: string | null;
	attachmentCount: number;
	createdAt: string;
	lastActivityAt: string;
	closedAt: string | null;
	version: number;
}

export interface FeedbackTimelineItem {
	id: string;
	at: string;
	kind: "created" | "comment" | "note" | "status" | "priority" | "assignee" | "classify" | string;
	actorUserId: string | null;
	actorName: string | null;
	summary: string;
	text: string | null;
	isInternal: boolean;
	isReporter: boolean;
	attachments: FeedbackAttachment[];
}

export interface FeedbackDetail {
	summary: FeedbackSummary;
	description: string | null;
	stepsToReproduce: string | null;
	expectedResult: string | null;
	actualResult: string | null;
	frequency: FeedbackFrequency | null;
	problem: string | null;
	desiredSolution: string | null;
	context: FeedbackContext | null;
	attachments: FeedbackAttachment[];
	timeline: FeedbackTimelineItem[];
}

export interface FeedbackCounts {
	total: number; open: number; new: number; inReview: number; planned: number; inProgress: number; done: number; rejected: number; duplicate: number;
	openBugs: number; openFeatures: number;
}

export interface FeedbackListResponse { items: FeedbackSummary[]; counts: FeedbackCounts; areas: string[] }

export const feedbackKinds: Record<FeedbackKind, { text: string; icon: string; intro: string }> = {
	bug: { text: "Fehler", icon: "sap-icon://quality-issue", intro: "Etwas funktioniert nicht wie erwartet, zeigt eine Fehlermeldung oder hängt." },
	feature: { text: "Funktionswunsch", icon: "sap-icon://lightbulb", intro: "Eine Idee, was EstrichONE zusätzlich können oder einfacher machen sollte." },
};

/** Statusablauf in Reihenfolge; Abgelehnt und Duplikat schließen eine Meldung ebenfalls ab. */
export const feedbackStatus: Record<FeedbackStatus, { text: string; tone: Tone; hint: string }> = {
	new: { text: "Neu", tone: "Information", hint: "Eingegangen, noch nicht gesichtet." },
	inReview: { text: "In Prüfung", tone: "Information", hint: "Die IT sieht sich die Meldung an." },
	planned: { text: "Geplant", tone: "Warning", hint: "Wird in einer der nächsten Versionen umgesetzt." },
	inProgress: { text: "In Umsetzung", tone: "Warning", hint: "Wird gerade umgesetzt." },
	done: { text: "Erledigt", tone: "Success", hint: "Behoben bzw. umgesetzt." },
	rejected: { text: "Abgelehnt", tone: "Error", hint: "Wird nicht umgesetzt. Die Begründung steht im Verlauf." },
	duplicate: { text: "Duplikat", tone: "None", hint: "Bereits gemeldet, siehe verknüpfte Meldung." },
};
export const statusFlow: FeedbackStatus[] = ["new", "inReview", "planned", "inProgress", "done"];

export const feedbackPriorities: Record<FeedbackPriority, { text: string; tone: Tone }> = {
	low: { text: "Niedrig", tone: "None" }, normal: { text: "Normal", tone: "Information" }, high: { text: "Hoch", tone: "Warning" },
	urgent: { text: "Dringend", tone: "Error" },
};

export const feedbackFrequencies: Record<FeedbackFrequency, string> = {
	once: "Einmal", sometimes: "Manchmal", always: "Immer (nachstellbar)",
};

/** Fehler: Auswirkung; Funktionswunsch: Wichtigkeit. */
export const bugImpacts: Record<FeedbackImpact, string> = {
	low: "Gering: stört, Weiterarbeit möglich", medium: "Mittel: nur mit Umweg möglich", high: "Hoch: Arbeiten nicht möglich",
};
export const featureImpacts: Record<FeedbackImpact, string> = {
	low: "Wäre schön", medium: "Wichtig: spart regelmäßig Zeit", high: "Sehr wichtig: wesentliche Funktion fehlt",
};
export const impactText = (kind: FeedbackKind, impact: FeedbackImpact): string => (kind === "bug" ? bugImpacts : featureImpacts)[impact] ?? impact;
export const impactShort: Record<FeedbackImpact, string> = { low: "Gering", medium: "Mittel", high: "Hoch" };

export const isClosed = (status: FeedbackStatus): boolean => status === "done" || status === "rejected" || status === "duplicate";

/** Bereiche der Anwendung (Auswahl im Formular; vorbelegt aus der aktuellen Seite). */
export const feedbackAreas: { name: string; prefixes: string[] }[] = [
	{ name: "Übersicht (Start)", prefixes: ["/start"] },
	{ name: "Tagesübersicht", prefixes: ["/tagesuebersicht"] },
	{ name: "Bauakten", prefixes: ["/bauakten"] },
	{ name: "Dokumente und Freigaben", prefixes: ["/dokumente"] },
	{ name: "Anfragen und Anrufe", prefixes: ["/anfragen", "/anrufe"] },
	{ name: "Kunden", prefixes: ["/kunden"] },
	{ name: "Material und Bestellungen", prefixes: ["/material", "/bestellungen", "/lieferanten"] },
	{ name: "Planung", prefixes: ["/planung"] },
	{ name: "Personal und Urlaub", prefixes: ["/personal"] },
	{ name: "Chat", prefixes: ["/chat"] },
	{ name: "Benachrichtigungen", prefixes: ["/benachrichtigungen"] },
	{ name: "Verwaltung", prefixes: ["/verwaltung"] },
	{ name: "IT Center und Helpdesk", prefixes: ["/it-center"] },
	{ name: "System und Datensicherung", prefixes: ["/system"] },
	{ name: "Anmeldung und Konto", prefixes: ["/anmelden", "/konto"] },
	{ name: "Meine Meldungen", prefixes: ["/meine-meldungen"] },
	{ name: "App (iPhone, iPad, Android)", prefixes: [] },
	{ name: "Sonstiges", prefixes: [] },
];

/** Bereich zur aktuellen Seite (längster passender Pfadanfang), sonst „Sonstiges“. */
export function areaForPath(path: string): string {
	const clean = (path.split(/[?#]/)[0] ?? "").replace(/\/+$/, "") || "/";
	let best = { name: "Sonstiges", length: 0 };
	for (const area of feedbackAreas) {
		for (const prefix of area.prefixes) {
			if ((clean === prefix || clean.startsWith(`${prefix}/`)) && prefix.length > best.length) best = { name: area.name, length: prefix.length };
		}
	}
	return best.name;
}

/** Browser mit Hauptversion aus dem User-Agent (Edge, Opera und Samsung vor Chrome, Chrome vor Safari). */
export function browserName(userAgent: string): string {
	const rules: [RegExp, string][] = [
		[/Edg(?:e|A|iOS)?\/(\d+)/, "Edge"], [/OPR\/(\d+)/, "Opera"], [/SamsungBrowser\/(\d+)/, "Samsung Internet"], [/Firefox\/(\d+)/, "Firefox"],
		[/FxiOS\/(\d+)/, "Firefox"], [/CriOS\/(\d+)/, "Chrome"], [/Chrome\/(\d+)/, "Chrome"], [/Version\/(\d+)[\d.]* .*Safari\//, "Safari"],
	];
	for (const [pattern, name] of rules) {
		const match = pattern.exec(userAgent);
		if (match) return `${name} ${match[1]}`;
	}
	return "Unbekannter Browser";
}

/** Betriebssystem grob aus dem User-Agent. */
export function platformName(userAgent: string): string {
	if (/iPad|iPhone|iPod/.test(userAgent)) return "iOS/iPadOS";
	if (/Android/.test(userAgent)) return "Android";
	if (/Windows NT/.test(userAgent)) return "Windows";
	if (/Mac OS X|Macintosh/.test(userAgent)) return "macOS";
	if (/CrOS/.test(userAgent)) return "ChromeOS";
	if (/Linux/.test(userAgent)) return "Linux";
	return "Unbekannt";
}

export interface ContextEnvironment {
	path: string;
	title: string;
	userAgent: string;
	screenWidth: number;
	screenHeight: number;
	innerWidth: number;
	innerHeight: number;
	devicePixelRatio: number;
	language: string;
	timeZone: string;
	appVersion: string | null;
}

/** Technische Angaben aus der Umgebung (Browserfenster) zusammenstellen. */
export function collectContext(env: ContextEnvironment): FeedbackContext {
	const ratio = Math.round(env.devicePixelRatio * 100);
	return {
		page: env.path || null,
		pageTitle: env.title.replace(/ – EstrichONE$/, "") || null,
		browser: browserName(env.userAgent),
		userAgent: env.userAgent || null,
		platform: platformName(env.userAgent),
		screen: `${env.screenWidth} × ${env.screenHeight}`,
		viewport: `${env.innerWidth} × ${env.innerHeight}`,
		pixelRatio: `${ratio} %`,
		appVersion: env.appVersion,
		language: env.language || null,
		timeZone: env.timeZone || null,
	};
}

/** Technische Angaben als beschriftete Zeilen (Zusammenfassung und Detail). */
export function contextRows(context: FeedbackContext | null | undefined, withUserAgent = false): { label: string; value: string }[] {
	if (!context) return [];
	const rows: [string, string | null][] = [
		["Seite", [context.pageTitle, context.page].filter(Boolean).join(" · ") || null],
		["Browser", [context.browser, context.platform].filter(Boolean).join(" auf ") || null],
		["Bildschirm", context.screen ? `${context.screen}${context.pixelRatio ? ` (Skalierung ${context.pixelRatio})` : ""}` : null],
		["Fenster", context.viewport],
		["EstrichONE-Version", context.appVersion],
		["Sprache / Zeitzone", [context.language, context.timeZone].filter(Boolean).join(" · ") || null],
		...(withUserAgent ? [["User-Agent", context.userAgent] as [string, string | null]] : []),
	];
	return rows.filter((r): r is [string, string] => !!r[1]).map(([label, value]) => ({ label, value }));
}

/** Eingaben des Formulars (Schritt 2). */
export interface FeedbackDraft {
	kind: FeedbackKind;
	title: string;
	area: string;
	description: string;
	stepsToReproduce: string;
	expectedResult: string;
	actualResult: string;
	frequency: FeedbackFrequency | "";
	problem: string;
	desiredSolution: string;
	impact: FeedbackImpact | "";
}

export const emptyDraft = (kind: FeedbackKind, area = ""): FeedbackDraft => ({
	kind, title: "", area, description: "", stepsToReproduce: "", expectedResult: "", actualResult: "", frequency: "", problem: "",
	desiredSolution: "", impact: "",
});

/** Pflichtfelder je Art: Schlüssel und Beschriftung. */
export function requiredFields(kind: FeedbackKind): { key: keyof FeedbackDraft; label: string }[] {
	return kind === "bug"
		? [{ key: "title", label: "Kurze Zusammenfassung" }, { key: "area", label: "Bereich / Seite" }, { key: "actualResult", label: "Was ist passiert?" },
			{ key: "frequency", label: "Wie oft?" }, { key: "impact", label: "Auswirkung" }]
		: [{ key: "title", label: "Kurze Zusammenfassung" }, { key: "area", label: "Bereich" }, { key: "problem", label: "Problem bzw. Nutzen" },
			{ key: "desiredSolution", label: "Gewünschte Lösung" }, { key: "impact", label: "Wichtigkeit" }];
}

/** Fehlende Pflichtfelder (leer bzw. nur Leerzeichen). */
export function missingFields(draft: FeedbackDraft): { key: keyof FeedbackDraft; label: string }[] {
	return requiredFields(draft.kind).filter((f) => !String(draft[f.key] ?? "").trim());
}

/** Zusammenfassung vor dem Absenden (nur ausgefüllte Felder). */
export function summaryRows(draft: FeedbackDraft, attachments: string[] = []): { label: string; value: string }[] {
	const bug = draft.kind === "bug";
	const rows: [string, string][] = [
		["Art", feedbackKinds[draft.kind].text],
		["Zusammenfassung", draft.title],
		[bug ? "Bereich / Seite" : "Bereich", draft.area],
		...(bug
			? [["Schritte zum Nachstellen", draft.stepsToReproduce], ["Erwartet", draft.expectedResult], ["Tatsächlich passiert", draft.actualResult],
				["Häufigkeit", draft.frequency ? feedbackFrequencies[draft.frequency] : ""]] as [string, string][]
			: [["Problem bzw. Nutzen", draft.problem], ["Gewünschte Lösung", draft.desiredSolution]] as [string, string][]),
		[bug ? "Auswirkung" : "Wichtigkeit", draft.impact ? impactText(draft.kind, draft.impact) : ""],
		["Weitere Hinweise", draft.description],
		["Anhänge", attachments.join(", ")],
	];
	return rows.filter(([, value]) => !!value?.trim()).map(([label, value]) => ({ label, value: value.trim() }));
}

/** Anfrage für POST /feedback. */
export function createRequest(id: string, draft: FeedbackDraft, context: FeedbackContext, attachmentFileIds: string[]): Record<string, unknown> {
	const text = (value: string) => value.trim() || null;
	const bug = draft.kind === "bug";
	return {
		id, kind: draft.kind, title: draft.title.trim(), area: draft.area.trim(), description: text(draft.description),
		stepsToReproduce: bug ? text(draft.stepsToReproduce) : null, expectedResult: bug ? text(draft.expectedResult) : null,
		actualResult: bug ? text(draft.actualResult) : null, frequency: bug ? draft.frequency || null : null,
		problem: bug ? null : text(draft.problem), desiredSolution: bug ? null : text(draft.desiredSolution),
		impact: draft.impact || "medium", context, attachmentFileIds,
	};
}

/** Angaben einer Meldung als beschriftete Abschnitte (Detail für Einreicher und IT). */
export function detailRows(d: FeedbackDetail): { label: string; value: string }[] {
	const bug = d.summary.kind === "bug";
	const rows: [string, string | null][] = bug
		? [["Schritte zum Nachstellen", d.stepsToReproduce], ["Erwartet", d.expectedResult], ["Tatsächlich passiert", d.actualResult],
			["Häufigkeit", d.frequency ? feedbackFrequencies[d.frequency] : null], ["Auswirkung", impactText("bug", d.summary.impact)],
			["Weitere Hinweise", d.description]]
		: [["Problem bzw. Nutzen", d.problem], ["Gewünschte Lösung", d.desiredSolution], ["Wichtigkeit", impactText("feature", d.summary.impact)],
			["Weitere Hinweise", d.description]];
	return rows.filter((r): r is [string, string] => !!r[1]).map(([label, value]) => ({ label, value }));
}

export interface ListFilter {
	kind: "" | FeedbackKind;
	status: string;
	area: string;
	priority: "" | FeedbackPriority;
	query: string;
}

const fold = (value: string) => value.toLocaleLowerCase("de-DE").normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/ß/g, "ss");

/** Filter auf dem Client („Meine Meldungen“): Status offen/erledigt/alle, Art, Suche in Nummer, Titel, Bereich. */
export function filterFeedback(items: FeedbackSummary[], filter: Partial<ListFilter>): FeedbackSummary[] {
	const words = fold(filter.query ?? "").split(/\s+/).filter(Boolean);
	return items.filter((f) => {
		if (filter.kind && f.kind !== filter.kind) return false;
		if (filter.priority && f.priority !== filter.priority) return false;
		if (filter.area && f.area !== filter.area) return false;
		const status = filter.status ?? "all";
		if (status === "open" && isClosed(f.status)) return false;
		if (status === "closed" && !isClosed(f.status)) return false;
		if (status !== "open" && status !== "closed" && status !== "all" && f.status !== status) return false;
		const text = fold([f.number, f.title, f.area, f.reporterName ?? "", f.assigneeName ?? ""].join(" "));
		return words.every((w) => text.includes(w));
	});
}

const priorityRank: Record<FeedbackPriority, number> = { urgent: 0, high: 1, normal: 2, low: 3 };

/** Sortierung der IT-Liste. */
export function sortFeedback(items: FeedbackSummary[], sort: string): FeedbackSummary[] {
	const by: Record<string, (a: FeedbackSummary, b: FeedbackSummary) => number> = {
		recent: (a, b) => b.lastActivityAt.localeCompare(a.lastActivityAt),
		oldest: (a, b) => a.createdAt.localeCompare(b.createdAt),
		priority: (a, b) => priorityRank[a.priority] - priorityRank[b.priority] || b.lastActivityAt.localeCompare(a.lastActivityAt),
		number: (a, b) => b.number.localeCompare(a.number, "de", { numeric: true }),
	};
	return items.slice().sort(by[sort] ?? by.recent);
}

/** Ab welchem Schritt des Statusablaufs die Meldung steht (für die Fortschrittsanzeige). */
export function flowProgress(status: FeedbackStatus): { step: number; total: number; text: string } {
	const index = statusFlow.indexOf(status);
	return index >= 0
		? { step: index + 1, total: statusFlow.length, text: `Schritt ${index + 1} von ${statusFlow.length}: ${feedbackStatus[status].text}` }
		: { step: statusFlow.length, total: statusFlow.length, text: feedbackStatus[status].text };
}

/** Nummer einer Meldung normalisieren (Eingabe „f-2026-3“ → „F-2026-0003“). */
export function normalizeNumber(value: string): string | null {
	const match = /^\s*F-?(\d{4})-?(\d{1,4})\s*$/i.exec(value);
	return match ? `F-${match[1]}-${match[2]!.padStart(4, "0")}` : null;
}
