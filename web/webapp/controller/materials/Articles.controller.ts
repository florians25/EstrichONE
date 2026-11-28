import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Table from "sap/m/Table";
import ViewSettingsDialog from "sap/m/ViewSettingsDialog";
import ViewSettingsItem from "sap/m/ViewSettingsItem";
import { get, post } from "../../util/api";
import { dateTime, money } from "../../util/format";
import { articleForm, createInLexware, massPriceChange, resolveDeviations, saveWith } from "../../util/articleDialogs";
import { openExternal } from "../../util/lexwareDialogs";
import {
	catalogSyncText, filterArticles, grossOf, kindText, sortArticles, syncResultText, type ArticleFilter, type ArticleReconciliation, type ArticleSortKey,
	type ArticleSyncResult, type CatalogItem, type CatalogList,
} from "../../util/quotations";

/**
 * Material › Artikel und Leistungen als SAP List Report: Filterleiste (Suche, Art, Einheit, Abgleichstand, aktiv),
 * sortierbare Tabelle mit Preis netto und brutto, Mehrfachauswahl für „Preise ändern“ und „Aktiv/Inaktiv setzen“, Zeile
 * öffnet die Objektseite. Abweichungen zu Lexware Office lassen sich direkt aus der Zeile entscheiden.
 * @namespace estrichone.controller.materials
 */
export default class Articles extends BaseController {
	private data: CatalogList | null = null;
	private sort: { key: ArticleSortKey; descending: boolean } = { key: "title", descending: false };

	onInit(): void {
		this.local({
			shown: [], onlyLexware: [], busy: false, syncEnabled: false, canPush: false, standText: "", standType: "None", selected: 0, selectedActive: true, units: [],
			f: { query: "", kind: "", unit: "", sync: "", active: "active" } as ArticleFilter,
			filters: [
				{ key: "", text: "Alle" }, { key: "deviations", text: "Mit Abweichungen" }, { key: "local", text: "Nur in EstrichONE" },
				{ key: "synced", text: "Abgeglichen" }, { key: "missing", text: "In Lexware Office nicht vorhanden" }, { key: "error", text: "Fehler beim Abgleich" },
			],
		});
	}

	onRouteMatched(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			this.data = await get<CatalogList>("lexware/articles", { includeInactive: true });
			m.setProperty("/syncEnabled", this.data.syncEnabled);
			m.setProperty("/canPush", this.data.canPush);
			m.setProperty("/units", [{ key: "", text: "Alle" }, ...[...new Set(this.data.items.map((i) => i.unit).filter(Boolean))].sort().map((u) => ({ key: u, text: u }))]);
			const direction = this.data.direction === "both" ? "beide Richtungen" : this.data.direction === "off" ? "aus" : "aus Lexware Office nach EstrichONE";
			m.setProperty("/standType", this.data.lastSyncError ? "Warning" : "Information");
			m.setProperty("/standText", !this.data.syncEnabled ? "" : this.data.lastSyncError
				? `Der letzte Artikelabgleich ist fehlgeschlagen: ${this.data.lastSyncError}`
				: `Abgleich mit Lexware Office: ${direction}. ${this.data.lastSyncAt ? `Zuletzt abgeglichen ${dateTime(this.data.lastSyncAt)}.` : "Noch nicht abgeglichen."} Der Abgleich läuft regelmäßig im Hintergrund.`);
			this.refresh();
			if (this.data.syncEnabled && this.has("materials.manage")) await this.loadReconciliation();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private async loadReconciliation(): Promise<void> {
		try {
			const rec = await get<ArticleReconciliation>("lexware/articles/reconciliation");
			this.model().setProperty("/onlyLexware", rec.onlyLexware.filter((r) => r.candidates.length > 0).map((r) => ({
				...r, remoteText: [r.article.articleNumber, r.article.unitName ? `je ${r.article.unitName}` : ""].filter(Boolean).join(", "),
				candidateId: r.candidates[0]?.item.id ?? null, candidateTitle: r.candidates.map((c) => c.item.title).join(", "),
				reasonText: [...new Set(r.candidates.flatMap((c) => c.reasons))].join(", "),
			})));
		} catch {
			this.model().setProperty("/onlyLexware", []);
		}
	}

	refresh(): void {
		const m = this.model();
		const items = sortArticles(filterArticles(this.data?.items ?? [], m.getProperty("/f") as ArticleFilter), this.sort.key, this.sort.descending);
		m.setProperty("/shown", items.map((i) => {
			const s = catalogSyncText(i.sync);
			return { ...i, kindText: kindText(i.kind), taxText: `${i.taxRate.toLocaleString("de-DE")} %`, grossText: money(grossOf(i.netPrice, i.taxRate)),
				syncText: s.text, syncState: s.state, syncIcon: s.icon };
		}));
		(this.byId("articleTable") as Table | undefined)?.removeSelections(true);
		m.setProperty("/selected", 0);
	}

	private item(e: Event): CatalogItem {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as CatalogItem;
	}

	private selectedItems(): CatalogItem[] {
		return (this.byId("articleTable") as Table).getSelectedItems().map((i) => i.getBindingContext("view")?.getObject() as CatalogItem);
	}

	onSelection(): void {
		const items = this.selectedItems();
		this.model().setProperty("/selected", items.length);
		this.model().setProperty("/selectedActive", items.every((i) => i.isActive));
	}

	onSort(): void {
		const columns: [ArticleSortKey, string][] = [["articleNumber", "Artikelnummer"], ["title", "Bezeichnung"], ["kind", "Art"], ["unit", "Einheit"],
			["netPrice", "Preis netto"], ["taxRate", "USt"], ["grossPrice", "Preis brutto"], ["sync", "Abgleich mit Lexware Office"]];
		const dialog = new ViewSettingsDialog({
			title: "Sortieren", sortDescending: this.sort.descending,
			sortItems: columns.map(([key, text]) => new ViewSettingsItem({ key, text, selected: key === this.sort.key })),
			confirm: (e: Event) => {
				const item = e.getParameter("sortItem" as never) as ViewSettingsItem | null;
				this.sort = { key: (item?.getKey() ?? "title") as ArticleSortKey, descending: !!e.getParameter("sortDescending" as never) };
				this.refresh();
				dialog.destroy();
			},
			cancel: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	async onPrices(): Promise<void> {
		const count = await massPriceChange(this.v(), this.selectedItems());
		if (count) { this.toast(`${count} ${count === 1 ? "Preis" : "Preise"} geändert`); await this.load(); }
	}

	async onToggleSelected(): Promise<void> {
		const items = this.selectedItems();
		const active = !items.every((i) => i.isActive);
		if (!active && !await this.confirm(`${items.length} ${items.length === 1 ? "Eintrag" : "Einträge"} inaktiv setzen? Inaktive Einträge bleiben erhalten, werden in Belegen aber nicht mehr vorgeschlagen.`,
			{ action: "Inaktiv setzen" })) return;
		if (await this.run(async () => { for (const i of items) await saveWith(i, { isActive: active }); return true; }, active ? "Aktiv gesetzt" : "Inaktiv gesetzt")) await this.load();
	}

	onOpenItem(e: Event): void {
		this.nav(`/artikel/${this.item(e).id}`);
	}

	async onNew(): Promise<void> {
		const id = await articleForm(null, this.data?.materials ?? []);
		if (id) { this.toast("Eintrag gespeichert"); await this.load(); }
	}

	onOpen(e: Event): void { openExternal(this.item(e).lexwareUrl); }

	async onSync(): Promise<void> {
		const result = await this.run(() => post<ArticleSyncResult>("lexware/articles/sync"));
		if (result) { this.toast(syncResultText(result)); await this.load(); }
	}

	async onDeviations(e: Event): Promise<void> {
		if (await resolveDeviations(this.item(e), this.data?.canPush === true)) { this.toast("Abweichungen übernommen"); await this.load(); }
	}

	async onCreate(e: Event): Promise<void> {
		const item = this.item(e);
		try {
			if (!await createInLexware(item, (text) => this.confirm(text, { action: "Trotzdem anlegen" }))) return;
		} catch (error) {
			this.showError(error);
			return;
		}
		this.toast("In Lexware Office angelegt");
		await this.load();
	}

	private remote(e: Event): { article: { id: string; title: string }; candidateId: string | null } {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as { article: { id: string; title: string }; candidateId: string | null };
	}

	async onLinkCandidate(e: Event): Promise<void> {
		const row = this.remote(e);
		if (!row.candidateId) return;
		if (await this.run(() => post(`lexware/articles/${row.candidateId}/link`, { lexwareId: row.article.id }), "Verknüpft. Unterschiede erscheinen als Abweichungen.")) await this.load();
	}

	async onImportAnyway(e: Event): Promise<void> {
		const row = this.remote(e);
		if (!await this.confirm(`„${row.article.title}“ als neuen Eintrag übernehmen, obwohl es einen ähnlichen Eintrag gibt?`, { action: "Übernehmen" })) return;
		if (await this.run(() => post("lexware/articles/import", { lexwareId: row.article.id, createAnyway: true }), "Übernommen")) await this.load();
	}
}
