import MessageToast from "sap/m/MessageToast";
import Popup from "sap/ui/core/Popup";

/**
 * Kurzmeldungen („Gespeichert“, „Kopiert“ …): dünner gemeinsamer Helfer um sap.m.MessageToast im SAP-Standard-Aussehen.
 * Position oben rechts unter der Kopfleiste (Nutzerwunsch) – nur über die Positionsparameter von MessageToast, ohne eigenes CSS.
 * Die Aufrufer bleiben unverändert; Fehler mit Rückfrage bleiben Dialoge.
 */
export interface ToastOptions {
	/** Anzeigedauer in ms; ohne Angabe die Standarddauer von MessageToast. */
	duration?: number;
}

/** Oben rechts, unterhalb der ShellBar (Höhe ≈ 3,25 rem) mit 1 rem Abstand zum Rand. */
export const TOAST_POSITION = { my: Popup.Dock.EndTop, at: Popup.Dock.EndTop, offset: "-16 68" };

export function showToast(text: string, options: ToastOptions = {}): void {
	MessageToast.show(text, {
		...TOAST_POSITION,
		of: window,
		...(options.duration ? { duration: options.duration } : {}),
	});
}
