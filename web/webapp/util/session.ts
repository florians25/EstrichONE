/**
 * Anmeldezustand im Browser: Benutzer und Rechte (Tokens nur in HttpOnly-Cookies). Als JSONModel „session“
 * an allen Views verfügbar, z. B. visible="{= ${session>/permissions/bauakten.create} }".
 */
import { valueLists } from "./valueLists";
import JSONModel from "sap/ui/model/json/JSONModel";
import { api, ApiError, get, post } from "./api";
import { loadSystemSettings } from "./systemSettings";
import { disabledModuleOfPath, filterPermissions, moduleOn, pathEnabled, type ModuleStates } from "./modules";
import type { DirectoryUser, ServerInfo, UserSummary, WebSessionResponse } from "./models";
import type { TeamsStatus } from "./teams";

export interface SessionData {
	ready: boolean;
	user: UserSummary | null;
	permissions: Record<string, boolean>;
	mustChangePassword: boolean;
	server: ServerInfo | null;
	people: DirectoryUser[];
	/** Schalter der Module (Einstellungen › Module, dazu „chat“); fehlende gelten als an. */
	modules: ModuleStates;
	/** Microsoft Teams: Chat-Modus und Team „Baustellen“ (null = nicht geladen bzw. Server ohne Teams-Anbindung). */
	teams?: TeamsStatus | null;
}

const empty = (): SessionData => ({ ready: false, user: null, permissions: {}, mustChangePassword: false, server: null, people: [], modules: {} });

class Session {
	readonly model = new JSONModel(empty());
	private peopleLoaded: Promise<DirectoryUser[]> | null = null;

	get data(): SessionData {
		return this.model.getData() as SessionData;
	}

	has(permission: string): boolean {
		return this.data.permissions[permission] === true;
	}

	/** Modul eingeschaltet (Einstellungen › Module)? */
	moduleOn(key: string): boolean {
		return moduleOn(this.data.modules ?? {}, key);
	}

	/** Ziel (Pfad mit Query) gehört zu keinem abgeschalteten Modul – für Kacheln, Karten, Navigation und Suche. */
	pathEnabled(target: string): boolean {
		return pathEnabled(this.data.modules ?? {}, target);
	}

	/** Abgeschaltetes Modul der Seite, sonst null. */
	disabledModuleOf(target: string): string | null {
		return disabledModuleOfPath(this.data.modules ?? {}, target);
	}

	get userId(): string | null {
		return this.data.user?.id ?? null;
	}

	private async loadPermissions(): Promise<void> {
		try {
			await this.loadPermissionsNow();
		} catch (error) {
			// Einmalpasswort: Der Server gibt bis zum Wechsel nichts heraus – nur den Passwortwechsel anbieten.
			if (!(error instanceof ApiError && error.code === "PASSWORD_CHANGE_REQUIRED")) throw error;
			this.model.setProperty("/mustChangePassword", true);
			this.model.setProperty("/permissions", {});
		}
	}

	private async loadPermissionsNow(): Promise<void> {
		const [list, settings, , chat, twoFactor, teams] = await Promise.all([get<string[]>("access/me/permissions"), loadSystemSettings(true), valueLists(true),
			get<{ enabled: boolean }>("chat/status").catch(() => ({ enabled: false })),
			get<{ restricted: boolean }>("auth/two-factor").catch(() => ({ restricted: false })),
			get<TeamsStatus>("teams/status").catch(() => null)]);
		this.model.setProperty("/teams", teams);
		// Zwei-Faktor-Pflicht für Administratoren noch nicht erfüllt: Hinweis in der Kopfzeile (Verwaltungsrechte ruhen).
		this.model.setProperty("/twoFactorRestricted", twoFactor.restricted);
		// Abgeschaltete Module (Einstellungen → Module): Rechte des Moduls ausblenden – Navigation, Reiter und Kacheln verschwinden.
		const modules: ModuleStates = { ...(settings?.settings.modules ?? {}), chat: chat.enabled };
		this.model.setProperty("/modules", modules);
		this.model.setProperty("/permissions", Object.fromEntries(filterPermissions(modules, list).map((p) => [p, true])));
	}

	/** Nach dem Speichern der Einstellungen: Module und Rechte neu anwenden. */
	async reloadPermissions(): Promise<void> {
		await this.loadPermissions();
		document.dispatchEvent(new CustomEvent("estrichone-permissions"));
	}

	async loadServer(): Promise<ServerInfo | null> {
		try {
			const info = await get<ServerInfo>("server-info");
			this.model.setProperty("/server", info);
			return info;
		} catch {
			return null;
		}
	}

	/** Beim Start: angemeldet? (stille Prüfung, ohne „Sitzung abgelaufen“). */
	async restore(): Promise<void> {
		try {
			this.model.setProperty("/user", await api<UserSummary>("auth/me", { quiet: true }));
			await this.loadPermissions();
		} catch {
			this.clear();
		} finally {
			this.model.setProperty("/ready", true);
		}
	}

	async signIn(login: string, password: string, rememberMe = false, twoFactorCode?: string): Promise<void> {
		const session = await post<WebSessionResponse>("auth/web/login", { login, password, rememberMe, twoFactorCode });
		// Anmeldung angenommen, aber der Browser hat die Sitzungs-Cookies verworfen: sofort klar melden, statt dass jede
		// Folgeanfrage mit 401 endet und die Anmeldung „hängt“ (Fehlerbild 0.1.0).
		if (!hasSessionCookie()) throw cookieProblem();
		this.model.setProperty("/user", session.user);
		this.model.setProperty("/mustChangePassword", session.mustChangePassword);
		await this.loadPermissions();
		this.model.setProperty("/ready", true);
		this.peopleLoaded = null;
	}

	async signOut(): Promise<void> {
		try {
			await post("auth/web/logout");
		} finally {
			this.clear();
		}
	}

	async changePassword(currentPassword: string, newPassword: string): Promise<void> {
		await post("auth/change-password", { currentPassword, newPassword });
		const forced = this.data.mustChangePassword;
		this.model.setProperty("/mustChangePassword", false);
		// Nach dem Pflichtwechsel gibt der Server Rechte und Einstellungen frei.
		if (forced) await this.loadPermissions();
	}

	clear(): void {
		const server = this.data.server;
		this.model.setData({ ...empty(), ready: true, server });
		this.peopleLoaded = null;
	}

	/** Mitarbeiterverzeichnis (Namen zu IDs), einmal je Sitzung geladen. */
	people(): Promise<DirectoryUser[]> {
		this.peopleLoaded ??= get<DirectoryUser[]>("directory/users").then((list) => {
			this.model.setProperty("/people", list);
			return list;
		}).catch(() => {
			this.peopleLoaded = null;
			return [];
		});
		return this.peopleLoaded;
	}

	name(userId: string | null | undefined): string {
		return (userId && this.data.people.find((p) => p.id === userId)?.displayName) || "–";
	}
}

/** Der CSRF-Cookie (nicht HttpOnly) kommt mit den Sitzungs-Cookies; fehlt er, hat der Browser sie nicht gespeichert. */
export function hasSessionCookie(): boolean {
	return document.cookie.split("; ").some((c) => c.startsWith("XSRF-TOKEN="));
}

/** Verständliche Ursache, warum der Browser die Anmeldung nicht behält. */
export function cookieProblem(location: Pick<Location, "protocol" | "host"> = window.location): ApiError {
	const insecure = location.protocol === "http:";
	const message = insecure
		? `Die Anmeldung wurde angenommen, aber der Browser speichert die Sitzung über die unverschlüsselte Adresse http://${location.host} nicht. `
			+ "Bitte EstrichONE über die HTTPS-Adresse aufrufen (z. B. die Domain im Nginx Proxy Manager) aufrufen, nicht direkt über IP und Port."
		: "Die Anmeldung wurde angenommen, aber der Browser hat die Sitzung nicht gespeichert. Bitte Cookies für diese Seite erlauben "
			+ "(privater Modus, Tracking-Schutz, Erweiterungen) sowie Datum und Uhrzeit dieses Geräts prüfen und es erneut versuchen.";
	return new ApiError(0, "SESSION_COOKIE_REJECTED", message);
}

export const session = new Session();
