import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import Button from "sap/m/Button";
import { del, get, post, put } from "../../util/api";
import { date } from "../../util/format";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { session } from "../../util/session";
import { planningChanged } from "../../util/assignmentDialog";
import { fileUrl, uploadLoose } from "../../util/looseUpload";
import { vacationKinds, type VacationGridData } from "../../control/VacationGrid";
import type { RouteMatch } from "../../util/router";
import type { VacationSummary } from "../../util/planningModels";
import { download, navQuery } from "../../util/planningModels";

interface RequestView {
	id: string; userId: string; userName: string; from: string; to: string; kind: string; note: string | null; status: string; workingDays: number;
	decisionComment: string | null; warnings: string[]; remainingAfter: number | null; canDecide: boolean; canCancel: boolean;
}
interface PlanRow { employeeId: string | null; userId: string; name: string; crews: { name: string }[]; entries: VacationGridData["rows"][number]["entries"]; vacationRemaining: number | null }
interface Plan { from: string; to: string; state: string; holidays: { date: string; name: string }[]; rows: PlanRow[] }
interface Reminder { kind: string; employeeId: string; employeeName: string; date: string; text: string; severity: string }
interface SickNote { id: string; userId: string; userName: string; from: string; to: string; workingDays: number; note: string | null; certificateFileId: string | null }
interface Settings { state: string; stateName: string; states: { key: string; name: string }[]; maxCrewAbsencePercent: number; remindBirthdays: boolean; remindAnniversaries: boolean }

const tabs = ["mein", "antraege", "plan", "krank", "fristen"];
const statusTexts: Record<string, { text: string; state: string }> = {
	pending: { text: "Beantragt", state: "Warning" }, approved: { text: "Genehmigt", state: "Success" }, rejected: { text: "Abgelehnt", state: "Error" },
	cancelled: { text: "Zurückgezogen", state: "None" },
};
const requestKinds = [
	{ key: "vacation", text: "Urlaub" }, { key: "specialLeave", text: "Sonderurlaub" }, { key: "school", text: "Berufsschule" },
	{ key: "training", text: "Schulung" }, { key: "other", text: "Sonstige Abwesenheit" },
];
const reminderIcons: Record<string, string> = {
	qualification: "sap-icon://certificate", probation: "sap-icon://employee-approvals", birthday: "sap-icon://lead", anniversary: "sap-icon://competitor",
	document: "sap-icon://document",
};
const num = (v: number | null | undefined) => (v == null ? "–" : v.toLocaleString("de-DE", { maximumFractionDigits: 1 }));
const pad = (n: number) => String(n).padStart(2, "0");

/**
 * Personal › Urlaub und Abwesenheiten (C7): Mein Urlaub (Resturlaub, Anträge), Genehmigung mit Konflikthinweisen,
 * Urlaubsplan (Monat, Feiertage je Bundesland, PDF), Krankmeldungen mit AU (nur Personalverwaltung), Fristen.
 * @namespace estrichone.controller.staff
 */
export default class Vacation extends BaseController {
	private month = new Date();

	onInit(): void {
		this.local({ tab: "mein", busy: false, subtitle: "", me: {}, meText: {}, myRequests: [], pending: [], grid: null, monthTitle: "", stateText: "",
			holidayText: "", sickNotes: [], reminders: [], can: {}, filter: { crew: "", kind: "", status: "" }, crewOptions: [],
			kpi: { remaining: "–", pending: 0, away: 0, awayText: "" } });
	}

	onRouteMatched(match: RouteMatch): void {
		const m = this.model();
		const can = { approve: this.has("absence.approve"), hrRead: this.has("hr.read"), hrManage: this.has("hr.manage") };
		m.setProperty("/can", can);
		const tab = match.query.get("reiter") ?? "";
		const allowed = (key: string) => key === "mein" || (key === "antraege" && can.approve) || ((key === "plan" || key === "fristen") && can.hrRead)
			|| (key === "krank" && can.hrManage);
		m.setProperty("/tab", tabs.includes(tab) && allowed(tab) ? tab : can.approve ? "antraege" : "mein");
		const monat = match.query.get("monat");
		if (monat && /^\d{4}-\d{2}$/.test(monat)) this.month = new Date(Number(monat.slice(0, 4)), Number(monat.slice(5)) - 1, 1);
		void this.load();
	}

	onTab(e: Event): void {
		this.setQuery({ reiter: String(e.getParameter("key" as never)) });
	}

	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		const can = m.getProperty("/can") as { approve: boolean; hrRead: boolean; hrManage: boolean };
		m.setProperty("/busy", true);
		await Promise.allSettled([
			this.loadMine(),
			can.approve ? this.loadPending() : Promise.resolve(),
			can.hrRead ? this.loadPlan() : Promise.resolve(),
			can.hrRead ? get<Reminder[]>("hr/reminders", { days: 60 }).then((r) => m.setProperty("/reminders", r.map((x) => ({ ...x, icon: reminderIcons[x.kind] ?? "sap-icon://alarm" })))) : Promise.resolve(),
			can.hrRead ? this.loadToday() : Promise.resolve(),
			can.hrManage ? get<SickNote[]>("hr/sick-notes").then((s) => m.setProperty("/sickNotes", s.map((x) => ({ ...x, rangeText: `${date(x.from)} – ${date(x.to)}` })))) : Promise.resolve(),
		]);
		m.setProperty("/busy", false);
		m.setProperty("/kpi/pending", (m.getProperty("/pending") as unknown[]).length);
	}

	/** Kennzahl „heute abwesend“ (genehmigte Abwesenheiten, Krankheit nur mit Recht sichtbar). */
	private async loadToday(): Promise<void> {
		const now = new Date();
		const today = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
		const plan = await get<Plan>("hr/vacation-plan", { from: today, to: today });
		const away = plan.rows.filter((r) => r.entries.some((e) => e.status === "approved" && e.from <= today && today <= e.to));
		this.model().setProperty("/kpi/away", away.length);
		this.model().setProperty("/kpi/awayText", away.map((r) => r.name).join(", "));
	}

	private requestRows(list: RequestView[]) {
		return list.map((r) => ({
			...r, rangeText: r.from === r.to ? date(r.from) : `${date(r.from)} – ${date(r.to)}`, kindText: vacationKinds[r.kind]?.text ?? r.kind,
			statusText: statusTexts[r.status]?.text ?? r.status, statusState: statusTexts[r.status]?.state ?? "None", workingDays: num(r.workingDays),
			remainingAfter: r.remainingAfter == null ? null : num(r.remainingAfter),
		}));
	}

	private async loadMine(): Promise<void> {
		const me = await this.run(() => get<{ employeeId: string | null; vacation: VacationSummary | null; requests: RequestView[] }>("hr/me"));
		if (!me) return;
		const m = this.model();
		m.setProperty("/me", me);
		const v = me.vacation;
		m.setProperty("/meText", v ? { entitlement: num(v.entitlement), carryOver: num(v.carryOver), taken: num(v.taken), planned: num(v.planned),
			requested: num((v as VacationSummary & { requested?: number }).requested ?? 0), remaining: num(v.remaining) } : {});
		m.setProperty("/myRequests", this.requestRows(me.requests));
		m.setProperty("/subtitle", v ? `Resturlaub ${v.year}: ${num(v.remaining)} Tage` : "Urlaub beantragen und Anträge verfolgen");
		m.setProperty("/kpi/remaining", v ? num(v.remaining) : "–");
	}

	private async loadPending(): Promise<void> {
		const list = await get<RequestView[]>("hr/absence-requests", { scope: "pending" });
		this.model().setProperty("/pending", this.requestRows(list.filter((r) => r.userId !== session.userId || r.canDecide)));
	}

	private monthRange(): { from: string; to: string } {
		const y = this.month.getFullYear();
		const mo = this.month.getMonth();
		const last = new Date(y, mo + 1, 0).getDate();
		return { from: `${y}-${pad(mo + 1)}-01`, to: `${y}-${pad(mo + 1)}-${pad(last)}` };
	}

	private async loadPlan(): Promise<void> {
		const m = this.model();
		const { from, to } = this.monthRange();
		const plan = await get<Plan>("hr/vacation-plan", { from, to });
		const days: string[] = [];
		for (let d = new Date(`${from}T00:00:00`); d <= new Date(`${to}T00:00:00`); d.setDate(d.getDate() + 1)) {
			days.push(`${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`);
		}
		const now = new Date();
		m.setProperty("/grid", {
			days, today: `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`,
			holidays: Object.fromEntries(plan.holidays.map((h) => [h.date, h.name])),
			rows: [],
		} satisfies VacationGridData);
		this.plan = plan;
		const crews = [...new Set(plan.rows.flatMap((r) => r.crews.map((c) => c.name)))].sort((a, b) => a.localeCompare(b, "de"));
		m.setProperty("/crewOptions", [{ key: "", text: "Alle Kolonnen" }, ...crews.map((c) => ({ key: c, text: c })), { key: "-", text: "Ohne Kolonne" }]);
		this.renderPlan();
		m.setProperty("/monthTitle", this.month.toLocaleDateString("de-DE", { month: "long", year: "numeric" }));
		m.setProperty("/holidayText", plan.holidays.length ? `Feiertage: ${plan.holidays.map((h) => `${date(h.date)} ${h.name}`).join(" · ")}` : "Keine Feiertage in diesem Monat.");
		const settings = await get<Settings>("hr/settings").catch(() => null);
		m.setProperty("/stateText", settings ? `Feiertage: ${settings.stateName}` : "");
	}

	private plan: Plan | null = null;

	/** Urlaubsplan filtern (Kolonne, Art, Status) und nach Kolonne gruppieren. */
	private renderPlan(): void {
		const m = this.model();
		const plan = this.plan;
		if (!plan) return;
		const f = m.getProperty("/filter") as { crew: string; kind: string; status: string };
		const groupOf = (r: PlanRow) => r.crews[0]?.name ?? "Ohne Kolonne";
		const rows = plan.rows
			.filter((r) => !f.crew || (f.crew === "-" ? r.crews.length === 0 : r.crews.some((c) => c.name === f.crew)))
			.map((r) => ({
				userId: r.userId, employeeId: r.employeeId, name: r.name, group: groupOf(r),
				sub: r.crews.length > 1 ? `auch ${r.crews.slice(1).map((c) => c.name).join(", ")}` : "",
				remaining: r.vacationRemaining,
				entries: r.entries.filter((e) => (!f.kind || e.kind === f.kind) && (!f.status || e.status === f.status)),
			}))
			.filter((r) => !(f.kind || f.status) || r.entries.length > 0)
			.sort((a, b) => (a.group === "Ohne Kolonne" ? 1 : 0) - (b.group === "Ohne Kolonne" ? 1 : 0) || a.group.localeCompare(b.group, "de") || a.name.localeCompare(b.name, "de"));
		m.setProperty("/grid/rows", rows);
		m.setProperty("/grid", { ...(m.getProperty("/grid") as VacationGridData) });
	}

	onPlanFilter(): void { this.renderPlan(); }

	onKpiPending(): void { navQuery({ reiter: "antraege" }); }

	private shiftMonth(delta: number): void {
		this.month = new Date(this.month.getFullYear(), this.month.getMonth() + delta, 1);
		navQuery({ reiter: "plan", monat: `${this.month.getFullYear()}-${pad(this.month.getMonth() + 1)}` });
	}

	onPrevMonth(): void { this.shiftMonth(-1); }
	onNextMonth(): void { this.shiftMonth(1); }
	onThisMonth(): void {
		this.month = new Date();
		navQuery({ reiter: "plan", monat: null });
	}

	onPdfMonth(): void { download(`/api/v1/hr/vacation-plan.pdf?year=${this.month.getFullYear()}&month=${this.month.getMonth() + 1}`).catch((error: unknown) => this.showError(error)); }
	onPdfYear(): void { download(`/api/v1/hr/vacation-plan.pdf?year=${this.month.getFullYear()}`).catch((error: unknown) => this.showError(error)); }

	onCell(e: Event): void {
		if (!this.has("absence.approve")) return;
		const day = String(e.getParameter("day" as never));
		void this.request(String(e.getParameter("userId" as never)), day);
	}

	// ---- Anträge ---------------------------------------------------------------------------------------------------

	onRequest(): void { void this.request(null); }
	onRequestFor(): void { void this.request(""); }

	/** userId null = für mich; "" = Mitarbeiter wählen. */
	private async request(userId: string | null, day?: string): Promise<void> {
		const forOthers = userId !== null;
		const people = forOthers ? (await session.people()).filter((p) => p.isActive) : [];
		const saved = await openForm<RequestView>({
			title: forOthers ? "Abwesenheit für Mitarbeiter beantragen" : "Urlaub beantragen", width: "32rem",
			intro: forOthers ? "Der Antrag erscheint unter „Anträge“ und wird nach der Genehmigung in die Planung übernommen." : "Ihr Antrag geht an das Büro zur Genehmigung.",
			initial: { userId: userId || null, kind: "vacation", from: day ?? null, to: day ?? null },
			fields: [
				...(forOthers ? [{ key: "userId", label: "Mitarbeiter", type: "select" as const, required: true, options: people.map((p) => ({ key: p.id, text: p.displayName })) }] : []),
				{ key: "kind", label: "Art", type: "select", required: true, options: requestKinds },
				{ key: "from", label: "Von", type: "date", required: true },
				{ key: "to", label: "Bis einschließlich", type: "date", required: true,
					validate: (v, all) => (v && all.from && String(v) < String(all.from) ? "Das Ende liegt vor dem Beginn." : null) },
				{ key: "note", label: "Bemerkung", maxLength: 500, placeholder: "z. B. Familienurlaub, Umzug" },
			],
			submitText: "Beantragen",
			submit: (v) => put<RequestView>(`hr/absence-requests/${uuidv7()}`, {
				userId: forOthers ? v.userId : null, kind: v.kind, from: v.from, to: v.to, note: v.note || null,
			}),
		});
		if (!saved) return;
		const days = num(saved.workingDays);
		if (saved.warnings.length) {
			MessageBox.warning(`Antrag gestellt (${days} Arbeitstage). Hinweise:\n\n${saved.warnings.join("\n")}`, { title: "Antrag gestellt" });
		} else {
			this.toast(`Antrag gestellt (${days} Arbeitstage)`);
		}
		await this.load();
	}

	private row<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	private async decide(r: RequestView, approve: boolean): Promise<void> {
		const result = await openForm<RequestView>({
			title: approve ? `${r.userName}: genehmigen` : `${r.userName}: ablehnen`, width: "30rem",
			intro: `${vacationKinds[r.kind]?.text ?? r.kind} ${r.from === r.to ? date(r.from) : `${date(r.from)} – ${date(r.to)}`} (${num(r.workingDays)} Arbeitstage)`
				+ (r.warnings.length ? `\nHinweise: ${r.warnings.join(" ")}` : ""),
			fields: [{ key: "comment", label: "Kommentar", type: "textarea", rows: 3, maxLength: 1000, required: !approve,
				placeholder: approve ? "optional, z. B. Gute Erholung!" : "Begründung für den Mitarbeiter" }],
			submitText: approve ? "Genehmigen" : "Ablehnen", danger: !approve,
			submit: (v) => post<RequestView>(`hr/absence-requests/${r.id}/decision`, { approve, comment: v.comment || null }),
		});
		if (result) {
			this.toast(approve ? "Genehmigt – in Planung und Urlaubsplan übernommen" : "Abgelehnt");
			planningChanged();
			await this.load();
		}
	}

	/** Genehmigen mit einem Klick; nur bei Hinweisen (Überschneidung, Kolonne dünn besetzt …) erst bestätigen. */
	async onApprove(e: Event): Promise<void> {
		const r = this.row<RequestView>(e);
		if (r.warnings.length) {
			await this.decide(r, true);
			return;
		}
		if (await this.run(() => post<RequestView>(`hr/absence-requests/${r.id}/decision`, { approve: true, comment: null }))) {
			this.toast(`Genehmigt: ${r.userName} – in Planung und Urlaubsplan übernommen`);
			planningChanged();
			await this.load();
		}
	}
	onReject(e: Event): void { void this.decide(this.row<RequestView>(e), false); }

	async onCancel(e: Event): Promise<void> {
		const r = this.row<RequestView>(e);
		if (!(await this.confirm("Antrag zurückziehen? Genehmigter Urlaub wird wieder freigegeben.", { action: "Zurückziehen", danger: true }))) return;
		if (await this.run(() => post(`hr/absence-requests/${r.id}/cancel`))) {
			this.toast("Antrag zurückgezogen");
			planningChanged();
			await this.load();
		}
	}

	// ---- Krankmeldungen --------------------------------------------------------------------------------------------

	async onSickNote(): Promise<void> {
		const people = (await session.people()).filter((p) => p.isActive);
		let file: File | null = null;
		const saved = await openForm<SickNote>({
			title: "Krankmeldung erfassen", width: "32rem", intro: "Vertraulich – nur für die Personalverwaltung sichtbar.",
			fields: [
				{ key: "userId", label: "Mitarbeiter", type: "select", required: true, options: people.map((p) => ({ key: p.id, text: p.displayName })) },
				{ key: "from", label: "Von", type: "date", required: true },
				{ key: "to", label: "Bis einschließlich", type: "date", required: true,
					validate: (v, all) => (v && all.from && String(v) < String(all.from) ? "Das Ende liegt vor dem Beginn." : null) },
				{ key: "note", label: "Notiz", maxLength: 500 },
			],
			extra: () => {
				const button = new Button({
					text: "AU-Bescheinigung anhängen", icon: "sap-icon://attachment",
					press: () => {
						const input = document.createElement("input");
						input.type = "file";
						input.accept = ".pdf,.jpg,.jpeg,.png,.heic";
						input.addEventListener("change", () => {
							file = input.files?.[0] ?? null;
							button.setText(file ? `AU: ${file.name}` : "AU-Bescheinigung anhängen");
						});
						input.click();
					},
				});
				return [button];
			},
			submit: async (v) => {
				const certificateFileId = file ? await uploadLoose(file) : null;
				return put<SickNote>(`hr/sick-notes/${uuidv7()}`, { userId: v.userId, from: v.from, to: v.to, note: v.note || null, certificateFileId });
			},
		});
		if (saved) {
			this.toast("Krankmeldung erfasst");
			planningChanged();
			await this.load();
		}
	}

	onCertificate(e: Event): void {
		const s = this.row<SickNote>(e);
		if (s.certificateFileId) window.open(fileUrl(s.certificateFileId), "_blank", "noopener");
	}

	async onDeleteSick(e: Event): Promise<void> {
		const s = this.row<SickNote>(e);
		if (!(await this.confirm("Krankmeldung löschen?", { action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`hr/sick-notes/${s.id}`); return true; })) {
			planningChanged();
			await this.load();
		}
	}

	onReminder(e: Event): void {
		const r = this.row<Reminder>(e);
		this.nav(`/personal/${r.employeeId}${r.kind === "qualification" ? "?abschnitt=qualifikationen" : ""}`);
	}

	// ---- Einstellungen ---------------------------------------------------------------------------------------------

	async onSettings(): Promise<void> {
		const s = await this.run(() => get<Settings>("hr/settings"));
		if (!s) return;
		const saved = await openForm<Settings>({
			title: "Einstellungen Personal", width: "32rem", initial: { ...s },
			fields: [
				{ key: "state", label: "Bundesland (Feiertage)", type: "select", required: true, options: s.states.map((x) => ({ key: x.key, text: x.name })) },
				{ key: "maxCrewAbsencePercent", label: "Hinweis ab Anteil gleichzeitig abwesend je Kolonne", type: "integer", required: true, min: 0, max: 100, unit: "%" },
				{ key: "remindBirthdays", label: "Geburtstage in den Fristen", type: "switch" },
				{ key: "remindAnniversaries", label: "Betriebsjubiläen in den Fristen", type: "switch" },
			],
			submit: (v) => put<Settings>("hr/settings", v),
		});
		if (saved) {
			this.toast("Einstellungen gespeichert");
			await this.load();
		}
	}
}
