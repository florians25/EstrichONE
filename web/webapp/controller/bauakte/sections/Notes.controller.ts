import SectionBase from "./SectionBase";
import ActionSheet from "sap/m/ActionSheet";
import Button from "sap/m/Button";
import { textBlocks } from "../../../util/catalog";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type MenuItem from "sap/m/MenuItem";
import type TextArea from "sap/m/TextArea";
import { patch, post } from "../../../util/api";
import { akte } from "../../../util/akteStore";
import { uuidv7 } from "../../../util/ids";
import type { BauakteHistoryEntry, NoteDto } from "../../../util/models";
import { session } from "../../../util/session";

/** Notizen und Verlauf als ein Verlauf: Notizen anheften und einfärben, Systemänderungen dazwischen. @namespace estrichone.controller.bauakte.sections */
export default class Notes extends SectionBase {
	protected watches = ["notes", "history"];
	private notes: NoteDto[] = [];
	private history: BauakteHistoryEntry[] = [];

	protected init(): void {
		this.v().setModel(new JSONModel({ items: [], pinned: [], filter: "all", text: "", pin: false }), "nt");
		(this.v().byId("noteInput") as TextArea).attachBrowserEvent("keydown", (event: KeyboardEvent) => {
			if (event.ctrlKey && event.key === "Enter") {
				event.preventDefault();
				void this.onSend();
			}
		});
	}

	private nt(): JSONModel {
		return this.v().getModel("nt") as JSONModel;
	}

	protected async load(): Promise<void> {
		await session.people();
		[this.notes, this.history] = await Promise.all([akte.notes(this.bauakteId), akte.history(this.bauakteId)]);
		this.refresh();
	}

	refresh(): void {
		const m = this.nt();
		const notes = this.notes.map((n) => ({ ...n, type: "note", at: n.createdAt, authorName: session.name(n.authorUserId) }));
		const events = m.getProperty("/filter") === "notes" ? [] : this.history.filter((h) => h.action !== "bauakte.note_added" && h.action !== "note_added")
			.map((h) => ({
				type: "event", at: h.at, text: h.text,
				changes: (h.changes ?? []).map((c) => `${c.label}: ${c.from || "–"} → ${c.to || "–"}`).join("\n"),
			}));
		m.setProperty("/items", [...notes, ...events].sort((a, b) => a.at.localeCompare(b.at)));
		m.setProperty("/pinned", this.notes.filter((n) => n.isPinned));
	}

	/** Textbaustein aus den Stammdaten an den Notiztext anhängen. */
	async onTextBlock(event: Event): Promise<void> {
		const source = event.getSource() as Control;
		const blocks = await textBlocks("general");
		const sheet = new ActionSheet({
			title: "Textbaustein einfügen", placement: "Top",
			buttons: blocks.length
				? blocks.map((b) => new Button({ text: b.name, press: () => {
					const m = this.nt();
					const current = String(m.getProperty("/text") ?? "");
					m.setProperty("/text", current ? `${current.trimEnd()}\n${b.content.text}` : b.content.text);
				} }))
				: [new Button({ text: "Keine Textbausteine – unter Verwaltung › Stammdaten anlegen", enabled: false })],
			afterClose: () => sheet.destroy(),
		});
		sheet.openBy(source);
	}

	async onSend(): Promise<void> {
		const m = this.nt();
		const text = String(m.getProperty("/text") ?? "").trim();
		if (!text) return;
		const id = uuidv7();
		const ok = await this.run(async () => {
			await post(`bauakten/${this.bauakteId}/notes`, { id, text });
			if (m.getProperty("/pin")) await patch(`bauakten/${this.bauakteId}/notes/${id}`, { isPinned: true });
			return true;
		});
		if (ok) {
			m.setProperty("/text", "");
			m.setProperty("/pin", false);
			akte.invalidate(this.bauakteId, "notes");
		}
	}

	private noteOf(control: Control): NoteDto {
		let current: Control | null = control;
		while (current && !(current.getBindingContext("nt")?.getObject() as { id?: string } | undefined)?.id) {
			current = current.getParent() as Control | null;
		}
		return current?.getBindingContext("nt")?.getObject() as NoteDto;
	}

	async onPin(event: Event): Promise<void> {
		const note = this.noteOf(event.getSource() as Control);
		await this.run(() => patch(`bauakten/${this.bauakteId}/notes/${note.id}`, { isPinned: !note.isPinned }));
		akte.invalidate(this.bauakteId, "notes");
	}

	async onColor(event: Event): Promise<void> {
		const item = (event as unknown as { getParameter(n: string): MenuItem }).getParameter("item");
		const note = this.noteOf(event.getSource() as Control);
		await this.run(() => patch(`bauakten/${this.bauakteId}/notes/${note.id}`, { color: item.getKey() }));
		akte.invalidate(this.bauakteId, "notes");
	}
}
