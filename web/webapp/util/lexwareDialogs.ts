/**
 * Lexware Office: Dialoge der Weboberfläche. Kunde verknüpfen bzw. in Lexware anlegen (immer mit Dublettenprüfung),
 * Lexware-Kunden nach EstrichONE übernehmen (ebenfalls mit Dublettenprüfung) und einen Beleg (Rechnung, Angebot) einer
 * Bauakte zuordnen. Belege werden nur gelesen: Es gibt keinen Dialog, der in Lexware Office einen Beleg anlegt oder ändert.
 */
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import SearchField from "sap/m/SearchField";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import Table from "sap/m/Table";
import Column from "sap/m/Column";
import ColumnListItem from "sap/m/ColumnListItem";
import Toolbar from "sap/m/Toolbar";
import ToolbarSpacer from "sap/m/ToolbarSpacer";
import Title from "sap/m/Title";
import MessageStrip from "sap/m/MessageStrip";
import ObjectStatus from "sap/m/ObjectStatus";
import ObjectIdentifier from "sap/m/ObjectIdentifier";
import JSONModel from "sap/ui/model/json/JSONModel";
import type Control from "sap/ui/core/Control";
import { get, post, problemMessage } from "./api";
import { date } from "./format";
import { largeDialogSize } from "./dialogSize";
import { showToast } from "./toast";
import {
	assignedText, createAnywayQuestion, euro, matchLabel, placeText, voucherNumber, voucherStatus, voucherTypeText,
	type BauakteVoucherCandidates, type CreateLexwareContactResponse, type CustomerLexware, type ImportLexwareContactResponse, type LexwareCandidates,
	type LexwareContactCandidate, type LexwareContactList, type LexwareContactSummary, type LexwareCustomerCandidate, type LexwareVoucher,
	type LexwareVoucherItem,
} from "./lexware";

const TITLE = "Lexware Office";

function fail(error: unknown, title = TITLE): void {
	MessageBox.error(problemMessage(error), { title });
}

function ask(text: string, action: string, title = TITLE): Promise<boolean> {
	return new Promise((resolve) => {
		MessageBox.show(text, {
			icon: MessageBox.Icon.WARNING, title, actions: [action, MessageBox.Action.CANCEL], emphasizedAction: action,
			onClose: (result: string | null) => resolve(result === action),
		});
	});
}

export function openExternal(url: string | null | undefined): void {
	if (url) window.open(url, "_blank", "noopener");
}

const column = (text: string, options: Record<string, unknown> = {}) => new Column({ header: new Text({ text }), ...options });

interface CandidateRow extends LexwareContactCandidate { matchText: string; matchState: string; place: string; taken: boolean; hint: string }

const candidateRows = (candidates: LexwareContactCandidate[], customerId: string): CandidateRow[] => candidates.map((c) => {
	const label = matchLabel(c);
	const taken = !!c.contact.linkedContactId && c.contact.linkedContactId !== customerId;
	return { ...c, matchText: label.text, matchState: label.state, place: placeText(c.contact), taken,
		hint: taken ? `Bereits verknüpft mit ${c.contact.linkedContactName ?? "einem anderen Kunden"}` : "" };
});

/**
 * Kunde mit Lexware Office verknüpfen: zeigt zuerst die Dublettenprüfung, erlaubt eine gezielte Suche und legt den
 * Kontakt nur nach ausdrücklicher Entscheidung neu an. Liefert true, wenn sich die Verknüpfung geändert hat.
 */
export function openCustomerLinkDialog(customer: { id: string; name: string }): Promise<boolean> {
	return new Promise((resolve) => {
		let changed = false;
		const model = new JSONModel({ rows: [] as CandidateRow[], busy: true, searched: false, info: "" });
		const table = new Table({
			mode: "SingleSelectLeft", growing: true, growingThreshold: 25, busy: "{/busy}", busyIndicatorDelay: 0,
			noDataText: "{= ${/searched} ? 'Kein Kontakt in Lexware Office gefunden.' : 'Die Prüfung hat keinen ähnlichen Kontakt in Lexware Office gefunden.' }",
			columns: [column("Kontakt in Lexware Office"), column("Anschrift", { minScreenWidth: "Tablet", demandPopin: true }),
				column("E-Mail", { minScreenWidth: "Desktop", demandPopin: true }), column("Übereinstimmung")],
			items: {
				path: "/rows",
				template: new ColumnListItem({
					cells: [
						new ObjectIdentifier({ title: "{contact/name}", text: "{= ${contact/customerNumber} ? 'Kundennummer ' + ${contact/customerNumber} : '' }" }),
						new Text({ text: "{place}" }), new Text({ text: "{contact/email}" }),
						new VBox({ items: [new ObjectStatus({ text: "{matchText}", state: "{matchState}" }),
							new ObjectStatus({ text: "{hint}", state: "Warning", visible: "{taken}" })] }),
					],
				}),
			},
		});
		table.setModel(model);
		const info = new MessageStrip({ text: "{/info}", type: "Information", showIcon: true, visible: "{= !!${/info} }" }).addStyleClass("sapUiSmallMarginBottom");
		info.setModel(model);

		const load = async (query: string): Promise<void> => {
			model.setProperty("/busy", true);
			try {
				const result = await get<LexwareCandidates>(`lexware/customers/${customer.id}/candidates`, query ? { q: query } : undefined);
				model.setProperty("/rows", candidateRows(result.candidates, customer.id));
				model.setProperty("/searched", !!query);
				model.setProperty("/info", query ? "" : result.candidates.length > 0
					? "Die Prüfung hat ähnliche Kontakte in Lexware Office gefunden. Wählen Sie einen Treffer zum Verknüpfen oder legen Sie den Kunden bewusst neu an."
					: "Die Prüfung hat keinen ähnlichen Kontakt gefunden. Sie können gezielt suchen oder den Kunden in Lexware Office anlegen.");
			} catch (error) {
				fail(error);
			} finally {
				model.setProperty("/busy", false);
			}
		};

		const selected = (): CandidateRow | null => (table.getSelectedItem()?.getBindingContext()?.getObject() as CandidateRow | undefined) ?? null;
		const link = async (): Promise<void> => {
			const row = selected();
			if (!row) { MessageBox.information("Bitte wählen Sie zuerst einen Kontakt aus der Liste.", { title: TITLE }); return; }
			if (row.taken) { MessageBox.warning(`${row.hint}. Ein Lexware-Kontakt kann nur zu einem Kunden gehören.`, { title: TITLE }); return; }
			dialog.setBusy(true);
			try {
				await post<CustomerLexware>(`lexware/customers/${customer.id}/link`, { lexwareContactId: row.contact.id });
				changed = true;
				showToast(`Verknüpft mit ${row.contact.name}`);
				dialog.close();
			} catch (error) {
				fail(error);
			} finally {
				dialog.setBusy(false);
			}
		};
		const create = async (): Promise<void> => {
			dialog.setBusy(true);
			try {
				// Der Server prüft immer zuerst auf Dubletten und legt bei Treffern nur mit ausdrücklicher Bestätigung an.
				let result = await post<CreateLexwareContactResponse>(`lexware/customers/${customer.id}/create`, { createAnyway: false });
				if (!result.created) {
					model.setProperty("/rows", candidateRows(result.candidates, customer.id));
					model.setProperty("/searched", false);
					model.setProperty("/info", "Vor dem Anlegen gefunden: Diese Kontakte in Lexware Office könnten derselbe Kunde sein.");
					if (!await ask(createAnywayQuestion(customer.name, result.candidates.length, "lexware"), "Trotzdem neu anlegen")) return;
					result = await post<CreateLexwareContactResponse>(`lexware/customers/${customer.id}/create`, { createAnyway: true });
				}
				if (result.created) {
					changed = true;
					showToast(`In Lexware Office angelegt${result.link?.number ? ` (Kundennummer ${result.link.number})` : ""}`);
					dialog.close();
				}
			} catch (error) {
				fail(error);
			} finally {
				dialog.setBusy(false);
			}
		};

		const search = new SearchField({ width: "100%", placeholder: "In Lexware Office suchen: Name, E-Mail-Adresse oder Kundennummer",
			search: (e) => void load(String(e.getParameter("query") ?? "").trim()) });
		const dialog = new Dialog({
			title: `${customer.name} mit Lexware Office verknüpfen`, ...largeDialogSize(Math.min(window.innerWidth, 1100)), resizable: true, draggable: true,
			content: [new VBox({ items: [info, search, table] }).addStyleClass("sapUiSmallMargin")],
			buttons: [
				new Button({ text: "Mit Auswahl verknüpfen", type: "Emphasized", icon: "sap-icon://chain-link", press: () => void link() }),
				new Button({ text: "In Lexware anlegen", icon: "sap-icon://add", press: () => void create() }),
				new Button({ text: "Abbrechen", press: () => dialog.close() }),
			],
			afterClose: () => { dialog.destroy(); model.destroy(); resolve(changed); },
		});
		dialog.open();
		void load("");
	});
}

interface CustomerRow extends LexwareCustomerCandidate { matchText: string; matchState: string; place: string; taken: boolean }

/**
 * Lexware-Kontakt als Kunde nach EstrichONE übernehmen. Vorher die Dublettenprüfung gegen die EstrichONE-Kunden:
 * bei Treffern verknüpfen oder bewusst neu anlegen. Liefert true, wenn etwas verknüpft oder angelegt wurde.
 */
export async function importLexwareContact(contact: LexwareContactSummary): Promise<boolean> {
	let candidates: LexwareCustomerCandidate[];
	try {
		candidates = await get<LexwareCustomerCandidate[]>(`lexware/contacts/${encodeURIComponent(contact.id)}/candidates`);
	} catch (error) {
		fail(error);
		return false;
	}
	const doImport = async (createAnyway: boolean): Promise<boolean> => {
		try {
			const result = await post<ImportLexwareContactResponse>(`lexware/contacts/${encodeURIComponent(contact.id)}/import`, { createAnyway });
			if (!result.imported) {
				MessageBox.warning("Inzwischen gibt es einen ähnlichen Kunden in EstrichONE. Bitte öffnen Sie die Übernahme erneut.", { title: TITLE });
				return false;
			}
			showToast(`${contact.name} als Kunde ${result.customerNumber ?? ""} übernommen`.replace("  ", " "));
			return true;
		} catch (error) {
			fail(error);
			return false;
		}
	};
	if (candidates.length === 0) {
		return await ask(`„${contact.name}“ als neuen Kunden in EstrichONE anlegen? Die Prüfung hat keinen ähnlichen Kunden gefunden. `
			+ "Die Kundennummer wird automatisch vergeben.", "Kunde anlegen") && doImport(false);
	}

	return new Promise((resolve) => {
		let done = false;
		const rows: CustomerRow[] = candidates.map((c) => {
			const label = matchLabel(c);
			return { ...c, matchText: label.text, matchState: label.state, place: placeText(c.customer), taken: !!c.customer.linkedLexwareContactId };
		});
		const model = new JSONModel({ rows });
		const table = new Table({
			mode: "SingleSelectLeft",
			columns: [column("Kunde in EstrichONE"), column("Anschrift", { minScreenWidth: "Tablet", demandPopin: true }),
				column("E-Mail", { minScreenWidth: "Desktop", demandPopin: true }), column("Übereinstimmung")],
			items: {
				path: "/rows",
				template: new ColumnListItem({
					cells: [
						new ObjectIdentifier({ title: "{customer/name}", text: "{= ${customer/customerNumber} ? 'Kundennummer ' + ${customer/customerNumber} : '' }" }),
						new Text({ text: "{place}" }), new Text({ text: "{customer/email}" }),
						new VBox({ items: [new ObjectStatus({ text: "{matchText}", state: "{matchState}" }),
							new ObjectStatus({ text: "Bereits mit einem anderen Lexware-Kontakt verknüpft", state: "Warning", visible: "{taken}" })] }),
					],
				}),
			},
		});
		table.setModel(model);
		const finish = (ok: boolean): void => { done = ok; dialog.close(); };
		const dialog = new Dialog({
			title: `${contact.name} nach EstrichONE übernehmen`, ...largeDialogSize(Math.min(window.innerWidth, 1000)), resizable: true, draggable: true,
			content: [new VBox({ items: [
				new MessageStrip({ type: "Warning", showIcon: true,
					text: "Die Prüfung hat ähnliche Kunden in EstrichONE gefunden. Verknüpfen Sie den Lexware-Kontakt mit einem vorhandenen Kunden oder legen Sie ihn bewusst neu an." })
					.addStyleClass("sapUiSmallMarginBottom"),
				table,
			] }).addStyleClass("sapUiSmallMargin")],
			buttons: [
				new Button({ text: "Mit Auswahl verknüpfen", type: "Emphasized", icon: "sap-icon://chain-link", press: () => void (async () => {
					const row = table.getSelectedItem()?.getBindingContext()?.getObject() as CustomerRow | undefined;
					if (!row) { MessageBox.information("Bitte wählen Sie zuerst einen Kunden aus der Liste.", { title: TITLE }); return; }
					if (row.taken) { MessageBox.warning("Dieser Kunde ist bereits mit einem anderen Lexware-Kontakt verknüpft.", { title: TITLE }); return; }
					dialog.setBusy(true);
					try {
						await post<CustomerLexware>(`lexware/customers/${row.customer.id}/link`, { lexwareContactId: contact.id });
						showToast(`${contact.name} mit ${row.customer.name} verknüpft`);
						finish(true);
					} catch (error) {
						fail(error);
					} finally {
						dialog.setBusy(false);
					}
				})() }),
				new Button({ text: "Trotzdem neu anlegen", icon: "sap-icon://add", press: () => void (async () => {
					if (!await ask(createAnywayQuestion(contact.name, candidates.length, "estrichOne"), "Trotzdem neu anlegen")) return;
					dialog.setBusy(true);
					const ok = await doImport(true);
					dialog.setBusy(false);
					if (ok) finish(true);
				})() }),
				new Button({ text: "Abbrechen", press: () => dialog.close() }),
			],
			afterClose: () => { dialog.destroy(); model.destroy(); resolve(done); },
		});
		dialog.open();
	});
}

/** Kunden aus Lexware Office durchsuchen (seitenweise) und einzeln übernehmen. Liefert true, wenn etwas übernommen wurde. */
export function openLexwareImportDialog(): Promise<boolean> {
	return new Promise((resolve) => {
		let changed = false;
		let query = "";
		const pageSize = 50;
		const model = new JSONModel({ rows: [] as (LexwareContactSummary & { place: string })[], total: 0, busy: true, loadedAt: "" });
		const load = async (reset: boolean, refresh = false): Promise<void> => {
			model.setProperty("/busy", true);
			try {
				const have = reset ? [] : model.getProperty("/rows") as LexwareContactSummary[];
				const result = await get<LexwareContactList>("lexware/contacts", { q: query || undefined, page: Math.floor(have.length / pageSize), size: pageSize,
					unlinked: true, refresh: refresh || undefined });
				model.setProperty("/rows", [...have, ...result.items.map((c) => ({ ...c, place: placeText(c) }))]);
				model.setProperty("/total", result.total);
				model.setProperty("/loadedAt", `Stand der Liste aus Lexware Office: ${new Date(result.loadedAt).toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" })} Uhr`);
			} catch (error) {
				fail(error);
			} finally {
				model.setProperty("/busy", false);
			}
		};
		const table = new Table({
			busy: "{/busy}", busyIndicatorDelay: 0, noDataText: "Kein Kunde aus Lexware Office ohne Verknüpfung gefunden.", sticky: ["HeaderToolbar", "ColumnHeaders"],
			headerToolbar: new Toolbar({ content: [
				new Title({ text: "{= ${/total} === 1 ? '1 Kunde ohne Verknüpfung' : ${/total} + ' Kunden ohne Verknüpfung' }", level: "H3" }),
				new ToolbarSpacer(),
				new SearchField({ width: "20rem", placeholder: "Name, Kundennummer, Ort, E-Mail", search: (e) => { query = String(e.getParameter("query") ?? "").trim(); void load(true); } }),
				new Button({ icon: "sap-icon://refresh", tooltip: "Liste neu aus Lexware Office laden", press: () => void load(true, true) }),
			] }),
			columns: [column("Kontakt in Lexware Office"), column("Anschrift", { minScreenWidth: "Tablet", demandPopin: true }),
				column("E-Mail", { minScreenWidth: "Desktop", demandPopin: true }), column("", { width: "9rem", hAlign: "End" })],
			items: {
				path: "/rows",
				template: new ColumnListItem({
					cells: [
						new ObjectIdentifier({ title: "{name}", text: "{= ${customerNumber} ? 'Kundennummer ' + ${customerNumber} : '' }" }),
						new Text({ text: "{place}" }), new Text({ text: "{email}" }),
						new Button({ text: "Übernehmen", icon: "sap-icon://download", press: (e) => void (async () => {
							const row = (e.getSource() as Control).getBindingContext()?.getObject() as LexwareContactSummary;
							if (await importLexwareContact(row)) { changed = true; await load(true); }
						})() }),
					],
				}),
			},
		});
		table.setModel(model);
		const more = new Button({ text: "Weitere laden", visible: "{= ${/rows}.length < ${/total} }", press: () => void load(false) });
		more.setModel(model);
		const stand = new Text({ text: "{/loadedAt}" }).addStyleClass("eoMuted sapUiTinyMarginTop");
		stand.setModel(model);
		const dialog = new Dialog({
			title: "Kunden aus Lexware Office übernehmen", ...largeDialogSize(Math.min(window.innerWidth, 1100)), contentHeight: "70vh", resizable: true, draggable: true,
			content: [new VBox({ items: [
				new Text({ text: "Vor jeder Übernahme prüft EstrichONE, ob es den Kunden schon gibt. Es wird nichts automatisch angelegt oder zusammengeführt." })
					.addStyleClass("sapUiSmallMarginBottom"),
				table, new HBox({ justifyContent: "Center", items: [more] }).addStyleClass("sapUiTinyMarginTop"), stand,
			] }).addStyleClass("sapUiSmallMargin")],
			endButton: new Button({ text: "Schließen", press: () => dialog.close() }),
			afterClose: () => { dialog.destroy(); model.destroy(); resolve(changed); },
		});
		dialog.open();
		void load(true);
	});
}

interface VoucherRow extends LexwareVoucherItem {
	typeText: string; numberText: string; statusText: string; statusState: string; dateText: string; amountText: string; taken: boolean; takenText: string;
}

/**
 * Beleg aus Lexware Office der Bauakte zuordnen: zeigt die Belege des Kunden der Bauakte (Vorschläge zuerst, wenn die
 * Bauaktennummer im Beleg vorkommt) und sucht auf Wunsch über die Belegnummer. Zugeordnet wird nur nach Klick; in Lexware
 * Office ändert sich nichts. Liefert true, wenn etwas zugeordnet wurde.
 */
export function openAssignVoucherDialog(bauakteId: string): Promise<boolean> {
	return new Promise((resolve) => {
		let changed = false;
		const model = new JSONModel({ rows: [] as VoucherRow[], busy: true, searched: false, linked: true, info: "" });
		const load = async (number: string): Promise<void> => {
			model.setProperty("/busy", true);
			try {
				// Ohne Nummer: Belege des Kunden, beim Öffnen frisch aus Lexware Office (ein Aufruf), damit neue Belege sofort erscheinen.
				const result = await get<BauakteVoucherCandidates>(`bauakten/${bauakteId}/lexware/candidates`, number ? { number } : { refresh: true });
				model.setProperty("/rows", result.vouchers.map((v): VoucherRow => {
					const status = voucherStatus(v.status);
					return { ...v, typeText: voucherTypeText(v.type), numberText: voucherNumber(v), statusText: status.text, statusState: status.state,
						dateText: date(v.voucherDate), amountText: euro(v.grossAmount, v.currency), taken: !!v.assignedBauakteId, takenText: assignedText(v, bauakteId) };
				}));
				model.setProperty("/searched", !!number);
				model.setProperty("/linked", result.customerLinked);
				const suggested = result.vouchers.filter((v) => v.suggested).length;
				model.setProperty("/info", number ? "" : !result.customerLinked
					? "Der Kunde der Bauakte ist nicht mit Lexware Office verknüpft. Suchen Sie den Beleg über seine Nummer."
					: suggested > 0 ? `${suggested === 1 ? "Ein Beleg nennt" : `${suggested} Belege nennen`} die Nummer dieser Bauakte und ${suggested === 1 ? "steht" : "stehen"} oben.`
						: "");
			} catch (error) {
				fail(error, "Beleg zuordnen");
			} finally {
				model.setProperty("/busy", false);
			}
		};
		const table = new Table({
			busy: "{/busy}", busyIndicatorDelay: 0, growing: true, growingThreshold: 50,
			noDataText: "{= ${/searched} ? 'Kein Beleg mit dieser Nummer gefunden. Entwürfe haben noch keine Nummer.' : (${/linked} ? 'Zu diesem Kunden gibt es in Lexware Office noch keine Belege.' : 'Bitte geben Sie die vollständige Belegnummer ein.') }",
			columns: [column("Art", { width: "10rem" }), column("Nummer"), column("Datum", { width: "7rem" }), column("Kunde", { minScreenWidth: "Desktop", demandPopin: true }),
				column("Betrag brutto", { hAlign: "End", width: "9rem" }), column("Status"), column("", { width: "9rem", hAlign: "End" })],
			items: {
				path: "/rows",
				template: new ColumnListItem({
					cells: [
						new VBox({ items: [new Text({ text: "{typeText}" }),
							new ObjectStatus({ text: "Nennt diese Bauakte", state: "Success", icon: "sap-icon://sys-enter-2", visible: "{suggested}" })] }),
						new Text({ text: "{numberText}" }), new Text({ text: "{dateText}" }), new Text({ text: "{contactName}" }), new Text({ text: "{amountText}" }),
						new VBox({ items: [new ObjectStatus({ text: "{statusText}", state: "{statusState}" }), new ObjectStatus({ text: "{takenText}", state: "Warning", visible: "{taken}" })] }),
						new Button({ text: "Zuordnen", type: "Emphasized", enabled: "{= !${taken} }", press: (e) => void (async () => {
							const row = (e.getSource() as Control).getBindingContext()?.getObject() as VoucherRow;
							dialog.setBusy(true);
							try {
								await post<LexwareVoucher>(`bauakten/${bauakteId}/lexware/vouchers/assign`, { lexwareId: row.lexwareId, type: row.type });
								changed = true;
								showToast(row.number ? `${row.typeText} ${row.number} zugeordnet` : `${row.typeText} im Entwurf zugeordnet`);
								dialog.close();
							} catch (error) {
								fail(error, "Beleg zuordnen");
							} finally {
								dialog.setBusy(false);
							}
						})() }),
					],
				}),
			},
		});
		table.setModel(model);
		const info = new MessageStrip({ text: "{/info}", type: "Information", showIcon: true, visible: "{= !!${/info} }" }).addStyleClass("sapUiSmallMarginBottom");
		info.setModel(model);
		const dialog = new Dialog({
			title: "Beleg zuordnen", ...largeDialogSize(Math.min(window.innerWidth, 1150)), resizable: true, draggable: true,
			content: [new VBox({ items: [
				new Text({ text: "Rechnungen und Angebote entstehen in Lexware Office. Hier ordnen Sie einen vorhandenen Beleg dieser Bauakte zu; der Beleg selbst wird nicht verändert." })
					.addStyleClass("sapUiSmallMarginBottom"),
				info,
				new SearchField({ width: "100%", placeholder: "Über die Belegnummer suchen, z. B. RE1042 oder AG0311 (leer: Belege des Kunden)",
					search: (e) => void load(String(e.getParameter("query") ?? "").trim()) }),
				table,
			] }).addStyleClass("sapUiSmallMargin")],
			endButton: new Button({ text: "Schließen", press: () => dialog.close() }),
			afterClose: () => { dialog.destroy(); model.destroy(); resolve(changed); },
		});
		dialog.open();
		void load("");
	});
}
