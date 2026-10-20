import BaseController from "../BaseController";
import EventBus from "sap/ui/core/EventBus";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get } from "../../util/api";
import { isoDay } from "../../util/days";
import { quantity } from "../../util/format";
import type { OrderResponse, SupplierResponse } from "../../util/models";
import { day, deliveryAddress, orderTotal } from "../../util/orders";
import { openOrderDetail, openOrderEditor } from "../../util/orderDialogs";
import type { RouteMatch } from "../../util/router";

/** Materialbestellungen: Kennzahlen, Ansichten, Bestellschein, Status. @namespace estrichone.controller.materials */
export default class Orders extends BaseController {
	onInit(): void {
		this.local({ items: [], view: "open", supplier: "", suppliers: [], search: "", busy: false, kpis: [] });
		EventBus.getInstance().subscribe("estrichone", "ordersChanged", () => void this.load());
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const suppliers = await get<SupplierResponse[]>("suppliers", { includeInactive: true }).catch(() => []);
		this.model().setProperty("/suppliers", [{ key: "", text: "Alle Lieferanten" }, ...suppliers.map((s) => ({ key: s.id, text: s.name }))]);
		await this.load();
		const id = match.query.get("bestellung");
		if (id) {
			this.setQuery({ bestellung: null });
			void openOrderDetail(id, this.has("materials.manage"));
		}
	}

	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		const view = m.getProperty("/view") as string;
		const today = isoDay(new Date());
		m.setProperty("/busy", true);
		try {
			const [orders, open, drafts] = await Promise.all([
				get<OrderResponse[]>("material-orders", {
					open: view === "open" || undefined, status: ["draft", "delivered", "cancelled"].includes(view) ? view : undefined,
					supplierId: m.getProperty("/supplier") || undefined, search: m.getProperty("/search") || undefined,
				}),
				get<OrderResponse[]>("material-orders", { open: true }), get<OrderResponse[]>("material-orders", { status: "draft" }),
			]);
			m.setProperty("/items", orders.map((o) => ({
				...o, deliveryText: o.deliveryDate ? day(o.deliveryDate) : "nach Absprache", address: deliveryAddress(o) || "–", totalText: orderTotal(o),
				linesText: o.lines.map((l) => `${quantity(l.quantity)} ${l.unit} ${l.description}`).join(" · ") || "–",
				overdue: !!o.deliveryDate && o.deliveryDate < today && ["ordered", "confirmed"].includes(o.status),
			})));
			const overdue = open.filter((o) => o.deliveryDate && o.deliveryDate < today).length;
			m.setProperty("/kpis", [
				{ title: "Lieferungen heute", value: String(open.filter((o) => o.deliveryDate === today).length), icon: "sap-icon://shipping-status", color: "Neutral" },
				{ title: "Offene Bestellungen", value: String(open.length), icon: "sap-icon://pending", color: "Neutral" },
				{ title: "Noch nicht abgeschickt", value: String(drafts.length), icon: "sap-icon://document", color: drafts.length ? "Critical" : "Neutral" },
				{ title: "Lieferung überfällig", value: String(overdue), icon: "sap-icon://alert", color: overdue ? "Error" : "Neutral" },
			]);
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	async onNew(): Promise<void> {
		const saved = await openOrderEditor({ order: null });
		if (saved) void openOrderDetail(saved.id, this.has("materials.manage"));
	}

	onOpen(event: Event): void {
		const o = (event.getSource() as Control).getBindingContext("view")?.getObject() as OrderResponse;
		void openOrderDetail(o.id, this.has("materials.manage"));
	}
}
