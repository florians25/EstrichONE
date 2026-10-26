/**
 * Aktenarchiv-Anzeige für die Bauakte (kleine, abgegrenzte Komponente für web-a):
 *   const strip = await dossierStrip(bauakteId, () => reload());  if (strip) page.insertContent(strip, 0);
 * Zeigt bei archivierten Akten den Zustand, Fortschritt beim Abruf und die Aktionen „Akte abrufen“ / „Entsperren“.
 */
import MessageStrip from "sap/m/MessageStrip";
import Link from "sap/m/Link";
import MessageBox from "sap/m/MessageBox";
import { get, post, problemMessage } from "./api";
import { dateTime } from "./format";
import { openForm } from "./forms";
import { dossierStatus } from "./infraModels";
import type { DossierDto } from "./infraModels";
import { session } from "./session";

export function dossierText(d: DossierDto): string {
	const base = `${dossierStatus[d.status].text} · Fassung ${d.version}`;
	switch (d.status) {
		case "archived": return `${base} · liegt auf dem Archivserver. Zum Ansehen abrufen (danach 5 Tage verfügbar).`;
		case "restoring": return `${base} · ${d.filesDone} von ${d.filesTotal} Dateien geholt …`;
		case "available": return `${base} · verfügbar bis ${dateTime(d.availableUntil)}. Änderungen erst nach Entsperren.`;
		case "unlocked": return `${base} · bis ${dateTime(d.unlockedUntil)}; danach wird eine neue Fassung archiviert.`;
		default: return `${base} · ${d.filesDone} von ${d.filesTotal} Dateien übertragen …`;
	}
}

export async function restoreDossier(bauakteId: string): Promise<DossierDto> {
	return post<DossierDto>(`archive/dossiers/${bauakteId}/restore`);
}

/** Mit Passwort entsperren; true bei Erfolg. */
export async function unlockDossier(bauakteId: string): Promise<boolean> {
	const result = await openForm<DossierDto>({
		title: "Archivierte Akte entsperren", submitText: "Entsperren",
		fields: [
			{ key: "intro", type: "info", label: "Änderungen an archivierten Akten werden protokolliert und danach als neue Fassung archiviert." },
			{ key: "password", label: "Ihr Passwort", type: "password", required: true },
		],
		submit: (v) => post<DossierDto>(`archive/dossiers/${bauakteId}/unlock`, { password: String(v.password), hours: 8 }),
	});
	return !!result;
}

/** Hinweisleiste für eine archivierte Bauakte oder null (nicht archiviert). Aktualisiert sich während des Abrufs. */
export async function dossierStrip(bauakteId: string, onChange?: () => void): Promise<MessageStrip | null> {
	const d = await get<DossierDto | undefined>(`archive/dossiers/${bauakteId}`).catch(() => undefined);
	if (!d) return null;
	const strip = new MessageStrip({ text: dossierText(d), showIcon: true, type: d.status === "unlocked" ? "Warning" : "Information" });
	strip.addStyleClass("sapUiSmallMarginBottom");
	const refresh = async () => {
		const next = await get<DossierDto | undefined>(`archive/dossiers/${bauakteId}`).catch(() => undefined);
		if (!next || strip.isDestroyed()) return;
		strip.setText(dossierText(next));
		if (next.status === "restoring" || next.status === "archiving") setTimeout(() => void refresh(), 3000);
		else if (d.status !== next.status) onChange?.();
	};
	if (d.status === "archived") {
		strip.setLink(new Link({ text: "Akte abrufen", press: async () => { await restoreDossier(bauakteId).catch((e) => MessageBox.error(problemMessage(e))); void refresh(); } }));
	} else if (d.status === "available" && session.has("archive.unlock")) {
		strip.setLink(new Link({ text: "Zum Bearbeiten entsperren", press: async () => { if (await unlockDossier(bauakteId)) { void refresh(); onChange?.(); } } }));
	} else if (d.status === "restoring" || d.status === "archiving") {
		setTimeout(() => void refresh(), 3000);
	}
	return strip;
}
