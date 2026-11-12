/**
 * Bauakte ↔ Microsoft Teams: Verknüpfung laden, „In Teams öffnen“, verknüpfen (Kanal, Gruppenchat oder per Link),
 * Mitglieder abgleichen und Verknüpfung lösen. Die Bauakte-Ansicht bindet nur einen Knopf an „akte>/teams“.
 */
import type JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import { del, get, post } from "./api";
import { openForm } from "./forms";
import { akteButtonText, akteButtonVisible, isTeamsUrl, linkRequest, linkSummary, type BauakteTeams, type TeamsLink } from "./teams";
import { dateTime } from "./format";

/** Verknüpfung der Bauakte nach „akte>/teams“ (nur, wenn Teams im Einsatz ist). */
export async function loadAkteTeams(model: JSONModel, bauakteId: string, available: boolean): Promise<void> {
	model.setProperty("/teamsFor", bauakteId);
	model.setProperty("/teams", null);
	if (!available) return;
	const teams = await get<BauakteTeams>(`bauakten/${bauakteId}/teams`).catch(() => null);
	if (model.getProperty("/teamsFor") !== bauakteId) return;
	applyAkteTeams(model, teams);
}

function applyAkteTeams(model: JSONModel, teams: BauakteTeams | null): void {
	model.setProperty("/teams", teams ? {
		...teams,
		visible: akteButtonVisible(teams),
		buttonText: akteButtonText(teams),
		tooltip: linkSummary(teams.link) + (teams.link?.lastSyncError ? `\n${teams.link.lastSyncError}` : ""),
		warning: !!teams.link?.lastSyncError || !!teams.link?.lastPostError,
	} : null);
}

/** Teams öffnen: im Browser der https-Link (öffnet die Teams-App, falls installiert, sonst Teams im Web). */
export function openTeamsLink(link: TeamsLink | null | undefined): void {
	if (!link || !isTeamsUrl(link.webUrl)) return;
	window.open(link.webUrl, "_blank", "noopener");
}

export async function linkAkteTeams(model: JSONModel, bauakteId: string): Promise<void> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams) return;
	const options = [
		...(teams.canCreateChannel ? [{ key: "channel", text: "Kanal im Team „Baustellen“ anlegen" }] : []),
		...(teams.canCreate ? [{ key: "groupChat", text: "Gruppenchat mit dem Bauakten-Team anlegen" }] : []),
		{ key: "url", text: "Vorhandenen Kanal oder Chat per Link verknüpfen" },
	];
	const initialChoice = teams.canCreate ? (teams.defaultLinkKind === "channel" && teams.canCreateChannel ? "channel" : "groupChat") : "url";
	const linked = await openForm<TeamsLink>({
		title: "Mit Microsoft Teams verknüpfen",
		width: "38rem",
		intro: teams.canCreate
			? "EstrichONE legt die Unterhaltung an und nimmt Zuständige und Mitglieder der Bauakte auf (sofern ihnen ein Microsoft-Konto zugeordnet ist)."
			: "Ohne App-Registrierung lässt sich ein vorhandener Kanal oder Chat per Link verknüpfen (Teams › „…“ › „Link zum Kanal abrufen“ bzw. „Link kopieren“).",
		fields: [
			{ key: "choice", label: "Unterhaltung", type: "select", options, required: true },
			{ key: "url", label: "Link aus Teams", type: "text", placeholder: "https://teams.microsoft.com/l/channel/…" },
			{ key: "syncMembers", label: "Mitglieder abgleichen", type: "switch", help: teams.canCreate ? undefined : "Nur mit App-Registrierung möglich." },
		],
		initial: { choice: initialChoice, url: "", syncMembers: teams.canCreate },
		submitText: "Verknüpfen",
		submit: async (values) => {
			const request = linkRequest(values.choice as "channel" | "groupChat" | "url", String(values.url ?? ""), values.syncMembers === true);
			if (typeof request === "string") throw new Error(request);
			return post<TeamsLink>(`bauakten/${bauakteId}/teams`, request);
		},
	});
	if (!linked) return;
	applyAkteTeams(model, { ...teams, link: linked });
	if (linked.lastSyncError) {
		MessageBox.warning(`Verknüpft. Hinweis zum Mitgliederabgleich: ${linked.lastSyncError}`, { title: "Microsoft Teams" });
	}
}

export async function syncAkteTeams(model: JSONModel, bauakteId: string): Promise<void> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams?.link) return;
	const link = await post<TeamsLink>(`bauakten/${bauakteId}/teams/sync`);
	applyAkteTeams(model, { ...teams, link });
	if (link.lastSyncError) MessageBox.warning(link.lastSyncError, { title: "Mitgliederabgleich mit Hinweis" });
	else MessageBox.success(`${link.syncedMembers} Mitglieder abgeglichen.`, { title: "Microsoft Teams" });
}

export async function unlinkAkteTeams(model: JSONModel, bauakteId: string): Promise<void> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams?.link) return;
	await del(`bauakten/${bauakteId}/teams`);
	applyAkteTeams(model, { ...teams, link: null });
}

/** Infotext zur Verknüpfung (Menü „Details“). */
export function akteTeamsDetails(teams: BauakteTeams | null): string {
	const link = teams?.link;
	if (!link) return "Nicht verknüpft.";
	return [
		linkSummary(link),
		`Verknüpft am ${dateTime(link.linkedAt)}${link.linkedByName ? ` von ${link.linkedByName}` : ""}${link.created ? " (von EstrichONE angelegt)" : " (per Link)"}`,
		link.syncMembers ? (link.lastSyncAt ? `Letzter Abgleich ${dateTime(link.lastSyncAt)}` : "Noch nicht abgeglichen") : "Mitglieder werden nicht abgeglichen",
		link.unmappedMembers > 0 ? `${link.unmappedMembers} Person(en) ohne Microsoft-Konto` : "",
		link.lastSyncError ? `Hinweis: ${link.lastSyncError}` : "",
		link.lastPostError ? `Letzte Karte nicht gesendet: ${link.lastPostError}` : link.lastPostAt ? `Letzte Karte ${dateTime(link.lastPostAt)}` : "",
	].filter(Boolean).join("\n");
}
