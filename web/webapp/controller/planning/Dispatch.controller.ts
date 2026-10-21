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
import { assignmentKinds } from "../../util/labels";
import { openForm } from "../../util/forms";
import { planningChanged } from "../../util/assignmentDialog";
import { enablePointerDrag, type DragSource } from "../../util/pointerDrag";
import { parseLocal, type DispatchBoard, type DispatchSite } from "../../util/planningModels";
import { download, navQuery } from "../../util/planningModels";

type Target = "person" | "vehicle" | "crew";
const hm = (value: string) => parseLocal(value).toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });

/**
 * „Wer fährt wohin“ (C3): Schnelleinsatzplanung für heute/morgen – KOLONNEN per Ziehen oder Auswahl auf Baustellen
 * verteilen (mehrere Kolonnen je Baustelle, Hinweis bei Kolonne doppelt am Tag). Einzelne Mitarbeiter und Fahrzeuge
 * nur im Bereich „Details“. Die Einteilung ändert direkt die Einsätze der Planung. Druck und PDF für den Aushang.
 * @namespace estrichone.controller.planning
 */
export default class Dispatch extends BaseController {
	private board: DispatchBoard | null = null;

	onInit(): void {
		this.local({ day: isoDay(new Date()), dayKey: "heute", subtitle: "", search: "", busy: false, error: "", board: { canPlan: false },
			sites: [], crews: [], freeCrews: 0, details: false, free: [], idleVehicles: [], away: [], appointments: [] });
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
	onSearch(): void { this.render(); }
	onPrint(): void { window.print(); }

	onPdf(): void {
		download(`/api/v1/planning/dispatch/${this.model().getProperty("/day")}/print.pdf`).catch((error: unknown) => this.showError(error));
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		m.setProperty("/error", "");
		try {
			this.board = await get<DispatchBoard>(`planning/dispatch/${m.getProperty("/day")}`);
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
		const crewSites = new Map(b.crews.map((c) => [c.id, c.siteIds.length]));
		const siteName = (id: string) => b.sites.find((s) => s.bauakteId === id)?.title ?? "";
		m.setProperty("/board", b);
		m.setProperty("/sites", b.sites
			.filter((s) => match(s.title, s.number, s.address, ...s.crews.map((c) => c.name), ...s.people.map((p) => p.name)))
			.map((s) => ({
				...s,
				crews: s.crews.map((c) => {
					const twice = (crewSites.get(c.id) ?? 0) > 1;
					return { ...c, siteId: s.bauakteId, twice,
						label: `${c.startTime ? `${c.startTime.slice(0, 5)} ` : ""}${c.name} · ${c.activity}`,
						tooltip: twice ? `${c.name} ist an diesem Tag auf mehreren Baustellen.` : `${c.name}: ${c.activity}` };
				}),
				detailText: `Details: Team ${s.people.length} · Fahrzeuge ${s.vehicles.length}`,
				soloJobs: s.jobs.filter((j) => !j.crewId).map((j) => ({
					color: j.crewColor ?? assignmentKinds[j.kind]?.color ?? "",
					label: `${j.startTime ? `${j.startTime.slice(0, 5)}${j.endTime ? `–${j.endTime.slice(0, 5)}` : ""} ` : ""}${j.activity}${j.crewName ? ` (${j.crewName})` : ""}`,
				})),
				people: s.people.map((p) => ({ ...p, siteId: s.bauakteId, conflict: (busyElsewhere.get(p.id) ?? 0) > 1,
					tooltip: (busyElsewhere.get(p.id) ?? 0) > 1 ? `${p.name} ist an diesem Tag auf mehreren Baustellen.` : p.name })),
				vehicles: s.vehicles.map((v) => ({ ...v, siteId: s.bauakteId })),
			})));
		m.setProperty("/crews", b.crews.filter((c) => match(c.name, c.leaderName)).map((c) => ({
			...c,
			info: [c.leaderName ? `Leitung: ${c.leaderName}` : null, `${c.memberCount} ${c.memberCount === 1 ? "Mitarbeiter" : "Mitarbeiter"}`].filter(Boolean).join(" · "),
			vehicleText: c.vehicles.map((v) => v.name).join(", "),
			placeText: c.siteIds.length ? `auf ${c.siteIds.map(siteName).join(", ")}` : "frei",
			placeState: c.siteIds.length > 1 ? "Warning" : c.siteIds.length ? "Information" : "Success",
			absentText: c.absentMembers.length ? `Fehlt: ${c.absentMembers.map((a) => `${a.name} (${a.note})`).join(", ")}` : "",
		})));
		m.setProperty("/freeCrews", b.crews.filter((c) => c.siteIds.length === 0).length);
		m.setProperty("/free", b.people.filter((p) => p.siteIds.length === 0 && !p.note && match(p.name, p.crewName)));
		m.setProperty("/away", b.people.filter((p) => p.siteIds.length === 0 && p.note).map((p) => ({ name: p.name, note: p.note })));
		m.setProperty("/idleVehicles", b.vehicles.filter((v) => v.siteIds.length === 0 && match(v.name, v.identifier)));
		m.setProperty("/appointments", b.appointments.map((a) => ({
			title: a.title, description: [a.location, a.people.map((p) => p.name).join(", ")].filter(Boolean).join(" · "),
			time: a.allDay ? "ganztags" : `${hm(a.start)}–${hm(a.end)}`,
		})));
		const day = parseDay(b.day).toLocaleDateString("de-DE", { weekday: "long", day: "2-digit", month: "long", year: "numeric" });
		const placed = b.crews.filter((c) => c.siteIds.length > 0).length;
		m.setProperty("/subtitle", `${day} · ${b.sites.length} ${b.sites.length === 1 ? "Baustelle" : "Baustellen"} · ${placed} von ${b.crews.length} Kolonnen eingeteilt`);
	}

	private async move(target: Target, id: string, toBauakteId: string | null, fromBauakteId: string | null = null, extra: Record<string, unknown> = {}): Promise<void> {
		const m = this.model();
		const updated = await this.run(() => post<DispatchBoard>(`planning/dispatch/${m.getProperty("/day")}/moves`, {
			target, id, toBauakteId, fromBauakteId, ...extra,
		}));
		if (!updated) return;
		this.board = updated;
		this.render();
		const site = updated.sites.find((s) => s.bauakteId === toBauakteId);
		const name = target === "crew" ? updated.crews.find((c) => c.id === id)?.name
			: target === "person" ? updated.people.find((p) => p.id === id)?.name : updated.vehicles.find((v) => v.id === id)?.name;
		MessageToast.show(toBauakteId ? `${name ?? "Eingeteilt"} → ${site?.title ?? "Baustelle"}` : `${name ?? "Einteilung"} herausgenommen`);
		EventBus.getInstance().publish("estrichone", "planningChanged", { source: "dispatch" });
	}

	private async dropped(source: DragSource, target: string): Promise<void> {
		if (source.kind !== "person" && source.kind !== "vehicle" && source.kind !== "crew") return;
		if (target === "free") {
			if (source.from) await this.move(source.kind, source.id, null, source.from);
			return;
		}
		if (target === source.from) return;
		await this.move(source.kind, source.id, target, source.from);
	}

	/** Kolonne wählen (statt Ziehen, z. B. am Tablet). */
	private chooseCrew(title: string, onPick: (id: string) => void): void {
		const b = this.board;
		if (!b) return;
		const siteName = (id: string) => b.sites.find((s) => s.bauakteId === id)?.title ?? "";
		const items = b.crews.map((c) => new StandardListItem({
			title: c.name, description: c.siteIds.length ? `auf ${c.siteIds.map(siteName).join(", ")}` : "frei",
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

	onAddCrew(e: Event): void {
		const s = this.site(e);
		// Zusätzlich: Eine Kolonne, die schon woanders ist, wird hier dazu genommen – umsetzen über das Kolonnen-Menü.
		this.chooseCrew(`Kolonne für ${s.title}`, (id) => {
			const placed = (this.board?.crews.find((c) => c.id === id)?.siteIds.length ?? 0) > 0;
			void this.move("crew", id, s.bauakteId, null, placed ? { keep: true } : {});
		});
	}

	onAssignCrew(e: Event): void {
		const c = (e.getSource() as Control).getBindingContext("view")?.getObject() as { id: string; name: string };
		this.pickSite(`${c.name} einteilen`, (site) => void this.move("crew", c.id, site));
	}

	onCrewMenu(e: Event): void {
		if (this.board?.canPlan !== true) return;
		const source = e.getSource() as Button;
		const item = source.getBindingContext("view")?.getObject() as { id: string; name: string; siteId: string; activity: string; startTime: string | null };
		const sheet = new ActionSheet({
			title: item.name, placement: "Bottom", showCancelButton: true,
			buttons: [
				new Button({ text: "Auf andere Baustelle", icon: "sap-icon://journey-change", press: () => this.pickSite(`${item.name} umsetzen`, (site) => void this.move("crew", item.id, site, item.siteId)) }),
				new Button({ text: "Zusätzlich auf weitere Baustelle", icon: "sap-icon://add", press: () => this.pickSite(`${item.name} zusätzlich`, (site) => void this.move("crew", item.id, site, null, { keep: true })) }),
				new Button({ text: "Tätigkeit und Beginn", icon: "sap-icon://edit", press: () => void this.editCrewJob(item) }),
				new Button({ text: "Herausnehmen", icon: "sap-icon://decline", type: "Reject", press: () => void this.move("crew", item.id, null, item.siteId) }),
			],
			afterClose: () => setTimeout(() => sheet.destroy(), 0),
		});
		sheet.openBy(source);
	}

	private async editCrewJob(item: { id: string; name: string; siteId: string; activity: string; startTime: string | null }): Promise<void> {
		const result = await openForm<DispatchBoard>({
			title: `${item.name}: Tätigkeit und Beginn`, width: "30rem",
			initial: { activity: item.activity, startTime: item.startTime },
			fields: [
				{ key: "activity", label: "Tätigkeit", required: true, maxLength: 200 },
				{ key: "startTime", label: "Beginn vor Ort", type: "time" },
			],
			submit: (v) => post<DispatchBoard>(`planning/dispatch/${this.model().getProperty("/day")}/moves`, {
				target: "crew", id: item.id, toBauakteId: item.siteId, keep: true, activity: v.activity, startTime: v.startTime || null,
			}),
		});
		if (result) {
			this.board = result;
			this.render();
			planningChanged();
		}
	}

	private site(e: Event): DispatchSite & { bauakteId: string } {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as DispatchSite;
	}

	/** Auswahl statt Ziehen (z. B. am Tablet): Mitarbeiter bzw. Fahrzeug für die Baustelle wählen. */
	private choose(target: Target, title: string, onPick: (id: string) => void): void {
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
			cancel: () => undefined,
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
		if (!b || b.sites.length === 0) {
			this.onAddSite();
			return;
		}
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

	onAssignPerson(e: Event): void {
		const p = (e.getSource() as Control).getBindingContext("view")?.getObject() as { id: string; name: string };
		this.pickSite(`${p.name} einteilen`, (site) => void this.move("person", p.id, site));
	}

	onAssignVehicle(e: Event): void {
		const v = (e.getSource() as Control).getBindingContext("view")?.getObject() as { id: string; name: string };
		this.pickSite(`${v.name} einteilen`, (site) => void this.move("vehicle", v.id, site));
	}

	/** Klick auf eine Person der Baustelle: umsetzen oder herausnehmen. */
	onPersonMenu(e: Event): void { this.chipMenu(e, "person"); }
	onVehicleMenu(e: Event): void { this.chipMenu(e, "vehicle"); }

	private chipMenu(e: Event, target: Target): void {
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

	/** Neue Baustelle für den Tag: Bauakte und Kolonne wählen (legt den Einsatz der Kolonne an). */
	async onAddSite(): Promise<void> {
		const b = this.board;
		if (!b) return;
		const options = b.crews.map((c) => ({ key: c.id, text: c.name, additionalText: c.siteIds.length ? "eingeteilt" : "frei" }));
		const result = await openForm<DispatchBoard>({
			title: "Baustelle einteilen", width: "36rem",
			intro: "Nur vorhandene Bauvorhaben. Der Einsatz der Kolonne wird in der Planung angelegt und kann dort weiter bearbeitet werden.",
			fields: [
				{ key: "bauakteId", label: "Bauakte", type: "bauakte", required: true },
				{ key: "crewId", label: "Kolonne", type: "select", required: true, options },
				{ key: "activity", label: "Tätigkeit", placeholder: "Estrich einbringen", maxLength: 200 },
				{ key: "startTime", label: "Beginn vor Ort", type: "time" },
			],
			initial: { startTime: "07:00:00" },
			submit: (v) => post<DispatchBoard>(`planning/dispatch/${this.model().getProperty("/day")}/moves`, {
				target: "crew", id: v.crewId, toBauakteId: v.bauakteId, keep: (b.crews.find((c) => c.id === v.crewId)?.siteIds.length ?? 0) > 0,
				activity: v.activity || null, startTime: v.startTime || null,
			}),
		});
		if (result) {
			this.board = result;
			this.render();
			MessageToast.show("Baustelle eingeteilt");
			planningChanged();
		}
	}
}
