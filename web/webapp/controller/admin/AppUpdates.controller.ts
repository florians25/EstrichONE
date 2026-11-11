import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Image from "sap/m/Image";
import Text from "sap/m/Text";
import Link from "sap/m/Link";
import VBox from "sap/m/VBox";
import QRCode from "qrcode";
import { get, post } from "../../util/api";
import { dateTime, fileSize } from "../../util/format";
import { loadSystemSettings, saveSystemSettings } from "../../util/systemSettings";
import {
	defaultAppUpdateSettings, overviewIssues, shortHash, sortDevices, stateText, validateSettings, wouldBlock,
	type AppUpdateDevice, type AppUpdateOverview, type AppUpdateSettings,
} from "../../util/appUpdates";

/**
 * Verwaltung › App-Updates (docs/app-updates.md): verfügbare Versionen (Android-Paket des Servers, iOS über App Store,
 * TestFlight oder MDM), Regeln (Hinweis, erzwungene Mindestversion, eigener Text) und alle Geräte mit App-Version.
 * @namespace estrichone.controller.admin
 */
export default class AppUpdates extends BaseController {
	private version = 0;

	onInit(): void {
		this.local({ o: null, s: { ...defaultAppUpdateSettings }, rows: [], filter: "outdated", busy: false, dirty: false, androidText: "", iosText: "",
			packageText: "", hashText: "", enforcePreview: "", canManage: false, canSave: false });
		this.model().attachPropertyChange((event) => {
			if (String(event.getParameter("path")).startsWith("/s/") || String(event.getParameter("path")).startsWith("s/")) {
				this.model().setProperty("/dirty", true);
				this.updatePreview();
			}
		});
	}

	onRouteMatched(): void {
		this.model().setProperty("/canManage", this.has("devices.manage"));
		this.model().setProperty("/canSave", this.has("settings.manage"));
		void this.load();
	}

	onRefresh(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const [o, settings] = await Promise.all([
			this.run(() => get<AppUpdateOverview>("app-updates/overview")),
			loadSystemSettings(true),
		]);
		m.setProperty("/busy", false);
		if (!o) return;
		this.version = settings?.version ?? 0;
		m.setProperty("/o", o);
		m.setProperty("/s", { ...defaultAppUpdateSettings, ...(settings?.settings.appUpdates ?? o.settings) });
		m.setProperty("/dirty", false);
		const p = o.androidPackage;
		m.setProperty("/androidText", o.android.latestVersion
			? `${o.android.latestVersion}${o.android.download.kind === "package" ? " – vom Server ausgeliefert" : o.android.download.kind === "link" ? " – Download über Link" : ""}`
			: "keine Bezugsquelle");
		m.setProperty("/packageText", p ? `${p.releaseSigned ? "Release-Signatur" : "Testversion (Debug-Signatur)"} · ${fileSize(p.size)} · Versionscode ${p.versionCode}` : "");
		m.setProperty("/hashText", p ? `SHA-256 ${shortHash(p.sha256)}${p.valid ? " · geprüft" : ` · ${p.error ?? "ungültig"}`}` : "");
		m.setProperty("/iosText", `${o.ios.latestVersion ?? "–"}${o.settings.iosUpdateUrl ? " – über den hinterlegten Link" : " – Link noch nicht hinterlegt"}`);
		this.statusLine(overviewIssues(o), "alle Apps aktuell");
		this.applyFilter();
		this.updatePreview();
	}

	onFilter(): void { this.applyFilter(); }

	private applyFilter(): void {
		const m = this.model();
		const o = m.getProperty("/o") as AppUpdateOverview | null;
		if (!o) return;
		const outdated = m.getProperty("/filter") === "outdated";
		const list = sortDevices(o.devices).filter((d) => !outdated || (d.state !== "current"));
		m.setProperty("/rows", list.map((d) => {
			const s = stateText(d.state);
			return { ...d, stateText: s.text, stateState: s.state, icon: d.platform === "ios" ? "sap-icon://iphone" : "sap-icon://iphone-2",
				person: d.kind === "pool" ? "Poolgerät" : d.assignedUserName ?? "–", seen: dateTime(d.lastSeenAt),
				canRequest: d.kind === "personal" && (d.state === "recommended" || d.state === "required" || d.state === null) };
		}));
	}

	/** Vorschau: Wie viele aktive Geräte würde die eingestellte Mindestversion sperren? */
	private updatePreview(): void {
		const m = this.model();
		const o = m.getProperty("/o") as AppUpdateOverview | null;
		const s = m.getProperty("/s") as AppUpdateSettings;
		if (!o || !s.enforceMinimum) {
			m.setProperty("/enforcePreview", "");
			return;
		}
		const minimum = (s.minimumVersion ?? "").trim() || o.serverVersion;
		const count = wouldBlock(o.devices, minimum);
		m.setProperty("/enforcePreview", count
			? `Sperrt derzeit ${count} ${count === 1 ? "Gerät" : "Geräte"} unter Version ${minimum}, bis die App aktualisiert ist. Nicht übertragene Änderungen bleiben auf den Geräten.`
			: `Alle aktiven Geräte haben mindestens Version ${minimum}.`);
	}

	onUseServerVersion(): void {
		const o = this.model().getProperty("/o") as AppUpdateOverview | null;
		if (!o) return;
		this.model().setProperty("/s/minimumVersion", o.serverVersion);
		this.model().setProperty("/dirty", true);
		this.updatePreview();
	}

	async onSave(): Promise<void> {
		const m = this.model();
		const { errors, clean } = validateSettings(m.getProperty("/s") as AppUpdateSettings);
		if (errors.length) {
			this.showError(new Error(errors.join("\n")), "Bitte prüfen");
			return;
		}
		const o = m.getProperty("/o") as AppUpdateOverview;
		if (clean.enforceMinimum && !(o.settings.enforceMinimum)) {
			const count = wouldBlock(o.devices, clean.minimumVersion ?? o.serverVersion);
			if (count && !(await this.confirm(`${count} ${count === 1 ? "Gerät wird" : "Geräte werden"} sofort gesperrt, bis die App aktualisiert ist. Fortfahren?`,
				{ title: "Mindestversion erzwingen", action: "Erzwingen", danger: true }))) return;
		}
		const saved = await this.run(async () => {
			const current = await loadSystemSettings(true);
			if (!current) throw new Error("Die Einstellungen konnten nicht geladen werden.");
			return saveSystemSettings({ ...current.settings, appUpdates: clean }, this.version || current.version);
		}, "App-Updates gespeichert");
		if (saved) await this.load();
	}

	onDiscard(): void { void this.load(); }

	async onRequestUpdate(event: Event): Promise<void> {
		const d = (event.getSource() as Control).getBindingContext("view")?.getObject() as AppUpdateDevice;
		const done = await this.run(async () => {
			await post(`app-updates/devices/${d.id}/request-update`);
			return true;
		}, `${d.assignedUserName ?? d.name} wurde benachrichtigt (einmal je Version)`);
		if (done) void this.load();
	}

	/** QR-Code mit dem Download-Link der Android-App (Ersteinrichtung neuer Geräte, Aktualisieren sehr alter Apps). */
	async onAndroidQr(): Promise<void> {
		const link = `${window.location.origin}/api/v1/app-updates/android/package`;
		const qr = await QRCode.toDataURL(link, { width: 512, margin: 1, errorCorrectionLevel: "M" });
		const dialog = new Dialog({
			title: "Android-App laden", contentWidth: "26rem",
			content: [new VBox({ alignItems: "Center", items: [
				new Image({ src: qr, alt: "QR-Code zum Download der Android-App", width: "15rem", height: "15rem", decorative: false }),
				new Link({ text: link, href: link, target: "_blank", wrapping: true }).addStyleClass("sapUiSmallMarginTop"),
				new Text({ text: "Mit der Kamera des Android-Geräts scannen, Datei öffnen und „Installieren“ wählen. Beim ersten Mal fragt Android, ob der Browser Apps installieren darf.", textAlign: "Center" })
					.addStyleClass("sapUiSmallMarginTop"),
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Fertig", type: "Emphasized", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		dialog.open();
	}
}
