import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { api, del, get, post, put } from "../../util/api";
import { catalog } from "../../util/catalog";
import type { CompanyResponse, RoleResponse } from "../../util/models";
import { resetCompany } from "../../util/orderDialogs";
import { session } from "../../util/session";
import { loadSystemSettings, saveSystemSettings, type SystemSettings } from "../../util/systemSettings";
import { themes } from "../../util/theme";
import type { RouteMatch } from "../../util/router";
import { sendTest, type PushSettings } from "../../util/notifications";
import type { SegmentedButton$SelectionChangeEvent } from "sap/m/SegmentedButton";

interface StorageStats {
	kinds: { kind: string; files: number; bytes: number; uploadedBytes: number; optimized: number }[];
	storedBytes: number;
	savedBytes: number;
	duplicateFiles: number;
	duplicateBytes: number;
	originalsDiskBytes: number;
	variantsDiskBytes: number;
	freeDiskBytes: number | null;
	candidates: number;
	job: { running: boolean; processed: number; optimized: number; savedBytes: number; skipped: number; failed: number; startedAt: string | null; finishedAt: string | null };
}

const bytes = (n: number) => n >= 1024 ** 3 ? `${(n / 1024 ** 3).toLocaleString("de-DE", { maximumFractionDigits: 1 })} GB`
	: n >= 1024 ** 2 ? `${(n / 1024 ** 2).toLocaleString("de-DE", { maximumFractionDigits: 1 })} MB`
	: `${Math.round(n / 1024).toLocaleString("de-DE")} KB`;

interface PushForm extends PushSettings {
	vapidSubjectInput: string;
	apnsKey: string | null;
	removeApnsKey: boolean;
}

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
			themes: [{ id: "system", text: "Systemeinstellung folgen (hell/dunkel)" }, ...themes], patternExample: "" });
		this.model().attachPropertyChange((event) => {
			if (String(event.getParameter("path")).includes("bauaktePattern")) this.updateExample();
		});
	}

	private updateExample(): void {
		this.model().setProperty("/patternExample", numberExample(String(this.model().getProperty("/s/numbering/bauaktePattern") ?? "")));
	}

	async onRouteMatched(match?: RouteMatch): Promise<void> {
		const tab = match?.query.get("bereich");
		if (tab) this.model().setProperty("/tab", tab);
		this.model().setProperty("/busy", true);
		try {
			void this.loadPush();
			void this.loadStorage();
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

	private async loadPush(): Promise<void> {
		const push = await get<PushSettings>("notifications/settings").catch(() => null);
		this.model().setProperty("/push", push ? this.pushForm(push) : null);
	}

	private pushForm(push: PushSettings): PushForm {
		const defaultSubject = push.vapidSubject;
		return { ...push, vapidSubjectInput: defaultSubject.startsWith("mailto:") || defaultSubject.startsWith("https://") ? defaultSubject : "", apnsKey: null, removeApnsKey: false };
	}

	private async savePush(): Promise<void> {
		const p = this.model().getProperty("/push") as PushForm | null;
		if (!p) return;
		const saved = await put<PushSettings>("notifications/settings", {
			webPushEnabled: p.webPushEnabled, vapidSubject: p.vapidSubjectInput || null, apnsEnabled: p.apnsEnabled, apnsKeyId: p.apnsKeyId || null,
			apnsTeamId: p.apnsTeamId || null, apnsBundleId: p.apnsBundleId || null, apnsProduction: p.apnsProduction, apnsKey: p.apnsKey,
			removeApnsKey: p.removeApnsKey, showContent: p.showContent, version: p.version,
		});
		this.model().setProperty("/push", this.pushForm(saved));
	}

	private storageTimer: ReturnType<typeof setTimeout> | undefined;

	private async loadStorage(): Promise<void> {
		const stats = await get<StorageStats>("files/storage").catch(() => null);
		if (!stats) return;
		const kinds: Record<string, string> = { photo: "Fotos", video: "Videos", document: "Dokumente" };
		const job = stats.job;
		this.model().setProperty("/storage", {
			...stats,
			storedText: `${bytes(stats.storedBytes)} in ${stats.kinds.reduce((n, k) => n + k.files, 0)} Dateien`,
			savedText: stats.savedBytes > 0 ? `${bytes(stats.savedBytes)} durch Verdichtung` : "noch nichts",
			duplicateText: stats.duplicateFiles ? `${stats.duplicateFiles} Dateien teilen sich die Ablage (${bytes(stats.duplicateBytes)} gespart)` : "keine",
			diskText: `Originale ${bytes(stats.originalsDiskBytes)}, Vorschaustufen ${bytes(stats.variantsDiskBytes)}${stats.freeDiskBytes != null ? ` · frei ${bytes(stats.freeDiskBytes)}` : ""}`,
			kinds: stats.kinds.map((k) => ({ ...k, label: kinds[k.kind] ?? k.kind, bytesText: bytes(k.bytes), uploadedText: bytes(k.uploadedBytes) })),
			jobText: job.running ? `läuft: ${job.processed} geprüft, ${job.optimized} verdichtet (${bytes(job.savedBytes)} gespart)`
				: job.finishedAt ? `zuletzt: ${job.optimized} verdichtet, ${bytes(job.savedBytes)} gespart, ${job.skipped} übersprungen${job.failed ? `, ${job.failed} Fehler` : ""}`
				: "bisher nicht gelaufen",
		});
		clearTimeout(this.storageTimer);
		if (job.running) this.storageTimer = setTimeout(() => void this.loadStorage(), 2000);
	}

	async onOptimizeStorage(): Promise<void> {
		if (!(await this.confirm("Bestehende Fotos und PDFs jetzt im Hintergrund verdichten? Archivierte Dateien bleiben unverändert. Die Arbeit läuft weiter, auch wenn Sie die Seite verlassen.",
			{ title: "Nachverdichten", action: "Starten" }))) return;
		try {
			await post("files/storage/optimize");
			await this.loadStorage();
		} catch (error) {
			this.showError(error);
		}
	}

	onStorageRefresh(): void {
		void this.loadStorage();
	}

	onApnsEnvironment(event: SegmentedButton$SelectionChangeEvent): void {
		this.model().setProperty("/push/apnsProduction", event.getParameter("item")?.getKey() === "prod");
	}

	onApnsKey(): void {
		const input = document.createElement("input");
		input.type = "file";
		input.accept = ".p8,application/pkcs8,text/plain";
		input.addEventListener("change", async () => {
			const file = input.files?.[0];
			if (!file) return;
			const text = await file.text();
			if (!text.includes("BEGIN PRIVATE KEY")) {
				this.toast("Das ist keine .p8-Datei von Apple (PRIVATE KEY erwartet).");
				return;
			}
			this.model().setProperty("/push/apnsKey", text);
			this.model().setProperty("/push/removeApnsKey", false);
			const id = /AuthKey_([A-Z0-9]{10})\.p8/i.exec(file.name)?.[1];
			if (id && !this.model().getProperty("/push/apnsKeyId")) this.model().setProperty("/push/apnsKeyId", id.toUpperCase());
		});
		input.click();
	}

	onApnsKeyRemove(): void {
		this.model().setProperty("/push/apnsKey", null);
		this.model().setProperty("/push/removeApnsKey", true);
		this.model().setProperty("/push/apnsKeyStored", false);
	}

	async onPushTest(): Promise<void> {
		try {
			await sendTest();
			this.toast("Testnachricht gesendet – Postfach und Pushnachricht prüfen.");
			document.dispatchEvent(new CustomEvent("estrichone-notices"));
		} catch (error) {
			this.showError(error);
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
				this.savePush(),
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
