import BaseController from "../BaseController";
import EventBus from "sap/ui/core/EventBus";
import { get } from "../../util/api";
import { download } from "../../util/planningModels";
import { addDays, isoDay } from "../../util/days";
import { longDay, timeRange } from "../../util/format";
import { absenceKinds, assignmentKinds } from "../../util/labels";
import { planningLookups } from "../../util/assignmentDialog";
import type { AssignmentView, PlanningRange } from "../../util/models";

const route = (a: AssignmentView) => (a.bauakteAddress ? `https://www.openstreetmap.org/search?query=${encodeURIComponent(a.bauakteAddress)}` : null);

/** Planung › Arbeitsplan (Agenda): eigene Einsätze oder alle Kolonnen, mit Navigation und Druck. @namespace estrichone.controller.planning */
export default class WorkPlan extends BaseController {
	private data: PlanningRange | null = null;

	private changed(): void { void this.load(); }

	onInit(): void {
		this.local({ length: "7", mine: false, crew: "", crewOptions: [{ key: "", text: "Alle Kolonnen" }], days: [], busy: false });
		EventBus.getInstance().subscribe("estrichone", "planningChanged", this.changed, this);
	}

	onExit(): void {
		EventBus.getInstance().unsubscribe("estrichone", "planningChanged", this.changed, this);
	}

	onRouteMatched(): void { void this.load(); }
	onRefresh(): void { void this.load(); }
	onPrint(): void { window.print(); }

	/** Arbeitsplan als PDF vom Server (gleicher Zeitraum und Filter wie die Ansicht). */
	onPdf(): void {
		const m = this.model();
		const query = new URLSearchParams({ from: isoDay(new Date()), days: String(m.getProperty("/length")) });
		if (m.getProperty("/crew")) query.set("crewId", String(m.getProperty("/crew")));
		if (m.getProperty("/mine") === true) query.set("mine", "true");
		download(`/api/v1/planning/work-plan.pdf?${query.toString()}`).catch((error: unknown) => this.showError(error));
	}
	onFilter(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const today = isoDay(new Date());
		const length = Number(m.getProperty("/length"));
		const to = addDays(today, length - 1);
		const mine = m.getProperty("/mine") === true;
		const [range, lookups] = await Promise.all([
			this.run(() => get<PlanningRange>(mine ? "planning/mine" : "planning/range", { from: today, to })),
			this.run(() => planningLookups()),
		]);
		this.data = range ?? null;
		m.setProperty("/crewOptions", [{ key: "", text: "Alle Kolonnen" }, ...(lookups?.crews ?? []).filter((c) => c.isActive).map((c) => ({ key: c.id, text: c.name })), { key: "none", text: "Ohne Kolonne" }]);
		const crew = String(m.getProperty("/crew") ?? "");
		const items = (this.data?.assignments ?? []).filter((a) => !crew || (crew === "none" ? !a.crewId : a.crewId === crew));
		const days = Array.from({ length }, (_, i) => addDays(today, i)).map((d) => {
			const own = items.filter((a) => a.day === d).sort((x, y) => (x.startTime ?? "99").localeCompare(y.startTime ?? "99"));
			return {
				day: d, title: longDay(d), today: d === today,
				countText: `${own.length} ${own.length === 1 ? "Einsatz" : "Einsätze"}`,
				absences: (this.data?.absences ?? []).filter((a) => a.from <= d && d <= a.to).map((a) => ({ text: `${a.userName} · ${absenceKinds[a.kind] ?? a.kind}` })),
				items: own.map((a) => ({
					...a,
					icon: assignmentKinds[a.kind]?.icon ?? "sap-icon://appointment-2",
					kindText: assignmentKinds[a.kind]?.text ?? a.kind,
					timeText: timeRange(a.startTime, a.endTime) || "ganztags",
					bauakteText: `${a.bauakteNumber ?? ""} ${a.bauakteTitle}`.trim(),
					line: [a.activity, a.bauakteAddress].filter(Boolean).join(" · "),
					team: [a.people.map((p) => p.name).join(", ") || "–", a.resources.map((r) => r.name).join(", ")].filter(Boolean).join(" · "),
					warningText: a.warnings.join("\n"),
					route: route(a),
				})),
			};
		});
		m.setProperty("/days", days);
		m.setProperty("/busy", false);
	}
}
