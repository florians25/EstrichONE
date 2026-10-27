import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";
import { icon, startCockpitDrag } from "./Cockpit";

export type Urgency = "overdue" | "soon" | "later" | "none";

/** Eintrag im Vorrat (vom Controller aufbereitet). */
export interface BacklogCard {
	id: string;
	title: string;
	number: string | null;
	meta: string;
	area: string;
	/** Termin-Hinweis, z. B. „Beginn überfällig (12.10.)“. */
	due: string;
	urgency: Urgency;
	planned: string;
}

const groups: { key: Urgency; text: string; chip: string }[] = [
	{ key: "overdue", text: "Beginn überfällig", chip: "Überfällig" },
	{ key: "soon", text: "Beginn in den nächsten 7 Tagen", chip: "7 Tage" },
	{ key: "later", text: "Später", chip: "Später" },
	{ key: "none", text: "Ohne Termin", chip: "Ohne Termin" },
];

/**
 * Vorrat „Ungeplante Bauvorhaben“ im Cockpit: Filter-Chips nach Dringlichkeit (mit Anzahl), Liste (bei Sortierung nach
 * Dringlichkeit gruppiert), Einträge zum Ziehen auf Kolonne/Tag; Enter bzw. „Einplanen“ öffnet den Dialog.
 * @namespace estrichone.control
 */
export default class CockpitBacklog extends Control {
	static readonly metadata: MetadataOptions = {
		properties: {
			items: { type: "object", defaultValue: [] }, canPlan: { type: "boolean", defaultValue: false },
			/** Anzahl je Dringlichkeit (vor dem Dringlichkeitsfilter). */
			counts: { type: "object", defaultValue: {} },
			/** Aktive Dringlichkeitsfilter (leer = alle). */
			urgency: { type: "object", defaultValue: [] },
			grouped: { type: "boolean", defaultValue: false },
		},
		events: {
			plan: { parameters: { id: { type: "string" } } }, open: { parameters: { id: { type: "string" } } },
			urgencyChange: { parameters: { urgency: { type: "object" } } },
		},
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: CockpitBacklog): void {
			rm.openStart("div", control);
			rm.class("eoCkBacklogHost");
			rm.openEnd();
			rm.close("div");
		},
	};

	onAfterRendering(): void {
		const host = this.getDomRef() as HTMLElement | null;
		if (!host) return;
		const list = document.createElement("div");
		list.className = "eoCkBacklog";
		list.setAttribute("role", "list");
		list.setAttribute("aria-label", "Ungeplante Bauvorhaben");
		const old = host.querySelector<HTMLElement>(".eoCkBacklog");
		const scrollTop = old?.scrollTop ?? 0;
		host.replaceChildren(this.chips(), list);
		const items = (this.getProperty("items") ?? []) as BacklogCard[];
		const grouped = this.getProperty("grouped") === true;
		let group: Urgency | "" = "";
		for (const b of items) {
			if (grouped && b.urgency !== group) {
				group = b.urgency;
				const g = groups.find((x) => x.key === b.urgency);
				const head = document.createElement("div");
				head.className = "eoCkBlGroup";
				head.setAttribute("role", "presentation");
				const label = document.createElement("span");
				label.textContent = g?.text ?? "";
				const count = document.createElement("span");
				count.textContent = String(items.filter((x) => x.urgency === b.urgency).length);
				head.append(label, count);
				list.append(head);
			}
			list.append(this.row(b));
		}
		if (!items.length) {
			const empty = document.createElement("div");
			empty.className = "eoCkBlEmpty";
			empty.textContent = "Alles eingeplant – keine Bauvorhaben in dieser Auswahl.";
			list.append(empty);
		}
		list.scrollTop = scrollTop;
	}

	private chips(): HTMLElement {
		const box = document.createElement("div");
		box.className = "eoCkBlChips";
		box.setAttribute("role", "group");
		box.setAttribute("aria-label", "Nach Dringlichkeit filtern");
		const counts = (this.getProperty("counts") ?? {}) as Partial<Record<Urgency, number>>;
		const active = new Set((this.getProperty("urgency") ?? []) as Urgency[]);
		for (const g of groups) {
			const chip = document.createElement("button");
			chip.type = "button";
			chip.className = `eoCkChip eoCkChip-${g.key}`;
			chip.setAttribute("aria-pressed", String(active.has(g.key)));
			chip.title = `${g.text}: nur diese anzeigen (erneut klicken hebt den Filter auf)`;
			const dot = document.createElement("span");
			dot.className = "eoCkChipDot";
			const text = document.createElement("span");
			text.textContent = g.chip;
			const n = document.createElement("b");
			n.textContent = String(counts[g.key] ?? 0);
			chip.append(dot, text, n);
			chip.addEventListener("click", () => {
				if (active.has(g.key)) active.delete(g.key);
				else active.add(g.key);
				this.fireEvent("urgencyChange", { urgency: [...active] });
			});
			box.append(chip);
		}
		return box;
	}

	private row(b: BacklogCard): HTMLElement {
		const canPlan = this.getProperty("canPlan") === true;
		const card = document.createElement("div");
		card.className = `eoCkBl eoCkBl-${b.urgency}${canPlan ? " eoCkMovable" : ""}`;
		card.setAttribute("role", "listitem");
		card.tabIndex = 0;
		card.dataset.flipKey = `bl:${b.id}`;
		card.addEventListener("dragstart", (e) => e.preventDefault());
		card.setAttribute("aria-label", `${b.title}, ${b.meta}, ${b.due}${canPlan ? ". Auf Kolonne und Tag ziehen oder Enter zum Einplanen." : ""}`);
		card.title = canPlan ? "Auf Kolonne und Tag ziehen, um einzuplanen" : b.title;
		const title = document.createElement("a");
		title.className = "eoCkBlTitle";
		title.textContent = b.title;
		title.href = `/bauakten/${b.id}`;
		title.draggable = false;
		title.title = `${b.title} – Bauakte öffnen`;
		title.addEventListener("click", (e) => {
			e.preventDefault();
			this.fireEvent("open", { id: b.id });
		});
		card.append(title);
		const area = document.createElement("span");
		area.className = "eoCkBlArea";
		area.textContent = b.area;
		card.append(area);
		const meta = document.createElement("div");
		meta.className = "eoCkBlMeta";
		meta.textContent = b.meta;
		meta.title = b.meta;
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
			planned.title = b.planned;
			foot.append(planned);
		}
		if (canPlan) {
			const plan = document.createElement("button");
			plan.type = "button";
			plan.className = "eoCkBlPlan";
			plan.title = "Einplanen …";
			plan.setAttribute("aria-label", `${b.title} einplanen`);
			const label = document.createElement("span");
			label.textContent = "Einplanen";
			plan.append(icon("sap-icon://appointment-2"), label);
			plan.addEventListener("click", () => this.fireEvent("plan", { id: b.id }));
			foot.append(plan);
			card.addEventListener("pointerdown", (e) => startCockpitDrag(e, card, { kind: "bauakte", id: b.id, label: b.title }));
			card.addEventListener("keydown", (e) => {
				if (e.key === "Enter" && e.target === card) this.fireEvent("plan", { id: b.id });
			});
		}
		card.append(meta, foot);
		return card;
	}
}
