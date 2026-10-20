import JSONModel from "sap/ui/model/json/JSONModel";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import BaseController from "./BaseController";
import { del, get, post, put } from "../model/Api";

interface Target { slug: string; synced?: boolean | null; error?: string | null }
interface Notice {
	id: string; kind: string; title: string; text: string; severity: string; startsAt?: string | null; endsAt?: string | null;
	adminsMayWork: boolean; allTenants: boolean; tenantIds: string[]; targets: Target[];
}

/**
 * Systemhinweise: Ankündigungen und Wartungsfenster an einzelne, ausgewählte oder alle Instanzen verteilen.
 * @namespace estrichone.hosting.controller
 */
export default class NoticesController extends BaseController {
	private readonly notices = new JSONModel({ items: [] });
	private readonly notice = new JSONModel({});

	public onInit(): void {
		this.setModel(this.notices, "notices");
		this.setModel(this.notice, "notice");
		this.getRouter().getRoute("notices")!.attachPatternMatched(() => void this.load());
	}

	private async load(): Promise<void> {
		await this.run(async () => this.notices.setProperty("/items", await get<Notice[]>("/api/notices")));
	}

	public targetsText(all: boolean, targets?: Target[]): string {
		return all ? `Alle (${targets?.length ?? 0})` : (targets ?? []).map((t) => t.slug).join(", ");
	}

	public syncText(targets?: Target[]): string {
		if (!targets?.length) return "–";
		const ok = targets.filter((t) => t.synced).length;
		return `${ok} von ${targets.length}`;
	}

	public syncState(targets?: Target[]): string {
		if (!targets?.length) return "None";
		return targets.every((t) => t.synced) ? "Success" : targets.some((t) => t.synced === false) ? "Warning" : "None";
	}

	public syncDetails(targets?: Target[]): string {
		return (targets ?? []).map((t) => `${t.slug}: ${t.synced ? "übertragen" : t.error ?? "ausstehend (Instanz läuft nicht)"}`).join("\n");
	}

	private async open(data: Partial<Notice> & { kind: string }): Promise<void> {
		const tenants = await this.run(() => get<{ id: string; slug: string; companyName: string }[]>("/api/tenants"));
		this.notice.setData({
			severity: "info", adminsMayWork: true, allTenants: true, tenantIds: [], text: "", title: "",
			...data,
			startsAt: data.startsAt ? new Date(data.startsAt) : data.kind === "Maintenance" ? new Date(Date.now() + 86_400_000) : null,
			endsAt: data.endsAt ? new Date(data.endsAt) : data.kind === "Maintenance" ? new Date(Date.now() + 86_400_000 + 7_200_000) : null,
			tenants: tenants ?? []
		});
		(this.byId("noticeDialog") as Dialog).open();
	}

	public onCreateAnnouncement(): void {
		void this.open({ kind: "Announcement" });
	}

	public onCreateMaintenance(): void {
		void this.open({ kind: "Maintenance", title: "Wartungsarbeiten", text: "EstrichONE wird aktualisiert und ist in dieser Zeit nicht erreichbar." });
	}

	public onEdit(event: Event): void {
		void this.open({ ...((event.getSource() as Control).getBindingContext("notices")!.getObject() as Notice) });
	}

	public async onSave(): Promise<void> {
		const data = this.notice.getData();
		const body = {
			kind: data.kind, title: data.title, text: data.text, severity: data.severity,
			startsAt: data.startsAt ? (data.startsAt as Date).toISOString() : null,
			endsAt: data.endsAt ? (data.endsAt as Date).toISOString() : null,
			adminsMayWork: data.adminsMayWork, allTenants: data.allTenants, tenantIds: data.allTenants ? [] : data.tenantIds
		};
		const dialog = this.byId("noticeDialog") as Dialog;
		const done = await this.run(async () => {
			if (data.id) await put(`/api/notices/${data.id}`, body);
			else await post("/api/notices", body);
			return true;
		}, dialog);
		if (done) {
			dialog.close();
			this.toast("Gespeichert und verteilt");
			await this.load();
		}
	}

	public onCancel(): void {
		(this.byId("noticeDialog") as Dialog).close();
	}

	public async onEnd(event: Event): Promise<void> {
		const id = (event.getSource() as Control).getBindingContext("notices")!.getProperty("id") as string;
		await this.run(() => post(`/api/notices/${id}/end`));
		await this.load();
	}

	public onDelete(event: Event): void {
		const item = (event.getSource() as Control).getBindingContext("notices")!.getObject() as Notice;
		MessageBox.confirm(`„${item.title}“ löschen? Die Instanzen entfernen den Hinweis beim nächsten Abgleich.`, {
			onClose: async (choice: string) => {
				if (choice === MessageBox.Action.OK) {
					await this.run(() => del(`/api/notices/${item.id}`));
					await this.load();
				}
			}
		});
	}

	public async onSync(): Promise<void> {
		await this.run(() => post("/api/notices/sync"));
		this.toast("An alle laufenden Instanzen verteilt");
		await this.load();
	}
}
