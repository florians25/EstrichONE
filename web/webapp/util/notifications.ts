/**
 * Postfach und Pushnachrichten (docs/benachrichtigungen.md): Typen, Abfragen und Web Push im Browser
 * (Service Worker /sw.js, VAPID-Schlüssel vom Server, Abo als Gerät „web“ registrieren).
 */
import { del, get, post } from "./api";

export type NotificationCategory = "message" | "ticket" | "approval" | "planning" | "bauakte" | "deadline" | "system";
export type NotificationUrgency = "low" | "normal" | "high";
export type PushState = "pending" | "sent" | "noDevice" | "inAppOnly" | "failed" | "expired";

export interface NotificationItem {
	id: string;
	category: NotificationCategory;
	urgency: NotificationUrgency;
	title: string;
	body: string | null;
	link: string | null;
	objectType: string | null;
	objectId: string | null;
	senderName: string | null;
	createdAt: string;
	readAt: string | null;
}

export interface NotificationInbox { items: NotificationItem[]; unreadCount: number }
export interface PushClientConfig { webPushEnabled: boolean; vapidPublicKey: string | null; apnsEnabled: boolean; showContent: boolean }

export interface PushDevice {
	id: string;
	platform: "ios" | "android" | "web";
	deviceName: string | null;
	appVersion: string | null;
	sandbox: boolean;
	createdAt: string;
	lastSeenAt: string;
	lastSuccessAt: string | null;
	lastError: string | null;
}

export interface AudienceOption { id: string; name: string; members: number }
export interface NotificationAudiences { roles: AudienceOption[]; crews: AudienceOption[]; activeUsers: number }

export interface BroadcastResponse {
	id: string;
	title: string;
	body: string | null;
	link: string | null;
	urgency: NotificationUrgency;
	audience: string;
	senderName: string;
	createdAt: string;
	recipients: number;
	read: number;
	pushed: number;
	inAppOnly: number;
	failed: number;
	pending: number;
}

export interface BroadcastRecipient { userId: string; name: string; readAt: string | null; pushState: PushState; pushedAt: string | null; pushError: string | null }
export interface BroadcastDetail { broadcast: BroadcastResponse; recipients: BroadcastRecipient[] }

export interface PushSettings {
	webPushEnabled: boolean;
	vapidPublicKey: string | null;
	vapidSubject: string;
	apnsEnabled: boolean;
	apnsKeyId: string | null;
	apnsTeamId: string | null;
	apnsBundleId: string | null;
	apnsProduction: boolean;
	apnsKeyStored: boolean;
	apnsFromEnvironment: boolean;
	apnsReady: boolean;
	showContent: boolean;
	webDevices: number;
	iosDevices: number;
	androidDevices: number;
	version: number;
}

export const CATEGORY_ICONS: Record<NotificationCategory, string> = {
	message: "sap-icon://message-popup",
	ticket: "sap-icon://headset",
	approval: "sap-icon://approvals",
	planning: "sap-icon://calendar",
	bauakte: "sap-icon://folder-full",
	deadline: "sap-icon://lateness",
	system: "sap-icon://bell",
};

export const CATEGORY_TEXTS: Record<NotificationCategory, string> = {
	message: "Nachricht", ticket: "Helpdesk", approval: "Freigabe", planning: "Planung", bauakte: "Bauakte", deadline: "Frist", system: "System",
};

export const PUSH_STATE_TEXTS: Record<PushState, string> = {
	pending: "Wird zugestellt", sent: "Push zugestellt", noDevice: "Nur Postfach (kein Gerät)", inAppOnly: "Nur Postfach",
	failed: "Push fehlgeschlagen", expired: "Abgelaufen",
};

export const PUSH_STATE_STATES: Record<PushState, "Success" | "Information" | "None" | "Warning" | "Error"> = {
	pending: "Information", sent: "Success", noDevice: "None", inAppOnly: "None", failed: "Error", expired: "Warning",
};

export const inbox = (query: { unread?: boolean; category?: string; limit?: number; before?: string } = {}) =>
	get<NotificationInbox>("notifications", query);
export const unreadCount = () => get<{ count: number }>("notifications/unread-count");
export const markRead = (id: string) => post<void>(`notifications/${id}/read`);
export const markUnread = (id: string) => post<void>(`notifications/${id}/unread`);
export const markAllRead = () => post<{ count: number }>("notifications/read-all");
export const removeNotification = (id: string) => del<void>(`notifications/${id}`);
export const sendTest = () => post<NotificationItem>("notifications/test");
export const myDevices = () => get<PushDevice[]>("notifications/devices");
export const removeDevice = (id: string) => del<void>(`notifications/devices/${id}`);

/** Zeit relativ („vor 5 Min.“, „gestern 14:10“, „Mo, 03.11.“). */
export function relativeTime(iso: string, now = new Date()): string {
	const at = new Date(iso);
	const minutes = Math.round((now.getTime() - at.getTime()) / 60_000);
	if (minutes < 1) return "gerade eben";
	if (minutes < 60) return `vor ${minutes} Min.`;
	const time = at.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });
	const day = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
	if (at.getTime() >= day) return `heute ${time}`;
	if (at.getTime() >= day - 86_400_000) return `gestern ${time}`;
	return at.toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit" }) + ` ${time}`;
}

// --- Web Push ---

export type WebPushStatus = "unsupported" | "insecure" | "disabled" | "denied" | "off" | "on";

const supported = () => "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;

function urlBase64ToUint8Array(base64: string): Uint8Array<ArrayBuffer> {
	const padded = (base64 + "=".repeat((4 - (base64.length % 4)) % 4)).replace(/-/g, "+").replace(/_/g, "/");
	const raw = atob(padded);
	const bytes = new Uint8Array(new ArrayBuffer(raw.length));
	for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
	return bytes;
}

async function registration(): Promise<ServiceWorkerRegistration> {
	return (await navigator.serviceWorker.getRegistration("/")) ?? navigator.serviceWorker.register("/sw.js", { scope: "/" });
}

function browserName(): string {
	const ua = navigator.userAgent;
	const browser = /Edg\//.test(ua) ? "Edge" : /Firefox\//.test(ua) ? "Firefox" : /Chrome\//.test(ua) ? "Chrome" : /Safari\//.test(ua) ? "Safari" : "Browser";
	const os = /Windows/.test(ua) ? "Windows" : /iPhone|iPad/.test(ua) ? "iOS" : /Mac OS/.test(ua) ? "macOS" : /Android/.test(ua) ? "Android" : /Linux/.test(ua) ? "Linux" : "";
	return os ? `${browser} unter ${os}` : browser;
}

export async function webPushStatus(): Promise<WebPushStatus> {
	if (!supported()) return "unsupported";
	if (!window.isSecureContext) return "insecure";
	const config = await get<PushClientConfig>("notifications/push-config").catch(() => null);
	if (!config?.webPushEnabled || !config.vapidPublicKey) return "disabled";
	if (Notification.permission === "denied") return "denied";
	const reg = await navigator.serviceWorker.getRegistration("/");
	const sub = await reg?.pushManager.getSubscription();
	return sub && Notification.permission === "granted" ? "on" : "off";
}

async function register(sub: PushSubscription): Promise<void> {
	const json = sub.toJSON();
	await post("notifications/devices", {
		platform: "web", token: sub.endpoint, p256dh: json.keys?.["p256dh"], auth: json.keys?.["auth"], deviceName: browserName(),
	});
}

/** Pushnachrichten in diesem Browser einschalten (fragt die Erlaubnis ab). */
export async function enableWebPush(): Promise<WebPushStatus> {
	const config = await get<PushClientConfig>("notifications/push-config");
	if (!config.webPushEnabled || !config.vapidPublicKey) return "disabled";
	const permission = await Notification.requestPermission();
	if (permission !== "granted") return permission === "denied" ? "denied" : "off";
	const reg = await registration();
	await navigator.serviceWorker.ready;
	const key = urlBase64ToUint8Array(config.vapidPublicKey);
	let sub = await reg.pushManager.getSubscription();
	// Neuer Serverschlüssel (z. B. nach Wiederherstellung): altes Abo ersetzen.
	const current = sub?.options.applicationServerKey ? new Uint8Array(sub.options.applicationServerKey) : null;
	if (sub && current && (current.length !== key.length || current.some((b, i) => b !== key[i]))) {
		await sub.unsubscribe();
		sub = null;
	}
	sub ??= await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
	await register(sub);
	return "on";
}

export async function disableWebPush(): Promise<WebPushStatus> {
	const reg = await navigator.serviceWorker.getRegistration("/");
	const sub = await reg?.pushManager.getSubscription();
	if (sub) {
		await del(`notifications/devices?token=${encodeURIComponent(sub.endpoint)}`).catch(() => undefined);
		await sub.unsubscribe();
	}
	return "off";
}

/** Beim Start: bestehendes Abo auffrischen (Benutzerwechsel, „zuletzt gesehen“). Still, ohne Rückfrage. */
export async function refreshWebPush(): Promise<void> {
	try {
		if (!supported() || !window.isSecureContext || Notification.permission !== "granted") return;
		const reg = await navigator.serviceWorker.getRegistration("/");
		const sub = await reg?.pushManager.getSubscription();
		if (sub) await register(sub);
	} catch {
		// Zusatz – nie stören.
	}
}
