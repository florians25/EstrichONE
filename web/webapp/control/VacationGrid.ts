import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";

export interface VacationGridRow {
	userId: string;
	employeeId: string | null;
	name: string;
	sub: string;
	remaining: number | null;
	/** Gruppe (Kolonne): bei Wechsel eine Zwischenzeile. */
	group?: string;
	entries: { from: string; to: string; kind: string; status: string; note: string | null }[];
}

export interface VacationGridData {
	days: string[];
	holidays: Record<string, string>;
	today: string;
	rows: VacationGridRow[];
}

export const vacationKinds: Record<string, { text: string; code: string }> = {
	vacation: { text: "Urlaub", code: "U" }, sick: { text: "Krank", code: "K" }, training: { text: "Schulung", code: "S" },
	school: { text: "Berufsschule", code: "B" }, specialLeave: { text: "Sonderurlaub", code: "SU" }, other: { text: "Abwesend", code: "A" },
};

function el<K extends keyof HTMLElementTagNameMap>(tag: K, cls?: string, text?: string): HTMLElementTagNameMap[K] {
	const e = document.createElement(tag);
	if (cls) e.className = cls;
	if (text !== undefined) e.textContent = text;
	return e;
}

/**
 * Urlaubsplan: Zeilen = Mitarbeiter, Spalten = Tage; Urlaub, Krank, Schule, Sonderurlaub farbig (Theme-Farben),
 * beantragte Tage schraffiert, Wochenenden und Feiertage markiert. Klick auf eine Zelle meldet Mitarbeiter und Tag.
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
		const table = el("table", "eoVacTable");
		table.setAttribute("aria-label", "Urlaubsplan");
		const head = el("tr");
		const corner = el("th", "eoVacName", "Mitarbeiter");
		corner.scope = "col";
		head.append(corner);
		for (const d of data.days) {
			const date = new Date(`${d}T00:00:00`);
			const weekend = date.getDay() === 0 || date.getDay() === 6;
			const th = el("th", `eoVacDay${weekend ? " eoVacWeekend" : ""}${data.holidays[d] ? " eoVacHoliday" : ""}${d === data.today ? " eoVacToday" : ""}`);
			th.scope = "col";
			th.title = data.holidays[d] ?? date.toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long" });
			th.append(el("span", undefined, String(date.getDate())), el("small", undefined, date.toLocaleDateString("de-DE", { weekday: "short" }).slice(0, 2)));
			head.append(th);
		}
		const rest = el("th", "eoVacRest", "Rest");
		rest.scope = "col";
		head.append(rest);
		const thead = el("thead");
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
				const entry = row.entries.find((e) => e.from <= d && d <= e.to);
				const td = el("td", `eoVacCell${weekend ? " eoVacWeekend" : ""}${data.holidays[d] ? " eoVacHoliday" : ""}${d === data.today ? " eoVacToday" : ""}`);
				if (entry) {
					const kind = vacationKinds[entry.kind] ?? vacationKinds.other;
					td.classList.add(`eoVac-${entry.kind}`);
					if (entry.status === "pending") td.classList.add("eoVacPending");
					td.textContent = weekend || data.holidays[d] ? "" : kind.code;
					td.title = `${row.name}: ${kind.text}${entry.status === "pending" ? " (beantragt)" : ""}${entry.note ? ` – ${entry.note}` : ""}`;
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
