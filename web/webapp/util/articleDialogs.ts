/**
 * Dialoge zu „Artikel und Leistungen“, gemeinsam für Liste und Objektseite: anlegen, bearbeiten und duplizieren (mit
 * Brutto-Netto-Umrechnung und Vorbelegung m² und 19 %), Abweichungen zu Lexware Office entscheiden, in Lexware Office
 * anlegen, aktiv bzw. inaktiv setzen und Preise mehrerer Einträge um Prozent ändern (mit Vorschau).
 */
import MessageBox from "sap/m/MessageBox";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import StepInput from "sap/m/StepInput";
import Select from "sap/m/Select";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import Table from "sap/m/Table";
import Column from "sap/m/Column";
import ColumnListItem from "sap/m/ColumnListItem";
import Item from "sap/ui/core/Item";
import type View from "sap/ui/core/mvc/View";
import { ApiError, post, put } from "./api";
import { money } from "./format";
import { KEEP_OPEN, openForm } from "./forms";
import { uuidv7 } from "./ids";
import { deviationValue } from "./lexware";
import { changedPrice, grossOf, netOf, type CatalogItem, type MaterialSummary, type PriceRounding } from "./quotations";

export const units = ["m²", "lfm", "Stück", "Sack", "t", "m³", "pauschal", "Std."];

function body(id: string, v: Record<string, unknown>, item: CatalogItem | null, overrides: Partial<CatalogItem> = {}): Record<string, unknown> {
	return {
		id, title: v.title, kind: v.kind, description: v.description || null, articleNumber: v.articleNumber || null, gtin: item?.gtin ?? null, unit: v.unit,
		netPrice: Number(v.netPrice ?? 0), taxRate: Number(v.taxRate ?? 19), materialId: v.materialId || null, isActive: v.isActive !== false,
		version: item?.version ?? null, ...overrides,
	};
}

/** Speichert einen Eintrag unverändert bis auf die genannten Werte (aktiv setzen, Preis ändern). */
export function saveWith(item: CatalogItem, overrides: Partial<CatalogItem>): Promise<CatalogItem> {
	return put<CatalogItem>(`lexware/articles/${item.id}`, body(item.id, { ...item, taxRate: item.taxRate }, item, overrides));
}

/**
 * Anlegen, Bearbeiten oder Duplizieren. Der Preis kann netto oder brutto eingegeben werden; die andere Angabe rechnet die
 * Maske sofort mit. Liefert die ID des gespeicherten Eintrags.
 */
export async function articleForm(item: CatalogItem | null, materials: MaterialSummary[], mode: "new" | "edit" | "duplicate" = item ? "edit" : "new"): Promise<string | null> {
	const source = item;
	const initial = source
		? { ...source, title: mode === "duplicate" ? `${source.title} (Kopie)` : source.title, articleNumber: mode === "duplicate" ? null : source.articleNumber,
			taxRate: String(source.taxRate), grossPrice: grossOf(source.netPrice, source.taxRate) }
		: { kind: "service", unit: "m²", taxRate: "19", isActive: true, netPrice: 0, grossPrice: 0 };
	let id: string | null = null;
	const sync = (key: "net" | "gross", form: { get: (k: string) => unknown; set: (k: string, v: unknown) => void }) => {
		const rate = Number(form.get("taxRate") ?? 19);
		if (key === "net") form.set("grossPrice", grossOf(Number(form.get("netPrice") ?? 0), rate));
		else form.set("netPrice", netOf(Number(form.get("grossPrice") ?? 0), rate));
	};
	const saved = await openForm({
		title: mode === "edit" ? "Eintrag bearbeiten" : mode === "duplicate" ? "Eintrag duplizieren" : "Eintrag anlegen", width: "40rem",
		intro: mode === "edit" && source?.lexwareId
			? "Mit Lexware Office verknüpft. Änderungen gehen nur bei der Richtung „Beide Richtungen“ nach Lexware Office; sonst bleiben sie als Abweichung sichtbar."
			: mode === "duplicate" ? "Die Kopie ist ein neuer Eintrag nur in EstrichONE. Passen Sie Bezeichnung und Artikelnummer an." : undefined,
		initial,
		fields: [
			{ key: "title", label: "Bezeichnung", required: true, maxLength: 255, placeholder: "z. B. Zementestrich CT-C25-F4 liefern und einbauen" },
			{ key: "kind", label: "Art", type: "select", required: true, options: [{ key: "service", text: "Leistung" }, { key: "product", text: "Material" }] },
			{ key: "description", label: "Beschreibung", type: "textarea", rows: 2, maxLength: 2000 },
			{ key: "articleNumber", label: "Artikelnummer", maxLength: 50 },
			{ key: "unit", label: "Einheit", type: "combo", required: true, options: units.map((u) => ({ key: u, text: u })) },
			{ key: "taxRate", label: "Umsatzsteuer", type: "select", options: [{ key: "19", text: "19 %" }, { key: "7", text: "7 %" }, { key: "0", text: "0 %" }],
				onChange: (_v, form) => sync("net", form) },
			{ key: "netPrice", label: "Preis je Einheit netto", type: "number", unit: "€", required: true, onChange: (_v, form) => sync("net", form),
				help: "Geben Sie netto oder brutto ein; der andere Wert wird umgerechnet. Gespeichert wird der Nettopreis." },
			{ key: "grossPrice", label: "Preis je Einheit brutto", type: "number", unit: "€", onChange: (_v, form) => sync("gross", form) },
			{ key: "materialId", label: "Verknüpftes Material (Materialstamm)", type: "select", options: materials.map((x) => ({ key: x.id, text: x.number ? `${x.number} ${x.name}` : x.name })) },
			{ key: "isActive", label: "Aktiv (in Belegen auswählbar)", type: "switch" },
		],
		submit: async (v) => {
			const target = mode === "edit" && source ? source.id : uuidv7();
			await put(`lexware/articles/${target}`, body(target, v, mode === "edit" ? source : null));
			id = target;
			return true;
		},
	});
	return saved ? id : null;
}

export async function resolveDeviations(item: CatalogItem, canPush: boolean): Promise<boolean> {
	const options = [{ key: "", text: "offen lassen" }, { key: "lexware", text: "Wert aus Lexware Office übernehmen" },
		...(canPush ? [{ key: "estrichOne", text: "Wert aus EstrichONE nach Lexware Office" }] : [])];
	const saved = await openForm<Record<string, string>>({
		title: `Abweichungen: ${item.title}`, width: "44rem",
		intro: canPush ? "Beide Seiten haben unterschiedliche Werte. Wählen Sie je Feld, welcher gilt." : "Die Richtung ist „Aus Lexware Office nach EstrichONE“. Sie können den Wert aus Lexware Office übernehmen; eigene Werte gehen nicht nach Lexware Office.",
		initial: {},
		fields: item.sync.deviations.map((d) => ({
			key: d.field, label: d.label, type: "select" as const, options,
			help: `EstrichONE: ${deviationValue(d.estrichOne)}. Lexware Office: ${deviationValue(d.lexware)}.`,
		})),
		submit: async (v) => {
			const fields = Object.fromEntries(Object.entries(v).filter(([, s]) => s === "lexware" || s === "estrichOne"));
			if (!Object.keys(fields).length) return KEEP_OPEN;
			await post(`lexware/articles/${item.id}/resolve`, { fields });
			return fields as Record<string, string>;
		},
	});
	return !!saved;
}

/** In Lexware Office anlegen; bei möglichem Treffer nur nach Rückfrage. */
export async function createInLexware(item: CatalogItem, confirm: (text: string) => Promise<boolean>): Promise<boolean> {
	try {
		await post(`lexware/articles/${item.id}/create`, {});
		return true;
	} catch (error) {
		if (!(error instanceof ApiError) || error.code !== "LEXWARE_ARTICLE_DUPLICATE") throw error;
		if (!await confirm(`${error.message} Trotzdem neu in Lexware Office anlegen?`)) return false;
		await post(`lexware/articles/${item.id}/create`, { createAnyway: true });
		return true;
	}
}

/**
 * Preise mehrerer Einträge um Prozent ändern: Vorschau mit altem und neuem Preis, gespeichert wird erst nach „Preise
 * übernehmen“. Liefert die Zahl der geänderten Einträge.
 */
export function massPriceChange(view: View, items: CatalogItem[]): Promise<number> {
	return new Promise((resolve) => {
		const percent = new StepInput({ value: 3, min: -50, max: 100, step: 0.5, displayValuePrecision: 1, description: "%", width: "11rem" });
		const rounding = new Select({ width: "14rem", items: [new Item({ key: "cent", text: "auf Cent" }), new Item({ key: "tenCent", text: "auf 10 Cent" }),
			new Item({ key: "euro", text: "auf ganze Euro" })] });
		const preview = new Table({ columns: [new Column({ header: new Text({ text: "Eintrag" }) }), new Column({ hAlign: "End", width: "8rem", header: new Text({ text: "Bisher netto" }) }),
			new Column({ hAlign: "End", width: "8rem", header: new Text({ text: "Neu netto" }) })] });
		const update = () => {
			preview.removeAllItems();
			for (const i of items) {
				preview.addItem(new ColumnListItem({ cells: [new Text({ text: i.title }), new Text({ text: money(i.netPrice) }),
					new Text({ text: money(changedPrice(i.netPrice, percent.getValue(), rounding.getSelectedKey() as PriceRounding)) })] }));
			}
		};
		percent.attachChange(update);
		rounding.attachChange(update);
		update();
		let done = 0;
		const dialog: Dialog = new Dialog({
			title: `Preise ändern (${items.length} ${items.length === 1 ? "Eintrag" : "Einträge"})`, contentWidth: "40rem",
			content: [new VBox({ items: [
				new Text({ text: "Alle ausgewählten Preise ändern sich um den Prozentsatz (negativ für eine Senkung). Prüfen Sie die Vorschau, bevor Sie übernehmen." }),
				new Label({ text: "Änderung", labelFor: percent }).addStyleClass("sapUiSmallMarginTop"), percent,
				new Label({ text: "Runden", labelFor: rounding }).addStyleClass("sapUiSmallMarginTop"), rounding, preview.addStyleClass("sapUiSmallMarginTop"),
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Preise übernehmen", type: "Emphasized", press: async () => {
				dialog.setBusy(true);
				try {
					for (const i of items) {
						await saveWith(i, { netPrice: changedPrice(i.netPrice, percent.getValue(), rounding.getSelectedKey() as PriceRounding) });
						done++;
					}
				} catch (error) {
					MessageBox.error(`${done} von ${items.length} Preisen geändert. ${error instanceof Error ? error.message : String(error)}`, { title: "Preise ändern" });
				}
				dialog.close();
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => { dialog.destroy(); resolve(done); },
		});
		view.addDependent(dialog);
		dialog.open();
	});
}
