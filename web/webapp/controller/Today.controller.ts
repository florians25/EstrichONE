import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get, post } from "../util/api";
import { isoDay } from "../util/days";
import { longDay, quantity, timeRange } from "../util/format";
import type { BackupStatusResponse, BauaktePage, CallResponse, OrderResponse, PlanningRange, TicketSummary, WorkItemDto } from "../util/models";
import type { ApprovalResponse } from "../util/approvals";
import type { ArchiveOverview, MailSettingsDto } from "../util/infraModels";
import { dateTime } from "../util/format";
import { openOrderDetail } from "../util/orderDialogs";

const who = (c: CallResponse) => [c.callerName, c.company ?? c.customerName].filter(Boolean).join(" · ") || c.callerPhone || "–";

/**
 * Tagesübersicht: oben Kennzahlen, darunter kompakte Listen (Einsätze heute nach Kolonne, Wiedervorlagen, Freigaben,
 * Aufgaben, neue Tickets, Abwesende, Lieferungen, Anfragen), unten eine schmale Zeile mit dem System-Status.
 * @namespace estrichone.controller
 */
export default class Today extends BaseController {
	onInit(): void {
		this.local({ today: "", kpis: [], assignments: [], absences: [], deliveries: [], due: [], inquiries: [], work: [], recent: [], approvals: [], tickets: [], system: [] });
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
		const kpis = [
			this.has("planning.read") && { key: "assignments", title: "Einsätze heute", icon: "sap-icon://appointment-2", to: "/planung/cockpit?ansicht=day" },
			this.has("work.read") && { key: "work", title: "Offene Aufgaben / Mängel", icon: "sap-icon://task", to: "/tagesuebersicht" },
			this.has("bauakten.read") && { key: "approvals", title: "Freigaben für mich", icon: "sap-icon://approvals", to: "/dokumente?reiter=freigaben" },
			this.has("contacts.read") && { key: "due", title: "Wiedervorlagen fällig", icon: "sap-icon://call", to: "/anrufe" },
			this.has("contacts.read") && { key: "inquiries", title: "Offene Anfragen", icon: "sap-icon://inbox", to: "/anfragen" },
			this.has("helpdesk.agent") && { key: "tickets", title: "Neue Tickets", icon: "sap-icon://headset", to: "/it-center/helpdesk" },
		].filter(Boolean).map((k) => ({ ...(k as object), value: "…", color: "Neutral", loading: true })) as { key: string; value: string; color: string; loading: boolean }[];
		m.setProperty("/kpis", kpis);
		const set = (key: string, count: number, critical = false) => {
			const i = kpis.findIndex((k) => k.key === key);
			if (i >= 0) m.setProperty(`/kpis/${i}`, { ...kpis[i], value: String(count), loading: false, color: critical && count > 0 ? "Critical" : "Neutral" });
		};
		const [planning, due, inquiries, work, deliveries, recent, approvals, tickets, backup, archive, mail] = await Promise.allSettled([
			this.has("planning.read") ? get<PlanningRange>("planning/range", { from: today, to: today }) : Promise.resolve(null),
			this.has("contacts.read") ? get<CallResponse[]>("calls", { due: true }) : Promise.resolve([]),
			this.has("contacts.read") ? get<CallResponse[]>("calls", { kind: "inquiry", open: true, limit: 8 }) : Promise.resolve([]),
			this.has("work.read") ? get<WorkItemDto[]>("work-items/mine") : Promise.resolve([]),
			this.has("materials.read") ? get<OrderResponse[]>("material-orders", { from: today, to: today }) : Promise.resolve([]),
			get<BauaktePage>("bauakten", { limit: 8 }),
			this.has("bauakten.read") ? get<ApprovalResponse[]>("approvals", { scope: "mine", status: "pending" }) : Promise.resolve([]),
			this.has("helpdesk.agent") ? get<TicketSummary[]>("tickets", { filter: "open" }) : Promise.resolve([]),
			this.has("backup.manage") ? get<BackupStatusResponse>("backup/status") : Promise.resolve(null),
			this.has("settings.manage") ? get<ArchiveOverview>("archive/status") : Promise.resolve(null),
			this.has("settings.manage") ? get<MailSettingsDto>("mail/settings") : Promise.resolve(null),
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
		const myApprovals = value(approvals, []);
		m.setProperty("/approvals", myApprovals.slice(0, 6));
		const newTickets = value(tickets, []).filter((t) => t.status === "new" || t.reporterBlocked);
		m.setProperty("/tickets", newTickets.slice(0, 6));
		// Leere Bereiche nicht als eigene Kästen zeigen, sondern in einer Zeile zusammenfassen.
		const empty = [
			this.has("contacts.read") && !(m.getProperty("/due") as unknown[]).length && "Wiedervorlagen",
			this.has("bauakten.read") && !myApprovals.length && "Freigaben",
			this.has("work.read") && !(m.getProperty("/work") as unknown[]).length && "Aufgaben und Mängel",
			this.has("helpdesk.agent") && !newTickets.length && "Tickets",
			this.has("contacts.read") && !(m.getProperty("/inquiries") as unknown[]).length && "Anfragen",
		].filter(Boolean);
		m.setProperty("/emptyText", empty.length ? `Nichts offen: ${empty.join(", ")}.` : "");
		set("assignments", assignments.length);
		set("work", m.getProperty("/work").length, true);
		set("approvals", myApprovals.length, true);
		set("due", value(due, []).length, true);
		set("inquiries", value(inquiries, []).length);
		set("tickets", newTickets.length, true);

		// System-Status (schmale Zeile unten).
		const system: { label: string; text: string; state: string; icon: string; detail: string; to: string }[] = [];
		const b = value(backup, null);
		if (b) system.push({ label: "Sicherung", icon: "sap-icon://upload-to-cloud", to: "/system/sicherung",
			...(b.lastSuccess ? { text: b.healthy ? "aktuell" : "veraltet", state: b.healthy ? "Success" : "Warning", detail: `zuletzt ${dateTime(b.lastSuccess.at)}` }
				: { text: "noch keine", state: "Error", detail: "" }) });
		const a = value(archive, null);
		if (a) system.push({ label: "Archiv", icon: "sap-icon://database", to: "/system/archiv",
			text: !a.connected ? "nicht verbunden" : a.counts.failed ? `${a.counts.failed} fehlgeschlagen` : "verbunden",
			state: !a.connected ? "None" : a.counts.failed ? "Error" : "Success", detail: a.connected ? `${a.counts.pending} wartend` : "" });
		const mail2 = value(mail, null);
		if (mail2) system.push({ label: "E-Mail", icon: "sap-icon://email", to: "/system/e-mail",
			text: !mail2.configured ? "nicht eingerichtet" : mail2.lastTestOk === false ? "Fehler beim Test" : mail2.enabled ? "aktiv" : "ausgeschaltet",
			state: !mail2.configured ? "None" : mail2.lastTestOk === false ? "Error" : mail2.enabled ? "Success" : "Warning", detail: "" });
		m.setProperty("/system", system);
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

	onApproval(event: Event): void {
		const a = this.obj<ApprovalResponse>(event);
		this.nav(`/bauakten/${a.bauakteId}/datei/${a.fileId}?abschnitt=dokumente`);
	}

	onTicket(event: Event): void {
		this.nav(`/it-center/helpdesk/${this.obj<TicketSummary>(event).id}`);
	}

	onToDay(): void { this.nav("/planung?ansicht=tag"); }
	onToOrders(): void { this.nav("/bestellungen"); }
	onToCalls(): void { this.nav("/anrufe"); }
	onToInquiries(): void { this.nav("/anfragen"); }
}
