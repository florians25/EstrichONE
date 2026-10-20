import Theming from "sap/ui/core/Theming";
import JSONModel from "sap/ui/model/json/JSONModel";

/** Alle Themes der verwendeten OpenUI5-Version (Theme-Bibliotheken Horizon, Fiori 3, Belize) – vollständig self-hosted. */
export const THEMES: { id: string; name: string; group: string }[] = [
	{ id: "sap_horizon", name: "Morning Horizon (hell)", group: "Horizon" },
	{ id: "sap_horizon_dark", name: "Evening Horizon (dunkel)", group: "Horizon" },
	{ id: "sap_horizon_hcb", name: "Horizon Hoher Kontrast Schwarz", group: "Horizon" },
	{ id: "sap_horizon_hcw", name: "Horizon Hoher Kontrast Weiß", group: "Horizon" },
	{ id: "sap_fiori_3", name: "Quartz Light (Fiori 3)", group: "Fiori 3" },
	{ id: "sap_fiori_3_dark", name: "Quartz Dark (Fiori 3)", group: "Fiori 3" },
	{ id: "sap_fiori_3_hcb", name: "Quartz Hoher Kontrast Schwarz", group: "Fiori 3" },
	{ id: "sap_fiori_3_hcw", name: "Quartz Hoher Kontrast Weiß", group: "Fiori 3" },
	{ id: "sap_belize", name: "Belize", group: "Belize" },
	{ id: "sap_belize_plus", name: "Belize Deep", group: "Belize" },
	{ id: "sap_belize_hcb", name: "Belize Hoher Kontrast Schwarz", group: "Belize" },
	{ id: "sap_belize_hcw", name: "Belize Hoher Kontrast Weiß", group: "Belize" }
];

export const AUTO = "auto";
const STORAGE_KEY = "estrichone-hosting-theme";
const DARK = new Set(["sap_horizon_dark", "sap_fiori_3_dark"]);

function media(query: string): MediaQueryList | null {
	return typeof window.matchMedia === "function" ? window.matchMedia(query) : null;
}

/** Systemeinstellung: Kontrast und Hell/Dunkel des Betriebssystems. */
export function systemTheme(): string {
	const dark = media("(prefers-color-scheme: dark)")?.matches ?? false;
	const contrast = (media("(prefers-contrast: more)")?.matches ?? false) || (media("(forced-colors: active)")?.matches ?? false);
	if (contrast) {
		return dark ? "sap_horizon_hcb" : "sap_horizon_hcw";
	}
	return dark ? "sap_horizon_dark" : "sap_horizon";
}

export function resolve(choice: string): string {
	return THEMES.some((t) => t.id === choice) ? choice : systemTheme();
}

/** Logo-Varianten je Theme (branding/README.md, Abschnitt OpenUI5-Themes). */
export function logos(theme: string): { shell: string; wordmark: string } {
	if (theme.endsWith("_hcb")) {
		return { shell: "brand/logo-mark-white.svg", wordmark: "brand/logo-horizontal-mono-white.svg" };
	}
	if (theme.endsWith("_hcw")) {
		return { shell: "brand/logo-mark-black.svg", wordmark: "brand/logo-horizontal-mono-black.svg" };
	}
	return { shell: "brand/logo.svg", wordmark: DARK.has(theme) ? "brand/logo-horizontal-white.svg" : "brand/logo-horizontal.svg" };
}

export function storedChoice(): string {
	try {
		return window.localStorage.getItem(STORAGE_KEY) ?? AUTO;
	} catch {
		return AUTO;
	}
}

/** Darstellung: gewählte Option, wirksames Theme und Logos – als Modell „ui“ für die Oberfläche. */
export default class ThemeManager {
	readonly model = new JSONModel({ choice: AUTO, theme: "sap_horizon", logo: "brand/logo.svg", wordmark: "brand/logo-horizontal.svg", themes: THEMES });
	private choice = AUTO;

	constructor() {
		const update = () => {
			if (this.choice === AUTO) {
				this.apply(AUTO, false);
			}
		};
		for (const query of ["(prefers-color-scheme: dark)", "(prefers-contrast: more)", "(forced-colors: active)"]) {
			media(query)?.addEventListener("change", update);
		}
		this.apply(storedChoice(), false);
	}

	get current(): string {
		return this.choice;
	}

	/** Theme sofort umschalten; mit <c>persist</c> auch im Browser merken. */
	apply(choice: string, persist = true): void {
		this.choice = THEMES.some((t) => t.id === choice) ? choice : AUTO;
		const theme = resolve(this.choice);
		if (Theming.getTheme() !== theme) {
			Theming.setTheme(theme);
		}
		const { shell, wordmark } = logos(theme);
		this.model.setData({ choice: this.choice, theme, logo: shell, wordmark, themes: THEMES }, true);
		document.documentElement.dataset.theme = theme;
		if (persist) {
			try {
				window.localStorage.setItem(STORAGE_KEY, this.choice);
			} catch {
				// Privater Modus: Wahl gilt nur für diese Sitzung (serverseitig trotzdem gespeichert).
			}
		}
	}
}
