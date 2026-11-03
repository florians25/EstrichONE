/** Zwei-Faktor-Anmeldung und Sitzungen: API-Typen und reine Hilfsfunktionen (ohne UI5, testbar). */

export interface TwoFactorStatus {
	enabled: boolean;
	enabledAt: string | null;
	recoveryCodesRemaining: number;
	requiredByPolicy: boolean;
	restricted: boolean;
}

export interface TwoFactorSetup {
	secret: string;
	otpAuthUri: string;
	digits: number;
	periodSeconds: number;
}

export interface SessionInfo {
	id: string;
	client: "web" | "desktop" | "ios" | "android" | "other";
	clientName: string | null;
	deviceBound: boolean;
	createdAt: string;
	lastSeenAt: string;
	expiresAt: string;
	current: boolean;
}

/** Schlüssel in Vierergruppen – leichter abzutippen. */
export function groupSecret(secret: string): string {
	return secret.replace(/(.{4})/g, "$1 ").trim();
}

/** Lesbarer Name einer Sitzung (Programm und Browser/Gerät). */
export function sessionLabel(s: Pick<SessionInfo, "client" | "clientName">): string {
	const kinds: Record<SessionInfo["client"], string> = { web: "Browser", desktop: "Windows-App", ios: "iPhone/iPad-App", android: "Android-App", other: "Programm" };
	const name = s.clientName?.trim();
	if (s.client === "web" && name) {
		const browser = /Edg\//.test(name) ? "Edge" : /Firefox\//.test(name) ? "Firefox" : /Chrome\//.test(name) ? "Chrome" : /Safari\//.test(name) ? "Safari" : null;
		const system = /Windows/.test(name) ? "Windows" : /iPhone|iPad/.test(name) ? "iOS" : /Mac OS X|Macintosh/.test(name) ? "macOS" : /Android/.test(name) ? "Android" : /Linux/.test(name) ? "Linux" : null;
		const parts = [browser, system].filter(Boolean).join(" auf ");
		return parts ? `Browser (${parts})` : "Browser";
	}
	return name ? `${kinds[s.client]} · ${name}` : kinds[s.client];
}
