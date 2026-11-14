/**
 * Größe des großen Formularfensters (`openForm({ large: true })`, z. B. Kennwerte und „Bauakte bearbeiten“).
 *
 * sap.m.Dialog prüft `contentWidth`/`contentHeight` als sap.ui.core.CSSSize: erlaubt sind feste Einheiten, %, vw/vh
 * und calc(), aber nicht min()/max()/clamp(). Ein Wert wie „min(90rem, 92vw)“ lässt den Konstruktor scheitern und der
 * Dialog öffnet sich gar nicht. Deshalb wird die Breite beim Öffnen in Pixeln berechnet.
 */
const MAX_WIDTH_PX = 90 * 16;

/** Breite: 92 % des Fensters, höchstens 90rem. Die Höhe richtet sich nach dem Inhalt (sap.m.Dialog begrenzt sie aufs Fenster). */
export function largeDialogSize(viewportWidth: number): { contentWidth: string } {
	const width = Math.round(Math.min(MAX_WIDTH_PX, Math.max(320, viewportWidth * 0.92)));
	return { contentWidth: `${width}px` };
}

/** Prüfmuster von sap.ui.core.CSSSize (vereinfacht: ohne verschachteltes calc). */
export const cssSizePattern = /^(auto|inherit|[-+]?(0*|([0-9]+|[0-9]*\.[0-9]+)(rem|em|ex|px|cm|mm|in|pt|pc|vw|vh|vmin|vmax|%))|calc\(.*\))$/i;
