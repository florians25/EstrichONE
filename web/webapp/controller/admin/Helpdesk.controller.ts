import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type MenuItem from "sap/m/MenuItem";
import type { Menu$ItemSelectedEvent } from "sap/m/Menu";
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import { get, patch, post } from "../../util/api";
import { textBlocks } from "../../util/catalog";
import { waitingSince } from "../../util/format";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { options, ticketCategories, ticketPriorities, ticketStatus } from "../../util/labels";
import { fileUrl, uploadLoose } from "../../util/looseUpload";
import type { FileResponse, TicketDetail, TicketSummary } from "../../util/models";
import type { TicketTimelineItem } from "../../util/planningModels";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";

type Summary = TicketSummary & { hasUnread?: boolean };

const icons: Record<string, string> = {
	created: "sap-icon://add-document", comment: "sap-icon://comment", note: "sap-icon://notes", status: "sap-icon://status-in-process",
	assignee: "sap-icon://employee", priority: "sap-icon://high-priority", category: "sap-icon://tag",
};
const kindTexts: Record<string, string> = {
	created: "Meldung", comment: "Antwort", note: "Interne Notiz", status: "Statuswechsel", assignee: "Zuweisung", priority: "Priorität", category: "Kategorie",
};
/** Reaktionsziel je Priorität (Stunden bis zur ersten Antwort der IT). */
const responseHours: Record<string, number> = { urgent: 2, high: 8, normal: 24, low: 72 };
const priorityRank: Record<string, number> = { urgent: 0, high: 1, normal: 2, low: 3 };
const done = (s: string): boolean => s === "resolved" || s === "closed";

/**
 * IT Center › Helpdesk als Master-Detail: links die Liste mit Suche, Status-Segmenten, Kategorie und Sortierung
 * (neue Antworten markiert), rechts das Ticket mit Kopf (Status, Priorität, Kategorie, Melder, Zuständig, Alter,
 * Reaktionsziel), Schnellaktionen, Verlauf als Zeitleiste und Antwortfeld mit Anhängen (Ziehen, Strg+V) und
 * Textbausteinen. Bearbeitung sieht alle Tickets, alle anderen ihre eigenen („Meine Tickets“).
 * @namespace estrichone.controller.admin
 */
export default class Helpdesk extends BaseController {
	private files = new Map<string, FileResponse | null>();
	private timeline: TicketTimelineItem[] = [];
	private loadedAt = 0;
	private loadedFor = "";

	onInit(): void {
		this.local({ items: [], visible: [], filter: "open", category: "", sort: "waiting", query: "", busy: false, agent: false, segments: [],
			categories: [{ key: "", text: "Alle Kategorien" }, ...options(ticketCategories)], noData: "", countText: "", selectedId: "",
			t: null, timeline: [], facts: [], edit: {}, reply: "", internal: false, detailBusy: false, diagnostics: [], pending: [], show: "all",
			me: "", canUpload: false, blocks: [], resolvedText: "",
			statusOptions: options(ticketStatus), priorityOptions: options(ticketPriorities), categoryOptions: options(ticketCategories), people: [] });
		// Das Antwortfeld wird erst mit einem Ticket gezeichnet: Ziehen/Einfügen danach anbinden.
		this.byId("composer")?.addEventDelegate({ onAfterRendering: () => this.wireComposer() });
	}


	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		const agent = this.has("helpdesk.agent");
		const switched = this.loadedFor !== session.userId;
		m.setProperty("/agent", agent);
		m.setProperty("/me", session.userId);
		m.setProperty("/canUpload", this.has("files.upload"));
		if (switched) {
			this.loadedFor = session.userId ?? "";
			m.setProperty("/filter", "open");
			m.setProperty("/segments", agent
				? [{ key: "open", text: "Offen" }, { key: "mine", text: "Mir" }, { key: "unassigned", text: "Neu/frei" }, { key: "all", text: "Alle" }]
				: [{ key: "open", text: "Offen" }, { key: "done", text: "Erledigt" }, { key: "all", text: "Alle" }]);
			if (agent) {
				void session.people().then((people) => m.setProperty("/people", [{ key: "none", text: "Niemand" },
					...people.filter((p) => p.isActive).map((p) => ({ key: p.id, text: p.displayName }))]));
				void textBlocks("helpdesk").then((blocks) => m.setProperty("/blocks", blocks));
			}
		}
		const id = match.params.id ?? "";
		m.setProperty("/selectedId", id);
		// Liste nur neu laden, wenn sie älter als 30 Sekunden ist (Wechsel zwischen Tickets ohne Flackern).
		if (switched || Date.now() - this.loadedAt > 30_000 || !id) void this.load();
		if (id) await this.open(id);
		else this.clearDetail();
	}

	// ---- Liste ------------------------------------------------------------------------------------------------------

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const filter = m.getProperty("/filter") as string;
			const items = m.getProperty("/agent")
				? await get<Summary[]>("tickets", { filter, category: m.getProperty("/category") || undefined })
				: await get<Summary[]>("tickets/mine");
			this.loadedAt = Date.now();
			m.setProperty("/items", items);
			this.applyList();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private applyList(): void {
		const m = this.model();
		const agent = m.getProperty("/agent") as boolean;
		const filter = m.getProperty("/filter") as string;
		const category = m.getProperty("/category") as string;
		const q = String(m.getProperty("/query") ?? "").trim().toLowerCase();
		const sort = m.getProperty("/sort") as string;
		let list = (m.getProperty("/items") as Summary[]).slice();
		if (!agent) {
			list = list.filter((t) => filter === "all" || (filter === "done" ? done(t.status) : !done(t.status)));
			if (category) list = list.filter((t) => t.category === category);
		}
		if (q) {
			list = list.filter((t) => q.split(/\s+/).every((w) =>
				[t.number, t.title, t.reporterDisplayName, t.assigneeName, ticketCategories[t.category]].join(" ").toLowerCase().includes(w)));
		}
		const by: Record<string, (a: Summary, b: Summary) => number> = {
			waiting: (a, b) => Number(b.reporterBlocked) - Number(a.reporterBlocked) || a.lastActivityAt.localeCompare(b.lastActivityAt),
			recent: (a, b) => b.lastActivityAt.localeCompare(a.lastActivityAt),
			priority: (a, b) => (priorityRank[a.priority] ?? 9) - (priorityRank[b.priority] ?? 9) || a.lastActivityAt.localeCompare(b.lastActivityAt),
			number: (a, b) => b.number.localeCompare(a.number, "de", { numeric: true }),
		};
		list.sort(by[sort] ?? by.waiting);
		m.setProperty("/visible", list.map((t) => ({ ...t,
			who: agent ? `${t.reporterDisplayName ?? "–"} · ${t.assigneeName ? `bei ${t.assigneeName}` : "nicht zugewiesen"}` : t.assigneeName ? `bei ${t.assigneeName}` : "noch nicht zugewiesen" })));
		const unread = list.filter((t) => t.hasUnread).length;
		m.setProperty("/countText", `${list.length} ${list.length === 1 ? "Ticket" : "Tickets"}${unread ? ` · ${unread} mit neuer Antwort` : ""}`);
		m.setProperty("/noData", q ? `Keine Tickets zu „${q}“.` : agent ? "Keine Tickets in dieser Ansicht." : "Sie haben noch kein Ticket gemeldet.");
	}

	onFilter(): void {
		if (this.model().getProperty("/agent")) void this.load();
		else this.applyList();
	}

	onSort(): void { this.applyList(); }

	onQuery(): void { this.applyList(); }

	onSelect(event: Event): void {
		const item = (event as Event<{ listItem: Control }>).getParameter("listItem");
		const t = item?.getBindingContext("view")?.getObject() as Summary | undefined;
		if (t) this.nav(`/it-center/helpdesk/${t.id}`);
	}

	/** Zeile in der Liste nach einer Änderung aktualisieren (ohne die ganze Liste neu zu laden). */
	private patchRow(s: Summary): void {
		const m = this.model();
		const items = (m.getProperty("/items") as Summary[]).map((t) => (t.id === s.id ? { ...s, hasUnread: false } : t));
		if (!items.some((t) => t.id === s.id)) items.unshift({ ...s, hasUnread: false });
		m.setProperty("/items", items);
		this.applyList();
	}

	// ---- Ticket -----------------------------------------------------------------------------------------------------

	private clearDetail(): void {
		const m = this.model();
		m.setProperty("/t", null);
		m.setProperty("/timeline", []);
		m.setProperty("/facts", []);
		document.title = "Helpdesk – EstrichONE";
	}

	private async open(id: string): Promise<void> {
		const m = this.model();
		if ((m.getProperty("/t") as TicketDetail | null)?.summary.id !== id) {
			m.setProperty("/reply", "");
			m.setProperty("/pending", []);
			m.setProperty("/internal", false);
		}
		m.setProperty("/detailBusy", true);
		try {
			await this.apply(await get<TicketDetail>(`tickets/${id}`));
			// Abruf markiert als gelesen: Zähler in der Kopfleiste aktualisieren.
			document.dispatchEvent(new CustomEvent("estrichone-notices"));
		} catch (error) {
			this.showError(error);
			this.nav("/it-center/helpdesk", true);
		} finally {
			m.setProperty("/detailBusy", false);
		}
	}

	private async apply(t: TicketDetail): Promise<void> {
		const m = this.model();
		const s = t.summary;
		m.setProperty("/t", t);
		m.setProperty("/edit", { status: s.status, priority: s.priority, category: s.category, assignee: s.assigneeUserId ?? "none" });
		m.setProperty("/resolvedText", `Die IT hat das Ticket als gelöst markiert${t.resolution ? `: ${t.resolution}` : "."} Besteht das Problem weiter?`);
		m.setProperty("/diagnostics", Object.entries(t.diagnostics ?? {}).map(([key, value]) => ({ key,
			value: Array.isArray(value) ? value.map((v) => (typeof v === "object" ? JSON.stringify(v) : String(v))).join("\n") : typeof value === "object" ? JSON.stringify(value) : String(value) })));
		document.title = `${s.number} ${s.title} – EstrichONE`;
		const timeline = (t as TicketDetail & { timeline?: TicketTimelineItem[] | null }).timeline ?? [];
		const ids = [...new Set(timeline.flatMap((i) => i.attachmentFileIds))].filter((id) => !this.files.has(id));
		await Promise.all(ids.map(async (id) => this.files.set(id, await get<FileResponse>(`files/${id}`).catch(() => null))));
		this.timeline = timeline;
		m.setProperty("/facts", this.facts(t, timeline));
		this.renderTimeline();
		this.patchRow(s);
		setTimeout(() => this.scrollToEnd(), 0);
	}

	/** Kopf-Fakten: Kategorie, Melder, Zuständig, Alter, Reaktionsziel (erste Antwort der IT), Quelle, Gerät/Bauakte. */
	private facts(t: TicketDetail, timeline: TicketTimelineItem[]): { label: string; value: string; state: string; icon: string }[] {
		const s = t.summary;
		const firstAnswer = timeline.find((i) => i.kind === "comment" && !i.isReporter);
		const target = (responseHours[s.priority] ?? 24) * 3_600_000;
		const created = new Date(s.createdAt).getTime();
		const sla = done(s.status) ? { value: "erledigt", state: "Success" }
			: firstAnswer ? { value: new Date(firstAnswer.at).getTime() - created <= target ? "eingehalten" : "verspätet beantwortet", state: new Date(firstAnswer.at).getTime() - created <= target ? "Success" : "Warning" }
				: Date.now() - created > target ? { value: `überschritten (Ziel ${responseHours[s.priority] ?? 24} h)`, state: "Error" }
					: { value: `Antwort bis ${new Date(created + target).toLocaleString("de-DE", { weekday: "short", hour: "2-digit", minute: "2-digit" })}`, state: "None" };
		const sources: Record<string, string> = { web: "Web", desktop: "Windows", ios: "iOS", android: "Android" };
		return [
			{ label: "Kategorie", value: ticketCategories[s.category] ?? s.category, state: "None", icon: "" },
			{ label: "Melder", value: s.reporterDisplayName ?? "–", state: "None", icon: "" },
			{ label: "Zuständig", value: s.assigneeName ?? "nicht zugewiesen", state: s.assigneeName ? "None" : "Warning", icon: "" },
			{ label: "Gemeldet", value: `${waitingSince(s.createdAt)} · ${sources[s.source] ?? s.source}`, state: "None", icon: "" },
			{ label: "Reaktionsziel", value: sla.value, state: sla.state, icon: "" },
			...(t.deviceName ? [{ label: "Gerät", value: t.deviceName, state: "None", icon: "" }] : []),
		];
	}

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

	private scrollToEnd(): void {
		const dom = this.byId("hdScroll")?.getDomRef() as HTMLElement | null;
		if (dom) dom.scrollTop = dom.scrollHeight;
	}

	onShow(): void { this.renderTimeline(); }

	private ticket(): TicketDetail {
		return this.model().getProperty("/t") as TicketDetail;
	}

	private async update(body: Record<string, unknown>, message: string): Promise<void> {
		const t = this.ticket();
		const updated = await this.run(() => patch<TicketDetail>(`tickets/${t.summary.id}`, { unassign: false, ...body }));
		if (updated) {
			await this.apply(updated);
			this.toast(message);
		} else {
			await this.apply(t); // Auswahl zurücksetzen
		}
	}

	async onQuickStatus(): Promise<void> {
		const status = this.model().getProperty("/edit/status") as string;
		const t = this.ticket();
		if (status === t.summary.status) return;
		if (done(status)) {
			// Lösung ist Pflicht: kurz im Dialog erfassen (Textbausteine als Vorlage).
			const blocks = (this.model().getProperty("/blocks") as { name: string; content: { text: string } }[]) ?? [];
			const saved = await openForm<TicketDetail>({
				title: status === "resolved" ? "Als gelöst markieren" : "Ticket schließen", width: "34rem", submitText: "Speichern",
				intro: "Die Lösung sieht der Melder im Verlauf – kurz und verständlich beschreiben.",
				initial: { resolution: t.resolution ?? "" },
				fields: [
					...(blocks.length ? [{ key: "block", label: "Textbaustein", type: "select" as const, options: [{ key: "", text: "–" }, ...blocks.map((b, i) => ({ key: String(i), text: b.name }))],
						onChange: (value: unknown, form: { set(key: string, v: unknown): void }) => { if (value !== "") form.set("resolution", blocks[Number(value)]?.content.text ?? ""); } }] : []),
					{ key: "resolution", label: "Lösung", type: "textarea", rows: 4, required: true, maxLength: 8000 },
				],
				submit: (v) => patch<TicketDetail>(`tickets/${t.summary.id}`, { status, resolution: v.resolution, unassign: false }),
			});
			if (saved) {
				await this.apply(saved);
				this.toast("Ticket gespeichert");
			} else {
				this.model().setProperty("/edit/status", t.summary.status);
			}
			return;
		}
		await this.update({ status }, `Status: ${ticketStatus[status as keyof typeof ticketStatus]?.text ?? status}`);
	}

	async onQuickPriority(): Promise<void> {
		const priority = this.model().getProperty("/edit/priority") as string;
		if (priority !== this.ticket().summary.priority) await this.update({ priority }, "Priorität geändert");
	}

	async onQuickCategory(): Promise<void> {
		const category = this.model().getProperty("/edit/category") as string;
		if (category !== this.ticket().summary.category) await this.update({ category }, "Kategorie geändert");
	}

	async onQuickAssignee(): Promise<void> {
		const picked = this.model().getProperty("/edit/assignee") as string;
		const assignee = picked === "none" ? "" : picked;
		const t = this.ticket();
		if ((assignee || null) === t.summary.assigneeUserId) return;
		await this.update(assignee ? { assigneeUserId: assignee } : { unassign: true }, assignee ? "Zugewiesen" : "Zuweisung entfernt");
	}

	async onMine(): Promise<void> {
		this.model().setProperty("/edit/assignee", session.userId);
		await this.onQuickAssignee();
	}

	async onReopen(): Promise<void> {
		const t = this.ticket();
		const updated = await this.run(() => post<TicketDetail>(`tickets/${t.summary.id}/reopen`, {}));
		if (updated) await this.apply(updated);
	}

	// ---- Antworten ---------------------------------------------------------------------------------------------------

	private addFiles(files: Iterable<File>): void {
		if (!this.model().getProperty("/canUpload")) return;
		const pending = this.model().getProperty("/pending") as { name: string; file: File }[];
		const added = [...files].map((file, i) => ({
			name: file.name && file.name !== "image.png" ? file.name : `Bildschirmfoto-${pending.length + i + 1}.png`, file }));
		this.model().setProperty("/pending", [...pending, ...added].slice(0, 10));
	}

	/** Antwortfeld: Dateien hineinziehen, Bildschirmfotos mit Strg+V, Senden mit Strg+Enter. */
	private wireComposer(): void {
		const dom = this.byId("composer")?.getDomRef() as HTMLElement | null;
		if (!dom || dom.dataset.eoWired) return;
		dom.dataset.eoWired = "1";
		dom.addEventListener("dragover", (e) => {
			if (!e.dataTransfer?.types.includes("Files")) return;
			e.preventDefault();
			dom.classList.add("eoDropActive");
		});
		dom.addEventListener("dragleave", (e) => { if (!dom.contains(e.relatedTarget as Node)) dom.classList.remove("eoDropActive"); });
		dom.addEventListener("drop", (e) => {
			dom.classList.remove("eoDropActive");
			if (!e.dataTransfer?.files.length) return;
			e.preventDefault();
			this.addFiles(e.dataTransfer.files);
		});
		dom.addEventListener("paste", (e) => {
			const images = [...(e.clipboardData?.files ?? [])].filter((f) => f.type.startsWith("image/"));
			if (images.length) {
				e.preventDefault();
				this.addFiles(images);
			}
		});
		dom.addEventListener("keydown", (e) => {
			if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
				e.preventDefault();
				void this.onComment();
			}
		});
	}

	onAttach(): void {
		const input = document.createElement("input");
		input.type = "file";
		input.multiple = true;
		input.accept = "image/*,.pdf,.txt,.log,.zip,.doc,.docx,.xlsx";
		input.addEventListener("change", () => this.addFiles(input.files ?? []));
		input.click();
	}

	onRemovePending(e: Event): void {
		const item = (e.getSource() as Control).getBindingContext("view")?.getObject();
		const pending = this.model().getProperty("/pending") as object[];
		this.model().setProperty("/pending", pending.filter((p) => p !== item));
	}

	onTextBlock(event: Menu$ItemSelectedEvent): void {
		const text = String((event.getParameter("item") as MenuItem).data("text") ?? "");
		const m = this.model();
		const reply = String(m.getProperty("/reply") ?? "");
		const name = this.ticket().summary.reporterDisplayName?.replace(/ \(laut Eingabe\)$/, "") ?? "";
		m.setProperty("/reply", (reply ? `${reply.trimEnd()}\n\n` : "") + text.replace(/\{Melder\}/g, name).replace(/\{Ticket\}/g, this.ticket().summary.number));
	}

	async onComment(): Promise<void> {
		const m = this.model();
		const t = this.ticket();
		const pending = m.getProperty("/pending") as { name: string; file: File }[];
		const text = String(m.getProperty("/reply") ?? "");
		if (!text.trim() && !pending.length) return;
		const updated = await this.run(async () => {
			const attachmentFileIds: string[] = [];
			for (const p of pending) attachmentFileIds.push(await uploadLoose(p.file));
			return post<TicketDetail>(`tickets/${t.summary.id}/comments`, { text, isInternal: m.getProperty("/internal"), attachmentFileIds });
		});
		if (updated) {
			m.setProperty("/reply", "");
			m.setProperty("/internal", false);
			m.setProperty("/pending", []);
			await this.apply(updated);
		}
	}

	onAttachment(event: Event): void {
		const id = ((event.getSource() as Control).getBindingContext("view")?.getObject() as { id: string }).id;
		window.open(fileUrl(id), "_blank", "noopener");
	}

	// ---- Neues Ticket ------------------------------------------------------------------------------------------------

	async onNew(): Promise<void> {
		const files: File[] = [];
		const canUpload = this.has("files.upload");
		const list = new Text({ text: "" });
		const show = (): void => { list.setText(files.map((f) => f.name).join(", ")); };
		const add = (added: Iterable<File>): void => { files.push(...added); files.splice(10); show(); };
		const paste = (e: ClipboardEvent): void => {
			const images = [...(e.clipboardData?.files ?? [])].filter((f) => f.type.startsWith("image/"));
			if (images.length) add(images.map((f, i) => new File([f], f.name && f.name !== "image.png" ? f.name : `Bildschirmfoto-${files.length + i + 1}.png`, { type: f.type })));
		};
		if (canUpload) document.addEventListener("paste", paste);
		try {
			const saved = await openForm<TicketDetail>({
				title: "Neues IT-Ticket", width: "36rem",
				intro: "Beschreiben Sie das Problem möglichst genau – die IT meldet sich im Verlauf des Tickets.",
				initial: { category: "other", priority: "normal", reporterBlocked: false },
				fields: [
					{ key: "title", label: "Kurzbeschreibung", required: true, maxLength: 200, placeholder: "z. B. Drucker im Büro druckt nicht" },
					{ key: "category", label: "Kategorie", type: "select", required: true, options: options(ticketCategories) },
					{ key: "priority", label: "Priorität", type: "select", required: true, options: options(ticketPriorities) },
					{ key: "description", label: "Beschreibung", type: "textarea", rows: 6, maxLength: 8000,
						placeholder: "Was ist passiert? Seit wann? Fehlermeldung? Was haben Sie schon versucht?" },
					{ key: "reporterBlocked", label: "Ich kann deswegen nicht arbeiten", type: "checkbox" },
				],
				extra: canUpload ? () => [new Button({
					text: "Anhang oder Bildschirmfoto", icon: "sap-icon://attachment",
					tooltip: "Dateien wählen – Bildschirmfotos auch mit Strg+V einfügen",
					press: () => {
						const input = document.createElement("input");
						input.type = "file";
						input.multiple = true;
						input.accept = "image/*,.pdf,.txt,.log,.zip,.doc,.docx,.xlsx";
						input.addEventListener("change", () => add(input.files ?? []));
						input.click();
					},
				}), list] : undefined,
				submitText: "Ticket anlegen",
				submit: async (v) => {
					const attachmentFileIds: string[] = [];
					for (const f of files) attachmentFileIds.push(await uploadLoose(f));
					return post<TicketDetail>("tickets", {
						id: uuidv7(), title: v.title, description: v.description || null, category: v.category, priority: v.priority,
						source: "web", reporterBlocked: !!v.reporterBlocked, attachmentFileIds,
					});
				},
			});
			if (saved) {
				this.toast(`Ticket ${saved.summary.number} angelegt`);
				this.patchRow(saved.summary);
				this.nav(`/it-center/helpdesk/${saved.summary.id}`);
			}
		} finally {
			document.removeEventListener("paste", paste);
		}
	}
}
