import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Table from "sap/m/Table";
import type ListItemBase from "sap/m/ListItemBase";
import type Input from "sap/m/Input";
import MessageBox from "sap/m/MessageBox";
import SelectDialog from "sap/m/SelectDialog";
import StandardListItem from "sap/m/StandardListItem";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import CheckBox from "sap/m/CheckBox";
import VBox from "sap/m/VBox";
import Text from "sap/m/Text";
import Panel from "sap/m/Panel";
import MessageStrip from "sap/m/MessageStrip";
import { ApiError, apiUrl, del, get, post, put } from "../../util/api";
import { date, dateTime, money, parseNumber } from "../../util/format";
import { uuidv7 } from "../../util/ids";
import { openExternal } from "../../util/lexwareDialogs";
import type { RouteMatch } from "../../util/router";
import { fixAction, taxTypeHint, taxTypeOptions, type TaxTypeOption } from "../../util/quotations";
import {
	lineFromCatalog, lineFromSuggestion, lineNet, numberText, periodText, searchCatalog, statusText, subtotal, totals, withoutTax, type SalesLine, type SalesOptions,
	type SalesSuggestions, type SalesType, type SalesVoucher,
} from "../../util/sales";

/** Zeile der Positionstabelle: Werte der API und Eingabetexte (deutsche Zahlen). */
interface Row extends SalesLine {
	pos: string; quantityText: string; priceText: string; discountText: string; taxKey: string; netText: string; hint: string;
}

const num = (value: number | null | undefined, digits = 4, min = 0) =>
	value === null || value === undefined ? "" : new Intl.NumberFormat("de-DE", { minimumFractionDigits: min, maximumFractionDigits: digits, useGrouping: false }).format(value);
const nan = (value: number | null | undefined) => (value === null || value === undefined || Number.isNaN(value) ? 0 : value);

/**
 * Belegeditor des Verkaufs als SAP-Objektseite im Bearbeitungsmodus: Kopfdaten, Positionen mit Bearbeitung in der Zeile
 * (Tastatur: Enter in der Eingabezeile legt eine Position an), Texte, Summen live, Belegkette und Zahlungen. Fußleiste:
 * Speichern, Vorschau, An Lexware Office senden, Festschreiben (mit Bestätigung), Weitere (Duplizieren, Umwandeln, Verwerfen).
 * @namespace estrichone.controller.sales
 */
export default class Voucher extends BaseController {
	private id = "";
	private options: SalesOptions | null = null;
	private picked = 0;
	private keyHandler = (e: KeyboardEvent) => {
		if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s" && this.model().getProperty("/edit")) {
			e.preventDefault();
			void this.onSave();
		}
	};

	onInit(): void {
		this.local({
			v: null, lines: [], busy: false, dirty: false, edit: false, sending: false, numberText: "", bauakteText: "", sentText: "", fixText: "", taxHint: "",
			statusView: { text: "", state: "None" }, totalsView: {}, openText: "", paymentText: "", chain: [], followers: [], payments: [], taxTypes: taxTypeOptions,
			suggest: [], problemsText: "", noTax: false, paymentsModule: false,
		});
		document.addEventListener("keydown", this.keyHandler);
	}

	onExit(): void {
		document.removeEventListener("keydown", this.keyHandler);
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		this.id = match.params.id;
		this.options = null;
		await this.load();
	}

	private async load(refresh = false): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const v = await get<SalesVoucher>(`sales/vouchers/${this.id}`, refresh ? { refresh: "true" } : undefined);
			const options = this.options ?? await get<SalesOptions>("sales/options", { type: v.type });
			this.options = options;
			m.setProperty("/taxTypes", options.taxTypes.length ? options.taxTypes : taxTypeOptions);
			m.setProperty("/paymentsModule", options.paymentsModule);
			m.setProperty("/payments", [{ key: "", text: "Eigener Text oder keine" }, ...options.paymentConditions.map((p) => ({ key: p.id ?? p.label, text: p.label }))]);
			this.apply(v);
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private apply(v: SalesVoucher): void {
		const m = this.model();
		m.setProperty("/v", { ...v, paymentConditionId: v.paymentConditionId ?? "" });
		m.setProperty("/edit", v.editable && v.canEdit);
		m.setProperty("/numberText", numberText(v.lexwareNumber));
		m.setProperty("/bauakteText", v.bauakteId ? [v.bauakteNumber, v.bauakteTitle].filter(Boolean).join(" ") : "");
		m.setProperty("/statusView", statusText(v.status, v.type));
		m.setProperty("/sentText", v.sentAt
			? `${v.finalized ? "In Lexware Office festgeschrieben" : "Als Entwurf an Lexware Office gesendet"} am ${dateTime(v.sentAt)}. Ab jetzt gilt der Beleg in Lexware Office. Lexware Office erlaubt keine Änderung über die Schnittstelle; Änderungen, Versand und Buchung erfolgen dort.`
			: "");
		m.setProperty("/fixText", v.state === "failed" ? fixAction(v.sendErrorFix)?.text ?? "" : "");
		m.setProperty("/openText", v.openAmount === null || v.openAmount === undefined ? "" : money(v.openAmount));
		m.setProperty("/paymentText", v.paymentStatus === "balanced" ? "ausgeglichen" : v.paymentStatus === "openRevenue" ? "offen" : v.paymentStatus ?? "noch nicht abgeglichen");
		m.setProperty("/followers", v.state === "sent" ? v.followers : []);
		m.setProperty("/chain", v.chain.map((c) => {
			const s = statusText(c.status, c.type);
			return { ...c, statusText: s.text, statusState: s.state, info: [c.date ? date(c.date) : "", c.gross === null ? "" : money(c.gross)].filter(Boolean).join(", ") };
		}));
		m.setProperty("/lines", v.lines.map((l) => this.row(l)));
		m.setProperty("/dirty", false);
		this.refresh(v.problems);
	}

	private row(l: SalesLine): Row {
		const catalog = l.catalogItemId ? this.options?.catalog.find((c) => c.id === l.catalogItemId) : undefined;
		return {
			...l, pos: "", quantityText: num(l.quantity, 3), priceText: num(l.netPrice, 4, 2), discountText: num(l.discountPercent, 2), taxKey: String(l.taxRate ?? 19), netText: "",
			hint: l.kind === "deduction" ? "Abzug einer Abschlagsrechnung" : catalog ? `Aus Artikeln und Leistungen${catalog.inLexware ? ", als Artikel aus Lexware Office" : ""}` : "",
		};
	}

	private lines(): Row[] {
		return this.model().getProperty("/lines") as Row[];
	}

	/** Eingaben in Werte übernehmen, Positionsnummern, Zwischensummen und Summen live neu berechnen. */
	private refresh(serverProblems?: string[]): void {
		const m = this.model();
		const v = m.getProperty("/v") as SalesVoucher | null;
		if (!v) return;
		const rows = this.lines();
		const problems: string[] = [];
		let pos = 0;
		for (const r of rows) {
			r.pos = String(++pos);
			if (r.kind === "item" || r.kind === "deduction") {
				r.quantity = r.kind === "deduction" ? 1 : parseNumber(r.quantityText);
				r.netPrice = parseNumber(r.priceText);
				r.discountPercent = r.kind === "deduction" ? null : parseNumber(r.discountText);
				r.taxRate = Number(r.taxKey);
				for (const [value, label] of [[r.quantity, "Menge"], [r.netPrice, "Einzelpreis"], [r.discountPercent, "Rabatt"]] as const) {
					if (value !== null && Number.isNaN(value)) problems.push(`Position ${pos}: ${label} ist keine Zahl.`);
				}
			}
		}
		const clean = rows.map((r) => ({ ...r, quantity: nan(r.quantity), netPrice: nan(r.netPrice), discountPercent: nan(r.discountPercent) }));
		rows.forEach((r, i) => { r.netText = r.kind === "subtotal" ? money(subtotal(clean, i)) : r.kind === "text" ? "" : money(lineNet(clean[i])); });
		m.setProperty("/lines", rows);
		const t = totals(clean, v.taxType);
		const reverse = v.taxType === "constructionService13b";
		m.setProperty("/noTax", withoutTax(v.taxType));
		m.setProperty("/totalsView", {
			netText: money(t.net), grossText: money(t.gross),
			taxLines: t.taxLines.length && !withoutTax(v.taxType)
				? t.taxLines.map((l) => ({ label: `Umsatzsteuer ${num(l.rate, 0)} % auf ${money(l.net)}`, taxText: money(l.tax) }))
				: [{ label: reverse ? "Umsatzsteuer (§ 13b UStG: Steuerschuld beim Leistungsempfänger)" : v.taxType === "vatfree" ? "Umsatzsteuer (steuerfrei)" : "Umsatzsteuer", taxText: money(0) }],
			note: reverse ? "Bei Bauleistungen nach § 13b UStG schuldet der Leistungsempfänger die Umsatzsteuer. Lexware Office druckt den Hinweis auf dem Beleg." : "",
		});
		m.setProperty("/taxHint", taxTypeHint(m.getProperty("/taxTypes") as TaxTypeOption[], v.taxType));
		m.setProperty("/problemsText", [...problems, ...(serverProblems ?? [])].join(" "));
	}

	onTotals(): void {
		this.model().setProperty("/dirty", true);
		this.refresh();
	}

	onDirty(): void {
		this.model().setProperty("/dirty", true);
	}

	onPayment(): void {
		const m = this.model();
		const key = m.getProperty("/v/paymentConditionId") as string;
		const condition = this.options?.paymentConditions.find((p) => (p.id ?? p.label) === key);
		if (condition) m.setProperty("/v/paymentLabel", condition.label);
		this.onDirty();
	}

	private add(lines: SalesLine[]): void {
		this.model().setProperty("/lines", [...this.lines(), ...lines.map((l) => this.row(l))]);
		this.onTotals();
	}

	onAddItem(): void {
		this.add([{ kind: "item", name: "", quantity: 1, unit: "m²", netPrice: 0, taxRate: 19 }]);
	}

	onAddText(): void {
		this.add([{ kind: "text", name: "", description: "" }]);
	}

	onAddSubtotal(): void {
		this.add([{ kind: "subtotal", name: "Zwischensumme" }]);
	}

	/** Eingabezeile: Vorschlagsliste aus Artikeln und Leistungen. */
	onQuickType(e: Event): void {
		const text = String(e.getParameter("value" as never) ?? "");
		this.model().setProperty("/suggest", searchCatalog(this.options?.catalog ?? [], text).map((c) => ({
			id: c.id, title: c.title, info: [c.articleNumber, `${money(c.netPrice)} je ${c.unit ?? "Stück"}`].filter(Boolean).join(", "),
		})));
	}

	onQuickPick(e: Event): void {
		const item = e.getParameter("selectedItem" as never) as { getKey: () => string } | null;
		const catalog = this.options?.catalog.find((c) => c.id === item?.getKey());
		if (!catalog) return;
		this.add([lineFromCatalog(catalog)]);
		this.picked = Date.now();
		const input = e.getSource() as Input;
		// UI5 setzt den gewählten Text nach diesem Ereignis: danach leeren, damit die Eingabezeile wieder frei ist.
		setTimeout(() => { input.setValue(""); input.focus(); }, 0);
	}

	onQuickSubmit(e: Event): void {
		const input = e.getSource() as Input;
		const text = String(e.getParameter("value" as never) ?? input.getValue()).trim();
		if (!text) return;
		const last = this.lines().at(-1);
		if (last?.catalogItemId && last.name === text && Date.now() - this.picked < 1000) {
			input.setValue("");
			return; // eben über die Vorschlagsliste übernommen
		}
		const exact = this.options?.catalog.find((c) => c.title.toLowerCase() === text.toLowerCase() || (c.articleNumber ?? "").toLowerCase() === text.toLowerCase());
		this.add([exact ? lineFromCatalog(exact) : { kind: "item", name: text, quantity: 1, unit: "m²", netPrice: 0, taxRate: 19 }]);
		input.setValue("");
	}

	onCatalog(): void {
		const catalog = this.options?.catalog ?? [];
		if (!catalog.length) {
			MessageBox.information("In „Artikel und Leistungen“ gibt es noch keine Einträge. Legen Sie sie unter Material › Artikel und Leistungen an oder übernehmen Sie sie aus Lexware Office. Bis dahin erfassen Sie Positionen direkt in der Eingabezeile.",
				{ title: "Artikel suchen" });
			return;
		}
		const dialog = new SelectDialog({
			title: "Artikel suchen", multiSelect: true, rememberSelections: false, contentWidth: "40rem",
			items: catalog.map((c) => new StandardListItem({
				title: c.title, description: [c.articleNumber, c.unit ? `je ${c.unit}` : "", `${money(c.netPrice)} netto`, `${num(c.taxRate, 0)} %`].filter(Boolean).join(", "),
				info: c.inLexware ? "in Lexware Office" : "",
			}).data("id", c.id)),
			search: (e: Event) => {
				const q = String(e.getParameter("value" as never) ?? "").toLowerCase();
				(e.getSource() as SelectDialog).getItems().forEach((item) => item.setVisible(!q || (item as StandardListItem).getTitle().toLowerCase().includes(q)));
			},
			confirm: (e: Event) => {
				const selected = (e.getParameter("selectedItems" as never) as StandardListItem[] | undefined) ?? [];
				this.add(selected.map((item) => lineFromCatalog(catalog.find((c) => c.id === item.data("id"))!)));
				dialog.destroy();
			},
			cancel: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open("");
	}

	async onSuggest(): Promise<void> {
		const v = this.model().getProperty("/v") as SalesVoucher;
		const data = await this.run(() => get<SalesSuggestions>("sales/suggestions", { bauakteId: v.bauakteId! }));
		if (!data) return;
		const boxes = data.lines.map((s) => new CheckBox({
			selected: s.selected, wrapping: true,
			text: `${s.name}: ${num(s.quantity, 2)} ${s.unit}${s.netPrice ? ` zu ${money(s.netPrice)} netto` : ""} (${s.hint})`,
		}));
		const period = v.type !== "quotation" && data.serviceDate ? new CheckBox({ selected: !v.serviceDate, wrapping: true,
			text: `Leistungszeitraum aus den Einsätzen übernehmen: ${periodText(data.serviceDate, data.serviceEndDate, date)}` }) : null;
		const content = new VBox({ items: [
			new Text({ text: data.lines.length ? "Diese Positionen schlägt EstrichONE aus der Bauakte vor. Übernommen wird nur, was Sie auswählen; Mengen und Preise können Sie danach ändern." : "Für diese Bauakte gibt es keine Positionsvorschläge." }),
			...boxes, ...(period ? [period] : []),
			...data.hints.map((h) => new MessageStrip({ text: h, type: "Information", showIcon: true })),
		] }).addStyleClass("sapUiSmallMargin");
		const dialog: Dialog = new Dialog({
			title: "Vorschläge aus der Bauakte", contentWidth: "36rem", content: [content],
			beginButton: new Button({ text: "Übernehmen", type: "Emphasized", enabled: data.lines.length > 0 || !!period, press: () => {
				if (period?.getSelected()) {
					this.model().setProperty("/v/serviceDate", data.serviceDate);
					this.model().setProperty("/v/serviceEndDate", data.serviceEndDate);
				}
				this.add(data.lines.filter((_, i) => boxes[i].getSelected()).map(lineFromSuggestion));
				dialog.close();
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	private selected(): number[] {
		const table = this.byId("salesLines") as Table;
		return table.getSelectedItems().map((item: ListItemBase) => Number(item.getBindingContext("view")?.getPath().split("/").pop())).sort((a, b) => a - b);
	}

	private move(by: number): void {
		const picked = this.selected();
		if (!picked.length) {
			this.toast("Bitte wählen Sie zuerst Positionen aus.");
			return;
		}
		const rows = [...this.lines()];
		const order = by < 0 ? picked : [...picked].reverse();
		const moved: number[] = [];
		for (const i of order) {
			const j = i + by;
			if (j < 0 || j >= rows.length || (picked.includes(j) && !moved.includes(j))) { moved.push(i); continue; }
			[rows[i], rows[j]] = [rows[j], rows[i]];
			moved.push(j);
		}
		this.model().setProperty("/lines", rows);
		const table = this.byId("salesLines") as Table;
		table.removeSelections(true);
		this.onTotals();
		table.getItems().forEach((item, index) => { if (moved.includes(index)) table.setSelectedItem(item, true); });
	}

	onUp(): void { this.move(-1); }
	onDown(): void { this.move(1); }

	onRemove(): void {
		const picked = this.selected();
		if (!picked.length) {
			this.toast("Bitte wählen Sie zuerst Positionen aus.");
			return;
		}
		this.model().setProperty("/lines", this.lines().filter((_, i) => !picked.includes(i)));
		(this.byId("salesLines") as Table).removeSelections(true);
		this.onTotals();
	}

	private body(): Record<string, unknown> {
		const v = this.model().getProperty("/v") as SalesVoucher;
		const condition = this.options?.paymentConditions.find((p) => (p.id ?? p.label) === v.paymentConditionId);
		const fixed = condition && condition.label === v.paymentLabel;
		return {
			version: v.version, title: v.title, introduction: v.introduction || null, remark: v.remark || null, voucherDate: v.voucherDate || null, validUntil: v.validUntil || null,
			serviceDate: v.serviceDate || null, serviceEndDate: v.serviceEndDate || null, taxType: v.taxType, paymentConditionId: v.paymentConditionId || null,
			paymentLabel: v.paymentLabel || null, paymentDuration: fixed ? condition.duration : v.paymentDuration, paymentDiscountPercent: fixed ? condition.discountPercent : null,
			paymentDiscountRange: fixed ? condition.discountRange : null,
			lines: this.lines().map((r) => r.kind === "text" || r.kind === "subtotal"
				? { kind: r.kind, name: r.name || (r.kind === "text" ? "Hinweis" : "Zwischensumme"), description: r.description || null }
				: { kind: r.kind, name: r.name, description: r.description || null, catalogItemId: r.catalogItemId ?? null, quantity: r.quantity, unit: r.unit, netPrice: r.netPrice,
					taxRate: r.taxRate, discountPercent: r.discountPercent, deductionVoucherId: r.deductionVoucherId ?? null }),
		};
	}

	private async save(): Promise<SalesVoucher | undefined> {
		this.refresh();
		const text = this.model().getProperty("/problemsText") as string;
		if (/keine Zahl/.test(text)) {
			MessageBox.warning(text, { title: "Beleg speichern" });
			return undefined;
		}
		const saved = await this.run(() => put<SalesVoucher>(`sales/vouchers/${this.id}`, this.body()));
		if (saved) this.apply(saved);
		return saved;
	}

	async onSave(): Promise<void> {
		if (await this.save()) this.toast("Beleg gespeichert");
	}

	/** Bekannte Behebung aus der Fehlermeldung: Steuerart übernehmen, speichern und erneut senden. */
	async onFix(): Promise<void> {
		await this.applyFix((this.model().getProperty("/v") as SalesVoucher).sendErrorFix, false);
	}

	private async applyFix(fix: string | null | undefined, finalize: boolean): Promise<void> {
		const action = fixAction(fix);
		if (!action) return;
		this.model().setProperty("/v/taxType", action.taxType);
		this.onTotals();
		if (!await this.save()) return;
		await this.send(false, finalize);
	}

	async onSend(): Promise<void> {
		await this.send(true, false);
	}

	/** Festschreiben: ausdrücklich bestätigen (Nummer wird vergeben, danach nicht mehr änderbar). */
	async onFinalize(): Promise<void> {
		const m = this.model();
		if (m.getProperty("/dirty") && !await this.save()) return;
		const v = m.getProperty("/v") as SalesVoucher;
		if (v.problems.length) {
			MessageBox.warning(v.problems.join("\n"), { title: "Noch nicht sendebereit" });
			return;
		}
		const check = new CheckBox({ text: "Ich habe den Beleg geprüft und möchte ihn festschreiben.", wrapping: true });
		const dialog: Dialog = new Dialog({
			title: `${v.typeName} festschreiben`, type: "Message", state: "Warning", contentWidth: "30rem",
			content: [new VBox({ items: [
				new Text({ text: `Der Beleg wird in Lexware Office angelegt und festgeschrieben. Lexware Office vergibt die Nummer; danach lässt sich der Beleg nicht mehr ändern, nur noch stornieren. Summe brutto: ${money(v.totals.gross)}.` }),
				check.addStyleClass("sapUiSmallMarginTop"),
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ id: this.createId("salesFinalizeConfirm"), text: "Festschreiben", type: "Emphasized", enabled: false, press: () => { dialog.close(); void this.send(false, true); } }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		check.attachSelect(() => (dialog.getBeginButton() as Button).setEnabled(check.getSelected()));
		this.v().addDependent(dialog);
		dialog.open();
	}

	private taxTypeFix(v: SalesVoucher): string | null {
		const option = (this.model().getProperty("/taxTypes") as TaxTypeOption[]).find((o) => o.key === v.taxType);
		if (!option || option.allowed) return null;
		return this.options?.smallBusiness ? "taxTypeVatfree" : "taxTypeNet";
	}

	private async send(ask: boolean, finalize: boolean): Promise<void> {
		const m = this.model();
		if (m.getProperty("/dirty") && !await this.save()) return;
		const v = m.getProperty("/v") as SalesVoucher;
		const taxFix = this.taxTypeFix(v);
		if (taxFix) {
			this.showProblem(v.problems.join(" "), taxFix, null, finalize);
			return;
		}
		if (v.problems.length) {
			MessageBox.warning(v.problems.join("\n"), { title: "Noch nicht sendebereit" });
			return;
		}
		if (ask && !await this.confirm(`${v.typeName} wird als Entwurf in Lexware Office angelegt. Nummer, Versand und Buchung erfolgen dort. Danach ist der Beleg in EstrichONE schreibgeschützt. Jetzt senden?`,
			{ title: "An Lexware Office senden", action: "Senden" })) return;
		m.setProperty("/sending", true);
		try {
			this.apply(await post<SalesVoucher>(`sales/vouchers/${this.id}/send`, finalize ? { finalize: true, confirmed: true } : {}));
			this.toast(finalize ? "In Lexware Office festgeschrieben" : "Als Entwurf an Lexware Office gesendet");
		} catch (error) {
			this.options = null; // erlaubte Steuerarten können sich durch die Ablehnung geändert haben
			await this.load();
			const problem = error instanceof ApiError ? error.problem as { technical?: string; fix?: string } | undefined : undefined;
			this.showProblem(`Das Senden an Lexware Office ist fehlgeschlagen. ${error instanceof ApiError ? error.message : String(error)}`, problem?.fix, problem?.technical, finalize);
		} finally {
			m.setProperty("/sending", false);
		}
	}

	/** Fehler mit verständlicher Meldung, direkter Behebung (falls bekannt) und aufklappbarem Originaltext. */
	private showProblem(message: string, fix: string | null | undefined, technical: string | null | undefined, finalize: boolean): void {
		const action = fixAction(fix);
		const content = new VBox({ items: [
			new Text({ text: message }),
			...(action ? [new Text({ text: "Mit der Schaltfläche unten wird die Steuerart im Beleg geändert, gespeichert und der Beleg erneut gesendet." }).addStyleClass("sapUiSmallMarginTop")] : []),
			...(technical ? [new Panel({ headerText: "Technische Einzelheiten", expandable: true, expanded: false, content: [new Text({ text: technical })] }).addStyleClass("sapUiSmallMarginTop")] : []),
		] }).addStyleClass("sapUiSmallMargin");
		const dialog: Dialog = new Dialog({
			title: "Senden an Lexware Office", type: "Message", state: "Error", contentWidth: "32rem", content: [content],
			beginButton: action ? new Button({ text: action.text, type: "Emphasized", press: () => { dialog.close(); void this.applyFix(fix, finalize); } }) : undefined,
			endButton: new Button({ text: action ? "Abbrechen" : "Schließen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	async onMore(e: Event): Promise<void> {
		const key = (e.getParameter("item" as never) as { getKey: () => string }).getKey();
		const v = this.model().getProperty("/v") as SalesVoucher;
		if (key === "discard") {
			if (!await this.confirm("Diesen Beleg in EstrichONE verwerfen? In Lexware Office wurde noch nichts angelegt.", { action: "Verwerfen", danger: true })) return;
			if (await this.run(async () => { await del(`sales/vouchers/${this.id}`); return true; }, "Beleg verworfen")) {
				this.nav(v.bauakteId ? `/bauakten/${v.bauakteId}?abschnitt=rechnungen` : "/verkauf");
			}
			return;
		}
		if (this.model().getProperty("/dirty") && !await this.save()) return;
		const type = key === "duplicate" ? undefined : key as SalesType;
		const copy = await this.run(() => post<SalesVoucher>(`sales/vouchers/${this.id}/copy`, { id: uuidv7(), type }));
		if (copy) {
			this.toast(type ? `${copy.typeName} angelegt` : "Beleg dupliziert");
			this.nav(`/verkauf/${copy.id}`);
		}
	}

	onPreview(): void {
		const v = this.model().getProperty("/v") as SalesVoucher;
		if (v.lexwareId) window.open(apiUrl(`lexware/customers/${v.contactId}/vouchers/${v.lexwareId}/document`), "_blank", "noopener");
	}

	async onRefreshPayment(): Promise<void> {
		await this.load(true);
		this.toast("Zahlstand aus Lexware Office gelesen");
	}

	onChainOpen(e: Event): void {
		const item = (e.getSource() as Control).getBindingContext("view")?.getObject() as { id: string | null; current: boolean };
		if (item.id && !item.current) this.nav(`/verkauf/${item.id}`);
	}

	onOpenLexware(): void {
		openExternal((this.model().getProperty("/v") as SalesVoucher).lexwareUrl);
	}

	onBack(): void {
		this.nav("/verkauf");
	}

	onBauakte(): void {
		const v = this.model().getProperty("/v") as SalesVoucher;
		if (v.bauakteId) this.nav(`/bauakten/${v.bauakteId}?abschnitt=rechnungen`);
	}

	onCustomer(): void {
		this.nav(`/kunden/${(this.model().getProperty("/v") as SalesVoucher).contactId}`);
	}
}
