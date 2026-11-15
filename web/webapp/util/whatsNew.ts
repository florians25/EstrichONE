/**
 * „Was gibt’s Neues“: nach der Anmeldung bzw. dem ersten Laden nach einem Server-Update einmal pro Benutzer und Version
 * die Neuerungen (Abschnitt „## Neu“ der Versionshinweise) zeigen. Der Gesehen-Stand liegt auf dem Server
 * (geräteübergreifend); neue Benutzer markiert der Server still als gesehen. Zusätzlich über das Benutzermenü erreichbar.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import FormattedText from "sap/m/FormattedText";
import Panel from "sap/m/Panel";
import Title from "sap/m/Title";
import VBox from "sap/m/VBox";
import Text from "sap/m/Text";
import Device from "sap/ui/Device";
import { get, put } from "./api";
import { dialogTitle, noteHtml, splitNotes, type ReleaseNote, type WhatsNewResponse } from "./releaseNotes";

const fetchWhatsNew = () => get<WhatsNewResponse>("release-notes/whats-new", { audience: "web" });
const fetchRecent = () => get<ReleaseNote[]>("release-notes", { audience: "web", limit: 10 });
const markSeen = (version?: string) => put<void>("release-notes/seen", { audience: "web", version: version ?? null });

function versionBlock(note: ReleaseNote, single: boolean): VBox {
	const box = new VBox({ renderType: "Bare" }).addStyleClass("eoWhatsNewVersion");
	if (!single) box.addItem(new Title({ text: `Version ${note.version}`, level: "H3", titleStyle: "H5" }));
	box.addItem(new FormattedText({ htmlText: noteHtml(note) }).addStyleClass("eoWhatsNewText"));
	return box;
}

function buildContent(notes: ReleaseNote[]): VBox {
	const { shown, older } = splitNotes(notes);
	const content = new VBox({ renderType: "Bare" }).addStyleClass("sapUiSmallMargin eoWhatsNew");
	if (!notes.length) {
		content.addItem(new Text({ text: "Für diese Version sind keine Neuerungen hinterlegt." }));
		return content;
	}
	const single = notes.length === 1;
	for (const note of shown) content.addItem(versionBlock(note, single));
	if (older.length) {
		const panel = new Panel({
			expandable: true, expanded: false, backgroundDesign: "Transparent",
			headerText: older.length === 1 ? `Ältere Version (${older[0].version})` : `Ältere Versionen (${older.length})`,
		}).addStyleClass("eoWhatsNewOlder");
		for (const note of older) panel.addContent(versionBlock(note, false));
		content.addItem(panel);
	}
	return content;
}

let open: Dialog | undefined;

/** Fenster mit „Verstanden“; erst „Verstanden“ (oder Esc) markiert die Version als gesehen. */
export function showWhatsNew(notes: ReleaseNote[], serverVersion: string | undefined, onDone: () => void): Dialog {
	open?.destroy();
	// Fokus auf „Verstanden“ (Eingabetaste schließt), nicht auf die Titelleiste.
	const ok = new Button({ text: "Verstanden", type: "Emphasized", press: () => dialog.close() });
	const dialog: Dialog = new Dialog({
		title: dialogTitle(notes, serverVersion),
		contentWidth: "40rem",
		contentHeight: notes.length > 2 ? "70vh" : undefined,
		stretch: Device.system.phone,
		draggable: true,
		resizable: true,
		content: [buildContent(notes)],
		beginButton: ok,
		initialFocus: ok,
		afterClose: () => {
			onDone();
			dialog.destroy();
			if (open === dialog) open = undefined;
		},
	}).addStyleClass("eoWhatsNewDialog");
	open = dialog;
	dialog.open();
	return dialog;
}

let checkedFor = "";

/** Nach der Anmeldung einmal je Sitzung und Benutzer prüfen; Fehler (alter Server, offline) bleiben still. */
export async function checkWhatsNew(userId: string | undefined): Promise<void> {
	if (!userId || checkedFor === userId) return;
	checkedFor = userId;
	let state: WhatsNewResponse;
	try {
		state = await fetchWhatsNew();
	} catch {
		checkedFor = "";
		return;
	}
	if (!state.notes.length) return;
	showWhatsNew(state.notes, state.serverVersion, () => void markSeen(state.serverVersion).catch(() => undefined));
}

/** Nach dem Abmelden: beim nächsten Benutzer erneut prüfen. */
export function resetWhatsNew(): void {
	checkedFor = "";
	open?.destroy();
	open = undefined;
}

/** Benutzermenü › „Was gibt’s Neues“: die letzten Versionen, unabhängig vom Gesehen-Stand. */
export async function openWhatsNew(): Promise<void> {
	let notes: ReleaseNote[] = [];
	try {
		notes = await fetchRecent();
	} catch {
		// Ohne Verbindung: Fenster mit Hinweis statt nichts.
	}
	showWhatsNew(notes, undefined, () => void markSeen().catch(() => undefined));
}
