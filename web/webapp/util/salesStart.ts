/** „Angebot erstellen (Beta)“ aus Bauakte und Kunde: Entwurf anlegen oder auf das fehlende Verknüpfen hinweisen. */
import MessageBox from "sap/m/MessageBox";
import { ApiError, post } from "./api";
import { uuidv7 } from "./ids";
import { NOT_LINKED_CODE, type Quotation } from "./quotations";

const TITLE = "Angebot erstellen (Beta)";

/**
 * Legt einen Angebotsentwurf an und liefert seine ID. Ist der Kunde nicht mit Lexware Office verknüpft, erklärt ein Dialog
 * den Weg und bietet „Zum Kunden“ an (<paramref name="openCustomer"/>).
 */
export async function startQuotation(target: { bauakteId?: string; contactId?: string }, openCustomer: (contactId: string) => void): Promise<string | null> {
	try {
		const draft = await post<Quotation>("lexware/quotations", { id: uuidv7(), bauakteId: target.bauakteId ?? null, contactId: target.contactId ?? null });
		return draft.id;
	} catch (error) {
		if (error instanceof ApiError && error.code === NOT_LINKED_CODE) {
			const contactId = String((error.problem as Record<string, unknown> | undefined)?.contactId ?? target.contactId ?? "");
			const action = "Zum Kunden";
			MessageBox.warning(error.message, {
				title: TITLE, actions: contactId ? [action, MessageBox.Action.CLOSE] : [MessageBox.Action.CLOSE], emphasizedAction: contactId ? action : undefined,
				onClose: (choice: unknown) => { if (choice === action) openCustomer(contactId); },
			});
			return null;
		}

		MessageBox.error(error instanceof Error ? error.message : String(error), { title: TITLE });
		return null;
	}
}
