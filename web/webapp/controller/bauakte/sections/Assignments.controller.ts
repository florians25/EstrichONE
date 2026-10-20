import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import EventBus from "sap/ui/core/EventBus";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { akte } from "../../../util/akteStore";
import { draftOf, newDraft, openAssignment, planningLookups } from "../../../util/assignmentDialog";
import { isoDay } from "../../../util/days";
import { timeRange } from "../../../util/format";
import { assignmentKinds } from "../../../util/labels";
import type { AssignmentView } from "../../../util/models";

/** Einsätze der Bauakte (vergangene und geplante) mit Planen direkt aus der Akte. @namespace estrichone.controller.bauakte.sections */
export default class Assignments extends SectionBase {
	protected watches = ["planning"];

	protected init(): void {
		this.v().setModel(new JSONModel({ items: [] }), "as");
		EventBus.getInstance().subscribe("estrichone", "planningChanged", () => {
			if (this.bauakteId) akte.invalidate(this.bauakteId, "planning");
		});
	}

	protected async load(): Promise<void> {
		const range = await akte.planning(this.bauakteId);
		const today = isoDay(new Date());
		(this.v().getModel("as") as JSONModel).setProperty("/items", [...range.assignments]
			.sort((a, b) => (a.day + (a.startTime ?? "")).localeCompare(b.day + (b.startTime ?? "")))
			.map((a) => ({
				...a, group: a.day >= today ? "Geplant" : "Erledigt / vergangen", timeText: timeRange(a.startTime, a.endTime),
				peopleText: a.people.map((p) => p.name).join(", "), resourceText: a.resources.map((r) => r.name).join(", ") || "–",
				warningText: a.warnings.join(" ") || "–", color: assignmentKinds[a.kind]?.color,
			})));
	}

	onPlan(): void {
		const b = this.bauakte();
		void openAssignment(newDraft(isoDay(new Date()), null, { id: b.id, label: `${b.number ?? "neu"} · ${b.title}` }));
	}

	async onOpen(event: Event): Promise<void> {
		const a = (event.getSource() as Control).getBindingContext("as")?.getObject() as AssignmentView;
		const { crews } = await planningLookups();
		void openAssignment(draftOf(a, crews));
	}
}
