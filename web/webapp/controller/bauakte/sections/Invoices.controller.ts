import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get } from "../../../util/api";
import { date } from "../../../util/format";
import {
	euro, pdfAvailable, PDF_DRAFT_HINT, voucherNumber, voucherPdfPath, voucherStatus, voucherSummary, voucherTitle, voucherTypeText, type BauakteLexware,
	type LexwareVoucher,
} from "../../../util/lexware";
import { session } from "../../../util/session";
import { startPlanVoucher, startVoucher } from "../../../util/salesStart";
import { planProgress, statusText, type SalesPlan, type SalesListItem } from "../../../util/sales";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import RadioButton from "sap/m/RadioButton";
import StepInput from "sap/m/StepInput";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import { downloadVoucherPdf, openAssignVoucherDialog, openExternal, openVoucherPdf, type VoucherPdf } from "../../../util/lexwareDialogs";

/**
 * Rechnungen und Angebote der Bauakte aus Lexware Office (nur Anzeige): Art, Nummer, Datum, Betrag und Status, der Beleg
 * als PDF, „In Lexware öffnen“ und die Zuordnung vorhandener Belege. Erstellt und bearbeitet werden Belege ausschließlich in Lexware Office.
 * @namespace estrichone.controller.bauakte.sections
 */
export default class Invoices extends SectionBase {
	protected init(): void {
		this.v().setModel(new JSONModel({
			loaded: false, configured: false, canAssign: false, vouchers: [], summary: "", syncError: "", drafts: [], canQuote: false, canInvoice: false, plan: null,
			planBar: { billed: 0, label: "" }, planDetail: "",
		}), "inv");
	}

	private inv(): JSONModel {
		return this.v().getModel("inv") as JSONModel;
	}

	protected async load(): Promise<void> {
		this.apply(await get<BauakteLexware>(`bauakten/${this.bauakteId}/lexware`));
		await this.loadPlan();
	}

	private salesOn(): boolean {
		return session.moduleOn("lexware") && session.moduleOn("sales");
	}

	/** Rechnungsplan und Belege in Vorbereitung (Modul Verkauf). */
	private async loadPlan(): Promise<void> {
		const m = this.inv();
		const canQuote = this.salesOn() && session.moduleOn("quotations") && this.has("quotations.prepare");
		const canInvoice = this.salesOn() && session.moduleOn("salesInvoices") && this.has("salesinvoices.edit");
		m.setProperty("/canQuote", canQuote);
		m.setProperty("/canInvoice", canInvoice);
		const canRead = this.salesOn() && ["quotations.read", "quotations.prepare", "orderconfirmations.read", "salesinvoices.read", "creditnotes.read"].some((p) => this.has(p));
		if (!canRead) { m.setProperty("/plan", null); m.setProperty("/drafts", []); return; }
		const plan = await get<SalesPlan>(`sales/bauakten/${this.bauakteId}/plan`).catch(() => null);
		m.setProperty("/plan", plan);
		if (!plan) { m.setProperty("/drafts", []); return; }
		const bar = planProgress(plan);
		m.setProperty("/planBar", { billed: bar.billed, label: `${bar.billed} % der Auftragssumme berechnet` });
		m.setProperty("/planDetail", plan.progressPercent !== null ? `Leistungsstand laut Räume und Flächen: ${plan.progressPercent.toLocaleString("de-DE")} % der Fläche eingebracht.` : "");
		m.setProperty("/drafts", plan.vouchers.filter((v) => !v.lexwareId).map((v: SalesListItem) => {
			const state = statusText(v.status, v.type);
			return { ...v, stateText: state.text, stateState: state.state, netText: euro(v.net), grossText: euro(v.gross) };
		}));
	}

	async onCreateQuotation(): Promise<void> {
		const id = await startVoucher("quotation", { bauakteId: this.bauakteId }, (contactId) => this.nav(`/kunden/${contactId}`));
		if (id) this.nav(`/verkauf/${id}`);
	}

	onOpenDraft(e: Event): void {
		const v = (e.getSource() as Control).getBindingContext("inv")?.getObject() as SalesListItem;
		if (v.id) this.nav(`/verkauf/${v.id}`);
	}

	/** Abschlag: Vorschlag aus dem Leistungsstand oder Prozent der Auftragssumme bzw. fester Betrag. */
	onDownPayment(): void {
		const plan = this.inv().getProperty("/plan") as SalesPlan;
		const byPercent = new RadioButton({ text: "Prozent der Auftragssumme", selected: !!plan.orderNet, enabled: !!plan.orderNet, groupName: "dp" });
		const byAmount = new RadioButton({ text: "Fester Betrag netto", selected: !plan.orderNet, groupName: "dp" });
		const percent = new StepInput({ value: plan.suggestedPercent && plan.suggestedPercent > 0 ? plan.suggestedPercent : 30, min: 1, max: 100, step: 5, displayValuePrecision: 0,
			description: "%", width: "10rem" });
		const amount = new StepInput({ value: 1000, min: 1, max: 100000000, step: 100, displayValuePrecision: 2, description: "€", width: "12rem" });
		const dialog: Dialog = new Dialog({
			title: "Abschlag erstellen", contentWidth: "32rem",
			content: [new VBox({ items: [
				new Text({ text: plan.orderNet
					? `Auftragssumme ${euro(plan.orderNet)} netto (${plan.orderSource}). Bisher ${plan.downPaymentCount} ${plan.downPaymentCount === 1 ? "Abschlag" : "Abschläge"} über ${euro(plan.downPaymentsNet)}.${plan.suggestedPercent !== null ? ` Vorschlag nach Leistungsstand: ${plan.suggestedPercent} %.` : ""}`
					: "Für diese Bauakte gibt es noch keine Auftragssumme. Geben Sie den Betrag des Abschlags an." }),
				byPercent, percent, byAmount, amount,
				new Text({ text: "EstrichONE legt die Abschlagsrechnung als Beleg an. Sie prüfen sie im Belegeditor und senden sie dann an Lexware Office." }).addStyleClass("eoHelpText"),
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Abschlag anlegen", type: "Emphasized", press: async () => {
				dialog.close();
				const id = await startPlanVoucher("down-payment", this.bauakteId, byPercent.getSelected() ? { percent: percent.getValue() } : { amount: amount.getValue() },
					(contactId) => this.nav(`/kunden/${contactId}`));
				if (id) this.nav(`/verkauf/${id}`);
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	async onFinalInvoice(): Promise<void> {
		const plan = this.inv().getProperty("/plan") as SalesPlan;
		if (plan.finalInvoiceExists && !await this.confirm("Für diese Bauakte gibt es bereits eine Schlussrechnung. Trotzdem eine weitere anlegen?", { action: "Anlegen" })) return;
		const id = await startPlanVoucher("final-invoice", this.bauakteId, {}, (contactId) => this.nav(`/kunden/${contactId}`));
		if (id) this.nav(`/verkauf/${id}`);
	}

	private apply(data: BauakteLexware): void {
		const m = this.inv();
		m.setProperty("/loaded", true);
		m.setProperty("/configured", data.configured);
		m.setProperty("/canAssign", data.canAssign);
		m.setProperty("/syncError", data.syncError ?? "");
		m.setProperty("/summary", voucherSummary(data.vouchers));
		m.setProperty("/vouchers", data.vouchers.map((v: LexwareVoucher) => {
			const status = voucherStatus(v.status, v.type);
			return {
				...v, typeText: voucherTypeText(v.type), numberText: voucherNumber(v), dateText: date(v.voucherDate), dueText: date(v.dueDate),
				netText: euro(v.netAmount, v.currency), grossText: euro(v.grossAmount, v.currency), statusText: status.text, statusState: status.state, statusIcon: status.icon,
				pdf: pdfAvailable(v), pdfHint: pdfAvailable(v) ? "Beleg als PDF anzeigen" : PDF_DRAFT_HINT,
			};
		}));
	}

	async onRefresh(): Promise<void> {
		const data = await this.run(() => get<BauakteLexware>(`bauakten/${this.bauakteId}/lexware`, { refresh: true }));
		if (!data) return;
		this.apply(data);
		await this.loadPlan();
		if (!data.syncError) this.toast("Status mit Lexware Office abgeglichen");
	}

	async onAssign(): Promise<void> {
		if (await openAssignVoucherDialog(this.bauakteId)) await this.reload();
	}

	private row(e: Event): LexwareVoucher {
		return (e.getSource() as Control).getBindingContext("inv")?.getObject() as LexwareVoucher;
	}

	onOpen(e: Event): void {
		openExternal(this.row(e).url);
	}

	private pdf(e: Event): VoucherPdf {
		const v = this.row(e);
		return { path: voucherPdfPath({ bauakteId: this.bauakteId, linkId: v.id }), title: voucherTitle(v), lexwareUrl: v.url };
	}

	onPdf(e: Event): void {
		void openVoucherPdf(this.pdf(e));
	}

	onDownload(e: Event): void {
		void downloadVoucherPdf(this.pdf(e));
	}

	async onUnassign(e: Event): Promise<void> {
		const v = this.row(e);
		if (!await this.confirm(`Zuordnung von ${voucherTypeText(v.type)} ${voucherNumber(v)} zu dieser Bauakte lösen? Der Beleg bleibt in Lexware Office unverändert bestehen.`,
			{ action: "Lösen", danger: true })) return;
		if (await this.run(async () => { await del(`bauakten/${this.bauakteId}/lexware/vouchers/${v.id}`); return true; }, "Zuordnung gelöst")) await this.reload();
	}
}
