import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get, post } from "../util/api";
import { isoDay } from "../util/days";
import { longDay, quantity, timeRange } from "../util/format";
import type { BauaktePage, CallResponse, OrderResponse, PlanningRange, WorkItemDto } from "../util/models";
import { openOrderDetail } from "../util/orderDialogs";

const who = (c: CallResponse) => [c.callerName, c.company ?? c.customerName].filter(Boolean).join(" · ") || c.callerPhone || "–";

/** Tagesübersicht: Einsätze je Kolonne, Abwesenheiten, Lieferungen, Wiedervorlagen, Anfragen, eigene Aufgaben. @namespace estrichone.controller */
export default class Today extends BaseController {
	onInit(): void {
		this.local({ today: "", kpis: [], assignments: [], absences: [], deliveries: [], due: [], inquiries: [], work: [], recent: [] });
	}

	onRouteMatched(): void {
		void this.load();
	}

	onRefresh(): void {
		void this.load();
	}

	private async load(): Promise<void> {
		const m = this.model();
		const today = isoDay(new Date());
		m.setProperty("/today", longDay(today));
		const [planning, due, inquiries, work, deliveries, recent] = await Promise.allSettled([
			this.has("planning.read") ? get<PlanningRange>("planning/range", { from: today, to: today }) : Promise.resolve(null),
			this.has("contacts.read") ? get<CallResponse[]>("calls", { due: true }) : Promise.resolve([]),
			this.has("contacts.read") ? get<CallResponse[]>("calls", { kind: "inquiry", open: true, limit: 8 }) : Promise.resolve([]),
			this.has("work.read") ? get<WorkItemDto[]>("work-items/mine") : Promise.resolve([]),
			this.has("materials.read") ? get<OrderResponse[]>("material-orders", { from: today, to: today }) : Promise.resolve([]),
			get<BauaktePage>("bauakten", { limit: 8 }),
		]);
		const value = <T>(r: PromiseSettledResult<T>, fallback: T) => (r.status === "fulfilled" ? r.value : fallback);
		const range = value(planning, null);
		const assignments = (range?.assignments ?? []).map((a) => ({
			...a, crewLabel: a.crewName ?? "Ohne Kolonne", timeText: timeRange(a.startTime, a.endTime) || "ganztags",
			peopleText: [a.people.map((p) => p.name).join(", "), a.resources.map((r) => r.name).join(", ")].filter(Boolean).join(" · "),
			warningText: a.warnings.length ? `${a.warnings.length} Hinweise` : "",
		}));
		m.setProperty("/assignments", assignments);
		m.setProperty("/absences", range?.absences ?? []);
		m.setProperty("/due", value(due, []).map((c) => ({ ...c, who: who(c) })));
		m.setProperty("/inquiries", value(inquiries, []).map((c) => ({ ...c, who: who(c) })));
		m.setProperty("/work", value(work, []).filter((w) => w.status !== "done").slice(0, 10));
		m.setProperty("/deliveries", value(deliveries, []).filter((o) => o.status !== "cancelled")
			.map((o) => ({ ...o, linesText: o.lines.map((l) => `${quantity(l.quantity)} ${l.unit} ${l.description}`).join(" · ") })));
		m.setProperty("/recent", value(recent, { items: [], nextCursor: null }).items);
		const warnings = assignments.filter((a) => a.warnings.length).length;
		m.setProperty("/kpis", [
			{ title: "Einsätze heute", value: String(assignments.length), icon: "sap-icon://appointment-2", color: "Neutral", to: "/planung/tageseinteilung" },
			{ title: "Abwesend", value: String(range?.absences.length ?? 0), icon: "sap-icon://general-leave-request", color: "Neutral", to: "/personal/urlaub?reiter=plan" },
			{ title: "Hinweise in der Planung", value: String(warnings), icon: "sap-icon://alert", color: warnings ? "Critical" : "Neutral", to: "/planung" },
			{ title: "Wiedervorlagen fällig", value: String(value(due, []).length), icon: "sap-icon://call", color: "Neutral", to: "/anrufe" },
			{ title: "Offene Anfragen", value: String(value(inquiries, []).length), icon: "sap-icon://inbox", color: "Neutral", to: "/anfragen" },
		]);
	}

	private obj<T>(event: Event): T {
		return (event.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	onKpi(event: Event): void {
		this.nav(this.obj<{ to: string }>(event).to);
	}

	onAssignment(event: Event): void {
		this.nav(`/bauakten/${this.obj<{ bauakteId: string }>(event).bauakteId}?abschnitt=einsaetze`);
	}

	onBauakte(event: Event): void {
		this.nav(`/bauakten/${this.obj<{ id: string }>(event).id}`);
	}

	onWork(event: Event): void {
		const w = this.obj<WorkItemDto>(event);
		if (w.bauakteId) this.nav(`/bauakten/${w.bauakteId}?abschnitt=aufgaben`);
	}

	onOrder(event: Event): void {
		void openOrderDetail(this.obj<OrderResponse>(event).id, this.has("materials.manage"));
	}

	async onDone(event: Event): Promise<void> {
		const c = this.obj<CallResponse>(event);
		await this.run(() => post(`calls/${c.id}/follow-up-done`), "Wiedervorlage erledigt");
		void this.load();
	}

	onToDay(): void { this.nav("/planung/tageseinteilung"); }
	onToOrders(): void { this.nav("/bestellungen"); }
	onToCalls(): void { this.nav("/anrufe"); }
	onToInquiries(): void { this.nav("/anfragen"); }
}
