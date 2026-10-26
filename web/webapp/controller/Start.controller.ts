import type { ApprovalResponse } from "../util/approvals";
import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get } from "../util/api";
import { isoDay } from "../util/days";
import { dateTime } from "../util/format";
import { session } from "../util/session";
import type { AssignmentView, BackupStatusResponse, BauaktePage, PlanningRange, TicketSummary } from "../util/models";

interface Tile { label: string; icon: string; to: string; permission?: string; color?: string }

const groups: { label: string; tiles: Tile[] }[] = [
	{ label: "Auftragsbearbeitung", tiles: [
		{ label: "Tagesübersicht", icon: "sap-icon://date-time", to: "/tagesuebersicht", permission: "bauakten.read" },
		{ label: "Bauakten", icon: "sap-icon://folder-full", to: "/bauakten", permission: "bauakten.read" },
		{ label: "Neue Bauakte", icon: "sap-icon://add-document", to: "/bauakten?neu=bauakte", permission: "bauakten.create" },
		{ label: "Anfragen", icon: "sap-icon://inbox", to: "/anfragen", permission: "contacts.read" },
		{ label: "Baustellenkarte", icon: "sap-icon://map-2", to: "/bauakten/karte", permission: "bauakten.read" },
	] },
	{ label: "Planung", tiles: [
		{ label: "Kolonnenplanung", icon: "sap-icon://gantt-bars", to: "/planung", permission: "planning.read" },
		{ label: "Arbeitsplan", icon: "sap-icon://list", to: "/planung/arbeitsplan", permission: "planning.read" },
		{ label: "Kolonnen", icon: "sap-icon://group", to: "/planung/kolonnen", permission: "planning.read" },
		{ label: "Geräte & Fahrzeuge", icon: "sap-icon://shipping-status", to: "/planung/geraete", permission: "planning.read" },
		{ label: "Abwesenheiten", icon: "sap-icon://general-leave-request", to: "/personal/urlaub?reiter=plan", permission: "hr.read" },
	] },
	{ label: "Material", tiles: [
		{ label: "Bestellungen", icon: "sap-icon://cart", to: "/bestellungen", permission: "materials.read" },
		{ label: "Materialstamm", icon: "sap-icon://product", to: "/material", permission: "materials.read" },
		{ label: "Lieferanten", icon: "sap-icon://supplier", to: "/lieferanten", permission: "materials.read" },
	] },
	{ label: "Büro", tiles: [
		{ label: "Kunden", icon: "sap-icon://customer", to: "/kunden", permission: "contacts.read" },
		{ label: "Anrufe", icon: "sap-icon://call", to: "/anrufe", permission: "contacts.read" },
	] },
	{ label: "IT Center", tiles: [
		{ label: "Benutzer", icon: "sap-icon://employee", to: "/verwaltung/benutzer", permission: "users.manage" },
		{ label: "Geräte", icon: "sap-icon://iphone", to: "/it-center/geraete", permission: "devices.read" },
		{ label: "Helpdesk", icon: "sap-icon://headset", to: "/it-center/helpdesk", permission: "helpdesk.agent" },
	] },
	{ label: "System", tiles: [
		{ label: "Firmendaten", icon: "sap-icon://building", to: "/system/firmendaten", permission: "settings.manage" },
		{ label: "Sicherung", icon: "sap-icon://upload-to-cloud", to: "/system/sicherung", permission: "backup.manage" },
		{ label: "Protokoll", icon: "sap-icon://activities", to: "/system/protokoll", permission: "audit.read" },
		{ label: "Passwort ändern", icon: "sap-icon://key", to: "/konto/passwort" },
	] },
];

/** Startseite: Kennzahlen, Programmkacheln nach Fachgruppen, heutige Einsätze und Sicherung. @namespace estrichone.controller */
export default class Start extends BaseController {
	onInit(): void {
		this.local({ greeting: "", today: "", kpis: [], groups: [], assignments: [] as AssignmentView[], approvals: [], backup: null, busy: false });
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
		m.setProperty("/greeting", `Guten Tag, ${session.data.user?.displayName ?? ""}`);
		m.setProperty("/today", new Date().toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long", year: "numeric" }));
		m.setProperty("/groups", groups.map((g) => ({ ...g, tiles: g.tiles.filter((t) => !t.permission || this.has(t.permission)) }))
			.filter((g) => g.tiles.length > 0));
		const kpis: Record<string, unknown>[] = [];
		const count = (page: BauaktePage) => (page.nextCursor ? "100+" : String(page.items.length));
		if (this.has("planning.read")) kpis.push({ key: "assignments", title: "Einsätze heute", icon: "sap-icon://appointment-2", value: "…", to: "/planung?ansicht=tag", loading: true, color: "Neutral" });
		if (this.has("bauakten.read")) {
			kpis.push({ key: "inquiries", title: "Offene Anfragen", subtitle: "Bauakten im Status Anfrage", icon: "sap-icon://inbox", value: "…", to: "/bauakten?status=inquiry", loading: true, color: "Neutral" });
			kpis.push({ key: "running", title: "Baustellen in Ausführung", icon: "sap-icon://process", value: "…", to: "/bauakten?status=inProgress", loading: true, color: "Good" });
		}
		if (this.has("bauakten.read")) kpis.push({ key: "approvals", title: "Warten auf Genehmigung", subtitle: "Dokumentfreigaben für mich", icon: "sap-icon://approvals", value: "…", to: "/dokumente?reiter=freigaben", loading: true, color: "Neutral" });
		if (this.has("absence.approve")) kpis.push({ key: "vacation", title: "Urlaubsanträge", subtitle: "Warten auf Genehmigung", icon: "sap-icon://general-leave-request", value: "…", to: "/personal/urlaub?reiter=antraege", loading: true, color: "Neutral" });
		if (this.has("hr.read")) kpis.push({ key: "hrDeadlines", title: "Personal-Fristen", subtitle: "Nachweise, Probezeit (30 Tage)", icon: "sap-icon://lateness", value: "…", to: "/personal/urlaub?reiter=fristen", loading: true, color: "Neutral" });
		if (this.has("helpdesk.agent")) kpis.push({ key: "tickets", title: "Offene Tickets", icon: "sap-icon://headset", value: "…", to: "/it-center/helpdesk", loading: true, color: "Neutral" });
		m.setProperty("/kpis", kpis);
		const set = (key: string, value: string, extra: Record<string, unknown> = {}) => {
			const index = kpis.findIndex((k) => k.key === key);
			if (index >= 0) {
				m.setProperty(`/kpis/${index}`, { ...kpis[index], ...extra, value, loading: false });
			}
		};
		m.setProperty("/busy", true);
		await Promise.allSettled([
			this.has("planning.read") ? get<PlanningRange>("planning/range", { from: today, to: today }).then((r) => {
				m.setProperty("/assignments", r.assignments);
				set("assignments", String(r.assignments.length));
			}) : Promise.resolve(),
			this.has("bauakten.read") ? get<BauaktePage>("bauakten", { status: "inquiry", limit: 100 }).then((p) => set("inquiries", count(p))) : Promise.resolve(),
			this.has("bauakten.read") ? get<BauaktePage>("bauakten", { status: "inProgress", limit: 100 }).then((p) => set("running", count(p))) : Promise.resolve(),
			this.has("bauakten.read") ? get<ApprovalResponse[]>("approvals", { scope: "mine", status: "pending" }).then((list) => {
				m.setProperty("/approvals", list.slice(0, 5));
				set("approvals", String(list.length), list.length ? { color: "Critical" } : {});
			}) : Promise.resolve(),
			this.has("absence.request") ? get<{ pendingApprovals: number; remindersDue: number }>("hr/summary").then((h) => {
				set("vacation", String(h.pendingApprovals), h.pendingApprovals ? { color: "Critical" } : {});
				set("hrDeadlines", String(h.remindersDue), h.remindersDue ? { color: "Critical" } : {});
			}) : Promise.resolve(),
			this.has("helpdesk.agent") ? get<TicketSummary[]>("tickets", { filter: "open" }).then((t) => {
				const blocked = t.filter((x) => x.reporterBlocked).length;
				set("tickets", String(t.length), blocked ? { footer: `${blocked} können nicht arbeiten`, color: "Critical" } : {});
			}) : Promise.resolve(),
			this.has("backup.manage") ? get<BackupStatusResponse>("backup/status").then((b) => m.setProperty("/backup", b.lastSuccess
				? { text: b.healthy ? "Aktuell" : "Veraltet", state: b.healthy ? "Success" : "Warning", detail: `Letzte erfolgreiche Sicherung: ${dateTime(b.lastSuccess.at)}` }
				: { text: "Bisher wurde keine Sicherung abgeschlossen.", state: "Error", detail: b.summary })) : Promise.resolve(),
		]);
		m.setProperty("/busy", false);
	}

	onTile(event: Event): void {
		const control = event.getSource() as Control;
		const to = control.getBindingContext("view")?.getProperty("to") as string | undefined;
		if (to) this.nav(to);
	}

	onAssignment(event: Event): void {
		const id = (event.getSource() as Control).getBindingContext("view")?.getProperty("bauakteId") as string;
		this.nav(`/bauakten/${id}`);
	}

	onApproval(event: Event): void {
		const a = (event.getSource() as Control).getBindingContext("view")?.getObject() as ApprovalResponse;
		this.nav(`/bauakten/${a.bauakteId}/datei/${a.fileId}?abschnitt=dokumente`);
	}
}
