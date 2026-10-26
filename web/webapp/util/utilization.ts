/** Auslastung einer Kolonne (SAP-typischer Balken): geplante Stunden / Kapazität der Arbeitstage (8 h je Tag). */
import type { AssignmentView } from "./models";

const hours = (a: AssignmentView): number => {
	if (!a.startTime) return 8;
	const [sh, sm] = a.startTime.split(":").map(Number);
	if (!a.endTime) return Math.max(1, Math.min(8, 16 - sh - sm / 60));
	const [eh, em] = a.endTime.split(":").map(Number);
	return Math.max(0.5, eh + em / 60 - (sh + sm / 60));
};

export interface Load { percent: number; state: "Success" | "Warning" | "Error"; text: string }

/** Ganztägige Einsätze zählen 8 h; ohne Ende bis 16 Uhr. Kapazität: Werktage (Mo–Fr) der Tage × 8 h. */
export function crewLoad(crewId: string, assignments: AssignmentView[], days: string[]): Load {
	const workdays = days.filter((d) => { const w = new Date(`${d}T00:00:00`).getDay(); return w !== 0 && w !== 6; }).length || 1;
	const set = new Set(days);
	const planned = assignments.filter((a) => a.crewId === crewId && set.has(a.day)).reduce((sum, a) => sum + hours(a), 0);
	const percent = Math.round((planned / (workdays * 8)) * 100);
	return { percent, state: percent > 100 ? "Error" : percent >= 80 ? "Warning" : "Success", text: `${percent} % ausgelastet (${Math.round(planned)} von ${workdays * 8} h)` };
}
