/** Einsatz planen oder bearbeiten (Plantafel, Vorrat, Bauakte). Hinweise (Urlaub, Doppelbelegung) bleiben sichtbar. */
import Button from "sap/m/Button";
import MessageStrip from "sap/m/MessageStrip";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import Link from "sap/m/Link";
import EventBus from "sap/ui/core/EventBus";
import { del, get, problemMessage, put } from "./api";
import { KEEP_OPEN, openForm } from "./forms";
import { assignmentKinds } from "./labels";
import type { AssignmentKind, AssignmentView, CrewResponse, ResourceResponse } from "./models";
import { navTo } from "./navigation";
import { session } from "./session";
import { uuidv7 } from "./ids";
import { openEventDialog } from "./calendarDialog";
import { parseLocal } from "./planningModels";

export interface AssignmentDraft {
	id: string;
	existing: boolean;
	bauakteId: string | null;
	bauakteLabel: string | null;
	kind: AssignmentKind;
	day: string;
	startTime: string | null;
	endTime: string | null;
	activity: string;
	crewId: string | null;
	userIds: string[];
	resourceIds: string[];
	notes: string;
	warnings: string[];
	/** Kolonnenmitglieder, die bei diesem Einsatz nicht dabei sind. */
	excludedUserIds?: string[];
}

export function newDraft(day: string, crewId: string | null, bauakte?: { id: string; label: string }): AssignmentDraft {
	return {
		id: uuidv7(), existing: false, bauakteId: bauakte?.id ?? null, bauakteLabel: bauakte?.label ?? null, kind: "screed", day, startTime: "07:00:00",
		endTime: null, activity: bauakte ? assignmentKinds.screed.activity : "", crewId, userIds: [], resourceIds: [], notes: "", warnings: [],
	};
}

/** Vorhandenen Einsatz bearbeiten; zusätzliche Mitarbeiter = alle außer der Kolonne. */
export function draftOf(a: AssignmentView, crews: CrewResponse[]): AssignmentDraft {
	const crewMembers = new Set(crews.find((c) => c.id === a.crewId)?.memberUserIds ?? []);
	return {
		id: a.id, existing: true, bauakteId: a.bauakteId, bauakteLabel: `${a.bauakteNumber ?? "neu"} · ${a.bauakteTitle}`, kind: a.kind, day: a.day,
		startTime: a.startTime, endTime: a.endTime, activity: a.activity, crewId: a.crewId,
		userIds: a.people.map((p) => p.id).filter((id) => !crewMembers.has(id)), resourceIds: a.resources.map((r) => r.id), notes: a.notes ?? "", warnings: a.warnings,
		excludedUserIds: (a as AssignmentView & { excludedUserIds?: string[] | null }).excludedUserIds ?? [],
	};
}

export const planningChanged = () => EventBus.getInstance().publish("estrichone", "planningChanged");

let lookups: Promise<{ crews: CrewResponse[]; resources: ResourceResponse[] }> | null = null;
export function planningLookups(force = false): Promise<{ crews: CrewResponse[]; resources: ResourceResponse[] }> {
	if (force || !lookups) {
		lookups = Promise.all([get<CrewResponse[]>("planning/crews"), get<ResourceResponse[]>("planning/resources")])
			.then(([crews, resources]) => ({ crews, resources }));
		lookups.catch(() => { lookups = null; });
	}
	return lookups;
}

export async function openAssignment(draft: AssignmentDraft): Promise<void> {
	const canPlan = session.has("planning.manage");
	const { crews, resources } = await planningLookups();
	const people = (await session.people()).filter((p) => p.isActive);
	let current = draft;
	await openForm({
		title: draft.existing ? "Einsatz bearbeiten" : "Einsatz planen",
		width: "44rem",
		readOnly: !canPlan,
		initial: { ...draft },
		fields: [
			{ key: "bauakteId", label: "Bauakte", type: "bauakte", required: true, displayText: draft.bauakteLabel ?? undefined },
			{ key: "kind", label: "Terminart", type: "select", required: true, options: Object.entries(assignmentKinds).map(([key, v]) => ({ key, text: v.text })),
				onChange: (value, form) => {
					const activity = String(form.get("activity") ?? "");
					if (!activity || Object.values(assignmentKinds).some((k) => k.activity === activity)) {
						form.set("activity", assignmentKinds[value as AssignmentKind]?.activity ?? "");
					}
				} },
			{ key: "day", label: "Tag", type: "date", required: true },
			{ key: "startTime", label: "Beginn", type: "time" },
			{ key: "endTime", label: "Ende (optional)", type: "time",
				validate: (v, all) => (v && all.startTime && String(v) <= String(all.startTime) ? "Ende nach Beginn" : null) },
			{ key: "activity", label: "Tätigkeit", required: true, placeholder: "z. B. Estrich einbringen EG", maxLength: 200 },
			{ key: "crewId", label: "Kolonne", type: "select", placeholder: "keine", options: crews.filter((c) => c.isActive).map((c) => ({ key: c.id, text: c.name })) },
			{ key: "userIds", label: "Weitere Mitarbeiter", type: "multi", options: people.map((p) => ({ key: p.id, text: p.displayName })),
				validate: (v, all) => (all.crewId || (v as string[] | null)?.length ? null : "Bitte eine Kolonne oder Mitarbeiter einplanen.") },
			{ key: "resourceIds", label: "Fahrzeuge und Geräte", type: "multi", options: resources.filter((r) => r.isActive).map((r) => ({ key: r.id, text: r.name })) },
			{ key: "notes", label: "Hinweise", type: "textarea", rows: 2, maxLength: 2000 },
		],
		extra: (dialog) => {
			const content = [];
			if (!draft.existing && !draft.bauakteId && canPlan) {
				// C1: Ohne Bauvorhaben wird nicht „irgendwas“ eingeplant, sondern ein Termin im Kalender angelegt.
				content.push(new Link({
					text: "Kein Bauvorhaben? Stattdessen Termin anlegen (z. B. Schulung, Werkstatt, Besprechung)",
					press: () => {
						dialog.close();
						const start = parseLocal(`${draft.day}T${draft.startTime ?? "07:00:00"}`);
						const end = draft.endTime ? parseLocal(`${draft.day}T${draft.endTime}`) : new Date(start.getTime() + 2 * 3600_000);
						void openEventDialog({ defaults: { start, end, crewIds: draft.crewId ? [draft.crewId] : [], participantUserIds: draft.userIds } });
					},
				}));
			}
			if (draft.bauakteId) {
				content.push(new Link({ text: "Bauakte öffnen", press: () => { dialog.close(); navTo(`/bauakten/${draft.bauakteId}?abschnitt=einsaetze`); } }));
			}
			const strip = new MessageStrip({ type: "Warning", showIcon: true, text: `Hinweise: ${draft.warnings.join(" ")}`, visible: draft.warnings.length > 0 });
			strip.data("role", "status", true);
			content.push(strip);
			return content;
		},
		leftButton: draft.existing && canPlan ? (dialog) => new Button({
			text: "Löschen", type: "Reject", icon: "sap-icon://delete", press: () => {
				MessageBox.confirm("Einsatz löschen?", { actions: ["Löschen", MessageBox.Action.CANCEL], emphasizedAction: "Löschen", onClose: async (action: string) => {
					if (action !== "Löschen") return;
					try {
						await del(`planning/assignments/${current.id}`);
						planningChanged();
						dialog.close();
					} catch (error) {
						MessageBox.error(problemMessage(error));
					}
				} });
			},
		}) : undefined,
		submit: async (v, { warn }) => {
			const saved = await put<AssignmentView>(`planning/assignments/${current.id}`, {
				bauakteId: v.bauakteId, kind: v.kind, day: v.day, startTime: v.startTime || null, endTime: v.endTime || null, activity: v.activity,
				crewId: v.crewId || null, userIds: v.userIds ?? [], resourceIds: v.resourceIds ?? [], notes: v.notes || null,
				// Ausnahmen aus „Wer fährt wohin“ bleiben erhalten, solange die Kolonne gleich bleibt.
				excludedUserIds: v.crewId && v.crewId === draft.crewId ? draft.excludedUserIds ?? [] : [],
			});
			planningChanged();
			current = { ...current, existing: true, warnings: saved.warnings };
			if (saved.warnings.length > 0) {
				// Hinweise sichtbar lassen; die Planung entscheidet, ob sie so bleibt.
				warn(saved.warnings);
				MessageToast.show("Gespeichert – bitte die Hinweise prüfen.");
				return KEEP_OPEN;
			}
			MessageToast.show("Einsatz gespeichert");
			return true;
		},
	});
}

