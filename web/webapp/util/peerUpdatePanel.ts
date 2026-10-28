/** Abschnitt „Aktualisierung der Zusatzserver“ (Backup-Ziele, Archiv) – Aufträge, Verfolgung, Schalter. */
import type JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import { get, post, put } from "./api";
import { peerRow, type PeerKind, type PeerUpdateAllResponse, type PeerUpdateDto, type PeerUpdatesResponse } from "./peerUpdates";

interface Host {
	model(): JSONModel;
	run<T>(work: () => Promise<T>, success?: string): Promise<T | undefined>;
	confirm(text: string, options?: { title?: string; action?: string; danger?: boolean }): Promise<boolean>;
}

/** Abschnitt „Aktualisierung der Zusatzserver“ einer Seite (Daten unter /peers im View-Modell). */
export class PeerUpdatePanel {
	private timer: ReturnType<typeof setTimeout> | undefined;

	constructor(private readonly host: Host, private readonly kinds: PeerKind[]) {}

	static initial(): Record<string, unknown> {
		return { items: [], current: "", outdated: 0, updatable: 0, loaded: false };
	}

	async load(live = true): Promise<void> {
		const r = await get<PeerUpdatesResponse>("backup/peer-updates", { live: live ? undefined : "false" }).catch(() => undefined);
		if (!r) return;
		const items = r.items.filter((i) => this.kinds.includes(i.kind)).map(peerRow);
		const m = this.host.model();
		m.setProperty("/peers", {
			items, current: r.currentVersion, loaded: true,
			outdated: items.filter((i) => i["updateAvailable"]).length,
			updatable: items.filter((i) => i["canUpdate"]).length,
		});
		// Laufende Aufträge verfolgen, bis ein Ergebnis da ist.
		clearTimeout(this.timer);
		if (items.some((i) => i["active"])) this.timer = setTimeout(() => void this.load(false), 5000);
	}

	stop(): void { clearTimeout(this.timer); }

	async update(p: PeerUpdateDto): Promise<void> {
		if (!await this.host.confirm(`„${p.name}“ von Version ${p.installedVersion ?? "?"} auf ${p.availableVersion} aktualisieren?\n\n`
			+ "Der Rechner tauscht den Container (Daten bleiben) und springt automatisch auf die bisherige Version zurück, wenn die neue nicht gesund wird. "
			+ "Während des Austauschs ist er etwa eine Minute nicht erreichbar.", { title: "Zusatzserver aktualisieren", action: "Aktualisieren" })) return;
		const r = await this.host.run(() => post<PeerUpdateDto>(`backup/peer-updates/${encodeURIComponent(p.key)}/update`, { version: null }), "Aktualisierung beauftragt");
		if (r?.status === "failed") MessageBox.error(r.message ?? "Aktualisierung fehlgeschlagen.", { title: "Nicht möglich" });
		await this.load(false);
	}

	async updateAll(): Promise<void> {
		const count = Number(this.host.model().getProperty("/peers/updatable") ?? 0);
		if (!await this.host.confirm(`${count} Zusatzserver auf Version ${this.host.model().getProperty("/peers/current")} aktualisieren?`,
			{ title: "Alle aktualisieren", action: "Alle aktualisieren" })) return;
		const r = await this.host.run(() => post<PeerUpdateAllResponse>(`backup/peer-updates/update-all?kind=${this.kinds.join(",")}`, {}));
		if (r) this.host.model().setProperty("/peers/requestedText", `${r.requested} beauftragt`);
		await this.load(false);
	}

	async setAuto(p: PeerUpdateDto, on: boolean): Promise<void> {
		await this.host.run(() => put<PeerUpdateDto>(`backup/peer-updates/${encodeURIComponent(p.key)}/auto`, { autoUpdate: on }),
			on ? "Wird künftig automatisch mitaktualisiert" : "Automatisches Mitaktualisieren aus");
		await this.load(false);
	}

	showCommand(p: PeerUpdateDto): void {
		MessageBox.information(`Dieser Rechner hat noch keinen Updater. Einmal am Rechner selbst ausführen (bisherige -e-Optionen wie bei der Einrichtung ergänzen):\n\n`
			+ `${p.localCommand}\n\nDanach geht jede weitere Aktualisierung hier per Knopfdruck.`, { title: "Einmalig lokal aktualisieren" });
	}
}
