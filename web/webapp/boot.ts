import ComponentContainer from "sap/ui/core/ComponentContainer";
import Theming from "sap/ui/core/Theming";
import Device from "sap/ui/Device";
import { resolve, storedChoice, storedDensity } from "./util/theme";
import { followSystem } from "./util/settings";

/** Start: Farbschema und Dichte des Benutzers anwenden, dann die Anwendung laden. */
const choice = storedChoice();
const theme = resolve(choice);
if (Theming.getTheme() !== theme) {
	Theming.setTheme(theme);
}
followSystem();
// Aktuelles Theme als Attribut, damit das Stylesheet je Theme nachschärfen kann (z. B. kräftigere Icons in Quartz).
const markTheme = () => { document.documentElement.dataset.eoTheme = Theming.getTheme(); };
markTheme();
Theming.attachApplied(markTheme);
document.body.classList.add(storedDensity(Device.support.touch && !Device.system.desktop) === "compact" ? "sapUiSizeCompact" : "sapUiSizeCozy");

new ComponentContainer({
	name: "estrichone",
	manifest: true,
	async: true,
	height: "100%",
	settings: { id: "estrichone" },
	componentCreated: () => document.getElementById("splash")?.remove(),
}).placeAt("content");
