import UIComponent from "sap/ui/core/UIComponent";
import JSONModel from "sap/ui/model/json/JSONModel";
import Device from "sap/ui/Device";
import ThemeManager from "./util/Theme";
import { get, onUnauthorized, put, setCsrfToken } from "./model/Api";

export interface SessionState {
	setupRequired: boolean;
	authenticated: boolean;
	user: { id: string; userName: string; displayName?: string; totpEnabled: boolean; preferences?: { theme?: string } } | null;
	csrfToken: string | null;
	simulation: boolean;
	consoleVersion: string;
}

/**
 * @namespace estrichone.hosting
 */
export default class Component extends UIComponent {
	public static metadata = {
		manifest: "json",
		interfaces: ["sap.ui.core.IAsyncContentCreation"]
	};

	// UIComponent ruft init() bereits im Konstruktor auf – Felder daher dort setzen, nicht per Initialisierer.
	declare themes: ThemeManager;
	declare session: JSONModel;
	declare private routerStarted: boolean;
	declare private sessionListeners: (() => void)[];

	onSessionChanged(listener: () => void): void {
		this.sessionListeners.push(listener);
	}

	public init(): void {
		this.themes = new ThemeManager();
		this.session = new JSONModel({ ready: false, setupRequired: false, authenticated: false, user: null, simulation: false });
		this.routerStarted = false;
		this.sessionListeners = [];
		super.init();
		this.setModel(this.session, "session");
		this.setModel(this.themes.model, "ui");
		const device = new JSONModel(Device);
		device.setDefaultBindingMode("OneWay");
		this.setModel(device, "device");
		onUnauthorized(() => void this.refreshSession());
		void this.refreshSession();
	}

	/** Sitzung vom Server lesen; danach Anmeldung oder Konsole zeigen. */
	async refreshSession(): Promise<SessionState> {
		const state = await get<SessionState>("/api/auth/state");
		setCsrfToken(state.csrfToken);
		this.session.setData({ ...state, ready: true });
		const saved = state.user?.preferences?.theme;
		if (state.authenticated && saved && saved !== this.themes.current) {
			this.themes.apply(saved);
		}
		if (state.authenticated && !this.routerStarted) {
			this.routerStarted = true;
			this.getRouter().initialize();
		}
		this.sessionListeners.forEach((listener) => listener());
		return state;
	}

	/** Theme umschalten und für den Benutzer speichern (Browser und Server). */
	async saveTheme(choice: string): Promise<void> {
		this.themes.apply(choice);
		if (this.session.getProperty("/authenticated")) {
			const preferences = { ...(this.session.getProperty("/user/preferences") ?? {}), theme: choice };
			await put("/api/account/preferences", preferences);
			this.session.setProperty("/user/preferences", preferences);
		}
	}
}
