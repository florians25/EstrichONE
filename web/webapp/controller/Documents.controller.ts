import type Tree from "sap/m/Tree";
import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type { RouteMatch } from "../util/router";
import { get } from "../util/api";
import { documentCategories } from "../util/labels";
import { fileIcon } from "../util/documents";
import { dateTime } from "../util/format";
import { approvalStatus, cancelApproval, decideApproval, requestApproval, type ApprovalResponse, type ApprovalStatus } from "../util/approvals";
import type { BauaktePage, FileResponse } from "../util/models";

interface DocumentListItem {
	file: FileResponse;
	bauakteNumber: string | null;
	bauakteTitle: string;
	uploadedByName: string;
	versionCount: number;
	approvalStatus: ApprovalStatus | null;
	approvalAssignee: string | null;
}

interface SearchResult {
	items: DocumentListItem[];
	facets: { total: number; categories: Record<string, number>; pendingApprovals: number };
}

/**
 * Zentrale Dokumentverwaltung: Ordner (Dokumentarten) über alle Bauakten, Suche/Filter, Fassungen, Freigabestand;
 * Reiter „Freigaben“ mit „Warten auf meine Freigabe“ und „Von mir angefragt“.
 * @namespace estrichone.controller
 */
interface TreeNode {
	key: string;
	text: string;
	icon: string;
	count: number | null;
	category: string | null;
	bauakteId: string | null;
	customerAkten?: string[];
	nodes: TreeNode[];
}

interface DocumentTreeCustomer {
	customerId: string | null;
	name: string;
	bauakten: { id: string; number: string | null; title: string; categories: Record<string, number> }[];
}

export default class Documents extends BaseController {
	private timer: ReturnType<typeof setTimeout> | undefined;

	onInit(): void {
		this.local({
			tab: "ablage", search: "", bauakteId: "", approval: "", sort: "newest", category: "", busy: false,
			items: [], folders: [], tree: [], node: "", customerAkten: null, akten: [], facets: { total: 0, categories: {}, pendingApprovals: 0 }, mine: [], requested: [],
		});
	}

	onRouteMatched(match: RouteMatch): void {
		const m = this.model();
		m.setProperty("/tab", match.query.get("reiter") === "freigaben" ? "freigaben" : "ablage");
		m.setProperty("/category", match.query.get("ordner") ?? "");
		m.setProperty("/approval", match.query.get("freigabe") ?? "");
		void this.loadAkten();
		void this.loadTree();
		void this.load();
		void this.loadApprovals();
	}

	onTab(e: Event): void {
		this.setQuery({ reiter: String(e.getParameter("key" as never)) === "freigaben" ? "freigaben" : null });
	}

	onRefresh(): void {
		void this.load();
		void this.loadApprovals();
	}

	/** Ordnerbaum Kunde → Bauakte → Dokumentart: setzt die Filter (Kunde = alle seine Bauakten). */
	onTreeSelect(e: Event): void {
		const item = e.getParameter("listItem" as never) as Control;
		const node = item.getBindingContext("view")?.getObject() as TreeNode;
		const m = this.model();
		m.setProperty("/node", node.key);
		m.setProperty("/category", node.category ?? "");
		m.setProperty("/bauakteId", node.bauakteId ?? "");
		m.setProperty("/customerAkten", node.customerAkten ?? null);
		void this.load();
	}

	private async loadTree(): Promise<void> {
		const customers = await get<DocumentTreeCustomer[]>("documents/tree").catch(() => [] as DocumentTreeCustomer[]);
		const sum = (c: Record<string, number>) => Object.values(c).reduce((a, b) => a + b, 0);
		const tree: TreeNode[] = [{ key: "", text: "Alle Dokumente", icon: "sap-icon://documents", count: null, category: null, bauakteId: null, nodes: [] }];
		for (const c of customers) {
			const akten = c.bauakten.filter((b) => sum(b.categories) > 0);
			if (!akten.length) continue;
			tree.push({
				key: `c:${c.customerId ?? "none"}`, text: c.name, icon: "sap-icon://customer", count: akten.reduce((n, b) => n + sum(b.categories), 0),
				category: null, bauakteId: null, customerAkten: akten.map((b) => b.id),
				nodes: akten.map((b) => ({
					key: `b:${b.id}`, text: `${b.number ?? "neu"} · ${b.title}`, icon: "sap-icon://folder-full", count: sum(b.categories), category: null, bauakteId: b.id,
					nodes: Object.entries(documentCategories).filter(([key]) => (b.categories[key] ?? 0) > 0).map(([key, cat]) => ({
						key: `b:${b.id}:${key}`, text: cat.text, icon: cat.icon, count: b.categories[key], category: key, bauakteId: b.id, nodes: [],
					})),
				})),
			});
		}
		this.model().setProperty("/tree", tree);
		// Überschaubar: Kunden mit ihren Bauakten aufgeklappt, bei wenigen Kunden auch die Dokumentarten.
		setTimeout(() => (this.byId("dmsTree") as Tree | undefined)?.expandToLevel(customers.length <= 5 ? 2 : 1), 0);
	}

	onFilter(): void {
		this.model().setProperty("/customerAkten", null);
		void this.load();
	}
	onSearch(): void { void this.load(); }
	onSearchLive(): void {
		clearTimeout(this.timer);
		this.timer = setTimeout(() => void this.load(), 300);
	}

	private async loadAkten(): Promise<void> {
		const page = await get<BauaktePage>("bauakten", { limit: 200 }).catch(() => null);
		this.model().setProperty("/akten", [{ key: "", text: "Alle Bauakten" },
			...(page?.items ?? []).map((b) => ({ key: b.id, text: `${b.number ?? "neu"} · ${b.title}` }))]);
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const category = String(m.getProperty("/category") ?? "");
		const result = await this.run(() => get<SearchResult>("documents", {
			search: String(m.getProperty("/search") ?? "").trim(), category, bauakteId: m.getProperty("/bauakteId") || null,
			approval: m.getProperty("/approval") || null, sort: m.getProperty("/sort"),
		}));
		m.setProperty("/busy", false);
		if (!result) return;
		m.setProperty("/facets", result.facets);
		const restrict = m.getProperty("/customerAkten") as string[] | null;
		if (restrict) result.items = result.items.filter((i) => restrict.includes(i.file.bauakteId ?? ""));
		m.setProperty("/folders", [
			{ key: "", text: "Alle Dokumente", icon: "sap-icon://documents", count: result.facets.total, selected: !category },
			...Object.entries(documentCategories).map(([key, c]) => ({
				key, text: c.text, icon: c.icon, count: result.facets.categories[key] ?? 0, selected: category === key,
			})),
		]);
		m.setProperty("/items", result.items.map((i) => ({
			...i,
			icon: fileIcon(i.file.contentType, i.file.originalName),
			akteText: `${i.bauakteNumber ?? "neu"} · ${i.bauakteTitle}`,
			categoryText: documentCategories[(i.file.category ?? "other") as keyof typeof documentCategories]?.text ?? "Sonstiges",
			approvalText: i.approvalStatus ? approvalStatus[i.approvalStatus].text : "",
			approvalState: i.approvalStatus ? approvalStatus[i.approvalStatus].state : "None",
			openHref: `/bauakten/${i.file.bauakteId}/datei/${i.file.id}?abschnitt=dokumente`,
		})));
	}

	private async loadApprovals(): Promise<void> {
		const [mine, requested] = await Promise.all([
			get<ApprovalResponse[]>("approvals", { scope: "mine", status: "pending" }).catch(() => [] as ApprovalResponse[]),
			get<ApprovalResponse[]>("approvals", { scope: "requested", status: "all", limit: 50 }).catch(() => [] as ApprovalResponse[]),
		]);
		const m = this.model();
		m.setProperty("/mine", mine.map((a) => ({ ...a, requestText: `angefragt von ${a.requestedByName} am ${dateTime(a.requestedAt)}${a.dueDate ? ` · bis ${a.dueDate.split("-").reverse().join(".")}` : ""}` })));
		m.setProperty("/requested", requested.map((a) => ({ ...a, statusText: approvalStatus[a.status].text, statusState: approvalStatus[a.status].state })));
	}

	private row<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	onDownload(e: Event): void {
		const item = this.row<DocumentListItem>(e);
		const a = document.createElement("a");
		a.href = `/api/v1/files/${item.file.id}/content`;
		a.download = item.file.originalName;
		a.click();
	}

	async onRequest(e: Event): Promise<void> {
		const item = this.row<DocumentListItem>(e);
		if (await requestApproval(item.file)) {
			this.toast("Freigabe angefordert");
			this.onRefresh();
		}
	}

	async onApprove(e: Event): Promise<void> {
		if (await decideApproval(this.row<ApprovalResponse>(e), true)) {
			this.toast("Dokument freigegeben");
			this.onRefresh();
		}
	}

	async onReject(e: Event): Promise<void> {
		if (await decideApproval(this.row<ApprovalResponse>(e), false)) {
			this.toast("Freigabe abgelehnt");
			this.onRefresh();
		}
	}

	async onCancel(e: Event): Promise<void> {
		const a = this.row<ApprovalResponse>(e);
		if (!(await this.confirm(`Freigabeanfrage für „${a.fileName}“ zurückziehen?`, { action: "Zurückziehen" }))) return;
		if (await this.run(() => cancelApproval(a))) {
			this.toast("Anfrage zurückgezogen");
			void this.loadApprovals();
			void this.load();
		}
	}
}
