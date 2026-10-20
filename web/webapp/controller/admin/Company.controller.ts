import BaseController from "../BaseController";
import { get, put } from "../../util/api";
import type { CompanyResponse } from "../../util/models";
import { resetCompany } from "../../util/orderDialogs";

/** Firmendaten für den Briefkopf. @namespace estrichone.controller.admin */
export default class Company extends BaseController {
	onInit(): void {
		this.local({ c: {}, loaded: false, busy: false });
	}

	async onRouteMatched(): Promise<void> {
		const c = await this.run(() => get<CompanyResponse>("company"));
		if (c) this.model().setData({ c, loaded: true, busy: false });
	}

	async onSave(): Promise<void> {
		const c = this.model().getProperty("/c") as CompanyResponse;
		if (!c.name?.trim()) {
			this.toast("Bitte den Firmennamen angeben.");
			return;
		}
		const saved = await this.run(() => put<CompanyResponse>("company", { ...c, email: c.email || null }), "Firmendaten gespeichert");
		if (saved) {
			resetCompany();
			this.model().setProperty("/c", saved);
		}
	}
}
