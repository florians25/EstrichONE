import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import BaseController from "./BaseController";
import { get } from "../model/Api";

interface Dashboard {
	storageBytes: number;
	system: { diskFreeBytes?: number; diskTotalBytes?: number };
}

/**
 * Übersicht: Kennzahlen, Handlungsbedarf, Serverzustand, Versionen, letzte Ereignisse.
 * @namespace estrichone.hosting.controller
 */
export default class DashboardController extends BaseController {
	private readonly dashboard = new JSONModel({});
	private timer?: ReturnType<typeof setInterval>;

	public onInit(): void {
		this.setModel(this.dashboard, "dashboard");
		this.getRouter().getRoute("dashboard")!.attachPatternMatched(() => {
			void this.load();
			this.timer ??= setInterval(() => {
				if (this.isVisible()) {
					void this.load(true);
				}
			}, 15000);
		});
	}

	public onExit(): void {
		clearInterval(this.timer);
	}

	public onRefresh(): void {
		void this.load();
	}

	private async load(silent = false): Promise<void> {
		const action = async () => {
			const data = await get<Dashboard>("/api/dashboard");
			const used = data.system.diskTotalBytes && data.system.diskFreeBytes !== undefined ? data.system.diskTotalBytes - data.system.diskFreeBytes : null;
			this.dashboard.setData({ ...data, diskUsedBytes: used, loadedAt: new Date().toISOString() });
		};
		if (silent) {
			try {
				await action();
			} catch {
				// Hintergrundaktualisierung: Fehler beim nächsten Versuch erneut.
			}
		} else {
			await this.run(action);
		}
	}

	public onTile(event: Event): void {
		const filter = (event.getSource() as Control).data("filter") as string;
		this.navTo("tenants", filter === "all" ? {} : { "?query": { filter } });
	}

	public onProblem(event: Event): void {
		const id = ((event.getSource() as Control).getBindingContext("dashboard")!.getProperty("tenantId")) as string;
		this.navTo("tenant", { id });
	}

	public onAudit(): void {
		this.navTo("audit");
	}
}
