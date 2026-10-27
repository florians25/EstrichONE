import type { ApprovalResponse } from "../util/approvals";
import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get } from "../util/api";
import { isoDay } from "../util/days";
import { dateTime } from "../util/format";
import { session } from "../util/session";
import type { BackupStatusResponse, BauaktePage, PlanningRange, TicketSummary, WorkItemDto } from "../util/models";
import { CATEGORY_ICONS, inbox, relativeTime } from "../util/notifications";

interface HrReminder { kind: string; employeeId: string; employeeName: string; date: string; text: string; severity: string }

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

interface CardRow { text: string; icon?: string; side?: string; sideState?: string; to: string }

interface StartCard {
	key: string;
	title: string;
	subtitle: string;
	icon: string;
	value: string;
	state: "Neutral" | "Good" | "Critical" | "Error";
	details: string;
	empty: string;
	to: string;
	rows: CardRow[];
	loading: boolean;
}

const shortDay = (iso: string) => new Date(`${iso}T12:00:00`).toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit" });

/**
 * Startseite: oben SAP Cards (Kennzahl + wichtigste Einträge) – meine Aufgaben, heutige Einsätze, Genehmigungen, Tickets, Fristen,
 * neue Bauakten, Benachrichtigungen; darunter Schnellzugriff nach Fachgruppen und Sicherung. @namespace estrichone.controller
 */
export default class Start extends BaseController {
	onInit(): void {
		this.local({ greeting: "", today: "", cards: [] as StartCard[], kpis: [], groups: [], backup: null, monitor: null, busy: false });
	}

	onRouteMatched(): void {
		void this.load();
	}

	onRefresh(): void {
		void this.load();
	}

	private card(key: string, title: string, subtitle: string, icon: string, to: string, empty: string): StartCard {
		return { key, title, subtitle, icon, to, empty, value: "…", state: "Neutral", details: "", rows: [], loading: true };
	}

	private async load(): Promise<void> {
		const m = this.model();
		const today = isoDay(new Date());
		const inWeek = isoDay(new Date(Date.now() + 7 * 86_400_000));
		m.setProperty("/greeting", `Guten Tag, ${session.data.user?.displayName ?? ""}`);
		m.setProperty("/today", new Date().toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long", year: "numeric" }));
		m.setProperty("/groups", groups.map((g) => ({ ...g, tiles: g.tiles.filter((t) => !t.permission || this.has(t.permission)) }))
			.filter((g) => g.tiles.length > 0));

		const cards: StartCard[] = [];
		if (this.has("work.read")) cards.push(this.card("work", "Meine Aufgaben", "offen", "sap-icon://task", "/tagesuebersicht", "Nichts offen – gut gemacht."));
		if (this.has("planning.read")) cards.push(this.card("assignments", "Einsätze heute", new Date().toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "2-digit" }), "sap-icon://appointment-2", "/planung/wer-faehrt-wohin", "Heute ist nichts eingeplant."));
		if (this.has("bauakten.read")) cards.push(this.card("approvals", "Warten auf Genehmigung", "Freigaben für mich", "sap-icon://approvals", "/dokumente?reiter=freigaben", "Keine Freigaben offen."));
		// Helpdesk-Bearbeitung sieht offene Tickets, alle anderen an dieser Stelle ihr Postfach (sechs Karten = volle Raster-Zeilen).
		if (this.has("helpdesk.agent")) cards.push(this.card("tickets", "Offene Tickets", "Helpdesk", "sap-icon://headset", "/it-center/helpdesk", "Keine offenen Tickets."));
		else cards.push(this.card("inbox", "Benachrichtigungen", "ungelesen", "sap-icon://bell", "/benachrichtigungen", "Alles gelesen."));
		cards.push(this.card("deadlines", "Fristen", "nächste 7 Tage", "sap-icon://lateness",
			this.has("hr.read") ? "/personal/urlaub?reiter=fristen" : "/tagesuebersicht", "Keine Fristen in den nächsten 7 Tagen."));
		if (this.has("bauakten.read")) cards.push(this.card("newFiles", "Neue Bauakten", "im Status Anfrage", "sap-icon://add-folder", "/bauakten?status=inquiry", "Keine neuen Anfragen."));
		m.setProperty("/cards", cards);
		const set = (key: string, patch: Partial<StartCard>) => {
			const index = cards.findIndex((c) => c.key === key);
			if (index >= 0) {
				cards[index] = { ...cards[index], ...patch, loading: false };
				m.setProperty(`/cards/${index}`, cards[index]);
			}
		};
		const failed = (key: string) => () => set(key, { value: "–", details: "nicht verfügbar" });

		const kpis: Record<string, unknown>[] = [];
		const count = (page: BauaktePage) => (page.nextCursor ? "100+" : String(page.items.length));
		const addKpi = (k: Record<string, unknown>) => {
			kpis.push(k);
			m.setProperty("/kpis", [...kpis]);
		};
		m.setProperty("/kpis", []);
		m.setProperty("/busy", true);
		const mineWork = this.has("work.read") ? get<WorkItemDto[]>("work-items/mine") : Promise.resolve([] as WorkItemDto[]);
		await Promise.allSettled([
			mineWork.then((work) => {
				const open = work.filter((w) => w.status !== "done").sort((a, b) => (a.dueDate ?? "9999").localeCompare(b.dueDate ?? "9999"));
				const overdue = open.filter((w) => w.dueDate && w.dueDate < today).length;
				set("work", {
					value: String(open.length), state: overdue ? "Error" : open.length ? "Neutral" : "Good",
					details: overdue ? `${overdue} überfällig` : open.some((w) => w.kind === "defect") ? `${open.filter((w) => w.kind === "defect").length} Mängel` : "",
					rows: open.slice(0, 3).map((w) => ({
						text: w.title, icon: w.kind === "defect" ? "sap-icon://warning2" : "sap-icon://task",
						side: w.dueDate ? shortDay(w.dueDate) : "", sideState: w.dueDate && w.dueDate < today ? "Error" : w.dueDate === today ? "Warning" : "None",
						to: w.bauakteId ? `/bauakten/${w.bauakteId}?abschnitt=aufgaben` : "/tagesuebersicht",
					})),
				});
			}).catch(failed("work")),
			this.has("planning.read") ? get<PlanningRange>("planning/range", { from: today, to: today }).then((r) => {
				const list = [...r.assignments].sort((a, b) => (a.startTime ?? "99").localeCompare(b.startTime ?? "99"));
				const crews = new Set(list.map((a) => a.crewName).filter(Boolean)).size;
				const warnings = list.filter((a) => a.warnings.length).length;
				set("assignments", {
					value: String(list.length), state: warnings ? "Critical" : "Neutral",
					details: warnings ? `${warnings} mit Hinweisen` : crews ? `${crews} ${crews === 1 ? "Kolonne" : "Kolonnen"} unterwegs` : "",
					rows: list.slice(0, 3).map((a) => ({
						text: `${a.startTime ? a.startTime.slice(0, 5) + " · " : ""}${a.bauakteTitle}`, icon: "sap-icon://shipping-status",
						side: a.crewName ?? "", sideState: "None", to: `/bauakten/${a.bauakteId}`,
					})),
				});
			}).catch(failed("assignments")) : Promise.resolve(),
			this.has("bauakten.read") ? get<ApprovalResponse[]>("approvals", { scope: "mine", status: "pending" }).then((list) => {
				set("approvals", {
					value: String(list.length), state: list.length ? "Critical" : "Good", details: list.length ? "bitte entscheiden" : "",
					rows: list.slice(0, 3).map((a) => ({
						text: a.fileName, icon: "sap-icon://document", side: a.bauakteNumber ?? "", sideState: "None",
						to: `/bauakten/${a.bauakteId}/datei/${a.fileId}?abschnitt=dokumente`,
					})),
				});
			}).catch(failed("approvals")) : Promise.resolve(),
			this.has("helpdesk.agent") ? get<TicketSummary[]>("tickets", { filter: "open" }).then((t) => {
				const blocked = t.filter((x) => x.reporterBlocked).length;
				const fresh = t.filter((x) => x.status === "new").length;
				set("tickets", {
					value: String(t.length), state: blocked ? "Error" : fresh ? "Critical" : "Neutral",
					details: blocked ? `${blocked} ${blocked === 1 ? "kann" : "können"} nicht arbeiten` : fresh ? `${fresh} neu` : "",
					rows: [...t].sort((a, b) => Number(b.reporterBlocked) - Number(a.reporterBlocked)).slice(0, 3).map((x) => ({
						text: `${x.number} ${x.title}`, icon: x.reporterBlocked ? "sap-icon://alert" : "sap-icon://headset",
						side: x.status === "new" ? "neu" : "", sideState: "Information", to: `/it-center/helpdesk/${x.id}`,
					})),
				});
			}).catch(failed("tickets")) : Promise.resolve(),
			Promise.all([
				mineWork.catch(() => [] as WorkItemDto[]),
				this.has("hr.read") ? get<HrReminder[]>("hr/reminders", { days: 7 }).catch(() => [] as HrReminder[]) : Promise.resolve([] as HrReminder[]),
			]).then(([work, hr]) => {
				const rows: (CardRow & { at: string })[] = [
					...work.filter((w) => w.status !== "done" && w.dueDate && w.dueDate <= inWeek).map((w) => ({
						at: w.dueDate!, text: w.title, icon: "sap-icon://task", side: shortDay(w.dueDate!),
						sideState: w.dueDate! < today ? "Error" : w.dueDate === today ? "Warning" : "None",
						to: w.bauakteId ? `/bauakten/${w.bauakteId}?abschnitt=aufgaben` : "/tagesuebersicht",
					})),
					...hr.filter((r) => r.date <= inWeek && r.severity !== "info").map((r) => ({
						at: r.date, text: `${r.employeeName}: ${r.text}`, icon: "sap-icon://employee", side: shortDay(r.date),
						sideState: r.severity === "critical" || r.date < today ? "Error" : r.severity === "warning" ? "Warning" : "None", to: `/personal/${r.employeeId}`,
					})),
				].sort((a, b) => a.at.localeCompare(b.at));
				const due = rows.filter((r) => r.at <= today).length;
				set("deadlines", {
					value: String(rows.length), state: due ? "Error" : rows.length ? "Critical" : "Good",
					details: due ? `${due} heute oder überfällig` : rows.length ? `nächste ${shortDay(rows[0].at)}` : "",
					rows: rows.slice(0, 3),
				});
			}).catch(failed("deadlines")),
			this.has("bauakten.read") ? get<BauaktePage>("bauakten", { status: "inquiry", limit: 100 }).then((p) => {
				set("newFiles", {
					value: count(p), state: "Neutral", details: p.items[0] ? `zuletzt ${shortDay(p.items[0].updatedAt.slice(0, 10))}` : "",
					rows: p.items.slice(0, 3).map((b) => ({
						text: `${b.number ? b.number + " · " : ""}${b.title}`, icon: "sap-icon://folder-blank", side: b.city ?? "", sideState: "None", to: `/bauakten/${b.id}`,
					})),
				});
			}).catch(failed("newFiles")) : Promise.resolve(),
			cards.some((c) => c.key === "inbox") ? inbox({ unread: true, limit: 3 }).then((box) => {
				set("inbox", {
					value: String(box.unreadCount), state: box.items.some((n) => n.urgency === "high") ? "Critical" : "Neutral",
					details: box.items[0] ? relativeTime(box.items[0].createdAt) : "",
					rows: box.items.map((n) => ({ text: n.title, icon: CATEGORY_ICONS[n.category], side: "", sideState: "None", to: n.link ?? "/benachrichtigungen" })),
				});
			}).catch(failed("inbox")) : Promise.resolve(),
			this.has("bauakten.read") ? get<BauaktePage>("bauakten", { status: "inProgress", limit: 100 }).then((p) =>
				addKpi({ title: "Baustellen in Ausführung", value: count(p), icon: "sap-icon://process", to: "/bauakten?status=inProgress", infoState: "Success" })) : Promise.resolve(),
			this.has("absence.approve") ? get<{ pendingApprovals: number; remindersDue: number }>("hr/summary").then((h) =>
				addKpi({ title: "Urlaubsanträge offen", value: String(h.pendingApprovals), icon: "sap-icon://general-leave-request", to: "/personal/urlaub?reiter=antraege",
					infoState: h.pendingApprovals ? "Warning" : "None" })) : Promise.resolve(),
			this.has("backup.manage") ? get<BackupStatusResponse>("backup/status").then((b) => m.setProperty("/backup", b.lastSuccess
				? { text: b.healthy ? "Aktuell" : "Veraltet", state: b.healthy ? "Success" : "Warning", detail: `Letzte erfolgreiche Sicherung: ${dateTime(b.lastSuccess.at)}` }
				: { text: "Bisher wurde keine Sicherung abgeschlossen.", state: "Error", detail: b.summary })) : Promise.resolve(),
			// Überwachung (System → Überwachung): Hinweis nur bei Warnung
			this.has("backup.manage") ? get<{ level: string; warnings: string[] }>("system/monitoring").then((o) => m.setProperty("/monitor",
				o.level === "ok" ? null : { type: o.level === "critical" ? "Error" : "Warning", text: o.warnings.slice(0, 3).join(" · ") })).catch(() => undefined) : Promise.resolve(),
		]);
		m.setProperty("/busy", false);
	}

	onTile(event: Event): void {
		const control = event.getSource() as Control;
		const to = control.getBindingContext("view")?.getProperty("to") as string | undefined;
		if (to) this.nav(to);
	}

	onRow(event: Event): void {
		const to = (event.getSource() as Control).getBindingContext("view")?.getProperty("to") as string | undefined;
		if (to) this.nav(to);
	}
}
