import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Input from "sap/m/Input";
import type Select from "sap/m/Select";
import type DatePicker from "sap/m/DatePicker";
import { patch, post } from "../../../util/api";
import { akte } from "../../../util/akteStore";
import { isoDay } from "../../../util/days";
import { uuidv7 } from "../../../util/ids";
import { openForm } from "../../../util/forms";
import { openLightbox } from "../../../util/lightbox";
import type { FileResponse, WorkItemDto } from "../../../util/models";
import { session } from "../../../util/session";

type Row = WorkItemDto & { overdue: boolean; photoCount: number };

/** Aufgaben und Mängel: erfassen, direkt in der Tabelle bearbeiten, abhaken, Fotos des Mangels. @namespace estrichone.controller.bauakte.sections */
export default class Work extends SectionBase {
	protected watches = ["work", "files"];
	private items: Row[] = [];
	private files: FileResponse[] = [];

	protected init(): void {
		this.v().setModel(new JSONModel({ people: [], shown: [], filter: "open" }), "wk");
	}

	private wk(): JSONModel {
		return this.v().getModel("wk") as JSONModel;
	}

	protected async load(): Promise<void> {
		const people = await session.people();
		this.wk().setProperty("/people", [{ key: "", text: "Niemand" }, ...people.filter((p) => p.isActive).map((p) => ({ key: p.id, text: p.displayName }))]);
		const [items, files] = await Promise.all([akte.work(this.bauakteId), akte.files(this.bauakteId)]);
		const today = isoDay(new Date());
		this.files = files.filter((f) => f.kind === "photo");
		this.items = [...items].sort((a, b) => Number(a.status === "done") - Number(b.status === "done") || (a.dueDate ?? "9999").localeCompare(b.dueDate ?? "9999"))
			.map((w) => ({ ...w, assigneeUserId: w.assigneeUserId ?? "", overdue: w.status !== "done" && !!w.dueDate && w.dueDate < today,
				photoCount: this.files.filter((f) => f.workItemId === w.id).length }));
		this.refresh();
	}

	refresh(): void {
		const filter = this.wk().getProperty("/filter") as string;
		this.wk().setProperty("/shown", this.items.filter((w) => filter === "all" || (filter === "open" && w.status !== "done")
			|| (filter === "defects" && w.kind === "defect") || (filter === "overdue" && w.overdue)));
	}

	private rowOf(event: Event): Row {
		return (event.getSource() as Control).getBindingContext("wk")?.getObject() as Row;
	}

	private async change(row: Row, body: Record<string, unknown>): Promise<void> {
		await this.run(() => patch(`work-items/${row.id}`, body));
		akte.invalidate(this.bauakteId, "work");
	}

	onAddTask(): Promise<void> {
		return this.openCreate("task");
	}

	onAddDefect(): Promise<void> {
		return this.openCreate("defect");
	}

	/** Aufgabe bzw. Mangel im Fenster anlegen (Titel, Beschreibung, Fälligkeit, Zuständig). */
	private async openCreate(kind: "task" | "defect"): Promise<void> {
		const defect = kind === "defect";
		const people = (this.wk().getProperty("/people") as { key: string; text: string }[]) ?? [];
		const saved = await openForm<WorkItemDto>({
			title: defect ? "Mangel erfassen" : "Aufgabe anlegen", submitText: "Anlegen", width: "40rem",
			initial: { title: "", description: "", dueDate: "", assignee: "" },
			fields: [
				{ key: "title", label: defect ? "Mangel" : "Aufgabe", required: true, maxLength: 200,
					placeholder: defect ? "Was ist mangelhaft?" : "Was ist zu tun?" },
				{ key: "description", label: "Beschreibung", type: "textarea", rows: 4, maxLength: 4000,
					placeholder: defect ? "Ort, Umfang, Ursache …" : "Details (optional)" },
				{ key: "dueDate", label: "Fällig am", type: "date" },
				{ key: "assignee", label: "Zuständig", type: "select", options: people.map((p) => ({ key: p.key, text: p.text })) },
			],
			submit: (v) => post<WorkItemDto>("work-items", {
				id: uuidv7(), bauakteId: this.bauakteId, kind, title: String(v.title ?? "").trim(),
				description: String(v.description ?? "").trim() || null, dueDate: v.dueDate || null, assigneeUserId: v.assignee || null,
			}),
		});
		if (saved) {
			this.wk().setProperty("/filter", "open");
			akte.invalidate(this.bauakteId, "work");
			this.toast(defect ? "Mangel erfasst" : "Aufgabe angelegt");
		}
	}

	onToggle(event: Event): void {
		const row = this.rowOf(event);
		void this.change(row, { status: row.status === "done" ? "open" : "done" });
	}

	onTitle(event: Event): void {
		const value = (event.getSource() as Input).getValue().trim();
		const row = this.rowOf(event);
		if (value) void this.change(row, { title: value });
	}

	onDue(event: Event): void {
		const value = (event.getSource() as DatePicker).getValue();
		void this.change(this.rowOf(event), value ? { dueDate: value } : { clearDueDate: true });
	}

	onAssignee(event: Event): void {
		const value = (event.getSource() as Select).getSelectedKey();
		void this.change(this.rowOf(event), value ? { assigneeUserId: value } : { unassign: true });
	}

	onStatus(event: Event): void {
		void this.change(this.rowOf(event), { status: (event.getSource() as Select).getSelectedKey() });
	}

	onPhotos(event: Event): void {
		const row = this.rowOf(event);
		openLightbox(this.files.filter((f) => f.workItemId === row.id), 0, { workName: () => row.title });
	}
}
