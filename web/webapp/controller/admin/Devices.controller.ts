import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Image from "sap/m/Image";
import Text from "sap/m/Text";
import Title from "sap/m/Title";
import VBox from "sap/m/VBox";
import MessageStrip from "sap/m/MessageStrip";
import QRCode from "qrcode";
import { get, post } from "../../util/api";
import { openForm } from "../../util/forms";
import type { DeviceResponse, EnrollmentCodeResponse } from "../../util/models";

const icons: Record<string, string> = { ios: "sap-icon://iphone", android: "sap-icon://iphone-2", windows: "sap-icon://laptop", other: "sap-icon://devices" };

/** IT Center › Geräte: freigeben, sperren, entfernen, neues Gerät per QR-Code einrichten. @namespace estrichone.controller.admin */
export default class Devices extends BaseController {
	onInit(): void {
		this.local({ items: [], pending: 0, pendingText: "", busy: false });
	}

	onRouteMatched(): void { void this.load(); }

	private async load(): Promise<void> {
		const list = (await this.run(() => get<DeviceResponse[]>("devices"))) ?? [];
		const pending = list.filter((d) => d.status === "pending").length;
		this.model().setData({ items: list.map((d) => ({ ...d, icon: icons[d.platform] ?? icons.other })), pending,
			pendingText: `${pending} ${pending === 1 ? "Gerät wartet" : "Geräte warten"} auf Freigabe.`, busy: false });
	}

	private async act(event: Event, verb: string): Promise<void> {
		const d = (event.getSource() as Control).getBindingContext("view")?.getObject() as DeviceResponse;
		if (verb === "retire" && !(await this.confirm(`${d.name} entfernen? Alle Sitzungen des Geräts enden sofort; es muss neu eingerichtet werden.`, { action: "Entfernen", danger: true }))) return;
		await this.run(() => post(`devices/${d.id}/${verb}`));
		void this.load();
	}

	onApprove(e: Event): void { void this.act(e, "approve"); }
	onLock(e: Event): void { void this.act(e, "lock"); }
	onUnlock(e: Event): void { void this.act(e, "unlock"); }
	onRetire(e: Event): void { void this.act(e, "retire"); }

	async onEnroll(): Promise<void> { await this.enroll({ kind: "personal" }); }

	/** Entferntes Gerät neu einrichten: neuer Code mit Nutzung und Name des bisherigen Geräts. */
	onReEnroll(e: Event): void {
		const d = (e.getSource() as Control).getBindingContext("view")?.getObject() as DeviceResponse;
		void this.enroll({ kind: d.kind, deviceName: d.name });
	}

	private async enroll(initial: Record<string, unknown>): Promise<void> {
		const code = await openForm<EnrollmentCodeResponse>({
			title: "Gerät einrichten", submitText: "Code erzeugen", initial,
			intro: "Ein Gerät, das mit diesem Code eingerichtet wird, ist sofort freigegeben.",
			fields: [
				{ key: "kind", label: "Nutzung", type: "segmented", options: [{ key: "personal", text: "Persönliches Gerät" }, { key: "pool", text: "Poolgerät (mehrere Personen)" }] },
				{ key: "deviceName", label: "Gerätename (optional)", placeholder: "z. B. Fahrzeug 3", maxLength: 100 },
			],
			submit: async (v) => post<EnrollmentCodeResponse>("devices/enrollment-codes", { kind: v.kind, deviceName: v.deviceName || null }),
		});
		if (!code) return;
		const qr = code.link ? await QRCode.toDataURL(code.link, { width: 512, margin: 1, errorCorrectionLevel: "M" }) : null;
		const until = new Intl.DateTimeFormat("de-DE", { timeStyle: "short" }).format(new Date(code.expiresAt));
		const dialog = new Dialog({
			title: "Gerät einrichten", contentWidth: "26rem",
			content: [new VBox({ alignItems: "Center", items: [
				...(qr ? [new Image({ src: qr, alt: "QR-Code zur Einrichtung", width: "15rem", height: "15rem", decorative: false })]
					: [new MessageStrip({ type: "Information", text: "Für einen QR-Code muss die öffentliche Serveradresse konfiguriert sein." })]),
				new Text({ text: "Code zum Abtippen:" }).addStyleClass("sapUiSmallMarginTop"),
				new Title({ text: code.code, level: "H2", titleStyle: "H2" }).data("testid", "enrollment-code", true).addStyleClass("eoTabular"),
				new Text({ text: `In der App „QR-Code scannen“ wählen. Der Code gilt bis ${until} Uhr und nur einmal.`, textAlign: "Center" }).addStyleClass("sapUiSmallMarginTop"),
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Fertig", type: "Emphasized", press: () => dialog.close() }),
			afterClose: () => { dialog.destroy(); void this.load(); },
		});
		dialog.open();
	}
}
