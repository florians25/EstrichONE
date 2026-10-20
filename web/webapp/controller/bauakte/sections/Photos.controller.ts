import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import Sorter from "sap/ui/model/Sorter";
import type Context from "sap/ui/model/Context";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import GridList from "sap/f/GridList";
import GridListItem from "sap/f/GridListItem";
import VBox from "sap/m/VBox";
import Image from "sap/m/Image";
import Text from "sap/m/Text";
import ObjectStatus from "sap/m/ObjectStatus";
import type UploadSet from "sap/m/upload/UploadSet";
import MessageBox from "sap/m/MessageBox";
import DocumentUploader from "../../../control/DocumentUploader";
import { del, problemMessage, put } from "../../../util/api";
import { akte } from "../../../util/akteStore";
import { isoDay } from "../../../util/days";
import { dateTime, longDay } from "../../../util/format";
import { openForm } from "../../../util/forms";
import { openLightbox } from "../../../util/lightbox";
import type { FileResponse, RoomOverview, WorkItemDto } from "../../../util/models";
import { session } from "../../../util/session";

interface PhotoRow extends FileResponse {
	thumb: string;
	takenAt: string;
	dayKey: string;
	time: string;
	relation: string;
}

/**
 * Fotos und Videos: Galerie als Zeitstrahl (nach Tag) oder Raster, Großansicht mit Vor/Zurück, Beschriftung,
 * Zuordnung zu Räumen und Mängeln, Mehrfachauswahl mit ZIP-Download, Filter.
 * @namespace estrichone.controller.bauakte.sections
 */
export default class Photos extends SectionBase {
	protected watches = ["files", "rooms", "work"];
	private all: PhotoRow[] = [];
	private rooms: RoomOverview | null = null;
	private work: WorkItemDto[] = [];
	private uploader!: DocumentUploader;
	private timer: ReturnType<typeof setTimeout> | undefined;

	protected init(): void {
		const model = new JSONModel({ photos: [], videos: [], mode: "timeline", query: "", room: "", work: "", selecting: false, selectedCount: 0,
			title: "", roomOptions: [], workOptions: [] });
		model.setSizeLimit(10000);
		this.v().setModel(model, "ph");
		this.uploader = new DocumentUploader();
		this.uploader.target = () => ({ bauakteId: this.bauakteId });
		const photoSet = this.v().byId("photoUpload") as UploadSet;
		photoSet.setUploader(this.uploader);
		photoSet.registerUploaderEvents(this.uploader);
		this.bind();
	}

	private ph(): JSONModel {
		return this.v().getModel("ph") as JSONModel;
	}

	private grid(): GridList {
		return this.v().byId("photoGrid") as GridList;
	}

	private roomName = (id: string | null) => (id ? this.rooms?.rooms.find((r) => r.id === id)?.name ?? null : null);
	private workName = (id: string | null) => (id ? this.work.find((w) => w.id === id)?.title ?? null : null);

	protected async load(): Promise<void> {
		const [files, rooms, work] = await Promise.all([
			akte.files(this.bauakteId), akte.rooms(this.bauakteId).catch(() => null),
			session.has("work.read") ? akte.work(this.bauakteId).catch(() => []) : Promise.resolve([]),
		]);
		this.rooms = rooms;
		this.work = work;
		this.all = files.filter((f) => f.kind === "photo").map((f) => {
			const taken = f.capturedAt ?? f.createdAt;
			return {
				...f, takenAt: taken, dayKey: isoDay(new Date(taken)),
				time: new Date(taken).toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" }),
				thumb: `/api/v1/files/${f.id}/content?variant=${f.variants === "ready" ? "thumb" : "original"}`,
				relation: [this.roomName(f.roomId), this.workName(f.workItemId)].filter(Boolean).join(" · "),
			};
		});
		const m = this.ph();
		m.setProperty("/roomOptions", [{ key: "", text: "Alle Räume" }, { key: "__none", text: "Ohne Raum" },
			...(rooms?.rooms ?? []).map((r) => ({ key: r.id, text: [r.name, r.floor].filter(Boolean).join(" · ") }))]);
		m.setProperty("/workOptions", [{ key: "", text: "Alle Bezüge" }, { key: "__defects", text: "Nur Mängel-Fotos" },
			...work.filter((w) => w.kind === "defect").map((w) => ({ key: w.id, text: `Mangel: ${w.title}` }))]);
		m.setProperty("/videos", files.filter((f) => f.kind === "video").map((v) => ({
			...v, html: v.state === "stored"
				? `<video controls preload="metadata" class="eoVideo" style="width:100%;border-radius:4px;background:#000" src="/api/v1/files/${v.id}/content"></video>`
				: "<div class=\"eoMuted\">Upload ausstehend</div>",
		})));
		this.refresh();
	}

	refresh(): void {
		const m = this.ph();
		const query = String(m.getProperty("/query") ?? "").trim().toLowerCase();
		const room = m.getProperty("/room") as string;
		const work = m.getProperty("/work") as string;
		const shown = this.all.filter((p) => (!query || [p.caption, p.originalName, p.relation].some((v) => v?.toLowerCase().includes(query)))
			&& (!room || (room === "__none" ? !p.roomId : p.roomId === room))
			&& (!work || (work === "__defects" ? !!p.workItemId && this.work.some((w) => w.id === p.workItemId && w.kind === "defect") : p.workItemId === work)));
		m.setProperty("/photos", shown);
		m.setProperty("/title", `Fotos (${shown.length}${shown.length !== this.all.length ? ` von ${this.all.length}` : ""})`);
		this.bind();
		this.grid().removeSelections(true);
		m.setProperty("/selectedCount", 0);
	}

	/** Zeitstrahl: nach Aufnahmetag gruppiert, neueste zuerst; Raster: ohne Gruppen. */
	private bind(): void {
		const timeline = this.ph().getProperty("/mode") === "timeline";
		const template = new GridListItem({
			type: "Active",
			press: (event) => this.onOpen(event),
			content: [new VBox({
				items: [
					new Image({ src: "{ph>thumb}", alt: "{= ${ph>caption} || 'Foto vom ' + ${ph>dayKey} }", width: "100%", lazyLoading: true, decorative: false })
						.addStyleClass("eoPhoto"),
					new VBox({ items: [
						new Text({ text: "{= ${ph>caption} || ${ph>originalName} }", wrapping: false }),
						new Text({ text: timeline ? "{ph>time}" : "{path: 'ph>takenAt', formatter: '.formatTaken'}" }).addStyleClass("eoMuted eoTabular"),
						new ObjectStatus({ text: "{ph>relation}", icon: "sap-icon://chain-link", visible: "{= !!${ph>relation} }", state: "Information" }),
					] }).addStyleClass("eoPhotoMeta"),
				],
			})],
		}).addStyleClass("eoPhotoTile");
		this.grid().bindItems({
			path: "ph>/photos",
			template,
			templateShareable: false,
			sorter: timeline
				? [new Sorter("dayKey", true, (context: Context) => {
					const day = context.getProperty("dayKey") as string;
					const count = (this.ph().getProperty("/photos") as PhotoRow[]).filter((p) => p.dayKey === day).length;
					return { key: day, text: `${longDay(day)} · ${count} ${count === 1 ? "Foto" : "Fotos"}` };
				}), new Sorter("takenAt", true)]
				: [new Sorter("takenAt", true)],
		});
	}

	formatTaken(value: string): string {
		return dateTime(value);
	}

	private rowOf(control: Control): PhotoRow {
		return control.getBindingContext("ph")?.getObject() as PhotoRow;
	}

	onOpen(event: Event): void {
		if (this.ph().getProperty("/selecting")) return;
		const row = this.rowOf(event.getSource() as Control);
		const list = this.ph().getProperty("/photos") as PhotoRow[];
		const ordered = [...list].sort((a, b) => b.takenAt.localeCompare(a.takenAt));
		openLightbox(ordered, ordered.findIndex((p) => p.id === row.id), {
			roomName: this.roomName, workName: this.workName, onChanged: () => akte.invalidate(this.bauakteId, "files"),
		});
	}

	onSelectMode(): void {
		this.grid().removeSelections(true);
		this.ph().setProperty("/selectedCount", 0);
	}

	onSelection(): void {
		this.ph().setProperty("/selectedCount", this.grid().getSelectedItems().length);
	}

	onSelectAll(): void {
		for (const item of this.grid().getItems()) {
			if (item instanceof GridListItem) item.setSelected(true);
		}
		this.onSelection();
	}

	private selected(): PhotoRow[] {
		return this.grid().getSelectedItems().map((i) => this.rowOf(i));
	}

	onDownloadSelected(): void {
		const rows = this.selected();
		if (rows.length) {
			window.location.href = `/api/v1/bauakten/${this.bauakteId}/files/archive?ids=${rows.map((r) => r.id).join(",")}`;
		}
	}

	async onAssign(): Promise<void> {
		const rows = this.selected();
		if (!rows.length) return;
		const done = await openForm({
			title: `${rows.length} ${rows.length === 1 ? "Foto" : "Fotos"} zuordnen`, submitText: "Zuordnen",
			initial: { roomId: rows.length === 1 ? rows[0].roomId : "", workItemId: rows.length === 1 ? rows[0].workItemId : "",
				caption: rows.length === 1 ? rows[0].caption ?? "" : "" },
			fields: [
				{ key: "roomId", label: "Raum", type: "select", placeholder: "kein Raum",
					options: (this.rooms?.rooms ?? []).map((r) => ({ key: r.id, text: [r.name, r.floor].filter(Boolean).join(" · ") })) },
				{ key: "workItemId", label: "Aufgabe / Mangel", type: "select", placeholder: "kein Bezug",
					options: this.work.map((w) => ({ key: w.id, text: `${w.kind === "defect" ? "Mangel" : "Aufgabe"}: ${w.title}` })) },
				{ key: "caption", label: "Beschriftung", maxLength: 500,
					placeholder: rows.length === 1 ? "z. B. Randdämmung Bad OG" : "leer = Beschriftungen bleiben unverändert" },
			],
			submit: async (v) => {
				for (const row of rows) {
					await put(`files/${row.id}/details`, {
						originalName: row.originalName, caption: (v.caption as string) || (rows.length === 1 ? null : row.caption), category: row.category,
						folder: row.folder, roomId: (v.roomId as string) || null, workItemId: (v.workItemId as string) || null,
					});
				}
				return true;
			},
		});
		if (done) {
			this.toast("Zuordnung gespeichert");
			akte.invalidate(this.bauakteId, "files");
		}
	}

	async onDeleteSelected(): Promise<void> {
		const rows = this.selected();
		if (!rows.length || !(await this.confirm(`${rows.length} ${rows.length === 1 ? "Foto" : "Fotos"} löschen?`, { action: "Löschen", danger: true }))) return;
		await this.deleteFiles(rows);
	}

	async onDeleteVideo(event: Event): Promise<void> {
		const row = (event.getSource() as Control).getBindingContext("ph")?.getObject() as FileResponse;
		if (await this.confirm(`„${row.originalName}“ löschen?`, { action: "Löschen", danger: true })) {
			await this.deleteFiles([row]);
		}
	}

	private async deleteFiles(rows: FileResponse[]): Promise<void> {
		const errors: string[] = [];
		for (const row of rows) {
			try {
				await del(`files/${row.id}`);
			} catch (error) {
				errors.push(`${row.originalName}: ${problemMessage(error)}`);
			}
		}
		if (errors.length) MessageBox.error(errors.join("\n"));
		else this.toast("Gelöscht");
		this.ph().setProperty("/selecting", false);
		akte.invalidate(this.bauakteId, "files");
	}

	onUploadCompleted(): void {
		clearTimeout(this.timer);
		this.timer = setTimeout(() => {
			const set = this.v().byId("photoUpload") as UploadSet;
			if (set.getIncompleteItems().some((i) => i.getUploadState() === "Uploading" || i.getUploadState() === "Ready")) {
				this.onUploadCompleted();
				return;
			}
			const done = set.getItems().filter((i) => i.getUploadState() === "Complete");
			for (const item of done) set.removeItem(item);
			for (const item of set.getIncompleteItems().filter((i) => i.getUploadState() === "Error")) set.removeIncompleteItem(item);
			if (done.length) this.toast(`${done.length} ${done.length === 1 ? "Datei" : "Dateien"} hochgeladen.`);
			if (this.uploader.errors.length) MessageBox.error(this.uploader.errors.join("\n"), { title: "Nicht hochgeladen" });
			this.uploader.errors = [];
			akte.invalidate(this.bauakteId, "files");
		}, 400);
	}
}
