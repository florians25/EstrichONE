import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";
import { cellBackground, dayFraction } from "../util/absences";

export interface VacationGridEntry {
	from: string;
	to: string;
	kind: string;
	status: string;
	note: string | null;
	typeId?: string | null;
	halfDayStart?: boolean;
	halfDayEnd?: boolean;
	closureId?: string | null;
}

export interface VacationGridRow {
	userId: string;
	employeeId: string | null;
	name: string;
	sub: string;
	remaining: number | null;
	/** Gruppe (Kolonne): bei Wechsel eine Zwischenzeile. */
	group?: string;
	entries: VacationGridEntry[];
}

export interface VacationGridType { code: string; name: string; color: string }

export interface VacationGridData {
	days: string[];
	holidays: Record<string, string>;
	today: string;
	rows: VacationGridRow[];
	/** Abwesenheitsarten (Kürzel, Name, Farbe) nach ID. */
	types?: Record<string, VacationGridType>;
	/** Jahresansicht: schmale Spalten ohne Kürzel, Monatsnamen im Kopf. */
	compact?: boolean;
	/** Betriebsferien je Tag (Name). */
	closures?: Record<string, string>;
}

export const vacationKinds: Record<string, { text: string; code: string }> = {
	vacation: { text: "Urlaub", code: "U" }, sick: { text: "Krank", code: "K" }, training: { text: "Fortbildung", code: "F" },
	school: { text: "Berufsschule", code: "B" }, specialLeave: { text: "Sonderurlaub", code: "SU" }, other: { text: "Abwesend", code: "A" },
};

function el<K extends keyof HTMLElementTagNameMap>(tag: K, cls?: string, text?: string): HTMLElementTagNameMap[K] {
	const e = document.createElement(tag);
	if (cls) e.className = cls;
	if (text !== undefined) e.textContent = text;
	return e;
}

/**
 * Teamkalender: Zeilen = Mitarbeiter (nach Kolonne gruppiert), Spalten = Tage; Farben und Kürzel je Abwesenheitsart,
 * beantragte Tage schraffiert, halbe Tage diagonal, Wochenenden, Feiertage und Betriebsferien markiert.
 * Klick auf eine Zelle meldet Mitarbeiter und Tag.
 * @namespace estrichone.control
 */
export default class VacationGrid extends Control {
	static readonly metadata: MetadataOptions = {
		properties: { data: { type: "object" } },
		events: { cellPress: { parameters: { userId: { type: "string" }, day: { type: "string" } } } },
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: VacationGrid): void {
			rm.openStart("div", control);
			rm.class("eoVacationGrid");
			rm.openEnd();
			rm.close("div");
		},
	};

	onAfterRendering(): void {
		const host = this.getDomRef() as HTMLElement | null;
		const data = this.getProperty("data") as VacationGridData | null;
		if (!host || !data) return;
		const compact = !!data.compact;
		const table = el("table", `eoVacTable${compact ? " eoVacCompact" : ""}`);
		table.setAttribute("aria-label", "Teamkalender");
		const thead = el("thead");
		if (compact) {
			// Monatskopf über den Tagen.
			const months = el("tr");
			const blank = el("th", "eoVacName", "");
			months.append(blank);
			let i = 0;
			while (i < data.days.length) {
				const month = data.days[i].slice(0, 7);
				let n = 0;
				while (i + n < data.days.length && data.days[i + n].startsWith(month)) n++;
				const th = el("th", "eoVacMonth", new Date(`${month}-01T00:00:00`).toLocaleDateString("de-DE", { month: "short" }));
				th.colSpan = n;
				th.scope = "colgroup";
				months.append(th);
				i += n;
			}
			months.append(el("th", "eoVacRest", ""));
			thead.append(months);
		}
		const head = el("tr");
		const corner = el("th", "eoVacName", "Mitarbeiter");
		corner.scope = "col";
		head.append(corner);
		for (const d of data.days) {
			const date = new Date(`${d}T00:00:00`);
			const weekend = date.getDay() === 0 || date.getDay() === 6;
			const th = el("th", `eoVacDay${weekend ? " eoVacWeekend" : ""}${data.holidays[d] ? " eoVacHoliday" : ""}${d === data.today ? " eoVacToday" : ""}${date.getDate() === 1 ? " eoVacFirst" : ""}`);
			th.scope = "col";
			th.title = [data.holidays[d], data.closures?.[d], date.toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long" })].filter(Boolean).join(" · ");
			if (!compact) th.append(el("span", undefined, String(date.getDate())), el("small", undefined, date.toLocaleDateString("de-DE", { weekday: "short" }).slice(0, 2)));
			head.append(th);
		}
		const rest = el("th", "eoVacRest", "Rest");
		rest.scope = "col";
		head.append(rest);
		thead.append(head);
		const body = el("tbody");
		let group: string | undefined;
		for (const row of data.rows) {
			if (row.group !== undefined && row.group !== group) {
				group = row.group;
				const gr = el("tr", "eoVacGroup");
				const gh = el("th", undefined, group);
				gh.colSpan = data.days.length + 2;
				gh.scope = "colgroup";
				gr.append(gh);
				body.append(gr);
			}
			const tr = el("tr");
			const name = el("th", "eoVacName");
			name.scope = "row";
			name.append(el("strong", undefined, row.name), el("small", undefined, row.sub));
			tr.append(name);
			for (const d of data.days) {
				const date = new Date(`${d}T00:00:00`);
				const weekend = date.getDay() === 0 || date.getDay() === 6;
				const free = weekend || !!data.holidays[d];
				// Genehmigte Einträge vor beantragten anzeigen.
				const entry = row.entries.filter((e) => e.from <= d && d <= e.to).sort((a, b) => (a.status === "approved" ? -1 : 1) - (b.status === "approved" ? -1 : 1))[0];
				const td = el("td", `eoVacCell${weekend ? " eoVacWeekend" : ""}${data.holidays[d] ? " eoVacHoliday" : ""}${d === data.today ? " eoVacToday" : ""}${date.getDate() === 1 ? " eoVacFirst" : ""}`);
				if (entry) {
					const type = entry.typeId ? data.types?.[entry.typeId] : undefined;
					const kind = vacationKinds[entry.kind] ?? vacationKinds.other;
					const half = dayFraction(entry, d) < 1;
					const pending = entry.status === "pending";
					td.classList.add(`eoVac-${entry.kind}`);
					if (pending) td.classList.add("eoVacPending");
					if (half) td.classList.add("eoVacHalf");
					if (type && !free) td.style.background = cellBackground(type.color, pending, half);
					if (!compact) td.textContent = free ? "" : half ? "½" : type?.code ?? kind.code;
					td.title = `${row.name}: ${type?.name ?? kind.text}${half ? " (halber Tag)" : ""}${pending ? " (beantragt)" : ""}${entry.note ? `, ${entry.note}` : ""}`;
				} else if (data.holidays[d]) {
					td.title = data.holidays[d];
				}
				td.addEventListener("click", () => this.fireEvent("cellPress", { userId: row.userId, day: d }));
				tr.append(td);
			}
			const r = el("td", "eoVacRest", row.remaining == null ? "–" : row.remaining.toLocaleString("de-DE", { maximumFractionDigits: 1 }));
			if (row.remaining != null && row.remaining < 0) r.classList.add("eoVacNegative");
			tr.append(r);
			body.append(tr);
		}
		table.append(thead, body);
		host.replaceChildren(table);
		if (data.rows.length === 0) host.append(el("p", "eoMuted", "Keine Mitarbeiter mit Benutzerkonto im Personalstamm."));
	}
}
