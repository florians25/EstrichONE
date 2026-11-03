import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import View from "sap/ui/core/mvc/View";

/**
 * Erneute Passwortabfrage vor dem Herausgeben von Geheimnissen oder Daten (Zugangsbrief, Export, Archiv).
 * Liefert das eingegebene Passwort oder null (abgebrochen). Das Passwort wird nach dem Schließen verworfen.
 */
export function askPassword(view: View, title: string, text: string, confirmText = "Weiter"): Promise<string | null> {
	return new Promise((resolve) => {
		let result: string | null = null;
		const input = new Input({ id: view.createId("passwordPrompt"), type: "Password", width: "100%", placeholder: "Passwort Ihres Kontos" });
		const dialog = new Dialog({
			title,
			contentWidth: "26rem",
			content: new VBox({
				items: [new Text({ text }), new Label({ text: "Ihr Passwort", labelFor: input, required: true }).addStyleClass("sapUiSmallMarginTop"), input]
			}).addStyleClass("sapUiSmallMargin"),
			beginButton: new Button({
				id: view.createId("passwordPromptConfirm"),
				text: confirmText,
				type: "Emphasized",
				press: () => {
					if (!input.getValue()) {
						input.setValueState("Error");
						input.setValueStateText("Bitte das Passwort eingeben.");
						return;
					}
					result = input.getValue();
					dialog.close();
				}
			}),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterOpen: () => input.focus(),
			afterClose: () => {
				dialog.destroy();
				resolve(result);
			}
		});
		input.attachSubmit(() => (dialog.getBeginButton() as Button).firePress());
		view.addDependent(dialog);
		dialog.open();
	});
}

/** Lädt eine Datei über einen (einmaligen) Download-Link der Konsole herunter. */
export function download(url: string, fileName: string): void {
	const link = document.createElement("a");
	link.href = url;
	link.download = fileName;
	document.body.appendChild(link);
	link.click();
	link.remove();
}
