import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get } from "../../../util/api";
import { date } from "../../../util/format";
import { euro, voucherNumber, voucherStatus, voucherSummary, voucherTypeText, type BauakteLexware, type LexwareVoucher } from "../../../util/lexware";
import { openAssignVoucherDialog, openExternal } from "../../../util/lexwareDialogs";

/**
 * Rechnungen und Angebote der Bauakte aus Lexware Office (nur Anzeige): Art, Nummer, Datum, Betrag und Status, „In Lexware
 * öffnen“ und die Zuordnung vorhandener Belege. Erstellt und bearbeitet werden Belege ausschließlich in Lexware Office.
 * @namespace estrichone.controller.bauakte.sections
 */
export default class Invoices extends SectionBase {
	protected init(): void {
		this.v().setModel(new JSONModel({ loaded: false, configured: false, canAssign: false, vouchers: [], summary: "", syncError: "" }), "inv");
	}

	private inv(): JSONModel {
		return this.v().getModel("inv") as JSONModel;
	}

	protected async load(): Promise<void> {
		this.apply(await get<BauakteLexware>(`bauakten/${this.bauakteId}/lexware`));
	}

	private apply(data: BauakteLexware): void {
		const m = this.inv();
		m.setProperty("/loaded", true);
		m.setProperty("/configured", data.configured);
		m.setProperty("/canAssign", data.canAssign);
		m.setProperty("/syncError", data.syncError ?? "");
		m.setProperty("/summary", voucherSummary(data.vouchers));
		m.setProperty("/vouchers", data.vouchers.map((v: LexwareVoucher) => {
			const status = voucherStatus(v.status);
			return {
				...v, typeText: voucherTypeText(v.type), numberText: voucherNumber(v), dateText: date(v.voucherDate), dueText: date(v.dueDate),
				netText: euro(v.netAmount, v.currency), grossText: euro(v.grossAmount, v.currency), statusText: status.text, statusState: status.state, statusIcon: status.icon,
			};
		}));
	}

	async onRefresh(): Promise<void> {
		const data = await this.run(() => get<BauakteLexware>(`bauakten/${this.bauakteId}/lexware`, { refresh: true }));
		if (!data) return;
		this.apply(data);
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

	async onUnassign(e: Event): Promise<void> {
		const v = this.row(e);
		if (!await this.confirm(`Zuordnung von ${voucherTypeText(v.type)} ${voucherNumber(v)} zu dieser Bauakte lösen? Der Beleg bleibt in Lexware Office unverändert bestehen.`,
			{ action: "Lösen", danger: true })) return;
		if (await this.run(async () => { await del(`bauakten/${this.bauakteId}/lexware/vouchers/${v.id}`); return true; }, "Zuordnung gelöst")) await this.reload();
	}
}
