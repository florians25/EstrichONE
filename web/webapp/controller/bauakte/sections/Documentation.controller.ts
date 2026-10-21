import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get, syncPush } from "../../../util/api";
import { akte } from "../../../util/akteStore";
import { addDays, isoDay } from "../../../util/days";
import { area, date, dateTime, timeRange, weatherLine, weatherNames } from "../../../util/format";
import { openInitialInspectionWizard } from "../../../util/inspectionWizard";
import { openForm, type Option } from "../../../util/forms";
import { uuidv7 } from "../../../util/ids";
import { binderNames, checkTitles, inspectionResult } from "../../../util/labels";
import { openLightbox } from "../../../util/lightbox";
import type { CrewResponse, DiaryEntry, FileResponse, Inspection, RoomOverview } from "../../../util/models";
import { session } from "../../../util/session";
import { acceptanceResults, type AcceptanceResult } from "../../../util/acceptanceWizard";
import { runAcceptance, showAcceptanceLink } from "../../../util/acceptanceFlow";
import { akteContextOf } from "../../../util/akteHeader";
import { openPreview } from "../../../util/previewDialog";
import { post } from "../../../util/api";

interface AcceptanceDto {
	id: string;
	performedAt: string;
	performedByUserId: string;
	clientName: string | null;
	contractorName: string | null;
	result: AcceptanceResult;
	state: "recorded" | "awaitingCustomer" | "completed";
	customerLinkExpiresAt: string | null;
	customerEmail: string | null;
	customerDecision: "approved" | "rejected" | null;
	customerDecidedAt: string | null;
	customerDecisionName: string | null;
	customerComment: string | null;
	pdfFileId: string | null;
}

const acceptanceStates: Record<AcceptanceDto["state"], { text: string; state: string }> = {
	recorded: { text: "Unterschrift des Kunden fehlt", state: "Warning" },
	awaitingCustomer: { text: "Wartet auf Freigabe durch den Kunden", state: "Information" },
	completed: { text: "Abgeschlossen, PDF unter „Abnahmen“", state: "Success" },
};

interface PhotoRef {
	id: string;
	thumb: string;
	alt: string;
}

/**
 * Baustellendokumentation: Bautagebuch (je Tag mit Wetter, Arbeitszeit, Kolonne, Fotos), Prüfungen und Protokolle
 * (Erstbegehung, CM-Messung mit Belegreife) und Berichte zum Drucken. Erfassen läuft wie in den Apps über den Sync.
 * @namespace estrichone.controller.bauakte.sections
 */
export default class Documentation extends SectionBase {
	protected watches = ["diary", "inspections", "files", "rooms"];
	private photos: FileResponse[] = [];
	private rooms: RoomOverview | null = null;
	private crews: CrewResponse[] = [];

	protected init(): void {
		const today = isoDay(new Date());
		const model = new JSONModel({ tab: "tagebuch", diary: [], diaryShown: [], diaryQuery: "", cm: [], initial: [], initialOlder: [], latest: null,
			checks: [], latestPhotos: [], reportFrom: addDays(today, -90), reportTo: today, reportPhotos: true, reportWork: true, diaryCount: "", inspectionCount: "" });
		model.setSizeLimit(5000);
		this.v().setModel(model, "dc");
	}

	private dc(): JSONModel {
		return this.v().getModel("dc") as JSONModel;
	}

	private photoRefs(ids: string[] | null | undefined): PhotoRef[] {
		return (ids ?? []).map((id) => this.photos.find((p) => p.id === id)).filter((p): p is FileResponse => !!p && p.state === "stored").map((p) => ({
			id: p.id, thumb: `/api/v1/files/${p.id}/content?variant=${p.variants === "ready" ? "thumb" : "original"}`,
			alt: p.caption ?? `Foto vom ${dateTime(p.capturedAt ?? p.createdAt)}`,
		}));
	}

	private showAcceptances(list: AcceptanceDto[]): void {
		const m = this.dc();
		m.setProperty("/acceptanceCount", String(list.length));
		m.setProperty("/acceptances", list.map((a) => ({
			...a,
			resultText: acceptanceResults[a.result].text, resultState: acceptanceResults[a.result].state,
			stateText: acceptanceStates[a.state].text, stateState: acceptanceStates[a.state].state,
			line: `${dateTime(a.performedAt)} · ${[a.clientName, a.contractorName].filter(Boolean).join(" / ") || session.name(a.performedByUserId)}`,
			customerLine: a.customerDecision
				? `Kunde ${a.customerDecision === "approved" ? "hat freigegeben" : "hat abgelehnt"}: ${a.customerDecisionName} am ${dateTime(a.customerDecidedAt)}${a.customerComment ? ` – „${a.customerComment}“` : ""}`
				: a.state === "awaitingCustomer" && a.customerLinkExpiresAt ? `Link gültig bis ${dateTime(a.customerLinkExpiresAt)}${a.customerEmail ? ` · an ${a.customerEmail}` : ""}` : "",
		})));
	}

	private acceptanceOf(event: Event): AcceptanceDto {
		return (event.getSource() as Control).getBindingContext("dc")?.getObject() as AcceptanceDto;
	}

	async onAddAcceptance(): Promise<void> {
		if (await runAcceptance(this.bauakteId, (this.v().getModel("akte") as JSONModel | undefined)?.getData() as Parameters<typeof runAcceptance>[1])) {
			this.dc().setProperty("/tab", "abnahmen");
		}
	}

	async onAcceptanceLink(event: Event): Promise<void> {
		const a = this.acceptanceOf(event);
		const email = await openForm<string | null>({
			title: "Freigabelink für den Kunden", submitText: "Link erstellen", initial: { email: a.customerEmail ?? "" },
			intro: "Ein neuer Link ersetzt einen früheren. Der Kunde sieht nur dieses Protokoll und kann freigeben oder mit Begründung ablehnen.",
			fields: [{ key: "email", label: "E-Mail-Adresse (optional)", type: "email", maxLength: 320 }],
			submit: async (v) => (v.email as string) || "",
		});
		if (email === null) return;
		try {
			const link = await post<{ url: string; expiresAt: string; emailed: boolean }>(`acceptances/${a.id}/customer-link`, { email: email || null });
			showAcceptanceLink(link);
			void this.reload();
		} catch (error) {
			this.showError(error);
		}
	}

	async onAcceptancePdf(event: Event): Promise<void> {
		const a = this.acceptanceOf(event);
		const file = await get<FileResponse>(`files/${a.pdfFileId}`).catch(() => null);
		if (!file) return;
		openPreview({ files: [file], index: 0, context: akteContextOf((this.v().getModel("akte") as JSONModel | undefined)?.getData() as Parameters<typeof akteContextOf>[0]) });
	}

	protected async load(): Promise<void> {
		const id = this.bauakteId;
		const [diary, inspections, files, rooms, acceptances] = await Promise.all([
			akte.diary(id), akte.inspections(id), akte.files(id), akte.rooms(id).catch(() => null),
			get<AcceptanceDto[]>(`bauakten/${id}/acceptances`).catch(() => [] as AcceptanceDto[]),
		]);
		this.showAcceptances(acceptances);
		if (session.has("planning.read") && !this.crews.length) {
			this.crews = await get<CrewResponse[]>("planning/crews").catch(() => []);
		}
		this.photos = files.filter((f) => f.kind === "photo");
		this.rooms = rooms;
		const m = this.dc();
		const own = session.userId;
		const entries = [...diary].sort((a, b) => b.day.localeCompare(a.day) || b.createdAt.localeCompare(a.createdAt)).map((e) => ({
			...e,
			weatherText: weatherLine(e),
			timeText: timeRange(e.startTime, e.endTime),
			photos: this.photoRefs(e.photoFileIds),
			own: e.authorUserId === own && session.has("diary.write"),
			meta: `${session.name(e.authorUserId)} · ${dateTime(e.createdAt)}${e.editedAt ? " · bearbeitet" : ""}`,
		}));
		m.setProperty("/diary", entries);
		m.setProperty("/diaryCount", String(entries.length));
		this.refreshDiary();

		const all = [...inspections].sort((a, b) => b.performedAt.localeCompare(a.performedAt));
		const initial = all.filter((i) => i.kind === "initialInspection");
		const latest = initial[0] ?? null;
		m.setProperty("/initial", initial);
		m.setProperty("/cm", all.filter((i) => i.kind === "cmMeasurement"));
		m.setProperty("/inspectionCount", String(all.length));
		m.setProperty("/latest", latest);
		m.setProperty("/latestText", latest ? `Erfasst am ${dateTime(latest.performedAt)}` : "");
		m.setProperty("/latestResult", latest ? inspectionResult(latest) : null);
		m.setProperty("/checks", latest && !latest.notRequired ? Object.keys(checkTitles).map((k) => ({ title: checkTitles[k], result: latest.checks[k] ?? null })) : []);
		m.setProperty("/latestPhotos", this.photoRefs(latest?.photoFileIds));
		m.setProperty("/initialOlder", initial.slice(1).map((i) => ({ ...i, resultText: inspectionResult(i).text, resultTone: inspectionResult(i).tone })));
		m.setProperty("/screeds", [...new Set((rooms?.rooms ?? []).map((r) => r.screedTypeName).filter(Boolean))].join(", "));
		m.setProperty("/areaText", rooms ? area(rooms.totalAreaSquareMeters) : "–");
		m.setProperty("/heatingText", rooms?.rooms.length ? (rooms.rooms.some((r) => r.underfloorHeating) ? "ja" : "nein") : "unbekannt");
	}

	refreshDiary(): void {
		const m = this.dc();
		const q = String(m.getProperty("/diaryQuery") ?? "").trim().toLowerCase();
		const all = m.getProperty("/diary") as (DiaryEntry & { meta: string })[];
		m.setProperty("/diaryShown", all.filter((e) => !q || [e.text, e.crew, e.meta, date(e.day)].some((v) => v?.toLowerCase().includes(q))));
	}

	private photoOptions(): Option[] {
		return [...this.photos].filter((p) => p.state === "stored").sort((a, b) => (b.capturedAt ?? b.createdAt).localeCompare(a.capturedAt ?? a.createdAt))
			.map((p) => ({ key: p.id, text: p.caption || p.originalName, additionalText: dateTime(p.capturedAt ?? p.createdAt) }));
	}

	private async diaryForm(entry: DiaryEntry | null): Promise<void> {
		const crews = this.crews.filter((c) => c.isActive).map((c) => ({ key: c.name, text: c.name }));
		const saved = await openForm({
			title: entry ? "Tagesbericht bearbeiten" : "Tagesbericht erfassen", width: "42rem",
			initial: entry ? { ...entry, photoFileIds: entry.photoFileIds ?? [] }
				: { day: isoDay(new Date()), startTime: "07:00:00", photoFileIds: [] },
			fields: [
				{ key: "day", label: "Tag", type: "date", required: true },
				{ key: "weather", label: "Wetter", type: "select", options: Object.entries(weatherNames).map(([key, text]) => ({ key, text })) },
				{ key: "temperatureCelsius", label: "Temperatur", type: "integer", unit: "°C", min: -40, max: 60 },
				{ key: "startTime", label: "Arbeitsbeginn", type: "time" },
				{ key: "endTime", label: "Arbeitsende", type: "time",
					validate: (v, all) => (v && all.startTime && String(v) <= String(all.startTime) ? "Das Ende muss nach dem Beginn liegen." : null) },
				{ key: "crew", label: "Kolonne", type: "combo", options: crews, placeholder: "z. B. Kolonne Nord" },
				{ key: "workersPresent", label: "Anwesende", type: "integer", unit: "Pers.", min: 0, max: 500 },
				{ key: "text", label: "Was wurde gemacht?", type: "textarea", rows: 6, required: true, maxLength: 10000,
					placeholder: "Arbeiten, Lieferungen, Besonderheiten, Behinderungen …" },
				{ key: "photoFileIds", label: "Fotos des Tages", type: "multi", options: this.photoOptions(), placeholder: "Fotos der Bauakte verknüpfen" },
			],
			submit: async (v) => {
				const values = {
					text: v.text, weather: v.weather || null, temperatureCelsius: v.temperatureCelsius ?? null, workersPresent: v.workersPresent ?? null,
					startTime: v.startTime || null, endTime: v.endTime || null, crew: v.crew || null, photoFileIds: v.photoFileIds ?? [],
				};
				if (entry) {
					await syncPush("diary.update", entry.id, { changes: values, base: {} }, uuidv7());
				} else {
					await syncPush("diary.create", uuidv7(), { bauakteId: this.bauakteId, day: v.day, ...values }, uuidv7());
				}
				return true;
			},
		});
		if (saved) {
			this.toast(entry ? "Tagesbericht gespeichert" : "Tagesbericht erfasst");
			akte.invalidate(this.bauakteId, "diary");
		}
	}

	onAddDiary(): void {
		void this.diaryForm(null);
	}

	private entryOf(event: Event): DiaryEntry {
		let control = event.getSource() as Control | null;
		while (control && !(control.getBindingContext("dc")?.getObject() as { day?: string } | undefined)?.day) {
			control = control.getParent() as Control | null;
		}
		return control?.getBindingContext("dc")?.getObject() as DiaryEntry;
	}

	onEditDiary(event: Event): void {
		void this.diaryForm(this.entryOf(event));
	}

	async onDeleteDiary(event: Event): Promise<void> {
		const entry = this.entryOf(event);
		if (!(await this.confirm(`Tagesbericht vom ${date(entry.day)} löschen?`, { action: "Löschen", danger: true }))) return;
		await this.run(() => syncPush("diary.delete", entry.id, {}, uuidv7()), "Tagesbericht gelöscht");
		akte.invalidate(this.bauakteId, "diary");
	}

	private openPhoto(event: Event, list: PhotoRef[]): void {
		const ref = (event.getSource() as Control).getBindingContext("dc")?.getObject() as PhotoRef;
		const files = list.map((r) => this.photos.find((p) => p.id === r.id)).filter((p): p is FileResponse => !!p);
		openLightbox(files, files.findIndex((f) => f.id === ref.id));
	}

	onDiaryPhoto(event: Event): void {
		const control = event.getSource() as Control;
		const entry = control.getParent()?.getBindingContext("dc")?.getObject() as { photos: PhotoRef[] };
		this.openPhoto(event, entry.photos);
	}

	onInspectionPhoto(event: Event): void {
		this.openPhoto(event, this.dc().getProperty("/latestPhotos") as PhotoRef[]);
	}

	// Prüfungen

	private async initialForm(corrects: Inspection | null): Promise<void> {
		const saved = await openInitialInspectionWizard({ bauakteId: this.bauakteId, photos: [...this.photos], corrects });
		if (saved) {
			this.toast("Erstbegehung gespeichert");
			akte.invalidate(this.bauakteId, "inspections", "work", "files");
		}
	}

	onAddInitial(): void {
		this.dc().setProperty("/tab", "pruefungen");
		void this.initialForm(null);
	}

	onCorrectInitial(): void {
		void this.initialForm(this.dc().getProperty("/latest") as Inspection);
	}

	async onAddCm(): Promise<void> {
		this.dc().setProperty("/tab", "pruefungen");
		const rooms = (this.rooms?.rooms ?? []).map((r) => ({ key: [r.name, r.floor].filter(Boolean).join(" "), text: [r.name, r.floor].filter(Boolean).join(" ") }));
		const firstRoom = this.rooms?.rooms[0];
		const saved = await openForm({
			title: "CM-Messung erfassen",
			intro: "Der Grenzwert für die Belegreife ergibt sich aus Bindemittel und Beheizung (CT 2,0/1,8 CM-%, CA 0,5/0,3 CM-%).",
			initial: { binder: "cement", heated: firstRoom?.underfloorHeating ?? false, thicknessMm: firstRoom?.thicknessMillimeters ?? null },
			fields: [
				{ key: "location", label: "Messstelle (Raum)", type: "combo", options: rooms, required: true, placeholder: "z. B. Bad OG" },
				{ key: "binder", label: "Bindemittel", type: "select", required: true, options: Object.entries(binderNames).map(([key, text]) => ({ key, text })) },
				{ key: "heated", label: "Beheizter Estrich", type: "switch" },
				{ key: "thicknessMm", label: "Estrichdicke", type: "integer", unit: "mm", min: 10, max: 300 },
				{ key: "measuredCmPercent", label: "Messwert", type: "number", unit: "CM-%", required: true, min: 0, max: 20 },
				{ key: "remarks", label: "Bemerkung", type: "textarea", maxLength: 4000 },
			],
			submit: async (v) => {
				await syncPush("inspection.create", uuidv7(), {
					bauakteId: this.bauakteId, kind: "cmMeasurement", location: v.location, binder: v.binder, heated: !!v.heated,
					thicknessMm: v.thicknessMm ?? null, measuredCmPercent: v.measuredCmPercent, remarks: v.remarks || null,
				}, uuidv7());
				return true;
			},
		});
		if (saved) {
			this.toast("CM-Messung gespeichert");
			akte.invalidate(this.bauakteId, "inspections");
		}
	}

	// Berichte

	private download(path: string, stored: boolean): void {
		const a = document.createElement("a");
		a.href = `/api/v1/${path}`;
		a.download = "";
		a.click();
		this.toast(stored ? "PDF wird erstellt und unter „Protokolle / Berichte“ abgelegt …" : "PDF wird erstellt …");
		if (stored) setTimeout(() => akte.invalidate(this.bauakteId, "files"), 4000);
	}

	/** Baustellenbericht als Server-PDF (Zeitraum, Fotos, Aufgaben wählbar). */
	onReport(): void {
		const m = this.dc();
		const parts = ["uebersicht", "raeume", "tagebuch", "pruefungen", ...(m.getProperty("/reportWork") ? ["maengel"] : []), ...(m.getProperty("/reportPhotos") ? ["fotos"] : [])];
		const query = new URLSearchParams({ parts: parts.join(","), maxPhotos: m.getProperty("/reportPhotos") ? "40" : "0" });
		if (m.getProperty("/reportFrom")) query.set("from", m.getProperty("/reportFrom") as string);
		if (m.getProperty("/reportTo")) query.set("to", m.getProperty("/reportTo") as string);
		this.download(`bauakten/${this.bauakteId}/report.pdf?${query.toString()}`, true);
	}

	onAcceptance(): void {
		this.download(`bauakten/${this.bauakteId}/acceptance-form.pdf`, false);
	}

	onSheet(): void {
		this.download(`bauakten/${this.bauakteId}/report.pdf?parts=uebersicht,raeume&maxPhotos=0`, true);
	}
}
