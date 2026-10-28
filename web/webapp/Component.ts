import UIComponent from "sap/ui/core/UIComponent";
import JSONModel from "sap/ui/model/json/JSONModel";
import Device from "sap/ui/Device";
import { session } from "./util/session";
import { installFormLayout } from "./util/formLayout";

/**
 * EstrichONE im Browser. Der Pfad-Router (util/router) und der Programmrahmen sitzen im App-Controller.
 * @namespace estrichone
 */
export default class Component extends UIComponent {
	public static metadata = {
		manifest: "json",
		interfaces: ["sap.ui.core.IAsyncContentCreation"],
	};

	init(): void {
		// Vor allen Ansichten: Abschnittsüberschriften und Spalten aller SimpleForms vereinheitlichen.
		installFormLayout();
		super.init();
		this.setModel(new JSONModel(Device), "device");
		this.setModel(session.model, "session");
		// Service Worker auch ohne Push-Opt-in: zeigt ohne Verbindung die Störungsseite (docs/backup.md, B8).
		if ("serviceWorker" in navigator && window.isSecureContext) {
			void navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(() => undefined);
		}
	}
}
