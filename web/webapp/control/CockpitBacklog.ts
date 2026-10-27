import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";
import { icon, startCockpitDrag } from "./Cockpit";

/** Eintrag im Vorrat (vom Controller aufbereitet). */
export interface BacklogCard {
	id: string;
	title: string;
	number: string | null;
	meta: string;
	area: string;
	/** Termin-Hinweis, z. B. „Beginn überfällig (12.10.)“. */
	due: string;
	urgency: "overdue" | "soon" | "later" | "none";
	planned: string;
}

/**
 * Vorrat „Ungeplante Bauvorhaben“ im Cockpit: Karten zum Ziehen auf Kolonne/Tag; Enter bzw. „Einplanen“ öffnet den Dialog.
 * @namespace estrichone.control
 */
export default class CockpitBacklog extends Control {
	static readonly metadata: MetadataOptions = {
		properties: { items: { type: "object", defaultValue: [] }, canPlan: { type: "boolean", defaultValue: false } },
		events: { plan: { parameters: { id: { type: "string" } } }, open: { parameters: { id: { type: "string" } } } },
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: CockpitBacklog): void {
			rm.openStart("div", control);
			rm.class("eoCkBacklog");
			rm.attr("role", "list");
			rm.attr("aria-label", "Ungeplante Bauvorhaben");
			rm.openEnd();
			rm.close("div");
		},
	};

	onAfterRendering(): void {
		const host = this.getDomRef() as HTMLElement | null;
		if (!host) return;
		const items = (this.getProperty("items") ?? []) as BacklogCard[];
		const canPlan = this.getProperty("canPlan") === true;
		host.replaceChildren(...items.map((b) => {
			const card = document.createElement("div");
			card.className = `eoCkBl eoCkBl-${b.urgency}${canPlan ? " eoCkMovable" : ""}`;
			card.setAttribute("role", "listitem");
			card.tabIndex = 0;
			card.dataset.flipKey = `bl:${b.id}`;
			card.addEventListener("dragstart", (e) => e.preventDefault());
			card.setAttribute("aria-label", `${b.title}, ${b.meta}, ${b.due}${canPlan ? ". Auf Kolonne und Tag ziehen oder Enter zum Einplanen." : ""}`);
			const top = document.createElement("div");
			top.className = "eoCkBlTop";
			const title = document.createElement("a");
			title.className = "eoCkBlTitle";
			title.textContent = b.title;
			title.href = `/bauakten/${b.id}`;
			title.draggable = false;
			title.addEventListener("click", (e) => {
				e.preventDefault();
				this.fireEvent("open", { id: b.id });
			});
			top.append(title);
			if (b.area) {
				const area = document.createElement("span");
				area.className = "eoCkBlArea";
				area.textContent = b.area;
				top.append(area);
			}
			const meta = document.createElement("div");
			meta.className = "eoCkBlMeta";
			meta.textContent = b.meta;
			const foot = document.createElement("div");
			foot.className = "eoCkBlFoot";
			const due = document.createElement("span");
			due.className = `eoCkDue eoCkDue-${b.urgency}`;
			due.textContent = b.due;
			foot.append(due);
			if (b.planned) {
				const planned = document.createElement("span");
				planned.className = "eoCkBlPlanned";
				planned.textContent = b.planned;
				foot.append(planned);
			}
			if (canPlan) {
				const plan = document.createElement("button");
				plan.type = "button";
				plan.className = "eoCkBlPlan";
				plan.title = "Einplanen …";
				plan.setAttribute("aria-label", `${b.title} einplanen`);
				plan.append(icon("sap-icon://appointment-2"));
				plan.addEventListener("click", () => this.fireEvent("plan", { id: b.id }));
				foot.append(plan);
				card.addEventListener("pointerdown", (e) => startCockpitDrag(e, card, { kind: "bauakte", id: b.id, label: b.title }));
				card.addEventListener("keydown", (e) => {
					if (e.key === "Enter" && e.target === card) this.fireEvent("plan", { id: b.id });
				});
			}
			card.append(top, meta, foot);
			return card;
		}));
		if (!items.length) {
			const empty = document.createElement("div");
			empty.className = "eoCkBlEmpty";
			empty.textContent = "Alles eingeplant – keine Bauvorhaben in dieser Auswahl.";
			host.append(empty);
		}
	}
}
