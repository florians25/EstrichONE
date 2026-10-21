import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { api, del, get, put } from "../../util/api";
import { catalog } from "../../util/catalog";
import type { CompanyResponse, RoleResponse } from "../../util/models";
import { resetCompany } from "../../util/orderDialogs";
import { session } from "../../util/session";
import { loadSystemSettings, saveSystemSettings, type SystemSettings } from "../../util/systemSettings";
import { themes } from "../../util/theme";

interface LogoInfo {
	hasLogo: boolean;
	width: number;
	height: number;
	updatedAt: string | null;
}

/** Beispiel zum Nummernkreis: {JJJJ}, {JJ}, {N…}. */
export function numberExample(pattern: string, year = new Date().getFullYear(), number = 7): string {
	return pattern.replace(/\{JJJJ\}/g, String(year)).replace(/\{JJ\}/g, String(year).slice(-2))
		.replace(/\{(N+)\}/g, (_m, n: string) => String(number).padStart(n.length, "0"));
}

/**
 * Einstellungen der Installation, sauber gruppiert: Firma und Logo, Bauakten (Nummernkreis, Vorgaben, Automatik),
 * Dokumente und Freigaben, Anmeldung, Benachrichtigungen, Darstellung und Karte, PDF-Layout, Module, System.
 * @namespace estrichone.controller.admin
 */
export default class Company extends BaseController {
	private version = 0;

	onInit(): void {
		this.local({ c: {}, s: null, loaded: false, busy: false, tab: "firma", logo: { hasLogo: false }, logoUrl: "", templates: [], roles: [],
			themes, patternExample: "" });
		this.model().attachPropertyChange((event) => {
			if (String(event.getParameter("path")).includes("bauaktePattern")) this.updateExample();
		});
	}

	private updateExample(): void {
		this.model().setProperty("/patternExample", numberExample(String(this.model().getProperty("/s/numbering/bauaktePattern") ?? "")));
	}

	async onRouteMatched(): Promise<void> {
		this.model().setProperty("/busy", true);
		try {
			const [c, settings, logo, templates, roles] = await Promise.all([
				get<CompanyResponse>("company"),
				loadSystemSettings(true),
				get<LogoInfo>("company/logo/info").catch(() => ({ hasLogo: false, width: 0, height: 0, updatedAt: null })),
				catalog("bauakteTemplate").catch(() => []),
				get<RoleResponse[]>("access/roles").catch(() => []),
			]);
			this.version = settings?.version ?? 0;
			const m = this.model();
			m.setProperty("/c", c);
			m.setProperty("/s", settings?.settings ?? null);
			m.setProperty("/logo", logo);
			m.setProperty("/logoUrl", `/api/v1/company/logo?v=${encodeURIComponent(logo.updatedAt ?? "")}`);
			m.setProperty("/templates", [{ key: "", text: "Keine Vorlage" }, ...templates.map((t) => ({ key: t.id, text: t.name }))]);
			m.setProperty("/roles", [{ key: "", text: "Keine" }, ...roles.map((r) => ({ key: r.id, text: r.name }))]);
			m.setProperty("/loaded", !!settings);
			this.updateExample();
		} catch (error) {
			this.showError(error);
		} finally {
			this.model().setProperty("/busy", false);
		}
	}

	async onSave(): Promise<void> {
		const m = this.model();
		const c = m.getProperty("/c") as CompanyResponse;
		const s = m.getProperty("/s") as SystemSettings;
		if (!c.name?.trim()) {
			m.setProperty("/tab", "firma");
			this.toast("Bitte den Firmennamen angeben.");
			return;
		}
		if (!/\{N+\}/.test(s.numbering.bauaktePattern)) {
			m.setProperty("/tab", "bauakte");
			this.toast("Der Nummernkreis braucht eine laufende Nummer, z. B. {NNN}.");
			return;
		}
		const clean = {
			...s,
			bauakte: { ...s.bauakte, defaultTemplateId: s.bauakte.defaultTemplateId || null },
			approvals: { ...s.approvals, defaultRoleId: s.approvals.defaultRoleId || null },
			pdf: { ...s.pdf, footerText: s.pdf.footerText || null },
		};
		m.setProperty("/busy", true);
		try {
			const [savedCompany, savedSettings] = await Promise.all([
				put<CompanyResponse>("company", { ...c, email: c.email || null }),
				saveSystemSettings(clean, this.version),
			]);
			resetCompany();
			this.version = savedSettings.version;
			m.setProperty("/c", savedCompany);
			m.setProperty("/s", savedSettings.settings);
			await session.reloadPermissions();
			this.toast("Einstellungen gespeichert");
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onLogoUpload(): void {
		const input = document.createElement("input");
		input.type = "file";
		input.accept = "image/png,image/jpeg,image/webp";
		input.addEventListener("change", async () => {
			const file = input.files?.[0];
			if (!file) return;
			try {
				const logo = await api<LogoInfo>("company/logo", { method: "PUT", raw: file, headers: { "Content-Type": file.type || "application/octet-stream" } });
				this.model().setProperty("/logo", logo);
				this.model().setProperty("/logoUrl", `/api/v1/company/logo?v=${encodeURIComponent(logo.updatedAt ?? String(Date.now()))}`);
				this.toast("Logo gespeichert");
				document.dispatchEvent(new CustomEvent("estrichone-logo"));
			} catch (error) {
				this.showError(error);
			}
		});
		input.click();
	}

	async onLogoDelete(): Promise<void> {
		if (!(await this.confirm("Firmenlogo entfernen? Ausdrucke zeigen dann wieder das EstrichONE-Logo.", { title: "Logo entfernen", action: "Entfernen", danger: true }))) return;
		try {
			await del("company/logo");
			this.model().setProperty("/logo", { hasLogo: false, width: 0, height: 0, updatedAt: null });
			document.dispatchEvent(new CustomEvent("estrichone-logo"));
		} catch (error) {
			this.showError(error);
		}
	}

	onLink(event: Event): void {
		const path = (event.getSource() as Control).data("path") as string;
		this.nav(path);
	}
}
