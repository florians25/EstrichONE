/**
 * Formatierer für XML-Bindungen: core:require="{fmt: 'estrichone/model/formatter'}" und text="{path: 'x', formatter: 'fmt.date'}".
 */
import * as f from "../util/format";
import * as l from "../util/labels";
import { session } from "../util/session";

const state = (tone: l.Tone | undefined) => tone ?? "None";

export default {
	date: (v: string | null) => f.date(v),
	dateTime: (v: string | null) => f.dateTime(v),
	weekdayDate: (v: string | null) => f.weekdayDate(v),
	longDay: (v: string | null) => f.longDay(v),
	time: (v: string | null) => f.time(v),
	timeRange: (a: string | null, b: string | null) => f.timeRange(a, b),
	fileSize: (v: number) => (typeof v === "number" ? f.fileSize(v) : ""),
	area: (v: number | null) => f.area(v),
	money: (v: number | null) => f.money(v),
	quantity: (v: number) => (typeof v === "number" ? f.quantity(v) : ""),
	cm: (v: number | null) => f.cmNumber(v),
	address: (street: string | null, postalCode: string | null, city: string | null) => f.address({ street, postalCode, city }),
	dash: (v: unknown) => (v === null || v === undefined || v === "" ? "–" : String(v)),
	initials: (v: string | null) => f.initials(v),
	person: (id: string | null) => session.name(id),
	waiting: (v: string) => (v ? f.waitingSince(v) : ""),
	weather: (weather: string | null, temperature: number | null, workers: number | null) =>
		f.weatherLine({ weather, temperatureCelsius: temperature, workersPresent: workers }),
	yesNo: (v: boolean) => (v ? "Ja" : "Nein"),

	bauakteStatusText: (s: keyof typeof l.bauakteStatus) => l.bauakteStatus[s]?.text ?? s,
	bauakteStatusState: (s: keyof typeof l.bauakteStatus) => state(l.bauakteStatus[s]?.tone),
	bauakteStatusIcon: (s: keyof typeof l.bauakteStatus) => l.bauakteStatus[s]?.icon ?? "",
	documentCategoryText: (c: keyof typeof l.documentCategories | null) => l.documentCategories[c ?? "other"]?.text ?? c,
	documentCategoryIcon: (c: keyof typeof l.documentCategories | null) => l.documentCategories[c ?? "other"]?.icon ?? "sap-icon://document",
	roomStatusText: (s: keyof typeof l.roomStatus) => l.roomStatus[s]?.text ?? s,
	roomStatusState: (s: keyof typeof l.roomStatus) => state(l.roomStatus[s]?.tone),
	deviceStatusText: (s: keyof typeof l.deviceStatus) => l.deviceStatus[s]?.text ?? s,
	deviceStatusState: (s: keyof typeof l.deviceStatus) => state(l.deviceStatus[s]?.tone),
	ticketStatusText: (s: keyof typeof l.ticketStatus) => l.ticketStatus[s]?.text ?? s,
	ticketStatusState: (s: keyof typeof l.ticketStatus) => state(l.ticketStatus[s]?.tone),
	ticketCategoryText: (s: keyof typeof l.ticketCategories) => l.ticketCategories[s] ?? s,
	ticketPriorityText: (s: keyof typeof l.ticketPriorities) => l.ticketPriorities[s]?.text ?? s,
	ticketPriorityState: (s: keyof typeof l.ticketPriorities) => state(l.ticketPriorities[s]?.tone),
	inquiryStatusText: (s: keyof typeof l.inquiryStatus) => l.inquiryStatus[s]?.text ?? s,
	inquiryStatusState: (s: keyof typeof l.inquiryStatus) => state(l.inquiryStatus[s]?.tone),
	callSourceText: (s: keyof typeof l.callSources) => l.callSources[s] ?? s,
	materialCategoryText: (s: keyof typeof l.materialCategories) => l.materialCategories[s] ?? s,
	orderStatusText: (s: keyof typeof l.orderStatus) => l.orderStatus[s]?.text ?? s,
	orderStatusState: (s: keyof typeof l.orderStatus) => state(l.orderStatus[s]?.tone),
	kindText: (k: keyof typeof l.assignmentKinds) => l.assignmentKinds[k]?.text ?? k,
	kindIcon: (k: keyof typeof l.assignmentKinds) => l.assignmentKinds[k]?.icon ?? "",
	absenceText: (k: string) => l.absenceKinds[k] ?? k,
	resourceKindText: (k: string) => l.resourceKinds[k] ?? k,
	auditText: (a: string) => l.auditTexts[a] ?? a,
	auditState: (a: string) => state(l.auditTone(a)),
	binderText: (b: string | null) => (b ? l.binderNames[b] ?? b : "–"),
	checkText: (r: string | null) => (r ? l.checkResults[r]?.text ?? r : "–"),
	checkState: (r: string | null) => state(r ? l.checkResults[r]?.tone : undefined),
	readyText: (v: boolean | null) => (v ? "Belegreif" : "Noch nicht belegreif"),
	readyState: (v: boolean | null) => (v ? "Success" : "Warning"),
	activeText: (v: boolean) => (v ? "Aktiv" : "Inaktiv"),
	activeState: (v: boolean) => (v ? "Success" : "None"),
	overdueState: (due: string | null, status?: string) => {
		const today = new Date().toISOString().slice(0, 10);
		return due && due < today && status !== "done" ? "Error" : "None";
	},
	fileUrl: (id: string, variant?: string) => `/api/v1/files/${id}/content${variant ? `?variant=${variant}` : ""}`,
};
