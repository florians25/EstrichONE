import BaseController from "../BaseController";
import { get } from "../../util/api";
import type { OrderResponse } from "../../util/models";
import { loadCompany } from "../../util/orderDialogs";
import { orderSheet } from "../../util/orders";
import type { RouteMatch } from "../../util/router";

/** Eigenständige Druckseite für den Bestellschein (öffnet in neuem Tab). @namespace estrichone.controller.materials */
export default class OrderPrint extends BaseController {
	onInit(): void {
		this.local({ html: "", title: "Bestellschein", busy: false });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const [order, company] = await Promise.all([get<OrderResponse>(`material-orders/${match.params.id}`), loadCompany()]);
			m.setProperty("/title", `Bestellung ${order.number}`);
			m.setProperty("/html", orderSheet(order, company));
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onPrint(): void {
		window.print();
	}

	onClose(): void {
		window.close();
		this.nav("/bestellungen");
	}
}
