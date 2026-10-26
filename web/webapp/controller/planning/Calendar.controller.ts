import BaseController from "../BaseController";
import Element from "sap/ui/core/Element";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import EventBus from "sap/ui/core/EventBus";
import Device from "sap/ui/Device";
import type SinglePlanningCalendar from "sap/m/SinglePlanningCalendar";
import SinglePlanningCalendarDayView from "sap/m/SinglePlanningCalendarDayView";
import SinglePlanningCalendarWeekView from "sap/m/SinglePlanningCalendarWeekView";
import SinglePlanningCalendarWorkWeekView from "sap/m/SinglePlanningCalendarWorkWeekView";
import SinglePlanningCalendarMonthView from "sap/m/SinglePlanningCalendarMonthView";
import type SinglePlanningCalendarView from "sap/m/SinglePlanningCalendarView";
import type CalendarAppointment from "sap/ui/unified/CalendarAppointment";
import type UnifiedCalendar from "sap/ui/unified/Calendar";
import DateRange from "sap/ui/unified/DateRange";
import MessageToast from "sap/m/MessageToast";
import Button from "sap/m/Button";
import type { RouteMatch } from "../../util/router";
import { del, get, post, put } from "../../util/api";
import { addDays, isoDay, mondayOf, parseDay } from "../../util/days";
import { assignmentKinds } from "../../util/labels";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { session } from "../../util/session";
import { draftOf, openAssignment, planningLookups } from "../../util/assignmentDialog";
import { askScope, calendarChanged, calendars, openEventDialog, palette } from "../../util/calendarDialog";
import { assignmentSpan, calendarEventKinds, localIso, parseLocal, type CalendarOccurrence, type CalendarResponse } from "../../util/planningModels";
import { navQuery } from "../../util/planningModels";
import type { AssignmentView, CrewResponse, PlanningRange } from "../../util/models";

type Mode = "tag" | "woche" | "arbeitswoche" | "monat" | "agenda";
const modes: Mode[] = ["tag", "woche", "arbeitswoche", "monat", "agenda"];
const HIDDEN_KEY = "eo.calendar.hidden";
const PREFS_KEY = "eo.calendar.prefs";

function readJson<T>(key: string, fallback: T): T {
	try {
		const raw = window.localStorage.getItem(key);
		return raw ? (JSON.parse(raw) as T) : fallback;
	} catch {
		return fallback;
	}
}
function writeJson(key: string, value: unknown): void {
	try {
		window.localStorage.setItem(key, JSON.stringify(value));
	} catch {
		// Kein Speicher (privates Fenster) – Einstellung gilt dann nur für diese Sitzung.
	}
}
const hm = (d: Date) => d.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });

/**
 * Terminkalender (C4) wie Google Kalender: Tag, Woche, Mo–Fr, Monat und Agenda; mehrere Kalender ein- und ausblenden,
 * Farben nach Kalender oder Terminart, Termine ziehen und verlängern, im freien Bereich anlegen, wiederkehrende
 * Termine, Teilnehmer, Bezug zur Bauakte. Einsätze der Planung lassen sich einblenden und ebenfalls verschieben.
 * @namespace estrichone.controller.planning
 */
export default class Calendar extends BaseController {
	private views: Record<Exclude<Mode, "agenda">, SinglePlanningCalendarView> | null = null;
	private occurrences: CalendarOccurrence[] = [];
	private assignments: AssignmentView[] = [];
	private crews: CrewResponse[] = [];
	private loadSeq = 0;

	onInit(): void {
		const prefs = readJson<{ colorMode?: string; showAssignments?: boolean; fullDay?: boolean }>(PREFS_KEY, {});
		this.local({
			mode: "woche", startDate: new Date(), title: "", busy: false, error: "", appointments: [], agenda: [], calendars: [],
			colorMode: prefs.colorMode === "kind" ? "kind" : "calendar", showAssignments: prefs.showAssignments !== false, fullDay: prefs.fullDay === true,
			sidebar: !Device.system.phone,
			legend: Object.values(calendarEventKinds).map((k) => ({ color: k.color, text: k.text })),
		});
		const spc = this.byId("spc") as SinglePlanningCalendar;
		const id = this.createId("");
		this.views = {
			tag: new SinglePlanningCalendarDayView(`${id}vDay`, { key: "tag", title: "Tag" }),
			woche: new SinglePlanningCalendarWeekView(`${id}vWeek`, { key: "woche", title: "Woche" }),
			arbeitswoche: new SinglePlanningCalendarWorkWeekView(`${id}vWorkWeek`, { key: "arbeitswoche", title: "Mo–Fr" }),
			monat: new SinglePlanningCalendarMonthView(`${id}vMonth`, { key: "monat", title: "Monat" }),
		};
		spc.addView(this.views.woche);
		EventBus.getInstance().subscribe("estrichone", "calendarChanged", this.changed, this);
		EventBus.getInstance().subscribe("estrichone", "planningChanged", this.changed, this);
	}

	onExit(): void {
		EventBus.getInstance().unsubscribe("estrichone", "calendarChanged", this.changed, this);
		EventBus.getInstance().unsubscribe("estrichone", "planningChanged", this.changed, this);
		for (const view of Object.values(this.views ?? {})) view.destroy();
	}

	private changed(): void { void this.load(); }

	onRouteMatched(match: RouteMatch): void {
		const m = this.model();
		const mode = match.query.get("ansicht") as Mode | null;
		const date = match.query.get("datum") ?? "";
		const next: Mode = mode && modes.includes(mode) ? mode : Device.system.phone ? "agenda" : "woche";
		m.setProperty("/mode", next);
		m.setProperty("/startDate", parseDay(/^\d{4}-\d{2}-\d{2}$/.test(date) ? date : isoDay(new Date())));
		this.applyView(next);
		void this.load();
	}

	/** Genau eine Ansicht im SinglePlanningCalendar – die Umschaltung (inkl. Agenda) liegt in eigener Hand. */
	private applyView(mode: Mode): void {
		const spc = this.byId("spc") as SinglePlanningCalendar;
		if (!this.views || mode === "agenda") return;
		const wanted = this.views[mode];
		const current = spc.getViews()[0];
		if (current !== wanted) {
			spc.removeAllViews();
			// removeAllViews() entfernt die Umschalt-Einträge („<Ansicht>--item“) nur, ohne sie zu zerstören – beim erneuten
			// Hinzufügen derselben Ansicht gäbe es sonst eine doppelte ID („duplicate id …vWeek--item“).
			for (const view of Object.values(this.views)) Element.getElementById(`${view.getId()}--item`)?.destroy();
			spc.addView(wanted);
			spc.setSelectedView(wanted);
		}
	}

	private anchor(): string { return isoDay(this.model().getProperty("/startDate") as Date); }

	/** Geladener Zeitraum: sichtbare Tage der Ansicht (Monat inkl. angrenzender Wochen, Agenda 30 Tage). */
	private range(): { from: string; to: string } {
		const mode = this.model().getProperty("/mode") as Mode;
		const a = this.anchor();
		if (mode === "tag") return { from: a, to: a };
		if (mode === "woche" || mode === "arbeitswoche") return { from: mondayOf(a), to: addDays(mondayOf(a), 6) };
		if (mode === "agenda") return { from: a, to: addDays(a, 29) };
		const first = `${a.slice(0, 7)}-01`;
		const f = parseDay(first);
		const last = isoDay(new Date(f.getFullYear(), f.getMonth() + 1, 0));
		return { from: mondayOf(first), to: addDays(mondayOf(last), 6) };
	}

	private go(values: { ansicht?: Mode; datum?: string }): void {
		navQuery({ ansicht: values.ansicht ?? String(this.model().getProperty("/mode")), datum: values.datum ?? this.anchor() });
	}

	onMode(e: Event): void {
		const key = String((e.getSource() as Control & { getSelectedKey(): string }).getSelectedKey()) as Mode;
		this.go({ ansicht: key });
	}

	onStartDateChange(e: Event): void {
		const spc = e.getSource() as SinglePlanningCalendar;
		const start = spc.getStartDate() as Date;
		if (isoDay(start) !== this.anchor()) this.go({ datum: isoDay(start) });
	}

	onHeaderDate(e: Event): void {
		this.go({ ansicht: "tag", datum: isoDay(e.getParameter("date" as never) as Date) });
	}

	onMore(e: Event): void {
		this.go({ ansicht: "tag", datum: isoDay(e.getParameter("date" as never) as Date) });
	}

	onMiniSelect(e: Event): void {
		const cal = e.getSource() as UnifiedCalendar;
		const date = cal.getSelectedDates()[0]?.getStartDate();
		if (date) this.go({ datum: isoDay(date as Date) });
	}

	onToday(): void { this.go({ datum: isoDay(new Date()) }); }
	onAgendaPrev(): void { this.go({ datum: addDays(this.anchor(), -30) }); }
	onAgendaNext(): void { this.go({ datum: addDays(this.anchor(), 30) }); }

	private savePrefs(): void {
		const m = this.model();
		writeJson(PREFS_KEY, { colorMode: m.getProperty("/colorMode"), showAssignments: m.getProperty("/showAssignments"), fullDay: m.getProperty("/fullDay") });
	}

	onColorMode(): void {
		this.savePrefs();
		this.render();
	}

	onToggleAssignments(): void {
		this.savePrefs();
		void this.load();
	}

	onToggleCalendar(): void {
		const visible = this.model().getProperty("/calendars") as { id: string; visible: boolean }[];
		writeJson(HIDDEN_KEY, visible.filter((c) => !c.visible).map((c) => c.id));
		this.render();
	}

	private async load(): Promise<void> {
		const m = this.model();
		const seq = ++this.loadSeq;
		const { from, to } = this.range();
		m.setProperty("/busy", true);
		m.setProperty("/error", "");
		try {
			const showAssignments = m.getProperty("/showAssignments") === true && session.has("planning.read");
			const [all, occurrences, lookups] = await Promise.all([
				calendars(true), get<CalendarOccurrence[]>("calendar/events", { from, to }), planningLookups(),
			]);
			// Einsätze der Planung: höchstens 62 Tage je Abfrage.
			const assignments = showAssignments ? (await get<PlanningRange>("planning/range", { from, to: to > addDays(from, 61) ? addDays(from, 61) : to })).assignments : [];
			if (seq !== this.loadSeq) return;
			this.occurrences = occurrences;
			this.assignments = assignments;
			this.crews = lookups.crews;
			const hidden = new Set(readJson<string[]>(HIDDEN_KEY, []));
			m.setProperty("/calendars", all.filter((c) => c.isActive).map((c) => ({ ...c, visible: !hidden.has(c.id) })));
		} catch (error) {
			m.setProperty("/error", (error as Error).message);
		}
		m.setProperty("/busy", false);
		this.render();
		const mini = this.byId("mini") as UnifiedCalendar | undefined;
		if (mini) {
			mini.removeAllSelectedDates();
			mini.addSelectedDate(new DateRange({ startDate: parseDay(this.anchor()) }));
			mini.focusDate(parseDay(this.anchor()));
		}
	}

	private colorOf(o: CalendarOccurrence): string {
		if (o.color) return o.color;
		return this.model().getProperty("/colorMode") === "kind" ? calendarEventKinds[o.kind]?.color ?? o.calendarColor : o.calendarColor;
	}

	private render(): void {
		const m = this.model();
		const visible = new Set((m.getProperty("/calendars") as { id: string; visible: boolean }[]).filter((c) => c.visible).map((c) => c.id));
		const events = this.occurrences.filter((o) => visible.has(o.calendarId));
		const appointments = [
			...events.map((o) => {
				const kind = calendarEventKinds[o.kind];
				return {
					key: `e:${o.eventId}:${o.occurrenceDate ?? ""}:${o.start}`, title: o.title,
					text: [o.location, o.people.length ? o.people.map((p) => p.name).join(", ") : null, o.bauakteTitle].filter(Boolean).join(" · "),
					start: parseLocal(o.start), end: parseLocal(o.end), color: this.colorOf(o), icon: o.isRecurring ? "sap-icon://repost" : kind?.icon ?? "",
					tooltip: [o.title, o.calendarName, kind?.text, o.location, o.description].filter(Boolean).join("\n"),
				};
			}),
			...this.assignments.map((a) => {
				const kind = assignmentKinds[a.kind];
				const span = assignmentSpan(a);
				return {
					key: `a:${a.id}`, title: `${a.bauakteNumber ? `${a.bauakteNumber} ` : ""}${a.bauakteTitle}`,
					text: [a.activity, a.crewName ?? a.people.map((p) => p.name).join(", ")].filter(Boolean).join(" · "),
					start: span.timed ? span.start : parseDay(a.day), end: span.timed ? span.end : parseDay(addDays(a.day, 1)),
					color: a.crewColor ?? kind?.color ?? "#0B2D5B", icon: kind?.icon ?? "sap-icon://building",
					tooltip: ["Einsatz (Planung)", a.activity, a.bauakteAddress, ...a.warnings].filter(Boolean).join("\n"),
				};
			}),
		];
		m.setProperty("/appointments", appointments);
		const mode = m.getProperty("/mode") as Mode;
		const a = parseDay(this.anchor());
		m.setProperty("/title", mode === "monat" ? a.toLocaleDateString("de-DE", { month: "long", year: "numeric" })
			: mode === "agenda" ? `Agenda ab ${a.toLocaleDateString("de-DE", { day: "2-digit", month: "long", year: "numeric" })}` : "Terminkalender");
		m.setProperty("/agenda", appointments.slice().sort((x, y) => x.start.getTime() - y.start.getTime()).map((x) => {
			const allDay = x.start.getHours() === 0 && x.start.getMinutes() === 0 && x.end.getHours() === 0 && x.end.getMinutes() === 0;
			return {
				key: x.key, title: x.title, meta: x.text, color: x.color, icon: x.icon || "sap-icon://appointment", kindText: x.tooltip.split("\n")[1] ?? "",
				day: x.start.toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long" }), time: allDay ? "ganztags" : `${hm(x.start)}–${hm(x.end)}`,
			};
		}));
	}

	// ---- Termine öffnen, anlegen, verschieben -------------------------------------------------------------------

	private parseKey(key: string): { type: string; id: string; occurrence: string | null; start: string | null } {
		const [type, id, occurrence, ...rest] = key.split(":");
		return { type, id, occurrence: occurrence || null, start: rest.length ? rest.join(":") : null };
	}

	private open(key: string): void {
		const k = this.parseKey(key);
		if (k.type === "a") {
			const a = this.assignments.find((x) => x.id === k.id);
			if (a) void openAssignment(draftOf(a, this.crews));
			return;
		}
		void openEventDialog({ eventId: k.id, occurrenceDate: k.occurrence, occurrenceStart: k.start ?? undefined });
	}

	onSelect(e: Event): void {
		const appointment = e.getParameter("appointment" as never) as CalendarAppointment | null;
		if (!appointment) return;
		appointment.setSelected(false);
		this.open(appointment.getKey());
	}

	onAgendaPress(e: Event): void {
		const item = (e.getSource() as Control).getBindingContext("view")?.getObject() as { key: string };
		this.open(item.key);
	}

	onNew(): void {
		const start = new Date();
		start.setMinutes(0, 0, 0);
		// Nächste volle Stunde, aber im sichtbaren Bereich (6–21 Uhr) und am selben Tag – sonst erschiene der neue
		// Termin abends bzw. Sonntagnacht außerhalb der Ansicht (nächster Tag/nächste Woche).
		start.setHours(Math.min(Math.max(start.getHours() + 1, 8), 17));
		const day = parseDay(this.anchor());
		if (isoDay(day) !== isoDay(new Date())) start.setFullYear(day.getFullYear(), day.getMonth(), day.getDate());
		void openEventDialog({ defaults: { start, end: new Date(start.getTime() + 3600_000) } });
	}

	onCreate(e: Event): void {
		const start = e.getParameter("startDate" as never) as Date;
		const end = e.getParameter("endDate" as never) as Date;
		const mode = this.model().getProperty("/mode") as Mode;
		const allDay = mode === "monat" || (start.getHours() === 0 && start.getMinutes() === 0 && end.getHours() === 0 && end.getMinutes() === 0 && end > start);
		void openEventDialog({ defaults: { start, end, allDay } });
	}

	onCell(e: Event): void {
		// Monatsansicht: Klick in einen leeren Tag legt einen ganztägigen Termin an.
		if (this.model().getProperty("/mode") !== "monat") return;
		const start = e.getParameter("startDate" as never) as Date;
		if (start) void openEventDialog({ defaults: { start, end: new Date(start.getTime() + 86400_000), allDay: true } });
	}

	onDrop(e: Event): void {
		const appointment = e.getParameter("appointment" as never) as CalendarAppointment;
		void this.move(appointment.getKey(), e.getParameter("startDate" as never) as Date, e.getParameter("endDate" as never) as Date);
	}

	onResize(e: Event): void {
		const appointment = e.getParameter("appointment" as never) as CalendarAppointment;
		void this.move(appointment.getKey(), e.getParameter("startDate" as never) as Date, e.getParameter("endDate" as never) as Date);
	}

	private async move(key: string, start: Date, end: Date): Promise<void> {
		const k = this.parseKey(key);
		const allDay = start.getHours() === 0 && start.getMinutes() === 0 && end.getHours() === 0 && end.getMinutes() === 0 && end > start;
		if (k.type === "a") {
			const a = this.assignments.find((x) => x.id === k.id);
			if (!a || !session.has("planning.manage")) {
				MessageToast.show("Einsätze plant das Büro.");
				this.render();
				return;
			}
			const pad = (n: number) => String(n).padStart(2, "0");
			const t = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}:00`;
			const saved = await this.run(() => post<AssignmentView>(`planning/assignments/${a.id}/move`, {
				day: isoDay(start), startTime: allDay ? null : t(start), endTime: allDay || isoDay(end) !== isoDay(start) ? null : t(end),
			}));
			if (saved?.warnings.length) MessageToast.show(`Verschoben – ${saved.warnings.join(" ")}`);
			calendarChanged();
			return;
		}
		const o = this.occurrences.find((x) => x.eventId === k.id && x.start === k.start);
		if (!o?.canEdit) {
			MessageToast.show("Diesen Termin dürfen Sie nicht ändern.");
			this.render();
			return;
		}
		let scope = "series";
		if (o.isRecurring) {
			const chosen = await askScope("Verschieben");
			if (!chosen) {
				this.render();
				return;
			}
			scope = chosen;
		}
		const ok = await this.run(() => post(`calendar/events/${o.eventId}/move`, {
			start: localIso(start), end: localIso(end), allDay: allDay || (o.allDay && this.model().getProperty("/mode") === "monat"), occurrenceDate: o.occurrenceDate, scope,
		}));
		if (ok) {
			MessageToast.show(`„${o.title}“ verschoben`);
			calendarChanged();
		} else {
			this.render();
		}
	}

	// ---- Kalender verwalten ---------------------------------------------------------------------------------------

	onAddCalendar(): void { void this.editCalendar(null); }

	onEditCalendar(e: Event): void {
		const c = (e.getSource() as Control).getBindingContext("view")?.getObject() as CalendarResponse;
		void this.editCalendar(c);
	}

	private async editCalendar(c: CalendarResponse | null): Promise<void> {
		const canShared = session.has("planning.manage");
		const result = await openForm<CalendarResponse>({
			title: c ? "Kalender bearbeiten" : "Kalender hinzufügen", width: "30rem",
			initial: c ? { ...c } : { kind: canShared ? "shared" : "personal", color: palette[Math.floor(Math.random() * palette.length)].key, sortOrder: 0 },
			fields: [
				{ key: "name", label: "Name", required: true, maxLength: 100, placeholder: "z. B. Kundentermine, Werkstatt" },
				...(c ? [] : [{ key: "kind", label: "Art", type: "segmented" as const, options: [
					...(canShared ? [{ key: "shared", text: "Gemeinsam (Firma)" }] : []), { key: "personal", text: "Persönlich" }] }]),
				{ key: "color", label: "Farbe", type: "select", required: true, options: palette },
				{ key: "description", label: "Beschreibung", type: "textarea", rows: 2, maxLength: 500 },
				{ key: "sortOrder", label: "Reihenfolge", type: "integer", min: 0, max: 999 },
			],
			leftButton: c ? (dialog) => {
				const b = new Button({
					text: "Löschen", type: "Reject", icon: "sap-icon://delete",
					press: async () => {
						if (!(await this.confirm(`Kalender „${c.name}“ mit allen Terminen löschen?`, { action: "Löschen", danger: true }))) return;
						const ok = await this.run(async () => { await del(`calendar/calendars/${c.id}`); return true; });
						if (ok) {
							dialog.close();
							calendarChanged();
						}
					},
				});
				return b;
			} : undefined,
			submit: (v) => put<CalendarResponse>(`calendar/calendars/${c?.id ?? uuidv7()}`, {
				name: v.name, description: v.description || null, color: v.color, kind: c?.kind ?? v.kind ?? "personal", sortOrder: v.sortOrder ?? 0, isActive: true,
			}),
		});
		if (result) {
			this.toast("Kalender gespeichert");
			calendarChanged();
		}
	}
}
