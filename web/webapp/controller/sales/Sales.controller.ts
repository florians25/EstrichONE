import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import ComboBox from "sap/m/ComboBox";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import Item from "sap/ui/core/Item";
import MessageStrip from "sap/m/MessageStrip";
import { ApiError, get, post } from "../../util/api";
import { date, money } from "../../util/format";
import { uuidv7 as newId } from "../../util/ids";
import type { RouteMatch } from "../../util/router";
import {
	filterItems, numberText, salesTypes, selectionText, statusTabs, statusText, type SalesFilter, type SalesList, type SalesListItem, type SalesOptions,
	type SalesType, type SalesVoucher,
} from "../../util/sales";

interface Row extends SalesListItem {
	numberText: string; bauakteText: string; dateText: string; dueText: string; netText: string; grossText: string; openText: string; statusText: string; statusState: string;
}

/**
 * Verkauf: Übersicht aller Belege wie in Lexware Office (SAP List Report). Reiter je Belegart, Status als Umschalter, Suche,
 * Filter Kunde und Bauakte, Summen der Auswahl als Textzeile. Zeilen öffnen den Belegeditor bzw. den Beleg in Lexware Office.
 * @namespace estrichone.controller.sales
 */
export default class Sales extends BaseController {
	private all: SalesListItem[] = [];
	private options: SalesOptions | null = null;

	onInit(): void {
		this.local({
			busy: false, items: [], summary: "", lexwareError: null, customers: [], bauakten: [], creatable: [],
			typeTabs: [{ key: "all", text: "Alle Belege" }], statusTabs: statusTabs.map((t) => ({ key: t.key, text: t.text })),
			filter: { type: "all", status: "all", query: "", customer: "", bauakte: "" } as SalesFilter,
		});
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		const type = match.query.get("art");
		if (type) m.setProperty("/filter/type", type);
		const bauakte = match.query.get("bauakte");
		if (bauakte) m.setProperty("/filter/bauakte", bauakte);
		await this.load(false);
	}

	private async load(refresh: boolean): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const [list, options] = await Promise.all([
				get<SalesList>("sales/vouchers", refresh ? { refresh: "true" } : undefined),
				this.options ? Promise.resolve(this.options) : get<SalesOptions>("sales/options"),
			]);
			this.options = options;
			this.all = list.items;
			m.setProperty("/lexwareError", list.lexwareError);
			m.setProperty("/typeTabs", [{ key: "all", text: "Alle Belege" },
				...salesTypes.filter((t) => options.types.find((a) => a.type === t.key)?.canRead).map((t) => ({ key: t.key, text: t.plural }))]);
			m.setProperty("/creatable", options.types.filter((t) => t.canEdit).map((t) => ({ type: t.type, name: t.name })));
			const unique = <T extends { key: string }>(rows: T[]) => [...new Map(rows.map((r) => [r.key, r])).values()];
			m.setProperty("/customers", unique(this.all.filter((i) => i.customerName).map((i) => ({ key: i.contactId ?? i.customerName!, text: i.customerName! })))
				.sort((a, b) => a.text.localeCompare(b.text, "de")));
			m.setProperty("/bauakten", unique(this.all.filter((i) => i.bauakteId).map((i) => ({ key: i.bauakteId!, text: [i.bauakteNumber, i.bauakteTitle].filter(Boolean).join(" ") })))
				.sort((a, b) => a.text.localeCompare(b.text, "de")));
			this.onFilter();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onRefresh(): void {
		void this.load(true);
	}

	onFilter(): void {
		const m = this.model();
		const items = filterItems(this.all, m.getProperty("/filter") as SalesFilter);
		m.setProperty("/items", items.map((i): Row => {
			const status = statusText(i.status, i.type);
			return {
				...i, numberText: numberText(i.number), bauakteText: [i.bauakteNumber, i.bauakteTitle].filter(Boolean).join(" "), dateText: i.voucherDate ? date(i.voucherDate) : "",
				dueText: i.dueDate ? date(i.dueDate) : "", netText: i.net === null ? "" : money(i.net), grossText: i.gross === null ? "" : money(i.gross),
				openText: i.openAmount === null || i.openAmount === undefined ? "" : money(i.openAmount), statusText: status.text, statusState: status.state,
			};
		}));
		m.setProperty("/summary", selectionText(items));
	}

	onOpen(e: Event): void {
		const row = (e.getSource() as Control).getBindingContext("view")?.getObject() as Row;
		if (row.id) this.nav(`/verkauf/${row.id}`);
		else if (row.lexwareUrl) window.open(row.lexwareUrl, "_blank", "noopener");
	}

	/** Neuer Beleg: Kunde (mit Lexware verknüpft) und optional Bauakte wählen. */
	async onNew(e: Event): Promise<void> {
		const type = (e.getParameter("item" as never) as { getKey: () => string }).getKey() as SalesType;
		const customers = await this.run(() => get<{ id: string; name: string; customerNumber: string | null }[]>("contacts", { sort: "name" }));
		if (!customers) return;
		const customer = new ComboBox({ width: "100%", placeholder: "Kunde wählen", items: customers.map((c) => new Item({ key: c.id, text: [c.name, c.customerNumber].filter(Boolean).join(", ") })) });
		const bauakte = new ComboBox({ width: "100%", placeholder: "Ohne Bauakte (Beleg zum Kunden)", enabled: false, showClearIcon: true });
		const hint = new MessageStrip({ type: "Information", showIcon: true, visible: false });
		customer.attachChange(async () => {
			bauakte.removeAllItems();
			bauakte.setSelectedKey("");
			const id = customer.getSelectedKey();
			bauakte.setEnabled(!!id);
			if (!id) return;
			const list = await get<{ items: { id: string; number: string | null; title: string }[] }>("bauakten", { customerId: id, limit: "100" }).catch(() => ({ items: [] }));
			list.items.forEach((b) => bauakte.addItem(new Item({ key: b.id, text: [b.number, b.title].filter(Boolean).join(" ") })));
		});
		const dialog: Dialog = new Dialog({
			title: `Neuer Beleg: ${salesTypes.find((t) => t.key === type)?.text ?? ""}`, contentWidth: "30rem",
			content: [new VBox({ items: [
				new Text({ text: "Wählen Sie den Kunden. Mit einer Bauakte übernimmt der Beleg Anschrift der Baustelle, Leistungszeitraum und Vorschläge aus der Bauakte." }),
				new Label({ text: "Kunde", required: true, labelFor: customer }).addStyleClass("sapUiSmallMarginTop"), customer,
				new Label({ text: "Bauakte", labelFor: bauakte }).addStyleClass("sapUiSmallMarginTop"), bauakte, hint,
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Anlegen", type: "Emphasized", press: async () => {
				if (!customer.getSelectedKey()) {
					customer.setValueState("Error");
					customer.setValueStateText("Bitte wählen Sie einen Kunden.");
					return;
				}
				try {
					const created = await post<SalesVoucher>("sales/vouchers", { id: newId(), type, contactId: customer.getSelectedKey(), bauakteId: bauakte.getSelectedKey() || null });
					dialog.close();
					this.nav(`/verkauf/${created.id}`);
				} catch (error) {
					hint.setType("Warning");
					hint.setText(error instanceof ApiError ? error.message : String(error));
					hint.setVisible(true);
				}
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}
}
