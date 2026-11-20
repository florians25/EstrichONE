import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type Table from "sap/m/Table";
import { apiUrl, get } from "../../util/api";
import { isoDay } from "../../util/days";
import { date, money } from "../../util/format";
import { fileUrl } from "../../util/looseUpload";
import type { RouteMatch } from "../../util/router";
import {
	allowedDecisions, consumption, defaultRange, fuelKpis, fuelQuery, fuelStatus, fuelTypes, km, liters, monthText, paymentMethods,
	statisticRows, warningSummary, type FuelDecision, type FuelFilter, type FuelReceiptResponse, type FuelVehicleStatistics,
} from "../../util/fuel";
import { decideFuel, editFuelReceipt, openFuelReceipt, type FuelOptions } from "../../util/fuelDialogs";

const toDate = (day: string | null) => (day ? new Date(`${day}T00:00:00`) : null);

/**
 * Büro › Tankbelege: Belege aus den Apps prüfen, freigeben, ablehnen, erstatten bzw. buchen; Hinweise zur Plausibilität,
 * Auswertung je Fahrzeug und CSV für die Buchhaltung. Ohne fuel.manage: nur die eigenen Belege.
 * @namespace estrichone.controller.office
 */
export default class FuelReceipts extends BaseController {
	private receipts: FuelReceiptResponse[] = [];

	onInit(): void {
		const range = defaultRange(isoDay(new Date()));
		this.local({
			items: [], busy: false, tab: "belege", manage: false, allowed: [], kpis: [], subtitle: "",
			fromDate: toDate(range.from), toDate: toDate(range.to), rangeText: "",
			filter: { vehicleId: "", userId: "", status: "", paymentMethod: "", warningsOnly: false },
			vehicleOptions: [{ key: "", text: "Alle Fahrzeuge" }], peopleOptions: [{ key: "", text: "Alle Mitarbeiter" }],
			statVehicles: [], statRows: [],
		});
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const m = this.model();
		const manage = this.has("fuel.manage");
		m.setProperty("/manage", manage);
		m.setProperty("/subtitle", manage
			? "Belege aus den Apps prüfen, freigeben und erstatten bzw. buchen – mit Hinweisen zur Plausibilität und Export für die Buchhaltung"
			: "Ihre Tankbelege – erfassen in der App oder hier");
		const vehicle = match.query.get("fahrzeug");
		if (vehicle) m.setProperty("/filter/vehicleId", vehicle);
		const status = match.query.get("status");
		if (status) m.setProperty("/filter/status", status);
		if (match.query.get("reiter") === "auswertung" && manage) m.setProperty("/tab", "auswertung");
		const [options, people] = await Promise.all([
			get<FuelOptions>("fuel-receipts/options").catch(() => null),
			manage ? get<{ id: string; displayName: string }[]>("directory/users").catch(() => []) : Promise.resolve([]),
		]);
		m.setProperty("/vehicleOptions", [{ key: "", text: "Alle Fahrzeuge" },
			...(options?.vehicles ?? []).map((v) => ({ key: v.id, text: v.identifier ? `${v.name} (${v.identifier})` : v.name }))]);
		m.setProperty("/peopleOptions", [{ key: "", text: "Alle Mitarbeiter" }, ...people.map((p) => ({ key: p.id, text: p.displayName }))]);
		await this.load();
		const open = match.query.get("beleg");
		if (open) {
			this.setQuery({ beleg: null });
			const receipt = this.receipts.find((r) => r.id === open) ?? await get<FuelReceiptResponse>(`fuel-receipts/${open}`).catch(() => null);
			if (receipt) openFuelReceipt(receipt, manage, () => void this.load());
		}
	}

	onRefresh(): void { void this.load(); }

	onTab(): void { void this.load(); }

	private filter(): FuelFilter {
		const m = this.model();
		const f = m.getProperty("/filter") as Omit<FuelFilter, "from" | "to">;
		const from = m.getProperty("/fromDate") as Date | null;
		const to = m.getProperty("/toDate") as Date | null;
		return { ...f, from: from ? isoDay(from) : null, to: to ? isoDay(to) : null };
	}

	private async load(): Promise<void> {
		const m = this.model();
		const manage = m.getProperty("/manage") as boolean;
		const filter = this.filter();
		m.setProperty("/busy", true);
		m.setProperty("/rangeText", `${date(filter.from)} – ${date(filter.to)}`);
		try {
			const [items, stats] = await Promise.all([
				manage ? get<FuelReceiptResponse[]>("fuel-receipts", fuelQuery(filter)) : get<FuelReceiptResponse[]>("fuel-receipts/mine", { limit: 300 }),
				manage && m.getProperty("/tab") === "auswertung"
					? get<FuelVehicleStatistics[]>("fuel-receipts/statistics", { from: filter.from ?? undefined, to: filter.to ?? undefined, vehicleId: filter.vehicleId || undefined })
					: Promise.resolve(null),
			]);
			this.receipts = manage ? items : items.filter((r) => (!filter.from || r.date >= filter.from) && (!filter.to || r.date <= filter.to)
				&& (!filter.vehicleId || r.vehicleId === filter.vehicleId) && (!filter.paymentMethod || r.paymentMethod === filter.paymentMethod)
				&& (!filter.status || (filter.status === "open" ? r.status === "new" || r.status === "checked" : r.status === filter.status)));
			m.setProperty("/items", this.receipts.map((r) => this.row(r)));
			const k = fuelKpis(this.receipts);
			m.setProperty("/kpis", [
				{ title: "Zu prüfen", value: String(k.open), unit: "", color: k.open ? "Critical" : "Neutral" },
				...(manage ? [{ title: "Mit Hinweis", value: String(k.flagged), unit: "", color: k.flagged ? "Error" : "Neutral" }] : []),
				{ title: "Offene Erstattungen", value: money(k.refundAmount).replace(/\s?€$/, ""), unit: `€ · ${k.refundOpen} Belege`, color: "Neutral" },
				{ title: "Kosten im Zeitraum", value: money(k.cost).replace(/\s?€$/, ""), unit: "€", color: "Neutral" },
				{ title: "Kraftstoff", value: liters(k.liters).replace(/\s?l$/, ""), unit: "l", color: "Neutral" },
			]);
			if (stats) {
				m.setProperty("/statVehicles", stats.map((s) => ({
					...s, title: s.identifier ? `${s.vehicleName} (${s.identifier})` : s.vehicleName, litersText: liters(s.liters),
					adBlueText: s.adBlueLiters ? liters(s.adBlueLiters) : "–", costText: money(s.cost), distanceText: s.distanceKm ? km(s.distanceKm) : "–",
					consumptionText: consumption(s.averageConsumption),
					monthsText: s.months.length ? `${monthText(s.months[0].month)} bis ${monthText(s.months[s.months.length - 1].month)}` : "",
				})));
				m.setProperty("/statRows", statisticRows(stats).map((r) => ({
					...r, litersText: liters(r.liters), costText: money(r.cost), distanceText: r.distanceKm ? km(r.distanceKm) : "–",
					consumptionText: consumption(r.averageConsumption),
				})));
			}
			this.onSelection();
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private row(r: FuelReceiptResponse) {
		const status = fuelStatus(r.status, r.paymentMethod);
		const warning = warningSummary(r.warnings);
		return {
			...r, dateText: date(r.date), vehicleText: r.vehicleIdentifier ? `${r.vehicleName} · ${r.vehicleIdentifier}` : r.vehicleName,
			litersText: `${liters(r.liters)} ${fuelTypes[r.fuelType]}`, kmText: r.odometerKm ? km(r.odometerKm) : "ohne km-Stand",
			consumptionText: r.consumptionPer100Km ? consumption(r.consumptionPer100Km) : "", amountText: money(r.amountGross),
			paymentText: paymentMethods[r.paymentMethod], statusText: status.text, statusTone: status.tone,
			warningText: warning.text, warningTone: warning.tone, warningTooltip: r.warnings.map((w) => w.text).join("\n"),
			highlight: warning.tone === "Error" ? "Error" : warning.tone === "Warning" && warning.text !== "Ohne Foto" ? "Warning" : "None",
			thumb: r.imageFileId && r.imageStored ? fileUrl(r.imageFileId, "thumb") : "",
		};
	}

	private selected(): FuelReceiptResponse[] {
		const table = this.v().byId("receipts") as Table | undefined;
		const ids = new Set((table?.getSelectedContexts() ?? []).map((c) => (c.getObject() as FuelReceiptResponse).id));
		return this.receipts.filter((r) => ids.has(r.id));
	}

	onSelection(): void {
		this.model().setProperty("/allowed", allowedDecisions(this.selected()));
	}

	async onDecide(e: Event): Promise<void> {
		const action = (e.getSource() as Control).data("action") as FuelDecision;
		const chosen = this.selected();
		if (!chosen.length) return;
		if (await decideFuel(chosen, action)) {
			(this.v().byId("receipts") as Table | undefined)?.removeSelections(true);
			await this.load();
		}
	}

	onOpen(e: Event): void {
		const row = (e.getSource() as Control).getBindingContext("view")?.getObject() as FuelReceiptResponse;
		const receipt = this.receipts.find((r) => r.id === row.id);
		if (receipt) openFuelReceipt(receipt, this.model().getProperty("/manage") as boolean, () => void this.load());
	}

	async onNew(): Promise<void> {
		const vehicleId = this.model().getProperty("/filter/vehicleId") as string;
		if (await editFuelReceipt(null, this.model().getProperty("/manage") as boolean, { vehicleId: vehicleId || undefined })) await this.load();
	}

	onStatVehicle(e: Event): void {
		const s = (e.getSource() as Control).getBindingContext("view")?.getObject() as FuelVehicleStatistics;
		this.model().setProperty("/filter/vehicleId", s.vehicleId);
		this.model().setProperty("/tab", "belege");
		void this.load();
	}

	onExport(): void {
		window.open(apiUrl("fuel-receipts/export.csv", fuelQuery({ ...this.filter(), warningsOnly: false })), "_blank", "noopener");
	}
}
