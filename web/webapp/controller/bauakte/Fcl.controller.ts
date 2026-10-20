import BaseController from "../BaseController";
import type View from "sap/ui/core/mvc/View";
import type { RouteMatch } from "../../util/router";

export interface ColumnController {
	show(match: RouteMatch): Promise<void> | void;
}

/**
 * Bauakten als Liste → Objektseite → Unterobjekt (FlexibleColumnLayout). Die Spaltenaufteilung ergibt sich aus
 * der Adresse: /bauakten (Liste), /bauakten/<id> (Objektseite, mit ?liste=1 neben der Liste),
 * /bauakten/<id>/datei/<id> (Dateivorschau rechts).
 * @namespace estrichone.controller.bauakte
 */
export default class Fcl extends BaseController {
	onInit(): void {
		this.local({ layout: "OneColumn" });
	}

	private async controllerOf(id: string): Promise<ColumnController> {
		const view = this.v().byId(id) as View;
		await view.loaded();
		return view.getController() as unknown as ColumnController;
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const { route, query } = match;
		const list = query.get("liste") === "1";
		const full = query.get("voll") === "1";
		let layout = "OneColumn";
		if (route.name === "bauakte") {
			layout = list ? "TwoColumnsMidExpanded" : "MidColumnFullScreen";
		} else if (route.name === "bauakteItem") {
			layout = full ? "EndColumnFullScreen" : "ThreeColumnsEndExpanded";
		}
		this.model().setProperty("/layout", layout);
		const tasks: Promise<void>[] = [];
		if (route.name === "bauakten" || route.name === "bauakteItem" || list) {
			tasks.push(this.controllerOf("listView").then((c) => c.show(match)));
		}
		if (route.name !== "bauakten") {
			tasks.push(this.controllerOf("objectView").then((c) => c.show(match)));
		}
		if (route.name === "bauakteItem") {
			tasks.push(this.controllerOf("itemView").then((c) => c.show(match)));
		}
		await Promise.all(tasks);
	}

	onStateChange(): void {
		// Platz für spätere Anpassungen (z. B. Spaltenbreiten merken).
	}
}
