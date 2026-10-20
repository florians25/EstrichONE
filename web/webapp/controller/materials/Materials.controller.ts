import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, put } from "../../util/api";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { materialCategories, options, units } from "../../util/labels";
import type { MaterialResponse, SupplierResponse } from "../../util/models";

/** Materialstamm. @namespace estrichone.controller.materials */
export default class Materials extends BaseController {
	private all: MaterialResponse[] = [];
	private suppliers: SupplierResponse[] = [];

	onInit(): void {
		this.local({ shown: [], search: "", category: "", inactive: false, busy: false,
			categories: [{ key: "", text: "Alle Kategorien" }, ...options(materialCategories)] });
	}

	onRouteMatched(): void { void this.load(); }
	onInactive(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			[this.all, this.suppliers] = await Promise.all([get<MaterialResponse[]>("materials", { includeInactive: m.getProperty("/inactive") || undefined }),
				get<SupplierResponse[]>("suppliers")]);
			this.refresh();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	refresh(): void {
		const m = this.model();
		const q = String(m.getProperty("/search") ?? "").trim().toLowerCase();
		const c = m.getProperty("/category") as string;
		m.setProperty("/shown", this.all.filter((x) => (!c || x.category === c) && (!q || [x.name, x.articleNumber, x.supplierName].some((v) => v?.toLowerCase().includes(q)))));
	}

	private async form(item: MaterialResponse | null): Promise<void> {
		// Lieferanten frisch laden (z. B. gerade angelegt, Seite noch beim Laden).
		this.suppliers = (await this.run(() => get<SupplierResponse[]>("suppliers"))) ?? this.suppliers;
		const saved = await openForm({
			title: item ? "Artikel bearbeiten" : "Artikel anlegen", width: "38rem",
			initial: item ? { ...item } : { category: "screed", unit: "Sack", isActive: true },
			fields: [
				{ key: "name", label: "Bezeichnung", required: true, maxLength: 200, placeholder: "z. B. Zementestrich CT-C25-F4, Sackware 40 kg" },
				{ key: "category", label: "Kategorie", type: "select", required: true, options: options(materialCategories) },
				{ key: "articleNumber", label: "Artikelnummer", maxLength: 50 },
				{ key: "unit", label: "Einheit", type: "combo", required: true, options: units.map((u) => ({ key: u, text: u })) },
				{ key: "price", label: "Einkaufspreis je Einheit (netto)", type: "number", unit: "€", min: 0 },
				{ key: "supplierId", label: "Standardlieferant", type: "select", options: this.suppliers.map((s) => ({ key: s.id, text: s.name })) },
				{ key: "notes", label: "Hinweise", type: "textarea", rows: 2, placeholder: "z. B. Verbrauch ca. 20 kg/m² je cm" },
				{ key: "isActive", label: "Aktiv (in Bestellungen auswählbar)", type: "switch" },
			],
			submit: async (v) => {
				const id = item?.id ?? uuidv7();
				await put(`materials/${id}`, { id, name: v.name, articleNumber: v.articleNumber || null, category: v.category, unit: v.unit,
					supplierId: v.supplierId || null, price: v.price ?? null, notes: v.notes || null, isActive: !!v.isActive });
				return true;
			},
		});
		if (saved) { this.toast("Artikel gespeichert"); void this.load(); }
	}

	onNew(): void { void this.form(null); }
	onEdit(event: Event): void { void this.form((event.getSource() as Control).getBindingContext("view")?.getObject() as MaterialResponse); }

	async onDelete(event: Event): Promise<void> {
		const item = (event.getSource() as Control).getBindingContext("view")?.getObject() as MaterialResponse;
		if (await this.confirm(`Artikel „${item.name}“ löschen? Bestehende Bestellungen behalten ihre Positionen.`, { action: "Löschen", danger: true })) {
			await this.run(() => del(`materials/${item.id}`));
			void this.load();
		}
	}
}
