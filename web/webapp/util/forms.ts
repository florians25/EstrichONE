/**
 * Formulardialoge aus einer Feldbeschreibung: ein Muster für alle Stammdaten- und Erfassungsdialoge
 * (SimpleForm, Pflichtfelder, deutsche Zahlen und Datumsformate, Fehler im Dialog statt Alert).
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Label from "sap/m/Label";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import DatePicker from "sap/m/DatePicker";
import TimePicker from "sap/m/TimePicker";
import Select from "sap/m/Select";
import ComboBox from "sap/m/ComboBox";
import MultiComboBox from "sap/m/MultiComboBox";
import Switch from "sap/m/Switch";
import CheckBox from "sap/m/CheckBox";
import SegmentedButton from "sap/m/SegmentedButton";
import SegmentedButtonItem from "sap/m/SegmentedButtonItem";
import MessageStrip from "sap/m/MessageStrip";
import Text from "sap/m/Text";
import Title from "sap/m/Title";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import Item from "sap/ui/core/Item";
import ListItem from "sap/ui/core/ListItem";
import SuggestionItem from "sap/m/SuggestionItem";
import type Control from "sap/ui/core/Control";
import type InputBase from "sap/m/InputBase";
import { ValueState } from "sap/ui/core/library";
import { get, post, problemMessage } from "./api";
import { parseNumber } from "./format";
import { uuidv7 } from "./ids";
import type { BauaktePage, ContactResponse } from "./models";

export type FieldType =
	| "text" | "textarea" | "email" | "password" | "number" | "integer" | "date" | "time" | "select" | "combo" | "multi" | "switch"
	| "checkbox" | "segmented" | "customer" | "bauakte" | "section" | "info";

export interface Option {
	key: string;
	text: string;
	additionalText?: string;
}

export interface Field {
	key: string;
	label?: string;
	type?: FieldType;
	required?: boolean;
	placeholder?: string;
	options?: Option[];
	rows?: number;
	maxLength?: number;
	/** Hinweis unter dem Feld (Infozeile). */
	help?: string;
	min?: number;
	max?: number;
	/** Einheit hinter Zahlen, z. B. „m²“. */
	unit?: string;
	/** Anzeigename für Kunde/Bauakte, wenn schon ein Wert gesetzt ist. */
	displayText?: string;
	/** Vorschläge für Freitext (z. B. Einheiten). */
	suggestions?: string[];
	editable?: boolean;
	/** Zusätzliche Prüfung, liefert Fehlermeldung oder null. */
	validate?: (value: unknown, values: Record<string, unknown>) => string | null;
	/** Bei Änderung andere Felder anpassen (z. B. Tätigkeit aus Terminart). */
	onChange?: (value: unknown, form: FormHandle) => void;
}

export interface FormHandle {
	get(key: string): unknown;
	set(key: string, value: unknown): void;
	control(key: string): Control | undefined;
}

export interface FormSpec<T> {
	title: string;
	fields: Field[];
	initial?: Record<string, unknown>;
	submitText?: string;
	/** Rote Bestätigung (Löschen, Stornieren). */
	danger?: boolean;
	/** Breite des Dialogs, z. B. „40rem“. */
	width?: string;
	intro?: string;
	/** Nur ansehen (ohne Speichern), z. B. ohne Planungsrecht. */
	readOnly?: boolean;
	/** Zusätzliche Inhalte unter dem Formular (Links, Hinweise). */
	extra?: (dialog: Dialog) => Control[];
	/** Zusätzliche Schaltfläche links (z. B. Löschen). */
	leftButton?: (dialog: Dialog) => Button | undefined;
	/**
	 * Speichern; ein geworfener Fehler bleibt im Dialog sichtbar. Liefert `KEEP_OPEN`, bleibt der Dialog offen
	 * (z. B. um Hinweise nach dem Speichern zu zeigen – `context.warn`).
	 */
	submit: (values: Record<string, unknown>, context: { dialog: Dialog; warn: (messages: string[], saved?: boolean) => void }) => Promise<T | typeof KEEP_OPEN>;
}

export const KEEP_OPEN = Symbol("keepOpen");

interface Bound {
	field: Field;
	control: Control;
	read: () => unknown;
	write: (value: unknown) => void;
}

const CUSTOMER_NEW = "__new__";

/** Kunde suchen (Name, Kundennummer, Ort) oder mit dem eingegebenen Namen direkt anlegen. */
export function customerInput(id: string, initialId: string | null, initialText: string | undefined, onChange?: (id: string | null) => void): {
	control: Input; read: () => string | null; write: (v: unknown) => void;
} {
	let selected: string | null = initialId;
	let timer: ReturnType<typeof setTimeout> | undefined;
	const input = new Input(id, {
		value: initialText ?? "",
		placeholder: "Name, Kundennummer oder Ort",
		showSuggestion: true,
		filterSuggests: false,
		startSuggestion: 2,
		maxSuggestionWidth: "32rem",
		width: "100%",
	});
	const fill = (list: ContactResponse[], text: string) => {
		input.destroySuggestionItems();
		for (const c of list) {
			input.addSuggestionItem(new SuggestionItem({ key: c.id, text: c.name, description: [c.customerNumber, c.city].filter(Boolean).join(" · ") }));
		}
		if (text.trim().length >= 2 && !list.some((c) => c.name.toLowerCase() === text.trim().toLowerCase())) {
			input.addSuggestionItem(new SuggestionItem({ key: CUSTOMER_NEW, text: `„${text.trim()}“ als neuen Kunden anlegen`, icon: "sap-icon://add" }));
		}
	};
	input.attachSuggest((event) => {
		const text = String(event.getParameter("suggestValue") ?? "");
		clearTimeout(timer);
		timer = setTimeout(async () => {
			try {
				fill(await get<ContactResponse[]>("contacts", { search: text.trim(), limit: 10 }), text);
				input.setFilterSuggests(false);
			} catch {
				fill([], text);
			}
		}, 150);
	});
	input.attachSuggestionItemSelected(async (event) => {
		const item = event.getParameter("selectedItem") as SuggestionItem | null;
		if (!item) return;
		if (item.getKey() === CUSTOMER_NEW) {
			const name = item.getText().replace(/^„/, "").replace(/“ als neuen Kunden anlegen$/, "");
			input.setBusy(true);
			try {
				const contact = await post<ContactResponse>("contacts", { id: uuidv7(), kind: "person", name });
				selected = contact.id;
				input.setValue(contact.name);
				input.setValueState(ValueState.Success);
				input.setValueStateText("Neuer Kunde angelegt");
				onChange?.(selected);
			} catch (error) {
				input.setValueState(ValueState.Error);
				input.setValueStateText(problemMessage(error));
			} finally {
				input.setBusy(false);
			}
			return;
		}
		selected = item.getKey();
		input.setValue(item.getText());
		input.setValueState(ValueState.None);
		onChange?.(selected);
	});
	input.attachLiveChange(() => {
		if (selected) {
			selected = null;
			input.setValueState(ValueState.None);
			onChange?.(null);
		}
	});
	return {
		control: input,
		read: () => (input.getValue().trim() ? selected : null),
		write: (v) => {
			selected = (v as string | null) ?? null;
			if (!selected) input.setValue("");
		},
	};
}

/** Bauakte suchen (Nummer, Bauvorhaben, Ort). */
export function bauakteInput(id: string, initialId: string | null, initialText: string | undefined, onChange?: (id: string | null) => void): {
	control: Input; read: () => string | null; write: (v: unknown) => void;
} {
	let selected: string | null = initialId;
	let timer: ReturnType<typeof setTimeout> | undefined;
	const input = new Input(id, {
		value: initialText ?? "",
		placeholder: "Nummer oder Bauvorhaben eingeben",
		showSuggestion: true,
		filterSuggests: false,
		startSuggestion: 2,
		maxSuggestionWidth: "32rem",
		width: "100%",
	});
	input.attachSuggest((event) => {
		const text = String(event.getParameter("suggestValue") ?? "");
		clearTimeout(timer);
		timer = setTimeout(async () => {
			const page = await get<BauaktePage>("bauakten", { search: text.trim(), limit: 10 }).catch(() => ({ items: [] }));
			input.destroySuggestionItems();
			for (const b of page.items) {
				input.addSuggestionItem(new SuggestionItem({ key: b.id, text: `${b.number ?? "neu"} · ${b.title}`, description: b.city ?? "" }));
			}
		}, 150);
	});
	input.attachSuggestionItemSelected((event) => {
		const item = event.getParameter("selectedItem") as SuggestionItem | null;
		if (!item) return;
		selected = item.getKey();
		input.setValue(item.getText());
		onChange?.(selected);
	});
	input.attachLiveChange(() => {
		if (selected) {
			selected = null;
			onChange?.(null);
		}
	});
	return { control: input, read: () => (input.getValue().trim() ? selected : null), write: (v) => { selected = (v as string | null) ?? null; } };
}

let counter = 0;

function build(field: Field, initial: unknown, prefix: string, handle: () => FormHandle): Bound {
	const id = `${prefix}-${field.key}`;
	const type = field.type ?? "text";
	const notify = (value: unknown) => field.onChange?.(value, handle());
	switch (type) {
		case "textarea": {
			const c = new TextArea(id, { value: (initial as string) ?? "", rows: field.rows ?? 3, width: "100%", placeholder: field.placeholder,
				maxLength: field.maxLength ?? 0, showExceededText: !!field.maxLength, growing: true, growingMaxLines: 12 });
			return { field, control: c, read: () => c.getValue().trim(), write: (v) => c.setValue((v as string) ?? "") };
		}
		case "number":
		case "integer": {
			const format = (v: unknown) => (v === null || v === undefined || v === "" ? "" : Number(v).toLocaleString("de-DE", { maximumFractionDigits: 3, useGrouping: false }));
			const c = new Input(id, { value: format(initial), placeholder: field.placeholder, description: field.unit, fieldWidth: field.unit ? "75%" : "100%",
				width: "100%", type: "Text", textAlign: "End" });
			c.attachChange(() => notify(parseNumber(c.getValue())));
			return { field, control: c, read: () => parseNumber(c.getValue()), write: (v) => c.setValue(format(v)) };
		}
		case "date": {
			const c = new DatePicker(id, { value: (initial as string) ?? "", valueFormat: "yyyy-MM-dd", displayFormat: "dd.MM.yyyy", width: "100%",
				placeholder: field.placeholder ?? "TT.MM.JJJJ" });
			c.attachChange(() => notify(c.getValue()));
			return { field, control: c, read: () => c.getValue() || null, write: (v) => c.setValue((v as string) ?? "") };
		}
		case "time": {
			const c = new TimePicker(id, { value: (initial as string) ?? "", valueFormat: "HH:mm:ss", displayFormat: "HH:mm", minutesStep: 5, width: "100%",
				placeholder: field.placeholder ?? "HH:MM", support2400: false });
			return { field, control: c, read: () => c.getValue() || null, write: (v) => c.setValue((v as string) ?? "") };
		}
		case "select": {
			const c = new Select(id, { selectedKey: (initial as string) ?? "", width: "100%", forceSelection: false });
			if (!field.required) c.addItem(new Item({ key: "", text: field.placeholder ?? "–" }));
			for (const o of field.options ?? []) c.addItem(new ListItem({ key: o.key, text: o.text, additionalText: o.additionalText }));
			c.setSelectedKey((initial as string) ?? "");
			c.attachChange(() => notify(c.getSelectedKey() || null));
			return { field, control: c, read: () => c.getSelectedKey() || null, write: (v) => c.setSelectedKey((v as string) ?? "") };
		}
		case "combo": {
			// Mit Optionen: Auswahl liefert den Schlüssel (Person, Rolle …); nur Vorschläge: freier Text.
			const keyed = !!field.options && field.options.some((o) => o.key !== o.text);
			const c = new ComboBox(id, { width: "100%", placeholder: field.placeholder });
			for (const o of field.options ?? (field.suggestions ?? []).map((s): Option => ({ key: s, text: s }))) {
				c.addItem(new ListItem({ key: o.key, text: o.text, additionalText: o.additionalText }));
			}
			const read = () => {
				if (!keyed) return c.getValue().trim();
				return c.getSelectedKey() || field.options?.find((o) => o.text === c.getValue().trim())?.key || "";
			};
			const write = (v: unknown) => (keyed ? c.setSelectedKey((v as string) ?? "") : c.setValue((v as string) ?? ""));
			write(initial);
			c.attachChange(() => notify(read()));
			return { field, control: c, read, write };
		}
		case "multi": {
			const c = new MultiComboBox(id, { width: "100%", placeholder: field.placeholder, showSelectAll: false });
			for (const o of field.options ?? []) c.addItem(new ListItem({ key: o.key, text: o.text, additionalText: o.additionalText }));
			c.setSelectedKeys((initial as string[]) ?? []);
			c.attachSelectionFinish(() => notify(c.getSelectedKeys()));
			return { field, control: c, read: () => c.getSelectedKeys(), write: (v) => c.setSelectedKeys((v as string[]) ?? []) };
		}
		case "switch": {
			const c = new Switch(id, { state: !!initial, customTextOn: "Ja", customTextOff: "Nein" });
			c.attachChange(() => notify(c.getState()));
			return { field, control: c, read: () => c.getState(), write: (v) => c.setState(!!v) };
		}
		case "checkbox": {
			const c = new CheckBox(id, { selected: !!initial, text: field.placeholder });
			c.attachSelect(() => notify(c.getSelected()));
			return { field, control: c, read: () => c.getSelected(), write: (v) => c.setSelected(!!v) };
		}
		case "segmented": {
			const c = new SegmentedButton(id, { selectedKey: (initial as string) ?? field.options?.[0]?.key, width: "auto" });
			for (const o of field.options ?? []) c.addItem(new SegmentedButtonItem({ key: o.key, text: o.text }));
			c.setSelectedKey((initial as string) ?? field.options?.[0]?.key ?? "");
			c.attachSelectionChange(() => notify(c.getSelectedKey()));
			return { field, control: c, read: () => c.getSelectedKey(), write: (v) => c.setSelectedKey(v as string) };
		}
		case "customer": {
			const b = customerInput(id, (initial as string) ?? null, field.displayText, (v) => notify(v));
			return { field, ...b };
		}
		case "bauakte": {
			const b = bauakteInput(id, (initial as string) ?? null, field.displayText, (v) => notify(v));
			return { field, ...b };
		}
		case "section": {
			const c = new Title(id, { text: field.label, level: "H5" });
			return { field, control: c, read: () => undefined, write: () => undefined };
		}
		case "info": {
			const c = new Text(id, { text: field.help ?? field.label });
			c.addStyleClass("sapUiTinyMarginBottom");
			return { field, control: c, read: () => undefined, write: () => undefined };
		}
		default: {
			const c = new Input(id, {
				value: (initial as string) ?? "", placeholder: field.placeholder, width: "100%", editable: field.editable ?? true,
				type: type === "email" ? "Email" : type === "password" ? "Password" : "Text", maxLength: field.maxLength ?? 0,
				showSuggestion: !!field.suggestions?.length, startSuggestion: 0,
			});
			for (const s of field.suggestions ?? []) c.addSuggestionItem(new Item({ key: s, text: s }));
			c.attachChange(() => notify(c.getValue()));
			return { field, control: c, read: () => c.getValue().trim(), write: (v) => c.setValue((v as string) ?? "") };
		}
	}
}

function check(bound: Bound, values: Record<string, unknown>): string | null {
	const { field } = bound;
	const value = values[field.key];
	const type = field.type ?? "text";
	if (["section", "info", "switch", "checkbox", "segmented"].includes(type)) return null;
	const empty = value === null || value === undefined || value === "" || (Array.isArray(value) && value.length === 0);
	if (field.required && empty) {
		return `Bitte „${field.label}“ angeben.`;
	}
	if ((type === "number" || type === "integer") && typeof value === "number" && Number.isNaN(value)) {
		return "Bitte eine Zahl eingeben (z. B. 42,5).";
	}
	if (type === "integer" && typeof value === "number" && !Number.isInteger(value)) {
		return "Bitte eine ganze Zahl eingeben.";
	}
	if (typeof value === "number" && field.min !== undefined && value < field.min) return `Mindestens ${field.min}.`;
	if (typeof value === "number" && field.max !== undefined && value > field.max) return `Höchstens ${field.max}.`;
	if (type === "email" && typeof value === "string" && value && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) {
		return "Bitte eine gültige E-Mail-Adresse angeben.";
	}
	return field.validate?.(value, values) ?? null;
}

/** Öffnet den Dialog; liefert das Ergebnis von `submit` oder `null` bei Abbruch. */
export function openForm<T>(spec: FormSpec<T>): Promise<T | null> {
	const prefix = `form${++counter}`;
	const bound: Bound[] = [];
	const handle: FormHandle = {
		get: (key) => bound.find((b) => b.field.key === key)?.read(),
		set: (key, value) => bound.find((b) => b.field.key === key)?.write(value),
		control: (key) => bound.find((b) => b.field.key === key)?.control,
	};
	const form = new SimpleForm({
		editable: true,
		layout: "ResponsiveGridLayout",
		labelSpanXL: 4, labelSpanL: 4, labelSpanM: 4, labelSpanS: 12,
		columnsXL: 1, columnsL: 1, columnsM: 1,
		singleContainerFullSize: true,
	});
	const strip = new MessageStrip({ type: "Error", showIcon: true, visible: false });
	strip.addStyleClass("sapUiSmallMarginBottom");
	const warnings = new MessageStrip({ type: "Warning", showIcon: true, visible: false });
	warnings.data("role", "status", true);
	if (spec.intro) {
		form.addContent(new Text({ text: spec.intro }).addStyleClass("sapUiSmallMarginBottom"));
	}
	for (const field of spec.fields) {
		const b = build(field, spec.initial?.[field.key], prefix, () => handle);
		bound.push(b);
		if (field.type === "section") {
			form.addContent(b.control);
			continue;
		}
		if (field.label && field.type !== "info") {
			form.addContent(new Label({ text: field.label, labelFor: b.control, required: !!field.required }));
		}
		form.addContent(b.control);
		if (field.help && field.type !== "info") {
			form.addContent(new Text({ text: field.help }).addStyleClass("eoHelpText"));
		}
	}

	return new Promise<T | null>((resolve) => {
		let done = false;
		let cancelRef: Button | null = null;
		const submit = new Button({
			text: spec.submitText ?? "Speichern",
			type: spec.danger ? "Reject" : "Emphasized",
			press: async () => {
				const values: Record<string, unknown> = {};
				for (const b of bound) values[b.field.key] = b.read();
				let firstError: string | null = null;
				for (const b of bound) {
					const message = check(b, values);
					const control = b.control as unknown as InputBase;
					if (typeof control.setValueState === "function") {
						control.setValueState(message ? ValueState.Error : ValueState.None);
						control.setValueStateText(message ?? "");
					}
					firstError ??= message;
				}
				if (firstError) {
					strip.setText(firstError);
					strip.setVisible(true);
					return;
				}
				dialog.setBusy(true);
				strip.setVisible(false);
				try {
					const result = await spec.submit(values, {
						dialog,
						warn: (messages, saved = true) => {
							warnings.setText(`Hinweise: ${messages.join(" ")}`);
							warnings.setVisible(messages.length > 0);
							// Bereits gespeichert: „Abbrechen“ würde nichts mehr verwerfen.
							if (messages.length > 0 && saved) {
								cancelRef?.setText("Schließen");
							}
						},
					});
					if (result === KEEP_OPEN) return;
					done = true;
					dialog.close();
					resolve(result as T);
				} catch (error) {
					strip.setText(problemMessage(error));
					strip.setVisible(true);
				} finally {
					dialog.setBusy(false);
				}
			},
		});
		const dialog = new Dialog({
			title: spec.title,
			contentWidth: spec.width ?? "36rem",
			resizable: true,
			draggable: true,
			content: [strip, form, warnings],
			afterClose: () => {
				dialog.destroy();
				if (!done) resolve(null);
			},
		});
		for (const control of spec.extra?.(dialog) ?? []) dialog.addContent(control);
		const left = spec.leftButton?.(dialog);
		if (left) dialog.addButton(left);
		if (!spec.readOnly) dialog.addButton(submit);
		const cancel = new Button({ text: spec.readOnly ? "Schließen" : "Abbrechen", press: () => dialog.close() });
		dialog.addButton(cancel);
		cancelRef = cancel;
		dialog.addStyleClass("sapUiContentPadding");
		// Strg+Enter speichert, wie in der Telefon-Schnellerfassung.
		dialog.attachBrowserEvent("keydown", (event: KeyboardEvent) => {
			if (event.ctrlKey && event.key === "Enter") {
				event.preventDefault();
				submit.firePress();
			}
		});
		dialog.open();
	});
}

/** Einfache Abfrage eines Textes (z. B. Stornogrund). */
export function prompt(title: string, label: string, options: { submitText?: string; danger?: boolean; required?: boolean; initial?: string; intro?: string; rows?: number } = {}): Promise<string | null> {
	return openForm<string>({
		title, intro: options.intro, submitText: options.submitText, danger: options.danger,
		fields: [{ key: "value", label, type: options.rows ? "textarea" : "text", rows: options.rows, required: options.required ?? true }],
		initial: { value: options.initial ?? "" },
		submit: async (values) => String(values.value ?? ""),
	});
}
