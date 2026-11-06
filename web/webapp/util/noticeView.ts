/**
 * Darstellung der Systemhinweise (ohne UI5, unit-getestet): Art, Farbe und Symbol, verständlicher Zeitraum
 * („Sa., 08.11., 22:00–23:30 Uhr“), relative Angabe („in 2 Tagen“, „läuft seit 10 Min.“), Reihenfolge und
 * Ausblenden je Benutzer. Wird vom Banner in der Shell, der Startseite und der Vorschau unter /system/hinweise genutzt.
 */

/** Art eines Hinweises; bestimmt Farbe, Symbol und ob er ausgeblendet werden darf. */
export type NoticeKind = "maintenance" | "maintenanceSoon" | "maintenanceRunning" | "info" | "warning" | "critical";

/** UI5-Wertzustand (Farbe) je Art. */
export type NoticeTone = "Information" | "Warning" | "Error";

export interface NoticeAnnouncementInput {
	id: string;
	title: string;
	text: string;
	severity: string;
	startsAt?: string | null;
	endsAt?: string | null;
	source?: string | null;
}

export interface NoticeMaintenanceInput {
	active?: boolean;
	message?: string | null;
	startsAt: string;
	endsAt?: string | null;
	adminsMayWork?: boolean;
	source?: string | null;
}

/** Ein fertig aufbereiteter Hinweis für Banner, Startseite, Details und Vorschau. */
export interface NoticeCard {
	/** Schlüssel zum Ausblenden; ändert sich, wenn sich der Inhalt ändert (dann erscheint der Hinweis wieder). */
	key: string;
	kind: NoticeKind;
	tone: NoticeTone;
	icon: string;
	/** Kurzbezeichnung der Art („Geplante Wartung“, „Störung“ …). */
	label: string;
	title: string;
	text: string;
	/** Absoluter Zeitraum, leer ohne Zeitangabe. */
	period: string;
	/** Relative Angabe („in 2 Tagen“, „läuft seit 10 Min.“), leer wenn nicht sinnvoll. */
	relative: string;
	/** Restdauer bei laufender Wartung bzw. befristetem Hinweis („noch 20 Min.“). */
	remaining: string;
	dismissible: boolean;
	/** Herkunft für die Details („Betreiber“, „Verwaltung“). */
	source: string;
	/** Sortierung: kleiner = wichtiger. */
	rank: number;
}

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

const pad = (n: number) => String(n).padStart(2, "0");
const weekdays = ["So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa."];

/** „Sa., 08.11.“ – mit Jahr, wenn es nicht das laufende ist. */
export function shortDate(d: Date, now: Date): string {
	const year = d.getFullYear() !== now.getFullYear() ? String(d.getFullYear()) : "";
	return `${weekdays[d.getDay()]}, ${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${year}`;
}

const clock = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
const sameDay = (a: Date, b: Date) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
const parse = (value: string | null | undefined): Date | null => {
	if (!value) return null;
	const d = new Date(value);
	return Number.isNaN(d.getTime()) ? null : d;
};

/**
 * Zeitraum in Ortszeit: „Sa., 08.11., 22:00–23:30 Uhr“, über Mitternacht „Sa., 08.11., 22:00 Uhr – So., 09.11., 02:00 Uhr“,
 * nur Beginn „ab Sa., 08.11., 22:00 Uhr“, nur Ende „bis Sa., 08.11., 22:00 Uhr“.
 */
export function formatPeriod(startsAt: string | null | undefined, endsAt: string | null | undefined, now: Date = new Date()): string {
	const start = parse(startsAt);
	const end = parse(endsAt);
	if (start && end) {
		if (sameDay(start, end)) return `${shortDate(start, now)}, ${clock(start)}–${clock(end)} Uhr`;
		return `${shortDate(start, now)}, ${clock(start)} Uhr – ${shortDate(end, now)}, ${clock(end)} Uhr`;
	}
	if (start) return `ab ${shortDate(start, now)}, ${clock(start)} Uhr`;
	if (end) return `bis ${shortDate(end, now)}, ${clock(end)} Uhr`;
	return "";
}

type Case = "dat" | "acc";

/** Dauer in Worten: „10 Min.“, „1 Std. 30 Min.“, „5 Std.“, „2 Tagen“ (Dativ) bzw. „2 Tage“ (Akkusativ). */
export function duration(ms: number, grammar: Case = "dat"): string {
	const minutes = Math.max(1, Math.round(ms / MINUTE));
	if (minutes < 60) return `${minutes} Min.`;
	if (ms < DAY) {
		const hours = Math.floor(minutes / 60);
		const rest = minutes % 60;
		return hours < 3 && rest >= 5 ? `${hours} Std. ${rest} Min.` : `${Math.round(minutes / 60)} Std.`;
	}
	const days = Math.round(ms / DAY);
	if (days === 1) return "1 Tag";
	return `${days} ${grammar === "dat" ? "Tagen" : "Tage"}`;
}

/** Kalendertage zwischen zwei Zeitpunkten (0 = heute, 1 = morgen …). */
const dayDiff = (from: Date, to: Date) =>
	Math.round((new Date(to.getFullYear(), to.getMonth(), to.getDate()).getTime() - new Date(from.getFullYear(), from.getMonth(), from.getDate()).getTime()) / DAY);

/**
 * Relative Angabe zum Zeitraum: vorher „in 45 Min.“ / „heute, in 5 Std.“ / „morgen“ / „in 2 Tagen“,
 * währenddessen „läuft seit 10 Min.“, danach „beendet“. Ohne Beginn leer.
 */
export function relativeLabel(startsAt: string | null | undefined, endsAt: string | null | undefined, now: Date = new Date()): string {
	const start = parse(startsAt);
	const end = parse(endsAt);
	if (end && now.getTime() >= end.getTime()) return "beendet";
	if (!start) return "";
	const diff = start.getTime() - now.getTime();
	if (diff > 0) {
		if (diff < MINUTE) return "beginnt gleich";
		if (diff < HOUR) return `in ${duration(diff)}`;
		const days = dayDiff(now, start);
		if (days === 0) return `heute, in ${duration(diff)}`;
		if (days === 1) return "morgen";
		return `in ${days} Tagen`;
	}
	const since = now.getTime() - start.getTime();
	return since < MINUTE ? "läuft gerade an" : `läuft seit ${duration(since)}`;
}

/** Restdauer bis zum Ende: „noch 20 Min.“, „noch 3 Tage“; leer ohne Ende oder wenn vorbei. */
export function remainingLabel(endsAt: string | null | undefined, now: Date = new Date()): string {
	const end = parse(endsAt);
	if (!end) return "";
	const left = end.getTime() - now.getTime();
	return left > 0 ? `noch ${duration(left, "acc")}` : "";
}

const style: Record<NoticeKind, { tone: NoticeTone; icon: string; label: string; rank: number; dismissible: boolean }> = {
	maintenanceRunning: { tone: "Warning", icon: "sap-icon://wrench", label: "Wartung läuft", rank: 0, dismissible: false },
	critical: { tone: "Error", icon: "sap-icon://message-error", label: "Störung", rank: 1, dismissible: false },
	maintenanceSoon: { tone: "Warning", icon: "sap-icon://appointment-2", label: "Wartung in Kürze", rank: 2, dismissible: true },
	warning: { tone: "Warning", icon: "sap-icon://message-warning", label: "Wichtiger Hinweis", rank: 3, dismissible: true },
	maintenance: { tone: "Information", icon: "sap-icon://appointment-2", label: "Geplante Wartung", rank: 4, dismissible: true },
	info: { tone: "Information", icon: "sap-icon://message-information", label: "Information", rank: 5, dismissible: true },
};

/** Art einer Ankündigung aus der Schwere (unbekannt = Information). */
export function announcementKind(severity: string): NoticeKind {
	return severity === "critical" ? "critical" : severity === "warning" ? "warning" : "info";
}

/** Art der Wartung: läuft, beginnt binnen einer Stunde, oder geplant. */
export function maintenanceKind(m: NoticeMaintenanceInput, now: Date = new Date()): NoticeKind {
	const start = parse(m.startsAt);
	if (m.active || (start && start.getTime() <= now.getTime())) return "maintenanceRunning";
	if (start && start.getTime() - now.getTime() <= HOUR) return "maintenanceSoon";
	return "maintenance";
}

/** Kurzer, stabiler Inhaltsschlüssel (FNV-1a), damit geänderte Hinweise wieder erscheinen. */
export function contentHash(text: string): string {
	let h = 0x811c9dc5;
	for (let i = 0; i < text.length; i++) {
		h ^= text.charCodeAt(i);
		h = Math.imul(h, 0x01000193);
	}
	return (h >>> 0).toString(36);
}

const sourceText = (source: string | null | undefined) => (source === "console" ? "Betreiber" : source === "instance" ? "Verwaltung" : "");

export function announcementCard(a: NoticeAnnouncementInput, now: Date = new Date()): NoticeCard {
	const kind = announcementKind(a.severity);
	const s = style[kind];
	const start = parse(a.startsAt);
	// Bereits laufende Ankündigungen ohne Ende brauchen keine „läuft seit“-Angabe; befristete zeigen die Restdauer.
	const upcoming = !!start && start.getTime() > now.getTime();
	return {
		key: `a:${a.id}:${contentHash(`${a.severity}|${a.title}|${a.text}|${a.startsAt ?? ""}|${a.endsAt ?? ""}`)}`,
		kind, tone: s.tone, icon: s.icon, label: s.label, rank: s.rank, dismissible: s.dismissible,
		title: a.title, text: a.text ?? "",
		period: formatPeriod(a.startsAt, a.endsAt, now),
		relative: upcoming ? relativeLabel(a.startsAt, a.endsAt, now) : "",
		remaining: upcoming ? "" : remainingLabel(a.endsAt, now),
		source: sourceText(a.source),
	};
}

export function maintenanceCard(m: NoticeMaintenanceInput, now: Date = new Date(), adminWorking = false): NoticeCard {
	const kind = maintenanceKind(m, now);
	const s = style[kind];
	const running = kind === "maintenanceRunning";
	return {
		// „Bald“ hat einen eigenen Schlüssel: Wer früh ausblendet, wird eine Stunde vorher noch einmal erinnert.
		key: `m:${m.startsAt}:${kind}:${contentHash(`${m.message ?? ""}|${m.endsAt ?? ""}`)}`,
		kind, tone: s.tone, icon: s.icon, label: s.label, rank: s.rank, dismissible: s.dismissible,
		title: running
			? adminWorking ? "Wartungsmodus aktiv – nur Administratoren können arbeiten" : "Wartungsarbeiten laufen"
			: "Geplante Wartung",
		text: running && adminWorking
			? `Benutzer sehen die Wartungsseite.${m.message ? ` Nachricht: ${m.message}` : ""}`
			: m.message || "EstrichONE ist in dieser Zeit nicht erreichbar. Bitte speichern Sie Ihre Eingaben rechtzeitig.",
		period: formatPeriod(m.startsAt, m.endsAt, now),
		relative: relativeLabel(m.startsAt, m.endsAt, now),
		remaining: running ? remainingLabel(m.endsAt, now) : "",
		source: sourceText(m.source),
	};
}

/** Alle Hinweise in Anzeige-Reihenfolge: laufende Wartung, Störungen, Wartung in Kürze, Warnungen, geplante Wartung, Infos. */
export function buildCards(
	announcements: NoticeAnnouncementInput[], maintenance: NoticeMaintenanceInput | null, now: Date = new Date(), adminWorking = false,
): NoticeCard[] {
	const cards = announcements.map((a) => announcementCard(a, now));
	if (maintenance) {
		const end = parse(maintenance.endsAt);
		if (!end || end.getTime() > now.getTime() || maintenance.active) cards.push(maintenanceCard(maintenance, now, adminWorking));
	}
	return cards.map((c, i) => ({ c, i })).sort((x, y) => x.c.rank - y.c.rank || x.i - y.i).map(({ c }) => c);
}

/**
 * Banner-Auswahl: ausgeblendete (nur ausblendbare) fallen weg. Eingeklappt bleibt der wichtigste Hinweis plus alle nicht
 * ausblendbaren sichtbar; der Rest wird als „+n weitere“ zusammengefasst.
 */
export function bannerSelection(cards: NoticeCard[], dismissed: ReadonlySet<string>, expanded: boolean): { shown: NoticeCard[]; hidden: number } {
	const open = cards.filter((c) => !c.dismissible || !dismissed.has(c.key));
	if (expanded || open.length <= 1) return { shown: open, hidden: 0 };
	const shown = open.filter((c, i) => i === 0 || !c.dismissible);
	return { shown, hidden: open.length - shown.length };
}

/** Text für den Sammel-Schalter („1 weiterer Hinweis“, „3 weitere Hinweise“). */
export const moreText = (n: number) => (n === 1 ? "1 weiterer Hinweis" : `${n} weitere Hinweise`);
