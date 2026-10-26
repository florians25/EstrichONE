/**
 * Wertelisten der Stammdaten (B6): Estricharten, Konstruktionen, Objektarten, Festigkeitsklassen, Dokumentarten …
 * Einmal geladen und zwischengespeichert; Dokumentarten werden in die festen Beschriftungen übernommen.
 */
import { get } from "./api";
import { documentCategories } from "./labels";

export interface ValueListItem {
	id: string;
	list: string;
	key: string;
	label: string;
	shortLabel: string | null;
	sortOrder: number;
	isActive: boolean;
	version: number;
}

export const listNames: Record<string, string> = {
	objectType: "Objektarten", screedType: "Estricharten", construction: "Konstruktionen", strengthClass: "Festigkeitsklassen",
	priority: "Prioritäten", heatingType: "Heizungsarten", insulation: "Dämmungen", documentCategory: "Dokumentarten",
};

let loaded: Promise<ValueListItem[]> | null = null;

export function valueLists(force = false): Promise<ValueListItem[]> {
	if (force || !loaded) {
		loaded = get<ValueListItem[]>("value-lists").then((items) => {
			applyDocumentCategories(items);
			return items;
		}).catch(() => {
			loaded = null;
			return [];
		});
	}
	return loaded;
}

/** Einträge einer Liste, sortiert; ohne deaktivierte, außer der aktuelle Wert ist einer davon. */
export function itemsOf(all: ValueListItem[], list: string, keep?: string | null): ValueListItem[] {
	return all.filter((i) => i.list === list && (i.isActive || i.key === keep)).sort((a, b) => a.sortOrder - b.sortOrder || a.label.localeCompare(b.label));
}

export function labelOf(all: ValueListItem[], list: string, key: string | null | undefined): string {
	if (!key) return "–";
	return all.find((i) => i.list === list && i.key === key)?.label ?? key;
}

/** Dokumentarten: Bezeichnung und Reihenfolge aus den Stammdaten, neue Arten ergänzen (Symbol „Dokument“). */
function applyDocumentCategories(items: ValueListItem[]): void {
	const cats = items.filter((i) => i.list === "documentCategory").sort((a, b) => a.sortOrder - b.sortOrder);
	if (!cats.length) return;
	const map = documentCategories as Record<string, { text: string; icon: string; inactive?: boolean }>;
	const before = { ...map };
	for (const key of Object.keys(map)) delete map[key];
	for (const c of cats) map[c.key] = { text: c.label, icon: before[c.key]?.icon ?? "sap-icon://document", inactive: !c.isActive };
	for (const [key, value] of Object.entries(before)) if (!(key in map)) map[key] = value;
}
