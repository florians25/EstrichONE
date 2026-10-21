/** Fachliche Bezeichnungen und Farbbedeutungen (ohne UI5-Abhängigkeit). */
import type {
	AssignmentKind, BauakteStatus, CallPriority, CallSource, DeviceStatus, DocumentCategory, InquiryStatus, MaterialCategory, OrderStatus,
	RoomStatus, TicketCategory, TicketPriority, TicketStatus,
} from "./models";

/** Semantische Farbe in UI5-Begriffen: ValueState bzw. Indikationsfarbe. */
export type Tone = "None" | "Information" | "Success" | "Warning" | "Error";

export const bauakteStatus: Record<BauakteStatus, { text: string; tone: Tone; icon: string }> = {
	inquiry: { text: "Anfrage", tone: "None", icon: "sap-icon://inbox" },
	planned: { text: "Geplant", tone: "Information", icon: "sap-icon://appointment-2" },
	inProgress: { text: "In Ausführung", tone: "Information", icon: "sap-icon://process" },
	drying: { text: "Belegreif / Trocknung", tone: "Warning", icon: "sap-icon://temperature" },
	completed: { text: "Abgeschlossen", tone: "Success", icon: "sap-icon://complete" },
	cancelled: { text: "Storniert", tone: "Error", icon: "sap-icon://cancel" },
};
export const flowStatuses: BauakteStatus[] = ["inquiry", "planned", "inProgress", "drying", "completed"];

/** Markerfarben der Karte je Status. */
export const statusMarkerColors: Record<BauakteStatus, string> = {
	inquiry: "#8C8C8C", planned: "#1677FF", inProgress: "#0B2D5B", drying: "#FA8C16", completed: "#52C41A", cancelled: "#BFBFBF",
};

export const documentCategories: Record<DocumentCategory, { text: string; icon: string }> = {
	offer: { text: "Angebote", icon: "sap-icon://sales-quote" },
	order: { text: "Aufträge", icon: "sap-icon://sales-order" },
	plan: { text: "Pläne", icon: "sap-icon://blueprint" },
	protocol: { text: "Protokolle", icon: "sap-icon://inspection" },
	acceptance: { text: "Abnahmen", icon: "sap-icon://approvals" },
	invoice: { text: "Rechnungen", icon: "sap-icon://receipt" },
	delivery: { text: "Lieferscheine", icon: "sap-icon://shipping-status" },
	customer: { text: "Kundendokumente", icon: "sap-icon://customer" },
	office: { text: "Büro", icon: "sap-icon://folder-blank" },
	other: { text: "Sonstiges", icon: "sap-icon://document" },
};

export const roomStatus: Record<RoomStatus, { text: string; tone: Tone }> = {
	planned: { text: "Geplant", tone: "Information" }, laid: { text: "Eingebracht", tone: "Warning" }, ready: { text: "Belegreif", tone: "Success" },
};

export const deviceStatus: Record<DeviceStatus, { text: string; tone: Tone }> = {
	pending: { text: "Wartet auf Freigabe", tone: "Warning" }, active: { text: "Freigegeben", tone: "Success" },
	locked: { text: "Gesperrt", tone: "Error" }, retired: { text: "Entfernt", tone: "None" },
};

export const ticketStatus: Record<TicketStatus, { text: string; tone: Tone }> = {
	new: { text: "Neu", tone: "Information" }, inProgress: { text: "In Bearbeitung", tone: "Information" },
	waitingForReporter: { text: "Rückfrage", tone: "Warning" }, resolved: { text: "Gelöst", tone: "Success" }, closed: { text: "Geschlossen", tone: "None" },
};
export const ticketCategories: Record<TicketCategory, string> = {
	signIn: "Anmeldung", sync: "Synchronisation", appError: "App-Fehler", deviceDefect: "Gerät defekt", officeIt: "Drucker/Büro-IT",
	access: "Zugang/Rechte", other: "Sonstiges",
};
export const ticketPriorities: Record<TicketPriority, { text: string; tone: Tone }> = {
	low: { text: "Niedrig", tone: "None" }, normal: { text: "Normal", tone: "Information" }, high: { text: "Hoch", tone: "Warning" },
	urgent: { text: "Dringend", tone: "Error" },
};

const eventTexts: Record<string, string> = {
	created: "hat das Problem gemeldet", status: "hat den Status geändert", assignee: "hat die Zuständigkeit geändert",
	priority: "hat die Priorität geändert", category: "hat die Kategorie geändert",
};
/** Ereignisse speichern die C#-Namen (`InProgress`), die API liefert sonst camelCase. */
const camel = (value: string) => value.charAt(0).toLowerCase() + value.slice(1);

export function describeTicketEvent(kind: string, to: string | null): string {
	const base = eventTexts[kind] ?? kind;
	if (kind === "status" && to) {
		return `${base}: ${ticketStatus[camel(to) as TicketStatus]?.text ?? to}`;
	}
	if (kind === "priority" && to) {
		return `${base}: ${ticketPriorities[camel(to) as TicketPriority]?.text ?? to}`;
	}
	return base;
}

export const inquiryStatus: Record<InquiryStatus, { text: string; tone: Tone }> = {
	open: { text: "Offen", tone: "Information" }, inProgress: { text: "In Bearbeitung", tone: "Warning" },
	converted: { text: "Bauakte erstellt", tone: "Success" }, cancelled: { text: "Abgebrochen", tone: "None" },
};
export const callSources: Record<CallSource, string> = { phone: "Telefon", email: "E-Mail", website: "Website", personal: "Persönlich", other: "Sonstiges" };
export const callPriorities: Record<CallPriority, string> = { low: "Niedrig", normal: "Normal", high: "Hoch" };

export const materialCategories: Record<MaterialCategory, string> = {
	screed: "Estrichmörtel", binder: "Bindemittel", aggregate: "Zuschlag (Sand/Kies)", insulation: "Dämmung", membrane: "Folie / Abdeckung",
	edgeStrip: "Randdämmstreifen", additive: "Zusatzmittel / Fasern", other: "Sonstiges",
};
export const orderStatus: Record<OrderStatus, { text: string; tone: Tone }> = {
	draft: { text: "Entwurf", tone: "None" }, ordered: { text: "Bestellt", tone: "Information" }, confirmed: { text: "Bestätigt", tone: "Information" },
	delivered: { text: "Geliefert", tone: "Success" }, cancelled: { text: "Storniert", tone: "Error" },
};
/** Übliche Einheiten im Estrichbau; frei erweiterbar. */
export const units = ["t", "m³", "Sack", "Palette", "m²", "lfm", "Rolle", "Stk", "kg", "l", "Eimer"];
export const deliveryWindows = ["6:00–7:00", "7:00–8:00", "8:00–10:00", "vormittags", "nachmittags", "nach telefonischer Absprache"];

export const assignmentKinds: Record<AssignmentKind, { text: string; color: string; icon: string; activity: string }> = {
	screed: { text: "Estrich / Ausführung", color: "#0B2D5B", icon: "sap-icon://building", activity: "Estrich einbringen" },
	initialInspection: { text: "Erstbegehung", color: "#7C3AED", icon: "sap-icon://inspection", activity: "Erstbegehung" },
	secondInspection: { text: "Zweitbegehung", color: "#9F67FF", icon: "sap-icon://search", activity: "Zweitbegehung" },
	cmMeasurement: { text: "CM-Messung", color: "#0891B2", icon: "sap-icon://lab", activity: "CM-Messung" },
	acceptance: { text: "Abnahme", color: "#16A34A", icon: "sap-icon://approvals", activity: "Abnahme" },
	survey: { text: "Aufmaß", color: "#CA8A04", icon: "sap-icon://measure", activity: "Aufmaß" },
	check: { text: "Kontrolle", color: "#EA580C", icon: "sap-icon://wrench", activity: "Kontrolle" },
	customerAppointment: { text: "Kundentermin", color: "#DB2777", icon: "sap-icon://customer", activity: "Kundentermin" },
	delivery: { text: "Lieferung", color: "#64748B", icon: "sap-icon://shipping-status", activity: "Lieferung" },
	other: { text: "Sonstiges", color: "#94A3B8", icon: "sap-icon://overflow", activity: "" },
};

export const absenceKinds: Record<string, string> = {
	vacation: "Urlaub", sick: "Krank", training: "Schulung", other: "Sonstiges", specialLeave: "Sonderurlaub", school: "Berufsschule",
};
export const resourceKinds: Record<string, string> = { vehicle: "Fahrzeug", pump: "Pumpe", machine: "Maschine" };

export const checkTitles: Record<string, string> = {
	substrate: "Untergrund tragfähig, eben und sauber",
	edgeInsulation: "Randdämmstreifen vorhanden",
	heatingPressureTest: "Fußbodenheizung abgedrückt",
	access: "Zufahrt und Pumpenstandort",
	utilities: "Wasser und Strom vorhanden",
};
export const checkResults: Record<string, { text: string; tone: Tone }> = {
	ok: { text: "In Ordnung", tone: "Success" }, notOk: { text: "Mangel", tone: "Warning" }, notApplicable: { text: "Entfällt", tone: "None" },
};
export const binderNames: Record<string, string> = { cement: "Zementestrich (CT)", calciumSulfate: "Calciumsulfatestrich (CA)" };

/** Ergebnis der Erstbegehung in einem Wort, wie im Protokoll. */
export function inspectionResult(i: { notRequired: boolean; checks: Record<string, string> }): { text: string; tone: Tone } {
	if (i.notRequired) {
		return { text: "Nicht notwendig", tone: "None" };
	}
	return Object.values(i.checks).includes("notOk")
		? { text: "Mit Mängeln – Klärung nötig", tone: "Warning" }
		: { text: "Bestanden / freigegeben", tone: "Success" };
}

/** Bekannte Ereignisse des Protokolls. */
export const auditTexts: Record<string, string> = {
	"auth.login.succeeded": "Anmeldung", "auth.login.failed": "Anmeldung fehlgeschlagen", "auth.login.locked_out": "Konto vorübergehend gesperrt",
	"auth.logout": "Abmeldung", "auth.refresh.reused": "Wiederverwendetes Sitzungs-Token (Sitzung beendet)",
	"auth.password.changed": "Passwort geändert", "auth.session.revoked": "Sitzung beendet",
	"documents.approval_requested": "Freigabe angefordert", "documents.approved": "Dokument freigegeben", "documents.rejected": "Freigabe abgelehnt",
	"documents.approval_cancelled": "Freigabeanfrage zurückgezogen", "catalog.saved": "Stammdaten gespeichert", "catalog.deleted": "Stammdaten gelöscht",
	"access.role.saved": "Rolle gespeichert", "access.role.deleted": "Rolle gelöscht", "bauakte.report_printed": "Bauakte als PDF erstellt",
	"users.created": "Benutzer angelegt", "users.updated": "Benutzer geändert", "users.password_reset": "Passwort zurückgesetzt",
	"users.activated": "Benutzer entsperrt", "users.deactivated": "Benutzer gesperrt", "access.user_roles.changed": "Rollen geändert",
	"devices.registered": "Gerät registriert", "devices.attached": "Gerät verknüpft", "devices.enrollment_code_created": "Einrichtungscode erzeugt",
	"devices.enrollment_code_rejected": "Einrichtungscode abgelehnt", "devices.approved": "Gerät freigegeben", "devices.locked": "Gerät gesperrt",
	"devices.unlocked": "Gerät entsperrt", "devices.retired": "Gerät entfernt",
	"backup.run_requested": "Sicherung angefordert", "setup.completed": "Ersteinrichtung abgeschlossen",
	"bauakte.created": "Bauakte angelegt", "bauakte.updated": "Bauakte geändert", "bauakte.deleted": "Bauakte gelöscht",
};
export const auditGroups = [
	{ key: "auth.", text: "Anmeldung und Sitzungen" }, { key: "bauakte.", text: "Bauakten" }, { key: "users.", text: "Benutzer" },
	{ key: "access.", text: "Rollen und Rechte" }, { key: "devices.", text: "Geräte" }, { key: "backup.", text: "Sicherung" },
	{ key: "setup.", text: "Einrichtung" }, { key: "documents.", text: "Dokumentfreigaben" }, { key: "catalog.", text: "Stammdaten" },
];
export function auditTone(action: string): Tone {
	if (action.includes("failed") || action.includes("reused") || action.includes("locked")) return "Error";
	if (action.startsWith("users.") || action.startsWith("access.")) return "Warning";
	if (action.startsWith("auth.") || action.startsWith("bauakte.")) return "Information";
	return "None";
}

/** Auswahllisten aus Bezeichnungen. */
export function options<K extends string>(labels: Record<K, string | { text: string }>): { key: K; text: string }[] {
	return (Object.keys(labels) as K[]).map((key) => {
		const label = labels[key];
		return { key, text: typeof label === "string" ? label : label.text };
	});
}
