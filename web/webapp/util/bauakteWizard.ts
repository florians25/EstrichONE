/**
 * Geführte Anlage einer Bauakte oder Anfrage (sap.m.Wizard im Dialog):
 *  1. Kunde (Pflicht) – freie Eingabe mit Vorschlägen vorhandener Kunden; vor dem Weitergehen prüft der Server auf
 *     Dubletten (Name, Telefon, E-Mail, Anschrift). Treffer werden angezeigt: vorhandenen Kunden wählen oder bewusst
 *     neu anlegen.
 *  2. Bauvorhaben und Baustelle – Baustellenadresse auf Wunsch vom Kunden übernehmen.
 *  3. Prüfen und anlegen.
 */
import { projectView, projectWizardControls } from "./project";
import Dialog from "sap/m/Dialog";
import { loadSystemSettings } from "./systemSettings";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import Label from "sap/m/Label";
import Title from "sap/m/Title";
import Text from "sap/m/Text";
import CheckBox from "sap/m/CheckBox";
import SegmentedButton from "sap/m/SegmentedButton";
import SegmentedButtonItem from "sap/m/SegmentedButtonItem";
import MessageStrip from "sap/m/MessageStrip";
import MessageToast from "sap/m/MessageToast";
import Wizard from "sap/m/Wizard";
import WizardStep from "sap/m/WizardStep";
import List from "sap/m/List";
import CustomListItem from "sap/m/CustomListItem";
import HBox from "sap/m/HBox";
import VBox from "sap/m/VBox";
import ObjectStatus from "sap/m/ObjectStatus";
import SuggestionItem from "sap/m/SuggestionItem";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import type Control from "sap/ui/core/Control";
import { ValueState } from "sap/ui/core/library";
import { get, post, problemMessage, put } from "./api";
import { catalog, type BauakteTemplate } from "./catalog";
import Select from "sap/m/Select";
import Item from "sap/ui/core/Item";
import { uuidv7 } from "./ids";
import type { BauakteDetail, BauakteStatus, ContactResponse } from "./models";

export interface WizardOptions {
	status: Extract<BauakteStatus, "planned" | "inquiry">;
}

export interface DuplicateCandidate {
	contact: ContactResponse;
	score: number;
	reasons: string[];
}

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function form(content: Control[]): SimpleForm {
	return new SimpleForm({
		editable: true, layout: "ResponsiveGridLayout", labelSpanXL: 4, labelSpanL: 4, labelSpanM: 4, labelSpanS: 12,
		columnsL: 1, columnsM: 1, singleContainerFullSize: true, content,
	});
}

export function openBauakteWizard(options: WizardOptions): Promise<(BauakteDetail & { startInspection?: boolean }) | null> {
	const titleOf = (status: string) => (status === "inquiry" ? "Neue Anfrage" : "Neue Bauakte");

	// ---- Schritt 1: Kunde -----------------------------------------------------------------------------------------
	let existing: ContactResponse | null = null;
	let candidates: DuplicateCandidate[] = [];
	let checkedFor = "";
	const customerKind = new SegmentedButton("newCustomerKind", {
		selectedKey: "person",
		items: [new SegmentedButtonItem({ key: "person", text: "Person" }), new SegmentedButtonItem({ key: "company", text: "Firma" })],
	});
	const c = {
		name: new Input("newCustomerName", {
			width: "100%", maxLength: 200, placeholder: "Name, Firma – vorhandene Kunden werden vorgeschlagen",
			showSuggestion: true, filterSuggests: false, startSuggestion: 2, maxSuggestionWidth: "34rem",
		}),
		number: new Input("newCustomerNumber", { width: "100%", maxLength: 50 }),
		street: new Input("newCustomerStreet", { width: "100%", maxLength: 200 }),
		postalCode: new Input("newCustomerPostalCode", { width: "100%", maxLength: 10 }),
		city: new Input("newCustomerCity", { width: "100%", maxLength: 100 }),
		phone: new Input("newCustomerPhone", { width: "100%", type: "Tel", maxLength: 50 }),
		email: new Input("newCustomerEmail", { width: "100%", type: "Email", maxLength: 320 }),
	};
	const chosen = new MessageStrip({ type: "Success", showIcon: true, visible: false, showCloseButton: true });
	const dupStrip = new MessageStrip({
		type: "Warning", showIcon: true, visible: false,
		text: "Es gibt bereits ähnliche Kunden. Bitte einen vorhandenen Kunden verwenden oder bewusst einen neuen anlegen.",
	});
	const dupList = new List("newCustomerDuplicates", { visible: false, headerText: "Mögliche Dubletten" });
	const forceNew = new CheckBox("newCustomerForce", { text: "Trotzdem als neuen Kunden anlegen", visible: false });

	const customerFields = (): Record<string, string | null> => ({
		name: c.name.getValue().trim(), street: c.street.getValue().trim() || null, postalCode: c.postalCode.getValue().trim() || null,
		city: c.city.getValue().trim() || null, phone: c.phone.getValue().trim() || null, email: c.email.getValue().trim() || null,
	});
	const fingerprint = () => JSON.stringify(customerFields());

	const useExisting = (contact: ContactResponse) => {
		existing = contact;
		c.name.setValue(contact.name);
		c.number.setValue(contact.customerNumber ?? "");
		c.street.setValue(contact.street ?? "");
		c.postalCode.setValue(contact.postalCode ?? "");
		c.city.setValue(contact.city ?? "");
		c.phone.setValue(contact.phone ?? "");
		c.email.setValue(contact.email ?? "");
		customerKind.setSelectedKey(contact.kind === "company" ? "company" : "person");
		for (const field of Object.values(c)) field.setEditable(field === c.name);
		customerKind.setEnabled(false);
		chosen.setText(`Vorhandener Kunde: ${contact.name}${contact.customerNumber ? ` (${contact.customerNumber})` : ""}${contact.city ? `, ${contact.city}` : ""}`);
		chosen.setVisible(true);
		showDuplicates([]);
		checkedFor = fingerprint();
		validate();
	};
	const release = () => {
		existing = null;
		for (const field of Object.values(c)) field.setEditable(true);
		customerKind.setEnabled(true);
		chosen.setVisible(false);
	};
	chosen.attachClose(() => { release(); validate(); });

	const showDuplicates = (list: DuplicateCandidate[]) => {
		candidates = list;
		dupList.destroyItems();
		for (const d of list) {
			const contact = d.contact;
			dupList.addItem(new CustomListItem({
				content: [new HBox({
					alignItems: "Center", justifyContent: "SpaceBetween", width: "100%",
					items: [
						new VBox({ items: [
							new Text({ text: `${contact.name}${contact.customerNumber ? ` · ${contact.customerNumber}` : ""}` }).addStyleClass("sapUiTinyMarginBottom"),
							new Text({ text: [contact.street, [contact.postalCode, contact.city].filter(Boolean).join(" "), contact.phone, contact.email].filter(Boolean).join(" · ") || "–" }).addStyleClass("eoMuted"),
							new ObjectStatus({ text: d.reasons.join(", "), state: d.score >= 60 ? "Error" : "Warning" }),
						] }),
						new Button({ text: "Diesen Kunden verwenden", type: "Emphasized", press: () => useExisting(contact) }),
					],
				}).addStyleClass("sapUiTinyMargin")],
			}));
		}
		const any = list.length > 0;
		dupStrip.setVisible(any);
		dupList.setVisible(any);
		forceNew.setVisible(any);
		forceNew.setSelected(false);
	};

	let timer: ReturnType<typeof setTimeout> | undefined;
	c.name.attachSuggest((event) => {
		const text = String(event.getParameter("suggestValue") ?? "").trim();
		clearTimeout(timer);
		timer = setTimeout(async () => {
			try {
				const list = await get<ContactResponse[]>("contacts", { search: text, limit: 10 });
				c.name.destroySuggestionItems();
				for (const contact of list) {
					c.name.addSuggestionItem(new SuggestionItem({ key: contact.id, text: contact.name, description: [contact.customerNumber, contact.city].filter(Boolean).join(" · ") }));
				}
			} catch {
				c.name.destroySuggestionItems();
			}
		}, 150);
	});
	c.name.attachSuggestionItemSelected(async (event) => {
		const item = event.getParameter("selectedItem") as SuggestionItem | null;
		if (!item) return;
		try {
			useExisting(await get<ContactResponse>(`contacts/${item.getKey()}`));
		} catch (error) {
			MessageToast.show(problemMessage(error));
		}
	});
	c.name.attachLiveChange(() => {
		if (existing && c.name.getValue().trim() !== existing.name) release();
		validate();
	});
	for (const field of [c.street, c.postalCode, c.city, c.phone, c.email]) field.attachLiveChange(() => validate());
	forceNew.attachSelect(() => validate());

	const customerStep = new WizardStep("newStepCustomer", {
		title: "Kunde", icon: "sap-icon://customer", validated: false,
		content: [
			chosen,
			form([
				new Label({ text: "Name des Kunden", labelFor: c.name, required: true }), c.name,
				new Label({ text: "Art", labelFor: customerKind }), customerKind,
				new Label({ text: "Kundennummer", labelFor: c.number }), c.number,
				new Label({ text: "Straße (Kunde)", labelFor: c.street }), c.street,
				new Label({ text: "PLZ (Kunde)", labelFor: c.postalCode }), c.postalCode,
				new Label({ text: "Ort (Kunde)", labelFor: c.city }), c.city,
				new Label({ text: "Telefon (Kunde)", labelFor: c.phone }), c.phone,
				new Label({ text: "E-Mail (Kunde)", labelFor: c.email }), c.email,
			]),
			dupStrip, dupList, forceNew,
		],
	});

	// ---- Schritt 2: Bauvorhaben und Baustelle -----------------------------------------------------------------------
	const title = new Input("newTitle", { placeholder: "z. B. EFH Müller, Neubau", width: "100%", maxLength: 200 });
	const kind = new SegmentedButton("newKind", {
		selectedKey: options.status,
		items: [new SegmentedButtonItem({ key: "planned", text: "Bauakte (Auftrag)" }), new SegmentedButtonItem({ key: "inquiry", text: "Anfrage" })],
	});
	const site = {
		street: new Input("newStreet", { width: "100%", maxLength: 200 }),
		postalCode: new Input("newPostalCode", { width: "100%", maxLength: 10 }),
		city: new Input("newCity", { width: "100%", maxLength: 100 }),
		description: new TextArea("newDescription", { rows: 3, width: "100%", maxLength: 4000, growing: true }),
	};
	const copy = new Button("newCopyAddress", {
		text: "Adresse des Kunden übernehmen", icon: "sap-icon://copy", type: "Transparent",
		press: () => {
			site.street.setValue(c.street.getValue());
			site.postalCode.setValue(c.postalCode.getValue());
			site.city.setValue(c.city.getValue());
		},
	});
	title.attachLiveChange(() => validate());
	// Bauakten-Vorlagen aus den Stammdaten: Beschreibung und Standardräume.
	let templates: BauakteTemplate[] = [];
	const template = new Select("newTemplate", { width: "100%", forceSelection: false, items: [new Item({ key: "", text: "– ohne Vorlage –" })] });
	void Promise.all([catalog<BauakteTemplate>("bauakteTemplate"), loadSystemSettings()]).then(([list, settings]) => {
		templates = list;
		for (const t of list) {
			template.addItem(new Item({ key: t.id, text: `${t.name}${t.content.rooms?.length ? ` (${t.content.rooms.length} Räume)` : ""}` }));
		}
		// Vorgeschlagene Vorlage aus den Einstellungen.
		const preferred = settings?.settings.bauakte.defaultTemplateId;
		if (preferred && list.some((t) => t.id === preferred) && !template.getSelectedKey()) template.setSelectedKey(preferred);
	});
	template.attachChange(() => {
		const t = templates.find((x) => x.id === template.getSelectedKey());
		if (t?.content.description && !site.description.getValue().trim()) site.description.setValue(t.content.description);
	});
	const kennwerte = projectWizardControls("new");
	const projectStep = new WizardStep("newStepProject", {
		title: "Bauvorhaben", icon: "sap-icon://building", validated: false,
		content: [form([
			new Label({ text: "Bauvorhaben", labelFor: title, required: true }), title,
			new Label({ text: "Anlegen als", labelFor: kind }), kind,
			new Label({ text: "Vorlage", labelFor: template }), template,
			new Title({ text: "Baustelle", level: "H5" }),
			new Label({ text: "" }), copy,
			new Label({ text: "Straße", labelFor: site.street }), site.street,
			new Label({ text: "PLZ", labelFor: site.postalCode }), site.postalCode,
			new Label({ text: "Ort", labelFor: site.city }), site.city,
			new Label({ text: "Beschreibung", labelFor: site.description }), site.description,
			...kennwerte.content,
		])],
	});

	// ---- Schritt 3: Prüfen -------------------------------------------------------------------------------------------
	const summary = new VBox({ renderType: "Bare" });
	// Beim Kunden vor Ort: direkt im Anschluss die Erstbegehung der neuen Akte erfassen.
	const inspectNow = new CheckBox("newInspectNow", { text: "Direkt im Anschluss die Erstbegehung durchführen (z. B. jetzt beim Kunden vor Ort)" });
	const reviewStep = new WizardStep("newStepReview", { title: "Prüfen", icon: "sap-icon://accept", validated: true,
		content: [new VBox({ items: [summary, inspectNow.addStyleClass("sapUiSmallMarginTop")] })] });
	const fillSummary = () => {
		const line = (label: string, value: string) => new HBox({ items: [new Label({ text: label, width: "10rem" }), new Text({ text: value || "–" })] }).addStyleClass("sapUiTinyMarginBottom");
		summary.destroyItems();
		const f = customerFields();
		summary.addItem(new Title({ text: "Kunde", level: "H5" }).addStyleClass("sapUiTinyMarginBottom"));
		summary.addItem(line("Kunde", `${f.name}${existing ? " (vorhanden)" : " (wird neu angelegt)"}`));
		summary.addItem(line("Anschrift", [f.street, [f.postalCode, f.city].filter(Boolean).join(" ")].filter(Boolean).join(", ")));
		summary.addItem(line("Kontakt", [f.phone, f.email].filter(Boolean).join(" · ")));
		summary.addItem(new Title({ text: "Bauvorhaben", level: "H5" }).addStyleClass("sapUiSmallMarginTop sapUiTinyMarginBottom"));
		summary.addItem(line("Bauvorhaben", title.getValue().trim()));
		summary.addItem(line("Anlegen als", kind.getSelectedKey() === "inquiry" ? "Anfrage" : "Bauakte (Auftrag)"));
		const chosenTemplate = templates.find((x) => x.id === template.getSelectedKey());
		if (chosenTemplate) summary.addItem(line("Vorlage", `${chosenTemplate.name}${chosenTemplate.content.rooms?.length ? ` – legt ${chosenTemplate.content.rooms.length} Räume an` : ""}`));
		const pv = projectView(kennwerte.read());
		summary.addItem(line("Kennwerte", [pv.objectType, pv.totalArea, pv.screedType, pv.construction, pv.thickness, pv.strengthClass]
			.filter((x) => x !== "–").join(" · ")));
		summary.addItem(line("Baustelle", [site.street.getValue().trim(), [site.postalCode.getValue().trim(), site.city.getValue().trim()].filter(Boolean).join(" ")].filter(Boolean).join(", ")));
	};

	const wizard = new Wizard("newBauakteWizard", {
		steps: [customerStep, projectStep, reviewStep], showNextButton: false, renderMode: "Page", height: "100%",
		enableBranching: false,
	});

	const strip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
	let stepIndex = 0;
	const steps = [customerStep, projectStep, reviewStep];

	let updateButtons: () => void = () => undefined;

	function validate(): void {
		const f = customerFields();
		const emailOk = !f.email || emailPattern.test(f.email);
		c.email.setValueState(emailOk ? ValueState.None : ValueState.Error);
		c.email.setValueStateText("Bitte eine gültige E-Mail-Adresse angeben.");
		const pendingDuplicates = !existing && candidates.length > 0 && checkedFor === fingerprint() && !forceNew.getSelected();
		customerStep.setValidated(!!f.name && emailOk && !pendingDuplicates);
		projectStep.setValidated(!!title.getValue().trim());
		updateButtons();
	}

	return new Promise((resolve) => {
		let result: BauakteDetail | null = null;
		const back = new Button("newBauakteBack", { text: "Zurück", press: () => go(stepIndex - 1) });
		const next = new Button("newBauakteNext", { text: "Weiter", type: "Emphasized", press: () => void forward() });
		const create = new Button("newBauakteCreate", { text: "Anlegen", type: "Emphasized", press: () => void submit() });
		const cancel = new Button({ text: "Abbrechen", press: () => dialog.close() });

		updateButtons = () => {
			back.setVisible(stepIndex > 0);
			next.setVisible(stepIndex < steps.length - 1);
			// Im Kundenschritt prüft „Weiter“ selbst (Pflichtname, Dubletten) und erklärt, was fehlt.
			next.setEnabled(stepIndex === 0 || (steps[stepIndex]?.getValidated() ?? false));
			create.setVisible(stepIndex === steps.length - 1);
		};

		const go = (index: number) => {
			if (index < 0 || index >= steps.length) return;
			strip.setVisible(false);
			if (index > stepIndex) {
				while (stepIndex < index) {
					wizard.nextStep();
					stepIndex++;
				}
			} else {
				wizard.goToStep(steps[index], true);
				stepIndex = index;
			}
			if (index === 2) fillSummary();
			if (index === 1 && !title.getValue()) setTimeout(() => title.focus(), 50);
			updateButtons();
		};

		/** Weiter: im Kundenschritt erst die Dublettenprüfung des Servers. */
		const forward = async () => {
			if (stepIndex === 0) {
				const f = customerFields();
				if (!f.name) {
					c.name.setValueState(ValueState.Error);
					c.name.setValueStateText("Bitte den Kunden angeben – ohne Kunde keine Bauakte.");
					strip.setText("Bitte den Kunden angeben – ohne Kunde keine Bauakte.");
					strip.setVisible(true);
					return;
				}
				c.name.setValueState(ValueState.None);
				if (!existing && checkedFor !== fingerprint()) {
					dialog.setBusy(true);
					try {
						const found = await get<DuplicateCandidate[]>("contacts/duplicates", {
							name: f.name, street: f.street, postalCode: f.postalCode, city: f.city, phone: f.phone, email: f.email,
						});
						checkedFor = fingerprint();
						showDuplicates(found);
						validate();
						if (found.length > 0) return;
					} catch (error) {
						strip.setText(problemMessage(error));
						strip.setVisible(true);
						return;
					} finally {
						dialog.setBusy(false);
					}
				}
				if (!customerStep.getValidated()) return;
			}
			go(stepIndex + 1);
		};

		const submit = async () => {
			dialog.setBusy(true);
			strip.setVisible(false);
			try {
				let customerId = existing?.id ?? null;
				if (!existing) {
					const f = customerFields();
					const contact = await post<ContactResponse>("contacts", {
						id: uuidv7(), kind: customerKind.getSelectedKey(), name: f.name, customerNumber: c.number.getValue().trim() || null,
						street: f.street, postalCode: f.postalCode, city: f.city, phone: f.phone, email: f.email, notes: null,
					});
					// Kunde ist angelegt: bei erneutem Versuch nicht doppelt anlegen.
					useExisting(contact);
					customerId = contact.id;
				}
				const status = kind.getSelectedKey() as BauakteStatus;
				result = await post<BauakteDetail>("bauakten", {
					id: uuidv7(), title: title.getValue().trim(), customerId, street: site.street.getValue().trim() || null,
					postalCode: site.postalCode.getValue().trim() || null, city: site.city.getValue().trim() || null,
					description: site.description.getValue().trim() || null, status,
					templateKey: template.getSelectedKey() || null,
					project: kennwerte.read(), plannedStart: kennwerte.period().start, plannedEnd: kennwerte.period().end,
				});
				const chosenTemplate = templates.find((x) => x.id === template.getSelectedKey());
				for (const [index, room] of (chosenTemplate?.content.rooms ?? []).entries()) {
					const roomId = uuidv7();
					await put(`bauakten/${result.id}/rooms/${roomId}`, {
						id: roomId, sectionId: null, name: room.name, floor: room.floor ?? null, areaSquareMeters: room.areaSquareMeters ?? null,
						screedTypeId: null, thicknessMillimeters: room.thicknessMillimeters ?? null, insulationMillimeters: null,
						underfloorHeating: !!room.underfloorHeating, status: "planned", notes: null, sortOrder: index,
					});
				}
				if (inspectNow.getSelected()) (result as BauakteDetail & { startInspection?: boolean }).startInspection = true;
				MessageToast.show(status === "inquiry" ? "Anfrage angelegt" : "Bauakte angelegt");
				dialog.close();
			} catch (error) {
				strip.setText(problemMessage(error));
				strip.setVisible(true);
			} finally {
				dialog.setBusy(false);
			}
		};

		const dialog = new Dialog("newBauakteDialog", {
			title: titleOf(options.status),
			contentWidth: "44rem",
			contentHeight: "34rem",
			resizable: true,
			draggable: true,
			verticalScrolling: false,
			initialFocus: c.name,
			content: [new VBox({ height: "100%", renderType: "Bare", items: [strip, wizard] })],
			buttons: [back, next, create, cancel],
			afterClose: () => {
				dialog.destroy();
				resolve(result);
			},
		});
		kind.attachSelectionChange(() => dialog.setTitle(titleOf(kind.getSelectedKey())));
		dialog.addStyleClass("eoWizardDialog");
		validate();
		dialog.open();
	});
}
