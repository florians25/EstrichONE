/**
 * Anmeldezustand im Browser: Benutzer und Rechte (Tokens nur in HttpOnly-Cookies). Als JSONModel „session“
 * an allen Views verfügbar, z. B. visible="{= ${session>/permissions/bauakten.create} }".
 */
import JSONModel from "sap/ui/model/json/JSONModel";
import { api, get, post } from "./api";
import type { DirectoryUser, ServerInfo, UserSummary, WebSessionResponse } from "./models";

export interface SessionData {
	ready: boolean;
	user: UserSummary | null;
	permissions: Record<string, boolean>;
	mustChangePassword: boolean;
	server: ServerInfo | null;
	people: DirectoryUser[];
}

const empty = (): SessionData => ({ ready: false, user: null, permissions: {}, mustChangePassword: false, server: null, people: [] });

class Session {
	readonly model = new JSONModel(empty());
	private peopleLoaded: Promise<DirectoryUser[]> | null = null;

	get data(): SessionData {
		return this.model.getData() as SessionData;
	}

	has(permission: string): boolean {
		return this.data.permissions[permission] === true;
	}

	get userId(): string | null {
		return this.data.user?.id ?? null;
	}

	private async loadPermissions(): Promise<void> {
		const list = await get<string[]>("access/me/permissions");
		this.model.setProperty("/permissions", Object.fromEntries(list.map((p) => [p, true])));
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

	async signIn(login: string, password: string, rememberMe = false): Promise<void> {
		const session = await post<WebSessionResponse>("auth/web/login", { login, password, rememberMe });
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
		this.model.setProperty("/mustChangePassword", false);
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

export const session = new Session();
