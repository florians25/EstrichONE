import Fragment from "sap/ui/core/Fragment";
import Dialog from "sap/m/Dialog";
import JSONModel from "sap/ui/model/json/JSONModel";
import View from "sap/ui/core/mvc/View";
import TextArea from "sap/m/TextArea";
import formatter from "../model/formatter";
import { get } from "../model/Api";

/** Protokoll eines Vorgangs; solange er läuft, wird es jede Sekunde nachgeladen (live). */
export default class OperationLogDialog {
	public readonly formatter = formatter;
	private dialog?: Dialog;
	private readonly model = new JSONModel({});
	private timer?: ReturnType<typeof setInterval>;
	private onFinished?: () => void;

	constructor(private readonly view: View) {}

	async open(operationId: string, onFinished?: () => void): Promise<void> {
		this.onFinished = onFinished;
		if (!this.dialog) {
			this.dialog = (await Fragment.load({ id: this.view.getId() + "--operationLog", name: "estrichone.hosting.fragment.OperationLog", controller: this })) as Dialog;
			this.dialog.setModel(this.model, "op");
			this.view.addDependent(this.dialog);
		}
		this.model.setData({ id: operationId, log: "" });
		await this.load();
		this.dialog.open();
		clearInterval(this.timer);
		this.timer = setInterval(() => void this.load(), 1000);
	}

	private async load(): Promise<void> {
		const id = this.model.getProperty("/id") as string;
		try {
			const operation = await get<{ status: string; log: string }>(`/api/operations/${id}`);
			const wasOpen = ["Queued", "Running"].includes(this.model.getProperty("/status") as string);
			this.model.setData(operation);
			const area = Fragment.byId(this.view.getId() + "--operationLog", "operationLogText") as TextArea | undefined;
			const dom = area?.getFocusDomRef() as HTMLTextAreaElement | null | undefined;
			if (dom) {
				dom.scrollTop = dom.scrollHeight;
			}
			if (!["Queued", "Running"].includes(operation.status)) {
				clearInterval(this.timer);
				if (wasOpen) {
					this.onFinished?.();
				}
			}
		} catch {
			clearInterval(this.timer);
		}
	}

	public onOperationClose(): void {
		clearInterval(this.timer);
		this.dialog?.close();
	}
}
