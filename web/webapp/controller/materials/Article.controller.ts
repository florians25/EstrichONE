import BaseController from "../BaseController";
import { get } from "../../util/api";
import { dateTime, money } from "../../util/format";
import { articleForm, createInLexware, resolveDeviations, saveWith } from "../../util/articleDialogs";
import { openExternal } from "../../util/lexwareDialogs";
import type { RouteMatch } from "../../util/router";
import { catalogSyncText, grossOf, kindText, type CatalogItem, type CatalogList } from "../../util/quotations";

/**
 * Objektseite eines Eintrags aus „Artikel und Leistungen“: Allgemein, Preis (netto, Umsatzsteuer, brutto), Abgleich mit
 * Lexware Office mit Abweichungen. Aktionen: Bearbeiten, Duplizieren, Aktiv bzw. Inaktiv setzen (statt Löschen).
 * @namespace estrichone.controller.materials
 */
export default class Article extends BaseController {
	private id = "";
	private data: CatalogList | null = null;

	onInit(): void {
		this.local({ a: null, busy: false, canManage: false, canPush: false, subtitle: "", kindText: "", netText: "", grossText: "", taxText: "", sync: {}, syncDetail: "" });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		this.id = match.params.id;
		await this.load();
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			this.data = await get<CatalogList>("lexware/articles", { includeInactive: true });
			const a = this.data.items.find((i) => i.id === this.id);
			if (!a) { this.toast("Diesen Eintrag gibt es nicht mehr."); this.nav("/artikel", true); return; }
			m.setProperty("/a", a);
			m.setProperty("/canManage", this.has("materials.manage"));
			m.setProperty("/canPush", this.data.canPush);
			m.setProperty("/kindText", kindText(a.kind));
			m.setProperty("/subtitle", [kindText(a.kind), a.articleNumber, `je ${a.unit ?? "Stück"}`, a.isActive ? "" : "inaktiv"].filter(Boolean).join(", "));
			m.setProperty("/netText", money(a.netPrice));
			m.setProperty("/grossText", money(grossOf(a.netPrice, a.taxRate)));
			m.setProperty("/taxText", `${a.taxRate.toLocaleString("de-DE")} %`);
			m.setProperty("/sync", catalogSyncText(a.sync));
			m.setProperty("/syncDetail", a.sync.error ? `Fehler beim letzten Abgleich: ${a.sync.error}`
				: a.sync.syncedAt ? `Zuletzt abgeglichen ${dateTime(a.sync.syncedAt)}.` : a.lexwareId ? "" : "Nur in EstrichONE, nicht mit Lexware Office verknüpft.");
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private item(): CatalogItem {
		return this.model().getProperty("/a") as CatalogItem;
	}

	async onEdit(): Promise<void> {
		if (await articleForm(this.item(), this.data?.materials ?? [], "edit")) { this.toast("Eintrag gespeichert"); await this.load(); }
	}

	async onDuplicate(): Promise<void> {
		const id = await articleForm(this.item(), this.data?.materials ?? [], "duplicate");
		if (id) { this.toast("Kopie angelegt"); this.nav(`/artikel/${id}`); }
	}

	async onToggle(): Promise<void> {
		const a = this.item();
		if (a.isActive && !await this.confirm("Eintrag inaktiv setzen? Er bleibt erhalten, wird in Belegen aber nicht mehr vorgeschlagen.", { action: "Inaktiv setzen" })) return;
		if (await this.run(() => saveWith(a, { isActive: !a.isActive }), a.isActive ? "Inaktiv gesetzt" : "Aktiv gesetzt")) await this.load();
	}

	async onDeviations(): Promise<void> {
		if (await resolveDeviations(this.item(), this.data?.canPush === true)) { this.toast("Abweichungen übernommen"); await this.load(); }
	}

	async onCreate(): Promise<void> {
		try {
			if (!await createInLexware(this.item(), (text) => this.confirm(text, { action: "Trotzdem anlegen" }))) return;
		} catch (error) {
			this.showError(error);
			return;
		}
		this.toast("In Lexware Office angelegt");
		await this.load();
	}

	onOpenLexware(): void { openExternal(this.item().lexwareUrl); }
	onBack(): void { this.nav("/artikel"); }
}
