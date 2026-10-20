import JSONModel from "sap/ui/model/json/JSONModel";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Table from "sap/m/Table";
import ListBinding from "sap/ui/model/ListBinding";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import Fragment from "sap/ui/core/Fragment";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import BaseController from "./BaseController";
import TenantFormDialog from "../util/TenantFormDialog";
import { get, post } from "../model/Api";

interface TenantItem {
	id: string;
	companyName: string;
	slug: string;
	domain: string;
	customerNumber?: string;
	contactName?: string;
	contactEmail?: string;
	state: string;
	health: string;
	outdated: boolean;
	version: string;
	backupHealthy?: boolean | null;
	createdAt: string;
}

/**
 * Mandantenliste mit Suche, Filtern, Live-Status; Anlegen und „Alle aktualisieren“.
 * @namespace estrichone.hosting.controller
 */
export default class TenantsController extends BaseController {
	private readonly tenants = new JSONModel({ items: [], count: 0, filter: "all", query: "" });
	private form?: TenantFormDialog;
	private updateDialog?: Dialog;
	private readonly update = new JSONModel({});
	private timer?: ReturnType<typeof setInterval>;

	public onInit(): void {
		this.setModel(this.tenants, "tenants");
		this.getRouter().getRoute("tenants")!.attachPatternMatched((event) => {
			const query = (event.getParameter("arguments") as { "?query"?: { filter?: string } })["?query"];
			this.tenants.setProperty("/filter", query?.filter ?? "all");
			void this.load();
			this.timer ??= setInterval(() => {
				if (this.isVisible()) {
					void this.load(true);
				}
			}, 10000);
		});
	}

	public onExit(): void {
		clearInterval(this.timer);
	}

	public formatStorage(used?: number | null, limit?: number | null): string {
		return this.formatter.storageText(used, limit);
	}

	private async load(silent = false): Promise<void> {
		const action = async () => {
			const items = await get<TenantItem[]>("/api/tenants");
			this.tenants.setProperty("/items", items);
			this.applyFilters();
		};
		if (silent) {
			await action().catch(() => undefined);
		} else {
			await this.run(action, this.byId("tenantTable") as Control);
		}
	}

	public onRefresh(): void {
		void this.load();
	}

	public onSearch(event: Event): void {
		this.tenants.setProperty("/query", (event.getParameter("newValue" as never) ?? event.getParameter("query" as never) ?? "") as string);
		this.applyFilters();
	}

	public applyFilters(): void {
		const query = (this.tenants.getProperty("/query") as string).trim();
		const filter = this.tenants.getProperty("/filter") as string;
		const filters: Filter[] = [];
		if (query) {
			filters.push(new Filter({
				filters: ["companyName", "slug", "domain", "customerNumber", "contactName", "contactEmail"].map(
					(path) => new Filter({ path, operator: FilterOperator.Contains, value1: query, caseSensitive: false })),
				and: false
			}));
		}
		switch (filter) {
			case "problems":
				filters.push(new Filter({ path: "", test: (t: TenantItem) => t.state === "Failed" || t.health === "Unhealthy" || t.health === "Degraded" }));
				break;
			case "outdated":
				filters.push(new Filter("outdated", FilterOperator.EQ, true));
				break;
			case "backup":
				filters.push(new Filter({ path: "", test: (t: TenantItem) => t.backupHealthy === false }));
				break;
			case "all":
				break;
			default:
				filters.push(new Filter("state", FilterOperator.EQ, filter));
		}
		const binding = (this.byId("tenantTable") as Table).getBinding("items") as ListBinding;
		binding.filter(filters);
		this.tenants.setProperty("/count", binding.getLength());
	}

	public onOpen(event: Event): void {
		const id = (event.getSource() as Control).getBindingContext("tenants")!.getProperty("id") as string;
		this.navTo("tenant", { id });
	}

	public async onCreate(): Promise<void> {
		this.form ??= new TenantFormDialog(this.getView()!);
		const id = await this.form.open();
		if (id) {
			this.toast("Mandant angelegt – Bereitstellung läuft");
			this.navTo("tenant", { id });
		}
	}

	public async onUpdateAll(): Promise<void> {
		const [versions, settings] = await Promise.all([
			get<{ versions: string[]; latest?: string }>("/api/versions").catch(() => ({ versions: [] as string[], latest: undefined })),
			get<{ defaultVersion: string }>("/api/settings")
		]);
		this.update.setData({ versions: versions.versions, version: settings.defaultVersion, includeStopped: false, backupFirst: true, affected: 0 });
		this.update.attachPropertyChange(() => this.countAffected());
		this.countAffected();
		if (!this.updateDialog) {
			this.updateDialog = (await Fragment.load({ id: this.getView()!.getId(), name: "estrichone.hosting.fragment.UpdateAll", controller: this })) as Dialog;
			this.updateDialog.setModel(this.update, "update");
			this.getView()!.addDependent(this.updateDialog);
		}
		this.updateDialog.open();
	}

	private countAffected(): void {
		const version = this.update.getProperty("/version") as string;
		const includeStopped = !!this.update.getProperty("/includeStopped");
		const items = this.tenants.getProperty("/items") as (TenantItem & { state: string })[];
		const affected = items.filter((t) => t.version !== version && (includeStopped || t.state !== "Stopped") && t.state !== "Provisioning").length;
		this.update.setProperty("/affected", affected);
	}

	public async onUpdateAllConfirm(): Promise<void> {
		const { version, includeStopped, backupFirst } = this.update.getData();
		this.updateDialog!.close();
		const result = await this.run(() => post<{ queued: number; skipped: string[] }>("/api/tenants/update-all", { version, includeStopped, backupFirst }));
		if (result) {
			MessageBox.information(
				`${result.queued} Aktualisierung(en) eingereiht.` + (result.skipped.length ? `\nÜbersprungen (laufender Vorgang): ${result.skipped.join(", ")}` : ""),
				{ title: "Alle aktualisieren" });
			void this.load();
		}
	}

	public onUpdateAllCancel(): void {
		this.updateDialog!.close();
	}
}
