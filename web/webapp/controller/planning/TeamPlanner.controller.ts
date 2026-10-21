import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import EventBus from "sap/ui/core/EventBus";
import type CalendarAppointment from "sap/ui/unified/CalendarAppointment";
import type PlanningCalendarRow from "sap/m/PlanningCalendarRow";
import type PlanningCalendar from "sap/m/PlanningCalendar";
import DateTypeRange from "sap/ui/unified/DateTypeRange";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import MessageToast from "sap/m/MessageToast";
import type { RouteMatch } from "../../util/router";
import { get, post } from "../../util/api";
import { addDays, isoDay, mondayOf, parseDay } from "../../util/days";
import { absenceKinds, assignmentKinds } from "../../util/labels";
import { draftOf, newDraft, openAssignment, planningChanged, planningLookups } from "../../util/assignmentDialog";
import { askScope, openEventDialog } from "../../util/calendarDialog";
import { session } from "../../util/session";
import { assignmentSpan, calendarEventKinds, localIso, parseLocal, type CalendarOccurrence } from "../../util/planningModels";
import { download, navQuery } from "../../util/planningModels";
import type { AbsenceView, AssignmentView, CrewResponse, DirectoryUser, PlanningRange } from "../../util/models";

type ViewKey = "tag" | "woche" | "zweiwochen" | "monat";
type Range = PlanningRange & { appointments?: CalendarOccurrence[] | null };

interface Appt {
	key: string; start: Date; end: Date; title: string; text: string; icon: string; color: string; tooltip: string; tentative: boolean;
}
interface Row { key: string; title: string; text: string; icon: string; appointments: Appt[]; headers: Appt[] }

const viewKeys: ViewKey[] = ["tag", "woche", "zweiwochen", "monat"];
const pad = (n: number) => String(n).padStart(2, "0");
/** Auf 15 Minuten runden (Ziehen im Stundenraster). */
const snap = (d: Date) => {
	const r = new Date(d);
	r.setMinutes(Math.round(r.getMinutes() / 15) * 15, 0, 0);
	return r;
};
const timeOf = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}:00`;
const hm = (d: Date) => d.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });

/**
 * Teamplaner (C2): Zeilen = Kolonnen oder Mitarbeiter, Zeitachse in Stunden (Tag) bzw. Tagen (Woche, 2 Wochen, Monat).
 * Einsätze und Termine per Ziehen verschieben (auch in eine andere Zeile), Dauer ziehen, im leeren Bereich neu anlegen.
 * Konflikte (Doppelbelegung, Abwesenheit, Termin zur selben Zeit) markiert und gesammelt.
 * @namespace estrichone.controller.planning
 */
export default class TeamPlanner extends BaseController {
	private range: Range | null = null;
	private crews: CrewResponse[] = [];
	private people: DirectoryUser[] = [];

	onInit(): void {
		this.local({
			startDate: new Date(), viewKey: "tag", rowsMode: "kolonnen", canPlan: false, busy: false, error: "", rows: [], conflicts: [], conflictText: "",
			subtitle: "", search: "", showEvents: true, showAbsences: true,
			legend: [...Object.values(assignmentKinds).map((k) => ({ color: k.color, text: k.text })), { color: calendarEventKinds.appointment.color, text: "Termin (Kalender)" }],
		});
		EventBus.getInstance().subscribe("estrichone", "planningChanged", this.changed, this);
	}

	onExit(): void {
		EventBus.getInstance().unsubscribe("estrichone", "planningChanged", this.changed, this);
	}

	private changed(): void { void this.load(); }

	onRouteMatched(match: RouteMatch): void {
		const m = this.model();
		const view = match.query.get("ansicht") as ViewKey | null;
		const date = match.query.get("datum") ?? "";
		const key: ViewKey = view && viewKeys.includes(view) ? view : "tag";
		m.setProperty("/viewKey", key);
		m.setProperty("/rowsMode", match.query.get("zeilen") === "mitarbeiter" ? "mitarbeiter" : "kolonnen");
		m.setProperty("/canPlan", session.has("planning.manage"));
		m.setProperty("/startDate", this.startFor(key, /^\d{4}-\d{2}-\d{2}$/.test(date) ? date : isoDay(new Date())));
		void this.load();
	}

	/** Anfang der Zeitachse: Tag ab 6 Uhr, Wochen ab Montag, Monat ab dem 1. */
	private startFor(view: ViewKey, day: string): Date {
		if (view === "tag") {
			const d = parseDay(day);
			d.setHours(6, 0, 0, 0);
			return d;
		}
		if (view === "monat") return parseDay(`${day.slice(0, 7)}-01`);
		return parseDay(mondayOf(day));
	}

	private days(): { from: string; to: string } {
		const m = this.model();
		const start = m.getProperty("/startDate") as Date;
		const from = isoDay(start);
		const view = m.getProperty("/viewKey") as ViewKey;
		if (view === "tag") return { from, to: from };
		if (view === "woche") return { from, to: addDays(from, 6) };
		if (view === "zweiwochen") return { from, to: addDays(from, 13) };
		return { from, to: isoDay(new Date(start.getFullYear(), start.getMonth() + 1, 0)) };
	}

	private go(values: { ansicht?: string; datum?: string; zeilen?: string }): void {
		const m = this.model();
		navQuery({
			ansicht: values.ansicht ?? String(m.getProperty("/viewKey")),
			datum: values.datum ?? isoDay(m.getProperty("/startDate") as Date),
			zeilen: (values.zeilen ?? m.getProperty("/rowsMode")) === "mitarbeiter" ? "mitarbeiter" : null,
		});
	}

	onStartDateChange(e: Event): void {
		const pc = e.getSource() as PlanningCalendar;
		this.go({ datum: isoDay(pc.getStartDate() as Date) });
	}

	onViewChange(e: Event): void {
		const pc = e.getSource() as PlanningCalendar;
		const view = pc.getViewKey() as ViewKey;
		const start = this.startFor(view, isoDay(pc.getStartDate() as Date));
		this.go({ ansicht: view, datum: isoDay(start) });
	}

	onRowsMode(): void { this.go({}); }
	onRefresh(): void { void this.load(); }
	onToggle(): void { this.render(); }
	onSearch(): void { this.render(); }

	private async load(): Promise<void> {
		const m = this.model();
		const { from, to } = this.days();
		m.setProperty("/busy", true);
		m.setProperty("/error", "");
		try {
			const [range, lookups, people] = await Promise.all([get<Range>("planning/range", { from, to }), planningLookups(), session.people()]);
			this.range = range;
			this.crews = lookups.crews.filter((c) => c.isActive);
			this.people = people.filter((p) => p.isActive);
		} catch (error) {
			m.setProperty("/error", (error as Error).message);
		}
		m.setProperty("/busy", false);
		this.render();
	}

	private assignmentAppt(a: AssignmentView): Appt {
		const kind = assignmentKinds[a.kind];
		const { start, end, timed } = assignmentSpan(a);
		const warn = a.warnings.length > 0;
		return {
			key: `a:${a.id}`, start, end, title: `${warn ? "⚠ " : ""}${a.bauakteNumber ? `${a.bauakteNumber} ` : ""}${a.bauakteTitle}`,
			text: [a.activity, timed ? (a.endTime ? "" : "Ende offen") : "ohne Uhrzeit"].filter(Boolean).join(" · "),
			icon: kind?.icon ?? "sap-icon://building", color: warn ? "#AA0808" : a.crewColor && this.model().getProperty("/rowsMode") === "mitarbeiter" ? a.crewColor : kind?.color ?? "#0B2D5B",
			tooltip: [a.activity, a.bauakteAddress, a.people.map((p) => p.name).join(", "), a.resources.map((r) => r.name).join(", "), ...a.warnings].filter(Boolean).join("\n"),
			tentative: !timed,
		};
	}

	private eventAppt(o: CalendarOccurrence): Appt {
		const kind = calendarEventKinds[o.kind];
		return {
			key: `e:${o.eventId}:${o.occurrenceDate ?? ""}:${o.start}`, start: parseLocal(o.start), end: parseLocal(o.end), title: o.title,
			text: [kind?.text, o.location].filter(Boolean).join(" · "), icon: kind?.icon ?? "sap-icon://appointment",
			color: o.color ?? o.calendarColor ?? kind?.color ?? "#0064D9",
			tooltip: [o.title, o.location, o.calendarName, o.people.map((p) => p.name).join(", ")].filter(Boolean).join("\n"), tentative: false,
		};
	}

	private absenceAppt(a: AbsenceView, withName: boolean): Appt {
		const text = `${withName ? `${a.userName}: ` : ""}${absenceKinds[a.kind] ?? a.kind}`;
		return {
			key: `x:${a.id}`, start: parseDay(a.from), end: parseDay(addDays(a.to, 1)), title: text, text: a.note ?? "", icon: "sap-icon://general-leave-request",
			color: a.kind === "sick" ? "#AA0808" : "#8D6E00", tooltip: [text, a.note].filter(Boolean).join("\n"), tentative: false,
		};
	}

	private render(): void {
		const m = this.model();
		const assignments = this.range?.assignments ?? [];
		const events = m.getProperty("/showEvents") ? this.range?.appointments ?? [] : [];
		const absences = m.getProperty("/showAbsences") ? this.range?.absences ?? [] : [];
		const q = String(m.getProperty("/search") ?? "").trim().toLowerCase();
		const byPeople = m.getProperty("/rowsMode") === "mitarbeiter";
		const name = (id: string | null) => this.people.find((p) => p.id === id)?.displayName ?? "?";
		let rows: Row[];
		if (byPeople) {
			rows = this.people.map((p) => {
				const crew = this.crews.find((c) => c.memberUserIds.includes(p.id));
				return {
					key: p.id, title: p.displayName, text: crew?.name ?? "ohne Kolonne", icon: "sap-icon://employee",
					appointments: [...assignments.filter((a) => a.people.some((x) => x.id === p.id)).map((a) => this.assignmentAppt(a)),
						...events.filter((o) => o.people.some((x) => x.id === p.id)).map((o) => this.eventAppt(o))],
					headers: absences.filter((a) => a.userId === p.id).map((a) => this.absenceAppt(a, false)),
				};
			});
		} else {
			rows = this.crews.map((c) => {
				const members = [...new Set([c.leaderUserId, ...c.memberUserIds].filter((x): x is string => !!x))];
				return {
					key: c.id, title: c.name, text: members.map(name).join(", ") || "keine Mitglieder", icon: "sap-icon://group",
					appointments: [...assignments.filter((a) => a.crewId === c.id).map((a) => this.assignmentAppt(a)),
						...events.filter((o) => o.crews.some((x) => x.id === c.id)).map((o) => this.eventAppt(o))],
					headers: absences.filter((a) => members.includes(a.userId)).map((a) => this.absenceAppt(a, true)),
				};
			});
			rows.push({
				key: "none", title: "Einzelne Mitarbeiter", text: "Einsätze und Termine ohne Kolonne", icon: "sap-icon://person-placeholder",
				appointments: [...assignments.filter((a) => !a.crewId).map((a) => this.assignmentAppt(a)),
					...events.filter((o) => o.crews.length === 0).map((o) => this.eventAppt(o))],
				headers: [],
			});
		}
		if (q) rows = rows.filter((r) => `${r.title} ${r.text}`.toLowerCase().includes(q));
		// Feiertage (Bundesland aus den Personaleinstellungen) als arbeitsfreie Tage markieren.
		const pc = this.byId("pc") as PlanningCalendar | undefined;
		if (pc) {
			pc.destroySpecialDates();
			for (const h of (this.range as (Range & { holidays?: { date: string; name: string }[] | null }) | null)?.holidays ?? []) {
				pc.addSpecialDate(new DateTypeRange({ startDate: parseDay(h.date), type: "NonWorking", tooltip: h.name }));
			}
		}
		m.setProperty("/rows", rows);
		const conflicts = assignments.filter((a) => a.warnings.length > 0);
		m.setProperty("/conflicts", conflicts.map((a) => ({ id: a.id, title: `${a.day.split("-").reverse().join(".")} · ${a.bauakteTitle} · ${a.activity}`, text: a.warnings.join(" ") })));
		m.setProperty("/conflictText", `${conflicts.length} ${conflicts.length === 1 ? "Einsatz hat" : "Einsätze haben"} Konflikte (Doppelbelegung, Abwesenheit oder Termin zur selben Zeit).`);
		const { from, to } = this.days();
		m.setProperty("/subtitle", `${byPeople ? "Mitarbeiter" : "Kolonnen"} · ${from === to ? parseDay(from).toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long", year: "numeric" })
			: `${parseDay(from).toLocaleDateString("de-DE")} – ${parseDay(to).toLocaleDateString("de-DE")}`} · ${assignments.length} Einsätze, ${(this.range?.appointments ?? []).length} Termine`);
	}

	// ---- Bearbeiten -------------------------------------------------------------------------------------------------

	private parseKey(key: string): { type: "a" | "e" | "x"; id: string; occurrence: string | null; start: string | null } {
		const [type, id, occurrence, ...rest] = key.split(":");
		return { type: type as "a" | "e" | "x", id, occurrence: occurrence || null, start: rest.length ? rest.join(":") : null };
	}

	onSelect(e: Event): void {
		const appointment = e.getParameter("appointment" as never) as CalendarAppointment | null;
		if (!appointment) return;
		const k = this.parseKey(appointment.getKey());
		appointment.setSelected(false);
		if (k.type === "a") {
			const a = this.range?.assignments.find((x) => x.id === k.id);
			if (a) void openAssignment(draftOf(a, this.crews));
		} else if (k.type === "e") {
			void openEventDialog({ eventId: k.id, occurrenceDate: k.occurrence, occurrenceStart: k.start ?? undefined });
		} else {
			this.nav("/planung/ressourcen?reiter=abwesenheiten");
		}
	}

	onDrop(e: Event): void {
		const appointment = e.getParameter("appointment" as never) as CalendarAppointment;
		const target = e.getParameter("calendarRow" as never) as PlanningCalendarRow;
		const source = appointment.getParent() as PlanningCalendarRow;
		void this.change(appointment, e.getParameter("startDate" as never) as Date, e.getParameter("endDate" as never) as Date, source.getKey(), target.getKey());
	}

	onResize(e: Event): void {
		const appointment = e.getParameter("appointment" as never) as CalendarAppointment;
		const row = appointment.getParent() as PlanningCalendarRow;
		void this.change(appointment, e.getParameter("startDate" as never) as Date, e.getParameter("endDate" as never) as Date, row.getKey(), row.getKey());
	}

	/** Verschieben bzw. Dauer ändern; Zeilenwechsel = andere Kolonne bzw. Person tauschen. */
	private async change(appointment: CalendarAppointment, startDate: Date, endDate: Date, fromRow: string, toRow: string): Promise<void> {
		const k = this.parseKey(appointment.getKey());
		const view = this.model().getProperty("/viewKey") as ViewKey;
		const hourly = view === "tag";
		let start = snap(startDate);
		let end = snap(endDate);
		if (k.type === "x") {
			MessageToast.show("Abwesenheiten bitte unter „Kolonnen und Geräte“ ändern.");
			this.render();
			return;
		}
		if (k.type === "a") {
			const a = this.range?.assignments.find((x) => x.id === k.id);
			if (!a) return;
			if (!hourly) {
				// In Tagesansichten bleibt die Uhrzeit, nur der Tag ändert sich.
				const span = assignmentSpan(a);
				const shiftDays = Math.round((parseDay(isoDay(startDate)).getTime() - parseDay(a.day).getTime()) / 86400_000);
				start = new Date(span.start.getTime() + shiftDays * 86400_000);
				end = new Date(span.end.getTime() + shiftDays * 86400_000);
				if (isoDay(end) !== isoDay(start)) end = parseLocal(`${isoDay(start)}T23:45:00`);
			}
			if (isoDay(end) !== isoDay(start)) end = parseLocal(`${isoDay(start)}T23:45:00`);
			const body: Record<string, unknown> = {
				day: isoDay(start), startTime: hourly || a.startTime ? timeOf(start) : null, endTime: hourly || a.endTime ? timeOf(end) : null,
			};
			if (fromRow !== toRow) {
				if (this.model().getProperty("/rowsMode") === "mitarbeiter") {
					body.fromUserId = fromRow;
					body.toUserId = toRow;
				} else {
					body.changeCrew = true;
					body.toCrewId = toRow === "none" ? null : toRow;
				}
			}
			const saved = await this.run(() => post<AssignmentView>(`planning/assignments/${a.id}/move`, body));
			if (saved) {
				MessageToast.show(saved.warnings.length ? `Verschoben – Konflikt: ${saved.warnings.join(" ")}` : `„${saved.activity}“ ${parseDay(saved.day).toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit" })} ${saved.startTime ? hm(parseLocal(`${saved.day}T${saved.startTime}`)) : ""} geplant`);
				planningChanged();
			} else {
				this.render();
			}
			return;
		}
		const o = this.range?.appointments?.find((x) => x.eventId === k.id && x.start === k.start);
		if (!o || !o.canEdit) {
			MessageToast.show("Diesen Termin dürfen Sie nicht verschieben.");
			this.render();
			return;
		}
		if (!hourly) {
			const shift = parseDay(isoDay(startDate)).getTime() - parseDay(o.start.slice(0, 10)).getTime();
			start = new Date(parseLocal(o.start).getTime() + shift);
			end = new Date(parseLocal(o.end).getTime() + shift);
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
		if (fromRow !== toRow) MessageToast.show("Termine wechseln die Zeile nicht – Teilnehmer bitte im Termin ändern.");
		const ok = await this.run(() => post(`calendar/events/${o.eventId}/move`, {
			start: localIso(start), end: localIso(end), allDay: o.allDay, occurrenceDate: o.occurrenceDate, scope,
		}));
		if (ok) planningChanged();
		else this.render();
	}

	onCreate(e: Event): void {
		const row = e.getParameter("calendarRow" as never) as PlanningCalendarRow;
		this.create(e.getParameter("startDate" as never) as Date, e.getParameter("endDate" as never) as Date, row.getKey());
	}

	onInterval(e: Event): void {
		if (this.model().getProperty("/canPlan") !== true) return;
		const row = e.getParameter("row" as never) as PlanningCalendarRow | null;
		const start = e.getParameter("startDate" as never) as Date;
		const end = e.getParameter("endDate" as never) as Date;
		if (row && start && end) this.create(start, new Date(Math.min(end.getTime(), start.getTime() + 2 * 3600_000) || start.getTime() + 3600_000), row.getKey());
	}

	private create(startDate: Date, endDate: Date, rowKey: string): void {
		const hourly = this.model().getProperty("/viewKey") === "tag";
		const start = hourly ? snap(startDate) : parseLocal(`${isoDay(startDate)}T07:00:00`);
		const end = hourly ? snap(endDate) : parseLocal(`${isoDay(startDate)}T16:00:00`);
		const draft = newDraft(isoDay(start), null);
		draft.startTime = timeOf(start);
		draft.endTime = isoDay(end) === isoDay(start) && end > start ? timeOf(end) : null;
		if (this.model().getProperty("/rowsMode") === "mitarbeiter") draft.userIds = [rowKey];
		else if (rowKey !== "none") draft.crewId = rowKey;
		void openAssignment(draft);
	}

	onNewAssignment(): void {
		const day = this.days().from;
		const draft = newDraft(day === isoDay(new Date()) || this.model().getProperty("/viewKey") === "tag" ? day : isoDay(new Date()), null);
		void openAssignment(draft);
	}

	onNewEvent(): void {
		const start = parseLocal(`${this.days().from}T08:00:00`);
		void openEventDialog({ defaults: { start, end: new Date(start.getTime() + 3600_000) } });
	}

	onRowHeader(e: Event): void {
		const row = e.getParameter("row" as never) as PlanningCalendarRow | null;
		if (!row || this.model().getProperty("/rowsMode") !== "kolonnen") return;
		this.nav("/planung/ressourcen");
	}

	onConflicts(): void {
		const list = new List({ items: (this.model().getProperty("/conflicts") as { id: string; title: string; text: string }[]).map((c) => new StandardListItem({
			title: c.title, description: c.text, icon: "sap-icon://alert", type: "Navigation", wrapping: true,
			press: () => {
				dialog.close();
				const a = this.range?.assignments.find((x) => x.id === c.id);
				if (a) void openAssignment(draftOf(a, this.crews));
			},
		})) });
		const dialog = new Dialog({
			title: "Konflikte", contentWidth: "36rem", content: [list],
			endButton: new Button({ text: "Schließen", press: () => dialog.close() }), afterClose: () => dialog.destroy(),
		});
		dialog.open();
	}

	onPdf(): void {
		download(`/api/v1/planning/print.pdf?mode=day&from=${this.days().from}&rows=${this.model().getProperty("/rowsMode") === "mitarbeiter" ? "people" : "crews"}`).catch((error: unknown) => this.showError(error));
	}
}
