import BaseController from "../BaseController";
import { get, post } from "../../util/api";
import { dateTime } from "../../util/format";
import type { BackupRunInfo, BackupStatusResponse } from "../../util/models";

const info = (r: BackupRunInfo | null) => (r
	? { text: `${r.status === "succeeded" ? "Erfolgreich" : "Fehlgeschlagen"} · ${dateTime(r.at)} · ${r.message}`, state: r.status === "succeeded" ? "Success" : "Error" }
	: { text: "Noch nie", state: "None" });

/** Sicherung: Zustand, Verlauf, jetzt anstoßen; aktualisiert sich alle 15 Sekunden. @namespace estrichone.controller.admin */
export default class Backup extends BaseController {
	private timer: ReturnType<typeof setInterval> | undefined;

	onInit(): void {
		this.local({ s: null });
	}

	onRouteMatched(): void {
		void this.load();
		clearInterval(this.timer);
		this.timer = setInterval(() => {
			if (this.v().getDomRef()) void this.load(); else clearInterval(this.timer);
		}, 15_000);
	}

	private async load(): Promise<void> {
		try {
			const s = await get<BackupStatusResponse>("backup/status");
			this.model().setData({ s, run: info(s.lastRun), success: info(s.lastSuccess), check: info(s.lastCheck), restore: info(s.lastRestoreTest) });
		} catch (error) {
			clearInterval(this.timer);
			this.showError(error);
		}
	}

	async onRun(): Promise<void> {
		await this.run(() => post("backup/run"), "Sicherung angefordert");
		void this.load();
	}
}
