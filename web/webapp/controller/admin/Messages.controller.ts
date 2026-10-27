import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Dialog from "sap/m/Dialog";
import { get, post } from "../../util/api";
import { session } from "../../util/session";
import type { RouteMatch } from "../../util/router";
import {
	PUSH_STATE_STATES, PUSH_STATE_TEXTS, relativeTime, type BroadcastDetail, type BroadcastResponse, type NotificationAudiences,
} from "../../util/notifications";

interface Compose {
	mode: "pick" | "all";
	userIds: string[];
	roleIds: string[];
	crewIds: string[];
	title: string;
	body: string;
	urgency: "low" | "normal" | "high";
	link: string;
}

const empty = (): Compose => ({ mode: "pick", userIds: [], roleIds: [], crewIds: [], title: "", body: "", urgency: "normal", link: "" });

/**
 * Nachrichten senden (Recht „notifications.send“): an Personen, Rollen, Kolonnen oder alle; Verlauf mit Lese- und Zustellstatus.
 * @namespace estrichone.controller.admin
 */
export default class Messages extends BaseController {
	onInit(): void {
		this.local({ sent: [], busy: false, c: empty(), people: [], audiences: { roles: [], crews: [], activeUsers: 0 }, detail: {} });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		await this.load();
		if (match.query.get("neu")) {
			this.setQuery({ neu: null });
			void this.onNew();
		}
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const sent = await get<BroadcastResponse[]>("notifications/sent");
			m.setProperty("/sent", sent.map((b) => ({
				...b,
				when: relativeTime(b.createdAt),
				readPercent: b.recipients ? Math.round((b.read / b.recipients) * 100) : 0,
				readText: `${b.read} von ${b.recipients}`,
			})));
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onRefresh(): void {
		void this.load();
	}

	private dialog(id: string): Dialog {
		return this.byId(id) as Dialog;
	}

	async onNew(): Promise<void> {
		const m = this.model();
		m.setProperty("/c", empty());
		this.dialog("composeDialog").open();
		try {
			const [people, audiences] = await Promise.all([session.people(), get<NotificationAudiences>("notifications/audiences")]);
			m.setProperty("/people", people.filter((p) => p.isActive).sort((a, b) => a.displayName.localeCompare(b.displayName, "de")));
			m.setProperty("/audiences", audiences);
		} catch (error) {
			this.showError(error);
		}
	}

	onComposeCancel(): void {
		this.dialog("composeDialog").close();
	}

	onComposeClosed(): void {
		this.model().setProperty("/c", empty());
	}

	async onSendMessage(): Promise<void> {
		const c = this.model().getProperty("/c") as Compose;
		const dialog = this.dialog("composeDialog");
		dialog.setBusy(true);
		try {
			const sent = await post<BroadcastResponse>("notifications/send", {
				title: c.title.trim(), body: c.body.trim() || null, link: c.link || null, urgency: c.urgency, all: c.mode === "all",
				userIds: c.mode === "pick" ? c.userIds : [], roleIds: c.mode === "pick" ? c.roleIds : [], crewIds: c.mode === "pick" ? c.crewIds : [],
			});
			dialog.close();
			this.toast(`An ${sent.recipients} ${sent.recipients === 1 ? "Person" : "Personen"} gesendet`);
			await this.load();
			// Zustellung läuft im Hintergrund: kurz danach Status auffrischen.
			setTimeout(() => void this.load(), 4000);
		} catch (error) {
			this.showError(error);
		} finally {
			dialog.setBusy(false);
		}
	}

	async onDetail(event: Event): Promise<void> {
		const row = (event.getSource() as Control).getBindingContext("view")!.getObject() as BroadcastResponse;
		try {
			const detail = await get<BroadcastDetail>(`notifications/sent/${row.id}`);
			this.model().setProperty("/detail", {
				...detail.broadcast,
				list: detail.recipients.map((r) => ({
					...r, readText: r.readAt ? relativeTime(r.readAt) : "–", stateText: PUSH_STATE_TEXTS[r.pushState], state: PUSH_STATE_STATES[r.pushState],
				})),
			});
			this.dialog("detailDialog").open();
		} catch (error) {
			this.showError(error);
		}
	}

	onDetailClose(): void {
		this.dialog("detailDialog").close();
	}
}
