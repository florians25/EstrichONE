import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import NoticeCard from "../control/NoticeCard";
import type { NoticeCard as Card } from "./noticeView";

/** Felder einer Karte als Eigenschaften der Steuerelement-Karte. */
export function cardSettings(card: Card, mode: "banner" | "tile" | "full") {
	return {
		kind: card.kind, tone: card.tone, icon: card.icon, label: card.label, title: card.title, text: card.text,
		period: card.period, relative: card.relative, remaining: card.remaining, source: card.source,
		dismissible: card.dismissible, mode,
	};
}

/**
 * Details eines Systemhinweises: vollständiger Text, Zeitraum, Herkunft. Mit <c>onDismiss</c> zusätzlich „Ausblenden“
 * (nur für ausblendbare Hinweise).
 */
export function openNoticeDetails(card: Card, onDismiss?: () => void): void {
	const dialog = new Dialog({
		title: card.label && card.label !== card.title ? card.label : "Systemhinweis",
		contentWidth: "34rem",
		draggable: true,
		resizable: true,
		content: [new NoticeCard(cardSettings(card, "full"))],
		beginButton: onDismiss && card.dismissible
			? new Button({ text: "Ausblenden", icon: "sap-icon://hide", press: () => { onDismiss(); dialog.close(); } })
			: undefined,
		endButton: new Button({ text: "Schließen", type: "Emphasized", press: () => dialog.close() }),
		afterClose: () => dialog.destroy(),
	});
	dialog.addStyleClass("eoNoticeDialog");
	dialog.open();
}
