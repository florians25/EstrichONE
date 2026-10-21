import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageToast from "sap/m/MessageToast";
import { get, patch, post, problemMessage } from "../../util/api";
import { options, ticketCategories, ticketPriorities, ticketStatus } from "../../util/labels";
import type { FileResponse, TicketDetail } from "../../util/models";
import type { TicketTimelineItem } from "../../util/planningModels";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";
import { fileUrl, uploadLoose } from "../../util/looseUpload";

const icons: Record<string, string> = {
	created: "sap-icon://add-document", comment: "sap-icon://comment", note: "sap-icon://notes", status: "sap-icon://status-in-process",
	assignee: "sap-icon://employee", priority: "sap-icon://high-priority", category: "sap-icon://tag",
};
const kindTexts: Record<string, string> = {
	created: "Meldung", comment: "Antwort", note: "Interne Notiz", status: "Statuswechsel", assignee: "Zuweisung", priority: "Priorität", category: "Kategorie",
};

/**
 * Ticket (C6): Verlauf als Zeitleiste – Meldung, Antworten (mit Anhängen), interne Notizen, Status, Zuweisung,
 * Priorität und Kategorie; Antworten mit Dateien; Bearbeitung rechts (auf dem Handy darunter).
 * @namespace estrichone.controller.admin
 */
export default class Ticket extends BaseController {
	private files = new Map<string, FileResponse | null>();

	onInit(): void {
		this.local({ t: null, timeline: [], edit: {}, reply: "", internal: false, busy: false, diagnostics: [], pending: [], show: "all", me: "", agent: false,
			canUpload: false, statusOptions: options(ticketStatus), priorityOptions: options(ticketPriorities), categoryOptions: options(ticketCategories), people: [] });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		m.setProperty("/me", session.userId);
		m.setProperty("/canUpload", this.has("files.upload"));
		m.setProperty("/pending", []);
		const agent = this.has("helpdesk.agent");
		m.setProperty("/agent", agent);
		m.setProperty("/internal", false);
		if (agent) {
			const people = await session.people();
			m.setProperty("/people", [{ key: "", text: "Niemand" }, ...people.filter((p) => p.isActive).map((p) => ({ key: p.id, text: p.displayName }))]);
		}
		const t = await this.run(() => get<TicketDetail>(`tickets/${match.params.id}`));
		if (t) await this.apply(t);
		else this.nav("/it-center/helpdesk", true);
	}

	private async apply(t: TicketDetail): Promise<void> {
		const m = this.model();
		m.setProperty("/t", t);
		m.setProperty("/edit", { status: t.summary.status, priority: t.summary.priority, category: t.summary.category, assignee: t.summary.assigneeUserId ?? "", resolution: t.resolution ?? "" });
		m.setProperty("/diagnostics", Object.entries(t.diagnostics ?? {}).map(([key, value]) => ({ key,
			value: Array.isArray(value) ? value.map((v) => (typeof v === "object" ? JSON.stringify(v) : String(v))).join("\n") : typeof value === "object" ? JSON.stringify(value) : String(value) })));
		document.title = `${t.summary.number} ${t.summary.title} – EstrichONE`;
		const timeline = (t as TicketDetail & { timeline?: TicketTimelineItem[] | null }).timeline ?? [];
		// Dateinamen und -typen der Anhänge (für Vorschaubild oder Dokumentsymbol).
		const ids = [...new Set(timeline.flatMap((i) => i.attachmentFileIds))].filter((id) => !this.files.has(id));
		await Promise.all(ids.map(async (id) => this.files.set(id, await get<FileResponse>(`files/${id}`).catch(() => null))));
		this.timeline = timeline;
		this.renderTimeline();
	}

	private timeline: TicketTimelineItem[] = [];

	private renderTimeline(): void {
		const m = this.model();
		const talkOnly = m.getProperty("/show") === "talk";
		m.setProperty("/timeline", this.timeline
			.filter((i) => !talkOnly || ["created", "comment", "note"].includes(i.kind))
			.map((i) => {
				const bubble = ["created", "comment", "note"].includes(i.kind);
				return {
					...i, bubble, icon: icons[i.kind] ?? "sap-icon://history", kindText: kindTexts[i.kind] ?? i.kind,
					tone: i.kind, who: i.kind === "note" ? "note" : i.isReporter ? "reporter" : "agent",
					headline: i.kind === "created" ? `${i.actorName ?? "Melder"} hat gemeldet` : i.kind === "note" ? `${i.actorName ?? ""} · interne Notiz` : i.actorName ?? "",
					eventText: i.kind === "status" ? i.text : null,
					attachments: i.attachmentFileIds.map((id) => {
						const f = this.files.get(id);
						return { id, name: f?.originalName ?? "Anhang", image: !!f && f.contentType.startsWith("image/"), thumb: fileUrl(id, "thumb") };
					}),
				};
			}));
	}

	onShow(): void { this.renderTimeline(); }

	async onSave(): Promise<void> {
		const m = this.model();
		const t = m.getProperty("/t") as TicketDetail;
		const e = m.getProperty("/edit") as { status: string; priority: string; category: string; assignee: string; resolution: string };
		try {
			await this.apply(await patch<TicketDetail>(`tickets/${t.summary.id}`, {
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

	onAttach(): void {
		const input = document.createElement("input");
		input.type = "file";
		input.multiple = true;
		input.accept = "image/*,.pdf,.txt,.log,.zip,.doc,.docx,.xlsx";
		input.addEventListener("change", () => {
			const pending = this.model().getProperty("/pending") as { name: string; file: File }[];
			const added = [...(input.files ?? [])].map((file) => ({ name: file.name, file }));
			this.model().setProperty("/pending", [...pending, ...added].slice(0, 10));
		});
		input.click();
	}

	onRemovePending(e: Event): void {
		const item = (e.getSource() as Control).getBindingContext("view")?.getObject();
		const pending = this.model().getProperty("/pending") as object[];
		this.model().setProperty("/pending", pending.filter((p) => p !== item));
	}

	async onComment(): Promise<void> {
		const m = this.model();
		const t = m.getProperty("/t") as TicketDetail;
		const pending = m.getProperty("/pending") as { name: string; file: File }[];
		const updated = await this.run(async () => {
			const attachmentFileIds: string[] = [];
			for (const p of pending) attachmentFileIds.push(await uploadLoose(p.file));
			return post<TicketDetail>(`tickets/${t.summary.id}/comments`, { text: m.getProperty("/reply"), isInternal: m.getProperty("/internal"), attachmentFileIds });
		});
		if (updated) {
			await this.apply(updated);
			m.setProperty("/reply", "");
			m.setProperty("/internal", false);
			m.setProperty("/pending", []);
		}
	}

	async onReopen(): Promise<void> {
		const t = this.model().getProperty("/t") as TicketDetail;
		const updated = await this.run(() => post<TicketDetail>(`tickets/${t.summary.id}/reopen`, {}));
		if (updated) await this.apply(updated);
	}

	onAttachment(event: Event): void {
		const id = ((event.getSource() as Control).getBindingContext("view")?.getObject() as { id: string }).id;
		window.open(fileUrl(id), "_blank", "noopener");
	}
}
