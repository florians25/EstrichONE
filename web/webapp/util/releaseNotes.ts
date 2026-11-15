/**
 * „Was gibt’s Neues“ ohne UI5: Typen der Versionshinweise, sicheres HTML für sap.m.FormattedText, Aufteilung offen/eingeklappt.
 */
export interface ReleaseSection {
	title: string;
	items: string[];
}

export interface ReleaseNote {
	version: string;
	title: string;
	sections: ReleaseSection[];
}

export interface WhatsNewResponse {
	serverVersion: string;
	lastSeenVersion: string | null;
	notes: ReleaseNote[];
}

/** So viele Versionen stehen offen da; ältere stehen eingeklappt darunter. */
export const VISIBLE_VERSIONS = 5;

const escapeHtml = (s: string): string => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

/** Ein Punkt als sicheres HTML: alles maskiert, nur **fett** wird zu &lt;strong&gt;. */
export function itemHtml(text: string): string {
	return escapeHtml(text).replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>").replace(/\*\*/g, "");
}

/** Neuerungen einer Version als HTML für sap.m.FormattedText (Abschnitte außer „Neu“ mit Zwischenüberschrift). */
export function noteHtml(note: ReleaseNote): string {
	const out: string[] = [];
	for (const section of note.sections) {
		if (note.sections.length > 1 || !/^neu\b/i.test(section.title)) {
			out.push(`<p><strong>${escapeHtml(section.title)}</strong></p>`);
		}
		out.push(`<ul>${section.items.map((i) => `<li>${itemHtml(i)}</li>`).join("")}</ul>`);
	}
	return out.join("");
}

/** Neueste zuerst; die ersten {@link VISIBLE_VERSIONS} offen, der Rest eingeklappt. */
export function splitNotes(notes: ReleaseNote[], visible = VISIBLE_VERSIONS): { shown: ReleaseNote[]; older: ReleaseNote[] } {
	const sorted = [...notes];
	return { shown: sorted.slice(0, visible), older: sorted.slice(visible) };
}

/** Fenstertitel: neueste Version der Liste (sonst die Serverversion). */
export function dialogTitle(notes: ReleaseNote[], serverVersion?: string): string {
	const newest = notes[0]?.version ?? serverVersion ?? "";
	return newest ? `Was gibt’s Neues in Version ${newest}` : "Was gibt’s Neues";
}
