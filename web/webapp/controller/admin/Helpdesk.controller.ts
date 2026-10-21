import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import { get, post } from "../../util/api";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { options, ticketCategories, ticketPriorities } from "../../util/labels";
import { uploadLoose } from "../../util/looseUpload";
import type { TicketDetail, TicketSummary } from "../../util/models";

/**
 * IT Center › Helpdesk: Bearbeitung sieht alle Tickets; jeder andere Benutzer seine eigenen Meldungen.
 * Neue Tickets (Runde 3b, F2) können alle anlegen – mit Kategorie, Priorität, Beschreibung und Anhängen/Bildschirmfotos.
 * @namespace estrichone.controller.admin
 */
export default class Helpdesk extends BaseController {
	onInit(): void {
		this.local({ items: [], filter: "open", category: "", busy: false, agent: false,
			categories: [{ key: "", text: "Alle Kategorien" }, ...options(ticketCategories)] });
	}

	onRouteMatched(): void {
		this.model().setProperty("/agent", this.has("helpdesk.agent"));
		void this.load();
	}

	onFilter(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		const items = m.getProperty("/agent")
			? await this.run(() => get<TicketSummary[]>("tickets", { filter: m.getProperty("/filter"), category: m.getProperty("/category") || undefined }))
			: await this.run(() => get<TicketSummary[]>("tickets/mine"));
		m.setProperty("/items", items ?? []);
	}

	onOpen(event: Event): void {
		this.nav(`/it-center/helpdesk/${((event.getSource() as Control).getBindingContext("view")?.getObject() as TicketSummary).id}`);
	}

	async onNew(): Promise<void> {
		const files: File[] = [];
		const canUpload = this.has("files.upload");
		const list = new Text({ text: "" });
		const show = (): void => { list.setText(files.map((f) => f.name).join(", ")); };
		const add = (added: Iterable<File>): void => { files.push(...added); files.splice(10); show(); };
		// Bildschirmfoto aus der Zwischenablage (Strg+V) übernehmen, solange das Formular offen ist.
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
				this.nav(`/it-center/helpdesk/${saved.summary.id}`);
			}
		} finally {
			document.removeEventListener("paste", paste);
		}
	}
}
