import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, post, put } from "../../util/api";
import { openForm } from "../../util/forms";
import {
	describeRule, formatHolidays, parseHolidays, strategies, type CannedResponse, type HelpdeskAgent, type HelpdeskGroup, type HelpdeskRule,
	type HelpdeskHolidays, type HelpdeskSettings as Settings, type TicketSubcategory,
} from "../../util/helpdesk";
import { options, ticketCategories, ticketPriorities } from "../../util/labels";
import type { RouteMatch } from "../../util/router";

const days = [{ key: "1", text: "Montag" }, { key: "2", text: "Dienstag" }, { key: "3", text: "Mittwoch" }, { key: "4", text: "Donnerstag" },
	{ key: "5", text: "Freitag" }, { key: "6", text: "Samstag" }, { key: "7", text: "Sonntag" }];
const priorityOrder = ["urgent", "high", "normal", "low"];

/**
 * IT Center › Helpdesk-Einstellungen: Ziele je Priorität und Geschäftszeiten, Gruppen, Unterkategorien, Regeln der automatischen
 * Zuweisung (Recht „Helpdesk einrichten“) sowie Textbausteine (alle Bearbeiter).
 * @namespace estrichone.controller.admin
 */
export default class HelpdeskSettings extends BaseController {
	private agents: HelpdeskAgent[] = [];
	private saved: Settings | null = null;

	onInit(): void {
		const year = new Date().getFullYear();
		this.local({ busy: false, dirty: false, tab: "sla", canManage: false, s: null, days: [], dayOptions: days, holidays: "", holidayState: "None",
			holidayError: "", groups: [], subcategories: [], rules: [], responses: [], stateKey: "", states: [{ key: "", text: "Keins – nur zusätzliche Tage" }],
			holidayYear: String(year), holidayYears: [{ key: String(year) }, { key: String(year + 1) }], holidayDays: [], holidayEmpty: "" });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		this.model().setProperty("/canManage", this.has("helpdesk.manage"));
		const tab = match.query.get("reiter");
		if (tab) this.model().setProperty("/tab", tab);
		await this.load();
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const [settings, groups, subs, rules, responses, agents] = await Promise.all([
				get<Settings>("helpdesk/settings"), get<HelpdeskGroup[]>("helpdesk/groups"), get<TicketSubcategory[]>("helpdesk/subcategories"),
				get<HelpdeskRule[]>("helpdesk/rules"), get<CannedResponse[]>("helpdesk/responses"), get<HelpdeskAgent[]>("helpdesk/agents"),
			]);
			this.agents = agents;
			this.saved = settings;
			this.applySettings(settings);
			const people = new Map(agents.map((a) => [a.id, a.displayName]));
			m.setProperty("/groups", groups.map((g) => ({ ...g, memberText: g.memberUserIds.map((id) => people.get(id) ?? "ehemalige Person").join(", ") || "keine Mitglieder" })));
			m.setProperty("/subcategories", subs);
			const names = { categories: ticketCategories, subcategories: new Map(subs.map((s) => [s.id, s.name])), groups: new Map(groups.map((g) => [g.id, g.name])), people };
			m.setProperty("/rules", rules.map((r) => ({ ...r, ...describeRule(r, names) })));
			m.setProperty("/responses", responses.map((r) => ({ ...r, categoryText: r.category ? ticketCategories[r.category] : "" })));
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private applySettings(settings: Settings): void {
		const m = this.model();
		const copy = JSON.parse(JSON.stringify(settings)) as Settings;
		copy.targets.sort((a, b) => priorityOrder.indexOf(a.priority) - priorityOrder.indexOf(b.priority));
		m.setProperty("/s", copy);
		m.setProperty("/days", copy.hours.days.map(String));
		m.setProperty("/holidays", formatHolidays(copy.holidays));
		m.setProperty("/stateKey", copy.state ?? "");
		void this.loadHolidays();
		m.setProperty("/holidayState", "None");
		m.setProperty("/dirty", false);
	}

	/** Feiertage des gewählten Jahres als Vorschau (gesetzliche des gewählten Bundeslands und gespeicherte Zusatztage). */
	private async loadHolidays(): Promise<void> {
		const m = this.model();
		const state = String(m.getProperty("/stateKey") ?? "");
		const preview = await get<HelpdeskHolidays>("helpdesk/holidays", { year: m.getProperty("/holidayYear"), state: state || "-" }).catch(() => null);
		if (!preview) return;
		m.setProperty("/states", [{ key: "", text: "Keins – nur zusätzliche Tage" }, ...preview.states.map((x) => ({ key: x.code, text: x.name }))]);
		const day = (iso: string) => iso.split("-").reverse().slice(0, 2).join(".") + ".";
		m.setProperty("/holidayDays", preview.days.map((d) => ({ ...d, text: `${day(d.date)} ${d.name}` })));
		m.setProperty("/holidayEmpty", state ? "Keine Feiertage gefunden." : "Kein Bundesland gewählt: Es zählen nur die zusätzlichen Tage.");
	}

	onState(): void {
		this.onDirty();
		void this.loadHolidays();
	}

	onHolidayYear(): void { void this.loadHolidays(); }

	onDirty(): void {
		this.model().setProperty("/dirty", true);
	}

	onReset(): void {
		if (this.saved) this.applySettings(this.saved);
	}

	onHelpdesk(): void { this.nav("/it-center/helpdesk"); }

	async onSave(): Promise<void> {
		const m = this.model();
		const s = m.getProperty("/s") as Settings;
		const holidays = parseHolidays(String(m.getProperty("/holidays") ?? ""));
		if (holidays.invalid.length) {
			m.setProperty("/holidayState", "Error");
			m.setProperty("/holidayError", `Kein gültiges Datum: ${holidays.invalid.join(", ")}`);
			m.setProperty("/tab", "sla");
			return;
		}
		const body: Settings = { ...s, state: String(m.getProperty("/stateKey") ?? "") || null, hours: { ...s.hours, days: (m.getProperty("/days") as string[]).map(Number) }, holidays: holidays.dates,
			targets: s.targets.map((t) => ({ ...t, responseHours: Number(t.responseHours), resolutionHours: Math.max(Number(t.resolutionHours), Number(t.responseHours)) })) };
		const saved = await this.run(() => put<Settings>("helpdesk/settings", body), "Einstellungen gespeichert");
		if (saved) {
			this.saved = saved;
			this.applySettings(saved);
		}
	}

	private row<T>(event: Event): T {
		return (event.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	private agentOptions(): { key: string; text: string }[] {
		return this.agents.map((a) => ({ key: a.id, text: a.displayName }));
	}

	// ---- Gruppen ---------------------------------------------------------------------------------------------------

	private async editGroup(group?: HelpdeskGroup): Promise<void> {
		const saved = await openForm({
			title: group ? `Gruppe „${group.name}“` : "Gruppe anlegen", width: "34rem", submitText: "Speichern",
			intro: "Mitglieder sehen die Ansicht „Meine Gruppen“ und erhalten neue Tickets der Gruppe, wenn niemand zugewiesen ist.",
			initial: { name: group?.name ?? "", description: group?.description ?? "", members: group?.memberUserIds ?? [], isActive: group?.isActive ?? true,
				sortOrder: group?.sortOrder ?? 0 },
			fields: [
				{ key: "name", label: "Name", required: true, maxLength: 100, placeholder: "z. B. IT" },
				{ key: "description", label: "Zuständig für", maxLength: 500, placeholder: "z. B. Geräte, Apps, Zugänge" },
				{ key: "members", label: "Mitglieder", type: "multi", options: this.agentOptions(), help: "Nur Personen mit dem Recht „Supporttickets bearbeiten“." },
				{ key: "isActive", label: "Aktiv", type: "switch" },
				{ key: "sortOrder", label: "Reihenfolge", type: "integer" },
			],
			submit: (v) => {
				const body = { name: v.name, description: v.description || null, memberUserIds: v.members, isActive: !!v.isActive, sortOrder: v.sortOrder ?? 0 };
				return group ? put(`helpdesk/groups/${group.id}`, body) : post("helpdesk/groups", body);
			},
		});
		if (saved) await this.load();
	}

	onAddGroup(): void { void this.editGroup(); }

	onEditGroup(event: Event): void { void this.editGroup(this.row<HelpdeskGroup>(event)); }

	async onDeleteGroup(event: Event): Promise<void> {
		const group = this.row<HelpdeskGroup>(event);
		if (!(await this.confirm(`Gruppe „${group.name}“ löschen? Tickets behalten ihre Zuweisung, verlieren aber die Gruppe.`, { action: "Löschen", danger: true }))) return;
		await this.run(() => del(`helpdesk/groups/${group.id}`), "Gruppe gelöscht");
		await this.load();
	}

	// ---- Unterkategorien -------------------------------------------------------------------------------------------

	private async editSub(sub?: TicketSubcategory): Promise<void> {
		const saved = await openForm({
			title: sub ? `Unterkategorie „${sub.name}“` : "Unterkategorie anlegen", width: "30rem", submitText: "Speichern",
			initial: { category: sub?.category ?? "officeIt", name: sub?.name ?? "", isActive: sub?.isActive ?? true, sortOrder: sub?.sortOrder ?? 0 },
			fields: [
				{ key: "category", label: "Kategorie", type: "select", required: true, options: options(ticketCategories) },
				{ key: "name", label: "Name", required: true, maxLength: 100, placeholder: "z. B. Drucker" },
				{ key: "isActive", label: "Beim Melden anbieten", type: "switch" },
				{ key: "sortOrder", label: "Reihenfolge", type: "integer" },
			],
			submit: (v) => {
				const body = { category: v.category, name: v.name, isActive: !!v.isActive, sortOrder: v.sortOrder ?? 0 };
				return sub ? put(`helpdesk/subcategories/${sub.id}`, body) : post("helpdesk/subcategories", body);
			},
		});
		if (saved) await this.load();
	}

	onAddSub(): void { void this.editSub(); }

	onEditSub(event: Event): void { void this.editSub(this.row<TicketSubcategory>(event)); }

	async onDeleteSub(event: Event): Promise<void> {
		const sub = this.row<TicketSubcategory>(event);
		if (!(await this.confirm(`Unterkategorie „${sub.name}“ löschen? Bestehende Tickets behalten ihre Kategorie.`, { action: "Löschen", danger: true }))) return;
		await this.run(() => del(`helpdesk/subcategories/${sub.id}`), "Unterkategorie gelöscht");
		await this.load();
	}

	// ---- Regeln ----------------------------------------------------------------------------------------------------

	private async editRule(rule?: HelpdeskRule): Promise<void> {
		const m = this.model();
		const groups = (m.getProperty("/groups") as HelpdeskGroup[]).map((g) => ({ key: g.id, text: g.name }));
		const subs = (m.getProperty("/subcategories") as TicketSubcategory[]).map((s) => ({ key: s.id, text: `${ticketCategories[s.category]} › ${s.name}` }));
		const rules = m.getProperty("/rules") as HelpdeskRule[];
		const saved = await openForm({
			title: rule ? `Regel „${rule.name}“` : "Regel anlegen", width: "38rem", submitText: "Speichern",
			intro: "Neue Tickets durchlaufen die Regeln in der angegebenen Reihenfolge; die erste passende gilt. Leere Bedingungen passen immer.",
			initial: { name: rule?.name ?? "", category: rule?.category ?? "", subcategoryId: rule?.subcategoryId ?? "", keyword: rule?.keyword ?? "",
				groupId: rule?.groupId ?? "", strategy: rule?.strategy ?? "groupOnly", assigneeUserId: rule?.assigneeUserId ?? "", priority: rule?.priority ?? "",
				isActive: rule?.isActive ?? true, sortOrder: rule?.sortOrder ?? (rules.length ? Math.max(...rules.map((r) => r.sortOrder)) + 10 : 10) },
			fields: [
				{ key: "name", label: "Name", required: true, maxLength: 100, placeholder: "z. B. Drucker an die IT" },
				{ key: "s1", label: "Wenn", type: "section" },
				{ key: "category", label: "Kategorie", type: "select", options: [{ key: "", text: "beliebig" }, ...options(ticketCategories)] },
				{ key: "subcategoryId", label: "Unterkategorie", type: "select", options: [{ key: "", text: "beliebig" }, ...subs] },
				{ key: "keyword", label: "Text enthält", maxLength: 100, placeholder: "z. B. VPN (Titel oder Beschreibung)" },
				{ key: "s2", label: "Dann", type: "section" },
				{ key: "groupId", label: "Gruppe", type: "select", options: [{ key: "", text: "keine" }, ...groups] },
				{ key: "strategy", label: "Zuweisen", type: "select", required: true, options: options(strategies) },
				{ key: "assigneeUserId", label: "Person (bei fester Zuweisung)", type: "select", options: [{ key: "", text: "–" }, ...this.agentOptions()] },
				{ key: "priority", label: "Priorität setzen", type: "select", options: [{ key: "", text: "nicht ändern" }, ...options(ticketPriorities)],
					help: "Nur wenn der Melder keine Priorität gewählt hat." },
				{ key: "isActive", label: "Aktiv", type: "switch" },
				{ key: "sortOrder", label: "Reihenfolge", type: "integer" },
			],
			submit: (v) => {
				const body = { name: v.name, category: v.category || null, subcategoryId: v.subcategoryId || null, keyword: v.keyword || null, groupId: v.groupId || null,
					strategy: v.strategy, assigneeUserId: v.assigneeUserId || null, priority: v.priority || null, isActive: !!v.isActive, sortOrder: v.sortOrder ?? 0 };
				return rule ? put(`helpdesk/rules/${rule.id}`, body) : post("helpdesk/rules", body);
			},
		});
		if (saved) await this.load();
	}

	onAddRule(): void { void this.editRule(); }

	onEditRule(event: Event): void { void this.editRule(this.row<HelpdeskRule>(event)); }

	async onDeleteRule(event: Event): Promise<void> {
		const rule = this.row<HelpdeskRule>(event);
		if (!(await this.confirm(`Regel „${rule.name}“ löschen?`, { action: "Löschen", danger: true }))) return;
		await this.run(() => del(`helpdesk/rules/${rule.id}`), "Regel gelöscht");
		await this.load();
	}

	// ---- Textbausteine ---------------------------------------------------------------------------------------------

	private async editResponse(response?: CannedResponse): Promise<void> {
		const saved = await openForm({
			title: response ? `Textbaustein „${response.name}“` : "Textbaustein anlegen", width: "40rem", submitText: "Speichern",
			intro: "Platzhalter werden beim Einfügen ersetzt: {Melder}, {Vorname}, {Ticket}, {Titel}, {Bearbeiter}.",
			initial: { name: response?.name ?? "", text: response?.text ?? "", category: response?.category ?? "", isPersonal: response?.isPersonal ?? false,
				sortOrder: response?.sortOrder ?? 0 },
			fields: [
				{ key: "name", label: "Name", required: true, maxLength: 100, placeholder: "z. B. Bitte neu anmelden" },
				{ key: "text", label: "Text", type: "textarea", rows: 7, required: true, maxLength: 8000,
					placeholder: "z. B. Bitte melden Sie sich in der App einmal ab und wieder an. Danach sollte der Abgleich wieder laufen." },
				{ key: "category", label: "Passt zu Kategorie", type: "select", options: [{ key: "", text: "alle" }, ...options(ticketCategories)] },
				...(response ? [] : [{ key: "isPersonal", label: "Nur für mich", type: "checkbox" as const }]),
				{ key: "sortOrder", label: "Reihenfolge", type: "integer" },
			],
			submit: (v) => {
				const body = { name: v.name, text: v.text, category: v.category || null, isPersonal: !!v.isPersonal, sortOrder: v.sortOrder ?? 0 };
				return response ? put(`helpdesk/responses/${response.id}`, body) : post("helpdesk/responses", body);
			},
		});
		if (saved) await this.load();
	}

	onAddResponse(): void { void this.editResponse(); }

	onEditResponse(event: Event): void { void this.editResponse(this.row<CannedResponse>(event)); }

	async onDeleteResponse(event: Event): Promise<void> {
		const response = this.row<CannedResponse>(event);
		if (!(await this.confirm(`Textbaustein „${response.name}“ löschen?`, { action: "Löschen", danger: true }))) return;
		await this.run(() => del(`helpdesk/responses/${response.id}`), "Textbaustein gelöscht");
		await this.load();
	}
}
