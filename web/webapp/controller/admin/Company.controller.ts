import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { api, del, get, post, put } from "../../util/api";
import { catalog } from "../../util/catalog";
import type { CompanyResponse, RoleResponse } from "../../util/models";
import { resetCompany } from "../../util/orderDialogs";
import { session } from "../../util/session";
import { loadSystemSettings, saveSystemSettings, type SystemSettings } from "../../util/systemSettings";
import { themes } from "../../util/theme";
import { dateTime } from "../../util/format";
import type { RouteMatch } from "../../util/router";
import { currentRoute } from "../../util/navigation";
import { sendTest, type PushSettings } from "../../util/notifications";
import { chatSettings, saveChatSettings, type ChatSettings } from "../../util/chat";
import type { SegmentedButton$SelectionChangeEvent } from "sap/m/SegmentedButton";
import type List from "sap/m/List";
import type { ListBase$SelectionChangeEvent } from "sap/m/ListBase";
import StandardListItem from "sap/m/StandardListItem";
import Text from "sap/m/Text";
import { openForm } from "../../util/forms";
import type { NumberRangeResponse } from "../../util/models";
import { capacity, formatNumber, isYearly, rangeError, rangeText, remainingLevel, remainingText } from "../../util/numberRanges";
import type Switch from "sap/m/Switch";
import type { Switch$ChangeEvent } from "sap/m/Switch";
import { MODULE_AREAS, dependencyHint, dependentsToDisable, disableQuestion, requirementsToEnable, type ModuleInfo } from "../../util/modules";

/** Symbol je Modul (Einstellungen › Module). */
const moduleIcons: Record<string, string> = {
	bauakten: "sap-icon://folder-full", contacts: "sap-icon://customer", access: "sap-icon://key-user-settings", settings: "sap-icon://action-settings",
	backup: "sap-icon://upload-to-cloud", calls: "sap-icon://call", map: "sap-icon://map-2", planning: "sap-icon://gantt-bars",
	machines: "sap-icon://shipping-status", materials: "sap-icon://product", orders: "sap-icon://cart", staff: "sap-icon://employee-lookup",
	absence: "sap-icon://general-leave-request", inspections: "sap-icon://inspection", diary: "sap-icon://notes", acceptance: "sap-icon://signature",
	tasks: "sap-icon://task", defects: "sap-icon://warning2", approvals: "sap-icon://approvals", dossiers: "sap-icon://folder-blank",
	helpdesk: "sap-icon://headset", feedback: "sap-icon://feedback", chat: "sap-icon://discussion-2",
};

/** Chat hat eigene Einstellungen (Löschfrist, Datenschutz) und erscheint als Karte unter „Kommunikation und Service“. */
const chatModule = (enabled: boolean): ModuleInfo => ({
	key: "chat", area: "service", title: "Chat", core: false, enabled, requires: [], requiredBy: [],
	description: "Einzel- und Gruppenchats für Büro und Mitarbeiter, auch je Kolonne und Baustelle. Vor dem Einschalten mit dem Betriebsrat abstimmen.",
});

interface StorageStats {
	kinds: { kind: string; files: number; bytes: number; uploadedBytes: number; optimized: number }[];
	storedBytes: number;
	savedBytes: number;
	duplicateFiles: number;
	duplicateBytes: number;
	originalsDiskBytes: number;
	variantsDiskBytes: number;
	freeDiskBytes: number | null;
	candidates: number;
	job: { running: boolean; processed: number; optimized: number; savedBytes: number; skipped: number; failed: number; startedAt: string | null; finishedAt: string | null };
}

const bytes = (n: number) => n >= 1024 ** 3 ? `${(n / 1024 ** 3).toLocaleString("de-DE", { maximumFractionDigits: 1 })} GB`
	: n >= 1024 ** 2 ? `${(n / 1024 ** 2).toLocaleString("de-DE", { maximumFractionDigits: 1 })} MB`
	: `${Math.round(n / 1024).toLocaleString("de-DE")} KB`;

interface PushForm extends PushSettings {
	vapidSubjectInput: string;
	apnsKey: string | null;
	removeApnsKey: boolean;
}

interface LogoInfo {
	hasLogo: boolean;
	width: number;
	height: number;
	updatedAt: string | null;
}

/** Beispiel zum Nummernkreis: {JJJJ}, {JJ}, {N…}. */
export function numberExample(pattern: string, year = new Date().getFullYear(), number = 7): string {
	return pattern.replace(/\{JJJJ\}/g, String(year)).replace(/\{JJ\}/g, String(year).slice(-2))
		.replace(/\{(N+)\}/g, (_m, n: string) => String(number).padStart(n.length, "0"));
}

/**
 * Einstellungen der Installation, sauber gruppiert: Firma und Logo, Bauakten (Nummernkreis, Vorgaben, Automatik),
 * Dokumente und Freigaben, Anmeldung, Benachrichtigungen, Darstellung und Karte, PDF-Layout, Module, System.
 * @namespace estrichone.controller.admin
 */
const tabInfo: Record<string, { title: string; text: string }> = {
	firma: { title: "Firma", text: "Briefkopf und Logo für Weboberfläche und alle PDF-Dokumente." },
	darstellung: { title: "Darstellung und Karte", text: "Farbschema und Dichte als Vorgabe für neue Benutzer, Kartenkacheln für Baustellenkarte und Bauakten." },
	pdf: { title: "PDF-Layout", text: "Logo, Akzentfarbe, Fußzeile und Fotos je Zeile in Ausdrucken (Bauakte, Abnahme, Planung, Benutzerbrief)." },
	bauakte: { title: "Bauakten", text: "Vorgaben für neue Bauakten und automatischer Statuswechsel." },
	nummernkreise: { title: "Nummernkreise", text: "Nummern aller Datensätze: Kunden, Lieferanten, Personal, Maschinen, Material, Bauakten, Bestellungen, Anfragen, Aufgaben, Mängel." },
	dokumente: { title: "Dokumente und Freigaben", text: "Vorschläge für Frist und Rolle bei Freigaben; Ordner und Aufbewahrung." },
	module: { title: "Module", text: "Funktionen der Suite für alle Benutzer ein- oder ausschalten. Daten bleiben beim Ausschalten erhalten." },
	sitzungen: { title: "Anmeldung", text: "Wie lange Browser und Apps angemeldet bleiben, Zwei-Faktor-Pflicht für Administratoren." },
	benachrichtigungen: { title: "Benachrichtigungen", text: "Welche E-Mails verschickt werden, Web Push im Browser und Apple Push für die iOS-App." },
	speicher: { title: "Speicher", text: "Fotos und PDFs speicherschonend ablegen, Belegung des Servers und Nachverdichten bestehender Dateien." },
	system: { title: "Weitere Bereiche", text: "Sicherung, Backup-Ziele, Archiv, App-Updates, E-Mail und Protokoll haben eigene Seiten in der Verwaltung." },
};

export default class Company extends BaseController {
	private version = 0;
	private modules: ModuleInfo[] = [];

	onInit(): void {
		this.local({ c: {}, s: null, loaded: false, busy: false, tab: "firma", tabInfo: tabInfo.firma, status: null, dirty: false, logo: { hasLogo: false }, logoUrl: "", templates: [], roles: [],
			themes: [{ id: "system", text: "Systemeinstellung folgen (hell/dunkel)" }, ...themes], ranges: [], rangesBusy: false, rangeWarning: null,
			numbering: { bauakte: "", customer: "" } });
		// Eingaben des Benutzers (Zwei-Wege-Bindung) markieren ungespeicherte Änderungen: Fußleiste mit Speichern/Verwerfen.
		this.model().attachPropertyChange((event) => {
			const path = String(event.getParameter("path"));
			const context = event.getParameter("context") as { getPath?: () => string } | undefined;
			const full = path.startsWith("/") ? path : `${context?.getPath?.() ?? ""}/${path}`;
			if (/^\/(c|s|push|chat)\//.test(full)) this.model().setProperty("/dirty", true);
		});
	}

	/**
	 * Bereich wählen (Liste links); die Adresse merkt sich den Bereich (?bereich=…), die Navigation markiert ihn.
	 * Die Markierung der Liste wird hier gesetzt (nicht per Bindung je Eintrag): Im Modus SingleSelectMaster führt die
	 * Liste ihre eigene Auswahl, gebundene `selected`-Werte liefen dagegen auseinander (falscher Eintrag markiert).
	 */
	private selectTab(tab: string, remember = true): void {
		const m = this.model();
		const changed = m.getProperty("/tab") !== tab;
		m.setProperty("/tab", tab);
		m.setProperty("/tabInfo", tabInfo[tab] ?? tabInfo.firma);
		const list = this.byId("settingsNav") as List | undefined;
		for (const item of list?.getItems() ?? []) {
			if (item instanceof StandardListItem) item.setSelected(item.data("tab") === tab);
		}
		if (remember) this.setQuery({ bereich: tab === "firma" ? null : tab });
		// Neuer Bereich beginnt mit seiner Überschrift im Blick (auch bei schmalem Fenster mit der Liste über dem Inhalt).
		const head = changed && remember ? this.byId("settingsHead")?.getDomRef() : null;
		const top = head?.getBoundingClientRect().top ?? 0;
		if (head && (top < 0 || top > window.innerHeight * 0.6)) head.scrollIntoView({ block: "start" });
	}

	onSection(event: ListBase$SelectionChangeEvent): void {
		const item = event.getParameter("listItem") as Control | undefined;
		this.selectTab(String(item?.data("tab") ?? "firma"));
	}

	/** Statuszeile: fehlende Angaben, die auf Ausdrucken oder beim Versand auffallen. */
	private updateStatus(): void {
		const m = this.model();
		const c = (m.getProperty("/c") ?? {}) as Partial<CompanyResponse>;
		const logo = m.getProperty("/logo") as { hasLogo: boolean };
		const push = m.getProperty("/push") as { apnsReady?: boolean } | null;
		this.statusLine([
			!(c.street && c.postalCode && c.city) && "Anschrift im Briefkopf unvollständig (Bereich Firma)",
			!c.phone && !c.email && "Telefon oder E-Mail für den Briefkopf fehlt",
			!logo?.hasLogo && { text: "Noch kein Firmenlogo hinterlegt", level: "info" },
			push && !push.apnsReady && { text: "Apple Push nicht eingerichtet (iOS-App nur mit Postfach)", level: "info" },
		], "Firmendaten vollständig");
	}

	/** Nummernkreise laden: Tabelle mit Vorschau und Ampel „verbleibend“, Hinweis bei knappen Bereichen. */
	private async loadRanges(): Promise<void> {
		const m = this.model();
		m.setProperty("/rangesBusy", true);
		try {
			const ranges = await get<NumberRangeResponse[]>("settings/number-ranges");
			const state = { ok: "Success", warning: "Warning", error: "Error" } as const;
			const icon = { ok: "sap-icon://sys-enter-2", warning: "sap-icon://warning", error: "sap-icon://error" } as const;
			m.setProperty("/ranges", ranges.map((r) => {
				const level = remainingLevel(r);
				return { ...r, level, rangeText: rangeText(r), remainingText: remainingText(r), remainingState: r.remaining === null ? "None" : state[level],
					remainingIcon: r.remaining === null ? "" : icon[level], nextText: r.nextNumber.toLocaleString("de-DE") };
			}));
			const tight = ranges.filter((r) => remainingLevel(r) !== "ok");
			const empty = tight.filter((r) => remainingLevel(r) === "error");
			m.setProperty("/rangeWarning", tight.length === 0 ? null : {
				type: empty.length ? "Error" : "Warning",
				text: empty.length
					? `Ausgeschöpft: ${empty.map((r) => r.label).join(", ")}. Neue Datensätze dieser Art können erst angelegt werden, wenn Muster oder Bereich erweitert sind.`
					: `Bald ausgeschöpft: ${tight.map((r) => `${r.label} (${r.remaining} frei)`).join(", ")}. Bitte rechtzeitig Muster oder Bereich erweitern.`,
			});
			m.setProperty("/numbering", { bauakte: ranges.find((r) => r.key === "bauakte")?.preview ?? "", customer: ranges.find((r) => r.key === "customer")?.preview ?? "" });
		} catch (error) {
			if (this.has("settings.manage")) this.showError(error);
		} finally {
			m.setProperty("/rangesBusy", false);
		}
	}

	onNumberRanges(): void {
		this.selectTab("nummernkreise");
	}

	/** Nummernkreis bearbeiten: Muster, Bereich, nächste Nummer mit Vorschau; gespeichert wird sofort (eigenes Protokoll). */
	async onEditRange(event: Event): Promise<void> {
		const r = (event.getSource() as Control).getBindingContext("view")?.getObject() as NumberRangeResponse;
		const preview = new Text({ text: "" }).addStyleClass("eoHelpText sapUiSmallMarginTop sapUiSmallMarginBeginEnd");
		const read = (values: Record<string, unknown>) => ({
			pattern: String(values.pattern ?? "").trim(),
			rangeStart: values.rangeStart === null || values.rangeStart === undefined || values.rangeStart === "" ? 1 : Number(values.rangeStart),
			rangeEnd: values.rangeEnd === null || values.rangeEnd === undefined || values.rangeEnd === "" ? null : Number(values.rangeEnd),
			nextNumber: values.nextNumber === null || values.nextNumber === undefined || values.nextNumber === "" ? null : Number(values.nextNumber),
		});
		const show = (values: Record<string, unknown>) => {
			const v = read(values);
			const error = rangeError(v, r.maxLength);
			const next = v.nextNumber ?? r.nextNumber;
			const end = v.rangeEnd ?? (isYearly(v.pattern) ? null : capacity(v.pattern));
			preview.setText(error ?? `Nächste Nummer: ${formatNumber(v.pattern, next)} · letzte im Bereich: ${end === null ? "unbegrenzt" : formatNumber(v.pattern, end)}`
				+ (isYearly(v.pattern) ? " · zählt jedes Jahr ab dem Anfang neu" : ""));
		};
		let current: Record<string, unknown> = { pattern: r.pattern, rangeStart: r.rangeStart, rangeEnd: r.rangeEnd, nextNumber: r.nextNumber };
		const changed = (key: string) => (value: unknown) => { current = { ...current, [key]: value }; show(current); };
		show(current);
		const saved = await openForm<NumberRangeResponse>({
			title: `Nummernkreis ${r.label}`, width: "38rem",
			intro: `Standard: ${r.defaultPattern}. Bereits vergebene Nummern bleiben unverändert.`,
			initial: current,
			fields: [
				{ key: "pattern", label: "Muster", required: true, maxLength: r.maxLength, onChange: changed("pattern"),
					help: "{JJJJ} Jahr, {JJ} Jahr zweistellig, {N…} laufende Nummer (Anzahl N = Mindeststellen); sonst Buchstaben, Ziffern und - / . _",
					validate: (_v, values) => rangeError(read(values), r.maxLength) },
				{ key: "rangeStart", label: "Bereich von", type: "integer", min: 1, required: true, onChange: changed("rangeStart") },
				{ key: "rangeEnd", label: "Bereich bis", type: "integer", min: 1, onChange: changed("rangeEnd"),
					help: "Leer: ohne Jahr bis zur letzten Nummer, die in die Stellen passt; mit Jahr unbegrenzt." },
				{ key: "nextNumber", label: "Nächste Nummer", type: "integer", min: 1, onChange: changed("nextNumber"),
					help: r.yearly ? "Gilt für das laufende Jahr." : "Z. B. nach Übernahme aus einem Altsystem höher setzen." },
			],
			extra: () => [preview],
			submit: async (values) => {
				const v = read(values);
				return put<NumberRangeResponse>(`settings/number-ranges/${r.key}`, { ...v, nextNumber: v.nextNumber === r.nextNumber ? null : v.nextNumber, version: r.version });
			},
		});
		if (!saved) return;
		// Bauakten- und Kundenmuster stehen auch in den Systemeinstellungen; sonst würde „Speichern“ sie zurücksetzen.
		if (saved.key === "bauakte") this.model().setProperty("/s/numbering/bauaktePattern", saved.pattern);
		if (saved.key === "customer") this.model().setProperty("/s/numbering/customerPattern", saved.pattern);
		this.toast(`Nummernkreis ${saved.label} gespeichert`);
		await this.loadRanges();
	}

	async onRouteMatched(match?: RouteMatch): Promise<void> {
		const tab = match?.query.get("bereich");
		this.selectTab(tab && tabInfo[tab] ? tab : "firma", false);
		// Bereich über die Seitennavigation gewechselt, während Änderungen offen sind: nur umschalten, nichts verwerfen.
		if (this.model().getProperty("/dirty")) return;
		this.model().setProperty("/busy", true);
		try {
			void this.loadPush();
			void this.loadChat().then(() => this.loadModules());
			void this.loadStorage();
			void this.loadRanges();
			const [c, settings, logo, templates, roles] = await Promise.all([
				get<CompanyResponse>("company"),
				loadSystemSettings(true),
				get<LogoInfo>("company/logo/info").catch(() => ({ hasLogo: false, width: 0, height: 0, updatedAt: null })),
				catalog("bauakteTemplate").catch(() => []),
				get<RoleResponse[]>("access/roles").catch(() => []),
			]);
			this.version = settings?.version ?? 0;
			const m = this.model();
			m.setProperty("/c", c);
			m.setProperty("/s", this.editable(settings?.settings ?? null));
			m.setProperty("/logo", logo);
			m.setProperty("/logoUrl", `/api/v1/company/logo?v=${encodeURIComponent(logo.updatedAt ?? "")}`);
			m.setProperty("/templates", [{ key: "", text: "Keine Vorlage" }, ...templates.map((t) => ({ key: t.id, text: t.name }))]);
			m.setProperty("/roles", [{ key: "", text: "Keine" }, ...roles.map((r) => ({ key: r.id, text: r.name }))]);
			m.setProperty("/loaded", !!settings);
			m.setProperty("/dirty", false);
			this.updateStatus();
		} catch (error) {
			this.showError(error);
		} finally {
			this.model().setProperty("/busy", false);
		}
	}

	/** Module mit Zustand vom Server (dazu der Chat) nach Bereichen für die Karten. */
	private async loadModules(): Promise<void> {
		const response = await get<{ modules: ModuleInfo[]; version: number }>("settings/modules").catch(() => null);
		if (!response) return;
		this.modules = response.modules;
		this.showModules();
	}

	private showModules(): void {
		const chat = this.model().getProperty("/chat") as ChatSettings | null;
		const all = chat ? [...this.modules, chatModule(chat.enabled)] : this.modules;
		this.model().setProperty("/moduleAreas", MODULE_AREAS.map((area) => ({
			...area,
			modules: all.filter((m) => m.area === area.key).map((m) => ({ ...m, icon: moduleIcons[m.key] ?? "sap-icon://puzzle", hint: dependencyHint(all, m) })),
		})).filter((area) => area.modules.length > 0));
	}

	/**
	 * Schalter eines Moduls: Ausschalten mit Rückfrage (abhängige Module gehen mit aus), Einschalten nimmt ausgeschaltete
	 * Voraussetzungen mit (nach Rückfrage). Wirkt sofort für alle; Navigation und Rechte werden neu angewendet.
	 */
	async onModuleSwitch(event: Switch$ChangeEvent): Promise<void> {
		const control = event.getSource() as Switch;
		const on = event.getParameter("state") === true;
		const key = String(control.getBindingContext("view")?.getProperty("key") ?? "");
		const revert = (): void => { control.setState(!on); };
		if (key === "chat") {
			await this.switchChat(on, revert);
			return;
		}
		const module = this.modules.find((m) => m.key === key);
		if (!module) return revert();
		const changes: Record<string, boolean> = { [key]: on };
		if (!on) {
			const dependents = dependentsToDisable(this.modules, key);
			if (!(await this.confirm(disableQuestion(module, dependents), { title: "Modul ausschalten", action: "Ausschalten", danger: true }))) return revert();
			for (const d of dependents) changes[d.key] = false;
		} else {
			const required = requirementsToEnable(this.modules, key);
			if (required.length) {
				const names = required.map((r) => `„${r.title}“`).join(", ");
				if (!(await this.confirm(`„${module.title}“ benötigt ${names}. ${required.length === 1 ? "Dieses Modul wird" : "Diese Module werden"} mit eingeschaltet.`,
					{ title: "Modul einschalten", action: "Einschalten" }))) return revert();
				for (const r of required) changes[r.key] = true;
			}
		}
		const m = this.model();
		m.setProperty("/moduleBusy", true);
		try {
			const saved = await put<{ modules: ModuleInfo[]; version: number }>("settings/modules", { modules: changes, version: this.version });
			this.version = saved.version;
			this.modules = saved.modules;
			// Stand im Formular nachziehen (ohne „ungespeichert“), damit „Speichern“ die Schalter nicht zurücksetzt.
			const s = m.getProperty("/s") as SystemSettings | null;
			if (s) m.setProperty("/s/modules", { ...s.modules, ...Object.fromEntries(saved.modules.filter((x) => !x.core).map((x) => [x.key, x.enabled])) });
			await loadSystemSettings(true);
			await session.reloadPermissions();
			const extra = Object.keys(changes).length - 1;
			this.toast(`${module.title} ${on ? "eingeschaltet" : "ausgeschaltet"}${extra > 0 ? ` (mit ${extra} abhängigen ${extra === 1 ? "Modul" : "Modulen"})` : ""}`);
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/moduleBusy", false);
			this.showModules();
		}
	}

	private async switchChat(on: boolean, revert: () => void): Promise<void> {
		const c = this.model().getProperty("/chat") as ChatSettings | null;
		if (!c) return revert();
		const question = on
			? "Chat einschalten? Ein Chat unter Beschäftigten ist mit dem Betriebsrat abzustimmen (§ 87 BetrVG) und ins Verzeichnis der Verarbeitungstätigkeiten aufzunehmen."
			: disableQuestion(chatModule(true), []);
		if (!(await this.confirm(question, { title: on ? "Chat einschalten" : "Modul ausschalten", action: on ? "Einschalten" : "Ausschalten", danger: !on }))) return revert();
		try {
			const saved = await saveChatSettings({ enabled: on, retentionDays: Number(c.retentionDays), showPreview: c.showPreview,
				workersCanChatEachOther: c.workersCanChatEachOther, version: c.version });
			this.model().setProperty("/chat", { ...saved, cleanupText: saved.lastCleanupAt ? dateTime(saved.lastCleanupAt) : "" });
			await session.reloadPermissions();
			this.toast(on ? "Chat eingeschaltet" : "Chat ausgeschaltet");
		} catch (error) {
			revert();
			this.showError(error);
		} finally {
			this.showModules();
		}
	}

	private async loadChat(): Promise<void> {
		const chat = await chatSettings().catch(() => null);
		this.model().setProperty("/chat", chat ? { ...chat, cleanupText: chat.lastCleanupAt ? dateTime(chat.lastCleanupAt) : "" } : null);
	}

	private async saveChat(): Promise<void> {
		const c = this.model().getProperty("/chat") as ChatSettings | null;
		if (!c) return;
		const saved = await saveChatSettings({ enabled: c.enabled, retentionDays: Number(c.retentionDays), showPreview: c.showPreview,
			workersCanChatEachOther: c.workersCanChatEachOther, version: c.version });
		this.model().setProperty("/chat", { ...saved, cleanupText: saved.lastCleanupAt ? dateTime(saved.lastCleanupAt) : "" });
	}

	private async loadPush(): Promise<void> {
		const push = await get<PushSettings>("notifications/settings").catch(() => null);
		this.model().setProperty("/push", push ? this.pushForm(push) : null);
		if (this.model().getProperty("/loaded")) this.updateStatus();
	}

	private pushForm(push: PushSettings): PushForm {
		const defaultSubject = push.vapidSubject;
		return { ...push, vapidSubjectInput: defaultSubject.startsWith("mailto:") || defaultSubject.startsWith("https://") ? defaultSubject : "", apnsKey: null, removeApnsKey: false };
	}

	private async savePush(): Promise<void> {
		const p = this.model().getProperty("/push") as PushForm | null;
		if (!p) return;
		const saved = await put<PushSettings>("notifications/settings", {
			webPushEnabled: p.webPushEnabled, vapidSubject: p.vapidSubjectInput || null, apnsEnabled: p.apnsEnabled, apnsKeyId: p.apnsKeyId || null,
			apnsTeamId: p.apnsTeamId || null, apnsBundleId: p.apnsBundleId || null, apnsProduction: p.apnsProduction, apnsKey: p.apnsKey,
			removeApnsKey: p.removeApnsKey, showContent: p.showContent, version: p.version,
		});
		this.model().setProperty("/push", this.pushForm(saved));
	}

	private storageTimer: ReturnType<typeof setTimeout> | undefined;

	private async loadStorage(): Promise<void> {
		const stats = await get<StorageStats>("files/storage").catch(() => null);
		if (!stats) return;
		const kinds: Record<string, string> = { photo: "Fotos", video: "Videos", document: "Dokumente" };
		const job = stats.job;
		this.model().setProperty("/storage", {
			...stats,
			storedText: `${bytes(stats.storedBytes)} in ${stats.kinds.reduce((n, k) => n + k.files, 0)} Dateien`,
			savedText: stats.savedBytes > 0 ? `${bytes(stats.savedBytes)} durch Verdichtung` : "noch nichts",
			duplicateText: stats.duplicateFiles ? `${stats.duplicateFiles === 1 ? "1 Datei" : `${stats.duplicateFiles} Dateien`} mehrfach hochgeladen, nur einmal abgelegt (${bytes(stats.duplicateBytes)} gespart)` : "keine",
			diskText: `Originale ${bytes(stats.originalsDiskBytes)}, Vorschaustufen ${bytes(stats.variantsDiskBytes)}${stats.freeDiskBytes != null ? ` · frei ${bytes(stats.freeDiskBytes)}` : ""}`,
			kinds: stats.kinds.map((k) => ({ ...k, label: kinds[k.kind] ?? k.kind, bytesText: bytes(k.bytes), uploadedText: bytes(k.uploadedBytes) })),
			jobText: job.running ? `läuft: ${job.processed} geprüft, ${job.optimized} verdichtet (${bytes(job.savedBytes)} gespart)`
				: job.finishedAt ? `zuletzt: ${job.optimized} verdichtet, ${bytes(job.savedBytes)} gespart, ${job.skipped} übersprungen${job.failed ? `, ${job.failed} Fehler` : ""}`
				: "bisher nicht gelaufen",
		});
		clearTimeout(this.storageTimer);
		if (job.running) this.storageTimer = setTimeout(() => void this.loadStorage(), 2000);
	}

	async onOptimizeStorage(): Promise<void> {
		if (!(await this.confirm("Bestehende Fotos und PDFs jetzt im Hintergrund verdichten? Archivierte Dateien bleiben unverändert. Die Arbeit läuft weiter, auch wenn Sie die Seite verlassen.",
			{ title: "Nachverdichten", action: "Starten" }))) return;
		try {
			await post("files/storage/optimize");
			await this.loadStorage();
		} catch (error) {
			this.showError(error);
		}
	}

	onStorageRefresh(): void {
		void this.loadStorage();
	}

	onApnsEnvironment(event: SegmentedButton$SelectionChangeEvent): void {
		this.model().setProperty("/push/apnsProduction", event.getParameter("item")?.getKey() === "prod");
	}

	onApnsKey(): void {
		const input = document.createElement("input");
		input.type = "file";
		input.accept = ".p8,application/pkcs8,text/plain";
		input.addEventListener("change", async () => {
			const file = input.files?.[0];
			if (!file) return;
			const text = await file.text();
			if (!text.includes("BEGIN PRIVATE KEY")) {
				this.toast("Das ist keine .p8-Datei von Apple (PRIVATE KEY erwartet).");
				return;
			}
			this.model().setProperty("/push/apnsKey", text);
			this.model().setProperty("/push/removeApnsKey", false);
			const id = /AuthKey_([A-Z0-9]{10})\.p8/i.exec(file.name)?.[1];
			if (id && !this.model().getProperty("/push/apnsKeyId")) this.model().setProperty("/push/apnsKeyId", id.toUpperCase());
		});
		input.click();
	}

	onApnsKeyRemove(): void {
		this.model().setProperty("/push/apnsKey", null);
		this.model().setProperty("/push/removeApnsKey", true);
		this.model().setProperty("/push/apnsKeyStored", false);
	}

	async onPushTest(): Promise<void> {
		try {
			await sendTest();
			this.toast("Testnachricht gesendet. Bitte Postfach und Pushnachricht prüfen.");
			document.dispatchEvent(new CustomEvent("estrichone-notices"));
		} catch (error) {
			this.showError(error);
		}
	}

	async onSave(): Promise<void> {
		const m = this.model();
		const c = m.getProperty("/c") as CompanyResponse;
		const s = m.getProperty("/s") as SystemSettings;
		if (!c.name?.trim()) {
			this.selectTab("firma");
			this.toast("Bitte den Firmennamen angeben.");
			return;
		}
		const clean = {
			...s,
			bauakte: { ...s.bauakte, defaultTemplateId: s.bauakte.defaultTemplateId || null },
			approvals: { ...s.approvals, defaultRoleId: s.approvals.defaultRoleId || null },
			pdf: { ...s.pdf, footerText: s.pdf.footerText || null },
		};
		m.setProperty("/busy", true);
		try {
			const [savedCompany, savedSettings] = await Promise.all([
				put<CompanyResponse>("company", { ...c, email: c.email || null }),
				saveSystemSettings(clean, this.version),
				this.savePush(),
				this.saveChat(),
			]);
			resetCompany();
			this.version = savedSettings.version;
			m.setProperty("/c", savedCompany);
			m.setProperty("/s", this.editable(savedSettings.settings));
			// Firmenname erscheint sofort in Kopfzeile, Statuszeile und Anmeldung (server-info liefert ihn als serverName).
			await Promise.all([session.reloadPermissions(), session.loadServer(), this.loadModules()]);
			m.setProperty("/dirty", false);
			this.toast("Einstellungen gespeichert");
			this.updateStatus();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	/** Ungespeicherte Änderungen verwerfen: alles neu vom Server laden, Bereich bleibt. */
	async onDiscard(): Promise<void> {
		this.model().setProperty("/dirty", false);
		await this.onRouteMatched(currentRoute() ?? undefined);
	}

	onLogoUpload(): void {
		const input = document.createElement("input");
		input.type = "file";
		input.accept = "image/png,image/jpeg,image/webp";
		input.addEventListener("change", async () => {
			const file = input.files?.[0];
			if (!file) return;
			try {
				const logo = await api<LogoInfo>("company/logo", { method: "PUT", raw: file, headers: { "Content-Type": file.type || "application/octet-stream" } });
				this.model().setProperty("/logo", logo);
				this.model().setProperty("/logoUrl", `/api/v1/company/logo?v=${encodeURIComponent(logo.updatedAt ?? String(Date.now()))}`);
				this.toast("Logo gespeichert");
				document.dispatchEvent(new CustomEvent("estrichone-logo"));
			} catch (error) {
				this.showError(error);
			}
		});
		input.click();
	}

	async onLogoDelete(): Promise<void> {
		if (!(await this.confirm("Firmenlogo entfernen? Ausdrucke zeigen dann wieder das EstrichONE-Logo.", { title: "Logo entfernen", action: "Entfernen", danger: true }))) return;
		try {
			await del("company/logo");
			this.model().setProperty("/logo", { hasLogo: false, width: 0, height: 0, updatedAt: null });
			document.dispatchEvent(new CustomEvent("estrichone-logo"));
		} catch (error) {
			this.showError(error);
		}
	}

	onLink(event: Event): void {
		const path = (event.getSource() as Control).data("path") as string;
		this.nav(path);
	}

	/** Für die Auswahlfelder: „Keine Vorlage“/„Keine“ ist der Eintrag mit leerem Schlüssel (gespeichert wird null). */
	private editable(settings: SystemSettings | null): SystemSettings | null {
		return settings ? { ...settings, bauakte: { ...settings.bauakte, defaultTemplateId: settings.bauakte.defaultTemplateId ?? "" },
			approvals: { ...settings.approvals, defaultRoleId: settings.approvals.defaultRoleId ?? "" } } : null;
	}
}
