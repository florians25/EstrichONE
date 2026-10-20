/**
 * Systemhinweise der Hosting-Konsole (GET /api/v1/system/notices, ohne Anmeldung): Ankündigungen als Banner
 * (je Sitzung wegklickbar), Wartungsankündigung vorab, Wartungsseite bei laufender Wartung bzw. jeder Antwort
 * 503 MAINTENANCE. Administratoren mit Freigabe (adminsMayWork + settings.manage) arbeiten weiter und sehen einen
 * deutlichen Hinweis. Neu geladen wird alle 2 Minuten und beim Zurückkehren ins Fenster.
 */
import JSONModel from "sap/ui/model/json/JSONModel";
import { onMaintenance, type Problem } from "./api";
import { session } from "./session";

export interface Announcement {
	id: string;
	title: string;
	text: string;
	severity: "info" | "warning" | "critical";
	startsAt?: string | null;
	endsAt?: string | null;
}

export interface MaintenanceState {
	active: boolean;
	message?: string | null;
	startsAt: string;
	endsAt?: string | null;
	adminsMayWork: boolean;
}

export interface NoticesResponse {
	announcements: Announcement[];
	maintenance: MaintenanceState | null;
	serverTime: string;
}

const dismissedKey = "eo.notices.dismissed";
const types: Record<Announcement["severity"], string> = { info: "Information", warning: "Warning", critical: "Error" };
const when = (value: string | null | undefined) =>
	value ? new Intl.DateTimeFormat("de-DE", { weekday: "short", day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }).format(new Date(value)) : "";

function dismissed(): Set<string> {
	try {
		return new Set(JSON.parse(sessionStorage.getItem(dismissedKey) ?? "[]") as string[]);
	} catch {
		return new Set();
	}
}

class Notices {
	readonly model = new JSONModel({ banners: [], adminHint: "", page: false, pageText: "", pageEnd: "", checking: false, canBypass: false });
	private data: NoticesResponse | null = null;
	private pageTimer: ReturnType<typeof setInterval> | undefined;
	private started = false;
	/** Durch eine 503-Antwort erkannt, auch wenn /system/notices (noch) nichts meldet. */
	private forced: Problem | null = null;
	/** Administrator möchte sich während der Wartung anmelden (Wartungsseite vorübergehend ausblenden). */
	private bypass = false;

	start(): void {
		if (this.started) return;
		this.started = true;
		onMaintenance((problem) => this.enterFromProblem(problem));
		void this.refresh();
		setInterval(() => void this.refresh(), 120_000);
		window.addEventListener("focus", () => void this.refresh());
		document.addEventListener("visibilitychange", () => {
			if (document.visibilityState === "visible") void this.refresh();
		});
	}

	async refresh(): Promise<void> {
		try {
			const response = await fetch("/api/v1/system/notices", { credentials: "same-origin", headers: { Accept: "application/json" } });
			if (!response.ok) return;
			this.data = (await response.json()) as NoticesResponse;
			if (!this.data.maintenance?.active) this.forced = null;
			this.apply();
		} catch {
			// Server nicht erreichbar: Zustand beibehalten.
		}
	}

	dismiss(id: string): void {
		const set = dismissed();
		set.add(id);
		try {
			sessionStorage.setItem(dismissedKey, JSON.stringify([...set]));
		} catch {
			// privater Modus: nur bis zum Neuladen ausgeblendet
		}
		this.apply();
	}

	private enterFromProblem(problem: Problem | undefined): void {
		this.forced = problem ?? {};
		this.bypass = false;
		this.apply();
		void this.refresh();
	}

	private adminMayWork(maintenance: MaintenanceState | null): boolean {
		return !!maintenance?.adminsMayWork && session.has("settings.manage");
	}

	private apply(): void {
		const m = this.model;
		const hidden = dismissed();
		const maintenance = this.data?.maintenance ?? null;
		const banners = (this.data?.announcements ?? []).filter((a) => !hidden.has(a.id)).map((a) => ({
			id: a.id, type: types[a.severity] ?? "Information", text: `${a.title}: ${a.text}`,
		}));
		if (maintenance && !maintenance.active) {
			const id = `maintenance-${maintenance.startsAt}`;
			if (!hidden.has(id)) {
				banners.unshift({
					id, type: "Warning",
					text: `Geplante Wartung ${when(maintenance.startsAt)}${maintenance.endsAt ? ` bis ${when(maintenance.endsAt)}` : ""}: ${maintenance.message || "EstrichONE ist in dieser Zeit nicht erreichbar."}`,
				});
			}
		}
		m.setProperty("/banners", banners);
		const active = !!maintenance?.active || !!this.forced;
		const admin = active && this.adminMayWork(maintenance);
		m.setProperty("/adminHint", admin
			? `Wartungsmodus aktiv${maintenance?.endsAt ? ` bis ${when(maintenance.endsAt)}` : ""} – nur Administratoren können arbeiten. Benutzer sehen die Wartungsseite.`
			: "");
		const page = active && !admin && !this.bypass;
		m.setProperty("/canBypass", active && !!maintenance?.adminsMayWork && !session.data.user);
		const end = maintenance?.endsAt ?? this.forced?.endsAt ?? null;
		m.setProperty("/pageText", maintenance?.message || this.forced?.detail || "EstrichONE wird gerade gewartet. Ihre Daten sind sicher; bitte versuchen Sie es später erneut.");
		m.setProperty("/pageEnd", end ? `Voraussichtlich wieder erreichbar: ${when(end)} Uhr` : "");
		const wasPage = m.getProperty("/page") === true;
		m.setProperty("/page", page);
		if (page && !this.pageTimer) {
			// Während der Wartung häufiger nachsehen und danach automatisch weiterarbeiten.
			this.pageTimer = setInterval(() => void this.refresh(), 30_000);
		} else if (!page && this.pageTimer) {
			clearInterval(this.pageTimer);
			this.pageTimer = undefined;
		}
		if (wasPage && !active) {
			window.location.reload();
		}
	}

	/** Nach Anmeldung bzw. Laden der Sitzung: Rechte sind jetzt bekannt (Administrator darf während der Wartung arbeiten). */
	sessionChanged(): void {
		if (this.started) this.apply();
	}

	/** „Als Administrator anmelden“: Anmeldeseite zeigen; ohne Recht greift die Wartungsseite nach der Anmeldung wieder. */
	adminLogin(): void {
		this.bypass = true;
		this.apply();
	}

	async recheck(): Promise<void> {
		this.model.setProperty("/checking", true);
		this.forced = null;
		await this.refresh();
		this.model.setProperty("/checking", false);
	}
}

export const notices = new Notices();
