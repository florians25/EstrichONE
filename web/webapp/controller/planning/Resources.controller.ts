import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type { RouteMatch } from "../../util/router";
import { del, get, put } from "../../util/api";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { addDays, isoDay } from "../../util/days";
import { date } from "../../util/format";
import { absenceKinds, options, resourceKinds } from "../../util/labels";
import { planningChanged, planningLookups } from "../../util/assignmentDialog";
import type { AbsenceView, CrewResponse, DirectoryUser, PlanningRange, ResourceResponse } from "../../util/models";

const tabs = ["kolonnen", "geraete", "abwesenheiten"];
const addTexts: Record<string, string> = { kolonnen: "Kolonne anlegen", geraete: "Fahrzeug oder Gerät hinzufügen", abwesenheiten: "Abwesenheit eintragen" };
/** Gut unterscheidbare Kolonnenfarben (auch in dunklen Themes lesbar). */
const crewColors = [
	{ key: "#0064D9", text: "Blau" }, { key: "#188918", text: "Grün" }, { key: "#E76500", text: "Orange" }, { key: "#AA0808", text: "Rot" },
	{ key: "#7800A4", text: "Violett" }, { key: "#00838F", text: "Petrol" }, { key: "#C0399F", text: "Magenta" }, { key: "#5D6D7E", text: "Grau" },
	{ key: "#8D6E00", text: "Ocker" }, { key: "#1B3B6F", text: "Dunkelblau" },
];

/** Planung › Kolonnen und Geräte: Kolonnen, Fahrzeuge/Geräte, Abwesenheiten. @namespace estrichone.controller.planning */
export default class Resources extends BaseController {
	private people: DirectoryUser[] = [];
	private crewList: CrewResponse[] = [];

	onInit(): void {
		this.local({ tab: "kolonnen", addText: addTexts.kolonnen, crews: [], resources: [], absences: [], busy: false });
	}

	onRouteMatched(match: RouteMatch): void {
		const tab = match.query.get("reiter") ?? "";
		const key = tabs.includes(tab) ? tab : "kolonnen";
		this.model().setProperty("/tab", key);
		this.model().setProperty("/addText", addTexts[key]);
		void this.load();
	}

	onTab(e: Event): void {
		const key = String(e.getParameter("key" as never));
		this.model().setProperty("/addText", addTexts[key]);
		this.setQuery({ reiter: key === "kolonnen" ? null : key });
	}

	private async load(): Promise<void> {
		this.model().setProperty("/busy", true);
		const today = isoDay(new Date());
		const [lookups, people, range] = await Promise.all([
			this.run(() => planningLookups(true)),
			this.run(() => get<DirectoryUser[]>("directory/users")),
			this.run(() => get<PlanningRange>("planning/range", { from: today, to: addDays(today, 60) })),
		]);
		this.people = people ?? [];
		this.crewList = lookups?.crews ?? [];
		const m = this.model();
		m.setProperty("/crews", this.crewList.map((c) => ({ ...c, leaderName: this.name(c.leaderUserId), memberNames: c.memberUserIds.map((id) => this.name(id)).join(", ") || "–" })));
		m.setProperty("/resources", (lookups?.resources ?? []).map((r) => ({ ...r, kindText: resourceKinds[r.kind] ?? r.kind })));
		m.setProperty("/absences", (range?.absences ?? []).map((a) => ({ ...a, kindText: absenceKinds[a.kind] ?? a.kind, rangeText: `${date(a.from)} – ${date(a.to)}` })));
		m.setProperty("/busy", false);
	}

	private name(id: string | null): string {
		return (id && this.people.find((p) => p.id === id)?.displayName) || "–";
	}

	private row<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	private activePeople() {
		return this.people.filter((p) => p.isActive).map((p) => ({ key: p.id, text: p.displayName }));
	}

	private async saved(message: string): Promise<void> {
		planningChanged();
		this.toast(message);
		await this.load();
	}

	onAdd(): void {
		const tab = String(this.model().getProperty("/tab"));
		if (tab === "geraete") void this.editResource(null);
		else if (tab === "abwesenheiten") void this.addAbsence();
		else void this.editCrew(null);
	}

	onEditCrew(e: Event): void { void this.editCrew(this.row<CrewResponse>(e)); }
	onEditResource(e: Event): void { void this.editResource(this.row<ResourceResponse>(e)); }

	private putCrew(id: string, c: Pick<CrewResponse, "name" | "leaderUserId" | "memberUserIds" | "color" | "isActive" | "sortOrder">) {
		return put(`planning/crews/${id}`, { name: c.name, leaderUserId: c.leaderUserId, memberUserIds: c.memberUserIds, color: c.color, isActive: c.isActive, sortOrder: c.sortOrder });
	}

	private async editCrew(crew: CrewResponse | null): Promise<void> {
		const ok = await openForm<boolean>({
			title: crew ? "Kolonne bearbeiten" : "Kolonne anlegen", submitText: "Speichern",
			initial: { name: crew?.name ?? "", leaderUserId: crew?.leaderUserId ?? "", memberUserIds: crew?.memberUserIds ?? [], color: crew?.color ?? crewColors[this.crewList.length % crewColors.length].key },
			fields: [
				{ key: "name", label: "Name der Kolonne", required: true, placeholder: "z. B. Kolonne Nord", maxLength: 100 },
				{ key: "leaderUserId", label: "Kolonnenführer", type: "select", options: [{ key: "", text: "–" }, ...this.activePeople()] },
				{ key: "memberUserIds", label: "Mitglieder", type: "multi", options: this.activePeople() },
				{ key: "color", label: "Farbe in der Plantafel", type: "select", options: crewColors },
			],
			submit: async (v) => {
				await this.putCrew(crew?.id ?? uuidv7(), {
					name: String(v.name).trim(), leaderUserId: (v.leaderUserId as string) || null, memberUserIds: (v.memberUserIds as string[]) ?? [],
					color: (v.color as string) || null, isActive: crew?.isActive ?? true, sortOrder: crew?.sortOrder ?? this.crewList.length + 1,
				});
				return true;
			},
		});
		if (ok) await this.saved("Kolonne gespeichert");
	}

	async onToggleCrew(e: Event): Promise<void> {
		const c = this.row<CrewResponse>(e);
		const done = await this.run(async () => { await this.putCrew(c.id, { ...c, isActive: !c.isActive }); return true; });
		if (done !== undefined) await this.saved(c.isActive ? "Kolonne deaktiviert" : "Kolonne aktiviert");
	}

	private putResource(id: string, r: Pick<ResourceResponse, "kind" | "name" | "identifier" | "notes" | "isActive">) {
		return put(`planning/resources/${id}`, { kind: r.kind, name: r.name, identifier: r.identifier, notes: r.notes, isActive: r.isActive });
	}

	private async editResource(r: ResourceResponse | null): Promise<void> {
		const ok = await openForm<boolean>({
			title: r ? "Fahrzeug oder Gerät bearbeiten" : "Fahrzeug oder Gerät hinzufügen", submitText: "Speichern",
			initial: { kind: r?.kind ?? "pump", name: r?.name ?? "", identifier: r?.identifier ?? "", notes: r?.notes ?? "" },
			fields: [
				{ key: "kind", label: "Art", type: "select", options: options(resourceKinds) },
				{ key: "name", label: "Bezeichnung", required: true, placeholder: "z. B. Mixokret 2", maxLength: 100 },
				{ key: "identifier", label: "Kennzeichen oder Seriennummer", maxLength: 100 },
				{ key: "notes", label: "Notiz", type: "textarea", rows: 2 },
			],
			submit: async (v) => {
				await this.putResource(r?.id ?? uuidv7(), {
					kind: v.kind as ResourceResponse["kind"], name: String(v.name).trim(), identifier: (v.identifier as string) || null,
					notes: (v.notes as string) || null, isActive: r?.isActive ?? true,
				});
				return true;
			},
		});
		if (ok) await this.saved("Gespeichert");
	}

	async onToggleResource(e: Event): Promise<void> {
		const r = this.row<ResourceResponse>(e);
		const done = await this.run(async () => { await this.putResource(r.id, { ...r, isActive: !r.isActive }); return true; });
		if (done !== undefined) await this.saved(r.isActive ? "Außer Betrieb gesetzt" : "Wieder verfügbar");
	}

	private async addAbsence(): Promise<void> {
		const today = isoDay(new Date());
		const ok = await openForm<boolean>({
			title: "Abwesenheit eintragen", submitText: "Eintragen", initial: { kind: "vacation", from: today, to: today },
			fields: [
				{ key: "userId", label: "Mitarbeiter", type: "combo", required: true, options: this.activePeople() },
				{ key: "kind", label: "Art", type: "select", options: options(absenceKinds) },
				{ key: "from", label: "Von", type: "date", required: true },
				{ key: "to", label: "Bis", type: "date", required: true, validate: (v, all) => (String(v) < String(all.from) ? "Das Ende liegt vor dem Beginn." : null) },
				{ key: "note", label: "Notiz (optional)", maxLength: 200 },
			],
			submit: async (v) => {
				await put(`planning/absences/${uuidv7()}`, { userId: v.userId, kind: v.kind, from: v.from, to: v.to, note: (v.note as string) || null });
				return true;
			},
		});
		if (ok) await this.saved("Abwesenheit eingetragen");
	}

	async onRemoveAbsence(e: Event): Promise<void> {
		const a = this.row<AbsenceView>(e);
		if (!(await this.confirm(`Abwesenheit von ${a.userName} (${date(a.from)} – ${date(a.to)}) entfernen?`, { action: "Entfernen", danger: true }))) return;
		const done = await this.run(async () => { await del(`planning/absences/${a.id}`); return true; });
		if (done !== undefined) await this.saved("Abwesenheit entfernt");
	}
}
