import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import SelectDialog from "sap/m/SelectDialog";
import StandardListItem from "sap/m/StandardListItem";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import CheckBox from "sap/m/CheckBox";
import VBox from "sap/m/VBox";
import Text from "sap/m/Text";
import MessageStrip from "sap/m/MessageStrip";
import Panel from "sap/m/Panel";
import { ApiError, del, get, post, put } from "../../util/api";
import { dateTime, money, parseNumber } from "../../util/format";
import { openExternal } from "../../util/lexwareDialogs";
import type { RouteMatch } from "../../util/router";
import {
	BETA_TEXT, fixAction, lineFromCatalog, lineFromSuggestion, lineNet, quotationStateText, quotationTotals, taxTypeHint, taxTypeOptions,
	type Quotation as QuotationData, type QuotationLine, type QuotationOptions, type QuotationSuggestions, type TaxTypeOption,
} from "../../util/quotations";

/** Zeile der Positionstabelle: Werte der API und Eingabetexte (deutsche Zahlen). */
interface Row extends QuotationLine {
	pos: string; quantityText: string; priceText: string; discountText: string; taxKey: string; netText: string; sourceText: string;
}

const num = (value: number | null | undefined, digits = 4, min = 0) =>
	value === null || value === undefined ? "" : new Intl.NumberFormat("de-DE", { minimumFractionDigits: min, maximumFractionDigits: digits, useGrouping: false }).format(value);

/**
 * Angebot (Beta): Kopf, Positionen und Summen eines Angebotsentwurfs. Speichern in EstrichONE, dann „Als Entwurf an
 * Lexware Office senden“. Danach ist der Entwurf hier schreibgeschützt; der Beleg gilt in Lexware Office.
 * @namespace estrichone.controller.office
 */
export default class Quotation extends BaseController {
	private id = "";
	private options: QuotationOptions | null = null;

	onInit(): void {
		this.local({
			q: null, lines: [], totals: {}, busy: false, dirty: false, sending: false, stateText: "", stateState: "None", subtitle: "", sentText: "",
			betaText: BETA_TEXT, taxTypes: taxTypeOptions, taxHint: "", fixText: "", payments: [], options: null, problemsText: "",
		});
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		this.id = match.params.id;
		await this.load();
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const [q, options] = await Promise.all([get<QuotationData>(`lexware/quotations/${this.id}`), this.options ? Promise.resolve(this.options) : get<QuotationOptions>("lexware/quotations/options")]);
			this.options = options;
			m.setProperty("/options", options);
			m.setProperty("/taxTypes", options.taxTypes?.length ? options.taxTypes : taxTypeOptions);
			m.setProperty("/payments", [{ key: "", text: "Eigener Text oder keine" }, ...options.paymentConditions.map((p) => ({ key: p.id ?? p.label, text: p.label }))]);
			this.apply(q);
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private apply(q: QuotationData): void {
		const m = this.model();
		m.setProperty("/q", { ...q, paymentConditionId: q.paymentConditionId ?? "" });
		const state = quotationStateText(q.state);
		m.setProperty("/stateText", state.text);
		m.setProperty("/stateState", state.state);
		m.setProperty("/subtitle", [q.customerName, q.bauakteId ? `Bauakte ${q.bauakteNumber ?? ""} ${q.bauakteTitle ?? ""}`.trim() : ""].filter(Boolean).join(", "));
		m.setProperty("/sentText", q.sentAt ? dateTime(q.sentAt) : "");
		m.setProperty("/lines", q.lines.map((l) => this.row(l)));
		m.setProperty("/fixText", q.state === "failed" ? fixAction(q.sendErrorFix)?.text ?? "" : "");
		m.setProperty("/dirty", false);
		this.refreshTotals(q.problems);
	}

	private row(l: QuotationLine): Row {
		const catalog = l.catalogItemId ? this.options?.catalog.find((c) => c.id === l.catalogItemId) : undefined;
		return {
			...l, pos: "", quantityText: num(l.quantity, 3), priceText: num(l.netPrice, 4, 2), discountText: num(l.discountPercent, 2), taxKey: String(l.taxRate ?? 19), netText: "",
			sourceText: catalog ? `Aus Artikeln und Leistungen${catalog.inLexware ? ", als Artikel aus Lexware Office" : ""}` : "",
		};
	}

	/** Eingaben in Werte übernehmen, Positionsnummern und Summen live neu berechnen. */
	private refreshTotals(serverProblems?: string[]): void {
		const m = this.model();
		const q = m.getProperty("/q") as QuotationData | null;
		if (!q) return;
		const rows = (m.getProperty("/lines") as Row[]).map((r) => r);
		const problems: string[] = [];
		let pos = 0;
		for (const r of rows) {
			r.pos = String(++pos);
			if (r.kind === "item") {
				r.quantity = parseNumber(r.quantityText);
				r.netPrice = parseNumber(r.priceText);
				r.discountPercent = parseNumber(r.discountText);
				r.taxRate = Number(r.taxKey);
				for (const [value, label] of [[r.quantity, "Menge"], [r.netPrice, "Einzelpreis"], [r.discountPercent, "Rabatt"]] as const) {
					if (Number.isNaN(value)) problems.push(`Position ${pos}: ${label} ist keine Zahl.`);
				}
				r.netText = money(lineNet({ quantity: Number.isNaN(r.quantity) ? 0 : r.quantity, netPrice: Number.isNaN(r.netPrice) ? 0 : r.netPrice, discountPercent: Number.isNaN(r.discountPercent) ? 0 : r.discountPercent }));
			} else {
				r.netText = "";
			}
		}
		m.setProperty("/lines", rows);
		const totals = quotationTotals(rows.map((r) => ({ ...r, quantity: Number.isNaN(r.quantity) ? 0 : r.quantity, netPrice: Number.isNaN(r.netPrice) ? 0 : r.netPrice })), q.taxType);
		const reverse = q.taxType === "constructionService13b";
		m.setProperty("/taxHint", taxTypeHint(m.getProperty("/taxTypes") as never, q.taxType));
		m.setProperty("/totals", {
			netText: money(totals.net), grossText: money(totals.gross), taxText: money(totals.tax),
			taxLabel: reverse ? "Umsatzsteuer (§ 13b UStG: Steuerschuld beim Leistungsempfänger)" : q.taxType === "vatfree" ? "Umsatzsteuer (steuerfrei)" : `Umsatzsteuer ${totals.taxLines.filter((t) => t.rate > 0).map((t) => `${num(t.rate, 0)} %`).join(" und ") || "0 %"}`,
		});
		m.setProperty("/problemsText", [...problems, ...(serverProblems ?? [])].join(" "));
	}

	onTotals(): void {
		this.model().setProperty("/dirty", true);
		this.refreshTotals();
	}

	onDirty(): void {
		this.model().setProperty("/dirty", true);
	}

	onPayment(): void {
		const m = this.model();
		const key = m.getProperty("/q/paymentConditionId") as string;
		const condition = this.options?.paymentConditions.find((p) => (p.id ?? p.label) === key);
		if (condition) m.setProperty("/q/paymentLabel", condition.label);
		this.onDirty();
	}

	private lines(): Row[] {
		return this.model().getProperty("/lines") as Row[];
	}

	private add(lines: QuotationLine[]): void {
		this.model().setProperty("/lines", [...this.lines(), ...lines.map((l) => this.row(l))]);
		this.onTotals();
	}

	onAddFree(): void {
		this.add([{ kind: "item", name: "", quantity: 1, unit: "Stück", netPrice: 0, taxRate: 19 }]);
	}

	onAddText(): void {
		this.add([{ kind: "text", name: "", description: "" }]);
	}

	onCatalog(): void {
		const catalog = this.options?.catalog ?? [];
		if (!catalog.length) {
			MessageBox.information("In „Artikel und Leistungen“ gibt es noch keine Einträge. Legen Sie sie unter Material › Artikel und Leistungen an oder übernehmen Sie sie aus Lexware Office. Bis dahin können Sie freie Positionen erfassen.",
				{ title: "Aus Artikeln und Leistungen" });
			return;
		}
		const dialog = new SelectDialog({
			title: "Aus Artikeln und Leistungen", multiSelect: true, rememberSelections: false, contentWidth: "40rem",
			items: catalog.map((c) => new StandardListItem({
				title: c.title, description: [c.articleNumber, c.unit ? `je ${c.unit}` : "", `${money(c.netPrice)} netto`, `${num(c.taxRate, 0)} %`].filter(Boolean).join(", "),
				info: c.inLexware ? "in Lexware Office" : "", customData: [],
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
		const q = this.model().getProperty("/q") as QuotationData;
		const data = await this.run(() => get<QuotationSuggestions>("lexware/quotations/suggestions", { bauakteId: q.bauakteId! }));
		if (!data) return;
		const boxes = data.lines.map((s) => new CheckBox({
			selected: true, wrapping: true,
			text: `${s.name}: ${num(s.quantity, 2)} ${s.unit}${s.netPrice ? ` zu ${money(s.netPrice)} netto` : ""} (${s.hint})`,
		}));
		const content = new VBox({ items: [
			new Text({ text: data.lines.length ? "Diese Positionen schlägt EstrichONE aus der Bauakte vor. Übernommen wird nur, was Sie auswählen; Mengen und Preise können Sie danach ändern." : "Für diese Bauakte gibt es keine Vorschläge." }),
			...boxes,
			...data.hints.map((h) => new MessageStrip({ text: h, type: "Information", showIcon: true })),
		] }).addStyleClass("sapUiSmallMargin");
		const dialog: Dialog = new Dialog({
			title: "Vorschläge aus der Bauakte", contentWidth: "36rem", content: [content],
			beginButton: new Button({ text: "Übernehmen", type: "Emphasized", enabled: data.lines.length > 0, press: () => {
				this.add(data.lines.filter((_, i) => boxes[i].getSelected()).map(lineFromSuggestion));
				dialog.close();
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	private index(e: Event): number {
		const path = (e.getSource() as Control).getBindingContext("view")?.getPath() ?? "";
		return Number(path.split("/").pop());
	}

	private move(e: Event, by: number): void {
		const i = this.index(e);
		const rows = [...this.lines()];
		const j = i + by;
		if (j < 0 || j >= rows.length) return;
		[rows[i], rows[j]] = [rows[j], rows[i]];
		this.model().setProperty("/lines", rows);
		this.onTotals();
	}

	onUp(e: Event): void { this.move(e, -1); }
	onDown(e: Event): void { this.move(e, 1); }

	onRemove(e: Event): void {
		const rows = [...this.lines()];
		rows.splice(this.index(e), 1);
		this.model().setProperty("/lines", rows);
		this.onTotals();
	}

	private body(): Record<string, unknown> {
		const q = this.model().getProperty("/q") as QuotationData;
		return {
			version: q.version, title: q.title, introduction: q.introduction || null, remark: q.remark || null, validUntil: q.validUntil || null, taxType: q.taxType,
			paymentConditionId: q.paymentConditionId || null, paymentLabel: q.paymentLabel || null,
			...(() => {
				const c = this.options?.paymentConditions.find((p) => (p.id ?? p.label) === q.paymentConditionId);
				return c && c.label === q.paymentLabel
					? { paymentDuration: c.duration, paymentDiscountPercent: c.discountPercent, paymentDiscountRange: c.discountRange }
					: { paymentDuration: null, paymentDiscountPercent: null, paymentDiscountRange: null };
			})(),
			lines: this.lines().map((r) => r.kind === "text"
				? { kind: "text", name: r.name || "Hinweis", description: r.description || null }
				: { kind: "item", name: r.name, description: r.description || null, catalogItemId: r.catalogItemId ?? null, quantity: r.quantity, unit: r.unit, netPrice: r.netPrice,
					taxRate: r.taxRate, discountPercent: r.discountPercent }),
		};
	}

	private async save(): Promise<QuotationData | undefined> {
		this.refreshTotals();
		if (this.model().getProperty("/problemsText") && /keine Zahl/.test(this.model().getProperty("/problemsText") as string)) {
			MessageBox.warning(this.model().getProperty("/problemsText") as string, { title: "Angebot speichern" });
			return undefined;
		}
		const saved = await this.run(() => put<QuotationData>(`lexware/quotations/${this.id}`, this.body()));
		if (saved) this.apply(saved);
		return saved;
	}

	async onSave(): Promise<void> {
		if (await this.save()) this.toast("Entwurf gespeichert");
	}

	/** Bekannte Behebung aus der Fehlermeldung: Steuerart übernehmen, speichern und erneut senden (ohne weitere Rückfrage). */
	async onFix(): Promise<void> {
		await this.applyFix((this.model().getProperty("/q") as QuotationData).sendErrorFix);
	}

	private async applyFix(fix: string | null | undefined): Promise<void> {
		const action = fixAction(fix);
		if (!action) return;
		this.model().setProperty("/q/taxType", action.taxType);
		this.onTotals();
		if (!await this.save()) return;
		await this.send(false);
	}

	async onSend(): Promise<void> {
		await this.send(true);
	}

	/** Fehler beim Senden: deutsche Meldung, wo bekannt mit direkter Behebung, englischer Originaltext nur aufklappbar. */
	private showSendError(error: unknown): void {
		const problem = error instanceof ApiError ? error.problem as { technical?: string; fix?: string } | undefined : undefined;
		this.showProblem(`Das Senden an Lexware Office ist fehlgeschlagen. ${error instanceof ApiError ? error.message : String(error)}`, problem?.fix, problem?.technical);
	}

	private showProblem(message: string, fix: string | null | undefined, technical?: string | null): void {
		const action = fixAction(fix);
		const content = new VBox({ items: [
			new Text({ text: message }),
			...(action ? [new Text({ text: "Mit der Schaltfläche unten wird die Steuerart im Entwurf geändert, gespeichert und das Angebot erneut gesendet." }).addStyleClass("sapUiSmallMarginTop")] : []),
			...(technical ? [new Panel({ headerText: "Technische Einzelheiten", expandable: true, expanded: false, content: [new Text({ text: technical })] })
				.addStyleClass("sapUiSmallMarginTop")] : []),
		] }).addStyleClass("sapUiSmallMargin");
		const dialog: Dialog = new Dialog({
			title: "Senden an Lexware Office", type: "Message", state: "Error", contentWidth: "32rem", content: [content],
			beginButton: action ? new Button({ text: action.text, type: "Emphasized", press: () => { dialog.close(); void this.applyFix(fix); } }) : undefined,
			endButton: new Button({ text: action ? "Abbrechen" : "Schließen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	/** Die gewählte Steuerart lässt Lexware Office nicht zu (gemerkt): Behebung wie nach der Ablehnung anbieten. */
	private taxTypeFix(q: QuotationData): string | null {
		const option = (this.model().getProperty("/taxTypes") as TaxTypeOption[]).find((o) => o.key === q.taxType);
		if (!option || option.allowed) return null;
		return this.options?.smallBusiness ? "taxTypeVatfree" : "taxTypeNet";
	}

	private async send(ask: boolean): Promise<void> {
		const m = this.model();
		if (m.getProperty("/dirty") && !await this.save()) return;
		const q = m.getProperty("/q") as QuotationData;
		const taxFix = this.taxTypeFix(q);
		if (taxFix) {
			this.showProblem(q.problems.join(" "), taxFix);
			return;
		}
		if (q.problems.length) {
			MessageBox.warning(q.problems.join("\n"), { title: "Noch nicht sendebereit" });
			return;
		}
		if (ask && !await this.confirm("Das Angebot wird als Entwurf in Lexware Office angelegt. Nummer, Festschreiben und Versand erfolgen dort. Danach ist der Entwurf in EstrichONE schreibgeschützt. Jetzt senden?",
			{ title: "Als Entwurf an Lexware Office senden", action: "Senden" })) return;
		m.setProperty("/sending", true);
		try {
			this.apply(await post<QuotationData>(`lexware/quotations/${this.id}/send`));
			this.toast("Als Entwurf an Lexware Office gesendet");
		} catch (error) {
			this.options = null; // erlaubte Steuerarten können sich durch die Ablehnung geändert haben
			await this.load();
			this.showSendError(error);
		} finally {
			m.setProperty("/sending", false);
		}
	}

	async onDiscard(): Promise<void> {
		if (!await this.confirm("Diesen Angebotsentwurf in EstrichONE verwerfen? In Lexware Office wurde noch nichts angelegt.", { action: "Verwerfen", danger: true })) return;
		const q = this.model().getProperty("/q") as QuotationData;
		if (await this.run(async () => { await del(`lexware/quotations/${this.id}`); return true; }, "Entwurf verworfen")) {
			this.nav(q.bauakteId ? `/bauakten/${q.bauakteId}` : `/kunden/${q.contactId}`);
		}
	}

	onOpenLexware(): void {
		openExternal((this.model().getProperty("/q") as QuotationData).lexwareUrl);
	}

	onBauakte(): void {
		this.nav(`/bauakten/${(this.model().getProperty("/q") as QuotationData).bauakteId}`);
	}

	onCustomer(): void {
		this.nav(`/kunden/${(this.model().getProperty("/q") as QuotationData).contactId}`);
	}
}
