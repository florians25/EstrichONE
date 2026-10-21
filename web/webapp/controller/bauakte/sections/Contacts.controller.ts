import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import EventBus from "sap/ui/core/EventBus";
import type View from "sap/ui/core/mvc/View";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { del, get, post, put } from "../../../util/api";
import { akte } from "../../../util/akteStore";
import { saveBauakte } from "../../../util/bauakte";
import type { DuplicateCandidate } from "../../../util/bauakteWizard";
import { KEEP_OPEN, openForm } from "../../../util/forms";
import { uuidv7 } from "../../../util/ids";
import type { BauakteDetail, BauakteMember, ContactResponse } from "../../../util/models";
import { session } from "../../../util/session";
import type ObjectController from "../Object.controller";

export type PartyRole = "client" | "siteContact" | "siteManager" | "architect" | "planner" | "generalContractor" | "supplier" | "subcontractor" | "other";

export interface BauakteParty {
	id: string;
	role: PartyRole;
	contactId: string | null;
	name: string;
	company: string | null;
	phone: string | null;
	email: string | null;
	note: string | null;
	address: string | null;
	version: number;
}

export const partyRoles: Record<PartyRole, { text: string; icon: string; state: "Information" | "Success" | "Warning" | "None" | "Indication05" }> = {
	client: { text: "Bauherr/Kunde", icon: "sap-icon://customer", state: "Information" },
	siteContact: { text: "Ansprechpartner vor Ort", icon: "sap-icon://building", state: "Success" },
	siteManager: { text: "Bauleiter", icon: "sap-icon://manager", state: "Warning" },
	architect: { text: "Architekt", icon: "sap-icon://blueprint", state: "Indication05" },
	planner: { text: "Planer", icon: "sap-icon://drawing-tool", state: "Indication05" },
	generalContractor: { text: "Generalunternehmer", icon: "sap-icon://factory", state: "Warning" },
	supplier: { text: "Lieferant", icon: "sap-icon://shipping-status", state: "None" },
	subcontractor: { text: "Nachunternehmer", icon: "sap-icon://group", state: "None" },
	other: { text: "Sonstige", icon: "sap-icon://person-placeholder", state: "None" },
};

interface Card {
	key: string;
	kind: "customer" | "site" | "party";
	icon: string;
	roleText: string;
	roleState: string;
	name: string;
	company: string | null;
	phone: string | null;
	email: string | null;
	address: string | null;
	note: string | null;
	editable: boolean;
	removable: boolean;
}

const initials = (name: string) => name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join("") || "?";
const roleOptions = (Object.keys(partyRoles) as PartyRole[]).filter((r) => r !== "siteContact").map((key) => ({ key, text: partyRoles[key].text }));

/**
 * Kontakte der Bauakte als Karten: Kunde, Ansprechpartner vor Ort und weitere Beteiligte (Bauleiter, Architekt,
 * Lieferant …) mit Telefon und E-Mail als Aktionen; Schnellerfassung (mit Dublettenprüfung, optional in den
 * Kundenstamm), Übernahme aus dem Kundenstamm, Team.
 * @namespace estrichone.controller.bauakte.sections
 */
export default class Contacts extends SectionBase {
	protected watches = ["parties", "members"];
	private parties: BauakteParty[] = [];

	protected init(): void {
		this.v().setModel(new JSONModel({ cards: [], team: [] }), "ct");
		// Kunde und Team lädt die Objektseite nach: dann neu zeichnen.
		EventBus.getInstance().subscribe("bauakte", "side", (_c: string, _e: string, data: object) => {
			if ((data as { id: string }).id === this.bauakteId) this.render();
		});
	}

	private ct(): JSONModel {
		return this.v().getModel("ct") as JSONModel;
	}

	protected async load(): Promise<void> {
		this.parties = await get<BauakteParty[]>(`bauakten/${this.bauakteId}/parties`).catch(() => []);
		this.render();
	}

	protected onBauakteUpdated(): void {
		this.render();
	}

	private akteData(): { bauakte: BauakteDetail; customer: ContactResponse | null; members: BauakteMember[] } {
		return (this.v().getModel("akte") as JSONModel).getData() as { bauakte: BauakteDetail; customer: ContactResponse | null; members: BauakteMember[] };
	}

	/** Karten in sinnvoller Reihenfolge: Kunde, vor Ort, dann Beteiligte nach Rolle und Name (Server sortiert). */
	render(): void {
		const { bauakte: b, customer, members } = this.akteData();
		if (!b) return;
		const cards: Card[] = [];
		const place = (street: string | null, postal: string | null, city: string | null) =>
			[street, [postal, city].filter(Boolean).join(" ")].filter(Boolean).join(", ") || null;
		if (customer) {
			cards.push({ key: "customer", kind: "customer", icon: partyRoles.client.icon, roleText: "Kunde (Auftraggeber)", roleState: "Information",
				name: customer.name, company: customer.customerNumber ? `Kundennummer ${customer.customerNumber}` : null, phone: customer.phone, email: customer.email,
				address: place(customer.street, customer.postalCode, customer.city), note: null, editable: true, removable: false });
		} else {
			cards.push({ key: "customer", kind: "customer", icon: partyRoles.client.icon, roleText: "Kunde (Auftraggeber)", roleState: "Warning",
				name: "Noch kein Kunde", company: null, phone: null, email: null, address: null, note: "Kunde über „Bearbeiten“ zuordnen.", editable: true, removable: false });
		}
		cards.push({ key: "site", kind: "site", icon: partyRoles.siteContact.icon, roleText: partyRoles.siteContact.text, roleState: partyRoles.siteContact.state,
			name: b.siteContactName || "Kein Ansprechpartner vor Ort", company: null, phone: b.siteContactPhone, email: b.siteContactEmail,
			address: place(b.street, b.postalCode, b.city), note: b.siteNotes, editable: true, removable: false });
		for (const p of this.parties) {
			const role = partyRoles[p.role] ?? partyRoles.other;
			cards.push({ key: p.id, kind: "party", icon: role.icon, roleText: role.text, roleState: role.state, name: p.name, company: p.company,
				phone: p.phone, email: p.email, address: p.address, note: p.note, editable: true, removable: true });
		}
		this.ct().setProperty("/cards", cards);
		this.ct().setProperty("/team", (members ?? []).map((m) => ({ displayName: m.displayName, initials: initials(m.displayName),
			roleText: m.role === "responsible" ? "Zuständig" : m.isActive ? "Mitarbeiter" : "Gesperrt" })));
	}

	private cardOf(event: Event): Card {
		const control = event.getSource() as Control;
		return control.getBindingContext("ct")?.getObject() as Card;
	}

	onCall(event: Event): void {
		const card = this.cardOf(event);
		if (card.phone) window.location.href = `tel:${card.phone.replace(/[^\d+]/g, "")}`;
	}

	onMail(event: Event): void {
		const card = this.cardOf(event);
		if (card.email) window.location.href = `mailto:${card.email}`;
	}

	private objectController(): ObjectController {
		let parent = this.v().getParent();
		while (parent && !(parent as View).getController?.()?.getMetadata().getName().endsWith("bauakte.Object")) {
			parent = parent.getParent();
		}
		return (parent as View).getController() as unknown as ObjectController;
	}

	async onEditCard(event: Event): Promise<void> {
		const card = this.cardOf(event);
		if (card.kind === "customer") {
			void this.objectController().onEdit();
			return;
		}
		if (card.kind === "site") {
			await this.editSiteContact();
			return;
		}
		const party = this.parties.find((p) => p.id === card.key);
		if (party) await this.editParty(party);
	}

	private async editSiteContact(): Promise<void> {
		const b = this.akteData().bauakte;
		const saved = await openForm({
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
				return true;
			},
		});
		if (saved) {
			akte.invalidate(b.id);
			this.toast("Gespeichert");
		}
	}

	/** Schnellerfassung: frei, optional zusätzlich in den Kundenstamm (mit Dublettenprüfung auf dem Server). */
	async onQuickAdd(): Promise<void> {
		await this.editParty(null);
	}

	private async editParty(party: BauakteParty | null): Promise<void> {
		let checkedFor = "";
		const canCreateContact = session.has("contacts.write");
		const saved = await openForm<BauakteParty[]>({
			title: party ? "Kontakt bearbeiten" : "Kontakt erfassen", width: "34rem",
			initial: party ? { ...party } : { role: "siteManager", toContacts: false },
			fields: [
				{ key: "role", label: "Rolle", type: "select", required: true, options: roleOptions },
				{ key: "name", label: "Name", required: true, maxLength: 200, placeholder: "z. B. Herr Lehner", editable: !party?.contactId,
					help: party?.contactId ? "Aus dem Kundenstamm – Name dort ändern." : undefined },
				{ key: "company", label: "Firma", maxLength: 200 },
				{ key: "phone", label: "Telefon", maxLength: 50 },
				{ key: "email", label: "E-Mail", type: "email", maxLength: 320 },
				{ key: "note", label: "Notiz", type: "textarea", rows: 2, maxLength: 1000, placeholder: "z. B. erreichbar ab 7 Uhr, Anlieferung nur vormittags" },
				...(!party && canCreateContact ? [{ key: "toContacts", label: "Auch im Kundenstamm anlegen", type: "checkbox" as const }] : []),
			],
			submit: async (v, { warn }) => {
				let contactId = party?.contactId ?? null;
				if (!party && v.toContacts === true) {
					const name = String(v.company || v.name).trim();
					const key = JSON.stringify([name, v.phone, v.email]);
					if (key !== checkedFor) {
						checkedFor = key;
						const found = await get<DuplicateCandidate[]>("contacts/duplicates", { name, phone: (v.phone as string) || null, email: (v.email as string) || null });
						if (found.length > 0) {
							warn([`Mögliche Dubletten im Kundenstamm: ${found.map((d) => `${d.contact.name}${d.contact.city ? ` (${d.contact.city})` : ""} – ${d.reasons.join(", ")}`).join("; ")}.`,
								"Tipp: „Aus Kundenstamm übernehmen“ verknüpft den vorhandenen Eintrag. Erneut „Speichern“ legt trotzdem neu an."], false);
							return KEEP_OPEN;
						}
					}
					contactId = uuidv7();
					await post("contacts", { id: contactId, kind: v.company ? "company" : "person", name, phone: v.phone || null, email: v.email || null });
				}
				return put<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${party?.id ?? uuidv7()}`, {
					role: v.role, contactId, name: v.name, company: v.company || null, phone: v.phone || null, email: v.email || null, note: v.note || null,
					version: party?.version ?? null,
				});
			},
		});
		if (saved) {
			this.parties = saved;
			this.render();
			this.toast("Kontakt gespeichert");
			akte.invalidate(this.bauakteId, "history");
		}
	}

	/** Vorhandenen Eintrag aus dem Kundenstamm als Beteiligten verknüpfen. */
	async onFromContacts(): Promise<void> {
		const saved = await openForm<BauakteParty[]>({
			title: "Aus Kundenstamm übernehmen", width: "32rem",
			initial: { role: "architect", contactId: null },
			fields: [
				{ key: "contactId", label: "Kontakt", type: "customer", required: true },
				{ key: "role", label: "Rolle", type: "select", required: true, options: roleOptions },
				{ key: "note", label: "Notiz", type: "textarea", rows: 2, maxLength: 1000 },
			],
			submit: (v) => put<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${uuidv7()}`, {
				role: v.role, contactId: v.contactId, name: null, note: v.note || null,
			}),
		});
		if (saved) {
			this.parties = saved;
			this.render();
			this.toast("Kontakt übernommen");
			akte.invalidate(this.bauakteId, "history");
		}
	}

	async onRemoveCard(event: Event): Promise<void> {
		const card = this.cardOf(event);
		if (!(await this.confirm(`„${card.name}“ (${card.roleText}) aus dieser Bauakte entfernen? Der Eintrag im Kundenstamm bleibt erhalten.`,
			{ title: "Entfernen", action: "Entfernen", danger: true }))) return;
		try {
			this.parties = await del<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${card.key}`);
			this.render();
			this.toast("Entfernt");
			akte.invalidate(this.bauakteId, "history");
		} catch (error) {
			this.showError(error);
		}
	}

	onAssign(): void {
		void this.objectController().onAssign();
	}
}
