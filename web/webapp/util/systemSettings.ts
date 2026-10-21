/**
 * Einstellungen der Installation (Einstellungen → System), einmal je Sitzung geladen. Fehlt die Verbindung, gelten
 * die Standards – die Oberfläche bleibt benutzbar.
 */
import { get, put } from "./api";

export interface SystemSettings {
	session: { idleHours: number; rememberDays: number };
	map: { tileUrl: string; attribution: string; maxZoom: number };
	numbering: { bauaktePattern: string };
	bauakte: {
		assignCreator: boolean; warnMissingInspection: boolean; defaultTemplateId: string | null;
		autoStart: "manual" | "firstAssignmentDay" | "firstSiteActivity" | "both"; autoCompleteAfterAcceptance: boolean; acceptanceLinkDays: number;
	};
	approvals: { defaultDueDays: number; defaultRoleId: string | null; requireCommentOnApprove: boolean };
	notifications: { approvalRequested: boolean; approvalDecided: boolean; helpdeskUpdates: boolean; newUserLetterByEmail: boolean };
	appearance: { defaultTheme: string; density: "compact" | "cozy"; showCompanyLogo: boolean };
	pdf: { showCompanyLogo: boolean; showEstrichOneLogo: boolean; accentColor: string; footerText: string | null; photosPerRow: number };
	modules: { materials: boolean };
}

export interface SystemSettingsResponse {
	settings: SystemSettings;
	version: number;
}

export const DEFAULT_TILES = {
	tileUrl: "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
	attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>-Mitwirkende',
	maxZoom: 19,
};

let loaded: Promise<SystemSettingsResponse | null> | null = null;

export function loadSystemSettings(force = false): Promise<SystemSettingsResponse | null> {
	if (force) loaded = null;
	loaded ??= get<SystemSettingsResponse>("settings").catch(() => {
		loaded = null;
		return null;
	});
	return loaded;
}

export async function saveSystemSettings(settings: SystemSettings, version: number): Promise<SystemSettingsResponse> {
	const saved = await put<SystemSettingsResponse>("settings", { settings, version });
	loaded = Promise.resolve(saved);
	return saved;
}

export async function mapTiles(): Promise<typeof DEFAULT_TILES> {
	return (await loadSystemSettings())?.settings.map ?? DEFAULT_TILES;
}
