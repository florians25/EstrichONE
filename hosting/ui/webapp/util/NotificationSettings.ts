import JSONModel from "sap/ui/model/json/JSONModel";
import { get, post, put } from "../model/Api";

/** Gemeinsame Logik für das Formular „Benachrichtigungen“ (Einrichtungsassistent und Einstellungen). */
export async function loadNotifications(model: JSONModel): Promise<void> {
	model.setData({ ...(await get<object>("/api/settings/notifications")), smtpPassword: "" });
}

export function notificationBody(model: JSONModel): object {
	const d = model.getData();
	return {
		emailTo: d.emailTo, smtpHost: d.smtpHost, smtpPort: Number(d.smtpPort) || 587, smtpUser: d.smtpUser, smtpPassword: d.smtpPassword,
		smtpFrom: d.smtpFrom, smtpStartTls: d.smtpStartTls !== false, webhookUrl: d.webhookUrl
	};
}

export async function saveNotifications(model: JSONModel): Promise<void> {
	model.setData({ ...(await put<object>("/api/settings/notifications", notificationBody(model))), smtpPassword: "" });
}

export async function testNotifications(): Promise<string> {
	return (await post<{ message: string }>("/api/settings/notifications/test")).message;
}
