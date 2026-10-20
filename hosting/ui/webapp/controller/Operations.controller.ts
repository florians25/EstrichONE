import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import BaseController from "./BaseController";
import OperationLogDialog from "../util/OperationLogDialog";
import { get } from "../model/Api";

/**
 * Alle Vorgänge (Bereitstellungen, Aktualisierungen …) mit Live-Protokoll.
 * @namespace estrichone.hosting.controller
 */
export default class OperationsController extends BaseController {
	private readonly ops = new JSONModel({ items: [] });
	private logDialog?: OperationLogDialog;
	private timer?: ReturnType<typeof setInterval>;

	public onInit(): void {
		this.setModel(this.ops, "ops");
		this.getRouter().getRoute("operations")!.attachPatternMatched(() => {
			void this.load();
			this.timer ??= setInterval(() => {
				if (this.isVisible()) {
					void this.load(true);
				}
			}, 5000);
		});
	}

	public onExit(): void {
		clearInterval(this.timer);
	}

	public duration(started?: string | null, finished?: string | null): string {
		if (!started) {
			return "–";
		}
		const seconds = Math.round(((finished ? new Date(finished) : new Date()).getTime() - new Date(started).getTime()) / 1000);
		return seconds < 60 ? `${seconds} s` : `${Math.floor(seconds / 60)} min ${seconds % 60} s`;
	}

	private async load(silent = false): Promise<void> {
		const action = async () => this.ops.setProperty("/items", await get<unknown[]>("/api/operations"));
		if (silent) {
			await action().catch(() => undefined);
		} else {
			await this.run(action);
		}
	}

	public onRefresh(): void {
		void this.load();
	}

	public onOpen(event: Event): void {
		this.logDialog ??= new OperationLogDialog(this.getView()!);
		void this.logDialog.open((event.getSource() as Control).getBindingContext("ops")!.getProperty("operation/id") as string, () => void this.load(true));
	}

	public onTenant(event: Event): void {
		this.navTo("tenant", { id: (event.getSource() as Control).getBindingContext("ops")!.getProperty("tenantId") as string });
	}
}
