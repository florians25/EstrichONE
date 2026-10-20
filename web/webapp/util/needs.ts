/** Materialbedarf aus „Räume & Flächen“: Estrichvolumen je Estrichart und Dämmfläche je Dicke. */
import { quantity } from "./format";
import type { RoomOverview } from "./models";

export interface Need {
	key: string;
	description: string;
	detail: string;
	amount: number;
	unit: string;
}

const roundUp = (v: number, step: number) => Math.ceil(v / step - 1e-9) * step;

export function requirements(rooms: RoomOverview, reservePercent: number): Need[] {
	const factor = 1 + reservePercent / 100;
	const screed = new Map<string, { area: number; volume: number; missing: number }>();
	const insulation = new Map<number, number>();
	for (const r of rooms.rooms) {
		const area = r.areaSquareMeters ?? 0;
		if (area <= 0) continue;
		const name = r.screedTypeName ?? "Estrich (Art offen)";
		const entry = screed.get(name) ?? { area: 0, volume: 0, missing: 0 };
		entry.area += area;
		if (r.thicknessMillimeters) entry.volume += (area * r.thicknessMillimeters) / 1000;
		else entry.missing += 1;
		screed.set(name, entry);
		if (r.insulationMillimeters) insulation.set(r.insulationMillimeters, (insulation.get(r.insulationMillimeters) ?? 0) + area);
	}
	const needs: Need[] = [];
	for (const [name, e] of screed) {
		if (e.volume > 0) {
			needs.push({
				key: `screed-${name}`, description: name, unit: "m³", amount: roundUp(e.volume * factor, 0.5),
				detail: `${quantity(e.area)} m² · ${quantity(Math.round(e.volume * 100) / 100)} m³ netto${e.missing ? ` · ${e.missing} Räume ohne Dicke nicht berechnet` : ""}`,
			});
		}
	}
	for (const [mm, area] of [...insulation].sort((a, b) => a[0] - b[0])) {
		needs.push({ key: `ins-${mm}`, description: `Dämmung ${mm} mm`, unit: "m²", amount: roundUp(area * factor, 1), detail: `${quantity(area)} m² netto` });
	}
	return needs;
}
