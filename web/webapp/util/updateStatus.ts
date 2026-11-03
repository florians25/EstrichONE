/**
 * Hinweis „Sicherheitsupdate verfügbar“ für Administratoren (GET /api/v1/system/updates, Recht backup.manage;
 * docs/sicherheit.md, „Sicherheitsupdates“). Reine Logik ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */
export interface AutoUpdateStatus {
	mode: string;
	window: string;
	checkedAt?: string | null;
	lastAt?: string | null;
	lastFrom?: string | null;
	lastTo?: string | null;
	result?: string | null;
	message?: string | null;
}

export interface UpdateStatus {
	installed: string;
	available?: string | null;
	securityUpdate: boolean;
	latest?: string | null;
	releaseName?: string | null;
	releaseUrl?: string | null;
	publishedAt?: string | null;
	checkedAt?: string | null;
	source: string;
	error?: string | null;
	command: string;
	auto?: AutoUpdateStatus | null;
}

export interface UpdateHint {
	key: string;
	type: "Error" | "Warning" | "Information";
	text: string;
	url: string;
}

const autoText = (auto: AutoUpdateStatus | null | undefined, security: boolean) =>
	auto && (auto.mode === "patch" || (auto.mode === "sicherheit" && security))
		? ` Wird automatisch im Zeitfenster ${auto.window} eingespielt.`
		: "";

/**
 * Anzuzeigender Hinweis oder null: Sicherheitsupdate rot (bis zum Einspielen nicht dauerhaft ausblendbar), normales Update
 * derselben Versionslinie als Information, fehlgeschlagene Automatik gelb.
 */
export function updateHint(status: UpdateStatus | null | undefined): UpdateHint | null {
	if (!status) return null;
	const url = status.releaseUrl ?? "";
	if (status.available && status.securityUpdate) {
		return {
			key: `security:${status.available}`, type: "Error", url,
			text: `Sicherheitsupdate verfügbar: EstrichONE ${status.available} (installiert ${status.installed}). Bitte zeitnah einspielen – auf dem Server: ${status.command}.${autoText(status.auto, true)}`,
		};
	}
	if (status.auto?.result === "failed") {
		return {
			key: `failed:${status.auto.lastAt ?? ""}`, type: "Warning", url,
			text: `Automatisches Update fehlgeschlagen: ${status.auto.message ?? "bitte Protokoll auf dem Server prüfen"}.`,
		};
	}
	if (status.available) {
		return {
			key: `update:${status.available}`, type: "Information", url,
			text: `Update verfügbar: EstrichONE ${status.available} (installiert ${status.installed}) – auf dem Server: ${status.command}.${autoText(status.auto, false)}`,
		};
	}
	return null;
}
