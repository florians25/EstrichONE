import Fragment from "sap/ui/core/Fragment";
import Dialog from "sap/m/Dialog";
import JSONModel from "sap/ui/model/json/JSONModel";
import View from "sap/ui/core/mvc/View";
import Control from "sap/ui/core/Control";
import Input from "sap/m/Input";
import MessageBox from "sap/m/MessageBox";
import { ValueState } from "sap/ui/core/library";
import { ApiError, get, post, put } from "../model/Api";

export interface TenantData {
	id?: string;
	companyName?: string;
	customerNumber?: string | null;
	contactName?: string | null;
	contactEmail?: string | null;
	contactPhone?: string | null;
	slug?: string;
	domain?: string;
	planId?: string;
	version?: string;
	notes?: string | null;
}

const FIELDS: Record<string, string> = {
	companyName: "fCompany",
	customerNumber: "fCustomer",
	contactName: "fContact",
	contactEmail: "fEmail",
	contactPhone: "fPhone",
	slug: "fSlug",
	domain: "fDomain",
	planId: "fPlan",
	version: "fVersion",
	notes: "fNotes"
};

/** Dialog „Mandant anlegen/bearbeiten“ mit Live-Vorschlag für Kürzel und Domain und Feldfehlern vom Server. */
export default class TenantFormDialog {
	private dialog?: Dialog;
	private readonly model = new JSONModel({});
	private slugTouched = false;
	private suggestTimer?: ReturnType<typeof setTimeout>;
	private resolve?: (id: string | null) => void;

	constructor(private readonly view: View) {}

	private byId(id: string): Control {
		return Fragment.byId(this.view.getId() + "--tenantForm", id) as Control;
	}

	async open(existing?: TenantData): Promise<string | null> {
		if (!this.dialog) {
			this.dialog = (await Fragment.load({ id: this.view.getId() + "--tenantForm", name: "estrichone.hosting.fragment.TenantForm", controller: this })) as Dialog;
			this.dialog.setModel(this.model, "form");
			this.view.addDependent(this.dialog);
		}
		const [plans, versions, settings] = await Promise.all([
			get<{ id: string; name: string }[]>("/api/plans"),
			get<{ versions: string[] }>("/api/versions").catch(() => ({ versions: [] as string[] })),
			get<{ defaultVersion: string }>("/api/settings")
		]);
		this.slugTouched = !!existing;
		this.model.setData({
			edit: !!existing,
			plans,
			versions: versions.versions,
			companyName: existing?.companyName ?? "",
			customerNumber: existing?.customerNumber ?? "",
			contactName: existing?.contactName ?? "",
			contactEmail: existing?.contactEmail ?? "",
			contactPhone: existing?.contactPhone ?? "",
			slug: existing?.slug ?? "",
			domain: existing?.domain ?? "",
			suggestedDomain: existing?.domain ?? "",
			planId: existing?.planId ?? plans[0]?.id ?? "",
			version: settings.defaultVersion,
			notes: existing?.notes ?? "",
			provisionNow: true,
			id: existing?.id
		});
		this.clearStates();
		this.dialog.open();
		return new Promise((resolve) => (this.resolve = resolve));
	}

	private clearStates(): void {
		for (const id of Object.values(FIELDS)) {
			(this.byId(id) as Input | undefined)?.setValueState?.(ValueState.None);
		}
	}

	public onCompanyChange(): void {
		if (!this.slugTouched) {
			this.suggest(false);
		}
	}

	public onSlugChange(): void {
		this.slugTouched = true;
		this.suggest(true);
	}

	private suggest(fromSlug: boolean): void {
		clearTimeout(this.suggestTimer);
		this.suggestTimer = setTimeout(async () => {
			const data = this.model.getData();
			const params = new URLSearchParams({ company: data.companyName ?? "" });
			if (fromSlug && data.slug) {
				params.set("slug", data.slug);
			}
			try {
				const result = await get<{ slug: string; domain: string; slugError?: string }>(`/api/tenants/suggest?${params}`);
				if (!fromSlug) {
					this.model.setProperty("/slug", result.slug);
				}
				this.model.setProperty("/suggestedDomain", result.domain);
				const slugInput = this.byId("fSlug") as Input;
				slugInput.setValueState(fromSlug && result.slugError ? ValueState.Error : ValueState.None);
				slugInput.setValueStateText(result.slugError ?? "");
			} catch {
				// Vorschlag ist eine Komfortfunktion.
			}
		}, 250);
	}

	public async onSave(): Promise<void> {
		const data = this.model.getData();
		const body = {
			companyName: data.companyName,
			customerNumber: data.customerNumber,
			contactName: data.contactName,
			contactEmail: data.contactEmail,
			contactPhone: data.contactPhone,
			slug: data.slug,
			domain: data.domain || null,
			planId: data.planId || null,
			version: data.version,
			notes: data.notes,
			provisionNow: data.provisionNow
		};
		this.clearStates();
		this.dialog!.setBusy(true);
		try {
			let id = data.id as string;
			if (data.edit) {
				await put(`/api/tenants/${id}`, body);
			} else {
				id = (await post<{ id: string }>("/api/tenants", body)).id;
			}
			this.dialog!.close();
			this.resolve?.(id);
		} catch (error) {
			if (error instanceof ApiError) {
				for (const [field, message] of Object.entries(error.fields)) {
					const input = this.byId(FIELDS[field]) as Input | undefined;
					input?.setValueState?.(ValueState.Error);
					input?.setValueStateText?.(message);
				}
				MessageBox.error(error.detail, { title: "Bitte prüfen" });
			} else {
				MessageBox.error(String(error));
			}
		} finally {
			this.dialog!.setBusy(false);
		}
	}

	public onCancel(): void {
		this.dialog!.close();
		this.resolve?.(null);
	}
}
