import BaseController from "../BaseController";
import { get } from "../../util/api";
import { dateTime, fileSize } from "../../util/format";
import type { MonitorLevel, MonitoringOverview } from "../../util/infraModels";

const state: Record<MonitorLevel, string> = { ok: "Success", warning: "Warning", critical: "Error" };
const text: Record<MonitorLevel, string> = { ok: "In Ordnung", warning: "Warnung", critical: "Kritisch" };

/** System › Überwachung: Produktivsystem, Archivserver, Backup-Ziele (Platten, Trend, Dienste); alle 60 s aktualisiert. @namespace estrichone.controller.admin */
export default class Monitoring extends BaseController {
	private timer: ReturnType<typeof setInterval> | undefined;

	onInit(): void { this.local({ o: null, systems: [], busy: false, subtitle: "", warningText: "" }); }

	onRouteMatched(): void {
		void this.load();
		clearInterval(this.timer);
		this.timer = setInterval(() => { if (this.v().getDomRef()) void this.load(); else clearInterval(this.timer); }, 60_000);
	}

	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const o = await this.run(() => get<MonitoringOverview>("system/monitoring"));
		m.setProperty("/busy", false);
		if (!o) return;
		m.setProperty("/o", o);
		m.setProperty("/subtitle", `Stand ${dateTime(o.at)} · Warnschwellen: Platte ab 85 % (kritisch ab 95 % oder voll in weniger als 14 Tagen), Sicherung älter als 36 Stunden`);
		m.setProperty("/warningText", o.warnings.join(" · "));
		m.setProperty("/systems", o.systems.map((s) => ({
			...s, levelText: text[s.level], state: state[s.level],
			items: s.items.map((i) => ({ ...i, state: state[i.level] })),
			diskText: s.disk ? `${s.disk.usedPercent} % belegt · ${fileSize(s.disk.freeBytes)} frei` : "",
			diskState: !s.disk ? "None" : s.disk.usedPercent >= 95 ? "Error" : s.disk.usedPercent >= 85 ? "Warning" : "Success",
		})));
	}
}
