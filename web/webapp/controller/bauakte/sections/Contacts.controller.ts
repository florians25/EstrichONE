import SectionBase from "./SectionBase";
import type View from "sap/ui/core/mvc/View";
import { akte } from "../../../util/akteStore";
import { saveBauakte } from "../../../util/bauakte";
import { openForm } from "../../../util/forms";
import type ObjectController from "../Object.controller";

/** Kunde, Baustellenkontakt vor Ort und zugewiesene Mitarbeiter. @namespace estrichone.controller.bauakte.sections */
export default class Contacts extends SectionBase {
	protected async load(): Promise<void> {
		// Daten kommen aus dem Modell „akte“ der Objektseite.
	}

	private objectController(): ObjectController {
		let parent = this.v().getParent();
		while (parent && !(parent as View).getController?.()?.getMetadata().getName().endsWith("bauakte.Object")) {
			parent = parent.getParent();
		}
		return (parent as View).getController() as unknown as ObjectController;
	}

	async onSiteContact(): Promise<void> {
		const b = this.bauakte();
		await openForm({
			title: "Baustellenkontakt bearbeiten", initial: { ...b },
			fields: [
				{ key: "siteContactName", label: "Ansprechpartner vor Ort", placeholder: "z. B. Herr Bauer (Bauleitung GU)", maxLength: 100 },
				{ key: "siteContactPhone", label: "Telefon", maxLength: 50 },
				{ key: "siteContactEmail", label: "E-Mail Baustellenkontakt", type: "email", maxLength: 320 },
				{ key: "siteNotes", label: "Hinweise / Behinderungen", type: "textarea", maxLength: 2000 },
			],
			submit: async (v) => {
				await saveBauakte(b, { siteContactName: (v.siteContactName as string) || null, siteContactPhone: (v.siteContactPhone as string) || null,
					siteContactEmail: (v.siteContactEmail as string) || null, siteNotes: (v.siteNotes as string) || null });
				akte.invalidate(b.id);
				this.toast("Gespeichert");
			},
		});
	}

	onCustomer(): void {
		void this.objectController().onEdit();
	}

	onAssign(): void {
		void this.objectController().onAssign();
	}
}
