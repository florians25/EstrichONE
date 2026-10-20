/** Stammdaten-Katalog (Textbausteine, Ordnervorlagen, Bauakten-Vorlagen) mit kurzem Cache. */
import { get } from "./api";
import { parseNumber } from "./format";

export type CatalogKind = "textBlock" | "folderTemplate" | "bauakteTemplate";

export interface TemplateRoom { name: string; floor?: string | null; areaSquareMeters?: number | null; thicknessMillimeters?: number | null; underfloorHeating?: boolean }

export interface CatalogEntry<T = Record<string, unknown>> {
	id: string;
	kind: CatalogKind;
	name: string;
	content: T;
	sortOrder: number;
	isActive: boolean;
	version: number;
}

export type TextBlock = CatalogEntry<{ text: string }>;
export type FolderTemplate = CatalogEntry<{ category: string; folders: string[] }>;
export type BauakteTemplate = CatalogEntry<{ description?: string | null; rooms?: TemplateRoom[] }>;

const cache = new Map<string, { at: number; data: Promise<CatalogEntry[]> }>();

export function catalog<T extends CatalogEntry>(kind: CatalogKind, force = false): Promise<T[]> {
	const hit = cache.get(kind);
	if (!force && hit && Date.now() - hit.at < 60_000) return hit.data as Promise<T[]>;
	const data = get<CatalogEntry[]>("catalog", { kind, active: true }).catch(() => [] as CatalogEntry[]);
	cache.set(kind, { at: Date.now(), data });
	return data as Promise<T[]>;
}

export function invalidateCatalog(): void {
	cache.clear();
}

/** Räume als Zeilen „Name; Geschoss; Fläche; Dicke mm; FBH“ (Semikolon getrennt). */
export function roomsToText(rooms: TemplateRoom[] | undefined): string {
	return (rooms ?? []).map((r) => [r.name, r.floor ?? "", r.areaSquareMeters ?? "", r.thicknessMillimeters ?? "", r.underfloorHeating ? "FBH" : ""]
		.join("; ").replace(/(; )+$/, "")).join("\n");
}

export function textToRooms(text: string): TemplateRoom[] {
	return text.split("\n").map((line) => line.split(";").map((p) => p.trim())).filter((p) => p[0]).map(([name, floor, area, thickness, heating]) => ({
		name, floor: floor || null, areaSquareMeters: area ? parseNumber(area) : null,
		thicknessMillimeters: thickness ? Math.round(parseNumber(thickness) ?? 0) || null : null, underfloorHeating: /^(fbh|ja|x|1|heiz)/i.test(heating ?? ""),
	}));
}

