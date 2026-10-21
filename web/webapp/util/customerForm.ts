/** Kunde anlegen/bearbeiten (erweiterte Kundendaten, Dublettenprüfung über alle Nummern). */
import { get, post, put } from "./api";
import { KEEP_OPEN, openForm } from "./forms";
import { uuidv7 } from "./ids";
import type { DuplicateCandidate } from "./bauakteWizard";
import type { ContactAddress, ContactEmail, ContactPhone, ContactResponse } from "./models";

export async function openCustomerForm(contact: ContactResponse | null, readOnly: boolean): Promise<boolean> {
	let checkedFor = "";
	const addr = (a: ContactAddress | null | undefined, p: string) => ({
		[`${p}Name`]: a?.name ?? null, [`${p}Street`]: a?.street ?? null, [`${p}PostalCode`]: a?.postalCode ?? null, [`${p}City`]: a?.city ?? null });
	const initial = contact
		? { ...contact, phones: contact.phones?.length ? contact.phones : contact.phone ? [{ kind: "landline", number: contact.phone, label: null }] : [],
			emails: contact.emails?.length ? contact.emails : contact.email ? [{ address: contact.email, label: null }] : [],
			...addr(contact.billingAddress, "billing"), ...addr(contact.deliveryAddress, "delivery") }
		: { kind: "person", phones: [], emails: [] };
	const address = (v: Record<string, unknown>, p: string): ContactAddress | null => {
		const a = { name: (v[`${p}Name`] as string) || null, street: (v[`${p}Street`] as string) || null,
			postalCode: (v[`${p}PostalCode`] as string) || null, city: (v[`${p}City`] as string) || null };
		return a.name || a.street || a.postalCode || a.city ? a : null;
	};
	const saved = await openForm({
		title: contact ? "Kunde bearbeiten" : "Neuer Kunde", readOnly, width: "42rem",
		initial,
		fields: [
			{ key: "kind", label: "Art", type: "segmented", options: [{ key: "person", text: "Privatperson" }, { key: "company", text: "Firma" }] },
			{ key: "salutation", label: "Anrede", type: "combo", maxLength: 30, options: ["Herr", "Frau", "Familie", "Firma", "Eheleute"].map((t) => ({ key: t, text: t })) },
			{ key: "title", label: "Titel", maxLength: 50, placeholder: "z. B. Dr." },
			{ key: "name", label: "Name", required: true, maxLength: 200 },
			{ key: "customerNumber", label: "Kundennummer", maxLength: 50 },
			{ key: "sAddress", label: "Anschrift", type: "section" },
			{ key: "street", label: "Straße", maxLength: 200 },
			{ key: "postalCode", label: "PLZ", maxLength: 10 },
			{ key: "city", label: "Ort", maxLength: 100 },
			{ key: "sContact", label: "Erreichbarkeit", type: "section" },
			{ key: "phones", label: "Telefon", type: "phones" },
			{ key: "emails", label: "E-Mail", type: "emails" },
			{ key: "preferredChannel", label: "Bevorzugter Kontaktweg", type: "select",
				options: [{ key: "", text: "–" }, { key: "phone", text: "Telefon" }, { key: "mobile", text: "Mobil" }, { key: "email", text: "E-Mail" }, { key: "letter", text: "Brief" }] },
			{ key: "website", label: "Website", maxLength: 200, placeholder: "https://" },
			{ key: "birthday", label: "Geburtstag (Privatkunden)", type: "date",
				validate: (v, all) => (v && all.kind === "company" ? "Geburtstag nur bei Privatpersonen." : null) },
			{ key: "sTax", label: "Steuer", type: "section" },
			{ key: "vatId", label: "USt-IdNr.", maxLength: 30, placeholder: "DE…" },
			{ key: "taxNumber", label: "Steuernummer", maxLength: 30 },
			{ key: "sBilling", label: "Abweichende Rechnungsanschrift", type: "section" },
			{ key: "billingName", label: "Empfänger", maxLength: 200 },
			{ key: "billingStreet", label: "Straße", maxLength: 200 },
			{ key: "billingPostalCode", label: "PLZ", maxLength: 10 },
			{ key: "billingCity", label: "Ort", maxLength: 100 },
			{ key: "sDelivery", label: "Abweichende Lieferanschrift", type: "section" },
			{ key: "deliveryName", label: "Empfänger", maxLength: 200 },
			{ key: "deliveryStreet", label: "Straße", maxLength: 200 },
			{ key: "deliveryPostalCode", label: "PLZ", maxLength: 10 },
			{ key: "deliveryCity", label: "Ort", maxLength: 100 },
			{ key: "sNotes", label: "Notiz", type: "section" },
			{ key: "notes", label: "Notizen", type: "textarea", maxLength: 4000 },
		],
		submit: async (v, { warn }) => {
			const phones = (v.phones as ContactPhone[]) ?? [];
			const emails = (v.emails as ContactEmail[]) ?? [];
			const bad = emails.find((e) => !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e.address));
			if (bad) throw new Error(`Ungültige E-Mail-Adresse „${bad.address}“.`);
			const body = { kind: v.kind, name: v.name, customerNumber: v.customerNumber || null, street: v.street || null, postalCode: v.postalCode || null,
				city: v.city || null, phone: phones[0]?.number ?? null, email: emails[0]?.address ?? null, notes: v.notes || null,
				phones, emails, salutation: v.salutation || null, title: v.title || null,
				billingAddress: address(v, "billing"), deliveryAddress: address(v, "delivery"), vatId: v.vatId || null, taxNumber: v.taxNumber || null,
				preferredChannel: v.preferredChannel || null, birthday: v.kind === "person" ? v.birthday || null : null, website: v.website || null };
			if (!contact) {
				// Dublettenprüfung auf dem Server (alle Nummern): Treffer zeigen; erneutes Speichern legt bewusst neu an.
				const key = JSON.stringify([body.name, body.street, body.postalCode, phones, emails]);
				if (key !== checkedFor) {
					checkedFor = key;
					const checks = await Promise.all((phones.length ? phones.map((p) => p.number) : [null]).map((phone, i) => get<DuplicateCandidate[]>("contacts/duplicates", {
						name: body.name as string, street: body.street as string | null, postalCode: body.postalCode as string | null,
						city: body.city as string | null, phone, email: emails[i]?.address ?? emails[0]?.address ?? null,
					})));
					const found = [...new Map(checks.flat().map((d) => [d.contact.id, d])).values()];
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
	return !!saved;
}
