import BaseController from "./BaseController";
import { session } from "../util/session";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get, post } from "../util/api";
import { addDays, isoDay } from "../util/days";
import { date, longDay, quantity, weekdayDate } from "../util/format";
import { absenceKinds } from "../util/labels";
import { groupByCrew, relativeDay } from "../util/today";
import type { RouteMatch } from "../util/router";
import type DatePicker from "sap/m/DatePicker";
import type { BackupStatusResponse, BauaktePage, CallResponse, OrderResponse, PlanningRange, TicketSummary, WorkItemDto } from "../util/models";
import type { ApprovalResponse } from "../util/approvals";
import type { ArchiveOverview, MailSettingsDto } from "../util/infraModels";
import { dateTime } from "../util/format";
import { openOrderDetail } from "../util/orderDialogs";

const who = (c: CallResponse) => [c.callerName, c.company ?? c.customerName].filter(Boolean).join(" · ") || c.callerPhone || "–";

/**
 * Tagesübersicht: Tagesnavigation (Vortag, Datum, Folgetag), oben Kennzahlen, links die Einsätze des Tages je Kolonne
 * als Zeitleiste und Abwesende, rechts die offenen Punkte (Stand jetzt: Wiedervorlagen, Freigaben, Aufgaben, Tickets,
 * Anfragen), Lieferungen des Tages, zuletzt geänderte Bauakten und der Systemstatus. Druckbar.
 * @namespace estrichone.controller
 */
export default class Today extends BaseController {
	private loadId = 0;
	private keys = (e: KeyboardEvent) => this.onKey(e);

	onInit(): void {
		this.local({ day: isoDay(new Date()), dayText: "", relText: "", isToday: true, busy: false, kpis: [], crews: [], absences: [], deliveries: [], due: [],
			inquiries: [], work: [], recent: [], approvals: [], tickets: [], system: [], assignTitle: "Einsätze", assignSubtitle: "", emptyAssignTitle: "",
			absenceSubtitle: "", deliverySubtitle: "", systemSubtitle: "", emptyText: "", printedAt: "" });
	}

	onExit(): void {
		document.removeEventListener("keydown", this.keys);
	}

	onRouteMatched(match: RouteMatch): void {
		const datum = match.query.get("datum");
		this.model().setProperty("/day", datum && /^\d{4}-\d{2}-\d{2}$/.test(datum) ? datum : isoDay(new Date()));
		document.removeEventListener("keydown", this.keys);
		document.addEventListener("keydown", this.keys);
		void this.load();
	}

	onRefresh(): void {
		void this.load();
	}

	private go(day: string): void {
		this.model().setProperty("/day", day);
		this.setQuery({ datum: day === isoDay(new Date()) ? null : day });
		void this.load();
	}

	onPrevDay(): void { this.go(addDays(this.model().getProperty("/day") as string, -1)); }
	onNextDay(): void { this.go(addDays(this.model().getProperty("/day") as string, 1)); }
	onToday(): void { this.go(isoDay(new Date())); }

	onPickDay(event: Event): void {
		const value = (event.getSource() as DatePicker).getValue();
		if (/^\d{4}-\d{2}-\d{2}$/.test(value)) this.go(value);
	}

	/** Alt+← / Alt+→ blättern tageweise (nicht in Eingabefeldern). */
	private onKey(e: KeyboardEvent): void {
		if (!e.altKey || e.ctrlKey || e.metaKey || !(this.v().getDomRef() as HTMLElement | null)?.offsetParent) return;
		if ((e.target as HTMLElement | null)?.closest("input, textarea")) return;
		if (e.key === "ArrowLeft") { e.preventDefault(); this.onPrevDay(); }
		if (e.key === "ArrowRight") { e.preventDefault(); this.onNextDay(); }
	}

	onPrint(): void {
		this.model().setProperty("/printedAt", dateTime(new Date().toISOString()));
		setTimeout(() => window.print(), 50);
	}

	onPlan(): void {
		this.nav(`/planung?ansicht=tag&datum=${this.model().getProperty("/day") as string}`);
	}

	private async load(): Promise<void> {
		const id = ++this.loadId;
		const m = this.model();
		const now = isoDay(new Date());
		const day = m.getProperty("/day") as string;
		const isToday = day === now;
		const short = date(day).slice(0, 6);
		m.setProperty("/isToday", isToday);
		m.setProperty("/dayText", longDay(day));
		m.setProperty("/relText", relativeDay(day, now));
		m.setProperty("/busy", true);
		// Einstellungen › Module: abgeschaltete Module erscheinen nicht (keine Kachel, keine Abfrage).
		const calls = this.has("contacts.read") && session.moduleOn("calls");
		const kpis = [
			this.has("planning.read") && { key: "assignments", title: isToday ? "Einsätze heute" : `Einsätze ${short}`, icon: "sap-icon://appointment-2", to: `/planung/cockpit?ansicht=day&datum=${day}` },
			this.has("work.read") && { key: "work", title: "Aufgaben / Mängel", icon: "sap-icon://task", to: "/bauakten" },
			this.has("bauakten.read") && session.moduleOn("approvals") && { key: "approvals", title: "Freigaben", icon: "sap-icon://approvals", to: "/dokumente?reiter=freigaben" },
			calls && { key: "due", title: "Wiedervorlagen", icon: "sap-icon://call", to: "/anrufe" },
			calls && { key: "inquiries", title: "Anfragen", icon: "sap-icon://inbox", to: "/anfragen" },
			this.has("helpdesk.agent") && { key: "tickets", title: "Tickets", icon: "sap-icon://headset", to: "/it-center/helpdesk" },
		].filter(Boolean).map((k) => ({ ...(k as object), value: "…", state: "None", hint: "\u00a0" })) as { key: string; value: string; state: string; hint: string }[];
		m.setProperty("/kpis", kpis);
		const set = (key: string, count: number, critical = false, hint = "") => {
			const i = kpis.findIndex((k) => k.key === key);
			if (i >= 0) m.setProperty(`/kpis/${i}`, { ...kpis[i], value: String(count), state: critical && count > 0 ? "Warning" : "None",
				hint: hint || (count ? "\u00a0" : "nichts offen") });
		};
		const [planning, due, inquiries, work, deliveries, recent, approvals, tickets, backup, archive, mail] = await Promise.allSettled([
			this.has("planning.read") ? get<PlanningRange>("planning/range", { from: day, to: day }) : Promise.resolve(null),
			calls ? get<CallResponse[]>("calls", { due: true }) : Promise.resolve([]),
			calls ? get<CallResponse[]>("calls", { kind: "inquiry", open: true, limit: 8 }) : Promise.resolve([]),
			this.has("work.read") ? get<WorkItemDto[]>("work-items/mine") : Promise.resolve([]),
			this.has("materials.read") && session.moduleOn("orders") ? get<OrderResponse[]>("material-orders", { from: day, to: day }) : Promise.resolve([]),
			get<BauaktePage>("bauakten", { limit: 8 }),
			this.has("bauakten.read") && session.moduleOn("approvals") ? get<ApprovalResponse[]>("approvals", { scope: "mine", status: "pending" }) : Promise.resolve([]),
			this.has("helpdesk.agent") ? get<TicketSummary[]>("tickets", { filter: "open" }) : Promise.resolve([]),
			this.has("backup.manage") ? get<BackupStatusResponse>("backup/status") : Promise.resolve(null),
			this.has("settings.manage") ? get<ArchiveOverview>("archive/status") : Promise.resolve(null),
			this.has("settings.manage") ? get<MailSettingsDto>("mail/settings") : Promise.resolve(null),
		]);
		if (id !== this.loadId) return;
		const value = <T>(r: PromiseSettledResult<T>, fallback: T) => (r.status === "fulfilled" ? r.value : fallback);
		const range = value(planning, null);
		const assignments = range?.assignments ?? [];
		const crews = groupByCrew(assignments);
		m.setProperty("/crews", crews);
		const warnings = assignments.filter((a) => a.warnings.length).length;
		m.setProperty("/assignTitle", isToday ? "Einsätze heute" : `Einsätze am ${weekdayDate(day)}`);
		m.setProperty("/assignSubtitle", assignments.length
			? `${assignments.length} ${assignments.length === 1 ? "Einsatz" : "Einsätze"} · ${crews.length} ${crews.length === 1 ? "Kolonne" : "Kolonnen"}`
			: "Nichts eingeplant");
		m.setProperty("/emptyAssignTitle", isToday ? "Heute ist nichts eingeplant." : `Am ${weekdayDate(day)} ist nichts eingeplant.`);
		const absences = (range?.absences ?? []).map((a) => ({ ...a, kindText: absenceKinds[a.kind] ?? a.kind, state: a.kind === "sick" ? "Error" : "Warning" }))
			.sort((a, b) => a.userName.localeCompare(b.userName, "de"));
		m.setProperty("/absences", absences);
		m.setProperty("/absenceSubtitle", isToday ? "Heute nicht verfügbar" : `Am ${weekdayDate(day)} nicht verfügbar`);
		m.setProperty("/due", value(due, []).map((c) => ({ ...c, who: who(c) })));
		m.setProperty("/inquiries", value(inquiries, []).map((c) => ({ ...c, who: who(c) })));
		m.setProperty("/work", value(work, []).filter((w) => w.status !== "done").slice(0, 10));
		const dayDeliveries = value(deliveries, []).filter((o) => o.status !== "cancelled")
			.map((o) => ({ ...o, linesText: o.lines.map((l) => `${quantity(l.quantity)} ${l.unit} ${l.description}`).join(" · ") }));
		m.setProperty("/deliveries", dayDeliveries);
		m.setProperty("/deliverySubtitle", isToday ? "Heute erwartet" : `Erwartet am ${weekdayDate(day)}`);
		m.setProperty("/recent", value(recent, { items: [], nextCursor: null }).items);
		const myApprovals = value(approvals, []);
		m.setProperty("/approvals", myApprovals.slice(0, 6));
		const newTickets = value(tickets, []).filter((t) => t.status === "new" || t.reporterBlocked);
		m.setProperty("/tickets", newTickets.slice(0, 6));
		// Leere Bereiche nicht als eigene Karten zeigen, sondern in einer Zeile zusammenfassen.
		const empty = [
			calls && !(m.getProperty("/due") as unknown[]).length && "Wiedervorlagen",
			this.has("bauakten.read") && session.moduleOn("approvals") && !myApprovals.length && "Freigaben",
			this.has("work.read") && !(m.getProperty("/work") as unknown[]).length && "Aufgaben und Mängel",
			this.has("helpdesk.agent") && !newTickets.length && "Tickets",
			calls && !(m.getProperty("/inquiries") as unknown[]).length && "Anfragen",
		].filter(Boolean);
		m.setProperty("/emptyText", empty.length ? `Nichts offen: ${empty.join(", ")}.` : "");
		set("assignments", assignments.length, false, warnings ? `${warnings} mit Hinweisen` : assignments.length ? `${crews.length} ${crews.length === 1 ? "Kolonne" : "Kolonnen"}` : "nichts eingeplant");
		const overdue = (m.getProperty("/work") as WorkItemDto[]).filter((w) => w.dueDate && w.dueDate < now).length;
		set("work", m.getProperty("/work").length, true, overdue ? `${overdue} überfällig` : m.getProperty("/work").length ? "offen" : "");
		set("approvals", myApprovals.length, true, myApprovals.length ? "warten auf Sie" : "");
		set("due", value(due, []).length, true, value(due, []).length ? "fällig" : "");
		set("inquiries", value(inquiries, []).length, false, value(inquiries, []).length ? "offen" : "");
		const blocked = newTickets.filter((t) => t.reporterBlocked).length;
		set("tickets", newTickets.length, true, blocked ? `${blocked} kann nicht arbeiten` : newTickets.length ? "neu" : "");

		// System-Status (eigene Karte rechts unten).
		const system: { label: string; text: string; state: string; icon: string; detail: string; to: string }[] = [];
		const b = value(backup, null);
		if (b) system.push({ label: "Sicherung", icon: "sap-icon://upload-to-cloud", to: "/system/sicherung",
			...(b.lastSuccess ? { text: b.healthy ? "aktuell" : "veraltet", state: b.healthy ? "Success" : "Warning", detail: `zuletzt ${dateTime(b.lastSuccess.at)}` }
				: { text: "noch keine", state: "Error", detail: "Noch keine erfolgreiche Sicherung" }) });
		const a = value(archive, null);
		if (a) system.push({ label: "Archiv", icon: "sap-icon://database", to: "/system/archiv",
			text: !a.connected ? "nicht verbunden" : a.counts.failed ? `${a.counts.failed} fehlgeschlagen` : "verbunden",
			state: !a.connected ? "None" : a.counts.failed ? "Error" : "Success", detail: a.connected ? `${a.counts.pending} wartend` : "Kein Archivserver gekoppelt" });
		const mail2 = value(mail, null);
		if (mail2) system.push({ label: "E-Mail", icon: "sap-icon://email", to: "/system/e-mail",
			text: !mail2.configured ? "nicht eingerichtet" : mail2.lastTestOk === false ? "Fehler beim Test" : mail2.enabled ? "aktiv" : "ausgeschaltet",
			state: !mail2.configured ? "None" : mail2.lastTestOk === false ? "Error" : mail2.enabled ? "Success" : "Warning",
			detail: !mail2.configured ? "Benachrichtigungen nur im Postfach" : "" });
		m.setProperty("/system", system);
		const problems = system.filter((s) => s.state === "Error" || s.state === "Warning").length;
		m.setProperty("/systemSubtitle", problems ? `${problems} ${problems === 1 ? "Punkt braucht" : "Punkte brauchen"} Aufmerksamkeit` : "Alles in Ordnung");
		m.setProperty("/busy", false);
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

	onSystem(event: Event): void {
		this.nav(this.obj<{ to: string }>(event).to);
	}

	onToOrders(): void { this.nav("/bestellungen"); }
	onToCalls(): void { this.nav("/anrufe"); }
	onToInquiries(): void { this.nav("/anfragen"); }
	onInquiry(): void { this.nav("/anfragen"); }
	onToApprovals(): void { this.nav("/dokumente?reiter=freigaben"); }
	onToHelpdesk(): void { this.nav("/it-center/helpdesk"); }
	onToBauakten(): void { this.nav("/bauakten"); }
}
