import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type MenuItem from "sap/m/MenuItem";
import { del, get, put } from "../../../util/api";
import { akte } from "../../../util/akteStore";
import { openForm, prompt } from "../../../util/forms";
import { uuidv7 } from "../../../util/ids";
import { roomStatus } from "../../../util/labels";
import type { RoomOverview, RoomResponse, ScreedTypeResponse } from "../../../util/models";

const num = (v: number) => v.toLocaleString("de-DE", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** Räume und Flächen mit Estrichaufbau, nach Bereichen gruppiert, mit Summen und Volumen. @namespace estrichone.controller.bauakte.sections */
export default class Rooms extends SectionBase {
	protected watches = ["rooms"];
	private overview: RoomOverview | null = null;
	private types: ScreedTypeResponse[] = [];

	protected init(): void {
		this.v().setModel(new JSONModel({ rooms: [], sectionMenu: [] }), "rm");
	}

	private rm(): JSONModel {
		return this.v().getModel("rm") as JSONModel;
	}

	protected async load(): Promise<void> {
		const [overview, types] = await Promise.all([akte.rooms(this.bauakteId),
			this.types.length ? Promise.resolve(this.types) : get<ScreedTypeResponse[]>("screed-types")]);
		this.types = types;
		this.apply(overview);
	}

	private apply(o: RoomOverview): void {
		this.overview = o;
		const m = this.rm();
		const sectionName = (id: string | null) => o.sections.find((s) => s.id === id)?.name ?? (o.sections.length ? "Ohne Bereich" : "Räume");
		m.setProperty("/rooms", o.rooms.map((r) => ({ ...r, sectionName: sectionName(r.sectionId) })));
		m.setProperty("/total", num(o.totalAreaSquareMeters));
		m.setProperty("/heated", num(o.heatedAreaSquareMeters));
		m.setProperty("/count", String(o.rooms.length));
		const ready = o.rooms.filter((r) => r.status === "ready").length;
		const laid = o.rooms.filter((r) => r.status === "laid").length;
		m.setProperty("/progress", `${laid} eingebracht · ${ready} belegreif`);
		const volume = o.rooms.reduce((sum, r) => sum + ((r.areaSquareMeters ?? 0) * (r.thicknessMillimeters ?? 0)) / 1000, 0);
		m.setProperty("/volume", volume.toLocaleString("de-DE", { maximumFractionDigits: 2 }));
		m.setProperty("/sectionMenu", [
			{ key: "__new", text: "Bereich hinzufügen", icon: "sap-icon://add" },
			...o.sections.flatMap((s) => [
				{ key: `rename:${s.id}`, text: `„${s.name}“ umbenennen`, icon: "sap-icon://edit" },
				{ key: `delete:${s.id}`, text: `„${s.name}“ entfernen`, icon: "sap-icon://delete" },
			]),
		]);
	}

	private async save(request: Promise<RoomOverview>): Promise<void> {
		this.apply(await request);
		akte.invalidate(this.bauakteId, "rooms");
	}

	async onSectionMenu(event: Event): Promise<void> {
		const key = ((event as unknown as { getParameter(n: string): MenuItem }).getParameter("item")).getKey();
		if (key === "__new" || key.startsWith("rename:")) {
			const id = key === "__new" ? uuidv7() : key.slice(7);
			const current = this.overview?.sections.find((s) => s.id === id)?.name ?? "";
			const name = await prompt(key === "__new" ? "Bereich hinzufügen" : "Bereich umbenennen", "Name des Bereichs", { initial: current,
				intro: "Bereiche gliedern die Räume, z. B. Haus A, OG oder Wohnung 3." });
			if (name) await this.run(() => this.save(put<RoomOverview>(`bauakten/${this.bauakteId}/sections/${id}`, { id, name })));
		} else if (key.startsWith("delete:")) {
			if (await this.confirm("Bereich entfernen? Die Räume bleiben erhalten.", { action: "Entfernen", danger: true })) {
				await this.run(() => this.save(del<RoomOverview>(`bauakten/${this.bauakteId}/sections/${key.slice(7)}`)));
			}
		}
	}

	private async form(room: RoomResponse | null): Promise<void> {
		const o = this.overview;
		await openForm({
			title: room ? "Raum bearbeiten" : "Raum hinzufügen", width: "38rem",
			initial: room ? { ...room } : { status: "planned", underfloorHeating: false },
			fields: [
				{ key: "name", label: "Raum", required: true, maxLength: 100 },
				{ key: "floor", label: "Geschoss", placeholder: "EG", maxLength: 50 },
				...(o?.sections.length ? [{ key: "sectionId", label: "Bereich", type: "select" as const, options: o.sections.map((s) => ({ key: s.id, text: s.name })) }] : []),
				{ key: "areaSquareMeters", label: "Fläche (m²)", type: "number", unit: "m²", min: 0, max: 100000 },
				{ key: "status", label: "Status", type: "select", required: true, options: Object.entries(roomStatus).map(([key, v]) => ({ key, text: v.text })) },
				{ key: "screedTypeId", label: "Estrichart", type: "select", placeholder: "noch offen",
					options: this.types.filter((t) => t.isActive).map((t) => ({ key: t.id, text: t.designation ? `${t.name} (${t.designation})` : t.name })) },
				{ key: "thicknessMillimeters", label: "Dicke (mm)", type: "integer", unit: "mm", min: 10, max: 300 },
				{ key: "insulationMillimeters", label: "Dämmung (mm)", type: "integer", unit: "mm", min: 0, max: 500 },
				{ key: "underfloorHeating", label: "Fußbodenheizung", type: "switch" },
				{ key: "notes", label: "Hinweise", type: "textarea", rows: 2, maxLength: 2000 },
			],
			submit: async (v) => {
				const id = room?.id ?? uuidv7();
				await this.save(put<RoomOverview>(`bauakten/${this.bauakteId}/rooms/${id}`, {
					id, sectionId: v.sectionId || null, name: v.name, floor: v.floor || null, areaSquareMeters: v.areaSquareMeters ?? null,
					screedTypeId: v.screedTypeId || null, thicknessMillimeters: v.thicknessMillimeters ?? null, insulationMillimeters: v.insulationMillimeters ?? null,
					underfloorHeating: !!v.underfloorHeating, status: v.status, notes: v.notes || null,
				}));
			},
		});
	}

	onAdd(): void {
		void this.form(null);
	}

	onEdit(event: Event): void {
		void this.form((event.getSource() as Control).getBindingContext("rm")?.getObject() as RoomResponse);
	}

	async onDelete(event: Event): Promise<void> {
		const room = (event.getSource() as Control).getBindingContext("rm")?.getObject() as RoomResponse;
		if (await this.confirm(`Raum „${room.name}“ entfernen?`, { action: "Entfernen", danger: true })) {
			await this.run(() => this.save(del<RoomOverview>(`bauakten/${this.bauakteId}/rooms/${room.id}`)));
		}
	}
}
