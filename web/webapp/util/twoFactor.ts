/** Zwei-Faktor-Anmeldung (TOTP) und eigene Sitzungen – API-Typen und Dialoge (Konto → Anmeldung und Sicherheit). */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Image from "sap/m/Image";
import Input from "sap/m/Input";
import Label from "sap/m/Label";
import MessageStrip from "sap/m/MessageStrip";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import CustomData from "sap/ui/core/CustomData";
import QRCode from "qrcode";
import { post, problemMessage } from "./api";
import { groupSecret, type TwoFactorSetup } from "./accountSecurity";

export * from "./accountSecurity";

/** Schritt 2 der Einrichtung: QR-Code anzeigen, ersten Code bestätigen. Liefert die Wiederherstellungscodes oder null. */
export async function confirmSetup(setup: TwoFactorSetup): Promise<string[] | null> {
	const qr = await QRCode.toDataURL(setup.otpAuthUri, { width: 512, margin: 1, errorCorrectionLevel: "M" });
	return new Promise((resolve) => {
		let result: string[] | null = null;
		const error = new MessageStrip({ type: "Error", showIcon: true, visible: false });
		const code = new Input({ width: "100%", maxLength: 10, placeholder: "123456", submit: () => void submit() });
		code.addCustomData(new CustomData({ key: "autocomplete", value: "one-time-code", writeToDom: true }));
		const submit = async (): Promise<void> => {
			error.setVisible(false);
			dialog.setBusy(true);
			try {
				result = (await post<{ recoveryCodes: string[] }>("auth/two-factor/enable", { code: code.getValue().trim() })).recoveryCodes;
				dialog.close();
			} catch (e) {
				error.setText(problemMessage(e)).setVisible(true);
			} finally {
				dialog.setBusy(false);
			}
		};
		const dialog = new Dialog({
			title: "Zwei-Faktor-Anmeldung einrichten", contentWidth: "28rem",
			content: [new VBox({ items: [
				new Text({ text: "1. QR-Code mit der Authenticator-App scannen (oder den Schlüssel von Hand eingeben)." }),
				new VBox({ alignItems: "Center", items: [
					new Image({ src: qr, alt: "QR-Code für die Authenticator-App", width: "13rem", height: "13rem", decorative: false }),
					new Text({ text: groupSecret(setup.secret) }).addStyleClass("eoTabular sapUiTinyMarginTop").data("testid", "totp-secret", true),
				] }).addStyleClass("sapUiSmallMarginTopBottom"),
				new Label({ text: "2. Den angezeigten 6-stelligen Code eingeben", labelFor: code, required: true }),
				code,
				error,
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Einschalten", type: "Emphasized", press: () => void submit() }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => { dialog.destroy(); resolve(result); },
		});
		dialog.open();
		setTimeout(() => code.focus(), 200);
	});
}

/** Wiederherstellungscodes einmalig anzeigen, speichern oder kopieren. */
export function showRecoveryCodes(codes: string[], account: string): Promise<void> {
	return new Promise((resolve) => {
		const text = codes.join("\n");
		const file = `EstrichONE – Wiederherstellungscodes für ${account}\nErstellt: ${new Date().toLocaleString("de-DE")}\n\nJeder Code gilt einmal (statt des Codes aus der App).\n\n${text}\n`;
		const dialog = new Dialog({
			title: "Wiederherstellungscodes", contentWidth: "26rem",
			content: [new VBox({ items: [
				new MessageStrip({ type: "Warning", showIcon: true, text: "Jeder Code ist einmal gültig und ersetzt bei Verlust des Mobilgeräts den Code aus der App. Bitte ausdrucken oder sicher ablegen; die Codes werden nicht erneut angezeigt." }),
				new Text({ text, renderWhitespace: true }).addStyleClass("eoTabular sapUiSmallMarginTop").data("testid", "recovery-codes", true),
			] }).addStyleClass("sapUiSmallMargin")],
			buttons: [
				new Button({ text: "Als Textdatei speichern", icon: "sap-icon://download", press: () => {
					const url = URL.createObjectURL(new Blob([file], { type: "text/plain;charset=utf-8" }));
					const a = document.createElement("a");
					a.href = url;
					a.download = "estrichone-wiederherstellungscodes.txt";
					a.click();
					setTimeout(() => URL.revokeObjectURL(url), 1000);
				} }),
				new Button({ text: "Kopieren", icon: "sap-icon://copy", press: () => void navigator.clipboard?.writeText(text) }),
				new Button({ text: "Fertig", type: "Emphasized", press: () => dialog.close() }),
			],
			afterClose: () => { dialog.destroy(); resolve(); },
		});
		dialog.open();
	});
}
