import BaseController from "../BaseController";
import Theming from "sap/ui/core/Theming";
import type Image from "sap/m/Image";
import type Event from "sap/ui/base/Event";
import { post, problemMessage } from "../../util/api";
import { session } from "../../util/session";
import { logos } from "../../util/theme";

/** Ersteinrichtung mit dem Einmalcode aus dem Server-Protokoll. @namespace estrichone.controller.auth */
export default class Setup extends BaseController {
	onInit(): void {
		this.local({ setupCode: "", companyName: "", displayName: "", userName: "", email: "", password: "", error: "", busy: false,
			logo: logos(Theming.getTheme()).horizontal });
		Theming.attachApplied(() => this.model().setProperty("/logo", logos(Theming.getTheme()).horizontal));
	}

	onLogoError(event: Event): void {
		(event.getSource() as Image).setSrc("/favicon.svg");
	}

	async onSubmit(): Promise<void> {
		const m = this.model();
		const v = m.getData() as Record<string, string>;
		if (![v.setupCode, v.companyName, v.displayName, v.userName, v.password].every((x) => x?.trim())) {
			m.setProperty("/error", this.t("setup.missing"));
			return;
		}
		m.setProperty("/busy", true);
		m.setProperty("/error", "");
		try {
			await post("setup", {
				setupCode: v.setupCode.trim(), companyName: v.companyName.trim(),
				administrator: { displayName: v.displayName.trim(), userName: v.userName.trim(), email: v.email.trim() || null, password: v.password },
			});
			await session.signIn(v.userName.trim(), v.password);
			await session.loadServer();
			this.nav("/start", true);
		} catch (error) {
			m.setProperty("/error", problemMessage(error));
		} finally {
			m.setProperty("/busy", false);
		}
	}
}
