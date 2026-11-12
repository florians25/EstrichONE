/**
 * Microsoft Teams (docs/handbuch/microsoft-teams.md): Typen der API und reine Hilfsfunktionen für Navigation,
 * Bauakte und Chat-Archiv. Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */

export type ChatMode = "estrichOne" | "teams" | "both";
export type TeamsLinkKind = "channel" | "groupChat";
export type TeamsPostVia = "off" | "graph" | "webhook";
export type TeamsAuthKind = "secret" | "certificate";

export interface TeamsStatus {
	chatMode: ChatMode;
	chatReadOnly: boolean;
	available: boolean;
	graphConfigured: boolean;
	postVia: TeamsPostVia;
	canManage: boolean;
	siteTeamName: string | null;
	siteTeamUrl: string | null;
	siteTeamAppUrl: string | null;
	teamsWebUrl: string;
	teamsAppUrl: string;
}

export interface TeamsPermissionInfo { name: string; type: string; required: boolean; reason: string }

export interface TeamsSettings {
	chatMode: ChatMode;
	tenantId: string | null;
	clientId: string | null;
	authKind: TeamsAuthKind;
	hasClientSecret: boolean;
	certificateThumbprint: string | null;
	certificateExpiresAt: string | null;
	siteTeamId: string | null;
	siteTeamName: string | null;
	siteTeamUrl: string | null;
	defaultLinkKind: TeamsLinkKind;
	autoLink: boolean;
	syncMembers: boolean;
	postVia: TeamsPostVia;
	hasWebhook: boolean;
	webhookHost: string | null;
	postDefects: boolean;
	postAcceptance: boolean;
	postSchedule: boolean;
	senderName: string | null;
	senderConnectedAt: string | null;
	lastTestAt: string | null;
	lastTestOk: boolean | null;
	lastTestError: string | null;
	lastPostAt: string | null;
	lastPostError: string | null;
	graphConfigured: boolean;
	mappedUsers: number;
	activeUsers: number;
	linkedBauakten: number;
	permissions: TeamsPermissionInfo[];
	version: number;
}

export interface TeamsTestStep { name: string; ok: boolean; detail: string }
export interface TeamsTestResponse { ok: boolean; steps: TeamsTestStep[]; at: string }

export interface TeamsUserMapping {
	userId: string; name: string; email: string | null; active: boolean; aadUserId: string | null; userPrincipalName: string | null;
	accountName: string | null; manual: boolean; error: string | null; checkedAt: string | null;
}

export interface TeamsSenderStart { userCode: string; verificationUri: string; expiresAt: string; intervalSeconds: number; message: string | null }
export interface TeamsSenderState { connected: boolean; pending: boolean; senderName: string | null; connectedAt: string | null }

export interface TeamsLink {
	kind: TeamsLinkKind; created: boolean; displayName: string | null; webUrl: string; appUrl: string; teamId: string | null; channelId: string | null;
	chatId: string | null; syncMembers: boolean; lastSyncAt: string | null; lastSyncError: string | null; syncedMembers: number; unmappedMembers: number;
	lastPostAt: string | null; lastPostError: string | null; hasWebhook: boolean; linkedAt: string; linkedByName: string | null;
}

export interface BauakteTeams {
	bauakteId: string; chatMode: ChatMode; link: TeamsLink | null; canManage: boolean; canCreate: boolean; canCreateChannel: boolean;
	defaultLinkKind: TeamsLinkKind;
}

export const chatModeLabels: Record<ChatMode, string> = {
	estrichOne: "EstrichONE-Chat",
	teams: "Microsoft Teams",
	both: "Beides",
};

/** Wie der eingebaute Chat in Navigation und Kopfzeile erscheint. */
export function chatPresentation(status: Pick<TeamsStatus, "chatMode" | "chatReadOnly" | "available"> | null | undefined): {
	chatLabel: string; chatWritable: boolean; showTeams: boolean;
} {
	if (!status) return { chatLabel: "Chat", chatWritable: true, showTeams: false };
	return {
		chatLabel: status.chatReadOnly ? "Chat-Archiv" : "Chat",
		chatWritable: !status.chatReadOnly,
		showTeams: status.available,
	};
}

/** Ziel für „Microsoft Teams“ in der Navigation: Team „Baustellen“, sonst Teams allgemein. */
export function teamsHome(status: Pick<TeamsStatus, "siteTeamUrl" | "teamsWebUrl"> | null | undefined): string {
	return status?.siteTeamUrl || status?.teamsWebUrl || "https://teams.microsoft.com";
}

/** Nur echte Teams-Adressen öffnen (keine fremden Links aus der Datenbank). */
export function isTeamsUrl(url: string | null | undefined): boolean {
	if (!url) return false;
	try {
		const parsed = new URL(url);
		return parsed.protocol === "https:" && ["teams.microsoft.com", "teams.cloud.microsoft", "teams.live.com"].includes(parsed.hostname);
	} catch {
		return false;
	}
}

/** Kurzbeschreibung der Verknüpfung für Knopf-Tooltip und Hinweise. */
export function linkSummary(link: TeamsLink | null | undefined): string {
	if (!link) return "Noch nicht mit Teams verknüpft";
	const kind = link.kind === "channel" ? "Kanal" : "Gruppenchat";
	const name = link.displayName ? ` „${link.displayName}“` : "";
	const sync = !link.syncMembers ? "" : link.lastSyncError ? " · Abgleich mit Hinweis" : link.lastSyncAt ? ` · ${link.syncedMembers} Mitglieder abgeglichen` : "";
	return `${kind}${name}${sync}`;
}

/** Text für den Bauakte-Knopf. */
export function akteButtonText(teams: BauakteTeams | null | undefined): string {
	return teams?.link ? "In Teams öffnen" : "Mit Teams verknüpfen";
}

/** Sichtbar, wenn Teams im Einsatz ist und es etwas zu öffnen oder zu verknüpfen gibt. */
export function akteButtonVisible(teams: BauakteTeams | null | undefined): boolean {
	return !!teams && teams.chatMode !== "estrichOne" && (!!teams.link || teams.canManage);
}

/** Anfrage zum Verknüpfen aus der Auswahl im Dialog. */
export function linkRequest(choice: "channel" | "groupChat" | "url", url: string, syncMembers: boolean): Record<string, unknown> | string {
	if (choice === "url") {
		const trimmed = url.trim();
		if (!trimmed) return "Bitte den Link aus Teams einfügen („Link zum Kanal abrufen“ bzw. „Link kopieren“).";
		if (!isTeamsUrl(trimmed) && !trimmed.startsWith("msteams:")) return "Das ist kein Link aus Microsoft Teams.";
		return { url: trimmed, syncMembers };
	}
	return { kind: choice, syncMembers };
}

/** Zeile der Benutzerzuordnung für die Tabelle. */
export function mappingState(row: TeamsUserMapping): { text: string; state: "Success" | "Warning" | "Error" | "None" } {
	if (row.aadUserId) return { text: row.manual ? "von Hand zugeordnet" : "automatisch zugeordnet", state: "Success" };
	if (row.error) return { text: row.error, state: "Warning" };
	if (!row.email) return { text: "Keine E-Mail-Adresse hinterlegt", state: "None" };
	return { text: "Noch nicht abgeglichen", state: "None" };
}
