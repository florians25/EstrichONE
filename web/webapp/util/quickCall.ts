/**
 * Telefon-Schnellerfassung: während des Gesprächs Wer und Notiz, Speichern mit Strg+Enter.
 * Als Anruf (Gesprächsnotiz) oder direkt als Anfrage; Details lassen sich später unter „Anrufe“ ergänzen.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import Label from "sap/m/Label";
import Switch from "sap/m/Switch";
import DatePicker from "sap/m/DatePicker";
import SegmentedButton from "sap/m/SegmentedButton";
import SegmentedButtonItem from "sap/m/SegmentedButtonItem";
import MessageStrip from "sap/m/MessageStrip";
import MessageToast from "sap/m/MessageToast";
import ObjectStatus from "sap/m/ObjectStatus";
import Bar from "sap/m/Bar";
import Title from "sap/m/Title";
import Text from "sap/m/Text";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import { ValueState } from "sap/ui/core/library";
import EventBus from "sap/ui/core/EventBus";
import { problemMessage, put } from "./api";
import { bauakteInput, customerInput } from "./forms";
import { uuidv7 } from "./ids";

export function openQuickCall(): void {
	const startedAt = Date.now();
	const live = new ObjectStatus("quickCallTimer", { text: "Gespräch läuft – 00:00", state: "Error", icon: "sap-icon://record", inverted: false });
	live.addStyleClass("eoLiveTimer");
	const tick = setInterval(() => {
		const seconds = Math.max(0, Math.floor((Date.now() - startedAt) / 1000));
		live.setText(`Gespräch läuft – ${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`);
	}, 1000);

	const who = new Input("quickWho", { placeholder: "Name, Firma oder Telefonnummer", width: "100%" });
	const phone = new Input("quickPhone", { placeholder: "optional", width: "100%", type: "Tel" });
	const direction = new SegmentedButton("quickDirection", { selectedKey: "inbound",
		items: [new SegmentedButtonItem({ key: "inbound", text: "Eingehend" }), new SegmentedButtonItem({ key: "outbound", text: "Ausgehend" })] });
	const notes = new TextArea("quickNotes", { rows: 6, width: "100%", placeholder: "Worum geht es? Was wurde besprochen?" });
	const customer = customerInput("quickCustomer", null, undefined);
	const bauakte = bauakteInput("quickBauakte", null, undefined);
	const followUp = new Switch("quickFollowUp", { customTextOn: "Ja", customTextOff: "Nein" });
	const due = new DatePicker("quickDue", { valueFormat: "yyyy-MM-dd", displayFormat: "dd.MM.yyyy", width: "100%" });
	const strip = new MessageStrip({ type: "Error", showIcon: true, visible: false });

	const form = new SimpleForm({
		editable: true, layout: "ResponsiveGridLayout", labelSpanL: 12, labelSpanM: 12, labelSpanS: 12, columnsL: 1, columnsM: 1,
		singleContainerFullSize: true,
		content: [
			new Label({ text: "Wer ruft an?", labelFor: who, required: true }), who,
			new Label({ text: "Rückrufnummer", labelFor: phone }), phone,
			new Label({ text: "Richtung", labelFor: direction }), direction,
			new Label({ text: "Notiz", labelFor: notes }), notes,
			new Label({ text: "Kunde zuordnen", labelFor: customer.control }), customer.control,
			new Label({ text: "Bauakte zuordnen", labelFor: bauakte.control }), bauakte.control,
			new Label({ text: "Wiedervorlage", labelFor: followUp }), followUp,
			new Label({ text: "Wiedervorlage am", labelFor: due }), due,
			new Text({ text: "Der Zeitpunkt des Gesprächsbeginns wird übernommen. Details lassen sich unter „Anrufe“ ergänzen." }).addStyleClass("eoHelpText"),
		],
	});

	const save = async (kind: "call" | "inquiry") => {
		const text = who.getValue().trim();
		if (!text) {
			who.setValueState(ValueState.Error);
			who.setValueStateText("Name, Firma oder Telefonnummer angeben.");
			who.focus();
			return;
		}
		who.setValueState(ValueState.None);
		const looksLikePhone = /^[+\d][\d\s/()-]{5,}$/.test(text);
		const note = notes.getValue().trim();
		const id = uuidv7();
		dialog.setBusy(true);
		try {
			await put(`calls/${id}`, {
				id, kind, direction: direction.getSelectedKey(), source: "phone", callerName: looksLikePhone ? null : text,
				callerPhone: phone.getValue().trim() || (looksLikePhone ? text : null), customerId: customer.read(),
				bauakteId: kind === "call" ? bauakte.read() : null, notes: note || null,
				subject: kind === "inquiry" ? note.split("\n")[0]?.slice(0, 120) || null : null,
				followUp: followUp.getState(), dueDate: due.getValue() || null, calledAt: new Date(startedAt).toISOString(),
			});
			EventBus.getInstance().publish("estrichone", "callsChanged");
			MessageToast.show(kind === "inquiry" ? "Anfrage gespeichert" : "Gesprächsnotiz gespeichert");
			dialog.close();
		} catch (error) {
			strip.setText(problemMessage(error));
			strip.setVisible(true);
		} finally {
			dialog.setBusy(false);
		}
	};

	const dialog = new Dialog("quickCallDialog", {
		title: "Telefon-Schnellerfassung",
		customHeader: new Bar({ contentLeft: [new Title({ text: "Telefon-Schnellerfassung", level: "H1" })], contentRight: [live] }),
		contentWidth: "34rem",
		resizable: true,
		draggable: true,
		initialFocus: who,
		content: [strip, form],
		buttons: [
			new Button({ text: "Als Anfrage speichern", press: () => void save("inquiry") }),
			new Button({ text: "Speichern (Strg+Enter)", type: "Emphasized", icon: "sap-icon://save", press: () => void save("call") }),
			new Button({ text: "Abbrechen", press: () => dialog.close() }),
		],
		afterClose: () => {
			clearInterval(tick);
			dialog.destroy();
		},
	});
	dialog.addAriaLabelledBy(live);
	live.addStyleClass("sapUiTinyMarginEnd");
	dialog.attachBrowserEvent("keydown", (event: KeyboardEvent) => {
		if (event.ctrlKey && event.key === "Enter") {
			event.preventDefault();
			void save("call");
		}
	});
	dialog.open();
	dialog.getDomRef()?.querySelector("#quickCallTimer")?.setAttribute("role", "timer");
	setTimeout(() => document.getElementById("quickCallTimer")?.setAttribute("role", "timer"), 300);
}
