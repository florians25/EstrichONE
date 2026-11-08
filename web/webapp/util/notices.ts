/**
 * Systemhinweise der Hosting-Konsole (GET /api/v1/system/notices, ohne Anmeldung): Ankündigungen und Wartung als
 * Hinweiskarten (control/NoticeCard, Aufbereitung in noticeView.ts) – je Benutzer ausblendbar (Störungen und laufende
 * Wartung nicht), mehrere zusammengefasst („+2 weitere Hinweise“), relative Zeitangaben minütlich aktualisiert; Wartungsseite bei laufender Wartung bzw. jeder Antwort
 * 503 MAINTENANCE. Administratoren mit Freigabe (adminsMayWork + settings.manage) arbeiten weiter und sehen einen
 * deutlichen Hinweis. Neu geladen wird alle 2 Minuten und beim Zurückkehren ins Fenster.
 *
 * Dazu der Systemstatus (Backup-/Archivserver, docs/backup.md): rotes bzw. gelbes Banner, nur wenn eingerichtet.
 * Und die Störungsseite: Antwortet der Server nicht (Netzwerkfehler, 502/503/504), prüft die Seite nach und zeigt
 * „EstrichONE ist gerade nicht erreichbar“ mit automatischem Neuversuch – ohne die geöffnete Seite zu verwerfen.
 */
import JSONModel from "sap/ui/model/json/JSONModel";
import { onMaintenance, onUnreachable, type Problem } from "./api";
import { session } from "./session";
import { addressStrip, healthStrips, type AddressCheck, type HealthBanner } from "./health";
import { bannerSelection, buildCards, moreText, remainingLabel, shortDate, type NoticeCard } from "./noticeView";
import { openNoticeDetails } from "./noticeDialog";

export interface Announcement {
	id: string;
	title: string;
	text: string;
	severity: "info" | "warning" | "critical";
	startsAt?: string | null;
	endsAt?: string | null;
	source?: string | null;
}

export interface MaintenanceState {
	active: boolean;
	message?: string | null;
	startsAt: string;
	endsAt?: string | null;
	adminsMayWork: boolean;
	source?: string | null;
}

export interface NoticesResponse {
	announcements: Announcement[];
	maintenance: MaintenanceState | null;
	serverTime: string;
}

/** Ausgeblendete Hinweise je Benutzer (bzw. vor der Anmeldung „anon“), dauerhaft im Browser; höchstens 100 Einträge. */
const dismissedKey = () => `eo.notices.dismissed.v2:${session.data.user?.id ?? "anon"}`;
function dismissed(): Set<string> {
	try {
		return new Set(JSON.parse(localStorage.getItem(dismissedKey()) ?? "[]") as string[]);
	} catch {
		return new Set();
	}
}

async function addressCheck(): Promise<AddressCheck | null> {
	try {
		const response = await fetch("/api/v1/system/address", { credentials: "same-origin", headers: { Accept: "application/json" } });
		return response.ok ? ((await response.json()) as AddressCheck) : null;
	} catch {
		return null;
	}
}

class Notices {
	readonly model = new JSONModel({
		banners: [] as NoticeCard[], all: [] as NoticeCard[], moreCount: 0, moreText: "", expanded: false, page: false, pageText: "", pageEnd: "", checking: false, canBypass: false,
		health: [], outage: false, outageTitle: "", outageText: "", outageSince: "", outageNext: "",
	});
	private outageTimer: ReturnType<typeof setInterval> | undefined;
	private outageCountdown = 0;
	private outageAttempts = 0;
	private healthUser = "";
	private probing = false;
	private data: NoticesResponse | null = null;
	/** Abweichung der Serveruhr (ms), damit „in 10 Min.“ auch bei falsch gehender Geräteuhr stimmt. */
	private skew = 0;
	private expanded = false;
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
		onUnreachable(() => void this.checkOutage());
		window.addEventListener("offline", () => void this.checkOutage());
		void this.refresh();
		setInterval(() => void this.refresh(), 120_000);
		// Relative Angaben („läuft seit 10 Min.“) minütlich nachführen.
		setInterval(() => { if (this.data) this.apply(); }, 60_000);
		// Systemstatus alle 5 Minuten (docs/backup.md).
		setInterval(() => void this.refreshHealth(), 300_000);
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
			const server = Date.parse(this.data.serverTime);
			this.skew = Number.isNaN(server) || Math.abs(server - Date.now()) < 30_000 ? 0 : server - Date.now();
			if (!this.data.maintenance?.active) this.forced = null;
			this.apply();
		} catch {
			// Server nicht erreichbar: Zustand beibehalten.
		}
	}

	/** Backup-/Archivstatus (rot/gelb), nur angemeldet; der Server liefert nur Banner für Eingerichtetes. */
	private async refreshHealth(): Promise<void> {
		if (!session.data.user) return;
		try {
			const response = await fetch("/api/v1/system/health-banner", { credentials: "same-origin", headers: { Accept: "application/json" } });
			if (!response.ok) return;
			const strips = healthStrips((await response.json()) as HealthBanner);
			const address = session.has("settings.manage") ? addressStrip(await addressCheck(), window.location.origin) : null;
			this.model.setProperty("/health", address ? [...strips, address] : strips);
		} catch {
			// Server nicht erreichbar: letzten Stand behalten (Störungsseite übernimmt).
		}
	}

	// ---- Störungsseite --------------------------------------------------------------------------------------------

	/** Ist der Server wirklich weg? Eine eigene Prüfanfrage entscheidet (einzelne Aussetzer zeigen keine Seite). */
	private async probe(): Promise<boolean> {
		try {
			const response = await fetch("/api/v1/system/notices", { credentials: "same-origin", headers: { Accept: "application/json" }, cache: "no-store" });
			return response.ok || response.status === 401 || response.status === 403 || (response.status === 503 && (response.headers.get("Content-Type") ?? "").includes("problem"));
		} catch {
			return false;
		}
	}

	async checkOutage(): Promise<void> {
		if (this.probing || this.model.getProperty("/outage")) return;
		this.probing = true;
		try {
			if (await this.probe()) return;
			// Noch einmal nach kurzer Pause – Neustart des Servers oder kurzer Netzaussetzer.
			await new Promise((resolve) => setTimeout(resolve, 2500));
			if (await this.probe()) return;
			this.enterOutage();
		} finally {
			this.probing = false;
		}
	}

	private enterOutage(): void {
		const m = this.model;
		const offline = typeof navigator !== "undefined" && navigator.onLine === false;
		m.setProperty("/outageTitle", offline ? "Keine Internetverbindung" : "EstrichONE ist gerade nicht erreichbar");
		m.setProperty("/outageText", offline
			? "Dieses Gerät ist offline. Sobald die Verbindung zurück ist, geht es hier automatisch weiter."
			: "Der Server antwortet derzeit nicht, in der Regel wegen eines Neustarts oder einer kurzen Störung. Gespeicherte Daten sind gesichert. Bitte nicht gespeicherte Eingaben anschließend prüfen.");
		m.setProperty("/outageSince", `Nicht erreichbar seit ${new Intl.DateTimeFormat("de-DE", { hour: "2-digit", minute: "2-digit" }).format(new Date())} Uhr`);
		m.setProperty("/outage", true);
		this.outageCountdown = 10;
		this.outageAttempts = 0;
		clearInterval(this.outageTimer);
		this.outageTimer = setInterval(() => {
			this.outageCountdown -= 1;
			if (this.outageCountdown <= 0) {
				void this.retryOutage();
			} else {
				m.setProperty("/outageNext", `Automatischer Neuversuch in ${this.outageCountdown} s`);
			}
		}, 1000);
		m.setProperty("/outageNext", `Automatischer Neuversuch in ${this.outageCountdown} s`);
	}

	async retryOutage(): Promise<void> {
		const m = this.model;
		m.setProperty("/checking", true);
		m.setProperty("/outageNext", "Verbindung wird geprüft …");
		const ok = await this.probe();
		m.setProperty("/checking", false);
		if (ok) {
			clearInterval(this.outageTimer);
			this.outageTimer = undefined;
			m.setProperty("/outage", false);
			document.dispatchEvent(new CustomEvent("estrichone-notices"));
			void this.refresh();
			void this.refreshHealth();
			return;
		}
		// Länger weg: seltener prüfen (erst alle 10 s, nach einer Minute alle 30 s).
		this.outageAttempts += 1;
		this.outageCountdown = this.outageAttempts > 6 ? 30 : 10;
		m.setProperty("/outageNext", `Automatischer Neuversuch in ${this.outageCountdown} s`);
	}

	/** Hinweis für diesen Benutzer ausblenden (nicht möglich für Störungen und laufende Wartung). */
	dismiss(key: string): void {
		const card = (this.model.getProperty("/all") as NoticeCard[]).find((c) => c.key === key);
		if (card && !card.dismissible) return;
		const list = [...dismissed()].filter((k) => k !== key);
		list.push(key);
		try {
			localStorage.setItem(dismissedKey(), JSON.stringify(list.slice(-100)));
		} catch {
			// privater Modus: nur bis zum Neuladen ausgeblendet
		}
		this.apply();
	}

	/** Mehrere Hinweise auf- bzw. zuklappen. */
	toggleExpanded(): void {
		this.expanded = !this.expanded;
		this.apply();
	}

	/** Details-Dialog; ausblendbare Hinweise lassen sich dort ebenfalls ausblenden. */
	details(key: string): void {
		const card = (this.model.getProperty("/all") as NoticeCard[]).find((c) => c.key === key);
		if (card) openNoticeDetails(card, () => this.dismiss(key));
	}

	isDismissed(key: string): boolean {
		return dismissed().has(key);
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
		const now = new Date(Date.now() + this.skew);
		const maintenance = this.data?.maintenance ?? null;
		const started = !!maintenance && new Date(maintenance.startsAt).getTime() <= now.getTime()
			&& (!maintenance.endsAt || new Date(maintenance.endsAt).getTime() > now.getTime());
		const active = !!maintenance?.active || started || !!this.forced;
		const admin = active && this.adminMayWork(maintenance);
		// Laufende Wartung erscheint als Karte nur für Administratoren, die weiterarbeiten (Benutzer sehen die Wartungsseite).
		const shownMaintenance = active
			? admin ? { ...(maintenance ?? { startsAt: now.toISOString(), endsAt: this.forced?.endsAt ?? null, adminsMayWork: true }), active: true } : null
			: maintenance;
		const all = buildCards(this.data?.announcements ?? [], shownMaintenance, now, admin);
		const { shown, hidden: more } = bannerSelection(all, hidden, this.expanded);
		if (!more && this.expanded && shown.length <= 1) this.expanded = false;
		m.setProperty("/all", all);
		m.setProperty("/banners", shown);
		m.setProperty("/moreCount", more);
		m.setProperty("/expanded", this.expanded && shown.length > 1);
		m.setProperty("/moreText", more ? moreText(more) : "Weniger anzeigen");
		const page = active && !admin && !this.bypass;
		m.setProperty("/canBypass", active && !!maintenance?.adminsMayWork && !session.data.user);
		const end = maintenance?.endsAt ?? this.forced?.endsAt ?? null;
		m.setProperty("/pageText", maintenance?.message || this.forced?.detail || "EstrichONE wird gerade gewartet. Ihre Daten sind sicher; bitte versuchen Sie es später erneut.");
		const endDate = end ? new Date(end) : null;
		const left = end ? remainingLabel(end, now) : "";
		m.setProperty("/pageEnd", endDate
			? `Voraussichtlich wieder erreichbar: ${shortDate(endDate, now)}, ${endDate.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" })} Uhr${left ? ` (${left})` : ""}`
			: "");
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
		if (!this.started) return;
		this.apply();
		const user = session.data.user?.id ?? "";
		// Schlüssel mit Verwaltungsrecht: Der Adresshinweis für Administratoren erscheint, sobald die Rechte geladen sind.
		const key = user ? `${user}:${session.has("settings.manage")}` : "";
		if (key !== this.healthUser) {
			this.healthUser = key;
			if (user) void this.refreshHealth();
			else this.model.setProperty("/health", []);
		}
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
