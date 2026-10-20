import BaseController from "../BaseController";
import EventBus from "sap/ui/core/EventBus";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import ObjectStatus from "sap/m/ObjectStatus";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import Label from "sap/m/Label";
import Link from "sap/m/Link";
import ProgressIndicator from "sap/m/ProgressIndicator";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import { get, post } from "../../util/api";
import { callForm, callTime, caller } from "../../util/callForm";
import { date } from "../../util/format";
import { callSources, inquiryStatus } from "../../util/labels";
import type { CallResponse, ConvertInquiryResponse, InquiryStatus } from "../../util/models";

/** Anfragen als Vorgänge mit Status-Ablauf und Umwandlung in eine Bauakte. @namespace estrichone.controller.office */
export default class Inquiries extends BaseController {
	onInit(): void {
		this.local({ items: [], tab: "open", busy: false });
		EventBus.getInstance().subscribe("estrichone", "callsChanged", () => void this.load());
	}

	onRouteMatched(): void { void this.load(); }
	onTab(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const items = await get<CallResponse[]>("calls", { kind: "inquiry", open: m.getProperty("/tab") === "open" || undefined });
			m.setProperty("/items", items.map((c) => ({ ...c, who: caller(c), time: callTime(c.calledAt) })));
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	async onNew(): Promise<void> {
		if (await callForm("inquiry", null)) { this.toast("Anfrage gespeichert"); void this.load(); }
	}

	onOpen(event: Event): void {
		this.detail((event.getSource() as Control).getBindingContext("view")?.getObject() as CallResponse);
	}

	/** Vorgang: Fortschritt, Angaben, Aktionen (In Bearbeitung, Abbrechen, Umwandeln). */
	private detail(c: CallResponse): void {
		const canWrite = this.has("contacts.edit");
		const step = { open: 1, inProgress: 2, converted: 3, cancelled: 3 }[c.inquiryStatus];
		const row = (label: string, value: string | null | undefined) => [new Label({ text: label }), new Text({ text: value || "–" })];
		const form = new SimpleForm({ editable: false, layout: "ResponsiveGridLayout", labelSpanL: 4, labelSpanM: 4, content: [
			new Label({ text: "Status" }), new ObjectStatus({ text: inquiryStatus[c.inquiryStatus].text, state: inquiryStatus[c.inquiryStatus].tone, inverted: true }),
			...row("Eingang", `${callTime(c.calledAt)} · ${callSources[c.source]}`), ...row("Name", c.callerName), ...row("Firma", c.company),
			new Label({ text: "Telefon" }), new Link({ text: c.callerPhone ?? "–", href: c.callerPhone ? `tel:${c.callerPhone}` : undefined, enabled: !!c.callerPhone }),
			new Label({ text: "E-Mail" }), new Link({ text: c.callerEmail ?? "–", href: c.callerEmail ? `mailto:${c.callerEmail}` : undefined, enabled: !!c.callerEmail }),
			...row("Kunde", c.customerName), ...row("Baustelle", [c.siteStreet, [c.sitePostalCode, c.siteCity].filter(Boolean).join(" ")].filter(Boolean).join(", ")),
			...row("Fläche / Estrich", `${c.areaSquareMeters ? `${c.areaSquareMeters.toLocaleString("de-DE")} m²` : "–"} · ${c.screedType ?? "–"}`),
			...row("Wunschtermin", date(c.desiredStart)), ...row("Anliegen", c.subject), ...row("Ticket-Notiz", c.notes), ...row("Nächster Schritt", c.nextStep),
			new Label({ text: "Bauakte" }), new Link({ text: c.convertedBauakteNumber ?? (c.convertedBauakteId ? "öffnen" : "–"), enabled: !!c.convertedBauakteId,
				press: () => { dialog.close(); this.nav(`/bauakten/${c.convertedBauakteId}`); } }),
		] });
		const setStatus = async (status: InquiryStatus) => {
			const updated = await this.run(() => post<CallResponse>(`calls/${c.id}/status`, { status }));
			if (updated) { dialog.close(); void this.load(); this.detail(updated); }
		};
		const buttons: Button[] = [];
		if (canWrite && c.inquiryStatus !== "converted") {
			buttons.push(new Button({ text: "Bearbeiten", icon: "sap-icon://edit", press: async () => {
				const saved = await callForm("inquiry", c);
				if (saved) { dialog.close(); void this.load(); this.detail(saved); }
			} }));
			if (c.inquiryStatus === "open") buttons.push(new Button({ text: "In Bearbeitung", icon: "sap-icon://process", press: () => void setStatus("inProgress") }));
			buttons.push(c.inquiryStatus === "cancelled"
				? new Button({ text: "Wieder öffnen", icon: "sap-icon://undo", press: () => void setStatus("open") })
				: new Button({ text: "Abbrechen", icon: "sap-icon://cancel", press: () => void setStatus("cancelled") }));
			if (this.has("bauakten.create") && c.inquiryStatus !== "cancelled") {
				buttons.push(new Button({ text: "In Bauakte umwandeln", type: "Emphasized", icon: "sap-icon://folder-full", press: async () => {
					if (!(await this.confirm("Anfrage in eine Bauakte umwandeln? Ohne Kunde wird einer aus Name/Firma angelegt.", { action: "Umwandeln" }))) return;
					const result = await this.run(() => post<ConvertInquiryResponse>(`calls/${c.id}/convert`, {}));
					if (result) {
						dialog.close();
						this.toast(`Bauakte ${result.bauakteNumber ?? ""} angelegt`);
						this.nav(`/bauakten/${result.bauakteId}`);
					}
				} }));
			}
		}
		buttons.push(new Button({ text: "Schließen", press: () => dialog.close() }));
		const progress = new ProgressIndicator({ percentValue: (step / 3) * 100, displayValue: `${step} von 3: ${inquiryStatus[c.inquiryStatus].text}`,
			state: c.inquiryStatus === "cancelled" ? "Error" : c.inquiryStatus === "converted" ? "Success" : "Information" });
		const dialog = new Dialog({ title: `Anfrage: ${caller(c)}`, contentWidth: "40rem", draggable: true, resizable: true,
			content: [new VBox({ items: [new HBox({ items: [new Text({ text: "Offen → In Bearbeitung → Bauakte erstellt" })] }), progress, form] }).addStyleClass("sapUiSmallMargin")],
			buttons, afterClose: () => dialog.destroy() });
		dialog.open();
	}
}
