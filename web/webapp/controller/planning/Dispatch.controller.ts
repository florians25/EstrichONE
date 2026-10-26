import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type SegmentedButton from "sap/m/SegmentedButton";
import ActionSheet from "sap/m/ActionSheet";
import Button from "sap/m/Button";
import SelectDialog from "sap/m/SelectDialog";
import StandardListItem from "sap/m/StandardListItem";
import MessageToast from "sap/m/MessageToast";
import EventBus from "sap/ui/core/EventBus";
import type { RouteMatch } from "../../util/router";
import { get, post } from "../../util/api";
import { addDays, isoDay, parseDay } from "../../util/days";
import { openForm } from "../../util/forms";
import { planningChanged } from "../../util/assignmentDialog";
import { enablePointerDrag, type DragSource } from "../../util/pointerDrag";
import { parseLocal, type DispatchBoard, type DispatchPoolSite, type DispatchSite } from "../../util/planningModels";
import { download, navQuery } from "../../util/planningModels";

type Target = "person" | "vehicle" | "crew";
const hm = (value: string) => parseLocal(value).toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });

interface LaneSite { bauakteId: string; title: string; crewId: string; crewName: string; activity: string; startTime: string | null; endTime: string | null }
const hhmm = (t: string | null) => (t ? t.slice(0, 5) : "");
const addHours = (t: string | null, h: number): string | null => {
	if (!t) return null;
	const [hh, mm] = t.split(":").map(Number);
	return `${String(Math.min(hh + h, 20)).padStart(2, "0")}:${String(mm).padStart(2, "0")}:00`;
};

/**
 * „Wer fährt wohin“ (C3): Kolonnen des Tages als feste Bahnen, darin die zugeordneten Baustellen. Baustellen aus dem
 * Vorrat per Ziehen oder „Kolonne zuordnen…“ einer Kolonne zuweisen, zwischen Kolonnen verschieben oder entfernen;
 * eine Baustelle darf mehreren Kolonnen gehören, mehrere Baustellen je Kolonne sind ein Hinweis. Einzelne Mitarbeiter
 * und Fahrzeuge nur unter „Details“. Die Zuordnung ändert direkt die Einsätze der Planung. Druck und PDF für den Aushang.
 * @namespace estrichone.controller.planning
 */
export default class Dispatch extends BaseController {
	private board: DispatchBoard | null = null;
	private pool: DispatchPoolSite[] = [];
	private poolTimer: ReturnType<typeof setTimeout> | undefined;

	onInit(): void {
		this.local({ day: isoDay(new Date()), dayKey: "heute", subtitle: "", search: "", busy: false, error: "", board: { canPlan: false },
			sites: [], lanes: [], pool: [], unassigned: 0, details: false, freeText: "", idleText: "", away: [], appointments: [] });
		EventBus.getInstance().subscribe("estrichone", "planningChanged", this.changed, this);
	}

	onExit(): void {
		EventBus.getInstance().unsubscribe("estrichone", "planningChanged", this.changed, this);
	}

	private changed(_channel?: string, _event?: string, data?: { source?: string }): void {
		if (data?.source !== "dispatch") void this.load();
	}

	onAfterRendering(): void {
		const root = this.v().getDomRef() as HTMLElement | null;
		if (root) enablePointerDrag(root, (source, target) => void this.dropped(source, target), () => this.board?.canPlan === true);
	}

	onRouteMatched(match: RouteMatch): void {
		const value = match.query.get("tag") ?? "heute";
		const today = isoDay(new Date());
		const day = value === "morgen" ? addDays(today, 1) : /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : today;
		this.model().setProperty("/day", day);
		this.model().setProperty("/dayKey", day === today ? "heute" : day === addDays(today, 1) ? "morgen" : "datum");
		void this.load();
	}

	private go(day: string): void {
		const today = isoDay(new Date());
		navQuery({ tag: day === today ? null : day === addDays(today, 1) ? "morgen" : day });
	}

	onDayKey(e: Event): void {
		const key = (e.getSource() as SegmentedButton).getSelectedKey();
		const today = isoDay(new Date());
		if (key === "heute") this.go(today);
		else if (key === "morgen") this.go(addDays(today, 1));
		else this.go(addDays(today, 2));
	}

	onPick(e: Event): void {
		const v = String(e.getParameter("value" as never) ?? "");
		if (/^\d{4}-\d{2}-\d{2}$/.test(v)) this.go(v);
	}

	onPrev(): void { this.go(addDays(String(this.model().getProperty("/day")), -1)); }
	onNext(): void { this.go(addDays(String(this.model().getProperty("/day")), 1)); }
	onRefresh(): void { void this.load(); }
	/** Suche: Kolonnen/Baustellen im Plan sofort, Vorrat (alle aktiven Bauakten) über den Server. */
	onSearch(): void {
		this.render();
		clearTimeout(this.poolTimer);
		this.poolTimer = setTimeout(() => void this.loadPool(), 300);
	}

	private async loadPool(): Promise<void> {
		const m = this.model();
		this.pool = await get<DispatchPoolSite[]>(`planning/dispatch/${m.getProperty("/day")}/pool`, { search: String(m.getProperty("/search") ?? "") || undefined })
			.catch(() => this.pool);
		this.render();
	}
	onPrint(): void { window.print(); }

	onPdf(): void {
		download(`/api/v1/planning/dispatch/${this.model().getProperty("/day")}/print.pdf`).catch((error: unknown) => this.showError(error));
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		m.setProperty("/error", "");
		try {
			[this.board, this.pool] = await Promise.all([
				get<DispatchBoard>(`planning/dispatch/${m.getProperty("/day")}`),
				get<DispatchPoolSite[]>(`planning/dispatch/${m.getProperty("/day")}/pool`, { search: String(m.getProperty("/search") ?? "") || undefined }),
			]);
		} catch (error) {
			m.setProperty("/error", (error as Error).message);
		}
		m.setProperty("/busy", false);
		this.render();
	}

	private render(): void {
		const m = this.model();
		const b = this.board;
		if (!b) return;
		const q = String(m.getProperty("/search") ?? "").trim().toLowerCase();
		const match = (...values: (string | null | undefined)[]) => !q || values.some((v) => v?.toLowerCase().includes(q));
		const busyElsewhere = new Map(b.people.map((p) => [p.id, p.siteIds.length]));
		const sites = new Map(b.sites.map((s) => [s.bauakteId, s]));
		m.setProperty("/board", b);
		m.setProperty("/lanes", b.crews.filter((c) => match(c.name, c.leaderName, ...c.siteIds.map((id) => sites.get(id)?.title))).map((c) => ({
			...c,
			info: [c.leaderName ? `Leitung: ${c.leaderName}` : null, `${c.memberCount} Mitarbeiter`].filter(Boolean).join(" · "),
			vehicleText: c.vehicles.map((v) => v.name).join(", "),
			absentText: c.absentMembers.length ? `Fehlt: ${c.absentMembers.map((a) => `${a.name} (${a.note})`).join(", ")}` : "",
			warningText: (c.warnings ?? []).join(" "),
			sites: c.siteIds.map((id) => sites.get(id)).filter((s): s is DispatchSite => !!s).map((s) => {
				const job = s.crews.find((x) => x.id === c.id);
				return { bauakteId: s.bauakteId, number: s.number, title: s.title, address: s.address, crewId: c.id, crewName: c.name,
					startTime: job?.startTime ?? null, endTime: job?.endTime ?? null, activity: job?.activity ?? "",
					timeText: job?.startTime ? `${hhmm(job.startTime)}${job.endTime ? `–${hhmm(job.endTime)}` : ""}` : "–" };
			}),
		})));
		// Vorrat: alle aktiven Bauakten (Server), am Tag eingeteilte zuerst.
		const crewName = new Map(b.crews.map((c) => [c.id, c.name]));
		m.setProperty("/pool", this.pool.map((s) => ({
			...s, crewCount: s.crewIds.length, crewText: s.crewIds.length ? s.crewIds.map((id) => crewName.get(id) ?? "Kolonne").join(", ") : "ohne Kolonne",
		})));
		m.setProperty("/unassigned", b.sites.filter((s) => s.crews.length === 0).length);
		m.setProperty("/sites", b.sites.map((s) => ({
			...s,
			people: s.people.map((p) => ({ ...p, siteId: s.bauakteId, conflict: (busyElsewhere.get(p.id) ?? 0) > 1,
				tooltip: (busyElsewhere.get(p.id) ?? 0) > 1 ? `${p.name} ist an diesem Tag auf mehreren Baustellen.` : p.name })),
			vehicles: s.vehicles.map((v) => ({ ...v, siteId: s.bauakteId })),
		})));
		m.setProperty("/freeText", b.people.filter((p) => p.siteIds.length === 0 && !p.note).map((p) => p.name).join(", "));
		m.setProperty("/idleText", b.vehicles.filter((v) => v.siteIds.length === 0).map((v) => v.name).join(", "));
		m.setProperty("/away", b.people.filter((p) => p.siteIds.length === 0 && p.note).map((p) => ({ name: p.name, note: p.note })));
		m.setProperty("/appointments", b.appointments.map((a) => ({
			title: a.title, description: [a.location, a.people.map((p) => p.name).join(", ")].filter(Boolean).join(" · "),
			time: a.allDay ? "ganztags" : `${hm(a.start)}–${hm(a.end)}`,
		})));
		const day = parseDay(b.day).toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long", year: "numeric" });
		const placed = b.crews.filter((c) => c.siteIds.length > 0).length;
		m.setProperty("/subtitle", `${day} · ${b.sites.length} ${b.sites.length === 1 ? "Baustelle" : "Baustellen"} · ${placed} von ${b.crews.length} Kolonnen eingeteilt`);
	}

	private async move(target: Target, id: string, toBauakteId: string | null, fromBauakteId: string | null = null,
		extra: Record<string, unknown> = {}, quiet = false): Promise<boolean> {
		const m = this.model();
		const updated = await this.run(() => post<DispatchBoard>(`planning/dispatch/${m.getProperty("/day")}/moves`, {
			target, id, toBauakteId, fromBauakteId, ...extra,
		}));
		if (!updated) return false;
		this.board = updated;
		await this.loadPool();
		EventBus.getInstance().publish("estrichone", "planningChanged", { source: "dispatch" });
		if (quiet) return true;
		const site = updated.sites.find((s) => s.bauakteId === (toBauakteId ?? fromBauakteId))?.title ?? "Baustelle";
		const name = target === "crew" ? updated.crews.find((c) => c.id === id)?.name
			: target === "person" ? updated.people.find((p) => p.id === id)?.name : updated.vehicles.find((v) => v.id === id)?.name;
		MessageToast.show(target === "crew"
			? toBauakteId ? `${site} → ${name ?? "Kolonne"}` : `${site} von ${name ?? "Kolonne"} entfernt`
			: toBauakteId ? `${name ?? "Eingeteilt"} → ${site}` : `${name ?? "Einteilung"} herausgenommen`);
		return true;
	}

	/** Baustelle einer Kolonne zuordnen (zusätzlich zu anderen Kolonnen); Tätigkeit und Beginn von einem vorhandenen Einsatz. */
	private assign(crewId: string, bauakteId: string): Promise<boolean> {
		const job = this.board?.sites.find((s) => s.bauakteId === bauakteId)?.jobs[0];
		// Weitere Baustelle der Kolonne: nach dem letzten Einsatz beginnen (Ende des letzten bzw. +4 Stunden).
		const crew = this.board?.crews.find((c) => c.id === crewId);
		const last = crew?.siteIds.map((id) => this.board?.sites.find((s) => s.bauakteId === id)?.crews.find((c) => c.id === crewId)).filter(Boolean).at(-1);
		const after = last ? (last.endTime ?? addHours(last.startTime, 4)) : null;
		return this.move("crew", crewId, bauakteId, null, { keep: true, activity: job?.activity ?? null, startTime: after ?? job?.startTime ?? null });
	}

	/** Baustelle von einer Kolonne zur anderen: dort mit gleicher Tätigkeit und Beginn anlegen, hier entfernen. */
	private async reassign(fromCrewId: string, toCrewId: string, bauakteId: string): Promise<void> {
		const job = this.board?.sites.find((s) => s.bauakteId === bauakteId)?.crews.find((c) => c.id === fromCrewId);
		if (await this.move("crew", toCrewId, bauakteId, null, { keep: true, activity: job?.activity ?? null, startTime: job?.startTime ?? null, endTime: job?.endTime ?? null }, true)) {
			await this.move("crew", fromCrewId, null, bauakteId, {}, true);
			const site = this.board?.sites.find((s) => s.bauakteId === bauakteId)?.title ?? "Baustelle";
			MessageToast.show(`${site} → ${this.board?.crews.find((c) => c.id === toCrewId)?.name ?? "Kolonne"}`);
		}
	}

	private async dropped(source: DragSource, target: string): Promise<void> {
		if (source.kind !== "site") return;
		if (target === "pool") {
			if (source.from) await this.move("crew", source.from, null, source.id);
			return;
		}
		if (target === source.from) return;
		if (source.from) await this.reassign(source.from, target, source.id);
		else await this.assign(target, source.id);
	}

	/** Kolonne wählen (statt Ziehen, z. B. am Tablet). */
	private chooseCrew(title: string, onPick: (id: string) => void, except?: string): void {
		const b = this.board;
		if (!b) return;
		const siteName = (id: string) => b.sites.find((s) => s.bauakteId === id)?.title ?? "";
		const items = b.crews.filter((c) => c.id !== except).map((c) => new StandardListItem({
			title: c.name, description: c.siteIds.length ? `hat ${c.siteIds.map(siteName).join(", ")}` : "noch keine Baustelle",
			info: c.absentMembers.length ? `${c.absentMembers.length} fehlt` : `${c.memberCount} MA`, infoState: c.absentMembers.length ? "Warning" : "None",
			icon: "sap-icon://group",
		}).data("id", c.id));
		const dialog = new SelectDialog({
			title, items, noDataText: "Keine Kolonnen angelegt",
			search: (ev) => {
				const value = String(ev.getParameter("value") ?? "").toLowerCase();
				for (const item of items) item.setVisible(!value || `${item.getTitle()} ${item.getDescription()}`.toLowerCase().includes(value));
			},
			confirm: (ev) => {
				const item = ev.getParameter("selectedItem") as StandardListItem | null;
				if (item) onPick(String(item.data("id")));
			},
		});
		dialog.attachEventOnce("confirm", () => setTimeout(() => dialog.destroy(), 0));
		dialog.attachEventOnce("cancel", () => setTimeout(() => dialog.destroy(), 0));
		dialog.open("");
	}

	/** Vorrat: „Kolonne zuordnen…“. */
	onAssignSite(e: Event): void {
		const s = (e.getSource() as Control).getBindingContext("view")?.getObject() as DispatchSite;
		this.chooseCrew(`Kolonne zuordnen: ${s.title}`, (crewId) => void this.assign(crewId, s.bauakteId));
	}

	/** Baustelle in einer Kolonne: verschieben, weitere Kolonne, Tätigkeit/Beginn, entfernen. */
	onSiteMenu(e: Event): void {
		if (this.board?.canPlan !== true) return;
		const source = e.getSource() as Button;
		const item = source.getBindingContext("view")?.getObject() as LaneSite;
		const sheet = new ActionSheet({
			title: item.title, placement: "Bottom", showCancelButton: true,
			buttons: [
				new Button({ text: "Andere Kolonne", icon: "sap-icon://journey-change",
					press: () => this.chooseCrew(`${item.title}: andere Kolonne`, (crewId) => void this.reassign(item.crewId, crewId, item.bauakteId), item.crewId) }),
				new Button({ text: "Weitere Kolonne zuordnen", icon: "sap-icon://add",
					press: () => this.chooseCrew(`${item.title}: weitere Kolonne`, (crewId) => void this.assign(crewId, item.bauakteId), item.crewId) }),
				new Button({ text: "Tätigkeit und Uhrzeit", icon: "sap-icon://edit", press: () => void this.editJob(item) }),
				new Button({ text: "Von Kolonne entfernen", icon: "sap-icon://decline", type: "Reject", press: () => void this.move("crew", item.crewId, null, item.bauakteId) }),
			],
			afterClose: () => setTimeout(() => sheet.destroy(), 0),
		});
		sheet.openBy(source);
	}

	private async editJob(item: LaneSite): Promise<void> {
		const result = await openForm<DispatchBoard>({
			title: `${item.crewName}: Tätigkeit und Uhrzeit`, width: "30rem",
			initial: { activity: item.activity, startTime: item.startTime, endTime: item.endTime },
			fields: [
				{ key: "activity", label: "Tätigkeit", required: true, maxLength: 200 },
				{ key: "startTime", label: "Beginn vor Ort", type: "time" },
				{ key: "endTime", label: "Ende (optional)", type: "time",
					validate: (v, all) => (v && all.startTime && String(v) <= String(all.startTime) ? "Das Ende muss nach dem Beginn liegen." : null) },
			],
			submit: (v) => post<DispatchBoard>(`planning/dispatch/${this.model().getProperty("/day")}/moves`, {
				target: "crew", id: item.crewId, toBauakteId: item.bauakteId, keep: true, activity: v.activity, startTime: v.startTime || null, endTime: v.endTime || null,
			}),
		});
		if (result) {
			this.board = result;
			await this.loadPool();
			planningChanged();
		}
	}

	private site(e: Event): DispatchSite & { bauakteId: string } {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as DispatchSite;
	}

	/** Details: Mitarbeiter bzw. Fahrzeug für die Baustelle wählen. */
	private choose(target: "person" | "vehicle", title: string, onPick: (id: string) => void): void {
		const b = this.board;
		if (!b) return;
		const siteName = (id: string) => b.sites.find((s) => s.bauakteId === id)?.title ?? "";
		const items = target === "person"
			? b.people.map((p) => new StandardListItem({
				title: p.name, description: p.note ?? (p.siteIds.length ? `auf ${p.siteIds.map(siteName).join(", ")}` : "frei"),
				info: p.crewName ?? "", infoState: p.note ? "Warning" : p.siteIds.length ? "Information" : "Success", icon: "sap-icon://employee",
			}).data("id", p.id))
			: b.vehicles.map((v) => new StandardListItem({
				title: v.name, description: v.siteIds.length ? `auf ${v.siteIds.map(siteName).join(", ")}` : "frei", info: v.identifier ?? "",
				icon: v.kind === "vehicle" ? "sap-icon://shipping-status" : "sap-icon://wrench",
			}).data("id", v.id));
		const dialog = new SelectDialog({
			title, items, noDataText: "Keine Einträge",
			search: (ev) => {
				const value = String(ev.getParameter("value") ?? "").toLowerCase();
				for (const item of items) item.setVisible(!value || `${item.getTitle()} ${item.getDescription()} ${item.getInfo()}`.toLowerCase().includes(value));
			},
			confirm: (ev) => {
				const item = ev.getParameter("selectedItem") as StandardListItem | null;
				if (item) onPick(String(item.data("id")));
			},
		});
		dialog.attachEventOnce("confirm", () => setTimeout(() => dialog.destroy(), 0));
		dialog.attachEventOnce("cancel", () => setTimeout(() => dialog.destroy(), 0));
		dialog.open("");
	}

	onAddPerson(e: Event): void {
		const s = this.site(e);
		this.choose("person", `Mitarbeiter für ${s.title}`, (id) => void this.move("person", id, s.bauakteId));
	}

	onAddVehicle(e: Event): void {
		const s = this.site(e);
		this.choose("vehicle", `Fahrzeug für ${s.title}`, (id) => void this.move("vehicle", id, s.bauakteId));
	}

	private pickSite(title: string, onPick: (bauakteId: string) => void): void {
		const b = this.board;
		if (!b) return;
		const items = b.sites.map((s) => new StandardListItem({ title: s.title, description: s.address ?? "", info: s.startTime?.slice(0, 5) ?? "", icon: "sap-icon://building" })
			.data("id", s.bauakteId));
		const dialog = new SelectDialog({
			title, items,
			confirm: (ev) => {
				const item = ev.getParameter("selectedItem") as StandardListItem | null;
				if (item) onPick(String(item.data("id")));
			},
		});
		dialog.attachEventOnce("confirm", () => setTimeout(() => dialog.destroy(), 0));
		dialog.attachEventOnce("cancel", () => setTimeout(() => dialog.destroy(), 0));
		dialog.open("");
	}

	/** Details: Klick auf Person/Fahrzeug einer Baustelle – umsetzen, zusätzlich, herausnehmen. */
	onPersonMenu(e: Event): void { this.chipMenu(e, "person"); }
	onVehicleMenu(e: Event): void { this.chipMenu(e, "vehicle"); }

	private chipMenu(e: Event, target: "person" | "vehicle"): void {
		if (this.board?.canPlan !== true) return;
		const source = e.getSource() as Button;
		const item = source.getBindingContext("view")?.getObject() as { id: string; name: string; siteId: string };
		const sheet = new ActionSheet({
			title: item.name, placement: "Bottom", showCancelButton: true,
			buttons: [
				new Button({ text: "Auf andere Baustelle", icon: "sap-icon://journey-change", press: () => this.pickSite(`${item.name} umsetzen`, (site) => void this.move(target, item.id, site, item.siteId)) }),
				new Button({ text: "Zusätzlich auf weitere Baustelle", icon: "sap-icon://add", press: () => this.pickSite(`${item.name} zusätzlich`, (site) => void this.move(target, item.id, site, null, { keep: true })) }),
				new Button({ text: "Herausnehmen", icon: "sap-icon://decline", type: "Reject", press: () => void this.move(target, item.id, null, item.siteId) }),
			],
			afterClose: () => setTimeout(() => sheet.destroy(), 0),
		});
		sheet.openBy(source);
	}

	/** Weitere Baustelle (auch ohne Planung an dem Tag): Bauakte und Kolonne wählen – legt den Einsatz der Kolonne an. */
	async onAddSite(): Promise<void> {
		const b = this.board;
		if (!b) return;
		const options = b.crews.map((c) => ({ key: c.id, text: c.name, additionalText: c.siteIds.length ? `${c.siteIds.length} Baustelle(n)` : "frei" }));
		const result = await openForm<DispatchBoard>({
			title: "Baustelle zuordnen", width: "36rem",
			intro: "Nur vorhandene Bauvorhaben. Der Einsatz der Kolonne wird in der Planung angelegt und kann dort weiter bearbeitet werden.",
			fields: [
				{ key: "bauakteId", label: "Bauakte", type: "bauakte", required: true },
				{ key: "crewId", label: "Kolonne", type: "select", required: true, options },
				{ key: "activity", label: "Tätigkeit", placeholder: "Estrich einbringen", maxLength: 200 },
				{ key: "startTime", label: "Beginn vor Ort", type: "time" },
			],
			initial: { startTime: "07:00:00" },
			submit: (v) => post<DispatchBoard>(`planning/dispatch/${this.model().getProperty("/day")}/moves`, {
				target: "crew", id: v.crewId, toBauakteId: v.bauakteId, keep: true, activity: v.activity || null, startTime: v.startTime || null,
			}),
		});
		if (result) {
			this.board = result;
			await this.loadPool();
			MessageToast.show("Baustelle zugeordnet");
			planningChanged();
		}
	}
}
