import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get, post } from "../../util/api";
import { dateTime } from "../../util/format";
import { openForm } from "../../util/forms";
import { dossierText, restoreDossier, unlockDossier } from "../../util/dossier";
import { dossierStatus } from "../../util/infraModels";
import type { DossierDto } from "../../util/infraModels";

/** System › Aktenarchiv: ausgelagerte Bauakten, Abruf mit Fortschritt, Entsperren, neue Fassung. @namespace estrichone.controller.admin */
export default class Dossiers extends BaseController {
	private timer: ReturnType<typeof setTimeout> | undefined;

	onInit(): void { this.local({ items: [], busy: false }); }

	onRouteMatched(): void { void this.load(); }

	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		this.model().setProperty("/busy", true);
		const items = await this.run(() => get<DossierDto[]>("archive/dossiers"));
		this.model().setProperty("/busy", false);
		const rows = (items ?? []).map((d) => ({
			...d, stateText: dossierStatus[d.status].text, state: dossierStatus[d.status].state, detail: dossierText(d),
			busy: d.status === "restoring" || d.status === "archiving", percent: d.filesTotal ? Math.round((100 * d.filesDone) / d.filesTotal) : 0,
			archived: d.archivedAt ? dateTime(d.archivedAt) : "–",
		}));
		this.model().setProperty("/items", rows);
		clearTimeout(this.timer);
		if (rows.some((r) => r.busy)) this.timer = setTimeout(() => { if (this.v().getDomRef()) void this.load(); }, 3000);
	}

	private row(e: Event): DossierDto {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as DossierDto;
	}

	onOpen(e: Event): void { this.nav(`/bauakten/${this.row(e).bauakteId}`); }

	async onRestore(e: Event): Promise<void> {
		await this.run(() => restoreDossier(this.row(e).bauakteId), "Abruf gestartet");
		void this.load();
	}

	async onUnlock(e: Event): Promise<void> {
		if (await unlockDossier(this.row(e).bauakteId)) { this.toast("Entsperrt"); void this.load(); }
	}

	async onRearchive(e: Event): Promise<void> {
		await this.run(() => post(`archive/dossiers/${this.row(e).bauakteId}`), "Neue Fassung wird archiviert");
		void this.load();
	}

	async onArchive(): Promise<void> {
		const done = await openForm<DossierDto>({
			title: "Bauakte archivieren", submitText: "Archivieren und auslagern",
			fields: [
				{ key: "intro", type: "info", label: "Alle Daten, Dokumente, Fotos und Videos gehen auf den Archivserver und werden danach aus dem System entfernt. Die Akte bleibt als Platzhalter sichtbar und lässt sich jederzeit abrufen." },
				{ key: "bauakteId", label: "Bauakte", type: "bauakte", required: true },
			],
			submit: (v) => post<DossierDto>(`archive/dossiers/${String(v.bauakteId)}`),
		});
		if (done) { this.toast("Archivierung gestartet"); void this.load(); }
	}
}
