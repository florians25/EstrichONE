import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import EventBus from "sap/ui/core/EventBus";
import type List from "sap/m/List";
import type SegmentedButton from "sap/m/SegmentedButton";
import MessageToast from "sap/m/MessageToast";
import type { RouteMatch } from "../../util/router";
import { get, put } from "../../util/api";
import { addDays, isoDay, isoWeek, mondayOf, parseDay } from "../../util/days";
import { area } from "../../util/format";
import { assignmentKinds, bauakteStatus, flowStatuses } from "../../util/labels";
import { draftOf, newDraft, openAssignment, planningChanged, planningLookups } from "../../util/assignmentDialog";
import { session } from "../../util/session";
import { startDrag, type BoardData, type Lane } from "../../control/PlanBoard";
import type { AssignmentView, BacklogItem, CrewResponse, DirectoryUser, PlanningRange } from "../../util/models";
import { openEventDialog } from "../../util/calendarDialog";
import { download, type CalendarOccurrence } from "../../util/planningModels";

type Mode = "tag" | "woche" | "monat";
const modes: Mode[] = ["tag", "woche", "monat"];
const titles: Record<Mode, string> = { tag: "Tageseinteilung", woche: "Kolonnenplanung", monat: "Monatsplan" };
const defaults: Record<string, Mode> = { planning: "woche", calendar: "monat", dayPlan: "tag" };
const short = (d: string) => parseDay(d).toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit" });
const weekday = (d: string) => parseDay(d).toLocaleDateString("de-DE", { weekday: "short" });

/** Sichtbare Tage je Ansicht; Monat = volle Wochen (Mo–So) um den Monat. */
export function visibleDays(mode: Mode, anchor: string, fullWeek: boolean): string[] {
	if (mode === "tag") return [anchor];
	if (mode === "woche") {
		const monday = mondayOf(anchor);
		return Array.from({ length: fullWeek ? 7 : 5 }, (_, i) => addDays(monday, i));
	}
	const first = `${anchor.slice(0, 7)}-01`;
	const f = parseDay(first);
	const last = addDays(isoDay(new Date(f.getFullYear(), f.getMonth() + 1, 1)), -1);
	const days: string[] = [];
	for (let d = mondayOf(first); d <= addDays(mondayOf(last), 6); d = addDays(d, 1)) days.push(d);
	return days;
}

/**
 * Disposition: Kolonnenplanung (Woche), Tageseinteilung (Tag), Kalender (Monat) als Plantafel mit Zeilen je Kolonne
 * oder je Mitarbeiter, Ziehen &amp; Ablegen, Abwesenheiten/Konflikte, Baustellen-Vorrat und PDF für Woche/Tag.
 * @namespace estrichone.controller.planning
 */
export default class Planning extends BaseController {
	private range: PlanningRange | null = null;
	private crews: CrewResponse[] = [];
	private people: DirectoryUser[] = [];
	private backlog: BacklogItem[] = [];
	private routeName = "planning";
	private loadedKey = "";

	onInit(): void {
		this.local({
			mode: "woche", anchor: isoDay(new Date()), rows: "kolonnen", fullWeek: false, title: "", subtitle: "", period: "", periodSub: "", todayText: "",
			canPlan: false, busy: false, error: "", warningCount: 0, warningText: "", board: null, legend: [],
			filter: { bauakte: "", crew: "", status: "", kind: "", search: "" },
			bauakteOptions: [], crewOptions: [], statusOptions: [], kindOptions: [],
			backlogOpen: false, backlogMode: "free", backlogSearch: "", backlogShown: [], backlogCounts: { free: 0, planned: 0, all: 0 },
		});
		const m = this.model();
		m.setProperty("/statusOptions", [{ key: "", text: "Alle Status" }, ...flowStatuses.map((s) => ({ key: s, text: bauakteStatus[s].text }))]);
		m.setProperty("/kindOptions", [{ key: "", text: "Alle Terminarten" }, ...Object.entries(assignmentKinds).map(([key, k]) => ({ key, text: k.text }))]);
		m.setProperty("/legend", Object.values(assignmentKinds).map((k) => ({ color: k.color, text: k.text })));
		EventBus.getInstance().subscribe("estrichone", "planningChanged", this.changed, this);
	}

	onExit(): void {
		EventBus.getInstance().unsubscribe("estrichone", "planningChanged", this.changed, this);
	}

	private changed(): void { void this.load(true); }

	onRouteMatched(match: RouteMatch): void {
		this.routeName = match.route.name;
		const m = this.model();
		const mode = match.query.get("ansicht") as Mode | null;
		const date = match.query.get("datum") ?? "";
		m.setProperty("/mode", mode && modes.includes(mode) ? mode : defaults[this.routeName] ?? "woche");
		m.setProperty("/anchor", /^\d{4}-\d{2}-\d{2}$/.test(date) ? date : isoDay(new Date()));
		m.setProperty("/rows", match.query.get("zeilen") === "mitarbeiter" ? "mitarbeiter" : "kolonnen");
		m.setProperty("/canPlan", session.has("planning.manage"));
		void this.load(false);
	}

	private mode(): Mode { return this.model().getProperty("/mode") as Mode; }
	private anchor(): string { return String(this.model().getProperty("/anchor")); }
	private days(): string[] { return visibleDays(this.mode(), this.anchor(), this.model().getProperty("/fullWeek") === true); }

	/** Ansicht, Datum oder Zeilen wechseln: neue Adresse (Deep-Link), Seite lädt passend neu. */
	private go(values: { ansicht?: Mode; datum?: string; zeilen?: string }): void {
		const query = new URLSearchParams(window.location.search);
		for (const [key, value] of Object.entries(values)) {
			if (value) query.set(key, value);
			else query.delete(key);
		}
		const text = query.toString();
		this.nav(`${window.location.pathname}${text ? `?${text}` : ""}`);
	}

	onMode(e: Event): void { this.go({ ansicht: (e.getSource() as SegmentedButton).getSelectedKey() as Mode }); }
	onRows(e: Event): void { this.go({ zeilen: (e.getSource() as SegmentedButton).getSelectedKey() }); }
	onFullWeek(): void { void this.load(false); }
	onToday(): void { this.go({ datum: isoDay(new Date()) }); }
	onPick(e: Event): void {
		const v = String(e.getParameter("value" as never) ?? "");
		if (/^\d{4}-\d{2}-\d{2}$/.test(v)) this.go({ datum: v });
	}
	onPrev(): void { this.shift(-1); }
	onNext(): void { this.shift(1); }
	onDay(e: Event): void { this.go({ ansicht: "tag", datum: String(e.getParameter("day" as never)) }); }
	onRefresh(): void { void this.load(true); }
	onResources(): void { this.nav("/planung/kolonnen"); }
	onFilter(): void { this.render(); }

	private shift(direction: number): void {
		const mode = this.mode();
		if (mode === "monat") {
			const d = parseDay(this.anchor());
			this.go({ datum: isoDay(new Date(d.getFullYear(), d.getMonth() + direction, 1)) });
		} else {
			this.go({ datum: addDays(this.anchor(), direction * (mode === "tag" ? 1 : 7)) });
		}
	}

	private async load(force: boolean): Promise<void> {
		const m = this.model();
		const days = this.days();
		const key = `${days[0]}|${days[days.length - 1]}`;
		this.header(days);
		if (!force && key === this.loadedKey && this.range) {
			this.render();
			return;
		}
		m.setProperty("/busy", true);
		m.setProperty("/error", "");
		try {
			const [range, lookups, people, backlog] = await Promise.all([
				get<PlanningRange>("planning/range", { from: days[0], to: days[days.length - 1] }),
				planningLookups(force),
				session.people(),
				get<BacklogItem[]>("planning/backlog").catch(() => [] as BacklogItem[]),
			]);
			this.range = range;
			this.crews = lookups.crews.filter((c) => c.isActive);
			this.people = people.filter((p) => p.isActive);
			this.backlog = backlog;
			this.loadedKey = key;
		} catch (error) {
			m.setProperty("/error", (error as Error).message);
		}
		m.setProperty("/busy", false);
		this.render();
		this.renderBacklog();
	}

	private header(days: string[]): void {
		const m = this.model();
		const mode = this.mode();
		const anchor = this.anchor();
		m.setProperty("/title", titles[mode]);
		m.setProperty("/subtitle", mode === "monat" ? "Alle Einsätze des Monats" : m.getProperty("/rows") === "mitarbeiter" ? "Dienstplan je Mitarbeiter" : "Einsätze je Kolonne");
		m.setProperty("/todayText", mode === "tag" ? "Heute" : mode === "woche" ? "Diese Woche" : "Dieser Monat");
		m.setProperty("/period", mode === "tag"
			? parseDay(anchor).toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long", year: "numeric" })
			: mode === "woche" ? `KW ${isoWeek(days[0])}` : parseDay(anchor).toLocaleDateString("de-DE", { month: "long", year: "numeric" }));
		m.setProperty("/periodSub", mode === "woche" ? `${short(days[0])} – ${short(days[days.length - 1])} ${parseDay(days[days.length - 1]).getFullYear()}` : "");
	}

	private filtered(): AssignmentView[] {
		const f = this.model().getProperty("/filter") as { bauakte: string; crew: string; status: string; kind: string; search: string };
		const q = (f.search ?? "").trim().toLowerCase();
		return (this.range?.assignments ?? []).filter((a) => (!f.bauakte || a.bauakteId === f.bauakte)
			&& (!f.crew || (f.crew === "none" ? !a.crewId : a.crewId === f.crew))
			&& (!f.status || a.bauakteStatus === f.status)
			&& (!f.kind || a.kind === f.kind)
			&& (!q || [a.bauakteNumber, a.bauakteTitle, a.bauakteAddress, a.activity, a.crewName, ...a.people.map((p) => p.name)].some((v) => v?.toLowerCase().includes(q))));
	}

	private lanes(assignments: AssignmentView[]): Lane[] {
		const m = this.model();
		const crewFilter = String(m.getProperty("/filter/crew") ?? "");
		if (m.getProperty("/rows") === "mitarbeiter") {
			const ids = crewFilter && crewFilter !== "none" ? new Set(this.crews.find((c) => c.id === crewFilter)?.memberUserIds ?? []) : null;
			return this.people.filter((p) => !ids || ids.has(p.id)).map((p) => {
				const crew = this.crews.find((c) => c.memberUserIds.includes(p.id) || c.leaderUserId === p.id);
				const count = assignments.filter((a) => a.people.some((x) => x.id === p.id)).length;
				return { id: p.id, name: p.displayName, color: crew?.color ?? null, userIds: [p.id], sub: [crew?.name, count ? `${count} ${count === 1 ? "Einsatz" : "Einsätze"}` : "frei"].filter(Boolean).join(" · ") };
			});
		}
		const name = (id: string) => this.people.find((p) => p.id === id)?.displayName ?? "?";
		const lanes: Lane[] = this.crews.filter((c) => !crewFilter || crewFilter === c.id).map((c) => {
			const count = assignments.filter((a) => a.crewId === c.id).length;
			const members = [...new Set([c.leaderUserId, ...c.memberUserIds].filter((x): x is string => !!x))];
			return {
				id: c.id, name: c.name, color: c.color, userIds: members,
				sub: `${count === 0 ? "Kapazität offen" : `${count} ${count === 1 ? "Einsatz" : "Einsätze"}`}${members.length ? ` · ${members.map(name).join(", ")}` : ""}`,
			};
		});
		if (!crewFilter || crewFilter === "none") lanes.push({ id: null, name: "Termine ohne Kolonne", color: null, userIds: [], sub: "Einzelne Mitarbeiter" });
		return lanes;
	}

	private render(): void {
		const m = this.model();
		const days = this.days();
		const all = this.range?.assignments ?? [];
		const assignments = this.filtered();
		const mode = this.mode();
		const board: BoardData = {
			mode: mode === "tag" ? "day" : mode === "woche" ? "week" : "month",
			rows: m.getProperty("/rows") === "mitarbeiter" && mode !== "monat" ? "people" : "crews",
			days, today: isoDay(new Date()), month: this.anchor().slice(0, 7), canPlan: m.getProperty("/canPlan") === true,
			lanes: this.lanes(assignments), assignments, absences: this.range?.absences ?? [],
			appointments: (this.range as (PlanningRange & { appointments?: CalendarOccurrence[] | null }) | null)?.appointments ?? [],
		};
		m.setProperty("/board", board);
		const warnings = assignments.filter((a) => a.warnings.length > 0).length;
		m.setProperty("/warningCount", warnings);
		m.setProperty("/warningText", `${warnings} ${warnings === 1 ? "Einsatz hat" : "Einsätze haben"} Hinweise (z. B. Urlaub, Doppelbelegung oder Gerät mehrfach verplant).`);
		m.setProperty("/bauakteOptions", [{ key: "", text: "Alle Bauakten" }, ...[...new Map(all.map((a) => [a.bauakteId, `${a.bauakteNumber ?? "neu"} · ${a.bauakteTitle}`])).entries()].map(([key, text]) => ({ key, text }))]);
		m.setProperty("/crewOptions", [{ key: "", text: "Alle Kolonnen" }, ...this.crews.map((c) => ({ key: c.id, text: c.name })), { key: "none", text: "Termine ohne Kolonne" }]);
	}

	// ---- Einsätze -------------------------------------------------------------------------------------------------

	private find(id: string): AssignmentView | undefined {
		return this.range?.assignments.find((a) => a.id === id);
	}

	onOpen(e: Event): void {
		const a = this.find(String(e.getParameter("id" as never)));
		if (a) void openAssignment(draftOf(a, this.crews));
	}

	onAdd(e: Event): void {
		const laneId = String(e.getParameter("laneId" as never) || "") || null;
		const day = String(e.getParameter("day" as never));
		const draft = newDraft(day, null);
		if (this.model().getProperty("/rows") === "mitarbeiter" && this.mode() !== "monat") {
			if (laneId) draft.userIds = [laneId];
		} else {
			draft.crewId = laneId;
		}
		void openAssignment(draft);
	}

	onNew(): void {
		const days = this.days();
		const today = isoDay(new Date());
		void openAssignment(newDraft(days.includes(today) ? today : days[0], null));
	}

	/** C1: Termin ohne Bauvorhaben (Schulung, Werkstatt …) – landet im Kalender und erscheint im Plan der Teilnehmer. */
	onNewEvent(): void {
		const days = this.days();
		const today = isoDay(new Date());
		const day = days.includes(today) ? today : days[0];
		const start = parseDay(day);
		start.setHours(7, 0, 0, 0);
		void openEventDialog({ defaults: { start, end: new Date(start.getTime() + 2 * 3600_000) } });
	}

	onOpenEvent(e: Event): void {
		void openEventDialog({
			eventId: String(e.getParameter("eventId" as never)),
			occurrenceDate: String(e.getParameter("occurrenceDate" as never) || "") || null,
			occurrenceStart: String(e.getParameter("start" as never)),
		});
	}

	/** Einsatz auf anderen Tag, andere Kolonne oder anderen Mitarbeiter ziehen; zusätzliche Mitarbeiter und Geräte bleiben erhalten. */
	async onMove(e: Event): Promise<void> {
		const a = this.find(String(e.getParameter("id" as never)));
		if (!a) return;
		const day = String(e.getParameter("day" as never));
		const target = String(e.getParameter("laneId" as never) || "") || null;
		const byPeople = this.model().getProperty("/rows") === "mitarbeiter" && this.mode() !== "monat";
		const monthView = this.mode() === "monat";
		const crewMembers = new Set(this.crews.find((c) => c.id === a.crewId)?.memberUserIds ?? []);
		let crewId = monthView || byPeople ? a.crewId : target;
		let extras = a.people.map((p) => p.id).filter((id) => !crewMembers.has(id));
		if (byPeople) {
			const from = String(e.getParameter("fromLaneId" as never) || "");
			const source = a.people.find((p) => p.id === from)?.id ?? null;
			if (target && !a.people.some((p) => p.id === target)) {
				// Person tauschen: aus der Kolonne gezogen → Einsatz bleibt bei der Kolonne, Person kommt zusätzlich dazu.
				extras = [...extras.filter((id) => id !== source), target];
				if (source && crewMembers.has(source)) crewId = a.crewId;
			}
		}
		if (a.day === day && (a.crewId ?? null) === (crewId ?? null) && extras.join() === a.people.map((p) => p.id).filter((id) => !crewMembers.has(id)).join()) return;
		if (!crewId && extras.length === 0) {
			MessageToast.show("Ohne Kolonne braucht der Einsatz mindestens einen Mitarbeiter. Bitte im Einsatz Mitarbeiter wählen.");
			return;
		}
		const saved = await this.run(() => put<AssignmentView>(`planning/assignments/${a.id}`, {
			bauakteId: a.bauakteId, kind: a.kind, day, startTime: a.startTime, endTime: a.endTime, activity: a.activity, crewId,
			userIds: extras, resourceIds: a.resources.map((r) => r.id), notes: a.notes,
		}));
		if (!saved) return;
		MessageToast.show(saved.warnings.length ? `Verschoben – ${saved.warnings.join(" ")}` : `„${a.activity}“ auf ${weekday(day)}, ${short(day)} verschoben`);
		planningChanged();
	}

	onDropBauakte(e: Event): void {
		const b = e.getParameter("bauakte" as never) as { id: string; label: string };
		const laneId = String(e.getParameter("laneId" as never) || "") || null;
		const draft = newDraft(String(e.getParameter("day" as never)), null, b);
		if (this.model().getProperty("/rows") === "mitarbeiter" && this.mode() !== "monat") {
			if (laneId) draft.userIds = [laneId];
		} else {
			draft.crewId = laneId;
		}
		void openAssignment(draft);
	}

	// ---- Vorrat ---------------------------------------------------------------------------------------------------

	onBacklog(): void {
		const m = this.model();
		m.setProperty("/backlogOpen", !m.getProperty("/backlogOpen"));
	}

	onBacklogFilter(): void { this.renderBacklog(); }

	private renderBacklog(): void {
		const m = this.model();
		const mode = String(m.getProperty("/backlogMode"));
		const q = String(m.getProperty("/backlogSearch") ?? "").trim().toLowerCase();
		const items = this.backlog;
		m.setProperty("/backlogCounts", {
			free: items.filter((b) => b.upcomingAssignments === 0).length,
			planned: items.filter((b) => b.upcomingAssignments > 0).length,
			all: items.length,
		});
		m.setProperty("/backlogShown", items
			.filter((b) => (mode === "all" || (mode === "free") === (b.upcomingAssignments === 0))
				&& (!q || [b.number, b.title, b.city, b.customerName].some((v) => v?.toLowerCase().includes(q))))
			.map((b) => ({
				...b,
				meta: [b.customerName, [b.postalCode, b.city].filter(Boolean).join(" ")].filter(Boolean).join(" · ") || "–",
				meta2: [b.number ?? "neu", b.totalAreaSquareMeters > 0 ? area(b.totalAreaSquareMeters) : "", b.screedTypes.join(", ")].filter(Boolean).join(" · "),
				statusText: bauakteStatus[b.status]?.text ?? b.status,
				planText: b.nextAssignment ? `ab ${short(b.nextAssignment)}` : "frei",
			})));
	}

	/** Vorrat-Einträge ziehbar machen (Ablegen auf der Plantafel legt einen Einsatz an). */
	onBacklogRendered(): void {
		if (this.model().getProperty("/canPlan") !== true) return;
		for (const item of (this.byId("backlogList") as List).getItems()) {
			const dom = item.getDomRef() as HTMLElement | null;
			if (!dom || dom.dataset.drag) continue;
			dom.dataset.drag = "1";
			dom.classList.add("eoBacklogDraggable");
			dom.addEventListener("pointerdown", (ev) => {
				const b = item.getBindingContext("view")?.getObject() as BacklogItem | undefined;
				if (b) startDrag(ev, dom, { kind: "bauakte", bauakte: { id: b.bauakteId, label: `${b.number ?? "neu"} · ${b.title}` } });
			});
		}
	}

	onPlanBacklog(e: Event): void {
		const b = (e.getSource() as Control).getBindingContext("view")?.getObject() as BacklogItem;
		const days = this.days();
		const today = isoDay(new Date());
		void openAssignment(newDraft(days.includes(today) ? today : days[0], null, { id: b.bauakteId, label: `${b.number ?? "neu"} · ${b.title}` }));
	}

	// ---- PDF ------------------------------------------------------------------------------------------------------

	/** Kolonnenplan als PDF vom Server (Woche quer bzw. Tag hoch, Zeilen wie in der Ansicht). */
	onPdf(): void {
		const days = this.days();
		const day = this.mode() === "tag";
		const rows = this.model().getProperty("/rows") === "mitarbeiter" ? "people" : "crews";
		download(`/api/v1/planning/print.pdf?mode=${day ? "day" : "week"}&from=${day ? days[0] : mondayOf(days[0])}&rows=${rows}&weekend=${this.model().getProperty("/fullWeek") === true}`).catch((error: unknown) => this.showError(error));
	}


}
