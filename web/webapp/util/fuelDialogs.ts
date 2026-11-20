/**
 * Tankbelege im Web: Beleg ansehen (Foto, Angaben, Hinweise, Prüfen/Freigeben/Ablehnen/Erstatten), erfassen bzw.
 * korrigieren und die letzten Belege eines Fahrzeugs (Geräte & Fahrzeuge).
 */
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import Button from "sap/m/Button";
import Image from "sap/m/Image";
import Text from "sap/m/Text";
import Title from "sap/m/Title";
import Label from "sap/m/Label";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import FlexBox from "sap/m/FlexBox";
import MessageStrip from "sap/m/MessageStrip";
import ObjectStatus from "sap/m/ObjectStatus";
import List from "sap/m/List";
import StandardListItem from "sap/m/StandardListItem";
import Link from "sap/m/Link";
import TextArea from "sap/m/TextArea";
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import CoreTitle from "sap/ui/core/Title";
import type Control from "sap/ui/core/Control";
import { del, get, post, problemMessage, put } from "./api";
import { date, dateTime, money } from "./format";
import { uuidv7 } from "./ids";
import { openForm } from "./forms";
import { numberField } from "./numberField";
import { uploadLoose, fileUrl } from "./looseUpload";
import { options } from "./labels";
import { navTo } from "./navigation";
import { showToast } from "./toast";
import {
	allowedDecisions, consumption, decisionTexts, fuelStatus, fuelTypes, km, liters, monthText, paymentMethods, pricePerLiter, settleText,
	type FuelDecision, type FuelReceiptResponse, type FuelVehicleStatistics,
} from "./fuel";

export interface FuelVehicleOption {
	id: string;
	kind: string;
	name: string;
	identifier: string | null;
	tankCapacityLiters: number | null;
	lastOdometerKm: number | null;
	lastFuelType: string | null;
}

export interface FuelOptions {
	vehicles: FuelVehicleOption[];
	suggestedVehicleId: string | null;
	today: string;
}

const vehicleText = (v: { name: string; identifier: string | null }) => v.identifier ? `${v.name} (${v.identifier})` : v.name;

/** Belege entscheiden (mehrere); beim Ablehnen wird nach dem Grund gefragt. Liefert die geänderten Belege oder null. */
export async function decideFuel(receipts: FuelReceiptResponse[], action: FuelDecision): Promise<FuelReceiptResponse[] | null> {
	let reason: string | null = null;
	if (action === "reject") {
		reason = await askReason(receipts.length);
		if (reason === null) return null;
	}
	try {
		const changed = await post<FuelReceiptResponse[]>("fuel-receipts/decisions", { ids: receipts.map((r) => r.id), action, reason });
		const texts: Record<FuelDecision, string> = {
			check: "freigegeben", reject: "abgelehnt", settle: settleText(receipts) === "Als erstattet markieren" ? "als erstattet markiert" : "als erstattet bzw. gebucht markiert",
			reopen: "wieder geöffnet",
		};
		showToast(`${changed.length === 1 ? `Beleg ${changed[0].number}` : `${changed.length} Belege`} ${texts[action]}`);
		return changed;
	} catch (error) {
		MessageBox.error(problemMessage(error), { title: "Tankbeleg" });
		return null;
	}
}

function askReason(count: number): Promise<string | null> {
	return new Promise((resolve) => {
		const area = new TextArea({ width: "100%", rows: 3, maxLength: 500, placeholder: "z. B. Betrag nicht lesbar, Fahrzeug falsch, Beleg doppelt" });
		let result: string | null = null;
		const dialog = new Dialog({
			title: count === 1 ? "Beleg ablehnen" : `${count} Belege ablehnen`, contentWidth: "28rem", type: "Message",
			content: [new Label({ text: "Grund (sieht der Erfasser in der App)", labelFor: area, required: true }), area],
			beginButton: new Button({ text: "Ablehnen", type: "Emphasized", press: () => {
				const text = area.getValue().trim();
				if (!text) { area.setValueState("Error"); area.setValueStateText("Bitte kurz begründen."); return; }
				result = text;
				dialog.close();
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => { dialog.destroy(); resolve(result); },
		});
		dialog.open();
	});
}

/** Beleg ansehen: Foto groß, Angaben, Hinweise und die passenden Entscheidungen. `changed` meldet Änderungen. */
export function openFuelReceipt(receipt: FuelReceiptResponse, manage: boolean, changed: () => void): void {
	const status = fuelStatus(receipt.status, receipt.paymentMethod);
	const image: Control = receipt.imageFileId
		? new Image({ src: fileUrl(receipt.imageFileId, "display"), alt: `Belegfoto ${receipt.number}`, width: "100%", densityAware: false,
			press: () => window.open(fileUrl(receipt.imageFileId!), "_blank", "noopener") }).addStyleClass("eoFuelImage")
		: new MessageStrip({ text: "Kein Belegfoto vorhanden.", type: "Warning", showIcon: true });
	const notStored = receipt.imageFileId && !receipt.imageStored
		? [new MessageStrip({ text: "Das Belegfoto wird noch vom Gerät übertragen.", type: "Information", showIcon: true })] : [];
	const form = new SimpleForm({
		editable: false, layout: "ColumnLayout", columnsM: 1, columnsL: 1, columnsXL: 1, labelSpanM: 4,
		content: [
			new CoreTitle({ text: "Beleg" }),
			...pair("Datum", date(receipt.date)),
			...pair("Fahrzeug", vehicleText({ name: receipt.vehicleName, identifier: receipt.vehicleIdentifier })),
			...pair("Getankt von", receipt.userName),
			...pair("Kraftstoff", fuelTypes[receipt.fuelType]),
			...pair("Menge", `${liters(receipt.liters)} · ${pricePerLiter(receipt.pricePerLiter)}`),
			...pair("Betrag brutto", money(receipt.amountGross)),
			...pair("Zahlart", paymentMethods[receipt.paymentMethod]),
			...pair("Kilometerstand", km(receipt.odometerKm)),
			...(receipt.distanceKm ? pair("Seit letztem Tanken", `${km(receipt.distanceKm)} · ${consumption(receipt.consumptionPer100Km)}`) : []),
			...(receipt.station ? pair("Tankstelle", receipt.station) : []),
			...(receipt.notes ? pair("Notiz", receipt.notes) : []),
			new CoreTitle({ text: "Bearbeitung" }),
			new Label({ text: "Status" }), new ObjectStatus({ text: status.text, state: status.tone }),
			...(receipt.rejectionReason ? pair("Grund", receipt.rejectionReason) : []),
			...(receipt.checkedByName ? pair(receipt.status === "rejected" ? "Abgelehnt von" : "Geprüft von", `${receipt.checkedByName} · ${dateTime(receipt.checkedAt)}`) : []),
			...(receipt.settledByName ? pair(receipt.paymentMethod === "private" ? "Erstattet von" : "Gebucht von", `${receipt.settledByName} · ${dateTime(receipt.settledAt)}`) : []),
			...pair("Erfasst", `${dateTime(receipt.createdAt)} · ${receipt.source === "ios" ? "iPhone/iPad" : receipt.source === "android" ? "Android" : "Web"}`),
		],
	});
	const warnings = receipt.warnings.filter((w) => w.code !== "missing_image")
		.map((w) => new MessageStrip({ text: w.text, type: w.code === "duplicate" || w.code === "odometer_backwards" ? "Error" : "Warning", showIcon: true }));
	const layout = new FlexBox({ wrap: "Wrap", alignItems: "Start", items: [
		new VBox({ width: "22rem", items: [image, ...notStored] }).addStyleClass("sapUiSmallMarginEnd sapUiSmallMarginBottom"),
		new VBox({ width: "24rem", items: [...warnings, form] }),
	] });

	const dialog = new Dialog({
		title: `Tankbeleg ${receipt.number}`, contentWidth: "50rem", resizable: true, draggable: true,
		content: [layout], afterClose: () => dialog.destroy(),
		buttons: [
			...(manage ? allowedDecisions([receipt]).map((action) => new Button({
				text: action === "settle" ? settleText([receipt]).replace("Als ", "").replace(" markieren", "") : decisionTexts[action],
				type: action === "check" ? "Emphasized" : "Default",
				press: async () => { if (await decideFuel([receipt], action)) { dialog.close(); changed(); } },
			})) : []),
			...(receipt.canEdit ? [new Button({ text: "Bearbeiten", press: async () => {
				dialog.close();
				if (await editFuelReceipt(receipt, manage)) changed();
			} })] : []),
			...(receipt.canEdit ? [new Button({ text: "Löschen", type: "Reject", press: async () => {
				try {
					await del(`fuel-receipts/${receipt.id}`);
					showToast(`Beleg ${receipt.number} gelöscht`);
					dialog.close();
					changed();
				} catch (error) {
					MessageBox.error(problemMessage(error), { title: "Tankbeleg" });
				}
			} })] : []),
			new Button({ text: "Schließen", press: () => dialog.close() }),
		],
	});
	dialog.addStyleClass("sapUiContentPadding");
	dialog.open();
}

function pair(label: string, value: string): Control[] {
	return [new Label({ text: label }), new Text({ text: value })];
}

/** Erfassen (ohne Beleg) bzw. korrigieren; im Büro auch für andere (Mitarbeiter wählbar mit fuel.manage). */
export async function editFuelReceipt(receipt: FuelReceiptResponse | null, manage: boolean, preset?: { vehicleId?: string }): Promise<boolean> {
	const [opts, people] = await Promise.all([
		get<FuelOptions>("fuel-receipts/options"),
		manage ? get<{ id: string; displayName: string; isActive: boolean }[]>("directory/users").catch(() => []) : Promise.resolve([]),
	]);
	let image: File | null = null;
	const imageButton = new Button({ text: receipt?.imageFileId ? "Belegfoto ersetzen" : "Belegfoto hinzufügen", icon: "sap-icon://add-photo", press: () => {
		const input = document.createElement("input");
		input.type = "file";
		input.accept = "image/*";
		input.addEventListener("change", () => {
			image = input.files?.[0] ?? null;
			if (image) imageButton.setText(`Belegfoto: ${image.name}`);
		});
		input.click();
	} });
	const vehicleOptions = opts.vehicles.map((v) => ({ key: v.id, text: vehicleText(v), additionalText: v.lastOdometerKm ? `zuletzt ${km(v.lastOdometerKm)}` : undefined }));
	if (receipt && !vehicleOptions.some((v) => v.key === receipt.vehicleId)) {
		vehicleOptions.push({ key: receipt.vehicleId, text: vehicleText({ name: receipt.vehicleName, identifier: receipt.vehicleIdentifier }), additionalText: undefined });
	}
	const saved = await openForm<FuelReceiptResponse>({
		title: receipt ? `Tankbeleg ${receipt.number} bearbeiten` : "Tankbeleg erfassen", submitText: "Speichern", width: "34rem",
		initial: {
			number: receipt?.number ?? "", vehicleId: receipt?.vehicleId ?? preset?.vehicleId ?? opts.suggestedVehicleId ?? opts.vehicles[0]?.id ?? "",
			date: receipt?.date ?? opts.today, odometerKm: receipt?.odometerKm ?? null, liters: receipt?.liters ?? null, amountGross: receipt?.amountGross ?? null,
			fuelType: receipt?.fuelType ?? "diesel", paymentMethod: receipt?.paymentMethod ?? "fuelCard", station: receipt?.station ?? "", notes: receipt?.notes ?? "",
			userId: receipt?.userId ?? "",
		},
		fields: [
			numberField("number", "Belegnummer", !receipt),
			{ key: "vehicleId", label: "Fahrzeug", type: "select", required: true, options: vehicleOptions },
			...(manage ? [{ key: "userId", label: "Getankt von", type: "select" as const,
				options: [{ key: "", text: receipt ? receipt.userName : "Ich selbst" }, ...people.filter((p) => p.isActive && p.id !== receipt?.userId).map((p) => ({ key: p.id, text: p.displayName }))] }] : []),
			{ key: "date", label: "Datum", type: "date", required: true },
			{ key: "fuelType", label: "Kraftstoff", type: "segmented", options: options(fuelTypes) },
			{ key: "liters", label: "Menge", type: "number", required: true, min: 0.01, max: 5000, unit: "l" },
			{ key: "amountGross", label: "Betrag brutto", type: "number", required: true, min: 0.01, max: 50000, unit: "€" },
			{ key: "odometerKm", label: "Kilometerstand", type: "integer", min: 0, max: 5000000, unit: "km",
				validate: (value, values) => {
					const vehicle = opts.vehicles.find((v) => v.id === values.vehicleId);
					return typeof value === "number" && vehicle?.lastOdometerKm && value < vehicle.lastOdometerKm && !receipt
						? `Kleiner als der letzte bekannte Stand (${km(vehicle.lastOdometerKm)}). Bitte prüfen.` : null;
				} },
			{ key: "paymentMethod", label: "Bezahlt mit", type: "select", options: options(paymentMethods),
				help: "„Privat ausgelegt“: Der Betrag wird nach der Prüfung erstattet." },
			{ key: "station", label: "Tankstelle (optional)", maxLength: 200, placeholder: "z. B. Aral Landshut Nord" },
			{ key: "notes", label: "Notiz", type: "textarea", rows: 2, maxLength: 1000 },
		],
		extra: () => [imageButton],
		submit: async (v) => {
			const imageFileId = image ? await uploadLoose(image) : receipt?.imageFileId ?? null;
			return put<FuelReceiptResponse>(`fuel-receipts/${receipt?.id ?? uuidv7()}`, {
				number: receipt?.number ?? null, version: receipt?.version ?? null, vehicleId: v.vehicleId, date: v.date, odometerKm: v.odometerKm ?? null,
				liters: v.liters, amountGross: v.amountGross, fuelType: v.fuelType, paymentMethod: v.paymentMethod, station: (v.station as string) || null,
				notes: (v.notes as string) || null, imageFileId, userId: (v.userId as string) || null,
			});
		},
	});
	if (saved) showToast(receipt ? `Beleg ${saved.number} gespeichert` : `Beleg ${saved.number} erfasst`);
	return !!saved;
}

/** Geräte & Fahrzeuge › Tankbelege: Verbrauch je Monat und die letzten Belege des Fahrzeugs. */
export async function openVehicleFuel(vehicle: { id: string; name: string; identifier: string | null }, today: string): Promise<void> {
	const from = `${Number(today.slice(0, 4)) - 1}${today.slice(4, 8)}01`;
	const [stats, receipts] = await Promise.all([
		get<FuelVehicleStatistics[]>("fuel-receipts/statistics", { vehicleId: vehicle.id, from, to: today }).catch(() => []),
		get<FuelReceiptResponse[]>("fuel-receipts", { vehicleId: vehicle.id, limit: 10 }).catch(() => []),
	]);
	const s = stats[0];
	const figures = new HBox({ wrap: "Wrap", items: [
		figure("Ø Verbrauch (12 Monate)", consumption(s?.averageConsumption)), figure("Kraftstoff", liters(s?.liters ?? 0)),
		figure("Kosten brutto", money(s?.cost ?? 0)), figure("Letzter Kilometerstand", km(s?.lastOdometerKm)),
	] }).addStyleClass("eoKpiBar");
	const months = new List({ headerText: "Je Monat", noDataText: "Noch keine Tankbelege.", items: [...(s?.months ?? [])].reverse().slice(0, 6).map((m) => new StandardListItem({
		title: monthText(m.month), description: `${liters(m.liters)} · ${money(m.cost)}`, info: consumption(m.averageConsumption),
	})) });
	const list = new List({ headerText: "Letzte Belege", noDataText: "Noch keine Tankbelege.", items: receipts.map((r) => {
		const status = fuelStatus(r.status, r.paymentMethod);
		return new StandardListItem({
			title: `${date(r.date)} · ${liters(r.liters)} ${fuelTypes[r.fuelType]} · ${money(r.amountGross)}`,
			description: [r.number, r.userName, r.odometerKm ? km(r.odometerKm) : null, r.consumptionPer100Km ? consumption(r.consumptionPer100Km) : null].filter(Boolean).join(" · "),
			info: status.text, infoState: status.tone, icon: r.imageFileId ? fileUrl(r.imageFileId, "thumb") : "sap-icon://receipt",
			highlight: r.warnings.some((w) => w.code !== "missing_image") ? "Warning" : "None",
		});
	}) });
	const dialog = new Dialog({
		title: `Tankbelege: ${vehicleText(vehicle)}`, contentWidth: "40rem",
		content: [figures, months, list, new Link({ text: "Alle Tankbelege dieses Fahrzeugs", press: () => { dialog.close(); navTo(`/tankbelege?fahrzeug=${vehicle.id}`); } })
			.addStyleClass("sapUiSmallMargin")],
		buttons: [new Button({ text: "Schließen", press: () => dialog.close() })],
		afterClose: () => dialog.destroy(),
	});
	dialog.open();
}

function figure(label: string, value: string): Control {
	return new VBox({ items: [new Label({ text: label, wrapping: true }), new Title({ text: value, level: "H3" })] }).addStyleClass("eoKpiItem");
}
