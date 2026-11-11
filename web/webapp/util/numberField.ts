import type { Field } from "./forms";
import { session } from "./session";

/**
 * Feld „Nummer“ für Stammdaten-Dialoge: leer = automatisch aus dem Nummernkreis. Von Hand ändern dürfen nur
 * Administratoren (Einstellungen ändern); alle anderen sehen die vergebene Nummer schreibgeschützt.
 */
export function numberField(key: string, label: string, isNew: boolean): Field {
	const admin = session.has("settings.manage");
	return {
		key, label, maxLength: 30, editable: admin,
		placeholder: isNew ? "wird automatisch vergeben" : "",
		help: admin ? "Leer lassen für die nächste Nummer aus dem Nummernkreis (Systemeinstellungen › Nummernkreise)." : undefined,
	};
}
