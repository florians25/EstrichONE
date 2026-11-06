/**
 * Farbschemata (OpenUI5-Themes) je Benutzer: Auswahl im Einstellungsdialog, sofort wirksam, gespeichert im Browser.
 * „Systemeinstellung folgen“ wählt Horizon hell oder dunkel nach prefers-color-scheme und reagiert auf Wechsel.
 * Alle Themes liegen im eigenen Build (keine CDN-Quellen, strikte CSP). OpenUI5 1.136 liefert Horizon und Quartz
 * (sap_fiori_3) je hell, dunkel und mit hohem Kontrast; Belize und sap_hcb leitet es selbst auf Horizon um, daher
 * stehen sie nicht zur Auswahl.
 */

export interface ThemeInfo {
	id: string;
	text: string;
	family: "Horizon" | "Quartz";
	dark: boolean;
	contrast?: "black" | "white";
}

export const SYSTEM = "system";

export const themes: ThemeInfo[] = [
	{ id: "sap_horizon", text: "Morgenhorizont (hell)", family: "Horizon", dark: false },
	{ id: "sap_horizon_dark", text: "Abendhorizont (dunkel)", family: "Horizon", dark: true },
	{ id: "sap_horizon_hcb", text: "Horizon Hoher Kontrast Schwarz", family: "Horizon", dark: true, contrast: "black" },
	{ id: "sap_horizon_hcw", text: "Horizon Hoher Kontrast Weiß", family: "Horizon", dark: false, contrast: "white" },
	{ id: "sap_fiori_3", text: "Quartz hell", family: "Quartz", dark: false },
	{ id: "sap_fiori_3_dark", text: "Quartz dunkel", family: "Quartz", dark: true },
	{ id: "sap_fiori_3_hcb", text: "Quartz Hoher Kontrast Schwarz", family: "Quartz", dark: true, contrast: "black" },
	{ id: "sap_fiori_3_hcw", text: "Quartz Hoher Kontrast Weiß", family: "Quartz", dark: false, contrast: "white" },
];

const STORAGE_KEY = "estrichone.theme";
const DENSITY_KEY = "estrichone.density";

function read(key: string): string | null {
	try {
		return window.localStorage.getItem(key);
	} catch {
		return null;
	}
}

function write(key: string, value: string): void {
	try {
		window.localStorage.setItem(key, value);
	} catch {
		// Privater Modus: Auswahl gilt nur für diese Sitzung.
	}
}

/** Gespeicherte Auswahl: Theme-ID oder „system“ (Standard). */
export function storedChoice(): string {
	const value = read(STORAGE_KEY);
	return value && (value === SYSTEM || themes.some((t) => t.id === value)) ? value : SYSTEM;
}

export function prefersDark(): boolean {
	return typeof window.matchMedia === "function" && window.matchMedia("(prefers-color-scheme: dark)").matches;
}

/** Tatsächlich anzuwendendes Theme zur Auswahl. */
export function resolve(choice: string, dark = prefersDark()): string {
	if (choice === SYSTEM) {
		return dark ? "sap_horizon_dark" : "sap_horizon";
	}
	return themes.some((t) => t.id === choice) ? choice : "sap_horizon";
}

export function info(id: string): ThemeInfo {
	return themes.find((t) => t.id === id) ?? themes[0];
}

export function storeChoice(choice: string): void {
	write(STORAGE_KEY, choice);
}

export type Density = "compact" | "cozy";

/** Kompakt am Desktop, sonst touchfreundlich – vom Benutzer überschreibbar. */
export function storedDensity(touch: boolean): Density {
	const value = read(DENSITY_KEY);
	return value === "compact" || value === "cozy" ? value : touch ? "cozy" : "compact";
}

export function storeDensity(density: Density): void {
	write(DENSITY_KEY, density);
}

/**
 * Logo je Theme (branding/README.md): ShellBar-Kachel auf allen normalen Themes, weiße bzw. schwarze
 * Bildmarke bei hohem Kontrast; Anmeldeseite mit Schriftzug hell/dunkel bzw. einfarbig.
 */
export function logos(themeId: string): { shell: string; horizontal: string; mark: string } {
	const t = info(themeId);
	if (t.contrast === "black") {
		return { shell: "/logo-mark-white.svg", horizontal: "/logo-horizontal-mono-white.svg", mark: "/logo-mark-white.svg" };
	}
	if (t.contrast === "white") {
		return { shell: "/logo-mark-black.svg", horizontal: "/logo-horizontal-mono-black.svg", mark: "/logo-mark-black.svg" };
	}
	return t.dark
		? { shell: "/logo.svg", horizontal: "/logo-horizontal-white.svg", mark: "/logo-mark-dark.svg" }
		: { shell: "/logo.svg", horizontal: "/logo-horizontal.svg", mark: "/logo-mark.svg" };
}

/** Hat der Benutzer auf diesem Gerät selbst ein Farbschema bzw. eine Dichte gewählt? */
export function hasOwnChoice(): { theme: boolean; density: boolean } {
	return { theme: !!read(STORAGE_KEY), density: !!read(DENSITY_KEY) };
}

/**
 * Darstellungsgröße: skaliert Schrift, Bedienelemente und Fenster gemeinsam über die Grundschriftgröße (OpenUI5 und das
 * eigene Stylesheet rechnen in rem). „Automatisch“ vergrößert auf sehr breiten Bildschirmen (z. B. 4K oder Curved/Ultrawide bei 100 %
 * Windows-Skalierung), damit Texte lesbar bleiben.
 */
const SCALE_KEY = "estrichone.scale";
export const AUTO_SCALE = "auto";
export const scales: { key: string; text: string }[] = [
	{ key: AUTO_SCALE, text: "Automatisch" },
	{ key: "100", text: "100 %" },
	{ key: "112", text: "112 %" },
	{ key: "125", text: "125 %" },
	{ key: "150", text: "150 %" },
	{ key: "175", text: "175 %" },
];

export function storedScale(): string {
	const value = read(SCALE_KEY);
	return value && scales.some((s) => s.key === value) ? value : AUTO_SCALE;
}

export function storeScale(scale: string): void {
	write(SCALE_KEY, scale);
}

/**
 * Tatsächlicher Faktor in Prozent; „Automatisch“ richtet sich nach Breite und Höhe des Fensters in CSS-Pixeln:
 * 4K und 5K2K (breit und hoch) 150 %, breite Curved-/Ultrawide-Monitore (z. B. 3440×1440, 5120×1440) 125 %.
 */
export function resolveScale(scale: string, width: number, height = width * 9 / 16): number {
	if (scale !== AUTO_SCALE) {
		const value = Number(scale);
		return value === 112 ? 112.5 : value;
	}
	if (width >= 3400 && height >= 1900) return 150;
	if (width >= 3400) return 125;
	if (width >= 2800) return 125;
	if (width >= 2200) return 112.5;
	return 100;
}
