import BaseController from "../BaseController";
import { session } from "../../util/session";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get } from "../../util/api";
import { adminSections, type AdminArea } from "../../util/adminCatalog";
import { dateTime } from "../../util/format";
import type { BackupStatusResponse, TicketSummary } from "../../util/models";
import type { ArchiveOverview, MailSettingsDto, MonitoringOverview } from "../../util/infraModels";

type AreaStatus = { statusText: string; statusState: string; issue?: { text: string; level: "error" | "warning" } };

/**
 * Verwaltungsübersicht: alle Verwaltungsbereiche als Karten in Gruppen (Organisation, Benutzer & Geräte, Datensicherung & Archiv,
 * System & Betrieb, Integrationen) mit Suche; Status je Bereich und eine Statuszeile oben.
 * @namespace estrichone.controller.admin
 */
export default class Overview extends BaseController {
	private states: Record<string, AreaStatus> = {};

	onInit(): void {
		this.local({ query: "", sections: [], status: null });
	}

	onRouteMatched(): void {
		this.render();
		void this.load();
	}

	onRefresh(): void { void this.load(); }

	onSearch(): void { this.render(); }

	/** Enter: genau ein Treffer → direkt öffnen. */
	onSearchSubmit(): void {
		const sections = this.model().getProperty("/sections") as { areas: AdminArea[] }[];
		const areas = sections.flatMap((s) => s.areas);
		if (areas.length === 1) this.nav(areas[0].path);
	}

	onOpen(event: Event): void {
		const area = (event.getSource() as Control).getBindingContext("view")?.getObject() as AdminArea;
		this.nav(area.path);
	}

	private render(): void {
		const m = this.model();
		const query = String(m.getProperty("/query") ?? "");
		m.setProperty("/sections", adminSections((p) => this.has(p), query, (path) => session.pathEnabled(path)).map((g) => ({
			...g, areas: g.areas.map((a) => ({ ...a, statusText: this.states[a.key]?.statusText ?? "", statusState: this.states[a.key]?.statusState ?? "None" })),
		})));
	}

	private async load(): Promise<void> {
		const [monitoring, backup, archive, mail, tickets] = await Promise.allSettled([
			this.has("backup.manage") ? get<MonitoringOverview>("system/monitoring") : Promise.resolve(null),
			this.has("backup.manage") ? get<BackupStatusResponse>("backup/status") : Promise.resolve(null),
			this.has("settings.manage") ? get<ArchiveOverview>("archive/status") : Promise.resolve(null),
			this.has("settings.manage") ? get<MailSettingsDto>("mail/settings") : Promise.resolve(null),
			this.has("helpdesk.agent") ? get<TicketSummary[]>("tickets", { filter: "open" }) : Promise.resolve(null),
		]);
		const value = <T>(r: PromiseSettledResult<T | null>) => (r.status === "fulfilled" ? r.value : null);
		const states: Record<string, AreaStatus> = {};
		const b = value(backup);
		if (b) {
			states.backup = !b.configured ? { statusText: "nicht eingerichtet", statusState: "Warning", issue: { text: "Sicherung nicht eingerichtet", level: "warning" } }
				: !b.lastSuccess ? { statusText: "noch keine", statusState: "Error", issue: { text: "noch keine erfolgreiche Sicherung", level: "error" } }
				: b.healthy ? { statusText: "aktuell", statusState: "Success" }
				: { statusText: "veraltet", statusState: "Error", issue: { text: `Sicherung veraltet (zuletzt ${dateTime(b.lastSuccess.at)})`, level: "error" } };
		}
		const o = value(monitoring);
		if (o) {
			states.monitoring = o.level === "ok" ? { statusText: "in Ordnung", statusState: "Success" }
				: { statusText: o.level === "critical" ? "kritisch" : "Warnung", statusState: o.level === "critical" ? "Error" : "Warning",
					issue: { text: o.warnings[0] ?? "Überwachung meldet eine Warnung", level: o.level === "critical" ? "error" : "warning" } };
		}
		const a = value(archive);
		if (a) {
			states.archive = !a.connected ? { statusText: "nicht gekoppelt", statusState: "None" }
				: a.counts.failed ? { statusText: `${a.counts.failed} fehlgeschlagen`, statusState: "Error", issue: { text: `${a.counts.failed} Archivierungen fehlgeschlagen`, level: "error" } }
				: { statusText: "verbunden", statusState: "Success" };
		}
		const mailState = value(mail);
		if (mailState) {
			states.mail = !mailState.configured ? { statusText: "nicht eingerichtet", statusState: "None" }
				: mailState.lastTestOk === false ? { statusText: "Fehler", statusState: "Error", issue: { text: "E-Mail-Versand fehlerhaft (letzter Test)", level: "warning" } }
				: mailState.enabled ? { statusText: "aktiv", statusState: "Success" } : { statusText: "aus", statusState: "Warning" };
		}
		const t = value(tickets);
		if (t) {
			const open = t.filter((x) => x.status === "new" || x.reporterBlocked).length;
			states.helpdesk = open ? { statusText: `${open} neu`, statusState: "Warning" } : { statusText: "nichts neu", statusState: "Success" };
		}
		this.states = states;
		this.statusLine(Object.values(states).map((s) => s.issue), "Sicherung, Archiv, E-Mail und Überwachung ohne Befund");
		if (!Object.keys(states).length) this.model().setProperty("/status", null);
		this.render();
	}
}
