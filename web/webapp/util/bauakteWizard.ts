/**
 * Geführte Anlage einer Bauakte oder Anfrage (sap.m.Wizard in einem großen Dialog mit Zusammenfassung rechts):
 *  1. Kunde (Pflicht) – Suche mit Vorschlägen vorhandener Kunden oder Neuanlage (kurz im Schritt oder ausführlich im
 *     Kundendialog). Schon beim Tippen prüft der Server auf Dubletten (Name, Telefon, E-Mail, Anschrift); Treffer
 *     werden angezeigt: vorhandenen Kunden wählen oder bewusst neu anlegen.
 *  2. Bauvorhaben – Bezeichnung, Vorlage, Zuständige(r), Baustellenadresse „wie Kundenanschrift“ oder abweichend (mit
 *     Hinweis auf Bauakten an derselben Adresse), Eckdaten (Objektart, Geschosse, Fläche, Estrich, Dicke, Heizung) und
 *     Ausführung (Zeitraum, Priorität) aus dem Formular-Schema der Stammdaten.
 *  3. Beteiligte (optional) – Ansprechpartner vor Ort, Bauleitung, Architekt oder Planer.
 *  4. Weitere Angaben (optional) – übrige Kennwerte, Erstbegehung (später, Termin eintragen, direkt im Anschluss oder
 *     nicht erforderlich) und eine interne Notiz.
 *  5. Prüfen und anlegen; danach öffnet der Aufrufer die neue Bauakte.
 * „Anlegen“ steht ab Schritt 2 zur Verfügung: die Schritte 3 und 4 sind Ergänzungen und können entfallen.
 */
import { schemaWizardControls } from "./project";
import Link from "sap/m/Link";
import { openNewCustomerDialog } from "./customerDialog";
import { addressLine, atSameAddress, draftKey, duplicateQueries, hasAddress, mergeCandidates, siteFromCustomer, type DuplicateCandidate } from "./customerMatch";
import { saveBauakte } from "./bauakte";
import { session } from "./session";
import { bauakteStatus } from "./labels";
import { bauakteSchema, displayValue, fieldsOf, writeValues, type FormSchema } from "./formSchema";
import Dialog from "sap/m/Dialog";
import { loadSystemSettings } from "./systemSettings";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import Label from "sap/m/Label";
import Title from "sap/m/Title";
import Text from "sap/m/Text";
import CheckBox from "sap/m/CheckBox";
import DatePicker from "sap/m/DatePicker";
import TimePicker from "sap/m/TimePicker";
import RadioButton from "sap/m/RadioButton";
import RadioButtonGroup from "sap/m/RadioButtonGroup";
import SegmentedButton from "sap/m/SegmentedButton";
import SegmentedButtonItem from "sap/m/SegmentedButtonItem";
import MessageBox from "sap/m/MessageBox";
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
import Device from "sap/ui/Device";
import { formTitle } from "./formLayout";
import type Control from "sap/ui/core/Control";
import { ValueState } from "sap/ui/core/library";
import { get, post, problemMessage, put } from "./api";
import { catalog, type BauakteTemplate } from "./catalog";
import Select from "sap/m/Select";
import Item from "sap/ui/core/Item";
import { uuidv7 } from "./ids";
import { calendars, calendarChanged } from "./calendarDialog";
import { localIso } from "./planningModels";
import { appointmentSpan, inspectionChoices, partyBody, splitSchema, type InspectionChoice, type PartyDraft } from "./bauakteDraft";
import { date } from "./format";
import type { BauaktePage, BauakteDetail, BauakteStatus, ContactResponse } from "./models";

export interface WizardOptions {
	status: Extract<BauakteStatus, "planned" | "inquiry">;
	/** Vorgewählter Kunde (z. B. von der Kundenseite). */
	customer?: ContactResponse | null;
}

export type { DuplicateCandidate } from "./customerMatch";
export { splitSchema } from "./bauakteDraft";

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Einspaltiges Formular; in den schmalen Spalten des Rasters stehen Beschriftungen über den Feldern. */
function form(content: Control[]): SimpleForm {
	return new SimpleForm({
		editable: true, layout: "ResponsiveGridLayout", labelSpanXL: 4, labelSpanL: 4, labelSpanM: 12, labelSpanS: 12,
		columnsXL: 1, columnsL: 1, columnsM: 1, singleContainerFullSize: true, content,
	}).addStyleClass("eoNewAkteForm");
}

/** Raster für Formulare nebeneinander (bricht bei wenig Platz auf eine Spalte um). */
function grid(items: Control[], narrow = false): HBox {
	return new HBox({ renderType: "Bare", items }).addStyleClass(narrow ? "eoNewAkteGrid eoNewAkteGridNarrow" : "eoNewAkteGrid");
}

interface SummarySection { title: string; rows: [string, string][] }

/** Zusammenfassung in einen Container schreiben (rechts im Dialog kompakt, im Schritt „Prüfen“ ausführlich). */
function renderSummary(target: VBox, sections: SummarySection[]): void {
	target.destroyItems();
	for (const s of sections) {
		if (!s.rows.length) continue;
		target.addItem(new Title({ text: s.title, level: "H5", wrapping: true }).addStyleClass("eoNewAkteSumTitle"));
		for (const [label, value] of s.rows) {
			target.addItem(new HBox({ renderType: "Bare", items: [
				new Text({ text: label }).addStyleClass("eoNewAkteSumLabel"),
				new Text({ text: value || "–", wrapping: true }).addStyleClass("eoNewAkteSumValue"),
			] }).addStyleClass("eoNewAkteSumRow"));
		}
	}
}

export function openBauakteWizard(options: WizardOptions): Promise<(BauakteDetail & { startInspection?: boolean }) | null> {
	const titleOf = (status: string) => (status === "inquiry" ? "Neue Anfrage" : "Neue Bauakte");
	let refreshSummary: () => void = () => undefined;

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
			width: "100%", maxLength: 200, placeholder: "Name, Firma (vorhandene Kunden werden vorgeschlagen)",
			showSuggestion: true, filterSuggests: false, startSuggestion: 2, maxSuggestionWidth: "34rem",
		}),
		number: new Input("newCustomerNumber", { width: "100%", maxLength: 50, placeholder: "wird automatisch vergeben", editable: session.has("settings.manage") }),
		street: new Input("newCustomerStreet", { width: "100%", maxLength: 200 }),
		postalCode: new Input("newCustomerPostalCode", { width: "100%", maxLength: 10 }),
		city: new Input("newCustomerCity", { width: "100%", maxLength: 100 }),
		phone: new Input("newCustomerPhone", { width: "100%", type: "Tel", maxLength: 50 }),
		email: new Input("newCustomerEmail", { width: "100%", type: "Email", maxLength: 320 }),
	};
	const chosen = new MessageStrip({ type: "Success", showIcon: true, visible: false, showCloseButton: true }).addStyleClass("sapUiSmallMarginBottom");
	const dupStrip = new MessageStrip({
		type: "Warning", showIcon: true, visible: false,
		text: "Es gibt bereits ähnliche Kunden. Bitte einen vorhandenen Kunden verwenden oder bewusst einen neuen anlegen.",
	}).addStyleClass("sapUiSmallMarginTop");
	const dupList = new List("newCustomerDuplicates", { visible: false, headerText: "Mögliche Dubletten" }).addStyleClass("eoNewAkteDupList");
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
		detailed.setVisible(false);
		showDuplicates([]);
		checkedFor = fingerprint();
		validate();
	};
	const release = () => {
		existing = null;
		for (const field of Object.values(c)) field.setEditable(field !== c.number || session.has("settings.manage"));
		customerKind.setEnabled(true);
		chosen.setVisible(false);
		detailed.setVisible(true);
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
		scheduleDuplicates();
	});
	for (const field of [c.street, c.postalCode, c.city, c.phone, c.email]) field.attachLiveChange(() => { validate(); scheduleDuplicates(); });
	forceNew.attachSelect(() => validate());
	customerKind.attachSelectionChange(() => validate());

	// Dublettenprüfung schon beim Tippen (verzögert); „Weiter“ prüft zusätzlich den letzten Stand.
	let dupTimer: ReturnType<typeof setTimeout> | undefined;
	let dupRequest = 0;
	const draft = () => {
		const f = customerFields();
		return { name: f.name ?? "", street: f.street, postalCode: f.postalCode, city: f.city, phones: f.phone ? [f.phone] : [], emails: f.email ? [f.email] : [] };
	};
	const runDuplicates = async (): Promise<DuplicateCandidate[]> => {
		const key = fingerprint();
		const queries = duplicateQueries(draft());
		const mine = ++dupRequest;
		const lists = await Promise.all(queries.map((q) => get<DuplicateCandidate[]>("contacts/duplicates", q)));
		const found = mergeCandidates(lists);
		if (mine === dupRequest && !existing) {
			checkedFor = key;
			showDuplicates(found);
			validate();
		}
		return found;
	};
	let lastDraft = "";
	function scheduleDuplicates(): void {
		clearTimeout(dupTimer);
		if (existing) return;
		dupTimer = setTimeout(() => {
			const key = draftKey(draft());
			if (key === lastDraft) return;
			lastDraft = key;
			void runDuplicates().catch(() => undefined);
		}, 500);
	}

	// Neuen Kunden mit allen Angaben (mehrere Nummern, Ansprechpartner, Notiz) im Kundendialog erfassen.
	const detailed = new Button("newCustomerDetailed", {
		text: "Neuen Kunden ausführlich erfassen …", icon: "sap-icon://add-employee", type: "Transparent",
		press: async () => {
			const f = customerFields();
			const created = await openNewCustomerDialog({ name: f.name || undefined, street: f.street ?? undefined, postalCode: f.postalCode ?? undefined,
				city: f.city ?? undefined, phone: f.phone ?? undefined, email: f.email ?? undefined, useExistingText: "Diesen Kunden verwenden" });
			if (created) useExisting(created.contact);
		},
	});

	const customerStep = new WizardStep("newStepCustomer", {
		title: "Kunde", icon: "sap-icon://customer", validated: false,
		content: [
			chosen,
			grid([
				form([
					formTitle("Kunde"),
					new Label({ text: "Name des Kunden", labelFor: c.name, required: true }), c.name,
					new Label({ text: "" }), detailed,
					new Label({ text: "Art", labelFor: customerKind }), customerKind,
					new Label({ text: "Kundennummer", labelFor: c.number }), c.number,
				]),
				form([
					formTitle("Anschrift und Erreichbarkeit"),
					new Label({ text: "Straße (Kunde)", labelFor: c.street }), c.street,
					new Label({ text: "PLZ (Kunde)", labelFor: c.postalCode }), c.postalCode,
					new Label({ text: "Ort (Kunde)", labelFor: c.city }), c.city,
					new Label({ text: "Telefon (Kunde)", labelFor: c.phone }), c.phone,
					new Label({ text: "E-Mail (Kunde)", labelFor: c.email }), c.email,
				]),
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
		description: new TextArea("newDescription", { rows: 3, width: "100%", maxLength: 4000, growing: true, growingMaxLines: 8,
			placeholder: "Umfang, Besonderheiten, Wünsche des Kunden" }),
	};
	// Baustellenadresse: wie Kundenanschrift (übernommen, nicht bearbeitbar) oder abweichend.
	const siteMode = new SegmentedButton("newSiteMode", {
		selectedKey: "other",
		items: [new SegmentedButtonItem({ key: "customer", text: "Wie Kundenanschrift" }), new SegmentedButtonItem({ key: "other", text: "Abweichende Adresse" })],
	});
	const customerAddress = () => siteFromCustomer({ street: c.street.getValue(), postalCode: c.postalCode.getValue(), city: c.city.getValue() });
	const siteAddress = () => ({ street: site.street.getValue(), postalCode: site.postalCode.getValue(), city: site.city.getValue() });
	const applySiteMode = () => {
		const same = siteMode.getSelectedKey() === "customer";
		if (same) {
			const a = customerAddress();
			site.street.setValue(a.street ?? "");
			site.postalCode.setValue(a.postalCode ?? "");
			site.city.setValue(a.city ?? "");
		}
		for (const f of [site.street, site.postalCode, site.city]) f.setEditable(!same);
		scheduleAddressCheck();
		refreshSummary();
	};
	siteMode.attachSelectionChange(applySiteMode);
	/** Beim Wechsel in diesen Schritt: Vorgabe „wie Kundenanschrift“, wenn der Kunde eine Anschrift hat und noch nichts eingetragen ist. */
	const prepareSite = () => {
		const customerHas = hasAddress(customerAddress());
		siteMode.getItems()[0].setEnabled(customerHas);
		const empty = !site.street.getValue().trim() && !site.city.getValue().trim() && !site.postalCode.getValue().trim();
		if (!customerHas) siteMode.setSelectedKey("other");
		else if (empty || siteMode.getSelectedKey() === "customer") siteMode.setSelectedKey("customer");
		applySiteMode();
	};

	// Hinweis auf vorhandene Bauakten an derselben Adresse.
	const addressHint = new MessageStrip("newAddressHint", { type: "Information", showIcon: true, visible: false });
	const addressLinks = new VBox({ renderType: "Bare", visible: false }).addStyleClass("sapUiTinyMarginTop sapUiSmallMarginBottom");
	let addressTimer: ReturnType<typeof setTimeout> | undefined;
	let addressKey = "";
	const checkAddress = async () => {
		const a = siteAddress();
		const key = JSON.stringify(a);
		if (key === addressKey) return;
		addressKey = key;
		const postal = a.postalCode.trim();
		if (!a.street.trim() || !postal || !session.has("bauakten.read")) {
			addressHint.setVisible(false);
			addressLinks.setVisible(false);
			refreshSummary();
			return;
		}
		try {
			const page = await get<BauaktePage>("bauakten", { postalCode: postal, status: "all", limit: 200 });
			if (key !== addressKey) return;
			const found = atSameAddress(page.items, a);
			addressLinks.destroyItems();
			for (const b of found.slice(0, 5)) {
				addressLinks.addItem(new Link({ text: `${b.number ? `${b.number} · ` : ""}${b.title} (${bauakteStatus[b.status]?.text ?? b.status})`,
					href: `/bauakten/${b.id}`, target: "_blank", tooltip: "Bauakte in neuem Fenster öffnen" }));
			}
			addressHint.setText(found.length === 1
				? `An dieser Adresse gibt es bereits eine Bauakte (${addressLine(a)}).`
				: `An dieser Adresse gibt es bereits ${found.length} Bauakten (${addressLine(a)}).`);
			addressHint.setVisible(found.length > 0);
			addressLinks.setVisible(found.length > 0);
		} catch {
			addressHint.setVisible(false);
			addressLinks.setVisible(false);
		}
		refreshSummary();
	};
	function scheduleAddressCheck(): void {
		clearTimeout(addressTimer);
		addressTimer = setTimeout(() => void checkAddress(), 400);
	}
	for (const f of [site.street, site.postalCode, site.city]) f.attachLiveChange(() => { scheduleAddressCheck(); refreshSummary(); });

	title.attachLiveChange(() => validate());
	kind.attachSelectionChange(() => validate());
	site.description.attachLiveChange(() => refreshSummary());
	// Bauakten-Vorlagen aus den Stammdaten: Beschreibung und Standardräume.
	let templates: BauakteTemplate[] = [];
	const template = new Select("newTemplate", { width: "100%", forceSelection: false, selectedKey: "", items: [new Item({ key: "", text: "– ohne Vorlage –" })] });
	void Promise.all([catalog<BauakteTemplate>("bauakteTemplate"), loadSystemSettings()]).then(([list, settings]) => {
		templates = list;
		for (const t of list) {
			template.addItem(new Item({ key: t.id, text: `${t.name}${t.content.rooms?.length ? ` (${t.content.rooms.length} Räume)` : ""}` }));
		}
		// Vorgeschlagene Vorlage aus den Einstellungen.
		const preferred = settings?.settings.bauakte.defaultTemplateId;
		if (preferred && list.some((t) => t.id === preferred) && !template.getSelectedKey()) template.setSelectedKey(preferred);
		refreshSummary();
	}).catch(() => undefined);
	template.attachChange(() => {
		const t = templates.find((x) => x.id === template.getSelectedKey());
		if (t?.content.description && !site.description.getValue().trim()) site.description.setValue(t.content.description);
		refreshSummary();
	});
	// Zuständige(r): Bauleitung; Benachrichtigung über die Zuweisung wie beim Bearbeiten der Bauakte.
	const responsible = new Select("newResponsible", { width: "100%", forceSelection: false, selectedKey: "", items: [new Item({ key: "", text: "– nicht festgelegt –" })] });
	void session.people().then((people) => {
		for (const p of people.filter((x) => x.isActive)) responsible.addItem(new Item({ key: p.id, text: p.displayName }));
	}).catch(() => undefined);
	responsible.attachChange(() => refreshSummary());

	// Kennwerte aus dem Formular-Schema der Stammdaten (B7): Eckdaten und Ausführung hier, weitere Kennwerte in Schritt 4.
	let schema: FormSchema | null = null;
	const parts: ReturnType<typeof schemaWizardControls>[] = [];
	const eckdatenForm = form([]);
	const scheduleForm = form([]);
	const figuresGrid = grid([eckdatenForm, scheduleForm]);
	const moreGrid = grid([]);
	const moreIntro = new Text({ text: "Weitere Kennwerte aus den Stammdaten. Alles lässt sich später in der Bauakte ergänzen.", visible: false })
		.addStyleClass("eoMuted eoNewAkteIntro");
	void bauakteSchema().then((loaded) => {
		if (!loaded) return;
		schema = loaded;
		const { primary, schedule, more } = splitSchema(loaded);
		const add = (part: FormSchema, target: SimpleForm) => {
			const controls = schemaWizardControls(part, "new", () => validate());
			parts.push(controls);
			for (const x of controls.content) target.addContent(x);
		};
		if (primary.groups.length) add(primary, eckdatenForm);
		if (schedule.groups.length) add(schedule, scheduleForm);
		for (const g of more.groups) {
			const target = form([]);
			add({ ...more, groups: [g] }, target);
			moreGrid.addItem(target);
		}
		moreIntro.setVisible(more.groups.length > 0);
		validate();
	}).catch(() => undefined);
	const readKennwerte = (): Record<string, unknown> => Object.assign({}, ...parts.map((p) => p.read())) as Record<string, unknown>;

	const projectStep = new WizardStep("newStepProject", {
		title: "Bauvorhaben", icon: "sap-icon://building", validated: false,
		content: [
			grid([
				form([
					formTitle("Bauvorhaben"),
					new Label({ text: "Bauvorhaben", labelFor: title, required: true }), title,
					new Label({ text: "Anlegen als", labelFor: kind }), kind,
					new Label({ text: "Vorlage", labelFor: template }), template,
					new Label({ text: "Zuständig", labelFor: responsible }), responsible,
					new Label({ text: "Beschreibung", labelFor: site.description }), site.description,
				]),
				form([
					formTitle("Baustelle"),
					new Label({ text: "Adresse", labelFor: siteMode }), siteMode,
					new Label({ text: "Straße", labelFor: site.street }), site.street,
					new Label({ text: "PLZ", labelFor: site.postalCode }), site.postalCode,
					new Label({ text: "Ort", labelFor: site.city }), site.city,
					new Label({ text: "" }), new VBox({ renderType: "Bare", items: [addressHint, addressLinks] }),
				]),
			]),
			figuresGrid,
		],
	});

	// ---- Schritt 3: Beteiligte ---------------------------------------------------------------------------------------
	const input = (id: string, maxLength: number, type: "Text" | "Tel" | "Email" = "Text", placeholder = "") => {
		const field = new Input(id, { width: "100%", maxLength, type, placeholder });
		field.attachLiveChange(() => validate());
		return field;
	};
	const onSite = {
		name: input("newSiteContactName", 100, "Text", "z. B. Herr Bauer (Polier)"),
		phone: input("newSiteContactPhone", 50, "Tel"),
		email: input("newSiteContactEmail", 320, "Email"),
		notes: new TextArea("newSiteNotes", { rows: 3, width: "100%", maxLength: 2000, growing: true, growingMaxLines: 6,
			placeholder: "Zufahrt, Schlüssel, Anlieferung, Behinderungen" }),
	};
	onSite.notes.attachLiveChange(() => refreshSummary());
	const takeCustomer = new Button("newSiteFromCustomer", {
		text: "Kunde ist Ansprechpartner vor Ort", icon: "sap-icon://customer", type: "Transparent",
		press: () => {
			const f = customerFields();
			onSite.name.setValue(f.name ?? "");
			onSite.phone.setValue(f.phone ?? "");
			onSite.email.setValue(f.email ?? "");
			validate();
		},
	});
	const partyInputs = (prefix: string) => ({
		name: input(`${prefix}Name`, 200, "Text", "Name"),
		company: input(`${prefix}Company`, 200, "Text", "Firma"),
		phone: input(`${prefix}Phone`, 50, "Tel"),
		email: input(`${prefix}Email`, 320, "Email"),
	});
	const manager = partyInputs("newManager");
	const architect = partyInputs("newArchitect");
	const partyRole = new SegmentedButton("newPlannerRole", {
		selectedKey: "architect",
		items: [new SegmentedButtonItem({ key: "architect", text: "Architekt" }), new SegmentedButtonItem({ key: "planner", text: "Planer" })],
	});
	partyRole.attachSelectionChange(() => refreshSummary());
	const partyDraft = (p: typeof manager): PartyDraft => ({ name: p.name.getValue(), company: p.company.getValue(), phone: p.phone.getValue(), email: p.email.getValue() });
	const partyForm = (heading: string, p: typeof manager, extra: Control[] = []) => form([
		formTitle(heading), ...extra,
		new Label({ text: "Name", labelFor: p.name }), p.name,
		new Label({ text: "Firma", labelFor: p.company }), p.company,
		new Label({ text: "Telefon", labelFor: p.phone }), p.phone,
		new Label({ text: "E-Mail", labelFor: p.email }), p.email,
	]);
	const partiesStep = new WizardStep("newStepParties", {
		title: "Beteiligte", icon: "sap-icon://group", validated: true,
		content: [
			new Text({ text: "Optional. Weitere Beteiligte wie Lieferanten oder Gewerke erfassen Sie später in der Bauakte unter „Kontakte“." })
				.addStyleClass("eoMuted eoNewAkteIntro"),
			grid([
				form([
					formTitle("Ansprechpartner vor Ort"),
					new Label({ text: "" }), takeCustomer,
					new Label({ text: "Name", labelFor: onSite.name }), onSite.name,
					new Label({ text: "Telefon", labelFor: onSite.phone }), onSite.phone,
					new Label({ text: "E-Mail", labelFor: onSite.email }), onSite.email,
					new Label({ text: "Hinweise zur Baustelle", labelFor: onSite.notes }), onSite.notes,
				]),
				partyForm("Bauleitung", manager),
				partyForm("Architekt oder Planer", architect, [new Label({ text: "Rolle", labelFor: partyRole }), partyRole]),
			], true),
		],
	});

	// ---- Schritt 4: Weitere Angaben (Kennwerte, Erstbegehung, Notiz) -------------------------------------------------
	const canPlan = session.has("planning.read");
	const canWaive = session.has("bauakten.edit");
	const canNote = session.has("notes.write");
	// Einstellungen › Module: ohne „Erstbegehung und Protokolle“ entfällt der Abschnitt (Auswahl bleibt „Später planen“).
	const inspectionsOn = session.moduleOn("inspections");
	const inspection = new RadioButtonGroup("newInspection", {
		columns: 1, selectedIndex: 0,
		buttons: inspectionChoices.map((o) => new RadioButton({ text: o.text, wrapping: true,
			enabled: (o.key !== "appointment" || canPlan) && (o.key !== "waived" || canWaive) })),
	});
	const inspectionChoice = (): InspectionChoice => inspectionChoices[inspection.getSelectedIndex()]?.key ?? "later";
	const nextWorkday = (() => {
		const d = new Date();
		d.setDate(d.getDate() + 1);
		while (d.getDay() === 0 || d.getDay() === 6) d.setDate(d.getDate() + 1);
		return localIso(d).slice(0, 10);
	})();
	const inspDay = new DatePicker("newInspectionDay", { width: "100%", valueFormat: "yyyy-MM-dd", displayFormat: "EEE, dd.MM.yyyy", value: nextWorkday });
	const inspTime = new TimePicker("newInspectionTime", { width: "100%", valueFormat: "HH:mm", displayFormat: "HH:mm", value: "09:00", minutesStep: 15 });
	const inspDuration = new Select("newInspectionDuration", { width: "100%", selectedKey: "60", items: [
		new Item({ key: "30", text: "30 Minuten" }), new Item({ key: "60", text: "1 Stunde" }), new Item({ key: "90", text: "1,5 Stunden" }), new Item({ key: "120", text: "2 Stunden" }),
	] });
	const waiveReason = new Input("newWaiveReason", { width: "100%", maxLength: 500, placeholder: "z. B. Folgeauftrag, Baustelle bekannt" });
	const note = new TextArea("newNote", { rows: 4, width: "100%", maxLength: 4000, growing: true, growingMaxLines: 10,
		placeholder: "Interne Notiz zur Bauakte, z. B. Herkunft der Anfrage, Absprachen am Telefon", enabled: canNote });
	const appointmentFields = [inspDay, inspTime, inspDuration];
	const inspectionLabels = { day: new Label({ text: "Datum", labelFor: inspDay }), time: new Label({ text: "Uhrzeit", labelFor: inspTime }),
		duration: new Label({ text: "Dauer", labelFor: inspDuration }), reason: new Label({ text: "Begründung", labelFor: waiveReason }) };
	const applyInspection = () => {
		const choice = inspectionChoice();
		for (const f of [...appointmentFields, inspectionLabels.day, inspectionLabels.time, inspectionLabels.duration]) f.setVisible(choice === "appointment");
		waiveReason.setVisible(choice === "waived");
		inspectionLabels.reason.setVisible(choice === "waived");
		refreshSummary();
	};
	inspection.attachSelect(applyInspection);
	for (const f of [inspDay, inspTime]) f.attachChange(() => refreshSummary());
	inspDuration.attachChange(() => refreshSummary());
	waiveReason.attachLiveChange(() => refreshSummary());
	note.attachLiveChange(() => refreshSummary());
	applyInspection();

	const moreStep = new WizardStep("newStepMore", {
		title: "Weitere Angaben", icon: "sap-icon://add-activity", validated: true,
		content: [
			grid([
				form([
					formTitle("Erstbegehung"),
					new Label({ text: "Vorgehen", labelFor: inspection }), inspection,
					inspectionLabels.day, inspDay, inspectionLabels.time, inspTime, inspectionLabels.duration, inspDuration,
					inspectionLabels.reason, waiveReason,
				]).setVisible(inspectionsOn),
				form([
					formTitle("Interne Notiz"),
					new Label({ text: "Notiz", labelFor: note }), note,
				]),
			]),
			moreIntro, moreGrid,
		],
	});

	// ---- Schritt 5: Prüfen -------------------------------------------------------------------------------------------
	const review = new VBox({ renderType: "Bare" }).addStyleClass("eoNewAkteSummary eoNewAkteReview");
	const reviewStep = new WizardStep("newStepReview", { title: "Prüfen", icon: "sap-icon://accept", validated: true,
		content: [new Text({ text: "Bitte die Angaben prüfen. Nach dem Anlegen öffnet sich die Bauakte." }).addStyleClass("eoMuted eoNewAkteIntro"), review] });

	const inspectionText = (): string => {
		switch (inspectionChoice()) {
			case "appointment": {
				const span = appointmentSpan(inspDay.getValue(), inspTime.getValue(), Number(inspDuration.getSelectedKey() || 60));
				return span ? `Termin am ${date(inspDay.getValue())}, ${inspTime.getValue()} Uhr (${inspDuration.getSelectedItem()?.getText() ?? ""})`
					: "Termin (Datum und Uhrzeit fehlen)";
			}
			case "now": return "Direkt im Anschluss durchführen";
			case "waived": return `Nicht erforderlich${waiveReason.getValue().trim() ? ` (${waiveReason.getValue().trim()})` : ""}`;
			default: return "Später planen";
		}
	};
	const sections = (): SummarySection[] => {
		const f = customerFields();
		const out: SummarySection[] = [];
		out.push({ title: "Kunde", rows: [
			["Kunde", f.name ? `${f.name}${existing ? " (vorhanden)" : " (wird neu angelegt)"}` : ""],
			...(f.street || f.postalCode || f.city ? [["Anschrift", [f.street, [f.postalCode, f.city].filter(Boolean).join(" ")].filter(Boolean).join(", ")] as [string, string]] : []),
			...(f.phone || f.email ? [["Kontakt", [f.phone, f.email].filter(Boolean).join(" · ")] as [string, string]] : []),
		] });
		const rows: [string, string][] = [
			["Bauvorhaben", title.getValue().trim()],
			["Anlegen als", kind.getSelectedKey() === "inquiry" ? "Anfrage" : "Bauakte (Auftrag)"],
		];
		const chosenTemplate = templates.find((x) => x.id === template.getSelectedKey());
		if (chosenTemplate) rows.push(["Vorlage", `${chosenTemplate.name}${chosenTemplate.content.rooms?.length ? ` (${chosenTemplate.content.rooms.length} Räume)` : ""}`]);
		const who = responsible.getSelectedItem();
		if (responsible.getSelectedKey() && who) rows.push(["Zuständig", who.getText()]);
		const a = siteAddress();
		if (hasAddress(a)) rows.push(["Baustelle", `${addressLine(a)}${siteMode.getSelectedKey() === "customer" ? " (wie Kundenanschrift)" : ""}`]);
		if (addressHint.getVisible()) rows.push(["Hinweis", addressHint.getText()]);
		const description = site.description.getValue().trim();
		if (description) rows.push(["Beschreibung", description.length > 160 ? `${description.slice(0, 160)} …` : description]);
		out.push({ title: "Bauvorhaben", rows });
		if (schema) {
			const values = readKennwerte();
			out.push({ title: "Kennwerte", rows: fieldsOf(schema).filter((fl) => values[fl.key] !== null && values[fl.key] !== undefined && values[fl.key] !== "")
				.map((fl) => [fl.label, displayValue(schema!, fl, values[fl.key])] as [string, string]) });
		}
		const person = (d: PartyDraft) => [d.name.trim(), d.company.trim() && d.company.trim() !== d.name.trim() ? d.company.trim() : "", d.phone.trim(), d.email.trim()]
			.filter(Boolean).join(" · ");
		const people: [string, string][] = [];
		const vorOrt = person({ name: onSite.name.getValue(), company: "", phone: onSite.phone.getValue(), email: onSite.email.getValue() });
		if (vorOrt) people.push(["Vor Ort", vorOrt]);
		if (onSite.notes.getValue().trim()) people.push(["Hinweise", onSite.notes.getValue().trim()]);
		if (person(partyDraft(manager))) people.push(["Bauleitung", person(partyDraft(manager))]);
		if (person(partyDraft(architect))) people.push([partyRole.getSelectedKey() === "planner" ? "Planer" : "Architekt", person(partyDraft(architect))]);
		out.push({ title: "Beteiligte", rows: people });
		const extra: [string, string][] = inspectionsOn ? [["Erstbegehung", inspectionText()]] : [];
		const n = note.getValue().trim();
		if (n) extra.push(["Notiz", n.length > 160 ? `${n.slice(0, 160)} …` : n]);
		out.push({ title: "Abschluss", rows: extra });
		return out;
	};

	// Zusammenfassung rechts (breite Fenster), laufend aktualisiert.
	const aside = new VBox({ renderType: "Bare", items: [new Title({ text: "Zusammenfassung", level: "H4" }).addStyleClass("eoNewAkteAsideTitle")] })
		.addStyleClass("eoNewAkteAside");
	const asideBody = new VBox({ renderType: "Bare" }).addStyleClass("eoNewAkteSummary");
	aside.addItem(asideBody);
	let summaryTimer: ReturnType<typeof setTimeout> | undefined;
	refreshSummary = () => {
		clearTimeout(summaryTimer);
		summaryTimer = setTimeout(() => {
			if (asideBody.isDestroyed?.()) return;
			const s = sections();
			renderSummary(asideBody, s);
			if (stepIndex === steps.length - 1) renderSummary(review, s);
		}, 120);
	};

	const steps = [customerStep, projectStep, partiesStep, moreStep, reviewStep];
	const wizard = new Wizard("newBauakteWizard", {
		steps, showNextButton: false, renderMode: "Page", height: "100%", enableBranching: false,
	});

	const strip = new MessageStrip({ type: "Error", showIcon: true, visible: false }).addStyleClass("eoNewAkteError");
	let stepIndex = 0;

	let updateButtons: () => void = () => undefined;

	function validate(): void {
		const f = customerFields();
		const emailOk = !f.email || emailPattern.test(f.email);
		c.email.setValueState(emailOk ? ValueState.None : ValueState.Error);
		c.email.setValueStateText("Bitte eine gültige E-Mail-Adresse angeben.");
		const pendingDuplicates = !existing && candidates.length > 0 && checkedFor === fingerprint() && !forceNew.getSelected();
		customerStep.setValidated(!!f.name && emailOk && !pendingDuplicates);
		const missing = parts.flatMap((p) => p.missing());
		projectStep.setValidated(!!title.getValue().trim() && missing.length === 0);
		// Beteiligte: E-Mail-Adressen prüfen.
		let partiesOk = true;
		for (const field of [onSite.email, manager.email, architect.email]) {
			const ok = !field.getValue().trim() || emailPattern.test(field.getValue().trim());
			field.setValueState(ok ? ValueState.None : ValueState.Error);
			field.setValueStateText("Bitte eine gültige E-Mail-Adresse angeben.");
			partiesOk &&= ok;
		}
		partiesStep.setValidated(partiesOk);
		updateButtons();
		refreshSummary();
	}

	return new Promise((resolve) => {
		let result: BauakteDetail | null = null;
		const back = new Button("newBauakteBack", { text: "Zurück", press: () => go(stepIndex - 1) });
		const next = new Button("newBauakteNext", { text: "Weiter", type: "Emphasized", press: () => void forward() });
		const create = new Button("newBauakteCreate", { text: "Anlegen", press: () => void submit() });
		const cancel = new Button({ text: "Abbrechen", press: () => dialog.close() });

		updateButtons = () => {
			const last = stepIndex === steps.length - 1;
			back.setVisible(stepIndex > 0);
			next.setVisible(!last);
			// Im Kundenschritt prüft „Weiter“ selbst (Pflichtname, Dubletten) und erklärt, was fehlt.
			next.setEnabled(stepIndex === 0 || (steps[stepIndex]?.getValidated() ?? false));
			// Anlegen ab „Bauvorhaben“: Beteiligte und weitere Angaben sind optional.
			create.setVisible(stepIndex >= 1);
			create.setEnabled(customerStep.getValidated() && projectStep.getValidated() && partiesStep.getValidated());
			create.setType(last ? "Emphasized" : "Default");
			layout.toggleStyleClass("eoNewAkteLast", last);
		};

		const entered = (index: number) => {
			stepIndex = index;
			if (index === 1) prepareSite();
			if (index === 1 && !title.getValue()) setTimeout(() => title.focus(), 50);
			if (index === steps.length - 1) renderSummary(review, sections());
			updateButtons();
		};

		const go = (index: number) => {
			if (index < 0 || index >= steps.length) return;
			strip.setVisible(false);
			if (index > stepIndex) {
				let i = stepIndex;
				while (i < index) {
					wizard.nextStep();
					i++;
				}
			} else {
				wizard.goToStep(steps[index], true);
			}
			entered(index);
		};
		// Sprung über die Schrittleiste oben.
		wizard.attachNavigationChange((event) => {
			const step = event.getParameter("step") as WizardStep;
			const index = steps.indexOf(step);
			if (index >= 0 && index !== stepIndex) entered(index);
		});

		/** Weiter: im Kundenschritt erst die Dublettenprüfung des Servers. */
		const forward = async () => {
			if (stepIndex === 0 && !(await customerReady())) return;
			go(stepIndex + 1);
		};

		/** Kunde vollständig und ohne offene Dubletten? Sonst Hinweis im Kundenschritt. */
		const customerReady = async (): Promise<boolean> => {
			const f = customerFields();
			if (!f.name) {
				if (stepIndex !== 0) go(0);
				c.name.setValueState(ValueState.Error);
				c.name.setValueStateText("Bitte den Kunden angeben. Eine Bauakte erfordert einen Kunden.");
				strip.setText("Bitte den Kunden angeben. Eine Bauakte erfordert einen Kunden.");
				strip.setVisible(true);
				return false;
			}
			c.name.setValueState(ValueState.None);
			if (!existing && checkedFor !== fingerprint()) {
				dialog.setBusy(true);
				try {
					clearTimeout(dupTimer);
					const found = await runDuplicates();
					if (found.length > 0) {
						if (stepIndex !== 0) go(0);
						return false;
					}
				} catch (error) {
					strip.setText(problemMessage(error));
					strip.setVisible(true);
					return false;
				} finally {
					dialog.setBusy(false);
				}
			}
			if (!customerStep.getValidated()) {
				if (stepIndex !== 0) go(0);
				return false;
			}
			return true;
		};

		const submit = async () => {
			strip.setVisible(false);
			if (!(await customerReady())) return;
			if (!projectStep.getValidated()) {
				go(1);
				strip.setText("Bitte die Bezeichnung des Bauvorhabens und alle Pflichtfelder angeben.");
				strip.setVisible(true);
				return;
			}
			const choice = inspectionChoice();
			const span = choice === "appointment" ? appointmentSpan(inspDay.getValue(), inspTime.getValue(), Number(inspDuration.getSelectedKey() || 60)) : null;
			if (choice === "appointment" && !span) {
				go(3);
				strip.setText("Bitte Datum und Uhrzeit der Erstbegehung angeben oder „Später planen“ wählen.");
				strip.setVisible(true);
				return;
			}
			dialog.setBusy(true);
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
					...(schema ? writeValues(schema, readKennwerte(), null) : {}),
				});
				const created = result;
				// Ergänzungen: Ein Fehler hier verhindert die Anlage nicht; was fehlt, steht danach im Hinweis.
				const failed: string[] = [];
				const attempt = async (label: string, work: () => Promise<unknown>) => {
					try {
						await work();
					} catch (error) {
						failed.push(`${label}: ${problemMessage(error)}`);
					}
				};
				const chosenTemplate = templates.find((x) => x.id === template.getSelectedKey());
				await attempt("Räume aus der Vorlage", async () => {
					for (const [index, room] of (chosenTemplate?.content.rooms ?? []).entries()) {
						const roomId = uuidv7();
						await put(`bauakten/${created.id}/rooms/${roomId}`, {
							id: roomId, sectionId: null, name: room.name, floor: room.floor ?? null, areaSquareMeters: room.areaSquareMeters ?? null,
							screedTypeId: null, thicknessMillimeters: room.thicknessMillimeters ?? null, insulationMillimeters: null,
							underfloorHeating: !!room.underfloorHeating, status: "planned", notes: null, sortOrder: index,
						});
					}
				});
				// Zuständigkeit (mit Benachrichtigung) und Ansprechpartner vor Ort wie beim Bearbeiten der Bauakte setzen.
				const changes: Record<string, string | null> = {};
				if (responsible.getSelectedKey()) changes.responsibleUserId = responsible.getSelectedKey();
				const sc = { siteContactName: onSite.name.getValue().trim() || null, siteContactPhone: onSite.phone.getValue().trim() || null,
					siteContactEmail: onSite.email.getValue().trim() || null, siteNotes: onSite.notes.getValue().trim() || null };
				for (const [key, value] of Object.entries(sc)) if (value) changes[key] = value;
				if (Object.keys(changes).length) {
					await attempt("Zuständig und Ansprechpartner vor Ort", async () => { result = await saveBauakte(created, changes); });
				}
				const managerBody = partyBody("siteManager", partyDraft(manager));
				if (managerBody) await attempt("Bauleitung", () => put(`bauakten/${created.id}/parties/${uuidv7()}`, managerBody));
				const plannerBody = partyBody(partyRole.getSelectedKey() || "architect", partyDraft(architect));
				if (plannerBody) await attempt("Architekt oder Planer", () => put(`bauakten/${created.id}/parties/${uuidv7()}`, plannerBody));
				const noteText = note.getValue().trim();
				if (noteText && canNote) await attempt("Notiz", () => post(`bauakten/${created.id}/notes`, { id: uuidv7(), text: noteText }));
				if (choice === "waived") {
					await attempt("Erstbegehung nicht erforderlich", async () => {
						result = await put<BauakteDetail>(`bauakten/${created.id}/initial-inspection-waiver`, { waived: true, reason: waiveReason.getValue().trim() || null });
					});
				} else if (choice === "appointment" && span) {
					await attempt("Termin der Erstbegehung", async () => {
						const writable = (await calendars()).filter((x) => x.canEdit && x.isActive);
						const calendar = writable.find((x) => x.kind === "shared") ?? writable[0];
						if (!calendar) throw new Error("Kein Kalender, in den Sie Termine eintragen dürfen.");
						const who = responsible.getSelectedKey() || session.userId;
						await put(`calendar/events/${uuidv7()}`, {
							calendarId: calendar.id, title: `Erstbegehung: ${created.title}`, location: addressLine(siteAddress()) || null,
							description: null, kind: "siteVisit", color: null, allDay: false, start: span.start, end: span.end,
							participantUserIds: who ? [who] : [], crewIds: [], bauakteId: created.id,
						});
						calendarChanged();
					});
				}
				if (choice === "now") (result as BauakteDetail & { startInspection?: boolean }).startInspection = true;
				MessageToast.show(status === "inquiry" ? "Anfrage angelegt" : "Bauakte angelegt");
				dialog.close();
				if (failed.length) {
					MessageBox.warning(`Die ${status === "inquiry" ? "Anfrage" : "Bauakte"} ist angelegt. Folgende Angaben konnten nicht gespeichert werden und lassen sich in der Bauakte nachtragen:\n\n${failed.join("\n")}`);
				}
			} catch (error) {
				strip.setText(problemMessage(error));
				strip.setVisible(true);
			} finally {
				dialog.setBusy(false);
			}
		};

		const layout = new HBox({ renderType: "Bare", items: [wizard, aside] }).addStyleClass("eoNewAkte");
		const dialog = new Dialog("newBauakteDialog", {
			title: titleOf(options.status),
			contentWidth: "90vw",
			contentHeight: "86vh",
			stretch: Device.system.phone,
			resizable: true,
			draggable: true,
			verticalScrolling: false,
			horizontalScrolling: false,
			initialFocus: c.name,
			content: [new VBox({ height: "100%", renderType: "Bare", items: [strip, layout] }).addStyleClass("eoNewAkteFrame")],
			buttons: [back, next, create, cancel],
			afterClose: () => {
				clearTimeout(summaryTimer);
				dialog.destroy();
				resolve(result);
			},
		});
		kind.attachSelectionChange(() => dialog.setTitle(titleOf(kind.getSelectedKey())));
		dialog.addStyleClass("eoWizardDialog eoNewAkteDialog");
		dialog.attachAfterOpen(() => updateButtons());
		validate();
		if (options.customer) useExisting(options.customer);
		dialog.open();
	});
}
