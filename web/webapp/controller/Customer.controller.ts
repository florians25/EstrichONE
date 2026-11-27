import { startQuotation } from "../util/quotationStart";
import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type ObjectPageLayout from "sap/uxap/ObjectPageLayout";
import type ObjectPageSection from "sap/uxap/ObjectPageSection";
import { del, get, post, put } from "../util/api";
import { approvalStatus, type ApprovalStatus } from "../util/approvals";
import { openBauakteWizard } from "../util/bauakteWizard";
import { openCustomerForm } from "../util/customerForm";
import { filterByStatus, latest, routeUrl, type KeyFigures } from "../util/customerMatch";
import { fileIcon } from "../util/documents";
import { area, date, dateTime } from "../util/format";
import { documentCategories } from "../util/labels";
import type { BauaktePage, BauakteListItem, CallResponse, ContactAddress, ContactResponse, FileResponse } from "../util/models";
import { openPreview } from "../util/previewDialog";
import { openForm, phoneKinds } from "../util/forms";
import { uuidv7 } from "../util/ids";
import type { RouteMatch } from "../util/router";
import { navTo } from "../util/navigation";
import { openCustomerRemoval, reactivateCustomer } from "../util/customerRemovalDialog";
import { customerState } from "../util/customerRemoval";
import { session } from "../util/session";
import {
	changeSyncState, creationStateText, deviationValue, euro, lexwareVisible, linkText, pdfAvailable, PDF_DRAFT_HINT, problemText, syncStateText, voucherNumber, voucherPdfPath, voucherStatus,
	voucherSummary, voucherTitle, voucherTypeText, type CustomerLexware, type CustomerVouchers, type DeviationSource, type LexwareDeviationField, type LexwareVoucherItem,
} from "../util/lexware";
import {
	downloadVoucherPdf, openCustomerLinkDialog, openDeviationDialog, openExternal, openVoucherPdf, resolveAllDeviations, type VoucherPdf,
} from "../util/lexwareDialogs";

interface DocumentListItem { file: FileResponse; bauakteNumber: string | null; bauakteTitle: string; approvalStatus: ApprovalStatus | null }
interface Person { id: string; name: string; function: string | null; phone: string | null; email: string | null }
type AkteRow = BauakteListItem & { place: string };

const sections: Record<string, string> = { uebersicht: "secOverview", bauakten: "secBauakten", dokumente: "secDocuments", kontakte: "secContacts", verlauf: "secHistory",
	lexware: "secLexware" };
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
	async onCreateQuotation(): Promise<void> {
		const id = await startQuotation({ contactId: this.id }, () => (this.byId("secLexware") as Control | undefined)?.getDomRef()?.scrollIntoView());
		if (id) this.nav(`/angebote/${id}`);
	}

	private id = "";
	private files: FileResponse[] = [];

	onInit(): void {
		this.local({ c: null, busy: false, stateText: "", stateHint: "", headline: "", address: "", salutation: "", channel: "", billing: "", delivery: "",
			primaryPhone: "", primaryEmail: "", routeUrl: null, mainPerson: "", kpis: [],
			bauakten: [], shownBauakten: [], bauakteFilter: "", bauakteSearch: "", bauakteEmpty: "", counts: { all: 0, active: 0, inquiry: 0, old: 0 },
			documents: [], reach: [], persons: [], history: [], lexware: { loaded: false, busy: false, link: null, canEdit: false, text: "", syncEnabled: false, syncText: "", syncState: "None", syncHint: "",
				vouchers: [], vouchersBusy: false, vouchersSummary: "", vouchersError: "", change: { text: "", state: "None", icon: "", hint: "", canRun: false }, deviations: [], creation: { text: "", state: "None", hint: "", retry: false } } });
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
		document.title = `${c.name} (EstrichONE)`;
		this.showContact(c);
		void this.loadLexware();

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
			group: `${i.bauakteNumber ? `${i.bauakteNumber}, ` : ""}${i.bauakteTitle} › ${i.file.category ? documentCategories[i.file.category]?.text ?? i.file.category : "Ohne Dokumentart"}`,
			approvalText: i.approvalStatus ? approvalStatus[i.approvalStatus].text : "",
		})));
		const people = value(persons, []);
		m.setProperty("/persons", people);
		m.setProperty("/mainPerson", people[0] ? [people[0].name, people[0].function, people[0].phone].filter(Boolean).join(", ") : "");

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
		m.setProperty("/stateText", customerState(c).text);
		m.setProperty("/stateHint", c.anonymizedAt
			? `Dieser Kunde wurde am ${date(c.anonymizedAt)} anonymisiert (DSGVO). Bauakten und Unterlagen bleiben erhalten.`
			: c.archivedAt ? `Archiviert seit ${date(c.archivedAt)}: Der Kunde erscheint nicht in Auswahllisten und Suche. Reaktivieren macht ihn wieder aktiv.` : "");
		m.setProperty("/headline", [c.customerNumber ? `Kd.-Nr. ${c.customerNumber}` : "ohne Kundennummer", [c.postalCode, c.city].filter(Boolean).join(" ")].filter(Boolean).join(", "));
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
			...(c.phones ?? []).map((p) => ({ value: p.number, label: [kindText[p.kind] ?? p.kind, p.label].filter(Boolean).join(", "),
				icon: p.kind === "mobile" ? "sap-icon://iphone" : p.kind === "fax" ? "sap-icon://print" : "sap-icon://call", href: `tel:${p.number.replace(/\s/g, "")}` })),
			...(c.emails ?? []).map((e) => ({ value: e.address, label: e.label ?? "E-Mail", icon: "sap-icon://email", href: `mailto:${e.address}` })),
		]);
	}

	private showKpis(f: KeyFigures | null, lastHistory: string | null): void {
		const kpis: { label: string; value: string; state: string; hint: string }[] = [];
		if (f) {
			kpis.push({ label: "Bauakten laufend", value: `${f.bauaktenActive} von ${f.bauaktenTotal}`, state: "None",
				hint: [f.inquiries ? plural(f.inquiries, "Anfrage", "Anfragen") : null, f.completed ? `${f.completed} abgeschlossen` : null].filter(Boolean).join(", ") });
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

	/** Löschen (ohne verknüpfte Daten), sonst Archivieren oder Anonymisieren. */
	async onRemove(): Promise<void> {
		const c = this.model().getProperty("/c") as ContactResponse;
		const result = await openCustomerRemoval(c).catch((e: unknown) => { this.showError(e); return null; });
		if (!result) return;
		if (result.action === "deleted") {
			this.toast(`${c.name} gelöscht`);
			this.nav("/kunden", true);
			return;
		}
		this.toast({ archived: "Kunde archiviert", reactivated: "Kunde wieder aktiv", anonymized: "Kunde anonymisiert" }[result.action]);
		if (result.contact) this.showContact(result.contact);
	}

	async onReactivate(): Promise<void> {
		const saved = await this.run(() => reactivateCustomer(this.id), "Kunde wieder aktiv");
		if (saved) this.showContact(saved);
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
		this.model().setProperty("/mainPerson", people[0] ? [people[0].name, people[0].function, people[0].phone].filter(Boolean).join(", ") : "");
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

	// ---------- Lexware Office ----------

	/** Verknüpfung mit Lexware Office (nur mit eingeschaltetem Modul und Leserecht; ohne Aufruf bei Lexware). */
	private async loadLexware(): Promise<void> {
		const m = this.model();
		if (!lexwareVisible(session.data.modules ?? {}, session.data.permissions ?? {})) {
			m.setProperty("/lexware", { loaded: false, busy: false, link: null, canEdit: false, text: "", syncEnabled: false, syncText: "", syncState: "None", syncHint: "",
				vouchers: [], vouchersBusy: false, vouchersSummary: "", vouchersError: "", change: { text: "", state: "None", icon: "", hint: "", canRun: false }, deviations: [], creation: { text: "", state: "None", hint: "", retry: false } });
			return;
		}
		m.setProperty("/lexware/busy", true);
		this.applyLexware(await get<CustomerLexware>(`lexware/customers/${this.id}`).catch(() => null));
	}

	private applyLexware(state: CustomerLexware | null): void {
		const link = state?.link ?? null;
		const sync = syncStateText(state?.syncEnabled === true, link);
		const m = this.model();
		const hadLink = !!m.getProperty("/lexware/link");
		m.setProperty("/lexware", { ...(m.getProperty("/lexware") as object), loaded: true, busy: false, link, canEdit: state?.canEdit === true, text: linkText(link),
			syncEnabled: state?.syncEnabled === true, syncText: sync.text, syncState: sync.state, syncHint: sync.hint,
			creation: creationStateText(link ? null : state?.creation?.state, state?.creation?.error),
			change: { ...changeSyncState(link ? state?.sync : null, dateTime), canRun: !!link && ["synced", "deviations", "pending", "error"].includes(state?.sync?.state ?? "") },
			deviations: (link ? state?.sync?.deviations ?? [] : []).map((d: LexwareDeviationField) => ({
				...d, estrichOneText: deviationValue(d.estrichOne), lexwareText: deviationValue(d.lexware) })) });
		m.setProperty("/c/lexwareSync", state?.syncEnabled === true);
		if (!link) {
			m.setProperty("/lexware/vouchers", []);
			m.setProperty("/lexware/vouchersSummary", "");
		} else if (!hadLink || this.vouchersFor !== this.id) {
			void this.loadLexwareVouchers(false);
		}
	}

	private vouchersFor = "";

	/** Rechnungen und Angebote des verknüpften Lexware-Kontakts (nur Anzeige; kurzer Cache auf dem Server). */
	private async loadLexwareVouchers(refresh: boolean): Promise<void> {
		const m = this.model();
		const id = this.id;
		this.vouchersFor = id;
		m.setProperty("/lexware/vouchersBusy", true);
		m.setProperty("/lexware/vouchersError", "");
		try {
			const result = await get<CustomerVouchers>(`lexware/customers/${id}/vouchers`, refresh ? { refresh: true } : undefined);
			if (id !== this.id) return;
			m.setProperty("/lexware/vouchers", result.vouchers.map((v: LexwareVoucherItem) => {
				const status = voucherStatus(v.status);
				return { ...v, typeText: voucherTypeText(v.type), numberText: voucherNumber(v), dateText: date(v.voucherDate), dueText: date(v.dueDate),
					amountText: euro(v.grossAmount, v.currency), statusText: status.text, statusState: status.state, statusIcon: status.icon,
					pdf: pdfAvailable(v), pdfHint: pdfAvailable(v) ? "Beleg als PDF anzeigen" : PDF_DRAFT_HINT };
			}));
			m.setProperty("/lexware/vouchersSummary", voucherSummary(result.vouchers));
		} catch (error) {
			m.setProperty("/lexware/vouchers", []);
			m.setProperty("/lexware/vouchersSummary", "");
			m.setProperty("/lexware/vouchersError", problemText(error));
		} finally {
			m.setProperty("/lexware/vouchersBusy", false);
		}
	}

	onLexwareVouchersRefresh(): void {
		void this.loadLexwareVouchers(true);
	}

	onLexwareVoucherOpen(e: Event): void {
		openExternal(this.obj<LexwareVoucherItem>(e).url);
	}

	private voucherPdf(e: Event): VoucherPdf {
		const v = this.obj<LexwareVoucherItem>(e);
		return { path: voucherPdfPath({ customerId: this.id, lexwareId: v.lexwareId }), title: voucherTitle(v), lexwareUrl: v.url };
	}

	onLexwareVoucherPdf(e: Event): void {
		void openVoucherPdf(this.voucherPdf(e));
	}

	onLexwareVoucherDownload(e: Event): void {
		void downloadVoucherPdf(this.voucherPdf(e));
	}

	/** Änderungsabgleich für diesen Kunden sofort ausführen. */
	async onLexwareSyncNow(): Promise<void> {
		const state = await this.run(() => post<CustomerLexware>(`lexware/customers/${this.id}/sync/run`));
		if (!state) return;
		await this.afterLexwareSync(state);
		this.toast(state.sync?.state === "deviations" ? "Abgeglichen, es gibt Abweichungen" : "Mit Lexware Office abgeglichen");
	}

	/** Nach einem Abgleich können sich Kundendaten geändert haben: Kunde neu laden, Stand anzeigen. */
	private async afterLexwareSync(state: CustomerLexware): Promise<void> {
		const c = await get<ContactResponse>(`contacts/${this.id}`).catch(() => null);
		if (c) this.showContact(c);
		this.applyLexware(state);
	}

	async onLexwareResolve(): Promise<void> {
		const c = this.model().getProperty("/c") as ContactResponse;
		const state = await openDeviationDialog({ id: c.id, name: c.name }, this.model().getProperty("/lexware/deviations") as LexwareDeviationField[]);
		if (state) await this.afterLexwareSync(state);
	}

	private async takeAll(source: DeviationSource): Promise<void> {
		const c = this.model().getProperty("/c") as ContactResponse;
		const state = await resolveAllDeviations({ id: c.id, name: c.name }, source);
		if (state) await this.afterLexwareSync(state);
	}

	onLexwareTakeLocal(): void {
		void this.takeAll("estrichOne");
	}

	onLexwareTakeRemote(): void {
		void this.takeAll("lexware");
	}

	/** Automatische Anlage in Lexware Office erneut versuchen (der Server prüft vorher erneut auf Dubletten). */
	async onLexwareCreateRetry(): Promise<void> {
		const state = await this.run(() => post<CustomerLexware>(`lexware/customers/${this.id}/create/retry`));
		if (!state) return;
		this.applyLexware(state);
		if (state.link) this.toast("In Lexware Office angelegt");
	}

	onLexwareVoucherBauakte(e: Event): void {
		this.nav(`/bauakten/${this.obj<LexwareVoucherItem>(e).assignedBauakteId}?abschnitt=rechnungen`);
	}

	/** „Mit Lexware Office abgleichen“ umschalten; eine bestehende Verknüpfung bleibt, bis sie gelöst wird. */
	async onLexwareSync(e: Event): Promise<void> {
		const enabled = e.getParameter("state" as never) as boolean;
		const state = await this.run(() => put<CustomerLexware>(`lexware/customers/${this.id}/sync`, { enabled }),
			enabled ? "Abgleich eingeschaltet" : "Abgleich ausgeschaltet");
		if (state) this.applyLexware(state);
		else await this.loadLexware();
	}

	async onLexwareLink(): Promise<void> {
		const c = this.model().getProperty("/c") as ContactResponse;
		if (await openCustomerLinkDialog({ id: c.id, name: c.name })) await this.loadLexware();
	}

	onLexwareOpen(): void {
		openExternal(this.model().getProperty("/lexware/link/url") as string | null);
	}

	async onLexwareUnlink(): Promise<void> {
		if (!await this.confirm("Verknüpfung mit Lexware Office lösen? Der Kontakt und alle Rechnungen bleiben in Lexware Office erhalten. "
			+ "Neue Rechnungen lassen sich für diesen Kunden erst nach erneuter Verknüpfung erstellen.", { action: "Lösen", danger: true })) return;
		if (await this.run(() => del(`lexware/customers/${this.id}/link`).then(() => true), "Verknüpfung gelöst")) await this.loadLexware();
	}
}
