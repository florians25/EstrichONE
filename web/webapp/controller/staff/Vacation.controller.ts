import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import Button from "sap/m/Button";
import type Text from "sap/m/Text";
import { del, get, post, put } from "../../util/api";
import { date, dateTime } from "../../util/format";
import Dialog from "sap/m/Dialog";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import TextControl from "sap/m/Text";
import { openForm, type Field, type FormHandle } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { session } from "../../util/session";
import { planningChanged } from "../../util/assignmentDialog";
import { fileUrl, uploadLoose } from "../../util/looseUpload";
import type { VacationGridData } from "../../control/VacationGrid";
import type { RouteMatch } from "../../util/router";
import { download, navQuery } from "../../util/planningModels";
import {
	type AbsenceTypeView, type AccountRow, type ClosureView, type MyVacation, type Plan, type PlanRow, type Preview, type RequestView, type Settings,
	accountTexts, colorDot, escapeHtml, num, range, requestRow, typeColors, typeRules,
} from "../../util/absences";

interface Reminder { kind: string; employeeId: string; employeeName: string; date: string; text: string; severity: string }
interface SickNote {
	id: string; userId: string; userName: string; from: string; to: string; workingDays: number; note: string | null; certificateFileId: string | null;
	caseId?: string | null; isFollowUp?: boolean; recordedAt?: string | null; typeId?: string | null;
}
const isoNext = (d: string) => { const x = new Date(`${d}T00:00:00`); x.setDate(x.getDate() + 1); return iso(x); };
const pad = (n: number) => String(n).padStart(2, "0");
const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

const tabs = ["mein", "antraege", "plan", "konten", "krank", "fristen", "verwaltung"];
const reminderIcons: Record<string, string> = {
	qualification: "sap-icon://lateness", probation: "sap-icon://employee-approvals", birthday: "sap-icon://lead", anniversary: "sap-icon://competitor",
	document: "sap-icon://document",
};
const months = ["Kein Verfall", "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];
const kindOptions = [
	{ key: "vacation", text: "Urlaub" }, { key: "specialLeave", text: "Sonderurlaub" }, { key: "school", text: "Berufsschule" },
	{ key: "training", text: "Fortbildung/Schulung" }, { key: "other", text: "Sonstige Abwesenheit" }, { key: "sick", text: "Krankheit (vertraulich)" },
];

/**
 * Personal › Urlaub und Abwesenheiten: eigene Übersicht mit Urlaubskonto, Anträge mit Genehmigung und Konflikthinweisen,
 * Teamkalender (Monat/Jahr, Farben je Art, Feiertage je Bundesland, Betriebsferien, PDF), Urlaubskonten (CSV/PDF),
 * Krankmeldungen mit AU (nur Personalverwaltung), Fristen, Abwesenheitsarten und Betriebsferien.
 * @namespace estrichone.controller.staff
 */
export default class Vacation extends BaseController {
	private month = new Date();
	private year = new Date().getFullYear();
	private accountsYear = new Date().getFullYear();
	private plan: Plan | null = null;
	private types: AbsenceTypeView[] = [];
	private accountRows: AccountRow[] = [];

	onInit(): void {
		this.local({ tab: "mein", busy: false, subtitle: "", year: this.year, me: {}, meText: {}, myRequests: [], myAbsences: [], myInfoText: "", pending: [],
			grid: null, monthTitle: "", stateText: "", holidayText: "", closureText: "", legendHtml: "", planMode: "month", sickNotes: [], reminders: [], can: {},
			filter: { crew: "", type: "", status: "" }, crewOptions: [], typeOptions: [], accounts: [], accountsYear: this.accountsYear, typeRows: [], closures: [],
			kpi: { remaining: "–", pending: 0, away: 0, awayText: "", expiring: "", expiresOn: "" } });
	}

	onRouteMatched(match: RouteMatch): void {
		const m = this.model();
		const approveAll = this.has("absence.approve");
		const can = { approve: approveAll || !!(m.getProperty("/can/approve") as boolean | undefined), approveAll, hrRead: this.has("hr.read"), hrManage: this.has("hr.manage") };
		m.setProperty("/can", can);
		const tab = match.query.get("reiter") ?? "";
		const monat = match.query.get("monat");
		if (monat && /^\d{4}-\d{2}$/.test(monat)) this.month = new Date(Number(monat.slice(0, 4)), Number(monat.slice(5)) - 1, 1);
		const jahr = match.query.get("jahr");
		if (jahr && /^\d{4}$/.test(jahr)) { this.year = Number(jahr); this.accountsYear = Number(jahr); }
		m.setProperty("/planMode", match.query.get("ansicht") === "jahr" ? "year" : "month");
		m.setProperty("/year", this.year);
		m.setProperty("/accountsYear", this.accountsYear);
		this.requestedTab = tab;
		this.applyTab();
		void this.load();
	}

	private requestedTab = "";

	private allowed(key: string): boolean {
		const can = this.model().getProperty("/can") as { approve: boolean; hrRead: boolean; hrManage: boolean };
		return key === "mein" || (key === "antraege" && can.approve) || ((key === "plan" || key === "fristen" || key === "konten") && can.hrRead)
			|| ((key === "krank" || key === "verwaltung") && can.hrManage);
	}

	private applyTab(): void {
		const can = this.model().getProperty("/can") as { approve: boolean };
		const tab = this.requestedTab;
		this.model().setProperty("/tab", tabs.includes(tab) && this.allowed(tab) ? tab : can.approve && !tab ? "antraege" : "mein");
	}

	onTab(e: Event): void {
		this.setQuery({ reiter: String(e.getParameter("key" as never)) });
	}

	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		this.types = await get<AbsenceTypeView[]>("hr/absence-types", { all: true }).catch(() => []);
		await this.loadMine();
		const can = m.getProperty("/can") as { approve: boolean; hrRead: boolean; hrManage: boolean };
		this.applyTab();
		await Promise.allSettled([
			can.approve ? this.loadPending() : Promise.resolve(),
			can.hrRead ? this.loadPlan() : Promise.resolve(),
			can.hrRead ? this.loadAccounts() : Promise.resolve(),
			can.hrRead ? get<Reminder[]>("hr/reminders", { days: 60 }).then((r) => m.setProperty("/reminders", r.map((x) => ({ ...x, icon: reminderIcons[x.kind] ?? "sap-icon://lateness" })))) : Promise.resolve(),
			can.hrRead ? this.loadToday() : Promise.resolve(),
			can.hrManage ? this.loadSickNotes() : Promise.resolve(),
			can.hrManage ? this.loadAdmin() : Promise.resolve(),
		]);
		m.setProperty("/busy", false);
		m.setProperty("/kpi/pending", (m.getProperty("/pending") as unknown[]).length);
	}

	private typeById(id: string | null | undefined): AbsenceTypeView | undefined {
		return this.types.find((t) => t.id === id);
	}

	private async loadSickNotes(): Promise<void> {
		const s = await get<SickNote[]>("hr/sick-notes", { year: this.accountsYear });
		// Nach Krankheitsfall gruppiert (Erstmeldung + Folgebescheinigungen).
		const first = new Map<string, SickNote>();
		for (const x of [...s].sort((a, b) => a.from.localeCompare(b.from))) if (!first.has(x.caseId ?? x.id)) first.set(x.caseId ?? x.id, x);
		this.model().setProperty("/sickNotes", s.map((x) => {
			const f = first.get(x.caseId ?? x.id) ?? x;
			return { ...x, rangeText: `${date(x.from)} – ${date(x.to)}`, kindText: x.isFollowUp ? "Folgebescheinigung" : "Erstmeldung",
				typeName: this.typeById(x.typeId)?.name ?? "Krankheit", caseLabel: `${f.userName} · seit ${date(f.from)}` };
		}));
	}

	/** Kennzahl „heute abwesend“ (genehmigte Abwesenheiten, Krankheit nur mit Recht sichtbar). */
	private async loadToday(): Promise<void> {
		const today = iso(new Date());
		const plan = await get<Plan>("hr/vacation-plan", { from: today, to: today });
		const away = plan.rows.filter((r) => r.entries.some((e) => e.status === "approved" && e.from <= today && today <= e.to));
		this.model().setProperty("/kpi/away", away.length);
		this.model().setProperty("/kpi/awayText", away.map((r) => r.name).join(", "));
	}

	private async loadMine(): Promise<void> {
		const me = await this.run(() => get<MyVacation>("hr/me", { year: this.year }));
		if (!me) return;
		const m = this.model();
		m.setProperty("/me", me);
		if (me.canApprove && !(m.getProperty("/can/approve") as boolean)) m.setProperty("/can/approve", true);
		const v = me.vacation;
		const texts = v ? accountTexts(v) : null;
		m.setProperty("/meText", texts ?? {});
		m.setProperty("/myRequests", me.requests.map((r) => requestRow(r, true)));
		m.setProperty("/myAbsences", (me.absences ?? []).map((a) => ({ ...a, rangeText: range(a.from, a.to, a.halfDayStart, a.halfDayEnd), dot: colorDot(a.color),
			workingDaysText: num(a.workingDays) })));
		const closures = (me.closures ?? []).map((c) => `${c.name} ${range(c.from, c.to)}`);
		m.setProperty("/myInfoText", [closures.length ? `Betriebsferien: ${closures.join(" · ")}` : null,
			`Feiertage ${me.year}: ${me.holidays.map((h) => `${date(h.date)} ${h.name}`).join(" · ")}`].filter(Boolean).join("\n"));
		m.setProperty("/subtitle", v ? `Resturlaub ${v.year}: ${num(v.remaining)} Tage` : "Urlaub beantragen und Anträge verfolgen");
		m.setProperty("/kpi/remaining", v ? num(v.remaining) : "–");
		m.setProperty("/kpi/expiring", v && v.carryOverExpiring > 0 ? `${num(v.carryOverExpiring)} Tage` : "");
		m.setProperty("/kpi/expiresOn", v?.carryOverExpiresOn ? date(v.carryOverExpiresOn) : "");
	}

	private async loadPending(): Promise<void> {
		const list = await get<RequestView[]>("hr/absence-requests", { scope: "pending" });
		this.model().setProperty("/pending", list.filter((r) => r.userId !== session.userId || r.canDecide).map((r) => requestRow(r, false)));
	}

	// ---- Teamkalender ----------------------------------------------------------------------------------------------

	private planRange(): { from: string; to: string } {
		const y = this.month.getFullYear();
		if (this.model().getProperty("/planMode") === "year") return { from: `${y}-01-01`, to: `${y}-12-31` };
		const mo = this.month.getMonth();
		const last = new Date(y, mo + 1, 0).getDate();
		return { from: `${y}-${pad(mo + 1)}-01`, to: `${y}-${pad(mo + 1)}-${pad(last)}` };
	}

	private async loadPlan(): Promise<void> {
		const m = this.model();
		const { from, to } = this.planRange();
		const plan = await get<Plan>("hr/vacation-plan", { from, to });
		const days: string[] = [];
		for (let d = new Date(`${from}T00:00:00`); d <= new Date(`${to}T00:00:00`); d.setDate(d.getDate() + 1)) days.push(iso(d));
		const closures: Record<string, string> = {};
		for (const c of plan.closures ?? []) for (const d of days) if (c.from <= d && d <= c.to) closures[d] = c.name;
		const types = plan.types ?? this.types;
		m.setProperty("/grid", {
			days, today: iso(new Date()), holidays: Object.fromEntries(plan.holidays.map((h) => [h.date, h.name])), rows: [], closures,
			types: Object.fromEntries(types.map((t) => [t.id, { code: t.code, name: t.name, color: t.color }])), compact: m.getProperty("/planMode") === "year",
		} satisfies VacationGridData);
		this.plan = plan;
		const crews = [...new Set(plan.rows.flatMap((r) => r.crews.map((c) => c.name)))].sort((a, b) => a.localeCompare(b, "de"));
		m.setProperty("/crewOptions", [{ key: "", text: "Alle Kolonnen" }, ...crews.map((c) => ({ key: c, text: c })), { key: "-", text: "Ohne Kolonne" }]);
		const used = new Set(plan.rows.flatMap((r) => r.entries.map((e) => e.typeId)));
		const shown = types.filter((t) => t.isActive || used.has(t.id)).filter((t) => !t.isSickness || used.has(t.id));
		m.setProperty("/typeOptions", [{ key: "", text: "Alle Arten" }, ...shown.map((t) => ({ key: t.id, text: t.name }))]);
		m.setProperty("/legendHtml", `<div class="eoVacSwatches" style="display:flex;flex-wrap:wrap">${shown.map((t) =>
			`<span class="eoVacSwatch"><i style="background:${t.color}80"></i>${escapeHtml(t.code)} ${escapeHtml(t.name)}</span>`).join("")}`
			+ `<span class="eoVacSwatch"><i class="eoVacPending"></i>beantragt</span><span class="eoVacSwatch"><i class="eoVacHoliday"></i>Wochenende, Feiertag</span>`
			+ `<span class="eoVacSwatch">½ halber Tag</span></div>`);
		this.renderPlan();
		m.setProperty("/monthTitle", m.getProperty("/planMode") === "year" ? String(this.month.getFullYear())
			: this.month.toLocaleDateString("de-DE", { month: "long", year: "numeric" }));
		m.setProperty("/holidayText", plan.holidays.length ? `Feiertage: ${plan.holidays.map((h) => `${date(h.date)} ${h.name}`).join(" · ")}` : "Keine Feiertage im Zeitraum.");
		m.setProperty("/closureText", (plan.closures ?? []).length ? `Betriebsferien: ${(plan.closures ?? []).map((c) => `${c.name} ${range(c.from, c.to)}`).join(" · ")}` : "");
		const minimums = (plan.crewMinimums ?? []).map((c) => `${c.name} mindestens ${c.minimumStaff} von ${c.members}`);
		const settings = await get<Settings>("hr/settings").catch(() => null);
		m.setProperty("/stateText", [settings ? `Feiertage: ${settings.stateName}` : null, minimums.length ? `Mindestbesetzung: ${minimums.join(", ")}` : null].filter(Boolean).join(" · "));
	}

	/** Teamkalender filtern (Kolonne, Art, Status) und nach Kolonne gruppieren. */
	private renderPlan(): void {
		const m = this.model();
		const plan = this.plan;
		if (!plan) return;
		const f = m.getProperty("/filter") as { crew: string; type: string; status: string };
		const groupOf = (r: PlanRow) => r.crews[0]?.name ?? "Ohne Kolonne";
		const rows = plan.rows
			.filter((r) => !f.crew || (f.crew === "-" ? r.crews.length === 0 : r.crews.some((c) => c.name === f.crew)))
			.map((r) => ({
				userId: r.userId, employeeId: r.employeeId, name: r.name, group: groupOf(r),
				sub: r.crews.length > 1 ? `auch ${r.crews.slice(1).map((c) => c.name).join(", ")}` : "",
				remaining: r.vacationRemaining,
				entries: r.entries.filter((e) => (!f.type || e.typeId === f.type) && (!f.status || e.status === f.status)),
			}))
			.filter((r) => !(f.type || f.status) || r.entries.length > 0)
			.sort((a, b) => (a.group === "Ohne Kolonne" ? 1 : 0) - (b.group === "Ohne Kolonne" ? 1 : 0) || a.group.localeCompare(b.group, "de") || a.name.localeCompare(b.name, "de"));
		m.setProperty("/grid", { ...(m.getProperty("/grid") as VacationGridData), rows });
	}

	onPlanFilter(): void { this.renderPlan(); }

	onPlanMode(e: Event): void {
		const item = e.getParameter("item" as never) as { getKey(): string } | undefined;
		const mode = item?.getKey() ?? (this.model().getProperty("/planMode") as string);
		this.model().setProperty("/planMode", mode);
		navQuery({ reiter: "plan", ansicht: mode === "year" ? "jahr" : null,
			monat: `${this.month.getFullYear()}-${pad(this.month.getMonth() + 1)}` });
	}

	onKpiPending(): void { navQuery({ reiter: "antraege" }); }

	private shiftMonth(delta: number): void {
		const year = this.model().getProperty("/planMode") === "year";
		this.month = new Date(this.month.getFullYear() + (year ? delta : 0), this.month.getMonth() + (year ? 0 : delta), 1);
		navQuery({ reiter: "plan", monat: `${this.month.getFullYear()}-${pad(this.month.getMonth() + 1)}`, ansicht: year ? "jahr" : null });
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
		void this.request({ userId: String(e.getParameter("userId" as never)), day });
	}

	// ---- Eigene Übersicht ------------------------------------------------------------------------------------------

	onPrevYear(): void { navQuery({ reiter: "mein", jahr: String(this.year - 1) }); }
	onNextYear(): void { navQuery({ reiter: "mein", jahr: String(this.year + 1) }); }

	// ---- Urlaubskonten ---------------------------------------------------------------------------------------------

	private async loadAccounts(): Promise<void> {
		this.accountRows = await get<AccountRow[]>("hr/vacation-accounts", { year: this.accountsYear });
		this.renderAccounts("");
	}

	private renderAccounts(search: string): void {
		const q = search.trim().toLowerCase();
		this.model().setProperty("/accounts", this.accountRows.filter((r) => !q || r.name.toLowerCase().includes(q)).map((r) => {
			const t = accountTexts(r.vacation);
			return {
				...r, ...t, sub: [r.personnelNumber, r.crews.map((c) => c.name).join(", "), r.entryDate?.startsWith(String(this.accountsYear)) ? `Eintritt ${date(r.entryDate)}` : null,
					r.exitDate?.startsWith(String(this.accountsYear)) ? `Austritt ${date(r.exitDate)}` : null].filter(Boolean).join(" · "),
				byTypeText: (r.vacation.byType ?? []).filter((x) => x.typeId !== this.types.find((y) => y.key === "vacation")?.id).map((x) => `${x.name} ${num(x.days)}`).join(", "),
			};
		}));
	}

	onAccountsSearch(e: Event): void { this.renderAccounts(String(e.getParameter("newValue" as never) ?? "")); }

	private shiftAccounts(delta: number): void { navQuery({ reiter: "konten", jahr: String(this.accountsYear + delta) }); }
	onAccountsPrev(): void { this.shiftAccounts(-1); }
	onAccountsNext(): void { this.shiftAccounts(1); }
	onAccountsCsv(): void { download(`/api/v1/hr/vacation-accounts.csv?year=${this.accountsYear}`).catch((error: unknown) => this.showError(error)); }
	onAccountsPdf(): void { download(`/api/v1/hr/vacation-accounts.pdf?year=${this.accountsYear}`).catch((error: unknown) => this.showError(error)); }

	async onAccountEdit(e: Event): Promise<void> {
		if (!this.has("hr.manage")) return;
		const r = this.row<AccountRow>(e);
		const a = r.account;
		const v = r.vacation;
		const saved = await openForm<AccountRow>({
			title: `Urlaubskonto ${this.accountsYear}: ${r.name}`, width: "34rem",
			intro: `Leere Felder werden berechnet: Anspruch laut Personalstamm ${num(v.annualEntitlement)} Tage (anteilig bei Ein- oder Austritt), Übertrag aus dem Rest des Vorjahres.`,
			initial: { entitlement: a?.entitlement ?? null, carryOver: a?.carryOver ?? null, carryOverExpiresOn: a?.carryOverExpiresOn ?? null, adjustment: a?.adjustment ?? 0, note: a?.note ?? "" },
			fields: [
				{ key: "entitlement", label: "Abweichender Anspruch", type: "number", unit: "Tage", min: 0, max: 100, placeholder: `berechnet: ${num(a?.entitlement == null ? v.entitlement : null)}` },
				{ key: "carryOver", label: "Übertrag aus Vorjahr", type: "number", unit: "Tage", placeholder: `berechnet: ${num(a?.carryOver == null ? v.carryOver : null)}` },
				{ key: "carryOverExpiresOn", label: "Übertrag verfällt nach", type: "date", help: "Leer: Einstellung des Betriebs. Zum Beispiel verlängern, wenn der Urlaub wegen Krankheit nicht genommen werden konnte." },
				{ key: "adjustment", label: "Korrektur (+/−)", type: "number", unit: "Tage", help: "Zum Beispiel Ausgleich oder ausgezahlter Urlaub (negativ)." },
				{ key: "note", label: "Notiz", type: "textarea", rows: 2, maxLength: 500 },
			],
			submit: (values) => put<AccountRow>(`hr/vacation-accounts/${r.employeeId}`, { year: this.accountsYear, entitlement: values.entitlement ?? null,
				carryOver: values.carryOver ?? null, carryOverExpiresOn: values.carryOverExpiresOn ?? null, adjustment: values.adjustment ?? 0, note: values.note || null }),
		});
		if (saved) {
			this.toast(`Urlaubskonto gespeichert, Rest ${num(saved.vacation.remaining)} Tage`);
			await this.loadAccounts();
		}
	}

	// ---- Anträge ---------------------------------------------------------------------------------------------------

	onRequest(): void { void this.request({ userId: null }); }
	onRequestFor(): void { void this.request({ userId: "" }); }

	/** Arten, die der Benutzer auswählen darf (eigene: beantragbar; Büro: alle außer Krankheit und Betriebsferien). */
	private requestTypes(forOthers: boolean): AbsenceTypeView[] {
		return this.types.filter((t) => t.isActive && !t.isSickness && t.key !== "closure" && (t.requestable || forOthers || this.has("absence.approve")));
	}

	/**
	 * Antrag stellen, offenen Antrag bearbeiten oder Änderungsantrag zu einem genehmigten Antrag.
	 * userId null = für mich; "" = Mitarbeiter wählen.
	 */
	private async request(o: { userId: string | null; day?: string; existing?: RequestView; replaces?: RequestView }): Promise<void> {
		const base = o.existing ?? o.replaces;
		const forOthers = o.userId !== null && !base;
		const people = (await session.people()).filter((p) => p.isActive);
		const types = this.requestTypes(forOthers);
		const vacationId = types.find((t) => t.key === "vacation")?.id ?? types[0]?.id;
		let timer: number | undefined;
		let seq = 0;
		const refresh = (form: FormHandle) => {
			window.clearTimeout(timer);
			timer = window.setTimeout(() => void (async () => {
				const from = form.get("from") as string | null;
				const to = form.get("to") as string | null;
				const text = form.control("preview") as Text | undefined;
				if (!text) return;
				if (!from || !to || to < from) { text.setText("Bitte Zeitraum wählen."); return; }
				const mine = ++seq;
				try {
					const p = await post<Preview>("hr/absence-requests/preview", {
						userId: forOthers ? form.get("userId") || null : base ? base.userId : null, from, to, typeId: form.get("typeId"),
						halfDayStart: !!form.get("halfDayStart"), halfDayEnd: !!form.get("halfDayEnd"), substituteUserId: form.get("substituteUserId") || null,
						replacesRequestId: o.replaces?.id ?? null,
					});
					if (mine !== seq) return;
					const lines = [`${num(p.workingDays)} Arbeitstage${p.remainingAfter == null ? "" : `, danach ${num(p.remainingAfter)} Tage Resturlaub`}.`,
						p.requiresApproval ? "Der Antrag wird zur Genehmigung weitergeleitet." : "Keine Genehmigung erforderlich, die Abwesenheit wird sofort eingetragen.",
						...(p.holidays.length ? [`Feiertage im Zeitraum: ${p.holidays.map((h) => `${date(h.date)} ${h.name}`).join(", ")}`] : []),
						...p.warnings.map((w) => `Hinweis: ${w}`)];
					text.setText(lines.join("\n"));
				} catch (error) {
					if (mine === seq) text.setText((error as Error).message);
				}
			})(), 300);
		};
		const onChange = (_: unknown, form: FormHandle) => refresh(form);
		const fields: Field[] = [
			...(forOthers ? [{ key: "userId", label: "Mitarbeiter", type: "select" as const, required: true, options: people.map((p) => ({ key: p.id, text: p.displayName })), onChange }] : []),
			{ key: "typeId", label: "Art", type: "select", required: true, options: types.map((t) => ({ key: t.id, text: t.name, additionalText: typeRules(t) })), onChange },
			{ key: "from", label: "Von", type: "date", required: true, onChange },
			{ key: "to", label: "Bis einschließlich", type: "date", required: true, onChange,
				validate: (v, all) => (v && all.from && String(v) < String(all.from) ? "Das Ende liegt vor dem Beginn." : null) },
			{ key: "halfDayStart", label: "Erster Tag nur halb", type: "switch", onChange, help: "Abwesend ab Mittag" },
			{ key: "halfDayEnd", label: "Letzter Tag nur halb", type: "switch", onChange, help: "Abwesend bis Mittag" },
			{ key: "substituteUserId", label: "Vertretung", type: "select", placeholder: "keine", options: people.filter((p) => p.id !== (base?.userId ?? session.userId))
				.map((p) => ({ key: p.id, text: p.displayName })), onChange },
			{ key: "note", label: "Bemerkung", maxLength: 500, placeholder: "z. B. Familienurlaub, Umzug" },
			{ key: "preview", type: "info", help: "Bitte Zeitraum wählen." },
		];
		const id = o.existing?.id ?? uuidv7();
		const saved = await openForm<RequestView>({
			title: o.replaces ? "Änderung beantragen" : o.existing ? "Antrag bearbeiten" : forOthers ? "Abwesenheit für Mitarbeiter eintragen" : "Urlaub beantragen",
			width: "34rem",
			intro: o.replaces ? `Genehmigt: ${o.replaces.typeName ?? ""} ${range(o.replaces.from, o.replaces.to, o.replaces.halfDayStart, o.replaces.halfDayEnd)}. Bis zur Genehmigung der Änderung gilt der bisherige Zeitraum.`
				: forOthers ? "Genehmigungspflichtige Arten erscheinen unter „Anträge“, die übrigen werden sofort eingetragen." : "Ihr Antrag geht an die Genehmiger. Sie sehen vorab Arbeitstage, Resturlaub und Hinweise.",
			initial: {
				userId: o.userId || null, typeId: base?.typeId ?? vacationId, from: base?.from ?? o.day ?? null, to: base?.to ?? o.day ?? null,
				halfDayStart: base?.halfDayStart ?? false, halfDayEnd: base?.halfDayEnd ?? false, substituteUserId: base?.substituteUserId ?? "", note: base?.note ?? "",
			},
			fields,
			submitText: o.existing ? "Speichern" : "Beantragen",
			submit: (v) => put<RequestView>(`hr/absence-requests/${id}`, {
				userId: forOthers ? v.userId : base ? base.userId : null, typeId: v.typeId, from: v.from, to: v.to, halfDayStart: !!v.halfDayStart,
				halfDayEnd: !!v.halfDayEnd, substituteUserId: v.substituteUserId || null, note: v.note || null, replacesRequestId: o.replaces?.id ?? null,
			}),
		});
		window.clearTimeout(timer);
		if (!saved) return;
		const days = num(saved.workingDays);
		const what = saved.status === "approved" ? "Eingetragen" : o.existing ? "Antrag gespeichert" : "Antrag gestellt";
		if (saved.warnings.length) {
			MessageBox.warning(`${what} (${days} Arbeitstage). Hinweise:\n\n${saved.warnings.join("\n")}`, { title: what });
		} else {
			this.toast(`${what} (${days} Arbeitstage)`);
		}
		if (saved.status === "approved") planningChanged();
		await this.load();
	}

	private row<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	onEditRequest(e: Event): void { void this.request({ userId: null, existing: this.row<RequestView>(e) }); }
	onChangeRequest(e: Event): void { void this.request({ userId: null, replaces: this.row<RequestView>(e) }); }

	private async decide(r: RequestView, approve: boolean): Promise<void> {
		const result = await openForm<RequestView>({
			title: approve ? `${r.userName}: genehmigen` : `${r.userName}: ablehnen`, width: "30rem",
			intro: `${r.typeName ?? ""} ${range(r.from, r.to, r.halfDayStart, r.halfDayEnd)} (${num(r.workingDays)} Arbeitstage)`
				+ (r.replacesRequestId ? "\nÄnderungsantrag: Der bisher genehmigte Zeitraum entfällt bei Genehmigung." : "")
				+ (r.warnings.length ? `\nHinweise: ${r.warnings.join(" ")}` : ""),
			fields: [{ key: "comment", label: "Kommentar", type: "textarea", rows: 3, maxLength: 1000, required: !approve,
				placeholder: approve ? "optional" : "Begründung für den Mitarbeiter" }],
			submitText: approve ? "Genehmigen" : "Ablehnen", danger: !approve,
			submit: (v) => post<RequestView>(`hr/absence-requests/${r.id}/decision`, { approve, comment: v.comment || null }),
		});
		if (result) {
			this.toast(approve ? "Genehmigt und in Planung und Teamkalender übernommen" : "Abgelehnt");
			planningChanged();
			await this.load();
		}
	}

	/** Genehmigen mit einem Klick; nur bei Hinweisen (Überschneidung, Kolonne dünn besetzt …) erst bestätigen. */
	async onApprove(e: Event): Promise<void> {
		const r = this.row<RequestView>(e);
		if (r.warnings.length || r.replacesRequestId) {
			await this.decide(r, true);
			return;
		}
		if (await this.run(() => post<RequestView>(`hr/absence-requests/${r.id}/decision`, { approve: true, comment: null }))) {
			this.toast(`Genehmigt: ${r.userName}, in Planung und Teamkalender übernommen`);
			planningChanged();
			await this.load();
		}
	}
	onReject(e: Event): void { void this.decide(this.row<RequestView>(e), false); }

	async onCancel(e: Event): Promise<void> {
		const r = this.row<RequestView>(e);
		const approved = r.status === "approved";
		if (!(await this.confirm(approved ? "Genehmigte Abwesenheit stornieren? Die Tage werden wieder freigegeben, die Genehmiger werden informiert."
			: "Antrag zurückziehen?", { action: approved ? "Stornieren" : "Zurückziehen", danger: true }))) return;
		if (await this.run(() => post(`hr/absence-requests/${r.id}/cancel`))) {
			this.toast(approved ? "Abwesenheit storniert" : "Antrag zurückgezogen");
			planningChanged();
			await this.load();
		}
	}

	// ---- Krankmeldungen --------------------------------------------------------------------------------------------

	onSickNote(): void { void this.sickNoteForm({}); }

	/** Krankmeldung erfassen, bearbeiten oder Folgebescheinigung (eigener Eintrag im selben Fall). */
	private async sickNoteForm(o: { existing?: SickNote; followUpOf?: SickNote }): Promise<void> {
		const people = (await session.people()).filter((p) => p.isActive);
		const base = o.existing ?? o.followUpOf;
		const next = o.followUpOf ? isoNext(o.followUpOf.to) : undefined;
		const sickTypes = this.types.filter((t) => t.isSickness && (t.isActive || t.id === base?.typeId));
		let file: File | null = null;
		const saved = await openForm<SickNote>({
			title: o.existing ? "Krankmeldung bearbeiten" : o.followUpOf ? `Folgebescheinigung: ${o.followUpOf.userName}` : "Krankmeldung erfassen", width: "32rem",
			intro: o.followUpOf ? `Verlängerung der Krankmeldung vom ${date(o.followUpOf.from)} – ${date(o.followUpOf.to)} (gleicher Krankheitsfall).`
				: "Vertraulich, nur für die Personalverwaltung sichtbar. Ohne Genehmigung; andere sehen nur „abwesend“.",
			initial: { userId: base?.userId, typeId: base?.typeId ?? sickTypes[0]?.id, from: o.existing?.from ?? next, to: o.existing?.to ?? null, note: o.existing?.note ?? "" },
			fields: [
				{ key: "userId", label: "Mitarbeiter", type: "select", required: true, editable: !base, options: people.map((p) => ({ key: p.id, text: p.displayName })) },
				...(sickTypes.length > 1 ? [{ key: "typeId", label: "Art", type: "select" as const, required: true, options: sickTypes.map((t) => ({ key: t.id, text: t.name })) }] : []),
				{ key: "from", label: "Von", type: "date", required: true },
				{ key: "to", label: "Bis einschließlich", type: "date", required: true,
					validate: (v, all) => (v && all.from && String(v) < String(all.from) ? "Das Ende liegt vor dem Beginn." : null) },
				{ key: "note", label: "Notiz", maxLength: 500, help: "Keine Diagnose eintragen." },
			],
			extra: () => {
				const button = new Button({
					text: o.existing?.certificateFileId ? "AU-Bescheinigung ersetzen" : "AU-Bescheinigung anhängen", icon: "sap-icon://attachment",
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
				const certificateFileId = file ? await uploadLoose(file) : o.existing?.certificateFileId ?? null;
				return put<SickNote>(`hr/sick-notes/${o.existing?.id ?? uuidv7()}`, {
					userId: base?.userId ?? v.userId, from: v.from, to: v.to, note: v.note || null, certificateFileId, followUpOf: o.followUpOf?.id ?? null,
					typeId: v.typeId ?? sickTypes[0]?.id ?? null,
				});
			},
		});
		if (saved) {
			this.toast(o.followUpOf ? "Folgebescheinigung erfasst" : o.existing ? "Krankmeldung gespeichert" : "Krankmeldung erfasst");
			planningChanged();
			await this.load();
		}
	}

	/** Detailfenster eines Krankheitsfalls: Zeitraum, Arbeitstage, AU, Notizen und Verlauf (Erstmeldung, Folgebescheinigungen). */
	onSickDetail(e: Event): void {
		const s = this.row<SickNote>(e);
		const all = (this.model().getProperty("/sickNotes") as SickNote[]).filter((x) => (x.caseId ?? x.id) === (s.caseId ?? s.id))
			.sort((a, b) => a.from.localeCompare(b.from));
		const first = all[0] ?? s;
		const last = all[all.length - 1] ?? s;
		const days = all.reduce((sum, x) => sum + x.workingDays, 0);
		const list = new List({ headerText: "Verlauf", items: all.map((x, i) => new StandardListItem({
			title: `${i === 0 ? "Erstmeldung" : `Folgebescheinigung ${i}`}: ${date(x.from)} – ${date(x.to)} (${x.workingDays} AT)`,
			description: [x.note, x.recordedAt ? `erfasst ${dateTime(x.recordedAt)}` : null].filter(Boolean).join(" · "),
			info: x.certificateFileId ? "AU vorhanden" : "AU fehlt", infoState: x.certificateFileId ? "Success" : "Warning",
			icon: "sap-icon://stethoscope", type: x.certificateFileId ? "Active" : "Inactive",
			press: () => { if (x.certificateFileId) window.open(fileUrl(x.certificateFileId), "_blank", "noopener"); },
		})) });
		const dialog = new Dialog({
			title: `Krankmeldung ${s.userName}`, contentWidth: "36rem",
			content: [
				new TextControl({ text: `Zeitraum ${date(first.from)} – ${date(last.to)} · ${days} Arbeitstage · ${all.length > 1 ? `${all.length - 1} Folgebescheinigung(en)` : "Erstmeldung"}` })
					.addStyleClass("sapUiSmallMargin"),
				list,
			],
			buttons: [
				new Button({ text: "Folgebescheinigung", icon: "sap-icon://add", type: "Emphasized", press: () => { dialog.close(); void this.sickNoteForm({ followUpOf: last }); } }),
				new Button({ text: "Bearbeiten", icon: "sap-icon://edit", press: () => { dialog.close(); void this.sickNoteForm({ existing: s }); } }),
				new Button({ text: "Schließen", press: () => dialog.close() }),
			],
			afterClose: () => dialog.destroy(),
		});
		dialog.open();
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

	// ---- Arten und Betriebsferien ----------------------------------------------------------------------------------

	private async loadAdmin(): Promise<void> {
		const m = this.model();
		m.setProperty("/typeRows", this.types.map((t) => ({ ...t, dot: colorDot(t.color), rulesText: typeRules(t) })));
		const closures = await get<ClosureView[]>("hr/closures", { year: this.accountsYear });
		m.setProperty("/closures", closures.map((c) => ({ ...c, rangeText: range(c.from, c.to), workingDaysText: num(c.workingDays), typeName: this.typeById(c.typeId)?.name ?? "" })));
	}

	onNewType(): void { void this.typeForm(null); }
	onEditType(e: Event): void { void this.typeForm(this.row<AbsenceTypeView>(e)); }

	private async typeForm(t: AbsenceTypeView | null): Promise<void> {
		const saved = await openForm<AbsenceTypeView>({
			title: t ? `Art bearbeiten: ${t.name}` : "Neue Abwesenheitsart", width: "34rem",
			intro: t?.isSystem ? "Standardart: Name, Farbe und Regeln lassen sich ändern, die Grundart nicht." : "Die Grundart bestimmt die Darstellung in Planung und Apps.",
			initial: t ? { ...t } : { kind: "other", color: typeColors[0].key, requiresApproval: true, countsAsVacation: false, requestable: true, isPaid: true, isActive: true,
				sortOrder: (this.types.length + 1) * 10 },
			fields: [
				{ key: "name", label: "Bezeichnung", required: true, maxLength: 60 },
				{ key: "code", label: "Kürzel im Kalender", required: true, maxLength: 3, placeholder: "z. B. AT" },
				{ key: "color", label: "Farbe", type: "select", required: true, options: [...typeColors, ...(t && !typeColors.some((c) => c.key === t.color) ? [{ key: t.color, text: "Eigene Farbe" }] : [])] },
				{ key: "kind", label: "Grundart", type: "select", required: true, editable: !t?.isSystem, options: kindOptions },
				{ key: "requiresApproval", label: "Genehmigungspflichtig", type: "switch" },
				{ key: "countsAsVacation", label: "Vom Urlaubskonto abziehen", type: "switch" },
				{ key: "requestable", label: "Mitarbeiter dürfen beantragen", type: "switch", help: "Krankheitsarten erfasst immer das Büro." },
				{ key: "isPaid", label: "Bezahlt", type: "switch" },
				{ key: "isActive", label: "Aktiv", type: "switch" },
				{ key: "sortOrder", label: "Reihenfolge", type: "integer", min: 0, max: 1000 },
			],
			submit: (v) => put<AbsenceTypeView>(`hr/absence-types/${t?.id ?? uuidv7()}`, {
				name: v.name, code: v.code, color: v.color, kind: v.kind, requiresApproval: !!v.requiresApproval && v.kind !== "sick",
				countsAsVacation: !!v.countsAsVacation && v.kind !== "sick", requestable: !!v.requestable && v.kind !== "sick", isPaid: !!v.isPaid, isActive: !!v.isActive,
				sortOrder: v.sortOrder ?? 100,
			}),
		});
		if (saved) {
			this.toast("Abwesenheitsart gespeichert");
			await this.load();
		}
	}

	onNewClosure(): void { void this.closureForm(null); }
	onEditClosure(e: Event): void { void this.closureForm(this.row<ClosureView>(e)); }

	private async closureForm(c: ClosureView | null): Promise<void> {
		const types = this.types.filter((t) => !t.isSickness && (t.isActive || t.id === c?.typeId));
		const saved = await openForm<ClosureView>({
			title: c ? "Betriebsferien bearbeiten" : "Betriebsferien oder Brückentag eintragen", width: "32rem",
			intro: "Wird für alle aktiven Mitarbeiter als Abwesenheit eingetragen (im Beschäftigungszeitraum). Die Art bestimmt, ob die Tage vom Urlaub abgezogen werden.",
			initial: c ? { ...c } : { typeId: this.types.find((t) => t.key === "closure")?.id },
			fields: [
				{ key: "name", label: "Bezeichnung", required: true, maxLength: 100, placeholder: "z. B. Betriebsferien Weihnachten, Brückentag" },
				{ key: "from", label: "Von", type: "date", required: true },
				{ key: "to", label: "Bis einschließlich", type: "date", required: true,
					validate: (v, all) => (v && all.from && String(v) < String(all.from) ? "Das Ende liegt vor dem Beginn." : null) },
				{ key: "typeId", label: "Art", type: "select", required: true, options: types.map((t) => ({ key: t.id, text: t.name, additionalText: typeRules(t) })) },
			],
			submit: (v) => put<ClosureView>(`hr/closures/${c?.id ?? uuidv7()}`, { name: v.name, from: v.from, to: v.to, typeId: v.typeId }),
		});
		if (saved) {
			this.toast(`Betriebsferien eingetragen (${saved.employees} Personen, ${num(saved.workingDays)} Arbeitstage)`);
			planningChanged();
			await this.load();
		}
	}

	async onDeleteClosure(e: Event): Promise<void> {
		const c = this.row<ClosureView>(e);
		if (!(await this.confirm(`„${c.name}“ löschen? Die Einträge aller Mitarbeiter werden entfernt.`, { action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`hr/closures/${c.id}`); return true; })) {
			planningChanged();
			await this.load();
		}
	}

	// ---- Einstellungen ---------------------------------------------------------------------------------------------

	async onSettings(): Promise<void> {
		const s = await this.run(() => get<Settings>("hr/settings"));
		if (!s) return;
		const saved = await openForm<Settings>({
			title: "Einstellungen Personal", width: "34rem", initial: { ...s, carryOverExpiryMonth: String(s.carryOverExpiryMonth) },
			fields: [
				{ key: "state", label: "Bundesland (Feiertage)", type: "select", required: true, options: s.states.map((x) => ({ key: x.key, text: x.name })) },
				{ key: "resturlaub", label: "Urlaubskonto", type: "section" },
				{ key: "carryOverExpiryMonth", label: "Resturlaub verfällt nach (Monat)", type: "select", required: true, options: months.map((text, i) => ({ key: String(i), text })) },
				{ key: "carryOverExpiryDay", label: "Tag", type: "integer", min: 1, max: 31, help: "Zum Beispiel 31 und März: Übertrag verfällt nach dem 31.03." },
				{ key: "prorateEntitlement", label: "Anspruch bei Ein- oder Austritt anteilig", type: "switch", help: "Ein Zwölftel je vollem Beschäftigungsmonat." },
				{ key: "accountingStartYear", label: "Automatischer Übertrag ab Jahr", type: "integer", min: 2000, max: 2100 },
				{ key: "genehmigung", label: "Genehmigung", type: "section" },
				{ key: "crewLeadersApprove", label: "Kolonnenführer genehmigen Anträge ihrer Kolonne", type: "switch" },
				{ key: "maxCrewAbsencePercent", label: "Hinweis ab Anteil gleichzeitig abwesend je Kolonne", type: "integer", required: true, min: 0, max: 100, unit: "%" },
				{ key: "fristen", label: "Fristen", type: "section" },
				{ key: "remindBirthdays", label: "Geburtstage in den Fristen", type: "switch" },
				{ key: "remindAnniversaries", label: "Betriebsjubiläen in den Fristen", type: "switch" },
			],
			submit: (v) => put<Settings>("hr/settings", { ...v, carryOverExpiryMonth: Number(v.carryOverExpiryMonth), carryOverExpiryDay: v.carryOverExpiryDay ?? 31 }),
		});
		if (saved) {
			this.toast("Einstellungen gespeichert");
			await this.load();
		}
	}
}
