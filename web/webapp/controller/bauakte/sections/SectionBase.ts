import BaseController from "../../BaseController";
import EventBus from "sap/ui/core/EventBus";
import type { BauakteDetail } from "../../../util/models";

/**
 * Grundlage der Abschnitte einer Bauakte: lädt beim Öffnen und erneut, wenn eine Änderung betroffene Daten
 * (z. B. Dateien, Räume) meldet – solange der Abschnitt schon einmal geöffnet war.
 * @namespace estrichone.controller.bauakte.sections
 */
export default abstract class SectionBase extends BaseController {
	protected bauakteId = "";
	private loadedFor = "";
	/** Datenarten, bei deren Änderung neu geladen wird. */
	protected watches: string[] = [];

	onInit(): void {
		EventBus.getInstance().subscribe("bauakte", "changed", (_c: string, _e: string, data: object) => {
			const { id, keys } = data as { id: string; keys: string[] };
			if (id === this.bauakteId && this.loadedFor === id && keys.some((k) => this.watches.includes(k))) {
				void this.reload();
			}
		});
		EventBus.getInstance().subscribe("bauakte", "updated", (_c: string, _e: string, data: object) => {
			const b = (data as { bauakte: BauakteDetail }).bauakte;
			if (b.id === this.bauakteId && this.loadedFor === b.id) this.onBauakteUpdated(b);
		});
		this.init();
	}

	/** Für Unterklassen statt onInit. */
	protected init(): void {
		// nichts
	}

	protected onBauakteUpdated(_b: BauakteDetail): void {
		// nichts
	}

	async show(bauakteId: string): Promise<void> {
		const first = this.loadedFor !== bauakteId;
		this.bauakteId = bauakteId;
		this.loadedFor = bauakteId;
		if (first) {
			await this.reload();
		}
	}

	protected async reload(): Promise<void> {
		this.v().setBusy(true);
		try {
			await this.load();
		} catch (error) {
			this.showError(error);
		} finally {
			this.v().setBusy(false);
		}
	}

	protected abstract load(): Promise<void>;

	protected bauakte(): BauakteDetail {
		return this.v().getModel("akte")?.getProperty("/bauakte") as BauakteDetail;
	}
}
