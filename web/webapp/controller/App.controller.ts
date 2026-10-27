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
import { chatStatus } from "../util/chat";
import { session } from "../util/session";
import { logos } from "../util/theme";
import { openSettings } from "../util/settings";
import { openQuickCall } from "../util/quickCall";
import { openBauakteWizard } from "../util/bauakteWizard";
import type { CallResponse, DeviceResponse, TicketSummary, WorkItemDto } from "../util/models";
import { isoDay } from "../util/days";
import Button from "sap/m/Button";
import Toolbar from "sap/m/Toolbar";
import ToolbarSpacer from "sap/m/ToolbarSpacer";
import Title from "sap/m/Title";
import GroupHeaderListItem from "sap/m/GroupHeaderListItem";
import NotificationListItem from "sap/m/NotificationListItem";
import { CATEGORY_ICONS, inbox, markAllRead, markRead, refreshWebPush, relativeTime, type NotificationItem } from "../util/notifications";
import { openNotificationPreferences } from "../util/notificationPreferences";

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
	private chatTimer: ReturnType<typeof setInterval> | undefined;

	onInit(): void {
		this.local({
			logo: logos(Theming.getTheme()).shell,
			loginLogo: logos(Theming.getTheme()).horizontal,
			initials: "",
			navKey: "",
			today: new Date().toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit", year: "numeric" }),
			online: true,
			notifications: { count: "", items: [] as Notice[], inbox: [] as NotificationItem[], unread: 0 },
			chatUnread: 0,
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
		document.addEventListener("estrichone-logo", () => void this.loadAppearance());
		// Helpdesk: Ticket gelesen oder beantwortet – Zähler sofort aktualisieren.
		document.addEventListener("estrichone-notices", () => { if (session.data.user) void this.loadNotices(); });
		// Module oder Rechte geändert (Einstellungen): Navigation sofort anpassen.
		document.addEventListener("estrichone-permissions", () => this.refreshShell());
		document.addEventListener("estrichone-chat", () => void this.loadChat());
		// Service Worker: neue Pushnachricht (Glocke sofort aktualisieren) bzw. Klick auf eine Benachrichtigung.
		navigator.serviceWorker?.addEventListener("message", (event: MessageEvent<{ type?: string; url?: string }>) => {
			if (event.data?.type === "estrichone-notification" && session.data.user) { void this.loadNotices(); void this.loadChat(); }
			if (event.data?.type === "estrichone-open" && event.data.url) this.nav(event.data.url);
		});
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
		clearInterval(this.chatTimer);
	}

	private el<T>(id: string): T {
		return this.v().byId(id) as unknown as T;
	}

	/** Navigation, Avatar und Hinweise an Benutzer und Rechte anpassen. */
	private logoUser = "-";

	/**
	 * Darstellungsvorgaben der Installation (Theme, Dichte). Das Firmenlogo erscheint bewusst nicht in der Kopfzeile –
	 * dort steht nur das EstrichONE-Logo; das Firmenlogo nutzen PDFs und Briefe.
	 */
	private async loadAppearance(): Promise<void> {
		if (!session.data.user) return;
		const settings = await loadSystemSettings();
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
	}

	private refreshShell(): void {
		notices.sessionChanged();
		if (this.logoUser !== (session.data.user?.id ?? "")) {
			this.logoUser = session.data.user?.id ?? "";
			void this.loadAppearance();
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
				new MenuItem({ text: this.t("inbox.preferences"), icon: "sap-icon://bell", press: () => openNotificationPreferences() }),
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
		if (route.redirect) {
			this.nav(route.redirect(match.query), true);
			return;
		}
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
			// Aus einer Pushnachricht geöffnet: als gelesen markieren.
			const nid = match.query.get("nid");
			if (nid) {
				void markRead(nid).then(() => this.loadNotices()).catch(() => undefined);
				const query = new URLSearchParams(window.location.search);
				query.delete("nid");
				window.history.replaceState(window.history.state, "", window.location.pathname + (query.size ? `?${query.toString()}` : ""));
			}
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
		void refreshWebPush();
		void this.loadNotices();
		void this.loadChat();
		this.noticeTimer = setInterval(() => void this.loadNotices(), 120_000);
		this.chatTimer = setInterval(() => void this.loadChat(), 30_000);
	}

	/** Ungelesene Chatnachrichten (Badge an der Sprechblase in der Kopfzeile). */
	private async loadChat(): Promise<void> {
		if (!session.data.user || !session.has("chat.use")) {
			this.model().setProperty("/chatUnread", 0);
			return;
		}
		const status = await chatStatus().catch(() => null);
		this.model().setProperty("/chatUnread", status?.unreadCount ?? 0);
	}

	onChat(): void {
		this.nav("/chat");
	}

	private async loadNotices(): Promise<void> {
		const items: Notice[] = [];
		const today = isoDay(new Date());
		const tasks: Promise<void>[] = [];
		// Postfach: Ereignisse und Nachrichten aus dem Büro. Hinweise zu denselben Objekten werden nicht doppelt gezeigt.
		const box = await inbox({ limit: 8 }).catch(() => ({ items: [] as NotificationItem[], unreadCount: 0 }));
		const covered = new Set(box.items.filter((n) => !n.readAt && n.objectId).map((n) => n.objectId));
		if (session.has("contacts.read")) {
			tasks.push(get<CallResponse[]>("calls", { due: true }).then((calls) => {
				for (const c of calls.slice(0, 10)) {
					items.push({ title: `${this.t("notice.followUp")}: ${[c.callerName, c.company ?? c.customerName].filter(Boolean).join(" · ") || "–"}`,
						description: c.subject ?? c.notes?.slice(0, 80) ?? "", icon: "sap-icon://call", path: "/anrufe", priority: "Medium" });
				}
			}));
		}
		// Neue Antworten in eigenen bzw. zu bearbeitenden Tickets (für alle Benutzer).
		const unreadIds = new Set<string>();
		const unreadTask = get<{ count: number; items: { id: string; number: string; title: string }[] }>("tickets/unread").then((unread) => {
			for (const t of unread.items.slice(0, 10)) {
				unreadIds.add(t.id);
				if (covered.has(t.id)) continue;
				items.push({ title: `${t.number}: ${t.title}`, description: this.t("notice.ticketReply"), icon: "sap-icon://comment",
					path: `/it-center/helpdesk/${t.id}`, priority: "Medium" });
			}
		}).catch(() => undefined);
		tasks.push(unreadTask);
		if (session.has("helpdesk.agent")) {
			tasks.push(unreadTask.then(() => get<TicketSummary[]>("tickets", { filter: "open" })).then((tickets) => {
				for (const t of tickets.filter((x) => (x.status === "new" || x.reporterBlocked) && !unreadIds.has(x.id)).slice(0, 10)) {
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
				for (const w of work.filter((x) => x.status !== "done" && x.dueDate && x.dueDate < today && !covered.has(x.id)).slice(0, 10)) {
					items.push({ title: `${this.t("notice.overdue")}: ${w.title}`, description: w.dueDate ?? "", icon: "sap-icon://alert",
						path: w.bauakteId ? `/bauakten/${w.bauakteId}?abschnitt=aufgaben` : "/tagesuebersicht", priority: "High" });
				}
			}));
		}
		await Promise.allSettled(tasks);
		const total = box.unreadCount + items.length;
		this.model().setProperty("/notifications", { count: total ? String(total > 99 ? "99+" : total) : "", items, inbox: box.items, unread: box.unreadCount });
		if ("setAppBadge" in navigator) void (navigator as Navigator & { setAppBadge(n: number): Promise<void> }).setAppBadge(box.unreadCount).catch(() => undefined);
	}

	/** Glocke: Postfach (neueste 8) und Hinweise; Aktionen „Alle gelesen“, „Alle anzeigen“, „Nachricht senden“, Einstellungen. */
	onNotifications(event: ShellBar$NotificationsPressedEvent): void {
		const data = this.model().getProperty("/notifications") as { items: Notice[]; inbox: NotificationItem[]; unread: number };
		const list = new List({ noDataText: this.t("notice.none"), showSeparators: "Inner" });
		list.addStyleClass("eoNotificationList");
		const open = (path: string | null) => {
			popover.close();
			if (path) this.nav(path);
		};
		list.addItem(new GroupHeaderListItem({ title: data.unread ? this.t("inbox.unread", [data.unread]) : this.t("inbox.title") }));
		if (!data.inbox.length) {
			list.addItem(new StandardListItem({ title: this.t("inbox.empty"), type: "Inactive" }));
		}
		for (const n of data.inbox) {
			list.addItem(new NotificationListItem({
				title: n.title, description: n.body ?? "", datetime: relativeTime(n.createdAt), unread: !n.readAt,
				priority: n.urgency === "high" ? "High" : "None", authorName: n.senderName ?? "", authorPicture: CATEGORY_ICONS[n.category],
				showCloseButton: false, truncate: true, showButtons: false,
				press: () => {
					if (!n.readAt) void markRead(n.id).then(() => this.loadNotices()).catch(() => undefined);
					open(n.link ?? "/benachrichtigungen");
				},
			}));
		}
		if (data.items.length) {
			list.addItem(new GroupHeaderListItem({ title: this.t("notice.title") }));
			for (const n of data.items) {
				list.addItem(new StandardListItem({
					title: n.title, description: n.description, icon: n.icon, type: "Navigation", wrapping: true,
					highlight: n.priority === "High" ? "Error" : n.priority === "Medium" ? "Warning" : "None",
					press: () => open(n.path),
				}));
			}
		}
		const header = new Toolbar({
			content: [new Title({ text: this.t("inbox.popoverTitle"), level: "H2" }), new ToolbarSpacer(),
				new Button({ text: this.t("inbox.markAll"), type: "Transparent", enabled: data.unread > 0, press: async () => {
					await markAllRead().catch((error: unknown) => this.showError(error));
					popover.close();
					void this.loadNotices();
				} }),
				new Button({ icon: "sap-icon://action-settings", type: "Transparent", tooltip: this.t("inbox.preferences"), press: () => {
					popover.close();
					openNotificationPreferences();
				} }),
			],
		});
		const footer = new Toolbar({
			content: [
				new Button({ text: this.t("inbox.showAll"), type: "Transparent", press: () => open("/benachrichtigungen") }),
				new ToolbarSpacer(),
				...(session.has("notifications.send")
					? [new Button({ text: this.t("inbox.send"), icon: "sap-icon://paper-plane", type: "Transparent", press: () => open("/verwaltung/nachrichten?neu=1") })]
					: []),
			],
		});
		const popover: ResponsivePopover = new ResponsivePopover({
			customHeader: header, contentWidth: "28rem", contentHeight: "auto", placement: "Bottom", content: [list], footer,
			afterClose: () => popover.destroy(),
		});
		popover.addStyleClass("eoNotificationPopover");
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

	onOutageRetry(): void {
		void notices.retryOutage();
	}
}
