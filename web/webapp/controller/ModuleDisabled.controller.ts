import BaseController from "./BaseController";
import { get } from "../util/api";
import type { ModuleInfo } from "../util/modules";
import type { RouteMatch } from "../util/router";
import { session } from "../util/session";

/** Chat ist kein Teil des Server-Katalogs (eigene Einstellungen unter Module › Chat). */
const fallback: Record<string, string> = { chat: "Chat" };

/**
 * Hinweisseite für Seiten abgeschalteter Module (Einstellungen › Module), z. B. nach einem Lesezeichen oder Link aus einer
 * älteren Benachrichtigung. Die Adresse bleibt stehen; Administratoren springen direkt zu den Modulen.
 * @namespace estrichone.controller
 */
export default class ModuleDisabled extends BaseController {
	onInit(): void {
		this.local({ key: "", title: "", text: "" });
	}

	async onRouteMatched(match?: RouteMatch): Promise<void> {
		const key = match?.params.module ?? "";
		const m = this.model();
		m.setProperty("/key", key);
		m.setProperty("/title", fallback[key] ?? key);
		m.setProperty("/text", this.text(null));
		const modules = await get<{ modules: ModuleInfo[] }>("settings/modules").then((r) => r.modules).catch(() => [] as ModuleInfo[]);
		const info = modules.find((x) => x.key === key);
		if (info) m.setProperty("/title", info.title);
		m.setProperty("/text", this.text(info ?? null));
	}

	private text(info: ModuleInfo | null): string {
		const admin = session.has("settings.manage");
		return `${info ? `${info.description} ` : ""}Das Modul wurde in den Systemeinstellungen ausgeschaltet. Vorhandene Daten bleiben erhalten und sind wieder da, sobald es eingeschaltet wird.`
			+ (admin ? " Sie können es unter Systemeinstellungen › Module wieder einschalten." : " Bei Bedarf bitte an die Verwaltung wenden.");
	}

	onManage(): void {
		this.nav("/system/firmendaten?bereich=module");
	}

	onHome(): void {
		this.nav("/start");
	}
}
