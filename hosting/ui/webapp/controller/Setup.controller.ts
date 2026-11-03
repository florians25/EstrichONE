import JSONModel from "sap/ui/model/json/JSONModel";
import NavContainer from "sap/m/NavContainer";
import Input from "sap/m/Input";
import Control from "sap/ui/core/Control";
import BaseController from "./BaseController";
import TenantWizard from "../util/TenantWizard";
import { get, post } from "../model/Api";
import { loadNotifications, notificationBody, testNotifications } from "../util/NotificationSettings";

const STEPS = ["Passwort", "Zwei-Faktor", "Version", "Sicherung", "Benachrichtigungen", "Fertig"];
const PAGES = ["spPassword", "spTotp", "spVersion", "spBackup", "spNotify", "spDone"];

interface WizardState {
	completed: boolean; mustChangePassword: boolean; totpEnabled: boolean; userName: string; defaultVersion: string;
	secondaryRepository?: string | null; backupRoot: string; archiveDirectory: string;
}

/**
 * Einrichtungsassistent nach der ersten Anmeldung: Passwort (Pflicht bei Konten aus dem Installer), Zwei-Faktor optional,
 * Standardversion, zweites Sicherungsziel, Benachrichtigungen.
 * @namespace estrichone.hosting.controller
 */
export default class SetupController extends BaseController {
	private readonly setup = new JSONModel({ step: 0, steps: STEPS.map((title, index) => ({ title, index })) });
	private readonly notify = new JSONModel({});
	private wizard?: TenantWizard;

	public onInit(): void {
		this.setModel(this.setup, "setup");
		this.setModel(this.notify, "notify");
		this.getRouter().getRoute("setup")!.attachPatternMatched(() => void this.load());
	}

	private async load(): Promise<void> {
		await this.run(async () => {
			const [state, versions] = await Promise.all([
				get<WizardState>("/api/setup-wizard"),
				get<{ versions: string[]; source: string; error?: string }>("/api/versions").catch(() => ({ versions: [] as string[], source: "–" }))
			]);
			// Mit Startpasswort sperrt der Server alles außer dem Passwortwechsel – Einstellungen erst danach laden.
			if (!state.mustChangePassword) await loadNotifications(this.notify).catch(() => undefined);
			this.setup.setData({
				...this.setup.getData(), ...state, secondaryRepository: state.secondaryRepository ?? "",
				versions: versions.versions, versionSource: versions.source, currentPassword: "", newPassword: "", repeatPassword: "", totp: null, totpCode: ""
			});
			this.go(state.completed ? 5 : 0);
		});
	}

	private go(step: number): void {
		this.setup.setProperty("/step", step);
		(this.byId("setupPages") as NavContainer).to(this.byId(PAGES[step]) as Control);
	}

	private fieldError(id: string, text: string): void {
		const input = this.byId(id) as Input;
		input.setValueState("Error");
		input.setValueStateText(text);
		input.focus();
	}

	public onBack(): void {
		this.go(Math.max(0, (this.setup.getProperty("/step") as number) - 1));
	}

	public onSkip(): void {
		const step = this.setup.getProperty("/step") as number;
		if (step === 4) {
			void this.finish(false);
		} else {
			this.go(step + 1);
		}
	}

	public async onNext(): Promise<void> {
		const step = this.setup.getProperty("/step") as number;
		const d = this.setup.getData();
		if (step === 0 && d.mustChangePassword) {
			if ((d.newPassword ?? "").length < 12) return this.fieldError("sNew", "Mindestens 12 Zeichen.");
			if (d.newPassword !== d.repeatPassword) return this.fieldError("sRepeat", "Die Passwörter stimmen nicht überein.");
			const done = await this.run(async () => {
				await post("/api/account/password", { currentPassword: d.currentPassword, newPassword: d.newPassword });
				// Neue Sitzung (Passwortwechsel beendet alte Sitzungen) – CSRF-Token neu holen.
				await this.getOwnerComponentTyped().refreshSession();
				return true;
			});
			this.setup.setProperty("/currentPassword", "");
			this.setup.setProperty("/newPassword", "");
			this.setup.setProperty("/repeatPassword", "");
			if (!done) return;
			this.setup.setProperty("/mustChangePassword", false);
			this.getOwnerComponentTyped().session.setProperty("/mustChangePassword", false);
			this.toast("Passwort geändert");
			await loadNotifications(this.notify).catch(() => undefined);
			const versions = await get<{ versions: string[]; source: string }>("/api/versions").catch(() => null);
			if (versions) {
				this.setup.setProperty("/versions", versions.versions);
				this.setup.setProperty("/versionSource", versions.source);
			}
		}
		if (step === 4) {
			await this.finish(true);
			return;
		}
		this.go(step + 1);
	}

	private async finish(withNotifications: boolean): Promise<void> {
		const d = this.setup.getData();
		const done = await this.run(async () => {
			await post("/api/setup-wizard/complete", {
				defaultVersion: d.defaultVersion, secondaryRepository: d.secondaryRepository,
				notifications: withNotifications ? notificationBody(this.notify) : null
			});
			return true;
		});
		if (done) {
			this.setup.setProperty("/completed", true);
			this.getOwnerComponentTyped().session.setProperty("/setupWizardOpen", false);
			this.go(5);
		}
	}

	public async onTotpBegin(): Promise<void> {
		const setup = await this.run(() => post<{ secret: string; qrSvg: string }>("/api/account/totp/begin"));
		if (setup) {
			const qr = "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(setup.qrSvg)));
			this.setup.setProperty("/totp", { secret: setup.secret.replace(/(.{4})/g, "$1 ").trim(), qr });
		}
	}

	public async onTotpEnable(): Promise<void> {
		const done = await this.run(async () => {
			await post("/api/account/totp/enable", { code: this.setup.getProperty("/totpCode") });
			return true;
		});
		if (done) {
			this.setup.setProperty("/totpEnabled", true);
			this.setup.setProperty("/totp", null);
			this.toast("Zwei-Faktor-Anmeldung eingeschaltet");
		}
	}

	public async onNotifyTest(): Promise<void> {
		// Erst speichern (Teil des Abschlusses), dann testen.
		const message = await this.run(async () => {
			await post("/api/setup-wizard/complete", { defaultVersion: this.setup.getProperty("/defaultVersion"),
				secondaryRepository: this.setup.getProperty("/secondaryRepository"), notifications: notificationBody(this.notify) });
			return testNotifications();
		}, this.byId("spNotify") as Control);
		if (message) this.toast(`Gesendet: ${message}`);
	}

	public async onCreateTenant(): Promise<void> {
		this.wizard ??= new TenantWizard(this.getView()!);
		const created = await this.wizard.open();
		if (created) this.navTo("tenant", { id: created.id });
	}

	public onDashboard(): void {
		this.navTo("dashboard");
	}
}
