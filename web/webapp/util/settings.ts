/**
 * Einstellungen des Benutzers: Farbschema (alle mitgelieferten OpenUI5-Themes, sofort wirksam) und Dichte.
 * Gespeichert im Browser je Benutzerprofil; „Systemeinstellung folgen“ wechselt mit dem Betriebssystem.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import GroupHeaderListItem from "sap/m/GroupHeaderListItem";
import SegmentedButton from "sap/m/SegmentedButton";
import SegmentedButtonItem from "sap/m/SegmentedButtonItem";
import Label from "sap/m/Label";
import VBox from "sap/m/VBox";
import Text from "sap/m/Text";
import Theming from "sap/ui/core/Theming";
import Device from "sap/ui/Device";
import { info, resolve, storeChoice, storeDensity, storedChoice, storedDensity, SYSTEM, themes, type Density } from "./theme";

/** Theme anwenden (falls nötig) und Auswahl merken. */
export function applyChoice(choice: string): void {
	storeChoice(choice);
	const theme = resolve(choice);
	if (Theming.getTheme() !== theme) {
		Theming.setTheme(theme);
	}
}

export function applyDensity(density: Density): void {
	storeDensity(density);
	document.body.classList.toggle("sapUiSizeCompact", density === "compact");
	document.body.classList.toggle("sapUiSizeCozy", density === "cozy");
}

/** Bei „Systemeinstellung folgen“ auf Hell/Dunkel des Betriebssystems reagieren. */
export function followSystem(): void {
	if (typeof window.matchMedia !== "function") return;
	window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => {
		if (storedChoice() === SYSTEM) {
			applyChoice(SYSTEM);
		}
	});
}

export function openSettings(): void {
	const list = new List("themeList", { mode: "SingleSelectLeft", includeItemInSelection: true, ariaLabelledBy: [] });
	const system = new StandardListItem({ title: "Systemeinstellung folgen", description: "Hell oder dunkel wie im Betriebssystem (Horizon)",
		icon: "sap-icon://laptop", customData: [] });
	system.data("theme", SYSTEM);
	list.addItem(system);
	let family = "";
	for (const t of themes) {
		if (t.family !== family) {
			family = t.family;
			list.addItem(new GroupHeaderListItem({ title: family === "Quartz" ? "Quartz (SAP Fiori 3)" : "Horizon" }));
		}
		const item = new StandardListItem({ title: t.text, description: t.id, icon: t.dark ? "sap-icon://dark-mode" : "sap-icon://light-mode" });
		item.data("theme", t.id);
		list.addItem(item);
	}
	const select = (choice: string) => {
		for (const item of list.getItems()) {
			if (item instanceof StandardListItem) item.setSelected(item.data("theme") === choice);
		}
	};
	select(storedChoice());
	list.attachSelectionChange((event) => {
		const item = event.getParameter("listItem") as StandardListItem;
		applyChoice(String(item.data("theme")));
		current.setText(`Aktiv: ${info(Theming.getTheme()).text}`);
	});
	const density = new SegmentedButton("densitySelect", {
		selectedKey: storedDensity(Device.support.touch && !Device.system.desktop),
		items: [new SegmentedButtonItem({ key: "compact", text: "Kompakt" }), new SegmentedButtonItem({ key: "cozy", text: "Komfortabel (Touch)" })],
		selectionChange: () => applyDensity(density.getSelectedKey() as Density),
	});
	const current = new Text({ text: `Aktiv: ${info(Theming.getTheme()).text}` });
	const dialog = new Dialog("settingsDialog", {
		title: "Einstellungen",
		contentWidth: "30rem",
		contentHeight: "34rem",
		resizable: true,
		draggable: true,
		content: [
			new VBox({
				items: [
					new Label({ text: "Dichte der Oberfläche", labelFor: density }), density,
					new Label({ text: "Farbschema", labelFor: list }).addStyleClass("sapUiSmallMarginTop"), current,
				],
			}).addStyleClass("sapUiSmallMargin"),
			list,
		],
		endButton: new Button({ text: "Schließen", type: "Emphasized", press: () => dialog.close() }),
		afterClose: () => dialog.destroy(),
	});
	dialog.open();
}
