/**
 * Abschlussdialog der Bauakte: „Abschließen“ und – wenn ein Archivserver eingerichtet ist – „Abschließen & Archivieren“
 * (Emphasized). Archiviert wird über den bestehenden Endpunkt POST archive/bauakten/{id} (reason=completion fürs Audit).
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import MessageStrip from "sap/m/MessageStrip";
import ProgressIndicator from "sap/m/ProgressIndicator";
import MessageBox from "sap/m/MessageBox";
import { get, post, problemMessage } from "./api";
import type { HealthBanner } from "./health";
import type { ArchiveRecordDto } from "./infraModels";
import { archiveProgress, completeArchiveOption } from "./kombiserver";
import { openArchiveProof } from "./archiveProof";
import { session } from "./session";

export type CompleteChoice = "complete" | "completeAndArchive" | null;

/** Fragt nach: Abschließen, Abschließen & Archivieren (falls angeboten) oder Abbrechen. */
export async function askComplete(texts: { question: string; complete: string }): Promise<CompleteChoice> {
	const health = await get<HealthBanner>("system/health-banner").catch(() => null);
	const option = completeArchiveOption(health, session.has("bauakten.edit"));
	return new Promise((resolve) => {
		let choice: CompleteChoice = null;
		const finish = (value: CompleteChoice) => { choice = value; dialog.close(); };
		const buttons: Button[] = [];
		if (option.visible) {
			buttons.push(new Button("completeArchiveButton", {
				text: "Abschließen & Archivieren", type: "Emphasized", icon: "sap-icon://inbox", enabled: option.enabled, tooltip: option.tooltip,
				press: () => finish("completeAndArchive"),
			}));
		}
		buttons.push(new Button("completeOnlyButton", { text: texts.complete, type: option.visible ? "Default" : "Emphasized", icon: "sap-icon://complete", press: () => finish("complete") }));
		buttons.push(new Button({ text: "Abbrechen", press: () => finish(null) }));
		const content: (Text | MessageStrip)[] = [new Text({ text: texts.question })];
		if (option.visible && !option.enabled) {
			content.push(new MessageStrip({ text: option.tooltip, type: "Warning", showIcon: true }));
		} else if (option.visible) {
			content.push(new MessageStrip({ text: "„Abschließen & Archivieren“ legt alle Dokumente der Bauakte sofort unveränderbar auf dem Archivserver ab.", type: "Information", showIcon: true }));
		}
		content.slice(1).forEach((c) => c.addStyleClass("sapUiSmallMarginTop"));
		const box = new VBox({ items: content });
		box.addStyleClass("sapUiSmallMargin");
		const dialog = new Dialog("completeDialog", {
			title: "Bauakte abschließen", contentWidth: "32rem", type: "Message", state: "None",
			content: [box], buttons,
			afterClose: () => { dialog.destroy(); resolve(choice); },
		});
		dialog.open();
	});
}

/**
 * Archivierung der (bereits abgeschlossenen) Bauakte anstoßen und den Fortschritt zeigen. Fehler lassen die Bauakte
 * abgeschlossen; die Archivierung ist erneut anstoßbar (hier oder unter „Aktionen → Archivnachweis“).
 */
export async function archiveAfterCompletion(bauakteId: string, title: string): Promise<void> {
	try {
		await post(`archive/bauakten/${bauakteId}?reason=completion`);
	} catch (error) {
		await new Promise<void>((resolve) => MessageBox.error(
			`Die Bauakte ist abgeschlossen, die Archivierung konnte aber nicht angestoßen werden:\n\n${problemMessage(error)}`, {
				title: "Archivierung fehlgeschlagen", actions: ["Erneut versuchen", MessageBox.Action.CLOSE], emphasizedAction: "Erneut versuchen",
				onClose: (action: string | null) => {
					resolve();
					if (action === "Erneut versuchen") void archiveAfterCompletion(bauakteId, title);
				},
			}));
		return;
	}
	const indicator = new ProgressIndicator("archiveProgress", { percentValue: 0, displayValue: "Wird übertragen …", showValue: true, state: "Information" });
	const status = new Text("archiveProgressText", { text: "Dokumente werden auf den Archivserver übertragen …" });
	const box = new VBox({ items: [status, indicator] });
	box.addStyleClass("sapUiSmallMargin");
	let stopped = false;
	const dialog = new Dialog("archiveProgressDialog", {
		title: `Archivierung: ${title}`, contentWidth: "30rem", content: [box],
		beginButton: new Button({ text: "Archivnachweis", icon: "sap-icon://inbox", press: () => { dialog.close(); void openArchiveProof({ bauakteId, title: `Archivnachweis: ${title}` }); } }),
		endButton: new Button({ text: "Schließen", press: () => dialog.close() }),
		afterClose: () => { stopped = true; dialog.destroy(); },
	});
	dialog.open();
	const until = Date.now() + 120_000;
	while (!stopped && Date.now() < until) {
		const records = await get<ArchiveRecordDto[]>(`archive/bauakten/${bauakteId}`).catch(() => null);
		if (stopped) return;
		if (records) {
			const p = archiveProgress(records);
			status.setText(p.text);
			indicator.setPercentValue(p.total ? Math.round(((p.archived + p.failed) * 100) / p.total) : 100);
			indicator.setDisplayValue(`${p.archived} / ${p.total}`);
			if (p.done || p.total === 0) {
				indicator.setState(p.failed ? "Error" : "Success");
				return;
			}
			const retrying = records.find((r) => r.status === "pending" && r.lastError);
			if (retrying) {
				status.setText(`${p.text} Archivserver meldet: ${retrying.lastError}. Die Archivierung wird automatisch wiederholt.`);
				indicator.setState("Warning");
			}
		}
		await new Promise((r) => setTimeout(r, 2000));
	}
	if (!stopped) status.setText(`${status.getText()} Die Übertragung läuft im Hintergrund weiter.`);
}
