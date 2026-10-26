/** Baustelle auf der Karte im Dialog (Kopf und Übersicht der Bauakte). */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import LeafletMap from "../control/LeafletMap";

export function openMapDialog(place: string, markers: unknown[], routeUrl?: string | null): void {
	const dialog = new Dialog({
		title: `Karte – ${place}`, contentWidth: "48rem", resizable: true, draggable: true,
		content: markers.length
			? [new LeafletMap({ height: "28rem", width: "100%", zoom: 15, markers } as unknown as ConstructorParameters<typeof LeafletMap>[0])]
			: [new Text({ text: "Für diese Baustelle ist noch kein Standort gesetzt. In der Übersicht unter „Standort“ festlegen." }).addStyleClass("sapUiSmallMargin")],
		buttons: [
			...(routeUrl ? [new Button({ text: "Route planen", icon: "sap-icon://map-3", press: () => window.open(routeUrl, "_blank", "noopener") })] : []),
			new Button({ text: "Schließen", press: () => dialog.close() }),
		],
		afterClose: () => dialog.destroy(),
	});
	dialog.open();
}
