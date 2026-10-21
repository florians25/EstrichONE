/**
 * „Letzte Aktivitäten“ der Bauakte im eigenen Fenster: wer hat wann was geändert (Feldänderungen alt → neu),
 * filterbar nach Art, Person und Suchbegriff, nach Tagen gruppiert.
 */
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import List from "sap/m/List";
import CustomListItem from "sap/m/CustomListItem";
import GroupHeaderListItem from "sap/m/GroupHeaderListItem";
import HBox from "sap/m/HBox";
import VBox from "sap/m/VBox";
import Text from "sap/m/Text";
import Avatar from "sap/m/Avatar";
import Select from "sap/m/Select";
import SearchField from "sap/m/SearchField";
import FlexBox from "sap/m/FlexBox";
import Item from "sap/ui/core/Item";
import JSONModel from "sap/ui/model/json/JSONModel";
import Sorter from "sap/ui/model/Sorter";
import { akteHeader, type AkteContext } from "./akteHeader";
import { longDay } from "./format";
import type { BauakteHistoryEntry } from "./models";

/** Art einer Aktivität (für den Filter) aus dem Aktionsschlüssel. */
export function activityKind(action: string): string {
	const a = action.replace(/^bauakte\./, "");
	if (a === "updated" || a === "created" || a === "status_changed" || a === "members_changed" || a === "deleted") return "akte";
	if (a.startsWith("file") || a.startsWith("document") || a.includes("approval")) return "files";
	if (a.startsWith("note")) return "notes";
	if (a.startsWith("diary") || a.startsWith("inspection")) return "documentation";
	if (a.startsWith("workitem")) return "work";
	if (a.startsWith("room")) return "rooms";
	return "other";
}

const kinds: { key: string; text: string }[] = [
	{ key: "", text: "Alle Aktivitäten" }, { key: "akte", text: "Akte (Daten, Status, Team)" }, { key: "files", text: "Dokumente und Fotos" },
	{ key: "documentation", text: "Dokumentation und Prüfungen" }, { key: "work", text: "Aufgaben und Mängel" }, { key: "rooms", text: "Räume" },
	{ key: "notes", text: "Notizen" }, { key: "other", text: "Sonstiges" },
];

const initials = (name: string | null) => (name ?? "?").split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join("") || "?";

export function openActivityDialog(entries: BauakteHistoryEntry[], context: AkteContext | null): Dialog {
	const rows = entries.map((e) => ({
		...e,
		kind: activityKind(e.action),
		day: e.at.slice(0, 10),
		time: new Date(e.at).toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" }),
		who: e.actorName ?? "System",
		initials: initials(e.actorName),
		changeLines: (e.changes ?? []).map((c) => `${c.label}: ${c.from ?? "–"} → ${c.to ?? "–"}`).join("\n"),
	})).sort((a, b) => b.at.localeCompare(a.at));
	const people = [...new Set(rows.map((r) => r.who))].sort();
	const model = new JSONModel({ rows, shown: rows, kind: "", person: "", query: "" });
	model.setSizeLimit(10000);

	const apply = () => {
		const kind = model.getProperty("/kind") as string;
		const person = model.getProperty("/person") as string;
		const query = String(model.getProperty("/query") ?? "").trim().toLowerCase();
		model.setProperty("/shown", rows.filter((r) => (!kind || r.kind === kind) && (!person || r.who === person)
			&& (!query || `${r.text} ${r.changeLines} ${r.who}`.toLowerCase().includes(query))));
	};

	const list = new List({
		noDataText: "Keine Aktivitäten für diese Auswahl.", growing: true, growingThreshold: 100,
		items: {
			path: "/shown",
			sorter: new Sorter("day", true, (ctx: { getProperty(p: string): unknown } | undefined) => ({ key: String(ctx?.getProperty("day") ?? "") })),
			groupHeaderFactory: (group: { key: string }) => new GroupHeaderListItem({ title: longDay(group.key) }),
			template: new CustomListItem({
				content: [new HBox({ alignItems: "Start", items: [
					new Avatar({ initials: "{initials}", displaySize: "XS", tooltip: "{who}" }).addStyleClass("sapUiSmallMarginEnd"),
					new VBox({ items: [
						new Text({ text: "{time} · {who}" }).addStyleClass("eoMuted eoTabular"),
						new Text({ text: "{text}", wrapping: true }),
						new Text({ text: "{changeLines}", visible: "{= !!${changeLines} }", wrapping: true }).addStyleClass("eoPre eoChanges"),
					] }),
				] }).addStyleClass("sapUiTinyMarginTopBottom")],
			}),
		},
	});

	const kind = new Select({ selectedKey: "{/kind}", change: apply, items: kinds.map((k) => new Item({ key: k.key, text: k.text })) });
	kind.setTooltip("Art der Aktivität");
	const person = new Select({ selectedKey: "{/person}", change: apply,
		items: [new Item({ key: "", text: "Alle Personen" }), ...people.map((p) => new Item({ key: p, text: p }))] });
	person.setTooltip("Person");
	const search = new SearchField({ value: "{/query}", placeholder: "Suchen (z. B. Feldname, Datei)", width: "16rem", liveChange: () => setTimeout(apply, 0) });

	const dialog = new Dialog({
		title: "Letzte Aktivitäten", contentWidth: "48rem", contentHeight: "75vh", resizable: true, draggable: true,
		content: [new VBox({ items: [
			...akteHeader(context),
			new FlexBox({ wrap: "Wrap", alignItems: "Center", items: [kind, person, search] }).addStyleClass("eoEntryRow sapUiSmallMarginBottom"),
			list,
		] }).addStyleClass("sapUiSmallMargin")],
		endButton: new Button({ text: "Schließen", press: () => dialog.close() }),
		afterClose: () => dialog.destroy(),
	});
	dialog.setModel(model);
	dialog.open();
	return dialog;
}
