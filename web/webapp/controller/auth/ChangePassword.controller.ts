import BaseController from "../BaseController";
import { problemMessage } from "../../util/api";
import { session } from "../../util/session";

/** Eigenes Passwort ändern; nach Einmalpasswort Pflicht vor allem anderen. @namespace estrichone.controller.auth */
export default class ChangePassword extends BaseController {
	onInit(): void {
		this.local({ current: "", next: "", repeat: "", error: "", done: false, busy: false });
	}

	onRouteMatched(): void {
		this.model().setData({ current: "", next: "", repeat: "", error: "", done: false, busy: false });
	}

	async onSubmit(): Promise<void> {
		const m = this.model();
		const { current, next, repeat } = m.getData() as { current: string; next: string; repeat: string };
		if (!current || !next) {
			m.setProperty("/error", this.t("setup.missing"));
			return;
		}
		if (next !== repeat) {
			m.setProperty("/error", this.t("password.mismatch"));
			return;
		}
		const forced = session.data.mustChangePassword;
		m.setProperty("/busy", true);
		m.setProperty("/error", "");
		try {
			await session.changePassword(current, next);
			m.setData({ current: "", next: "", repeat: "", error: "", done: !forced, busy: false });
			if (forced) this.nav("/start", true);
		} catch (error) {
			m.setProperty("/error", problemMessage(error));
		} finally {
			m.setProperty("/busy", false);
		}
	}
}
