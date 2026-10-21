import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type ObjectPageLayout from "sap/uxap/ObjectPageLayout";
import type ObjectPageSection from "sap/uxap/ObjectPageSection";
import { get } from "../util/api";
import { approvalStatus, type ApprovalStatus } from "../util/approvals";
import { openBauakteWizard } from "../util/bauakteWizard";
import { openCustomerForm } from "../util/customerForm";
import { fileIcon } from "../util/documents";
import { documentCategories } from "../util/labels";
import type { BauaktePage, CallResponse, ContactAddress, ContactResponse, FileResponse } from "../util/models";
import { openPreview } from "../util/previewDialog";
import { phoneKinds } from "../util/forms";
import type { RouteMatch } from "../util/router";
import { navTo } from "../util/navigation";

interface DocumentListItem { file: FileResponse; bauakteNumber: string | null; bauakteTitle: string; approvalStatus: ApprovalStatus | null }
interface Person { id: string; name: string; function: string | null; phone: string | null; email: string | null }

const sections: Record<string, string> = { uebersicht: "secOverview", bauakten: "secBauakten", dokumente: "secDocuments", kontakte: "secContacts", verlauf: "secHistory" };
const channels: Record<string, string> = { phone: "Telefon", mobile: "Mobil", email: "E-Mail", letter: "Brief" };
const addressText = (a: ContactAddress | null | undefined) =>
	a ? [a.name, a.street, [a.postalCode, a.city].filter(Boolean).join(" ")].filter(Boolean).join(", ") || "–" : "wie Anschrift";

/**
 * Kundenseite: kompakter Kopf mit Kontaktaktionen, Abschnitte Übersicht (Stammdaten mehrspaltig), Bauakten (mit
 * „Neue Bauakte“), Dokumente aller Akten (Bauakte → Dokumentart, Vorschau), Kontakte und Verlauf (Anfragen, Anrufe, Bauakten).
 * @namespace estrichone.controller
 */
export default class Customer extends BaseController {
	private id = "";
	private files: FileResponse[] = [];

	onInit(): void {
		this.local({ c: null, busy: false, headline: "", address: "", salutation: "", channel: "", billing: "", delivery: "",
			bauakten: [], documents: [], reach: [], persons: [], history: [] });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
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
		m.setProperty("/c", c);
		m.setProperty("/headline", [c.kind === "company" ? "Firma" : "Privatperson", c.customerNumber ? `Kd.-Nr. ${c.customerNumber}` : null, c.city].filter(Boolean).join(" · "));
		m.setProperty("/address", [c.street, [c.postalCode, c.city].filter(Boolean).join(" ")].filter(Boolean).join(", ") || "–");
		m.setProperty("/salutation", [c.salutation, c.title].filter(Boolean).join(" ") || "–");
		m.setProperty("/channel", c.preferredChannel ? channels[c.preferredChannel] : "–");
		m.setProperty("/billing", addressText(c.billingAddress));
		m.setProperty("/delivery", addressText(c.deliveryAddress));
		const kindText = Object.fromEntries(phoneKinds.map((k) => [k.key, k.text]));
		m.setProperty("/reach", [
			...(c.phones ?? []).map((p) => ({ value: p.number, label: [kindText[p.kind] ?? p.kind, p.label].filter(Boolean).join(" · "),
				icon: p.kind === "mobile" ? "sap-icon://iphone" : p.kind === "fax" ? "sap-icon://print" : "sap-icon://call", href: `tel:${p.number}` })),
			...(c.emails ?? []).map((e) => ({ value: e.address, label: e.label ?? "E-Mail", icon: "sap-icon://email", href: `mailto:${e.address}` })),
		]);

		const [akten, docs, persons, calls] = await Promise.allSettled([
			this.has("bauakten.read") ? get<BauaktePage>("bauakten", { customerId: this.id, limit: 100 }) : Promise.resolve({ items: [], nextCursor: null }),
			this.has("bauakten.read") ? get<{ items: DocumentListItem[] }>("documents", { customerId: this.id, sort: "bauakte", limit: 500 }) : Promise.resolve({ items: [] }),
			get<Person[]>(`contacts/${this.id}/persons`),
			get<CallResponse[]>("calls", { customerId: this.id, limit: 100 }),
		]);
		const value = <T>(r: PromiseSettledResult<T>, fallback: T) => (r.status === "fulfilled" ? r.value : fallback);
		const bauakten = value(akten, { items: [], nextCursor: null } as BauaktePage).items;
		m.setProperty("/bauakten", bauakten.map((b) => ({ ...b, place: [b.street, b.city].filter(Boolean).join(", ") || "–" })));
		const items = value(docs, { items: [] as DocumentListItem[] }).items;
		this.files = items.map((i) => i.file);
		m.setProperty("/documents", items.map((i) => ({
			...i, icon: fileIcon(i.file.contentType, i.file.originalName),
			group: `${i.bauakteNumber ? `${i.bauakteNumber} · ` : ""}${i.bauakteTitle} › ${i.file.category ? documentCategories[i.file.category]?.text ?? i.file.category : "Ohne Dokumentart"}`,
			approvalText: i.approvalStatus ? approvalStatus[i.approvalStatus].text : "",
		})));
		m.setProperty("/persons", value(persons, []).map((p) => ({ ...p, detail: [p.function, p.phone, p.email].filter(Boolean).join(" · ") })));
		const history = [
			...value(calls, []).map((x) => ({
				at: x.calledAt, icon: x.kind === "inquiry" ? "sap-icon://inbox" : "sap-icon://call",
				title: `${x.kind === "inquiry" ? "Anfrage" : "Anruf"}${x.subject ? `: ${x.subject}` : ""}`,
				text: [x.callerName, x.notes].filter(Boolean).join(" – "), to: x.kind === "inquiry" ? "/anfragen" : "/anrufe",
			})),
			...bauakten.map((b) => ({ at: b.updatedAt, icon: "sap-icon://folder-full", title: `Bauakte ${b.number ?? ""} ${b.title}`.replace(/\s+/g, " "),
				text: "zuletzt geändert", to: `/bauakten/${b.id}` })),
		].sort((a, b) => b.at.localeCompare(a.at));
		m.setProperty("/history", history);
		m.setProperty("/busy", false);
	}

	private obj<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	onCall(): void { window.location.href = `tel:${(this.model().getProperty("/c") as ContactResponse).phone}`; }
	onMail(): void { window.location.href = `mailto:${(this.model().getProperty("/c") as ContactResponse).email}`; }
	onReach(e: Event): void { window.location.href = this.obj<{ href: string }>(e).href; }
	onBauakte(e: Event): void { this.nav(`/bauakten/${this.obj<{ id: string }>(e).id}`); }
	onHistory(e: Event): void { const to = this.obj<{ to?: string }>(e).to; if (to) this.nav(to); }

	async onEdit(): Promise<void> {
		if (await openCustomerForm(this.model().getProperty("/c") as ContactResponse, !this.has("contacts.edit"))) {
			this.toast("Kunde gespeichert");
			await this.load();
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
