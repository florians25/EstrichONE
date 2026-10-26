import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import { del, get, put } from "../../util/api";
import { dateTime } from "../../util/format";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";

interface Announcement { id: string; title: string; text: string; severity: string; startsAt: string | null; endsAt: string | null; roleIds: string[] | null; notify: boolean }
interface Maintenance { message: string | null; startsAt: string; endsAt: string | null; adminsMayWork: boolean }
interface Doc { announcements: Announcement[]; maintenance: Maintenance | null }
interface Overview { instance: Doc; console: Doc; consoleManaged: boolean; serverTime: string }
interface Role { id: string; name: string }

const severities: Record<string, { text: string; state: string }> = {
	info: { text: "Info", state: "Information" }, warning: { text: "Warnung", state: "Warning" }, critical: { text: "Kritisch", state: "Error" },
};
const pad = (n: number) => String(n).padStart(2, "0");
const toLocal = (iso: string | null) => {
	if (!iso) return { date: null, time: null };
	const d = new Date(iso);
	return { date: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`, time: `${pad(d.getHours())}:${pad(d.getMinutes())}:00` };
};
const toIso = (date: unknown, time: unknown) => (date ? new Date(`${date}T${time || "00:00:00"}`).toISOString() : null);
const period = (a: { startsAt: string | null; endsAt: string | null }) =>
	a.startsAt || a.endsAt ? `${a.startsAt ? dateTime(a.startsAt) : "sofort"} – ${a.endsAt ? dateTime(a.endsAt) : "offen"}` : "dauerhaft";

/** System › Hinweise & Wartung: Ankündigungen und Wartungsmodus der Instanz; Hinweise der Hosting-Konsole nur lesen. @namespace estrichone.controller.admin */
export default class Notices extends BaseController {
	private data: Overview | null = null;
	private roles: Role[] = [];

	onInit(): void {
		this.local({ busy: false, announcements: [], console: [], consoleMaintenance: "", maintenance: {} });
	}

	onRouteMatched(): void { void this.load(); }
	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const [data, roles] = await Promise.all([this.run(() => get<Overview>("system/notices/admin")), this.roles.length ? Promise.resolve(this.roles) : get<Role[]>("access/roles").catch(() => [])]);
		m.setProperty("/busy", false);
		if (!data) return;
		this.data = data;
		this.roles = roles ?? [];
		const now = new Date(data.serverTime).getTime();
		const roleName = (id: string) => this.roles.find((r) => r.id === id)?.name ?? "Rolle";
		m.setProperty("/announcements", data.instance.announcements.map((a) => {
			const start = a.startsAt ? new Date(a.startsAt).getTime() : -Infinity;
			const end = a.endsAt ? new Date(a.endsAt).getTime() : Infinity;
			const status = now < start ? { statusText: "geplant", statusState: "Information" } : now >= end ? { statusText: "abgelaufen", statusState: "None" } : { statusText: "aktiv", statusState: "Success" };
			return { ...a, ...status, period: period(a), severityText: severities[a.severity]?.text ?? a.severity, severityState: severities[a.severity]?.state ?? "None",
				target: a.roleIds?.length ? a.roleIds.map(roleName).join(", ") : "alle Benutzer" };
		}));
		m.setProperty("/console", data.console.announcements.map((a) => ({ ...a, period: period(a), severityState: severities[a.severity]?.state ?? "None" })));
		const cm = data.console.maintenance;
		m.setProperty("/consoleMaintenance", cm ? `Wartung durch den Betreiber: ${period(cm)}${cm.message ? ` – ${cm.message}` : ""}` : "");
		const w = data.instance.maintenance;
		const running = !!w && new Date(w.startsAt).getTime() <= now && (!w.endsAt || new Date(w.endsAt).getTime() > now);
		m.setProperty("/maintenance", {
			has: !!w && (!w.endsAt || new Date(w.endsAt).getTime() > now), running,
			text: running ? "Wartung läuft" : w && (!w.endsAt || new Date(w.endsAt).getTime() > now) ? "Wartung geplant" : "Keine Wartung",
			state: running ? "Error" : w ? "Warning" : "Success",
			detail: w ? `${period(w)}${w.message ? ` · ${w.message}` : ""}${w.adminsMayWork ? " · Administratoren arbeiten weiter" : " · auch Administratoren gesperrt"}` : "",
		});
	}

	onNew(): void { void this.edit(null); }
	onEdit(e: Event): void { void this.edit((e.getSource() as Control).getBindingContext("view")?.getObject() as Announcement); }

	private async edit(a: Announcement | null): Promise<void> {
		const s = toLocal(a?.startsAt ?? null);
		const en = toLocal(a?.endsAt ?? null);
		const saved = await openForm<{ sentEmails: number }>({
			title: a ? "Ankündigung bearbeiten" : "Neue Ankündigung", width: "36rem",
			initial: { title: a?.title, text: a?.text, severity: a?.severity ?? "info", startDate: s.date, startTime: s.time, endDate: en.date, endTime: en.time,
				roleIds: a?.roleIds ?? [], notify: a?.notify ?? false, sendEmail: false },
			fields: [
				{ key: "title", label: "Titel", required: true, maxLength: 120 },
				{ key: "text", label: "Text", type: "textarea", rows: 4, maxLength: 2000 },
				{ key: "severity", label: "Art", type: "segmented", options: Object.entries(severities).map(([key, v]) => ({ key, text: v.text })) },
				{ key: "sPeriod", label: "Zeitraum (leer = sofort bzw. ohne Ende)", type: "section" },
				{ key: "startDate", label: "Beginn (Tag)", type: "date" }, { key: "startTime", label: "Beginn (Uhrzeit)", type: "time" },
				{ key: "endDate", label: "Ende (Tag)", type: "date" }, { key: "endTime", label: "Ende (Uhrzeit)", type: "time" },
				{ key: "sTarget", label: "Zielgruppe und Versand", type: "section" },
				{ key: "roleIds", label: "Nur für Rollen (leer = alle)", type: "multi", options: this.roles.map((r) => ({ key: r.id, text: r.name })) },
				{ key: "notify", label: "Mitteilung in den Apps", type: "checkbox", placeholder: "In den Apps als Mitteilung hervorheben" },
				{ key: "sendEmail", label: "Nachricht an alle Benutzer", type: "checkbox", placeholder: "Zusätzlich per E-Mail an alle aktiven Benutzer (bzw. die Zielgruppe)" },
			],
			submit: (v) => put<{ sentEmails: number }>(`system/notices/admin/announcements/${a?.id ?? uuidv7()}`, {
				title: v.title, text: v.text ?? "", severity: v.severity, startsAt: toIso(v.startDate, v.startTime), endsAt: toIso(v.endDate, v.endTime),
				roleIds: (v.roleIds as string[] | null)?.length ? v.roleIds : null, notify: !!v.notify, sendEmail: !!v.sendEmail,
			}),
		});
		if (saved) {
			this.toast(saved.sentEmails ? `Ankündigung gespeichert, ${saved.sentEmails} E-Mails eingereiht` : "Ankündigung gespeichert");
			await this.load();
		}
	}

	async onDelete(e: Event): Promise<void> {
		const a = (e.getSource() as Control).getBindingContext("view")?.getObject() as Announcement;
		if (!(await this.confirm(`„${a.title}“ löschen?`, { action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`system/notices/admin/announcements/${a.id}`); return true; })) await this.load();
	}

	private async maintenance(now: boolean): Promise<void> {
		const w = this.data?.instance.maintenance;
		const start = toLocal(now ? new Date().toISOString() : w?.startsAt ?? null);
		const end = toLocal(w?.endsAt ?? null);
		const saved = await openForm<Maintenance>({
			title: now ? "Wartung jetzt starten" : "Wartung planen", width: "34rem", danger: now,
			intro: now ? "Achtung: Mit dem Start erhalten alle Benutzer sofort die Wartungsseite; offene Eingaben gehen ggf. verloren. Vorher eine Ankündigung schalten!"
				: "Die geplante Wartung wird Benutzern vorab angekündigt (Banner) und sperrt zum Beginn die Anmeldung.",
			initial: { message: w?.message ?? "", startDate: start.date, startTime: start.time, endDate: end.date, endTime: end.time, adminsMayWork: w?.adminsMayWork ?? true },
			fields: [
				{ key: "message", label: "Nachricht an die Benutzer", type: "textarea", rows: 3, maxLength: 2000, placeholder: "z. B. Update auf die neue Version – gegen 18 Uhr wieder verfügbar." },
				...(now ? [] : [{ key: "startDate", label: "Beginn (Tag)", type: "date" as const, required: true }, { key: "startTime", label: "Beginn (Uhrzeit)", type: "time" as const, required: true }]),
				{ key: "endDate", label: "Ende (Tag)", type: "date" }, { key: "endTime", label: "Ende (Uhrzeit)", type: "time" },
				{ key: "adminsMayWork", label: "Administratoren", type: "checkbox", placeholder: "Administratoren dürfen während der Wartung weiterarbeiten" },
			],
			submitText: now ? "Wartung starten" : "Speichern",
			submit: (v) => put<Maintenance>("system/notices/admin/maintenance", {
				message: v.message || null, startsAt: now ? new Date().toISOString() : toIso(v.startDate, v.startTime),
				endsAt: toIso(v.endDate, v.endTime), adminsMayWork: !!v.adminsMayWork,
			}),
		});
		if (saved) {
			this.toast(now ? "Wartung läuft" : "Wartung geplant");
			await this.load();
		}
	}

	onPlanMaintenance(): void { void this.maintenance(false); }
	onStartMaintenance(): void { void this.maintenance(true); }

	onEndMaintenance(): void {
		MessageBox.confirm("Wartung beenden bzw. Planung verwerfen? Benutzer können sich danach sofort wieder anmelden.", {
			actions: ["Beenden", MessageBox.Action.CANCEL], emphasizedAction: "Beenden",
			onClose: async (action: string) => {
				if (action !== "Beenden") return;
				if (await this.run(async () => { await del("system/notices/admin/maintenance"); return true; })) {
					this.toast("Wartung beendet");
					await this.load();
				}
			},
		});
	}
}
