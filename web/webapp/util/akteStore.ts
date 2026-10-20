/**
 * Daten einer Bauakte, die mehrere Abschnitte gemeinsam nutzen (Räume, Dateien, Aufgaben …): einmal laden,
 * überall gleich aktuell. `invalidate` nach Änderungen; Abschnitte hören auf das Ereignis „changed“.
 */
import EventBus from "sap/ui/core/EventBus";
import { get } from "./api";
import { isoDay, addDays } from "./days";
import type {
	BauakteHistoryEntry, DiaryEntry, FileResponse, Inspection, NoteDto, OrderResponse, PlanningRange, RoomOverview, WorkItemDto,
} from "./models";

type Key = "rooms" | "files" | "work" | "diary" | "inspections" | "notes" | "history" | "orders" | "planning";

const loaders: Record<Key, (id: string) => Promise<unknown>> = {
	rooms: (id) => get<RoomOverview>(`bauakten/${id}/rooms`),
	files: (id) => get<FileResponse[]>(`bauakten/${id}/files`),
	work: (id) => get<WorkItemDto[]>(`bauakten/${id}/work-items`),
	diary: (id) => get<DiaryEntry[]>(`bauakten/${id}/diary`),
	inspections: (id) => get<Inspection[]>(`bauakten/${id}/inspections`),
	notes: (id) => get<NoteDto[]>(`bauakten/${id}/notes`),
	history: (id) => get<BauakteHistoryEntry[]>(`bauakten/${id}/history`),
	orders: (id) => get<OrderResponse[]>("material-orders", { bauakteId: id }),
	planning: async (id) => {
		// Der Server liefert höchstens 62 Tage je Abfrage: drei Fenster von 60 Tagen (zwei Monate zurück, vier voraus).
		const today = isoDay(new Date());
		const windows = [-60, 0, 60, 120].slice(0, 3).map((offset) => ({ from: addDays(today, offset), to: addDays(today, offset + 59) }));
		const parts = await Promise.all(windows.map((w) => get<PlanningRange>("planning/range", w)));
		return {
			from: windows[0].from, to: windows[windows.length - 1].to,
			assignments: parts.flatMap((r) => r.assignments).filter((a) => a.bauakteId === id),
			absences: parts.flatMap((r) => r.absences),
		};
	},
};

const cache = new Map<string, Promise<unknown>>();

function load<T>(key: Key, id: string, force = false): Promise<T> {
	const cacheKey = `${key}:${id}`;
	if (force || !cache.has(cacheKey)) {
		const promise = loaders[key](id).catch((error) => {
			cache.delete(cacheKey);
			throw error;
		});
		cache.set(cacheKey, promise);
	}
	return cache.get(cacheKey) as Promise<T>;
}

export const akte = {
	rooms: (id: string, force = false) => load<RoomOverview>("rooms", id, force),
	files: (id: string, force = false) => load<FileResponse[]>("files", id, force),
	work: (id: string, force = false) => load<WorkItemDto[]>("work", id, force),
	diary: (id: string, force = false) => load<DiaryEntry[]>("diary", id, force),
	inspections: (id: string, force = false) => load<Inspection[]>("inspections", id, force),
	notes: (id: string, force = false) => load<NoteDto[]>("notes", id, force),
	history: (id: string, force = false) => load<BauakteHistoryEntry[]>("history", id, force),
	orders: (id: string, force = false) => load<OrderResponse[]>("orders", id, force),
	planning: (id: string, force = false) => load<PlanningRange>("planning", id, force),

	/** Nach einer Änderung neu laden lassen; Verlauf und Hinweise ändern sich fast immer mit. */
	invalidate(id: string, ...keys: Key[]): void {
		for (const key of [...keys, "history" as Key]) cache.delete(`${key}:${id}`);
		EventBus.getInstance().publish("bauakte", "changed", { id, keys: [...keys, "history"] });
	},

	clear(id?: string): void {
		for (const key of [...cache.keys()]) {
			if (!id || key.endsWith(`:${id}`)) cache.delete(key);
		}
	},
};
