import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type { RouteMatch } from "../../util/router";
import { get, post, put } from "../../util/api";
import MessageBox from "sap/m/MessageBox";
import { session } from "../../util/session";
import { chatDeepLink, crewLabel, type TeamsCrewChat, type TeamsCrewChats, type TeamsPerson } from "../../util/teams";
import { openTeamsUrl, teamsPeople } from "../../util/teamsChat";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import { fileUrl, uploadLoose } from "../../util/looseUpload";
import { isoDay } from "../../util/days";
import { crewLoad } from "../../util/utilization";
import { openForm } from "../../util/forms";
import { numberField } from "../../util/numberField";
import { uuidv7 } from "../../util/ids";
import { options, resourceKinds } from "../../util/labels";
import { planningChanged, planningLookups } from "../../util/assignmentDialog";
import type { CrewResponse, DirectoryUser, PlanningRange, ResourceResponse } from "../../util/models";

/** Fälligkeit: überfällig (rot), in 30 Tagen (gelb), sonst Datum. */
const due = (d: string | null | undefined) => {
	if (!d) return { text: "–", state: "None" };
	const days = Math.round((new Date(`${d}T00:00:00`).getTime() - new Date(new Date().toDateString()).getTime()) / 86_400_000);
	return { text: `${d.slice(8, 10)}.${d.slice(5, 7)}.${d.slice(0, 4)}${days < 0 ? " (überfällig)" : days <= 30 ? ` (in ${days} T.)` : ""}`,
		state: days < 0 ? "Error" : days <= 30 ? "Warning" : "None" };
};

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
	private teamsPeopleList: TeamsPerson[] = [];

	onInit(): void {
		this.local({ teamsCrews: { available: false }, tab: "kolonnen", title: "Kolonnen", subtitle: "", addText: addTexts.kolonnen, crews: [], resources: [], busy: false, deviceTab: "fahrzeuge", vehicles: [], machines: [] });
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
		const teamsOn = session.data.teams?.available === true;
		const [lookups, people, range, teamsCrews, teamsPeopleList] = await Promise.all([
			this.run(() => planningLookups(true)),
			this.run(() => get<DirectoryUser[]>("directory/users")),
			get<PlanningRange>("planning/range", { from: week[0], to: week[4] }).catch(() => null),
			teamsOn ? get<TeamsCrewChats>("teams/crews").catch(() => null) : Promise.resolve(null),
			teamsOn ? teamsPeople() : Promise.resolve([] as TeamsPerson[]),
		]);
		this.teamsPeopleList = teamsPeopleList;
		const crewChats = new Map((teamsCrews?.chats ?? []).map((c) => [c.crewId, c]));
		this.people = people ?? [];
		this.crewList = lookups?.crews ?? [];
		const m = this.model();
		m.setProperty("/teamsCrews", teamsCrews ?? { available: false });
		m.setProperty("/crews", this.crewList.map((c) => ({ ...c, teamsChat: crewChats.get(c.id) ?? null, leaderName: this.name(c.leaderUserId), memberNames: c.memberUserIds.map((id) => this.name(id)).join(", ") || "–",
			load: crewLoad(c.id, range?.assignments ?? [], week) })));
		const crewName = new Map(this.crewList.map((x) => [x.id, x.name]));
		const resources = (lookups?.resources ?? []).map((r) => ({
			...r, kindText: resourceKinds[r.kind] ?? r.kind, crewName: r.crewId ? crewName.get(r.crewId) ?? "–" : "–",
			thumb: r.imageFileId ? fileUrl(r.imageFileId, "thumb") : null, docCount: r.documentFileIds?.length ?? 0,
			inspection: due(r.inspectionDue), service: due(r.serviceDue),
			detailText: (r.kind === "vehicle" ? [r.model, r.licenseClass ? `Klasse ${r.licenseClass}` : null] : [resourceKinds[r.kind] ?? r.kind, r.model]).filter(Boolean).join(" · ") || "–",
		}));
		m.setProperty("/vehicles", resources.filter((r) => r.kind === "vehicle"));
		m.setProperty("/machines", resources.filter((r) => r.kind !== "vehicle"));
		m.setProperty("/resources", resources);
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
		if (tab === "geraete") void this.editResource(null, this.model().getProperty("/deviceTab") === "maschinen" ? "pump" : "vehicle");
		else void this.editCrew(null);
	}

	/** Kolonne › „Teams-Chat“: öffnen bzw. anlegen (Graph) oder – ohne App-Registrierung – direkt in Teams starten (Teams-Link). */
	async onCrewTeams(e: Event): Promise<void> {
		const crew = this.row<CrewResponse & { teamsChat: TeamsCrewChat | null }>(e);
		if (crew.teamsChat) {
			openTeamsUrl(crew.teamsChat.webUrl);
			return;
		}
		const state = this.model().getProperty("/teamsCrews") as TeamsCrewChats;
		const name = crew.number ? `${crew.number} ${crew.name}` : crew.name;
		if (!state.viaGraph) {
			// Ohne App-Registrierung: Gruppenchat per Teams-Link (sofort im Klick öffnen, nicht gespeichert).
			const upns = crew.memberUserIds.filter((id) => id !== session.userId)
				.map((id) => this.teamsPeopleList.find((p) => p.userId === id)?.upn).filter((u): u is string => !!u);
			if (upns.length === 0) {
				MessageBox.information("Niemand aus der Kolonne hat ein Microsoft-Konto.", { title: "Teams-Chat" });
				return;
			}
			openTeamsUrl(chatDeepLink(upns, crewLabel(crew.name)));
			return;
		}
		if (!state.canCreate) return;
		if (!await this.confirm(`Teams-Chat „${crewLabel(crew.name)}“ mit den Mitgliedern der Kolonne ${name} anlegen? Sie sind ebenfalls dabei. `
			+ "Ändert sich die Kolonne, werden die Mitglieder im Chat automatisch angepasst.", { title: "Teams-Chat anlegen", action: "Anlegen" })) return;
		try {
			const chat = await post<TeamsCrewChat>(`teams/crews/${crew.id}/chat`);
			await this.load();
			const open = "In Teams öffnen";
			MessageBox.success(`Teams-Chat „${chat.displayName ?? ""}“ angelegt (${chat.syncedMembers} Personen`
				+ (chat.unmappedMembers > 0 ? `, ${chat.unmappedMembers} ohne Microsoft-Konto nicht aufgenommen` : "") + ").", {
				title: "Teams-Chat", actions: [open, MessageBox.Action.CLOSE], emphasizedAction: open,
				onClose: (action: unknown) => { if (action === open) openTeamsUrl(chat.webUrl); },
			});
		} catch (error) {
			this.showError(error, "Teams-Chat");
		}
	}

	async onCrewTeamsDelete(e: Event): Promise<void> {
		const crew = (e.getSource() as Control).getBindingContext("view")?.getObject() as CrewResponse & { teamsChat: TeamsCrewChat | null };
		if (!crew?.teamsChat) return;
		if (!await this.confirm(`Teams-Chat „${crew.teamsChat.displayName ?? ""}“ mit allen Nachrichten in Microsoft Teams löschen? `
			+ "Wiederherstellen kann nur ein Microsoft-365-Administrator.", { title: "Teams-Chat löschen", action: "Löschen", danger: true })) return;
		try {
			await post(`teams/crews/${crew.id}/chat/delete`);
			this.toast("Teams-Chat gelöscht");
			await this.load();
		} catch (error) {
			this.showError(error, "Teams-Chat");
		}
	}

	onEditCrew(e: Event): void { void this.editCrew(this.row<CrewResponse>(e)); }
	onEditResource(e: Event): void { void this.editResource(this.row<ResourceResponse>(e)); }

	/** Ohne minimumStaff bleibt die Mindestbesetzung unverändert (0 = keine). */
	private putCrew(id: string, c: Pick<CrewResponse, "name" | "leaderUserId" | "memberUserIds" | "color" | "isActive" | "sortOrder"> & { minimumStaff?: number | null; number?: string | null }) {
		return put(`planning/crews/${id}`, { number: c.number ?? null, name: c.name, leaderUserId: c.leaderUserId, memberUserIds: c.memberUserIds, color: c.color, isActive: c.isActive, sortOrder: c.sortOrder,
			minimumStaff: c.minimumStaff ?? null });
	}

	private async editCrew(crew: CrewResponse | null): Promise<void> {
		const ok = await openForm<boolean>({
			title: crew ? "Kolonne bearbeiten" : "Kolonne anlegen", submitText: "Speichern",
			initial: { name: crew?.name ?? "", leaderUserId: crew?.leaderUserId ?? "", memberUserIds: crew?.memberUserIds ?? [], color: crew?.color ?? crewColors[this.crewList.length % crewColors.length].key,
				minimumStaff: crew?.minimumStaff ?? null, number: crew?.number ?? "" },
			fields: [
				numberField("number", "Kolonnennummer", !crew),
				{ key: "name", label: "Name der Kolonne", required: true, placeholder: "z. B. Kolonne Nord", maxLength: 100 },
				{ key: "leaderUserId", label: "Kolonnenführer", type: "select", options: [{ key: "", text: "–" }, ...this.activePeople()] },
				{ key: "memberUserIds", label: "Mitglieder", type: "multi", options: this.activePeople() },
				{ key: "color", label: "Farbe in der Plantafel", type: "select", options: crewColors },
				{ key: "minimumStaff", label: "Mindestbesetzung", type: "integer", min: 0, max: 100, unit: "Personen",
					help: "Hinweis bei Urlaubsanträgen, wenn weniger anwesend wären (nicht blockierend). Leer: keine." },
			],
			submit: async (v) => {
				await this.putCrew(crew?.id ?? uuidv7(), {
					name: String(v.name).trim(), leaderUserId: (v.leaderUserId as string) || null, memberUserIds: (v.memberUserIds as string[]) ?? [],
					color: (v.color as string) || null, isActive: crew?.isActive ?? true, sortOrder: crew?.sortOrder ?? this.crewList.length + 1,
					minimumStaff: (v.minimumStaff as number | null) ?? 0, number: (v.number as string) || null,
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

	/** Immer alle Felder senden (sonst gingen Details beim Aus-/Einschalten verloren). */
	private putResource(id: string, r: Partial<ResourceResponse> & Pick<ResourceResponse, "kind" | "name">) {
		return put(`planning/resources/${id}`, {
			inventoryNumber: r.inventoryNumber ?? null, kind: r.kind, name: r.name, identifier: r.identifier ?? null, notes: r.notes ?? null, isActive: r.isActive ?? true,
			model: r.model ?? null, licenseClass: r.licenseClass ?? null, inspectionDue: r.inspectionDue ?? null, serviceDue: r.serviceDue ?? null,
			imageFileId: r.imageFileId ?? null, documentFileIds: r.documentFileIds ?? [], crewId: r.crewId ?? null,
		});
	}

	/** Dokumente eines Fahrzeugs/Geräts: öffnen, hinzufügen, entfernen. */
	async onDocuments(e: Event): Promise<void> {
		const r = this.row<ResourceResponse>(e);
		const ids = r.documentFileIds ?? [];
		const files = await Promise.all(ids.map((id) => get<{ id: string; originalName: string }>(`files/${id}`).catch(() => ({ id, originalName: "Dokument" }))));
		const list = new List({ noDataText: "Noch keine Dokumente.", items: files.map((f) => new StandardListItem({
			title: f.originalName, icon: "sap-icon://document", type: "Active", press: () => window.open(fileUrl(f.id), "_blank", "noopener"),
		})) });
		const canEdit = this.has("planning.manage");
		const dialog = new Dialog({
			title: `Dokumente: ${r.name}`, contentWidth: "28rem", content: [list],
			buttons: [
				...(canEdit ? [new Button({ text: "Hinzufügen", icon: "sap-icon://add", press: () => {
					const input = document.createElement("input");
					input.type = "file";
					input.multiple = true;
					input.addEventListener("change", async () => {
						const added: string[] = [];
						for (const file of input.files ?? []) added.push(await uploadLoose(file));
						if (added.length && await this.run(async () => { await this.putResource(r.id, { ...r, documentFileIds: [...ids, ...added] }); return true; })) {
							dialog.close();
							await this.saved(`${added.length} Dokument(e) hinzugefügt`);
						}
					});
					input.click();
				} })] : []),
				new Button({ text: "Schließen", press: () => dialog.close() }),
			],
			afterClose: () => dialog.destroy(),
		});
		dialog.open();
	}

	private async editResource(r: ResourceResponse | null, kind?: ResourceResponse["kind"]): Promise<void> {
		let image: File | null = null;
		const imageButton = new Button({ text: r?.imageFileId ? "Bild ersetzen" : "Bild hinzufügen", icon: "sap-icon://add-photo", press: () => {
			const input = document.createElement("input");
			input.type = "file";
			input.accept = "image/*";
			input.addEventListener("change", () => {
				image = input.files?.[0] ?? null;
				if (image) imageButton.setText(`Bild: ${image.name}`);
			});
			input.click();
		} });
		const ok = await openForm<boolean>({
			title: r ? `${r.name} bearbeiten` : "Fahrzeug oder Gerät hinzufügen", submitText: "Speichern", width: "36rem",
			initial: { kind: r?.kind ?? kind ?? "vehicle", name: r?.name ?? "", identifier: r?.identifier ?? "", notes: r?.notes ?? "", model: r?.model ?? "",
				licenseClass: r?.licenseClass ?? "", inspectionDue: r?.inspectionDue ?? null, serviceDue: r?.serviceDue ?? null, crewId: r?.crewId ?? "",
				inventoryNumber: r?.inventoryNumber ?? "" },
			fields: [
				numberField("inventoryNumber", "Inventarnummer", !r),
				{ key: "kind", label: "Art", type: "select", options: options(resourceKinds) },
				{ key: "name", label: "Bezeichnung", required: true, placeholder: "z. B. Sprinter 1 oder Mixokret 2", maxLength: 100 },
				{ key: "identifier", label: "Kennzeichen / Seriennummer", maxLength: 50 },
				{ key: "model", label: "Typ / Modell", maxLength: 100, placeholder: "z. B. Sprinter 316 CDI, P 700" },
				{ key: "licenseClass", label: "Führerscheinklasse (Fahrzeuge)", maxLength: 20, placeholder: "z. B. B, BE, C1E" },
				{ key: "sDue", label: "Fristen", type: "section" },
				{ key: "inspectionDue", label: "TÜV / Prüfung fällig", type: "date" },
				{ key: "serviceDue", label: "Wartung fällig", type: "date" },
				{ key: "sCrew", label: "Zuordnung", type: "section" },
				{ key: "crewId", label: "Feste Kolonne", type: "select", options: this.crewList.filter((c) => c.isActive).map((c) => ({ key: c.id, text: c.name })) },
				{ key: "notes", label: "Notiz", type: "textarea", rows: 2 },
			],
			extra: () => [imageButton],
			submit: async (v) => {
				const imageFileId = image ? await uploadLoose(image) : r?.imageFileId ?? null;
				await this.putResource(r?.id ?? uuidv7(), {
					...(r ?? {}), kind: v.kind as ResourceResponse["kind"], name: String(v.name).trim(), identifier: (v.identifier as string) || null,
					notes: (v.notes as string) || null, isActive: r?.isActive ?? true, model: (v.model as string) || null, licenseClass: (v.licenseClass as string) || null,
					inspectionDue: (v.inspectionDue as string) || null, serviceDue: (v.serviceDue as string) || null, crewId: (v.crewId as string) || null, imageFileId,
					inventoryNumber: (v.inventoryNumber as string) || null,
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
