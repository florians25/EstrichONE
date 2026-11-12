/**
 * „Chat in Teams“: Einzel- und Gruppenchats aus EstrichONE heraus (docs/microsoft-teams.md, Abschnitt „Chats starten“).
 * Microsoft-Konten der Kollegen kommen aus GET /teams/people (zwischengespeichert); den Link baut der Server
 * (POST /teams/chats – Deep Link, optional über Microsoft Graph). Teams öffnet sich in neuem Fenster bzw. in der Teams-App.
 */
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import { get, post } from "./api";
import { openForm } from "./forms";
import { session } from "./session";
import { isTeamsUrl, personChatState, type TeamsChatLink, type TeamsPerson } from "./teams";

let cache: { at: number; people: Promise<TeamsPerson[]> } | null = null;

/** Darf diese Sitzung Teams-Chats starten (Teams im Einsatz, Recht chat.use)? */
export function canStartTeamsChats(): boolean {
	return session.data.teams?.canStartChats === true;
}

/** Microsoft-Konten aller aktiven Kollegen (5 Minuten zwischengespeichert). */
export function teamsPeople(force = false): Promise<TeamsPerson[]> {
	if (!canStartTeamsChats()) return Promise.resolve([]);
	if (!cache || force || Date.now() - cache.at > 300_000) {
		cache = { at: Date.now(), people: get<TeamsPerson[]>("teams/people").catch(() => []) };
	}
	return cache.people;
}

/** Zustand „Chat in Teams“ je Benutzer-ID für Bindungen ({ visible, enabled, tooltip }). */
export async function teamsChatStates(userIds: string[]): Promise<Record<string, { visible: boolean; enabled: boolean; tooltip: string }>> {
	const visible = canStartTeamsChats();
	const people = visible ? await teamsPeople() : [];
	const states: Record<string, { visible: boolean; enabled: boolean; tooltip: string }> = {};
	for (const id of userIds) {
		const state = personChatState(people.find((p) => p.userId === id), id === session.userId);
		states[id] = { visible, ...state };
	}
	return states;
}

/** Teams-Link öffnen (nur echte Teams-Adressen). */
export function openTeamsUrl(url: string | null | undefined): void {
	if (url && isTeamsUrl(url)) window.open(url, "_blank", "noopener");
}

/** Chat mit den Personen starten (eine = Einzelchat, mehrere = Gruppenchat) und Teams öffnen. */
export async function startTeamsChat(userIds: string[], topic?: string, message?: string): Promise<void> {
	try {
		const chat = await post<TeamsChatLink>("teams/chats", { userIds, topic: topic || null, message: message || null });
		openTeamsUrl(chat.webUrl);
		if (chat.missing.length > 0) {
			MessageBox.warning(`Teams geöffnet. Nicht aufgenommen (kein Microsoft-Konto): ${chat.missing.map((m) => `${m.name} – ${m.reason}`).join("; ")}`,
				{ title: "Chat in Teams" });
		} else {
			MessageToast.show(chat.included.length > 1 ? `Gruppenchat mit ${chat.included.length} Personen in Teams geöffnet` : `Chat mit ${chat.included[0]} in Teams geöffnet`);
		}
	} catch (error) {
		MessageBox.error(error instanceof Error ? error.message : String(error), { title: "Chat in Teams" });
	}
}

/** Personen auswählen und Einzel- bzw. Gruppenchat in Teams starten (Kopfzeile, Chat-Archiv). */
export async function pickTeamsChat(preselected: string[] = []): Promise<void> {
	const people = (await teamsPeople(true)).filter((p) => p.userId !== session.userId);
	const reachable = people.filter((p) => p.upn);
	const unreachable = people.filter((p) => !p.upn);
	await openForm<boolean>({
		title: "Chat in Microsoft Teams starten",
		width: "34rem",
		intro: "Eine Person: Einzelchat (ein vorhandener Chat wird geöffnet). Mehrere: Gruppenchat. Teams öffnet sich in einem neuen Fenster bzw. in der Teams-App.",
		fields: [
			{ key: "users", label: "Personen", type: "multi", required: true, options: reachable.map((p) => ({ key: p.userId, text: p.name, additionalText: p.upn ?? "" })) },
			{ key: "topic", label: "Thema (nur Gruppenchat)", type: "text", maxLength: 250 },
			...(unreachable.length > 0 ? [{
				key: "hint", type: "info" as const,
				label: `Ohne Microsoft-Konto (nicht auswählbar): ${unreachable.map((p) => p.name).join(", ")}`,
			}] : []),
		],
		initial: { users: preselected.filter((id) => reachable.some((p) => p.userId === id)), topic: "" },
		submitText: "In Teams öffnen",
		submit: async (values) => {
			const ids = (values.users as string[] | undefined) ?? [];
			if (ids.length === 0) throw new Error("Bitte mindestens eine Person auswählen.");
			await startTeamsChat(ids, String(values.topic ?? ""));
			return true;
		},
	});
}
