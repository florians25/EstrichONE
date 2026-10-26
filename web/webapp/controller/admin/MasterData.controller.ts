import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, put } from "../../util/api";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { documentCategories, options } from "../../util/labels";
import { listNames, valueLists, type ValueListItem } from "../../util/valueLists";
import { bauakteSchema, fieldsOf, type FormSchema, type SchemaField, type SchemaFieldType } from "../../util/formSchema";
import { invalidateCatalog, roomsToText, textToRooms, type CatalogEntry, type CatalogKind, type TemplateRoom } from "../../util/catalog";

type Tab = CatalogKind | "valueList" | "form";
const addTexts: Record<Tab, string> = {
	textBlock: "Textbaustein anlegen", folderTemplate: "Ordnervorlage anlegen", bauakteTemplate: "Vorlage anlegen",
	valueList: "Eintrag anlegen", form: "Feld hinzufügen",
};
const typeTexts: Record<SchemaFieldType, string> = {
	text: "Text", textarea: "Langer Text", number: "Zahl", integer: "Ganze Zahl", select: "Auswahl", boolean: "Ja/Nein", date: "Datum",
};
/** Eingebaute Felder der Bauakte (Server legt Typ und Ablage fest). */
const builtinFields: Record<string, { label: string; type: SchemaFieldType; list?: string; unit?: string }> = {
	objectType: { label: "Objektart", type: "select", list: "objectType" }, floors: { label: "Geschosse", type: "text" },
	totalArea: { label: "Gesamtfläche", type: "number", unit: "m²" }, priority: { label: "Priorität", type: "select", list: "priority" },
	plannedStart: { label: "Ausführung von", type: "date" }, plannedEnd: { label: "Ausführung bis", type: "date" },
	screedType: { label: "Estrichart", type: "select", list: "screedType" }, construction: { label: "Konstruktion", type: "select", list: "construction" },
	nominalThicknessMm: { label: "Nenndicke", type: "integer", unit: "mm" }, strengthClass: { label: "Festigkeitsklasse", type: "select", list: "strengthClass" },
	underfloorHeating: { label: "Fußbodenheizung", type: "boolean" }, heatingType: { label: "Art der Heizung", type: "select", list: "heatingType" },
	insulation: { label: "Dämmung", type: "text", list: "insulation" }, insulationThicknessMm: { label: "Dämmdicke", type: "integer", unit: "mm" },
	siteAccess: { label: "Zugang / Besonderheiten", type: "textarea" }, waterOnSite: { label: "Wasser vor Ort", type: "boolean" },
	powerOnSite: { label: "Strom vor Ort", type: "boolean" },
};

interface FormRow { group: boolean; gi: number; fi: number; label: string; typeText: string; listText: string; required: boolean; badge: boolean; builtin: boolean }

/**
 * Verwaltung › Stammdaten: Textbausteine, Ordnervorlagen (Unterordner je Dokumentart) und Bauakten-Vorlagen
 * (Beschreibung und Standardräume, im Anlage-Assistenten wählbar).
 * @namespace estrichone.controller.admin
 */
export default class MasterData extends BaseController {
	onInit(): void {
		this.local({ tab: "textBlock", addText: addTexts.textBlock, canManage: false, textBlock: [], folderTemplate: [], bauakteTemplate: [],
			list: "screedType", lists: [], listItems: [], formRows: [], formDirty: false });
	}

	onRouteMatched(): void {
		this.model().setProperty("/canManage", this.has("settings.manage"));
		void this.load();
	}

	onTab(e: Event): void {
		const key = String(e.getParameter("key" as never)) as Tab;
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
		await Promise.all([this.loadLists(), this.loadForm()]);
	}

	onAdd(): void {
		const tab = this.model().getProperty("/tab") as Tab;
		if (tab === "valueList") void this.editItem(null);
		else if (tab === "form") void this.onAddField();
		else void this.edit(tab, null);
	}

	// ---- Wertelisten (B6) -----------------------------------------------------------------------------------------

	private items: ValueListItem[] = [];

	private async loadLists(): Promise<void> {
		this.items = await valueLists(true);
		const m = this.model();
		const keys = [...new Set([...Object.keys(listNames), ...this.items.map((i) => i.list)])];
		m.setProperty("/lists", keys.map((key) => ({ key, text: listNames[key] ?? key })));
		this.showList();
	}

	private showList(): void {
		const list = this.model().getProperty("/list") as string;
		this.model().setProperty("/listItems", this.items.filter((i) => i.list === list).sort((a, b) => a.sortOrder - b.sortOrder).map((i) => ({ ...i })));
	}

	onListChange(): void { this.showList(); }

	async onNewList(): Promise<void> {
		const saved = await openForm<ValueListItem>({
			title: "Neue Werteliste", width: "30rem",
			fields: [
				{ key: "list", label: "Name der Liste (Schlüssel)", required: true, maxLength: 40, placeholder: "z. B. bodenbelag",
					validate: (v) => (/^[A-Za-z][A-Za-z0-9_]*$/.test(String(v ?? "")) ? null : "Nur Buchstaben, Ziffern und Unterstrich.") },
				{ key: "label", label: "Erster Eintrag", required: true, maxLength: 200 },
			],
			submit: (v) => put<ValueListItem>(`value-lists/${uuidv7()}`, { list: v.list, key: keyOf(String(v.label)), label: v.label, sortOrder: 10 }),
		});
		if (saved) {
			this.model().setProperty("/list", saved.list);
			await this.loadLists();
		}
	}

	private async editItem(item: ValueListItem | null): Promise<void> {
		const list = this.model().getProperty("/list") as string;
		const max = Math.max(0, ...this.items.filter((i) => i.list === list).map((i) => i.sortOrder));
		const saved = await openForm<ValueListItem>({
			title: item ? `${item.label} umbenennen` : `${listNames[list] ?? list}: Eintrag anlegen`, width: "30rem",
			initial: { label: item?.label ?? "", shortLabel: item?.shortLabel ?? "", key: item?.key ?? "" },
			fields: [
				{ key: "label", label: "Bezeichnung", required: true, maxLength: 200 },
				{ key: "shortLabel", label: "Kurzform (Badge)", maxLength: 30, placeholder: "z. B. CT" },
				...(item ? [] : [{ key: "key", label: "Schlüssel (optional, sonst aus der Bezeichnung)", maxLength: 40 }]),
			],
			submit: (v) => put<ValueListItem>(`value-lists/${item?.id ?? uuidv7()}`, {
				list, key: item?.key ?? (String(v.key ?? "").trim() || keyOf(String(v.label))), label: v.label,
				shortLabel: String(v.shortLabel ?? "").trim() || null, sortOrder: item?.sortOrder ?? max + 10, isActive: item?.isActive ?? true,
			}),
		});
		if (saved) {
			this.toast("Gespeichert");
			await this.loadLists();
		}
	}

	onEditItem(e: Event): void {
		void this.editItem((e.getSource() as Control).getBindingContext("view")?.getObject() as ValueListItem);
	}

	private saveItem(i: ValueListItem): Promise<unknown> {
		return put(`value-lists/${i.id}`, { list: i.list, key: i.key, label: i.label, shortLabel: i.shortLabel, sortOrder: i.sortOrder, isActive: i.isActive });
	}

	async onToggleItem(e: Event): Promise<void> {
		const i = (e.getSource() as Control).getBindingContext("view")?.getObject() as ValueListItem;
		await this.run(() => this.saveItem(i));
		await this.loadLists();
	}

	async onMoveItem(e: Event): Promise<void> {
		const src = e.getSource() as Control;
		const dir = Number(src.data("dir"));
		const rows = this.model().getProperty("/listItems") as ValueListItem[];
		const i = rows.indexOf(src.getBindingContext("view")?.getObject() as ValueListItem);
		const j = i + dir;
		if (i < 0 || j < 0 || j >= rows.length) return;
		[rows[i], rows[j]] = [rows[j], rows[i]];
		await this.run(() => Promise.all(rows.map((r, n) => (r.sortOrder === (n + 1) * 10 ? null : this.saveItem({ ...r, sortOrder: (n + 1) * 10 })))));
		await this.loadLists();
	}

	// ---- Formular-Designer (B7) ---------------------------------------------------------------------------------

	private schema: FormSchema | null = null;

	private async loadForm(): Promise<void> {
		this.schema = await bauakteSchema(true);
		this.model().setProperty("/formDirty", false);
		this.showForm();
	}

	private showForm(): void {
		const rows: FormRow[] = [];
		(this.schema?.groups ?? []).forEach((g, gi) => {
			rows.push({ group: true, gi, fi: -1, label: g.label, typeText: "Gruppe", listText: `${g.fields.length} Felder`, required: false, badge: false, builtin: false });
			g.fields.forEach((f, fi) => rows.push({
				group: false, gi, fi, label: f.label, typeText: typeTexts[f.type] ?? f.type,
				listText: f.list ? listNames[f.list] ?? f.list : f.unit ?? "", required: !!f.required, badge: !!f.badge, builtin: !!f.builtin,
			}));
		});
		this.model().setProperty("/formRows", rows);
	}

	private changed(): void {
		this.model().setProperty("/formDirty", true);
		this.showForm();
	}

	private rowOf(e: Event): FormRow {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as FormRow;
	}

	onFormFlag(e: Event): void {
		const r = this.rowOf(e);
		const f = this.schema!.groups[r.gi].fields[r.fi];
		f.required = r.required;
		f.badge = r.badge;
		this.model().setProperty("/formDirty", true);
	}

	onMoveRow(e: Event): void {
		const r = this.rowOf(e);
		const dir = Number((e.getSource() as Control).data("dir"));
		const groups = this.schema!.groups;
		if (r.group) {
			const j = r.gi + dir;
			if (j < 0 || j >= groups.length) return;
			[groups[r.gi], groups[j]] = [groups[j], groups[r.gi]];
		} else {
			const fields = groups[r.gi].fields;
			const j = r.fi + dir;
			if (j >= 0 && j < fields.length) [fields[r.fi], fields[j]] = [fields[j], fields[r.fi]];
			else if (j < 0 && r.gi > 0) groups[r.gi - 1].fields.push(...fields.splice(r.fi, 1));
			else if (j >= fields.length && r.gi < groups.length - 1) groups[r.gi + 1].fields.unshift(...fields.splice(r.fi, 1));
			else return;
		}
		this.changed();
	}

	async onRemoveRow(e: Event): Promise<void> {
		const r = this.rowOf(e);
		const groups = this.schema!.groups;
		if (r.group) {
			if (groups[r.gi].fields.length && !(await this.confirm(`Gruppe „${r.label}“ mit ${groups[r.gi].fields.length} Feldern entfernen? Bereits erfasste Werte bleiben in den Bauakten gespeichert.`, { action: "Entfernen", danger: true }))) return;
			groups.splice(r.gi, 1);
		} else {
			groups[r.gi].fields.splice(r.fi, 1);
		}
		this.changed();
	}

	async onEditRow(e: Event): Promise<void> {
		const r = this.rowOf(e);
		const groups = this.schema!.groups;
		if (r.group) {
			const v = await openForm<Record<string, unknown>>({ title: "Gruppe umbenennen", width: "26rem", initial: { label: r.label },
				fields: [{ key: "label", label: "Bezeichnung", required: true, maxLength: 100 }], submit: async (x) => x });
			if (v) { groups[r.gi].label = String(v.label).trim(); this.changed(); }
			return;
		}
		const f = groups[r.gi].fields[r.fi];
		const v = await this.fieldDialog(f);
		if (v) { Object.assign(f, v); this.changed(); }
	}

	onAddGroup(): void {
		void openForm<Record<string, unknown>>({ title: "Gruppe anlegen", width: "26rem",
			fields: [{ key: "label", label: "Bezeichnung", required: true, maxLength: 100 }], submit: async (x) => x }).then((v) => {
			if (!v || !this.schema) return;
			this.schema.groups.push({ key: keyOf(String(v.label)).replace(/[^A-Za-z0-9_]/g, "_") || `gruppe${this.schema.groups.length + 1}`, label: String(v.label).trim(), fields: [] });
			this.changed();
		});
	}

	async onAddField(): Promise<void> {
		if (!this.schema) return;
		if (!this.schema.groups.length) this.schema.groups.push({ key: "bauvorhaben", label: "Bauvorhaben", fields: [] });
		const v = await this.fieldDialog(null);
		if (!v) return;
		const target = this.schema.groups.find((g) => g.key === v.group) ?? this.schema.groups[this.schema.groups.length - 1];
		delete v.group;
		target.fields.push(v as unknown as SchemaField);
		this.changed();
	}

	/** Dialog für ein Feld: eingebaut (nur Beschriftung, Pflicht, Badge, Hinweis) oder eigenes Feld. */
	private async fieldDialog(f: SchemaField | null): Promise<(Partial<SchemaField> & { group?: string }) | null> {
		const used = new Set(fieldsOf(this.schema!).map((x) => x.key));
		const freeBuiltins = Object.entries(builtinFields).filter(([k]) => !used.has(k));
		const lists = [...new Set([...Object.keys(listNames), ...this.items.map((i) => i.list)])].map((key) => ({ key, text: listNames[key] ?? key }));
		const result = await openForm<Record<string, unknown>>({
			title: f ? `Feld „${f.label}“ bearbeiten` : "Feld hinzufügen", width: "34rem",
			initial: f ? { label: f.label, type: f.type, list: f.list ?? "", unit: f.unit ?? "", help: f.help ?? "", required: !!f.required, badge: !!f.badge }
				: { source: freeBuiltins.length ? freeBuiltins[0][0] : "custom", type: "text", group: this.schema!.groups[this.schema!.groups.length - 1].key },
			fields: [
				...(f ? [] : [
					{ key: "source", label: "Feld", type: "select" as const, required: true,
						options: [...freeBuiltins.map(([key, b]) => ({ key, text: `${b.label} (eingebaut)` })), { key: "custom", text: "Eigenes Feld …" }] },
					{ key: "group", label: "Gruppe", type: "select" as const, required: true, options: this.schema!.groups.map((g) => ({ key: g.key, text: g.label })) },
				]),
				{ key: "label", label: "Beschriftung", maxLength: 100, help: f?.builtin || !f ? "Bei eingebauten Feldern leer lassen für die Standardbeschriftung." : undefined },
				...(f?.builtin ? [] : [
					{ key: "type", label: "Typ (eigenes Feld)", type: "select" as const, options: Object.entries(typeTexts).map(([key, text]) => ({ key, text })) },
					{ key: "list", label: "Werteliste (bei Auswahl)", type: "select" as const, options: lists, placeholder: "–" },
					{ key: "unit", label: "Einheit", maxLength: 20, placeholder: "z. B. m, mm, m²" },
				]),
				{ key: "help", label: "Hinweis", maxLength: 300 },
				{ key: "required", label: "Pflichtfeld", type: "switch" as const },
				{ key: "badge", label: "Im Kopf als Badge zeigen", type: "switch" as const },
			],
			submit: async (x) => x,
		});
		if (!result) return null;
		const s = (k: string) => String(result[k] ?? "").trim();
		if (f) {
			return { label: s("label") || f.label, help: s("help") || null, required: !!result.required, badge: !!result.badge,
				...(f.builtin ? {} : { type: (s("type") || f.type) as SchemaFieldType, list: s("list") || null, unit: s("unit") || null }) };
		}
		const source = s("source");
		if (source !== "custom") {
			const b = builtinFields[source];
			return { key: source, label: s("label") || b.label, type: b.type, path: "", list: b.list ?? null, unit: b.unit ?? null, help: s("help") || null,
				required: !!result.required, badge: !!result.badge, builtin: true, group: s("group") };
		}
		const label = s("label");
		if (!label) { this.toast("Bitte eine Beschriftung angeben."); return null; }
		let key = keyOf(label).replace(/[^A-Za-z0-9_]/g, "_").replace(/^[^A-Za-z]+/, "") || "feld";
		while (used.has(key) || key in builtinFields) key = `${key}_2`;
		const type = (s("type") || "text") as SchemaFieldType;
		if (type === "select" && !s("list")) { this.toast("Für eine Auswahl bitte eine Werteliste wählen."); return null; }
		return { key, label, type, path: `project.custom.${key}`, list: type === "select" ? s("list") : null, unit: s("unit") || null, help: s("help") || null,
			required: !!result.required, badge: !!result.badge, builtin: false, group: s("group") };
	}

	async onSaveForm(): Promise<void> {
		if (!this.schema) return;
		const saved = await this.run(() => put<FormSchema>("forms/bauakte", { groups: this.schema!.groups }));
		if (saved) {
			this.toast("Formular gespeichert");
			await this.loadForm();
		}
	}

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

/** Schlüssel aus einer Bezeichnung: „Magnesia-Estrich (MA)“ → „magnesia_estrich_ma“. */
function keyOf(label: string): string {
	return label.toLowerCase().replace(/ä/g, "ae").replace(/ö/g, "oe").replace(/ü/g, "ue").replace(/ß/g, "ss")
		.replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "").slice(0, 40) || "wert";
}
