import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type ObjectPageLayout from "sap/uxap/ObjectPageLayout";
import type ObjectPageSection from "sap/uxap/ObjectPageSection";
import { del, get, put } from "../../util/api";
import { absenceKinds } from "../../util/labels";
import { date } from "../../util/format";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { openEmployeeForm } from "../../util/employeeForm";
import { fileUrl, uploadLoose } from "../../util/looseUpload";
import { planningChanged, planningLookups } from "../../util/assignmentDialog";
import type { RouteMatch } from "../../util/router";
import {
	documentCategoriesStaff, employmentTypes, licenseClasses, qualificationKinds, weekdays,
	type EmployeeDetail, type EmployeeDocumentView, type EquipmentView, type QualificationView,
} from "../../util/planningModels";
import type { AbsenceView } from "../../util/models";

const sections: Record<string, string> = {
	uebersicht: "secOverview", kolonnen: "secCrews", qualifikationen: "secQualifications", abwesenheiten: "secAbsences", geraete: "secEquipment", dokumente: "secDocuments",
};
const num = (v: number | null | undefined) => (v == null ? "–" : v.toLocaleString("de-DE", { maximumFractionDigits: 1 }));

/** Personal › Mitarbeiter (C5): Personalakte mit allen Abschnitten. @namespace estrichone.controller.staff */
export default class Employee extends BaseController {
	private id = "";

	onInit(): void {
		this.local({ e: null, busy: false, headline: "", statusText: "", statusState: "None", crewText: "", address: "", employmentText: "", periodText: "",
			workText: "", rateText: "", vacation: {}, qualifications: [], absences: [], equipment: [], documents: [] });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		this.id = match.params.id;
		await this.load();
		const section = sections[match.query.get("abschnitt") ?? ""];
		if (section) (this.byId("objectPage") as ObjectPageLayout).setSelectedSection(this.byId(section) as ObjectPageSection);
	}

	onSection(e: Event): void {
		const section = e.getParameter("section" as never) as ObjectPageSection;
		const key = Object.entries(sections).find(([, id]) => this.byId(id) === section)?.[0];
		if (key) this.setQuery({ abschnitt: key === "uebersicht" ? null : key });
	}

	private async load(): Promise<void> {
		this.model().setProperty("/busy", true);
		const e = await this.run(() => get<EmployeeDetail>(`staff/employees/${this.id}`));
		this.model().setProperty("/busy", false);
		if (!e) {
			this.nav("/personal", true);
			return;
		}
		this.apply(e);
	}

	private apply(e: EmployeeDetail): void {
		const m = this.model();
		const today = new Date().toISOString().slice(0, 10);
		m.setProperty("/e", e);
		m.setProperty("/headline", [e.jobTitle, employmentTypes[e.employmentType], e.personnelNumber ? `Nr. ${e.personnelNumber}` : null].filter(Boolean).join(" · "));
		const away = e.absences.find((a) => a.from <= today && today <= a.to);
		m.setProperty("/statusText", !e.isActive ? (e.exitDate && e.exitDate < today ? `Ausgetreten am ${date(e.exitDate)}` : `Eintritt am ${date(e.entryDate)}`)
			: away ? `${absenceKinds[away.kind] ?? "Abwesend"} bis ${date(away.to)}` : "Aktiv");
		m.setProperty("/statusState", !e.isActive ? "None" : away ? (away.kind === "sick" ? "Error" : "Warning") : "Success");
		m.setProperty("/crewText", e.crews.map((c) => c.name).join(", ") || "–");
		m.setProperty("/address", [e.street, [e.postalCode, e.city].filter(Boolean).join(" ")].filter(Boolean).join(", ") || "–");
		m.setProperty("/employmentText", employmentTypes[e.employmentType] ?? e.employmentType);
		m.setProperty("/periodText", `${e.entryDate ? date(e.entryDate) : "–"} / ${e.exitDate ? date(e.exitDate) : "–"}${e.probationEnd ? ` · Probezeit bis ${date(e.probationEnd)}` : ""}`);
		const days = weekdays.filter((w) => e.workDays.includes(w.key)).map((w) => w.short).join(", ");
		m.setProperty("/workText", [e.workTimeModel, e.weeklyHours != null ? `${num(e.weeklyHours)} h/Woche` : null, days].filter(Boolean).join(" · ") || "–");
		m.setProperty("/rateText", e.hourlyRate != null ? `${e.hourlyRate.toLocaleString("de-DE", { minimumFractionDigits: 2 })} € je Stunde` : "–");
		m.setProperty("/vacation", {
			entitlement: num(e.vacation.entitlement), carryOver: num(e.vacation.carryOver), taken: num(e.vacation.taken), planned: num(e.vacation.planned),
			remaining: num(e.vacation.remaining), sickDays: num(e.vacation.sickDays),
		});
		m.setProperty("/qualifications", e.qualifications.map((q) => ({
			...q, kindText: qualificationKinds[q.kind] ?? q.kind,
			expiryText: q.expiresOn ? (q.expired ? `abgelaufen ${date(q.expiresOn)}` : date(q.expiresOn)) : "unbefristet",
			expiryState: q.expired ? "Error" : q.expiresSoon ? "Warning" : "None",
		})));
		const workdays = (from: string, to: string) => {
			let n = 0;
			for (let d = new Date(`${from}T00:00:00`); d <= new Date(`${to}T00:00:00`); d.setDate(d.getDate() + 1)) {
				if (e.workDays.includes(weekdays[(d.getDay() + 6) % 7].key)) n++;
			}
			return n;
		};
		m.setProperty("/absences", e.absences.map((a) => ({ ...a, kindText: absenceKinds[a.kind] ?? a.kind, rangeText: `${date(a.from)} – ${date(a.to)}`, days: workdays(a.from, a.to) })));
		m.setProperty("/equipment", e.equipment);
		m.setProperty("/documents", e.documents.map((d) => ({
			...d, categoryText: documentCategoriesStaff[d.category] ?? d.category,
			fileText: [d.fileName, d.sizeBytes ? `${Math.max(1, Math.round(d.sizeBytes / 1024)).toLocaleString("de-DE")} KB` : null].filter(Boolean).join(" · "),
		})));
		document.title = `${e.displayName} – EstrichONE`;
	}

	private row<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	private editing = false;

	async onEdit(): Promise<void> {
		// Nicht doppelt öffnen; Fehler beim Laden der Auswahllisten sichtbar machen statt still kein Formular zu zeigen.
		if (this.editing) return;
		this.editing = true;
		let saved: EmployeeDetail | null = null;
		try {
			saved = await openEmployeeForm(this.model().getProperty("/e") as EmployeeDetail);
		} catch (error) {
			this.showError(error);
		} finally {
			this.editing = false;
		}
		if (saved) {
			this.apply(saved);
			this.toast("Gespeichert");
			planningChanged();
		}
	}

	async onDelete(): Promise<void> {
		const e = this.model().getProperty("/e") as EmployeeDetail;
		if (!(await this.confirm(`${e.displayName} mit Qualifikationen, Geräten und Dokumentverweisen aus dem Personalstamm löschen? Das Benutzerkonto bleibt erhalten.`,
			{ action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`staff/employees/${e.id}`); return true; })) {
			this.toast("Mitarbeiter gelöscht");
			this.nav("/personal", true);
		}
	}

	async onCrews(): Promise<void> {
		const e = this.model().getProperty("/e") as EmployeeDetail;
		const { crews } = await planningLookups(true);
		const result = await openForm<unknown>({
			title: "Kolonnen", width: "28rem", initial: { crewIds: e.crews.map((c) => c.id) },
			fields: [{ key: "crewIds", label: "Kolonnen", type: "multi", options: crews.filter((c) => c.isActive).map((c) => ({ key: c.id, text: c.name })) }],
			submit: (v) => put(`staff/employees/${e.id}/crews`, { crewIds: v.crewIds ?? [] }),
		});
		if (result) {
			this.toast("Kolonnen gespeichert");
			planningChanged();
			await planningLookups(true);
			await this.load();
		}
	}

	// ---- Qualifikationen ----------------------------------------------------------------------------------------

	private async qualification(q: QualificationView | null): Promise<void> {
		const e = this.model().getProperty("/e") as EmployeeDetail;
		const saved = await openForm<QualificationView>({
			title: q ? "Qualifikation bearbeiten" : "Qualifikation hinzufügen", width: "32rem",
			initial: q ? { ...q } : { kind: "driverLicense" },
			fields: [
				{ key: "kind", label: "Art", type: "select", required: true, options: Object.entries(qualificationKinds).map(([key, text]) => ({ key, text })) },
				{ key: "name", label: "Bezeichnung", required: true, maxLength: 100, suggestions: [...licenseClasses, "Staplerschein", "Erste Hilfe", "G25 Fahrtätigkeit", "Ladungssicherung", "Fließestrich-Schulung"],
					placeholder: "z. B. C1E, Staplerschein, Erste Hilfe" },
				{ key: "number", label: "Nummer", maxLength: 50 },
				{ key: "issuedOn", label: "Ausgestellt am", type: "date" },
				{ key: "expiresOn", label: "Gültig bis", type: "date", help: "Leer = unbefristet. Ablauf in den nächsten 60 Tagen wird markiert.",
					validate: (v, all) => (v && all.issuedOn && String(v) < String(all.issuedOn) ? "Gültig bis liegt vor dem Ausstellungsdatum." : null) },
				{ key: "notes", label: "Notiz", type: "textarea", rows: 2, maxLength: 500 },
			],
			submit: (v) => put<QualificationView>(`staff/employees/${e.id}/qualifications/${q?.id ?? uuidv7()}`, {
				kind: v.kind, name: v.name, number: v.number || null, issuedOn: v.issuedOn || null, expiresOn: v.expiresOn || null, notes: v.notes || null,
			}),
		});
		if (saved) {
			this.toast("Qualifikation gespeichert");
			await this.load();
		}
	}

	onAddQualification(): void { void this.qualification(null); }
	onEditQualification(e: Event): void { void this.qualification(this.row<QualificationView>(e)); }

	async onDeleteQualification(e: Event): Promise<void> {
		const q = this.row<QualificationView>(e);
		if (!(await this.confirm(`„${q.name}“ löschen?`, { action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`staff/employees/${this.id}/qualifications/${q.id}`); return true; })) await this.load();
	}

	// ---- Abwesenheiten ------------------------------------------------------------------------------------------

	async onAddAbsence(): Promise<void> {
		const saved = await openForm<AbsenceView>({
			title: "Abwesenheit eintragen", width: "30rem", initial: { kind: "vacation" },
			fields: [
				{ key: "kind", label: "Art", type: "select", required: true, options: Object.entries(absenceKinds).map(([key, text]) => ({ key, text })) },
				{ key: "from", label: "Von", type: "date", required: true },
				{ key: "to", label: "Bis einschließlich", type: "date", required: true,
					validate: (v, all) => (v && all.from && String(v) < String(all.from) ? "Das Ende liegt vor dem Beginn." : null) },
				{ key: "note", label: "Notiz", maxLength: 500 },
			],
			submit: (v) => put<AbsenceView>(`staff/employees/${this.id}/absences/${uuidv7()}`, { kind: v.kind, from: v.from, to: v.to, note: v.note || null }),
		});
		if (saved) {
			this.toast("Abwesenheit eingetragen – erscheint in der Planung");
			planningChanged();
			await this.load();
		}
	}

	async onDeleteAbsence(e: Event): Promise<void> {
		const a = this.row<AbsenceView>(e);
		if (!(await this.confirm("Abwesenheit löschen?", { action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`staff/employees/${this.id}/absences/${a.id}`); return true; })) {
			planningChanged();
			await this.load();
		}
	}

	// ---- Geräte und Fahrzeuge -----------------------------------------------------------------------------------

	private async equipment(item: EquipmentView | null): Promise<void> {
		const { resources } = await planningLookups();
		const saved = await openForm<EquipmentView>({
			title: item ? "Gerät bearbeiten" : "Gerät oder Fahrzeug ausgeben", width: "32rem",
			initial: item ? { ...item } : { issuedOn: new Date().toISOString().slice(0, 10) },
			fields: [
				{ key: "resourceId", label: "Aus Fahrzeugen und Geräten", type: "select", placeholder: "– anderes Gerät –",
					options: resources.map((r) => ({ key: r.id, text: r.name, additionalText: r.identifier ?? "" })) },
				{ key: "name", label: "Bezeichnung", maxLength: 100, placeholder: "z. B. Diensthandy, Laser, Schlüssel Halle",
					validate: (v, all) => (!v && !all.resourceId ? "Bitte ein Gerät wählen oder eine Bezeichnung eingeben." : null) },
				{ key: "serialNumber", label: "Kennzeichen / Seriennummer", maxLength: 100 },
				{ key: "issuedOn", label: "Ausgegeben am", type: "date" },
				{ key: "returnedOn", label: "Zurückgegeben am", type: "date" },
				{ key: "notes", label: "Notiz", maxLength: 500 },
			],
			submit: (v) => put<EquipmentView>(`staff/employees/${this.id}/equipment/${item?.id ?? uuidv7()}`, {
				resourceId: v.resourceId || null, name: v.name || null, serialNumber: v.serialNumber || null, issuedOn: v.issuedOn || null,
				returnedOn: v.returnedOn || null, notes: v.notes || null,
			}),
		});
		if (saved) {
			this.toast("Gespeichert");
			await this.load();
		}
	}

	onAddEquipment(): void { void this.equipment(null); }
	onEditEquipment(e: Event): void { void this.equipment(this.row<EquipmentView>(e)); }

	async onDeleteEquipment(e: Event): Promise<void> {
		const item = this.row<EquipmentView>(e);
		if (!(await this.confirm(`„${item.name}“ löschen?`, { action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`staff/employees/${this.id}/equipment/${item.id}`); return true; })) await this.load();
	}

	// ---- Dokumente ----------------------------------------------------------------------------------------------

	onUpload(): void {
		const input = document.createElement("input");
		input.type = "file";
		input.accept = ".pdf,.jpg,.jpeg,.png,.heic,.doc,.docx,.xls,.xlsx";
		input.addEventListener("change", () => {
			const file = input.files?.[0];
			if (file) void this.document(file);
		});
		input.click();
	}

	private async document(file: File): Promise<void> {
		const saved = await openForm<EmployeeDocumentView>({
			title: "Personaldokument ablegen", width: "32rem", intro: `Datei: ${file.name}`,
			initial: { title: file.name.replace(/\.[^.]+$/, ""), category: "contract" },
			fields: [
				{ key: "title", label: "Titel", required: true, maxLength: 200 },
				{ key: "category", label: "Kategorie", type: "select", required: true, options: Object.entries(documentCategoriesStaff).map(([key, text]) => ({ key, text })) },
				{ key: "validUntil", label: "Gültig bis", type: "date" },
			],
			submitText: "Hochladen",
			submit: async (v) => {
				const fileId = await uploadLoose(file);
				return put<EmployeeDocumentView>(`staff/employees/${this.id}/documents/${uuidv7()}`, {
					fileId, category: v.category, title: v.title, validUntil: v.validUntil || null,
				});
			},
		});
		if (saved) {
			this.toast("Dokument abgelegt");
			await this.load();
		}
	}

	onOpenDocument(e: Event): void {
		window.open(fileUrl(this.row<EmployeeDocumentView>(e).fileId), "_blank", "noopener");
	}

	async onDeleteDocument(e: Event): Promise<void> {
		const d = this.row<EmployeeDocumentView>(e);
		if (!(await this.confirm(`„${d.title}“ aus der Personalakte entfernen?`, { action: "Entfernen", danger: true }))) return;
		if (await this.run(async () => { await del(`staff/employees/${this.id}/documents/${d.id}`); return true; })) await this.load();
	}
}
