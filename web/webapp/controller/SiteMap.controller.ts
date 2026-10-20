import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get } from "../util/api";
import { bauakteStatus, statusMarkerColors } from "../util/labels";
import type { BauakteMapPoint, BauakteStatus } from "../util/models";
import { session } from "../util/session";

const active: BauakteStatus[] = ["inquiry", "planned", "inProgress", "drying"];

/** Baustellenkarte (FixFlex: Filter oben fest, Karte füllt den Rest), Liste daneben. @namespace estrichone.controller */
export default class SiteMap extends BaseController {
	private points: BauakteMapPoint[] = [];

	onInit(): void {
		this.local({ all: false, search: "", markers: [], shown: [], subtitle: "", statusOptions: this.options(false) });
	}

	private options(all: boolean) {
		return (all ? [...active, "completed", "cancelled"] as BauakteStatus[] : active).map((s) => ({ key: s, text: bauakteStatus[s].text, selected: s !== "cancelled" }));
	}

	async onRouteMatched(): Promise<void> {
		await this.load();
	}

	private async load(): Promise<void> {
		await session.people();
		this.points = (await this.run(() => get<BauakteMapPoint[]>("bauakten/map", { all: this.model().getProperty("/all") }))) ?? [];
		this.refresh();
	}

	onAll(): void {
		this.model().setProperty("/statusOptions", this.options(this.model().getProperty("/all")));
		void this.load();
	}

	refresh(): void {
		const m = this.model();
		const statuses = (m.getProperty("/statusOptions") as { key: BauakteStatus; selected: boolean }[]).filter((o) => o.selected).map((o) => o.key);
		const q = String(m.getProperty("/search") ?? "").trim().toLowerCase();
		const shown = this.points.filter((p) => statuses.includes(p.status) && (!q || [p.number, p.title, p.street, p.city].some((v) => v?.toLowerCase().includes(q))));
		m.setProperty("/shown", shown);
		m.setProperty("/subtitle", `${shown.length} von ${this.points.length} Baustellen mit Standort`);
		m.setProperty("/markers", shown.map((p) => ({
			id: p.id, latitude: p.latitude, longitude: p.longitude, color: statusMarkerColors[p.status], label: `${p.number ?? "neu"} · ${p.title}`,
			popup: [
				{ text: p.number ?? "noch ohne Nummer" }, { text: p.title, href: `/bauakten/${p.id}`, strong: true },
				{ text: [p.street, p.city].filter(Boolean).join(", ") || "–" }, { text: bauakteStatus[p.status].text },
				...(p.responsibleUserId ? [{ text: `Zuständig: ${session.name(p.responsibleUserId)}` }] : []),
			],
		})));
	}

	onOpen(event: Event): void {
		const p = (event.getSource() as Control).getBindingContext("view")?.getObject() as BauakteMapPoint;
		this.nav(`/bauakten/${p.id}`);
	}

	onMarker(): void {
		// Popup mit Link öffnet Leaflet selbst.
	}

	onList(): void {
		this.nav("/bauakten");
	}
}
