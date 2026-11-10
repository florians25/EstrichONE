import BaseController from "../BaseController";
import Theming from "sap/ui/core/Theming";
import type Image from "sap/m/Image";
import type Event from "sap/ui/base/Event";
import { ApiError, problemMessage } from "../../util/api";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";
import { logos } from "../../util/theme";
import { readRemember, writeRemember } from "../../util/relogin";

/** @namespace estrichone.controller.auth */
export default class Login extends BaseController {
	private target = "/";

	onInit(): void {
		this.local({ login: "", password: "", code: "", needCode: false, remember: readRemember(), error: "", busy: false, reason: "", showPassword: false, capsLock: false,
			logo: logos(Theming.getTheme()).horizontal });
		Theming.attachApplied(() => this.model().setProperty("/logo", logos(Theming.getTheme()).horizontal));
		// Hinweis auf aktivierte Feststelltaste im Passwortfeld.
		const caps = (k: KeyboardEvent): void => {
			this.model().setProperty("/capsLock", typeof k.getModifierState === "function" && k.getModifierState("CapsLock"));
		};
		this.v().addEventDelegate({
			onAfterRendering: () => {
				const el = this.v().byId("loginPassword")?.getDomRef() as HTMLElement | null | undefined;
				if (el && !el.dataset.eoCaps) {
					el.dataset.eoCaps = "1";
					el.addEventListener("keydown", caps);
					el.addEventListener("keyup", caps);
				}
			},
		});
	}

	/** Passwort ein- und ausblenden. */
	onTogglePassword(): void {
		this.model().setProperty("/showPassword", this.model().getProperty("/showPassword") !== true);
	}

	onRouteMatched(match: RouteMatch): void {
		this.target = match.query.get("ziel") ?? "/";
		this.model().setData({ ...this.model().getData(), password: "", code: "", needCode: false, error: "", busy: false, showPassword: false, capsLock: false, reason: match.query.get("grund") ?? "" });
		void session.loadServer();
		setTimeout(() => this.v().byId("loginUser")?.focus(), 200);
	}

	onLogoError(event: Event): void {
		(event.getSource() as Image).setSrc("/favicon.svg");
	}

	/** Zurück zu Anmeldename und Passwort (z. B. anderes Konto). */
	onCodeBack(): void {
		this.model().setData({ ...this.model().getData(), password: "", code: "", needCode: false, error: "" });
		setTimeout(() => this.v().byId("loginUser")?.focus(), 100);
	}

	async onSubmit(): Promise<void> {
		const model = this.model();
		const login = String(model.getProperty("/login") ?? "").trim();
		const password = String(model.getProperty("/password") ?? "");
		const needCode = model.getProperty("/needCode") === true;
		const code = String(model.getProperty("/code") ?? "").trim();
		if (!login || !password) {
			model.setProperty("/error", this.t("login.missing"));
			return;
		}
		if (needCode && !code) {
			model.setProperty("/error", this.t("login.codeMissing"));
			return;
		}
		model.setProperty("/busy", true);
		model.setProperty("/error", "");
		try {
			const remember = model.getProperty("/remember") === true;
			writeRemember(remember);
			await session.signIn(login, password, remember, needCode ? code : undefined);
			model.setData({ ...model.getData(), password: "", code: "", needCode: false, showPassword: false });
			this.nav(this.target.startsWith("/") && !this.target.startsWith("//") ? this.target : "/", true);
		} catch (error) {
			// Zweiter Schritt: Passwort stimmt, jetzt den Code aus der Authenticator-App abfragen.
			if (error instanceof ApiError && error.code === "TWO_FACTOR_REQUIRED") {
				model.setProperty("/needCode", true);
				setTimeout(() => this.v().byId("loginCode")?.focus(), 100);
				return;
			}
			if (error instanceof ApiError && error.code === "TWO_FACTOR_INVALID") {
				model.setProperty("/code", "");
			} else if (needCode) {
				// Sperre oder falsches Passwort: von vorn.
				model.setData({ ...model.getData(), password: "", code: "", needCode: false });
			}
			model.setProperty("/error", problemMessage(error));
		} finally {
			model.setProperty("/busy", false);
		}
	}
}
