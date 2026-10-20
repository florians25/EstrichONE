import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, put } from "../../util/api";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { documentCategories, options } from "../../util/labels";
import { invalidateCatalog, roomsToText, textToRooms, type CatalogEntry, type CatalogKind, type TemplateRoom } from "../../util/catalog";

const addTexts: Record<CatalogKind, string> = { textBlock: "Textbaustein anlegen", folderTemplate: "Ordnervorlage anlegen", bauakteTemplate: "Vorlage anlegen" };

/**
 * Verwaltung › Stammdaten: Textbausteine, Ordnervorlagen (Unterordner je Dokumentart) und Bauakten-Vorlagen
 * (Beschreibung und Standardräume, im Anlage-Assistenten wählbar).
 * @namespace estrichone.controller.admin
 */
export default class MasterData extends BaseController {
	onInit(): void {
		this.local({ tab: "textBlock", addText: addTexts.textBlock, canManage: false, textBlock: [], folderTemplate: [], bauakteTemplate: [] });
	}

	onRouteMatched(): void {
		this.model().setProperty("/canManage", this.has("settings.manage"));
		void this.load();
	}

	onTab(e: Event): void {
		const key = String(e.getParameter("key" as never)) as CatalogKind;
		this.model().setProperty("/addText", addTexts[key]);
	}

	private async load(): Promise<void> {
		const all = (await this.run(() => get<CatalogEntry[]>("catalog"))) ?? [];
		const m = this.model();
		m.setProperty("/textBlock", all.filter((e) => e.kind === "textBlock"));
		m.setProperty("/folderTemplate", all.filter((e) => e.kind === "folderTemplate").map((e) => ({
			...e, categoryText: documentCategories[e.content.category as keyof typeof documentCategories]?.text ?? String(e.content.category),
			foldersText: ((e.content.folders as string[]) ?? []).join(", "),
		})));
		m.setProperty("/bauakteTemplate", all.filter((e) => e.kind === "bauakteTemplate").map((e) => ({
			...e, roomsText: ((e.content.rooms as TemplateRoom[]) ?? []).map((r) => [r.name, r.floor].filter(Boolean).join(" ")).join(", ") || "–",
		})));
		invalidateCatalog();
	}

	onAdd(): void { void this.edit(this.model().getProperty("/tab") as CatalogKind, null); }

	onEdit(e: Event): void {
		const entry = (e.getSource() as Control).getBindingContext("view")?.getObject() as CatalogEntry;
		void this.edit(entry.kind, entry);
	}

	async onDelete(e: Event): Promise<void> {
		const entry = (e.getSource() as Control).getBindingContext("view")?.getObject() as CatalogEntry;
		if (!(await this.confirm(`„${entry.name}“ löschen?`, { action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`catalog/${entry.id}`); return true; })) {
			this.toast("Gelöscht");
			await this.load();
		}
	}

	private async edit(kind: CatalogKind, entry: CatalogEntry | null): Promise<void> {
		const c = entry?.content ?? {};
		const common = [
			{ key: "name", label: "Name", required: true, maxLength: 200 },
			{ key: "sortOrder", label: "Reihenfolge", type: "integer" as const },
			{ key: "isActive", label: "Aktiv", type: "switch" as const },
		];
		const fields = kind === "textBlock"
			? [...common.slice(0, 1), { key: "text", label: "Text", type: "textarea" as const, rows: 6, required: true, maxLength: 4000 }, ...common.slice(1)]
			: kind === "folderTemplate"
				? [...common.slice(0, 1), { key: "category", label: "Dokumentart", type: "select" as const, options: options(documentCategories) },
					{ key: "folders", label: "Unterordner (eine Zeile je Ordner)", type: "textarea" as const, rows: 6, required: true }, ...common.slice(1)]
				: [...common.slice(0, 1), { key: "description", label: "Beschreibung", type: "textarea" as const, rows: 3, maxLength: 4000 },
					{ key: "rooms", label: "Räume (je Zeile: Name; Geschoss; Fläche m²; Dicke mm; FBH)", type: "textarea" as const, rows: 8,
						placeholder: "Wohnen/Essen; EG; 42,5; 65; FBH" }, ...common.slice(1)];
		const saved = await openForm<CatalogEntry>({
			title: entry ? `${entry.name} bearbeiten` : addTexts[kind], submitText: "Speichern", width: "40rem",
			initial: {
				name: entry?.name ?? "", sortOrder: entry?.sortOrder ?? 0, isActive: entry?.isActive ?? true,
				text: c.text ?? "", category: c.category ?? "offer", folders: ((c.folders as string[]) ?? []).join("\n"),
				description: c.description ?? "", rooms: roomsToText(c.rooms as TemplateRoom[] | undefined),
			},
			fields,
			submit: (v) => {
				const content = kind === "textBlock" ? { text: String(v.text).trim() }
					: kind === "folderTemplate" ? { category: v.category, folders: String(v.folders).split("\n").map((f) => f.trim()).filter(Boolean) }
						: { description: String(v.description ?? "").trim() || null, rooms: textToRooms(String(v.rooms ?? "")) };
				return put<CatalogEntry>(`catalog/${entry?.id ?? uuidv7()}`, {
					kind, name: String(v.name).trim(), content, sortOrder: Number(v.sortOrder ?? 0), isActive: v.isActive !== false, version: entry?.version ?? null,
				});
			},
		});
		if (saved) {
			this.toast("Gespeichert");
			await this.load();
		}
	}
}
