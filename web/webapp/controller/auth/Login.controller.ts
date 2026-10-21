import BaseController from "../BaseController";
import Theming from "sap/ui/core/Theming";
import type Image from "sap/m/Image";
import type Event from "sap/ui/base/Event";
import { problemMessage } from "../../util/api";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";
import { logos } from "../../util/theme";
import { readRemember, writeRemember } from "../../util/relogin";

/** @namespace estrichone.controller.auth */
export default class Login extends BaseController {
	private target = "/";

	onInit(): void {
		this.local({ login: "", password: "", remember: readRemember(), error: "", busy: false, reason: "", logo: logos(Theming.getTheme()).horizontal });
		Theming.attachApplied(() => this.model().setProperty("/logo", logos(Theming.getTheme()).horizontal));
	}

	onRouteMatched(match: RouteMatch): void {
		this.target = match.query.get("ziel") ?? "/";
		this.model().setData({ ...this.model().getData(), password: "", error: "", busy: false, reason: match.query.get("grund") ?? "" });
		void session.loadServer();
		setTimeout(() => this.v().byId("loginUser")?.focus(), 200);
	}

	onLogoError(event: Event): void {
		(event.getSource() as Image).setSrc("/favicon.svg");
	}

	async onSubmit(): Promise<void> {
		const model = this.model();
		const login = String(model.getProperty("/login") ?? "").trim();
		const password = String(model.getProperty("/password") ?? "");
		if (!login || !password) {
			model.setProperty("/error", this.t("login.missing"));
			return;
		}
		model.setProperty("/busy", true);
		model.setProperty("/error", "");
		try {
			const remember = model.getProperty("/remember") === true;
			writeRemember(remember);
			await session.signIn(login, password, remember);
			model.setProperty("/password", "");
			this.nav(this.target.startsWith("/") && !this.target.startsWith("//") ? this.target : "/", true);
		} catch (error) {
			model.setProperty("/error", problemMessage(error));
		} finally {
			model.setProperty("/busy", false);
		}
	}
}
