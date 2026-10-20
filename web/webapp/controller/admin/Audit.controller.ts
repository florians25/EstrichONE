import BaseController from "../BaseController";
import { get } from "../../util/api";
import { auditGroups } from "../../util/labels";
import type { AuditLogItem, AuditLogPage } from "../../util/models";
import { session } from "../../util/session";

/** Protokoll: das nur anfügbare Audit-Log, gefiltert und seitenweise. @namespace estrichone.controller.admin */
export default class Audit extends BaseController {
	onInit(): void {
		this.local({ items: [], cursor: null, busy: false, group: "", actor: "", from: null, to: null,
			groups: [{ key: "", text: "Alle Bereiche" }, ...auditGroups], people: [{ key: "", text: "Alle Personen" }] });
	}

	async onRouteMatched(): Promise<void> {
		const people = await session.people();
		this.model().setProperty("/people", [{ key: "", text: "Alle Personen" }, ...people.map((p) => ({ key: p.id, text: p.displayName }))]);
		await this.load(false);
	}

	onFilter(): void { void this.load(false); }
	onMore(): void { void this.load(true); }

	private async load(more: boolean): Promise<void> {
		const m = this.model();
		const from = m.getProperty("/from") as Date | null;
		const to = m.getProperty("/to") as Date | null;
		const end = to ? new Date(to.getFullYear(), to.getMonth(), to.getDate() + 1) : null;
		const page = await this.run(() => get<AuditLogPage>("audit", {
			limit: 100, action: m.getProperty("/group") || undefined, actorUserId: m.getProperty("/actor") || undefined,
			cursor: more ? m.getProperty("/cursor") : undefined, from: from?.toISOString(), to: end?.toISOString(),
		}));
		if (!page) return;
		const rows = page.items.map((e: AuditLogItem) => ({ ...e, area: e.action.split(".")[0],
			detailsText: Object.entries(e.details ?? {}).map(([k, v]) => `${k}: ${v ?? "–"}`).join(" · ") }));
		m.setProperty("/items", more ? [...m.getProperty("/items"), ...rows] : rows);
		m.setProperty("/cursor", page.nextCursor);
	}
}
