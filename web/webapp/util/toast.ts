import MessageToast from "sap/m/MessageToast";

/**
 * Kurzmeldungen („Gespeichert“, „Kopiert“ …): dünner gemeinsamer Helfer um sap.m.MessageToast im SAP-Standard
 * (Position, Aussehen und Verhalten wie ausgeliefert). Die Aufrufer bleiben unverändert; Fehler mit Rückfrage bleiben Dialoge.
 */
export interface ToastOptions {
	/** Anzeigedauer in ms; ohne Angabe die Standarddauer von MessageToast. */
	duration?: number;
}

export function showToast(text: string, options: ToastOptions = {}): void {
	MessageToast.show(text, options.duration ? { duration: options.duration } : undefined);
}
