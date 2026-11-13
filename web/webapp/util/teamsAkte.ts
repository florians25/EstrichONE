/**
 * Bauakte ↔ Microsoft Teams: Verknüpfung laden, „In Teams öffnen“, verknüpfen (Kanal, Gruppenchat oder per Link),
 * Mitglieder abgleichen und Verknüpfung lösen. Die Bauakte-Ansicht bindet nur einen Knopf an „akte>/teams“.
 */
import type JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import { del, get, post } from "./api";
import { openForm } from "./forms";
import { akteButtonText, akteButtonVisible, closeActionLabels, confirmationMatches, isTeamsUrl, linkRequest, linkSummary, type BauakteTeams,
	type TeamsLink } from "./teams";
import { openTeamsUrl } from "./teamsChat";
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
		canArchive: !!teams.canDelete && teams.link?.kind === "channel" && !teams.link?.archivedAt,
		canUnarchive: !!teams.canDelete && teams.link?.kind === "channel" && !!teams.link?.archivedAt,
	} : null);
}

/** Chat mit dem ganzen Bauakten-Team (Deep Link vom Server, ohne die eigene Person). */
export function chatWithAkteTeam(model: JSONModel): void {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams?.teamChat) {
		MessageBox.information("Niemand aus dem Bauakten-Team hat ein Microsoft-Konto – bitte Konten zuordnen (Einstellungen › Microsoft Teams › Benutzer).",
			{ title: "Chat mit dem Team" });
		return;
	}
	openTeamsUrl(teams.teamChat.webUrl);
}

/** Kanal archivieren bzw. wiederherstellen (Recht teams.delete). */
export async function archiveAkteTeams(model: JSONModel, bauakteId: string, archive: boolean): Promise<void> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams?.link) return;
	const link = await post<TeamsLink>(`bauakten/${bauakteId}/teams/${archive ? "archive" : "unarchive"}`);
	applyAkteTeams(model, { ...teams, link });
}

/** Kanal bzw. Gruppenchat in Teams löschen: Bestätigung durch Eintippen der Bauaktennummer. */
export async function deleteAkteTeams(model: JSONModel, bauakteId: string): Promise<boolean> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams?.link) return false;
	const expected = teams.confirmation ?? "";
	const what = teams.link.kind === "channel" ? "den Kanal" : "den Gruppenchat";
	const done = await openForm<boolean>({
		title: "In Microsoft Teams löschen",
		width: "34rem",
		danger: true,
		intro: `Löscht ${what} „${teams.link.displayName ?? ""}“ mit allen Nachrichten und Dateien in Microsoft Teams und löst die Verknüpfung. `
			+ (teams.link.kind === "channel"
				? "Ein Teambesitzer kann gelöschte Kanäle 30 Tage lang in Teams wiederherstellen."
				: "Gelöschte Chats kann nur ein Microsoft-365-Administrator wiederherstellen.")
			+ " Alternative: „Archivieren“ (nur Kanäle) oder „Verknüpfung lösen“ (in Teams bleibt alles erhalten).",
		fields: [{ key: "confirmation", label: `Zur Bestätigung die Bauaktennummer „${expected}“ eintippen`, type: "text", required: true,
			validate: (value) => confirmationMatches(String(value ?? ""), expected) ? null : `Bitte genau „${expected}“ eintippen.` }],
		initial: { confirmation: "" },
		submitText: "Endgültig löschen",
		submit: async (values) => {
			await post(`bauakten/${bauakteId}/teams/delete`, { confirmation: String(values.confirmation ?? "") });
			return true;
		},
	});
	if (!done) return false;
	applyAkteTeams(model, { ...teams, link: null });
	return true;
}

/** Kurze Nachricht in den verknüpften Kanal bzw. Chat. */
export async function postAkteTeams(model: JSONModel, bauakteId: string): Promise<void> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams?.link) return;
	const link = await openForm<TeamsLink>({
		title: "Nachricht in Teams posten",
		width: "34rem",
		intro: `Erscheint als Karte in ${teams.link.kind === "channel" ? "Kanal" : "Gruppenchat"} „${teams.link.displayName ?? ""}“ mit Ihrem Namen und einem Link zur Bauakte.`,
		fields: [{ key: "text", label: "Nachricht", type: "textarea", rows: 4, maxLength: 2000, required: true }],
		initial: { text: "" },
		submitText: "Posten",
		submit: (values) => post<TeamsLink>(`bauakten/${bauakteId}/teams/message`, { text: String(values.text ?? "") }),
	});
	if (link) applyAkteTeams(model, { ...teams, link });
}

/** Teams öffnen: im Browser der https-Link (öffnet die Teams-App, falls installiert, sonst Teams im Web). */
export function openTeamsLink(link: TeamsLink | null | undefined): void {
	if (!link || !isTeamsUrl(link.webUrl)) return;
	openTeamsUrl(link.webUrl);
}

export async function linkAkteTeams(model: JSONModel, bauakteId: string): Promise<void> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams) return;
	// Nur vorhandene Kanäle/Chats per Link verknüpfen; direktes Anlegen aus EstrichONE ist ausgeblendet (Nutzerwunsch).
	const linked = await openForm<TeamsLink>({
		title: "Mit Microsoft Teams verknüpfen",
		width: "38rem",
		intro: "Vorhandenen Kanal oder Chat per Link verknüpfen: in Teams › „…“ › „Link zum Kanal abrufen“ bzw. „Link kopieren“ und hier einfügen.",
		fields: [
			{ key: "url", label: "Link aus Teams", type: "text", required: true, placeholder: "https://teams.microsoft.com/l/channel/…" },
			...(teams.canCreate ? [{ key: "syncMembers", label: "Mitglieder abgleichen", type: "switch" as const }] : []),
		],
		initial: { url: "", syncMembers: false },
		submitText: "Verknüpfen",
		submit: async (values) => {
			const request = linkRequest("url", String(values.url ?? ""), values.syncMembers === true);
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
		teams?.closeAction && link.created ? `Beim Abschließen der Bauakte: ${closeActionLabels[teams.closeAction]}` : "",
		link.archivedAt ? `In Teams archiviert am ${dateTime(link.archivedAt)}` : "",
		link.closeError ? `Abschluss-Aktion nicht ausgeführt: ${link.closeError}` : "",
		`Verknüpft am ${dateTime(link.linkedAt)}${link.linkedByName ? ` von ${link.linkedByName}` : ""}${link.created ? " (von EstrichONE angelegt)" : " (per Link)"}`,
		link.syncMembers ? (link.lastSyncAt ? `Letzter Abgleich ${dateTime(link.lastSyncAt)}` : "Noch nicht abgeglichen") : "Mitglieder werden nicht abgeglichen",
		link.unmappedMembers > 0 ? `${link.unmappedMembers} Person(en) ohne Microsoft-Konto` : "",
		link.lastSyncError ? `Hinweis: ${link.lastSyncError}` : "",
		link.lastPostError ? `Letzte Karte nicht gesendet: ${link.lastPostError}` : link.lastPostAt ? `Letzte Karte ${dateTime(link.lastPostAt)}` : "",
	].filter(Boolean).join("\n");
}
