import Controller from "sap/ui/core/mvc/Controller";
import UIComponent from "sap/ui/core/UIComponent";
import Router from "sap/ui/core/routing/Router";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import Model from "sap/ui/model/Model";
import JSONModel from "sap/ui/model/json/JSONModel";
import Input from "sap/m/Input";
import Control from "sap/ui/core/Control";
import { ValueState } from "sap/ui/core/library";
import type Component from "../Component";
import formatter from "../model/formatter";
import { ApiError } from "../model/Api";

/**
 * @namespace estrichone.hosting.controller
 */
export default class BaseController extends Controller {
	public readonly formatter = formatter;

	protected getOwnerComponentTyped(): Component {
		return this.getOwnerComponent() as Component;
	}

	protected getRouter(): Router {
		return UIComponent.getRouterFor(this);
	}

	protected getModel<T extends Model = JSONModel>(name?: string): T {
		return this.getView()!.getModel(name) as T;
	}

	protected setModel(model: Model, name?: string): void {
		this.getView()!.setModel(model, name);
	}

	protected navTo(route: string, parameters?: object): void {
		this.getRouter().navTo(route, parameters);
	}

	/** Seite gerade sichtbar? (Hintergrundaktualisierung nur dann) */
	protected isVisible(): boolean {
		return ((this.getView()!.getDomRef() as HTMLElement | null)?.offsetParent ?? null) !== null;
	}

	protected toast(text: string): void {
		MessageToast.show(text);
	}

	/** Fehler verständlich anzeigen; Feldfehler werden zusätzlich an die Eingabefelder geschrieben. */
	protected showError(error: unknown, fields?: Record<string, Control | undefined>): void {
		if (error instanceof ApiError) {
			if (fields) {
				for (const [name, control] of Object.entries(fields)) {
					const input = control as Input | undefined;
					if (input?.setValueState) {
						const message = error.fields[name];
						input.setValueState(message ? ValueState.Error : ValueState.None);
						input.setValueStateText(message ?? "");
					}
				}
			}
			if (error.status === 401) {
				return;
			}
			MessageBox.error(error.detail, { title: error.status === 409 ? "Nicht möglich" : "Fehler" });
			return;
		}
		MessageBox.error(error instanceof Error ? error.message : String(error));
	}

	protected async run<T>(action: () => Promise<T>, busyControl?: Control): Promise<T | undefined> {
		const target = (busyControl ?? this.getView()) as Control & { setBusy?: (busy: boolean) => void };
		target.setBusy?.(true);
		try {
			return await action();
		} catch (error) {
			this.showError(error);
			return undefined;
		} finally {
			target.setBusy?.(false);
		}
	}
}
