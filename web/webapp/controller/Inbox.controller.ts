import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import {
	CATEGORY_ICONS, CATEGORY_TEXTS, inbox, markAllRead, markRead, markUnread, relativeTime, removeNotification, type NotificationItem,
} from "../util/notifications";
import { openNotificationPreferences } from "../util/notificationPreferences";

interface Row extends NotificationItem {
	when: string;
	icon: string;
	author: string;
}

const PAGE = 50;

/**
 * Postfach: alle Benachrichtigungen des Benutzers, filterbar nach ungelesen und Art, mit Gelesen/Ungelesen und Entfernen.
 * @namespace estrichone.controller
 */
export default class Inbox extends BaseController {
	onInit(): void {
		this.local({ items: [] as Row[], unread: 0, filter: "all", category: "", busy: false, more: false });
	}

	async onRouteMatched(): Promise<void> {
		await this.load();
	}

	private toRow(n: NotificationItem): Row {
		return { ...n, when: relativeTime(n.createdAt), icon: CATEGORY_ICONS[n.category], author: n.senderName ?? CATEGORY_TEXTS[n.category] };
	}

	private async load(append = false): Promise<void> {
		const m = this.model();
		const current = append ? (m.getProperty("/items") as Row[]) : [];
		m.setProperty("/busy", !append);
		try {
			const result = await inbox({
				unread: m.getProperty("/filter") === "unread" || undefined,
				category: (m.getProperty("/category") as string) || undefined,
				limit: PAGE,
				before: append ? current[current.length - 1]?.createdAt : undefined,
			});
			m.setProperty("/items", [...current, ...result.items.map((n) => this.toRow(n))]);
			m.setProperty("/unread", result.unreadCount);
			m.setProperty("/more", result.items.length === PAGE);
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private row(event: Event): Row {
		return (event.getSource() as Control).getBindingContext("view")!.getObject() as Row;
	}

	private changed(): void {
		document.dispatchEvent(new CustomEvent("estrichone-notices"));
	}

	onFilter(): void {
		void this.load();
	}

	onMore(): void {
		void this.load(true);
	}

	async onOpen(event: Event): Promise<void> {
		const row = this.row(event);
		if (!row.readAt) {
			await markRead(row.id).catch(() => undefined);
			this.changed();
		}
		if (row.link) this.nav(row.link);
		else void this.load();
	}

	async onToggleRead(event: Event): Promise<void> {
		const row = this.row(event);
		try {
			await (row.readAt ? markUnread(row.id) : markRead(row.id));
			this.changed();
			await this.load();
		} catch (error) {
			this.showError(error);
		}
	}

	async onDelete(event: Event): Promise<void> {
		const row = this.row(event);
		try {
			await removeNotification(row.id);
			this.changed();
			await this.load();
		} catch (error) {
			this.showError(error);
		}
	}

	async onReadAll(): Promise<void> {
		try {
			await markAllRead();
			this.changed();
			await this.load();
		} catch (error) {
			this.showError(error);
		}
	}

	onPreferences(): void {
		openNotificationPreferences();
	}

	onSend(): void {
		this.nav("/verwaltung/nachrichten?neu=1");
	}
}
