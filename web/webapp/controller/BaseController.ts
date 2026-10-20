import Controller from "sap/ui/core/mvc/Controller";
import type View from "sap/ui/core/mvc/View";
import type ResourceModel from "sap/ui/model/resource/ResourceModel";
import type ResourceBundle from "sap/base/i18n/ResourceBundle";
import JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import { problemMessage } from "../util/api";
import { navTo, setQuery } from "../util/navigation";
import type { RouteMatch } from "../util/router";
import { session } from "../util/session";

/**
 * Gemeinsame Hilfen aller Seiten: Texte (i18n), Navigation, Rechte, Meldungen, Busy-Anzeige.
 * Seiten bekommen über onRouteMatched die aktuelle Adresse (Parameter und Query).
 * @namespace estrichone.controller
 */
export default class BaseController extends Controller {
	/** Wird vom Programmrahmen nach jedem Seitenaufruf gerufen. */
	onRouteMatched(_match: RouteMatch): void | Promise<void> {
		// Standard: nichts zu tun.
	}

	protected v(): View {
		return this.getView() as View;
	}

	protected bundle(): ResourceBundle {
		const model = (this.getOwnerComponent()?.getModel("i18n") ?? this.v().getModel("i18n")) as ResourceModel;
		return model.getResourceBundle() as ResourceBundle;
	}

	/** Übersetzter Text mit Platzhaltern {0}, {1} … */
	protected t(key: string, args: (string | number)[] = []): string {
		return this.bundle().getText(key, args.map(String)) ?? key;
	}

	protected has(permission: string): boolean {
		return session.has(permission);
	}

	protected nav(path: string, replace = false): void {
		navTo(path, replace);
	}

	protected setQuery(values: Record<string, string | null | undefined>): void {
		setQuery(values);
	}

	/** Lokales JSONModel der Seite (Standardname „view“). */
	protected local<T extends object>(initial: T, name = "view"): JSONModel {
		const model = new JSONModel(initial);
		model.setSizeLimit(5000);
		this.v().setModel(model, name);
		return model;
	}

	protected model(name = "view"): JSONModel {
		return this.v().getModel(name) as JSONModel;
	}

	protected toast(text: string): void {
		MessageToast.show(text, { closeOnBrowserNavigation: false });
	}

	protected showError(error: unknown, title?: string): void {
		MessageBox.error(problemMessage(error), { title: title ?? this.t("common.error") });
	}

	/** Sicherheitsabfrage; `danger` für Löschen und Stornieren. */
	protected confirm(text: string, options: { title?: string; action?: string; danger?: boolean } = {}): Promise<boolean> {
		const action = options.action ?? this.t("common.ok");
		return new Promise((resolve) => {
			MessageBox.show(text, {
				icon: options.danger ? MessageBox.Icon.WARNING : MessageBox.Icon.QUESTION,
				title: options.title ?? this.t("common.confirm"),
				actions: [action, MessageBox.Action.CANCEL],
				emphasizedAction: action,
				onClose: (result: string | null) => resolve(result === action),
			});
		});
	}

	/** Arbeit mit Busy-Anzeige der Seite; Fehler als Meldung. Ergebnis oder `undefined` bei Fehler. */
	protected async run<T>(work: () => Promise<T>, success?: string): Promise<T | undefined> {
		this.v().setBusy(true);
		try {
			const result = await work();
			if (success) {
				this.toast(success);
			}
			return result;
		} catch (error) {
			this.showError(error);
			return undefined;
		} finally {
			this.v().setBusy(false);
		}
	}
}
