/**
 * Tagesübersicht: Einsätze eines Tages je Kolonne gruppieren und Tagesbezeichnungen bilden.
 * Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */
import { addDays } from "./days";
import { time } from "./format";
import { assignmentKinds } from "./labels";
import type { AssignmentView } from "./models";

export interface DayAssignment extends AssignmentView {
	startText: string;
	endText: string;
	siteText: string;
	kindText: string;
	kindIcon: string;
	kindColor: string;
	peopleText: string;
	resourceText: string;
	warningText: string;
	warningTooltip: string;
}

export interface DayCrew {
	key: string;
	name: string;
	color: string;
	countText: string;
	warningText: string;
	items: DayAssignment[];
}

/** „Heute“, „Morgen“, „Gestern“, sonst „in 3 Tagen“ / „vor 2 Tagen“. */
export function relativeDay(day: string, today: string): string {
	if (day === today) return "Heute";
	if (day === addDays(today, 1)) return "Morgen";
	if (day === addDays(today, -1)) return "Gestern";
	const diff = Math.round((Date.parse(`${day}T12:00:00`) - Date.parse(`${today}T12:00:00`)) / 86_400_000);
	return diff > 0 ? `in ${diff} Tagen` : `vor ${-diff} Tagen`;
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** Einsätze nach Kolonne (Name), darin nach Beginn; ganztägige zuerst, Einsätze ohne Kolonne am Ende. */
export function groupByCrew(assignments: AssignmentView[]): DayCrew[] {
	const crews = new Map<string, DayCrew>();
	const sorted = [...assignments].sort((a, b) => (a.startTime ?? "").localeCompare(b.startTime ?? ""));
	for (const a of sorted) {
		const key = a.crewId ?? "";
		let crew = crews.get(key);
		if (!crew) {
			crew = { key, name: a.crewName ?? "Ohne Kolonne", color: a.crewColor ?? "", countText: "", warningText: "", items: [] };
			crews.set(key, crew);
		}
		const kind = assignmentKinds[a.kind] ?? assignmentKinds.other;
		crew.items.push({
			...a,
			startText: a.startTime ? time(a.startTime) : "ganztags",
			endText: a.startTime && a.endTime ? `bis ${time(a.endTime)}` : "",
			siteText: [a.bauakteNumber, a.bauakteTitle].filter(Boolean).join(" "),
			kindText: a.activity && a.activity !== kind.activity ? `${kind.text} · ${a.activity}` : kind.text,
			kindIcon: kind.icon,
			kindColor: kind.color,
			peopleText: a.people.map((p) => p.name).join(", "),
			resourceText: a.resources.map((r) => r.name).join(", "),
			warningText: a.warnings.length ? plural(a.warnings.length, "Hinweis", "Hinweise") : "",
			warningTooltip: a.warnings.join("\n"),
		});
	}
	const list = [...crews.values()].sort((a, b) => (a.key === "" ? 1 : b.key === "" ? -1 : a.name.localeCompare(b.name, "de")));
	for (const crew of list) {
		const people = new Set(crew.items.flatMap((i) => i.people.map((p) => p.id)));
		crew.countText = [plural(crew.items.length, "Einsatz", "Einsätze"), people.size ? plural(people.size, "Person", "Personen") : ""]
			.filter(Boolean).join(" · ");
		const warnings = crew.items.reduce((n, i) => n + i.warnings.length, 0);
		crew.warningText = warnings ? plural(warnings, "Hinweis", "Hinweise") : "";
	}
	return list;
}
