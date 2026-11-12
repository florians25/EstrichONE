/** Chat (docs/chat.md): Typen und Aufrufe der API. */
import { api, apiUrl, del, get, post, put } from "./api";

export interface ChatStatus {
	enabled: boolean;
	canUse: boolean;
	canAdmin: boolean;
	unreadCount: number;
	unreadConversations: number;
	retentionDays: number;
	showPreview: boolean;
	privacyAccepted?: boolean;
	blocked?: boolean;
	editWindowMinutes?: number;
	attachmentsEnabled?: boolean;
	maxAttachmentBytes?: number;
	readReceipts?: boolean;
	personalShowPreview?: boolean;
	realtime?: string;
	/** Chat-Modus (Microsoft Teams); „teams“ = Archiv. */
	mode?: "estrichOne" | "teams" | "both";
	/** Nur lesen und exportieren (Modus „Microsoft Teams“). */
	readOnly?: boolean;
}

export type AttachmentKind = "photo" | "document";

export interface ChatAttachment {
	id: string; kind: AttachmentKind; fileName: string; contentType: string; sizeBytes: number; width: number | null; height: number | null;
	hasThumbnail: boolean; createdAt: string; messageId: string | null; uploadedByUserId: string;
}

export interface ChatReplyPreview { id: string; senderUserId: string; senderName: string; text: string | null; deleted: boolean; attachment: AttachmentKind | null }
export interface ChatReactionGroup { emoji: string; count: number; mine: boolean; names: string[] }
export interface ChatLocation { latitude: number; longitude: number; label?: string | null }

export interface ChatPerson { id: string; name: string }

export interface ChatLastMessage {
	id: string; senderUserId: string; senderName: string; text: string | null; createdAt: string; mine: boolean; deleted: boolean;
	attachment?: AttachmentKind | null; location?: boolean;
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
	description?: string | null;
	bauakteId?: string | null;
	announcementOnly?: boolean;
	imageUpdatedAt?: string | null;
	mutedUntil?: string | null;
	isAdmin?: boolean;
	canPost?: boolean;
}

export interface ChatMemberState {
	userId: string; name: string; deliveredUpTo: string | null; readUpTo: string | null; fromCrew: boolean; isAdmin?: boolean;
}

export interface ChatConversationDetail {
	conversation: ChatConversation; members: ChatMemberState[]; canManage: boolean; pinned?: ChatMessage[]; bauakteTitle?: string | null;
}

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
	replyTo?: ChatReplyPreview | null;
	attachments?: ChatAttachment[];
	reactions?: ChatReactionGroup[];
	mentionedUserIds?: string[];
	editedAt?: string | null;
	forwarded?: boolean;
	pinnedAt?: string | null;
	location?: ChatLocation | null;
	canEdit?: boolean;
}

export interface ChatSearchHit { messageId: string; conversationId: string; conversationTitle: string; senderName: string; snippet: string; createdAt: string }
export interface ChatSearchResult { items: ChatSearchHit[]; searched: number; truncated: boolean }
export interface ChatMySettings { showPreview: boolean; readReceipts: boolean; privacyAcceptedAt: string | null; blocked: boolean; previewAllowedByAdmin: boolean }
export interface ChatBauakteGroup { bauakteId: string; conversationId: string | null; title: string | null; member: boolean; members: number; canCreate: boolean }

/** Nachricht senden: Text und/oder Anhänge, Standort, Zitat, Erwähnungen. */
export interface SendBody {
	text?: string; clientMessageId: string; replyToId?: string | null; attachmentIds?: string[]; location?: ChatLocation | null; mentionIds?: string[];
}

export interface ChatMessagePage { items: ChatMessage[]; hasMore: boolean; members: ChatMemberState[] }

export interface ChatSettings {
	enabled: boolean; retentionDays: number; showPreview: boolean; workersCanChatEachOther: boolean; lastCleanupAt: string | null;
	conversations: number; messages: number; version: number; editWindowMinutes?: number; attachmentsEnabled?: boolean;
	attachments?: number; attachmentBytes?: number;
}

export interface CrewOption { id: string; name: string; members: number }

export const chatStatus = () => get<ChatStatus>("chat/status");
export const conversations = () => get<ChatConversation[]>("chat/conversations");
export const conversation = (id: string) => get<ChatConversationDetail>(`chat/conversations/${id}`);
export const contacts = () => get<ChatPerson[]>("chat/contacts");
export const crews = () => get<CrewOption[]>("chat/crews");
export const startDirect = (userId: string) => post<ChatConversation>("chat/conversations/direct", { userId });
export const createGroup = (title: string, memberIds: string[], crewId: string | null, extra: { description?: string; announcementOnly?: boolean } = {}) =>
	post<ChatConversation>("chat/conversations", { title, memberIds, crewId, ...extra });
export const updateGroup = (id: string, body: { title?: string; memberIds?: string[]; description?: string; adminIds?: string[]; announcementOnly?: boolean }) =>
	put<ChatConversationDetail>(`chat/conversations/${id}`, body);
export const deleteConversation = (id: string) => del<void>(`chat/conversations/${id}`);
export const leaveGroup = (id: string) => post<void>(`chat/conversations/${id}/leave`);
/** Stumm: `minutes` = Dauer, ohne Angabe immer. */
export const mute = (id: string, muted: boolean, minutes?: number) => post<void>(`chat/conversations/${id}/mute`, { muted, minutes });
export const typing = (id: string) => post<void>(`chat/conversations/${id}/typing`);
export const setGroupImage = (id: string, file: Blob) => api<ChatConversation>(`chat/conversations/${id}/image`, { method: "PUT", raw: file });
export const removeGroupImage = (id: string) => del<void>(`chat/conversations/${id}/image`);
export const groupImageUrl = (c: { id: string; imageUpdatedAt?: string | null }) =>
	c.imageUpdatedAt ? apiUrl(`chat/conversations/${c.id}/image`, { v: c.imageUpdatedAt }) : "";
export const uploadAttachment = (conversationId: string, file: Blob, fileName: string) =>
	api<ChatAttachment>(`chat/conversations/${conversationId}/attachments`, { method: "POST", raw: file, query: { fileName } });
export const attachmentUrl = (id: string, options: { thumbnail?: boolean; download?: boolean } = {}) =>
	apiUrl(`chat/attachments/${id}/content`, { thumbnail: options.thumbnail || undefined, download: options.download || undefined });
export const media = (conversationId: string, kind?: AttachmentKind) => get<ChatAttachment[]>(`chat/conversations/${conversationId}/attachments`, { kind });
export const toBauakte = (attachmentId: string, bauakteId?: string | null, caption?: string) =>
	post<{ fileId: string; bauakteId: string }>(`chat/attachments/${attachmentId}/bauakte`, { bauakteId: bauakteId ?? null, caption: caption ?? null });
export const message = (id: string) => get<ChatMessage>(`chat/messages/${id}`);
export const editMessage = (id: string, text: string, mentionIds: string[]) => put<ChatMessage>(`chat/messages/${id}`, { text, mentionIds });
export const react = (id: string, emoji: string | null) => put<ChatMessage>(`chat/messages/${id}/reaction`, { emoji });
export const pin = (id: string, pinned: boolean) => post<void>(`chat/messages/${id}/pin`, { pinned });
export const forward = (id: string, conversationIds: string[]) => post<{ messages: ChatMessage[] }>(`chat/messages/${id}/forward`, { conversationIds });
export const search = (q: string, conversationId?: string) => get<ChatSearchResult>("chat/search", { q, conversationId });
export const mySettings = () => get<ChatMySettings>("chat/me/settings");
export const saveMySettings = (body: { showPreview?: boolean; readReceipts?: boolean; privacyAccepted?: boolean }) => put<ChatMySettings>("chat/me/settings", body);
export const bauakteGroup = (bauakteId: string) => get<ChatBauakteGroup>(`chat/bauakten/${bauakteId}/group`);
export const createBauakteGroup = (bauakteId: string) => post<ChatConversation>(`chat/bauakten/${bauakteId}/group`);
export const blockUser = (userId: string, blocked: boolean) => put<void>(`chat/admin/users/${userId}/block`, { blocked });
export const messages = (id: string, query: { before?: string; after?: string; limit?: number } = {}) =>
	get<ChatMessagePage>(`chat/conversations/${id}/messages`, query);
export const send = (id: string, body: SendBody) => post<ChatMessage>(`chat/conversations/${id}/messages`, body);
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
	const hint = m.attachment === "photo" ? "Foto" : m.attachment === "document" ? "Dokument" : m.location ? "Standort" : "";
	const text = m.deleted ? "Nachricht gelöscht" : m.text ?? (hint || "Nachricht nicht lesbar");
	if (m.mine) return `Sie: ${text}`;
	return c.kind === "group" ? `${m.senderName.split(" ")[0]}: ${text}` : text;
}

/** Häufige Reaktionen (Schnellauswahl). */
export const QUICK_REACTIONS = ["👍", "❤️", "😂", "😮", "🙏", "✅"];

/** Stummschalten: Dauer in Minuten (undefined = immer). */
export const MUTE_OPTIONS: { label: string; minutes?: number }[] = [
	{ label: "1 Stunde", minutes: 60 }, { label: "8 Stunden", minutes: 480 }, { label: "1 Woche", minutes: 10_080 }, { label: "Immer" },
];

function escapeHtml(value: string): string {
	return value.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
}

/**
 * Nachrichtentext als sicheres HTML: alles maskiert, nur http(s)-Links klickbar (neues Fenster, ohne Referrer) und
 * @Erwähnungen bekannter Namen hervorgehoben. Zeilenumbrüche bleiben (CSS white-space: pre-wrap).
 */
export function richText(text: string, mentionNames: string[] = []): string {
	const pattern = /\bhttps?:\/\/[^\s<>"']+[^\s<>"'.,;:!?)\]]/gi;
	let html = "";
	let last = 0;
	for (const match of text.matchAll(pattern)) {
		html += mentions(escapeHtml(text.slice(last, match.index)), mentionNames);
		const url = match[0];
		html += `<a href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer nofollow" class="eoChatLink">${escapeHtml(url)}</a>`;
		last = (match.index ?? 0) + url.length;
	}
	return html + mentions(escapeHtml(text.slice(last)), mentionNames);
}

function mentions(escaped: string, names: string[]): string {
	let out = escaped;
	for (const name of [...names].sort((a, b) => b.length - a.length)) {
		const safe = escapeHtml(`@${name}`);
		out = out.split(safe).join(`<span class="eoChatMention">${safe}</span>`);
	}
	return out;
}

/** IDs der im Text noch vorhandenen @Erwähnungen. */
export function mentionIdsIn(text: string, candidates: { id: string; name: string }[]): string[] {
	return candidates.filter((c) => text.includes(`@${c.name}`)).map((c) => c.id);
}

/** Angefangene Erwähnung direkt vor dem Cursor („@Lu“ → „Lu“), sonst null. */
export function mentionQuery(text: string, caret: number): string | null {
	const before = text.slice(0, caret);
	const match = /(?:^|\s)@([\p{L}\-]{0,20})$/u.exec(before);
	return match ? match[1] : null;
}

/** Dateigröße lesbar (de-DE). */
export function formatBytes(bytes: number): string {
	if (bytes < 1024) return `${bytes} B`;
	if (bytes < 1024 * 1024) return `${(bytes / 1024).toLocaleString("de-DE", { maximumFractionDigits: 0 })} KB`;
	return `${(bytes / 1024 / 1024).toLocaleString("de-DE", { maximumFractionDigits: 1 })} MB`;
}

/** Nur Fotos und PDFs; Größe begrenzt. Liefert eine Fehlermeldung oder null. */
export function checkFile(file: { name: string; type: string; size: number }, max: number): string | null {
	const ok = /^image\/(jpeg|png|webp|heic|heif)$/.test(file.type) || file.type === "application/pdf" || /\.(jpe?g|png|webp|heic|pdf)$/i.test(file.name);
	if (!ok) return `„${file.name}“: nur Fotos (JPEG, PNG, WebP, HEIC) und PDF.`;
	if (file.size > max) return `„${file.name}“ ist größer als ${formatBytes(max)}.`;
	return null;
}

/** Karte/Route zu einem Standort (OpenStreetMap, ohne Tracking). */
export function mapUrl(location: ChatLocation): string {
	const lat = location.latitude.toFixed(6);
	const lon = location.longitude.toFixed(6);
	return `https://www.openstreetmap.org/?mlat=${lat}&mlon=${lon}#map=17/${lat}/${lon}`;
}

/** Restzeit zum Bearbeiten (Minuten) oder 0. */
export function editMinutesLeft(createdAt: string, windowMinutes: number, now = Date.now()): number {
	const left = windowMinutes - (now - new Date(createdAt).getTime()) / 60_000;
	return left > 0 ? Math.ceil(left) : 0;
}

/**
 * Ereignisstrom des Chats (Server-Sent Events, Cookie-Anmeldung). Meldet `onEvent` je Ereignis und `onState`, ob der
 * Strom steht; verbindet nach Abbruch selbst neu (EventSource), ohne Strom bleibt das Polling zuständig.
 */
export interface ChatEvent { type: string; conversationId: string; messageId?: string | null; userId?: string | null; userName?: string | null }

export function connectEvents(onEvent: (event: ChatEvent) => void, onState: (live: boolean) => void): () => void {
	if (typeof EventSource === "undefined") {
		onState(false);
		return () => undefined;
	}
	const types = ["message", "message.updated", "message.deleted", "reaction", "read", "typing", "conversation", "conversation.removed"];
	let source: EventSource | null = null;
	let retry: ReturnType<typeof setTimeout> | undefined;
	let closed = false;
	const open = () => {
		source = new EventSource(apiUrl("chat/events"), { withCredentials: true });
		source.addEventListener("ready", () => onState(true));
		source.onerror = () => {
			onState(false);
			// Abgewiesen (z. B. Sitzung abgelaufen): EventSource gibt auf – später selbst neu verbinden.
			if (source?.readyState === EventSource.CLOSED && !closed) {
				clearTimeout(retry);
				retry = setTimeout(open, 15_000);
			}
		};
		for (const type of types) {
			source.addEventListener(type, (e) => {
				try {
					onEvent(JSON.parse((e as MessageEvent<string>).data) as ChatEvent);
				} catch {
					// Unlesbares Ereignis ignorieren – der nächste Abruf gleicht ab.
				}
			});
		}
	};
	open();
	return () => {
		closed = true;
		clearTimeout(retry);
		source?.close();
	};
}
