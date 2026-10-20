import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, put } from "../../util/api";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import type { SupplierResponse } from "../../util/models";

/** Lieferanten. @namespace estrichone.controller.materials */
export default class Suppliers extends BaseController {
	onInit(): void {
		this.local({ items: [], inactive: false, busy: false });
	}

	onRouteMatched(): void { void this.load(); }
	onInactive(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/items", (await this.run(() => get<SupplierResponse[]>("suppliers", { includeInactive: m.getProperty("/inactive") || undefined }))) ?? []);
	}

	private async form(item: SupplierResponse | null): Promise<void> {
		const saved = await openForm({
			title: item ? "Lieferant bearbeiten" : "Lieferant anlegen", width: "38rem",
			initial: item ? { ...item } : { isActive: true },
			fields: [
				{ key: "name", label: "Name", required: true, maxLength: 200 },
				{ key: "customerNumber", label: "Unsere Kundennummer", maxLength: 50 },
				{ key: "contactName", label: "Ansprechpartner", maxLength: 100 },
				{ key: "phone", label: "Telefon (Disposition)", maxLength: 50 },
				{ key: "email", label: "E-Mail für Bestellungen", type: "email", maxLength: 320 },
				{ key: "street", label: "Straße", maxLength: 200 },
				{ key: "postalCode", label: "PLZ", maxLength: 10 },
				{ key: "city", label: "Ort", maxLength: 100 },
				{ key: "orderNotes", label: "Bestellhinweise", type: "textarea", rows: 2, placeholder: "z. B. Bestellschluss 12 Uhr für Lieferung am Folgetag" },
				{ key: "isActive", label: "Aktiv", type: "switch" },
			],
			submit: async (v) => {
				const id = item?.id ?? uuidv7();
				const t = (k: string) => (v[k] ? String(v[k]) : null);
				await put(`suppliers/${id}`, { id, name: v.name, customerNumber: t("customerNumber"), contactName: t("contactName"), phone: t("phone"), email: t("email"),
					street: t("street"), postalCode: t("postalCode"), city: t("city"), orderNotes: t("orderNotes"), isActive: !!v.isActive });
				return true;
			},
		});
		if (saved) { this.toast("Lieferant gespeichert"); void this.load(); }
	}

	onNew(): void { void this.form(null); }
	onEdit(event: Event): void { void this.form((event.getSource() as Control).getBindingContext("view")?.getObject() as SupplierResponse); }

	async onDelete(event: Event): Promise<void> {
		const item = (event.getSource() as Control).getBindingContext("view")?.getObject() as SupplierResponse;
		if (await this.confirm(`Lieferant „${item.name}“ löschen?`, { action: "Löschen", danger: true })) {
			await this.run(() => del(`suppliers/${item.id}`));
			void this.load();
		}
	}
}
