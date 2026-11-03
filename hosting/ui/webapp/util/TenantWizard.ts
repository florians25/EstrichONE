import Fragment from "sap/ui/core/Fragment";
import Dialog from "sap/m/Dialog";
import NavContainer from "sap/m/NavContainer";
import JSONModel from "sap/ui/model/json/JSONModel";
import View from "sap/ui/core/mvc/View";
import Control from "sap/ui/core/Control";
import Input from "sap/m/Input";
import MessageBox from "sap/m/MessageBox";
import Event from "sap/ui/base/Event";
import { ValueState } from "sap/ui/core/library";
import { ApiError, get, post } from "../model/Api";

interface Plan { id: string; name: string; description?: string; maxUsers: number; storageGb: number; memoryMb: number; cpuLimit: number }

const STEPS = ["Firma", "Adresse", "Ressourcen", "Admin-Person", "Prüfen"];
const PAGES = ["wpCompany", "wpAddress", "wpResources", "wpAdmin", "wpSummary"];
/** Feld → Eingabe und Schritt (Fehler vom Server springen zum passenden Schritt). */
const FIELDS: Record<string, [string, number]> = {
	companyName: ["wCompany", 0], customerNumber: ["wCustomer", 0], contactName: ["wContact", 0], contactEmail: ["wEmail", 0],
	contactPhone: ["wPhone", 0], notes: ["wNotes", 0], slug: ["wSlug", 1], domain: ["wDomain", 1], planId: ["wPlans", 2],
	version: ["wVersion", 2], firstAdminName: ["wAdminName", 3], firstAdminUserName: ["wAdminUser", 3], firstAdminEmail: ["wAdminEmail", 3]
};

/** Benutzername aus dem Namen: „Petra Müller“ → „petra.mueller“. */
export function suggestUserName(name: string): string {
	const ascii = name.trim().toLowerCase().replace(/ä/g, "ae").replace(/ö/g, "oe").replace(/ü/g, "ue").replace(/ß/g, "ss")
		.normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, ".").replace(/^\.+|\.+$/g, "");
	const value = /^[a-z]/.test(ascii) ? ascii : ascii ? `a${ascii}` : "";
	return value.slice(0, 32).replace(/\.+$/, "");
}

/** Assistent „Instanz anlegen“: Firma → Adresse → Ressourcen → erste Admin-Person → Prüfen. */
export default class TenantWizard {
	private dialog?: Dialog;
	private readonly model = new JSONModel({});
	private slugTouched = false;
	private userTouched = false;
	private suggestTimer?: ReturnType<typeof setTimeout>;
	private resolve?: (result: { id: string; operationId?: string } | null) => void;

	constructor(private readonly view: View) {}

	private byId(id: string): Control {
		return Fragment.byId(this.view.getId() + "--wizard", id) as Control;
	}

	async open(): Promise<{ id: string; operationId?: string } | null> {
		if (!this.dialog) {
			this.dialog = (await Fragment.load({ id: this.view.getId() + "--wizard", name: "estrichone.hosting.fragment.TenantWizard", controller: this })) as Dialog;
			this.dialog.setModel(this.model, "wiz");
			this.view.addDependent(this.dialog);
		}
		const [plans, versions, settings, wizard] = await Promise.all([
			get<Plan[]>("/api/plans"),
			get<{ versions: string[] }>("/api/versions").catch(() => ({ versions: [] as string[] })),
			get<{ defaultVersion: string }>("/api/settings"),
			get<{ baseDomain: string }>("/api/setup-wizard").catch(() => ({ baseDomain: "" }))
		]);
		this.slugTouched = false;
		this.userTouched = false;
		this.model.setData({
			step: 0,
			steps: STEPS.map((title, index) => ({ title, index })),
			plans, versions: versions.versions, baseDomain: wizard.baseDomain,
			companyName: "", customerNumber: "", contactName: "", contactEmail: "", contactPhone: "", notes: "",
			slug: "", domain: "", suggestedDomain: "", domainMode: 0,
			planId: plans[0]?.id ?? "", planName: plans[0]?.name ?? "", version: settings.defaultVersion,
			createAdmin: true, firstAdminName: "", firstAdminUserName: "", firstAdminEmail: "", provisionNow: true
		});
		this.clearStates();
		(this.byId("wizardPages") as NavContainer).to(this.byId(PAGES[0]));
		this.dialog.open();
		return new Promise((resolve) => (this.resolve = resolve));
	}

	private clearStates(): void {
		for (const [id] of Object.values(FIELDS)) {
			(this.byId(id) as Input | undefined)?.setValueState?.(ValueState.None);
		}
	}

	private error(id: string, text: string): false {
		const input = this.byId(id) as Input;
		input.setValueState(ValueState.Error);
		input.setValueStateText(text);
		input.focus();
		return false;
	}

	/** Prüfung im Browser vor dem Weiterblättern (die verbindliche Prüfung macht der Server). */
	private validate(step: number): boolean {
		const d = this.model.getData();
		this.clearStates();
		if (step === 0) {
			if (!d.companyName?.trim()) return this.error("wCompany", "Bitte den Firmennamen angeben.");
			if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(d.contactEmail ?? "")) return this.error("wEmail", "Bitte eine gültige E-Mail-Adresse angeben.");
		}
		if (step === 1) {
			if (!/^[a-z][a-z0-9-]{1,30}[a-z0-9]$/.test(d.slug ?? "")) return this.error("wSlug", "3–32 Zeichen a–z, 0–9 und Bindestrich, beginnt mit Buchstabe.");
			if (d.domainMode === 1 && !/^([a-z0-9]([a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,}$/.test((d.domain ?? "").trim().toLowerCase())) return this.error("wDomain", "Bitte eine gültige Domain angeben.");
		}
		if (step === 2 && !d.version?.trim()) return this.error("wVersion", "Bitte eine Version wählen.");
		if (step === 3 && d.createAdmin) {
			if (!d.firstAdminName?.trim()) return this.error("wAdminName", "Bitte den Namen angeben.");
			if (!/^[a-z][a-z0-9._-]{2,31}$/.test(d.firstAdminUserName ?? "")) return this.error("wAdminUser", "3–32 Zeichen a–z, 0–9, Punkt, Bindestrich, Unterstrich.");
		}
		return true;
	}

	private go(step: number): void {
		this.model.setProperty("/step", step);
		(this.byId("wizardPages") as NavContainer).to(this.byId(PAGES[step]));
		if (step === 3 && !this.model.getProperty("/firstAdminName")) {
			const d = this.model.getData();
			this.model.setProperty("/firstAdminName", d.contactName ?? "");
			this.model.setProperty("/firstAdminEmail", d.contactEmail ?? "");
			if (!this.userTouched) this.model.setProperty("/firstAdminUserName", suggestUserName(d.contactName || "admin"));
		}
	}

	public onNext(): void {
		const step = this.model.getProperty("/step") as number;
		if (this.validate(step)) {
			this.go(step + 1);
		}
	}

	public onBack(): void {
		this.go(Math.max(0, (this.model.getProperty("/step") as number) - 1));
	}

	public onPlanSelect(event: Event): void {
		const item = (event.getParameter("listItem" as never) as Control);
		const plan = item.getBindingContext("wiz")!.getObject() as Plan;
		this.model.setProperty("/planId", plan.id);
		this.model.setProperty("/planName", plan.name);
	}

	public onCompanyChange(): void {
		if (!this.slugTouched) this.suggest(false);
	}

	public onSlugChange(): void {
		this.slugTouched = true;
		this.suggest(true);
	}

	public onAdminNameChange(): void {
		if (!this.userTouched) this.model.setProperty("/firstAdminUserName", suggestUserName(this.model.getProperty("/firstAdminName") as string));
	}

	public onAdminUserChange(): void {
		this.userTouched = true;
	}

	private suggest(fromSlug: boolean): void {
		clearTimeout(this.suggestTimer);
		this.suggestTimer = setTimeout(async () => {
			const data = this.model.getData();
			const params = new URLSearchParams({ company: data.companyName ?? "" });
			if (fromSlug && data.slug) params.set("slug", data.slug);
			try {
				const result = await get<{ slug: string; domain: string; slugError?: string }>(`/api/tenants/suggest?${params}`);
				if (!fromSlug) this.model.setProperty("/slug", result.slug);
				this.model.setProperty("/suggestedDomain", result.domain);
				const slugInput = this.byId("wSlug") as Input;
				slugInput.setValueState(fromSlug && result.slugError ? ValueState.Error : ValueState.None);
				slugInput.setValueStateText(result.slugError ?? "");
			} catch {
				// Vorschlag ist eine Komfortfunktion.
			}
		}, 250);
	}

	public async onCreate(): Promise<void> {
		const d = this.model.getData();
		const body = {
			companyName: d.companyName, customerNumber: d.customerNumber, contactName: d.contactName, contactEmail: d.contactEmail,
			contactPhone: d.contactPhone, notes: d.notes, slug: d.slug, domain: d.domainMode === 1 ? d.domain : null, planId: d.planId,
			version: d.version, provisionNow: d.createAdmin ? true : d.provisionNow,
			firstAdminName: d.createAdmin ? d.firstAdminName : null,
			firstAdminUserName: d.createAdmin ? d.firstAdminUserName : null,
			firstAdminEmail: d.createAdmin ? d.firstAdminEmail : null
		};
		this.dialog!.setBusy(true);
		try {
			const created = await post<{ id: string; openOperation?: { id: string } | null }>("/api/tenants", body);
			this.dialog!.close();
			this.resolve?.({ id: created.id, operationId: created.openOperation?.id });
		} catch (error) {
			if (error instanceof ApiError) {
				let first: number | undefined;
				for (const [field, message] of Object.entries(error.fields)) {
					const target = FIELDS[field];
					if (!target) continue;
					first = Math.min(first ?? target[1], target[1]);
					const input = this.byId(target[0]) as Input;
					input.setValueState?.(ValueState.Error);
					input.setValueStateText?.(message);
				}
				if (first !== undefined) this.go(first);
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
