/**
 * Geführtes Formular „Fehler melden / Funktion wünschen“ (sap.m.Wizard im Dialog):
 *  1. Art wählen – zwei große Kacheln mit kurzer Erklärung.
 *  2. Angaben passend zur Art, mit Hilfetexten und Pflichtfeldprüfung.
 *  3. Optional Bildschirmfoto oder Datei anhängen (Auswahl, Ziehen, Strg+V).
 *  4. Zusammenfassung prüfen – inklusive der automatisch mitgesendeten technischen Angaben – und absenden.
 *  5. Bestätigung mit Nummer.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import ComboBox from "sap/m/ComboBox";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import Title from "sap/m/Title";
import MessageStrip from "sap/m/MessageStrip";
import RadioButtonGroup from "sap/m/RadioButtonGroup";
import RadioButton from "sap/m/RadioButton";
import Wizard from "sap/m/Wizard";
import WizardStep from "sap/m/WizardStep";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import FlexBox from "sap/m/FlexBox";
import Icon from "sap/ui/core/Icon";
import Item from "sap/ui/core/Item";
import HTML from "sap/ui/core/HTML";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import IllustratedMessage from "sap/m/IllustratedMessage";
import type Control from "sap/ui/core/Control";
import { ValueState } from "sap/ui/core/library";
import { post, problemMessage } from "./api";
import { uuidv7 } from "./ids";
import { uploadLoose } from "./looseUpload";
import { formTitle } from "./formLayout";
import { session } from "./session";
import {
	areaForPath, bugImpacts, collectContext, contextRows, createRequest, emptyDraft, featureImpacts, feedbackAreas, feedbackFrequencies, feedbackKinds,
	missingFields, summaryRows, type FeedbackContext, type FeedbackDetail, type FeedbackDraft, type FeedbackFrequency, type FeedbackImpact,
	type FeedbackKind,
} from "./feedback";

export interface FeedbackWizardOptions {
	/** Art vorwählen (überspringt Schritt 1 nicht, markiert aber die Kachel). */
	kind?: FeedbackKind;
	/** Nach dem Absenden „Zu meinen Meldungen“. */
	onShowMine?: (created: FeedbackDetail) => void;
}

const MAX_FILES = 5;
const MAX_BYTES = 20 * 1024 * 1024;

/** Technische Angaben dieses Browsers und der aktuellen Seite. */
export function currentContext(): FeedbackContext {
	return collectContext({
		path: window.location.pathname + window.location.search, title: document.title, userAgent: navigator.userAgent,
		screenWidth: window.screen.width, screenHeight: window.screen.height, innerWidth: window.innerWidth, innerHeight: window.innerHeight,
		devicePixelRatio: window.devicePixelRatio || 1, language: navigator.language,
		timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone ?? "", appVersion: session.data.server?.serverVersion ?? null,
	});
}

/** Feld mit Hilfetext darunter (in der SimpleForm ein Feld je Zeile). */
function withHint(field: Control, hint: string): VBox {
	return new VBox({ renderType: "Bare", width: "100%", items: [field, new Text({ text: hint }).addStyleClass("eoFbHint")] });
}

function radios(id: string, options: Record<string, string>, columns: number): RadioButtonGroup {
	return new RadioButtonGroup(id, {
		columns, selectedIndex: -1, width: "100%",
		buttons: Object.entries(options).map(([key, text]) => new RadioButton({ text }).data("key", key) as RadioButton),
	}).addStyleClass("eoFbRadios");
}

const radioKey = (group: RadioButtonGroup): string => (group.getSelectedButton()?.data("key") as string | undefined) ?? "";

export function openFeedbackWizard(options: FeedbackWizardOptions = {}): Promise<FeedbackDetail | null> {
	const context = currentContext();
	const startArea = areaForPath(context.page ?? "");
	let kind: FeedbackKind | null = null;
	let dialog!: Dialog;

	// ---- Schritt 1: Art ---------------------------------------------------------------------------------------------
	const tiles = new Map<FeedbackKind, VBox>();
	const kindTile = (key: FeedbackKind, title: string, examples: string): VBox => {
		const tile = new VBox(`feedbackKind-${key}`, {
			renderType: "Bare",
			items: [
				new Icon({ src: feedbackKinds[key].icon, size: "2rem", decorative: true }).addStyleClass("eoFbTileIcon"),
				new Title({ text: title, level: "H3", wrapping: true }),
				new Text({ text: feedbackKinds[key].intro }),
				new Text({ text: examples }).addStyleClass("eoFbHint"),
			],
		}).addStyleClass("eoFbTile");
		tile.attachBrowserEvent("click", () => chooseKind(key, true));
		tile.attachBrowserEvent("keydown", (event: KeyboardEvent) => {
			if (event.key === "Enter" || event.key === " ") {
				event.preventDefault();
				chooseKind(key, true);
			}
		});
		tile.addEventDelegate({
			onAfterRendering: () => {
				const dom = tile.getDomRef() as HTMLElement | null;
				if (!dom) return;
				dom.tabIndex = 0;
				dom.setAttribute("role", "radio");
				dom.setAttribute("aria-checked", String(kind === key));
				dom.setAttribute("aria-label", `${title}: ${feedbackKinds[key].intro}`);
			},
		});
		tiles.set(key, tile);
		return tile;
	};
	const kindBox = new FlexBox("feedbackKinds", {
		wrap: "Wrap", renderType: "Bare",
		items: [
			kindTile("bug", "Fehler melden", "Zum Beispiel: Speichern hängt, falsche Anzeige, Fehlermeldung beim Öffnen."),
			kindTile("feature", "Funktion wünschen", "Zum Beispiel: zusätzlicher Export, neues Feld, ein Arbeitsschritt weniger."),
		],
	}).addStyleClass("eoFbTiles");
	const kindStep = new WizardStep("feedbackStepKind", {
		title: "Art", icon: "sap-icon://hint", validated: false,
		content: [new VBox({ items: [new Text({ text: "Worum geht es? Wählen Sie eine Kachel – das Formular passt sich an." }).addStyleClass("sapUiSmallMarginBottom"), kindBox] })],
	});

	// ---- Schritt 2: Angaben -----------------------------------------------------------------------------------------
	const f = {
		title: new Input("feedbackTitle", { width: "100%", maxLength: 200, required: true }),
		area: new ComboBox("feedbackArea", {
			width: "100%", required: true, value: startArea,
			items: feedbackAreas.map((a) => new Item({ key: a.name, text: a.name })),
		}),
		steps: new TextArea("feedbackSteps", { width: "100%", rows: 4, maxLength: 10000, growing: true, growingMaxLines: 10,
			placeholder: "1. Bauakte geöffnet\n2. Reiter „Dokumente“ gewählt\n3. Auf „Hochladen“ geklickt" }),
		expected: new TextArea("feedbackExpected", { width: "100%", rows: 2, maxLength: 10000, growing: true, growingMaxLines: 6,
			placeholder: "z. B. Die Datei erscheint in der Liste." }),
		actual: new TextArea("feedbackActual", { width: "100%", rows: 3, maxLength: 10000, growing: true, growingMaxLines: 8, required: true,
			placeholder: "z. B. Die Ladeanzeige dreht sich endlos, Meldung „Verbindung unterbrochen“." }),
		frequency: radios("feedbackFrequency", feedbackFrequencies, 3),
		bugImpact: radios("feedbackBugImpact", bugImpacts, 1),
		problem: new TextArea("feedbackProblem", { width: "100%", rows: 3, maxLength: 10000, growing: true, growingMaxLines: 8, required: true,
			placeholder: "z. B. Für die Nachkalkulation tippe ich jede Woche alle Bauakten ab." }),
		solution: new TextArea("feedbackSolution", { width: "100%", rows: 3, maxLength: 10000, growing: true, growingMaxLines: 8, required: true,
			placeholder: "z. B. Ein Knopf „Als Excel exportieren“ über der Bauaktenliste." }),
		featureImpact: radios("feedbackFeatureImpact", featureImpacts, 1),
		description: new TextArea("feedbackDescription", { width: "100%", rows: 2, maxLength: 10000, growing: true, growingMaxLines: 8 }),
	};
	const label = (text: string, field: Control, required = false) => new Label({ text, labelFor: field.getId(), required, wrapping: true });
	const form = new SimpleForm("feedbackForm", {
		editable: true, layout: "ResponsiveGridLayout", labelSpanXL: 4, labelSpanL: 4, labelSpanM: 4, labelSpanS: 12, columnsL: 1, columnsM: 1,
		singleContainerFullSize: true,
	});
	const fieldStrip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
	const detailStep = new WizardStep("feedbackStepDetails", {
		title: "Angaben", icon: "sap-icon://edit", validated: false, content: [new VBox({ items: [fieldStrip, form] })],
	});
	const buildForm = (): void => {
		form.removeAllContent();
		const bug = kind === "bug";
		const add = (...controls: Control[]) => { for (const c of controls) form.addContent(c); };
		add(formTitle(bug ? "Was funktioniert nicht?" : "Was wünschen Sie sich?"),
			label("Kurze Zusammenfassung", f.title, true),
			withHint(f.title, bug ? "Ein Satz, z. B. „Foto-Upload in der Bauakte bricht ab“." : "Ein Satz, z. B. „Bauakten als Excel exportieren“."),
			label(bug ? "Bereich / Seite" : "Bereich", f.area, true),
			withHint(f.area, "Vorbelegt mit der Seite, auf der Sie gerade waren – bei Bedarf ändern."));
		if (bug) {
			add(label("Was haben Sie gemacht?", f.steps), withHint(f.steps, "Schritt für Schritt – damit die IT den Fehler nachstellen kann."),
				label("Was haben Sie erwartet?", f.expected), f.expected,
				label("Was ist stattdessen passiert?", f.actual, true), withHint(f.actual, "Fehlermeldungen bitte wörtlich abschreiben oder ein Bildschirmfoto anhängen."),
				label("Wie oft?", f.frequency, true), f.frequency,
				label("Auswirkung", f.bugImpact, true), f.bugImpact);
		} else {
			add(label("Problem bzw. Nutzen", f.problem, true), withHint(f.problem, "Welche Arbeit soll leichter, schneller oder sicherer werden?"),
				label("Gewünschte Lösung", f.solution, true), withHint(f.solution, "Wie stellen Sie sich das vor? Grobe Idee genügt."),
				label("Wichtigkeit", f.featureImpact, true), f.featureImpact);
		}
		add(label("Weitere Hinweise", f.description), withHint(f.description, "Optional: Bauakte, Kunde, Uhrzeit oder sonst Hilfreiches."));
	};
	const draft = (): FeedbackDraft => ({
		...emptyDraft(kind ?? "bug"),
		title: f.title.getValue(), area: f.area.getValue(), description: f.description.getValue(), stepsToReproduce: f.steps.getValue(),
		expectedResult: f.expected.getValue(), actualResult: f.actual.getValue(), frequency: radioKey(f.frequency) as FeedbackFrequency | "",
		problem: f.problem.getValue(), desiredSolution: f.solution.getValue(),
		impact: radioKey(kind === "feature" ? f.featureImpact : f.bugImpact) as FeedbackImpact | "",
	});
	const fieldOf: Partial<Record<keyof FeedbackDraft, Control>> = {
		title: f.title, area: f.area, actualResult: f.actual, problem: f.problem, desiredSolution: f.solution,
	};
	const markErrors = (show: boolean): boolean => {
		const missing = missingFields(draft());
		for (const [key, control] of Object.entries(fieldOf)) {
			const isMissing = show && missing.some((m) => m.key === key);
			if ("setValueState" in control) {
				(control as Input).setValueState(isMissing ? ValueState.Error : ValueState.None);
				(control as Input).setValueStateText("Bitte ausfüllen.");
			}
		}
		for (const group of [f.bugImpact, f.featureImpact, f.frequency]) {
			const key = group === f.frequency ? "frequency" : "impact";
			group.setValueState(show && missing.some((m) => m.key === key) ? ValueState.Error : ValueState.None);
		}
		fieldStrip.setText(missing.length ? `Bitte noch ausfüllen: ${missing.map((m) => m.label).join(", ")}.` : "");
		fieldStrip.setVisible(show && missing.length > 0);
		detailStep.setValidated(missing.length === 0);
		return missing.length === 0;
	};
	let showErrors = false;
	const revalidate = () => { markErrors(showErrors); updateButtons(); };
	f.title.attachLiveChange(revalidate);
	for (const control of [f.steps, f.expected, f.actual, f.problem, f.solution, f.description]) control.attachLiveChange(revalidate);
	f.area.attachChange(revalidate);
	for (const group of [f.frequency, f.bugImpact, f.featureImpact]) group.attachSelect(revalidate);

	// ---- Schritt 3: Anhänge -----------------------------------------------------------------------------------------
	const canUpload = session.has("files.upload");
	const files: File[] = [];
	const fileList = new FlexBox("feedbackFiles", { wrap: "Wrap", renderType: "Bare" }).addStyleClass("eoFbFiles");
	const fileStrip = new MessageStrip({ type: "Warning", showIcon: true, visible: false });
	const renderFiles = () => {
		fileList.destroyItems();
		for (const file of files) {
			fileList.addItem(new Button({
				text: `${file.name} (${Math.max(1, Math.round(file.size / 1024))} KB)`, icon: "sap-icon://decline", iconFirst: false, type: "Transparent",
				tooltip: `${file.name} entfernen`, press: () => { files.splice(files.indexOf(file), 1); renderFiles(); },
			}));
		}
	};
	const addFiles = (added: Iterable<File>) => {
		const rejected: string[] = [];
		for (const raw of added) {
			const file = raw.name && raw.name !== "image.png" ? raw : new File([raw], `Bildschirmfoto-${files.length + 1}.png`, { type: raw.type || "image/png" });
			if (file.size > MAX_BYTES) rejected.push(`${file.name} ist größer als 20 MB`);
			else if (files.length >= MAX_FILES) rejected.push(`höchstens ${MAX_FILES} Dateien`);
			else files.push(file);
		}
		fileStrip.setText(rejected.length ? `Nicht übernommen: ${[...new Set(rejected)].join(", ")}.` : "");
		fileStrip.setVisible(rejected.length > 0);
		renderFiles();
	};
	const pick = () => {
		const input = document.createElement("input");
		input.type = "file";
		input.multiple = true;
		input.accept = "image/*,.pdf,.txt,.log,.zip,.doc,.docx,.xlsx,.mp4,.mov";
		input.addEventListener("change", () => addFiles(input.files ?? []));
		input.click();
	};
	const drop = new HTML("feedbackDrop", {
		content: `<div class="eoDropZone eoFbDrop" tabindex="0" role="group" aria-label="Dateien hierher ziehen oder Bildschirmfoto mit Strg+V einfügen"><span class="eoDropText">Dateien hierher ziehen oder Bildschirmfoto mit Strg+V einfügen</span></div>`,
		afterRendering: () => {
			const dom = drop.getDomRef() as HTMLElement | null;
			if (!dom || dom.dataset.eoWired) return;
			dom.dataset.eoWired = "1";
			dom.addEventListener("dragover", (e) => { if (e.dataTransfer?.types.includes("Files")) { e.preventDefault(); dom.classList.add("eoDropActive"); } });
			dom.addEventListener("dragleave", () => dom.classList.remove("eoDropActive"));
			dom.addEventListener("drop", (e) => {
				dom.classList.remove("eoDropActive");
				if (!e.dataTransfer?.files.length) return;
				e.preventDefault();
				addFiles(e.dataTransfer.files);
			});
			dom.addEventListener("click", pick);
		},
	});
	const attachStep = new WizardStep("feedbackStepFiles", {
		title: "Anhänge", icon: "sap-icon://attachment", validated: true,
		content: [new VBox({
			items: canUpload
				? [
					new Text({ text: "Optional: Ein Bildschirmfoto sagt oft mehr als viele Worte. Bis zu 5 Dateien, je höchstens 20 MB." }).addStyleClass("sapUiSmallMarginBottom"),
					drop,
					new HBox({ alignItems: "Center", wrap: "Wrap", items: [
						new Button("feedbackPick", { text: "Datei auswählen", icon: "sap-icon://attachment", press: pick }),
						new Text({ text: "Tipp: Windows+Umschalt+S nimmt einen Bildschirmausschnitt auf, Strg+V fügt ihn hier ein." }).addStyleClass("eoFbHint sapUiSmallMarginBegin"),
					] }).addStyleClass("sapUiSmallMarginTop sapUiSmallMarginBottom"),
					fileStrip, fileList,
				]
				: [new MessageStrip({ type: "Information", showIcon: true,
					text: "Mit Ihrer Rolle können keine Dateien hochgeladen werden. Beschreiben Sie das Problem bitte möglichst genau im Text – die IT meldet sich bei Rückfragen." })],
		})],
	});

	// ---- Schritt 4: Prüfen ------------------------------------------------------------------------------------------
	const review = new VBox("feedbackReview", { renderType: "Bare" });
	const reviewStep = new WizardStep("feedbackStepReview", { title: "Prüfen", icon: "sap-icon://accept", validated: true, content: [review] });
	const fillReview = () => {
		review.destroyItems();
		const display = (title: string, rows: { label: string; value: string }[]) => new SimpleForm({
			editable: false, layout: "ResponsiveGridLayout", labelSpanXL: 4, labelSpanL: 4, labelSpanM: 4, labelSpanS: 12, columnsL: 1, columnsM: 1,
			singleContainerFullSize: true,
			content: [formTitle(title), ...rows.flatMap((r) => [new Label({ text: r.label, wrapping: true }), new Text({ text: r.value }).addStyleClass("eoPre")])],
		});
		review.addItem(display("Ihre Angaben", summaryRows(draft(), files.map((x) => x.name))));
		review.addItem(display("Automatisch mitgesendet", contextRows(context)));
		review.addItem(new Text({ text: "Die technischen Angaben helfen der IT, einen Fehler nachzustellen. Formularinhalte oder Passwörter werden nicht übertragen." })
			.addStyleClass("eoFbHint sapUiSmallMarginTop"));
	};

	const steps = [kindStep, detailStep, attachStep, reviewStep];
	const wizard = new Wizard("feedbackWizard", { steps, showNextButton: false, renderMode: "Page", height: "100%", enableBranching: false });
	const strip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
	let stepIndex = 0;
	let updateButtons: () => void = () => undefined;

	function chooseKind(key: FeedbackKind, advance: boolean): void {
		const changed = kind !== key;
		kind = key;
		for (const [k, tile] of tiles) {
			tile.toggleStyleClass("eoFbTileSelected", k === key);
			tile.getDomRef()?.setAttribute("aria-checked", String(k === key));
		}
		kindStep.setValidated(true);
		if (changed) {
			buildForm();
			showErrors = false;
			markErrors(false);
		}
		dialog.setTitle(key === "bug" ? "Fehler melden" : "Funktion wünschen");
		updateButtons();
		if (advance && stepIndex === 0) go(1);
	}

	const go = (index: number) => {
		if (index < 0 || index >= steps.length) return;
		strip.setVisible(false);
		if (index > stepIndex) {
			while (stepIndex < index) {
				wizard.nextStep();
				stepIndex++;
			}
		} else {
			wizard.goToStep(steps[index]!, true);
			stepIndex = index;
		}
		if (index === 3) fillReview();
		if (index === 1 && !f.title.getValue()) setTimeout(() => f.title.focus(), 50);
		updateButtons();
	};

	return new Promise((resolve) => {
		let result: FeedbackDetail | null = null;
		const back = new Button("feedbackBack", { text: "Zurück", press: () => go(stepIndex - 1) });
		const next = new Button("feedbackNext", { text: "Weiter", type: "Emphasized", press: () => forward() });
		const send = new Button("feedbackSend", { text: "Absenden", type: "Emphasized", icon: "sap-icon://paper-plane", press: () => void submit() });
		const cancel = new Button("feedbackCancel", { text: "Abbrechen", press: () => dialog.close() });
		const mine = new Button("feedbackMine", { text: "Zu meinen Meldungen", type: "Emphasized", visible: false, press: () => {
			dialog.close();
			if (result) options.onShowMine?.(result);
		} });
		const another = new Button("feedbackAnother", { text: "Weitere Meldung", visible: false, press: () => {
			dialog.close();
			setTimeout(() => void openFeedbackWizard(options), 300);
		} });
		const close = new Button("feedbackClose", { text: "Schließen", visible: false, press: () => dialog.close() });

		updateButtons = () => {
			back.setVisible(stepIndex > 0);
			next.setVisible(stepIndex < steps.length - 1);
			next.setEnabled(stepIndex !== 0 || !!kind);
			send.setVisible(stepIndex === steps.length - 1);
		};

		const forward = () => {
			if (stepIndex === 1) {
				showErrors = true;
				if (!markErrors(true)) {
					updateButtons();
					return;
				}
			}
			go(stepIndex + 1);
		};

		const submit = async () => {
			dialog.setBusy(true);
			strip.setVisible(false);
			try {
				const attachmentFileIds: string[] = [];
				for (const file of files) attachmentFileIds.push(await uploadLoose(file));
				result = await post<FeedbackDetail>("feedback", createRequest(uuidv7(), draft(), context, attachmentFileIds));
				showConfirmation(result);
			} catch (error) {
				strip.setText(problemMessage(error));
				strip.setVisible(true);
			} finally {
				dialog.setBusy(false);
			}
		};

		const content = new VBox({ height: "100%", renderType: "Bare", items: [strip, wizard] });
		const showConfirmation = (created: FeedbackDetail) => {
			const bug = created.summary.kind === "bug";
			const done = new IllustratedMessage("feedbackDone", {
				illustrationType: "sapIllus-SuccessHighFive", illustrationSize: "Dialog", enableDefaultTitleAndDescription: false,
				// Geschützte Bindestriche: die Nummer bricht nicht um („F-“ / „2026-0011“).
				title: `Vielen Dank! Ihre Meldung ${created.summary.number.replace(/-/g, "\u2011")} ist eingegangen.`,
				description: bug
					? "Die IT prüft den Fehler und meldet sich im Verlauf der Meldung. Status und Antworten sehen Sie unter „Meine Meldungen“ – Sie werden bei Änderungen benachrichtigt."
					: "Die IT prüft Ihren Wunsch und plant ihn ein. Status und Antworten sehen Sie unter „Meine Meldungen“ – Sie werden bei Änderungen benachrichtigt.",
			});
			content.destroyItems(); // Assistent mit festen IDs gleich mit aufräumen
			content.addItem(new VBox({ alignItems: "Center", justifyContent: "Center", height: "100%", items: [done] }).addStyleClass("eoFbDone"));
			dialog.setTitle(`Meldung ${created.summary.number} gesendet`);
			for (const b of [back, next, send, cancel]) b.setVisible(false);
			for (const b of [mine, another, close]) b.setVisible(true);
		};

		dialog = new Dialog("feedbackDialog", {
			title: "Fehler melden oder Funktion wünschen",
			contentWidth: "46rem",
			contentHeight: "38rem",
			resizable: true,
			draggable: true,
			verticalScrolling: false,
			content: [content],
			buttons: [back, next, send, mine, another, close, cancel],
			afterClose: () => {
				document.removeEventListener("paste", paste);
				dialog.destroy();
				// Felder der jeweils anderen Art hängen nicht im Dialog (feste IDs): ebenfalls aufräumen.
				for (const control of Object.values(f)) if (!control.isDestroyed()) control.destroy();
				resolve(result);
			},
		});
		const paste = (e: ClipboardEvent) => {
			if (!canUpload || stepIndex !== 2) return;
			const images = [...(e.clipboardData?.files ?? [])];
			if (images.length) {
				e.preventDefault();
				addFiles(images);
			}
		};
		document.addEventListener("paste", paste);
		dialog.addStyleClass("eoWizardDialog eoFeedbackDialog");
		if (options.kind) chooseKind(options.kind, false);
		updateButtons();
		dialog.open();
	});
}
