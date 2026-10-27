/**
 * Konto → Benachrichtigungen: Pushnachrichten für diesen Browser ein/aus, Testnachricht, eigene Geräte (Browser, iPhone,
 * iPad) mit letzter Zustellung; Geräte lassen sich entfernen.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Switch from "sap/m/Switch";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import MessageStrip from "sap/m/MessageStrip";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import Title from "sap/m/Title";
import MessageToast from "sap/m/MessageToast";
import MessageBox from "sap/m/MessageBox";
import { problemMessage } from "./api";
import {
	disableWebPush, enableWebPush, myDevices, relativeTime, removeDevice, sendTest, webPushStatus, type PushDevice, type WebPushStatus,
} from "./notifications";

const STATUS_TEXTS: Record<WebPushStatus, string> = {
	unsupported: "Dieser Browser unterstützt keine Pushnachrichten. Auf iPhone/iPad: EstrichONE zum Home-Bildschirm hinzufügen und von dort öffnen.",
	insecure: "Pushnachrichten brauchen eine HTTPS-Verbindung.",
	disabled: "Pushnachrichten im Browser sind in den Systemeinstellungen ausgeschaltet.",
	denied: "Der Browser blockiert Benachrichtigungen für EstrichONE. Bitte in den Website-Einstellungen des Browsers erlauben.",
	off: "Benachrichtigungen erscheinen im Postfach (Glocke). Mit Push zusätzlich als Systemmeldung – auch wenn EstrichONE nicht geöffnet ist.",
	on: "Pushnachrichten für diesen Browser sind eingeschaltet.",
};

const PLATFORM: Record<PushDevice["platform"], { icon: string; text: string }> = {
	web: { icon: "sap-icon://internet-browser", text: "Browser" },
	ios: { icon: "sap-icon://iphone", text: "iPhone/iPad" },
	android: { icon: "sap-icon://iphone-2", text: "Android" },
};

export function openNotificationPreferences(): void {
	const strip = new MessageStrip({ showIcon: true, type: "Information" });
	const toggle = new Switch({ customTextOn: " ", customTextOff: " ", ariaLabelledBy: [] });
	const label = new Label({ text: "Pushnachrichten in diesem Browser", labelFor: toggle, design: "Bold" });
	const devices = new List({ noDataText: "Noch kein Gerät registriert.", showSeparators: "Inner" });

	const refresh = async () => {
		const status = await webPushStatus().catch((): WebPushStatus => "unsupported");
		toggle.setState(status === "on");
		toggle.setEnabled(status === "on" || status === "off");
		strip.setText(STATUS_TEXTS[status]);
		strip.setType(status === "on" ? "Success" : status === "off" ? "Information" : "Warning");
		devices.removeAllItems();
		for (const d of await myDevices().catch(() => [] as PushDevice[])) {
			devices.addItem(new StandardListItem({
				icon: PLATFORM[d.platform].icon,
				title: d.deviceName ?? PLATFORM[d.platform].text,
				description: `${PLATFORM[d.platform].text}${d.appVersion ? ` · App ${d.appVersion}` : ""} · zuletzt ${relativeTime(d.lastSeenAt)}`,
				info: d.lastError ? "Fehler" : d.lastSuccessAt ? `zugestellt ${relativeTime(d.lastSuccessAt)}` : "",
				infoState: d.lastError ? "Error" : "Success",
				type: "Active",
				press: () => {
					MessageBox.confirm(`„${d.deviceName ?? PLATFORM[d.platform].text}“ entfernen? Es erhält dann keine Pushnachrichten mehr, bis es sich neu anmeldet.`, {
						title: "Gerät entfernen", actions: ["Entfernen", MessageBox.Action.CANCEL], emphasizedAction: "Entfernen",
						onClose: (action: string) => {
							if (action === "Entfernen") void removeDevice(d.id).then(refresh).catch((e: unknown) => MessageBox.error(problemMessage(e)));
						},
					});
				},
			}));
		}
	};

	toggle.attachChange(async () => {
		toggle.setEnabled(false);
		try {
			const result = toggle.getState() ? await enableWebPush() : await disableWebPush();
			if (result === "on") MessageToast.show("Pushnachrichten eingeschaltet");
		} catch (error) {
			MessageBox.error(problemMessage(error));
		}
		await refresh();
	});

	const dialog = new Dialog({
		title: "Benachrichtigungen",
		contentWidth: "34rem",
		resizable: true,
		content: [new VBox({
			items: [
				new HBox({ alignItems: "Center", justifyContent: "SpaceBetween", items: [label, toggle] }),
				strip,
				new Title({ text: "Meine Geräte", level: "H3" }).addStyleClass("sapUiSmallMarginTop"),
				new Text({ text: "Browser und Apps, an die Pushnachrichten gehen. Antippen zum Entfernen." }).addStyleClass("eoMuted"),
				devices,
			],
		}).addStyleClass("sapUiSmallMargin eoGap")],
		buttons: [
			new Button({ text: "Testnachricht senden", icon: "sap-icon://paper-plane", press: async () => {
				try {
					await sendTest();
					MessageToast.show("Testnachricht gesendet – sie erscheint im Postfach und als Push auf Ihren Geräten.");
					document.dispatchEvent(new CustomEvent("estrichone-notices"));
					setTimeout(() => void refresh(), 3000);
				} catch (error) {
					MessageBox.error(problemMessage(error));
				}
			} }),
			new Button({ text: "Schließen", type: "Emphasized", press: () => dialog.close() }),
		],
		afterClose: () => dialog.destroy(),
	});
	dialog.open();
	void refresh();
}
