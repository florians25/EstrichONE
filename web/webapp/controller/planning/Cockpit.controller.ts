import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import EventBus from "sap/ui/core/EventBus";
import type SegmentedButton from "sap/m/SegmentedButton";
import MessageToast from "sap/m/MessageToast";
import MessageBox from "sap/m/MessageBox";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import TextArea from "sap/m/TextArea";
import Select from "sap/m/Select";
import MultiComboBox from "sap/m/MultiComboBox";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import List from "sap/m/List";
import CustomListItem from "sap/m/CustomListItem";
import StandardListItem from "sap/m/StandardListItem";
import ObjectStatus from "sap/m/ObjectStatus";
import Title from "sap/m/Title";
import Item from "sap/ui/core/Item";
import type { RouteMatch } from "../../util/router";
import { del, get, post, put } from "../../util/api";
import { addDays, isoDay, isoWeek, mondayOf, parseDay } from "../../util/days";
import { area } from "../../util/format";
import { bauakteStatus, flowStatuses } from "../../util/labels";
import { uuidv7 } from "../../util/ids";
import { draftOf, newDraft, openAssignment, planningLookups } from "../../util/assignmentDialog";
import { session } from "../../util/session";
import { download } from "../../util/planningModels";
import type { BacklogItem, CrewResponse, DirectoryUser, ResourceResponse } from "../../util/models";
import type Cockpit from "../../control/Cockpit";
import type { CockpitMode, CockpitPayload, CockpitRender, MenuItem } from "../../control/Cockpit";
import type { BacklogCard } from "../../control/CockpitBacklog";
import LeafletMap from "../../control/LeafletMap";
import {
	cockpitDays, hhmm, minutes, ofTime, toSave, toTime, urgencyOf, type CockpitBoard, type DayPlan, type PlanAssignment, type PlanBatchResult, type PlanOperation,
	type PlanReleaseInfo, type SaveAssignment,
} from "../../util/cockpitModels";

/** Schritt für Rückgängig/Wiederholen. */
interface Action { label: string; redo: () => Promise<boolean>; undo: () => Promise<boolean> }

const modes: CockpitMode[] = ["day", "week", "twoWeeks", "month"];
const short = (d: string) => parseDay(d).toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit" });
const longDay = (d: string) => parseDay(d).toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long" });
const dayDiff = (a: string, b: string) => Math.round((parseDay(b).getTime() - parseDay(a).getTime()) / 86_400_000);
const editable = (t: EventTarget | null) => !!(t as HTMLElement | null)?.closest?.("input, textarea, select, [contenteditable=true], .sapMDialog, .sapMPopover");

/**
 * Disponenten-Cockpit (W6): Vorrat ungeplanter Bauvorhaben links, Planungsraster Kolonnen × Tage bzw. Zeitachse rechts.
 * Optimistische Änderungen über /planning/batch (alles oder nichts), Rückgängig/Wiederholen, Tagesbesetzung, Konflikte, Freigabe.
 * @namespace estrichone.controller.planning
 */
export default class CockpitController extends BaseController {
	private board: CockpitBoard | null = null;
	private backlog: BacklogItem[] = [];
	private crews: CrewResponse[] = [];
	private resources: ResourceResponse[] = [];
	private people: DirectoryUser[] = [];
	private undoStack: Action[] = [];
	private redoStack: Action[] = [];
	private selected: string[] = [];
	private loadSeq = 0;
	private keyHandler = (e: KeyboardEvent) => this.onKey(e);

	onInit(): void {
		this.local({
			mode: "week", anchor: isoDay(new Date()), period: "", canPlan: false, busy: false, error: "", render: null,
			filter: { crews: [], region: "", status: "", search: "" }, crewOptions: [], regionOptions: [], statusOptions: [],
			pool: { mode: "free", search: "", sort: "urgency" }, poolItems: [], poolCount: 0, poolFacts: "", poolOpen: true,
			kpi: { unplanned: 0, overdue: 0, assignments: 0, loadText: "–", loadState: "None", releaseText: "–", releaseState: "None", conflicts: 0, changed: 0 },
			undoText: "", redoText: "", selectedCount: 0,
		});
		this.model().setProperty("/statusOptions", [{ key: "", text: "Alle Status" }, ...flowStatuses.map((s) => ({ key: s, text: bauakteStatus[s].text }))]);
		try {
			if (window.localStorage.getItem("eo.cockpit.pool") === "0") this.model().setProperty("/poolOpen", false);
		} catch { /* Speicher nicht verfügbar */ }
		EventBus.getInstance().subscribe("estrichone", "planningChanged", this.changed, this);
		document.addEventListener("keydown", this.keyHandler);
	}

	onExit(): void {
		EventBus.getInstance().unsubscribe("estrichone", "planningChanged", this.changed, this);
		document.removeEventListener("keydown", this.keyHandler);
	}

	private changed(): void { void this.load(true); }

	onRouteMatched(match: RouteMatch): void {
		const m = this.model();
		const mode = match.query.get("ansicht") as CockpitMode | null;
		const date = match.query.get("datum") ?? "";
		m.setProperty("/mode", mode && modes.includes(mode) ? mode : "week");
		m.setProperty("/anchor", /^\d{4}-\d{2}-\d{2}$/.test(date) ? date : isoDay(new Date()));
		m.setProperty("/canPlan", session.has("planning.manage"));
		void this.load(true, true);
	}

	private mode(): CockpitMode { return this.model().getProperty("/mode") as CockpitMode; }
	private anchor(): string { return String(this.model().getProperty("/anchor")); }
	private days(): string[] { return cockpitDays(this.mode(), this.anchor()); }
	private cockpit(): Cockpit { return this.byId("cockpit") as Cockpit; }
	private canPlan(): boolean { return this.model().getProperty("/canPlan") === true; }

	private go(values: { ansicht?: CockpitMode; datum?: string }): void {
		const query = new URLSearchParams(window.location.search);
		for (const [key, value] of Object.entries(values)) {
			if (value) query.set(key, value);
			else query.delete(key);
		}
		this.nav(`${window.location.pathname}?${query.toString()}`);
	}

	onMode(e: Event): void { this.go({ ansicht: (e.getSource() as SegmentedButton).getSelectedKey() as CockpitMode }); }
	onToday(): void { this.go({ datum: isoDay(new Date()) }); }
	onPick(e: Event): void {
		const v = String(e.getParameter("value" as never) ?? "");
		if (/^\d{4}-\d{2}-\d{2}$/.test(v)) this.go({ datum: v });
	}
	onPrev(): void { this.shift(-1); }
	onNext(): void { this.shift(1); }
	onDayPress(e: Event): void { this.go({ ansicht: "day", datum: String(e.getParameter("day" as never)) }); }
	onRefresh(): void { void this.load(true, true); }
	onFilter(): void { this.render(); }

	private shift(direction: number): void {
		const mode = this.mode();
		if (mode === "month") {
			const d = parseDay(this.anchor());
			this.go({ datum: isoDay(new Date(d.getFullYear(), d.getMonth() + direction, 1)) });
		} else {
			this.go({ datum: addDays(this.anchor(), direction * (mode === "day" ? 1 : mode === "week" ? 7 : 14)) });
		}
	}

	// ---- Laden und Darstellen --------------------------------------------------------------------------------------

	/** Lädt Raster und Vorrat; `visible` zeigt die Busy-Anzeige (sonst still im Hintergrund, ohne Ruckeln). */
	private async load(force: boolean, visible = false): Promise<void> {
		const m = this.model();
		const days = this.days();
		const seq = ++this.loadSeq;
		this.header(days);
		if (visible) m.setProperty("/busy", true);
		try {
			const [board, backlog, lookups, people] = await Promise.all([
				get<CockpitBoard>("planning/cockpit", { from: days[0], to: days[days.length - 1] }),
				get<BacklogItem[]>("planning/backlog").catch(() => [] as BacklogItem[]),
				planningLookups(force),
				session.people(),
			]);
			if (seq !== this.loadSeq) return;
			this.board = board;
			this.backlog = backlog;
			this.crews = lookups.crews;
			this.resources = lookups.resources.filter((r) => r.isActive);
			this.people = people.filter((p) => p.isActive);
			m.setProperty("/canPlan", board.canPlan && session.has("planning.manage"));
			m.setProperty("/error", "");
			const known = new Set(board.assignments.map((a) => a.id));
			this.selected = this.selected.filter((id) => known.has(id));
		} catch (error) {
			if (seq === this.loadSeq) m.setProperty("/error", (error as Error).message);
		}
		if (seq !== this.loadSeq) return;
		m.setProperty("/busy", false);
		this.render();
		this.renderPool();
	}

	private header(days: string[]): void {
		const mode = this.mode();
		const first = days[0];
		const last = days[days.length - 1];
		this.model().setProperty("/period", mode === "day" ? longDay(first)
			: mode === "month" ? parseDay(first).toLocaleDateString("de-DE", { month: "long", year: "numeric" })
				: `KW ${isoWeek(first)}${mode === "twoWeeks" ? `–${isoWeek(last)}` : ""} · ${short(first)} – ${short(last)}${parseDay(last).getFullYear() !== new Date().getFullYear() ? ` ${parseDay(last).getFullYear()}` : ""}`);
	}

	private regionOf(a: { bauakteAddress?: string | null; postalCode?: string | null }): string {
		const plz = a.postalCode ?? /\b(\d{5})\b/.exec(a.bauakteAddress ?? "")?.[1] ?? "";
		return plz ? plz.slice(0, 2) : "";
	}

	private render(): void {
		const m = this.model();
		const board = this.board;
		if (!board) return;
		const f = m.getProperty("/filter") as { crews: string[]; region: string; status: string; search: string };
		const q = (f.search ?? "").trim().toLowerCase();
		const crewFilter = new Set(f.crews ?? []);
		const crews = board.crews.filter((c) => !crewFilter.size || crewFilter.has(c.id));
		const dimmed = board.assignments.filter((a) => (q && ![a.bauakteNumber, a.bauakteTitle, a.bauakteAddress, a.activity, a.crewName, ...a.people.map((p) => p.name)]
			.some((v) => v?.toLowerCase().includes(q)))
			|| (f.status && a.bauakteStatus !== f.status) || (f.region && this.regionOf(a) !== f.region)).map((a) => a.id);
		const render: CockpitRender = {
			mode: this.mode(), days: board.days, today: isoDay(new Date()), crews,
			noCrewLane: (!crewFilter.size || crewFilter.has("-")) && (board.assignments.some((a) => !a.crewId) || crews.length === 0 || this.mode() === "day"),
			assignments: board.assignments, selected: this.selected, dimmed, canPlan: m.getProperty("/canPlan") === true, hours: [6, 19],
		};
		m.setProperty("/render", render);
		this.cockpit()?.draw();
		m.setProperty("/crewOptions", [...board.crews.map((c) => ({ key: c.id, text: c.name })), { key: "-", text: "Ohne Kolonne" }]);
		const regions = new Set([...board.assignments.map((a) => this.regionOf(a)), ...this.backlog.map((b) => this.regionOf(b))].filter(Boolean));
		m.setProperty("/regionOptions", [{ key: "", text: "Alle Regionen" }, ...[...regions].sort().map((r) => ({ key: r, text: `PLZ ${r}…` }))]);
		this.kpis();
		this.selectionChanged();
	}

	private kpis(): void {
		const board = this.board!;
		const m = this.model();
		let planned = 0;
		let capacity = 0;
		for (const c of board.crews) {
			for (const cell of c.cells) {
				capacity += cell.capacityMinutes;
				planned += board.assignments.filter((a) => a.crewId === c.id && a.day === cell.day).reduce((s, a) => s + minutes(a.startTime, a.endTime), 0);
			}
		}
		const percent = capacity ? Math.round((planned / capacity) * 100) : 0;
		const today = isoDay(new Date());
		const free = this.backlog.filter((b) => b.upcomingAssignments === 0);
		const releasable = board.days.filter((d) => d.isWorkday && d.day >= today);
		const released = releasable.filter((d) => d.release);
		const changed = board.days.reduce((s, d) => s + (d.release?.changedCount ?? 0), 0);
		const conflicts = board.crews.reduce((s, c) => s + c.cells.reduce((x, cell) => x + (cell.assignmentIds.length ? cell.warnings.length : 0), 0), 0)
			+ board.assignments.filter((a) => !a.crewId).reduce((s, a) => s + a.warnings.length, 0);
		m.setProperty("/kpi", {
			unplanned: free.length, overdue: free.filter((b) => urgencyOf(b, today) === "overdue").length, assignments: board.assignments.length,
			loadText: capacity ? `${percent} % (${Math.round(planned / 60)} von ${Math.round(capacity / 60)} h)` : "–",
			loadState: percent > 100 ? "Error" : percent >= 85 ? "Warning" : "Success",
			releaseText: !releasable.length ? "–" : changed ? `${changed} ${changed === 1 ? "Änderung" : "Änderungen"} seit Freigabe`
				: `${released.length} von ${releasable.length} Tagen freigegeben`,
			releaseState: changed ? "Warning" : releasable.length && released.length === releasable.length ? "Success" : "None",
			conflicts, changed,
		});
	}

	// ---- Vorrat ------------------------------------------------------------------------------------------------------

	onPoolFilter(): void { this.renderPool(); }

	onTogglePool(): void {
		const m = this.model();
		const open = !m.getProperty("/poolOpen");
		m.setProperty("/poolOpen", open);
		try { window.localStorage.setItem("eo.cockpit.pool", open ? "1" : "0"); } catch { /* egal */ }
	}

	private renderPool(): void {
		const m = this.model();
		const pool = m.getProperty("/pool") as { mode: string; search: string; sort: string };
		const q = (pool.search ?? "").trim().toLowerCase();
		const region = String(m.getProperty("/filter/region") ?? "");
		const today = isoDay(new Date());
		const rank = { overdue: 0, soon: 1, later: 2, none: 3 };
		const items = this.backlog
			.filter((b) => (pool.mode === "all" || b.upcomingAssignments === 0)
				&& (!region || this.regionOf(b) === region)
				&& (!q || [b.number, b.title, b.city, b.customerName, b.street].some((v) => v?.toLowerCase().includes(q))))
			.sort((x, y) => pool.sort === "area" ? y.totalAreaSquareMeters - x.totalAreaSquareMeters
				: pool.sort === "city" ? (x.city ?? "~").localeCompare(y.city ?? "~")
					: pool.sort === "date" ? (x.plannedStart ?? "9999").localeCompare(y.plannedStart ?? "9999")
						: (rank[urgencyOf(x, today)] - rank[urgencyOf(y, today)]) || (x.plannedStart ?? "9999").localeCompare(y.plannedStart ?? "9999")
							|| y.totalAreaSquareMeters - x.totalAreaSquareMeters);
		m.setProperty("/poolItems", items.map((b): BacklogCard => {
			const urgency = urgencyOf(b, today);
			const start = b.plannedStart ? short(b.plannedStart) : "";
			return {
				id: b.bauakteId, title: b.title, number: b.number,
				meta: [b.number ?? "neu", b.customerName, [b.postalCode, b.city].filter(Boolean).join(" ")].filter(Boolean).join(" · "),
				area: b.totalAreaSquareMeters > 0 ? area(b.totalAreaSquareMeters) : "",
				due: urgency === "overdue" ? `Beginn überfällig (${start})` : urgency === "soon" ? `Beginn ${start}` : urgency === "later" ? `ab ${start}` : "ohne Termin",
				urgency, planned: b.upcomingAssignments > 0 ? `${b.upcomingAssignments}× geplant` : b.screedTypes.join(", "),
			};
		}));
		const free = this.backlog.filter((b) => b.upcomingAssignments === 0);
		m.setProperty("/poolCount", free.length);
		const total = free.reduce((s, b) => s + b.totalAreaSquareMeters, 0);
		m.setProperty("/poolFacts", `${items.length} angezeigt${total > 0 ? ` · ${area(total)} ungeplant` : ""}`);
	}

	onPoolPlan(e: Event): void {
		const b = this.backlog.find((x) => x.bauakteId === String(e.getParameter("id" as never)));
		if (!b) return;
		const days = this.days();
		const today = isoDay(new Date());
		void openAssignment(newDraft(days.includes(today) ? today : days[0], null, { id: b.bauakteId, label: `${b.number ?? "neu"} · ${b.title}` }));
	}

	onPoolOpen(e: Event): void { this.nav(`/bauakten/${String(e.getParameter("id" as never))}`); }

	// ---- Änderungen: optimistisch, gebündelt, rückgängig machbar ---------------------------------------------------------

	private find(id: string): PlanAssignment | undefined { return this.board?.assignments.find((a) => a.id === id); }

	/** Mitglieder einer Kolonne an einem Tag (Tagesbesetzung bzw. Stamm). */
	private membersOn(crewId: string | null, day: string): string[] {
		if (!crewId) return [];
		const crew = this.board?.crews.find((c) => c.id === crewId);
		const cell = crew?.cells.find((c) => c.day === day);
		return (cell?.members ?? crew?.members ?? []).map((p) => p.id);
	}

	private crewInfo(crewId: string | null): { crewName: string | null; crewColor: string | null } {
		const crew = crewId ? this.board?.crews.find((c) => c.id === crewId) ?? this.crews.find((c) => c.id === crewId) : null;
		return { crewName: crew?.name ?? null, crewColor: crew?.color ?? null };
	}

	/** Änderung sofort lokal anwenden (vor der Serverantwort). */
	private applyLocal(ops: PlanOperation[], views: PlanAssignment[] = []): void {
		const board = this.board;
		if (!board) return;
		for (const op of ops) {
			const i = board.assignments.findIndex((a) => a.id === op.id);
			if (op.op === "delete") {
				if (i >= 0) board.assignments.splice(i, 1);
			} else if (op.op === "move" && i >= 0) {
				const a = { ...board.assignments[i] };
				if (op.day) a.day = op.day;
				if (op.changeCrew) Object.assign(a, { crewId: op.crewId ?? null, ...this.crewInfo(op.crewId ?? null) });
				if (op.setTime) { a.startTime = op.startTime ?? null; a.endTime = op.endTime ?? null; }
				if (op.sequence !== undefined) a.sequence = op.sequence;
				board.assignments[i] = a;
			} else if (op.op === "upsert") {
				const template = views.find((v) => v.id === op.id) ?? (i >= 0 ? board.assignments[i] : undefined);
				const s = op.assignment;
				const view: PlanAssignment = {
					id: op.id, day: s.day, startTime: s.startTime, endTime: s.endTime, activity: s.activity, bauakteId: s.bauakteId,
					bauakteNumber: template?.bauakteNumber ?? null, bauakteTitle: template?.bauakteTitle ?? "…", bauakteAddress: template?.bauakteAddress ?? null,
					crewId: s.crewId, ...this.crewInfo(s.crewId), people: template?.people ?? [], resources: template?.resources ?? [], notes: s.notes,
					warnings: [], version: 0, kind: s.kind, bauakteStatus: template?.bauakteStatus ?? null, sequence: op.sequence ?? 0, change: null,
				};
				if (i >= 0) board.assignments[i] = view;
				else board.assignments.push(view);
			}
		}
		this.render();
	}

	/** Bündel an den Server; danach still neu laden (Kapazität, Konflikte). Fehler: Meldung und Stand vom Server. */
	private async send(ops: PlanOperation[], views: PlanAssignment[] = []): Promise<PlanBatchResult | null> {
		this.applyLocal(ops, views);
		try {
			const result = await post<PlanBatchResult>("planning/batch", { operations: ops });
			void this.load(false);
			return result;
		} catch (error) {
			this.showError(error);
			void this.load(false);
			return null;
		}
	}

	/** Ausführen und auf den Rückgängig-Stapel legen. */
	private async perform(label: string, forward: PlanOperation[], backward: PlanOperation[], forwardViews: PlanAssignment[] = [], backwardViews: PlanAssignment[] = []): Promise<void> {
		const action: Action = {
			label,
			redo: async () => !!(await this.send(forward, forwardViews)),
			undo: async () => !!(await this.send(backward, backwardViews)),
		};
		const result = await this.send(forward, forwardViews);
		if (!result) return;
		this.pushUndo(action);
		const warnings = result.warnings;
		MessageToast.show(warnings.length ? `${label} – Hinweis: ${warnings.slice(0, 2).join(" ")}${warnings.length > 2 ? " …" : ""}` : `${label}. Strg+Z macht es rückgängig.`,
			{ duration: warnings.length ? 5000 : 2500 });
	}

	private pushUndo(action: Action): void {
		this.undoStack.push(action);
		if (this.undoStack.length > 50) this.undoStack.shift();
		this.redoStack = [];
		this.updateUndo();
	}

	private updateUndo(): void {
		this.model().setProperty("/undoText", this.undoStack.at(-1)?.label ?? "");
		this.model().setProperty("/redoText", this.redoStack.at(-1)?.label ?? "");
	}

	async onUndo(): Promise<void> {
		const action = this.undoStack.pop();
		this.updateUndo();
		if (!action) return;
		if (await action.undo()) {
			this.redoStack.push(action);
			MessageToast.show(`Rückgängig: ${action.label}`);
		}
		this.updateUndo();
	}

	async onRedo(): Promise<void> {
		const action = this.redoStack.pop();
		this.updateUndo();
		if (!action) return;
		if (await action.redo()) {
			this.undoStack.push(action);
			MessageToast.show(`Wiederholt: ${action.label}`);
		}
		this.updateUndo();
	}

	/** Gegenoperation zum Zurücksetzen eines Einsatzes auf seinen jetzigen Stand. */
	private restoreOp(a: PlanAssignment): PlanOperation {
		return { op: "move", id: a.id, day: a.day, changeCrew: true, crewId: a.crewId, setTime: true, startTime: a.startTime, endTime: a.endTime, sequence: a.sequence ?? 0 };
	}

	private copyOp(a: PlanAssignment, day: string, crewId: string | null, start?: string | null): { op: PlanOperation; view: PlanAssignment } {
		const id = uuidv7();
		const save: SaveAssignment = { ...toSave(a, this.membersOn(a.crewId, a.day)), day, crewId };
		if (crewId !== a.crewId) save.excludedUserIds = [];
		if (start) {
			const length = minutes(a.startTime, a.endTime);
			save.startTime = start;
			save.endTime = a.endTime ? toTime(Math.min(23 * 60 + 45, ofTime(start) + length)) : null;
		}
		if (!save.crewId && save.userIds.length === 0) save.userIds = a.people.map((p) => p.id);
		return { op: { op: "upsert", id, assignment: save }, view: { ...a, id } };
	}

	// ---- Ziehen & Ablegen ----------------------------------------------------------------------------------------------

	async onDrop(e: Event): Promise<void> {
		if (!this.canPlan()) return;
		const payload = e.getParameter("payload" as never) as CockpitPayload;
		const crewId = String(e.getParameter("crewId" as never) || "") || null;
		const day = String(e.getParameter("day" as never));
		const copy = e.getParameter("copy" as never) === true;
		const start = String(e.getParameter("start" as never) || "") || null;
		if (payload.kind === "bauakte") {
			await this.planBauakte(payload.id, crewId, day, start);
			return;
		}
		const dragged = this.find(payload.id);
		if (!dragged) return;
		const ids = this.selected.includes(dragged.id) ? this.selected : [dragged.id];
		const group = ids.map((id) => this.find(id)).filter((a): a is PlanAssignment => !!a);
		const delta = dayDiff(dragged.day, day);
		const changeCrew = (dragged.crewId ?? null) !== crewId;
		if (copy) {
			const copies = group.map((a) => this.copyOp(a, addDays(a.day, delta), changeCrew ? crewId : a.crewId, a.id === dragged.id ? start : null));
			if (copies.some((c) => c.op.op === "upsert" && !c.op.assignment.crewId && c.op.assignment.userIds.length === 0)) {
				MessageToast.show("Ohne Kolonne braucht ein Einsatz mindestens einen Mitarbeiter.");
				return;
			}
			await this.perform(group.length === 1 ? `„${dragged.bauakteTitle}“ kopiert` : `${group.length} Einsätze kopiert`,
				copies.map((c) => c.op), copies.map((c) => ({ op: "delete", id: c.op.id }) as PlanOperation), copies.map((c) => c.view));
			return;
		}
		if (changeCrew && !crewId && group.some((a) => a.people.length === 0)) {
			MessageToast.show("Ohne Kolonne braucht ein Einsatz mindestens einen Mitarbeiter.");
			return;
		}
		const forward: PlanOperation[] = group.map((a) => {
			const op: PlanOperation = { op: "move", id: a.id, day: addDays(a.day, delta) };
			if (changeCrew) Object.assign(op, { changeCrew: true, crewId });
			if (start && a.id === dragged.id) {
				const length = minutes(a.startTime, a.endTime);
				Object.assign(op, { setTime: true, startTime: start, endTime: a.endTime || !a.startTime ? toTime(Math.min(23 * 60 + 45, ofTime(start) + length)) : null });
			}
			return op;
		});
		const unchanged = forward.every((op) => op.op === "move" && op.day === this.find(op.id)?.day && !op.changeCrew && !("setTime" in op));
		if (unchanged) return;
		const target = crewId ? this.crewInfo(crewId).crewName : "ohne Kolonne";
		await this.perform(group.length === 1 ? `„${dragged.bauakteTitle}“ → ${target ?? ""}, ${short(addDays(dragged.day, delta))}${start ? ` ${hhmm(start)}` : ""}`
			: `${group.length} Einsätze verschoben`, forward, group.map((a) => this.restoreOp(a)));
	}

	/** Bauvorhaben aus dem Vorrat einplanen: direkt anlegen (Kolonne) bzw. Dialog (ohne Kolonne). */
	private async planBauakte(bauakteId: string, crewId: string | null, day: string, start: string | null): Promise<void> {
		const b = this.backlog.find((x) => x.bauakteId === bauakteId);
		const label = b ? `${b.number ?? "neu"} · ${b.title}` : "Bauvorhaben";
		if (!crewId) {
			const draft = newDraft(day, null, { id: bauakteId, label });
			if (start) draft.startTime = start;
			void openAssignment(draft);
			return;
		}
		const id = uuidv7();
		const assignment: SaveAssignment = {
			bauakteId, day, startTime: start ?? "07:00:00", endTime: null, activity: "Estrich einbringen", crewId, userIds: [], resourceIds: [], notes: null, kind: "screed",
		};
		const view = {
			id, bauakteNumber: b?.number ?? null, bauakteTitle: b?.title ?? "…", bauakteAddress: b ? [b.street, [b.postalCode, b.city].filter(Boolean).join(" ")].filter(Boolean).join(", ") : null,
			people: [], resources: [], bauakteStatus: b?.status ?? null,
		} as unknown as PlanAssignment;
		if (b) b.upcomingAssignments += 1;
		this.renderPool();
		await this.perform(`„${b?.title ?? "Bauvorhaben"}“ eingeplant: ${this.crewInfo(crewId).crewName ?? ""}, ${short(day)}`,
			[{ op: "upsert", id, assignment }], [{ op: "delete", id }], [view]);
	}

	/** Kante gezogen: Einsatz auf die Folgetage bis `day` kopieren (Arbeitstage, ohne vorhandene gleiche Baustelle). */
	async onExtend(e: Event): Promise<void> {
		const a = this.find(String(e.getParameter("id" as never)));
		const until = String(e.getParameter("day" as never));
		if (!a || !this.board) return;
		const days = this.board.days.filter((d) => d.day > a.day && d.day <= until && d.isWorkday
			&& !this.board!.assignments.some((x) => x.day === d.day && x.bauakteId === a.bauakteId && x.crewId === a.crewId)).map((d) => d.day);
		if (!days.length) {
			MessageToast.show("Keine weiteren Arbeitstage in diesem Bereich frei.");
			return;
		}
		const copies = days.map((d) => this.copyOp(a, d, a.crewId));
		await this.perform(`„${a.bauakteTitle}“ auf ${days.length + 1} Tage ausgedehnt`, copies.map((c) => c.op),
			copies.map((c) => ({ op: "delete", id: c.op.id }) as PlanOperation), copies.map((c) => c.view));
	}

	async onRetime(e: Event): Promise<void> {
		const a = this.find(String(e.getParameter("id" as never)));
		if (!a) return;
		const start = String(e.getParameter("start" as never));
		const end = String(e.getParameter("end" as never));
		await this.perform(`„${a.bauakteTitle}“ ${hhmm(start)}–${hhmm(end)}`, [{ op: "move", id: a.id, setTime: true, startTime: start, endTime: end }], [this.restoreOp(a)]);
	}

	// ---- Auswahl, Öffnen, Anlegen --------------------------------------------------------------------------------------

	onSelect(e: Event): void {
		const id = String(e.getParameter("id" as never));
		const toggle = e.getParameter("toggle" as never) === true;
		if (toggle) this.selected = this.selected.includes(id) ? this.selected.filter((x) => x !== id) : [...this.selected, id];
		else this.selected = this.selected.length === 1 && this.selected[0] === id ? [] : [id];
		this.selectionChanged();
	}

	private selectionChanged(): void {
		const render = this.model().getProperty("/render") as CockpitRender | null;
		if (render) render.selected = this.selected;
		this.cockpit()?.markSelection(this.selected);
		this.model().setProperty("/selectedCount", this.selected.length);
	}

	onClearSelection(): void {
		this.selected = [];
		this.selectionChanged();
	}

	onOpen(e: Event): void {
		const a = this.find(String(e.getParameter("id" as never)));
		if (a) void openAssignment(draftOf(a, this.crews));
	}

	onAdd(e: Event): void {
		const crewId = String(e.getParameter("crewId" as never) || "") || null;
		void openAssignment(newDraft(String(e.getParameter("day" as never)), crewId));
	}

	// ---- Schnellaktionen -------------------------------------------------------------------------------------------------

	private async shiftSelected(delta: number): Promise<void> {
		const group = this.selected.map((id) => this.find(id)).filter((a): a is PlanAssignment => !!a);
		if (!group.length || !this.canPlan()) return;
		await this.perform(`${group.length === 1 ? `„${group[0].bauakteTitle}“` : `${group.length} Einsätze`} ${delta > 0 ? "einen Tag später" : "einen Tag früher"}`,
			group.map((a) => ({ op: "move", id: a.id, day: addDays(a.day, delta) }) as PlanOperation), group.map((a) => this.restoreOp(a)));
	}

	onShiftBack(): void { void this.shiftSelected(-1); }
	onShiftForward(): void { void this.shiftSelected(1); }

	private async nextFree(a: PlanAssignment): Promise<void> {
		if (!a.crewId) {
			MessageToast.show("Der nächste freie Tag wird je Kolonne gesucht – dieser Einsatz hat keine Kolonne.");
			return;
		}
		const before = { ...a };
		try {
			const moved = await post<PlanAssignment>(`planning/assignments/${a.id}/next-free-day`, {});
			this.pushUndo({
				label: `„${a.bauakteTitle}“ auf ${short(moved.day)}`,
				redo: async () => !!(await this.send([{ op: "move", id: a.id, day: moved.day }])),
				undo: async () => !!(await this.send([this.restoreOp(before)])),
			});
			MessageToast.show(`„${a.bauakteTitle}“ auf den nächsten freien Tag verschoben: ${longDay(moved.day)}`);
			if (!this.days().includes(moved.day)) this.selected = this.selected.filter((id) => id !== a.id);
			void this.load(false);
		} catch (error) {
			this.showError(error);
		}
	}

	onNextFree(): void {
		const a = this.selected.length === 1 ? this.find(this.selected[0]) : undefined;
		if (a) void this.nextFree(a);
		else MessageToast.show("Bitte genau einen Einsatz auswählen.");
	}

	async onDeleteSelected(): Promise<void> {
		const group = this.selected.map((id) => this.find(id)).filter((a): a is PlanAssignment => !!a);
		if (!group.length || !this.canPlan()) return;
		this.selected = [];
		await this.perform(group.length === 1 ? `„${group[0].bauakteTitle}“ entfernt` : `${group.length} Einsätze entfernt`,
			group.map((a) => ({ op: "delete", id: a.id }) as PlanOperation),
			group.map((a) => ({ op: "upsert", id: a.id, assignment: toSave(a, this.membersOn(a.crewId, a.day)), sequence: a.sequence ?? 0 }) as PlanOperation), [], group);
	}

	/** Alle Einsätze einer Kolonne an einem Tag auf die übrigen Arbeitstage der Woche kopieren. */
	private async copyDayToWeek(crewId: string | null, day: string): Promise<void> {
		const jobs = this.board?.assignments.filter((a) => a.day === day && (a.crewId ?? null) === crewId) ?? [];
		const monday = mondayOf(day);
		const targets = Array.from({ length: 5 }, (_, i) => addDays(monday, i)).filter((d) => d > day
			&& this.board?.days.find((x) => x.day === d)?.isWorkday !== false);
		if (!jobs.length || !targets.length) {
			MessageToast.show(jobs.length ? "Keine weiteren Arbeitstage in dieser Woche." : "An diesem Tag ist nichts geplant.");
			return;
		}
		const copies = targets.flatMap((d) => jobs.filter((a) => !this.board!.assignments.some((x) => x.day === d && x.bauakteId === a.bauakteId && x.crewId === a.crewId))
			.map((a) => this.copyOp(a, d, a.crewId)));
		if (!copies.length) {
			MessageToast.show("Die Woche ist bereits so geplant.");
			return;
		}
		await this.perform(`Tag ${short(day)} auf die Woche kopiert (${copies.length} Einsätze)`, copies.map((c) => c.op),
			copies.map((c) => ({ op: "delete", id: c.op.id }) as PlanOperation), copies.map((c) => c.view));
	}

	private async swapCrews(day: string, crewA: string, crewB: string): Promise<void> {
		const run = async () => {
			try {
				await post<DayPlan>(`planning/days/${day}/swap-crews`, { crewA, crewB });
				void this.load(false);
				return true;
			} catch (error) {
				this.showError(error);
				return false;
			}
		};
		if (!(await run())) return;
		const label = `${this.crewInfo(crewA).crewName} ⇄ ${this.crewInfo(crewB).crewName} am ${short(day)}`;
		this.pushUndo({ label, redo: run, undo: run });
		MessageToast.show(`Kolonnen getauscht: ${label}`);
	}

	onMenu(e: Event): void {
		const id = String(e.getParameter("assignmentId" as never) || "");
		const crewId = String(e.getParameter("crewId" as never) || "") || null;
		const day = String(e.getParameter("day" as never));
		const x = Number(e.getParameter("x" as never));
		const y = Number(e.getParameter("y" as never));
		const can = this.canPlan();
		const otherCrews = (this.board?.crews ?? []).filter((c) => c.id !== crewId);
		if (id) {
			const a = this.find(id);
			if (!a) return;
			if (!this.selected.includes(id)) {
				this.selected = [id];
				this.selectionChanged();
			}
			const many = this.selected.length > 1;
			const items: MenuItem[] = [
				{ text: "Öffnen und bearbeiten …", icon: "sap-icon://edit", action: () => void openAssignment(draftOf(a, this.crews)) },
				{ text: "Bauakte öffnen", icon: "sap-icon://folder-blank", action: () => this.nav(`/bauakten/${a.bauakteId}`) },
				{ text: "-" },
				{ text: many ? `${this.selected.length} Einsätze einen Tag später` : "Einen Tag später", icon: "sap-icon://open-command-field", disabled: !can, action: () => void this.shiftSelected(1) },
				{ text: many ? `${this.selected.length} Einsätze einen Tag früher` : "Einen Tag früher", icon: "sap-icon://close-command-field", disabled: !can, action: () => void this.shiftSelected(-1) },
				{ text: "Auf nächsten freien Tag", icon: "sap-icon://appointment", disabled: !can || many || !a.crewId, action: () => void this.nextFree(a) },
				{
					text: "Andere Kolonne", icon: "sap-icon://group", disabled: !can || !otherCrews.length,
					children: otherCrews.map((c) => ({
						text: c.name, action: () => {
							const group = this.selected.map((s) => this.find(s)).filter((g): g is PlanAssignment => !!g);
							void this.perform(`${many ? `${group.length} Einsätze` : `„${a.bauakteTitle}“`} → ${c.name}`,
								group.map((g) => ({ op: "move", id: g.id, changeCrew: true, crewId: c.id }) as PlanOperation), group.map((g) => this.restoreOp(g)));
						},
					})),
				},
				{ text: "Auf restliche Woche kopieren", icon: "sap-icon://copy", disabled: !can || many, action: () => void this.copyDayOfAssignment(a) },
				{ text: "-" },
				{ text: many ? `${this.selected.length} Einsätze entfernen` : "Entfernen", icon: "sap-icon://delete", danger: true, disabled: !can, action: () => void this.onDeleteSelected() },
			];
			this.cockpit().showMenu(x, y, items, many ? `${this.selected.length} Einsätze` : a.bauakteTitle);
			return;
		}
		const crew = this.board?.crews.find((c) => c.id === crewId);
		const items: MenuItem[] = [
			{ text: "Einsatz planen …", icon: "sap-icon://add", disabled: !can, action: () => void openAssignment(newDraft(day, crewId)) },
			{ text: "Zeitachse dieses Tages", icon: "sap-icon://gantt-bars", action: () => this.go({ ansicht: "day", datum: day }) },
			{ text: "Karte und Fahrtreihenfolge", icon: "sap-icon://map-2", action: () => void this.openMap(day) },
		];
		if (crew) {
			items.push(
				{ text: "-" },
				{ text: "Tagesbesetzung bearbeiten …", icon: "sap-icon://group", disabled: !can, action: () => void this.openRoster(crew.id, day) },
				{ text: "Tag auf restliche Woche kopieren", icon: "sap-icon://copy", disabled: !can, action: () => void this.copyDayToWeek(crew.id, day) },
				{
					text: "Kolonne tauschen mit", icon: "sap-icon://synchronize", disabled: !can || !otherCrews.length,
					children: otherCrews.map((c) => ({ text: c.name, action: () => void this.swapCrews(day, crew.id, c.id) })),
				},
			);
		}
		this.cockpit().showMenu(x, y, items, `${crew?.name ?? "Ohne Kolonne"} · ${longDay(day)}`);
	}

	private copyDayOfAssignment(a: PlanAssignment): Promise<void> {
		const monday = mondayOf(a.day);
		const targets = Array.from({ length: 5 }, (_, i) => addDays(monday, i)).filter((d) => d > a.day
			&& !this.board!.assignments.some((x) => x.day === d && x.bauakteId === a.bauakteId && x.crewId === a.crewId));
		if (!targets.length) {
			MessageToast.show("Keine weiteren Arbeitstage in dieser Woche frei.");
			return Promise.resolve();
		}
		const copies = targets.map((d) => this.copyOp(a, d, a.crewId));
		return this.perform(`„${a.bauakteTitle}“ auf ${targets.length} weitere Tage kopiert`, copies.map((c) => c.op),
			copies.map((c) => ({ op: "delete", id: c.op.id }) as PlanOperation), copies.map((c) => c.view));
	}

	// ---- Tastatur ------------------------------------------------------------------------------------------------------

	private onKey(e: KeyboardEvent): void {
		const dom = this.v().getDomRef() as HTMLElement | null;
		if (!dom?.isConnected || !dom.offsetParent || editable(e.target) || [...document.querySelectorAll<HTMLElement>(".sapMDialog")].some((d) => d.offsetParent)) return;
		const ctrl = e.ctrlKey || e.metaKey;
		const key = e.key.toLowerCase();
		if (ctrl && key === "z" && !e.shiftKey) { e.preventDefault(); void this.onUndo(); return; }
		if (ctrl && (key === "y" || (key === "z" && e.shiftKey))) { e.preventDefault(); void this.onRedo(); return; }
		if (!this.selected.length) return;
		if (e.key === "Delete") { e.preventDefault(); void this.onDeleteSelected(); }
		else if (e.key === "Escape") { this.onClearSelection(); }
		else if (e.altKey && e.key === "ArrowRight") { e.preventDefault(); void this.shiftSelected(1); }
		else if (e.altKey && e.key === "ArrowLeft") { e.preventDefault(); void this.shiftSelected(-1); }
		else if (!ctrl && key === "n" && this.selected.length === 1) { e.preventDefault(); this.onNextFree(); }
	}

	// ---- Konflikte ----------------------------------------------------------------------------------------------------

	onCellInfo(e: Event): void {
		const crewId = String(e.getParameter("crewId" as never) || "");
		const day = String(e.getParameter("day" as never));
		const crew = this.board?.crews.find((c) => c.id === crewId);
		const cell = crew?.cells.find((c) => c.day === day);
		MessageBox.warning(cell?.warnings.join("\n") || "Keine Konflikte.", { title: `${crew?.name ?? "Ohne Kolonne"} · ${longDay(day)}` });
	}

	onConflicts(): void {
		const board = this.board;
		if (!board) return;
		const list = new List({ noDataText: "Keine Konflikte im Zeitraum – sauber geplant." });
		for (const crew of board.crews) {
			for (const cell of crew.cells) {
				if (!cell.assignmentIds.length || !cell.warnings.length) continue;
				for (const w of cell.warnings) {
					list.addItem(new StandardListItem({
						title: w, description: `${crew.name} · ${longDay(cell.day)}`, icon: "sap-icon://alert", wrapping: true, type: "Active",
						press: () => { dialog.close(); this.go({ ansicht: "day", datum: cell.day }); },
					}));
				}
			}
		}
		for (const a of board.assignments.filter((x) => !x.crewId && x.warnings.length)) {
			for (const w of a.warnings) list.addItem(new StandardListItem({ title: w, description: `Ohne Kolonne · ${a.bauakteTitle} · ${longDay(a.day)}`, icon: "sap-icon://alert", wrapping: true }));
		}
		const dialog = new Dialog({
			title: "Konflikte im Zeitraum", contentWidth: "36rem", contentHeight: "28rem", content: [list],
			endButton: new Button({ text: "Schließen", press: () => dialog.close() }), afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	// ---- Tagesbesetzung -------------------------------------------------------------------------------------------------

	private async openRoster(crewId: string, day: string): Promise<void> {
		const crew = this.board?.crews.find((c) => c.id === crewId);
		const cell = crew?.cells.find((c) => c.day === day);
		if (!crew || !cell) return;
		const absence = new Map(cell.members.filter((m) => m.absenceNote).map((m) => [m.id, m.absenceNote]));
		const members = new MultiComboBox({
			width: "100%", selectedKeys: cell.members.map((m) => m.id),
			items: this.people.map((p) => new Item({ key: p.id, text: absence.has(p.id) ? `${p.displayName} (${absence.get(p.id)})` : p.displayName })),
		});
		const vehicles = new MultiComboBox({
			width: "100%", selectedKeys: cell.vehicles.map((v) => v.id),
			items: this.resources.map((r) => new Item({ key: r.id, text: `${r.name}${r.identifier ? ` (${r.identifier})` : ""}` })),
		});
		const note = new TextArea({ width: "100%", rows: 2, value: cell.rosterNote ?? "", maxLength: 500, placeholder: "z. B. Max fährt heute Sprinter 2" });
		const save = async (reset: boolean) => {
			try {
				if (reset) await del(`planning/crews/${crewId}/days/${day}/roster`);
				else await put(`planning/crews/${crewId}/days/${day}/roster`, { memberUserIds: members.getSelectedKeys(), resourceIds: vehicles.getSelectedKeys(), note: note.getValue() });
				dialog.close();
				MessageToast.show(reset ? "Stammbesetzung gilt wieder." : `Tagesbesetzung ${crew.name} am ${short(day)} gespeichert.`);
				void this.load(false);
			} catch (error) {
				this.showError(error);
			}
		};
		const dialog = new Dialog({
			title: `Tagesbesetzung ${crew.name} · ${longDay(day)}`, contentWidth: "30rem",
			content: [new VBox({
				items: [
					new Text({ text: "Gilt nur für diesen Tag: wer mitfährt und welches Fahrzeug/Gerät die Kolonne nutzt. Konflikte und „Wer fährt wohin“ richten sich danach." }).addStyleClass("eoMuted sapUiSmallMarginBottom"),
					new Label({ text: "Mitarbeiter", labelFor: members }), members,
					new Label({ text: "Fahrzeuge und Geräte", labelFor: vehicles }).addStyleClass("sapUiSmallMarginTop"), vehicles,
					new Label({ text: "Notiz", labelFor: note }).addStyleClass("sapUiSmallMarginTop"), note,
				],
			}).addStyleClass("sapUiSmallMargin")],
			buttons: [
				new Button({ text: "Speichern", type: "Emphasized", press: () => void save(false) }),
				...(cell.rosterChanged ? [new Button({ text: "Stammbesetzung", press: () => void save(true) })] : []),
				new Button({ text: "Abbrechen", press: () => dialog.close() }),
			],
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	// ---- Karte und Fahrtreihenfolge ---------------------------------------------------------------------------------------

	onMap(): void {
		const days = this.days();
		const today = isoDay(new Date());
		void this.openMap(this.mode() === "day" ? days[0] : days.includes(today) ? today : days[0]);
	}

	private async openMap(day: string): Promise<void> {
		let plan: DayPlan;
		try {
			plan = await get<DayPlan>(`planning/day/${day}`);
		} catch (error) {
			this.showError(error);
			return;
		}
		const map = new LeafletMap({ height: "30rem", width: "100%", zoom: 11, markers: [] } as unknown as ConstructorParameters<typeof LeafletMap>[0]);
		const side = new VBox({ width: "20rem" }).addStyleClass("eoCkMapSide");
		const canPlan = this.canPlan();
		const fill = (p: DayPlan) => {
			const markers = [...p.crews.flatMap((c) => c.stops.map((s) => ({ c, s }))), ...p.withoutCrew.map((s) => ({ c: null, s }))]
				.filter(({ s }) => s.latitude !== null && s.longitude !== null)
				.map(({ c, s }) => ({
					id: s.assignmentId, latitude: s.latitude!, longitude: s.longitude!, color: c?.color ?? "#64748B",
					label: `${c ? `${c.name} ${s.order}` : ""}: ${s.title}`,
					popup: [{ text: s.title, href: `/bauakten/${s.bauakteId}`, strong: true }, { text: `${c?.name ?? "Ohne Kolonne"} · ${s.order}. Station${s.startTime ? ` · ${hhmm(s.startTime)}` : ""}` },
						{ text: s.address ?? "" }],
				}));
			map.setProperty("markers", markers);
			side.destroyItems();
			for (const c of p.crews.filter((x) => x.stops.length)) {
				side.addItem(new Title({ text: `${c.name}${c.vehicles.length ? ` · ${c.vehicles.map((v) => v.name).join(", ")}` : ""}`, level: "H3", wrapping: true }).addStyleClass("sapUiSmallMarginTop"));
				const list = new List({ showSeparators: "Inner" });
				c.stops.forEach((s, i) => {
					const move = async (dir: number) => {
						const ids = c.stops.map((x) => x.assignmentId);
						[ids[i], ids[i + dir]] = [ids[i + dir], ids[i]];
						try {
							fill(await put<DayPlan>(`planning/crews/${c.id}/days/${day}/order`, { assignmentIds: ids }));
							void this.load(false);
						} catch (error) { this.showError(error); }
					};
					list.addItem(new CustomListItem({
						content: [new HBox({
							alignItems: "Center", justifyContent: "SpaceBetween",
							items: [
								new VBox({ items: [
									new Text({ text: `${s.order}. ${s.title}`, wrapping: true }).addStyleClass("eoStrong"),
									new Text({ text: [s.startTime ? `${hhmm(s.startTime)} Uhr` : "ganztags", s.address].filter(Boolean).join(" · "), wrapping: true }).addStyleClass("eoMuted"),
									...(s.latitude === null ? [new ObjectStatus({ text: "ohne Standort", state: "Warning" })] : []),
								] }),
								new HBox({ visible: canPlan, items: [
									new Button({ icon: "sap-icon://navigation-up-arrow", type: "Transparent", tooltip: "Früher anfahren", enabled: i > 0, press: () => void move(-1) }),
									new Button({ icon: "sap-icon://navigation-down-arrow", type: "Transparent", tooltip: "Später anfahren", enabled: i < c.stops.length - 1, press: () => void move(1) }),
								] }),
							],
						}).addStyleClass("sapUiTinyMargin")],
					}));
				});
				side.addItem(list);
			}
			if (!p.crews.some((c) => c.stops.length) && !p.withoutCrew.length) side.addItem(new Text({ text: "An diesem Tag ist nichts geplant." }).addStyleClass("sapUiSmallMargin"));
		};
		fill(plan);
		const dialog = new Dialog({
			title: `Einsätze ${longDay(day)} – Karte und Fahrtreihenfolge`, contentWidth: "64rem", resizable: true, draggable: true,
			content: [new HBox({ items: [new VBox({ items: [map], width: "100%" }).addStyleClass("eoGrow"), side] }).addStyleClass("eoCkMapLayout")],
			buttons: [
				new Button({ text: "Wer fährt wohin", icon: "sap-icon://journey-arrive", press: () => { dialog.close(); this.nav(`/planung/wer-faehrt-wohin?tag=${day}`); } }),
				new Button({ text: "Schließen", press: () => dialog.close() }),
			],
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	// ---- Freigabe -----------------------------------------------------------------------------------------------------

	onRelease(): void {
		const board = this.board;
		if (!board) return;
		const today = isoDay(new Date());
		const days = board.days.map((d) => d.day);
		const visible = days.filter((d) => d >= today);
		const options = [
			{ key: "visible", text: visible.length ? `Sichtbarer Zeitraum ab heute (${short(visible[0])} – ${short(visible[visible.length - 1])})` : "Sichtbarer Zeitraum", from: visible[0] ?? days[0], to: days[days.length - 1] },
			{ key: "today", text: `Heute (${short(today)})`, from: today, to: today },
			{ key: "tomorrow", text: `Morgen (${short(addDays(today, 1))})`, from: addDays(today, 1), to: addDays(today, 1) },
			{ key: "week", text: `Nächste Woche (KW ${isoWeek(addDays(mondayOf(today), 7))})`, from: addDays(mondayOf(today), 7), to: addDays(mondayOf(today), 13) },
		];
		const range = new Select({ width: "100%", items: options.map((o) => new Item({ key: o.key, text: o.text })) });
		const note = new TextArea({ width: "100%", rows: 2, maxLength: 500, placeholder: "Hinweis an die Kolonnen (optional), z. B. „Treffpunkt 6:30 Uhr am Lager“" });
		const changes = board.days.filter((d) => d.release?.changedCount).map((d) => `${short(d.day)}: ${d.release!.changedCount} ${d.release!.changedCount === 1 ? "Änderung" : "Änderungen"}`
			+ (d.release!.removed.length ? ` (entfernt: ${d.release!.removed.map((r) => r.title).join(", ")})` : ""));
		const dialog = new Dialog({
			title: "Plan freigeben", contentWidth: "32rem",
			content: [new VBox({
				items: [
					new Text({ text: "Freigegebene Tage sehen die Mitarbeiter in der App unter „Wer fährt wohin“ als verbindlich. Spätere Änderungen werden markiert, bis Sie erneut freigeben." }).addStyleClass("sapUiSmallMarginBottom"),
					new Label({ text: "Zeitraum", labelFor: range }), range,
					new Label({ text: "Hinweis", labelFor: note }).addStyleClass("sapUiSmallMarginTop"), note,
					...(changes.length ? [new Label({ text: "Änderungen seit der letzten Freigabe" }).addStyleClass("sapUiSmallMarginTop"), new Text({ text: changes.join("\n") })] : []),
				],
			}).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({
				text: "Freigeben", type: "Emphasized", press: async () => {
					const o = options.find((x) => x.key === range.getSelectedKey()) ?? options[0];
					try {
						const result = await post<PlanReleaseInfo[]>("planning/releases", { from: o.from, to: o.to, note: note.getValue() || null });
						dialog.close();
						MessageToast.show(`Plan freigegeben: ${result.length} ${result.length === 1 ? "Tag" : "Tage"}.`);
						void this.load(false);
					} catch (error) { this.showError(error); }
				},
			}),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	// ---- Druck ------------------------------------------------------------------------------------------------------------

	private pdfDay(): string {
		const days = this.days();
		const today = isoDay(new Date());
		return days.includes(today) ? today : days[0];
	}

	onPdfWeek(): void {
		download(`/api/v1/planning/print.pdf?mode=week&from=${mondayOf(this.days()[0])}&rows=crews&weekend=false`).catch((error: unknown) => this.showError(error));
	}

	onPdfDay(): void {
		download(`/api/v1/planning/print.pdf?mode=day&from=${this.pdfDay()}&rows=crews&weekend=false`).catch((error: unknown) => this.showError(error));
	}

	onPdfDispatch(): void {
		download(`/api/v1/planning/dispatch/${this.pdfDay()}/print.pdf`).catch((error: unknown) => this.showError(error));
	}
}
