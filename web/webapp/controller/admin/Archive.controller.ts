import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import { apiUrl, del, get, post, put } from "../../util/api";
import { date, dateTime, fileSize } from "../../util/format";
import { openForm } from "../../util/forms";
import { recordStatus } from "../../util/infraModels";
import type { ArchiveConnectionDto, ArchiveOverview, ArchiveRecordDto, ArchiveRuleDto, ArchiveVerification } from "../../util/infraModels";

/** Kurzform eines Prüfprotokolls für Anzeige und Meldung. */
export function verificationText(v: ArchiveVerification | null): { text: string; state: string } {
	if (!v) return { text: "Noch nicht geprüft", state: "None" };
	return v.ok
		? { text: `In Ordnung · ${dateTime(v.at)} · ${v.objectsChecked} Dokumente, Journal ${v.ledgerEntries} Einträge`, state: "Success" }
		: { text: `${v.errors.length} Abweichung(en) · ${dateTime(v.at)}`, state: "Error" };
}

/** System › Archiv: Archivserver koppeln, Regeln je Dokumentart, Warteschlange und Nachweis, Prüfung, Export. @namespace estrichone.controller.admin */
export default class Archive extends BaseController {
	onInit(): void {
		this.local({ o: null, busy: false, rules: [], records: [], filter: "", settings: { autoArchive: true, notifyEmail: "" }, stats: {}, contact: {} });
	}

	onRouteMatched(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const [o, rules] = await Promise.all([
			this.run(() => get<ArchiveOverview>("archive/status")),
			this.run(() => get<ArchiveRuleDto[]>("archive/rules")),
		]);
		m.setProperty("/busy", false);
		if (!o) return;
		m.setProperty("/o", o);
		m.setProperty("/rules", rules ?? []);
		const c = o.connection;
		m.setProperty("/pairedText", c ? dateTime(c.pairedAt) : "");
		m.setProperty("/settings", { autoArchive: c?.autoArchive ?? true, notifyEmail: c?.notifyEmail ?? "", dossierAfterMonths: c?.dossierAfterMonths ?? 0 });
		m.setProperty("/contact", c?.lastError
			? { text: `Fehler · zuletzt erreicht ${dateTime(c.lastContactAt)}`, state: "Error" }
			: { text: o.server ? `Erreichbar · ${dateTime(c?.lastContactAt)}` : `Nicht erreichbar · zuletzt ${dateTime(c?.lastContactAt)}`, state: o.server ? "Success" : "Warning" });
		const s = o.server;
		m.setProperty("/stats", {
			objects: s?.archive ? `${s.archive.objects} (${fileSize(s.archive.bytes)})${s.archive.deleted ? `, ${s.archive.deleted} nach Fristablauf gelöscht` : ""}` : "–",
			disk: s?.diskFreeBytes != null ? `${fileSize(s.diskFreeBytes)} frei von ${fileSize(s.diskTotalBytes ?? 0)}` : "–",
			ledger: s?.archive ? `${s.archive.ledgerEntries} Einträge · Kopf ${s.archive.ledgerHead.slice(0, 16)}…` : "–",
			verification: verificationText(s?.lastVerification ?? null),
		});
		await this.loadRecords();
	}

	private async loadRecords(): Promise<void> {
		const status = String(this.model().getProperty("/filter") || "");
		const records = await this.run(() => get<ArchiveRecordDto[]>("archive/records", { status: status || undefined }));
		this.model().setProperty("/records", (records ?? []).map((r) => ({
			...r,
			size: fileSize(r.sizeBytes),
			statusText: r.status === "archived" ? `Archiviert ${dateTime(r.archivedAt)}` : `${recordStatus[r.status].text}${r.attempts ? ` (${r.attempts} Versuch${r.attempts > 1 ? "e" : ""})` : ""}`,
			statusState: recordStatus[r.status].state,
			retain: r.retainUntil ? `${date(r.retainUntil)} (${r.retentionYears} Jahre)` : `${r.retentionYears} Jahre`,
			proof: r.ledgerHash ? `Journal Nr. ${r.ledgerSeq} · ${r.ledgerHash.slice(0, 16)}… · SHA-256 ${r.sha256.slice(0, 12)}…` : "–",
		})));
	}

	onFilter(): void { void this.loadRecords(); }

	onRefresh(): void { void this.load(); }

	async onPair(): Promise<void> {
		const result = await openForm<ArchiveConnectionDto>({
			title: "Archivserver koppeln", submitText: "Koppeln",
			fields: [
				{ key: "intro", type: "info", label: "Adresse und Kopplungscode zeigt der Archivrechner nach der Installation an (bzw. „sudo docker logs estrichone-archive“)." },
				{ key: "address", label: "Adresse", required: true, placeholder: "z. B. 192.168.1.50 oder archiv.firma.local:8443", help: "Archiv im selben Stack: „archive“" },
				{ key: "code", label: "Kopplungscode", required: true, placeholder: "ABCD-EFGH-JKLM", maxLength: 20 },
			],
			submit: (v) => post<ArchiveConnectionDto>("archive/pair", { address: String(v.address).trim(), code: String(v.code).trim() }),
		});
		if (result) {
			MessageBox.success(`Gekoppelt mit „${result.serverName}“.\n\nFingerabdruck:\n${result.fingerprintDisplay}\n\nDer Fingerabdruck muss mit der Anzeige auf dem Archivrechner übereinstimmen.`,
				{ title: "Archivserver verbunden" });
			void this.load();
		}
	}

	async onUnpair(): Promise<void> {
		if (!await this.confirm("Kopplung aufheben? Bereits archivierte Dokumente bleiben auf dem Archivserver erhalten; neue werden nicht mehr archiviert.",
			{ action: "Entkoppeln", danger: true })) return;
		await this.run(() => del("archive/connection"), "Entkoppelt");
		void this.load();
	}

	async onSaveSettings(): Promise<void> {
		const s = this.model().getProperty("/settings") as { autoArchive: boolean; notifyEmail: string; dossierAfterMonths: number };
		const months = Math.round(Number(s.dossierAfterMonths) || 0);
		await this.run(() => put("archive/settings", { autoArchive: s.autoArchive, notifyEmail: s.notifyEmail?.trim() || null,
			dossierAfterMonths: months > 0 ? months : null }), "Gespeichert");
	}

	async onSaveRules(): Promise<void> {
		const rules = (this.model().getProperty("/rules") as ArchiveRuleDto[]).map((r) => ({ ...r, retentionYears: Math.round(Number(r.retentionYears)) }));
		const saved = await this.run(() => put<ArchiveRuleDto[]>("archive/rules", rules), "Regeln gespeichert");
		if (saved) this.model().setProperty("/rules", saved);
	}

	async onRetry(e: Event): Promise<void> {
		const r = (e.getSource() as Control).getBindingContext("view")?.getObject() as ArchiveRecordDto;
		await this.run(() => post(`archive/records/${r.id}/retry`), "Wird erneut versucht");
		setTimeout(() => void this.loadRecords(), 3000);
	}

	async onVerify(): Promise<void> {
		const report = await this.run(() => post<ArchiveVerification>("archive/verify"));
		if (!report) return;
		const v = verificationText(report);
		if (report.ok) MessageBox.success(`${v.text}.\n\nJournal-Kopf: ${report.ledgerHead}`, { title: "Prüfprotokoll" });
		else MessageBox.error(`${v.text}\n\n${report.errors.slice(0, 20).join("\n")}`, { title: "Prüfprotokoll: Abweichungen" });
		void this.load();
	}

	onExport(): void {
		window.open(apiUrl("archive/export"), "_blank", "noopener");
	}
}
