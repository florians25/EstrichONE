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
 */
import SimpleForm from "sap/ui/layout/form/SimpleForm";
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

/** Einspaltig, sofern ein bearbeitbares Formular die Spaltenzahl nicht ausdrücklich festlegt. */
function singleColumn(form: SimpleForm): void {
	if (!form.getEditable() || form.getLayout() !== "ResponsiveGridLayout") return;
	if (form.isPropertyInitial("columnsL") && form.isPropertyInitial("columnsXL")) form.setColumnsL(1);
}

/** Einmalig beim Start (Component.init) aufrufen. */
export function installFormLayout(): void {
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
		return result;
	};
}
