/**
 * Kennwerte des Bauvorhabens (Feld „project“ der Bauakte, auch im Sync): Auswahllisten, Formularfelder,
 * Anzeige in den Basisdaten und Badges im Kopf.
 */
import Input from "sap/m/Input";
import Select from "sap/m/Select";
import Item from "sap/ui/core/Item";
import CheckBox from "sap/m/CheckBox";
import TextArea from "sap/m/TextArea";
import DatePicker from "sap/m/DatePicker";
import Label from "sap/m/Label";
import Title from "sap/m/Title";
import type Control from "sap/ui/core/Control";
import { area, parseNumber } from "./format";
import type { Field } from "./forms";

export interface BauakteProject {
	objectType?: string | null;
	totalArea?: number | null;
	screedType?: string | null;
	construction?: string | null;
	nominalThicknessMm?: number | null;
	strengthClass?: string | null;
	underfloorHeating?: boolean | null;
	heatingType?: string | null;
	insulation?: string | null;
	insulationThicknessMm?: number | null;
	floors?: string | null;
	priority?: string | null;
	siteAccess?: string | null;
	waterOnSite?: boolean | null;
	powerOnSite?: boolean | null;
}

export const objectTypes: Record<string, string> = {
	efh: "Einfamilienhaus", mfh: "Mehrfamilienhaus", commercial: "Gewerbe", industrial: "Industrie/Halle",
	public: "Öffentlicher Bau", renovation: "Sanierung", other: "Sonstiges",
};
export const screedTypes: Record<string, string> = {
	ct: "Zementestrich (CT)", caf: "Calciumsulfat-Fließestrich (CAF)", as: "Gussasphalt (AS)", dry: "Trockenestrich", other: "Sonstiges",
};
export const screedShort: Record<string, string> = { ct: "CT", caf: "CAF", as: "AS", dry: "Trockenestrich", other: "Sonstiges" };
export const constructions: Record<string, string> = {
	bonded: "Verbundestrich", separatingLayer: "Auf Trennlage", floating: "Schwimmend", heated: "Heizestrich",
};
export const priorities: Record<string, { text: string; state: string }> = {
	low: { text: "Niedrig", state: "None" }, normal: { text: "Normal", state: "None" },
	high: { text: "Hoch", state: "Warning" }, urgent: { text: "Dringend", state: "Error" },
};
const heatingTypes = ["Warmwasser-Fußbodenheizung", "Elektrische Fußbodenheizung", "Fußbodenkühlung"];
const strengthClasses = ["C20-F3", "C25-F4", "C30-F5", "C35-F5", "CA-C25-F4", "CA-C30-F5", "IC10", "IC15"];

const opts = (map: Record<string, string>) => Object.entries(map).map(([key, text]) => ({ key, text }));
const yesNo = (v: boolean | null | undefined) => (v === true ? "ja" : v === false ? "nein" : "–");

/** Felder für openForm (Kennwerte bearbeiten). */
export function projectFields(): Field[] {
	return [
		{ key: "objectType", label: "Objektart", type: "select", options: opts(objectTypes), placeholder: "–" },
		{ key: "floors", label: "Geschosse", maxLength: 100, placeholder: "z. B. KG, EG, OG" },
		{ key: "totalArea", label: "Gesamtfläche", type: "number", min: 0, max: 1_000_000, unit: "m²" },
		{ key: "priority", label: "Priorität", type: "select", options: Object.entries(priorities).map(([key, p]) => ({ key, text: p.text })), placeholder: "–" },
		{ key: "s1", label: "Estrich", type: "section" },
		{ key: "screedType", label: "Estrichart", type: "select", options: opts(screedTypes), placeholder: "–" },
		{ key: "construction", label: "Konstruktion", type: "select", options: opts(constructions), placeholder: "–" },
		{ key: "nominalThicknessMm", label: "Nenndicke", type: "integer", min: 0, max: 1000, unit: "mm" },
		{ key: "strengthClass", label: "Festigkeitsklasse", maxLength: 30, suggestions: strengthClasses, placeholder: "z. B. C25-F4" },
		{ key: "underfloorHeating", label: "Fußbodenheizung", type: "switch" },
		{ key: "heatingType", label: "Art der Heizung", maxLength: 100, suggestions: heatingTypes },
		{ key: "insulation", label: "Dämmung", maxLength: 100, placeholder: "z. B. EPS 035 DEO" },
		{ key: "insulationThicknessMm", label: "Dämmdicke", type: "integer", min: 0, max: 1000, unit: "mm" },
		{ key: "s2", label: "Baustelle", type: "section" },
		{ key: "siteAccess", label: "Zugang / Besonderheiten", type: "textarea", rows: 2, maxLength: 1000, placeholder: "Pumpenstandort, Zufahrt, Etage …" },
		{ key: "waterOnSite", label: "Wasser vor Ort", type: "switch" },
		{ key: "powerOnSite", label: "Strom vor Ort", type: "switch" },
	];
}

/** Werte aus openForm in das Sync-/API-Format bringen. */
export function projectFromValues(v: Record<string, unknown>): BauakteProject {
	const s = (k: string) => (typeof v[k] === "string" && (v[k] as string).trim() ? (v[k] as string).trim() : null);
	const n = (k: string) => (typeof v[k] === "number" && Number.isFinite(v[k]) ? (v[k] as number) : null);
	const b = (k: string) => (typeof v[k] === "boolean" ? (v[k] as boolean) : null);
	return {
		objectType: s("objectType"), totalArea: n("totalArea"), screedType: s("screedType"), construction: s("construction"),
		nominalThicknessMm: n("nominalThicknessMm"), strengthClass: s("strengthClass"), underfloorHeating: b("underfloorHeating"),
		heatingType: s("heatingType"), insulation: s("insulation"), insulationThicknessMm: n("insulationThicknessMm"),
		floors: s("floors"), priority: s("priority"), siteAccess: s("siteAccess"), waterOnSite: b("waterOnSite"), powerOnSite: b("powerOnSite"),
	};
}

/** Lesbare Werte für die Basisdaten. */
export function projectView(p: BauakteProject | null | undefined): Record<string, string> {
	const q = p ?? {};
	return {
		objectType: q.objectType ? objectTypes[q.objectType] ?? q.objectType : "–",
		totalArea: q.totalArea != null ? area(q.totalArea) : "–",
		screedType: q.screedType ? screedTypes[q.screedType] ?? q.screedType : "–",
		construction: q.construction ? constructions[q.construction] ?? q.construction : "–",
		thickness: q.nominalThicknessMm != null ? `${q.nominalThicknessMm} mm` : "–",
		strengthClass: q.strengthClass || "–",
		heating: q.underfloorHeating ? `ja${q.heatingType ? ` · ${q.heatingType}` : ""}` : yesNo(q.underfloorHeating),
		insulation: [q.insulation, q.insulationThicknessMm != null ? `${q.insulationThicknessMm} mm` : null].filter(Boolean).join(" · ") || "–",
		floors: q.floors || "–",
		priority: q.priority ? priorities[q.priority]?.text ?? q.priority : "–",
		siteAccess: q.siteAccess || "–",
		utilities: `Wasser ${yesNo(q.waterOnSite)} · Strom ${yesNo(q.powerOnSite)}`,
	};
}

/** Kompakte farbige Badges für den Kopf. */
export function projectBadges(p: BauakteProject | null | undefined): { text: string; state: string; icon: string }[] {
	const q = p ?? {};
	const out: { text: string; state: string; icon: string }[] = [];
	if (q.screedType) out.push({ text: [screedShort[q.screedType] ?? q.screedType, q.strengthClass].filter(Boolean).join(" "), state: "Information", icon: "sap-icon://product" });
	if (q.construction && q.construction !== "heated") out.push({ text: constructions[q.construction] ?? q.construction, state: "Information", icon: "" });
	if (q.underfloorHeating || q.construction === "heated") out.push({ text: "Heizestrich", state: "Indication05", icon: "sap-icon://temperature" });
	if (q.nominalThicknessMm != null) out.push({ text: `${q.nominalThicknessMm} mm`, state: "None", icon: "" });
	if (q.objectType) out.push({ text: objectTypes[q.objectType] ?? q.objectType, state: "None", icon: "sap-icon://building" });
	if (q.priority === "high" || q.priority === "urgent") out.push({ text: `Priorität ${priorities[q.priority].text}`, state: priorities[q.priority].state, icon: "sap-icon://high-priority" });
	return out;
}

/** Steuerelemente für den Anlage-Assistenten (Abschnitt „Estrich-Kennwerte“). */
export function projectWizardControls(prefix: string): { content: Control[]; read: () => BauakteProject; period: () => { start: string | null; end: string | null } } {
	const select = (id: string, map: Record<string, string>) => {
		const s = new Select(`${prefix}${id}`, { width: "100%", forceSelection: false, items: [new Item({ key: "", text: "–" })] });
		for (const [key, text] of Object.entries(map)) s.addItem(new Item({ key, text }));
		return s;
	};
	const objectType = select("ObjectType", objectTypes);
	const screedType = select("ScreedType", screedTypes);
	const construction = select("Construction", constructions);
	const priority = select("Priority", Object.fromEntries(Object.entries(priorities).map(([k, v]) => [k, v.text])));
	const totalArea = new Input(`${prefix}TotalArea`, { width: "100%", description: "m²", fieldWidth: "70%", textAlign: "End" });
	const thickness = new Input(`${prefix}Thickness`, { width: "100%", description: "mm", fieldWidth: "70%", textAlign: "End", type: "Number" });
	const strength = new Input(`${prefix}Strength`, { width: "100%", maxLength: 30, placeholder: "z. B. C25-F4", showSuggestion: true,
		suggestionItems: strengthClasses.map((t) => new Item({ text: t })) });
	const heating = new CheckBox(`${prefix}Heating`, { text: "Fußbodenheizung (Heizestrich)" });
	const insulation = new Input(`${prefix}Insulation`, { width: "100%", maxLength: 100, placeholder: "Art, z. B. EPS 035 DEO" });
	const insulationMm = new Input(`${prefix}InsulationMm`, { width: "100%", description: "mm", fieldWidth: "70%", textAlign: "End", type: "Number" });
	const floors = new Input(`${prefix}Floors`, { width: "100%", maxLength: 100, placeholder: "z. B. KG, EG, OG" });
	const start = new DatePicker(`${prefix}Start`, { width: "100%", valueFormat: "yyyy-MM-dd", displayFormat: "dd.MM.yyyy" });
	const end = new DatePicker(`${prefix}End`, { width: "100%", valueFormat: "yyyy-MM-dd", displayFormat: "dd.MM.yyyy" });
	const access = new TextArea(`${prefix}Access`, { rows: 2, width: "100%", maxLength: 1000, placeholder: "Pumpenstandort, Zufahrt, Etage, Besonderheiten" });
	const water = new CheckBox(`${prefix}Water`, { text: "Wasser vor Ort" });
	const power = new CheckBox(`${prefix}Power`, { text: "Strom vor Ort" });
	const l = (text: string, c: Control) => new Label({ text, labelFor: c });
	const int = (i: Input) => { const v = parseNumber(i.getValue()); return v == null ? null : Math.round(v); };
	return {
		content: [
			new Title({ text: "Estrich-Kennwerte", level: "H5" }),
			l("Objektart", objectType), objectType,
			l("Gesamtfläche", totalArea), totalArea,
			l("Estrichart", screedType), screedType,
			l("Konstruktion", construction), construction,
			l("Nenndicke", thickness), thickness,
			l("Festigkeitsklasse", strength), strength,
			new Label({ text: "" }), heating,
			l("Dämmung", insulation), insulation,
			l("Dämmdicke", insulationMm), insulationMm,
			l("Geschosse", floors), floors,
			l("Ausführung von", start), start,
			l("bis", end), end,
			l("Priorität", priority), priority,
			l("Zugang / Besonderheiten", access), access,
			new Label({ text: "Vor Ort" }), water, power,
		],
		read: () => ({
			objectType: objectType.getSelectedKey() || null, totalArea: parseNumber(totalArea.getValue()),
			screedType: screedType.getSelectedKey() || null,
			construction: construction.getSelectedKey() || (heating.getSelected() ? "heated" : null),
			nominalThicknessMm: int(thickness), strengthClass: strength.getValue().trim() || null,
			underfloorHeating: heating.getSelected() ? true : null, heatingType: null,
			insulation: insulation.getValue().trim() || null, insulationThicknessMm: int(insulationMm),
			floors: floors.getValue().trim() || null, priority: priority.getSelectedKey() || null,
			siteAccess: access.getValue().trim() || null, waterOnSite: water.getSelected() ? true : null, powerOnSite: power.getSelected() ? true : null,
		}),
		period: () => ({ start: start.getValue() || null, end: end.getValue() || null }),
	};
}
