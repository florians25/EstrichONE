import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Select from "sap/m/Select";
import type HBox from "sap/m/HBox";
import Item from "sap/ui/core/Item";
import { get } from "../../util/api";
import { customerInput } from "../../util/forms";
import { address, dateTime } from "../../util/format";
import { bauakteStatus } from "../../util/labels";
import type { BauakteListItem, BauaktePage, BauakteStatus } from "../../util/models";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";
import { openBauakteWizard } from "../../util/bauakteWizard";

/** Bauaktensuche: Volltext, Status, Zuständig, Kunde; seitenweise vom Server. @namespace estrichone.controller.bauakte */
export default class List extends BaseController {
	private customer: ReturnType<typeof customerInput> | null = null;
	private lastQuery = "";

	onInit(): void {
		this.local({ items: [] as BauakteListItem[], cursor: null as string | null, busy: false, search: "", status: "", statusKey: "all",
			responsible: "", customer: "", countText: "" });
	}

	async show(match: RouteMatch): Promise<void> {
		const q = match.query;
		const m = this.model();
		m.setProperty("/search", q.get("search") ?? "");
		m.setProperty("/status", q.get("status") ?? "");
		m.setProperty("/statusKey", q.get("status") || "all");
		m.setProperty("/responsible", q.get("zustaendig") ?? "");
		m.setProperty("/customer", q.get("kunde") ?? "");
		void this.fillPeople();
		this.ensureCustomer(q.get("kunde"));
		const neu = q.get("neu");
		if (neu === "bauakte" || neu === "anfrage") {
			this.setQuery({ neu: null });
			void this.create(neu === "anfrage" ? "inquiry" : "planned");
		}
		const key = ["search", "status", "zustaendig", "kunde"].map((k) => q.get(k) ?? "").join("|");
		if (key !== this.lastQuery || match.route.name === "bauakten" || m.getProperty("/items").length === 0) {
			this.lastQuery = key;
			await this.load(false);
		}
	}

	private async fillPeople(): Promise<void> {
		const people = await session.people();
		const select = this.v().byId("listResponsible") as Select;
		if (select.getItems().length) return;
		select.addItem(new Item({ key: "", text: "Alle" }));
		for (const p of [...people].sort((a, b) => a.displayName.localeCompare(b.displayName))) {
			select.addItem(new Item({ key: p.id, text: p.displayName }));
		}
		select.setSelectedKey(this.model().getProperty("/responsible"));
	}

	private ensureCustomer(id: string | null): void {
		if (this.customer) return;
		this.customer = customerInput(this.createId("listCustomer") as string, id, undefined, (value) => {
			this.model().setProperty("/customer", value ?? "");
			this.applyFilters();
		});
		this.customer.control.setPlaceholder("Kunde: alle");
		(this.v().byId("listCustomerHost") as HBox).addItem(this.customer.control);
	}

	private async load(more: boolean): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const page = await get<BauaktePage>("bauakten", {
				limit: 50, status: m.getProperty("/status") || undefined, search: m.getProperty("/search") || undefined,
				cursor: more ? m.getProperty("/cursor") : undefined, responsibleUserId: m.getProperty("/responsible") || undefined,
				customerId: m.getProperty("/customer") || undefined,
			});
			const items = more ? [...m.getProperty("/items"), ...page.items] : page.items;
			m.setProperty("/items", items);
			m.setProperty("/cursor", page.nextCursor);
			m.setProperty("/countText", `${items.length}${page.nextCursor ? "+" : ""} Bauakten`);
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private applyFilters(): void {
		const m = this.model();
		this.nav(`/bauakten?${new URLSearchParams(Object.entries({
			search: m.getProperty("/search"), status: m.getProperty("/status"), zustaendig: m.getProperty("/responsible"), kunde: m.getProperty("/customer"),
		}).filter(([, v]) => !!v) as [string, string][]).toString()}`.replace(/\?$/, ""), true);
	}

	onSearch(): void {
		this.applyFilters();
	}

	onFilter(): void {
		this.applyFilters();
	}

	onStatus(event: Event): void {
		const key = String((event as unknown as { getParameter(n: string): unknown }).getParameter("key"));
		this.model().setProperty("/status", key === "all" ? "" : key);
		this.applyFilters();
	}

	onReset(): void {
		this.customer?.write(null);
		this.nav("/bauakten", true);
	}

	onRefresh(): void {
		void this.load(false);
	}

	onMore(): void {
		void this.load(true);
	}

	onOpen(event: Event): void {
		const id = (event.getSource() as Control).getBindingContext("view")?.getProperty("id") as string;
		this.nav(`/bauakten/${id}`);
	}

	onMap(): void {
		this.nav("/bauakten/karte");
	}

	onNew(): void {
		void this.create("planned");
	}

	onNewInquiry(): void {
		void this.create("inquiry");
	}

	private async create(status: Extract<BauakteStatus, "planned" | "inquiry">): Promise<void> {
		const created = await openBauakteWizard({ status });
		if (created) this.nav(`/bauakten/${created.id}`);
	}

	/** Geladene Treffer als CSV (Excel, Semikolon, UTF-8 mit BOM). */
	onExport(): void {
		const rows = (this.model().getProperty("/items") as BauakteListItem[]).map((b) => [
			b.number ?? "", b.title, address(b), session.name(b.responsibleUserId), bauakteStatus[b.status]?.text ?? b.status, dateTime(b.updatedAt),
		]);
		const csv = [["Nummer", "Bauvorhaben", "Baustelle", "Zuständig", "Status", "Geändert"], ...rows]
			.map((r) => r.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(";")).join("\r\n");
		const url = URL.createObjectURL(new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8" }));
		const a = document.createElement("a");
		a.href = url;
		a.download = `Bauakten-${new Date().toISOString().slice(0, 10)}.csv`;
		a.click();
		setTimeout(() => URL.revokeObjectURL(url), 1000);
	}
}
