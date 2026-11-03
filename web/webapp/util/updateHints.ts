/**
 * Banner „Sicherheitsupdate verfügbar“ in der Shell (nur mit Recht backup.manage). Abfrage beim Anmelden und alle 6 Stunden;
 * der Server hält den Release-Feed selbst zwischen. Normale Updates lassen sich je Version ausblenden (dieser Browser),
 * Sicherheitsupdates nur für die laufende Sitzung.
 */
import JSONModel from "sap/ui/model/json/JSONModel";
import { session } from "./session";
import { updateHint, type UpdateStatus } from "./updateStatus";

const STORAGE_KEY = "estrichone.updateHint.hidden";
const INTERVAL = 6 * 3600_000;

class UpdateHints {
	readonly model = new JSONModel({ visible: false, text: "", type: "Information", url: "", key: "" });
	private user = "";
	private timer: ReturnType<typeof setInterval> | undefined;
	private readonly hiddenThisSession = new Set<string>();

	/** Bei jeder Änderung der Sitzung (Anmeldung, Rechte) aufrufen. */
	sessionChanged(): void {
		const user = session.data.user && session.has("backup.manage") ? session.data.user.id : "";
		if (user === this.user) return;
		this.user = user;
		clearInterval(this.timer);
		this.model.setProperty("/visible", false);
		if (!user) return;
		void this.refresh();
		this.timer = setInterval(() => void this.refresh(), INTERVAL);
	}

	async refresh(): Promise<void> {
		try {
			const response = await fetch("/api/v1/system/updates", { credentials: "same-origin", headers: { Accept: "application/json" } });
			if (!response.ok) return;
			const hint = updateHint((await response.json()) as UpdateStatus);
			const hidden = hint && (this.hiddenThisSession.has(hint.key) || (hint.type === "Information" && readHidden() === hint.key));
			this.model.setData({ visible: !!hint && !hidden, text: hint?.text ?? "", type: hint?.type ?? "Information", url: hint?.url ?? "", key: hint?.key ?? "" });
		} catch {
			// Server nicht erreichbar: Hinweis unverändert lassen.
		}
	}

	dismiss(): void {
		const key = this.model.getProperty("/key") as string;
		this.hiddenThisSession.add(key);
		if (this.model.getProperty("/type") === "Information") writeHidden(key);
		this.model.setProperty("/visible", false);
	}
}

function readHidden(): string {
	try {
		return localStorage.getItem(STORAGE_KEY) ?? "";
	} catch {
		return "";
	}
}

function writeHidden(key: string): void {
	try {
		localStorage.setItem(STORAGE_KEY, key);
	} catch {
		// Privater Modus: nur für diese Sitzung ausgeblendet.
	}
}

export const updateHints = new UpdateHints();
