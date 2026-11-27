import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { ApiError, get, post, put } from "../../util/api";
import { dateTime } from "../../util/format";
import { KEEP_OPEN, openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import { deviationValue } from "../../util/lexware";
import { openExternal } from "../../util/lexwareDialogs";
import {
	catalogSyncText, kindText, syncResultText, type ArticleReconciliation, type ArticleSyncResult, type CatalogItem, type CatalogList,
} from "../../util/quotations";

const units = ["m²", "lfm", "Stück", "Sack", "t", "m³", "pauschal", "Std."];

/**
 * Material › Artikel und Leistungen: Verkaufsartikel und Leistungen mit Abgleichstand zu Lexware Office, „Jetzt
 * abgleichen“, Abweichungen und mögliche Dubletten zur Entscheidung. @namespace estrichone.controller.materials
 */
export default class Articles extends BaseController {
	private data: CatalogList | null = null;

	onInit(): void {
		this.local({
			shown: [], onlyLexware: [], hasMaterial: false, search: "", filter: "", inactive: false, busy: false, syncEnabled: false, canPush: false, standText: "", standType: "None",
			filters: [
				{ key: "", text: "Alle" }, { key: "deviations", text: "Mit Abweichungen" }, { key: "local", text: "Nur in EstrichONE" },
				{ key: "synced", text: "Abgeglichen" }, { key: "missing", text: "In Lexware Office nicht vorhanden" },
			],
		});
	}

	onRouteMatched(): void { void this.load(); }
	onInactive(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			this.data = await get<CatalogList>("lexware/articles", { includeInactive: m.getProperty("/inactive") || undefined });
			m.setProperty("/syncEnabled", this.data.syncEnabled);
			m.setProperty("/canPush", this.data.canPush);
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
		const q = String(m.getProperty("/search") ?? "").trim().toLowerCase();
		const filter = m.getProperty("/filter") as string;
		m.setProperty("/hasMaterial", (this.data?.items ?? []).some((i) => !!i.materialId));
		m.setProperty("/shown", (this.data?.items ?? [])
			.filter((i) => (!filter || i.sync.state === filter) && (!q || [i.title, i.articleNumber, i.description].some((v) => v?.toLowerCase().includes(q))))
			.map((i) => {
				const s = catalogSyncText(i.sync);
				return { ...i, kindText: kindText(i.kind), taxText: `${i.taxRate.toLocaleString("de-DE")} %`, syncText: s.text, syncState: s.state, syncIcon: s.icon };
			}));
	}

	private item(e: Event): CatalogItem {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as CatalogItem;
	}

	private async form(item: CatalogItem | null): Promise<void> {
		const materials = this.data?.materials ?? [];
		const saved = await openForm({
			title: item ? "Eintrag bearbeiten" : "Eintrag anlegen", width: "40rem",
			intro: item?.lexwareId ? "Mit Lexware Office verknüpft. Änderungen gehen nur bei der Richtung „Beide Richtungen“ nach Lexware Office; sonst bleiben sie als Abweichung sichtbar." : undefined,
			initial: item ? { ...item, netPrice: item.netPrice, taxRate: String(item.taxRate) } : { kind: "service", unit: "m²", taxRate: "19", isActive: true, netPrice: 0 },
			fields: [
				{ key: "title", label: "Bezeichnung", required: true, maxLength: 255, placeholder: "z. B. Zementestrich CT-C25-F4 liefern und einbauen" },
				{ key: "kind", label: "Art", type: "select", required: true, options: [{ key: "service", text: "Leistung" }, { key: "product", text: "Material" }] },
				{ key: "description", label: "Beschreibung", type: "textarea", rows: 2, maxLength: 2000 },
				{ key: "articleNumber", label: "Artikelnummer", maxLength: 50 },
				{ key: "unit", label: "Einheit", type: "combo", required: true, options: units.map((u) => ({ key: u, text: u })) },
				{ key: "netPrice", label: "Preis je Einheit netto", type: "number", unit: "€", required: true },
				{ key: "taxRate", label: "Steuersatz", type: "select", options: [{ key: "19", text: "19 %" }, { key: "7", text: "7 %" }, { key: "0", text: "0 %" }] },
				{ key: "materialId", label: "Verknüpftes Material (Materialstamm)", type: "select", options: materials.map((x) => ({ key: x.id, text: x.number ? `${x.number} ${x.name}` : x.name })) },
				{ key: "isActive", label: "Aktiv (in Angeboten auswählbar)", type: "switch" },
			],
			submit: async (v) => {
				const id = item?.id ?? uuidv7();
				await put(`lexware/articles/${id}`, {
					id, title: v.title, kind: v.kind, description: v.description || null, articleNumber: v.articleNumber || null, gtin: item?.gtin ?? null, unit: v.unit,
					netPrice: Number(v.netPrice ?? 0), taxRate: Number(v.taxRate ?? 19), materialId: v.materialId || null, isActive: !!v.isActive, version: item?.version ?? null,
				});
				return true;
			},
		});
		if (saved) { this.toast("Eintrag gespeichert"); void this.load(); }
	}

	onNew(): void { void this.form(null); }
	onEdit(e: Event): void { void this.form(this.item(e)); }
	onOpen(e: Event): void { openExternal(this.item(e).lexwareUrl); }

	async onSync(): Promise<void> {
		const result = await this.run(() => post<ArticleSyncResult>("lexware/articles/sync"));
		if (result) { this.toast(syncResultText(result)); await this.load(); }
	}

	async onCreate(e: Event): Promise<void> {
		const item = this.item(e);
		try {
			await post(`lexware/articles/${item.id}/create`, {});
		} catch (error) {
			if (!(error instanceof ApiError) || error.code !== "LEXWARE_ARTICLE_DUPLICATE") { this.showError(error); return; }
			if (!await this.confirm(`${error.message} Trotzdem neu in Lexware Office anlegen?`, { action: "Trotzdem anlegen" })) return;
			if (!await this.run(() => post(`lexware/articles/${item.id}/create`, { createAnyway: true }))) return;
		}
		this.toast("In Lexware Office angelegt");
		await this.load();
	}

	async onDeviations(e: Event): Promise<void> {
		const item = this.item(e);
		const push = this.data?.canPush === true;
		const options = [{ key: "", text: "offen lassen" }, { key: "lexware", text: "Wert aus Lexware Office übernehmen" },
			...(push ? [{ key: "estrichOne", text: "Wert aus EstrichONE nach Lexware Office" }] : [])];
		const saved = await openForm<Record<string, string>>({
			title: `Abweichungen: ${item.title}`, width: "44rem",
			intro: push ? "Beide Seiten haben unterschiedliche Werte. Wählen Sie je Feld, welcher gilt." : "Die Richtung ist „Aus Lexware Office nach EstrichONE“. Sie können den Wert aus Lexware Office übernehmen; eigene Werte gehen nicht nach Lexware Office.",
			initial: {},
			fields: item.sync.deviations.map((d) => ({
				key: d.field, label: d.label, type: "select" as const, options,
				help: `EstrichONE: ${deviationValue(d.estrichOne)}. Lexware Office: ${deviationValue(d.lexware)}.`,
			})),
			submit: async (v) => {
				const fields = Object.fromEntries(Object.entries(v).filter(([, s]) => s === "lexware" || s === "estrichOne"));
				if (!Object.keys(fields).length) return KEEP_OPEN;
				await post(`lexware/articles/${item.id}/resolve`, { fields });
				return fields as Record<string, string>;
			},
		});
		if (saved) { this.toast("Abweichungen übernommen"); await this.load(); }
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
