/**
 * Digitales Abnahmeprotokoll als geführter Ablauf: Termin und Teilnehmer → Leistung und Räume → Ergebnis und Mängel →
 * Fotos → Notizen → Unterschriften (vor Ort) oder Freigabe per Link an den Kunden. Gespeichert wird wie in den Apps über
 * den Sync (acceptance.create); Mängel mit Frist entstehen als Mängel der Bauakte. Ist das Protokoll unterschrieben,
 * legt der Server das PDF unter „Abnahmen“ ab und schließt die Bauakte ggf. ab.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import TextArea from "sap/m/TextArea";
import Title from "sap/m/Title";
import Input from "sap/m/Input";
import CheckBox from "sap/m/CheckBox";
import DatePicker from "sap/m/DatePicker";
import DateTimePicker from "sap/m/DateTimePicker";
import SegmentedButton from "sap/m/SegmentedButton";
import SegmentedButtonItem from "sap/m/SegmentedButtonItem";
import MessageStrip from "sap/m/MessageStrip";
import Wizard from "sap/m/Wizard";
import WizardStep from "sap/m/WizardStep";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import VBox from "sap/m/VBox";
import FlexItemData from "sap/m/FlexItemData";
import FlexBox from "sap/m/FlexBox";
import { post, problemMessage, syncPush } from "./api";
import { akteHeader, type AkteContext } from "./akteHeader";
import { area, dateTime } from "./format";
import { uuidv7 } from "./ids";
import type { FileResponse, RoomOverview } from "./models";
import { uploadFile } from "./upload";
import SignaturePad from "../control/SignaturePad";

export type AcceptanceResult = "accepted" | "acceptedWithReservations" | "refused";

export const acceptanceResults: Record<AcceptanceResult, { text: string; state: "Success" | "Warning" | "Error" }> = {
	accepted: { text: "Abgenommen", state: "Success" },
	acceptedWithReservations: { text: "Abgenommen mit Vorbehalt", state: "Warning" },
	refused: { text: "Abnahme verweigert", state: "Error" },
};

export interface AcceptanceWizardOptions {
	bauakteId: string;
	context: AkteContext | null;
	customerName: string | null;
	customerEmail: string | null;
	contractorName: string;
	rooms: RoomOverview | null;
	photos: FileResponse[];
}

export interface AcceptanceSaved {
	id: string;
	signed: boolean;
	link: { url: string; expiresAt: string; emailed: boolean } | null;
}

const localNow = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 16);

export function openAcceptanceWizard(options: AcceptanceWizardOptions): Promise<AcceptanceSaved | null> {
	const { bauakteId } = options;
	return new Promise((resolve) => {
		// 1 · Termin und Teilnehmer
		const at = new DateTimePicker("accAt", { valueFormat: "yyyy-MM-dd'T'HH:mm", displayFormat: "dd.MM.yyyy, HH:mm", width: "100%", value: localNow() });
		const location = new Input("accLocation", { width: "100%", maxLength: 200, value: options.context?.address ?? "", placeholder: "Baustelle" });
		const client = new Input("accClient", { width: "100%", maxLength: 200, value: options.customerName ?? "", placeholder: "Auftraggeber oder Vertreter" });
		const contractor = new Input("accContractor", { width: "100%", maxLength: 200, value: options.contractorName, placeholder: "Bauleiter/Kolonnenführer" });
		const stepVisit = new WizardStep("accStepVisit", { title: "Termin", icon: "sap-icon://appointment-2", validated: true, content: [new VBox({ items: [
			new Label({ text: "Datum und Uhrzeit", labelFor: at }), at,
			new Label({ text: "Ort", labelFor: location }).addStyleClass("sapUiSmallMarginTop"), location,
			new Label({ text: "Für den Auftraggeber", labelFor: client }).addStyleClass("sapUiSmallMarginTop"), client,
			new Label({ text: "Für den Auftragnehmer", labelFor: contractor }).addStyleClass("sapUiSmallMarginTop"), contractor,
		] })] });

		// 2 · Leistung und Räume
		const scope = new TextArea("accScope", { rows: 3, width: "100%", maxLength: 4000, value: "Estricharbeiten gemäß Auftrag.", growing: true });
		const roomList = new List("accRooms", { mode: "MultiSelect", noDataText: "Keine Räume erfasst – Leistung oben beschreiben.", includeItemInSelection: true });
		const roomTotal = new Text().addStyleClass("eoMuted sapUiTinyMarginTop");
		const rooms = options.rooms?.rooms ?? [];
		for (const room of rooms) {
			const item = new StandardListItem({ title: room.name, description: [room.floor, room.screedTypeName].filter(Boolean).join(" · "),
				info: room.areaSquareMeters !== null ? area(room.areaSquareMeters) : "", selected: true });
			item.data("id", room.id);
			item.data("area", room.areaSquareMeters ?? 0);
			roomList.addItem(item);
		}
		const updateTotal = () => {
			const sum = roomList.getSelectedItems().reduce((s, i) => s + Number(i.data("area") ?? 0), 0);
			roomTotal.setText(rooms.length ? `${roomList.getSelectedItems().length} von ${rooms.length} Räumen · ${area(sum)}` : "");
		};
		roomList.attachSelectionChange(updateTotal);
		updateTotal();
		const stepScope = new WizardStep("accStepScope", { title: "Leistung", icon: "sap-icon://inventory", validated: true, content: [new VBox({ items: [
			new Label({ text: "Abgenommene Leistung", labelFor: scope }), scope,
			new Label({ text: "Räume und Flächen", labelFor: roomList }).addStyleClass("sapUiSmallMarginTop"), roomList, roomTotal,
		] })] });

		// 3 · Ergebnis und Mängel
		const result = new SegmentedButton("accResult", { selectedKey: "accepted", items: (Object.keys(acceptanceResults) as AcceptanceResult[])
			.map((k) => new SegmentedButtonItem({ key: k, text: acceptanceResults[k].text })) });
		const defectRows = new VBox("accDefects");
		const addDefect = (title = "") => {
			const text = new Input({ value: title, placeholder: "Mangel oder Restarbeit, z. B. Randfuge im Bad nachschneiden", width: "100%", maxLength: 200 });
			const due = new DatePicker({ valueFormat: "yyyy-MM-dd", displayFormat: "dd.MM.yyyy", width: "10rem", placeholder: "Frist" });
			const row = new FlexBox({ alignItems: "Center", items: [text, due, new Button({ icon: "sap-icon://decline", type: "Transparent", tooltip: "Entfernen",
				press: () => { defectRows.removeItem(row); row.destroy(); } })] }).addStyleClass("eoEntryRow sapUiTinyMarginBottom");
			text.setLayoutData(new FlexItemData({ growFactor: 1 }));
			defectRows.addItem(row);
		};
		const stepResult = new WizardStep("accStepResult", { title: "Ergebnis", icon: "sap-icon://complete", validated: true, content: [new VBox({ items: [
			new Label({ text: "Ergebnis der Abnahme", labelFor: result }), result,
			new Title({ text: "Mängel und Restarbeiten", level: "H5" }).addStyleClass("sapUiMediumMarginTop"),
			new Text({ text: "Werden als Mängel der Bauakte angelegt (mit Frist) und erscheinen im Protokoll." }).addStyleClass("eoMuted sapUiTinyMarginBottom"),
			defectRows,
			new Button("accAddDefect", { text: "Mangel hinzufügen", icon: "sap-icon://add", press: () => addDefect() }),
		] })] });

		// 4 · Fotos
		const photoList = new List("accPhotos", { mode: "MultiSelect", noDataText: "Noch keine Fotos – hier aufnehmen oder hochladen.", growing: true, growingThreshold: 30 });
		const addPhoto = (p: FileResponse, selected: boolean) => {
			const item = new StandardListItem({ title: p.caption || p.originalName, description: dateTime(p.capturedAt ?? p.createdAt),
				icon: `/api/v1/files/${p.id}/content?variant=${p.variants === "ready" ? "thumb" : "original"}`, iconInset: false, selected });
			item.data("id", p.id);
			photoList.addItem(item);
		};
		for (const p of options.photos.filter((f) => f.state === "stored").slice(0, 60)) addPhoto(p, false);
		const fileInput = document.createElement("input");
		fileInput.type = "file";
		fileInput.accept = "image/*";
		fileInput.multiple = true;
		fileInput.setAttribute("capture", "environment");
		const uploadStrip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
		fileInput.addEventListener("change", async () => {
			const files = [...(fileInput.files ?? [])];
			fileInput.value = "";
			dialog.setBusy(true);
			try {
				for (const file of files) addPhoto(await uploadFile(file, { bauakteId, caption: "Abnahme" }), true);
			} catch (error) {
				uploadStrip.setText(problemMessage(error));
				uploadStrip.setVisible(true);
			} finally {
				dialog.setBusy(false);
			}
		});
		const stepPhotos = new WizardStep("accStepPhotos", { title: "Fotos", icon: "sap-icon://camera", validated: true, content: [new VBox({ items: [
			new Button("accAddPhoto", { text: "Foto aufnehmen oder hochladen", icon: "sap-icon://add-photo", press: () => fileInput.click() }), uploadStrip, photoList,
		] })] });

		// 5 · Notizen
		const notes = new TextArea("accNotes", { rows: 8, width: "100%", maxLength: 100_000, growing: true, growingMaxLines: 30,
			placeholder: "Absprachen, Hinweise zur Nutzung (Belegreife, Heizprotokoll), Vorbehalte …" });
		const stepNotes = new WizardStep("accStepNotes", { title: "Notizen", icon: "sap-icon://notes", validated: true, content: [new VBox({ items: [
			new Label({ text: "Notizen (frei, beliebig lang)", labelFor: notes }), notes] })] });

		// 6 · Unterschriften oder Freigabe per Link
		const clientPad = new SignaturePad("accClientSignature");
		clientPad.setProperty("label", "Unterschrift Auftraggeber");
		const contractorPad = new SignaturePad("accContractorSignature");
		contractorPad.setProperty("label", "Unterschrift Auftragnehmer");
		const byLink = new CheckBox("accByLink", { text: "Auftraggeber nicht vor Ort: Freigabe per Link an den Kunden" });
		const email = new Input("accEmail", { type: "Email", width: "100%", value: options.customerEmail ?? "", placeholder: "E-Mail-Adresse des Kunden (leer = Link nur anzeigen)", visible: false });
		byLink.attachSelect(() => {
			email.setVisible(byLink.getSelected());
			clientBox.setVisible(!byLink.getSelected());
		});
		const clientBox = new VBox({ items: [new Label({ text: "Auftraggeber", labelFor: clientPad }), clientPad,
			new Button({ text: "Löschen", type: "Transparent", icon: "sap-icon://eraser", press: () => clientPad.clear() })] });
		const stepSign = new WizardStep("accStepSign", { title: "Unterschriften", icon: "sap-icon://signature", validated: true, content: [new VBox({ items: [
			new Text({ text: "Mit der Unterschrift bestätigen beide Seiten die Abnahme wie protokolliert." }).addStyleClass("eoMuted sapUiSmallMarginBottom"),
			new FlexBox({ wrap: "Wrap", items: [
				clientBox.addStyleClass("eoSignBox"),
				new VBox({ items: [new Label({ text: "Auftragnehmer", labelFor: contractorPad }), contractorPad,
					new Button({ text: "Löschen", type: "Transparent", icon: "sap-icon://eraser", press: () => contractorPad.clear() })] }).addStyleClass("eoSignBox"),
			] }).addStyleClass("eoEntryRow"),
			byLink, email,
		] })] });

		const steps = [stepVisit, stepScope, stepResult, stepPhotos, stepNotes, stepSign];
		const wizard = new Wizard("accWizard", { steps, showNextButton: false, renderMode: "Page", height: "100%" });
		const strip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
		let index = 0;
		const back = new Button("accBack", { text: "Zurück", press: () => go(index - 1) });
		const next = new Button("accNext", { text: "Weiter", type: "Emphasized", press: () => go(index + 1) });
		const save = new Button("accSave", { text: "Abnahme speichern", type: "Emphasized", press: () => void submit() });
		const updateButtons = () => {
			back.setVisible(index > 0);
			next.setVisible(index < steps.length - 1);
			save.setVisible(index === steps.length - 1);
		};
		const go = (target: number) => {
			if (target < 0 || target >= steps.length) return;
			strip.setVisible(false);
			if (target > index) for (let i = index; i < target; i++) wizard.nextStep();
			else wizard.goToStep(steps[target], true);
			index = target;
			updateButtons();
		};

		let saved: AcceptanceSaved | null = null;
		const submit = async () => {
			const link = byLink.getSelected();
			const clientPng = link ? null : clientPad.toPng();
			const contractorPng = contractorPad.toPng();
			if (!link && !clientPng) {
				strip.setText("Bitte vom Auftraggeber unterschreiben lassen – oder die Freigabe per Link wählen.");
				strip.setVisible(true);
				return;
			}
			dialog.setBusy(true);
			strip.setVisible(false);
			try {
				const defectIds: string[] = [];
				for (const row of defectRows.getItems() as FlexBox[]) {
					const [text, due] = row.getItems() as [Input, DatePicker];
					const title = text.getValue().trim();
					if (!title) continue;
					const id = uuidv7();
					await post("work-items", { id, bauakteId, kind: "defect", title: title.slice(0, 200), dueDate: due.getValue() || null, assigneeUserId: null });
					defectIds.push(id);
				}
				const id = uuidv7();
				const now = new Date().toISOString();
				await syncPush("acceptance.create", id, {
					bauakteId, performedAt: at.getValue() ? new Date(at.getValue()).toISOString() : now, location: location.getValue() || null,
					clientName: client.getValue() || null, contractorName: contractor.getValue() || null, scope: scope.getValue() || null,
					roomIds: roomList.getSelectedItems().map((i) => String(i.data("id"))), result: result.getSelectedKey(), notes: notes.getValue() || null,
					photoFileIds: photoList.getSelectedItems().map((i) => String(i.data("id"))), defectWorkItemIds: defectIds,
					clientSignature: clientPng ? { png: clientPng, signedAt: now } : null,
					contractorSignature: contractorPng ? { png: contractorPng, signedAt: now } : null,
				}, uuidv7());
				let linkInfo: AcceptanceSaved["link"] = null;
				if (link) {
					linkInfo = await post<{ url: string; expiresAt: string; emailed: boolean }>(`acceptances/${id}/customer-link`, { email: email.getValue().trim() || null });
				}
				saved = { id, signed: !!clientPng && !!contractorPng, link: linkInfo };
				dialog.close();
			} catch (error) {
				strip.setText(problemMessage(error));
				strip.setVisible(true);
			} finally {
				dialog.setBusy(false);
			}
		};

		const dialog = new Dialog("accDialog", {
			title: "Abnahmeprotokoll", contentWidth: "52rem", contentHeight: "80vh", resizable: true, draggable: true, verticalScrolling: false,
			content: [new VBox({ height: "100%", items: [...akteHeader(options.context), strip, wizard] }).addStyleClass("eoWizardBody")],
			buttons: [back, next, save, new Button("accCancel", { text: "Abbrechen", press: () => dialog.close() })],
			afterClose: () => {
				dialog.destroy();
				resolve(saved);
			},
		});
		updateButtons();
		dialog.open();
	});
}
