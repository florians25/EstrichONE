/**
 * Archivnachweis als Dialog – für Dokument (fileId) oder ganze Bauakte (bauakteId).
 * Einbindung z. B. im Menü „Aktionen“ der Bauakte oder an einem Dokument: `openArchiveProof({ bauakteId })`.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import List from "sap/m/List";
import CustomListItem from "sap/m/CustomListItem";
import VBox from "sap/m/VBox";
import Text from "sap/m/Text";
import Title from "sap/m/Title";
import ObjectStatus from "sap/m/ObjectStatus";
import MessageStrip from "sap/m/MessageStrip";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import { ApiError, get, post, problemMessage } from "./api";
import { date, dateTime, fileSize } from "./format";
import { recordStatus } from "./infraModels";
import type { ArchiveRecordDto } from "./infraModels";
import { session } from "./session";

export function proofLines(r: ArchiveRecordDto): string[] {
	const lines = [`${r.documentTypeLabel} · ${fileSize(r.sizeBytes)} · SHA-256 ${r.sha256}`];
	if (r.status === "archived") {
		lines.push(`Archiviert ${dateTime(r.archivedAt)} auf „${r.archiveServerName ?? "Archivserver"}“ · aufbewahren bis ${date(r.retainUntil)} (${r.retentionYears} Jahre)`);
		lines.push(`Journal Nr. ${r.ledgerSeq} · ${r.ledgerHash}`);
	} else if (r.lastError) {
		lines.push(`Fehler: ${r.lastError} · nächster Versuch ${dateTime(r.nextAttemptAt)}`);
	}
	return lines;
}

async function load(opts: { fileId?: string; bauakteId?: string }): Promise<ArchiveRecordDto[]> {
	if (opts.fileId) {
		const r = await get<ArchiveRecordDto | undefined>(`archive/files/${opts.fileId}`);
		return r ? [r] : [];
	}
	return get<ArchiveRecordDto[]>(`archive/bauakten/${opts.bauakteId}`);
}

export async function openArchiveProof(opts: { fileId?: string; bauakteId?: string; title?: string }): Promise<void> {
	const list = new List({ noDataText: opts.fileId ? "Dieses Dokument ist (noch) nicht archiviert." : "Aus dieser Bauakte ist (noch) nichts archiviert." });
	const fill = async () => {
		list.destroyItems();
		const records = await load(opts);
		for (const r of records) {
			const box = new VBox();
			box.addItem(new Title({ text: r.name, level: "H5" }));
			box.addItem(new ObjectStatus({ text: recordStatus[r.status].text, state: recordStatus[r.status].state as never }));
			for (const line of proofLines(r)) box.addItem(new Text({ text: line }));
			box.addStyleClass("sapUiSmallMargin");
			list.addItem(new CustomListItem({ content: [box] }));
		}
	};
	const archiveNow = new Button({
		text: opts.fileId ? "Jetzt archivieren" : "Bauakte archivieren", icon: "sap-icon://inbox", visible: session.has("bauakten.edit"),
		press: async () => {
			try {
				await post(opts.fileId ? `archive/files/${opts.fileId}` : `archive/bauakten/${opts.bauakteId}`);
				MessageToast.show("Zur Archivierung vorgemerkt – der Nachweis erscheint in wenigen Sekunden.");
				setTimeout(() => void fill().catch(() => undefined), 4000);
			} catch (error) {
				MessageBox.error(problemMessage(error));
			}
		},
	});
	const dialog = new Dialog({
		title: opts.title ?? "Archivnachweis", contentWidth: "44rem", resizable: true, draggable: true,
		content: [new MessageStrip({ text: "Archivierte Dokumente sind auf dem Archivserver unveränderbar und bis zum Fristende gegen Löschen gesperrt.", showIcon: true, type: "Information" }), list],
		beginButton: archiveNow,
		endButton: new Button({ text: "Schließen", press: () => dialog.close() }),
		afterClose: () => dialog.destroy(),
	});
	dialog.open();
	try {
		await fill();
	} catch (error) {
		if (!(error instanceof ApiError) || error.status !== 404) MessageBox.error(problemMessage(error));
	}
}
