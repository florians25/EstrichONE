import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import { akte } from "../../util/akteStore";
import { get } from "../../util/api";
import { approvalStatus, decideApproval, requestApproval, type ApprovalResponse } from "../../util/approvals";
import { session } from "../../util/session";
import { documentRows } from "../../util/documents";
import { dateTime, fileSize } from "../../util/format";
import { documentCategories } from "../../util/labels";
import type { FileResponse } from "../../util/models";
import type { RouteMatch } from "../../util/router";

/** Dateivorschau in der rechten Spalte: PDF und Bilder direkt, Videos mit Wiedergabe, Fassungen. @namespace estrichone.controller.bauakte */
export default class Item extends BaseController {
	private bauakteId = "";
	private query = "";

	onInit(): void {
		this.local({ file: null, kind: "none", frame: "", image: "", video: "", versions: [], busy: false, full: false, place: "", subtitle: "" }, "it");
	}

	async show(match: RouteMatch): Promise<void> {
		const m = this.model("it");
		this.bauakteId = match.params.id;
		this.query = match.query.toString();
		m.setProperty("/full", match.query.get("voll") === "1");
		m.setProperty("/busy", true);
		try {
			const files = await akte.files(this.bauakteId);
			const file = files.find((f) => f.id === match.params.item);
			if (!file) {
				this.toast("Datei nicht gefunden");
				this.onClose();
				return;
			}
			this.apply(file, files);
			await this.loadApprovals(file);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private apply(file: FileResponse, files: FileResponse[]): void {
		const m = this.model("it");
		const url = `/api/v1/files/${file.id}/content`;
		const type = file.contentType;
		const kind = type === "application/pdf" || file.originalName.toLowerCase().endsWith(".pdf") ? "pdf"
			: type.startsWith("image/") ? "image" : type.startsWith("video/") ? "video" : "none";
		m.setProperty("/file", file);
		m.setProperty("/kind", file.state === "stored" ? kind : "none");
		m.setProperty("/frame", kind === "pdf" ? `<iframe class="eoPreviewFrame" title="Vorschau ${file.originalName.replace(/["<>&]/g, "")}" src="${url}"></iframe>` : "");
		m.setProperty("/image", kind === "image" ? `${url}?variant=${file.variants === "ready" ? "display" : "original"}` : "");
		m.setProperty("/video", kind === "video" ? `<video controls preload="metadata" class="eoPreviewFrame" src="${url}"></video>` : "");
		m.setProperty("/place", file.kind === "document"
			? [documentCategories[(file.category ?? "other") as keyof typeof documentCategories]?.text, file.folder].filter(Boolean).join(" / ")
			: file.kind === "photo" ? "Fotos" : "Videos");
		m.setProperty("/subtitle", `${fileSize(file.sizeBytes)} · ${dateTime(file.createdAt)}`);
		const row = documentRows(files).find((r) => r.id === file.id);
		const chain = row?.chain ?? [file.id];
		m.setProperty("/versions", chain.map((id, index) => {
			const f = files.find((x) => x.id === id)!;
			return { ...f, number: chain.length - index, current: id === file.id, latest: index === 0 };
		}));
	}

	private async loadApprovals(file: FileResponse): Promise<void> {
		const m = this.model("it");
		const list = file.kind === "document" ? await get<ApprovalResponse[]>(`files/${file.id}/approvals`).catch(() => [] as ApprovalResponse[]) : [];
		m.setProperty("/approvals", list.map((a) => ({
			...a, statusText: approvalStatus[a.status].text, statusState: approvalStatus[a.status].state,
			line: `${a.requestedByName} → ${a.assigneeName} · ${dateTime(a.requestedAt)}${a.message ? ` · „${a.message}“` : ""}`,
			decision: a.decidedAt ? `${a.decidedByName ?? "?"} · ${dateTime(a.decidedAt)}${a.decisionComment ? ` · „${a.decisionComment}“` : ""}` : "",
		})));
		const pending = list.find((a) => a.status === "pending") ?? null;
		m.setProperty("/pendingApproval", pending);
		m.setProperty("/canRequest", file.kind === "document" && !pending && session.has("files.upload"));
	}

	async onRequestApproval(): Promise<void> {
		const file = this.model("it").getProperty("/file") as FileResponse;
		if (await requestApproval(file)) {
			this.toast("Freigabe angefordert");
			await this.loadApprovals(file);
		}
	}

	async onDecide(event: Event): Promise<void> {
		const approve = (event.getSource() as Control).data("approve") === "true";
		const m = this.model("it");
		if (await decideApproval(m.getProperty("/pendingApproval") as ApprovalResponse, approve)) {
			this.toast(approve ? "Dokument freigegeben" : "Freigabe abgelehnt");
			await this.loadApprovals(m.getProperty("/file") as FileResponse);
		}
	}

	onDownload(): void {
		const file = this.model("it").getProperty("/file") as FileResponse;
		const a = document.createElement("a");
		a.href = `/api/v1/files/${file.id}/content`;
		a.download = file.originalName;
		a.click();
	}

	onNewTab(): void {
		const file = this.model("it").getProperty("/file") as FileResponse;
		window.open(`/api/v1/files/${file.id}/content`, "_blank", "noopener");
	}

	onVersion(event: Event): void {
		const file = (event.getSource() as Control).getBindingContext("it")?.getObject() as FileResponse;
		this.nav(`/bauakten/${this.bauakteId}/datei/${file.id}${this.query ? `?${this.query}` : ""}`, true);
	}

	onFull(): void {
		const query = new URLSearchParams(this.query);
		if (query.get("voll") === "1") query.delete("voll");
		else query.set("voll", "1");
		const file = this.model("it").getProperty("/file") as FileResponse;
		const text = query.toString();
		this.nav(`/bauakten/${this.bauakteId}/datei/${file.id}${text ? `?${text}` : ""}`, true);
	}

	onClose(): void {
		const query = new URLSearchParams(this.query);
		query.delete("voll");
		const text = query.toString();
		this.nav(`/bauakten/${this.bauakteId}${text ? `?${text}` : ""}`);
	}
}
