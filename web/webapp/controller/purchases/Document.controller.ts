import BaseController from "../BaseController";
import { apiUrl, del, get, post, put } from "../../util/api";
import { dateTime, money } from "../../util/format";
import type { RouteMatch } from "../../util/router";
import { documentState, headerFacts, lineRows, totalsText, type PurchaseDocument } from "../../util/purchases";

/**
 * Hochgeladener Eingangsbeleg als Objektseite: Prüfung der Pflichtangaben, Kopfdaten und Positionen der E-Rechnung,
 * Zuordnung zur Bauakte, Übergabe an Lexware Office (genau einmal) und Verwerfen vor der Übergabe.
 * @namespace estrichone.controller.purchases
 */
export default class Document extends BaseController {
	private id = "";

	onInit(): void {
		this.local({ busy: false, d: null, title: "", kindText: "", stateView: {}, bauakteText: "", grossText: "", uploadedText: "", sentText: "", isEInvoice: false,
			facts: [], lines: [], totalsText: "", bauakten: [], bauakteId: "", note: "" });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		this.id = match.params.id;
		await this.load();
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const [doc, page] = await Promise.all([
				get<PurchaseDocument>(`purchases/documents/${this.id}`),
				get<{ items: { id: string; number: string | null; title: string }[] }>("bauakten", { limit: 200 }).catch(() => ({ items: [] })),
			]);
			this.show(doc, page.items);
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private show(doc: PurchaseDocument, bauakten?: { id: string; number: string | null; title: string }[]): void {
		const m = this.model();
		const d = doc.document;
		const data = doc.data;
		const isEInvoice = data.format !== "none";
		m.setProperty("/d", doc);
		m.setProperty("/title", isEInvoice
			? `${data.creditNote ? "Gutschrift" : "Rechnung"} ${data.number ?? "ohne Nummer"}${data.sellerName ? ` von ${data.sellerName}` : ""}`
			: d.fileName);
		m.setProperty("/kindText", d.formatName);
		m.setProperty("/stateView", documentState(d));
		m.setProperty("/bauakteText", [d.bauakteNumber, d.bauakteTitle].filter(Boolean).join(" "));
		m.setProperty("/grossText", d.grossAmount === null ? "aus der Datei nicht lesbar" : money(d.grossAmount));
		m.setProperty("/uploadedText", `${dateTime(d.uploadedAt)}${d.uploadedBy ? `, ${d.uploadedBy}` : ""}`);
		m.setProperty("/sentText", d.sentAt
			? `Am ${dateTime(d.sentAt)} an Lexware Office übergeben. Dort finden Sie den Beleg unter „Belege“, „zu prüfen“; nach dem Buchen erscheint er hier im Reiter „Aus Lexware Office“ und lässt sich der Bauakte zuordnen.`
			: "");
		m.setProperty("/isEInvoice", isEInvoice);
		m.setProperty("/facts", headerFacts(data));
		m.setProperty("/lines", lineRows(data));
		m.setProperty("/totalsText", totalsText(data));
		m.setProperty("/bauakteId", d.bauakteId ?? "");
		m.setProperty("/note", doc.note ?? "");
		if (bauakten) {
			const items = bauakten.map((b) => ({ key: b.id, text: [b.number, b.title].filter(Boolean).join(" ") }));
			if (d.bauakteId && !items.some((i) => i.key === d.bauakteId)) items.unshift({ key: d.bauakteId, text: m.getProperty("/bauakteText") as string });
			m.setProperty("/bauakten", items);
		}
	}

	async onSave(): Promise<void> {
		const m = this.model();
		const doc = await this.run(() => put<PurchaseDocument>(`purchases/documents/${this.id}`, {
			bauakteId: (m.getProperty("/bauakteId") as string) || null, note: m.getProperty("/note") as string,
		}), "Gespeichert.");
		if (doc) this.show(doc);
	}

	async onSend(): Promise<void> {
		const m = this.model();
		if ((m.getProperty("/bauakteId") as string) !== ((m.getProperty("/d/document/bauakteId") as string | null) ?? "")
			|| (m.getProperty("/note") as string) !== ((m.getProperty("/d/note") as string | null) ?? "")) {
			await this.onSave();
		}
		const problems = (m.getProperty("/d/data/problems") as string[]).length;
		if (!await this.confirm(`${problems ? "Bei diesem Beleg fehlen Pflichtangaben. " : ""}Den Beleg jetzt an Lexware Office übergeben? Er erscheint dort unter „Belege“, „zu prüfen“. Ein übergebener Beleg wird nicht ein zweites Mal gesendet.`,
			{ title: "An Lexware Office übergeben", action: "Übergeben" })) return;
		m.setProperty("/busy", true);
		try {
			this.show(await post<PurchaseDocument>(`purchases/documents/${this.id}/send`, {}));
			this.toast("An Lexware Office übergeben.");
		} catch (error) {
			this.showError(error, "An Lexware Office übergeben");
			await this.load();
		} finally {
			m.setProperty("/busy", false);
		}
	}

	async onDiscard(): Promise<void> {
		if (!await this.confirm("Den hochgeladenen Beleg verwerfen? Er wird in EstrichONE gelöscht; in Lexware Office ist er noch nicht vorhanden.",
			{ title: "Beleg verwerfen", action: "Verwerfen", danger: true })) return;
		const done = await this.run(async () => { await del(`purchases/documents/${this.id}`); return true; }, "Beleg verworfen.");
		if (done) this.nav("/eingangsbelege?reiter=eingang");
	}

	onFile(): void {
		window.open(apiUrl(`purchases/documents/${this.id}/file`), "_blank", "noopener");
	}

	onBack(): void {
		this.nav("/eingangsbelege?reiter=eingang");
	}
}
