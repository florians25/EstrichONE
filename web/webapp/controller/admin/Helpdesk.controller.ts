import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get } from "../../util/api";
import { options, ticketCategories } from "../../util/labels";
import type { TicketSummary } from "../../util/models";

/** IT Center › Helpdesk. @namespace estrichone.controller.admin */
export default class Helpdesk extends BaseController {
	onInit(): void {
		this.local({ items: [], filter: "open", category: "", busy: false, categories: [{ key: "", text: "Alle Kategorien" }, ...options(ticketCategories)] });
	}

	onRouteMatched(): void { void this.load(); }
	onFilter(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/items", (await this.run(() => get<TicketSummary[]>("tickets", { filter: m.getProperty("/filter"), category: m.getProperty("/category") || undefined }))) ?? []);
	}

	onOpen(event: Event): void {
		this.nav(`/it-center/helpdesk/${((event.getSource() as Control).getBindingContext("view")?.getObject() as TicketSummary).id}`);
	}
}
