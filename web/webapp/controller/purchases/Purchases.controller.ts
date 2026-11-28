import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import ComboBox from "sap/m/ComboBox";
import Select from "sap/m/Select";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import Item from "sap/ui/core/Item";
import type FileUploader from "sap/ui/unified/FileUploader";
import { get, put } from "../../util/api";
import { date, money } from "../../util/format";
import type { RouteMatch } from "../../util/router";
import {
	documentState, filterDocuments, filterPurchases, purchaseStatus, purchaseSummary, supplierText, type PurchaseDocumentItem, type PurchaseFilter, type PurchaseItem,
	type PurchaseList,
} from "../../util/purchases";
import { attachDrop, uploadPurchases, uploadText } from "../../util/purchaseUpload";

/**
 * Eingangsbelege (SAP List Report): Reiter „Aus Lexware Office“ (Belegliste der Lieferanten, Zuordnung zur Bauakte und zum
 * Lieferanten im Materialstamm) und „Belegeingang“ (hochgeladene Dateien mit E-Rechnung, Übergabe an Lexware Office).
 * @namespace estrichone.controller.purchases
 */
export default class Purchases extends BaseController {
	private list: PurchaseList | null = null;
	private dropReady = false;

	onInit(): void {
		this.local({
			busy: false, items: [], documents: [], summary: "", lexwareError: null, suppliers: [], bauakten: [], canEdit: false, lexwareCount: "", inboxCount: "",
			filter: { tab: "lexware", query: "", supplier: "", bauakte: "", status: "all" } as PurchaseFilter,
		});
	}

	onAfterRendering(): void {
		const dom = this.v().byId("purchaseContent")?.getDomRef() as HTMLElement | null;
		if (!dom || this.dropReady) return;
		this.dropReady = true;
		attachDrop(dom, () => this.model().getProperty("/canEdit") === true, (files) => void this.upload(files));
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		if (match.query.get("reiter") === "eingang") m.setProperty("/filter/tab", "inbox");
		const bauakte = match.query.get("bauakte");
		if (bauakte) m.setProperty("/filter/bauakte", bauakte);
		await this.load(false);
	}

	private async load(refresh: boolean): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const list = await get<PurchaseList>("purchases/vouchers", refresh ? { refresh: "true" } : undefined);
			this.list = list;
			m.setProperty("/lexwareError", list.lexwareError);
			m.setProperty("/canEdit", list.canEdit);
			const unique = <T extends { key: string }>(rows: T[]) => [...new Map(rows.map((r) => [r.key, r])).values()].sort((a, b) =>
				(a as unknown as { text: string }).text.localeCompare((b as unknown as { text: string }).text, "de"));
			m.setProperty("/suppliers", unique(list.items.filter((i) => i.contactName).map((i) => ({ key: i.lexwareContactId ?? i.contactName!, text: i.contactName! }))));
			const bauakten = [...list.items, ...list.documents].filter((i) => i.bauakteId)
				.map((i) => ({ key: i.bauakteId!, text: [i.bauakteNumber, i.bauakteTitle].filter(Boolean).join(" ") }));
			m.setProperty("/bauakten", [{ key: "none", text: "Ohne Bauakte" }, ...unique(bauakten)]);
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
		if (!this.list) return;
		const m = this.model();
		const filter = m.getProperty("/filter") as PurchaseFilter;
		const items = filterPurchases(this.list.items, filter);
		m.setProperty("/items", items.map((i) => ({
			...i, supplierText: supplierText(i), bauakteText: [i.bauakteNumber, i.bauakteTitle].filter(Boolean).join(" "), dateText: i.voucherDate ? date(i.voucherDate) : "",
			dueText: i.dueDate ? date(i.dueDate) : "", grossText: i.gross === null ? "" : money(i.type === "purchasecreditnote" ? -i.gross : i.gross),
			openText: i.openAmount === null ? "" : money(i.openAmount), statusText: purchaseStatus(i.status).text, statusState: purchaseStatus(i.status).state,
		})));
		const documents = filterDocuments(this.list.documents, filter);
		m.setProperty("/documents", documents.map((d) => ({
			...d, bauakteText: [d.bauakteNumber, d.bauakteTitle].filter(Boolean).join(" "), dateText: d.invoiceDate ? date(d.invoiceDate) : "",
			grossText: d.grossAmount === null ? "" : money(d.grossAmount), stateText: documentState(d).text, stateState: documentState(d).state,
		})));
		m.setProperty("/lexwareCount", String(this.list.items.length));
		m.setProperty("/inboxCount", String(this.list.documents.filter((d) => d.state !== "sent").length));
		m.setProperty("/summary", purchaseSummary(items));
	}

	async onPick(e: Event): Promise<void> {
		const uploader = e.getSource() as FileUploader;
		const files = [...((e.getParameter("files" as never) as FileList | undefined) ?? [])];
		uploader.clear();
		await this.upload(files);
	}

	private async upload(files: File[]): Promise<void> {
		if (!files.length) return;
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const bauakte = m.getProperty("/filter/bauakte") as string;
			const result = await uploadPurchases(files, bauakte && bauakte !== "none" ? bauakte : null);
			m.setProperty("/filter/tab", "inbox");
			await this.load(false);
			const text = uploadText(result);
			if (result.problems.length) this.showError(new Error([text, ...result.problems].filter(Boolean).join("\n")), "Beleg hochladen");
			else if (text) this.toast(text);
			if (result.uploaded.length === 1 && !result.problems.length) this.nav(`/eingangsbelege/${result.uploaded[0].document.id}`);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onDocument(e: Event): void {
		const row = (e.getSource() as Control).getBindingContext("view")?.getObject() as PurchaseDocumentItem;
		this.nav(`/eingangsbelege/${row.id}`);
	}

	/** Eingangsbeleg aus Lexware Office: Bauakte zuordnen, Lieferant verknüpfen, in Lexware Office öffnen. */
	async onVoucher(e: Event): Promise<void> {
		const row = (e.getSource() as Control).getBindingContext("view")?.getObject() as PurchaseItem;
		const list = this.list!;
		const canEdit = list.canEdit;
		const bauakte = new ComboBox({ width: "100%", placeholder: "Bauakte suchen", showClearIcon: true, enabled: canEdit });
		if (row.bauakteId) bauakte.addItem(new Item({ key: row.bauakteId, text: [row.bauakteNumber, row.bauakteTitle].filter(Boolean).join(" ") }));
		bauakte.setSelectedKey(row.bauakteId ?? "");
		const page = await get<{ items: { id: string; number: string | null; title: string }[] }>("bauakten", { limit: 200 }).catch(() => ({ items: [] }));
		page.items.filter((b) => b.id !== row.bauakteId).forEach((b) => bauakte.addItem(new Item({ key: b.id, text: [b.number, b.title].filter(Boolean).join(" ") })));
		const supplier = new Select({ width: "100%", enabled: canEdit && !!row.lexwareContactId, forceSelection: false,
			items: [new Item({ key: "", text: "Automatisch (gleicher Name)" }), ...list.suppliers.map((s) => new Item({ key: s.id, text: [s.number, s.name].filter(Boolean).join(" ") }))] });
		supplier.setSelectedKey(row.supplierLinkedByHand ? row.supplierId ?? "" : "");
		const status = purchaseStatus(row.status);
		const facts = [
			`Lieferant: ${row.contactName ?? "unbekannt"}`, `Datum: ${row.voucherDate ? date(row.voucherDate) : "unbekannt"}`,
			row.dueDate ? `Fällig: ${date(row.dueDate)}` : null, `Brutto: ${money(row.gross)}`, row.openAmount !== null ? `Offen: ${money(row.openAmount)}` : null,
			`Status: ${status.text}`,
		].filter(Boolean).join("\n");
		const dialog: Dialog = new Dialog({
			title: `${row.typeName} ${row.number ?? "ohne Nummer"}`, contentWidth: "32rem",
			content: [new VBox({ items: [
				new Text({ text: facts }),
				new Label({ text: "Bauakte (Kosten der Bauakte)", labelFor: bauakte }).addStyleClass("sapUiSmallMarginTop"), bauakte,
				new Label({ text: "Lieferant im Materialstamm", labelFor: supplier }).addStyleClass("sapUiSmallMarginTop"), supplier,
				new Text({ text: row.supplierName ? `Verknüpft mit ${row.supplierName}.` : "Kein Lieferant mit gleichem Namen im Materialstamm." }).addStyleClass("eoMuted"),
			] }).addStyleClass("sapUiSmallMargin")],
			buttons: [
				...(canEdit ? [new Button({ text: "Speichern", type: "Emphasized", press: async () => {
					const ok = await this.run(async () => {
						if ((bauakte.getSelectedKey() || null) !== row.bauakteId) {
							await put(`purchases/vouchers/${encodeURIComponent(row.lexwareId)}/bauakte`, { bauakteId: bauakte.getSelectedKey() || null });
						}
						const chosen = supplier.getSelectedKey() || null;
						if (row.lexwareContactId && chosen !== (row.supplierLinkedByHand ? row.supplierId : null)) {
							await put(`purchases/suppliers/${encodeURIComponent(row.lexwareContactId)}`, { supplierId: chosen });
						}
						return true;
					}, "Gespeichert.");
					if (!ok) return;
					dialog.close();
					await this.load(false);
				} })] : []),
				...(row.lexwareUrl ? [new Button({ text: "In Lexware öffnen", icon: "sap-icon://action", press: () => window.open(row.lexwareUrl!, "_blank", "noopener") })] : []),
				new Button({ text: "Schließen", press: () => dialog.close() }),
			],
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}
}
