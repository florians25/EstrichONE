import JSONModel from "sap/ui/model/json/JSONModel";
import Input from "sap/m/Input";
import BaseController from "./BaseController";
import { ApiError, post } from "../model/Api";

/**
 * Anmeldung (Passwort, optional TOTP) und Ersteinrichtung mit Einmal-Code aus dem Protokoll.
 * @namespace estrichone.hosting.controller
 */
export default class LoginController extends BaseController {
	private readonly login = new JSONModel({ userName: "", password: "", password2: "", setupCode: "", displayName: "", code: "", totpRequired: false, error: "" });

	public onInit(): void {
		this.setModel(this.login, "login");
	}

	public async onSubmit(): Promise<void> {
		const data = this.login.getData();
		const setup = !!this.getOwnerComponentTyped().session.getProperty("/setupRequired");
		this.login.setProperty("/error", "");
		if (!data.userName || !data.password || (setup && !data.setupCode)) {
			this.login.setProperty("/error", "Bitte alle Pflichtfelder ausfüllen.");
			return;
		}
		if (setup && data.password !== data.password2) {
			this.login.setProperty("/error", "Die Passwörter stimmen nicht überein.");
			return;
		}
		const view = this.getView()!;
		view.setBusy(true);
		try {
			if (setup) {
				await post("/api/auth/setup", { setupCode: data.setupCode.trim(), userName: data.userName.trim(), displayName: data.displayName, password: data.password });
			} else {
				await post("/api/auth/login", { userName: data.userName.trim(), password: data.password, code: data.totpRequired ? data.code : null });
			}
			this.login.setData({ userName: "", password: "", password2: "", setupCode: "", displayName: "", code: "", totpRequired: false, error: "" });
			await this.getOwnerComponentTyped().refreshSession();
		} catch (error) {
			if (error instanceof ApiError && error.code === "TOTP_REQUIRED") {
				this.login.setProperty("/totpRequired", true);
				setTimeout(() => (this.byId("totp") as Input).focus(), 0);
				return;
			}
			this.login.setProperty("/error", error instanceof ApiError ? error.detail : String(error));
			this.login.setProperty("/password", data.totpRequired ? data.password : "");
		} finally {
			view.setBusy(false);
		}
	}
}
