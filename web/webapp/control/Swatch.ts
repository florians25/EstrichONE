import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";

/**
 * Farbfeld (z. B. Kolonnenfarbe). Die Farbe wird nach dem Rendern per CSSOM gesetzt,
 * weil die CSP keine Inline-Style-Attribute im Markup zulässt.
 * @namespace estrichone.control
 */
export default class Swatch extends Control {
	static readonly metadata: MetadataOptions = {
		properties: {
			color: { type: "string", defaultValue: "" },
			/** "swatch" = kleines Quadrat, "bar" = senkrechter Balken am linken Rand der Elternkarte. */
			shape: { type: "string", defaultValue: "swatch" },
		},
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: Swatch): void {
			rm.openStart("span", control);
			rm.class(control.getProperty("shape") === "bar" ? "eoColorBar" : "eoSwatch");
			rm.attr("aria-hidden", "true");
			rm.openEnd();
			rm.close("span");
		},
	};

	onAfterRendering(): void {
		const el = this.getDomRef() as HTMLElement | null;
		const color = String(this.getProperty("color") || "");
		if (el) el.style.background = /^#[0-9a-fA-F]{3,8}$/.test(color) ? color : "var(--sapNeutralBorderColor)";
	}
}
