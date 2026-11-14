import type { Field } from "./forms";

/** Anzeige im Nummernfeld neuer Datensätze (ohne weitere Hinweistexte). */
export const AUTO_NUMBER = "wird automatisch vergeben";

/**
 * Feld „Nummer“ für Stammdaten-Dialoge: Nummern kommen ausschließlich aus dem Nummernkreis. Das Feld ist immer
 * schreibgeschützt – bei neuen Datensätzen mit „wird automatisch vergeben“, sonst mit der vergebenen Nummer, ohne
 * weitere Hinweistexte.
 * Der Server lehnt abweichende Nummern ab (NUMBER_MANUAL_FORBIDDEN).
 */
export function numberField(key: string, label: string, isNew: boolean): Field {
	return {
		key, label, maxLength: 30, editable: false,
		placeholder: isNew ? AUTO_NUMBER : "",
	};
}
