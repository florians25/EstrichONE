import SectionBase from "./SectionBase";
import JSONModel from "sap/ui/model/json/JSONModel";
import EventBus from "sap/ui/core/EventBus";
import type View from "sap/ui/core/mvc/View";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type MenuItem from "sap/m/MenuItem";
import type { Menu$ItemSelectedEvent } from "sap/m/Menu";
import { del, get, post, put } from "../../../util/api";
import { akte } from "../../../util/akteStore";
import { saveBauakte } from "../../../util/bauakte";
import type { DuplicateCandidate } from "../../../util/bauakteWizard";
import { KEEP_OPEN, openForm } from "../../../util/forms";
import { uuidv7 } from "../../../util/ids";
import type { BauakteDetail, BauakteMember, ContactEmail, ContactPhone, ContactResponse } from "../../../util/models";
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
	mobile?: string | null;
	street?: string | null;
	postalCode?: string | null;
	city?: string | null;
	isPrimary?: boolean;
	phones?: ContactPhone[] | null;
	emails?: ContactEmail[] | null;
	contactKind?: "company" | "person" | null;
	customerNumber?: string | null;
}

export const partyRoles: Record<PartyRole, { text: string; icon: string; state: "Information" | "Success" | "Warning" | "None" | "Indication05" }> = {
	client: { text: "Bauherr/Kunde", icon: "sap-icon://customer", state: "Information" },
	siteContact: { text: "Ansprechpartner vor Ort", icon: "sap-icon://building", state: "Success" },
	siteManager: { text: "Bauleiter", icon: "sap-icon://manager", state: "Warning" },
	architect: { text: "Architekt", icon: "sap-icon://draw-rectangle", state: "Indication05" },
	planner: { text: "Planer", icon: "sap-icon://draw-rectangle", state: "Indication05" },
	generalContractor: { text: "Generalunternehmer", icon: "sap-icon://factory", state: "Warning" },
	supplier: { text: "Lieferant", icon: "sap-icon://shipping-status", state: "None" },
	subcontractor: { text: "Nachunternehmer", icon: "sap-icon://group", state: "None" },
	other: { text: "Sonstige", icon: "sap-icon://person-placeholder", state: "None" },
};

/** Gruppen in der Anzeige (Reihenfolge = Wichtigkeit für Büro und Baustelle). */
const groups: { key: string; title: string; icon: string; roles: PartyRole[] }[] = [
	{ key: "client", title: "Kunde und Bauherr", icon: "sap-icon://customer", roles: ["client"] },
	{ key: "site", title: "Vor Ort", icon: "sap-icon://building", roles: ["siteContact"] },
	{ key: "manager", title: "Bauleitung", icon: "sap-icon://manager", roles: ["siteManager", "generalContractor"] },
	{ key: "planning", title: "Planung", icon: "sap-icon://draw-rectangle", roles: ["architect", "planner"] },
	{ key: "trades", title: "Lieferanten und Nachunternehmer", icon: "sap-icon://shipping-status", roles: ["supplier", "subcontractor"] },
	{ key: "other", title: "Sonstige", icon: "sap-icon://person-placeholder", roles: ["other"] },
];

const phoneKinds: Record<string, { text: string; icon: string }> = {
	mobile: { text: "Mobil", icon: "sap-icon://iphone" }, landline: { text: "Festnetz", icon: "sap-icon://call" },
	office: { text: "Telefon", icon: "sap-icon://call" }, fax: { text: "Fax", icon: "sap-icon://print" }, private: { text: "Privat", icon: "sap-icon://home" },
};

/** Ruhige Avatarfarben (Fiori Accent), stabil je Name. */
const accents = ["Accent6", "Accent7", "Accent8", "Accent10", "Accent9", "Accent5"];

interface Line { kind: string; icon: string; text: string; href: string | null; target: string; label: string | null }

interface Card {
	key: string;
	kind: "customer" | "site" | "party";
	role: PartyRole;
	roleText: string;
	name: string;
	subtitle: string;
	initials: string;
	avatarIcon: string;
	avatarColor: string;
	isCompany: boolean;
	primary: boolean;
	linked: boolean;
	contactId: string | null;
	customerNumber: string;
	lines: Line[];
	note: string | null;
	empty: string | null;
	removable: boolean;
	search: string;
}

export const initials = (name: string): string =>
	name.replace(/\b(Herr|Frau|Familie|Fam\.|Dr\.|GmbH|KG|AG|Co\.?)\b/g, "").split(/\s+/).filter((p) => /\p{L}/u.test(p)).slice(0, 2)
		.map((p) => p.match(/\p{L}/u)?.[0]?.toUpperCase() ?? "").join("") || "?";
const accentOf = (name: string): string => accents[[...name].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7) % accents.length];
const telHref = (n: string): string => `tel:${n.replace(/[^\d+]/g, "")}`;
const mapHref = (address: string): string => `https://www.openstreetmap.org/search?query=${encodeURIComponent(address)}`;
const place = (street?: string | null, postal?: string | null, city?: string | null): string | null =>
	[street, [postal, city].filter(Boolean).join(" ")].filter(Boolean).join(", ") || null;
const roleOptions = (Object.keys(partyRoles) as PartyRole[]).map((key) => ({ key, text: partyRoles[key].text }));

/** Telefon-, E-Mail- und Adresszeilen einer Karte (klickbar: tel:, mailto:, Karte). */
export function contactLines(phones: ContactPhone[], emails: ContactEmail[], address: string | null, website?: string | null): Line[] {
	const lines: Line[] = [];
	const seen = new Set<string>();
	for (const p of phones) {
		const key = p.number.replace(/[^\d+]/g, "");
		if (!key || seen.has(key)) continue;
		seen.add(key);
		const kind = phoneKinds[p.kind] ?? phoneKinds.office;
		lines.push({ kind: kind.text, icon: kind.icon, text: p.number, href: p.kind === "fax" ? null : telHref(p.number), target: "_self",
			label: [kind.text, p.label].filter(Boolean).join(" · ") });
	}
	for (const e of emails) {
		if (!e.address || seen.has(e.address.toLowerCase())) continue;
		seen.add(e.address.toLowerCase());
		lines.push({ kind: "E-Mail", icon: "sap-icon://email", text: e.address, href: `mailto:${e.address}`, target: "_self", label: e.label });
	}
	if (address) lines.push({ kind: "Anschrift", icon: "sap-icon://map-2", text: address, href: mapHref(address), target: "_blank", label: "In Karte öffnen" });
	if (website) lines.push({ kind: "Web", icon: "sap-icon://world", text: website.replace(/^https?:\/\//, ""), target: "_blank",
		href: /^https?:\/\//.test(website) ? website : `https://${website}`, label: null });
	return lines;
}

/**
 * Kontakte der Bauakte als Karten, gruppiert nach Rolle (Kunde, vor Ort, Bauleitung, Planung, Lieferanten …):
 * Avatar, Rolle als Badge, Hauptkontakt, alle Nummern und E-Mail-Adressen als Links (tel:/mailto:), Anschrift mit
 * Kartenlink. Hinzufügen aus dem Kundenstamm (Suche) oder neu mit Dublettenprüfung, Bearbeiten, Rolle ändern,
 * Hauptkontakt, Entfernen. Dieselben Endpunkte nutzt die iOS-App.
 * @namespace estrichone.controller.bauakte.sections
 */
export default class Contacts extends SectionBase {
	protected watches = ["parties", "members"];
	private parties: BauakteParty[] = [];
	private cards: Card[] = [];

	protected init(): void {
		this.v().setModel(new JSONModel({ groups: [], team: [], count: 0, partyCount: 0, query: "",
			roleItems: roleOptions }), "ct");
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

	private card(base: Partial<Card> & Pick<Card, "key" | "kind" | "role" | "name">): Card {
		const isCompany = base.isCompany ?? false;
		const name = base.name;
		const card: Card = {
			roleText: partyRoles[base.role].text, subtitle: "", initials: isCompany ? "" : initials(name), avatarIcon: isCompany ? "sap-icon://building" : "",
			avatarColor: accentOf(name), isCompany, primary: false, linked: false, contactId: null, customerNumber: "", lines: [], note: null,
			empty: null, removable: false, search: "", ...base,
		};
		card.search = [card.name, card.subtitle, card.roleText, card.note, ...card.lines.map((l) => l.text)].filter(Boolean).join(" ").toLowerCase();
		return card;
	}

	/** Karten aufbauen: Kunde und vor Ort aus der Bauakte, dann die Beteiligten (Hauptkontakt zuerst). */
	render(): void {
		const { bauakte: b, customer, members } = this.akteData();
		if (!b) return;
		const cards: Card[] = [];
		if (customer) {
			cards.push(this.card({ key: "customer", kind: "customer", role: "client", roleText: "Kunde (Auftraggeber)", name: customer.name,
				subtitle: customer.kind === "company" ? "Firma" : [customer.salutation, customer.title].filter(Boolean).join(" ") || "Privatkunde",
				isCompany: customer.kind === "company", linked: true, contactId: customer.id, customerNumber: customer.customerNumber ?? "",
				lines: contactLines(customer.phones?.length ? customer.phones : customer.phone ? [{ kind: "landline", number: customer.phone, label: null }] : [],
					customer.emails?.length ? customer.emails : customer.email ? [{ address: customer.email, label: null }] : [],
					place(customer.street, customer.postalCode, customer.city), customer.website),
				note: customer.notes }));
		} else {
			cards.push(this.card({ key: "customer", kind: "customer", role: "client", roleText: "Kunde (Auftraggeber)", name: "Noch kein Kunde",
				initials: "?", empty: "Kunde über „Bearbeiten“ zuordnen – mit Dublettenprüfung im Kundenstamm." }));
		}
		const siteLines = contactLines(b.siteContactPhone ? [{ kind: "mobile", number: b.siteContactPhone, label: null }] : [],
			b.siteContactEmail ? [{ address: b.siteContactEmail, label: null }] : [], place(b.street, b.postalCode, b.city));
		cards.push(this.card({ key: "site", kind: "site", role: "siteContact", name: b.siteContactName || "Baustelle", subtitle: b.siteContactName ? "laut Bauakte" : "Baustellenanschrift",
			isCompany: !b.siteContactName, lines: siteLines, note: b.siteNotes,
			empty: b.siteContactName ? null : "Kein Ansprechpartner vor Ort hinterlegt." }));
		for (const p of this.parties) {
			const own: ContactPhone[] = [];
			if (p.phone) own.push({ kind: "office", number: p.phone, label: null });
			if (p.mobile) own.push({ kind: "mobile", number: p.mobile, label: null });
			const phones = p.phones?.length ? p.phones : own;
			const emails = p.emails?.length ? p.emails : p.email ? [{ address: p.email, label: null }] : [];
			const company = p.contactKind === "company" || (!!p.company && p.company === p.name);
			cards.push(this.card({ key: p.id, kind: "party", role: p.role in partyRoles ? p.role : "other", name: p.name,
				subtitle: p.company && p.company !== p.name ? p.company : p.contactId ? "aus dem Kundenstamm" : "",
				isCompany: company, primary: !!p.isPrimary, linked: !!p.contactId, contactId: p.contactId, customerNumber: p.customerNumber ?? "",
				lines: contactLines(phones, emails, p.address), note: p.note, removable: true,
				empty: phones.length + emails.length === 0 && !p.address ? "Keine Telefonnummer oder E-Mail hinterlegt." : null }));
		}
		this.cards = cards;
		this.ct().setProperty("/count", cards.length);
		this.ct().setProperty("/partyCount", this.parties.length);
		this.ct().setProperty("/team", (members ?? []).map((m) => ({ displayName: m.displayName, initials: initials(m.displayName),
			roleText: m.role === "responsible" ? "Zuständig" : m.isActive ? "Mitarbeiter" : "Gesperrt" })));
		this.applyFilter();
	}

	private applyFilter(): void {
		const q = String(this.ct().getProperty("/query") ?? "").trim().toLowerCase();
		const visible = this.cards.filter((c) => !q || q.split(/\s+/).every((t) => c.search.includes(t)));
		this.ct().setProperty("/groups", groups
			.map((g) => {
				const cards = visible.filter((c) => g.roles.includes(c.role)).sort((a, b) => Number(b.primary) - Number(a.primary));
				return { key: g.key, title: g.title, icon: g.icon, count: String(cards.length), cards };
			})
			.filter((g) => g.cards.length > 0));
	}

	onFilter(): void {
		this.applyFilter();
	}

	private cardOf(event: Event): Card {
		const control = event.getSource() as Control;
		return control.getBindingContext("ct")?.getObject() as Card;
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

	/** Menü einer Karte: Hauptkontakt, Rolle ändern (Untermenü), im Kundenstamm öffnen, entfernen. */
	async onCardAction(event: Menu$ItemSelectedEvent): Promise<void> {
		const item = event.getParameter("item") as MenuItem;
		const menu = event.getSource() as Control;
		const card = menu.getBindingContext("ct")?.getObject() as Card | undefined;
		const party = card && this.parties.find((p) => p.id === card.key);
		if (!card || !party) return;
		const role = item.data("role") as PartyRole | null;
		const action = item.data("action") as string | null;
		try {
			if (role) {
				if (role !== party.role) await this.save(party, { role }, `Jetzt ${partyRoles[role].text}`);
			} else if (action === "primary") {
				this.apply(await (party.isPrimary ? del<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${party.id}/primary`)
					: post<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${party.id}/primary`, {})),
				party.isPrimary ? "Markierung entfernt" : `${party.name} ist jetzt Hauptkontakt`);
			} else if (action === "open" && party.contactId) {
				this.nav(`/kunden/${party.contactId}`);
			} else if (action === "remove") {
				await this.remove(card);
			}
		} catch (error) {
			this.showError(error);
		}
	}

	private apply(parties: BauakteParty[], message: string): void {
		this.parties = parties;
		this.render();
		this.toast(message);
		akte.invalidate(this.bauakteId, "history");
	}

	/** Eintrag mit geänderten Feldern speichern (vollständige Angaben, Version gegen parallele Änderungen). */
	private async save(party: BauakteParty, changes: Partial<BauakteParty>, message: string): Promise<void> {
		const p = { ...party, ...changes };
		this.apply(await put<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${p.id}`, {
			role: p.role, contactId: p.contactId, name: p.contactId ? null : p.name, company: p.company, phone: p.phone, email: p.email, note: p.note,
			mobile: p.mobile ?? null, street: p.street ?? null, postalCode: p.postalCode ?? null, city: p.city ?? null, version: p.version,
		}), message);
	}

	private async editSiteContact(): Promise<void> {
		const b = this.akteData().bauakte;
		const saved = await openForm({
			title: "Ansprechpartner vor Ort bearbeiten", initial: { ...b }, width: "32rem",
			fields: [
				{ key: "siteContactName", label: "Name", placeholder: "z. B. Herr Bauer (Bauleitung GU)", maxLength: 100 },
				{ key: "siteContactPhone", label: "Telefon", maxLength: 50 },
				{ key: "siteContactEmail", label: "E-Mail", type: "email", maxLength: 320 },
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

	/** Neu erfassen: frei, optional zusätzlich in den Kundenstamm (mit Dublettenprüfung auf dem Server). */
	async onQuickAdd(): Promise<void> {
		await this.editParty(null);
	}

	private async editParty(party: BauakteParty | null): Promise<void> {
		let checkedFor = "";
		const canCreateContact = session.has("contacts.edit");
		const linked = !!party?.contactId;
		const saved = await openForm<BauakteParty[]>({
			title: party ? `${party.name} bearbeiten` : "Neuen Kontakt erfassen", width: "38rem",
			intro: linked ? "Name, weitere Nummern und Anschrift kommen aus dem Kundenstamm. Hier erfasste Nummern gelten zusätzlich für diese Bauakte." : undefined,
			initial: party ? { ...party, isPrimary: !!party.isPrimary } : { role: "siteManager", toContacts: false, isPrimary: false },
			fields: [
				{ key: "role", label: "Rolle", type: "select", required: true, options: roleOptions },
				{ key: "name", label: "Name", required: !linked, maxLength: 200, placeholder: "z. B. Anna Lechner", editable: !linked,
					help: linked ? "Aus dem Kundenstamm – Name dort ändern." : undefined },
				{ key: "company", label: "Firma", maxLength: 200, placeholder: "z. B. Lechner Bau GmbH" },
				{ key: "phone", label: "Telefon", maxLength: 50, placeholder: "z. B. 0871 123456" },
				{ key: "mobile", label: "Mobil", maxLength: 50, placeholder: "z. B. 0171 2345678" },
				{ key: "email", label: "E-Mail", type: "email", maxLength: 320 },
				...(!linked ? [
					{ key: "street", label: "Straße", maxLength: 200 },
					{ key: "postalCode", label: "PLZ", maxLength: 10 },
					{ key: "city", label: "Ort", maxLength: 100 },
				] : []),
				{ key: "note", label: "Notiz", type: "textarea", rows: 2, maxLength: 1000, placeholder: "z. B. erreichbar ab 7 Uhr, Anlieferung nur vormittags" },
				{ key: "isPrimary", label: "Hauptkontakt dieser Bauakte", type: "checkbox" },
				...(!party && canCreateContact ? [{ key: "toContacts", label: "Auch im Kundenstamm anlegen (mit Dublettenprüfung)", type: "checkbox" as const }] : []),
			],
			submit: async (v, { warn }) => {
				let contactId = party?.contactId ?? null;
				const text = (key: string): string | null => (String(v[key] ?? "").trim() || null);
				if (!party && v.toContacts === true) {
					const name = String(v.company || v.name).trim();
					const key = JSON.stringify([name, v.phone, v.mobile, v.email, v.street]);
					if (key !== checkedFor) {
						checkedFor = key;
						const found = await get<DuplicateCandidate[]>("contacts/duplicates", { name, phone: text("phone") ?? text("mobile"), email: text("email"),
							street: text("street"), postalCode: text("postalCode"), city: text("city") });
						if (found.length > 0) {
							warn([`Mögliche Dubletten im Kundenstamm: ${found.slice(0, 3).map((d) => `${d.contact.name}${d.contact.city ? ` (${d.contact.city})` : ""} – ${d.reasons.join(", ")}`).join("; ")}.`,
								"Tipp: „Bestehenden Kontakt suchen“ verknüpft den vorhandenen Eintrag. Erneut „Speichern“ legt trotzdem neu an."], false);
							return KEEP_OPEN;
						}
					}
					contactId = uuidv7();
					const phones = [text("phone") && { kind: "office", number: text("phone"), label: null }, text("mobile") && { kind: "mobile", number: text("mobile"), label: null }].filter(Boolean);
					await post("contacts", { id: contactId, kind: v.company ? "company" : "person", name, phone: text("phone") ?? text("mobile"), email: text("email"),
						street: text("street"), postalCode: text("postalCode"), city: text("city"), phones });
				}
				return put<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${party?.id ?? uuidv7()}`, {
					role: v.role, contactId, name: linked ? null : text("name"), company: text("company"), phone: text("phone"), mobile: text("mobile"),
					email: text("email"), street: linked ? null : text("street"), postalCode: linked ? null : text("postalCode"), city: linked ? null : text("city"),
					note: text("note"), isPrimary: v.isPrimary === true, version: party?.version ?? null,
				});
			},
		});
		if (saved) this.apply(saved, "Kontakt gespeichert");
	}

	/** Vorhandenen Eintrag aus dem Kundenstamm suchen und als Beteiligten verknüpfen. */
	async onFromContacts(): Promise<void> {
		const saved = await openForm<BauakteParty[]>({
			title: "Bestehenden Kontakt hinzufügen", width: "34rem",
			intro: "Suchen Sie nach Name, Ort oder Telefonnummer. Telefon, E-Mail und Anschrift werden aus dem Kundenstamm übernommen und bleiben aktuell.",
			initial: { role: "architect", contactId: null, isPrimary: false },
			fields: [
				{ key: "contactId", label: "Kontakt", type: "customer", required: true },
				{ key: "role", label: "Rolle", type: "select", required: true, options: roleOptions },
				{ key: "company", label: "Firma / Funktion", maxLength: 200, placeholder: "optional, z. B. Bauleitung Nord" },
				{ key: "note", label: "Notiz", type: "textarea", rows: 2, maxLength: 1000 },
				{ key: "isPrimary", label: "Hauptkontakt dieser Bauakte", type: "checkbox" },
			],
			submit: (v) => put<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${uuidv7()}`, {
				role: v.role, contactId: v.contactId, name: null, company: (v.company as string) || null, note: (v.note as string) || null, isPrimary: v.isPrimary === true,
			}),
		});
		if (saved) this.apply(saved, "Kontakt hinzugefügt");
	}

	private async remove(card: Card): Promise<void> {
		if (!(await this.confirm(`„${card.name}“ (${card.roleText}) aus dieser Bauakte entfernen? Der Eintrag im Kundenstamm bleibt erhalten.`,
			{ title: "Entfernen", action: "Entfernen", danger: true }))) return;
		this.apply(await del<BauakteParty[]>(`bauakten/${this.bauakteId}/parties/${card.key}`), "Entfernt");
	}

	onAssign(): void {
		void this.objectController().onAssign();
	}
}
