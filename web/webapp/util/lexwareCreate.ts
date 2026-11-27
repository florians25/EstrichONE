/**
 * Kunden anlegen mit der verbindlichen Prüfung in Lexware Office: Der Server prüft beim Anlegen mit den endgültigen
 * Angaben, ob es den Kunden in Lexware Office schon gibt, und speichert erst danach. Bei Treffern oder wenn die Prüfung
 * nicht möglich ist, wird nichts angelegt; der Nutzer entscheidet ausdrücklich („Trotzdem neu anlegen“ bzw. „Ohne
 * Prüfung anlegen“). Für alle Stellen der Weboberfläche, die einen Kunden neu anlegen.
 */
import MessageBox from "sap/m/MessageBox";
import BusyIndicator from "sap/ui/core/BusyIndicator";
import { ApiError, post } from "./api";
import { showToast } from "./toast";
import { session } from "./session";
import {
	creationProblem, existsQuestion, LEXWARE_CHECK_RUNNING, lexwareCheckActive, UNCHECKED_QUESTION, type LexwareContactCheck,
} from "./lexware";

export { creationProblem };

function ask(text: string, action: string, title: string): Promise<boolean> {
	return new Promise((resolve) => {
		MessageBox.warning(text, {
			title, actions: [action, MessageBox.Action.CANCEL], emphasizedAction: MessageBox.Action.CANCEL,
			onClose: (result: string | null) => resolve(result === action),
		});
	});
}

/** Rückfrage, wenn die Prüfung in Lexware Office nicht möglich war. */
export function askUnchecked(detail: string): Promise<boolean> {
	return ask(`${detail}\n\n${UNCHECKED_QUESTION}`, "Ohne Prüfung anlegen", "Prüfung in Lexware Office nicht möglich");
}

/** Sichtbarer Zustand, solange der Server in Lexware Office prüft. */
export function checkRunning(): boolean {
	return lexwareCheckActive(session.data.modules ?? {}, session.data.lexware);
}

export interface CreateFlags { lexwareCreateAnyway?: boolean; lexwareSkipCheck?: boolean }

/**
 * Kunde anlegen (POST contacts) mit Rückfragen in einfachen Dialogen. Lehnt der Nutzer ab, wird nichts angelegt und die
 * Ablehnung als Fehler mit verständlichem Text weitergegeben. Der Dialog „Neuer Kunde“ zeigt die Treffer selbst an und
 * nutzt dafür <paramref name="onExists"/>.
 */
export async function createCustomerChecked<T>(body: Record<string, unknown>, onExists?: (check: LexwareContactCheck) => Promise<boolean>): Promise<T> {
	const flags: CreateFlags = {};
	for (;;) {
		const running = checkRunning() && !flags.lexwareCreateAnyway;
		if (running) {
			BusyIndicator.show(0);
			showToast(LEXWARE_CHECK_RUNNING);
		}
		try {
			return await post<T>("contacts", { ...body, ...flags });
		} catch (error) {
			const problem = creationProblem(error instanceof ApiError ? { code: error.code, problem: error.problem as Record<string, unknown> | undefined } : null);
			if (!problem) throw error;
			if (running) BusyIndicator.hide();
			if (problem.kind === "exists") {
				const proceed = onExists ? await onExists(problem.check) : await ask(existsQuestion(problem.check), "Trotzdem neu anlegen", "Lexware Office");
				if (!proceed) throw new ApiError(409, "LEXWARE_CUSTOMER_EXISTS", "Der Kunde wurde nicht angelegt, weil es ihn in Lexware Office bereits gibt.");
				flags.lexwareCreateAnyway = true;
			} else {
				if (!await askUnchecked(problem.detail)) throw new ApiError(409, "LEXWARE_CHECK_UNAVAILABLE", "Der Kunde wurde nicht angelegt, weil die Prüfung in Lexware Office nicht möglich war.");
				flags.lexwareSkipCheck = true;
			}
		} finally {
			if (running) BusyIndicator.hide();
		}
	}
}
