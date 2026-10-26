/**
 * Erstbegehung als geführter Ablauf (sap.m.Wizard): Begehung → Prüfpunkte → Fotos → Mängel → Abschluss.
 * Gespeichert wird wie in den Apps über den Sync (inspection.create); Prüfungen sind Nachweise und werden nie
 * überschrieben – eine Korrektur wird als neue Prüfung mit Verweis gespeichert. Beanstandete Punkte können direkt als
 * Mangel (Aufgabe) in der Bauakte angelegt werden.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import TextArea from "sap/m/TextArea";
import Title from "sap/m/Title";
import Switch from "sap/m/Switch";
import CheckBox from "sap/m/CheckBox";
import DateTimePicker from "sap/m/DateTimePicker";
import SegmentedButton from "sap/m/SegmentedButton";
import SegmentedButtonItem from "sap/m/SegmentedButtonItem";
import MessageStrip from "sap/m/MessageStrip";
import Wizard from "sap/m/Wizard";
import WizardStep from "sap/m/WizardStep";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import Input from "sap/m/Input";
import { ValueState } from "sap/ui/core/library";
import { post, problemMessage, syncPush } from "./api";
import { uploadFile } from "./upload";
import { uuidv7 } from "./ids";
import { checkTitles } from "./labels";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import { formSchema as getSchema } from "./formSchema";
import { schemaWizardControls } from "./project";
import { dateTime } from "./format";
import type { FileResponse, Inspection } from "./models";

const results = [{ key: "ok", text: "In Ordnung" }, { key: "notOk", text: "Mangel" }, { key: "notApplicable", text: "Entfällt" }];

export interface InspectionWizardOptions {
	bauakteId: string;
	photos: FileResponse[];
	corrects: Inspection | null;
}

/** Liefert true, wenn gespeichert wurde. */
export function openInitialInspectionWizard(options: InspectionWizardOptions): Promise<boolean> {
	const { bauakteId, corrects } = options;
	const photos = [...options.photos].filter((p) => p.state === "stored")
		.sort((a, b) => (b.capturedAt ?? b.createdAt).localeCompare(a.capturedAt ?? a.createdAt));

	// 1 · Begehung
	const performedAt = new DateTimePicker("inspAt", {
		valueFormat: "yyyy-MM-dd'T'HH:mm", displayFormat: "dd.MM.yyyy, HH:mm", width: "100%",
		value: new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 16),
	});
	const notRequired = new Switch("inspNotRequired", { state: corrects?.notRequired ?? false, customTextOn: " ", customTextOff: " " });
	const reason = new TextArea("inspReason", { rows: 3, width: "100%", maxLength: 4000, visible: notRequired.getState(), placeholder: "z. B. Bestandsgebäude, vom Bauherrn bestätigt" });
	const reasonLabel = new Label({ text: "Begründung", labelFor: reason, required: true, visible: notRequired.getState() });
	// Freie Notizen: das Wichtigste der Begehung, beliebig lang, mehrzeilig, auch offline in der App.
	const freeNotes = new TextArea("inspFreeNotes", {
		rows: 8, width: "100%", growing: true, growingMaxLines: 30, maxLength: 100_000, value: corrects?.notes ?? "",
		placeholder: "Alles, was vor Ort wichtig ist: Gespräch mit dem Kunden, Zufahrt, Besonderheiten, Wünsche, offene Fragen …",
	});
	const visitStep = new WizardStep("inspStepVisit", {
		title: "Begehung", icon: "sap-icon://appointment-2", validated: true,
		content: [new VBox({ items: [
			...(corrects ? [new MessageStrip({ type: "Information", showIcon: true, text: "Prüfungen sind Nachweise und werden nicht überschrieben: Die Korrektur wird als neue Prüfung mit Verweis gespeichert." }).addStyleClass("sapUiSmallMarginBottom")] : []),
			new Label({ text: "Zeitpunkt der Begehung", labelFor: performedAt }), performedAt,
			new HBox({ alignItems: "Center", items: [notRequired, new Label({ text: "Erstbegehung nicht notwendig", labelFor: notRequired }).addStyleClass("sapUiSmallMarginBegin")] }).addStyleClass("sapUiSmallMarginTop"),
			reasonLabel, reason,
			new Label({ text: "Notizen (frei, beliebig lang)", labelFor: freeNotes, design: "Bold" }).addStyleClass("sapUiSmallMarginTop"), freeNotes,
		] })],
	});

	// 2 · Prüfpunkte
	// Prüfpunkte und Messwerte aus dem Formular „Erstbegehung“ der Stammdaten (sonst die bisherigen festen Punkte).
	const checks: Record<string, { result: SegmentedButton; note: Input }> = {};
	const titles: Record<string, string> = { ...checkTitles };
	const checkBox = new VBox();
	const valuesForm = new SimpleForm({ editable: true, layout: "ResponsiveGridLayout", labelSpanL: 4, labelSpanM: 4, labelSpanS: 12, columnsL: 1, columnsM: 1 });
	let schemaVersion: number | null = null;
	let valueControls: ReturnType<typeof schemaWizardControls> | null = null;
	const addCheck = (key: string, title: string) => {
		titles[key] = title;
		const result = new SegmentedButton(`inspCheck-${key}`, {
			selectedKey: corrects?.checks[key] ?? "ok",
			items: results.map((r) => new SegmentedButtonItem({ key: r.key, text: r.text })),
		});
		const note = new Input(`inspNote-${key}`, { placeholder: "Notiz (bei Mangel: was genau?)", width: "100%", maxLength: 300,
			value: (corrects as (typeof corrects & { checkRemarks?: Record<string, string> }) | null)?.checkRemarks?.[key] ?? "" });
		checks[key] = { result, note };
		checkBox.addItem(new VBox({ items: [new Label({ text: title, labelFor: result, design: "Bold" }), result, note] }).addStyleClass("sapUiSmallMarginBottom eoCheckRow"));
	};
	void getSchema("erstbegehung").then((schema) => {
		if (!schema) {
			for (const [key, title] of Object.entries(checkTitles)) addCheck(key, title);
			return;
		}
		schemaVersion = schema.version;
		for (const g of schema.groups) {
			const own = g.fields.filter((f) => f.type === "check");
			if (own.length) checkBox.addItem(new Title({ text: g.label, level: "H5" }).addStyleClass("sapUiSmallMarginBottom"));
			for (const f of own) addCheck(f.key, f.label);
		}
		const valueSchema = { ...schema, groups: schema.groups.map((g) => ({ ...g, fields: g.fields.filter((f) => f.type !== "check") })).filter((g) => g.fields.length) };
		if (valueSchema.groups.length) {
			valueControls = schemaWizardControls(valueSchema, "insp", () => undefined);
			for (const c of valueControls.content) valuesForm.addContent(c);
		}
	});
	const checksStep = new WizardStep("inspStepChecks", { title: "Prüfpunkte", icon: "sap-icon://checklist-item", validated: true, content: [new VBox({ items: [checkBox, valuesForm] })] });

	// 3 · Fotos
	const photoList = new List("inspPhotos", { mode: "MultiSelect", noDataText: "Noch keine Fotos in der Bauakte – hier direkt aufnehmen oder hochladen.", growing: true, growingThreshold: 30 });
	const addPhotoItem = (p: FileResponse, selected: boolean) => {
		const item = new StandardListItem({
			title: p.caption || p.originalName, description: dateTime(p.capturedAt ?? p.createdAt),
			icon: `/api/v1/files/${p.id}/content?variant=${p.variants === "ready" ? "thumb" : "original"}`, iconInset: false, selected,
		});
		item.data("id", p.id);
		photoList.addItem(item);
	};
	const preselected = new Set(corrects?.photoFileIds ?? []);
	for (const p of photos) addPhotoItem(p, preselected.has(p.id));
	const fileInput = document.createElement("input");
	fileInput.type = "file";
	fileInput.accept = "image/*";
	fileInput.multiple = true;
	fileInput.setAttribute("capture", "environment");
	const uploadStrip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
	fileInput.addEventListener("change", async () => {
		const files = [...(fileInput.files ?? [])];
		fileInput.value = "";
		if (!files.length) return;
		dialog.setBusy(true);
		uploadStrip.setVisible(false);
		try {
			for (const file of files) addPhotoItem(await uploadFile(file, { bauakteId, caption: "Erstbegehung" }), true);
		} catch (error) {
			uploadStrip.setText(problemMessage(error));
			uploadStrip.setVisible(true);
		} finally {
			dialog.setBusy(false);
		}
	});
	const photosStep = new WizardStep("inspStepPhotos", {
		title: "Fotos", icon: "sap-icon://camera", validated: true,
		content: [new VBox({ items: [
			new Button("inspAddPhoto", { text: "Foto aufnehmen oder hochladen", icon: "sap-icon://add-photo", press: () => fileInput.click() }),
			uploadStrip,
			new Text({ text: "Fotos auswählen, die zur Begehung gehören (z. B. Untergrund, Randdämmung, Heizkreisverteiler)." }).addStyleClass("eoMuted sapUiTinyMarginTop"),
			photoList,
		] })],
	});

	// 4 · Mängel
	const defectBox = new VBox();
	const defectsStep = new WizardStep("inspStepDefects", { title: "Mängel", icon: "sap-icon://warning", validated: true, content: [defectBox] });
	const defectChecks: { key: string; box: CheckBox; text: string }[] = [];
	const fillDefects = () => {
		defectBox.destroyItems();
		defectChecks.length = 0;
		const failed = Object.entries(checks).filter(([, c]) => c.result.getSelectedKey() === "notOk");
		if (failed.length === 0) {
			defectBox.addItem(new MessageStrip({ type: "Success", showIcon: true, text: "Keine Prüfpunkte beanstandet." }));
			return;
		}
		defectBox.addItem(new Text({ text: "Beanstandete Punkte als Mangel in der Bauakte anlegen (erscheinen unter „Aufgaben & Mängel“):" }).addStyleClass("sapUiSmallMarginBottom"));
		for (const [key, c] of failed) {
			const text = `${titles[key]}: ${c.note.getValue().trim() || "beanstandet bei der Erstbegehung"}`;
			const box = new CheckBox({ text, selected: true, wrapping: true });
			defectChecks.push({ key, box, text });
			defectBox.addItem(box);
		}
	};

	// 5 · Abschluss
	const remarks = new TextArea("inspRemarks", { rows: 4, width: "100%", maxLength: 4000, value: corrects?.remarks ?? "", placeholder: "Absprachen, Besonderheiten, Hinweise für die Kolonne" });
	const summary = new VBox();
	const finishStep = new WizardStep("inspStepFinish", {
		title: "Abschluss", icon: "sap-icon://accept", validated: true,
		content: [new VBox({ items: [summary, new Label({ text: "Bemerkungen", labelFor: remarks }).addStyleClass("sapUiSmallMarginTop"), remarks] })],
	});
	const selectedPhotos = () => photoList.getSelectedItems().map((i) => String(i.data("id")));
	const fillSummary = () => {
		summary.destroyItems();
		summary.addItem(new Title({ text: notRequired.getState() ? "Erstbegehung nicht notwendig" : "Ergebnis", level: "H5" }));
		const noteText = freeNotes.getValue().trim();
		if (noteText) {
			summary.addItem(new Text({ text: `Notizen: ${noteText.length > 300 ? `${noteText.slice(0, 300)} …` : noteText}` }).addStyleClass("eoPre sapUiTinyMarginBottom"));
		}
		if (notRequired.getState()) {
			summary.addItem(new Text({ text: reason.getValue().trim() }));
			return;
		}
		for (const [key, c] of Object.entries(checks)) {
			const r = results.find((x) => x.key === c.result.getSelectedKey());
			summary.addItem(new Text({ text: `${titles[key]}: ${r?.text ?? "–"}${c.note.getValue().trim() ? ` – ${c.note.getValue().trim()}` : ""}` }));
		}
		summary.addItem(new Text({ text: `${selectedPhotos().length} Fotos · ${defectChecks.filter((d) => d.box.getSelected()).length} Mängel werden angelegt` }).addStyleClass("eoMuted sapUiTinyMarginTop"));
	};

	const allSteps = [visitStep, checksStep, photosStep, defectsStep, finishStep];
	const activeSteps = () => (notRequired.getState() ? [visitStep, finishStep] : allSteps);
	const wizard = new Wizard("inspWizard", { steps: allSteps, showNextButton: false, renderMode: "Page", height: "100%", enableBranching: true });
	visitStep.setNextStep(checksStep);
	checksStep.setNextStep(photosStep);
	photosStep.setNextStep(defectsStep);
	defectsStep.setNextStep(finishStep);
	visitStep.addSubsequentStep(checksStep);
	visitStep.addSubsequentStep(finishStep);

	const validateVisit = () => {
		const ok = !notRequired.getState() || !!reason.getValue().trim();
		reason.setValueState(ok ? ValueState.None : ValueState.Error);
		reason.setValueStateText("Bitte begründen, warum keine Erstbegehung notwendig ist.");
		visitStep.setValidated(!notRequired.getState() || !!reason.getValue().trim());
		return ok;
	};
	notRequired.attachChange(() => {
		const off = notRequired.getState();
		reason.setVisible(off);
		reasonLabel.setVisible(off);
		visitStep.setNextStep(off ? finishStep : checksStep);
		updateButtons();
	});
	reason.attachLiveChange(() => { validateVisit(); updateButtons(); });

	const strip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
	let index = 0;
	const back = new Button("inspBack", { text: "Zurück", press: () => go(index - 1) });
	const next = new Button("inspNext", { text: "Weiter", type: "Emphasized", press: () => go(index + 1) });
	const save = new Button("inspSave", { text: "Erstbegehung speichern", type: "Emphasized", press: () => void submit() });
	function updateButtons(): void {
		const steps = activeSteps();
		back.setVisible(index > 0);
		next.setVisible(index < steps.length - 1);
		save.setVisible(index === steps.length - 1);
	}
	function go(target: number): void {
		const steps = activeSteps();
		if (target < 0 || target >= steps.length) return;
		if (target > index && index === 0 && !validateVisit()) return;
		strip.setVisible(false);
		const step = steps[target];
		if (step === defectsStep) fillDefects();
		if (step === finishStep) {
			if (!notRequired.getState() && defectChecks.length === 0) fillDefects();
			fillSummary();
		}
		if (target > index) {
			for (let i = index; i < target; i++) wizard.nextStep();
		} else {
			wizard.goToStep(step, true);
		}
		index = target;
		updateButtons();
	}

	let saved = false;
	const submit = async () => {
		if (!validateVisit()) {
			go(0);
			return;
		}
		dialog.setBusy(true);
		strip.setVisible(false);
		try {
			const off = notRequired.getState();
			const checkRemarks = Object.fromEntries(Object.entries(checks).filter(([, c]) => c.note.getValue().trim()).map(([key, c]) => [key, c.note.getValue().trim()]));
			const values = Object.fromEntries(Object.entries(valueControls?.read() ?? {}).filter(([, v]) => v !== null && v !== ""));
			const text = off ? reason.getValue().trim() : remarks.getValue().trim();
			const at = performedAt.getValue();
			await syncPush("inspection.create", uuidv7(), {
				bauakteId, kind: "initialInspection", notRequired: off,
				checks: off ? {} : Object.fromEntries(Object.entries(checks).map(([k, c]) => [k, c.result.getSelectedKey()])),
				remarks: text || null, notes: freeNotes.getValue().trim() || null, values: off ? {} : values, checkRemarks: off ? {} : checkRemarks, schemaVersion, photoFileIds: off ? [] : selectedPhotos(), correctsInspectionId: corrects?.id ?? null,
				performedAt: at ? new Date(at).toISOString() : null,
			}, uuidv7());
			if (!off) {
				for (const d of defectChecks.filter((x) => x.box.getSelected())) {
					await post("work-items", { id: uuidv7(), bauakteId, kind: "defect", title: d.text.slice(0, 200), dueDate: null, assigneeUserId: null });
				}
			}
			saved = true;
			dialog.close();
		} catch (error) {
			strip.setText(problemMessage(error));
			strip.setVisible(true);
		} finally {
			dialog.setBusy(false);
		}
	};

	const dialog = new Dialog("inspDialog", {
		title: corrects ? "Erstbegehung korrigieren" : "Erstbegehung erfassen",
		contentWidth: "46rem", contentHeight: "36rem", resizable: true, draggable: true, verticalScrolling: false,
		content: [new VBox({ height: "100%", renderType: "Bare", items: [strip, wizard] })],
		buttons: [back, next, save, new Button({ text: "Abbrechen", press: () => dialog.close() })],
	});
	dialog.addStyleClass("eoWizardDialog");
	updateButtons();
	return new Promise((resolve) => {
		dialog.attachAfterClose(() => {
			dialog.destroy();
			resolve(saved);
		});
		dialog.open();
	});
}
