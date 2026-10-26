import BaseController from "./BaseController";
import XMLView from "sap/ui/core/mvc/XMLView";
import type View from "sap/ui/core/mvc/View";
import type App from "sap/m/App";
import type NavContainer from "sap/m/NavContainer";
import type Page from "sap/m/Page";
import type ToolPage from "sap/tnt/ToolPage";
import type NavigationList from "sap/tnt/NavigationList";
import NavigationListItem from "sap/tnt/NavigationListItem";
import NavigationListGroup from "sap/tnt/NavigationListGroup";
import type { SideNavigation$ItemSelectEvent } from "sap/tnt/SideNavigation";
import type { SearchField$SearchEvent } from "sap/m/SearchField";
import type SearchField from "sap/m/SearchField";
import type Event from "sap/ui/base/Event";
import SuggestionItem from "sap/m/SuggestionItem";
import type { ShellBar$AvatarPressedEvent, ShellBar$NotificationsPressedEvent } from "sap/f/ShellBar";
import Theming from "sap/ui/core/Theming";
import Menu from "sap/m/Menu";
import MenuItem from "sap/m/MenuItem";
import ResponsivePopover from "sap/m/ResponsivePopover";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import type UI5Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { get, onSessionEnded } from "../util/api";
import { relogin } from "../util/relogin";
import { loadSystemSettings } from "../util/systemSettings";
import { hasOwnChoice } from "../util/theme";
import { initials } from "../util/format";
import { setRouter } from "../util/navigation";
import { PathRouter, type RouteMatch } from "../util/router";
import { navKeyFor, navigation, routes, type NavEntry } from "../util/routes";
import { notices } from "../util/notices";
import { session } from "../util/session";
import { logos } from "../util/theme";
import { openSettings } from "../util/settings";
import { openQuickCall } from "../util/quickCall";
import { openBauakteWizard } from "../util/bauakteWizard";
import type { CallResponse, DeviceResponse, TicketSummary, WorkItemDto } from "../util/models";
import { isoDay } from "../util/days";

interface Notice {
	title: string;
	description: string;
	icon: string;
	path: string;
	priority: "High" | "Medium" | "Low" | "None";
}

/**
 * Programmrahmen: ShellBar, Seitennavigation, Inhaltsbereich und Statuszeile; Anmeldeseiten ohne Rahmen.
 * Der Pfad-Router legt die Seiten (XML-Views) beim ersten Aufruf an und meldet ihnen jeden Aufruf.
 * @namespace estrichone.controller
 */
export default class AppController extends BaseController {
	private router!: PathRouter;
	private readonly views = new Map<string, Promise<View>>();
	private sequence = 0;
	private noticeTimer: ReturnType<typeof setInterval> | undefined;

	onInit(): void {
		this.local({
			logo: logos(Theming.getTheme()).shell,
			loginLogo: logos(Theming.getTheme()).horizontal,
			initials: "",
			navKey: "",
			today: new Date().toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit", year: "numeric" }),
			online: true,
			notifications: { count: "", items: [] as Notice[] },
		});
		Theming.attachApplied(() => {
			this.model().setProperty("/logo", logos(Theming.getTheme()).shell);
			this.model().setProperty("/loginLogo", logos(Theming.getTheme()).horizontal);
		});
		this.v().setModel(notices.model, "notices");
		notices.start();
		session.model.attachPropertyChange(() => this.refreshShell());
		onSessionEnded(async (reason) => {
			// Während der Arbeit: Anmeldedialog über der Seite, die Anfrage wird danach wiederholt.
			if (reason === "expired" && session.data.user && (await relogin())) {
				return true;
			}
			session.clear();
			this.resetPages();
			this.nav(`/anmelden?grund=${reason === "disabled" ? "gesperrt" : "abgelaufen"}`, true);
			return false;
		});
		document.addEventListener("estrichone-logo", () => void this.loadCompanyLogo());
		// Module oder Rechte geändert (Einstellungen): Navigation sofort anpassen.
		document.addEventListener("estrichone-permissions", () => this.refreshShell());
		window.addEventListener("online", () => this.model().setProperty("/online", true));
		window.addEventListener("offline", () => this.model().setProperty("/online", false));
		// Nur die aktuelle Seite bleibt im DOM (Seiten bleiben im Speicher erhalten): eindeutige Beschriftungen,
		// keine verdeckten Doppelungen für Screenreader und Tests.
		for (const id of ["authNav", "bareNav", "content"]) {
			const container = this.el<NavContainer>(id);
			container.attachAfterNavigate(() => {
				const current = container.getCurrentPage();
				for (const page of container.getPages()) {
					if (page !== current) container.removePage(page);
				}
			});
		}
		this.router = new PathRouter(routes, (match) => void this.handle(match));
		setRouter(this.router);
		void session.loadServer();
		this.router.start();
	}

	onExit(): void {
		clearInterval(this.noticeTimer);
	}

	private el<T>(id: string): T {
		return this.v().byId(id) as unknown as T;
	}

	/** Navigation, Avatar und Hinweise an Benutzer und Rechte anpassen. */
	private logoUser = "-";

	/** Firmenlogo zusätzlich in der Kopfzeile (Einstellungen → Darstellung). */
	private async loadCompanyLogo(): Promise<void> {
		if (!session.data.user) {
			this.model().setProperty("/companyLogo", "");
			return;
		}
		const [settings, info] = await Promise.all([
			loadSystemSettings(),
			get<{ hasLogo: boolean; updatedAt: string | null }>("company/logo/info").catch(() => ({ hasLogo: false, updatedAt: null })),
		]);
		const show = info.hasLogo && settings?.settings.appearance.showCompanyLogo !== false;
		// Vorgabe der Installation, solange auf diesem Gerät nichts Eigenes gewählt ist.
		const own = hasOwnChoice();
		const appearance = settings?.settings.appearance;
		if (appearance && !own.theme && appearance.defaultTheme !== "system" && Theming.getTheme() !== appearance.defaultTheme) {
			Theming.setTheme(appearance.defaultTheme);
		}
		if (appearance && !own.density) {
			document.body.classList.toggle("sapUiSizeCompact", appearance.density === "compact");
			document.body.classList.toggle("sapUiSizeCozy", appearance.density === "cozy");
		}
		this.model().setProperty("/companyLogo", show ? `/api/v1/company/logo?v=${encodeURIComponent(info.updatedAt ?? "")}` : "");
	}

	private refreshShell(): void {
		notices.sessionChanged();
		if (this.logoUser !== (session.data.user?.id ?? "")) {
			this.logoUser = session.data.user?.id ?? "";
			void this.loadCompanyLogo();
		}
		const data = session.data;
		this.model().setProperty("/initials", initials(data.user?.displayName));
		const list = this.el<NavigationList>("navigationList");
		const signature = Object.keys(data.permissions).sort().join(",");
		if (list.data("signature") === signature) return;
		list.data("signature", signature);
		list.destroyItems();
		const visible = (e: NavEntry) => !e.permission || session.has(e.permission);
		for (const entry of navigation) {
			if (entry.children) {
				const children = entry.children.filter(visible);
				if (children.length === 0) continue;
				const group = new NavigationListGroup({ text: this.t(`nav.${entry.key}`), expanded: true });
				for (const child of children) group.addItem(this.navItem(child));
				list.addItem(group);
			} else if (visible(entry)) {
				list.addItem(this.navItem(entry));
			}
		}
	}

	private navItem(entry: NavEntry): NavigationListItem {
		return new NavigationListItem({ key: entry.key, text: this.t(`nav.${entry.key}`), icon: entry.icon });
	}

	onNavigate(event: SideNavigation$ItemSelectEvent): void {
		const item = event.getParameter("item") as NavigationListItem;
		const key = item.getKey();
		if (key === "__settings") {
			openSettings();
			return;
		}
		const entry = navigation.flatMap((e) => e.children ?? [e]).find((e) => e.key === key);
		if (entry?.path) {
			this.nav(entry.path);
			if (this.getOwnerComponent()?.getModel("device")?.getProperty("/system/phone")) {
				this.el<ToolPage>("toolPage").setSideExpanded(false);
			}
		}
	}

	onToggleSide(): void {
		const page = this.el<ToolPage>("toolPage");
		page.setSideExpanded(!page.getSideExpanded());
	}

	onHome(): void {
		this.nav("/start");
	}

	onSearch(event: SearchField$SearchEvent): void {
		const item = event.getParameter("suggestionItem") as SuggestionItem | undefined;
		if (item?.getKey()) {
			this.nav(item.getKey());
			(event.getSource() as SearchField).setValue("");
			return;
		}
		const value = String(event.getParameter("query") ?? "").trim();
		if (event.getParameter("clearButtonPressed")) return;
		this.nav(value ? `/bauakten?search=${encodeURIComponent(value)}&status=all` : "/bauakten");
	}

	private suggestSeq = 0;

	/** Globale Suche: Bauakten (auch Altakten), Kunden und Dokumente als Vorschläge. */
	async onSuggest(event: Event): Promise<void> {
		const field = event.getSource() as SearchField;
		const value = String(event.getParameter("suggestValue" as never) ?? "").trim();
		const seq = ++this.suggestSeq;
		if (value.length < 2) {
			field.destroySuggestionItems();
			return;
		}
		const has = (p: string) => session.has(p);
		const [akten, kunden, docs] = await Promise.all([
			has("bauakten.read") ? get<{ items: { id: string; number: string | null; title: string; city: string | null; status: string }[] }>("bauakten", { search: value, limit: 5, status: "all" }).catch(() => ({ items: [] })) : { items: [] },
			has("contacts.read") ? get<{ items?: { id: string; name: string; city: string | null }[] } | { id: string; name: string; city: string | null }[]>("contacts", { search: value, limit: 5 }).catch(() => []) : [],
			has("bauakten.read") ? get<{ items: { file: { id: string; bauakteId: string | null; originalName: string }; bauakteNumber: string | null }[] }>("documents", { search: value, limit: 5 }).catch(() => ({ items: [] })) : { items: [] },
		]);
		if (seq !== this.suggestSeq) return;
		field.destroySuggestionItems();
		const add = (text: string, description: string, icon: string, key: string) =>
			field.addSuggestionItem(new SuggestionItem({ text, description, icon, key }));
		for (const a of akten.items) add(`${a.number ?? "neu"} · ${a.title}`, `Bauakte${a.city ? ` · ${a.city}` : ""}`, "sap-icon://folder-full", `/bauakten/${a.id}`);
		const contacts = Array.isArray(kunden) ? kunden : kunden.items ?? [];
		for (const c of contacts.slice(0, 5)) add(c.name, `Kunde${c.city ? ` · ${c.city}` : ""}`, "sap-icon://customer", `/kunden/${c.id}`);
		for (const d of docs.items) if (d.file.bauakteId) add(d.file.originalName, `Dokument · ${d.bauakteNumber ?? ""}`, "sap-icon://document", `/bauakten/${d.file.bauakteId}/datei/${d.file.id}?abschnitt=dokumente`);
		field.suggest();
	}

	onNewBauakte(): void {
		void openBauakteWizard({ status: "planned" }).then((created) => {
			if (created) this.nav(`/bauakten/${created.id}${created.startInspection ? "?erstbegehung=1" : ""}`);
		});
	}

	onQuickCall(): void {
		openQuickCall();
	}

	onAvatar(event: ShellBar$AvatarPressedEvent): void {
		const menu = new Menu({
			title: session.data.user?.displayName ?? "",
			items: [
				new MenuItem({ text: this.t("settings.title"), icon: "sap-icon://action-settings", press: () => openSettings() }),
				new MenuItem({ text: this.t("password.title"), icon: "sap-icon://key", press: () => this.nav("/konto/passwort") }),
				new MenuItem({ text: this.t("shell.signOut"), icon: "sap-icon://log", startsSection: true, press: () => void this.signOut() }),
			],
			// Erst nach der Auswahl aufräumen (das Ereignis kommt nach dem Schließen).
			closed: () => setTimeout(() => menu.destroy(), 500),
		});
		this.v().addDependent(menu);
		menu.openBy(event.getParameter("avatar") as Control, false);
	}

	private async signOut(): Promise<void> {
		await session.signOut().catch(() => undefined);
		this.resetPages();
		this.nav("/anmelden");
	}

	/** Seiten des vorigen Benutzers verwerfen (Daten und Rechte gehören zur Sitzung). */
	private resetPages(): void {
		clearInterval(this.noticeTimer);
		this.noticeTimer = undefined;
		const content = this.el<NavContainer>("content");
		for (const [, view] of this.views) {
			void view.then((v) => {
				content.removePage(v);
				v.destroy();
			});
		}
		this.views.clear();
	}

	private async loadView(name: string): Promise<View> {
		let view = this.views.get(name);
		if (!view) {
			view = this.getOwnerComponent()!.runAsOwner(() => XMLView.create({ viewName: `estrichone.view.${name}` })) as Promise<View>;
			this.views.set(name, view);
		}
		return view;
	}

	private async handle(match: RouteMatch | null): Promise<void> {
		const ticket = ++this.sequence;
		if (!session.data.ready) {
			await session.restore();
		}
		const user = session.data.user;
		if (!match) {
			this.nav(user ? "/start" : "/anmelden", true);
			return;
		}
		const { route } = match;
		if (!route.public) {
			if (!user) {
				this.nav(`/anmelden?ziel=${encodeURIComponent(match.path + window.location.search)}`, true);
				return;
			}
			if (session.data.mustChangePassword && route.name !== "password") {
				this.nav("/konto/passwort", true);
				return;
			}
			if (route.permission && !session.has(route.permission)) {
				this.nav("/start", true);
				return;
			}
			void session.people();
			this.refreshShell();
			this.startNotices();
		}
		const root = this.el<App>("root");
		const containerId = route.public ? "authNav" : route.bare ? "bareNav" : "content";
		const container = this.el<NavContainer>(containerId);
		const page = route.public ? this.el<Page>("authPage") : route.bare ? this.el<Page>("barePage") : this.el<ToolPage>("toolPage");
		const view = await this.loadView(route.view);
		if (ticket !== this.sequence) return;
		if (!container.getPages().includes(view)) {
			container.addPage(view);
		}
		container.to(view, "show");
		root.to(page.getId(), "show");
		for (const other of ["authNav", "bareNav", "content"].filter((id) => id !== containerId)) {
			this.el<NavContainer>(other).removeAllPages();
		}
		this.model().setProperty("/navKey", navKeyFor(route.nav, match.query));
		document.title = `${route.title ? this.t(route.title) : ""} – EstrichONE`.replace(/^ – /, "");
		const controller = view.getController() as BaseController | undefined;
		try {
			await controller?.onRouteMatched?.(match);
		} catch (error) {
			this.showError(error);
		}
	}

	// Hinweise in der ShellBar: fällige Wiedervorlagen, offene Tickets, Geräte zur Freigabe, überfällige Aufgaben.

	private startNotices(): void {
		if (this.noticeTimer) return;
		void this.loadNotices();
		this.noticeTimer = setInterval(() => void this.loadNotices(), 120_000);
	}

	private async loadNotices(): Promise<void> {
		const items: Notice[] = [];
		const today = isoDay(new Date());
		const tasks: Promise<void>[] = [];
		if (session.has("contacts.read")) {
			tasks.push(get<CallResponse[]>("calls", { due: true }).then((calls) => {
				for (const c of calls.slice(0, 10)) {
					items.push({ title: `${this.t("notice.followUp")}: ${[c.callerName, c.company ?? c.customerName].filter(Boolean).join(" · ") || "–"}`,
						description: c.subject ?? c.notes?.slice(0, 80) ?? "", icon: "sap-icon://call", path: "/anrufe", priority: "Medium" });
				}
			}));
		}
		if (session.has("helpdesk.agent")) {
			tasks.push(get<TicketSummary[]>("tickets", { filter: "open" }).then((tickets) => {
				for (const t of tickets.filter((x) => x.status === "new" || x.reporterBlocked).slice(0, 10)) {
					items.push({ title: `${t.number}: ${t.title}`, description: t.reporterBlocked ? this.t("notice.blocked") : this.t("notice.newTicket"),
						icon: "sap-icon://headset", path: `/it-center/helpdesk/${t.id}`, priority: t.reporterBlocked ? "High" : "Medium" });
				}
			}));
		}
		if (session.has("devices.manage")) {
			tasks.push(get<DeviceResponse[]>("devices").then((devices) => {
				const pending = devices.filter((d) => d.status === "pending");
				if (pending.length) {
					items.push({ title: this.t("notice.devices", [pending.length]), description: pending.map((d) => d.name).join(", "),
						icon: "sap-icon://iphone", path: "/it-center/geraete", priority: "Medium" });
				}
			}));
		}
		if (session.has("work.read")) {
			tasks.push(get<WorkItemDto[]>("work-items/mine").then((work) => {
				for (const w of work.filter((x) => x.status !== "done" && x.dueDate && x.dueDate < today).slice(0, 10)) {
					items.push({ title: `${this.t("notice.overdue")}: ${w.title}`, description: w.dueDate ?? "", icon: "sap-icon://alert",
						path: w.bauakteId ? `/bauakten/${w.bauakteId}?abschnitt=aufgaben` : "/tagesuebersicht", priority: "High" });
				}
			}));
		}
		await Promise.allSettled(tasks);
		this.model().setProperty("/notifications", { count: items.length ? String(items.length) : "", items });
	}

	onNotifications(event: ShellBar$NotificationsPressedEvent): void {
		const items = (this.model().getProperty("/notifications/items") as Notice[]) ?? [];
		const list = new List({ noDataText: this.t("notice.none") });
		for (const n of items) {
			list.addItem(new StandardListItem({
				title: n.title, description: n.description, icon: n.icon, type: "Navigation", wrapping: true,
				highlight: n.priority === "High" ? "Error" : n.priority === "Medium" ? "Warning" : "None",
				press: () => {
					popover.close();
					this.nav(n.path);
				},
			}));
		}
		const popover = new ResponsivePopover({
			title: this.t("notice.title"), contentWidth: "26rem", placement: "Bottom", content: [list],
			afterClose: () => popover.destroy(),
		});
		this.v().addDependent(popover);
		popover.openBy(event.getParameter("button") as Control);
	}

	onNoticeClose(event: UI5Event): void {
		const id = (event.getSource() as Control).getBindingContext("notices")?.getProperty("id") as string | undefined;
		if (id) notices.dismiss(id);
	}

	onMaintenanceAdmin(): void {
		notices.adminLogin();
	}

	onMaintenanceRecheck(): void {
		void notices.recheck();
	}
}
