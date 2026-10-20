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
import { AUTO } from "../util/Theme";

/**
 * Rahmen der Konsole: Anmeldung oder ShellBar mit Navigation; Darstellungsdialog (Themes).
 * @namespace estrichone.hosting.controller
 */
export default class AppController extends BaseController {
	private readonly shell = new JSONModel({ selectedKey: "dashboard", openOperations: "" });
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
			void this.pollOperations();
			this.pollTimer ??= setInterval(() => void this.pollOperations(), 10000);
		}
	}

	private async pollOperations(): Promise<void> {
		if (!this.getOwnerComponentTyped().session.getProperty("/authenticated")) {
			return;
		}
		try {
			const dashboard = await get<{ runningOperations: number }>("/api/dashboard");
			this.shell.setProperty("/openOperations", dashboard.runningOperations > 0 ? String(dashboard.runningOperations) : "");
		} catch {
			// Anzeige ist optional.
		}
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
