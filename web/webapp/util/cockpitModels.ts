/** Typen des Disponenten-Cockpits (contracts/openapi: GetPlanningCockpit, GetDayPlan, ApplyPlanBatch …), siehe docs/planung-api.md. */
import type { AbsenceView, AssignmentKind, AssignmentView as BaseAssignment } from "./models";
import type { CalendarOccurrence, PersonRef, ResourceRef } from "./planningModels";
import { addDays, mondayOf, parseDay } from "./days";

export interface PlanAssignment extends BaseAssignment {
	excludedUserIds?: string[] | null;
	/** Fahrtreihenfolge (0 = nach Uhrzeit). */
	sequence?: number;
	/** Seit der Freigabe: neu bzw. geändert. */
	change?: "new" | "changed" | null;
}

export interface PlanReleaseItem { assignmentId: string; version: number; title: string; crewName: string | null; activity: string }
export interface PlanReleaseInfo { day: string; releasedAt: string; releasedByName: string; note: string | null; changedCount: number; removed: PlanReleaseItem[] }
export interface CockpitDay { day: string; isWorkday: boolean; holiday: string | null; release: PlanReleaseInfo | null }
export interface CockpitMember { id: string; name: string; absenceNote: string | null }
export interface CockpitCell {
	day: string; members: CockpitMember[]; vehicles: ResourceRef[]; rosterChanged: boolean; rosterNote: string | null; assignmentIds: string[];
	plannedMinutes: number; capacityMinutes: number; availableMembers: number; warnings: string[];
}
export interface CockpitCrew {
	id: string; name: string; color: string | null; leaderUserId: string | null; leaderName: string | null; members: PersonRef[]; vehicles: ResourceRef[];
	cells: CockpitCell[];
}
export interface CockpitBoard {
	from: string; to: string; days: CockpitDay[]; crews: CockpitCrew[]; assignments: PlanAssignment[]; absences: AbsenceView[];
	appointments: CalendarOccurrence[]; canPlan: boolean;
}

export interface DayPlanStop {
	assignmentId: string; order: number; sequence: number; startTime: string | null; endTime: string | null; kind: AssignmentKind; activity: string;
	bauakteId: string; number: string | null; title: string; address: string | null; latitude: number | null; longitude: number | null;
	siteContactName: string | null; siteContactPhone: string | null; navigationUrl: string | null; notes: string | null; people: PersonRef[];
	resources: ResourceRef[]; warnings: string[]; change: string | null; version: number;
}
export interface DayPlanCrew {
	id: string; name: string; color: string | null; leaderName: string | null; members: CockpitMember[]; vehicles: ResourceRef[]; rosterChanged: boolean;
	rosterNote: string | null; isMine: boolean; stops: DayPlanStop[]; warnings: string[];
}
export interface DayPlan {
	day: string; holiday: string | null; release: PlanReleaseInfo | null; canPlan: boolean; myCrewIds: string[]; crews: DayPlanCrew[];
	withoutCrew: DayPlanStop[]; absent: CockpitMember[]; free: PersonRef[];
}

export interface SaveAssignment {
	bauakteId: string; day: string; startTime: string | null; endTime: string | null; activity: string; crewId: string | null; userIds: string[];
	resourceIds: string[]; notes: string | null; kind: AssignmentKind; excludedUserIds?: string[] | null;
}

export type PlanOperation =
	| { op: "upsert"; id: string; assignment: SaveAssignment; sequence?: number }
	| { op: "move"; id: string; day?: string; changeCrew?: boolean; crewId?: string | null; setTime?: boolean; startTime?: string | null; endTime?: string | null; sequence?: number }
	| { op: "delete"; id: string };

export interface PlanBatchResult { assignments: PlanAssignment[]; deletedIds: string[]; warnings: string[]; dryRun: boolean }

/** Speicherbare Form eines Einsatzes (für Kopieren und Rückgängig). Zusätzliche Personen = nicht in der Kolonne des Tages. */
export function toSave(a: PlanAssignment, crewMembers: string[]): SaveAssignment {
	const members = new Set(crewMembers);
	return {
		bauakteId: a.bauakteId, day: a.day, startTime: a.startTime, endTime: a.endTime, activity: a.activity, crewId: a.crewId,
		userIds: a.crewId ? a.people.map((p) => p.id).filter((id) => !members.has(id)) : a.people.map((p) => p.id),
		resourceIds: a.resources.map((r) => r.id), notes: a.notes, kind: a.kind, excludedUserIds: a.excludedUserIds ?? [],
	};
}

/** Minuten wie im Server (CockpitService.Minutes): ganztägig 8 h, ohne Ende bis 16 Uhr. */
export function minutes(start: string | null, end: string | null): number {
	if (!start) return 480;
	const m = (t: string) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5));
	if (!end) return Math.min(480, Math.max(60, 16 * 60 - m(start)));
	return Math.max(30, m(end) - m(start));
}

/** Stunden mit deutschem Komma, z. B. „5,5“ (auf eine Nachkommastelle). */
export const hours = (min: number): string => (Math.round(min / 6) / 10).toLocaleString("de-DE");

export const hhmm = (t: string | null | undefined): string => (t ? t.slice(0, 5) : "");
export const toTime = (min: number): string => `${String(Math.floor(min / 60)).padStart(2, "0")}:${String(min % 60).padStart(2, "0")}:00`;
export const ofTime = (t: string): number => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5));

const dayDiff = (a: string, b: string) => Math.round((parseDay(b).getTime() - parseDay(a).getTime()) / 86_400_000);

/** Sichtbare Tage je Ansicht: Tag, Woche (Mo–So), 2 Wochen, Monat (1.–letzter). */
export function cockpitDays(mode: "day" | "week" | "twoWeeks" | "month", anchor: string): string[] {
	if (mode === "day") return [anchor];
	if (mode === "week" || mode === "twoWeeks") {
		const monday = mondayOf(anchor);
		return Array.from({ length: mode === "week" ? 7 : 14 }, (_, i) => addDays(monday, i));
	}
	const first = `${anchor.slice(0, 7)}-01`;
	const f = parseDay(first);
	const count = new Date(f.getFullYear(), f.getMonth() + 1, 0).getDate();
	return Array.from({ length: count }, (_, i) => addDays(first, i));
}

/** Dringlichkeit eines Bauvorhabens im Vorrat nach geplantem Beginn. */
export function urgencyOf(b: { plannedStart: string | null }, today: string): "overdue" | "soon" | "later" | "none" {
	if (!b.plannedStart) return "none";
	if (b.plannedStart < today) return "overdue";
	return dayDiff(today, b.plannedStart) <= 7 ? "soon" : "later";
}

