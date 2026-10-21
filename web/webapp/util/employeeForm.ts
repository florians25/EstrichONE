/** Personalstamm: Mitarbeiter anlegen oder bearbeiten (alle Stammdaten in einem gegliederten Dialog). */
import { put } from "./api";
import { openForm } from "./forms";
import { uuidv7 } from "./ids";
import { session } from "./session";
import { employmentTypes, weekdays, type EmployeeDetail } from "./planningModels";

const workTimeModels = ["Vollzeit 40 h (Mo–Fr)", "Vollzeit 39 h (Mo–Fr)", "Teilzeit", "Saison (März–November)", "Minijob", "Gleitzeit", "4-Tage-Woche"];

export async function openEmployeeForm(existing: EmployeeDetail | null): Promise<EmployeeDetail | null> {
	const people = (await session.people()).filter((p) => p.isActive || p.id === existing?.userId);
	return openForm<EmployeeDetail>({
		title: existing ? `${existing.displayName} bearbeiten` : "Mitarbeiter anlegen",
		width: "44rem",
		initial: existing ? { ...existing, workDays: existing.workDays } : {
			employmentType: "fullTime", vacationDaysPerYear: 30, vacationCarryOver: 0, weeklyHours: 40,
			workDays: ["monday", "tuesday", "wednesday", "thursday", "friday"], workTimeModel: "Vollzeit 40 h (Mo–Fr)",
		},
		fields: [
			{ key: "s1", label: "Person", type: "section" },
			{ key: "firstName", label: "Vorname", maxLength: 100 },
			{ key: "lastName", label: "Nachname", required: true, maxLength: 100 },
			{ key: "personnelNumber", label: "Personalnummer", maxLength: 20 },
			{ key: "jobTitle", label: "Tätigkeit", maxLength: 100, suggestions: ["Estrichleger", "Kolonnenführer", "Bauleiter", "Helfer", "Auszubildender", "Büro", "Fahrer"] },
			{ key: "employmentType", label: "Beschäftigung", type: "select", required: true, options: Object.entries(employmentTypes).map(([key, text]) => ({ key, text })) },
			{ key: "birthDate", label: "Geburtsdatum", type: "date" },
			{ key: "s2", label: "Kontakt und Anschrift", type: "section" },
			{ key: "mobile", label: "Handy", maxLength: 50 },
			{ key: "phone", label: "Telefon", maxLength: 50 },
			{ key: "email", label: "E-Mail", type: "email", maxLength: 320 },
			{ key: "street", label: "Straße", maxLength: 200 },
			{ key: "postalCode", label: "PLZ", maxLength: 10 },
			{ key: "city", label: "Ort", maxLength: 100 },
			{ key: "s3", label: "Notfallkontakt", type: "section" },
			{ key: "emergencyContactName", label: "Name", maxLength: 200 },
			{ key: "emergencyContactPhone", label: "Telefon", maxLength: 50 },
			{ key: "emergencyContactRelation", label: "Beziehung", maxLength: 50, suggestions: ["Ehefrau", "Ehemann", "Partner/in", "Mutter", "Vater", "Sohn", "Tochter"] },
			{ key: "s4", label: "Beschäftigung, Arbeitszeit und Urlaub", type: "section" },
			{ key: "entryDate", label: "Eintritt", type: "date" },
			{ key: "probationEnd", label: "Ende der Probezeit", type: "date", help: "Erinnerung unter Urlaub und Abwesenheiten → Fristen." },
			{ key: "exitDate", label: "Austritt", type: "date",
				validate: (v, all) => (v && all.entryDate && String(v) < String(all.entryDate) ? "Der Austritt liegt vor dem Eintritt." : null) },
			{ key: "workTimeModel", label: "Arbeitszeitmodell", maxLength: 100, suggestions: workTimeModels },
			{ key: "weeklyHours", label: "Wochenstunden", type: "number", min: 0, max: 80, unit: "h" },
			{ key: "workDays", label: "Arbeitstage", type: "multi", options: weekdays.map((w) => ({ key: w.key, text: w.text })),
				help: "Zählt für Urlaubs- und Krankheitstage." },
			...(existing === null || existing.canManage ? [{ key: "hourlyRate", label: "Stundensatz", type: "number" as const, min: 0, max: 1000, unit: "€/h" }] : []),
			{ key: "vacationDaysPerYear", label: "Urlaubsanspruch je Jahr", type: "number", required: true, min: 0, max: 60, unit: "Tage" },
			{ key: "vacationCarryOver", label: "Resturlaub aus dem Vorjahr", type: "number", min: -60, max: 120, unit: "Tage" },
			{ key: "s5", label: "Benutzerkonto", type: "section" },
			{ key: "userId", label: "Verknüpftes Konto", type: "select", placeholder: "kein Benutzerkonto",
				options: people.map((p) => ({ key: p.id, text: p.displayName })),
				help: "Mit Konto ist der Mitarbeiter in Kolonnen, Planung, Abwesenheiten und in der App verfügbar." },
			{ key: "notes", label: "Notizen", type: "textarea", rows: 3, maxLength: 4000 },
		],
		submit: (v) => put<EmployeeDetail>(`staff/employees/${existing?.id ?? uuidv7()}`, {
			firstName: v.firstName || null, lastName: v.lastName, personnelNumber: v.personnelNumber || null, jobTitle: v.jobTitle || null,
			employmentType: v.employmentType, userId: v.userId || null, email: v.email || null, phone: v.phone || null, mobile: v.mobile || null,
			street: v.street || null, postalCode: v.postalCode || null, city: v.city || null, birthDate: v.birthDate || null,
			emergencyContactName: v.emergencyContactName || null, emergencyContactPhone: v.emergencyContactPhone || null,
			emergencyContactRelation: v.emergencyContactRelation || null, entryDate: v.entryDate || null, exitDate: v.exitDate || null,
			workTimeModel: v.workTimeModel || null, weeklyHours: v.weeklyHours ?? null, workDays: v.workDays ?? [],
			hourlyRate: existing && !existing.canManage ? existing.hourlyRate : v.hourlyRate ?? null,
			probationEnd: v.probationEnd || null, vacationDaysPerYear: v.vacationDaysPerYear ?? 30, vacationCarryOver: v.vacationCarryOver ?? 0, notes: v.notes || null,
		}),
	});
}
