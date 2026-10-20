import BaseController from "./BaseController";
import type { DuplicateCandidate } from "../util/bauakteWizard";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type FileUploader from "sap/ui/unified/FileUploader";
import MessageBox from "sap/m/MessageBox";
import { api, get, post, put } from "../util/api";
import { KEEP_OPEN, openForm } from "../util/forms";
import { uuidv7 } from "../util/ids";
import type { ContactResponse } from "../util/models";
import type { RouteMatch } from "../util/router";

interface ImportResult {
	totalRows: number; imported: number; wouldImport: number; dryRun: boolean;
	errors: { line: number; message: string }[]; duplicates: { line: number; name: string }[];
}

/** Kundenstamm: suchen, anlegen, bearbeiten (Versionsprüfung), CSV-Import mit Vorprüfung. @namespace estrichone.controller */
export default class Customers extends BaseController {
	onInit(): void {
		this.local({ items: [] as ContactResponse[], search: "", busy: false });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		await this.load();
		const id = match.query.get("kunde");
		if (id) {
			this.setQuery({ kunde: null });
			const contact = await get<ContactResponse>(`contacts/${id}`).catch(() => null);
			if (contact) void this.form(contact);
		}
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			m.setProperty("/items", await get<ContactResponse[]>("contacts", { limit: 500, search: m.getProperty("/search") || undefined }));
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onSearch(): void {
		void this.load();
	}

	private async form(contact: ContactResponse | null): Promise<void> {
		let checkedFor = "";
		const saved = await openForm({
			title: contact ? "Kunde bearbeiten" : "Neuer Kunde", readOnly: !this.has("contacts.edit"),
			initial: contact ? { ...contact } : { kind: "person" },
			fields: [
				{ key: "kind", label: "Art", type: "segmented", options: [{ key: "person", text: "Person" }, { key: "company", text: "Firma" }] },
				{ key: "name", label: "Name", required: true, maxLength: 200 },
				{ key: "customerNumber", label: "Kundennummer", maxLength: 50 },
				{ key: "street", label: "Straße", maxLength: 200 },
				{ key: "postalCode", label: "PLZ", maxLength: 10 },
				{ key: "city", label: "Ort", maxLength: 100 },
				{ key: "phone", label: "Telefon", maxLength: 50 },
				{ key: "email", label: "E-Mail", type: "email", maxLength: 320 },
				{ key: "notes", label: "Notizen", type: "textarea", maxLength: 4000 },
			],
			submit: async (v, { warn }) => {
				const body = { kind: v.kind, name: v.name, customerNumber: v.customerNumber || null, street: v.street || null, postalCode: v.postalCode || null,
					city: v.city || null, phone: v.phone || null, email: v.email || null, notes: v.notes || null };
				if (!contact) {
					// Dublettenprüfung auf dem Server: Treffer zeigen; erneutes Speichern legt bewusst neu an.
					const key = JSON.stringify([body.name, body.street, body.postalCode, body.phone, body.email]);
					if (key !== checkedFor) {
						checkedFor = key;
						const found = await get<DuplicateCandidate[]>("contacts/duplicates", {
							name: body.name as string, street: body.street as string | null, postalCode: body.postalCode as string | null,
							city: body.city as string | null, phone: body.phone as string | null, email: body.email as string | null,
						});
						if (found.length > 0) {
							warn([`Mögliche Dubletten: ${found.map((d) => `${d.contact.name}${d.contact.city ? ` (${d.contact.city})` : ""} – ${d.reasons.join(", ")}`).join("; ")}.`,
								"Erneut „Speichern“ legt den Kunden trotzdem neu an."], false);
							return KEEP_OPEN;
						}
					}
				}
				if (contact) await put(`contacts/${contact.id}`, { ...body, version: contact.version });
				else await post("contacts", { ...body, id: uuidv7() });
				return true;
			},
		});
		if (saved) {
			this.toast("Kunde gespeichert");
			void this.load();
		}
	}

	onNew(): void {
		void this.form(null);
	}

	onEdit(event: Event): void {
		void this.form((event.getSource() as Control).getBindingContext("view")?.getObject() as ContactResponse);
	}

	/** CSV: erst prüfen (Fehler und Dubletten zeigen), dann auf Wunsch übernehmen. */
	async onImport(event: Event): Promise<void> {
		const uploader = event.getSource() as FileUploader;
		const file = (event as unknown as { getParameter(n: string): FileList }).getParameter("files")?.[0];
		if (!file) return;
		const content = await file.arrayBuffer();
		const send = (dryRun: boolean) => api<ImportResult>("contacts/import", { method: "POST", raw: content, query: { dryRun }, headers: { "Content-Type": "text/csv" } });
		const check = await this.run(() => send(true));
		uploader.clear();
		if (!check) return;
		const details = [
			`${check.totalRows} Zeilen, ${check.wouldImport} werden übernommen.`,
			...check.errors.slice(0, 10).map((e) => `Zeile ${e.line}: ${e.message}`),
			...check.duplicates.slice(0, 10).map((d) => `Zeile ${d.line}: „${d.name}“ gibt es schon (wird übersprungen).`),
		].join("\n");
		if (check.wouldImport === 0) {
			MessageBox.warning(details, { title: "Nichts zu importieren" });
			return;
		}
		MessageBox.confirm(details, {
			title: "Kunden importieren?", actions: ["Importieren", MessageBox.Action.CANCEL], emphasizedAction: "Importieren",
			onClose: async (action: string) => {
				if (action !== "Importieren") return;
				const result = await this.run(() => send(false));
				if (result) {
					this.toast(`${result.imported} Kunden importiert`);
					void this.load();
				}
			},
		});
	}
}
