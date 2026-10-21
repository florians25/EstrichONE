import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import { get, post } from "../../util/api";
import { absenceKinds } from "../../util/labels";
import { date } from "../../util/format";
import { openEmployeeForm } from "../../util/employeeForm";
import { canOnboard, openOnboarding } from "../../util/onboarding";
import { employmentTypes, qualificationKinds, type EmployeeListItem, type ExpiringQualification } from "../../util/planningModels";

const number = (v: number) => v.toLocaleString("de-DE", { maximumFractionDigits: 1 });

/** Personal › Personalstamm (C5): Liste mit Kolonne, Kontakt, Resturlaub, Abwesenheit heute, ablaufenden Nachweisen. @namespace estrichone.controller.staff */
export default class Staff extends BaseController {
	private items: EmployeeListItem[] = [];

	onInit(): void {
		this.local({ status: "active", search: "", busy: false, shown: [], expiring: [], expiringText: "", canManage: false });
	}

	onRouteMatched(): void {
		this.model().setProperty("/canManage", this.has("hr.manage"));
		void this.load();
	}

	onStatus(): void { void this.load(); }
	onRefresh(): void { void this.load(); }
	onFilter(): void { this.render(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const [items, expiring] = await Promise.all([
			this.run(() => get<EmployeeListItem[]>("staff/employees", { status: m.getProperty("/status") })),
			this.run(() => get<ExpiringQualification[]>("staff/qualifications/expiring")),
		]);
		this.items = items ?? [];
		m.setProperty("/expiring", expiring ?? []);
		const expired = (expiring ?? []).filter((x) => x.qualification.expired).length;
		m.setProperty("/expiringText", `${(expiring ?? []).length} Qualifikationen oder Führerscheine laufen in den nächsten 60 Tagen ab${expired ? ` (${expired} bereits abgelaufen)` : ""}.`);
		m.setProperty("/busy", false);
		this.render();
	}

	private render(): void {
		const q = String(this.model().getProperty("/search") ?? "").trim().toLowerCase();
		this.model().setProperty("/shown", this.items
			.filter((e) => !q || [e.displayName, e.personnelNumber, e.city, e.jobTitle, e.userName, ...e.crews.map((c) => c.name)].some((v) => v?.toLowerCase().includes(q)))
			.map((e) => ({
				...e,
				employmentText: employmentTypes[e.employmentType] ?? e.employmentType,
				crewText: e.crews.map((c) => c.name).join(", ") || "–",
				vacationText: number(e.vacationRemaining),
				todayText: !e.isActive ? (e.exitDate ? `ausgetreten ${date(e.exitDate)}` : "noch nicht eingetreten") : e.absentToday ? absenceKinds[e.absentToday] ?? "abwesend" : "verfügbar",
				todayState: !e.isActive ? "None" : e.absentToday === "sick" ? "Error" : e.absentToday ? "Warning" : "Success",
				rateText: e.hourlyRate != null ? `${e.hourlyRate.toLocaleString("de-DE", { minimumFractionDigits: 2 })} €` : "–",
			})));
	}

	onOpen(e: Event): void {
		const item = (e.getSource() as Control).getBindingContext("view")?.getObject() as EmployeeListItem;
		this.nav(`/personal/${item.id}`);
	}

	async onAdd(): Promise<void> {
		const saved = await openEmployeeForm(null);
		if (saved) {
			this.toast("Mitarbeiter angelegt");
			// Zweiter Schritt: direkt das Benutzerkonto mit Onboarding-PDF (abbrechbar, später auf der Mitarbeiterseite möglich).
			if (canOnboard()) await openOnboarding(saved).catch((error: unknown) => this.showError(error));
			this.nav(`/personal/${saved.id}`);
		}
	}

	async onImport(): Promise<void> {
		if (!(await this.confirm("Für alle aktiven Benutzerkonten ohne Personalstamm einen Mitarbeiter anlegen? Name, E-Mail und Telefon werden übernommen.",
			{ action: "Übernehmen" }))) return;
		const result = await this.run(() => post<{ created: number }>("staff/employees/import-users"));
		if (result) {
			this.toast(result.created === 0 ? "Alle Benutzer haben bereits einen Personalstamm." : `${result.created} Mitarbeiter angelegt`);
			await this.load();
		}
	}

	onExpiring(): void {
		const list = new List({
			items: (this.model().getProperty("/expiring") as ExpiringQualification[]).map((x) => new StandardListItem({
				title: `${x.employeeName}: ${x.qualification.name}`, description: qualificationKinds[x.qualification.kind],
				info: x.qualification.expired ? `abgelaufen ${date(x.qualification.expiresOn)}` : `bis ${date(x.qualification.expiresOn)}`,
				infoState: x.qualification.expired ? "Error" : "Warning", type: "Navigation",
				press: () => {
					dialog.close();
					this.nav(`/personal/${x.employeeId}?abschnitt=qualifikationen`);
				},
			})),
		});
		const dialog = new Dialog({
			title: "Ablaufende Nachweise", contentWidth: "32rem", content: [list],
			endButton: new Button({ text: "Schließen", press: () => dialog.close() }), afterClose: () => dialog.destroy(),
		});
		dialog.open();
	}
}
