import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import BaseController from "./BaseController";
import { del, get, post, put } from "../model/Api";

interface Plan {
	id?: string;
	name: string;
	description?: string;
	maxUsers: number;
	storageGb: number;
	memoryMb: number;
	cpuLimit: number;
	monthlyPrice?: number | null;
}

/**
 * Tarife: Limits für Benutzer, Speicher, RAM und CPU.
 * @namespace estrichone.hosting.controller
 */
export default class PlansController extends BaseController {
	private readonly plans = new JSONModel({ items: [] });
	private readonly plan = new JSONModel({});

	public onInit(): void {
		this.setModel(this.plans, "plans");
		this.setModel(this.plan, "plan");
		this.getRouter().getRoute("plans")!.attachPatternMatched(() => void this.load());
	}

	private async load(): Promise<void> {
		await this.run(async () => this.plans.setProperty("/items", await get<Plan[]>("/api/plans")));
	}

	public onCreate(): void {
		this.plan.setData({ name: "", description: "", maxUsers: 10, storageGb: 50, memoryMb: 2048, cpuLimit: 2, monthlyPrice: 0 });
		(this.byId("planDialog") as Dialog).open();
	}

	public onEdit(event: Event): void {
		this.plan.setData({ ...((event.getSource() as Control).getBindingContext("plans")!.getObject() as Plan) });
		(this.byId("planDialog") as Dialog).open();
	}

	public async onSave(): Promise<void> {
		const data = this.plan.getData() as Plan;
		const dialog = this.byId("planDialog") as Dialog;
		const result = await this.run(async () => {
			if (data.id) {
				return await put<{ reconfigured: number }>(`/api/plans/${data.id}`, data);
			}
			await post("/api/plans", data);
			return { reconfigured: 0 };
		}, dialog);
		if (result) {
			dialog.close();
			this.toast(result.reconfigured ? `Gespeichert – auf ${result.reconfigured} Instanz(en) angewendet` : "Gespeichert");
			await this.load();
		}
	}

	public onCancel(): void {
		(this.byId("planDialog") as Dialog).close();
	}

	public onDelete(event: Event): void {
		const plan = (event.getSource() as Control).getBindingContext("plans")!.getObject() as Plan;
		MessageBox.confirm(`Tarif „${plan.name}“ löschen?`, {
			onClose: async (choice: string) => {
				if (choice === MessageBox.Action.OK) {
					await this.run(() => del(`/api/plans/${plan.id}`));
					await this.load();
				}
			}
		});
	}
}
