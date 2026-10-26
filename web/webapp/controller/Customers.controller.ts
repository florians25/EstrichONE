import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type FileUploader from "sap/ui/unified/FileUploader";
import MessageBox from "sap/m/MessageBox";
import { api, get, post } from "../util/api";
import type Table from "sap/m/Table";
import { openForm } from "../util/forms";
import { openCustomerForm } from "../util/customerForm";
import type { ContactResponse } from "../util/models";
import type { RouteMatch } from "../util/router";

interface ImportResult {
	totalRows: number; imported: number; wouldImport: number; dryRun: boolean;
	errors: { line: number; message: string }[]; duplicates: { line: number; name: string }[];
}

const esc = (v: string) => v.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);

/** Suchtreffer hervorheben (FormattedText, sicher maskiert). */
export function highlight(text: string | null | undefined, query: string): string {
	const value = esc(text ?? "");
	const q = query.trim();
	if (!q) return value;
	const pattern = new RegExp(`(${esc(q).replace(/[.*+?^${}()|[\]\\]/g, "\\$&")})`, "gi");
	return value.replace(pattern, "<strong>$1</strong>");
}

/**
 * Kundenstamm: Suche über Name, Kundennummer, Ort, alle Telefonnummern/E-Mails und Ansprechpartner (Treffer
 * hervorgehoben), Filter Firma/Privat, Sortierung; Klick öffnet die Kundenseite. Anlegen, Bearbeiten, CSV-Import.
 * @namespace estrichone.controller
 */
export default class Customers extends BaseController {
	onInit(): void {
		this.local({ items: [], search: "", kind: "", sort: "name", busy: false });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		await this.load();
		const id = match.query.get("kunde");
		if (id) {
			this.setQuery({ kunde: null });
			this.nav(`/kunden/${id}`);
		}
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const q = String(m.getProperty("/search") ?? "");
		try {
			const items = await get<ContactResponse[]>("contacts", { limit: 500, search: q || undefined, kind: m.getProperty("/kind") || undefined,
				sort: m.getProperty("/sort") });
			m.setProperty("/items", items.map((c) => {
				const phones = (c.phones ?? []).map((p) => p.number);
				const emails = (c.emails ?? []).map((e) => e.address);
				return {
					...c, nameHtml: highlight(c.name, q), numberHtml: highlight(c.customerNumber ?? "–", q),
					placeHtml: highlight([c.postalCode, c.city].filter(Boolean).join(" ") || "–", q),
					phonesHtml: highlight(phones.join(", ") || "–", q), emailsHtml: highlight(emails.join(", ") || "–", q),
					sub: [c.salutation, c.title].filter(Boolean).join(" "),
				};
			}));
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onSearch(): void { void this.load(); }
	onFilter(): void { void this.load(); }

	onNew(): void {
		void openCustomerForm(null, false).then((saved) => {
			if (saved) {
				this.toast("Kunde gespeichert");
				void this.load();
			}
		});
	}

	onOpen(event: Event): void {
		const c = (event.getSource() as Control).getBindingContext("view")?.getObject() as ContactResponse;
		this.nav(`/kunden/${c.id}`);
	}

	async onEdit(event: Event): Promise<void> {
		const c = (event.getSource() as Control).getBindingContext("view")?.getObject() as ContactResponse;
		if (await openCustomerForm(c, !this.has("contacts.edit"))) {
			this.toast("Kunde gespeichert");
			void this.load();
		}
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

	/** Serien-E-Mail an die ausgewählten Kunden (B10); Platzhalter {Name} und {Anrede}. */
	async onMailing(): Promise<void> {
		const table = this.byId("customerTable") as Table;
		const selected = table.getSelectedItems().map((i) => i.getBindingContext("view")?.getObject() as ContactResponse).filter(Boolean);
		if (!selected.length) {
			this.toast("Bitte zuerst Kunden in der Liste auswählen (Häkchen).");
			return;
		}
		const result = await openForm<{ queued: number; withoutEmail: string[]; mailConfigured: boolean }>({
			title: `Serien-E-Mail an ${selected.length} Kunde${selected.length === 1 ? "" : "n"}`, width: "40rem",
			intro: "Jeder Kunde erhält eine eigene E-Mail. Platzhalter: {Anrede} (z. B. „Sehr geehrte Frau Kraus“) und {Name}.",
			initial: { body: "{Anrede},\n\n\n\nMit freundlichen Grüßen" },
			fields: [
				{ key: "subject", label: "Betreff", required: true, maxLength: 200 },
				{ key: "body", label: "Text", type: "textarea", rows: 10, required: true, maxLength: 20000 },
			],
			submit: (v) => post("contacts/mailing", { contactIds: selected.map((c) => c.id), subject: v.subject, body: v.body }),
		});
		if (!result) return;
		if (!result.mailConfigured) {
			MessageBox.warning("Es ist noch kein E-Mail-Konto eingerichtet (System → E-Mail). Es wurde nichts versendet.");
			return;
		}
		table.removeSelections(true);
		const missing = result.withoutEmail.length ? ` – ohne E-Mail-Adresse: ${result.withoutEmail.join(", ")}` : "";
		MessageBox.information(`${result.queued} E-Mail(s) in den Versand gegeben${missing}.`, { title: "Serien-E-Mail" });
	}
}
