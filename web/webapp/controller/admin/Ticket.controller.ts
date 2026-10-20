import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageToast from "sap/m/MessageToast";
import { get, patch, post, problemMessage } from "../../util/api";
import { describeTicketEvent, options, ticketCategories, ticketPriorities, ticketStatus } from "../../util/labels";
import type { TicketDetail } from "../../util/models";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";

/** Ticket: Verlauf (öffentlich/intern), Diagnosedaten, Zuweisen, Status, Lösung. @namespace estrichone.controller.admin */
export default class Ticket extends BaseController {
	onInit(): void {
		this.local({ t: null, timeline: [], edit: {}, reply: "", internal: false, busy: false, attachments: [], diagnostics: [],
			statusOptions: options(ticketStatus), priorityOptions: options(ticketPriorities), categoryOptions: options(ticketCategories), people: [] });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const people = await session.people();
		this.model().setProperty("/people", [{ key: "", text: "Niemand" }, ...people.filter((p) => p.isActive).map((p) => ({ key: p.id, text: p.displayName }))]);
		const t = await this.run(() => get<TicketDetail>(`tickets/${match.params.id}`));
		if (t) this.apply(t);
		else this.nav("/it-center/helpdesk", true);
	}

	private apply(t: TicketDetail): void {
		const m = this.model();
		m.setProperty("/t", t);
		m.setProperty("/edit", { status: t.summary.status, priority: t.summary.priority, category: t.summary.category, assignee: t.summary.assigneeUserId ?? "", resolution: t.resolution ?? "" });
		const comments = t.comments.map((c) => ({ at: c.createdAt, who: c.authorName ?? "–", text: c.text, internal: c.isInternal }));
		const events = t.events.map((e) => ({ at: e.at, who: e.actorName ?? (e.kind === "created" ? t.summary.reporterDisplayName ?? "Gerät" : "System"),
			text: describeTicketEvent(e.kind, e.toValue), internal: false }));
		m.setProperty("/timeline", [...comments, ...events].sort((a, b) => a.at.localeCompare(b.at)));
		m.setProperty("/attachments", t.attachmentFileIds.map((id) => ({ id })));
		m.setProperty("/diagnostics", Object.entries(t.diagnostics ?? {}).map(([key, value]) => ({ key,
			value: Array.isArray(value) ? value.map((v) => (typeof v === "object" ? JSON.stringify(v) : String(v))).join("\n") : typeof value === "object" ? JSON.stringify(value) : String(value) })));
		document.title = `${t.summary.number} ${t.summary.title} – EstrichONE`;
	}

	async onSave(): Promise<void> {
		const m = this.model();
		const t = m.getProperty("/t") as TicketDetail;
		const e = m.getProperty("/edit") as { status: string; priority: string; category: string; assignee: string; resolution: string };
		try {
			this.apply(await patch<TicketDetail>(`tickets/${t.summary.id}`, {
				status: e.status !== t.summary.status ? e.status : null, priority: e.priority !== t.summary.priority ? e.priority : null,
				category: e.category !== t.summary.category ? e.category : null,
				assigneeUserId: e.assignee && e.assignee !== t.summary.assigneeUserId ? e.assignee : null,
				unassign: !e.assignee && !!t.summary.assigneeUserId, resolution: e.resolution || null,
			}));
			MessageToast.show("Ticket gespeichert");
		} catch (error) {
			MessageToast.show(problemMessage(error));
		}
	}

	onMine(): void {
		this.model().setProperty("/edit/assignee", session.userId);
		void this.onSave();
	}

	async onComment(): Promise<void> {
		const m = this.model();
		const t = m.getProperty("/t") as TicketDetail;
		const updated = await this.run(() => post<TicketDetail>(`tickets/${t.summary.id}/comments`, { text: m.getProperty("/reply"), isInternal: m.getProperty("/internal") }));
		if (updated) {
			this.apply(updated);
			m.setProperty("/reply", "");
			m.setProperty("/internal", false);
		}
	}

	onAttachment(event: Event): void {
		const id = ((event.getSource() as Control).getBindingContext("view")?.getObject() as { id: string }).id;
		window.open(`/api/v1/files/${id}/content`, "_blank", "noopener");
	}
}
