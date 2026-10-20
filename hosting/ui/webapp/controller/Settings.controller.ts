import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import BaseController from "./BaseController";
import { del, get, post, put } from "../model/Api";
import { AUTO } from "../util/Theme";

/**
 * Einstellungen: Darstellung (alle Themes, Systemeinstellung), Passwort, TOTP, Administratoren, System.
 * @namespace estrichone.hosting.controller
 */
export default class SettingsController extends BaseController {
	private readonly settings = new JSONModel({});

	public onInit(): void {
		this.setModel(this.settings, "settings");
		this.getRouter().getRoute("settings")!.attachPatternMatched(() => void this.load());
	}

	private async load(): Promise<void> {
		const themes = this.getOwnerComponentTyped().themes;
		this.settings.setData({
			themeAuto: themes.current === AUTO,
			themeSelected: themes.current === AUTO ? themes.model.getProperty("/theme") : themes.current,
			currentPassword: "", newPassword: "", repeatPassword: "", totp: null, totpCode: "", disablePassword: "",
			admins: [], system: {}, versions: []
		});
		await this.run(async () => {
			const [admins, system, versions, consoleUpdate] = await Promise.all([
				get<unknown[]>("/api/admins"),
				get<object>("/api/settings"),
				get<{ versions: string[] }>("/api/versions").catch(() => ({ versions: [] as string[] })),
				get<{ latest?: string; current: string }>("/api/system/console-update").catch(() => null)
			]);
			this.settings.setProperty("/consoleUpdate", consoleUpdate);
			this.settings.setProperty("/consoleTarget", consoleUpdate?.latest ?? "");
			this.settings.setProperty("/admins", admins);
			this.settings.setProperty("/system", system);
			this.settings.setProperty("/versions", versions.versions);
		});
	}

	public async onThemeAuto(): Promise<void> {
		const auto = this.settings.getProperty("/themeAuto") as boolean;
		await this.run(() => this.getOwnerComponentTyped().saveTheme(auto ? AUTO : this.settings.getProperty("/themeSelected")));
	}

	public async onThemeChange(): Promise<void> {
		await this.run(() => this.getOwnerComponentTyped().saveTheme(this.settings.getProperty("/themeSelected")));
	}

	public async onChangePassword(): Promise<void> {
		const { currentPassword, newPassword, repeatPassword } = this.settings.getData();
		if (newPassword !== repeatPassword) {
			MessageBox.error("Die neuen Passwörter stimmen nicht überein.");
			return;
		}
		const done = await this.run(async () => {
			await post("/api/account/password", { currentPassword, newPassword });
			await this.getOwnerComponentTyped().refreshSession();
			return true;
		});
		if (done) {
			this.settings.setProperty("/currentPassword", "");
			this.settings.setProperty("/newPassword", "");
			this.settings.setProperty("/repeatPassword", "");
			this.toast("Passwort geändert – andere Sitzungen wurden abgemeldet");
		}
	}

	public async onTotpBegin(): Promise<void> {
		const setup = await this.run(() => post<{ secret: string; qrSvg: string }>("/api/account/totp/begin"));
		if (setup) {
			const qr = "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(setup.qrSvg)));
			this.settings.setProperty("/totp", { secret: setup.secret.replace(/(.{4})/g, "$1 ").trim(), qr });
		}
	}

	public async onTotpEnable(): Promise<void> {
		const done = await this.run(async () => {
			await post("/api/account/totp/enable", { code: this.settings.getProperty("/totpCode") });
			await this.getOwnerComponentTyped().refreshSession();
			return true;
		});
		if (done) {
			this.settings.setProperty("/totp", null);
			this.settings.setProperty("/totpCode", "");
			MessageBox.success("Zwei-Faktor-Anmeldung ist eingeschaltet. Ab der nächsten Anmeldung wird der Code abgefragt.");
		}
	}

	public async onTotpDisable(): Promise<void> {
		const done = await this.run(async () => {
			await post("/api/account/totp/disable", { password: this.settings.getProperty("/disablePassword") });
			await this.getOwnerComponentTyped().refreshSession();
			return true;
		});
		this.settings.setProperty("/disablePassword", "");
		if (done) {
			this.toast("Zwei-Faktor-Anmeldung ausgeschaltet");
		}
	}

	public onAddAdmin(): void {
		this.settings.setProperty("/newAdmin", { userName: "", displayName: "", password: "" });
		(this.byId("adminDialog") as Dialog).open();
	}

	public async onAdminSave(): Promise<void> {
		const dialog = this.byId("adminDialog") as Dialog;
		const done = await this.run(async () => {
			await post("/api/admins", this.settings.getProperty("/newAdmin"));
			return true;
		}, dialog);
		if (done) {
			dialog.close();
			await this.load();
		}
	}

	public onAdminCancel(): void {
		(this.byId("adminDialog") as Dialog).close();
	}

	public onDeleteAdmin(event: Event): void {
		const admin = (event.getSource() as Control).getBindingContext("settings")!.getObject() as { id: string; userName: string };
		MessageBox.confirm(`Administrator „${admin.userName}“ löschen?`, {
			onClose: async (choice: string) => {
				if (choice === MessageBox.Action.OK) {
					await this.run(() => del(`/api/admins/${admin.id}`));
					await this.load();
				}
			}
		});
	}

	public async onUnlockAdmin(event: Event): Promise<void> {
		const id = (event.getSource() as Control).getBindingContext("settings")!.getProperty("id") as string;
		await this.run(() => post(`/api/admins/${id}/unlock`));
		await this.load();
	}

	public onConsoleUpdate(): void {
		const version = (this.settings.getProperty("/consoleTarget") as string)?.trim();
		if (!version) {
			return;
		}
		MessageBox.confirm(`Konsole auf ${version} aktualisieren? Die Konsole ist dabei etwa eine Minute nicht erreichbar; die Instanzen laufen weiter.`, {
			onClose: async (choice: string) => {
				if (choice !== MessageBox.Action.OK) {
					return;
				}
				const state = await this.run(() => post<{ message: string }>("/api/system/console-update", { defaultVersion: version }));
				if (state) {
					this.settings.setProperty("/consoleUpdate/state", state);
					MessageBox.information(state.message, { title: "Konsole aktualisieren" });
				}
			}
		});
	}

	public async onSaveSystem(): Promise<void> {
		const done = await this.run(async () => {
			await put("/api/settings", { defaultVersion: this.settings.getProperty("/system/defaultVersion") });
			return true;
		});
		if (done) {
			this.toast("Standardversion gespeichert");
		}
	}
}
