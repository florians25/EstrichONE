import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type ListItemBase from "sap/m/ListItemBase";
import MessageBox from "sap/m/MessageBox";
import { del, get, post, put } from "../../util/api";
import { dateTime } from "../../util/format";
import type { MailLogItem, MailSecurity, MailSettingsDto, MailTemplateDto, TestMailResult } from "../../util/infraModels";

const status = { queued: ["Wartet", "Information"], sent: ["Versendet", "Success"], failed: ["Fehlgeschlagen", "Error"] } as const;
const defaultPort: Record<MailSecurity, number> = { startTls: 587, sslOnConnect: 465, none: 25 };

/** System › E-Mail: SMTP-Konto, Testmail, Vorlagen, Versandprotokoll. @namespace estrichone.controller.admin */
export default class Mail extends BaseController {
	onInit(): void {
		this.local({ s: null, loaded: false, busy: false, password: "", testTo: "", templates: [], t: null, log: [] });
	}

	onRouteMatched(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const [s, templates] = await Promise.all([
			this.run(() => get<MailSettingsDto>("mail/settings")),
			this.run(() => get<MailTemplateDto[]>("mail/templates")),
		]);
		m.setProperty("/busy", false);
		if (s) { m.setProperty("/s", s); m.setProperty("/loaded", true); m.setProperty("/password", ""); }
		m.setProperty("/templates", templates ?? []);
		await this.loadLog();
	}

	private async loadLog(): Promise<void> {
		const log = await this.run(() => get<MailLogItem[]>("mail/log", { take: 300 }));
		this.model().setProperty("/log", (log ?? []).map((l) => ({
			...l, time: dateTime(l.createdAt),
			statusText: `${status[l.status][0]}${l.attempts > 1 ? ` (${l.attempts} Versuche)` : ""}${l.sentAt ? ` · ${dateTime(l.sentAt)}` : ""}${l.attachmentNames ? ` · Anhang: ${l.attachmentNames}` : ""}`,
			statusState: status[l.status][1],
		})));
	}

	onRefreshLog(): void { void this.loadLog(); }

	onSecurity(): void {
		const s = this.model().getProperty("/s") as MailSettingsDto;
		if ([25, 465, 587].includes(Number(s.port))) this.model().setProperty("/s/port", defaultPort[s.security]);
	}

	private async save(): Promise<MailSettingsDto | undefined> {
		const s = this.model().getProperty("/s") as MailSettingsDto;
		const password = String(this.model().getProperty("/password") ?? "");
		const saved = await this.run(() => put<MailSettingsDto>("mail/settings", {
			enabled: s.enabled, host: s.host?.trim() ?? "", port: Math.round(Number(s.port)), security: s.security,
			userName: s.userName?.trim() || null, password: password || null, clearPassword: false,
			fromAddress: s.fromAddress?.trim() ?? "", fromName: s.fromName?.trim() || null, replyTo: s.replyTo?.trim() || null,
		}));
		if (saved) { this.model().setProperty("/s", saved); this.model().setProperty("/password", ""); }
		return saved;
	}

	async onSave(): Promise<void> {
		if (await this.save()) this.toast("Gespeichert");
	}

	async onTest(): Promise<void> {
		const to = String(this.model().getProperty("/testTo") ?? "").trim();
		if (!to) { this.toast("Bitte einen Empfänger eingeben."); return; }
		if (!await this.save()) return;
		const result = await this.run(() => post<TestMailResult>("mail/test", { to }));
		if (!result) return;
		if (result.ok) MessageBox.success(`Testmail an ${to} wurde vom Mailserver angenommen. Bitte den Posteingang (ggf. Spam) prüfen.`, { title: "Testmail gesendet" });
		else MessageBox.error(result.error ?? "Unbekannter Fehler", { title: "Testmail fehlgeschlagen" });
		void this.load();
	}

	onTemplate(e: Event): void {
		const item = e.getParameter("listItem" as never) as unknown as ListItemBase;
		const t = item.getBindingContext("view")?.getObject() as MailTemplateDto;
		this.model().setProperty("/t", { ...t, placeholderText: t.placeholders.map((p) => `{{${p}}}`).join(", ") });
	}

	private current(): MailTemplateDto {
		return this.model().getProperty("/t") as MailTemplateDto;
	}

	private applyTemplate(saved: MailTemplateDto): void {
		const list = (this.model().getProperty("/templates") as MailTemplateDto[]).map((t) => (t.key === saved.key ? saved : t));
		this.model().setProperty("/templates", list);
		this.model().setProperty("/t", { ...saved, placeholderText: saved.placeholders.map((p) => `{{${p}}}`).join(", ") });
	}

	async onSaveTemplate(): Promise<void> {
		const t = this.current();
		const saved = await this.run(() => put<MailTemplateDto>(`mail/templates/${t.key}`, { subject: t.subject, body: t.body }), "Vorlage gespeichert");
		if (saved) this.applyTemplate(saved);
	}

	async onResetTemplate(): Promise<void> {
		const t = this.current();
		if (!await this.confirm("Eigenen Text verwerfen und den Standardtext wiederherstellen?", { action: "Wiederherstellen" })) return;
		const saved = await this.run(() => del<MailTemplateDto>(`mail/templates/${t.key}`), "Standardtext wiederhergestellt");
		if (saved) this.applyTemplate(saved);
	}

	async onRetry(e: Event): Promise<void> {
		const l = (e.getSource() as Control).getBindingContext("view")?.getObject() as MailLogItem;
		await this.run(() => post(`mail/log/${l.id}/retry`), "Wird erneut gesendet");
		setTimeout(() => void this.loadLog(), 3000);
	}
}
