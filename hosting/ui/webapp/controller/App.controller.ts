import JSONModel from "sap/ui/model/json/JSONModel";
import App from "sap/m/App";
import ToolPage from "sap/tnt/ToolPage";
import Menu from "sap/m/Menu";
import MenuItem from "sap/m/MenuItem";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import Device from "sap/ui/Device";
import Fragment from "sap/ui/core/Fragment";
import Dialog from "sap/m/Dialog";
import BaseController from "./BaseController";
import { get, post } from "../model/Api";
import ResponsivePopover from "sap/m/ResponsivePopover";
import { AUTO } from "../util/Theme";

/**
 * Rahmen der Konsole: Anmeldung oder ShellBar mit Navigation; Darstellungsdialog (Themes).
 * @namespace estrichone.hosting.controller
 */
export default class AppController extends BaseController {
	private readonly shell = new JSONModel({ selectedKey: "dashboard", openOperations: "", notifications: "", notificationItems: [], runningOperations: 0 });
	private notificationPopover?: ResponsivePopover;
	private themeDialog?: Dialog;
	private pollTimer?: ReturnType<typeof setInterval>;

	public onInit(): void {
		this.setModel(this.shell, "shell");
		this.getView()!.addStyleClass(Device.support.touch ? "sapUiSizeCozy" : "sapUiSizeCompact");
		this.getOwnerComponentTyped().onSessionChanged(() => this.showPage());
		this.showPage();
		this.getRouter().attachRouteMatched((event) => {
			const name = event.getParameter("name") as string;
			this.shell.setProperty("/selectedKey", name === "tenant" ? "tenants" : name);
			// Konto aus dem Installer: erst ein eigenes Passwort, dann alles andere.
			if (name !== "setup" && this.getOwnerComponentTyped().session.getProperty("/mustChangePassword")) {
				this.getRouter().navTo("setup", {}, true);
			}
		});
	}

	private lastAuthenticated: boolean | null = null;

	/** Zeigt je nach Sitzung die Anmeldung oder die Konsole. */
	private showPage(): void {
		const session = this.getOwnerComponentTyped().session;
		if (!session.getProperty("/ready")) {
			return;
		}
		const authenticated = !!session.getProperty("/authenticated");
		if (authenticated === this.lastAuthenticated) {
			return;
		}
		this.lastAuthenticated = authenticated;
		const app = this.byId("rootApp") as App;
		app.to(this.byId(authenticated ? "shell" : "login")!.getId());
		if (authenticated) {
			void this.checkSetup();
			void this.pollOperations();
			this.pollTimer ??= setInterval(() => void this.pollOperations(), 10000);
		}
	}

	/** Nach der Anmeldung: Einrichtungsassistent, solange er nicht abgeschlossen ist (bzw. das Passwort geändert werden muss). */
	private async checkSetup(): Promise<void> {
		try {
			const state = await get<{ completed: boolean; mustChangePassword: boolean }>("/api/setup-wizard");
			this.getOwnerComponentTyped().session.setProperty("/mustChangePassword", state.mustChangePassword);
			if (state.mustChangePassword || !state.completed) {
				this.getRouter().navTo("setup", {}, true);
			}
		} catch {
			// Assistent ist optional erreichbar über Einstellungen.
		}
	}

	private async pollOperations(): Promise<void> {
		if (!this.getOwnerComponentTyped().session.getProperty("/authenticated")) {
			return;
		}
		try {
			const [dashboard, notifications] = await Promise.all([
				get<{ runningOperations: number }>("/api/dashboard"),
				get<{ items: unknown[]; unread: number; open: number }>("/api/notifications")
			]);
			this.shell.setProperty("/runningOperations", dashboard.runningOperations);
			this.shell.setProperty("/openOperations", dashboard.runningOperations > 0 ? String(dashboard.runningOperations) : "");
			this.shell.setProperty("/notifications", notifications.unread > 0 ? String(notifications.unread) : "");
			this.shell.setProperty("/notificationItems", notifications.items);
		} catch {
			// Anzeige ist optional.
		}
	}

	/** Glocke: offene Benachrichtigungen (Probleme) und laufende Vorgänge. */
	public async onNotifications(event: Event): Promise<void> {
		const source = (event.getParameter("button" as never) as Control | undefined) ?? (event.getSource() as Control);
		if (!this.notificationPopover) {
			this.notificationPopover = (await Fragment.load({ id: this.getView()!.getId(), name: "estrichone.hosting.fragment.Notifications", controller: this })) as ResponsivePopover;
			this.getView()!.addDependent(this.notificationPopover);
		}
		await this.pollOperations();
		this.notificationPopover.openBy(source);
	}

	public async onNotificationsReadAll(): Promise<void> {
		await this.run(() => post("/api/notifications/read-all"));
		await this.pollOperations();
	}

	public onNotificationPress(event: Event): void {
		const item = (event.getParameter("listItem" as never) as Control | undefined) ?? (event.getSource() as Control);
		const data = item.getBindingContext("shell")!.getObject() as { id: number; tenantId?: string | null };
		void post(`/api/notifications/${data.id}/read`).then(() => this.pollOperations()).catch(() => undefined);
		this.notificationPopover?.close();
		if (data.tenantId) this.navTo("tenant", { id: data.tenantId });
	}

	public onNotificationsOperations(): void {
		this.notificationPopover?.close();
		this.navTo("operations");
	}

	public onToggleSide(): void {
		const page = this.byId("shell") as ToolPage;
		page.setSideExpanded(!page.getSideExpanded());
	}

	public onHome(): void {
		this.navTo("dashboard");
	}

	public onOperations(): void {
		this.navTo("operations");
	}

	public onNavigate(event: Event): void {
		const item = event.getParameter("item" as never) as { getKey(): string };
		this.navTo(item.getKey());
		if (Device.system.phone) {
			(this.byId("shell") as ToolPage).setSideExpanded(false);
		}
	}

	public onAvatar(event: Event): void {
		const source = (event.getParameter("avatar" as never) as Control | undefined) ?? (event.getSource() as Control);
		const user = this.getOwnerComponentTyped().session.getProperty("/user/displayName") || this.getOwnerComponentTyped().session.getProperty("/user/userName");
		const menu = new Menu({
			title: user,
			items: [
				new MenuItem({ key: "user", text: `Angemeldet als ${user}`, icon: "sap-icon://person-placeholder", enabled: false }),
				new MenuItem({ key: "theme", text: "Darstellung …", icon: "sap-icon://palette" }),
				new MenuItem({ key: "settings", text: "Konto und Sicherheit", icon: "sap-icon://shield" }),
				new MenuItem({ key: "logout", text: "Abmelden", icon: "sap-icon://log", startsSection: true })
			],
			itemSelected: (selected) => {
				const key = (selected.getParameter("item") as MenuItem).getKey();
				if (key === "theme") void this.onTheme();
				if (key === "settings") this.navTo("settings");
				if (key === "logout") void this.onLogout();
			},
			closed: () => setTimeout(() => menu.destroy(), 0)
		});
		this.getView()!.addDependent(menu);
		menu.openBy(source, false);
	}

	private async onLogout(): Promise<void> {
		await this.run(async () => {
			await post("/api/auth/logout");
			window.location.hash = "";
			window.location.reload();
		});
	}

	/** Darstellungsdialog: Theme-Auswahl mit Live-Vorschau, „Systemeinstellung folgen“, Speicherung je Benutzer. */
	public async onTheme(): Promise<void> {
		const component = this.getOwnerComponentTyped();
		const previous = component.themes.current;
		const model = new JSONModel({ choice: previous, auto: previous === AUTO, selected: previous === AUTO ? component.themes.model.getProperty("/theme") : previous });
		if (!this.themeDialog) {
			this.themeDialog = (await Fragment.load({ id: this.getView()!.getId(), name: "estrichone.hosting.fragment.ThemeDialog", controller: this })) as Dialog;
			this.getView()!.addDependent(this.themeDialog);
		}
		this.themeDialog.setModel(model, "themeDialog");
		this.themeDialog.data("previous", previous);
		this.themeDialog.open();
	}

	public onThemeAuto(): void {
		const model = this.themeDialog!.getModel("themeDialog") as JSONModel;
		const choice = model.getProperty("/auto") ? AUTO : model.getProperty("/selected");
		this.getOwnerComponentTyped().themes.apply(choice, false);
	}

	public onThemeSelect(event: Event): void {
		const model = this.themeDialog!.getModel("themeDialog") as JSONModel;
		const item = event.getParameter("listItem" as never) as { getBindingContext(name: string): { getProperty(path: string): string } };
		const id = item.getBindingContext("ui").getProperty("id");
		model.setProperty("/selected", id);
		model.setProperty("/auto", false);
		this.getOwnerComponentTyped().themes.apply(id, false);
	}

	public async onThemeSave(): Promise<void> {
		const model = this.themeDialog!.getModel("themeDialog") as JSONModel;
		const choice = model.getProperty("/auto") ? AUTO : (model.getProperty("/selected") as string);
		this.themeDialog!.close();
		await this.run(() => this.getOwnerComponentTyped().saveTheme(choice));
		this.toast("Darstellung gespeichert");
	}

	public onThemeCancel(): void {
		this.getOwnerComponentTyped().themes.apply(this.themeDialog!.data("previous") as string, false);
		this.themeDialog!.close();
	}

	public isThemeSelected(id: string, selected: string, auto: boolean): boolean {
		return !auto && id === selected;
	}
}
