import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Table from "sap/m/Table";
import type IconTabBar from "sap/m/IconTabBar";
import MessageBox from "sap/m/MessageBox";
import { del, get, post, put } from "../../util/api";
import { dateTime } from "../../util/format";
import { session } from "../../util/session";
import type { RouteMatch } from "../../util/router";
import {
	changeSyncHelp, changeSyncOptions, changeSyncStand, changeSyncState, creationStateText, deviationValue, enableSyncQuestion, lastTestText, matchLabel, placeText, reconciliationFigures, uniquePairs,
	type DeviationSource, type LexwareContactSummary, type LexwareCustomerSummary, type LexwareDeviationItem, type LexwareInternalDuplicate, type LexwareLinkedPair,
	type LexwareReconciliation, type LexwareSettings, type LexwareSuggestion, type LexwareTestResponse,
} from "../../util/lexware";
import { articleSyncHelp, articleSyncOptions, statusRuleTexts } from "../../util/quotations";
import { importLexwareContact, openCustomerLinkDialog, openDeviationDialog, openExternal, resolveAllDeviations } from "../../util/lexwareDialogs";

const numberText = (number: string | null) => (number ? `Kundennummer ${number}` : "ohne Kundennummer");
const sideText = (c: LexwareCustomerSummary | LexwareContactSummary | null) => (c ? [numberText(c.customerNumber), placeText(c), c.email].filter(Boolean).join(", ") : "");

/** Verwaltung › Integrationen › Lexware Office: Zugang, Vorgaben für Rechnungen und Abgleichübersicht der Kunden. @namespace estrichone.controller.admin */
export default class Lexware extends BaseController {
	onInit(): void {
		this.local({
			s: null, loaded: false, busy: false, apiKey: "", baseUrl: "", lastTestText: "", tab: "connection",
			rec: null, recBusy: false, recStand: "", figures: [], list: "suggestions", suggestions: [], internal: [], onlyEstrichOne: [], onlyLexware: [], linked: [], excluded: [], excludedSelected: 0,
			deviations: [], changeSyncOptions, changeSyncHelp: "", changeSyncStand: { text: "", state: "None" },
			articleSyncOptions, articleSyncHelp: "", articleSyncStand: { text: "", state: "None" }, ruleTexts: statusRuleTexts,
		});
		this.model().bindProperty("/s/articleSync").attachChange(() => this.model().setProperty("/articleSyncHelp", articleSyncHelp(this.model().getProperty("/s/articleSync") as string)));
		// Die Erläuterung folgt der Auswahl, noch vor dem Speichern.
		this.model().bindProperty("/s/changeSync").attachChange(() => this.model().setProperty("/changeSyncHelp", changeSyncHelp(this.model().getProperty("/s/changeSync") as string)));
	}

	onRouteMatched(match: RouteMatch): void {
		const tab = match.query.get("reiter") === "kunden" ? "customers" : "connection";
		if (match.query.get("liste") === "abweichungen") this.model().setProperty("/list", "deviations");
		this.model().setProperty("/tab", tab);
		void this.load().then(() => {
			// Der Reiter „Kundenabgleich“ wird erst mit geladenen Einstellungen sichtbar: danach erneut auswählen.
			const s = this.model().getProperty("/s") as LexwareSettings | null;
			const shown = tab === "customers" && s?.moduleEnabled === true && s.hasApiKey ? "customers" : "connection";
			(this.byId("lexwareTabs") as IconTabBar).setSelectedKey(shown);
			this.model().setProperty("/tab", shown);
			if (shown === "customers") void this.loadReconciliation(false);
		});
	}

	onTab(e: Event): void {
		const key = String(e.getParameter("key" as never));
		this.setQuery({ reiter: key === "customers" ? "kunden" : null });
		if (key === "customers" && !this.model().getProperty("/rec")) void this.loadReconciliation(false);
	}

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const s = await this.run(() => get<LexwareSettings>("settings/integrations/lexware"));
		m.setProperty("/busy", false);
		if (s) this.apply(s);
	}

	private apply(s: LexwareSettings): void {
		const m = this.model();
		m.setProperty("/s", s);
		m.setProperty("/loaded", true);
		m.setProperty("/apiKey", "");
		m.setProperty("/baseUrl", s.customBaseUrl ? s.baseUrl : "");
		m.setProperty("/lastTestText", lastTestText(s, dateTime));
		m.setProperty("/changeSyncHelp", changeSyncHelp(s.changeSync));
		m.setProperty("/changeSyncStand", changeSyncStand(s, dateTime));
		m.setProperty("/articleSyncHelp", articleSyncHelp(s.articleSync));
		m.setProperty("/articleSyncStand", s.lastArticleSyncError
			? { text: `Letzter Artikelabgleich fehlgeschlagen: ${s.lastArticleSyncError}`, state: "Error" }
			: s.lastArticleSyncAt ? { text: `Letzter Artikelabgleich: ${dateTime(s.lastArticleSyncAt)}`, state: "None" } : { text: "", state: "None" });
		this.statusLine([
			!s.moduleEnabled && { text: "Das Modul ist ausgeschaltet. Kunde und Bauakte zeigen keine Lexware-Abschnitte.", level: "info" },
			s.moduleEnabled && !s.hasApiKey && { text: "Es ist noch kein API-Schlüssel hinterlegt.", level: "warning" },
			s.lastTestOk === false && { text: "Der letzte Verbindungstest ist fehlgeschlagen (Einzelheiten unter „Verbindung“).", level: "error" },
			s.hasApiKey && s.lastTestAt === null && { text: "Die Verbindung wurde noch nicht getestet.", level: "info" },
			s.customersWithDeviations > 0 && {
				text: `${s.customersWithDeviations === 1 ? "Bei einem verknüpften Kunden" : `Bei ${s.customersWithDeviations} verknüpften Kunden`} gibt es Abweichungen zu Lexware Office (Reiter „Kundenabgleich“).`,
				level: "warning",
			},
		], !s.hasApiKey ? "Nicht eingerichtet."
			: `Verbunden${s.organizationName ? ` mit ${s.organizationName}` : ""}, ${s.linkedCustomers} ${s.linkedCustomers === 1 ? "Kunde" : "Kunden"} verknüpft, `
				+ `${s.linkedInvoices} ${s.linkedInvoices === 1 ? "Beleg" : "Belege"} Bauakten zugeordnet`);
	}

	private async save(): Promise<LexwareSettings | undefined> {
		const m = this.model();
		const s = m.getProperty("/s") as LexwareSettings;
		const saved = await this.run(() => put<LexwareSettings>("settings/integrations/lexware", {
			apiKey: String(m.getProperty("/apiKey") ?? "").trim() || null, baseUrl: String(m.getProperty("/baseUrl") ?? "").trim() || null,
			version: s.version, newCustomersSync: s.newCustomersSync === true, changeSync: s.changeSync, autoCreate: s.autoCreate === true,
			articleSync: s.articleSync, statusOnQuotationAccepted: s.statusOnQuotationAccepted !== false, statusOnInvoicesPaid: s.statusOnInvoicesPaid !== false,
		}));
		if (saved) {
			this.apply(saved);
			m.setProperty("/rec", null);
			void session.reloadPermissions();
		}
		return saved;
	}

	async onSave(): Promise<void> {
		if (await this.save()) this.toast("Gespeichert");
	}

	async onTest(): Promise<void> {
		if (!await this.save()) return;
		const result = await this.run(() => post<LexwareTestResponse>("settings/integrations/lexware/test"));
		if (!result) return;
		if (result.ok) this.toast(`Verbindung in Ordnung${result.organizationName ? `: ${result.organizationName}` : ""}`);
		else MessageBox.error(result.error ?? "Die Verbindung ist fehlgeschlagen.", { title: "Verbindung testen" });
		await this.load();
	}

	async onClearKey(): Promise<void> {
		if (!await this.confirm("API-Schlüssel entfernen? Zugeordnete Belege und Kundenverknüpfungen bleiben gespeichert, lassen sich aber erst mit einem neuen Schlüssel wieder abgleichen.",
			{ action: "Entfernen", danger: true })) return;
		const s = this.model().getProperty("/s") as LexwareSettings;
		const saved = await this.run(() => put<LexwareSettings>("settings/integrations/lexware", {
			clearApiKey: true, baseUrl: s.customBaseUrl ? s.baseUrl : null, version: s.version,
		}), "Schlüssel entfernt");
		if (saved) this.apply(saved);
	}

	async onModule(e: Event): Promise<void> {
		const on = e.getParameter("state" as never) as boolean;
		const saved = await this.run(() => put("settings/modules", { modules: { lexware: on } }), on ? "Modul eingeschaltet" : "Modul ausgeschaltet");
		if (!saved) this.model().setProperty("/s/moduleEnabled", !on);
		await session.reloadPermissions();
		await this.load();
	}

	// ---------- Kundenabgleich ----------

	private async loadReconciliation(refresh: boolean): Promise<void> {
		const m = this.model();
		const s = m.getProperty("/s") as LexwareSettings | null;
		if (!s?.moduleEnabled || !s.hasApiKey) return;
		m.setProperty("/recBusy", true);
		try {
			this.applyReconciliation(await get<LexwareReconciliation>("settings/integrations/lexware/reconciliation", refresh ? { refresh: true } : undefined));
		} catch (error) {
			this.showError(error, "Kundenabgleich");
		} finally {
			m.setProperty("/recBusy", false);
		}
	}

	private applyReconciliation(r: LexwareReconciliation): void {
		const m = this.model();
		m.setProperty("/rec", r);
		m.setProperty("/figures", reconciliationFigures(r));
		m.setProperty("/recStand", `Stand der Kontaktliste: ${dateTime(r.loadedAt)}`);
		m.setProperty("/suggestions", r.suggestions.map((s: LexwareSuggestion) => {
			const label = matchLabel(s);
			return { ...s, customerText: sideText(s.customer), lexwareText: sideText(s.lexware), matchText: label.text, matchState: s.unique ? "Success" : label.state };
		}));
		m.setProperty("/internal", r.internalDuplicates.map((d: LexwareInternalDuplicate) => ({
			...d, systemText: d.system === "lexware" ? "Lexware Office" : "EstrichONE", firstText: numberText(d.firstNumber), secondText: numberText(d.secondNumber),
			matchText: d.reasons.join(", "),
		})));
		m.setProperty("/onlyEstrichOne", r.onlyEstrichOneItems.map((c) => {
			const creation = creationStateText(c.creation, c.creationError);
			return { ...c, numberText: numberText(c.customerNumber), place: placeText(c), creationText: creation.text, creationState: creation.state, creationHint: creation.hint };
		}));
		m.setProperty("/onlyLexware", r.onlyLexwareItems.map((c) => ({ ...c, numberText: numberText(c.customerNumber), place: placeText(c) })));
		m.setProperty("/excluded", (r.excludedItems ?? []).map((c) => ({ ...c, numberText: numberText(c.customerNumber), place: placeText(c) })));
		m.setProperty("/excludedSelected", 0);
		(this.byId("lexwareExcluded") as Table | undefined)?.removeSelections(true);
		m.setProperty("/linked", r.linkedItems.map((p: LexwareLinkedPair) => {
			const change = changeSyncState(p.sync, dateTime);
			const quiet = p.sync?.state === "off" || p.sync?.state === "paused";
			return {
				...p, customerText: sideText(p.customer), lexwareText: sideText(p.lexware), changeText: quiet ? "" : change.text, changeState: change.state, changeHint: change.hint,
				missingText: p.sync?.state === "archived" ? "In Lexware Office archiviert" : p.sync?.state === "missing" ? "In Lexware Office nicht mehr vorhanden"
					: "In Lexware Office nicht mehr vorhanden oder archiviert",
			};
		}));
		m.setProperty("/deviations", (r.deviationItems ?? []).map((d: LexwareDeviationItem) => ({
			...d, customerText: numberText(d.customer.customerNumber),
			rows: d.fields.map((f) => ({ ...f, estrichOneText: deviationValue(f.estrichOne), lexwareText: deviationValue(f.lexware) })),
		})));
		// Offene Abweichungen zuerst zeigen, solange der Nutzer noch keine Liste gewählt hat.
		if (!this.listChosen && (r.deviations ?? 0) > 0) m.setProperty("/list", "deviations");
		this.listChosen = true;
	}

	private listChosen = false;

	async onResolve(e: Event): Promise<void> {
		const d = this.row<LexwareDeviationItem>(e);
		if (await openDeviationDialog({ id: d.customer.id, name: d.customer.name }, d.fields)) await this.loadReconciliation(false);
	}

	private async takeAll(e: Event, source: DeviationSource): Promise<void> {
		const d = this.row<LexwareDeviationItem>(e);
		if (await resolveAllDeviations({ id: d.customer.id, name: d.customer.name }, source)) await this.loadReconciliation(false);
	}

	onTakeLocal(e: Event): void {
		void this.takeAll(e, "estrichOne");
	}

	onTakeRemote(e: Event): void {
		void this.takeAll(e, "lexware");
	}

	onRefresh(): void {
		void this.loadReconciliation(true);
	}

	private row<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}

	onOpenCustomer(e: Event): void {
		const row = this.row<{ customer?: LexwareCustomerSummary; id?: string }>(e);
		this.nav(`/kunden/${row.customer?.id ?? row.id}?abschnitt=lexware`);
	}

	onOpenInternal(e: Event): void {
		const row = this.row<LexwareInternalDuplicate>(e);
		const id = (e.getSource() as Control).data("which") === "second" ? row.secondId : row.firstId;
		if (row.system === "estrichOne") { this.nav(`/kunden/${id}`); return; }
		const rec = this.model().getProperty("/rec") as LexwareReconciliation;
		const url = [...rec.onlyLexwareItems, ...rec.linkedItems.map((l) => l.lexware)].find((c) => c?.id === id)?.url;
		openExternal(url);
	}

	async onLinkSuggestion(e: Event): Promise<void> {
		const s = this.row<LexwareSuggestion>(e);
		if (!await this.confirm(`„${s.customer.name}“ (EstrichONE) mit „${s.lexware.name}“ (Lexware Office) verknüpfen? Übereinstimmung: ${s.reasons.join(", ")}.`,
			{ action: "Verknüpfen" })) return;
		if (await this.run(() => post(`lexware/customers/${s.customer.id}/link`, { lexwareContactId: s.lexware.id }), "Verknüpft")) await this.loadReconciliation(false);
	}

	async onLinkCustomer(e: Event): Promise<void> {
		const c = this.row<LexwareCustomerSummary>(e);
		if (await openCustomerLinkDialog({ id: c.id, name: c.name })) await this.loadReconciliation(false);
	}

	async onImport(e: Event): Promise<void> {
		if (await importLexwareContact(this.row<LexwareContactSummary>(e))) await this.loadReconciliation(false);
	}

	async onUnlink(e: Event): Promise<void> {
		const p = this.row<LexwareLinkedPair>(e);
		if (!await this.confirm(`Verknüpfung von „${p.customer.name}“ mit Lexware Office lösen? In Lexware Office wird nichts gelöscht; bereits zugeordnete Belege bleiben an den Bauakten.`,
			{ action: "Lösen", danger: true })) return;
		if (await this.run(async () => { await del(`lexware/customers/${p.customer.id}/link`); return true; }, "Verknüpfung gelöst")) await this.loadReconciliation(false);
	}

	onExcludedSelection(): void {
		this.model().setProperty("/excludedSelected", (this.byId("lexwareExcluded") as Table).getSelectedItems().length);
	}

	/** Massenaktion: „Mit Lexware Office abgleichen“ für ausgewählte Kunden einschalten (überträgt nichts). */
	async onEnableSync(): Promise<void> {
		const contactIds = (this.byId("lexwareExcluded") as Table).getSelectedItems()
			.map((i) => (i.getBindingContext("view")?.getObject() as LexwareCustomerSummary).id);
		if (contactIds.length === 0) return;
		// Vorher zeigen, wie viele Kunden dadurch in Lexware Office neu angelegt würden.
		const preview = await this.run(() => post<{ changed: number; wouldCreate: number; withMatches: number }>(
			"settings/integrations/lexware/reconciliation/enable-sync", { contactIds, preview: true }));
		if (!preview) return;
		if (!await this.confirm(enableSyncQuestion(preview), { action: "Einschalten" })) return;
		const result = await this.run(() => post<{ changed: number }>("settings/integrations/lexware/reconciliation/enable-sync", { contactIds }));
		if (!result) return;
		this.toast(`Abgleich für ${result.changed} Kunden eingeschaltet`);
		await this.loadReconciliation(false);
	}

	async onLinkUnique(): Promise<void> {
		const rec = this.model().getProperty("/rec") as LexwareReconciliation | null;
		const pairs = uniquePairs(rec);
		if (!rec || pairs.length === 0) return;
		const preview = rec.suggestions.filter((s) => s.unique).slice(0, 15).map((s) => `${s.customer.name} und ${s.lexware.name} (${s.reasons.join(", ")})`).join("\n");
		const more = pairs.length > 15 ? `\nund ${pairs.length - 15} weitere` : "";
		if (!await this.confirm(`${pairs.length === 1 ? "Diesen eindeutigen Treffer" : `Diese ${pairs.length} eindeutigen Treffer`} verknüpfen?\n\n${preview}${more}\n\n`
			+ "Eindeutig heißt: auf beiden Seiten genau ein Gegenstück mit gleicher E-Mail-Adresse oder gleicher Kundennummer.", { action: "Verknüpfen", title: "Vorschau" })) return;
		const result = await this.run(() => post<{ linked: number; skipped: number }>("settings/integrations/lexware/reconciliation/link-unique", { pairs }));
		if (!result) return;
		this.toast(`${result.linked} verknüpft${result.skipped > 0 ? `, ${result.skipped} übersprungen, weil sie nicht mehr eindeutig sind` : ""}`);
		await this.loadReconciliation(false);
	}
}
