/** Bestellung erfassen/bearbeiten und ansehen (Bestellschein, Status, Druck, E-Mail). */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import MenuButton from "sap/m/MenuButton";
import Menu from "sap/m/Menu";
import MenuItem from "sap/m/MenuItem";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import Label from "sap/m/Label";
import Select from "sap/m/Select";
import ComboBox from "sap/m/ComboBox";
import DatePicker from "sap/m/DatePicker";
import Table from "sap/m/Table";
import Column from "sap/m/Column";
import ColumnListItem from "sap/m/ColumnListItem";
import Text from "sap/m/Text";
import Title from "sap/m/Title";
import Link from "sap/m/Link";
import ObjectStatus from "sap/m/ObjectStatus";
import MessageStrip from "sap/m/MessageStrip";
import MessageToast from "sap/m/MessageToast";
import MessageBox from "sap/m/MessageBox";
import OverflowToolbar from "sap/m/OverflowToolbar";
import ToolbarSpacer from "sap/m/ToolbarSpacer";
import HBox from "sap/m/HBox";
import VBox from "sap/m/VBox";
import Item from "sap/ui/core/Item";
import HTML from "sap/ui/core/HTML";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import JSONModel from "sap/ui/model/json/JSONModel";
import EventBus from "sap/ui/core/EventBus";
import { del, get, post, problemMessage, put } from "./api";
import { bauakteInput } from "./forms";
import { money, parseNumber } from "./format";
import { uuidv7 } from "./ids";
import { deliveryWindows, orderStatus, units } from "./labels";
import type { CompanyResponse, MaterialResponse, OrderResponse, OrderStatus, SupplierResponse } from "./models";
import { navTo } from "./navigation";
import { mailto, orderSheet, orderText } from "./orders";

export interface LineDraft {
	id: string;
	materialId: string | null;
	description: string;
	quantity: string;
	unit: string;
	unitPrice: string;
}

export type OrderEditing = { order: OrderResponse } | { order: null; bauakte?: { id: string; label: string }; lines?: Omit<LineDraft, "id">[] };

let company: Promise<CompanyResponse | null> | null = null;
export function loadCompany(): Promise<CompanyResponse | null> {
	company ??= get<CompanyResponse>("company").catch(() => null);
	return company;
}
export function resetCompany(): void {
	company = null;
}

const fmt = (v: number | null | undefined) => (v === null || v === undefined ? "" : v.toLocaleString("de-DE", { maximumFractionDigits: 3, useGrouping: false }));
const changed = () => EventBus.getInstance().publish("estrichone", "ordersChanged");

export async function openOrderEditor(editing: OrderEditing): Promise<OrderResponse | null> {
	const [suppliers, materials] = await Promise.all([get<SupplierResponse[]>("suppliers"), get<MaterialResponse[]>("materials")]);
	const o = editing.order;
	const model = new JSONModel({
		lines: (o ? o.lines.map((l) => ({ id: l.id, materialId: l.materialId, description: l.description, quantity: fmt(l.quantity), unit: l.unit, unitPrice: fmt(l.unitPrice) }))
			: (editing.lines?.length ? editing.lines.map((l) => ({ ...l, id: uuidv7() })) : [{ id: uuidv7(), materialId: null, description: "", quantity: "", unit: "Sack", unitPrice: "" }])) as LineDraft[],
		total: "",
		materials: [{ key: "", text: "– freier Artikel –" }, ...materials.map((m) => ({ key: m.id, text: `${m.name}${m.articleNumber ? ` · ${m.articleNumber}` : ""}` }))],
	});
	const supplier = new Select({ width: "100%", forceSelection: false, items: [new Item({ key: "", text: "Lieferant wählen" }),
		...suppliers.map((s) => new Item({ key: s.id, text: s.name }))], selectedKey: o?.supplierId ?? "" });
	const supplierNotes = new MessageStrip({ type: "Information", showIcon: true, visible: false });
	const showNotes = () => {
		const s = suppliers.find((x) => x.id === supplier.getSelectedKey());
		supplierNotes.setText(s?.orderNotes ? `${s.name}: ${s.orderNotes}` : "");
		supplierNotes.setVisible(!!s?.orderNotes);
	};
	supplier.attachChange(showNotes);
	showNotes();
	const bauakte = bauakteInput(`orderBauakte${Date.now()}`, o?.bauakteId ?? (editing.order === null ? editing.bauakte?.id ?? null : null),
		o?.bauakteId ? `${o.bauakteNumber ?? "neu"} · ${o.bauakteTitle}` : editing.order === null ? editing.bauakte?.label : undefined);
	const date = new DatePicker({ valueFormat: "yyyy-MM-dd", displayFormat: "EE, dd.MM.yyyy", value: o?.deliveryDate ?? "", placeholder: "nach Absprache", width: "100%" });
	const time = new ComboBox({ value: o?.deliveryTime ?? "", width: "100%", items: deliveryWindows.map((w) => new Item({ key: w, text: w })) });
	const street = new Input({ value: o?.deliveryStreet ?? "", placeholder: "leer = Baustelle der Bauakte", width: "100%" });
	const postal = new Input({ value: o?.deliveryPostalCode ?? "", width: "100%" });
	const city = new Input({ value: o?.deliveryCity ?? "", width: "100%" });
	const contact = new Input({ value: o?.siteContact ?? "", placeholder: "leer = Ansprechpartner der Bauakte", width: "100%" });
	const notes = new TextArea({ value: o?.notes ?? "", rows: 3, width: "100%", placeholder: "z. B. Anfahrt nur über Hofeinfahrt, Kran nötig" });

	const recalc = () => {
		const lines = model.getProperty("/lines") as LineDraft[];
		let total = 0;
		let complete = true;
		for (const l of lines) {
			const q = parseNumber(l.quantity);
			const p = parseNumber(l.unitPrice);
			if (q && p !== null && !Number.isNaN(p)) total += q * p;
			else complete = false;
		}
		model.setProperty("/total", total > 0 ? `${complete ? "Summe netto" : "Summe netto der Positionen mit Preis"} ${money(total)}` : "");
	};

	const table = new Table({
		width: "100%",
		noDataText: "Noch keine Positionen.",
		columns: [
			new Column({ header: new Text({ text: "Artikel" }), width: "28%" }), new Column({ header: new Text({ text: "Bezeichnung" }) }),
			new Column({ header: new Text({ text: "Menge" }), width: "7rem", hAlign: "End" }), new Column({ header: new Text({ text: "Einheit" }), width: "7rem" }),
			new Column({ header: new Text({ text: "Einzelpreis (€)" }), width: "8rem", hAlign: "End" }), new Column({ header: new Text({ text: "Summe" }), width: "7rem", hAlign: "End" }),
			new Column({ width: "3rem" }),
		],
	});
	table.setModel(model, "o");
	table.bindItems({
		path: "o>/lines",
		factory: (id: string, context) => {
			const index = Number(context.getPath().split("/").pop()) + 1;
			const materialSelect = new Select({ width: "100%", forceSelection: false, selectedKey: "{o>materialId}",
				items: { path: "o>/materials", template: new Item({ key: "{o>key}", text: "{o>text}" }), templateShareable: false } });
			materialSelect.data("aria", `Artikel Position ${index}`, true);
			materialSelect.attachChange(() => {
				const m = materials.find((x) => x.id === materialSelect.getSelectedKey());
				if (!m) return;
				const path = context.getPath();
				model.setProperty(`${path}/description`, m.name);
				model.setProperty(`${path}/unit`, m.unit);
				model.setProperty(`${path}/unitPrice`, fmt(m.price));
				if (!supplier.getSelectedKey() && m.supplierId) {
					supplier.setSelectedKey(m.supplierId);
					showNotes();
				}
				recalc();
			});
			const field = (path: string, label: string, align: "End" | "Begin" = "Begin") => {
				const input = new Input({ value: `{o>${path}}`, valueLiveUpdate: true, width: "100%", textAlign: align, liveChange: recalc });
				input.data("aria", `${label} Position ${index}`, true);
				return input;
			};
			const unit = new ComboBox({ value: "{o>unit}", width: "100%", items: units.map((u) => new Item({ key: u, text: u })) });
			unit.data("aria", `Einheit Position ${index}`, true);
			return new ColumnListItem(id, {
				cells: [
					materialSelect, field("description", "Bezeichnung"), field("quantity", "Menge", "End"), unit, field("unitPrice", "Einzelpreis", "End"),
					new Text({ text: { parts: ["o>quantity", "o>unitPrice"], formatter: (q: string, p: string) => {
						const a = parseNumber(q);
						const b = parseNumber(p);
						return a && b !== null && !Number.isNaN(b) ? money(a * b) : "";
					} } }).addStyleClass("eoTabular"),
					new Button({ icon: "sap-icon://delete", type: "Transparent", tooltip: `Position ${index} entfernen`, press: () => {
						const lines = (model.getProperty("/lines") as LineDraft[]).filter((_, i) => i !== index - 1);
						model.setProperty("/lines", lines);
						recalc();
					} }),
				],
			});
		},
	});
	// Zugängliche Namen der Eingaben je Position (für Bildschirmleser und Tests).
	table.addEventDelegate({ onAfterRendering: () => {
		table.getDomRef()?.querySelectorAll("[data-aria]").forEach((el) => {
			const target = el.querySelector("input") ?? el;
			target.setAttribute("aria-label", el.getAttribute("data-aria") ?? "");
		});
	} });
	recalc();

	const strip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
	const form = new SimpleForm({
		editable: true, layout: "ResponsiveGridLayout", columnsL: 2, columnsXL: 2, columnsM: 1, labelSpanL: 4, labelSpanXL: 4, labelSpanM: 4,
		content: [
			new Title({ text: "Lieferant und Bauakte" }),
			new Label({ text: "Lieferant", labelFor: supplier, required: true }), supplier,
			new Label({ text: "Bauakte (optional)", labelFor: bauakte.control }), bauakte.control,
			new Label({ text: "Liefertermin", labelFor: date }), date,
			new Label({ text: "Zeitfenster", labelFor: time }), time,
			new Title({ text: "Lieferadresse" }),
			new Label({ text: "Lieferadresse: Straße", labelFor: street }), street,
			new Label({ text: "PLZ", labelFor: postal }), postal,
			new Label({ text: "Ort", labelFor: city }), city,
			new Label({ text: "Ansprechpartner vor Ort", labelFor: contact }), contact,
		],
	});
	const totalText = new Text({ text: "{o>/total}" }).addStyleClass("eoTabular");
	totalText.setModel(model, "o");
	return new Promise((resolve) => {
		let result: OrderResponse | null = null;
		const save = async () => {
			if (!supplier.getSelectedKey()) {
				strip.setText("Bitte einen Lieferanten wählen.");
				strip.setVisible(true);
				return;
			}
			const lines = (model.getProperty("/lines") as LineDraft[]).map((l) => ({ ...l, q: parseNumber(l.quantity), p: parseNumber(l.unitPrice) }));
			const bad = lines.find((l) => !l.description.trim() || !l.q || Number.isNaN(l.q) || l.q <= 0 || !l.unit.trim() || Number.isNaN(l.p as number));
			if (bad) {
				strip.setText("Jede Position braucht Bezeichnung, Menge und Einheit (Preis optional).");
				strip.setVisible(true);
				return;
			}
			dialog.setBusy(true);
			try {
				const id = o?.id ?? uuidv7();
				result = await put<OrderResponse>(`material-orders/${id}`, {
					id, supplierId: supplier.getSelectedKey(), bauakteId: bauakte.read(), deliveryDate: date.getValue() || null,
					deliveryTime: time.getValue().trim() || null, deliveryStreet: street.getValue().trim() || null, deliveryPostalCode: postal.getValue().trim() || null,
					deliveryCity: city.getValue().trim() || null, siteContact: contact.getValue().trim() || null, notes: notes.getValue().trim() || null,
					lines: lines.map((l) => ({ id: l.id, materialId: l.materialId || null, description: l.description.trim(), quantity: l.q, unit: l.unit.trim(), unitPrice: l.p })),
				});
				MessageToast.show(o ? "Bestellung gespeichert" : `Bestellung ${result.number} angelegt`);
				changed();
				dialog.close();
			} catch (error) {
				strip.setText(problemMessage(error));
				strip.setVisible(true);
			} finally {
				dialog.setBusy(false);
			}
		};
		const dialog = new Dialog({
			title: o ? `Bestellung ${o.number} bearbeiten` : "Material bestellen",
			contentWidth: "62rem", resizable: true, draggable: true,
			content: [new VBox({ items: [strip, supplierNotes, form,
				new OverflowToolbar({ content: [new Title({ text: "Positionen", level: "H3" }), new ToolbarSpacer(),
					new Button({ text: "Position hinzufügen", icon: "sap-icon://add", press: () => {
						model.setProperty("/lines", [...(model.getProperty("/lines") as LineDraft[]), { id: uuidv7(), materialId: null, description: "", quantity: "", unit: "Sack", unitPrice: "" }]);
					} })] }),
				table,
				new HBox({ justifyContent: "End", items: [totalText] }).addStyleClass("sapUiSmallMarginTop"),
				new Label({ text: "Hinweise an den Lieferanten", labelFor: notes }).addStyleClass("sapUiSmallMarginTop"), notes,
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Speichern", type: "Emphasized", press: () => void save() }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => {
				dialog.destroy();
				resolve(result);
			},
		});
		dialog.open();
	});
}

/** Bestellung ansehen: Bestellschein, Status führen, drucken, per E-Mail, Text kopieren. */
export async function openOrderDetail(orderId: string, canManage: boolean): Promise<void> {
	const [order, comp] = await Promise.all([get<OrderResponse>(`material-orders/${orderId}`), loadCompany()]);
	let o = order;
	const sheet = new HTML({ sanitizeContent: false });
	const status = new ObjectStatus({ inverted: true });
	const info = new HBox({ wrap: "Wrap" });
	const actions = new OverflowToolbar();
	const dialog = new Dialog({
		contentWidth: "60rem", contentHeight: "80vh", resizable: true, draggable: true,
		content: [new VBox({ items: [info, actions, sheet] }).addStyleClass("sapUiSmallMargin")],
		endButton: new Button({ text: "Schließen", press: () => dialog.close() }),
		afterClose: () => dialog.destroy(),
	});
	const setStatus = async (next: OrderStatus, text: string) => {
		dialog.setBusy(true);
		try {
			await post(`material-orders/${o.id}/status`, { status: next });
			o = await get<OrderResponse>(`material-orders/${o.id}`);
			MessageToast.show(text);
			changed();
			render();
		} catch (error) {
			MessageBox.error(problemMessage(error));
		} finally {
			dialog.setBusy(false);
		}
	};
	const render = () => {
		dialog.setTitle(`Bestellung ${o.number}`);
		status.setText(orderStatus[o.status].text);
		status.setState(orderStatus[o.status].tone);
		sheet.setContent(orderSheet(o, comp));
		info.destroyItems();
		info.addItem(status);
		if (o.bauakteId) info.addItem(new Link({ text: `Bauakte ${o.bauakteNumber ?? ""} öffnen`, press: () => { dialog.close(); navTo(`/bauakten/${o.bauakteId}?abschnitt=material`); } }).addStyleClass("sapUiSmallMarginBegin"));
		if (o.supplierPhone) info.addItem(new Link({ text: `${o.supplierName} anrufen (${o.supplierPhone})`, href: `tel:${o.supplierPhone}` }).addStyleClass("sapUiSmallMarginBegin"));
		if (o.orderedAt) info.addItem(new Text({ text: `Bestellt am ${new Date(o.orderedAt).toLocaleString("de-DE", { dateStyle: "medium", timeStyle: "short" })}` }).addStyleClass("sapUiSmallMarginBegin eoMuted"));
		if (o.deliveredAt) info.addItem(new Text({ text: `Geliefert erfasst am ${new Date(o.deliveredAt).toLocaleString("de-DE", { dateStyle: "medium", timeStyle: "short" })}` }).addStyleClass("sapUiSmallMarginBegin eoMuted"));
		actions.destroyContent();
		const editable = ["draft", "ordered", "confirmed"].includes(o.status);
		if (canManage && o.status === "draft") {
			actions.addContent(new Button({ text: o.supplierEmail ? "Per E-Mail bestellen" : "Als bestellt markieren", type: "Emphasized", icon: "sap-icon://paper-plane",
				enabled: o.lines.length > 0, press: () => {
					if (!o.supplierEmail) {
						void setStatus("ordered", `Bestellung ${o.number} als bestellt markiert`);
						return;
					}
					window.location.href = mailto(o, comp);
					MessageBox.confirm(`Wenn die Bestellung an ${o.supplierEmail} gesendet ist, wird sie als „Bestellt“ markiert.`, {
						title: "E-Mail gesendet?", actions: ["Ja, als bestellt markieren", MessageBox.Action.CANCEL], emphasizedAction: "Ja, als bestellt markieren",
						onClose: (a: string) => { if (a === "Ja, als bestellt markieren") void setStatus("ordered", `Bestellung ${o.number} als bestellt markiert`); },
					});
				} }));
			if (o.supplierEmail) actions.addContent(new Button({ text: "Nur als bestellt markieren", press: () => void setStatus("ordered", "Als bestellt markiert") }));
		}
		if (canManage && o.status === "ordered") actions.addContent(new Button({ text: "Bestätigt", icon: "sap-icon://accept", press: () => void setStatus("confirmed", "Lieferung bestätigt") }));
		if (canManage && ["ordered", "confirmed"].includes(o.status)) {
			actions.addContent(new Button({ text: "Geliefert", type: "Success", icon: "sap-icon://shipping-status", press: () => void setStatus("delivered", "Als geliefert erfasst") }));
		}
		actions.addContent(new ToolbarSpacer());
		actions.addContent(new Button({ text: "Bestellschein (PDF)", icon: "sap-icon://pdf-attachment", press: () => {
			const a = document.createElement("a");
			a.href = `/api/v1/material-orders/${o.id}/pdf`;
			a.download = "";
			a.click();
		} }));
		actions.addContent(new Button({ text: "Text kopieren", icon: "sap-icon://copy", press: async () => {
			await navigator.clipboard.writeText(orderText(o, comp)).catch(() => undefined);
			MessageToast.show("Bestelltext kopiert");
		} }));
		if (canManage) {
			const items: MenuItem[] = [];
			if (editable) items.push(new MenuItem({ text: "Bearbeiten", icon: "sap-icon://edit", press: async () => {
				const saved = await openOrderEditor({ order: o });
				if (saved) { o = saved; render(); }
			} }));
			if (o.status !== "draft" && o.supplierEmail) items.push(new MenuItem({ text: "E-Mail an Lieferanten", icon: "sap-icon://email", press: () => { window.location.href = mailto(o, comp); } }));
			if (o.status === "ordered") items.push(new MenuItem({ text: "Zurück zum Entwurf", icon: "sap-icon://undo", press: () => void setStatus("draft", "Wieder Entwurf") }));
			if (o.status === "delivered") items.push(new MenuItem({ text: "Lieferung zurücknehmen", icon: "sap-icon://undo", press: () => void setStatus("confirmed", "Lieferung zurückgenommen") }));
			if (o.status === "cancelled") items.push(new MenuItem({ text: "Wieder als Entwurf öffnen", icon: "sap-icon://undo", press: () => void setStatus("draft", "Wieder Entwurf") }));
			if (editable) items.push(new MenuItem({ text: "Stornieren", icon: "sap-icon://cancel", press: () => void setStatus("cancelled", "Bestellung storniert") }));
			if (["draft", "cancelled"].includes(o.status)) items.push(new MenuItem({ text: "Löschen", icon: "sap-icon://delete", press: () => {
				MessageBox.confirm(`Bestellung ${o.number} löschen?`, { actions: ["Löschen", MessageBox.Action.CANCEL], emphasizedAction: "Löschen",
					onClose: async (a: string) => {
						if (a !== "Löschen") return;
						try {
							await del(`material-orders/${o.id}`);
							changed();
							dialog.close();
						} catch (error) {
							MessageBox.error(problemMessage(error));
						}
					} });
			} }));
			if (items.length) actions.addContent(new MenuButton({ text: "Weitere", menu: new Menu({ items }) }));
		}
	};
	render();
	dialog.open();
}
