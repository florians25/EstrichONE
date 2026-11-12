/**
 * Dialog „Kunde löschen“ (Kundenliste, Kundenakte): lädt die Bezüge des Kunden. Ohne Bezüge: endgültig löschen nach
 * Eintippen der Kundennummer. Mit Bezügen: klare Meldung mit Anzahl je Art und Links, stattdessen Archivieren; mit
 * Recht `contacts.anonymize` zusätzlich Anonymisieren (DSGVO) mit doppelter Bestätigung.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import Link from "sap/m/Link";
import CheckBox from "sap/m/CheckBox";
import MessageStrip from "sap/m/MessageStrip";
import VBox from "sap/m/VBox";
import type Control from "sap/ui/core/Control";
import { ValueState } from "sap/ui/core/library";
import { api, get, post, problemMessage } from "./api";
import { session } from "./session";
import { navTo } from "./navigation";
import { confirmMatches, describeReferences, removalActions, removalMessage, type BulkOutcome, type RemovalRights } from "./customerRemoval";
import type { ContactResponse, CustomerReferencesResponse } from "./models";

export type RemovalResult = { action: "deleted" | "archived" | "reactivated" | "anonymized"; contact?: ContactResponse } | null;

export function removalRights(): RemovalRights {
	return { delete: session.has("contacts.delete"), edit: session.has("contacts.edit"), anonymize: session.has("contacts.anonymize") };
}

export const deleteCustomer = (id: string, confirm: string) =>
	api<void>(`contacts/${id}`, { method: "DELETE", query: { confirm } });
export const archiveCustomer = (id: string) => post<ContactResponse>(`contacts/${id}/archive`);
export const reactivateCustomer = (id: string) => post<ContactResponse>(`contacts/${id}/reactivate`);

/** Treffer der Dublettenprüfung übernehmen: archivierte Kunden vorher reaktivieren (der Server lehnt sie sonst ab). */
export const ensureActive = async (contact: ContactResponse): Promise<ContactResponse> =>
	contact.archivedAt ? reactivateCustomer(contact.id) : contact;

function referenceList(refs: CustomerReferencesResponse, close: () => void): Control {
	const box = new VBox({ renderType: "Bare" }).addStyleClass("sapUiSmallMarginTop eoRemovalRefs");
	for (const ref of refs.references) {
		const head = ref.link
			? new Link({ text: `${ref.count} ${ref.label}`, press: () => { close(); navTo(ref.link!); } }).addStyleClass("eoStrong")
			: new Text({ text: `${ref.count} ${ref.label}` }).addStyleClass("eoStrong");
		box.addItem(head);
		for (const item of ref.items) {
			box.addItem(item.link
				? new Link({ text: `– ${item.text}`, press: () => { close(); navTo(item.link!); } }).addStyleClass("sapUiSmallMarginBegin")
				: new Text({ text: `– ${item.text}` }).addStyleClass("sapUiSmallMarginBegin"));
		}
		if (ref.count > ref.items.length) {
			box.addItem(new Text({ text: `… und ${ref.count - ref.items.length} weitere` }).addStyleClass("sapUiSmallMarginBegin eoMuted"));
		}
	}
	return box;
}

/** Bestätigungsfeld „Zum Bestätigen … eintippen“; ruft `changed` mit dem Ergebnis der Prüfung. */
function confirmField(id: string, refs: CustomerReferencesResponse, changed: (ok: boolean) => void): { controls: Control[]; input: Input } {
	const what = refs.customerNumber ? `die Kundennummer ${refs.confirmText}` : `den Namen „${refs.confirmText}“`;
	const input = new Input(id, {
		width: "100%", placeholder: refs.confirmText, valueLiveUpdate: true,
		liveChange: () => {
			const ok = confirmMatches(input.getValue(), refs.confirmText);
			input.setValueState(ok || !input.getValue() ? ValueState.None : ValueState.Warning);
			input.setValueStateText(ok ? "" : "Stimmt noch nicht überein.");
			changed(ok);
		},
	});
	return { controls: [new Label({ text: `Zum Bestätigen ${what} eintippen`, labelFor: input, wrapping: true }).addStyleClass("sapUiSmallMarginTop"), input], input };
}

/**
 * Löschen bzw. Archivieren eines Kunden. Liefert das Ergebnis oder `null` (abgebrochen).
 */
export async function openCustomerRemoval(contact: Pick<ContactResponse, "id" | "name">): Promise<RemovalResult> {
	const refs = await get<CustomerReferencesResponse>(`contacts/${contact.id}/references`);
	const actions = removalActions(refs, removalRights());
	return new Promise((resolve) => {
		let result: RemovalResult = null;
		let anonymizing = false;
		const error = new MessageStrip({ type: "Error", showIcon: true, visible: false }).addStyleClass("sapUiSmallMarginTop");
		const fail = (e: unknown) => { error.setText(problemMessage(e)); error.setVisible(true); };
		const done = (r: RemovalResult) => { result = r; dialog.close(); };
		const content: Control[] = [
			new MessageStrip({ type: refs.deletable ? "Warning" : "Information", showIcon: true, text: removalMessage(refs) }),
		];
		if (!refs.deletable) content.push(referenceList(refs, () => dialog.close()));

		const buttons: Button[] = [];
		if (actions.includes("delete")) {
			// Nur erzeugen, wenn es in den Dialog kommt (feste ID; der Dialog zerstört beim Schließen nur seinen Inhalt).
			const deleteButton = new Button("crDelete", { text: "Endgültig löschen", type: "Reject", icon: "sap-icon://delete", enabled: false });
			const field = confirmField("crConfirm", refs, (ok) => deleteButton.setEnabled(ok));
			content.push(...field.controls);
			deleteButton.attachPress(async () => {
				deleteButton.setEnabled(false);
				try {
					await deleteCustomer(refs.id, field.input.getValue().trim());
					done({ action: "deleted" });
				} catch (e) {
					fail(e);
					deleteButton.setEnabled(true);
				}
			});
			buttons.push(deleteButton);
		} else if (refs.deletable) {
			content.push(new Text({ text: "Zum Löschen fehlt die Berechtigung „Kunden löschen“. Archivieren ist möglich." }).addStyleClass("sapUiSmallMarginTop eoMuted"));
		}
		if (actions.includes("archive")) {
			buttons.push(new Button("crArchive", {
				text: refs.deletable ? "Stattdessen archivieren" : "Archivieren", icon: "sap-icon://inbox",
				type: refs.deletable ? "Default" : "Emphasized",
				press: async () => {
					try { done({ action: "archived", contact: await archiveCustomer(refs.id) }); } catch (e) { fail(e); }
				},
			}));
		}
		if (actions.includes("reactivate")) {
			content.push(new Text({ text: "Der Kunde ist bereits archiviert." }).addStyleClass("sapUiSmallMarginTop eoMuted"));
			buttons.push(new Button("crReactivate", {
				text: "Reaktivieren", icon: "sap-icon://undo",
				press: async () => {
					try { done({ action: "reactivated", contact: await reactivateCustomer(refs.id) }); } catch (e) { fail(e); }
				},
			}));
		}
		if (actions.includes("anonymize")) {
			buttons.push(new Button("crAnonymize", {
				text: "Anonymisieren …", icon: "sap-icon://hide",
				press: () => {
					anonymizing = true;
					dialog.close();
					void openAnonymize(refs).then(resolve);
				},
			}));
		}
		content.push(error);
		const cancel = new Button("crCancel", { text: buttons.length ? "Abbrechen" : "Schließen", press: () => dialog.close() });
		const dialog: Dialog = new Dialog("crDialog", {
			title: refs.deletable ? "Kunde löschen" : "Kunde kann nicht gelöscht werden", contentWidth: "34rem", type: "Message",
			state: refs.deletable ? ValueState.Warning : ValueState.Information,
			content: [new VBox({ items: content, renderType: "Bare" })],
			buttons: [...buttons, cancel],
			// Ohne Löschmöglichkeit nicht auf einem Link der Bezüge starten, sondern auf der empfohlenen Aktion.
			initialFocus: actions.includes("delete") ? undefined : (buttons[0] ?? cancel),
			afterClose: () => {
				dialog.destroy();
				// Anonymisieren öffnet einen eigenen Dialog und meldet sein Ergebnis selbst.
				if (!anonymizing) resolve(result);
			},
		});
		dialog.open();
	});
}

/** Zweite Stufe: Anonymisieren (DSGVO) – Kundennummer eintippen und Unwiderruflichkeit bestätigen. */
function openAnonymize(refs: CustomerReferencesResponse): Promise<RemovalResult> {
	return new Promise((resolve) => {
		let result: RemovalResult = null;
		const go = new Button("caGo", { text: "Unwiderruflich anonymisieren", type: "Reject", icon: "sap-icon://hide", enabled: false });
		let typed = false;
		const irreversible = new CheckBox("caIrreversible", {
			text: "Mir ist bewusst, dass Name, Anschrift, Telefon, E-Mail, Notiz und Ansprechpartner unwiderruflich ersetzt werden.",
			wrapping: true, select: () => go.setEnabled(typed && irreversible.getSelected()),
		});
		const field = confirmField("caConfirm", refs, (ok) => { typed = ok; go.setEnabled(ok && irreversible.getSelected()); });
		const error = new MessageStrip({ type: "Error", showIcon: true, visible: false }).addStyleClass("sapUiSmallMarginTop");
		go.attachPress(async () => {
			go.setEnabled(false);
			try {
				result = { action: "anonymized", contact: await post<ContactResponse>(`contacts/${refs.id}/anonymize`, { confirm: field.input.getValue().trim(), irreversible: true }) };
				dialog.close();
			} catch (e) {
				error.setText(problemMessage(e));
				error.setVisible(true);
				go.setEnabled(true);
			}
		});
		const dialog: Dialog = new Dialog("caDialog", {
			title: "Kunde anonymisieren (DSGVO)", contentWidth: "34rem", type: "Message", state: ValueState.Error,
			content: [new VBox({
				renderType: "Bare",
				items: [
					new MessageStrip({
						type: "Error", showIcon: true,
						text: "Name und alle Kontaktdaten des Kunden werden durch „Anonymisierter Kunde"
							+ (refs.customerNumber ? ` ${refs.customerNumber}` : "") + "“ ersetzt – auch in den Projektbeteiligten der Bauakten "
							+ "und in Anrufnotizen. Bauakten, Dokumente und Kundennummer bleiben erhalten. Das lässt sich nicht rückgängig machen.",
					}),
					...field.controls, irreversible.addStyleClass("sapUiSmallMarginTop"), error,
				],
			})],
			buttons: [go, new Button({ text: "Abbrechen", press: () => dialog.close() })],
			afterClose: () => { dialog.destroy(); resolve(result); },
		});
		dialog.open();
	});
}

/**
 * Mehrfachauswahl in der Kundenliste: Kunden ohne Bezüge werden gelöscht (mit Recht), alle anderen archiviert. Vorher
 * zeigt der Dialog die Aufteilung; bestätigt wird durch Eintippen der Anzahl.
 */
export async function openBulkRemoval(contacts: Pick<ContactResponse, "id" | "name">[]): Promise<BulkOutcome[] | null> {
	const rights = removalRights();
	const checked = await Promise.all(contacts.map(async (c) => ({ contact: c, refs: await get<CustomerReferencesResponse>(`contacts/${c.id}/references`) })));
	const toDelete = checked.filter((x) => x.refs.deletable && rights.delete);
	const toArchive = checked.filter((x) => !toDelete.includes(x) && !x.refs.archived && rights.edit);
	const skipped = checked.filter((x) => !toDelete.includes(x) && !toArchive.includes(x));
	const total = toDelete.length + toArchive.length;
	return new Promise((resolve) => {
		let outcome: BulkOutcome[] | null = null;
		const go = new Button("cbGo", { text: "Ausführen", type: "Reject", enabled: false });
		// Ohne feste ID: das Feld kommt nur in den Dialog, wenn es etwas zu bestätigen gibt.
		const field = new Input({
			width: "100%", placeholder: String(total), valueLiveUpdate: true,
			liveChange: () => go.setEnabled(total > 0 && confirmMatches(field.getValue(), String(total))),
		});
		const lines = [
			toDelete.length ? `${toDelete.length} ohne verknüpfte Daten werden endgültig gelöscht: ${toDelete.map((x) => x.contact.name).join(", ")}.` : "",
			toArchive.length ? `${toArchive.length} mit Bauakten, Anrufen o. ä. werden archiviert: ${toArchive.map((x) => `${x.contact.name} (${describeReferences(x.refs.references) || "keine Löschberechtigung"})`).join(", ")}.` : "",
			skipped.length ? `${skipped.length} bleiben unverändert (bereits archiviert oder keine Berechtigung).` : "",
		].filter(Boolean);
		go.attachPress(async () => {
			go.setEnabled(false);
			const results: BulkOutcome[] = skipped.map((x) => ({ name: x.contact.name, action: "skipped" }));
			for (const x of toDelete) {
				try { await deleteCustomer(x.refs.id, x.refs.confirmText); results.push({ name: x.contact.name, action: "deleted" }); } catch (e) { results.push({ name: x.contact.name, action: "failed", reason: problemMessage(e) }); }
			}
			for (const x of toArchive) {
				try { await archiveCustomer(x.refs.id); results.push({ name: x.contact.name, action: "archived" }); } catch (e) { results.push({ name: x.contact.name, action: "failed", reason: problemMessage(e) }); }
			}
			outcome = results;
			dialog.close();
		});
		const dialog: Dialog = new Dialog("cbDialog", {
			title: `${contacts.length} Kunden löschen oder archivieren`, contentWidth: "36rem", type: "Message", state: ValueState.Warning,
			content: [new VBox({
				renderType: "Bare",
				items: [
					...lines.map((t) => new Text({ text: t }).addStyleClass("sapUiTinyMarginBottom")),
					...(total ? [new Label({ text: `Zum Bestätigen die Anzahl ${total} eintippen`, labelFor: field }).addStyleClass("sapUiSmallMarginTop"), field] : []),
				],
			})],
			buttons: [go, new Button({ text: "Abbrechen", press: () => dialog.close() })],
			afterClose: () => { dialog.destroy(); if (!total) field.destroy(); resolve(outcome); },
		});
		dialog.open();
	});
}
