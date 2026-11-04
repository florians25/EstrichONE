import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import { del, get, post, put } from "../../util/api";
import { fileSize } from "../../util/format";
import { openForm } from "../../util/forms";
import { PeerUpdatePanel } from "../../util/peerUpdatePanel";
import type { PeerUpdateDto } from "../../util/peerUpdates";
import {
	type OffloadAccepted, type OffloadConnectionDto, type OffloadedBauakteDto, type OffloadedFilesPage, type OffloadOverview, type OffloadSettingsDto,
	offloadFileRow, offloadView,
} from "../../util/offload";

/**
 * System › Speicher-Auslagerung: Belegung Hauptserver/Storage, Schwelle und Regeln, „Jetzt auslagern“, Fortschritt,
 * ausgelagerte Bauakten/Dateien mit Rückholen, Sicherungskopie, Abgleich, Fernaktualisierung des Storage (docs/speicher.md).
 * @namespace estrichone.controller.admin
 */
export default class Offload extends BaseController {
	private peers!: PeerUpdatePanel;
	private timer: ReturnType<typeof setTimeout> | undefined;
	/** Nach „Jetzt auslagern“/„Zurückholen“ eine Weile weiter aktualisieren (der Auftrag startet im Hintergrund). */
	private followUntil = 0;

	onInit(): void {
		this.local({ o: null, ui: {}, settings: {}, bauakten: [], files: [], filesTotal: 0, filter: "", peers: PeerUpdatePanel.initial() });
		this.peers = new PeerUpdatePanel({ model: () => this.model(), run: (w, s) => this.run(w, s), confirm: (t, o) => this.confirm(t, o) }, ["offload"]);
	}

	onExit(): void {
		this.peers.stop();
		clearTimeout(this.timer);
	}

	onRouteMatched(): void { void this.load(); }

	onRefresh(): void { void this.load(); }

	private async load(quiet = false): Promise<void> {
		const o = quiet ? await get<OffloadOverview>("files/offload").catch(() => undefined) : await this.run(() => get<OffloadOverview>("files/offload"));
		if (!o) return;
		const m = this.model();
		m.setProperty("/o", o);
		m.setProperty("/ui", offloadView(o));
		const ok = o.status.text.replace(/^Alles in Ordnung\.\s*/, "");
		this.statusLine(o.status.level === "ok" ? (o.paired ? [] : [{ text: ok, level: "info" }])
			: [{ text: o.status.text, level: o.status.level === "critical" ? "error" : "warning" }], o.paired ? ok : "");
		if (!quiet) m.setProperty("/settings", { ...o.settings, thresholdFreeGb: o.settings.thresholdFreeGb ?? 0 });
		if (o.paired && this.has("backup.manage") && !quiet) void this.peers.load();
		await this.loadLists();
		// Laufenden Auftrag verfolgen (und wartende Sicherungskopie), bis er fertig ist.
		clearTimeout(this.timer);
		if (o.job.running || o.counts.pendingBlobs > 0 || Date.now() < this.followUntil) this.timer = setTimeout(() => void this.load(true), 4000);
	}

	private async loadLists(): Promise<void> {
		const state = String(this.model().getProperty("/filter") || "");
		const [bauakten, files] = await Promise.all([
			get<OffloadedBauakteDto[]>("files/offload/bauakten").catch(() => []),
			get<OffloadedFilesPage>("files/offload/files", { state: state || undefined, take: "100" }).catch(() => ({ items: [], total: 0 })),
		]);
		const m = this.model();
		m.setProperty("/bauakten", bauakten.map((b) => ({ ...b, size: fileSize(b.bytes) })));
		m.setProperty("/files", files.items.map(offloadFileRow));
		m.setProperty("/filesTotal", files.total);
	}

	onFilter(): void { void this.loadLists(); }

	async onPair(): Promise<void> {
		const result = await openForm<OffloadConnectionDto>({
			title: "Storage koppeln", submitText: "Koppeln",
			fields: [
				{ key: "intro", type: "info", label: "Adresse und Kopplungscode zeigt der Kombiserver bzw. Archivrechner nach der Installation an "
					+ "(Installer-Modus backup-archiv mit -e ESTRICHONE_OFFLOAD_DATA_DIR=… oder Modus auslagerung)." },
				{ key: "address", label: "Adresse", required: true, placeholder: "z. B. 192.168.1.50:8445", help: "Port des Storage: Standard 8445" },
				{ key: "code", label: "Kopplungscode", required: true, placeholder: "ABCD-EFGH-JKLM-NPQR", maxLength: 24 },
			],
			submit: (v) => post<OffloadConnectionDto>("files/offload/pair", { address: String(v.address).trim(), code: String(v.code).trim() }),
		});
		if (result) {
			MessageBox.success(`Gekoppelt mit „${result.serverName}“.\n\nFingerabdruck:\n${result.fingerprintDisplay}\n\n`
				+ "Der Fingerabdruck muss mit der Anzeige auf dem Storage übereinstimmen.", { title: "Storage verbunden" });
			void this.load();
		}
	}

	async onSaveSettings(): Promise<void> {
		const s = this.model().getProperty("/settings") as OffloadSettingsDto;
		const gb = Math.round(Number(s.thresholdFreeGb) || 0);
		if (!s.requireBackup && !await this.confirm("Ohne Sicherungskopie liegen ausgelagerte Originale nur noch auf dem Storage. Fällt seine Platte aus, "
			+ "sind sie verloren. Wirklich ohne Sicherungskopie auslagern?", { action: "Trotzdem speichern", danger: true })) return;
		const saved = await this.run(() => put<OffloadSettingsDto>("files/offload/settings", {
			...s, thresholdPercent: Math.round(Number(s.thresholdPercent)), thresholdFreeGb: gb > 0 ? gb : null,
			minAgeMonths: Math.round(Number(s.minAgeMonths)), cacheLimitMb: Math.round(Number(s.cacheLimitMb)),
		}), "Gespeichert");
		if (saved) void this.load();
	}

	async onRunNow(): Promise<void> {
		const r = await this.run(() => post<OffloadAccepted>("files/offload/run", {}));
		if (r) { this.toast(r.message); this.followUntil = Date.now() + 90_000; void this.load(true); }
	}

	async onRecallBauakte(e: Event): Promise<void> {
		const b = this.rowOf<OffloadedBauakteDto>(e);
		if (!await this.confirm(`Alle ${b.files} ausgelagerten Dateien von „${b.title}“ (${fileSize(b.bytes)}) wieder lokal speichern?`,
			{ action: "Wieder lokal speichern" })) return;
		await this.recall({ bauakteId: b.bauakteId });
	}

	async onRecallFile(e: Event): Promise<void> {
		const f = this.rowOf<{ fileId: string }>(e);
		await this.recall({ fileIds: [f.fileId] });
	}

	async onRecallAll(): Promise<void> {
		const o = this.model().getProperty("/o") as OffloadOverview;
		if (!await this.confirm(`Alle ausgelagerten Originale (${fileSize(o.counts.offloadedBytes)}) wieder auf diesem Server speichern? `
			+ "Vorher wird geprüft, ob genug Platz frei ist; reicht er nicht, hält das Rückholen rechtzeitig an.", { action: "Alles zurückholen", danger: true })) return;
		await this.recall({ all: true });
	}

	private async recall(body: Record<string, unknown>): Promise<void> {
		const r = await this.run(() => post<OffloadAccepted>("files/offload/recall", body));
		if (r) { this.toast(r.message); this.followUntil = Date.now() + 90_000; void this.load(true); }
	}

	async onReconcile(): Promise<void> {
		const r = await this.run(() => post<OffloadAccepted>("files/offload/reconcile", {}));
		if (r) { this.toast(r.message); void this.load(); }
	}

	async onRecoverFromBackup(): Promise<void> {
		if (!await this.confirm("Fehlende Originale aus den Auslagerungs-Snapshots der Sicherung zurückspielen? Vorhandene Dateien bleiben unverändert.",
			{ action: "Aus Sicherung zurückholen" })) return;
		const r = await this.run(() => post<OffloadAccepted>("files/offload/recover-from-backup", {}));
		if (r) MessageBox.information(r.message, { title: "Wiederherstellung angefordert" });
	}

	async onUnpair(): Promise<void> {
		if (!await this.confirm("Storage entkoppeln? Das geht nur, wenn nichts mehr ausgelagert ist.", { action: "Entkoppeln", danger: true })) return;
		await this.run(() => del("files/offload/pairing"), "Entkoppelt");
		void this.load();
	}

	onPeerUpdate(e: Event): void { void this.peers.update(this.rowOf<PeerUpdateDto>(e)); }

	onPeerAuto(e: Event): void { void this.peers.setAuto(this.rowOf<PeerUpdateDto>(e), Boolean((e as Event<{ state: boolean }>).getParameter("state"))); }

	onPeerCommand(e: Event): void { this.peers.showCommand(this.rowOf<PeerUpdateDto>(e)); }

	onOpenBauakte(e: Event): void {
		const id = this.rowOf<{ bauakteId: string | null }>(e).bauakteId;
		if (id) this.nav(`/bauakten/${id}`);
	}

	private rowOf<T>(e: Event): T {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as T;
	}
}
