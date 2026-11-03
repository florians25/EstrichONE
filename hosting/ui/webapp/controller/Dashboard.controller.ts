import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import BaseController from "./BaseController";
import { get } from "../model/Api";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Table from "sap/m/Table";
import ListBinding from "sap/ui/model/ListBinding";
import TenantWizard from "../util/TenantWizard";

interface Instance {
	id: string; companyName: string; slug: string; domain: string; state: string; health: string; problems: string[]; outdated: boolean;
	backupHealthy?: boolean | null; certificate?: { state: string; error?: string | null } | null; lastError?: string | null;
	setupRequired?: boolean | null; accessLetterAvailable?: boolean; openOperation?: { label: string } | null;
	rank?: number; hints?: string[];
}

/** Hinweise je Instanz für die Übersicht und Rang (3 = Fehler, 1 = Hinweis, 0 = in Ordnung) zum Sortieren. */
export function describeInstance(t: Instance): Instance {
	const hints: string[] = [];
	let rank = 0;
	if (t.openOperation) hints.push(`${t.openOperation.label} läuft`);
	if (t.state === "Failed" && t.lastError) { hints.push(t.lastError); rank = 3; }
	if (t.state === "Running" && t.health === "Unhealthy") rank = 3;
	if (t.state === "Running" && (t.health === "Unhealthy" || t.health === "Degraded")) { hints.push(...t.problems); rank = Math.max(rank, t.health === "Unhealthy" ? 3 : 1); }
	if (t.backupHealthy === false) rank = Math.max(rank, 1);
	if (["expiring", "invalid", "missing"].includes(t.certificate?.state ?? "")) { hints.push(t.certificate?.error ?? "Zertifikat prüfen"); rank = Math.max(rank, t.certificate?.state === "expiring" ? 1 : 2); }
	if (t.outdated) { hints.push("Aktualisierung verfügbar"); rank = Math.max(rank, 1); }
	if (t.setupRequired) hints.push("Ersteinrichtung offen");
	if (t.accessLetterAvailable) hints.push("Zugangsbrief noch nicht verworfen");
	if (t.state === "Locked") { hints.push("gesperrt"); rank = Math.max(rank, 1); }
	return { ...t, hints: [...new Set(hints)], rank };
}

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
	private readonly instances = new JSONModel({ items: [], filter: "all", warnings: 0 });
	private wizard?: TenantWizard;
	private timer?: ReturnType<typeof setInterval>;

	public onInit(): void {
		this.setModel(this.dashboard, "dashboard");
		this.setModel(this.instances, "instances");
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
			const items = (await get<Instance[]>("/api/tenants")).map(describeInstance);
			this.instances.setProperty("/items", items);
			this.instances.setProperty("/warnings", items.filter((i) => (i.rank ?? 0) > 0).length);
			this.onInstanceSearch();
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

	public formatStorage(used?: number | null, limit?: number | null): string {
		return this.formatter.storageText(used, limit);
	}

	public onInstanceSearch(): void {
		const query = ((this.byId("instanceSearch") as unknown as { getValue(): string }).getValue() ?? "").trim();
		const filters: Filter[] = [];
		if (query) {
			filters.push(new Filter({ filters: ["companyName", "slug", "domain", "version"].map((path) => new Filter({ path, operator: FilterOperator.Contains, value1: query, caseSensitive: false })), and: false }));
		}
		if (this.instances.getProperty("/filter") === "warnings") {
			filters.push(new Filter({ path: "rank", operator: FilterOperator.GT, value1: 0 }));
		}
		((this.byId("instanceTable") as Table).getBinding("items") as ListBinding).filter(filters);
	}

	public onInstance(event: Event): void {
		this.navTo("tenant", { id: (event.getSource() as Control).getBindingContext("instances")!.getProperty("id") as string });
	}

	public async onCreate(): Promise<void> {
		this.wizard ??= new TenantWizard(this.getView()!);
		const created = await this.wizard.open();
		if (created) this.navTo("tenant", { id: created.id });
	}
}
