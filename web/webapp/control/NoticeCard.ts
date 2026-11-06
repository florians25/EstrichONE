import Control from "sap/ui/core/Control";
import type { $ControlSettings } from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";

/**
 * Karte für einen Systemhinweis (Wartung, Störung, Information): farbiger Rand und Symbol je Art, Titel mit relativer
 * Angabe („in 2 Tagen“, „läuft seit 10 Min.“), Zeitraum und Kurztext; rechts „Details“ und – falls erlaubt – „Ausblenden“.
 * Darstellungen: <c>banner</c> (eine kompakte Zeile Text, in der Shell), <c>tile</c> (Startseite, zwei Zeilen Text),
 * <c>full</c> (Details-Dialog und Vorschau, vollständiger Text ohne Schaltflächen).
 * Farben kommen ausschließlich aus den Theme-Variablen (hell, dunkel, hoher Kontrast).
 * @namespace estrichone.control
 */
export interface NoticeCardSettings {
	kind?: string; tone?: string; icon?: string; label?: string; title?: string; text?: string; period?: string; relative?: string;
	remaining?: string; source?: string; dismissible?: boolean; showDetails?: boolean; mode?: string;
	details?: () => void; dismiss?: () => void;
}

export default class NoticeCard extends Control {
	// UI5 ruft beim Klonen (Listenvorlagen) new C(id, settings) auf – beide Formen weiterreichen.
	constructor(idOrSettings?: string | NoticeCardSettings, settings?: NoticeCardSettings) {
		if (typeof idOrSettings === "string") super(idOrSettings, settings as $ControlSettings);
		else super(idOrSettings as $ControlSettings);
	}

	/** Alle Anzeigeeigenschaften auf einmal setzen (Vorschau im Bearbeiten-Dialog). */
	show(settings: NoticeCardSettings): void {
		this.applySettings(settings as $ControlSettings);
	}

	static readonly metadata: MetadataOptions = {
		properties: {
			kind: { type: "string", defaultValue: "info" },
			tone: { type: "string", defaultValue: "Information" },
			icon: { type: "string", defaultValue: "sap-icon://message-information" },
			label: { type: "string", defaultValue: "" },
			title: { type: "string", defaultValue: "" },
			text: { type: "string", defaultValue: "" },
			period: { type: "string", defaultValue: "" },
			relative: { type: "string", defaultValue: "" },
			remaining: { type: "string", defaultValue: "" },
			source: { type: "string", defaultValue: "" },
			dismissible: { type: "boolean", defaultValue: false },
			showDetails: { type: "boolean", defaultValue: true },
			mode: { type: "string", defaultValue: "banner" },
		},
		events: { details: {}, dismiss: {} },
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, c: NoticeCard): void {
			const p = (name: string) => String(c.getProperty(name) ?? "");
			const mode = p("mode");
			const tone = ["Information", "Warning", "Error"].includes(p("tone")) ? p("tone") : "Information";
			const title = p("title");
			const label = p("label");
			rm.openStart("div", c);
			rm.class("eoNoticeCard").class(`eoNoticeCard--${tone}`).class(`eoNoticeCard--${mode}`).class(`eoNoticeKind-${p("kind")}`);
			rm.attr("role", "group");
			rm.attr("aria-label", label && label !== title ? `${label}: ${title}` : title);
			rm.openEnd();

			rm.openStart("span").class("eoNoticeIcon").attr("aria-hidden", "true").openEnd();
			rm.icon(p("icon"), [], { "aria-hidden": "true" });
			rm.close("span");

			rm.openStart("div").class("eoNoticeBody").openEnd();
			if (mode === "full" && label && label !== title) {
				rm.openStart("div").class("eoNoticeLabel").openEnd().text(label).close("div");
			}
			rm.openStart("div").class("eoNoticeHead").openEnd();
			rm.openStart("span").class("eoNoticeTitle").attr("title", mode === "banner" ? title : null).openEnd().text(title).close("span");
			for (const chip of [p("relative"), p("remaining")].filter(Boolean)) {
				rm.openStart("span").class("eoNoticeChip").openEnd().text(chip).close("span");
			}
			rm.close("div");

			const period = p("period");
			const text = p("text");
			if (mode === "banner") {
				// Eine Zeile: Zeitraum · Text (abgeschnitten, vollständig über „Details“ bzw. Tooltip).
				if (period || text) {
					rm.openStart("div").class("eoNoticeLine").attr("title", [period, text].filter(Boolean).join(" · ")).openEnd();
					if (period) rm.openStart("span").class("eoNoticePeriod").openEnd().text(period).close("span");
					if (period && text) rm.openStart("span").class("eoNoticeSep").attr("aria-hidden", "true").openEnd().text("·").close("span");
					if (text) rm.openStart("span").class("eoNoticeText").openEnd().text(text).close("span");
					rm.close("div");
				}
			} else {
				if (period) {
					rm.openStart("div").class("eoNoticeWhen").openEnd();
					rm.icon("sap-icon://appointment-2", ["eoNoticeWhenIcon"], { "aria-hidden": "true" });
					rm.openStart("span").class("eoNoticePeriod").openEnd().text(period).close("span");
					rm.close("div");
				}
				if (text) rm.openStart("div").class("eoNoticeText").openEnd().text(text).close("div");
				if (mode === "full" && p("source")) {
					rm.openStart("div").class("eoNoticeSource").openEnd().text(`Hinweis von: ${p("source")}`).close("div");
				}
			}
			rm.close("div");

			if (mode !== "full") {
				rm.openStart("div").class("eoNoticeActions").openEnd();
				if (c.getProperty("showDetails")) {
					rm.openStart("button").class("eoNoticeBtn").attr("type", "button").attr("data-eo-action", "details")
						.attr("aria-label", `Details: ${title}`).openEnd().text("Details").close("button");
				}
				if (c.getProperty("dismissible")) {
					rm.openStart("button").class("eoNoticeBtn").class("eoNoticeClose").attr("type", "button").attr("data-eo-action", "dismiss")
						.attr("aria-label", `Ausblenden: ${title}`).attr("title", "Ausblenden").openEnd();
					rm.icon("sap-icon://decline", [], { "aria-hidden": "true" });
					rm.close("button");
				}
				rm.close("div");
			}
			rm.close("div");
		},
	};

	onclick(event: { target: EventTarget | null }): void {
		const target = (event.target as HTMLElement | null)?.closest?.("[data-eo-action]");
		const action = target?.getAttribute("data-eo-action");
		if (action === "details") this.fireEvent("details");
		else if (action === "dismiss") this.fireEvent("dismiss");
	}
}
