/**
 * Kundenanlage im Web: strukturierter Dialog mit Art (privat/gewerblich), Name bzw. Firma, Anschrift, mehreren
 * Telefonnummern und E-Mail-Adressen, Ansprechpartner bei Gewerbekunden, automatischer Kundennummer und Notiz.
 * Während der Eingabe prüft der Server auf ähnliche Kunden (Name, Telefon, E-Mail, Anschrift); ein Treffer kann direkt
 * übernommen werden. Nach dem Anlegen kann direkt eine Bauakte für den Kunden folgen.
 */
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import Link from "sap/m/Link";
import ComboBox from "sap/m/ComboBox";
import Select from "sap/m/Select";
import CheckBox from "sap/m/CheckBox";
import SegmentedButton from "sap/m/SegmentedButton";
import SegmentedButtonItem from "sap/m/SegmentedButtonItem";
import MessageStrip from "sap/m/MessageStrip";
import List from "sap/m/List";
import CustomListItem from "sap/m/CustomListItem";
import HBox from "sap/m/HBox";
import VBox from "sap/m/VBox";
import ObjectStatus from "sap/m/ObjectStatus";
import FlexItemData from "sap/m/FlexItemData";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import Item from "sap/ui/core/Item";
import type Control from "sap/ui/core/Control";
import type InputBase from "sap/m/InputBase";
import { ValueState } from "sap/ui/core/library";
import { get, post, problemMessage, put } from "./api";
import { contactListField } from "./forms";
import { formTitle } from "./formLayout";
import { uuidv7 } from "./ids";
import { session } from "./session";
import { AUTO_NUMBER } from "./numberField";
import {
	candidateLine, candidateState, draftKey, duplicateQueries, mergeCandidates, validateCustomer, type CustomerDraft, type DuplicateCandidate,
} from "./customerMatch";
import type { ContactEmail, ContactPhone, ContactResponse } from "./models";
import { candidateUsable, useCandidateText } from "./customerRemoval";
import Switch from "sap/m/Switch";
import {
	lexwareCandidateAction, lexwareCandidateLine, lexwareCheckActive, lexwareCheckRequest, lexwareCheckText, SYNC_HELP, SYNC_LABEL, syncFieldVisible, syncInitial,
	type ImportLexwareContactResponse, type LexwareContactCandidate, type LexwareContactCheck,
} from "./lexware";
import { ensureActive } from "./customerRemovalDialog";

export interface NewCustomerOptions {
	/** Vorbelegung, z. B. der im Bauakten-Assistenten eingegebene Name. */
	name?: string;
	street?: string;
	postalCode?: string;
	city?: string;
	phone?: string;
	email?: string;
	/** Schaltfläche „Anlegen und Bauakte anlegen“ anbieten (nicht im Bauakten-Assistenten). */
	offerBauakte?: boolean;
	/** Beschriftung für das Übernehmen eines Treffers. */
	useExistingText?: string;
}

export interface NewCustomerResult {
	contact: ContactResponse;
	/** Vorhandener Kunde aus der Dublettenprüfung übernommen. */
	existing: boolean;
	/** „Anlegen und Bauakte anlegen“ gewählt. */
	createBauakte: boolean;
}

const salutations = ["Herr", "Frau", "Familie", "Eheleute", "Herr und Frau"];

function form(content: Control[]): SimpleForm {
	return new SimpleForm({
		editable: true, layout: "ResponsiveGridLayout", labelSpanXL: 4, labelSpanL: 4, labelSpanM: 4, labelSpanS: 12,
		columnsXL: 1, columnsL: 1, columnsM: 1, singleContainerFullSize: true, content,
	});
}

export function openNewCustomerDialog(options: NewCustomerOptions = {}): Promise<NewCustomerResult | null> {
	const kind = new SegmentedButton("ncKind", {
		selectedKey: "person",
		items: [new SegmentedButtonItem({ key: "person", text: "Privatkunde" }), new SegmentedButtonItem({ key: "company", text: "Gewerbekunde" })],
	});
	const salutation = new ComboBox("ncSalutation", { width: "100%", items: salutations.map((t) => new Item({ key: t, text: t })) });
	const title = new Input("ncTitle", { width: "100%", maxLength: 50, placeholder: "z. B. Dr." });
	const name = new Input("ncName", { width: "100%", maxLength: 200, value: options.name ?? "" });
	// Kundennummer ausschließlich aus dem Nummernkreis: schreibgeschützt, ohne weitere Hinweise.
	const number = new Input("ncNumber", { width: "100%", editable: false, value: AUTO_NUMBER });
	const vatId = new Input("ncVatId", { width: "100%", maxLength: 30, placeholder: "DE…" });
	const street = new Input("ncStreet", { width: "100%", maxLength: 200, value: options.street ?? "" });
	const postalCode = new Input("ncPostalCode", { width: "100%", maxLength: 10, value: options.postalCode ?? "" });
	const city = new Input("ncCity", { width: "100%", maxLength: 100, value: options.city ?? "" });
	const phones = contactListField("ncPhones", "phones", options.phone ? [{ kind: "landline", number: options.phone }] : [], () => scheduleCheck());
	const emails = contactListField("ncEmails", "emails", options.email ? [{ address: options.email }] : [], () => scheduleCheck());
	const channel = new Select("ncChannel", { width: "100%", items: [
		new Item({ key: "", text: "Keine Angabe" }), new Item({ key: "phone", text: "Telefon" }), new Item({ key: "mobile", text: "Mobil" }),
		new Item({ key: "email", text: "E-Mail" }), new Item({ key: "letter", text: "Brief" })] });
	const person = {
		name: new Input("ncPersonName", { width: "100%", maxLength: 200 }),
		fn: new Input("ncPersonFunction", { width: "100%", maxLength: 100, placeholder: "z. B. Bauleitung, Einkauf" }),
		phone: new Input("ncPersonPhone", { width: "100%", maxLength: 50, type: "Tel" }),
		email: new Input("ncPersonEmail", { width: "100%", maxLength: 320, type: "Email" }),
	};
	// „Mit Lexware Office abgleichen“ nur mit eingeschaltetem Modul, vorbelegt mit der Vorgabe aus den Einstellungen.
	const withSync = syncFieldVisible(session.data.modules ?? {});
	const lexwareSync = new Switch("ncLexwareSync", { state: syncInitial(null, session.data.lexware), customTextOn: " ", customTextOff: " " });
	const notes = new TextArea("ncNotes", { width: "100%", rows: 3, maxLength: 4000, growing: true,
		placeholder: "z. B. Zahlungsziel, Rechnung nur per E-Mail, Schlüssel bei Nachbarn" });

	const nameLabel = new Label({ text: "Name", labelFor: name, required: true });
	const companyOnly: Control[] = [];
	const personOnly: Control[] = [];
	const l = (text: string, c: Control, list?: Control[]) => {
		const label = new Label({ text, labelFor: c });
		list?.push(label, c);
		return [label, c];
	};

	const main = form([
		formTitle("Kunde"),
		new Label({ text: "Art", labelFor: kind }), kind,
		...l("Anrede", salutation, personOnly), ...l("Titel", title, personOnly),
		nameLabel, name,
		...l("USt-IdNr.", vatId, companyOnly),
		new Label({ text: "Kundennummer", labelFor: number }), number,
		formTitle("Anschrift"),
		new Label({ text: "Straße und Hausnummer", labelFor: street }), street,
		new Label({ text: "PLZ", labelFor: postalCode }), postalCode,
		new Label({ text: "Ort", labelFor: city }), city,
		formTitle("Erreichbarkeit"),
		new Label({ text: "Telefon", labelFor: phones.control }), phones.control,
		new Label({ text: "E-Mail", labelFor: emails.control }), emails.control,
		new Label({ text: "Bevorzugter Kontaktweg", labelFor: channel }), channel,
	]);
	const personForm = form([
		formTitle("Ansprechpartner"),
		new Label({ text: "Name", labelFor: person.name }), person.name,
		new Label({ text: "Funktion", labelFor: person.fn }), person.fn,
		new Label({ text: "Telefon", labelFor: person.phone }), person.phone,
		new Label({ text: "E-Mail", labelFor: person.email }), person.email,
	]);
	const notesForm = form([
		formTitle("Notiz"),
		new Label({ text: "Notiz und Zahlungshinweise", labelFor: notes }), notes,
		...(withSync ? [new Label({ text: SYNC_LABEL, labelFor: lexwareSync }), new VBox({ items: [lexwareSync, new Text({ text: SYNC_HELP }).addStyleClass("eoHelpText")] })] : []),
	]);

	const applyKind = () => {
		const company = kind.getSelectedKey() === "company";
		nameLabel.setText(company ? "Firma" : "Name");
		name.setPlaceholder(company ? "z. B. Wohnbau Süd GmbH" : "Vor- und Nachname, z. B. Anna Kraus");
		for (const c of companyOnly) c.setVisible(company);
		for (const c of personOnly) c.setVisible(!company);
		personForm.setVisible(company);
	};
	kind.attachSelectionChange(() => { applyKind(); scheduleCheck(); });

	// ---- Dublettenprüfung beim Tippen --------------------------------------------------------------------------------
	let candidates: DuplicateCandidate[] = [];
	let checkedKey = "";
	let pending: ReturnType<typeof setTimeout> | undefined;
	let request = 0;
	const dupStrip = new MessageStrip("ncDuplicateHint", { type: "Warning", showIcon: true, visible: false });
	const dupList = new List("ncDuplicates", { visible: false, showSeparators: "Inner" });
	const forceNew = new CheckBox("ncForceNew", { text: "Keiner davon: als neuen Kunden anlegen", visible: false });
	const checking = new Text({ text: "Ähnliche Kunden werden gesucht …", visible: false }).addStyleClass("eoMuted");
	// Prüfung gegen Lexware Office (Modul an, Schlüssel hinterlegt), unabhängig vom Schalter „Mit Lexware Office abgleichen“.
	const lexwareCheck = lexwareCheckActive(session.data.modules ?? {}, session.data.lexware);
	let lexwareHits: LexwareContactCandidate[] = [];
	let lexwareRequest = 0;
	const lxStrip = new MessageStrip("ncLexwareWarning", { type: "Warning", showIcon: true, visible: false });
	const lxList = new List("ncLexwareHits", { visible: false, showSeparators: "Inner" });
	const lxForce = new CheckBox("ncLexwareForce", { text: "Trotzdem neu anlegen (ohne Verknüpfung mit Lexware Office)", visible: false });
	const lxNote = new Text("ncLexwareNote", { visible: false }).addStyleClass("eoMuted");
	const dupBox = new VBox({ renderType: "Bare", items: [checking, dupStrip, dupList, forceNew, lxStrip, lxList, lxForce, lxNote] }).addStyleClass("eoDupBox");

	const phoneList = () => ((phones.read() as ContactPhone[]) ?? []);
	const emailList = () => ((emails.read() as ContactEmail[]) ?? []);
	const draft = (): CustomerDraft => ({
		name: name.getValue(), street: street.getValue(), postalCode: postalCode.getValue(), city: city.getValue(),
		phones: phoneList().map((p) => p.number), emails: emailList().map((e) => e.address),
	});

	let finish: (result: NewCustomerResult) => void = () => undefined;
	const showCandidates = (list: DuplicateCandidate[]) => {
		candidates = list;
		dupList.destroyItems();
		for (const d of list) {
			const c = d.contact;
			dupList.addItem(new CustomListItem({ content: [new HBox({
				alignItems: "Center", justifyContent: "SpaceBetween", width: "100%", wrap: "Wrap",
				items: [
					new VBox({ renderType: "Bare", items: [
						new Link({ text: c.name, href: `/kunden/${c.id}`, target: "_blank", tooltip: "Kundenakte in neuem Fenster öffnen" }),
						new Text({ text: candidateLine(c) }).addStyleClass("eoMuted"),
						new ObjectStatus({ text: d.reasons.join(", "), state: candidateState(d.score) }),
					] }).setLayoutData(new FlexItemData({ growFactor: 1, shrinkFactor: 1 })),
					new Button({ text: useCandidateText(c, options.useExistingText ?? "Vorhandenen Kunden verwenden"), type: "Emphasized",
						visible: candidateUsable(c),
						press: async () => {
							try {
								finish({ contact: await ensureActive(c), existing: true, createBauakte: false });
							} catch (error) {
								MessageBox.error(problemMessage(error));
							}
						} }).addStyleClass("sapUiTinyMarginBegin"),
				],
			}).addStyleClass("sapUiTinyMarginTopBottom sapUiSmallMarginBeginEnd")] }));
		}
		const any = list.length > 0;
		dupStrip.setText(list.length === 1
			? "Es gibt bereits einen ähnlichen Kunden. Bitte prüfen Sie, ob es sich um denselben Kunden handelt."
			: `Es gibt bereits ${list.length} ähnliche Kunden. Bitte prüfen Sie, ob einer davon der gesuchte Kunde ist.`);
		dupStrip.setVisible(any);
		dupList.setVisible(any);
		forceNew.setVisible(any);
		if (!any) forceNew.setSelected(false);
	};

	/** Kontakt aus Lexware Office als Kunde übernehmen: angelegt mit den Daten aus Lexware Office, verknüpft, Abgleich eingeschaltet. */
	const importFromLexware = async (candidate: LexwareContactCandidate): Promise<void> => {
		try {
			const result = await post<ImportLexwareContactResponse>(`lexware/contacts/${encodeURIComponent(candidate.contact.id)}/import`, { createAnyway: false });
			if (!result.imported || !result.contactId) {
				const first = result.candidates[0]?.customer;
				MessageBox.warning(first
					? `In EstrichONE gibt es bereits einen ähnlichen Kunden: ${first.name}${first.customerNumber ? ` (Kundennummer ${first.customerNumber})` : ""}. `
						+ "Verwenden Sie diesen Kunden und verknüpfen Sie ihn in der Kundenansicht mit Lexware Office."
					: "Der Kontakt konnte nicht übernommen werden.", { title: "Lexware Office" });
				return;
			}
			finish({ contact: await get<ContactResponse>(`contacts/${result.contactId}`), existing: false, createBauakte: false });
		} catch (error) {
			MessageBox.error(problemMessage(error), { title: "Lexware Office" });
		}
	};
	const showLexware = (result: LexwareContactCheck | null) => {
		lexwareHits = result?.checked ? result.candidates : [];
		lxList.destroyItems();
		for (const d of lexwareHits) {
			const c = d.contact;
			const action = lexwareCandidateAction(c, result?.canImport === true);
			lxList.addItem(new CustomListItem({ content: [new HBox({
				alignItems: "Center", justifyContent: "SpaceBetween", width: "100%", wrap: "Wrap",
				items: [
					new VBox({ renderType: "Bare", items: [
						new Link({ text: c.name, href: c.url, target: "_blank", tooltip: "Kontakt in Lexware Office öffnen" }),
						new Text({ text: lexwareCandidateLine(c) }).addStyleClass("eoMuted"),
						new ObjectStatus({ text: d.reasons.join(", "), state: d.strong ? "Error" : "Warning" }),
						new Link({ text: `Bereits verknüpft mit dem Kunden ${c.linkedContactName ?? ""}`.trim(), href: `/kunden/${c.linkedContactId}`, target: "_blank",
							visible: action === "linked" }),
						new Text({ text: "Zum Übernehmen fehlt Ihnen das Recht, Kunden mit Lexware Office zu verknüpfen.", visible: action === "none" }).addStyleClass("eoMuted"),
					] }).setLayoutData(new FlexItemData({ growFactor: 1, shrinkFactor: 1, baseSize: "50%" })),
					new Button({ text: "Aus Lexware Office übernehmen", type: "Emphasized", visible: action === "import",
						tooltip: "Kunde mit den Daten aus Lexware Office anlegen und verknüpfen", press: () => void importFromLexware(d) }).addStyleClass("sapUiTinyMarginBegin"),
				],
			}).addStyleClass("sapUiTinyMarginTopBottom sapUiSmallMarginBeginEnd")] }));
		}
		const text = lexwareCheckText(result);
		const any = lexwareHits.length > 0;
		lxStrip.setText(text.warning);
		lxStrip.setVisible(any);
		lxList.setVisible(any);
		lxForce.setVisible(any);
		if (!any) lxForce.setSelected(false);
		lxNote.setText(text.note);
		lxNote.setVisible(!!text.note);
	};
	let lexwareKey = "";
	const checkLexware = async (): Promise<void> => {
		if (!lexwareCheck) return;
		const body = lexwareCheckRequest({ ...draft(), kind: kind.getSelectedKey(), vatId: vatId.getValue() });
		const key = JSON.stringify(body);
		if (key === lexwareKey) return;
		const mine = ++lexwareRequest;
		if (!body) {
			lexwareKey = key;
			showLexware(null);
			return;
		}
		try {
			const result = await post<LexwareContactCheck>("lexware/contacts/check", body);
			if (mine !== lexwareRequest) return;
			lexwareKey = key;
			showLexware(result);
		} catch {
			// Die Prüfung blockiert nie: ohne Antwort bleibt das Anlegen möglich.
			if (mine === lexwareRequest) showLexware({ checked: false, message: "Die Prüfung in Lexware Office ist derzeit nicht möglich.", candidates: [], canImport: false });
		}
	};

	const check = async () => {
		void checkLexware();
		const key = draftKey(draft());
		if (key === checkedKey) return;
		const queries = duplicateQueries(draft());
		const mine = ++request;
		if (queries.length === 0) {
			checkedKey = key;
			showCandidates([]);
			return;
		}
		checking.setVisible(true);
		try {
			const lists = await Promise.all(queries.map((q) => get<DuplicateCandidate[]>("contacts/duplicates", q)));
			if (mine !== request) return;
			checkedKey = key;
			showCandidates(mergeCandidates(lists));
		} catch {
			// Die Prüfung ist ein Hinweis; ohne Verbindung wird beim Anlegen erneut geprüft.
		} finally {
			if (mine === request) checking.setVisible(false);
		}
	};
	function scheduleCheck(): void {
		clearTimeout(pending);
		pending = setTimeout(() => void check(), 450);
	}
	for (const input of [name, street, postalCode, city]) {
		input.attachLiveChange(() => {
			(input as InputBase).setValueState(ValueState.None);
			if (input === name && name.getValue().trim()) strip.setVisible(false);
			scheduleCheck();
		});
	}

	// ---- Anlegen ------------------------------------------------------------------------------------------------------
	const strip = new MessageStrip("ncError", { type: "Error", showIcon: true, visible: false }).addStyleClass("sapUiSmallMarginBottom");
	const fieldControls: Record<string, InputBase | null> = { name, postalCode, personEmail: person.email, phones: null, emails: null };

	const submit = async (createBauakte: boolean, dialog: Dialog): Promise<NewCustomerResult | null> => {
		const company = kind.getSelectedKey() === "company";
		const errors = validateCustomer({ kind: kind.getSelectedKey(), name: name.getValue(), postalCode: postalCode.getValue(),
			phones: phoneList().map((p) => p.number), emails: emailList().map((e) => e.address), personEmail: company ? person.email.getValue() : null });
		for (const [key, control] of Object.entries(fieldControls)) {
			control?.setValueState(errors[key] ? ValueState.Error : ValueState.None);
			control?.setValueStateText(errors[key] ?? "");
		}
		const first = Object.values(errors)[0];
		if (first) {
			strip.setText(first);
			strip.setVisible(true);
			if (errors.name) name.focus();
			return null;
		}
		strip.setVisible(false);
		// Letzte Prüfung vor dem Anlegen; offene Treffer müssen bewusst übergangen werden.
		clearTimeout(pending);
		await Promise.all([check(), checkLexware()]);
		if (lexwareHits.length > 0 && !lxForce.getSelected()) {
			strip.setText("Dieser Kunde ist bereits in Lexware Office vorhanden. Bitte aus Lexware Office übernehmen oder „Trotzdem neu anlegen“ wählen.");
			strip.setVisible(true);
			lxStrip.getDomRef()?.scrollIntoView({ block: "nearest" });
			return null;
		}
		if (candidates.length > 0 && !forceNew.getSelected()) {
			strip.setText("Es gibt ähnliche Kunden. Bitte einen vorhandenen Kunden verwenden oder „Keiner davon: als neuen Kunden anlegen“ wählen.");
			strip.setVisible(true);
			dupStrip.getDomRef()?.scrollIntoView({ block: "nearest" });
			return null;
		}
		const p = phoneList();
		const e = emailList();
		const text = (v: string) => v.trim() || null;
		dialog.setBusy(true);
		try {
			const contact = await post<ContactResponse>("contacts", {
				id: uuidv7(), kind: kind.getSelectedKey(), name: name.getValue().trim(), customerNumber: null,
				street: text(street.getValue()), postalCode: text(postalCode.getValue()), city: text(city.getValue()),
				phone: p[0]?.number ?? null, email: e[0]?.address ?? null, phones: p, emails: e,
				salutation: company ? "Firma" : text(salutation.getValue()), title: company ? null : text(title.getValue()),
				vatId: company ? text(vatId.getValue()) : null, preferredChannel: channel.getSelectedKey() || null, notes: text(notes.getValue()),
				...(withSync ? { lexwareSync: lexwareSync.getState() } : {}),
			});
			if (company && person.name.getValue().trim()) {
				await put(`contacts/${contact.id}/persons/${uuidv7()}`, {
					name: person.name.getValue().trim(), function: text(person.fn.getValue()), phone: text(person.phone.getValue()), email: text(person.email.getValue()),
				}).catch(() => undefined);
			}
			return { contact, existing: false, createBauakte };
		} catch (error) {
			strip.setText(problemMessage(error));
			strip.setVisible(true);
			return null;
		} finally {
			dialog.setBusy(false);
		}
	};

	return new Promise((resolve) => {
		let result: NewCustomerResult | null = null;
		const create = new Button("ncCreate", { text: "Kunde anlegen", type: "Emphasized", press: async () => {
			const r = await submit(false, dialog);
			if (r) finish(r);
		} });
		const createWithBauakte = new Button("ncCreateBauakte", { text: "Anlegen und Bauakte anlegen", icon: "sap-icon://add-document",
			visible: !!options.offerBauakte && session.has("bauakten.create"),
			press: async () => {
				const r = await submit(true, dialog);
				if (r) finish(r);
			} });
		const cancel = new Button({ text: "Abbrechen", press: () => dialog.close() });
		finish = (r) => {
			result = r;
			dialog.close();
		};
		const dialog = new Dialog("newCustomerDialog", {
			title: "Neuer Kunde", contentWidth: "48rem", resizable: true, draggable: true,
			content: [strip, dupBox, main, personForm, notesForm],
			buttons: [create, createWithBauakte, cancel],
			initialFocus: name,
			afterClose: () => {
				clearTimeout(pending);
				dialog.destroy();
				resolve(result);
			},
		});
		dialog.addStyleClass("sapUiContentPadding");
		dialog.attachBrowserEvent("keydown", (event: KeyboardEvent) => {
			if (event.ctrlKey && event.key === "Enter") {
				event.preventDefault();
				create.firePress();
			}
		});
		applyKind();
		if (options.name || options.phone || options.email) scheduleCheck();
		dialog.open();
	});
}
