import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, post } from "../../util/api";
import { dateTime } from "../../util/format";
import { openForm } from "../../util/forms";
import { session } from "../../util/session";
import { confirmSetup, sessionLabel, showRecoveryCodes, type SessionInfo, type TwoFactorSetup, type TwoFactorStatus } from "../../util/twoFactor";

/** Konto → Anmeldung und Sicherheit: Zwei-Faktor-Anmeldung und eigene Sitzungen. @namespace estrichone.controller.auth */
export default class Security extends BaseController {
	onInit(): void {
		this.local({ status: null, stateText: "", sessions: [] });
	}

	async onRouteMatched(): Promise<void> {
		await this.run(() => this.load());
	}

	private async load(): Promise<void> {
		const [status, sessions] = await Promise.all([get<TwoFactorStatus>("auth/two-factor"), get<SessionInfo[]>("auth/sessions")]);
		const m = this.model();
		m.setProperty("/status", status);
		m.setProperty("/stateText", status.enabled
			? this.t("security.enabled", [dateTime(status.enabledAt), status.recoveryCodesRemaining])
			: this.t("security.disabled"));
		m.setProperty("/sessions", sessions.map((s) => ({ ...s, label: sessionLabel(s), since: dateTime(s.createdAt), seen: dateTime(s.lastSeenAt) })));
	}

	async onSetup(): Promise<void> {
		const setup = await openForm<TwoFactorSetup>({
			title: this.t("security.setup"), submitText: "Weiter",
			intro: this.t("security.twoFactorIntro"),
			fields: [{ key: "password", label: this.t("security.password"), type: "password", required: true }],
			submit: (v) => post<TwoFactorSetup>("auth/two-factor/setup", { currentPassword: v.password }),
		});
		if (!setup) return;
		const codes = await confirmSetup(setup);
		if (!codes) return;
		await showRecoveryCodes(codes, session.data.user?.userName ?? "");
		this.toast(this.t("security.enabledDone"));
		// Rechte können sich ändern (Pflicht für Administratoren erfüllt).
		await session.reloadPermissions();
		await this.run(() => this.load());
	}

	private confirmWithCode(title: string, submitText: string, danger = false): Promise<{ currentPassword: string; code: string } | null> {
		return openForm({
			title, submitText, danger,
			fields: [
				{ key: "currentPassword", label: this.t("security.password"), type: "password", required: true },
				{ key: "code", label: this.t("security.code"), required: true, maxLength: 32, help: this.t("login.codeHint") },
			],
			submit: async (v) => ({ currentPassword: String(v.currentPassword), code: String(v.code).trim() }),
		});
	}

	async onRenew(): Promise<void> {
		const proof = await this.confirmWithCode(this.t("security.renew"), this.t("security.renew"));
		if (!proof) return;
		const result = await this.run(() => post<{ recoveryCodes: string[] }>("auth/two-factor/recovery-codes", proof));
		if (!result) return;
		await showRecoveryCodes(result.recoveryCodes, session.data.user?.userName ?? "");
		await this.run(() => this.load());
	}

	async onDisable(): Promise<void> {
		const proof = await this.confirmWithCode(this.t("security.disable"), this.t("security.disable"), true);
		if (!proof) return;
		const done = await this.run(() => post("auth/two-factor/disable", proof), this.t("security.disabledDone"));
		if (done !== undefined) await this.run(() => this.load());
	}

	async onSignOutOne(event: Event): Promise<void> {
		const item = (event.getSource() as Control).getBindingContext("view")?.getObject() as SessionInfo | undefined;
		if (!item) return;
		await this.run(() => del(`auth/sessions/${item.id}`));
		await this.run(() => this.load());
	}

	async onSignOutOthers(): Promise<void> {
		if (!(await this.confirm(this.t("security.sessionsIntro"), { title: this.t("security.signOutOthers"), action: this.t("security.signOutOthers"), danger: true }))) return;
		const result = await this.run(() => post<{ count: number }>("auth/sessions/revoke-others"));
		if (result) this.toast(this.t("security.signOutOthersDone", [result.count]));
		await this.run(() => this.load());
	}
}
