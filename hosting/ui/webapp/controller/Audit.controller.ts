import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import DateRangeSelection from "sap/m/DateRangeSelection";
import BaseController from "./BaseController";
import { get } from "../model/Api";

/**
 * Ereignis-/Audit-Protokoll aller Konsolenaktionen mit Suche, Zeitraum, Ergebnisfilter, Blättern und CSV-Export.
 * @namespace estrichone.hosting.controller
 */
export default class AuditController extends BaseController {
	private readonly audit = new JSONModel({ items: [], total: 0, skip: 0, take: 50, q: "", success: "" });

	public onInit(): void {
		this.setModel(this.audit, "audit");
		this.getRouter().getRoute("audit")!.attachPatternMatched(() => void this.load());
	}

	private query(): URLSearchParams {
		const { q, success, skip, take } = this.audit.getData();
		const params = new URLSearchParams({ skip: String(skip), take: String(take) });
		if (q) params.set("q", q);
		if (success) params.set("success", success);
		const range = this.byId("auditRange") as DateRangeSelection;
		const from = range.getDateValue();
		const to = range.getSecondDateValue();
		if (from) params.set("from", from.toISOString());
		if (to) params.set("to", new Date(to.getTime() + 86_399_000).toISOString());
		return params;
	}

	private async load(): Promise<void> {
		await this.run(async () => {
			const result = await get<{ items: unknown[]; total: number }>(`/api/audit?${this.query()}`);
			this.audit.setProperty("/items", result.items);
			this.audit.setProperty("/total", result.total);
		});
	}

	public onSearch(): void {
		this.audit.setProperty("/skip", 0);
		void this.load();
	}

	public onNext(): void {
		this.audit.setProperty("/skip", this.audit.getProperty("/skip") + this.audit.getProperty("/take"));
		void this.load();
	}

	public onPrevious(): void {
		this.audit.setProperty("/skip", Math.max(0, this.audit.getProperty("/skip") - this.audit.getProperty("/take")));
		void this.load();
	}

	public onExport(): void {
		const params = this.query();
		params.delete("skip");
		params.delete("take");
		window.location.href = `/api/audit/export?${params}`;
	}

	public onTenant(event: Event): void {
		this.navTo("tenant", { id: (event.getSource() as Control).getBindingContext("audit")!.getProperty("tenantId") as string });
	}
}
