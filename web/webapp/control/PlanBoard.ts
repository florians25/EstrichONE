import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";
import IconPool from "sap/ui/core/IconPool";
import Element from "sap/ui/core/Element";
import { assignmentKinds, absenceKinds } from "../util/labels";
import { timeRange } from "../util/format";
import { parseDay } from "../util/days";
import type { AbsenceView, AssignmentView } from "../util/models";
import { calendarEventKinds, parseLocal, type CalendarOccurrence } from "../util/planningModels";

/** Zeile der Plantafel: Kolonne oder Mitarbeiter. */
export interface Lane {
	id: string | null;
	name: string;
	color: string | null;
	sub: string;
	/** Mitarbeiter, deren Abwesenheit die Zeile betrifft (Kolonnenmitglieder bzw. die Person selbst). */
	userIds: string[];
	/** Auslastung im sichtbaren Zeitraum (Kolonnen). */
	load?: { percent: number; state: string; text: string } | null;
}

export interface BoardData {
	mode: "day" | "week" | "month";
	rows: "crews" | "people";
	days: string[];
	today: string;
	month: string;
	canPlan: boolean;
	lanes: Lane[];
	assignments: AssignmentView[];
	absences: AbsenceView[];
	/** Termine ohne Bauvorhaben (Kalender) mit Teilnehmern oder Kolonnen. */
	appointments?: CalendarOccurrence[];
}

const weekdays = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];
const short = (d: string) => parseDay(d).toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit" });
const weekday = (d: string) => parseDay(d).toLocaleDateString("de-DE", { weekday: "short" });
const safeColor = (c: string | null | undefined) => (c && /^#[0-9a-fA-F]{3,8}$/.test(c) ? c : "");

function el<K extends keyof HTMLElementTagNameMap>(tag: K, cls?: string, text?: string): HTMLElementTagNameMap[K] {
	const e = document.createElement(tag);
	if (cls) e.className = cls;
	if (text !== undefined) e.textContent = text;
	return e;
}

function icon(name: string): HTMLElement {
	const span = el("span", "eoPlanIcon");
	const info = IconPool.getIconInfo(name);
	if (info && typeof info === "object" && "content" in info) {
		span.textContent = String(info.content);
		span.style.fontFamily = `"${String(info.fontFamily)}"`;
	}
	span.setAttribute("aria-hidden", "true");
	return span;
}

export type DragPayload = { kind: "assignment"; id: string; lane: string } | { kind: "bauakte"; bauakte: { id: string; label: string } };

let lastDragEnd = 0;
const justDragged = () => Date.now() - lastDragEnd < 300;

/**
 * Ziehen mit Zeigerereignissen (Maus, Stift, Touch) statt HTML5-Drag&Drop: Innerhalb der UI5-Seitencontainer
 * startet Chromium kein natives Ziehen zuverlässig. Abgelegt wird auf Zellen mit data-drop der Plantafel.
 */
export function startDrag(event: PointerEvent, source: HTMLElement, payload: DragPayload): void {
	if (event.button !== 0 || (event.target as HTMLElement).closest("a, input, textarea, select")) return;
	const startX = event.clientX;
	const startY = event.clientY;
	let ghost: HTMLElement | null = null;
	let over: HTMLElement | null = null;
	const cellAt = (x: number, y: number) => {
		ghost?.style.setProperty("display", "none");
		const hit = document.elementFromPoint(x, y) as HTMLElement | null;
		ghost?.style.setProperty("display", "");
		return hit?.closest<HTMLElement>("[data-drop]") ?? null;
	};
	const move = (e: PointerEvent) => {
		if (!ghost) {
			if (Math.abs(e.clientX - startX) + Math.abs(e.clientY - startY) < 6) return;
			ghost = source.cloneNode(true) as HTMLElement;
			ghost.classList.add("eoPlanGhost");
			ghost.style.width = `${Math.min(source.getBoundingClientRect().width, 260)}px`;
			document.body.append(ghost);
			source.classList.add("eoPlanDragging");
		}
		ghost.style.left = `${e.clientX + 8}px`;
		ghost.style.top = `${e.clientY + 8}px`;
		const cell = cellAt(e.clientX, e.clientY);
		if (cell !== over) {
			over?.classList.remove("eoPlanDrop");
			cell?.classList.add("eoPlanDrop");
			over = cell;
		}
		e.preventDefault();
	};
	const end = (e: PointerEvent) => {
		window.removeEventListener("pointermove", move, true);
		window.removeEventListener("pointerup", end, true);
		window.removeEventListener("pointercancel", end, true);
		if (!ghost) return;
		ghost.remove();
		source.classList.remove("eoPlanDragging");
		over?.classList.remove("eoPlanDrop");
		lastDragEnd = Date.now();
		const cell = e.type === "pointerup" ? cellAt(e.clientX, e.clientY) : null;
		const boardDom = cell?.closest<HTMLElement>(".eoPlanBoard");
		const board = boardDom ? (Element.getElementById(boardDom.id) as PlanBoard | undefined) : undefined;
		if (cell && board) board.handleDrop(payload, cell);
	};
	window.addEventListener("pointermove", move, true);
	window.addEventListener("pointerup", end, true);
	window.addEventListener("pointercancel", end, true);
}

/**
 * Plantafel (Dienstplan): Zeilen = Kolonnen oder Mitarbeiter, Spalten = Tage; Schichtkarten per Ziehen verschieben,
 * Abwesenheiten und Konflikte sichtbar, Tastatur: Karten sind Schaltflächen, „+“ je Zelle.
 * Gerendert per DOM-API (CSP-konform: Farben über CSSOM statt Inline-Styles im Markup).
 * @namespace estrichone.control
 */
export default class PlanBoard extends Control {
	static readonly metadata: MetadataOptions = {
		properties: {
			data: { type: "object" },
		},
		events: {
			open: { parameters: { id: { type: "string" } } },
			add: { parameters: { laneId: { type: "string" }, day: { type: "string" } } },
			move: { parameters: { id: { type: "string" }, laneId: { type: "string" }, day: { type: "string" }, fromLaneId: { type: "string" } } },
			dropBauakte: { parameters: { laneId: { type: "string" }, day: { type: "string" }, bauakte: { type: "object" } } },
			dayPress: { parameters: { day: { type: "string" } } },
			openEvent: { parameters: { eventId: { type: "string" }, occurrenceDate: { type: "string" }, start: { type: "string" } } },
		},
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: PlanBoard): void {
			rm.openStart("div", control);
			rm.class("eoPlanBoard");
			rm.openEnd();
			rm.close("div");
		},
	};

	onAfterRendering(): void {
		const host = this.getDomRef() as HTMLElement | null;
		const data = this.getProperty("data") as BoardData | null;
		if (!host || !data) return;
		host.replaceChildren(data.mode === "month" ? this.month(data) : this.grid(data));
	}

	private absentOn(data: BoardData, day: string, userIds?: string[]): AbsenceView[] {
		return data.absences.filter((a) => a.from <= day && day <= a.to && (!userIds || userIds.includes(a.userId)));
	}

	private inCell(data: BoardData, lane: Lane, day: string): AssignmentView[] {
		return data.assignments
			.filter((a) => a.day === day && (data.rows === "people" ? a.people.some((p) => p.id === lane.id) : (a.crewId ?? null) === lane.id))
			.sort((x, y) => (x.startTime ?? "99").localeCompare(y.startTime ?? "99"));
	}

	/** Termine des Tages in der Zeile (Kolonne eingeladen, Person beteiligt bzw. ohne Kolonne). */
	private eventsInCell(data: BoardData, lane: Lane | null, day: string): CalendarOccurrence[] {
		return (data.appointments ?? []).filter((o) => o.start.slice(0, 10) <= day && day < (o.allDay ? o.end.slice(0, 10) : `${o.end.slice(0, 10)}~`)
			&& (!lane ? true : data.rows === "people" ? o.people.some((p) => p.id === lane.id)
				: lane.id ? o.crews.some((c) => c.id === lane.id) : o.crews.length === 0))
			.sort((x, y) => x.start.localeCompare(y.start));
	}

	private eventCard(o: CalendarOccurrence, compact: boolean): HTMLElement {
		const kind = calendarEventKinds[o.kind];
		const b = el("div", `eoPlanCard eoPlanEvent${compact ? " eoPlanCompact" : ""}`);
		b.setAttribute("role", "button");
		b.tabIndex = 0;
		const color = safeColor(o.color) || safeColor(o.calendarColor) || safeColor(kind?.color);
		if (color) b.style.borderLeftColor = color;
		const time = o.allDay ? "ganztags" : `${parseLocal(o.start).toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" })}–${parseLocal(o.end).toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" })}`;
		b.setAttribute("aria-label", `Termin ${o.title}, ${time}`);
		const head = el("span", "eoPlanTime");
		head.append(icon(kind?.icon ?? "sap-icon://appointment"), document.createTextNode(` ${time}${compact ? "" : ` · ${kind?.text ?? "Termin"}`}`));
		b.append(head, el("strong", undefined, o.title));
		if (!compact) {
			if (o.location) b.append(el("span", undefined, o.location));
			if (o.people.length) b.append(el("small", undefined, o.people.map((p) => p.name).join(", ")));
		}
		b.title = [o.title, o.location, o.calendarName].filter(Boolean).join("\n");
		const open = () => this.fireEvent("openEvent", { eventId: o.eventId, occurrenceDate: o.occurrenceDate ?? "", start: o.start });
		b.addEventListener("click", open);
		b.addEventListener("keydown", (e) => {
			if (e.key === "Enter" || e.key === " ") {
				e.preventDefault();
				open();
			}
		});
		return b;
	}

	private card(a: AssignmentView, data: BoardData, compact: boolean, showPeople: boolean): HTMLElement {
		const kind = assignmentKinds[a.kind];
		// div statt button: Chromium startet Ziehen aus <button> nicht zuverlässig; Tastatur über role/tabindex.
		const b = el("div", `eoPlanCard${a.warnings.length ? " eoPlanWarn" : ""}${compact ? " eoPlanCompact" : ""}`);
		b.setAttribute("role", "button");
		b.tabIndex = 0;
		b.addEventListener("keydown", (e) => {
			if (e.key === "Enter" || e.key === " ") {
				e.preventDefault();
				this.fireEvent("open", { id: a.id });
			}
		});
		b.dataset.id = a.id;
		b.dataset.lane = data.rows === "people" ? "" : a.crewId ?? "";
		const color = safeColor(a.crewColor) || safeColor(kind?.color);
		if (color && !a.warnings.length) b.style.borderLeftColor = color;
		b.setAttribute("aria-label", `${a.activity}, ${a.bauakteTitle}, ${timeRange(a.startTime, a.endTime) || "ganztags"}${a.warnings.length ? `, ${a.warnings.length} Hinweise` : ""}`);
		if (data.canPlan) {
			b.classList.add("eoPlanMovable");
			b.addEventListener("pointerdown", (e) => startDrag(e, b, { kind: "assignment", id: a.id, lane: b.dataset.lane ?? "" }));
		}
		const head = el("span", "eoPlanTime");
		const i = icon(kind?.icon ?? "sap-icon://appointment-2");
		if (safeColor(kind?.color)) i.style.color = safeColor(kind?.color);
		head.append(i, document.createTextNode(` ${timeRange(a.startTime, a.endTime) || "ganztags"}${compact ? "" : ` · ${kind?.text ?? a.kind}`}`));
		b.append(head, el("strong", undefined, `${a.bauakteNumber ?? ""} ${a.bauakteTitle}`.trim()));
		if (!compact) {
			if (a.activity) b.append(el("span", undefined, a.activity));
			if (data.rows === "people" && a.crewName) b.append(el("small", undefined, a.crewName));
			if (showPeople && a.people.length) b.append(el("small", undefined, a.people.map((p) => p.name).join(", ")));
			if (a.resources.length) b.append(el("small", undefined, a.resources.map((r) => r.name).join(", ")));
			if (a.warnings.length) {
				const w = el("small", "eoPlanWarning", `⚠ ${a.warnings[0]}${a.warnings.length > 1 ? ` (+${a.warnings.length - 1})` : ""}`);
				w.title = a.warnings.join("\n");
				b.append(w);
			}
		}
		b.title = [a.activity, a.bauakteAddress, ...a.warnings].filter(Boolean).join("\n");
		b.addEventListener("click", () => {
			if (!justDragged()) this.fireEvent("open", { id: a.id });
		});

		return b;
	}

	private dropZone(cell: HTMLElement, laneId: string | null, day: string, data: BoardData): void {
		if (!data.canPlan) return;
		cell.dataset.drop = "1";
		cell.dataset.lane = laneId ?? "";
		cell.dataset.day = day;
	}

	/** Ablegen (aus startDrag): Einsatz verschieben oder Baustelle aus dem Vorrat einplanen. */
	handleDrop(payload: DragPayload, cell: HTMLElement): void {
		const laneId = cell.dataset.lane ?? "";
		const day = cell.dataset.day ?? "";
		if (payload.kind === "assignment") {
			this.fireEvent("move", { id: payload.id, laneId, day, fromLaneId: payload.lane });
		} else {
			this.fireEvent("dropBauakte", { laneId, day, bauakte: payload.bauakte });
		}
	}


	private grid(data: BoardData): HTMLElement {
		const table = el("div", `eoPlanGrid eoPlan-${data.mode}`);
		table.setAttribute("role", "table");
		table.setAttribute("aria-label", data.mode === "day" ? "Tageseinteilung" : "Wochenplan");
		table.style.setProperty("--eo-days", String(data.days.length));
		const head = el("div", "eoPlanRow eoPlanHead");
		head.setAttribute("role", "row");
		const corner = el("div", "eoPlanCell eoPlanLane", data.rows === "people" ? "Mitarbeiter" : "Kolonne");
		corner.setAttribute("role", "columnheader");
		head.append(corner);
		for (const d of data.days) {
			const c = el("div", `eoPlanCell${d === data.today ? " eoPlanToday" : ""}`);
			c.setAttribute("role", "columnheader");
			const link = el("button", "eoPlanDayLink", `${weekday(d)} ${short(d)}`);
			link.type = "button";
			link.title = "Tagesansicht";
			link.addEventListener("click", () => this.fireEvent("dayPress", { day: d }));
			c.append(link);
			head.append(c);
		}
		table.append(head);
		if (data.rows === "crews") {
			const row = el("div", "eoPlanRow eoPlanAbsences");
			row.setAttribute("role", "row");
			const label = el("div", "eoPlanCell eoPlanLane", "Abwesend");
			label.setAttribute("role", "rowheader");
			row.append(label);
			for (const d of data.days) {
				const c = el("div", "eoPlanCell");
				c.setAttribute("role", "cell");
				for (const a of this.absentOn(data, d)) c.append(el("span", `eoPlanAbsence eoAbs-${a.kind}`, `${a.userName} · ${absenceKinds[a.kind] ?? a.kind}`));
				row.append(c);
			}
			table.append(row);
		}
		for (const lane of data.lanes) {
			const row = el("div", "eoPlanRow");
			row.setAttribute("role", "row");
			const label = el("div", "eoPlanCell eoPlanLane");
			label.setAttribute("role", "rowheader");
			const sw = el("span", "eoSwatch");
			if (safeColor(lane.color)) sw.style.background = safeColor(lane.color);
			else sw.classList.add("eoSwatchEmpty");
			const names = el("span", "eoPlanLaneText");
			names.append(el("strong", undefined, lane.name), el("small", undefined, lane.sub));
			if (lane.load) {
				const bar = el("span", `eoLoad eoLoad${lane.load.state}`);
				bar.title = lane.load.text;
				bar.setAttribute("role", "meter");
				bar.setAttribute("aria-label", `Auslastung ${lane.name}: ${lane.load.text}`);
				const fill = el("span", "eoLoadFill");
				fill.style.width = `${Math.min(100, lane.load.percent)}%`;
				bar.append(fill, el("span", "eoLoadText", `${lane.load.percent} %`));
				names.append(bar);
			}
			label.append(sw, names);
			row.append(label);
			for (const d of data.days) {
				const cell = el("div", `eoPlanCell eoPlanSlot${d === data.today ? " eoPlanToday" : ""}`);
				cell.setAttribute("role", "cell");
				const absent = lane.id ? this.absentOn(data, d, lane.userIds) : [];
				if (data.rows === "people" && absent.length) {
					cell.classList.add("eoPlanAbsent");
					cell.append(el("span", `eoPlanAbsence eoAbs-${absent[0].kind}`, absenceKinds[absent[0].kind] ?? absent[0].kind));
				}
				const items = this.inCell(data, lane, d);
				if (data.rows === "people" && items.length > 1) cell.classList.add("eoPlanConflict");
				for (const a of items) cell.append(this.card(a, data, false, data.rows === "crews" ? !lane.id || data.mode === "day" : false));
				for (const o of this.eventsInCell(data, lane, d)) cell.append(this.eventCard(o, false));
				if (data.mode === "day" && data.rows === "crews" && lane.id && absent.length) {
					cell.append(el("small", "eoPlanAbsentNote", `Abwesend: ${absent.map((a) => a.userName).join(", ")}`));
				}
				if (data.canPlan) {
					const add = el("button", "eoPlanAdd", "+");
					add.type = "button";
					add.setAttribute("aria-label", `Einsatz planen: ${lane.name}, ${parseDay(d).toLocaleDateString("de-DE", { weekday: "long" })} ${short(d)}`);
					add.addEventListener("click", () => this.fireEvent("add", { laneId: lane.id ?? "", day: d }));
					cell.append(add);
				}
				this.dropZone(cell, lane.id, d, data);
				row.append(cell);
			}
			table.append(row);
		}
		if (data.lanes.length === 0) {
			const empty = el("div", "eoPlanEmpty", data.rows === "people" ? "Keine Mitarbeiter für diese Auswahl." : "Noch keine Kolonnen – unter „Kolonnen und Geräte“ anlegen.");
			table.append(empty);
		}
		return table;
	}

	private month(data: BoardData): HTMLElement {
		const grid = el("div", "eoPlanMonth");
		grid.setAttribute("role", "grid");
		for (const w of weekdays) {
			const h = el("div", "eoPlanMonthHead", w);
			h.setAttribute("role", "columnheader");
			grid.append(h);
		}
		for (const d of data.days) {
			const cell = el("div", `eoPlanMonthCell${d.slice(0, 7) !== data.month ? " eoPlanOutside" : ""}${d === data.today ? " eoPlanToday" : ""}`);
			cell.setAttribute("role", "gridcell");
			const top = el("div", "eoPlanMonthDay");
			const link = el("button", "eoPlanDayLink", String(parseDay(d).getDate()));
			link.type = "button";
			link.setAttribute("aria-label", `Tagesansicht ${short(d)}`);
			link.addEventListener("click", () => this.fireEvent("dayPress", { day: d }));
			top.append(link);
			const absent = this.absentOn(data, d).length;
			if (absent) top.append(el("span", "eoPlanAbsence", `${absent} abw.`));
			cell.append(top);
			const items = data.assignments.filter((a) => a.day === d).sort((x, y) => (x.startTime ?? "99").localeCompare(y.startTime ?? "99"));
			for (const a of items.slice(0, 4)) cell.append(this.card(a, data, true, false));
			const events = this.eventsInCell(data, null, d);
			for (const o of events.slice(0, Math.max(0, 4 - items.length))) cell.append(this.eventCard(o, true));
			const hidden = Math.max(0, items.length - 4) + Math.max(0, events.length - Math.max(0, 4 - items.length));
			if (hidden > 0) {
				const more = el("button", "eoPlanDayLink eoPlanMore", `+${hidden} weitere`);
				more.type = "button";
				more.addEventListener("click", () => this.fireEvent("dayPress", { day: d }));
				cell.append(more);
			}
			this.dropZone(cell, null, d, { ...data, canPlan: data.canPlan });
			grid.append(cell);
		}
		return grid;
	}
}
