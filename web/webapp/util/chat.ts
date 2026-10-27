/** Chat (docs/chat.md): Typen und Aufrufe der API. */
import { api, del, get, post, put } from "./api";

export interface ChatStatus {
	enabled: boolean;
	canUse: boolean;
	canAdmin: boolean;
	unreadCount: number;
	unreadConversations: number;
	retentionDays: number;
	showPreview: boolean;
}

export interface ChatPerson { id: string; name: string }

export interface ChatLastMessage {
	id: string; senderUserId: string; senderName: string; text: string | null; createdAt: string; mine: boolean; deleted: boolean;
}

export interface ChatConversation {
	id: string;
	kind: "direct" | "group";
	title: string;
	crewId: string | null;
	members: ChatPerson[];
	lastMessage: ChatLastMessage | null;
	lastActivityAt: string;
	unreadCount: number;
	muted: boolean;
	partnerUserId: string | null;
}

export interface ChatMemberState { userId: string; name: string; deliveredUpTo: string | null; readUpTo: string | null; fromCrew: boolean }

export interface ChatConversationDetail { conversation: ChatConversation; members: ChatMemberState[]; canManage: boolean }

export interface ChatMessage {
	id: string;
	conversationId: string;
	senderUserId: string;
	senderName: string;
	text: string | null;
	createdAt: string;
	mine: boolean;
	deleted: boolean;
	status: "sent" | "delivered" | "read" | null;
	readBy: number;
	recipients: number;
	clientMessageId: string | null;
}

export interface ChatMessagePage { items: ChatMessage[]; hasMore: boolean; members: ChatMemberState[] }

export interface ChatSettings {
	enabled: boolean; retentionDays: number; showPreview: boolean; workersCanChatEachOther: boolean; lastCleanupAt: string | null;
	conversations: number; messages: number; version: number;
}

export interface CrewOption { id: string; name: string; members: number }

export const chatStatus = () => get<ChatStatus>("chat/status");
export const conversations = () => get<ChatConversation[]>("chat/conversations");
export const conversation = (id: string) => get<ChatConversationDetail>(`chat/conversations/${id}`);
export const contacts = () => get<ChatPerson[]>("chat/contacts");
export const crews = () => get<CrewOption[]>("chat/crews");
export const startDirect = (userId: string) => post<ChatConversation>("chat/conversations/direct", { userId });
export const createGroup = (title: string, memberIds: string[], crewId: string | null) =>
	post<ChatConversation>("chat/conversations", { title, memberIds, crewId });
export const updateGroup = (id: string, body: { title?: string; memberIds?: string[] }) => put<ChatConversationDetail>(`chat/conversations/${id}`, body);
export const deleteConversation = (id: string) => del<void>(`chat/conversations/${id}`);
export const mute = (id: string, muted: boolean) => post<void>(`chat/conversations/${id}/mute`, { muted });
export const messages = (id: string, query: { before?: string; after?: string; limit?: number } = {}) =>
	get<ChatMessagePage>(`chat/conversations/${id}/messages`, query);
export const send = (id: string, text: string, clientMessageId: string) =>
	post<ChatMessage>(`chat/conversations/${id}/messages`, { text, clientMessageId });
export const markRead = (id: string) => post<void>(`chat/conversations/${id}/read`);
export const deleteMessage = (id: string) => del<void>(`chat/messages/${id}`);
export const chatSettings = () => get<ChatSettings>("chat/admin/settings");
export const saveChatSettings = (s: Omit<ChatSettings, "lastCleanupAt" | "conversations" | "messages">) =>
	api<ChatSettings>("chat/admin/settings", { method: "PUT", body: s });

const time = new Intl.DateTimeFormat("de-DE", { hour: "2-digit", minute: "2-digit" });
const weekday = new Intl.DateTimeFormat("de-DE", { weekday: "short" });
const shortDate = new Intl.DateTimeFormat("de-DE", { day: "2-digit", month: "2-digit", year: "2-digit" });
const longDate = new Intl.DateTimeFormat("de-DE", { weekday: "long", day: "numeric", month: "long", year: "numeric" });

function startOfDay(d: Date): number {
	return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}

/** Zeit in der Unterhaltungsliste: heute Uhrzeit, gestern „Gestern“, diese Woche Wochentag, sonst Datum. */
export function listTime(value: string, now = new Date()): string {
	const d = new Date(value);
	const days = Math.round((startOfDay(now) - startOfDay(d)) / 86_400_000);
	if (days <= 0) return time.format(d);
	if (days === 1) return "Gestern";
	if (days < 7) return weekday.format(d);
	return shortDate.format(d);
}

/** Tagestrenner im Verlauf. */
export function dayLabel(value: string, now = new Date()): string {
	const d = new Date(value);
	const days = Math.round((startOfDay(now) - startOfDay(d)) / 86_400_000);
	if (days <= 0) return "Heute";
	if (days === 1) return "Gestern";
	return longDate.format(d);
}

export const clock = (value: string) => time.format(new Date(value));

export function dayKey(value: string): string {
	const d = new Date(value);
	return `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
}

/** Initialen für Avatare. */
export function initialsOf(name: string): string {
	const parts = name.replace(/[^\p{L}\s-]/gu, " ").trim().split(/\s+/).filter(Boolean);
	if (parts.length === 0) return "?";
	return (parts.length === 1 ? parts[0].slice(0, 2) : parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

/** Ruhige, gut unterscheidbare Avatarfarben (UI5 Accent 1–10), stabil je ID. */
export function accentOf(id: string): string {
	let h = 0;
	for (const ch of id) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
	return `Accent${(h % 10) + 1}`;
}

/** Erste Nachricht des Absenders in einer Folge (gleicher Absender, höchstens 5 Minuten Abstand, gleicher Tag). */
export function startsRun(previous: ChatMessage | undefined, current: ChatMessage): boolean {
	if (!previous) return true;
	if (previous.senderUserId !== current.senderUserId) return true;
	if (dayKey(previous.createdAt) !== dayKey(current.createdAt)) return true;
	return new Date(current.createdAt).getTime() - new Date(previous.createdAt).getTime() > 5 * 60_000;
}

export function previewText(c: ChatConversation): string {
	const m = c.lastMessage;
	if (!m) return c.kind === "group" ? `${c.members.length} Mitglieder` : "Noch keine Nachrichten";
	const text = m.deleted ? "Nachricht gelöscht" : m.text ?? "Nachricht nicht lesbar";
	if (m.mine) return `Du: ${text}`;
	return c.kind === "group" ? `${m.senderName.split(" ")[0]}: ${text}` : text;
}
