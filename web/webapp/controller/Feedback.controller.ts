import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type SimpleForm from "sap/ui/layout/form/SimpleForm";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import { get, patch, post } from "../util/api";
import { openForm } from "../util/forms";
import { formTitle } from "../util/formLayout";
import { fileUrl, uploadLoose } from "../util/looseUpload";
import { openFeedbackWizard } from "../util/feedbackWizard";
import { waitingSince } from "../util/format";
import type { RouteMatch } from "../util/router";
import { session } from "../util/session";
import {
	contextRows, detailRows, feedbackAreas, feedbackKinds, feedbackPriorities, feedbackStatus, filterFeedback, flowProgress, impactShort, isClosed,
	normalizeNumber, sortFeedback, type FeedbackDetail, type FeedbackListResponse, type FeedbackStatus, type FeedbackSummary, type FeedbackTimelineItem,
	type ListFilter,
} from "../util/feedback";

const icons: Record<string, string> = {
	created: "sap-icon://add-document", comment: "sap-icon://comment", note: "sap-icon://notes", status: "sap-icon://status-in-process",
	assignee: "sap-icon://employee", priority: "sap-icon://high-priority", classify: "sap-icon://tag",
};
const kindTexts: Record<string, string> = {
	created: "Meldung", comment: "Antwort", note: "Interne Notiz", status: "Statuswechsel", assignee: "Zuständigkeit", priority: "Priorität", classify: "Einordnung",
};
const opts = <T extends string>(map: Record<T, { text: string }>) => (Object.keys(map) as T[]).map((key) => ({ key, text: map[key].text }));

/**
 * Meldungen (Fehler und Funktionswünsche) als Master-Detail – zwei Seiten mit derselben Ansicht:
 * „Meine Meldungen“ (/meine-meldungen, jeder Benutzer: eigene Meldungen, Status, Verlauf, antworten) und
 * IT Center › Fehler &amp; Wünsche (/it-center/meldungen, Recht feedback.manage: alle Meldungen mit Filtern und Zählern,
 * Status, Priorität, Zuständigkeit, Duplikat-Verweis, Antworten und interne Notizen).
 * @namespace estrichone.controller
 */
export default class Feedback extends BaseController {
	private loadedFor = "";
	private loadedAt = 0;
	private timeline: FeedbackTimelineItem[] = [];
	private shownId = "";

	onInit(): void {
		this.local({
			manage: false, base: "/meine-meldungen", items: [], visible: [], busy: false, noData: "", countText: "", selectedId: "", counters: [],
			filter: { kind: "", status: "open", area: "", priority: "", query: "" } as ListFilter,
			statusFilters: [{ key: "open", text: "Offene" }, ...opts(feedbackStatus), { key: "closed", text: "Abgeschlossene" }, { key: "all", text: "Alle Status" }],
			areaFilters: [{ key: "", text: "Alle Bereiche" }],
			priorityFilters: [{ key: "", text: "Alle Prioritäten" }, ...opts(feedbackPriorities)],
			statusOptions: opts(feedbackStatus), priorityOptions: opts(feedbackPriorities), people: [],
			d: null, head: {}, facts: [], attachments: [], context: [], timeline: [], show: "all", edit: {}, me: "",
			reply: "", internal: false, pending: [], canUpload: false, detailBusy: false,
		});
		this.byId("fbComposer")?.addEventDelegate({ onAfterRendering: () => this.wireComposer() });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		const manage = match.route.name.startsWith("feedbackAdmin");
		const key = `${session.userId ?? ""}:${manage}`;
		const switched = this.loadedFor !== key;
		m.setProperty("/manage", manage);
		m.setProperty("/base", manage ? "/it-center/meldungen" : "/meine-meldungen");
		m.setProperty("/me", session.userId);
		m.setProperty("/canUpload", this.has("files.upload"));
		if (switched) {
			this.loadedFor = key;
			m.setProperty("/items", []);
			m.setProperty("/visible", []);
			m.setProperty("/filter", { kind: "", status: "open", area: "", priority: "", query: "" });
			this.clearDetail();
			if (manage) {
				void session.people().then((people) => m.setProperty("/people", [{ key: "none", text: "Niemand" },
					...people.filter((p) => p.isActive).map((p) => ({ key: p.id, text: p.displayName }))]));
			}
		}
		// Neue Meldung über ?neu=1 (z. B. aus dem Benutzermenü einer anderen Seite).
		if (match.query.get("neu")) {
			this.setQuery({ neu: null });
			void this.onNew();
		}
		const id = match.params.id ?? "";
		m.setProperty("/selectedId", id);
		if (switched || Date.now() - this.loadedAt > 30_000 || !id) void this.load();
		if (id) await this.open(id);
		else this.clearDetail();
	}

	// ---- Liste ------------------------------------------------------------------------------------------------------

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			if (m.getProperty("/manage")) {
				const filter = m.getProperty("/filter") as ListFilter;
				const result = await get<FeedbackListResponse>("feedback", {
					status: filter.status || "open", kind: filter.kind || undefined, area: filter.area || undefined, priority: filter.priority || undefined,
				});
				m.setProperty("/items", result.items);
				const c = result.counts;
				m.setProperty("/counters", [
					{ key: "open", text: "Offen", value: c.open, state: "Information" },
					{ key: "new", text: "Neu", value: c.new, state: c.new ? "Warning" : "None" },
					{ key: "bug", text: "Fehler", value: c.openBugs, state: c.openBugs ? "Error" : "None" },
					{ key: "feature", text: "Wünsche", value: c.openFeatures, state: "None" },
				]);
				const known = new Set([...feedbackAreas.map((a) => a.name), ...result.areas]);
				m.setProperty("/areaFilters", [{ key: "", text: "Alle Bereiche" }, ...[...known].filter((a) => result.areas.includes(a) || a === filter.area)
					.map((a) => ({ key: a, text: a }))]);
			} else {
				m.setProperty("/items", await get<FeedbackSummary[]>("feedback/mine"));
			}
			this.loadedAt = Date.now();
			this.applyList();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private applyList(): void {
		const m = this.model();
		const manage = m.getProperty("/manage") as boolean;
		const filter = m.getProperty("/filter") as ListFilter;
		const items = m.getProperty("/items") as FeedbackSummary[];
		// IT: Art, Status, Bereich und Priorität filtert der Server; hier nur die Suche.
		const list = sortFeedback(filterFeedback(items, manage ? { query: filter.query } : { status: filter.status, query: filter.query }), "recent");
		m.setProperty("/visible", list.map((f) => ({
			...f,
			kindIcon: feedbackKinds[f.kind].icon, kindText: feedbackKinds[f.kind].text,
			statusText: feedbackStatus[f.status]?.text ?? f.status, statusState: feedbackStatus[f.status]?.tone ?? "None",
			priorityText: feedbackPriorities[f.priority]?.text ?? f.priority, priorityState: feedbackPriorities[f.priority]?.tone ?? "None",
			showPriority: manage && (f.priority === "high" || f.priority === "urgent"),
			highlight: manage && f.status === "new" ? "Information" : "None",
			who: manage ? `${f.reporterName ?? "–"} · ${f.area}` : `${feedbackKinds[f.kind].text} · ${f.area}`,
		})));
		const open = list.filter((f) => !isClosed(f.status)).length;
		m.setProperty("/countText", `${list.length} ${list.length === 1 ? "Meldung" : "Meldungen"}${open && open !== list.length ? ` · ${open} offen` : ""}`);
		m.setProperty("/noData", filter.query ? `Keine Meldungen zu „${filter.query}“.`
			: manage ? "Keine Meldungen in dieser Ansicht." : "Sie haben noch nichts gemeldet. Fehler oder Idee? „Neue Meldung“ führt durch die Angaben.");
	}

	onFilter(): void {
		if (this.model().getProperty("/manage")) void this.load();
		else this.applyList();
	}

	onQuery(): void { this.applyList(); }

	onCounter(event: Event): void {
		const key = String((event.getSource() as Control).data("filter") ?? "");
		const m = this.model();
		const filter = { kind: "", status: "open", area: "", priority: "", query: "" };
		if (key === "new") filter.status = "new";
		if (key === "bug" || key === "feature") filter.kind = key;
		m.setProperty("/filter", filter);
		void this.load();
	}

	onSelect(event: Event): void {
		const item = (event as Event<{ listItem: Control }>).getParameter("listItem");
		const f = item?.getBindingContext("view")?.getObject() as FeedbackSummary | undefined;
		if (f) this.nav(`${this.model().getProperty("/base")}/${f.id}`);
	}

	private patchRow(s: FeedbackSummary): void {
		const m = this.model();
		const items = (m.getProperty("/items") as FeedbackSummary[]).map((f) => (f.id === s.id ? s : f));
		if (!items.some((f) => f.id === s.id)) items.unshift(s);
		m.setProperty("/items", items);
		this.applyList();
	}

	async onNew(): Promise<void> {
		const created = await openFeedbackWizard({ onShowMine: (d) => this.nav(`/meine-meldungen/${d.summary.id}`) });
		if (created && !this.model().getProperty("/manage")) this.patchRow(created.summary);
	}

	// ---- Meldung ----------------------------------------------------------------------------------------------------

	private clearDetail(): void {
		const m = this.model();
		m.setProperty("/d", null);
		this.shownId = "";
		m.setProperty("/timeline", []);
		m.setProperty("/facts", []);
	}

	private async open(id: string): Promise<void> {
		const m = this.model();
		if ((m.getProperty("/d") as FeedbackDetail | null)?.summary.id !== id) {
			m.setProperty("/reply", "");
			m.setProperty("/pending", []);
			m.setProperty("/internal", false);
		}
		m.setProperty("/detailBusy", true);
		try {
			this.apply(await get<FeedbackDetail>(`feedback/${id}`));
		} catch (error) {
			this.showError(error);
			this.nav(m.getProperty("/base") as string, true);
		} finally {
			m.setProperty("/detailBusy", false);
		}
	}

	private apply(d: FeedbackDetail): void {
		const m = this.model();
		const s = d.summary;
		const manage = m.getProperty("/manage") as boolean;
		m.setProperty("/d", d);
		m.setProperty("/edit", { status: s.status, priority: s.priority, assignee: s.assigneeUserId ?? "none" });
		const status = feedbackStatus[s.status];
		const progress = flowProgress(s.status);
		m.setProperty("/head", {
			kindText: feedbackKinds[s.kind].text, kindIcon: feedbackKinds[s.kind].icon, statusText: status.text, statusState: status.tone,
			priorityText: `Priorität ${feedbackPriorities[s.priority].text}`, priorityState: feedbackPriorities[s.priority].tone,
			progress: `${progress.text} – ${status.hint}${s.status === "duplicate" && s.duplicateOfNumber ? ` (${s.duplicateOfNumber})` : ""}`,
			progressType: s.status === "done" ? "Success" : s.status === "rejected" ? "Warning" : "Information",
		});
		m.setProperty("/facts", [
			{ label: "Bereich", value: s.area, state: "None" },
			...(manage ? [{ label: "Eingereicht von", value: s.reporterName ?? "–", state: "None" }] : []),
			{ label: "Eingereicht", value: `${new Date(s.createdAt).toLocaleDateString("de-DE")} · ${waitingSince(s.createdAt)}`, state: "None" },
			{ label: s.kind === "bug" ? "Auswirkung" : "Wichtigkeit", value: impactShort[s.impact], state: s.impact === "high" ? "Warning" : "None" },
			{ label: "Zuständig", value: s.assigneeName ?? "noch niemand", state: manage && !s.assigneeName ? "Warning" : "None" },
			...(s.duplicateOfNumber ? [{ label: "Duplikat von", value: s.duplicateOfNumber, state: "None" }] : []),
		]);
		const form = this.byId("fbDetails") as SimpleForm | undefined;
		if (form) {
			form.destroyContent();
			form.addContent(formTitle("Angaben"));
			for (const row of detailRows(d)) {
				form.addContent(new Label({ text: row.label, wrapping: true }));
				form.addContent(new Text({ text: row.value }).addStyleClass("eoPre"));
			}
		}
		const fileView = (a: { id: string; name: string; contentType: string }) =>
			({ id: a.id, name: a.name, image: a.contentType.startsWith("image/"), thumb: fileUrl(a.id, "thumb") });
		m.setProperty("/attachments", d.attachments.map(fileView));
		const context = contextRows(d.context, manage);
		m.setProperty("/context", context);
		const contextForm = this.byId("fbContextForm") as SimpleForm | undefined;
		if (contextForm) {
			contextForm.destroyContent();
			for (const row of context) {
				contextForm.addContent(new Label({ text: row.label, wrapping: true }));
				contextForm.addContent(new Text({ text: row.value }).addStyleClass("eoFbBreak"));
			}
		}
		this.timeline = d.timeline;
		this.renderTimeline();
		this.patchRow(s);
		document.title = `${s.number} ${s.title} – EstrichONE`;
		// Neue Meldung: oben beginnen (Angaben); nach einer Antwort: ans Ende des Verlaufs.
		const same = this.shownId === s.id;
		this.shownId = s.id;
		setTimeout(() => this.scroll(same), 0);
	}

	private renderTimeline(): void {
		const m = this.model();
		const talkOnly = m.getProperty("/show") === "talk";
		m.setProperty("/timeline", this.timeline
			.filter((i) => !talkOnly || ["created", "comment", "note"].includes(i.kind))
			.map((i) => {
				const bubble = ["comment", "note"].includes(i.kind);
				return {
					...i, bubble, icon: icons[i.kind] ?? "sap-icon://history", kindText: kindTexts[i.kind] ?? i.kind, tone: i.kind,
					who: i.kind === "note" ? "note" : i.isReporter ? "reporter" : "agent",
					headline: i.kind === "note" ? `${i.actorName ?? ""} · interne Notiz` : i.actorName ?? "",
					files: i.attachments.map((a) => ({ id: a.id, name: a.name, image: a.contentType.startsWith("image/"), thumb: fileUrl(a.id, "thumb") })),
				};
			}));
	}

	private scroll(toEnd: boolean): void {
		const dom = this.byId("fbScroll")?.getDomRef() as HTMLElement | null;
		if (dom) dom.scrollTop = toEnd ? dom.scrollHeight : 0;
	}

	onShow(): void { this.renderTimeline(); }

	private item(): FeedbackDetail {
		return this.model().getProperty("/d") as FeedbackDetail;
	}

	private async update(body: Record<string, unknown>, message: string): Promise<void> {
		const d = this.item();
		const updated = await this.run(() => patch<FeedbackDetail>(`feedback/${d.summary.id}`, { unassign: false, ...body }));
		if (updated) {
			this.apply(updated);
			this.toast(message);
		} else {
			this.apply(d);
		}
	}

	async onQuickStatus(): Promise<void> {
		const status = this.model().getProperty("/edit/status") as FeedbackStatus;
		const d = this.item();
		if (status === d.summary.status) return;
		if (status === "duplicate") {
			const saved = await openForm<FeedbackDetail>({
				title: "Als Duplikat markieren", width: "30rem", submitText: "Markieren",
				intro: "Der Einreicher sieht den Verweis auf die ursprüngliche Meldung.",
				initial: { number: d.summary.duplicateOfNumber ?? "" },
				fields: [{ key: "number", label: "Duplikat von (Nummer)", required: true, maxLength: 20, placeholder: "z. B. F-2026-0003" }],
				submit: (v) => patch<FeedbackDetail>(`feedback/${d.summary.id}`, {
					status, duplicateOfNumber: normalizeNumber(String(v.number ?? "")) ?? String(v.number ?? "").trim(), unassign: false,
				}),
			});
			if (saved) {
				this.apply(saved);
				this.toast(`Als Duplikat von ${saved.summary.duplicateOfNumber ?? ""} markiert`);
			} else {
				this.model().setProperty("/edit/status", d.summary.status);
			}
			return;
		}
		await this.update({ status }, `Status: ${feedbackStatus[status].text}`);
	}

	async onQuickPriority(): Promise<void> {
		const priority = this.model().getProperty("/edit/priority") as string;
		if (priority !== this.item().summary.priority) await this.update({ priority }, "Priorität geändert");
	}

	async onQuickAssignee(): Promise<void> {
		const picked = this.model().getProperty("/edit/assignee") as string;
		const assignee = picked === "none" ? "" : picked;
		if ((assignee || null) === this.item().summary.assigneeUserId) return;
		await this.update(assignee ? { assigneeUserId: assignee } : { unassign: true }, assignee ? "Zugewiesen" : "Zuständigkeit aufgehoben");
	}

	async onMine(): Promise<void> {
		this.model().setProperty("/edit/assignee", session.userId);
		await this.onQuickAssignee();
	}

	async onClassify(): Promise<void> {
		const d = this.item();
		const saved = await openForm<FeedbackDetail>({
			title: "Meldung einordnen", width: "30rem", submitText: "Speichern",
			initial: { kind: d.summary.kind, area: d.summary.area },
			fields: [
				{ key: "kind", label: "Art", type: "select", required: true, options: [{ key: "bug", text: "Fehler" }, { key: "feature", text: "Funktionswunsch" }] },
				{ key: "area", label: "Bereich", type: "select", required: true,
					options: [...new Set([d.summary.area, ...feedbackAreas.map((a) => a.name)])].map((a) => ({ key: a, text: a })) },
			],
			submit: (v) => patch<FeedbackDetail>(`feedback/${d.summary.id}`, { kind: v.kind, area: v.area, unassign: false }),
		});
		if (saved) {
			this.apply(saved);
			this.toast("Eingeordnet");
		}
	}

	// ---- Antworten ---------------------------------------------------------------------------------------------------

	private addFiles(files: Iterable<File>): void {
		if (!this.model().getProperty("/canUpload")) return;
		const pending = this.model().getProperty("/pending") as { name: string; file: File }[];
		const added = [...files].map((file, i) => ({ name: file.name && file.name !== "image.png" ? file.name : `Bildschirmfoto-${pending.length + i + 1}.png`, file }));
		this.model().setProperty("/pending", [...pending, ...added].slice(0, 10));
	}

	/** Antwortfeld: Dateien hineinziehen, Bildschirmfotos mit Strg+V, Senden mit Strg+Enter. */
	private wireComposer(): void {
		const dom = this.byId("fbComposer")?.getDomRef() as HTMLElement | null;
		if (!dom || dom.dataset.eoWired) return;
		dom.dataset.eoWired = "1";
		dom.addEventListener("dragover", (e) => { if (e.dataTransfer?.types.includes("Files")) { e.preventDefault(); dom.classList.add("eoDropActive"); } });
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

	async onComment(): Promise<void> {
		const m = this.model();
		const d = this.item();
		const pending = m.getProperty("/pending") as { name: string; file: File }[];
		const text = String(m.getProperty("/reply") ?? "");
		if (!text.trim() && !pending.length) return;
		const internal = !!m.getProperty("/internal") && !!m.getProperty("/manage");
		const updated = await this.run(async () => {
			const attachmentFileIds: string[] = [];
			for (const p of pending) attachmentFileIds.push(await uploadLoose(p.file));
			return post<FeedbackDetail>(`feedback/${d.summary.id}/comments`, { text, isInternal: internal, attachmentFileIds });
		}, internal ? "Notiz gespeichert" : "Gesendet");
		if (updated) {
			m.setProperty("/reply", "");
			m.setProperty("/internal", false);
			m.setProperty("/pending", []);
			this.apply(updated);
		}
	}

	onAttachment(event: Event): void {
		const id = ((event.getSource() as Control).getBindingContext("view")?.getObject() as { id: string }).id;
		window.open(fileUrl(id), "_blank", "noopener");
	}
}
