import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type ObjectPageLayout from "sap/uxap/ObjectPageLayout";
import type ObjectPageSection from "sap/uxap/ObjectPageSection";
import { del, get, put } from "../util/api";
import { approvalStatus, type ApprovalStatus } from "../util/approvals";
import { openBauakteWizard } from "../util/bauakteWizard";
import { openCustomerForm } from "../util/customerForm";
import { filterByStatus, latest, routeUrl, type KeyFigures } from "../util/customerMatch";
import { fileIcon } from "../util/documents";
import { area, date } from "../util/format";
import { documentCategories } from "../util/labels";
import type { BauaktePage, BauakteListItem, CallResponse, ContactAddress, ContactResponse, FileResponse } from "../util/models";
import { openPreview } from "../util/previewDialog";
import { openForm, phoneKinds } from "../util/forms";
import { uuidv7 } from "../util/ids";
import type { RouteMatch } from "../util/router";
import { navTo } from "../util/navigation";

interface DocumentListItem { file: FileResponse; bauakteNumber: string | null; bauakteTitle: string; approvalStatus: ApprovalStatus | null }
interface Person { id: string; name: string; function: string | null; phone: string | null; email: string | null }
type AkteRow = BauakteListItem & { place: string };

const sections: Record<string, string> = { uebersicht: "secOverview", bauakten: "secBauakten", dokumente: "secDocuments", kontakte: "secContacts", verlauf: "secHistory" };
const channels: Record<string, string> = { phone: "Telefon", mobile: "Mobil", email: "E-Mail", letter: "Brief" };
const addressText = (a: ContactAddress | null | undefined) =>
	a ? [a.name, a.street, [a.postalCode, a.city].filter(Boolean).join(" ")].filter(Boolean).join(", ") || "–" : "wie Anschrift";
const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/**
 * Kundenakte: Kopf mit Kennzahlen (Bauakten laufend/gesamt, Anfragen, Fläche, offene Aufgaben und Mängel, letzte
 * Aktivität) und Schnellaktionen (Anrufen, E-Mail, Route, Bearbeiten, Neue Bauakte). Abschnitte: Übersicht (Notiz,
 * Stammdaten), Bauakten mit Statusfilter und Suche, Kontakte (Telefon/E-Mail, Ansprechpartner pflegen), Dokumente aller
 * Bauakten und Notizen/Verlauf.
 * @namespace estrichone.controller
 */
export default class Customer extends BaseController {
	private id = "";
	private files: FileResponse[] = [];

	onInit(): void {
		this.local({ c: null, busy: false, headline: "", address: "", salutation: "", channel: "", billing: "", delivery: "",
			primaryPhone: "", primaryEmail: "", routeUrl: null, mainPerson: "", kpis: [],
			bauakten: [], shownBauakten: [], bauakteFilter: "", bauakteSearch: "", bauakteEmpty: "", counts: { all: 0, active: 0, inquiry: 0, old: 0 },
			documents: [], reach: [], persons: [], history: [] });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		if (this.id !== match.params.id) this.model().setProperty("/bauakteFilter", "");
		this.id = match.params.id;
		await this.load();
		const section = sections[match.query.get("abschnitt") ?? ""];
		if (section) (this.byId("objectPage") as ObjectPageLayout).setSelectedSection(this.byId(section) as ObjectPageSection);
	}

	onSection(e: Event): void {
		const section = e.getParameter("section" as never) as ObjectPageSection;
		const key = Object.entries(sections).find(([, id]) => this.byId(id) === section)?.[0];
		if (key) this.setQuery({ abschnitt: key === "uebersicht" ? null : key });
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const c = await this.run(() => get<ContactResponse>(`contacts/${this.id}`));
		if (!c) {
			m.setProperty("/busy", false);
			this.nav("/kunden", true);
			return;
		}
		document.title = `${c.name} – EstrichONE`;
		this.showContact(c);

		const readAkten = this.has("bauakten.read");
		const [akten, docs, persons, calls, figures] = await Promise.allSettled([
			readAkten ? get<BauaktePage>("bauakten", { customerId: this.id, status: "all", limit: 200 }) : Promise.resolve({ items: [], nextCursor: null }),
			readAkten ? get<{ items: DocumentListItem[] }>("documents", { customerId: this.id, sort: "bauakte", limit: 500 }) : Promise.resolve({ items: [] }),
			get<Person[]>(`contacts/${this.id}/persons`),
			get<CallResponse[]>("calls", { customerId: this.id, limit: 100 }),
			readAkten ? get<KeyFigures>(`contacts/${this.id}/key-figures`) : Promise.resolve(null),
		]);
		const value = <T>(r: PromiseSettledResult<T>, fallback: T) => (r.status === "fulfilled" ? r.value : fallback);
		const bauakten = value(akten, { items: [], nextCursor: null } as BauaktePage).items;
		m.setProperty("/bauakten", bauakten.map((b) => ({ ...b, place: [b.street, [b.postalCode, b.city].filter(Boolean).join(" ")].filter(Boolean).join(", ") || "–" })));
		m.setProperty("/counts", {
			all: bauakten.length, active: filterByStatus(bauakten, "active").length, inquiry: filterByStatus(bauakten, "inquiry").length,
			old: filterByStatus(bauakten, "old").length,
		});
		this.applyBauakteFilter();

		const items = value(docs, { items: [] as DocumentListItem[] }).items;
		this.files = items.map((i) => i.file);
		m.setProperty("/documents", items.map((i) => ({
			...i, icon: fileIcon(i.file.contentType, i.file.originalName),
			group: `${i.bauakteNumber ? `${i.bauakteNumber} · ` : ""}${i.bauakteTitle} › ${i.file.category ? documentCategories[i.file.category]?.text ?? i.file.category : "Ohne Dokumentart"}`,
			approvalText: i.approvalStatus ? approvalStatus[i.approvalStatus].text : "",
		})));
		const people = value(persons, []);
		m.setProperty("/persons", people);
		m.setProperty("/mainPerson", people[0] ? [people[0].name, people[0].function, people[0].phone].filter(Boolean).join(" · ") : "");

		const callList = value(calls, []);
		const history = [
			...callList.map((x) => ({
				at: x.calledAt, icon: x.kind === "inquiry" ? "sap-icon://inbox" : "sap-icon://call",
				title: `${x.kind === "inquiry" ? "Anfrage" : "Anruf"}${x.subject ? `: ${x.subject}` : ""}`,
				text: [x.callerName, x.notes].filter(Boolean).join(": "), to: x.kind === "inquiry" ? "/anfragen" : "/anrufe",
			})),
			...bauakten.map((b) => ({ at: b.updatedAt, icon: "sap-icon://folder-full", title: `Bauakte ${b.number ?? ""} ${b.title}`.replace(/\s+/g, " "),
				text: "zuletzt geändert", to: `/bauakten/${b.id}` })),
		].sort((a, b) => b.at.localeCompare(a.at));
		m.setProperty("/history", history);
		this.showKpis(value(figures, null), latest(history[0]?.at));
		m.setProperty("/busy", false);
	}

	private showContact(c: ContactResponse): void {
		const m = this.model();
		m.setProperty("/c", c);
		m.setProperty("/headline", [c.customerNumber ? `Kd.-Nr. ${c.customerNumber}` : "ohne Kundennummer", [c.postalCode, c.city].filter(Boolean).join(" ")].filter(Boolean).join(" · "));
		m.setProperty("/address", [c.street, [c.postalCode, c.city].filter(Boolean).join(" ")].filter(Boolean).join(", ") || "–");
		m.setProperty("/salutation", [c.salutation, c.title].filter(Boolean).join(" ") || "–");
		m.setProperty("/channel", c.preferredChannel ? channels[c.preferredChannel] : "–");
		m.setProperty("/billing", addressText(c.billingAddress));
		m.setProperty("/delivery", addressText(c.deliveryAddress));
		m.setProperty("/primaryPhone", c.phones?.[0]?.number ?? c.phone ?? "");
		m.setProperty("/primaryEmail", c.emails?.[0]?.address ?? c.email ?? "");
		m.setProperty("/routeUrl", routeUrl(c));
		const kindText = Object.fromEntries(phoneKinds.map((k) => [k.key, k.text]));
		m.setProperty("/reach", [
			...(c.phones ?? []).map((p) => ({ value: p.number, label: [kindText[p.kind] ?? p.kind, p.label].filter(Boolean).join(" · "),
				icon: p.kind === "mobile" ? "sap-icon://iphone" : p.kind === "fax" ? "sap-icon://print" : "sap-icon://call", href: `tel:${p.number.replace(/\s/g, "")}` })),
			...(c.emails ?? []).map((e) => ({ value: e.address, label: e.label ?? "E-Mail", icon: "sap-icon://email", href: `mailto:${e.address}` })),
		]);
	}

	private showKpis(f: KeyFigures | null, lastHistory: string | null): void {
		const kpis: { label: string; value: string; state: string; hint: string }[] = [];
		if (f) {
			kpis.push({ label: "Bauakten laufend", value: `${f.bauaktenActive} von ${f.bauaktenTotal}`, state: "None",
				hint: [f.inquiries ? plural(f.inquiries, "Anfrage", "Anfragen") : null, f.completed ? `${f.completed} abgeschlossen` : null].filter(Boolean).join(" · ") });
			kpis.push({ label: "Gesamtfläche", value: f.totalArea > 0 ? area(f.totalArea) : "–", state: "None", hint: "aus Kennwerten bzw. Räumen" });
			if (f.openTasks !== null || f.openDefects !== null) {
				const open = (f.openTasks ?? 0) + (f.openDefects ?? 0);
				kpis.push({ label: "Offene Aufgaben", value: String(open), state: f.openDefects ? "Warning" : "None",
					hint: f.openDefects ? `davon ${plural(f.openDefects, "Mangel", "Mängel")}` : "keine offenen Mängel" });
			}
			if (f.nextPlannedStart) kpis.push({ label: "Nächste Ausführung", value: date(f.nextPlannedStart), state: "Information", hint: "" });
		}
		const last = latest(f?.lastActivity, lastHistory);
		kpis.push({ label: "Letzte Aktivität", value: last ? date(last) : "–", state: "None", hint: "" });
		this.model().setProperty("/kpis", kpis);
	}

	private applyBauakteFilter(): void {
		const m = this.model();
		const q = String(m.getProperty("/bauakteSearch") ?? "").trim().toLowerCase();
		const filter = String(m.getProperty("/bauakteFilter") ?? "");
		const shown = filterByStatus(m.getProperty("/bauakten") as AkteRow[], filter)
			.filter((b) => !q || [b.number, b.title, b.place].some((t) => (t ?? "").toLowerCase().includes(q)));
		m.setProperty("/shownBauakten", shown);
		m.setProperty("/bauakteEmpty", (m.getProperty("/bauakten") as AkteRow[]).length === 0
			? "Für diesen Kunden gibt es noch keine Bauakte." : "Keine Bauakten in dieser Auswahl.");
	}

	onBauakteFilter(): void { this.applyBauakteFilter(); }

	private obj<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	onCall(): void { window.location.href = `tel:${String(this.model().getProperty("/primaryPhone")).replace(/\s/g, "")}`; }
	onMail(): void { window.location.href = `mailto:${this.model().getProperty("/primaryEmail")}`; }
	onRoute(): void { window.open(this.model().getProperty("/routeUrl") as string, "_blank", "noopener"); }
	onBauakte(e: Event): void { this.nav(`/bauakten/${this.obj<{ id: string }>(e).id}`); }
	onHistory(e: Event): void { const to = this.obj<{ to?: string }>(e).to; if (to) this.nav(to); }

	onCopy(e: Event): void {
		const text = this.obj<{ value: string }>(e).value;
		void navigator.clipboard?.writeText(text).then(() => this.toast(`Kopiert: ${text}`)).catch(() => undefined);
	}

	async onEdit(): Promise<void> {
		if (await openCustomerForm(this.model().getProperty("/c") as ContactResponse, !this.has("contacts.edit"))) {
			this.toast("Kunde gespeichert");
			await this.load();
		}
	}

	async onEditNotes(): Promise<void> {
		const c = this.model().getProperty("/c") as ContactResponse;
		const saved = await openForm({
			title: "Notiz bearbeiten", width: "36rem", initial: { notes: c.notes ?? "" },
			fields: [{ key: "notes", label: "Notiz und Zahlungshinweise", type: "textarea", rows: 6, maxLength: 4000 }],
			submit: (v) => put<ContactResponse>(`contacts/${c.id}`, {
				version: c.version, kind: c.kind, name: c.name, customerNumber: c.customerNumber, street: c.street, postalCode: c.postalCode, city: c.city,
				phone: c.phone, email: c.email, notes: String(v.notes ?? "").trim() || null, phones: c.phones, emails: c.emails, salutation: c.salutation,
				title: c.title, billingAddress: c.billingAddress, deliveryAddress: c.deliveryAddress, vatId: c.vatId, taxNumber: c.taxNumber,
				preferredChannel: c.preferredChannel, birthday: c.birthday, website: c.website,
			}),
		});
		if (saved) {
			this.toast("Notiz gespeichert");
			this.showContact(saved);
		}
	}

	private async editPerson(person: Person | null): Promise<void> {
		const id = person?.id ?? uuidv7();
		const saved = await openForm({
			title: person ? "Ansprechpartner bearbeiten" : "Ansprechpartner hinzufügen", width: "34rem", initial: { ...(person ?? {}) },
			fields: [
				{ key: "name", label: "Name", required: true, maxLength: 200 },
				{ key: "function", label: "Funktion", maxLength: 100, placeholder: "z. B. Bauleitung, Einkauf", suggestions: ["Bauleitung", "Einkauf", "Buchhaltung", "Geschäftsführung", "Hausverwaltung"] },
				{ key: "phone", label: "Telefon", maxLength: 50 },
				{ key: "email", label: "E-Mail", type: "email", maxLength: 320 },
			],
			submit: (v) => put<Person>(`contacts/${this.id}/persons/${id}`, {
				name: String(v.name).trim(), function: v.function || null, phone: v.phone || null, email: v.email || null,
			}),
		});
		if (saved) {
			this.toast("Ansprechpartner gespeichert");
			await this.reloadPersons();
		}
	}

	private async reloadPersons(): Promise<void> {
		const people = await this.run(() => get<Person[]>(`contacts/${this.id}/persons`));
		if (!people) return;
		this.model().setProperty("/persons", people);
		this.model().setProperty("/mainPerson", people[0] ? [people[0].name, people[0].function, people[0].phone].filter(Boolean).join(" · ") : "");
	}

	onAddPerson(): void { void this.editPerson(null); }
	onEditPerson(e: Event): void { void this.editPerson(this.obj<Person>(e)); }

	async onDeletePerson(e: Event): Promise<void> {
		const p = this.obj<Person>(e);
		if (!(await this.confirm(`Ansprechpartner „${p.name}“ entfernen?`, { action: "Entfernen", danger: true }))) return;
		if (await this.run(() => del(`contacts/${this.id}/persons/${p.id}`).then(() => true))) {
			this.toast("Ansprechpartner entfernt");
			await this.reloadPersons();
		}
	}

	async onNewBauakte(): Promise<void> {
		const created = await openBauakteWizard({ status: "planned", customer: this.model().getProperty("/c") as ContactResponse });
		if (created) this.nav(`/bauakten/${created.id}${created.startInspection ? "?erstbegehung=1" : ""}`);
	}

	onDocument(e: Event): void {
		const item = this.obj<DocumentListItem>(e);
		openPreview({
			files: this.files, index: Math.max(0, this.files.findIndex((f) => f.id === item.file.id)), context: null,
			onDetails: (file) => navTo(`/bauakten/${file.bauakteId}/datei/${file.id}?abschnitt=dokumente`),
		});
	}
}
