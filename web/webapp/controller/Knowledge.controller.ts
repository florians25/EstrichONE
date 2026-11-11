import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, post, put } from "../util/api";
import { openForm } from "../util/forms";
import { articleHtml, type KnowledgeArticle, type KnowledgeArticleSummary } from "../util/helpdesk";
import { options, ticketCategories } from "../util/labels";
import type { RouteMatch } from "../util/router";

/**
 * Wissensdatenbank: Anleitungen und bekannte Lösungen. Alle sehen veröffentlichte Artikel; die IT (Recht „Supporttickets
 * bearbeiten“) schreibt Artikel, sieht Entwürfe und verknüpft Artikel mit Tickets.
 * @namespace estrichone.controller
 */
export default class Knowledge extends BaseController {
	private timer: ReturnType<typeof setTimeout> | undefined;

	onInit(): void {
		this.local({ items: [], busy: false, query: "", category: "", selectedId: "", a: null, html: "", meta: "", voted: false, agent: false,
			categories: [{ key: "", text: "Alle Kategorien" }, ...options(ticketCategories)] });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		m.setProperty("/agent", this.has("helpdesk.agent"));
		const id = match.params.id ?? "";
		m.setProperty("/selectedId", id);
		if (!(m.getProperty("/items") as unknown[]).length) await this.load();
		if (id) await this.open(id);
		else m.setProperty("/a", null);
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			m.setProperty("/items", await get<KnowledgeArticleSummary[]>("helpdesk/articles", {
				q: String(m.getProperty("/query") ?? "").trim() || undefined, category: m.getProperty("/category") || undefined }));
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onQuery(): void {
		clearTimeout(this.timer);
		this.timer = setTimeout(() => void this.load(), 250);
	}

	onSelect(event: Event): void {
		const item = (event as Event<{ listItem: Control }>).getParameter("listItem");
		const a = item?.getBindingContext("view")?.getObject() as KnowledgeArticleSummary | undefined;
		if (a) this.nav(`/wissen/${a.id}`);
	}

	private async open(id: string): Promise<void> {
		try {
			const a = await get<KnowledgeArticle>(`helpdesk/articles/${id}`);
			this.show(a);
		} catch (error) {
			this.showError(error);
			this.nav("/wissen", true);
		}
	}

	private show(a: KnowledgeArticle): void {
		const m = this.model();
		m.setProperty("/a", a);
		m.setProperty("/voted", false);
		m.setProperty("/html", articleHtml(a.body));
		const date = new Date(a.updatedAt).toLocaleDateString("de-DE");
		m.setProperty("/meta", `Zuletzt geändert am ${date}${a.updatedByName ? ` von ${a.updatedByName}` : ""}${m.getProperty("/agent") ? ` · ${a.views}× gelesen` : ""}`);
		document.title = `${a.title} – EstrichONE`;
	}

	onHelpdesk(): void { this.nav("/it-center/helpdesk"); }

	async onHelpful(): Promise<void> {
		const a = this.model().getProperty("/a") as KnowledgeArticle;
		await this.run(() => post(`helpdesk/articles/${a.id}/helpful`));
		this.model().setProperty("/voted", true);
	}

	private async edit(article?: KnowledgeArticle): Promise<void> {
		const saved = await openForm<KnowledgeArticle>({
			title: article ? "Artikel bearbeiten" : "Neuer Artikel", width: "44rem", submitText: "Speichern",
			intro: "Gliederung: „# “ für Zwischenüberschriften, „1. “ für Schritte, „- “ für Aufzählungen, **fett** für Hervorhebungen.",
			initial: { title: article?.title ?? "", body: article?.body ?? "", category: article?.category ?? "", keywords: article?.keywords ?? "",
				isPublished: article?.isPublished ?? true },
			fields: [
				{ key: "title", label: "Titel", required: true, maxLength: 200, placeholder: "z. B. Fotos werden nicht übertragen" },
				{ key: "body", label: "Anleitung", type: "textarea", rows: 12, required: true, maxLength: 20000 },
				{ key: "category", label: "Kategorie", type: "select", options: [{ key: "", text: "–" }, ...options(ticketCategories)] },
				{ key: "keywords", label: "Suchbegriffe", maxLength: 500, placeholder: "z. B. upload, foto, abgleich", help: "Helfen beim Vorschlagen passender Artikel." },
				{ key: "isPublished", label: "Veröffentlichen (für alle sichtbar und als Vorschlag beim Melden)", type: "checkbox" },
			],
			submit: (v) => {
				const body = { title: v.title, body: v.body, category: v.category || null, keywords: v.keywords || null, isPublished: !!v.isPublished };
				return article ? put<KnowledgeArticle>(`helpdesk/articles/${article.id}`, body) : post<KnowledgeArticle>("helpdesk/articles", body);
			},
		});
		if (!saved) return;
		this.toast("Artikel gespeichert");
		await this.load();
		if (article) this.show(saved);
		else this.nav(`/wissen/${saved.id}`);
	}

	onNew(): void { void this.edit(); }

	onEdit(): void { void this.edit(this.model().getProperty("/a") as KnowledgeArticle); }

	async onDelete(): Promise<void> {
		const a = this.model().getProperty("/a") as KnowledgeArticle;
		if (!(await this.confirm(`Artikel „${a.title}“ löschen? Verknüpfungen in Tickets entfallen.`, { action: "Löschen", danger: true }))) return;
		await this.run(() => del(`helpdesk/articles/${a.id}`), "Artikel gelöscht");
		await this.load();
		this.nav("/wissen", true);
	}
}
