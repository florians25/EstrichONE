/**
 * Einheitliches Formular-Layout für alle SimpleForms (Dialoge, Assistenten, Seiten).
 *
 * Ursache verschobener Abschnittsüberschriften: In einer SimpleForm beginnt nur ein `sap.ui.core.Title` (oder eine
 * Toolbar) einen neuen Abschnitt mit Überschrift über die volle Breite. Ein `sap.m.Title` als Inhalt ist für die
 * SimpleForm ein gewöhnliches Feld ohne Beschriftung und wird im ResponsiveGridLayout in die freie Spalte rechts
 * neben das vorige Feld gesetzt (z. B. „Estrich“ neben „Priorität“). Zusätzlich verteilt das ResponsiveGridLayout
 * mehrere Abschnitte ab Breite „L“ standardmäßig auf zwei Spalten, sodass Überschriften nebeneinander stehen.
 *
 * Abhilfe zentral an der SimpleForm: `sap.m.Title` als Inhalt wird beim Hinzufügen in einen Abschnittstitel
 * (`sap.ui.core.Title`) umgewandelt, und bearbeitbare Formulare ohne ausdrückliche Spaltenzahl sind einspaltig.
 * Neuer Code verwendet direkt {@link formTitle}.
 *
 * Breite: Das ResponsiveGridLayout richtet sich nach der Breite des Formulars (Container-Query), nicht des Fensters.
 * Bei Windows-Laptops mit 125/150 % Skalierung bleiben neben Navigation und Bereichsliste oft nur 500–700 px. Damit
 * Beschriftungen dort nicht in einer schmalen, weit eingerückten Spalte neben abgeschnittenen Feldern stehen, gilt zentral:
 * - feste Breiten (z. B. `width="56rem"`) werden zur Höchstbreite, das Formular passt sich der verfügbaren Breite an;
 * - in Eingabeformularen stehen Beschriftungen unter 720 px Formularbreite über den Feldern (Standard wäre 600 px);
 */
import SimpleForm from "sap/ui/layout/form/SimpleForm";
import Table from "sap/m/Table";
import CoreTitle from "sap/ui/core/Title";
import MTitle from "sap/m/Title";
import Log from "sap/base/Log";
import type UI5Element from "sap/ui/core/Element";
import type Control from "sap/ui/core/Control";
import type { PropertyBindingInfo } from "sap/ui/base/ManagedObject";

/**
 * Abschnittsüberschrift für eine SimpleForm: beginnt einen neuen Abschnitt, linksbündig über die volle Breite.
 * Als Control typisiert, damit sie in Inhaltslisten (Control[]) der SimpleForm passt.
 */
export function formTitle(text: string): Control {
	return new CoreTitle({ text }) as unknown as Control;
}

/** Wandelt einen `sap.m.Title` in einen Abschnittstitel um; alle anderen Elemente bleiben unverändert. */
export function asGroupTitle<T extends UI5Element>(element: T): T | CoreTitle {
	if (!(element instanceof MTitle) || !element.getVisible()) return element;
	const title = new CoreTitle();
	const info = element.getBindingInfo("text") as { parts?: { path?: string; model?: string }[]; formatter?: PropertyBindingInfo["formatter"] } | undefined;
	if (info?.parts?.length) {
		title.bindProperty("text", { parts: info.parts.map((p) => ({ path: p.path ?? "", model: p.model })), formatter: info.formatter });
	} else {
		title.setText(element.getText());
	}
	const tooltip = element.getTooltip_AsString();
	if (tooltip) title.setTooltip(tooltip);
	Log.warning(`sap.m.Title „${element.getText()}“ in SimpleForm als Abschnittstitel übernommen – bitte formTitle() verwenden.`, "", "estrichone.formLayout");
	return title;
}

type Patchable = {
	addContent: (this: SimpleForm, element: UI5Element) => SimpleForm;
	insertContent: (this: SimpleForm, element: UI5Element, index: number) => SimpleForm;
	applySettings: (this: SimpleForm, settings: object, scope?: object) => SimpleForm;
	__eoFormLayout?: boolean;
};

/** Ab dieser Formularbreite (px) stehen Beschriftungen neben den Feldern, darunter über ihnen. */
export const LABELS_BESIDE_FROM = 720;

/** Einspaltig, sofern ein bearbeitbares Formular die Spaltenzahl nicht ausdrücklich festlegt. */
function singleColumn(form: SimpleForm): void {
	if (!form.getEditable() || form.getLayout() !== "ResponsiveGridLayout") return;
	if (form.isPropertyInitial("columnsL") && form.isPropertyInitial("columnsXL")) form.setColumnsL(1);
}

/** Anpassung an die verfügbare Breite (siehe Kopfkommentar). */
function fitWidth(form: SimpleForm): void {
	form.addStyleClass("eoForm");
	// Nur Eingabeformulare: Anzeigeformulare (z. B. Kennwerte der Bauakte) verteilen sich auf mehrere Spalten.
	if (form.getEditable() && form.getLayout() === "ResponsiveGridLayout" && form.isPropertyInitial("breakpointM")) form.setBreakpointM(LABELS_BESIDE_FROM);
	const width = form.getWidth();
	if (width && !/%$/.test(width) && width !== "auto") {
		form.setWidth("100%");
		form.addEventDelegate({ onAfterRendering: () => { const dom = form.getDomRef() as HTMLElement | null; if (dom) dom.style.maxWidth = width; } });
	}
}

/**
 * Tabellen (sap.m.Table): Spalten mit `minScreenWidth`/`demandPopin` richten sich standardmäßig nach der Fensterbreite.
 * Steht eine Tabelle neben einem Ordnerbaum, in einer Spalte des FlexibleColumnLayout oder bei Skalierung 125/150 % in
 * einem schmalen Bereich, bleiben dann alle Spalten nebeneinander und Texte werden zeichenweise umbrochen oder
 * überlagert. Mit `contextualWidth="Auto"` entscheidet die tatsächliche Breite der Tabelle (wie die Formulare).
 */
function installTableLayout(): void {
	const proto = Table.prototype as unknown as { applySettings: (this: Table, settings: object, scope?: object) => Table; __eoTableLayout?: boolean };
	if (proto.__eoTableLayout) return;
	proto.__eoTableLayout = true;
	const apply = proto.applySettings;
	proto.applySettings = function (settings, scope) {
		const result = apply.call(this, settings, scope);
		if (this.isPropertyInitial("contextualWidth")) this.setContextualWidth("Auto");
		return result;
	};
}

/** Einmalig beim Start (Component.init) aufrufen. */
export function installFormLayout(): void {
	installTableLayout();
	const proto = SimpleForm.prototype as unknown as Patchable;
	if (proto.__eoFormLayout) return;
	proto.__eoFormLayout = true;
	const add = proto.addContent;
	const insert = proto.insertContent;
	const apply = proto.applySettings;
	proto.addContent = function (element) { return add.call(this, asGroupTitle(element)); };
	proto.insertContent = function (element, index) { return insert.call(this, asGroupTitle(element), index); };
	proto.applySettings = function (settings, scope) {
		const result = apply.call(this, settings, scope);
		singleColumn(this);
		fitWidth(this);
		return result;
	};
}
