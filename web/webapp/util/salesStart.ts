/** Beleg anlegen aus Bauakte und Kunde (Verkauf): anlegen oder auf das fehlende Verknüpfen hinweisen. */
import MessageBox from "sap/m/MessageBox";
import { ApiError, post } from "./api";
import { uuidv7 } from "./ids";
import { NOT_LINKED_CODE } from "./quotations";
import { typeName, type SalesType, type SalesVoucher } from "./sales";

/** Ist der Kunde nicht verknüpft, erklärt ein Dialog den Weg und bietet „Zum Kunden“ an. Sonst die Meldung. */
export function explainStartError(error: unknown, title: string, fallbackContactId: string | undefined, openCustomer: (contactId: string) => void): void {
	if (error instanceof ApiError && error.code === NOT_LINKED_CODE) {
		const contactId = String((error.problem as Record<string, unknown> | undefined)?.contactId ?? fallbackContactId ?? "");
		const action = "Zum Kunden";
		MessageBox.warning(error.message, {
			title, actions: contactId ? [action, MessageBox.Action.CLOSE] : [MessageBox.Action.CLOSE], emphasizedAction: contactId ? action : undefined,
			onClose: (choice: unknown) => { if (choice === action) openCustomer(contactId); },
		});
		return;
	}
	MessageBox.error(error instanceof Error ? error.message : String(error), { title });
}

/** Legt einen Beleg der Belegart an und liefert seine ID (oder null nach einer Meldung). */
export async function startVoucher(type: SalesType, target: { bauakteId?: string; contactId?: string }, openCustomer: (contactId: string) => void): Promise<string | null> {
	try {
		const voucher = await post<SalesVoucher>("sales/vouchers", { id: uuidv7(), type, bauakteId: target.bauakteId ?? null, contactId: target.contactId ?? null });
		return voucher.id;
	} catch (error) {
		explainStartError(error, `${typeName(type)} erstellen`, target.contactId, openCustomer);
		return null;
	}
}

/** Abschlagsrechnung oder Schlussrechnung aus dem Rechnungsplan der Bauakte. */
export async function startPlanVoucher(kind: "down-payment" | "final-invoice", bauakteId: string, body: Record<string, unknown>,
	openCustomer: (contactId: string) => void): Promise<string | null> {
	try {
		const voucher = await post<SalesVoucher>(`sales/bauakten/${bauakteId}/${kind}`, { id: uuidv7(), ...body });
		return voucher.id;
	} catch (error) {
		explainStartError(error, kind === "down-payment" ? "Abschlag erstellen" : "Schlussrechnung erstellen", undefined, openCustomer);
		return null;
	}
}
