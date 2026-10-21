/**
 * Kompakter Kopf mit den Kerndaten der Bauakte für Dialoge (Hochladen, Vorschau, Details, Verlauf):
 * Aktennummer, Bezeichnung, Kunde, Baustellenadresse – damit klar ist, woran man gerade arbeitet.
 */
import HBox from "sap/m/HBox";
import VBox from "sap/m/VBox";
import Text from "sap/m/Text";
import Title from "sap/m/Title";
import Icon from "sap/ui/core/Icon";
import type Control from "sap/ui/core/Control";

export interface AkteContext {
	number: string | null;
	title: string;
	customer: string | null;
	address: string | null;
}

/** Aus dem Modell „akte“ der Objektseite (bauakte, customer). */
export function akteContextOf(data: { bauakte?: { number: string | null; title: string; street: string | null; postalCode: string | null; city: string | null } | null; customer?: { name?: string; displayName?: string } | null } | undefined): AkteContext | null {
	const b = data?.bauakte;
	if (!b) return null;
	const place = [b.postalCode, b.city].filter(Boolean).join(" ");
	return {
		number: b.number,
		title: b.title,
		customer: data?.customer?.displayName ?? data?.customer?.name ?? null,
		address: [b.street, place].filter(Boolean).join(", ") || null,
	};
}

export function akteHeader(context: AkteContext | null): Control[] {
	if (!context) return [];
	const facts = [context.customer ? `Kunde: ${context.customer}` : null, context.address].filter(Boolean).join(" · ");
	return [new HBox({
		alignItems: "Center",
		items: [
			new Icon({ src: "sap-icon://folder-full", size: "1.5rem", decorative: true }).addStyleClass("sapUiSmallMarginEnd"),
			new VBox({ items: [
				new Title({ text: `${context.number ? `${context.number} · ` : ""}${context.title}`, level: "H5", wrapping: true }),
				new Text({ text: facts || "–", wrapping: true }).addStyleClass("eoMuted"),
			] }),
		],
	}).addStyleClass("eoAkteHeader")];
}
