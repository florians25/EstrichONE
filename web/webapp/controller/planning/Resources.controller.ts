import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type { RouteMatch } from "../../util/router";
import { get, put } from "../../util/api";
import { isoDay } from "../../util/days";
import { crewLoad } from "../../util/utilization";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { options, resourceKinds } from "../../util/labels";
import { planningChanged, planningLookups } from "../../util/assignmentDialog";
import type { CrewResponse, DirectoryUser, PlanningRange, ResourceResponse } from "../../util/models";

const addTexts: Record<string, string> = { kolonnen: "Kolonne anlegen", geraete: "Fahrzeug oder Gerät hinzufügen" };
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
		this.local({ tab: "kolonnen", title: "Kolonnen", subtitle: "", addText: addTexts.kolonnen, crews: [], resources: [], busy: false });
	}

	onRouteMatched(match: RouteMatch): void {
		// Eigene Seiten: /planung/kolonnen und /planung/geraete (alt: /planung/ressourcen?reiter=geraete).
		const key = match.path.startsWith("/planung/geraete") || match.query.get("reiter") === "geraete" ? "geraete" : "kolonnen";
		this.model().setProperty("/tab", key);
		this.model().setProperty("/title", key === "geraete" ? "Geräte & Fahrzeuge" : "Kolonnen");
		this.model().setProperty("/subtitle", key === "geraete" ? "Fahrzeuge, Pumpen und Maschinen für die Einsatzplanung" : "Kolonnen mit Kolonnenführer und Mitgliedern");
		this.model().setProperty("/addText", addTexts[key]);
		void this.load();
	}


	private async load(): Promise<void> {
		this.model().setProperty("/busy", true);
		// Auslastung der laufenden Woche (Mo–Fr).
		const now = new Date();
		const monday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7));
		const week = Array.from({ length: 5 }, (_, i) => isoDay(new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + i)));
		const [lookups, people, range] = await Promise.all([
			this.run(() => planningLookups(true)),
			this.run(() => get<DirectoryUser[]>("directory/users")),
			get<PlanningRange>("planning/range", { from: week[0], to: week[4] }).catch(() => null),
		]);
		this.people = people ?? [];
		this.crewList = lookups?.crews ?? [];
		const m = this.model();
		m.setProperty("/crews", this.crewList.map((c) => ({ ...c, leaderName: this.name(c.leaderUserId), memberNames: c.memberUserIds.map((id) => this.name(id)).join(", ") || "–",
			load: crewLoad(c.id, range?.assignments ?? [], week) })));
		m.setProperty("/resources", (lookups?.resources ?? []).map((r) => ({ ...r, kindText: resourceKinds[r.kind] ?? r.kind })));
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
}
