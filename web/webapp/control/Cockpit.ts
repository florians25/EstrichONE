import { trackDrag } from "../util/pointerDrag";
import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";
import Element from "sap/ui/core/Element";
import IconPool from "sap/ui/core/IconPool";
import { assignmentKinds } from "../util/labels";
import { parseDay } from "../util/days";
import { hhmm, hours, minutes, ofTime, toTime, type CockpitCell, type CockpitCrew, type CockpitDay, type PlanAssignment } from "../util/cockpitModels";

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
	/** Kompakte Karten (eine Zeile) in Woche und 2 Wochen. */
	compact?: boolean;
}

export interface MenuItem { text: string; icon?: string; action?: () => void; danger?: boolean; disabled?: boolean; children?: MenuItem[] }

/** Nutzlast beim Ziehen: Einsätze (Auswahl) oder ein Bauvorhaben aus dem Vorrat. */
export type CockpitPayload =
	| { kind: "assignment"; id: string; grabOffset: number }
	| { kind: "bauakte"; id: string; label: string };

const NO_CREW = "-";
const SNAP = 15;
/** Kernarbeitszeit (Zeitachse: außerhalb dezent schattiert). */
const WORK: [number, number] = [7, 17];
const safeColor = (c: string | null | undefined) => (c && /^#[0-9a-fA-F]{3,8}$/.test(c) ? c : "");
const weekday = (d: string) => parseDay(d).toLocaleDateString("de-DE", { weekday: "short" }).replace(".", "");
const monthShort = (d: string) => parseDay(d).toLocaleDateString("de-DE", { month: "short" }).replace(".", "");
const longDay = (d: string) => parseDay(d).toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long" });
const shortLabel = (d: string) => parseDay(d).toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit" });
const firstName = (n: string) => n.split(" ")[0];

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

const controlOf = (node: HTMLElement): Cockpit | undefined => {
	const host = node.closest<HTMLElement>(".eoCk");
	return host ? (Element.getElementById(host.id) as Cockpit | undefined) : undefined;
};

/**
 * Ziehen starten (Karten im Raster, Bauvorhaben im Vorrat); abgelegt wird auf Zellen mit data-drop.
 * Der Geist zeigt Ziel und Art (Verschieben, Kopieren mit Strg, Einplanen) sowie die Anzahl bei Mehrfachauswahl;
 * in der Zeitachse markiert ein Schatten die künftige Lage.
 */
export function startCockpitDrag(event: PointerEvent, source: HTMLElement, payload: CockpitPayload, count = 1): void {
	let last: Cockpit | undefined;
	trackDrag(event, source, {
		target: "[data-drop]", overClass: "eoCkOver", draggingClass: "eoCkDragging",
		onStart: (ghost) => {
			ghost.classList.add("eoCkGhost");
			// Schmale Karten (2 Wochen, Monat) im Geist lesbar breit zeigen.
			ghost.style.width = `${Math.max(parseFloat(ghost.style.width) || 0, 232)}px`;
			ghost.classList.remove("eoCkCompact", "eoCkSelected", "eoCkDim");
			for (const g of ghost.querySelectorAll(".eoCkGrip, .eoCkBlPlan")) g.remove();
			if (count > 1) ghost.append(el("span", "eoCkGhostCount", String(count)));
			ghost.append(el("div", "eoCkGhostHint", "Auf Kolonne und Tag ziehen"));
			document.body.classList.add("eoCkDragActive");
		},
		onOver: (target, e, ghost) => {
			const copy = payload.kind === "assignment" && (e.ctrlKey || e.metaKey || e.altKey);
			ghost.classList.toggle("eoCkGhostCopy", copy);
			ghost.classList.toggle("eoCkGhostAway", !target);
			const control = target ? controlOf(target) : undefined;
			if (last && last !== control) last.clearPreview();
			last = control;
			const hint = ghost.querySelector<HTMLElement>(".eoCkGhostHint");
			if (!hint) return;
			if (!target || !control) {
				hint.textContent = "Loslassen bricht ab";
				return;
			}
			const time = control.preview(payload, target, e);
			const verb = payload.kind === "bauakte" ? "Einplanen" : copy ? "Kopieren" : "Verschieben";
			hint.textContent = `${verb}: ${target.dataset.dropLabel ?? ""}${time ? ` · ${time}` : ""}`;
		},
		onEnd: () => {
			last?.clearPreview();
			document.body.classList.remove("eoCkDragActive");
		},
		onDrop: (cell, e) => controlOf(cell)?.handleDrop(payload, cell, e),
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
	private scrolledFor = "";

	/** Daten setzen ohne Neurendern durch UI5 (sonst ginge die Scrollposition verloren); gezeichnet wird per DOM. */
	setData(value: CockpitRender | null): this {
		this.setProperty("data", value, true);
		this.draw();
		return this;
	}

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
		const key = `${data.mode}|${data.days[0]?.day}`;
		if (key !== this.scrolledFor) {
			// Neuer Zeitraum: an den Anfang bzw. zu heute (Monat, 2 Wochen) bzw. zur aktuellen Uhrzeit (Tag) scrollen.
			this.scrolledFor = key;
			const box = this.scroll;
			box.scrollLeft = 0;
			box.scrollTop = 0;
			requestAnimationFrame(() => {
				const today = box.querySelector<HTMLElement>(".eoCkDayHead.eoCkToday");
				const lane = box.querySelector<HTMLElement>(".eoCkCorner");
				if (today && lane && today.offsetLeft + today.offsetWidth > box.clientWidth) box.scrollLeft = Math.max(0, today.offsetLeft - lane.offsetWidth - 8);
				// Zeitachse: ab Arbeitsbeginn bzw. eine Stunde vor jetzt.
				const scale = box.querySelector<HTMLElement>(".eoCkScale");
				if (data.mode === "day" && scale && box.scrollWidth > box.clientWidth) {
					const hoursEls = scale.querySelectorAll<HTMLElement>(".eoCkHour");
					const now = new Date().getHours() - 1;
					const target = data.days[0]?.day === data.today && now > WORK[0] ? now : WORK[0];
					const h = hoursEls[Math.max(0, target - data.hours[0])];
					if (h) box.scrollLeft = Math.max(0, h.offsetLeft - 8);
				}
			});
		} else {
			this.scroll.scrollLeft = scrollLeft;
			this.scroll.scrollTop = scrollTop;
		}
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

	/** DOM der Karte (für die Schnellansicht). */
	cardDom(id: string): HTMLElement | null { return this.scroll?.querySelector<HTMLElement>(`.eoCkCard[data-id="${CSS.escape(id)}"]`) ?? null; }

	// ---- Raster (Woche, 2 Wochen, Monat) ------------------------------------------------------------------------------

	private inCell(data: CockpitRender, crewId: string | null, day: string): PlanAssignment[] {
		return data.assignments.filter((a) => a.day === day && (a.crewId ?? null) === crewId)
			.sort((x, y) => ((x.sequence || 999) - (y.sequence || 999)) || (x.startTime ?? "").localeCompare(y.startTime ?? "") || x.activity.localeCompare(y.activity));
	}

	private inDay(data: CockpitRender, day: string): boolean { return data.assignments.some((a) => a.day === day); }

	private compact(data: CockpitRender): boolean { return data.mode === "month" || data.compact === true; }

	private grid(data: CockpitRender): HTMLElement {
		const grid = el("div", `eoCkGrid eoCk-${data.mode}${data.compact ? " eoCkDense" : ""}`);
		grid.setAttribute("role", "grid");
		grid.setAttribute("aria-label", "Planungsraster Kolonnen und Tage");
		const [work, off] = data.mode === "month" ? ["minmax(2.5rem, 1fr)", "minmax(1.5rem, 0.45fr)"]
			: data.mode === "twoWeeks" ? ["minmax(5.5rem, 1fr)", "minmax(2.25rem, 0.25fr)"] : ["minmax(6.5rem, 1fr)", "minmax(2.5rem, 0.25fr)"];
		const lane = data.mode === "month" ? "minmax(9.5rem, 0.9fr)" : data.mode === "twoWeeks" ? "minmax(9rem, 1.1fr)" : "minmax(9.5rem, 1.15fr)";
		grid.style.gridTemplateColumns = `${lane} ${data.days.map((d) => (d.isWorkday || this.inDay(data, d.day) ? work : off)).join(" ")}`;
		const head = el("div", "eoCkRow eoCkHead");
		head.setAttribute("role", "row");
		const corner = el("div", "eoCkLane eoCkCorner");
		corner.setAttribute("role", "columnheader");
		const count = data.crews.length;
		corner.append(el("span", "eoCkCornerTitle", "Kolonne"), el("span", "eoCkCornerSub", `${count} ${count === 1 ? "Kolonne" : "Kolonnen"} · Auslastung`));
		head.append(corner);
		for (const d of data.days) head.append(this.dayHead(data, d));
		grid.append(head);
		for (const crew of data.crews) grid.append(this.crewRow(data, crew));
		if (data.noCrewLane) grid.append(this.crewRow(data, null));
		if (!data.crews.length && !data.noCrewLane) grid.append(el("div", "eoCkEmpty", "Keine Kolonnen in dieser Auswahl. Kolonnen legen Sie unter Büro → Kolonnen an."));
		return grid;
	}

	private dayHead(data: CockpitRender, d: CockpitDay): HTMLElement {
		const isToday = d.day === data.today;
		const narrow = data.mode === "month" || (!d.isWorkday && !this.inDay(data, d.day));
		const cell = el("div", `eoCkDayHead${isToday ? " eoCkToday" : ""}${d.isWorkday ? "" : " eoCkOff"}${d.day < data.today ? " eoCkPast" : ""}${narrow ? " eoCkDayNarrow" : ""}`);
		cell.setAttribute("role", "columnheader");
		const link = el("button", "eoCkDayLink");
		link.type = "button";
		link.title = `${longDay(d.day)}${isToday ? " (heute)" : ""} – als Zeitachse öffnen`;
		link.setAttribute("aria-label", `${longDay(d.day)}${isToday ? ", heute" : ""}${d.holiday ? `, ${d.holiday}` : ""}`);
		const date = el("span", "eoCkDateRow");
		date.append(el("span", "eoCkDate", String(parseDay(d.day).getDate())));
		if (!narrow) date.append(el("span", "eoCkMonth", monthShort(d.day)));
		link.append(el("span", "eoCkWd", weekday(d.day)), date);
		link.addEventListener("click", () => this.fireEvent("dayPress", { day: d.day }));
		cell.append(link);
		const status = el("div", "eoCkDayStatus");
		if (d.holiday) {
			const h = narrow ? icon("sap-icon://flag", "eoCkIcon eoCkHolidayIcon") : el("span", "eoCkHoliday", d.holiday);
			h.title = `Feiertag: ${d.holiday}`;
			status.append(h);
		}
		if (d.release) {
			const changed = d.release.changedCount;
			const badge = el("span", `eoCkRelease${changed ? " eoCkReleaseChanged" : ""}`);
			badge.append(icon(changed ? "sap-icon://edit" : "sap-icon://accept"));
			const tight = data.mode === "twoWeeks";
			if (!narrow && (!tight || changed)) badge.append(el("span", "eoCkReleaseText", changed ? (tight ? String(changed) : `${changed} geändert`) : "freigegeben"));
			badge.title = `Freigegeben ${new Date(d.release.releasedAt).toLocaleString("de-DE", { dateStyle: "short", timeStyle: "short" })} von ${d.release.releasedByName}`
				+ (changed ? ` – seither ${changed} ${changed === 1 ? "Änderung" : "Änderungen"}` : "") + (d.release.note ? `\nHinweis: ${d.release.note}` : "");
			status.append(badge);
		} else if (d.isWorkday && d.day >= data.today && this.inDay(data, d.day)) {
			const draft = el("span", "eoCkRelease eoCkReleaseDraft");
			draft.append(icon("sap-icon://pending"));
			if (!narrow && data.mode !== "twoWeeks") draft.append(el("span", "eoCkReleaseText", "Entwurf"));
			draft.title = "Noch nicht freigegeben (Entwurf)";
			status.append(draft);
		}
		if (status.childElementCount) cell.append(status);
		return cell;
	}

	private crewHead(crew: CockpitCrew | null, data: CockpitRender): HTMLElement {
		const lane = el("div", "eoCkLane");
		lane.setAttribute("role", "rowheader");
		const sw = el("span", "eoCkSwatch");
		if (crew && safeColor(crew.color)) sw.style.background = safeColor(crew.color);
		const text = el("div", "eoCkLaneText");
		const name = el("strong", "eoCkLaneName", crew ? crew.name : "Ohne Kolonne");
		name.title = crew?.name ?? "Einsätze einzelner Mitarbeiter";
		text.append(name);
		if (crew) {
			const others = crew.members.filter((m) => m.id !== crew.leaderUserId).map((m) => firstName(m.name));
			const people = [crew.leaderName ? `${firstName(crew.leaderName)} (Ltg.)` : "", ...others].filter(Boolean).join(", ");
			const sub = el("small", "eoCkLaneSub eoCkLanePeople");
			sub.append(icon("sap-icon://group", "eoCkIcon eoCkLaneIcon"), document.createTextNode(people || "keine Mitglieder"));
			sub.title = crew.members.map((m) => m.name).join(", ") || "keine Mitglieder";
			text.append(sub);
			if (crew.vehicles.length && data.mode !== "month") {
				const v = el("small", "eoCkLaneSub");
				v.append(icon("sap-icon://shipping-status", "eoCkIcon eoCkLaneIcon"), document.createTextNode(crew.vehicles.map((x) => x.name).join(", ")));
				v.title = crew.vehicles.map((x) => x.name).join(", ");
				text.append(v);
			}
			const planned = crew.cells.reduce((s, c) => s + this.planned(data, crew.id, c.day), 0);
			const capacity = crew.cells.reduce((s, c) => s + c.capacityMinutes, 0);
			const percent = capacity ? Math.round((planned / capacity) * 100) : planned ? 100 : 0;
			const load = el("div", "eoCkLoadRow");
			const bar = el("div", `eoCkLoad ${this.loadState(percent)}`);
			bar.setAttribute("role", "meter");
			bar.setAttribute("aria-valuemin", "0");
			bar.setAttribute("aria-valuemax", "100");
			bar.setAttribute("aria-valuenow", String(Math.min(100, percent)));
			bar.setAttribute("aria-label", `Auslastung ${percent} %`);
			const fill = el("span");
			fill.style.width = `${Math.min(100, percent)}%`;
			bar.append(fill);
			const pct = el("span", `eoCkLoadText ${this.loadState(percent)}`, `${percent} %`);
			load.append(bar, pct);
			load.title = `Auslastung im Zeitraum: ${percent} % – ${hours(planned)} von ${hours(capacity)} h verplant`;
			text.append(load);
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

	private conflictButton(warnings: string[], crewId: string, day: string, mini = false): HTMLElement {
		const w = el("button", `eoCkWarnBtn${mini ? " eoCkWarnMini" : ""}`);
		w.type = "button";
		w.append(icon("sap-icon://alert"));
		if (!mini) w.append(el("span", "eoCkWarnCount", String(warnings.length)));
		w.title = `${warnings.length} ${warnings.length === 1 ? "Konflikt" : "Konflikte"}:\n${warnings.join("\n")}`;
		w.setAttribute("aria-label", `${warnings.length} Konflikte: ${warnings.join(" ")}`);
		w.addEventListener("click", (e) => { e.stopPropagation(); this.fireEvent("cellInfo", { crewId, day }); });
		return w;
	}

	private cell(data: CockpitRender, crew: CockpitCrew | null, d: CockpitDay, info: CockpitCell | null): HTMLElement {
		const crewId = crew?.id ?? null;
		const jobs = this.inCell(data, crewId, d.day);
		const month = data.mode === "month";
		const cell = el("div", `eoCkCell${d.day === data.today ? " eoCkToday" : ""}${d.isWorkday ? "" : " eoCkOff"}${d.day < data.today ? " eoCkPast" : ""}`);
		cell.setAttribute("role", "gridcell");
		cell.dataset.drop = "1";
		cell.dataset.crew = crewId ?? NO_CREW;
		cell.dataset.day = d.day;
		cell.dataset.dropLabel = `${crew?.name ?? "Ohne Kolonne"}, ${shortLabel(d.day)}`;
		const label = `${crew?.name ?? "Ohne Kolonne"}, ${longDay(d.day)}`;
		cell.setAttribute("aria-label", `${label}: ${jobs.length ? `${jobs.length} ${jobs.length === 1 ? "Einsatz" : "Einsätze"}` : "frei"}`);
		if (info && crew) {
			const planned = jobs.reduce((s, a) => s + minutes(a.startTime, a.endTime), 0);
			const percent = info.capacityMinutes ? Math.round((planned / info.capacityMinutes) * 100) : planned ? 101 : 0;
			const absent = info.members.filter((m) => m.absenceNote);
			const nobody = info.members.length > 0 && info.availableMembers === 0;
			const tip = info.capacityMinutes
				? `${hours(planned)} von ${hours(info.capacityMinutes)} h verplant (${percent} %) · ${info.availableMembers} von ${info.members.length} Mitarbeitern verfügbar`
				: nobody ? `Niemand aus der Kolonne verfügbar – ${hours(planned)} h verplant` : `Kein Arbeitstag – ${hours(planned)} h verplant`;
			// Auslastung als schmaler Balken oben in der Zelle (nur wenn etwas geplant ist).
			if (planned > 0) {
				const meter = el("div", `eoCkCap ${this.loadState(percent)}`);
				const fill = el("span");
				fill.style.width = `${Math.min(100, percent)}%`;
				meter.append(fill);
				meter.title = tip;
				meter.setAttribute("role", "meter");
				meter.setAttribute("aria-label", tip);
				cell.append(meter);
			}
			const facts = el("div", "eoCkFacts");
			if (!month && planned > 0) {
				const cap = el("span", `eoCkCapText ${this.loadState(percent)}`, info.capacityMinutes ? `${hours(planned)}/${hours(info.capacityMinutes)} h` : `${hours(planned)} h`);
				cap.title = tip;
				facts.append(cap);
			}
			if (info.warnings.length && jobs.length) {
				facts.append(this.conflictButton(info.warnings, crewId ?? "", d.day, month));
				cell.classList.add("eoCkConflict");
			}
			if (info.rosterChanged) {
				const r = el("span", "eoCkRoster");
				r.append(icon("sap-icon://switch-classes"));
				r.title = `Tagesbesetzung: ${info.members.map((m) => m.name).join(", ") || "niemand"}${info.vehicles.length ? ` · ${info.vehicles.map((v) => v.name).join(", ")}` : ""}${info.rosterNote ? ` · ${info.rosterNote}` : ""}`;
				r.setAttribute("aria-label", r.title);
				facts.append(r);
			}
			if (absent.length && (d.isWorkday || jobs.length)) {
				const chip = el("span", `eoCkAbsent${month ? " eoCkAbsentMini" : ""}`);
				chip.append(icon("sap-icon://employee-rejections"));
				if (!month) chip.append(el("span", "eoCkAbsentText", absent.map((m) => `${firstName(m.name)}: ${m.absenceNote}`).join(" · ")));
				chip.title = absent.map((m) => `${m.name}: ${m.absenceNote}`).join("\n");
				chip.setAttribute("aria-label", `Abwesend: ${chip.title}`);
				facts.append(chip);
			}
			if (facts.childElementCount) cell.append(facts);
		}
		for (const a of jobs) cell.append(this.card(data, a));
		if (data.canPlan) {
			const add = el("button", `eoCkAdd${month ? " eoCkAddMini" : ""}`);
			add.type = "button";
			add.append(icon("sap-icon://add"));
			if (!month) add.append(el("span", "eoCkAddText", "Einsatz"));
			add.setAttribute("aria-label", `Einsatz planen: ${label}`);
			add.title = "Einsatz planen (oder Doppelklick in die Zelle)";
			add.addEventListener("click", () => this.fireEvent("add", { crewId: crewId ?? "", day: d.day }));
			cell.append(add);
		}
		cell.addEventListener("contextmenu", (e) => {
			if ((e.target as HTMLElement).closest(".eoCkCard")) return;
			e.preventDefault();
			this.fireEvent("menu", { assignmentId: "", crewId: crewId ?? "", day: d.day, x: e.clientX, y: e.clientY });
		});
		cell.addEventListener("dblclick", (e) => {
			if (!(e.target as HTMLElement).closest(".eoCkCard, button") && data.canPlan) this.fireEvent("add", { crewId: crewId ?? "", day: d.day });
		});
		return cell;
	}

	private card(data: CockpitRender, a: PlanAssignment, timeline?: { left: number; width: number }): HTMLElement {
		const compact = !timeline && this.compact(data);
		const kind = assignmentKinds[a.kind];
		const card = el("div", `eoCkCard${compact ? " eoCkCompact" : ""}${a.warnings.length ? " eoCkWarn" : ""}${a.change ? ` eoCkChanged-${a.change}` : ""}`);
		card.dataset.id = a.id;
		card.dataset.flipKey = `ck:${a.id}`;
		card.tabIndex = 0;
		card.setAttribute("role", "button");
		card.setAttribute("aria-haspopup", "dialog");
		const selected = data.selected.includes(a.id);
		if (selected) card.classList.add("eoCkSelected");
		if (data.dimmed.includes(a.id)) card.classList.add("eoCkDim");
		card.setAttribute("aria-pressed", String(selected));
		const time = a.startTime ? `${hhmm(a.startTime)}${a.endTime ? `–${hhmm(a.endTime)}` : ""}` : "ganztags";
		const title = `${a.bauakteNumber ? `${a.bauakteNumber} · ` : ""}${a.bauakteTitle}`;
		const city = a.bauakteAddress?.split(",").pop()?.trim().replace(/^\d{5}\s*/, "") ?? "";
		card.setAttribute("aria-label", `${a.activity}, ${title}, ${time}${a.warnings.length ? `, ${a.warnings.length} Hinweise` : ""}${a.change ? ", geändert seit Freigabe" : ""}`);
		card.title = [title, `${a.activity} · ${time}`, a.bauakteAddress ?? "", a.people.length ? `Team: ${a.people.map((p) => p.name).join(", ")}` : "",
			a.resources.length ? `Geräte: ${a.resources.map((r) => r.name).join(", ")}` : "", ...a.warnings.map((w) => `⚠ ${w}`),
			a.change ? (a.change === "new" ? "Neu seit Freigabe" : "Geändert seit Freigabe") : "", "Klick: Details · Doppelklick: bearbeiten"].filter(Boolean).join("\n");
		if (safeColor(kind?.color)) card.style.setProperty("--eo-kind", safeColor(kind?.color));
		if (timeline) {
			card.style.left = `${timeline.left}%`;
			card.style.width = `${timeline.width}%`;
		}
		const marks = el("span", "eoCkMarks");
		if (a.change) {
			const dot = el("span", `eoCkChange eoCkChange-${a.change}`);
			dot.title = a.change === "new" ? "Neu seit Freigabe" : "Geändert seit Freigabe";
			marks.append(dot);
		}
		if (a.warnings.length) {
			const w = icon("sap-icon://alert", "eoCkIcon eoCkWarnIcon");
			w.title = a.warnings.join("\n");
			marks.append(w);
		}
		if (compact) {
			const line = el("div", "eoCkCardLine");
			if (data.mode !== "month" && a.startTime) line.append(el("span", "eoCkTime", hhmm(a.startTime)));
			line.append(el("span", "eoCkTitle", a.bauakteTitle));
			if (marks.childElementCount) line.append(marks);
			card.append(line);
		} else {
			const top = el("div", "eoCkCardTop");
			if (a.sequence) {
				const seq = el("span", "eoCkSeq", String(a.sequence));
				seq.title = `${a.sequence}. Station der Fahrtreihenfolge`;
				top.append(seq);
			}
			if (kind) {
				const k = icon(kind.icon, "eoCkIcon eoCkKindIcon");
				k.title = kind.text;
				top.append(k);
			}
			const t = el("span", "eoCkTime", data.mode === "twoWeeks" && !timeline && a.startTime ? hhmm(a.startTime) : time);
			t.title = time;
			top.append(t);
			if (marks.childElementCount) top.append(marks);
			card.append(top, el("span", "eoCkTitle", a.bauakteTitle));
			card.append(el("span", "eoCkSub", [a.activity, city].filter(Boolean).join(" · ")));
		}
		if (data.canPlan) {
			card.classList.add("eoCkMovable");
			if (data.mode === "week" || data.mode === "twoWeeks" || timeline) {
				const grip = el("span", "eoCkGrip");
				grip.dataset.nodrag = "1";
				grip.title = timeline ? "Ende ziehen: Dauer ändern" : "Kante ziehen: auf weitere Tage ausdehnen";
				grip.addEventListener("pointerdown", (e) => (timeline ? this.resizeTime(e, card, a, data) : this.extendDays(e, card, a)));
				card.append(grip);
			}
			card.addEventListener("pointerdown", (e) => {
				if ((e.target as HTMLElement).closest(".eoCkGrip")) return;
				const rect = card.getBoundingClientRect();
				const current = this.dataValue();
				const count = current?.selected.includes(a.id) ? current.selected.length : 1;
				startCockpitDrag(e, card, { kind: "assignment", id: a.id, grabOffset: e.clientX - rect.left }, count);
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
		// Außerhalb der Kernarbeitszeit dezent schattiert.
		wrap.style.setProperty("--eo-work-from", `${((WORK[0] - from) / (to - from)) * 100}%`);
		wrap.style.setProperty("--eo-work-to", `${((WORK[1] - from) / (to - from)) * 100}%`);
		const head = el("div", "eoCkTRow eoCkHead");
		const corner = el("div", "eoCkLane eoCkCorner");
		corner.append(el("span", "eoCkCornerTitle", longDay(day.day)), el("span", "eoCkCornerSub", day.holiday ? `Feiertag: ${day.holiday}` : day.release
			? (day.release.changedCount ? `Freigegeben · ${day.release.changedCount} geändert` : "Freigegeben") : "Entwurf – noch nicht freigegeben"));
		head.append(corner);
		const scale = el("div", "eoCkScale");
		for (let h = from; h < to; h++) scale.append(el("span", `eoCkHour${h < WORK[0] || h >= WORK[1] ? " eoCkHourOff" : ""}`, `${String(h).padStart(2, "0")}:00`));
		head.append(scale);
		wrap.append(head);
		let nowPercent = -1;
		if (day.day === data.today) {
			const now = new Date();
			const m = now.getHours() * 60 + now.getMinutes();
			if (m > from * 60 && m < to * 60) {
				nowPercent = ((m - from * 60) / span) * 100;
				wrap.style.setProperty("--eo-now", `${nowPercent}%`);
				wrap.classList.add("eoCkHasNow");
				const tag = el("span", "eoCkNowTag", `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`);
				tag.style.left = `${nowPercent}%`;
				scale.append(tag);
			}
		}
		const lanes: (CockpitCrew | null)[] = [...data.crews, ...(data.noCrewLane ? [null] : [])];
		for (const crew of lanes) {
			const row = el("div", "eoCkTRow");
			row.append(this.crewHead(crew, data));
			const track = el("div", "eoCkTrack");
			track.dataset.drop = "1";
			track.dataset.crew = crew?.id ?? NO_CREW;
			track.dataset.day = day.day;
			track.dataset.dropLabel = crew?.name ?? "Ohne Kolonne";
			track.setAttribute("aria-label", `${crew?.name ?? "Ohne Kolonne"}, Zeitachse`);
			const info = crew?.cells[0];
			const jobs = this.inCell(data, crew?.id ?? null, day.day);
			const absent = info?.members.filter((m) => m.absenceNote) ?? [];
			if (absent.length || (info?.warnings.length && jobs.length) || info?.rosterChanged) {
				// Hinweise des Tages im (stehenden) Kolonnenkopf, damit sie beim waagrechten Scrollen sichtbar bleiben.
				const facts = el("div", "eoCkTFacts");
				for (const m of absent) {
					const chip = el("span", "eoCkAbsent");
					chip.append(icon("sap-icon://employee-rejections"), el("span", "eoCkAbsentText", `${m.name}: ${m.absenceNote}`));
					facts.append(chip);
				}
				if (info?.rosterChanged) {
					const r = el("span", "eoCkRoster");
					r.append(icon("sap-icon://switch-classes"), el("span", "eoCkAbsentText", "Tagesbesetzung"));
					r.title = `Tagesbesetzung: ${info.members.map((m) => m.name).join(", ") || "niemand"}${info.rosterNote ? ` · ${info.rosterNote}` : ""}`;
					facts.append(r);
				}
				if (info?.warnings.length && jobs.length) facts.append(this.conflictButton(info.warnings, crew?.id ?? "", day.day));
				row.querySelector(".eoCkLaneText")?.append(facts);
			}
			// Ganztägige Einsätze ohne Uhrzeit: über die ganze Breite, zeitliche in Spuren ohne Überlappung.
			const lanesEnd: number[] = [];
			for (const a of jobs) {
				const start = a.startTime ? ofTime(a.startTime) : from * 60;
				const end = a.startTime ? start + minutes(a.startTime, a.endTime) : to * 60;
				let lane = lanesEnd.findIndex((e) => e <= start);
				if (lane < 0) { lane = lanesEnd.length; lanesEnd.push(end); } else lanesEnd[lane] = end;
				const left = Math.max(0, ((start - from * 60) / span) * 100);
				const width = Math.max(4, Math.min(100 - left, ((end - start) / span) * 100));
				const card = this.card(data, a, { left, width });
				card.style.top = `${0.5 + lane * 4.375}rem`;
				track.append(card);
			}
			track.style.minHeight = `${Math.max(1, lanesEnd.length) * 4.375 + 0.625}rem`;
			track.addEventListener("contextmenu", (e) => {
				if ((e.target as HTMLElement).closest(".eoCkCard")) return;
				e.preventDefault();
				this.fireEvent("menu", { assignmentId: "", crewId: crew?.id ?? "", day: day.day, x: e.clientX, y: e.clientY });
			});
			track.addEventListener("dblclick", (e) => {
				if (!(e.target as HTMLElement).closest(".eoCkCard, button") && data.canPlan) this.fireEvent("add", { crewId: crew?.id ?? "", day: day.day });
			});
			row.append(track);
			wrap.append(row);
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
		card.classList.add("eoCkResizingCard");
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
			card.classList.remove("eoCkResizingCard");
			this.fireEvent("retime", { id: a.id, start: toTime(start), end: toTime(end) });
		};
		window.addEventListener("pointermove", move, true);
		window.addEventListener("pointerup", up, true);
	}

	/** Startzeit beim Ablegen auf einer Zeitachse (Griffpunkt berücksichtigt). */
	private dropStart(payload: CockpitPayload, track: HTMLElement, x: number): number {
		const offset = payload.kind === "assignment" ? payload.grabOffset : 0;
		return this.timeAt(track, x - offset);
	}

	/** Vorschau beim Ziehen: in der Zeitachse ein Schatten an der künftigen Lage; liefert ggf. die Uhrzeit für den Geist. */
	preview(payload: CockpitPayload, target: HTMLElement, event: PointerEvent): string {
		if (!target.classList.contains("eoCkTrack")) {
			this.clearPreview();
			return "";
		}
		const data = this.dataValue();
		if (!data) return "";
		const [from, to] = data.hours;
		const span = (to - from) * 60;
		const start = this.dropStart(payload, target, event.clientX);
		const a = payload.kind === "assignment" ? data.assignments.find((x) => x.id === payload.id) : undefined;
		const length = a ? minutes(a.startTime, a.endTime) : 480;
		const end = Math.min(to * 60, start + length);
		let mark = target.querySelector<HTMLElement>(":scope > .eoCkDropMark");
		if (!mark) {
			this.clearPreview();
			mark = el("div", "eoCkDropMark");
			target.append(mark);
		}
		mark.style.left = `${((start - from * 60) / span) * 100}%`;
		mark.style.width = `${Math.max(2, ((end - start) / span) * 100)}%`;
		mark.textContent = `${hhmm(toTime(start))}–${hhmm(toTime(end))}`;
		return `${hhmm(toTime(start))} Uhr`;
	}

	clearPreview(): void { for (const m of this.scroll?.querySelectorAll(".eoCkDropMark") ?? []) m.remove(); }

	/** Ablegen auf Zelle bzw. Zeitachse (aufgerufen vom Zieh-Kern). */
	handleDrop(payload: CockpitPayload, target: HTMLElement, event: PointerEvent): void {
		this.clearPreview();
		const crew = target.dataset.crew ?? NO_CREW;
		const start = target.classList.contains("eoCkTrack") ? toTime(this.dropStart(payload, target, event.clientX)) : "";
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
