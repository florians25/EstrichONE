import SectionBase from "./SectionBase";
import { get } from "../../../util/api";
import { fileIcon } from "../../../util/documents";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { akte } from "../../../util/akteStore";
import { saveBauakte } from "../../../util/bauakte";
import { isoDay, parseDay } from "../../../util/days";
import { cmNumber, date, dateTime } from "../../../util/format";
import { assignmentKinds, statusMarkerColors } from "../../../util/labels";
import type { BauakteDetail, FileResponse } from "../../../util/models";
import { navTo } from "../../../util/navigation";
import { session } from "../../../util/session";
import { openLightbox } from "../../../util/lightbox";
import { openActivityDialog } from "../../../util/activityDialog";
import { akteContextOf } from "../../../util/akteHeader";

/** Übersicht: Objektbilder, Standort (im Büro per Klick setzen), Kennzahlen, Notizen, Aufgaben, Stammdaten, Aktivitäten. @namespace estrichone.controller.bauakte.sections */
export default class Overview extends SectionBase {
	protected watches = ["files", "work", "notes", "inspections", "planning", "diary", "history", "rooms"];
	private photos: FileResponse[] = [];

	protected init(): void {
		const model = new JSONModel({ hero: [], markers: [], picking: false, picked: null, hasLocation: false, pinned: [], work: [], activity: [], approvals: [], latestDocs: [] });
		model.setSizeLimit(1000);
		this.v().setModel(model, "ov");
	}

	private ov(): JSONModel {
		return this.v().getModel("ov") as JSONModel;
	}

	protected onBauakteUpdated(b: BauakteDetail): void {
		this.location(b);
	}

	private location(b: BauakteDetail, picked: [number, number] | null = null): void {
		const m = this.ov();
		const position = picked ?? (b.latitude !== null && b.longitude !== null ? [b.latitude, b.longitude] as [number, number] : null);
		m.setProperty("/hasLocation", !!position);
		m.setProperty("/markers", position ? [{ id: b.id, latitude: position[0], longitude: position[1], color: statusMarkerColors[b.status], label: b.title }] : []);
		m.setProperty("/coordinates", position
			? `${position[0].toLocaleString("de-DE", { maximumFractionDigits: 5 })}, ${position[1].toLocaleString("de-DE", { maximumFractionDigits: 5 })}` : "");
	}

	protected async load(): Promise<void> {
		const id = this.bauakteId;
		const m = this.ov();
		const today = isoDay(new Date());
		this.location(this.bauakte());
		m.setProperty("/picking", false);
		const [files, work, notes, inspections, planning, history, diary] = await Promise.allSettled([
			akte.files(id), session.has("work.read") ? akte.work(id) : Promise.resolve([]), akte.notes(id), akte.inspections(id),
			session.has("planning.read") ? akte.planning(id) : Promise.resolve(null), akte.history(id), akte.diary(id),
		]);
		if (files.status === "fulfilled") {
			this.photos = files.value.filter((f) => f.kind === "photo" && f.state === "stored");
			m.setProperty("/photoText", `${this.photos.length} ${this.photos.length === 1 ? "Foto" : "Fotos"}`);
			m.setProperty("/hero", this.photos.slice(0, 8).map((p) => ({
				id: p.id, url: `/api/v1/files/${p.id}/content?variant=${p.variants === "ready" ? "display" : "original"}`,
				alt: p.caption ?? `Foto vom ${dateTime(p.capturedAt ?? p.createdAt)}`,
			})));
			const docs = files.value.filter((f) => f.kind === "document");
			m.setProperty("/latestDocs", [...docs].filter((d) => d.state === "stored").sort((a, b) => b.createdAt.localeCompare(a.createdAt)).slice(0, 5)
				.map((d) => ({ ...d, icon: fileIcon(d.contentType, d.originalName) })));
			m.setProperty("/documentCount", String(docs.length));
			m.setProperty("/documentFooter", docs.length ? `zuletzt ${date(docs.map((d) => d.createdAt).sort().pop() ?? null)}` : "noch keine");
		}
		void get<{ items: { file: { id: string; originalName: string }; approvalAssignee: string | null }[] }>("documents", { bauakteId: id, approval: "pending" })
			.then((r) => m.setProperty("/approvals", r.items.map((i) => ({ id: i.file.id, name: i.file.originalName, assignee: i.approvalAssignee ?? "" }))))
			.catch(() => m.setProperty("/approvals", []));
		if (work.status === "fulfilled") {
			const open = work.value.filter((w) => w.status !== "done");
			const overdue = open.filter((w) => w.dueDate && w.dueDate < today).length;
			m.setProperty("/openWork", String(open.length));
			m.setProperty("/overdue", overdue);
			m.setProperty("/workFooter", [open.filter((w) => w.kind === "defect").length + " Mängel", overdue ? `${overdue} überfällig` : ""].filter(Boolean).join(" · "));
			m.setProperty("/work", open.slice(0, 6));
		}
		if (notes.status === "fulfilled") {
			m.setProperty("/pinned", notes.value.filter((n) => n.isPinned));
		}
		if (inspections.status === "fulfilled") {
			const sorted = [...inspections.value].sort((a, b) => b.performedAt.localeCompare(a.performedAt));
			const cm = sorted.find((i) => i.kind === "cmMeasurement");
			const initial = sorted.find((i) => i.kind === "initialInspection");
			if (cm) {
				m.setProperty("/inspectionValue", cmNumber(cm.measuredCmPercent));
				m.setProperty("/inspectionScale", "CM-%");
				m.setProperty("/inspectionColor", cm.isReadyForCovering ? "Good" : "Critical");
				m.setProperty("/inspectionFooter", `${cm.isReadyForCovering ? "Belegreif" : "Noch nicht belegreif"} · ${cm.location ?? ""}`);
			} else {
				m.setProperty("/inspectionValue", initial ? "✓" : "–");
				m.setProperty("/inspectionScale", "");
				m.setProperty("/inspectionColor", "Neutral");
				m.setProperty("/inspectionFooter", initial ? `Erstbegehung ${dateTime(initial.performedAt)}` : "Erstbegehung noch nicht ausgeführt");
			}
		}
		if (planning.status === "fulfilled" && planning.value) {
			const next = planning.value.assignments.filter((a) => a.day >= today)
				.sort((a, b) => (a.day + (a.startTime ?? "")).localeCompare(b.day + (b.startTime ?? "")))[0];
			m.setProperty("/nextValue", next ? parseDay(next.day).toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit" }) : "–");
			m.setProperty("/nextFooter", next ? `${next.startTime?.slice(0, 5) ?? ""} ${assignmentKinds[next.kind]?.text ?? ""}${next.crewName ? ` · ${next.crewName}` : ""}` : "nicht eingeplant");
		}
		if (history.status === "fulfilled") {
			m.setProperty("/activity", [...history.value].sort((a, b) => b.at.localeCompare(a.at)).slice(0, 8));
		}
		if (diary.status === "fulfilled") {
			m.setProperty("/diaryCount", String(diary.value.length));
			const last = [...diary.value].sort((a, b) => b.day.localeCompare(a.day))[0];
			m.setProperty("/diaryFooter", last ? `zuletzt ${date(last.day)}` : "noch kein Eintrag");
		}
	}

	onGallery(): void {
		if (this.photos.length) {
			openLightbox(this.photos, 0);
		}
	}

	onPick(): void {
		this.ov().setProperty("/picking", true);
		this.ov().setProperty("/picked", null);
	}

	onPicked(event: Event): void {
		const p = event as unknown as { getParameter(n: string): number };
		const picked: [number, number] = [p.getParameter("latitude"), p.getParameter("longitude")];
		this.ov().setProperty("/picked", picked);
		this.location(this.bauakte(), picked);
	}

	onPickCancel(): void {
		this.ov().setProperty("/picking", false);
		this.location(this.bauakte());
	}

	async onPickSave(): Promise<void> {
		const picked = this.ov().getProperty("/picked") as [number, number] | null;
		if (!picked) return;
		try {
			await saveBauakte(this.bauakte(), { latitude: picked[0], longitude: picked[1] });
			this.ov().setProperty("/picking", false);
			this.toast(this.t("overview.locationSaved"));
		} catch (error) {
			this.showError(error);
		}
	}

	private section(key: string): void {
		navTo(`/bauakten/${this.bauakteId}?abschnitt=${key}`);
	}

	onToPhotos(): void {
		this.section("fotos");
	}

	onToWork(): void {
		this.section("aufgaben");
	}

	onToDocumentation(): void {
		this.section("dokumentation");
	}

	onToAssignments(): void {
		this.section("einsaetze");
	}

	onToDocuments(): void {
		this.section("dokumente");
	}

	async onActivities(): Promise<void> {
		try {
			openActivityDialog(await akte.history(this.bauakteId),
				akteContextOf((this.v().getModel("akte") as JSONModel | undefined)?.getData() as Parameters<typeof akteContextOf>[0]));
		} catch (error) {
			this.showError(error);
		}
	}

	onToNotes(): void {
		this.section("notizen");
	}

	onOpenDoc(event: Event): void {
		const id = (event.getSource() as Control).getBindingContext("ov")?.getProperty("id") as string;
		navTo(`/bauakten/${this.bauakteId}/datei/${id}?abschnitt=dokumente`);
	}
}
