/**
 * Bauakte ↔ Microsoft Teams: ein Knopf mit Menü – „In Teams öffnen“, „Teams-Chat anlegen …“, „Per Link verknüpfen …“,
 * „Teams-Chat löschen …“ (bzw. „Verknüpfung lösen“). Mitgliederabgleich, Archivieren und Karten laufen automatisch
 * bzw. über die Einstellungen; die Bauakte-Ansicht bindet nur an „akte>/teams“.
 */
import type JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import MessageStrip from "sap/m/MessageStrip";
import Button from "sap/m/Button";
import { del, get, post } from "./api";
import { openForm, type Option } from "./forms";
import { akteButtonText, akteButtonVisible, chatDeepLink, confirmationMatches, isTeamsUrl, linkRequest, linkSummary, participantHint, participantText,
	resolveParticipants, type BauakteTeams, type TeamsLink, type TeamsParticipants } from "./teams";
import { openTeamsUrl } from "./teamsChat";

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
		warning: !!teams.link?.lastSyncError,
	} : null);
}

/** Teams öffnen: im Browser der https-Link (öffnet die Teams-App, falls installiert, sonst Teams im Web). */
export function openTeamsLink(link: TeamsLink | null | undefined): void {
	if (!link || !isTeamsUrl(link.webUrl)) return;
	openTeamsUrl(link.webUrl);
}

type Created = { link: TeamsLink } | { opened: true };

/**
 * „Teams-Chat anlegen …“: Name (vorbelegt) und Teilnehmer (Bauakten-Team vorausgewählt, dazu Kolonnen, Gruppen, Personen).
 * Mit App-Registrierung legt Microsoft Graph den Gruppenchat an und er wird mit der Bauakte verknüpft (Mitglieder folgen der Auswahl).
 * Ohne App-Registrierung bzw. unter drei Personen öffnet sich der Chat direkt in Teams (Teams-Link, nicht gespeichert).
 */
export async function createAkteTeamsChat(model: JSONModel, bauakteId: string): Promise<boolean> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams) return false;
	const data = await get<TeamsParticipants>(`bauakten/${bauakteId}/teams/participants`);
	const people = new Map(data.people.map((p) => [p.userId, p]));
	const options: Option[] = data.options
		.filter((o) => o.kind !== "user" || o.userIds[0] !== data.selfUserId)
		.map((o) => ({ key: o.key, text: o.kind === "user" && !people.get(o.userIds[0])?.upn ? `${o.name} (ohne Microsoft-Konto)` : participantText(o) }));
	const hint = new MessageStrip({ type: "Information", showIcon: true });
	const showHint = (selected: string[]) => {
		const text = participantHint(data, selected);
		hint.setText(text);
		hint.setVisible(!!text);
	};
	showHint(["team"]);
	const result = await openForm<Created>({
		title: "Teams-Chat anlegen",
		width: "36rem",
		intro: data.viaGraph ? undefined
			: "Der Chat öffnet sich direkt in Microsoft Teams. Ohne App-Registrierung wird er nicht in der Bauakte gespeichert und lässt sich hier nicht löschen.",
		fields: [
			{ key: "topic", label: "Name", required: true, maxLength: 250 },
			{ key: "participants", label: "Teilnehmer", type: "multi", options, required: true, placeholder: "Bauakten-Team, Kolonne, Gruppe oder Person",
				onChange: (value) => showHint((value as string[]) ?? []) },
		],
		initial: { topic: data.topic, participants: ["team"] },
		extra: () => [hint],
		submitText: "Anlegen",
		submit: async (values) => {
			const selected = (values.participants as string[]) ?? [];
			const topic = String(values.topic ?? "").trim();
			const { withAccount } = resolveParticipants(data, selected);
			const others = withAccount.filter((p) => p.userId !== data.selfUserId);
			if (others.length === 0) throw new Error("Bitte mindestens eine weitere Person mit Microsoft-Konto auswählen.");
			if (!data.viaGraph || withAccount.length < data.minMembers) {
				// Sofort (noch im Klick) öffnen – nach einer Serveranfrage würde der Browser das neue Fenster blockieren.
				openTeamsUrl(chatDeepLink(others.map((p) => p.upn as string), topic));
				return { opened: true };
			}
			const link = await post<TeamsLink>(`bauakten/${bauakteId}/teams`, { kind: "groupChat", topic, participants: selected });
			return { link };
		},
	});
	if (!result) return false;
	if ("opened" in result) return true;
	applyAkteTeams(model, { ...teams, link: result.link });
	const open = "In Teams öffnen";
	MessageBox.success(`Teams-Chat „${result.link.displayName ?? ""}“ angelegt und mit der Bauakte verknüpft (${result.link.syncedMembers} Personen). `
		+ "Tipp: gleich öffnen und die erste Nachricht schreiben – erst dann steht der Chat bei allen in der Teams-Chatliste.", {
		title: "Microsoft Teams", actions: [open, MessageBox.Action.CLOSE], emphasizedAction: open,
		onClose: (action: unknown) => { if (action === open) openTeamsLink(result.link); },
	});
	return true;
}

/** „Per Link verknüpfen …“: vorhandenen Kanal oder Chat aus Teams. */
export async function linkAkteTeams(model: JSONModel, bauakteId: string): Promise<void> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams) return;
	const linked = await openForm<TeamsLink>({
		title: "Per Link verknüpfen",
		width: "36rem",
		intro: "Vorhandenen Chat oder Kanal verknüpfen: in Teams beim Chat „…“ › „Link kopieren“ bzw. beim Kanal „Link zum Kanal abrufen“ und hier einfügen.",
		fields: [{ key: "url", label: "Link aus Teams", type: "text", required: true, placeholder: "https://teams.microsoft.com/l/chat/…" }],
		initial: { url: "" },
		submitText: "Verknüpfen",
		submit: async (values) => {
			const request = linkRequest("url", String(values.url ?? ""), false);
			if (typeof request === "string") throw new Error(request);
			return post<TeamsLink>(`bauakten/${bauakteId}/teams`, request);
		},
	});
	if (linked) applyAkteTeams(model, { ...teams, link: linked });
}

/**
 * „Teams-Chat löschen …“ (Recht teams.delete): löscht Chat bzw. Kanal in Teams nach Eintippen der Bauaktennummer.
 * „Nur Verknüpfung lösen“ lässt alles in Teams stehen.
 */
export async function deleteAkteTeams(model: JSONModel, bauakteId: string): Promise<"deleted" | "unlinked" | null> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams?.link) return null;
	const expected = teams.confirmation ?? "";
	const channel = teams.link.kind === "channel";
	let unlinked = false;
	const done = await openForm<boolean>({
		title: "Teams-Chat löschen",
		width: "34rem",
		danger: true,
		intro: `Löscht ${channel ? "den Kanal" : "den Chat"} „${teams.link.displayName ?? ""}“ mit allen Nachrichten und Dateien in Microsoft Teams. `
			+ (channel ? "Ein Teambesitzer kann gelöschte Kanäle 30 Tage lang wiederherstellen." : "Wiederherstellen kann nur ein Microsoft-365-Administrator.")
			+ " Soll in Teams alles bleiben: „Nur Verknüpfung lösen“.",
		fields: [{ key: "confirmation", label: `Zur Bestätigung „${expected}“ eintippen`, type: "text", required: true,
			validate: (value) => confirmationMatches(String(value ?? ""), expected) ? null : `Bitte genau „${expected}“ eintippen.` }],
		initial: { confirmation: "" },
		submitText: "Endgültig löschen",
		leftButton: (dialog) => new Button({ text: "Nur Verknüpfung lösen", press: async () => {
			dialog.setBusy(true);
			try {
				await del(`bauakten/${bauakteId}/teams`);
				unlinked = true;
				dialog.close();
			} catch (error) {
				MessageBox.error(error instanceof Error ? error.message : String(error), { title: "Microsoft Teams" });
			} finally {
				dialog.setBusy(false);
			}
		} }),
		submit: async (values) => {
			await post(`bauakten/${bauakteId}/teams/delete`, { confirmation: String(values.confirmation ?? "") });
			return true;
		},
	});
	if (!done && !unlinked) return null;
	applyAkteTeams(model, { ...teams, link: null });
	return done ? "deleted" : "unlinked";
}

/** „Verknüpfung lösen“ (ohne Recht zum Löschen): in Teams bleibt alles erhalten. */
export async function unlinkAkteTeams(model: JSONModel, bauakteId: string): Promise<void> {
	const teams = model.getProperty("/teams") as BauakteTeams | null;
	if (!teams?.link) return;
	await del(`bauakten/${bauakteId}/teams`);
	applyAkteTeams(model, { ...teams, link: null });
}
