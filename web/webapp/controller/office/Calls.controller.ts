import BaseController from "../BaseController";
import EventBus from "sap/ui/core/EventBus";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, post } from "../../util/api";
import { callForm, callTime, caller } from "../../util/callForm";
import { isoDay } from "../../util/days";
import { date } from "../../util/format";
import type { CallResponse } from "../../util/models";
import { openQuickCall } from "../../util/quickCall";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";

/** Anrufe: Gesprächsnotizen mit Wiedervorlage (wie „Anrufe“ in Estrich Web). @namespace estrichone.controller.office */
export default class Calls extends BaseController {
	onInit(): void {
		this.local({ items: [], search: "", onlyDue: false, busy: false });
		EventBus.getInstance().subscribe("estrichone", "callsChanged", () => void this.load());
	}

	onRouteMatched(): void {
		void this.load();
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const today = isoDay(new Date());
			const calls = await get<CallResponse[]>("calls", { kind: "call", search: m.getProperty("/search") || undefined, due: m.getProperty("/onlyDue") || undefined });
			m.setProperty("/items", calls.map((c) => ({ ...c, who: caller(c), time: callTime(c.calledAt), overdue: !!c.dueDate && c.dueDate < today,
				followText: `Wiedervorlage${c.dueDate ? ` ${date(c.dueDate)}` : ""}` })));
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private obj(event: Event): CallResponse {
		return (event.getSource() as Control).getBindingContext("view")?.getObject() as CallResponse;
	}

	onSearch(): void { void this.load(); }
	onQuick(): void { openQuickCall(); }
	onPrint(): void { window.print(); }

	async onNew(): Promise<void> {
		if (await callForm("call", null)) { this.toast("Anruf gespeichert"); void this.load(); }
	}

	async onEdit(event: Event): Promise<void> {
		if (await callForm("call", this.obj(event))) { this.toast("Anruf gespeichert"); void this.load(); }
	}

	async onDone(event: Event): Promise<void> {
		await this.run(() => post(`calls/${this.obj(event).id}/follow-up-done`), "Wiedervorlage erledigt");
		void this.load();
	}

	async onDelete(event: Event): Promise<void> {
		const c = this.obj(event);
		if (await this.confirm("Anruf löschen?", { action: "Löschen", danger: true })) {
			await this.run(() => del(`calls/${c.id}`));
			void this.load();
		}
	}

	/** Telefonnotiz → Aufgabe in der Bauakte (B10): Titel aus Betreff/Anrufer, Fälligkeit aus der Wiedervorlage. */
	async onToTask(event: Event): Promise<void> {
		const c = this.obj(event);
		const saved = await openForm<unknown>({
			title: "Aufgabe aus Anruf", width: "32rem",
			initial: {
				bauakteId: c.bauakteId, title: (c.subject ?? `Rückruf ${caller(c)}`).slice(0, 200),
				dueDate: (c as CallResponse & { dueDate?: string | null }).dueDate ?? null,
			},
			fields: [
				{ key: "bauakteId", label: "Bauakte", type: "bauakte", required: true },
				{ key: "title", label: "Aufgabe", required: true, maxLength: 200 },
				{ key: "kind", label: "Art", type: "segmented", options: [{ key: "task", text: "Aufgabe" }, { key: "defect", text: "Mangel" }] },
				{ key: "dueDate", label: "Fällig am", type: "date" },
			],
			submit: (v) => post("work-items", {
				id: uuidv7(), bauakteId: v.bauakteId, kind: v.kind || "task", title: String(v.title).trim(), dueDate: v.dueDate || null, assigneeUserId: null,
			}),
		});
		if (saved !== null) this.toast("Aufgabe in der Bauakte angelegt");
	}
}
