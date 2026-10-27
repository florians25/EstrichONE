import { trackDrag } from "../util/pointerDrag";
import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";
import Element from "sap/ui/core/Element";
import IconPool from "sap/ui/core/IconPool";
import { assignmentKinds } from "../util/labels";
import { parseDay } from "../util/days";
import { hhmm, minutes, ofTime, toTime, type CockpitCell, type CockpitCrew, type CockpitDay, type PlanAssignment } from "../util/cockpitModels";

export type CockpitMode = "week" | "twoWeeks" | "month" | "day";

export interface CockpitRender {
	mode: CockpitMode;
	days: CockpitDay[];
	today: string;
	crews: CockpitCrew[];
	/** Zeile „Ohne Kolonne“ (Einsätze einzelner Mitarbeiter). */
	noCrewLane: boolean;
	assignments: PlanAssignment[];
	selected: string[];
	/** Durch Suche/Filter ausgeblendete (abgeschwächte) Einsätze. */
	dimmed: string[];
	canPlan: boolean;
	/** Zeitachse der Tagesansicht (Stunden). */
	hours: [number, number];
}

export interface MenuItem { text: string; icon?: string; action?: () => void; danger?: boolean; disabled?: boolean; children?: MenuItem[] }

/** Nutzlast beim Ziehen: Einsätze (Auswahl) oder ein Bauvorhaben aus dem Vorrat. */
export type CockpitPayload =
	| { kind: "assignment"; id: string; grabOffset: number }
	| { kind: "bauakte"; id: string; label: string };

const NO_CREW = "-";
const SNAP = 15;
const safeColor = (c: string | null | undefined) => (c && /^#[0-9a-fA-F]{3,8}$/.test(c) ? c : "");
const shortDay = (d: string) => parseDay(d).toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit" });
const weekday = (d: string) => parseDay(d).toLocaleDateString("de-DE", { weekday: "short" });
const longDay = (d: string) => parseDay(d).toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long" });

function el<K extends keyof HTMLElementTagNameMap>(tag: K, cls?: string, text?: string): HTMLElementTagNameMap[K] {
	const e = document.createElement(tag);
	if (cls) e.className = cls;
	if (text !== undefined) e.textContent = text;
	return e;
}

export function icon(name: string, cls = "eoCkIcon"): HTMLElement {
	const span = el("span", cls);
	const info = IconPool.getIconInfo(name.startsWith("sap-icon://") ? name : `sap-icon://${name}`);
	if (info && typeof info === "object" && "content" in info) {
		span.textContent = String(info.content);
		span.style.fontFamily = `"${String(info.fontFamily)}"`;
	}
	span.setAttribute("aria-hidden", "true");
	return span;
}

/** Ziehen starten (Karten im Raster, Bauvorhaben im Vorrat); abgelegt wird auf Zellen mit data-drop. */
export function startCockpitDrag(event: PointerEvent, source: HTMLElement, payload: CockpitPayload): void {
	trackDrag(event, source, {
		target: "[data-drop]", overClass: "eoCkOver", draggingClass: "eoCkDragging",
		onDrop: (cell, e) => {
			const host = cell.closest<HTMLElement>(".eoCk");
			const control = host ? (Element.getElementById(host.id) as Cockpit | undefined) : undefined;
			control?.handleDrop(payload, cell, e);
		},
	});
}

/**
 * Disponenten-Cockpit: Planungsraster Kolonnen × Tage (Woche, 2 Wochen, Monat) bzw. Zeitachse eines Tages.
 * Ziehen (Strg = kopieren), Mehrfachauswahl, Kante ziehen (mehrtägig bzw. Dauer), Kontextmenü, Kapazität und Konflikte je Zelle.
 * Gerendert per DOM-API (CSP-konform); Scrollposition bleibt beim Neuzeichnen erhalten.
 * @namespace estrichone.control
 */
export default class Cockpit extends Control {
	static readonly metadata: MetadataOptions = {
		properties: { data: { type: "object" } },
		events: {
			drop: { parameters: { payload: { type: "object" }, crewId: { type: "string" }, day: { type: "string" }, copy: { type: "boolean" }, start: { type: "string" } } },
			open: { parameters: { id: { type: "string" } } },
			select: { parameters: { id: { type: "string" }, toggle: { type: "boolean" } } },
			menu: { parameters: { assignmentId: { type: "string" }, crewId: { type: "string" }, day: { type: "string" }, x: { type: "int" }, y: { type: "int" } } },
			extend: { parameters: { id: { type: "string" }, day: { type: "string" } } },
			retime: { parameters: { id: { type: "string" }, start: { type: "string" }, end: { type: "string" } } },
			dayPress: { parameters: { day: { type: "string" } } },
			add: { parameters: { crewId: { type: "string" }, day: { type: "string" } } },
			cellInfo: { parameters: { crewId: { type: "string" }, day: { type: "string" } } },
		},
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: Cockpit): void {
			rm.openStart("div", control);
			rm.class("eoCk");
			rm.openEnd();
			rm.close("div");
		},
	};

	private scroll: HTMLElement | null = null;

	private dataValue(): CockpitRender | null { return this.getProperty("data") as CockpitRender | null; }

	onAfterRendering(): void {
		const host = this.getDomRef() as HTMLElement | null;
		if (!host) return;
		if (!this.scroll || !host.contains(this.scroll)) {
			this.scroll = el("div", "eoCkScroll");
			host.replaceChildren(this.scroll);
		}
		this.draw();
	}

	/** Neu zeichnen ohne UI5-Rerendering (Scrollposition bleibt). */
	draw(): void {
		const data = this.dataValue();
		if (!this.scroll || !data) return;
		const { scrollLeft, scrollTop } = this.scroll;
		const focused = (document.activeElement as HTMLElement | null)?.dataset?.id;
		this.scroll.replaceChildren(data.mode === "day" ? this.timeline(data) : this.grid(data));
		this.scroll.scrollLeft = scrollLeft;
		this.scroll.scrollTop = scrollTop;
		if (focused) this.scroll.querySelector<HTMLElement>(`[data-id="${CSS.escape(focused)}"]`)?.focus({ preventScroll: true });
	}

	/** Auswahl ohne Neuzeichnen markieren. */
	markSelection(ids: string[]): void {
		const set = new Set(ids);
		for (const card of this.scroll?.querySelectorAll<HTMLElement>(".eoCkCard") ?? []) {
			const on = set.has(card.dataset.id ?? "");
			card.classList.toggle("eoCkSelected", on);
			card.setAttribute("aria-pressed", String(on));
		}
	}

	focusCard(id: string): void { this.scroll?.querySelector<HTMLElement>(`.eoCkCard[data-id="${CSS.escape(id)}"]`)?.focus(); }

	// ---- Raster (Woche, 2 Wochen, Monat) ------------------------------------------------------------------------------

	private inCell(data: CockpitRender, crewId: string | null, day: string): PlanAssignment[] {
		return data.assignments.filter((a) => a.day === day && (a.crewId ?? null) === crewId)
			.sort((x, y) => ((x.sequence || 999) - (y.sequence || 999)) || (x.startTime ?? "").localeCompare(y.startTime ?? "") || x.activity.localeCompare(y.activity));
	}

	private inDay(data: CockpitRender, day: string): boolean { return data.assignments.some((a) => a.day === day); }

	private grid(data: CockpitRender): HTMLElement {
		const grid = el("div", `eoCkGrid eoCk-${data.mode}`);
		grid.setAttribute("role", "grid");
		grid.setAttribute("aria-label", "Planungsraster Kolonnen und Tage");
		const [work, off] = data.mode === "month" ? ["minmax(2.5rem, 1fr)", "minmax(1.5rem, 0.5fr)"]
			: data.mode === "twoWeeks" ? ["minmax(5.25rem, 1fr)", "minmax(2.25rem, 0.3fr)"] : ["minmax(6.25rem, 1fr)", "minmax(2.75rem, 0.35fr)"];
		grid.style.gridTemplateColumns = `minmax(8.5rem, ${data.mode === "month" ? 1.6 : 1.5}fr) ${data.days.map((d) => (d.isWorkday || this.inDay(data, d.day) ? work : off)).join(" ")}`;
		const head = el("div", "eoCkRow eoCkHead");
		head.setAttribute("role", "row");
		const corner = el("div", "eoCkLane eoCkCorner", data.mode === "month" ? "Kolonne" : "Kolonne / Tag");
		corner.setAttribute("role", "columnheader");
		head.append(corner);
		for (const d of data.days) head.append(this.dayHead(data, d));
		grid.append(head);
		for (const crew of data.crews) grid.append(this.crewRow(data, crew));
		if (data.noCrewLane) grid.append(this.crewRow(data, null));
		if (!data.crews.length && !data.noCrewLane) grid.append(el("div", "eoCkEmpty", "Keine Kolonnen in dieser Auswahl. Kolonnen legen Sie unter Büro → Kolonnen an."));
		return grid;
	}

	private dayHead(data: CockpitRender, d: CockpitDay): HTMLElement {
		const cell = el("div", `eoCkDayHead${d.day === data.today ? " eoCkToday" : ""}${d.isWorkday ? "" : " eoCkOff"}`);
		cell.setAttribute("role", "columnheader");
		const link = el("button", "eoCkDayLink");
		link.type = "button";
		link.title = `${longDay(d.day)} als Zeitachse öffnen`;
		link.append(el("span", "eoCkWd", weekday(d.day)), el("span", "eoCkDate", data.mode === "month" ? String(parseDay(d.day).getDate()) : shortDay(d.day)));
		link.addEventListener("click", () => this.fireEvent("dayPress", { day: d.day }));
		cell.append(link);
		if (d.holiday) {
			const h = el("span", "eoCkHoliday", data.mode === "month" ? "F" : d.holiday);
			h.title = d.holiday;
			cell.append(h);
		}
		if (d.release) {
			const changed = d.release.changedCount;
			const badge = el("span", `eoCkRelease${changed ? " eoCkReleaseChanged" : ""}`);
			badge.append(icon(changed ? "sap-icon://edit" : "sap-icon://accept"));
			if (data.mode !== "month") badge.append(document.createTextNode(changed ? ` ${changed} geändert` : " freigegeben"));
			badge.title = `Freigegeben ${new Date(d.release.releasedAt).toLocaleString("de-DE", { dateStyle: "short", timeStyle: "short" })} von ${d.release.releasedByName}`
				+ (changed ? ` – seither ${changed} ${changed === 1 ? "Änderung" : "Änderungen"}` : "");
			cell.append(badge);
		}
		return cell;
	}

	private crewHead(crew: CockpitCrew | null, data: CockpitRender): HTMLElement {
		const lane = el("div", "eoCkLane");
		lane.setAttribute("role", "rowheader");
		const sw = el("span", "eoCkSwatch");
		if (crew && safeColor(crew.color)) sw.style.background = safeColor(crew.color);
		const text = el("div", "eoCkLaneText");
		text.append(el("strong", "eoCkLaneName", crew ? crew.name : "Ohne Kolonne"));
		if (crew) {
			const members = crew.members.filter((m) => m.id !== crew.leaderUserId).map((m) => m.name.split(" ")[0]).join(", ");
			const sub = el("small", "eoCkLaneSub", [crew.leaderName ? `${crew.leaderName.split(" ")[0]} (Ltg.)` : "", members].filter(Boolean).join(" · ") || "keine Mitglieder");
			sub.title = crew.members.map((m) => m.name).join(", ");
			text.append(sub);
			if (crew.vehicles.length) text.append(el("small", "eoCkLaneSub", crew.vehicles.map((v) => v.name).join(", ")));
			const planned = crew.cells.reduce((s, c) => s + this.planned(data, crew.id, c.day), 0);
			const capacity = crew.cells.reduce((s, c) => s + c.capacityMinutes, 0);
			const percent = capacity ? Math.round((planned / capacity) * 100) : planned ? 100 : 0;
			const bar = el("div", `eoCkLoad ${this.loadState(percent)}`);
			bar.setAttribute("role", "img");
			bar.setAttribute("aria-label", `Auslastung ${percent} %`);
			bar.title = `Auslastung im Zeitraum: ${percent} % (${Math.round(planned / 60)} von ${Math.round(capacity / 60)} h)`;
			const fill = el("span");
			fill.style.width = `${Math.min(100, percent)}%`;
			bar.append(fill);
			text.append(bar);
		} else {
			text.append(el("small", "eoCkLaneSub", "Einzelne Mitarbeiter"));
		}
		lane.append(sw, text);
		return lane;
	}

	private planned(data: CockpitRender, crewId: string, day: string): number {
		return data.assignments.filter((a) => a.crewId === crewId && a.day === day).reduce((s, a) => s + minutes(a.startTime, a.endTime), 0);
	}

	private loadState(percent: number): string {
		return percent > 100 ? "eoCkLoadOver" : percent >= 85 ? "eoCkLoadHigh" : "eoCkLoadOk";
	}

	private crewRow(data: CockpitRender, crew: CockpitCrew | null): HTMLElement {
		const row = el("div", "eoCkRow");
		row.setAttribute("role", "row");
		row.append(this.crewHead(crew, data));
		for (const d of data.days) row.append(this.cell(data, crew, d, crew?.cells.find((c) => c.day === d.day) ?? null));
		return row;
	}

	private cell(data: CockpitRender, crew: CockpitCrew | null, d: CockpitDay, info: CockpitCell | null): HTMLElement {
		const crewId = crew?.id ?? null;
		const jobs = this.inCell(data, crewId, d.day);
		const cell = el("div", `eoCkCell${d.day === data.today ? " eoCkToday" : ""}${d.isWorkday ? "" : " eoCkOff"}`);
		cell.setAttribute("role", "gridcell");
		cell.dataset.drop = "1";
		cell.dataset.crew = crewId ?? NO_CREW;
		cell.dataset.day = d.day;
		const label = `${crew?.name ?? "Ohne Kolonne"}, ${longDay(d.day)}`;
		cell.setAttribute("aria-label", `${label}: ${jobs.length ? `${jobs.length} ${jobs.length === 1 ? "Einsatz" : "Einsätze"}` : "frei"}`);
		if (info && crew && (data.mode !== "month" || jobs.length)) {
			const planned = jobs.reduce((s, a) => s + minutes(a.startTime, a.endTime), 0);
			const percent = info.capacityMinutes ? Math.round((planned / info.capacityMinutes) * 100) : planned ? 101 : 0;
			const meter = el("div", `eoCkCap ${this.loadState(percent)}`);
			const fill = el("span");
			fill.style.width = `${Math.min(100, percent)}%`;
			meter.append(fill);
			meter.title = info.capacityMinutes
				? `${Math.round(planned / 60 * 10) / 10} von ${info.capacityMinutes / 60} h verplant · ${info.availableMembers} von ${info.members.length} verfügbar`
				: info.availableMembers === 0 && info.members.length ? "Niemand aus der Kolonne verfügbar" : "Kein Arbeitstag";
			cell.append(meter);
			const absent = info.members.filter((m) => m.absenceNote);
			const warnings = info.warnings;
			if (absent.length || info.rosterChanged || warnings.length) {
				const facts = el("div", "eoCkFacts");
				if (data.mode !== "month") {
					for (const m of absent) {
						const chip = el("span", "eoCkAbsent", `${m.name.split(" ")[0]}: ${m.absenceNote}`);
						chip.title = `${m.name}: ${m.absenceNote}`;
						facts.append(chip);
					}
				} else if (absent.length) {
					const chip = el("span", "eoCkAbsent", String(absent.length));
					chip.title = absent.map((m) => `${m.name}: ${m.absenceNote}`).join("\n");
					facts.append(chip);
				}
				if (info.rosterChanged) {
					const r = el("span", "eoCkRoster");
					r.append(icon("sap-icon://person-placeholder"));
					r.title = `Tagesbesetzung: ${info.members.map((m) => m.name).join(", ") || "niemand"}${info.vehicles.length ? ` · ${info.vehicles.map((v) => v.name).join(", ")}` : ""}${info.rosterNote ? ` · ${info.rosterNote}` : ""}`;
					facts.append(r);
				}
				if (warnings.length && jobs.length) {
					const w = el("button", "eoCkWarnBtn");
					w.type = "button";
					w.append(icon("sap-icon://alert"), document.createTextNode(` ${warnings.length}`));
					w.title = warnings.join("\n");
					w.setAttribute("aria-label", `${warnings.length} Konflikte: ${warnings.join(" ")}`);
					w.addEventListener("click", (e) => { e.stopPropagation(); this.fireEvent("cellInfo", { crewId: crewId ?? "", day: d.day }); });
					facts.append(w);
					cell.classList.add("eoCkConflict");
				}
				cell.append(facts);
			}
		}
		for (const a of jobs) cell.append(this.card(data, a));
		if (data.canPlan) {
			const add = el("button", "eoCkAdd");
			add.type = "button";
			add.append(icon("sap-icon://add"));
			add.setAttribute("aria-label", `Einsatz planen: ${label}`);
			add.title = "Einsatz planen";
			add.addEventListener("click", () => this.fireEvent("add", { crewId: crewId ?? "", day: d.day }));
			cell.append(add);
		}
		cell.addEventListener("contextmenu", (e) => {
			if ((e.target as HTMLElement).closest(".eoCkCard")) return;
			e.preventDefault();
			this.fireEvent("menu", { assignmentId: "", crewId: crewId ?? "", day: d.day, x: e.clientX, y: e.clientY });
		});
		cell.addEventListener("dblclick", (e) => {
			if (!(e.target as HTMLElement).closest(".eoCkCard") && data.canPlan) this.fireEvent("add", { crewId: crewId ?? "", day: d.day });
		});
		return cell;
	}

	private card(data: CockpitRender, a: PlanAssignment, timeline?: { left: number; width: number }): HTMLElement {
		const compact = data.mode === "month";
		const kind = assignmentKinds[a.kind];
		const card = el("div", `eoCkCard${compact ? " eoCkCompact" : ""}${a.warnings.length ? " eoCkWarn" : ""}`);
		card.dataset.id = a.id;
		card.dataset.flipKey = `ck:${a.id}`;
		card.tabIndex = 0;
		card.setAttribute("role", "button");
		const selected = data.selected.includes(a.id);
		if (selected) card.classList.add("eoCkSelected");
		if (data.dimmed.includes(a.id)) card.classList.add("eoCkDim");
		card.setAttribute("aria-pressed", String(selected));
		const time = a.startTime ? `${hhmm(a.startTime)}${a.endTime ? `–${hhmm(a.endTime)}` : ""}` : "ganztags";
		const title = `${a.bauakteNumber ? `${a.bauakteNumber} · ` : ""}${a.bauakteTitle}`;
		card.setAttribute("aria-label", `${a.activity}, ${title}, ${time}${a.warnings.length ? `, ${a.warnings.length} Hinweise` : ""}${a.change ? ", geändert seit Freigabe" : ""}`);
		card.title = [title, `${a.activity} · ${time}`, a.bauakteAddress ?? "", a.people.length ? `Team: ${a.people.map((p) => p.name).join(", ")}` : "",
			a.resources.length ? `Geräte: ${a.resources.map((r) => r.name).join(", ")}` : "", ...a.warnings.map((w) => `⚠ ${w}`),
			a.change ? (a.change === "new" ? "Neu seit Freigabe" : "Geändert seit Freigabe") : ""].filter(Boolean).join("\n");
		if (safeColor(kind?.color)) card.style.setProperty("--eo-kind", safeColor(kind?.color));
		if (timeline) {
			card.style.left = `${timeline.left}%`;
			card.style.width = `${timeline.width}%`;
		}
		const top = el("div", "eoCkCardTop");
		if (a.sequence && !compact) top.append(el("span", "eoCkSeq", String(a.sequence)));
		if (!compact) top.append(el("span", "eoCkTime", time));
		if (a.change) {
			const dot = el("span", `eoCkChange eoCkChange-${a.change}`);
			dot.title = a.change === "new" ? "Neu seit Freigabe" : "Geändert seit Freigabe";
			top.append(dot);
		}
		if (a.warnings.length && !compact) top.append(icon("sap-icon://alert", "eoCkIcon eoCkWarnIcon"));
		if (top.childElementCount) card.append(top);
		card.append(el("span", "eoCkTitle", a.bauakteTitle));
		if (!compact) {
			const city = a.bauakteAddress?.split(",").pop()?.trim().replace(/^\d{5}\s*/, "") ?? "";
			card.append(el("span", "eoCkSub", [a.activity, city].filter(Boolean).join(" · ")));
		}
		if (data.canPlan) {
			card.classList.add("eoCkMovable");
			if (data.mode === "week" || data.mode === "twoWeeks" || timeline) {
				const grip = el("span", "eoCkGrip");
				grip.dataset.nodrag = "1";
				grip.title = timeline ? "Dauer ziehen" : "Kante ziehen: auf weitere Tage ausdehnen";
				grip.addEventListener("pointerdown", (e) => (timeline ? this.resizeTime(e, card, a, data) : this.extendDays(e, card, a)));
				card.append(grip);
			}
			card.addEventListener("pointerdown", (e) => {
				if ((e.target as HTMLElement).closest(".eoCkGrip")) return;
				const rect = card.getBoundingClientRect();
				startCockpitDrag(e, card, { kind: "assignment", id: a.id, grabOffset: e.clientX - rect.left });
			});
		}
		card.addEventListener("click", (e) => {
			this.fireEvent("select", { id: a.id, toggle: e.ctrlKey || e.metaKey || e.shiftKey });
		});
		card.addEventListener("dblclick", (e) => { e.stopPropagation(); this.fireEvent("open", { id: a.id }); });
		card.addEventListener("keydown", (e) => {
			if (e.key === "Enter") { e.preventDefault(); this.fireEvent("open", { id: a.id }); }
			if (e.key === " ") { e.preventDefault(); this.fireEvent("select", { id: a.id, toggle: true }); }
			if (e.key === "ContextMenu" || (e.shiftKey && e.key === "F10")) {
				e.preventDefault();
				const r = card.getBoundingClientRect();
				this.fireEvent("menu", { assignmentId: a.id, crewId: a.crewId ?? "", day: a.day, x: Math.round(r.left + 12), y: Math.round(r.bottom) });
			}
		});
		card.addEventListener("contextmenu", (e) => {
			e.preventDefault();
			e.stopPropagation();
			this.fireEvent("menu", { assignmentId: a.id, crewId: a.crewId ?? "", day: a.day, x: e.clientX, y: e.clientY });
		});
		return card;
	}

	/** Kante ziehen: Einsatz auf Folgetage derselben Kolonne ausdehnen (Vorschau über die Zellen). */
	private extendDays(event: PointerEvent, card: HTMLElement, a: PlanAssignment): void {
		if (event.button !== 0) return;
		event.preventDefault();
		event.stopPropagation();
		window.getSelection()?.removeAllRanges();
		document.body.classList.add("is-dragging", "eoCkResizing");
		const row = card.closest<HTMLElement>(".eoCkRow");
		const cells = [...(row?.querySelectorAll<HTMLElement>(".eoCkCell") ?? [])];
		let target = a.day;
		const paint = () => {
			for (const c of cells) c.classList.toggle("eoCkSpan", (c.dataset.day ?? "") > a.day && (c.dataset.day ?? "") <= target);
		};
		const move = (e: PointerEvent) => {
			const hit = cells.find((c) => { const r = c.getBoundingClientRect(); return e.clientX >= r.left && e.clientX < r.right; });
			const day = hit?.dataset.day ?? target;
			target = day < a.day ? a.day : day;
			paint();
		};
		const up = () => {
			window.removeEventListener("pointermove", move, true);
			window.removeEventListener("pointerup", up, true);
			document.body.classList.remove("is-dragging", "eoCkResizing");
			for (const c of cells) c.classList.remove("eoCkSpan");
			if (target > a.day) this.fireEvent("extend", { id: a.id, day: target });
		};
		window.addEventListener("pointermove", move, true);
		window.addEventListener("pointerup", up, true);
	}

	// ---- Zeitachse (Tag) -------------------------------------------------------------------------------------------

	private timeline(data: CockpitRender): HTMLElement {
		const [from, to] = data.hours;
		const span = (to - from) * 60;
		const day = data.days[0];
		const wrap = el("div", "eoCkTimeline");
		wrap.setAttribute("role", "grid");
		wrap.setAttribute("aria-label", `Zeitachse ${longDay(day.day)}`);
		wrap.style.setProperty("--eo-hours", String(to - from));
		const head = el("div", "eoCkTRow eoCkHead");
		head.append(el("div", "eoCkLane eoCkCorner", longDay(day.day)));
		const scale = el("div", "eoCkScale");
		for (let h = from; h < to; h++) scale.append(el("span", "eoCkHour", `${String(h).padStart(2, "0")}:00`));
		head.append(scale);
		wrap.append(head);
		const lanes: (CockpitCrew | null)[] = [...data.crews, ...(data.noCrewLane ? [null] : [])];
		for (const crew of lanes) {
			const row = el("div", "eoCkTRow");
			row.append(this.crewHead(crew, data));
			const track = el("div", "eoCkTrack");
			track.dataset.drop = "1";
			track.dataset.crew = crew?.id ?? NO_CREW;
			track.dataset.day = day.day;
			track.setAttribute("aria-label", `${crew?.name ?? "Ohne Kolonne"}, Zeitachse`);
			const info = crew?.cells[0];
			const absent = info?.members.filter((m) => m.absenceNote) ?? [];
			if (absent.length || info?.warnings.length) {
				const facts = el("div", "eoCkTFacts");
				for (const m of absent) facts.append(el("span", "eoCkAbsent", `${m.name}: ${m.absenceNote}`));
				if (info?.warnings.length && this.inCell(data, crew?.id ?? null, day.day).length) {
					const w = el("button", "eoCkWarnBtn");
					w.type = "button";
					w.append(icon("sap-icon://alert"), document.createTextNode(` ${info.warnings.length}`));
					w.title = info.warnings.join("\n");
					w.addEventListener("click", () => this.fireEvent("cellInfo", { crewId: crew?.id ?? "", day: day.day }));
					facts.append(w);
				}
				track.append(facts);
			}
			const jobs = this.inCell(data, crew?.id ?? null, day.day);
			// Ganztägige Einsätze ohne Uhrzeit: über die ganze Breite (Reihe oben), zeitliche darunter in Spuren ohne Überlappung.
			const lanesEnd: number[] = [];
			for (const a of jobs) {
				const start = a.startTime ? ofTime(a.startTime) : from * 60;
				const end = a.startTime ? start + minutes(a.startTime, a.endTime) : to * 60;
				let lane = lanesEnd.findIndex((e) => e <= start);
				if (lane < 0) { lane = lanesEnd.length; lanesEnd.push(end); } else lanesEnd[lane] = end;
				const left = Math.max(0, ((start - from * 60) / span) * 100);
				const width = Math.max(4, Math.min(100 - left, ((end - start) / span) * 100));
				const card = this.card(data, a, { left, width });
				card.style.top = `${0.375 + lane * 3.875}rem`;
				track.append(card);
			}
			track.style.minHeight = `${Math.max(1, lanesEnd.length) * 3.875 + 1.25}rem`;
			track.addEventListener("contextmenu", (e) => {
				if ((e.target as HTMLElement).closest(".eoCkCard")) return;
				e.preventDefault();
				this.fireEvent("menu", { assignmentId: "", crewId: crew?.id ?? "", day: day.day, x: e.clientX, y: e.clientY });
			});
			track.addEventListener("dblclick", (e) => {
				if (!(e.target as HTMLElement).closest(".eoCkCard") && data.canPlan) this.fireEvent("add", { crewId: crew?.id ?? "", day: day.day });
			});
			row.append(track);
			wrap.append(row);
		}
		const now = new Date();
		if (day.day === data.today) {
			const m = now.getHours() * 60 + now.getMinutes();
			if (m > from * 60 && m < to * 60) wrap.style.setProperty("--eo-now", `${((m - from * 60) / span) * 100}%`);
			wrap.classList.add("eoCkHasNow");
		}
		return wrap;
	}

	/** Zeitpunkt (auf 15 min gerundet) an einer x-Position der Zeitachse. */
	private timeAt(track: HTMLElement, x: number): number {
		const data = this.dataValue()!;
		const [from, to] = data.hours;
		const r = track.getBoundingClientRect();
		const ratio = Math.min(1, Math.max(0, (x - r.left) / r.width));
		return Math.round((from * 60 + ratio * (to - from) * 60) / SNAP) * SNAP;
	}

	/** Dauer in der Zeitachse ziehen (rechte Kante). */
	private resizeTime(event: PointerEvent, card: HTMLElement, a: PlanAssignment, data: CockpitRender): void {
		if (event.button !== 0) return;
		event.preventDefault();
		event.stopPropagation();
		const track = card.closest<HTMLElement>(".eoCkTrack");
		if (!track) return;
		document.body.classList.add("is-dragging", "eoCkResizing");
		const [from, to] = data.hours;
		const span = (to - from) * 60;
		const start = a.startTime ? ofTime(a.startTime) : from * 60;
		let end = start + minutes(a.startTime, a.endTime);
		const label = card.querySelector<HTMLElement>(".eoCkTime");
		const move = (e: PointerEvent) => {
			end = Math.max(start + 30, Math.min(to * 60, this.timeAt(track, e.clientX)));
			card.style.width = `${((end - start) / span) * 100}%`;
			if (label) label.textContent = `${toTime(start).slice(0, 5)}–${toTime(end).slice(0, 5)}`;
		};
		const up = () => {
			window.removeEventListener("pointermove", move, true);
			window.removeEventListener("pointerup", up, true);
			document.body.classList.remove("is-dragging", "eoCkResizing");
			this.fireEvent("retime", { id: a.id, start: toTime(start), end: toTime(end) });
		};
		window.addEventListener("pointermove", move, true);
		window.addEventListener("pointerup", up, true);
	}

	/** Ablegen auf Zelle bzw. Zeitachse (aufgerufen vom Zieh-Kern). */
	handleDrop(payload: CockpitPayload, target: HTMLElement, event: PointerEvent): void {
		const crew = target.dataset.crew ?? NO_CREW;
		let start = "";
		if (target.classList.contains("eoCkTrack")) {
			const offset = payload.kind === "assignment" ? payload.grabOffset : 0;
			start = toTime(this.timeAt(target, event.clientX - offset));
		}
		this.fireEvent("drop", { payload, crewId: crew === NO_CREW ? "" : crew, day: target.dataset.day ?? "", copy: event.ctrlKey || event.metaKey || event.altKey, start });
	}

	// ---- Kontextmenü ---------------------------------------------------------------------------------------------------

	/** Schlichtes Kontextmenü (Tastatur: Pfeile, Enter, Escape; Untermenüs per Pfeil rechts). */
	showMenu(x: number, y: number, items: MenuItem[], title?: string): void {
		this.closeMenu();
		const menu = this.buildMenu(items, title);
		document.body.append(menu);
		const r = menu.getBoundingClientRect();
		menu.style.left = `${Math.max(8, Math.min(x, window.innerWidth - r.width - 8))}px`;
		menu.style.top = `${Math.max(8, Math.min(y, window.innerHeight - r.height - 8))}px`;
		menu.querySelector<HTMLElement>("[role=menuitem]:not([aria-disabled=true])")?.focus();
		const outside = (e: Event) => {
			if (!(e.target as HTMLElement).closest(".eoCkMenu")) this.closeMenu();
		};
		window.setTimeout(() => {
			window.addEventListener("pointerdown", outside, true);
			window.addEventListener("wheel", outside, true);
		});
		(menu as HTMLElement & { cleanup?: () => void }).cleanup = () => {
			window.removeEventListener("pointerdown", outside, true);
			window.removeEventListener("wheel", outside, true);
		};
	}

	closeMenu(): void {
		for (const m of document.querySelectorAll<HTMLElement & { cleanup?: () => void }>(".eoCkMenu")) {
			m.cleanup?.();
			m.remove();
		}
	}

	private buildMenu(items: MenuItem[], title?: string): HTMLElement {
		const menu = el("div", "eoCkMenu");
		menu.setAttribute("role", "menu");
		if (title) {
			const t = el("div", "eoCkMenuTitle", title);
			menu.setAttribute("aria-label", title);
			menu.append(t);
		}
		for (const item of items) {
			if (item.text === "-") {
				menu.append(el("div", "eoCkMenuSep"));
				continue;
			}
			const b = el("div", `eoCkMenuItem${item.danger ? " eoCkMenuDanger" : ""}`);
			b.setAttribute("role", "menuitem");
			b.tabIndex = -1;
			if (item.disabled) b.setAttribute("aria-disabled", "true");
			b.append(item.icon ? icon(item.icon) : el("span", "eoCkIcon"), el("span", "eoCkMenuText", item.text));
			if (item.children) {
				b.setAttribute("aria-haspopup", "menu");
				b.append(icon("sap-icon://navigation-right-arrow", "eoCkIcon eoCkMenuArrow"));
			}
			const activate = () => {
				if (item.disabled) return;
				if (item.children) {
					for (const s of menu.querySelectorAll(".eoCkSub")) s.remove();
					const sub = this.buildMenu(item.children);
					sub.classList.add("eoCkSub");
					document.body.append(sub);
					const r = b.getBoundingClientRect();
					const sr = sub.getBoundingClientRect();
					sub.style.left = `${r.right + sr.width > window.innerWidth ? r.left - sr.width : r.right}px`;
					sub.style.top = `${Math.min(r.top, window.innerHeight - sr.height - 8)}px`;
					sub.querySelector<HTMLElement>("[role=menuitem]")?.focus();
					return;
				}
				this.closeMenu();
				item.action?.();
			};
			b.addEventListener("click", activate);
			b.addEventListener("mouseenter", () => {
				b.focus();
				if (item.children) activate();
				else for (const s of document.querySelectorAll(".eoCkSub")) if (!s.contains(b) && !menu.classList.contains("eoCkSub")) s.remove();
			});
			b.addEventListener("keydown", (e) => {
				const all = [...menu.querySelectorAll<HTMLElement>(":scope > [role=menuitem]")];
				const i = all.indexOf(b);
				if (e.key === "ArrowDown") all[(i + 1) % all.length]?.focus();
				else if (e.key === "ArrowUp") all[(i - 1 + all.length) % all.length]?.focus();
				else if (e.key === "Enter" || e.key === " " || (e.key === "ArrowRight" && item.children)) activate();
				else if (e.key === "Escape" || (e.key === "ArrowLeft" && menu.classList.contains("eoCkSub"))) {
					if (menu.classList.contains("eoCkSub")) menu.remove();
					else this.closeMenu();
				} else return;
				e.preventDefault();
				e.stopPropagation();
			});
			menu.append(b);
		}
		return menu;
	}

	exit(): void { this.closeMenu(); }
}
