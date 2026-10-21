/**
 * Termin anlegen oder bearbeiten (Terminkalender, Teamplaner, Kolonnenplan „Termin ohne Bauvorhaben“): Kalender,
 * Terminart, ganztägig, Beginn/Ende, Wiederholung (täglich, wöchentlich mit Wochentagen, monatlich, jährlich; bis Datum
 * oder Anzahl), Teilnehmer und Kolonnen, Bezug zur Bauakte, Ort, Farbe, Beschreibung. Bei Serien: nur dieser Termin,
 * dieser und alle folgenden oder die ganze Serie.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Label from "sap/m/Label";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import DatePicker from "sap/m/DatePicker";
import TimePicker from "sap/m/TimePicker";
import Select from "sap/m/Select";
import MultiComboBox from "sap/m/MultiComboBox";
import Switch from "sap/m/Switch";
import StepInput from "sap/m/StepInput";
import HBox from "sap/m/HBox";
import Link from "sap/m/Link";
import MessageStrip from "sap/m/MessageStrip";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import Item from "sap/ui/core/Item";
import ListItem from "sap/ui/core/ListItem";
import EventBus from "sap/ui/core/EventBus";
import { del, get, problemMessage, put } from "./api";
import { bauakteInput } from "./forms";
import { uuidv7 } from "./ids";
import { navTo } from "./navigation";
import { session } from "./session";
import type { CrewResponse } from "./models";
import {
	calendarEventKinds, frequencies, localIso, parseLocal, weekdayOf, weekdays,
	type CalendarEditScope, type CalendarEventDetail, type CalendarEventKind, type CalendarResponse, type RecurrenceFrequency, type Weekday,
} from "./planningModels";

export const calendarChanged = () => {
	EventBus.getInstance().publish("estrichone", "calendarChanged");
	EventBus.getInstance().publish("estrichone", "planningChanged");
};

let calendarCache: Promise<CalendarResponse[]> | null = null;
export function calendars(force = false): Promise<CalendarResponse[]> {
	if (force || !calendarCache) {
		calendarCache = get<CalendarResponse[]>("calendar/calendars");
		calendarCache.catch(() => { calendarCache = null; });
	}
	return calendarCache;
}

/** Gut unterscheidbare Farben (auch in dunklen und kontrastreichen Themes erkennbar). */
export const palette = [
	{ key: "#0064D9", text: "Blau" }, { key: "#188918", text: "Grün" }, { key: "#E76500", text: "Orange" }, { key: "#AA0808", text: "Rot" },
	{ key: "#7800A4", text: "Violett" }, { key: "#00838F", text: "Petrol" }, { key: "#C0399F", text: "Magenta" }, { key: "#5D6D7E", text: "Grau" },
	{ key: "#8D6E00", text: "Ocker" }, { key: "#1B3B6F", text: "Dunkelblau" },
];

/** Bei Serien fragen, was gelten soll. `null` = abgebrochen. */
export function askScope(action: string): Promise<CalendarEditScope | null> {
	const only = "Nur dieser Termin";
	const following = "Dieser und alle folgenden";
	const series = "Ganze Serie";
	return new Promise((resolve) => {
		MessageBox.show(`${action} – für welche Termine der Serie?`, {
			title: "Wiederkehrender Termin", icon: MessageBox.Icon.QUESTION,
			actions: [only, following, series, MessageBox.Action.CANCEL], emphasizedAction: only,
			onClose: (choice: string | null) => resolve(choice === only ? "occurrence" : choice === following ? "following" : choice === series ? "series" : null),
		});
	});
}

export interface EventDefaults {
	start: Date;
	end: Date;
	allDay?: boolean;
	calendarId?: string;
	title?: string;
	kind?: CalendarEventKind;
	participantUserIds?: string[];
	crewIds?: string[];
	bauakte?: { id: string; label: string };
}

const pad = (n: number) => String(n).padStart(2, "0");
const day = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const clock = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}:00`;
const combine = (dayValue: string, timeValue: string | null) => parseLocal(`${dayValue}T${timeValue || "00:00:00"}`);

/**
 * Termin bearbeiten. `eventId` + `occurrenceDate` öffnen ein Vorkommen; ohne `eventId` wird angelegt.
 * Liefert true, wenn gespeichert oder gelöscht wurde.
 */
export async function openEventDialog(options: { eventId?: string; occurrenceDate?: string | null; occurrenceStart?: string; defaults?: EventDefaults }): Promise<boolean> {
	const [all, crewList, people] = await Promise.all([calendars(), get<CrewResponse[]>("planning/crews").catch(() => [] as CrewResponse[]), session.people()]);
	const writable = all.filter((c) => c.canEdit && c.isActive);
	let detail: CalendarEventDetail | null = null;
	if (options.eventId) {
		try {
			detail = await get<CalendarEventDetail>(`calendar/events/${options.eventId}`);
		} catch (error) {
			MessageBox.error(problemMessage(error));
			return false;
		}
	}
	if (!detail && writable.length === 0) {
		MessageBox.information("Sie haben keinen Kalender, in den Sie Termine eintragen dürfen.");
		return false;
	}
	const readOnly = !!detail && !detail.canEdit;
	const recurringOccurrence = !!detail && detail.recurrence.frequency !== "none" && !!options.occurrenceDate;

	// Anfangswerte: bei einem Vorkommen dessen Zeit (die Serie beginnt evtl. früher).
	const d = options.defaults;
	let start = detail ? parseLocal(detail.start) : d?.start ?? new Date();
	let end = detail ? parseLocal(detail.end) : d?.end ?? new Date(start.getTime() + 3600_000);
	if (detail && options.occurrenceStart) {
		const duration = end.getTime() - start.getTime();
		start = parseLocal(options.occurrenceStart);
		end = new Date(start.getTime() + duration);
	}
	const allDayInitial = detail?.allDay ?? d?.allDay ?? false;
	if (allDayInitial) end = new Date(end.getTime() - 86400_000); // Anzeige: letzter Tag einschließlich

	const id = `evt${Date.now()}`;
	const title = new Input(`${id}-title`, { value: detail?.title ?? d?.title ?? "", width: "100%", maxLength: 200, placeholder: "z. B. Baubesprechung" });
	const calendar = new Select(`${id}-calendar`, { width: "100%" });
	for (const c of (readOnly ? all : writable)) calendar.addItem(new ListItem({ key: c.id, text: c.name, additionalText: c.kind === "personal" ? "persönlich" : "gemeinsam" }));
	calendar.setSelectedKey(detail?.calendarId ?? d?.calendarId ?? writable.find((c) => c.kind === "shared")?.id ?? writable[0]?.id ?? "");
	const kind = new Select(`${id}-kind`, { width: "100%" });
	for (const [key, k] of Object.entries(calendarEventKinds)) kind.addItem(new ListItem({ key, text: k.text, icon: k.icon }));
	kind.setSelectedKey(detail?.kind ?? d?.kind ?? "appointment");
	const allDay = new Switch(`${id}-allday`, { state: allDayInitial, customTextOn: "Ja", customTextOff: "Nein" });
	const startDay = new DatePicker(`${id}-startday`, { value: day(start), valueFormat: "yyyy-MM-dd", displayFormat: "dd.MM.yyyy", width: "100%" });
	const startTime = new TimePicker(`${id}-starttime`, { value: clock(start), valueFormat: "HH:mm:ss", displayFormat: "HH:mm", minutesStep: 5, width: "100%", support2400: false });
	const endDay = new DatePicker(`${id}-endday`, { value: day(end), valueFormat: "yyyy-MM-dd", displayFormat: "dd.MM.yyyy", width: "100%" });
	const endTime = new TimePicker(`${id}-endtime`, { value: clock(end), valueFormat: "HH:mm:ss", displayFormat: "HH:mm", minutesStep: 5, width: "100%", support2400: false });

	const rule = detail?.recurrence ?? { frequency: "none" as RecurrenceFrequency, interval: 1, weekdays: null, until: null, count: null };
	const frequency = new Select(`${id}-freq`, { width: "100%" });
	for (const [key, text] of Object.entries(frequencies)) frequency.addItem(new Item({ key, text }));
	frequency.setSelectedKey(rule.frequency);
	const interval = new StepInput(`${id}-interval`, { value: rule.interval || 1, min: 1, max: 99, width: "8rem", description: "" });
	const days = new MultiComboBox(`${id}-weekdays`, { width: "100%", placeholder: "Wochentage" });
	for (const w of weekdays) days.addItem(new Item({ key: w.key, text: w.text }));
	days.setSelectedKeys(rule.weekdays?.length ? rule.weekdays : [weekdayOf(start)]);
	const endMode = new Select(`${id}-endmode`, { width: "100%" });
	endMode.addItem(new Item({ key: "never", text: "Ohne Ende" }));
	endMode.addItem(new Item({ key: "until", text: "Bis zum Datum" }));
	endMode.addItem(new Item({ key: "count", text: "Nach Anzahl Terminen" }));
	endMode.setSelectedKey(rule.until ? "until" : rule.count ? "count" : "never");
	const until = new DatePicker(`${id}-until`, { value: rule.until ?? "", valueFormat: "yyyy-MM-dd", displayFormat: "dd.MM.yyyy", width: "100%" });
	const count = new StepInput(`${id}-count`, { value: rule.count ?? 10, min: 1, max: 999, width: "8rem" });

	const participants = new MultiComboBox(`${id}-people`, { width: "100%", placeholder: "Mitarbeiter einladen" });
	for (const p of people.filter((x) => x.isActive)) participants.addItem(new Item({ key: p.id, text: p.displayName }));
	participants.setSelectedKeys(detail?.participantUserIds ?? d?.participantUserIds ?? []);
	const crews = new MultiComboBox(`${id}-crews`, { width: "100%", placeholder: "Kolonnen einladen" });
	for (const c of crewList.filter((x) => x.isActive)) crews.addItem(new Item({ key: c.id, text: c.name }));
	crews.setSelectedKeys(detail?.crewIds ?? d?.crewIds ?? []);
	const bauakte = bauakteInput(`${id}-bauakte`, detail?.bauakteId ?? d?.bauakte?.id ?? null, d?.bauakte?.label ?? (detail?.bauakteId ? "verknüpfte Bauakte" : undefined));
	const location = new Input(`${id}-location`, { value: detail?.location ?? "", width: "100%", maxLength: 300, placeholder: "Ort oder Adresse" });
	const color = new Select(`${id}-color`, { width: "100%" });
	color.addItem(new Item({ key: "", text: "Farbe des Kalenders / der Terminart" }));
	for (const c of palette) color.addItem(new Item({ key: c.key, text: c.text }));
	color.setSelectedKey(detail?.color ?? "");
	const description = new TextArea(`${id}-description`, { value: detail?.description ?? "", rows: 3, width: "100%", maxLength: 4000, growing: true, growingMaxLines: 10 });
	const error = new MessageStrip({ type: "Error", showIcon: true, visible: false });

	const intervalBox = new HBox({ alignItems: "Center", items: [interval] });
	const intervalLabel = new Label({ text: "Alle", labelFor: interval });
	const daysLabel = new Label({ text: "An Wochentagen", labelFor: days });
	const endLabel = new Label({ text: "Ende der Wiederholung", labelFor: endMode });
	const untilLabel = new Label({ text: "Letzter Tag", labelFor: until });
	const countLabel = new Label({ text: "Anzahl", labelFor: count });

	const unitText: Record<string, string> = { daily: "Tage", weekly: "Wochen", monthly: "Monate", yearly: "Jahre" };
	const refresh = () => {
		const timed = !allDay.getState();
		startTime.setVisible(timed);
		endTime.setVisible(timed);
		const f = frequency.getSelectedKey() as RecurrenceFrequency;
		const repeating = f !== "none";
		for (const c of [intervalBox, intervalLabel, endMode, endLabel]) c.setVisible(repeating);
		interval.setDescription(unitText[f] ?? "");
		days.setVisible(f === "weekly");
		daysLabel.setVisible(f === "weekly");
		const mode = endMode.getSelectedKey();
		until.setVisible(repeating && mode === "until");
		untilLabel.setVisible(repeating && mode === "until");
		count.setVisible(repeating && mode === "count");
		countLabel.setVisible(repeating && mode === "count");
	};
	allDay.attachChange(refresh);
	frequency.attachChange(refresh);
	endMode.attachChange(refresh);
	startDay.attachChange(() => {
		// Ende mitziehen, wenn es vor dem Beginn läge.
		if (endDay.getValue() < startDay.getValue()) endDay.setValue(startDay.getValue());
	});

	const pair = (a: DatePicker, b: TimePicker) => new HBox({ items: [a, b], renderType: "Bare", width: "100%" }).addStyleClass("eoDateTimePair");
	const form = new SimpleForm({
		editable: !readOnly, layout: "ResponsiveGridLayout", labelSpanXL: 4, labelSpanL: 4, labelSpanM: 4, labelSpanS: 12, columnsL: 1, columnsM: 1,
		singleContainerFullSize: true,
		content: [
			new Label({ text: "Titel", labelFor: title, required: true }), title,
			new Label({ text: "Kalender", labelFor: calendar, required: true }), calendar,
			new Label({ text: "Terminart", labelFor: kind }), kind,
			new Label({ text: "Ganztägig", labelFor: allDay }), allDay,
			new Label({ text: "Beginn", labelFor: startDay, required: true }), pair(startDay, startTime),
			new Label({ text: "Ende", labelFor: endDay, required: true }), pair(endDay, endTime),
			new Label({ text: "Wiederholung", labelFor: frequency }), frequency,
			intervalLabel, intervalBox,
			daysLabel, days,
			endLabel, endMode,
			untilLabel, until,
			countLabel, count,
			new Label({ text: "Teilnehmer", labelFor: participants }), participants,
			new Label({ text: "Kolonnen", labelFor: crews }), crews,
			new Label({ text: "Bauakte (optional)", labelFor: bauakte.control }), bauakte.control,
			new Label({ text: "Ort", labelFor: location }), location,
			new Label({ text: "Farbe", labelFor: color }), color,
			new Label({ text: "Beschreibung", labelFor: description }), description,
		],
	});
	if (readOnly) {
		for (const c of [title, calendar, kind, allDay, startDay, startTime, endDay, endTime, frequency, interval, days, endMode, until, count, participants, crews,
			bauakte.control, location, color, description]) {
			(c as unknown as { setEnabled?: (v: boolean) => void; setEditable?: (v: boolean) => void }).setEditable?.(false);
			if (c instanceof Select || c instanceof Switch) c.setEnabled(false);
		}
	}
	refresh();

	return new Promise<boolean>((resolve) => {
		let changed = false;
		const values = () => {
			const isAllDay = allDay.getState();
			let s = combine(startDay.getValue(), isAllDay ? null : startTime.getValue());
			let e = combine(endDay.getValue(), isAllDay ? null : endTime.getValue());
			if (isAllDay) e = new Date(e.getFullYear(), e.getMonth(), e.getDate() + 1);
			if (!startDay.getValue() || !endDay.getValue()) throw new Error("Bitte Beginn und Ende angeben.");
			if (!isAllDay && e <= s) throw new Error("Das Ende muss nach dem Beginn liegen.");
			if (!title.getValue().trim()) throw new Error("Bitte einen Titel angeben.");
			const f = frequency.getSelectedKey() as RecurrenceFrequency;
			const mode = endMode.getSelectedKey();
			if (f !== "none" && mode === "until" && !until.getValue()) throw new Error("Bitte den letzten Tag der Wiederholung angeben.");
			s = new Date(s);
			return {
				calendarId: calendar.getSelectedKey(), title: title.getValue().trim(), location: location.getValue().trim() || null,
				description: description.getValue().trim() || null, kind: kind.getSelectedKey(), color: color.getSelectedKey() || null, allDay: isAllDay,
				start: localIso(s), end: localIso(e), participantUserIds: participants.getSelectedKeys(), crewIds: crews.getSelectedKeys(),
				bauakteId: bauakte.read(),
				recurrence: {
					frequency: f, interval: Math.round(interval.getValue()) || 1, weekdays: f === "weekly" ? (days.getSelectedKeys() as Weekday[]) : null,
					until: f !== "none" && mode === "until" ? until.getValue() : null, count: f !== "none" && mode === "count" ? Math.round(count.getValue()) : null,
				},
			};
		};
		const save = new Button({
			text: "Speichern", type: "Emphasized", visible: !readOnly,
			press: async () => {
				let body: ReturnType<typeof values>;
				try {
					body = values();
				} catch (e) {
					error.setText((e as Error).message);
					error.setVisible(true);
					return;
				}
				let scope: CalendarEditScope = "series";
				if (recurringOccurrence) {
					const chosen = await askScope("Änderung speichern");
					if (!chosen) return;
					scope = chosen;
				}
				dialog.setBusy(true);
				try {
					if (detail && recurringOccurrence && scope !== "series") {
						const next = scope === "occurrence" ? { ...body, recurrence: { frequency: "none", interval: 1, weekdays: null, until: null, count: null } } : body;
						await put(`calendar/events/${uuidv7()}`, { ...next, seriesId: detail.id, occurrenceDate: options.occurrenceDate, scope });
					} else if (detail && recurringOccurrence && scope === "series") {
						// Ganze Serie: die Uhrzeit ändern, den ersten Tag der Serie behalten (Verschiebung um die Differenz).
						const shift = parseLocal(body.start).getTime() - parseLocal(options.occurrenceStart ?? detail.start).getTime();
						const first = new Date(parseLocal(detail.start).getTime() + shift);
						const duration = parseLocal(body.end).getTime() - parseLocal(body.start).getTime();
						await put(`calendar/events/${detail.id}`, { ...body, start: localIso(first), end: localIso(new Date(first.getTime() + duration)) });
					} else {
						await put(`calendar/events/${detail?.id ?? uuidv7()}`, body);
					}
					changed = true;
					calendarChanged();
					MessageToast.show("Termin gespeichert");
					dialog.close();
				} catch (e) {
					error.setText(problemMessage(e));
					error.setVisible(true);
				} finally {
					dialog.setBusy(false);
				}
			},
		});
		const remove = new Button({
			text: "Löschen", type: "Reject", icon: "sap-icon://delete", visible: !!detail && !readOnly,
			press: async () => {
				if (!detail) return;
				let query = "";
				if (recurringOccurrence) {
					const chosen = await askScope("Löschen");
					if (!chosen) return;
					query = chosen === "series" ? "" : `?occurrence=${options.occurrenceDate}&scope=${chosen}`;
				} else {
					const ok = await new Promise<boolean>((done) => MessageBox.confirm(`Termin „${detail?.title}“ löschen?`, {
						actions: ["Löschen", MessageBox.Action.CANCEL], emphasizedAction: "Löschen", onClose: (a: string) => done(a === "Löschen"),
					}));
					if (!ok) return;
				}
				try {
					await del(`calendar/events/${detail.id}${query}`);
					changed = true;
					calendarChanged();
					MessageToast.show("Termin gelöscht");
					dialog.close();
				} catch (e) {
					error.setText(problemMessage(e));
					error.setVisible(true);
				}
			},
		});
		const extra = detail?.bauakteId ? [new Link({ text: "Bauakte öffnen", press: () => { dialog.close(); navTo(`/bauakten/${detail?.bauakteId}`); } })] : [];
		const dialog: Dialog = new Dialog({
			title: detail ? (readOnly ? "Termin" : "Termin bearbeiten") : "Neuer Termin",
			contentWidth: "40rem", resizable: true, draggable: true,
			content: [error, form, ...extra.map((l) => l.addStyleClass("sapUiSmallMarginBegin sapUiSmallMarginBottom"))],
			buttons: [...(detail && !readOnly ? [remove] : []), save, new Button({ text: readOnly ? "Schließen" : "Abbrechen", press: () => dialog.close() })],
			initialFocus: title,
			afterClose: () => { dialog.destroy(); resolve(changed); },
		});
		dialog.addStyleClass("sapUiContentPadding");
		dialog.open();
	});
}
