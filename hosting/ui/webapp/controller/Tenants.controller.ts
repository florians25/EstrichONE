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
import TenantWizard from "../util/TenantWizard";
import OperationLogDialog from "../util/OperationLogDialog";
import ListItemBase from "sap/m/ListItemBase";
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
	certificate?: { state: string } | null;
}

/**
 * Mandantenliste mit Suche, Filtern, Live-Status; Anlegen und „Alle aktualisieren“.
 * @namespace estrichone.hosting.controller
 */
export default class TenantsController extends BaseController {
	private readonly tenants = new JSONModel({ items: [], count: 0, filter: "all", query: "", selected: 0 });
	private wizard?: TenantWizard;
	private operationLog?: OperationLogDialog;
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
			const selected = new Set(this.selectedIds());
			this.tenants.setProperty("/items", items);
			this.applyFilters();
			// Auswahl für Massenaktionen über die Aktualisierung hinweg behalten.
			for (const item of (this.byId("tenantTable") as Table).getItems()) {
				(item as ListItemBase).setSelected(selected.has(item.getBindingContext("tenants")?.getProperty("id") as string));
			}
			this.onSelectionChange();
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
			case "certificate":
				filters.push(new Filter({ path: "", test: (t: TenantItem) => ["expiring", "invalid", "missing"].includes(t.certificate?.state ?? "") }));
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
		this.wizard ??= new TenantWizard(this.getView()!);
		const created = await this.wizard.open();
		if (created) {
			this.toast("Mandant angelegt – Bereitstellung läuft");
			this.navTo("tenant", { id: created.id });
		}
	}

	private selectedIds(): string[] {
		return (this.byId("tenantTable") as Table).getSelectedItems().map((item) => item.getBindingContext("tenants")!.getProperty("id") as string);
	}

	public onSelectionChange(): void {
		this.tenants.setProperty("/selected", this.selectedIds().length);
	}

	/** Massenaktion (Starten, Stoppen, Neu starten, Sichern) für die ausgewählten Instanzen. */
	public onBulk(event: Event): void {
		const action = (event.getSource() as Control).data("action") as string;
		const ids = this.selectedIds();
		const labels: Record<string, string> = { start: "starten", stop: "stoppen", restart: "neu starten", backup: "jetzt sichern" };
		MessageBox.confirm(`${ids.length} Instanz(en) ${labels[action]}? Nicht passende (z. B. bereits laufende) werden übersprungen.`, {
			title: "Massenaktion",
			emphasizedAction: MessageBox.Action.OK,
			onClose: async (choice: string) => {
				if (choice !== MessageBox.Action.OK) return;
				const result = await this.run(() => post<{ queued: string[]; skipped: string[] }>("/api/tenants/bulk", { tenantIds: ids, action }));
				if (result) {
					this.toast(`${result.queued.length} eingereiht${result.skipped.length ? `, ${result.skipped.length} übersprungen` : ""}`);
					(this.byId("tenantTable") as Table).removeSelections(true);
					this.onSelectionChange();
					await this.load(true);
				}
			}
		});
	}

	public onShowOperation(event: Event): void {
		const id = (event.getSource() as Control).getBindingContext("tenants")!.getProperty("openOperation/id") as string;
		this.operationLog ??= new OperationLogDialog(this.getView()!);
		void this.operationLog.open(id, () => void this.load(true));
	}

	public async onUpdateAll(): Promise<void> {
		const [versions, settings] = await Promise.all([
			get<{ versions: string[]; latest?: string }>("/api/versions").catch(() => ({ versions: [] as string[], latest: undefined })),
			get<{ defaultVersion: string }>("/api/settings")
		]);
		// Vorschlag: neueste veröffentlichte Version (Instanzverwaltung auf dem neuesten Stand), sonst die Standardversion.
		const version = versions.latest ?? settings.defaultVersion;
		this.update.setData({
			versions: versions.versions, version, latest: versions.latest ?? null, defaultVersion: settings.defaultVersion,
			includeStopped: false, backupFirst: true, affected: 0, affectedNames: ""
		});
		if (!this.updateDialog) {
			this.update.attachPropertyChange((event) => {
				const path = event.getParameter("path") as string;
				if (path === "/version" || path === "/includeStopped") {
					void this.countAffected();
				}
			});
			this.updateDialog = (await Fragment.load({ id: this.getView()!.getId(), name: "estrichone.hosting.fragment.UpdateAll", controller: this })) as Dialog;
			this.updateDialog.setModel(this.update, "update");
			this.getView()!.addDependent(this.updateDialog);
		}
		void this.countAffected();
		this.updateDialog.open();
	}

	/** Vorschau vom Server (gleiche Auswahl wie beim Ausführen: nur ältere Versionen, nie eine Rückstufung). */
	private async countAffected(): Promise<void> {
		const { version, includeStopped } = this.update.getData() as { version: string; includeStopped: boolean };
		try {
			const preview = await post<{ affected: { companyName: string; version: string }[] }>("/api/tenants/update-all",
				{ version, includeStopped, dryRun: true });
			if (this.update.getProperty("/version") !== version) {
				return; // inzwischen andere Version gewählt
			}
			this.update.setProperty("/affected", preview.affected.length);
			this.update.setProperty("/affectedNames", preview.affected.map((t) => `${t.companyName} (${t.version})`).join(", "));
		} catch {
			this.update.setProperty("/affected", 0);
			this.update.setProperty("/affectedNames", "");
		}
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
