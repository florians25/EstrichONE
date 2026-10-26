/**
 * Formular-Schema der Basisdaten (B7): Gruppen und Felder aus dem Formular-Designer. Wird im Anlage-Assistenten,
 * beim Bearbeiten der Kennwerte, in den Basisdaten der Übersicht und für die Badges im Kopf verwendet.
 */
import { get } from "./api";
import { area, date } from "./format";
import type { Field } from "./forms";
import type { BauakteDetail } from "./models";
import type { BauakteProject } from "./project";

export type SchemaFieldType = "text" | "textarea" | "number" | "integer" | "select" | "boolean" | "date" | "check";

export interface SchemaField {
	key: string;
	label: string;
	type: SchemaFieldType;
	path: string;
	list?: string | null;
	unit?: string | null;
	required?: boolean;
	help?: string | null;
	badge?: boolean;
	builtin?: boolean;
	/** Erstbegehung: Fotos je Punkt anbieten. */
	photos?: boolean;
	/** Erstbegehung: Ziel in den Basisdaten (z. B. project.totalArea). */
	target?: string | null;
}

export interface SchemaGroup { key: string; label: string; fields: SchemaField[] }
export interface ListOption { key: string; label: string; shortLabel: string | null }
export interface FormSchema { id: string; key: string; version: number; groups: SchemaGroup[]; lists: Record<string, ListOption[]> }

const cached = new Map<string, Promise<FormSchema | null>>();

/** Formular-Schema der Stammdaten („bauakte“, „erstbegehung“), zwischengespeichert. */
export function formSchema(key: string, force = false): Promise<FormSchema | null> {
	if (force || !cached.has(key)) {
		cached.set(key, get<FormSchema>(`forms/${key}`).catch(() => {
			cached.delete(key);
			return null;
		}));
	}
	return cached.get(key)!;
}

export function bauakteSchema(force = false): Promise<FormSchema | null> {
	return formSchema("bauakte", force);
}

/** Wert eines Feldes aus der Bauakte lesen (path: project.x, project.custom.x oder Feld der Bauakte). */
export function readValue(b: Pick<BauakteDetail, "project" | "plannedStart" | "plannedEnd"> | null, path: string): unknown {
	if (!b) return null;
	const parts = path.split(".");
	let cur: unknown = b;
	for (const p of parts) cur = cur && typeof cur === "object" ? (cur as Record<string, unknown>)[p] : undefined;
	return cur ?? null;
}

/** Formularwerte (Schlüssel = Feldschlüssel) in project/custom und Bauakten-Felder schreiben. */
export function writeValues(schema: FormSchema, values: Record<string, unknown>, base: BauakteProject | null | undefined):
	{ project: BauakteProject; plannedStart?: string | null; plannedEnd?: string | null } {
	const project: BauakteProject & { custom?: Record<string, unknown> } = { ...(base ?? {}), custom: { ...((base as { custom?: Record<string, unknown> } | null)?.custom ?? {}) } };
	const out: { project: BauakteProject; plannedStart?: string | null; plannedEnd?: string | null } = { project };
	for (const f of fieldsOf(schema)) {
		if (!(f.key in values)) continue;
		let v = values[f.key];
		if (typeof v === "string") v = v.trim() || null;
		if (v === undefined || (typeof v === "number" && !Number.isFinite(v))) v = null;
		if (f.type === "integer" && typeof v === "number") v = Math.round(v);
		if (f.path.startsWith("project.custom.")) {
			const key = f.path.slice("project.custom.".length);
			if (v === null) delete project.custom![key]; else project.custom![key] = v;
		} else if (f.path.startsWith("project.")) {
			(project as Record<string, unknown>)[f.path.slice("project.".length)] = v;
		} else if (f.path === "plannedStart" || f.path === "plannedEnd") {
			out[f.path] = v as string | null;
		}
	}
	if (!Object.keys(project.custom!).length) delete project.custom;
	return out;
}

export function fieldsOf(schema: FormSchema): SchemaField[] {
	return schema.groups.flatMap((g) => g.fields);
}

/** Felder für openForm (Gruppen als Abschnitte). */
export function formFields(schema: FormSchema, current?: Pick<BauakteDetail, "project" | "plannedStart" | "plannedEnd"> | null): Field[] {
	const out: Field[] = [];
	for (const g of schema.groups) {
		out.push({ key: `__${g.key}`, label: g.label, type: "section" });
		for (const f of g.fields) {
			const keep = current ? readValue(current, f.path) as string | null : null;
			const options = f.list ? (schema.lists[f.list] ?? []).map((o) => ({ key: o.key, text: o.label })) : undefined;
			if (options && keep && !options.some((o) => o.key === keep)) options.push({ key: keep, text: keep });
			const base: Field = { key: f.key, label: f.label, required: !!f.required, help: f.help ?? undefined, unit: f.unit ?? undefined };
			switch (f.type) {
				case "select": out.push(f.builtin && f.key === "strengthClass"
					? { ...base, maxLength: 30, suggestions: options?.map((o) => o.text) }
					: { ...base, type: "select", options, placeholder: "–" }); break;
				case "text": out.push({ ...base, maxLength: f.builtin ? 100 : 2000, suggestions: f.list ? (schema.lists[f.list] ?? []).map((o) => o.label) : undefined }); break;
				case "textarea": out.push({ ...base, type: "textarea", rows: 2, maxLength: f.builtin ? 1000 : 2000 }); break;
				case "number": out.push({ ...base, type: "number", min: 0, max: 1_000_000 }); break;
				case "integer": out.push({ ...base, type: "integer", min: 0, max: 100_000 }); break;
				case "boolean": out.push({ ...base, type: "switch" }); break;
				case "date": out.push({ ...base, type: "date" }); break;
			}
		}
	}
	return out;
}

/** Startwerte für openForm aus der Bauakte. */
export function initialValues(schema: FormSchema, b: Pick<BauakteDetail, "project" | "plannedStart" | "plannedEnd"> | null): Record<string, unknown> {
	const out: Record<string, unknown> = {};
	for (const f of fieldsOf(schema)) {
		const v = readValue(b, f.path);
		if (v !== null && v !== undefined) out[f.key] = v;
	}
	return out;
}

/** Lesbarer Wert für Anzeige und Badges. */
export function displayValue(schema: FormSchema, f: SchemaField, v: unknown): string {
	if (v === null || v === undefined || v === "") return "–";
	if (f.type === "boolean") return v ? "ja" : "nein";
	if (f.type === "date") return date(String(v));
	if (f.type === "select" && f.list) return (schema.lists[f.list] ?? []).find((o) => o.key === v)?.label ?? String(v);
	if (f.type === "number") return f.unit === "m²" ? area(Number(v)) : `${new Intl.NumberFormat("de-DE", { maximumFractionDigits: 2 }).format(Number(v))}${f.unit ? ` ${f.unit}` : ""}`;
	if (f.type === "integer") return `${v}${f.unit ? ` ${f.unit}` : ""}`;
	return String(v);
}

/** Gruppen mit lesbaren Werten für die Basisdaten-Anzeige. */
export function viewGroups(schema: FormSchema, b: Pick<BauakteDetail, "project" | "plannedStart" | "plannedEnd"> | null): { label: string; rows: { label: string; value: string }[] }[] {
	return schema.groups.map((g) => ({ label: g.label, rows: g.fields.map((f) => ({ label: f.label, value: displayValue(schema, f, readValue(b, f.path)) })) }));
}

/** Badges für den Kopf: Felder mit „badge“, nur wenn gesetzt (Ja/Nein nur bei „ja“). */
export function badges(schema: FormSchema, b: Pick<BauakteDetail, "project" | "plannedStart" | "plannedEnd"> | null): { text: string; state: string; icon: string }[] {
	const out: { text: string; state: string; icon: string }[] = [];
	for (const f of fieldsOf(schema).filter((x) => x.badge)) {
		const v = readValue(b, f.path);
		if (v === null || v === undefined || v === "" || v === false) continue;
		if (f.key === "priority" && !(v === "high" || v === "urgent")) continue;
		const short = f.type === "select" && f.list ? (schema.lists[f.list] ?? []).find((o) => o.key === v)?.shortLabel : null;
		const text = f.type === "boolean" ? f.label : short ?? displayValue(schema, f, v);
		const state = f.key === "priority" ? (v === "urgent" ? "Error" : "Warning") : f.key === "underfloorHeating" ? "Indication05" : f.key === "screedType" ? "Information" : "None";
		out.push({ text: f.key === "priority" ? `Priorität ${text}` : text, state, icon: f.key === "underfloorHeating" ? "sap-icon://temperature" : "" });
	}
	return out;
}
