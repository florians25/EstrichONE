import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type MenuItem from "sap/m/MenuItem";
import type { Menu$ItemSelectedEvent } from "sap/m/Menu";
import type Table from "sap/m/Table";
import type ListItemBase from "sap/m/ListItemBase";
import type Select from "sap/m/Select";
import Item from "sap/ui/core/Item";
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import Link from "sap/m/Link";
import VBox from "sap/m/VBox";
import { del, get, patch, post } from "../../util/api";
import { textBlocks } from "../../util/catalog";
import { waitingSince } from "../../util/format";
import { openForm, type FormHandle } from "../../util/forms";
import {
	auditRows, compareTickets, deviceLine, fillPlaceholders, formatMinutes, insertMention, kpiTiles, mentionCandidates, mentionedAgents, mentionQuery,
	pageText, parseMinutes, queueViews, slaInfo, ticketChannels, updateStates,
	type CannedResponse, type HdDetail, type HdSummary, type HdTimelineItem, type HelpdeskAgent, type HelpdeskGroup, type HelpdeskOptions,
	type HelpdeskReporter, type HelpdeskStats, type KnowledgeArticleSummary, type SortColumn, type TicketChannel, type TicketContext, type TicketPage,
	type TimeEntry,
} from "../../util/helpdesk";
import { uuidv7 } from "../../util/ids";
import { options, ticketCategories, ticketPriorities, ticketStatus } from "../../util/labels";
import { uploadLoose } from "../../util/looseUpload";
import { attachmentView, type AttachmentView } from "../../util/media";
import { openAttachment } from "../../util/mediaViewer";
import type { FileResponse, TicketCategory } from "../../util/models";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";

const icons: Record<string, string> = {
	created: "sap-icon://add-document", comment: "sap-icon://comment", note: "sap-icon://notes", status: "sap-icon://status-in-process",
	assignee: "sap-icon://employee", priority: "sap-icon://high-priority", category: "sap-icon://tag", subcategory: "sap-icon://tag", group: "sap-icon://group",
	rule: "sap-icon://process", merged: "sap-icon://combine", mergedFrom: "sap-icon://combine", link: "sap-icon://chain-link", unlink: "sap-icon://broken-link",
	article: "sap-icon://course-book", rating: "sap-icon://favorite", time: "sap-icon://time-entry-request", watcher: "sap-icon://show",
};
const kindTexts: Record<string, string> = {
	created: "Meldung", comment: "Antwort", note: "Interne Notiz", status: "Statuswechsel", assignee: "Zuweisung", priority: "Priorität", category: "Kategorie",
	subcategory: "Unterkategorie", group: "Gruppe", rule: "Regel", merged: "Zusammengeführt", mergedFrom: "Zusammengeführt", link: "Verknüpfung",
	unlink: "Verknüpfung", article: "Anleitung", rating: "Bewertung", time: "Arbeitszeit", watcher: "Beobachter",
};
const done = (s: string): boolean => s === "resolved" || s === "closed";
const highlight: Record<string, string> = { overdue: "Error", dueSoon: "Warning", paused: "Information" };
/** Arbeitsliste: so viele Tickets je Seite vom Server, „Weitere laden“ holt die nächste Seite. */
const PAGE_SIZE = 100;

/**
 * IT Center › Helpdesk. Bearbeitung: Arbeitsplatz mit Kennzahlen, gespeicherten Ansichten (Meine, Nicht zugewiesen, Überfällig,
 * Wartet auf Rückmeldung, Alle offenen …), sortierbarer Liste mit Sammelbearbeitung und Volltextsuche; ein gewähltes Ticket
 * öffnet die geteilte Ansicht mit Verlauf (Antworten, interne Notizen, Anhänge), Schnellaktionen, Melder und Geräten,
 * Verknüpfungen, Arbeitszeit und Prüfpfad. Alle anderen sehen ihre eigenen Tickets („Meine Tickets“) und bewerten die Lösung.
 * @namespace estrichone.controller.admin
 */
export default class Helpdesk extends BaseController {
	private files = new Map<string, FileResponse | null>();
	private timeline: HdTimelineItem[] = [];
	private loadedAt = 0;
	private loadedFor = "";
	private searchTimer: ReturnType<typeof setTimeout> | undefined;
	private agents: HelpdeskAgent[] = [];
	private groups: HelpdeskGroup[] = [];
	private hdOptions: HelpdeskOptions | null = null;
	private mentionAt: { start: number; caret: number } | null = null;

	onInit(): void {
		this.local({ items: [], visible: [], filter: "open", category: "", priority: "", group: "", sort: "waiting", desc: false, sortIcons: {},
			query: "", busy: false, agent: false, segments: [], views: [], viewTitle: "Helpdesk", noData: "", countText: "", selectedId: "",
			categories: [{ key: "", text: "Alle Kategorien" }, ...options(ticketCategories)],
			priorities: [{ key: "", text: "Alle Prioritäten" }, ...options(ticketPriorities)], groupFilter: [], groups: [], groupMenu: [], assignMenu: [],
			kpis: [], byCategory: [], byAgent: [], selectedCount: 0, selectionText: "",
			t: null, timeline: [], facts: [], sla: {}, edit: {}, reply: "", internal: false, mentions: [], agentItems: [], detailBusy: false, diagnostics: [],
			pending: [], show: "all", me: "", canUpload: false, blocks: [], resolvedText: "", tab: "talk", ctx: null, contextBusy: false, system: [],
			lastLoginText: "", times: [], timeTotal: "0 min", timeText: "", linkCount: "", audit: [], rate: { value: 0, comment: "" },
			options: { timeTrackingEnabled: false }, watcherText: "", canDevices: false, subOptions: [],
			total: 0, hasMore: false, moreText: "", loadingMore: false, mention: { open: false, index: 0, items: [] },
			statusOptions: options(ticketStatus), priorityOptions: options(ticketPriorities), categoryOptions: options(ticketCategories), people: [] });
		// Das Antwortfeld wird erst mit einem Ticket gezeichnet: Ziehen/Einfügen danach anbinden.
		this.byId("composer")?.addEventDelegate({ onAfterRendering: () => this.wireComposer() });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		const agent = this.has("helpdesk.agent");
		const switched = this.loadedFor !== session.userId;
		m.setProperty("/agent", agent);
		m.setProperty("/me", session.userId);
		m.setProperty("/canUpload", this.has("files.upload"));
		m.setProperty("/canDevices", this.has("devices.read"));
		if (switched) {
			this.loadedFor = session.userId ?? "";
			m.setProperty("/filter", "open");
			m.setProperty("/segments", [{ key: "open", text: "Offen" }, { key: "done", text: "Erledigt" }, { key: "all", text: "Alle" }]);
			this.updateSortIcons();
			void this.loadMeta(agent);
		}
		const id = match.params.id ?? "";
		const previous = m.getProperty("/selectedId") as string;
		m.setProperty("/selectedId", id);
		if (!id && previous) this.clearSelection();
		// Liste nur neu laden, wenn sie älter als 30 Sekunden ist (Wechsel zwischen Tickets ohne Flackern).
		if (switched || Date.now() - this.loadedAt > 30_000 || !id) void this.load();
		if (id) await this.open(id);
		else this.clearDetail();
	}

	/** Stammdaten für Bearbeitung und Melden: Optionen, Bearbeiter, Gruppen, Textbausteine. */
	private async loadMeta(agent: boolean): Promise<void> {
		const m = this.model();
		this.hdOptions = await get<HelpdeskOptions>("helpdesk/options").catch(() => null);
		m.setProperty("/options", { timeTrackingEnabled: !!this.hdOptions?.timeTrackingEnabled });
		if (!agent) return;
		const [agents, groups, responses, legacy] = await Promise.all([
			get<HelpdeskAgent[]>("helpdesk/agents").catch(() => [] as HelpdeskAgent[]),
			get<HelpdeskGroup[]>("helpdesk/groups").catch(() => [] as HelpdeskGroup[]),
			get<CannedResponse[]>("helpdesk/responses").catch(() => [] as CannedResponse[]),
			textBlocks("helpdesk").catch(() => []),
		]);
		this.agents = agents;
		this.groups = groups.filter((g) => g.isActive);
		m.setProperty("/people", [{ key: "none", text: "Niemand" }, ...agents.map((a) => ({ key: a.id, text: a.displayName }))]);
		m.setProperty("/agentItems", agents.filter((a) => a.id !== session.userId).map((a) => ({ key: a.id, text: a.displayName })));
		m.setProperty("/assignMenu", [{ key: session.userId, text: "Mir zuweisen" }, { key: "none", text: "Zuweisung aufheben" },
			...agents.filter((a) => a.id !== session.userId).map((a) => ({ key: a.id, text: `${a.displayName} (${a.openTickets} offen)` }))]);
		m.setProperty("/groups", this.groups);
		m.setProperty("/groupMenu", [{ key: "none", text: "Keine Gruppe" }, ...this.groups.map((g) => ({ key: g.id, text: g.name }))]);
		m.setProperty("/groupFilter", [{ key: "", text: "Alle Gruppen" }, ...this.groups.map((g) => ({ key: g.id, text: g.name }))]);
		m.setProperty("/blocks", [
			...responses.map((r) => ({ name: r.isPersonal ? `${r.name} (eigener)` : r.name, text: r.text, section: false })),
			...legacy.map((b, i) => ({ name: b.name, text: b.content.text, section: i === 0 && responses.length > 0 })),
		]);
		// Namen für Erwähnungen im Verlauf stehen erst jetzt bereit.
		if (m.getProperty("/t")) this.renderTimeline();
	}

	// ---- Liste ------------------------------------------------------------------------------------------------------

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const agent = m.getProperty("/agent") as boolean;
			if (agent) {
				const q = String(m.getProperty("/query") ?? "").trim();
				const [page, stats] = await Promise.all([
					get<TicketPage>("tickets/page", { ...this.listQuery(q), offset: 0, limit: PAGE_SIZE }),
					get<HelpdeskStats>("tickets/stats").catch(() => null),
				]);
				m.setProperty("/items", page.items);
				m.setProperty("/total", page.total);
				if (stats) this.applyStats(stats);
			} else {
				m.setProperty("/items", await get<HdSummary[]>("tickets/mine"));
			}
			this.loadedAt = Date.now();
			this.applyList();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	/** Ansicht, Filter, Suche und Spaltensortierung für die Arbeitsliste (der Server sortiert über alle Seiten). */
	private listQuery(q: string): Record<string, string | number | boolean | undefined> {
		const m = this.model();
		return {
			filter: m.getProperty("/filter"), category: m.getProperty("/category") || undefined, priority: m.getProperty("/priority") || undefined,
			group: m.getProperty("/group") || undefined, q: q.length >= 2 ? q : undefined, sort: m.getProperty("/sort"), desc: !!m.getProperty("/desc"),
		};
	}

	/** Nächste Seite der Arbeitsliste anhängen. */
	async onLoadMore(): Promise<void> {
		const m = this.model();
		const items = m.getProperty("/items") as HdSummary[];
		m.setProperty("/loadingMore", true);
		try {
			const q = String(m.getProperty("/query") ?? "").trim();
			const page = await get<TicketPage>("tickets/page", { ...this.listQuery(q), offset: items.length, limit: PAGE_SIZE });
			const known = new Set(items.map((t) => t.id));
			m.setProperty("/items", [...items, ...page.items.filter((t) => !known.has(t.id))]);
			m.setProperty("/total", page.total);
			this.applyList();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/loadingMore", false);
		}
	}

	private applyStats(stats: HelpdeskStats): void {
		const m = this.model();
		m.setProperty("/kpis", kpiTiles(stats));
		const counts: Record<string, number> = { mine: stats.mine, unassigned: stats.unassigned, overdue: stats.overdue, waiting: stats.waiting, open: stats.open };
		m.setProperty("/views", queueViews.map((v) => ({ ...v, count: counts[v.key] === undefined ? "" : String(counts[v.key]) })));
		const maxCategory = Math.max(1, ...stats.byCategory.map((c) => c.open));
		m.setProperty("/byCategory", stats.byCategory.filter((c) => c.open > 0).map((c) => ({
			key: c.category, text: ticketCategories[c.category] ?? c.category, open: String(c.open), percent: Math.round((c.open / maxCategory) * 100) })));
		const maxAgent = Math.max(1, ...stats.byAgent.map((a) => a.open));
		m.setProperty("/byAgent", stats.byAgent.slice(0, 8).map((a) => ({ ...a, percent: Math.round((a.open / maxAgent) * 100),
			label: a.overdue ? `${a.open} offen · ${a.overdue} überfällig` : `${a.open} offen` })));
	}

	private applyList(): void {
		const m = this.model();
		const agent = m.getProperty("/agent") as boolean;
		const filter = m.getProperty("/filter") as string;
		const category = m.getProperty("/category") as string;
		const q = String(m.getProperty("/query") ?? "").trim().toLowerCase();
		const sort = m.getProperty("/sort") as SortColumn;
		const desc = m.getProperty("/desc") as boolean;
		let list = (m.getProperty("/items") as HdSummary[]).slice();
		if (!agent) {
			list = list.filter((t) => filter === "all" || (filter === "done" ? done(t.status) : !done(t.status)));
			if (category) list = list.filter((t) => t.category === category);
		}
		if (q && !agent) {
			// Bearbeitung: Der Server sucht im Volltext (auch Beschreibung und Antworten); Melder: hier in Nummer, Titel und Namen.
			list = list.filter((t) => q.split(/\s+/).every((w) =>
				[t.number, t.title, t.reporterDisplayName, t.assigneeName, ticketCategories[t.category]].join(" ").toLowerCase().includes(w)));
		}
		// Bearbeitung: Der Server sortiert über alle Seiten; Melder: hier.
		if (!agent) list.sort((a, b) => compareTickets(a, b, sort, desc));
		const now = Date.now();
		m.setProperty("/visible", list.map((t) => {
			const sla = slaInfo(t, now);
			return { ...t, slaText: sla.text, slaTone: sla.tone, slaIcon: sla.icon, slaShort: ["overdue", "dueSoon"].includes(sla.key) ? sla.text : "",
				highlight: highlight[sla.key] ?? "None",
				who: agent ? `${t.reporterDisplayName ?? "–"} · ${t.assigneeName ? `bei ${t.assigneeName}` : "nicht zugewiesen"}`
					: t.assigneeName ? `bei ${t.assigneeName}` : "noch nicht zugewiesen" };
		}));
		const unread = list.filter((t) => t.hasUnread).length;
		const total = agent ? Math.max(list.length, Number(m.getProperty("/total") ?? 0)) : list.length;
		m.setProperty("/countText", pageText(list.length, total, unread));
		m.setProperty("/hasMore", agent && total > list.length);
		m.setProperty("/moreText", `Weitere ${Math.min(PAGE_SIZE, total - list.length)} laden`);
		const view = queueViews.find((v) => v.key === filter);
		m.setProperty("/viewTitle", view?.text ?? "Helpdesk");
		m.setProperty("/noData", q ? `Keine Tickets zu „${q}“.` : agent ? view?.empty ?? "Keine Tickets in dieser Ansicht." : "Sie haben noch kein Ticket gemeldet.");
		this.clearSelection();
	}

	private updateSortIcons(): void {
		const m = this.model();
		const sort = m.getProperty("/sort") as string;
		const icon = m.getProperty("/desc") ? "sap-icon://sort-descending" : "sap-icon://sort-ascending";
		const cols = ["number", "title", "reporter", "category", "priority", "status", "assignee", "due", "activity"];
		m.setProperty("/sortIcons", Object.fromEntries(cols.map((c) => [c, c === sort ? icon : ""])));
	}

	onSortColumn(event: Event): void {
		const col = String((event.getSource() as Control).data("col"));
		const m = this.model();
		m.setProperty("/desc", m.getProperty("/sort") === col ? !m.getProperty("/desc") : false);
		m.setProperty("/sort", col);
		this.updateSortIcons();
		if (m.getProperty("/agent")) void this.load();
		else this.applyList();
	}

	onView(event: Event): void {
		const params = event.getParameters() as { key?: string; selectedItem?: Item };
		const key = params.key ?? params.selectedItem?.getKey() ?? (this.model().getProperty("/filter") as string);
		this.model().setProperty("/filter", key);
		void this.load();
	}

	onKpi(event: Event): void {
		const key = String((event.getSource() as Control).getBindingContext("view")?.getProperty("key") ?? "");
		if (!key) return;
		this.model().setProperty("/filter", key);
		void this.load();
	}

	onCategoryBar(event: Event): void {
		const key = String((event.getSource() as Control).getBindingContext("view")?.getProperty("key") ?? "");
		this.model().setProperty("/category", key);
		this.model().setProperty("/filter", "open");
		void this.load();
	}

	onFilter(): void {
		if (this.model().getProperty("/agent")) void this.load();
		else this.applyList();
	}

	onSort(): void {
		this.model().setProperty("/desc", false);
		this.applyList();
	}

	onQueryLive(): void {
		this.applyList();
		if (!this.model().getProperty("/agent")) return;
		clearTimeout(this.searchTimer);
		this.searchTimer = setTimeout(() => void this.load(), 350);
	}

	onSearch(): void {
		if (this.model().getProperty("/agent")) void this.load();
		else this.applyList();
	}

	onRefresh(): void { void this.load(); }

	onSelect(event: Event): void {
		const item = (event as Event<{ listItem: Control }>).getParameter("listItem");
		const t = item?.getBindingContext("view")?.getObject() as HdSummary | undefined;
		if (t) this.nav(`/it-center/helpdesk/${t.id}`);
	}

	onRowPress(event: Event): void {
		const t = (event.getSource() as Control).getBindingContext("view")?.getObject() as HdSummary | undefined;
		if (t) this.nav(`/it-center/helpdesk/${t.id}`);
	}

	onBackToQueue(): void {
		this.nav("/it-center/helpdesk");
	}

	onKnowledge(): void { this.nav("/wissen"); }

	onSettings(): void { this.nav("/it-center/helpdesk-einstellungen"); }

	// ---- Sammelbearbeitung -------------------------------------------------------------------------------------------

	private table(): Table | undefined {
		return this.byId("hdTable") as Table | undefined;
	}

	private selectedIds(): string[] {
		return (this.table()?.getSelectedItems() ?? []).map((i: ListItemBase) => (i.getBindingContext("view")?.getObject() as HdSummary).id);
	}

	private clearSelection(): void {
		this.table()?.removeSelections(true);
		this.onTableSelection();
	}

	onTableSelection(): void {
		const count = this.table()?.getSelectedItems().length ?? 0;
		this.model().setProperty("/selectedCount", count);
		this.model().setProperty("/selectionText", count === 1 ? "1 ausgewählt" : `${count} ausgewählt`);
	}

	private async bulk(body: Record<string, unknown>, what: string): Promise<void> {
		const ids = this.selectedIds();
		if (!ids.length) return;
		const result = await this.run(() => post<{ updated: number; failed: { number: string | null; message: string }[] }>("tickets/bulk", { ids, ...body }));
		if (!result) return;
		if (result.failed.length) {
			this.showError(new Error(`${result.updated} von ${ids.length} Tickets geändert. Nicht möglich:\n${
				result.failed.map((f) => `${f.number ?? "?"}: ${f.message}`).join("\n")}`), what);
		} else {
			this.toast(`${result.updated} ${result.updated === 1 ? "Ticket" : "Tickets"}: ${what}`);
		}
		await this.load();
	}

	private static key(event: Menu$ItemSelectedEvent): string {
		return String((event.getParameter("item") as MenuItem).data("key") ?? "");
	}

	async onBulkAssign(event: Menu$ItemSelectedEvent): Promise<void> {
		const key = Helpdesk.key(event);
		await this.bulk(key === "none" ? { unassign: true } : { assigneeUserId: key }, key === "none" ? "Zuweisung aufgehoben" : "zugewiesen");
	}

	async onBulkPriority(event: Menu$ItemSelectedEvent): Promise<void> {
		const key = Helpdesk.key(event);
		await this.bulk({ priority: key }, `Priorität ${ticketPriorities[key as keyof typeof ticketPriorities]?.text ?? key}`);
	}

	async onBulkGroup(event: Menu$ItemSelectedEvent): Promise<void> {
		const key = Helpdesk.key(event);
		await this.bulk(key === "none" ? { clearGroup: true } : { groupId: key }, "Gruppe gesetzt");
	}

	async onBulkStatus(event: Menu$ItemSelectedEvent): Promise<void> {
		const status = Helpdesk.key(event);
		const text = ticketStatus[status as keyof typeof ticketStatus]?.text ?? status;
		if (!done(status)) {
			await this.bulk({ status }, `Status ${text}`);
			return;
		}
		const ids = this.selectedIds();
		const saved = await openForm<boolean>({
			title: `${ids.length} Tickets: ${text}`, width: "34rem", submitText: "Speichern",
			intro: "Die Lösung erscheint bei allen ausgewählten Tickets im Verlauf und geht an die Melder.",
			fields: [{ key: "resolution", label: "Lösung", type: "textarea", rows: 4, required: true, maxLength: 8000 }],
			submit: async (v) => { await this.bulk({ status, resolution: v.resolution }, `Status ${text}`); return true; },
		});
		if (!saved) this.clearSelection();
	}

	// ---- Ticket -----------------------------------------------------------------------------------------------------

	private clearDetail(): void {
		const m = this.model();
		m.setProperty("/t", null);
		m.setProperty("/timeline", []);
		m.setProperty("/facts", []);
		m.setProperty("/ctx", null);
		document.title = "Helpdesk – EstrichONE";
	}

	private async open(id: string): Promise<void> {
		const m = this.model();
		if ((m.getProperty("/t") as HdDetail | null)?.summary.id !== id) {
			m.setProperty("/reply", "");
			m.setProperty("/pending", []);
			m.setProperty("/internal", false);
			m.setProperty("/mentions", []);
			m.setProperty("/tab", "talk");
			m.setProperty("/ctx", null);
			m.setProperty("/times", []);
		}
		m.setProperty("/detailBusy", true);
		try {
			await this.apply(await get<HdDetail>(`tickets/${id}`));
			// Schmale Fenster: Liste und Ticket stehen untereinander – zum Ticket springen.
			if (window.matchMedia("(max-width: 1000px)").matches) {
				setTimeout(() => (this.getView()?.getDomRef() as HTMLElement | null)?.querySelector(".eoHdDetail")?.scrollIntoView({ block: "start" }), 0);
			}
			// Abruf markiert als gelesen: Zähler in der Kopfleiste aktualisieren.
			document.dispatchEvent(new CustomEvent("estrichone-notices"));
			if (m.getProperty("/agent")) void this.loadContext(id);
		} catch (error) {
			this.showError(error);
			this.nav("/it-center/helpdesk", true);
		} finally {
			m.setProperty("/detailBusy", false);
		}
	}

	private async apply(t: HdDetail): Promise<void> {
		const m = this.model();
		const s = t.summary;
		m.setProperty("/t", t);
		m.setProperty("/edit", { status: s.status, priority: s.priority, category: s.category, assignee: s.assigneeUserId ?? "none",
			group: s.groupId ?? "none", subcategory: s.subcategoryId ?? "none" });
		this.updateSubOptions(s.category);
		m.setProperty("/resolvedText", `Die IT hat das Ticket als gelöst markiert${t.resolution ? `: ${t.resolution}` : "."} Besteht das Problem weiter?`);
		m.setProperty("/diagnostics", Object.entries(t.diagnostics ?? {}).map(([key, value]) => ({ key: diagnosticLabels[key] ?? key,
			value: Array.isArray(value) ? value.map((v) => (typeof v === "object" ? JSON.stringify(v) : String(v))).join("\n") : typeof value === "object" ? JSON.stringify(value) : String(value) })));
		m.setProperty("/sla", slaInfo(s));
		m.setProperty("/rate", { value: t.rating ?? 0, comment: t.ratingComment ?? "" });
		m.setProperty("/linkCount", (t.links?.length ?? 0) + (t.articles?.length ?? 0) ? String((t.links?.length ?? 0) + (t.articles?.length ?? 0)) : "");
		m.setProperty("/timeText", t.totalMinutes ? formatMinutes(t.totalMinutes) : "");
		m.setProperty("/timeTotal", formatMinutes(t.totalMinutes ?? 0));
		m.setProperty("/watcherText", t.watcherNames?.length ? `Verfolgt außerdem: ${t.watcherNames.join(", ")}` : "");
		document.title = `${s.number} ${s.title} – EstrichONE`;
		const timeline = t.timeline ?? [];
		const ids = [...new Set(timeline.flatMap((i) => i.attachmentFileIds))].filter((id) => !this.files.has(id));
		await Promise.all(ids.map(async (id) => this.files.set(id, await get<FileResponse>(`files/${id}`).catch(() => null))));
		this.timeline = timeline;
		m.setProperty("/facts", this.facts(t, timeline));
		m.setProperty("/audit", auditRows(timeline));
		this.renderTimeline();
		this.patchRow(s);
		if (m.getProperty("/tab") === "time") void this.loadTimes();
		setTimeout(() => this.scrollToEnd(), 0);
	}

	private updateSubOptions(category: TicketCategory): void {
		const subs = this.hdOptions?.categories.find((c) => c.key === category)?.subcategories ?? [];
		this.model().setProperty("/subOptions", [{ key: "none", text: "–" }, ...subs.map((s) => ({ key: s.id, text: s.name }))]);
	}

	/** Kopf-Fakten: Kategorie, Melder, Zuständig, Gruppe, Alter, Ziele, Quelle, Gerät. */
	private facts(t: HdDetail, timeline: HdTimelineItem[]): { label: string; value: string; state: string; icon: string }[] {
		const s = t.summary;
		const agent = this.model().getProperty("/agent") as boolean;
		const sources: Record<string, string> = { web: "Web", desktop: "Windows", ios: "iOS", android: "Android" };
		const fmt = (v: string | null | undefined) => (v ? new Date(v).toLocaleString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }) : "–");
		const firstAnswer = s.firstResponseAt ?? timeline.find((i) => i.kind === "comment" && !i.isReporter)?.at ?? null;
		const rows = [
			{ label: "Kategorie", value: `${ticketCategories[s.category] ?? s.category}${s.subcategoryName ? ` › ${s.subcategoryName}` : ""}`, state: "None", icon: "" },
			{ label: "Melder", value: s.reporterDisplayName ?? "–", state: "None", icon: "" },
			{ label: "Zuständig", value: `${s.assigneeName ?? "nicht zugewiesen"}${s.groupName ? ` (${s.groupName})` : ""}`, state: s.assigneeName ? "None" : "Warning", icon: "" },
			{ label: "Gemeldet", value: `${waitingSince(s.createdAt)} · ${s.channel && s.channel !== "selfService"
				? `${ticketChannels[s.channel].text}${s.createdByName ? `, aufgenommen von ${s.createdByName}` : ""}` : sources[s.source] ?? s.source}`, state: "None",
			icon: s.channel && s.channel !== "selfService" ? ticketChannels[s.channel].icon : "" },
		];
		if (agent && s.responseDueAt) {
			rows.push({ label: "Erste Antwort", value: firstAnswer ? `erfolgt ${fmt(firstAnswer)}` : `bis ${fmt(s.responseDueAt)}`,
				state: firstAnswer ? (new Date(firstAnswer) <= new Date(s.responseDueAt) ? "Success" : "Warning") : s.slaState === "overdue" && s.dueKind === "response" ? "Error" : "None", icon: "" });
			rows.push({ label: "Lösung bis", value: fmt(s.resolutionDueAt), state: s.slaState === "overdue" && s.dueKind === "resolution" ? "Error"
				: s.slaState === "breached" ? "Warning" : s.slaState === "met" ? "Success" : "None", icon: "" });
		}
		if (t.deviceName) rows.push({ label: "Gerät", value: t.deviceName, state: "None", icon: "" });
		if (t.rating) rows.push({ label: "Bewertung", value: `${"★".repeat(t.rating)}${"☆".repeat(5 - t.rating)}`, state: t.rating >= 4 ? "Success" : t.rating <= 2 ? "Error" : "None", icon: "" });
		return rows;
	}

	private renderTimeline(): void {
		const m = this.model();
		const talkOnly = m.getProperty("/show") === "talk";
		const names = new Map(this.agents.map((a) => [a.id, a.displayName]));
		m.setProperty("/timeline", this.timeline
			.filter((i) => !talkOnly || ["created", "comment", "note"].includes(i.kind))
			.map((i) => {
				const bubble = ["created", "comment", "note"].includes(i.kind);
				return {
					...i, bubble, icon: icons[i.kind] ?? "sap-icon://history", kindText: kindTexts[i.kind] ?? i.kind,
					tone: i.kind, who: i.kind === "note" ? "note" : i.isReporter ? "reporter" : "agent",
					headline: i.kind === "created" ? (i.fromValue && i.fromValue !== "SelfService" ? i.summary : `${i.actorName ?? "Melder"} hat gemeldet`) : i.kind === "note" ? `${i.actorName ?? ""} · interne Notiz` : i.actorName ?? "",
					eventText: ["status", "rating"].includes(i.kind) ? i.text : null,
					mentions: i.mentionUserIds?.length ? `Benachrichtigt: ${i.mentionUserIds.map((id) => names.get(id) ?? "Kollege").join(", ")}` : "",
					attachments: i.attachmentFileIds.map((id) => {
						const f = this.files.get(id);
						return attachmentView({ id, name: f?.originalName ?? "Anhang", contentType: f?.contentType ?? "" });
					}),
				};
			}));
	}

	private scrollToEnd(): void {
		const dom = this.byId("hdScroll")?.getDomRef() as HTMLElement | null;
		if (dom) dom.scrollTop = dom.scrollHeight;
	}

	onShow(): void { this.renderTimeline(); }

	onTab(): void {
		const tab = this.model().getProperty("/tab") as string;
		if (tab === "time") void this.loadTimes();
		if ((tab === "context" || tab === "links") && !this.model().getProperty("/ctx")) void this.loadContext(this.ticket().summary.id);
		if (tab === "talk") setTimeout(() => this.scrollToEnd(), 0);
	}

	private ticket(): HdDetail {
		return this.model().getProperty("/t") as HdDetail;
	}

	/** Melder, Geräte (App-Version, Update-Stand), weitere Tickets, passende Artikel, Systemstatus. */
	private async loadContext(id: string): Promise<void> {
		const m = this.model();
		m.setProperty("/contextBusy", true);
		try {
			const [ctx, health, info] = await Promise.all([
				get<TicketContext>(`tickets/${id}/context`),
				get<{ backup: { severity: string; message: string | null; configured: boolean }; archive: { severity: string; configured: boolean };
					banners: { text: string; severity: string }[] }>("system/health-banner").catch(() => null),
				get<{ serverVersion: string }>("server-info").catch(() => null),
			]);
			if (this.ticket()?.summary.id !== id) return;
			m.setProperty("/ctx", { ...ctx, devices: ctx.devices.map((d) => ({ ...d, line: deviceLine(d) || "–",
				updateText: d.updateState ? `${updateStates[d.updateState]?.text ?? d.updateState}${d.updateState === "recommended" || d.updateState === "required" ? ` (${d.latestVersion ?? "neu"})` : ""}` : "",
				updateTone: d.updateState ? updateStates[d.updateState]?.tone ?? "None" : "None" })) });
			m.setProperty("/lastLoginText", ctx.reporter?.lastLoginAt ? `Zuletzt angemeldet ${waitingSince(ctx.reporter.lastLoginAt)}` : "");
			const sev = (s: string | undefined) => (s === "error" || s === "critical" ? "Error" : s === "warning" ? "Warning" : "Success");
			const system = [{ label: "Server", value: info?.serverVersion ? `Version ${info.serverVersion}` : "erreichbar", state: "Success", icon: "sap-icon://it-host" }];
			if (health) {
				if (health.backup.configured) system.push({ label: "Sicherung", value: health.backup.message ?? "in Ordnung", state: sev(health.backup.severity), icon: "sap-icon://upload-to-cloud" });
				if (health.archive.configured) system.push({ label: "Archiv", value: health.archive.severity === "ok" ? "erreichbar" : "gestört", state: sev(health.archive.severity), icon: "sap-icon://inbox" });
				for (const b of health.banners.slice(0, 3)) system.push({ label: "Hinweis", value: b.text, state: sev(b.severity), icon: "sap-icon://message-warning" });
			}
			m.setProperty("/system", system);
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/contextBusy", false);
		}
	}

	private async loadTimes(): Promise<void> {
		const t = this.ticket();
		if (!t) return;
		const times = await get<TimeEntry[]>(`tickets/${t.summary.id}/time`).catch(() => [] as TimeEntry[]);
		this.model().setProperty("/times", times.map((e) => ({ ...e, duration: formatMinutes(e.minutes), own: e.userId === session.userId })));
		const total = times.reduce((sum, e) => sum + e.minutes, 0);
		this.model().setProperty("/timeTotal", formatMinutes(total));
		this.model().setProperty("/timeText", total ? formatMinutes(total) : "");
	}

	/** Zeile in der Liste nach einer Änderung aktualisieren (ohne die ganze Liste neu zu laden). */
	private patchRow(s: HdSummary): void {
		const m = this.model();
		const items = (m.getProperty("/items") as HdSummary[]).map((t) => (t.id === s.id ? { ...s, hasUnread: false } : t));
		if (!items.some((t) => t.id === s.id)) {
			items.unshift({ ...s, hasUnread: false });
			m.setProperty("/total", Number(m.getProperty("/total") ?? 0) + 1);
		}
		m.setProperty("/items", items);
		this.applyList();
	}

	private async update(body: Record<string, unknown>, message: string): Promise<void> {
		const t = this.ticket();
		const updated = await this.run(() => patch<HdDetail>(`tickets/${t.summary.id}`, { unassign: false, ...body }));
		if (updated) {
			await this.apply(updated);
			this.toast(message);
		} else {
			await this.apply(t); // Auswahl zurücksetzen
		}
	}

	async onQuickStatus(): Promise<void> {
		const status = this.model().getProperty("/edit/status") as string;
		const t = this.ticket();
		if (status === t.summary.status) return;
		if (done(status)) {
			// Lösung ist Pflicht: kurz im Dialog erfassen (Textbausteine als Vorlage).
			const blocks = (this.model().getProperty("/blocks") as { name: string; text: string }[]) ?? [];
			const saved = await openForm<HdDetail>({
				title: status === "resolved" ? "Als gelöst markieren" : "Ticket schließen", width: "34rem", submitText: "Speichern",
				intro: "Die Lösung wird dem Melder im Verlauf angezeigt. Bitte kurz und verständlich beschreiben.",
				initial: { resolution: t.resolution ?? "" },
				fields: [
					...(blocks.length ? [{ key: "block", label: "Textbaustein", type: "select" as const, options: [{ key: "", text: "–" }, ...blocks.map((b, i) => ({ key: String(i), text: b.name }))],
						onChange: (value: unknown, form: FormHandle) => { if (value !== "") form.set("resolution", this.fill(blocks[Number(value)]?.text ?? "")); } }] : []),
					{ key: "resolution", label: "Lösung", type: "textarea", rows: 4, required: true, maxLength: 8000 },
				],
				submit: (v) => patch<HdDetail>(`tickets/${t.summary.id}`, { status, resolution: v.resolution, unassign: false }),
			});
			if (saved) {
				await this.apply(saved);
				this.toast("Ticket gespeichert");
			} else {
				this.model().setProperty("/edit/status", t.summary.status);
			}
			return;
		}
		await this.update({ status }, `Status: ${ticketStatus[status as keyof typeof ticketStatus]?.text ?? status}`);
	}

	async onQuickPriority(): Promise<void> {
		const priority = this.model().getProperty("/edit/priority") as string;
		if (priority !== this.ticket().summary.priority) await this.update({ priority }, "Priorität geändert, Ziele neu berechnet");
	}

	async onQuickCategory(): Promise<void> {
		const category = this.model().getProperty("/edit/category") as string;
		if (category !== this.ticket().summary.category) await this.update({ category }, "Kategorie geändert");
	}

	async onQuickSubcategory(): Promise<void> {
		const sub = this.model().getProperty("/edit/subcategory") as string;
		if ((sub === "none" ? null : sub) === (this.ticket().summary.subcategoryId ?? null)) return;
		await this.update(sub === "none" ? { clearSubcategory: true } : { subcategoryId: sub }, "Unterkategorie geändert");
	}

	async onQuickGroup(): Promise<void> {
		const group = this.model().getProperty("/edit/group") as string;
		if ((group === "none" ? null : group) === (this.ticket().summary.groupId ?? null)) return;
		await this.update(group === "none" ? { clearGroup: true } : { groupId: group }, "Gruppe geändert");
	}

	async onQuickAssignee(): Promise<void> {
		const picked = this.model().getProperty("/edit/assignee") as string;
		const assignee = picked === "none" ? "" : picked;
		const t = this.ticket();
		if ((assignee || null) === t.summary.assigneeUserId) return;
		await this.update(assignee ? { assigneeUserId: assignee } : { unassign: true }, assignee ? "Zugewiesen" : "Zuweisung entfernt");
	}

	async onMine(): Promise<void> {
		this.model().setProperty("/edit/assignee", session.userId);
		await this.onQuickAssignee();
	}

	async onReopen(): Promise<void> {
		const t = this.ticket();
		const updated = await this.run(() => post<HdDetail>(`tickets/${t.summary.id}/reopen`, {}));
		if (updated) await this.apply(updated);
	}

	async onRate(): Promise<void> {
		const t = this.ticket();
		const rate = this.model().getProperty("/rate") as { value: number; comment: string };
		const updated = await this.run(() => post<HdDetail>(`tickets/${t.summary.id}/rating`, { rating: Math.round(rate.value), comment: rate.comment || null }),
			"Danke für Ihre Bewertung");
		if (updated) await this.apply(updated);
	}

	onOpenMerged(): void {
		const t = this.ticket();
		const id = t.summary.mergedIntoTicketId;
		if (id) this.nav(`/it-center/helpdesk/${id}`);
	}

	// ---- Verknüpfen, Zusammenführen, Wissen, Zeit --------------------------------------------------------------------

	/** Andere Tickets zur Auswahl (offene zuerst, ohne das aktuelle). */
	private async otherTicketOptions(): Promise<{ key: string; text: string; additionalText: string }[]> {
		const id = this.ticket().summary.id;
		const list = await get<HdSummary[]>("tickets", { filter: "all", limit: 300, sort: "recent" }).catch(() => [] as HdSummary[]);
		return list.filter((t) => t.id !== id && !t.mergedIntoTicketId)
			.sort((a, b) => Number(done(a.status)) - Number(done(b.status)))
			.map((t) => ({ key: t.id, text: `${t.number} · ${t.title}`, additionalText: ticketStatus[t.status]?.text ?? t.status }));
	}

	async onMerge(): Promise<void> {
		const t = this.ticket();
		const tickets = (await this.otherTicketOptions()).filter((o) => o.additionalText !== ticketStatus.closed.text);
		const merged = await openForm<HdDetail>({
			title: `${t.summary.number} zusammenführen`, width: "36rem", submitText: "Zusammenführen", danger: true,
			intro: "Dieses Ticket wird als Duplikat geschlossen. Der Melder verfolgt das Ziel-Ticket und erhält dort die Antworten. "
				+ "Beschreibung und Anhänge werden als interne Notiz übernommen.",
			fields: [{ key: "target", label: "Weiter bearbeiten in", type: "combo", required: true, options: tickets, placeholder: "Nummer oder Titel eingeben" }],
			submit: (v) => post<HdDetail>(`tickets/${t.summary.id}/merge`, { targetTicketId: v.target }),
		});
		if (merged) {
			this.toast(`Mit ${merged.summary.number} zusammengeführt`);
			this.loadedAt = 0;
			this.nav(`/it-center/helpdesk/${merged.summary.id}`);
		}
	}

	async onLink(): Promise<void> {
		const t = this.ticket();
		const tickets = await this.otherTicketOptions();
		const linked = await openForm<HdDetail>({
			title: "Ticket verknüpfen", width: "36rem", submitText: "Verknüpfen",
			intro: "Verknüpfte Tickets erscheinen bei beiden unter „Verknüpfungen“, z. B. bei gleicher Ursache.",
			fields: [{ key: "other", label: "Ticket", type: "combo", required: true, options: tickets, placeholder: "Nummer oder Titel eingeben" }],
			submit: (v) => post<HdDetail>(`tickets/${t.summary.id}/links`, { otherTicketId: v.other, kind: "related" }),
		});
		if (linked) {
			await this.apply(linked);
			this.model().setProperty("/tab", "links");
		}
	}

	async onUnlink(event: Event): Promise<void> {
		const item = (event as Event<{ listItem: Control }>).getParameter("listItem");
		const link = item.getBindingContext("view")?.getObject() as { ticketId: string; number: string };
		const updated = await this.run(() => del<HdDetail>(`tickets/${this.ticket().summary.id}/links/${link.ticketId}`), `Verknüpfung zu ${link.number} entfernt`);
		if (updated) await this.apply(updated);
	}

	onOpenLinked(event: Event): void {
		const link = (event.getSource() as Control).getBindingContext("view")?.getObject() as { ticketId: string };
		this.nav(`/it-center/helpdesk/${link.ticketId}`);
	}

	onOpenOther(event: Event): void {
		const other = (event.getSource() as Control).getBindingContext("view")?.getObject() as HdSummary;
		this.nav(`/it-center/helpdesk/${other.id}`);
	}

	onDevices(): void { this.nav("/it-center/geraete"); }

	async onLinkArticle(): Promise<void> {
		const t = this.ticket();
		const articles = await get<KnowledgeArticleSummary[]>("helpdesk/articles").catch(() => [] as KnowledgeArticleSummary[]);
		const linked = await openForm<HdDetail>({
			title: "Artikel verknüpfen", width: "36rem", submitText: "Verknüpfen",
			intro: "Veröffentlichte Artikel sieht der Melder im Ticket als Anleitung.",
			fields: [{ key: "article", label: "Artikel", type: "combo", required: true, placeholder: "Titel eingeben",
				options: articles.map((a) => ({ key: a.id, text: a.title, additionalText: a.isPublished ? "" : "Entwurf" })) }],
			submit: (v) => post<HdDetail>(`tickets/${t.summary.id}/articles`, { articleId: v.article }),
		});
		if (linked) await this.apply(linked);
	}

	async onLinkSuggested(event: Event): Promise<void> {
		const article = (event.getSource() as Control).getBindingContext("view")?.getObject() as KnowledgeArticleSummary;
		const updated = await this.run(() => post<HdDetail>(`tickets/${this.ticket().summary.id}/articles`, { articleId: article.id }), "Artikel verknüpft");
		if (updated) {
			await this.apply(updated);
			const ctx = this.model().getProperty("/ctx") as TicketContext | null;
			if (ctx) this.model().setProperty("/ctx/suggestedArticles", ctx.suggestedArticles.filter((a) => a.id !== article.id));
		}
	}

	async onUnlinkArticle(event: Event): Promise<void> {
		const item = (event as Event<{ listItem: Control }>).getParameter("listItem");
		const article = item.getBindingContext("view")?.getObject() as { id: string };
		const updated = await this.run(() => del<HdDetail>(`tickets/${this.ticket().summary.id}/articles/${article.id}`), "Verknüpfung entfernt");
		if (updated) await this.apply(updated);
	}

	onOpenArticle(event: Event): void {
		const article = (event.getSource() as Control).getBindingContext("view")?.getObject() as { id: string };
		this.nav(`/wissen/${article.id}`);
	}

	async onArticleFromTicket(): Promise<void> {
		const t = this.ticket();
		const lastAnswer = [...this.timeline].reverse().find((i) => i.kind === "comment" && !i.isReporter)?.text ?? "";
		const created = await openForm<{ id: string; title: string }>({
			title: "Lösung als Artikel speichern", width: "40rem", submitText: "Speichern und verknüpfen",
			intro: "So steht die Lösung beim nächsten Mal in der Wissensdatenbank bereit und wird beim Melden vorgeschlagen.",
			initial: { title: t.summary.title, body: t.resolution ?? lastAnswer, category: t.summary.category, isPublished: true },
			fields: [
				{ key: "title", label: "Titel", required: true, maxLength: 200 },
				{ key: "body", label: "Anleitung", type: "textarea", rows: 8, required: true, maxLength: 20000 },
				{ key: "category", label: "Kategorie", type: "select", options: [{ key: "", text: "–" }, ...options(ticketCategories)] },
				{ key: "keywords", label: "Suchbegriffe", placeholder: "z. B. drucker, toner, papierstau" },
				{ key: "isPublished", label: "Veröffentlichen (für alle sichtbar)", type: "checkbox" },
			],
			submit: (v) => post<{ id: string; title: string }>("helpdesk/articles", { title: v.title, body: v.body, category: v.category || null,
				keywords: v.keywords || null, isPublished: !!v.isPublished }),
		});
		if (!created) return;
		const updated = await this.run(() => post<HdDetail>(`tickets/${t.summary.id}/articles`, { articleId: created.id }), "Artikel gespeichert und verknüpft");
		if (updated) await this.apply(updated);
	}

	async onAddTime(): Promise<void> {
		const t = this.ticket();
		const saved = await openForm<TimeEntry>({
			title: `Arbeitszeit für ${t.summary.number}`, width: "30rem", submitText: "Erfassen",
			initial: { workDate: new Date().toISOString().slice(0, 10) },
			fields: [
				{ key: "duration", label: "Dauer", required: true, placeholder: "z. B. 30, 1:15 oder 1,5 h",
					validate: (v) => (parseMinutes(String(v ?? "")) ? null : "Bitte eine Dauer wie 30, 1:15 oder 1,5 h angeben.") },
				{ key: "workDate", label: "Datum", type: "date", required: true },
				{ key: "note", label: "Tätigkeit", maxLength: 500, placeholder: "z. B. Drucker vor Ort geprüft" },
			],
			submit: (v) => post<TimeEntry>(`tickets/${t.summary.id}/time`, { minutes: parseMinutes(String(v.duration)), workDate: v.workDate, note: v.note || null }),
		});
		if (saved) {
			this.toast(`${formatMinutes(saved.minutes)} erfasst`);
			this.model().setProperty("/tab", "time");
			await this.apply(await get<HdDetail>(`tickets/${t.summary.id}`));
		}
	}

	async onDeleteTime(event: Event): Promise<void> {
		const item = (event as Event<{ listItem: Control }>).getParameter("listItem");
		const entry = item.getBindingContext("view")?.getObject() as TimeEntry & { own: boolean };
		if (!entry.own) {
			this.toast("Nur eigene Zeitbuchungen lassen sich löschen.");
			return;
		}
		await this.run(() => del(`tickets/${this.ticket().summary.id}/time/${entry.id}`), "Zeitbuchung gelöscht");
		await this.loadTimes();
	}

	// ---- Antworten ---------------------------------------------------------------------------------------------------

	private fill(text: string): string {
		const t = this.ticket();
		return fillPlaceholders(text, { reporter: t.summary.reporterDisplayName, ticket: t.summary.number, title: t.summary.title,
			agent: this.agents.find((a) => a.id === session.userId)?.displayName ?? null });
	}

	private addFiles(files: Iterable<File>): void {
		if (!this.model().getProperty("/canUpload")) return;
		const pending = this.model().getProperty("/pending") as { name: string; file: File }[];
		const added = [...files].map((file, i) => ({
			name: file.name && file.name !== "image.png" ? file.name : `Bildschirmfoto-${pending.length + i + 1}.png`, file }));
		this.model().setProperty("/pending", [...pending, ...added].slice(0, 10));
	}

	/** Antwortfeld: Dateien hineinziehen, Bildschirmfotos mit Strg+V, Senden mit Strg+Enter. */
	private wireComposer(): void {
		const dom = this.byId("composer")?.getDomRef() as HTMLElement | null;
		if (!dom || dom.dataset.eoWired) return;
		dom.dataset.eoWired = "1";
		dom.addEventListener("dragover", (e) => {
			if (!e.dataTransfer?.types.includes("Files")) return;
			e.preventDefault();
			dom.classList.add("eoDropActive");
		});
		dom.addEventListener("dragleave", (e) => { if (!dom.contains(e.relatedTarget as Node)) dom.classList.remove("eoDropActive"); });
		dom.addEventListener("drop", (e) => {
			dom.classList.remove("eoDropActive");
			if (!e.dataTransfer?.files.length) return;
			e.preventDefault();
			this.addFiles(e.dataTransfer.files);
		});
		dom.addEventListener("paste", (e) => {
			const images = [...(e.clipboardData?.files ?? [])].filter((f) => f.type.startsWith("image/"));
			if (images.length) {
				e.preventDefault();
				this.addFiles(images);
			}
		});
		dom.addEventListener("keydown", (e) => {
			if (this.onMentionKey(e)) return;
			if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
				e.preventDefault();
				void this.onComment();
			}
		}, true);
		// Erwähnen: „@“ und Namensanfang öffnen die Auswahl; ein Klick in die Auswahl lässt die Schreibmarke im Antwortfeld.
		dom.addEventListener("input", () => this.updateMention());
		dom.addEventListener("click", (e) => { if ((e.target as HTMLElement).tagName === "TEXTAREA") this.updateMention(); });
		dom.addEventListener("keyup", (e) => { if (["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key)) this.updateMention(); });
		dom.addEventListener("mousedown", (e) => { if ((e.target as HTMLElement).closest(".eoHdMentionPop")) e.preventDefault(); });
		dom.addEventListener("focusout", (e) => {
			if (!dom.contains(e.relatedTarget as Node)) setTimeout(() => this.closeMention(), 150);
		});
	}

	private replyArea(): HTMLTextAreaElement | null {
		return (this.byId("reply")?.getDomRef() as HTMLElement | null)?.querySelector("textarea") ?? null;
	}

	/** Auswahl der Kolleginnen und Kollegen zum angefangenen „@Name“ an der Schreibmarke. */
	private updateMention(): void {
		const m = this.model();
		const area = this.replyArea();
		if (!m.getProperty("/agent") || !area) return;
		const caret = area.selectionStart ?? area.value.length;
		const hit = area.selectionStart === area.selectionEnd ? mentionQuery(area.value, caret) : null;
		const candidates = hit ? mentionCandidates(hit.query, this.agents.filter((a) => a.id !== session.userId)) : [];
		this.mentionAt = hit && candidates.length ? { start: hit.start, caret } : null;
		const index = Math.min(Number(m.getProperty("/mention/index") ?? 0), Math.max(0, candidates.length - 1));
		m.setProperty("/mention", { open: !!this.mentionAt, index, items: candidates.map((a, i) => ({ id: a.id, name: a.displayName,
			hint: `${a.openTickets} offen`, active: i === index })) });
	}

	private closeMention(): void {
		this.mentionAt = null;
		this.model().setProperty("/mention", { open: false, index: 0, items: [] });
	}

	/** Pfeiltasten, Eingabe/Tab und Esc in der Auswahl; true = Taste verbraucht. */
	private onMentionKey(e: KeyboardEvent): boolean {
		const m = this.model();
		const items = (m.getProperty("/mention/items") ?? []) as { active: boolean }[];
		if (!this.mentionAt || !items.length) return false;
		const index = Number(m.getProperty("/mention/index") ?? 0);
		const move = (to: number): void => {
			const next = (to + items.length) % items.length;
			m.setProperty("/mention/index", next);
			m.setProperty("/mention/items", items.map((item, i) => ({ ...item, active: i === next })));
		};
		if (e.key === "ArrowDown" || e.key === "ArrowUp") move(index + (e.key === "ArrowDown" ? 1 : -1));
		else if ((e.key === "Enter" && !e.ctrlKey && !e.metaKey) || e.key === "Tab") this.pickMention(index);
		else if (e.key === "Escape") this.closeMention();
		else return false;
		e.preventDefault();
		e.stopPropagation();
		return true;
	}

	onMentionPick(event: Event): void {
		const ctx = (event.getSource() as Control).getBindingContext("view");
		const path = ctx?.getPath() ?? "";
		this.pickMention(Number(path.slice(path.lastIndexOf("/") + 1)));
	}

	private pickMention(index: number): void {
		const m = this.model();
		const item = (m.getProperty("/mention/items") as { id: string; name: string }[])[index];
		const area = this.replyArea();
		const at = this.mentionAt;
		if (!item || !area || !at) return;
		const result = insertMention(area.value, at.start, at.caret, item.name);
		m.setProperty("/reply", result.text);
		area.value = result.text;
		area.focus();
		area.setSelectionRange(result.caret, result.caret);
		const mentions = m.getProperty("/mentions") as string[];
		if (!mentions.includes(item.id)) m.setProperty("/mentions", [...mentions, item.id]);
		this.closeMention();
	}

	onAttach(): void {
		const input = document.createElement("input");
		input.type = "file";
		input.multiple = true;
		input.accept = "image/*,.pdf,.txt,.log,.zip,.doc,.docx,.xlsx";
		input.addEventListener("change", () => this.addFiles(input.files ?? []));
		input.click();
	}

	onRemovePending(e: Event): void {
		const item = (e.getSource() as Control).getBindingContext("view")?.getObject();
		const pending = this.model().getProperty("/pending") as object[];
		this.model().setProperty("/pending", pending.filter((p) => p !== item));
	}

	onTextBlock(event: Menu$ItemSelectedEvent): void {
		const text = String((event.getParameter("item") as MenuItem).data("text") ?? "");
		const m = this.model();
		const reply = String(m.getProperty("/reply") ?? "");
		m.setProperty("/reply", (reply ? `${reply.trimEnd()}\n\n` : "") + this.fill(text));
	}

	async onComment(): Promise<void> {
		const m = this.model();
		const t = this.ticket();
		const pending = m.getProperty("/pending") as { name: string; file: File }[];
		const text = String(m.getProperty("/reply") ?? "");
		if (!text.trim() && !pending.length) return;
		const agent = m.getProperty("/agent") as boolean;
		const mentions = agent ? [...new Set([...(m.getProperty("/mentions") as string[]), ...mentionedAgents(text, this.agents)])].filter((id) => id !== session.userId) : [];
		const updated = await this.run(async () => {
			const attachmentFileIds: string[] = [];
			for (const p of pending) attachmentFileIds.push(await uploadLoose(p.file));
			return post<HdDetail>(`tickets/${t.summary.id}/comments`, { text, isInternal: m.getProperty("/internal"), attachmentFileIds,
				mentionUserIds: mentions.length ? mentions : undefined });
		});
		if (updated) {
			m.setProperty("/reply", "");
			m.setProperty("/internal", false);
			m.setProperty("/pending", []);
			m.setProperty("/mentions", []);
			await this.apply(updated);
		}
	}

	/** Bilder und Videos im Detailfenster (Blättern innerhalb derselben Anhangsliste), andere Dateien im neuen Tab. */
	onAttachment(event: Event): void {
		const ctx = (event.getSource() as Control).getBindingContext("view");
		if (!ctx) return;
		const path = ctx.getPath();
		const list = (ctx.getModel().getProperty(path.slice(0, path.lastIndexOf("/"))) ?? []) as AttachmentView[];
		openAttachment(list, (ctx.getObject() as AttachmentView).id);
	}

	// ---- Neues Ticket ------------------------------------------------------------------------------------------------

	/**
	 * Meldung aufnehmen: Die IT legt nach Anruf, E-Mail oder Besuch ein Ticket im Namen des Melders an. Der Melder sieht es
	 * unter „Meine Tickets“ und erhält einen Hinweis; ohne Zugang genügt der Name.
	 */
	async onNewOnBehalf(): Promise<void> {
		const reporters = await get<HelpdeskReporter[]>("helpdesk/reporters", { limit: 200 }).catch(() => [] as HelpdeskReporter[]);
		const files: File[] = [];
		const list = new Text({ text: "" });
		const subOptions = (category: string) => [{ key: "", text: "–" },
			...(this.hdOptions?.categories.find((c) => c.key === category)?.subcategories ?? []).map((sub) => ({ key: sub.id, text: sub.name }))];
		const anySubs = !!this.hdOptions?.categories.some((c) => c.subcategories.length);
		const channels = (["phone", "email", "inPerson"] as TicketChannel[]).map((key) => ({ key, text: ticketChannels[key].text }));
		const assign = [{ key: session.userId ?? "", text: "Mir (gleich in Bearbeitung)" }, { key: "", text: "Automatisch nach Regeln" },
			...this.agents.filter((a) => a.id !== session.userId).map((a) => ({ key: a.id, text: `${a.displayName} (${a.openTickets} offen)` }))];
		const saved = await openForm<HdDetail>({
			title: "Meldung aufnehmen", width: "38rem", submitText: "Ticket anlegen",
			intro: "Für Anrufe, E-Mails und Besuche: Das Ticket gehört dem Melder, er sieht den Verlauf und erhält die Antworten.",
			initial: { channel: "phone", category: "other", priority: "normal", assignee: session.userId ?? "", subcategory: "", reporterBlocked: false },
			fields: [
				{ key: "channel", label: "Eingang", type: "segmented", options: channels },
				{ key: "reporter", label: "Melder", type: "combo", placeholder: "Name, Benutzername oder E-Mail",
					options: reporters.map((r) => ({ key: r.id, text: r.displayName, additionalText: r.email ?? r.phone ?? r.userName })) },
				{ key: "reporterName", label: "Oder Name (ohne Zugang zu EstrichONE)", maxLength: 200, placeholder: "z. B. Aushilfe im Lager",
					validate: (v, values) => (values.reporter || String(v ?? "").trim() ? null : "Bitte den Melder auswählen oder einen Namen eintragen.") },
				{ key: "title", label: "Kurzbeschreibung", required: true, maxLength: 200, placeholder: "z. B. Tablet zeigt keine Aufträge" },
				{ key: "category", label: "Kategorie", type: "select", required: true, options: options(ticketCategories),
					onChange: (value, form) => {
						const select = form.control("subcategory") as Select | undefined;
						if (!select) return;
						select.destroyItems();
						for (const o of subOptions(String(value))) select.addItem(new Item({ key: o.key, text: o.text }));
						select.setSelectedKey("");
					} },
				...(anySubs ? [{ key: "subcategory", label: "Unterkategorie", type: "select" as const, options: subOptions("other") }] : []),
				{ key: "priority", label: "Priorität", type: "select", required: true, options: options(ticketPriorities) },
				{ key: "description", label: "Beschreibung", type: "textarea", rows: 5, maxLength: 8000,
					placeholder: "Was schildert der Melder? Fehlermeldung, Gerät, seit wann, was schon versucht wurde …" },
				{ key: "reporterBlocked", label: "Melder kann deswegen nicht arbeiten", type: "checkbox" },
				{ key: "assignee", label: "Zuweisen", type: "select", options: assign },
			],
			extra: () => (this.has("files.upload") ? [new Button({
				text: "Anhang (z. B. aus der E-Mail)", icon: "sap-icon://attachment",
				press: () => {
					const input = document.createElement("input");
					input.type = "file";
					input.multiple = true;
					input.accept = "image/*,.pdf,.txt,.log,.zip,.doc,.docx,.xlsx,.eml,.msg";
					input.addEventListener("change", () => { files.push(...(input.files ?? [])); files.splice(10); list.setText(files.map((f) => f.name).join(", ")); });
					input.click();
				},
			}), list] : []),
			submit: async (v) => {
				const attachmentFileIds: string[] = [];
				for (const f of files) attachmentFileIds.push(await uploadLoose(f));
				return post<HdDetail>("tickets/on-behalf", {
					id: uuidv7(), title: v.title, description: v.description || null, category: v.category, priority: v.priority,
					subcategoryId: v.subcategory || null, channel: v.channel, source: "web", reporterUserId: v.reporter || null,
					reporterName: v.reporter ? null : String(v.reporterName ?? "").trim() || null, reporterBlocked: !!v.reporterBlocked,
					assigneeUserId: v.assignee || null, attachmentFileIds,
				});
			},
		});
		if (saved) {
			this.toast(`Ticket ${saved.summary.number} für ${saved.summary.reporterDisplayName ?? "den Melder"} angelegt`);
			this.patchRow(saved.summary);
			this.nav(`/it-center/helpdesk/${saved.summary.id}`);
		}
	}

	async onNew(): Promise<void> {
		const files: File[] = [];
		const canUpload = this.has("files.upload");
		const list = new Text({ text: "" });
		const show = (): void => { list.setText(files.map((f) => f.name).join(", ")); };
		const add = (added: Iterable<File>): void => { files.push(...added); files.splice(10); show(); };
		const paste = (e: ClipboardEvent): void => {
			const images = [...(e.clipboardData?.files ?? [])].filter((f) => f.type.startsWith("image/"));
			if (images.length) add(images.map((f, i) => new File([f], f.name && f.name !== "image.png" ? f.name : `Bildschirmfoto-${files.length + i + 1}.png`, { type: f.type })));
		};
		// Passende Anleitungen aus der Wissensdatenbank, sobald Titel oder Beschreibung feststehen.
		const suggestions = new VBox({ visible: false });
		suggestions.addStyleClass("eoHdSuggestBox");
		let lastQuery = "";
		const suggest = async (form: FormHandle): Promise<void> => {
			const q = `${String(form.get("title") ?? "")} ${String(form.get("description") ?? "")}`.trim();
			if (q.length < 6 || q === lastQuery || !this.hdOptions?.suggestArticles) return;
			lastQuery = q;
			const found = await get<KnowledgeArticleSummary[]>("helpdesk/articles/suggest", { q, category: String(form.get("category") ?? "") || undefined })
				.catch(() => [] as KnowledgeArticleSummary[]);
			suggestions.destroyItems();
			if (!found.length) { suggestions.setVisible(false); return; }
			suggestions.addItem(new Text({ text: "Vielleicht hilft schon eine dieser Anleitungen:" }).addStyleClass("eoStrong"));
			for (const a of found) {
				suggestions.addItem(new Link({ text: a.title, wrapping: true, press: () => window.open(`/wissen/${a.id}`, "_blank", "noopener") }));
			}
			suggestions.setVisible(true);
		};
		const subOptions = (category: string) => [{ key: "", text: "–" },
			...(this.hdOptions?.categories.find((c) => c.key === category)?.subcategories ?? []).map((s) => ({ key: s.id, text: s.name }))];
		const anySubs = !!this.hdOptions?.categories.some((c) => c.subcategories.length);
		if (canUpload) document.addEventListener("paste", paste);
		try {
			const saved = await openForm<HdDetail>({
				title: "Neues IT-Ticket", width: "36rem",
				intro: "Bitte beschreiben Sie das Problem möglichst genau. Die IT antwortet im Verlauf des Tickets.",
				initial: { category: "other", priority: "normal", reporterBlocked: false, subcategory: "" },
				fields: [
					{ key: "title", label: "Kurzbeschreibung", required: true, maxLength: 200, placeholder: "z. B. Drucker im Büro druckt nicht",
						onChange: (_v, form) => void suggest(form) },
					{ key: "category", label: "Kategorie", type: "select", required: true, options: options(ticketCategories),
						onChange: (value, form) => {
							const select = form.control("subcategory") as Select | undefined;
							if (!select) return;
							select.destroyItems();
							for (const o of subOptions(String(value))) select.addItem(new Item({ key: o.key, text: o.text }));
							select.setSelectedKey("");
							void suggest(form);
						} },
					...(anySubs ? [{ key: "subcategory", label: "Unterkategorie", type: "select" as const, options: subOptions("other") }] : []),
					{ key: "priority", label: "Priorität", type: "select", required: true, options: options(ticketPriorities) },
					{ key: "description", label: "Beschreibung", type: "textarea", rows: 6, maxLength: 8000,
						placeholder: "Was ist passiert? Seit wann? Fehlermeldung? Was haben Sie schon versucht?", onChange: (_v, form) => void suggest(form) },
					{ key: "reporterBlocked", label: "Ich kann deswegen nicht arbeiten", type: "checkbox" },
				],
				extra: () => [suggestions, ...(canUpload ? [new Button({
					text: "Anhang oder Bildschirmfoto", icon: "sap-icon://attachment",
					tooltip: "Dateien wählen (Bildschirmfotos auch mit Strg+V einfügen)",
					press: () => {
						const input = document.createElement("input");
						input.type = "file";
						input.multiple = true;
						input.accept = "image/*,.pdf,.txt,.log,.zip,.doc,.docx,.xlsx";
						input.addEventListener("change", () => add(input.files ?? []));
						input.click();
					},
				}), list] : [])],
				submitText: "Ticket anlegen",
				submit: async (v) => {
					const attachmentFileIds: string[] = [];
					for (const f of files) attachmentFileIds.push(await uploadLoose(f));
					return post<HdDetail>("tickets", {
						id: uuidv7(), title: v.title, description: v.description || null, category: v.category, priority: v.priority,
						subcategoryId: v.subcategory || null, source: "web", reporterBlocked: !!v.reporterBlocked, attachmentFileIds,
					});
				},
			});
			if (saved) {
				this.toast(`Ticket ${saved.summary.number} angelegt`);
				this.patchRow(saved.summary);
				this.nav(`/it-center/helpdesk/${saved.summary.id}`);
			}
		} finally {
			document.removeEventListener("paste", paste);
		}
	}
}

/** Lesbare Namen der Diagnosefelder (14, Abschnitt 3). */
const diagnosticLabels: Record<string, string> = {
	appVersion: "App-Version", build: "Build", platform: "Plattform", osVersion: "Betriebssystem", deviceModel: "Gerätemodell", deviceKind: "Gerätetyp",
	deviceId: "Geräte-ID", network: "Netzwerk", syncOldestPending: "Ältester offener Vorgang", syncLastSuccess: "Letzter Abgleich",
	syncPendingOperations: "Offene Vorgänge", syncPendingUploads: "Offene Uploads", syncFailedUploads: "Fehlgeschlagene Uploads",
	syncPendingBytes: "Ausstehende Datenmenge (Byte)", syncConflicts: "Konflikte", freeStorageBytes: "Freier Speicher (Byte)", errorCodes: "Fehlercodes",
	log: "Protokoll",
};
