import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import EventBus from "sap/ui/core/EventBus";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Table from "sap/m/Table";
import { akte } from "../../../util/akteStore";
import { quantity } from "../../../util/format";
import { requirements, type Need } from "../../../util/needs";
import type { OrderResponse, RoomOverview } from "../../../util/models";
import { day, orderTotal } from "../../../util/orders";
import { openOrderDetail, openOrderEditor } from "../../../util/orderDialogs";
import { session } from "../../../util/session";

/** Material der Baustelle: Bedarf aus den Räumen (mit Reserve) bestellen, Bestellungen der Bauakte. @namespace estrichone.controller.bauakte.sections */
export default class Material extends SectionBase {
	protected watches = ["rooms", "orders"];
	private rooms: RoomOverview | null = null;

	protected init(): void {
		this.v().setModel(new JSONModel({ needs: [], orders: [], reserve: 5, selectedNeeds: 0 }), "mt");
		EventBus.getInstance().subscribe("estrichone", "ordersChanged", () => {
			if (this.bauakteId) akte.invalidate(this.bauakteId, "orders");
		});
	}

	private mt(): JSONModel {
		return this.v().getModel("mt") as JSONModel;
	}

	protected async load(): Promise<void> {
		const [rooms, orders] = await Promise.all([akte.rooms(this.bauakteId), akte.orders(this.bauakteId)]);
		this.rooms = rooms;
		this.computeNeeds();
		this.mt().setProperty("/orders", orders.map((o: OrderResponse) => ({
			...o, deliveryText: `${o.deliveryDate ? day(o.deliveryDate) : "nach Absprache"}${o.deliveryTime ? `, ${o.deliveryTime}` : ""}`,
			linesText: o.lines.map((l) => `${quantity(l.quantity)} ${l.unit} ${l.description}`).join(" · "), totalText: orderTotal(o),
		})));
	}

	private computeNeeds(): void {
		const needs = this.rooms ? requirements(this.rooms, Number(this.mt().getProperty("/reserve") ?? 0)) : [];
		this.mt().setProperty("/needs", needs.map((n) => ({ ...n, selected: true })));
		this.mt().setProperty("/selectedNeeds", needs.length);
	}

	onReserve(): void {
		this.computeNeeds();
	}

	onNeedSelection(): void {
		this.mt().setProperty("/selectedNeeds", (this.v().byId("needsTable") as Table).getSelectedItems().length);
	}

	private label(): { id: string; label: string } {
		const b = this.bauakte();
		return { id: b.id, label: `${b.number ?? "neu"} · ${b.title}` };
	}

	async onOrderNeeds(): Promise<void> {
		const picked = (this.v().byId("needsTable") as Table).getSelectedItems().map((i) => i.getBindingContext("mt")?.getObject() as Need);
		const saved = await openOrderEditor({ order: null, bauakte: this.label(),
			lines: picked.map((n) => ({ materialId: null, description: n.description, quantity: n.amount.toLocaleString("de-DE", { useGrouping: false }), unit: n.unit, unitPrice: "" })) });
		if (saved) {
			akte.invalidate(this.bauakteId, "orders");
			void openOrderDetail(saved.id, session.has("materials.manage"));
		}
	}

	async onOrder(): Promise<void> {
		const saved = await openOrderEditor({ order: null, bauakte: this.label() });
		if (saved) {
			akte.invalidate(this.bauakteId, "orders");
			void openOrderDetail(saved.id, session.has("materials.manage"));
		}
	}

	onOpenOrder(event: Event): void {
		const order = (event.getSource() as Control).getBindingContext("mt")?.getObject() as OrderResponse;
		void openOrderDetail(order.id, session.has("materials.manage"));
	}
}
