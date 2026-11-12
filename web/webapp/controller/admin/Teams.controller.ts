import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import MessageBox from "sap/m/MessageBox";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Text from "sap/m/Text";
import Link from "sap/m/Link";
import Title from "sap/m/Title";
import VBox from "sap/m/VBox";
import BusyIndicator from "sap/m/BusyIndicator";
import { del, get, post, put } from "../../util/api";
import { dateTime } from "../../util/format";
import { prompt } from "../../util/forms";
import { session } from "../../util/session";
import {
	chatModeLabels, jobText, mappingState, type ChatMode, type TeamsRollout, type TeamsSenderStart, type TeamsSenderState, type TeamsSettings, type TeamsTestResponse,
	type TeamsUserMapping,
} from "../../util/teams";

const modeHints: Record<ChatMode, string> = {
	estrichOne: "Wie bisher: Unterhaltungen im eingebauten Chat. Teams-Verknüpfungen sind ausgeblendet.",
	teams: "Unterhaltungen in Teams. Der eingebaute Chat wird zum Archiv (lesen und exportieren, nicht schreiben); Navigation, Bauakte und Apps zeigen „In Teams öffnen“. Es wird nichts gelöscht.",
	both: "Eingebauter Chat wie bisher und zusätzlich „In Teams öffnen“ an Bauakten und in der Navigation.",
};

/** Ergebnis des letzten Verbindungstests mit Zeitpunkt und den fehlgeschlagenen Schritten. */
function lastTestText(s: TeamsSettings): string {
	if (!s.lastTestAt) return "";
	const at = dateTime(s.lastTestAt);
	if (s.lastTestOk) return `Letzter Verbindungstest am ${at} erfolgreich.`;
	return `Letzter Verbindungstest am ${at} fehlgeschlagen – ${s.lastTestError ?? "Ohne Angabe – bitte „Verbindung testen“ wählen."}`;
}

/** Einstellungen › Integrationen › Microsoft Teams. @namespace estrichone.controller.admin */
export default class Teams extends BaseController {
	private pollTimer: ReturnType<typeof setTimeout> | undefined;

	onInit(): void {
		this.local({
			s: null, loaded: false, busy: false, secret: "", certificate: "", certificateText: "", webhook: "", team: "", users: [], steps: [],
			modeHint: "", lastPostText: "", lastTestText: "", tab: "connection", rollout: null, rolloutBusy: false, rolloutUsersText: "",
			rolloutBauaktenText: "", jobPercent: 0, jobText: "", jobErrors: "",
		});
		this.model().attachPropertyChange((e) => {
			if (String(e.getParameter("path")).endsWith("chatMode")) this.applyHints(this.model().getProperty("/s") as TeamsSettings);
		});
	}

	onExit(): void {
		clearTimeout(this.pollTimer);
		clearTimeout(this.jobTimer);
	}

	private jobTimer: ReturnType<typeof setTimeout> | undefined;

	private applyRollout(r: TeamsRollout): void {
		const m = this.model();
		m.setProperty("/rollout", r);
		m.setProperty("/rolloutUsersText", `${r.mapped} von ${r.activeUsers} mit Microsoft-Konto zugeordnet · ${r.notFound} ohne passendes Konto · `
			+ `${r.unchecked} noch nicht geprüft · ${r.noEmail} ohne E-Mail-Adresse. Ob jemand die Teams-App nutzt, zeigt EstrichONE nicht (Teams Admin Center › Berichte).`);
		m.setProperty("/rolloutBauaktenText", `${r.linkedOpen} von ${r.openBauakten} mit Teams verknüpft, ${r.unlinkedOpen} ohne Verknüpfung.`);
		const job = r.job;
		m.setProperty("/jobPercent", job && job.total > 0 ? Math.round(job.processed / job.total * 100) : job ? 100 : 0);
		m.setProperty("/jobText", jobText(job));
		m.setProperty("/jobErrors", job?.errors.length ? job.errors.join("\n") : "");
		clearTimeout(this.jobTimer);
		if (job?.state === "running") this.jobTimer = setTimeout(() => void this.loadRollout(), 2000);
	}

	private async loadRollout(): Promise<void> {
		const r = await get<TeamsRollout>("settings/integrations/teams/rollout").catch(() => null);
		if (r) this.applyRollout(r);
	}

	async onLinkAll(): Promise<void> {
		const r = this.model().getProperty("/rollout") as TeamsRollout | null;
		if (!r || !await this.confirm(`${r.unlinkedOpen} offene Bauakten ohne Teams-Verknüpfung jetzt verknüpfen? EstrichONE legt je Bauakte die eingestellte Unterhaltung an `
			+ "und nimmt das Bauakten-Team auf. Das läuft im Hintergrund; diese Seite zeigt den Fortschritt.", { action: "Verknüpfen" })) return;
		if (await this.run(() => post("settings/integrations/teams/rollout/link-all", {}))) await this.loadRollout();
	}

	async onCancelLinkAll(): Promise<void> {
		await this.run(() => del("settings/integrations/teams/rollout/job"));
		await this.loadRollout();
	}

	onRouteMatched(): void { void this.load(); }

	private async load(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		const [s, users] = await Promise.all([
			this.run(() => get<TeamsSettings>("settings/integrations/teams")),
			this.run(() => get<TeamsUserMapping[]>("settings/integrations/teams/users")),
		]);
		m.setProperty("/busy", false);
		if (s) this.apply(s);
		this.applyUsers(users ?? []);
		void this.loadRollout();
	}

	private apply(s: TeamsSettings): void {
		const m = this.model();
		m.setProperty("/s", s);
		m.setProperty("/loaded", true);
		m.setProperty("/secret", "");
		m.setProperty("/certificate", "");
		m.setProperty("/webhook", "");
		m.setProperty("/team", s.siteTeamUrl ?? s.siteTeamId ?? "");
		this.applyHints(s);
		this.statusLine([
			!s.graphConfigured && s.chatMode !== "estrichOne" && {
				text: "Ohne App-Registrierung lassen sich Kanäle und Chats nur per Link verknüpfen; Karten gehen nur über einen Workflows-Webhook.", level: "info",
			},
			s.lastTestOk === false && { text: "Letzter Verbindungstest fehlgeschlagen (Details unter „Verbindung“)", level: "error" },
			s.graphConfigured && s.lastTestAt === null && { text: "Noch kein Verbindungstest", level: "info" },
			s.certificateExpiresAt && new Date(s.certificateExpiresAt).getTime() - Date.now() < 30 * 86_400_000
				&& { text: `Zertifikat läuft am ${dateTime(s.certificateExpiresAt)} ab`, level: "warning" },
			s.graphConfigured && s.mappedUsers < s.activeUsers && `${s.activeUsers - s.mappedUsers} Benutzer ohne Microsoft-Konto`,
			s.postVia === "graph" && !s.senderName && { text: "Karten über Graph: Dienstkonto noch nicht verbunden", level: "warning" },
			s.postVia === "webhook" && !s.hasWebhook && { text: "Karten per Webhook: allgemeine Webhook-URL fehlt (nur Bauakten mit eigener URL)", level: "info" },
			!!s.lastPostError && { text: `Letzte Karte nicht gesendet: ${s.lastPostError}`, level: "error" },
		], s.chatMode === "estrichOne" && !s.graphConfigured
			? "Nicht eingerichtet – der eingebaute Chat arbeitet wie bisher"
			: `Chat-Modus: ${chatModeLabels[s.chatMode]} · ${s.linkedBauakten} Bauakten verknüpft`);
	}

	private applyHints(s: TeamsSettings | null): void {
		if (!s) return;
		const m = this.model();
		m.setProperty("/modeHint", modeHints[s.chatMode]);
		m.setProperty("/certificateText", s.certificateThumbprint
			? `Gespeichert: Fingerabdruck ${s.certificateThumbprint}${s.certificateExpiresAt ? `, gültig bis ${dateTime(s.certificateExpiresAt)}` : ""}` : "");
		m.setProperty("/lastTestText", lastTestText(s));
		m.setProperty("/lastPostText", s.lastPostAt ? `${dateTime(s.lastPostAt)}${s.lastPostError ? ` – ${s.lastPostError}` : " – gesendet"}` : "");
	}

	private applyUsers(users: TeamsUserMapping[]): void {
		this.model().setProperty("/users", users.filter((u) => u.active).map((u) => {
			const state = mappingState(u);
			return { ...u, stateText: state.text, state: state.state };
		}));
	}

	private async save(): Promise<TeamsSettings | undefined> {
		const m = this.model();
		const s = m.getProperty("/s") as TeamsSettings;
		const team = String(m.getProperty("/team") ?? "").trim();
		const previousTeam = s.siteTeamUrl ?? s.siteTeamId ?? "";
		const saved = await this.run(() => put<TeamsSettings>("settings/integrations/teams", {
			chatMode: s.chatMode, tenantId: s.tenantId?.trim() || null, clientId: s.clientId?.trim() || null, authKind: s.authKind,
			clientSecret: String(m.getProperty("/secret") ?? "") || null, certificatePem: String(m.getProperty("/certificate") ?? "") || null,
			siteTeam: team === previousTeam ? null : team, defaultLinkKind: s.defaultLinkKind, autoLink: s.autoLink, syncMembers: s.syncMembers,
			postVia: s.postVia, webhookUrl: String(m.getProperty("/webhook") ?? "") || null, postDefects: s.postDefects, postAcceptance: s.postAcceptance,
			postSchedule: s.postSchedule, version: s.version, closeAction: s.closeAction ?? "archive", chatsViaGraph: s.chatsViaGraph === true,
			rolloutNotice: s.rolloutNotice === true, rolloutNoticeText: s.rolloutNoticeText?.trim() || null,
		}));
		if (saved) {
			this.apply(saved);
			// Navigation, Kopfzeile und Bauakte richten sich nach dem Chat-Modus.
			void session.reloadPermissions();
		}
		return saved;
	}

	async onSave(): Promise<void> {
		if (await this.save()) this.toast("Gespeichert");
	}

	async onTest(): Promise<void> {
		if (!await this.save()) return;
		const result = await this.run(() => post<TeamsTestResponse>("settings/integrations/teams/test"));
		if (!result) return;
		this.model().setProperty("/steps", result.steps);
		this.model().setProperty("/tab", "connection");
		if (result.ok) this.toast("Verbindung zu Microsoft 365 in Ordnung");
		const s = await this.run(() => get<TeamsSettings>("settings/integrations/teams"));
		if (s) this.apply(s);
	}

	async onMatch(): Promise<void> {
		const result = await this.run(() => post<{ matched: number; notFound: number; manual: number; skipped: number }>("settings/integrations/teams/users/match"));
		if (!result) return;
		MessageBox.information(`${result.matched} zugeordnet, ${result.notFound} ohne passendes Konto, ${result.manual} von Hand gesetzt (unverändert), `
			+ `${result.skipped} ohne E-Mail-Adresse.`, { title: "Konten zugeordnet" });
		await this.load();
	}

	async onEditUser(e: Event): Promise<void> {
		const row = (e.getSource() as Control).getBindingContext("view")?.getObject() as TeamsUserMapping;
		const account = await prompt(`Microsoft-Konto für ${row.name}`, "UPN, E-Mail-Adresse oder Objekt-ID", {
			initial: row.userPrincipalName ?? "", submitText: "Zuordnen", required: false,
			intro: "Leer lassen, um wieder automatisch über die E-Mail-Adresse zuzuordnen.",
		});
		if (account === null) return;
		const saved = await this.run(() => put<TeamsUserMapping>(`settings/integrations/teams/users/${row.userId}`, { account: account.trim() || null }),
			"Zuordnung gespeichert");
		if (saved) await this.load();
	}

	async onWebhookTest(): Promise<void> {
		const url = String(this.model().getProperty("/webhook") ?? "").trim();
		const result = await this.run(() => post<TeamsTestResponse>("settings/integrations/teams/webhook/test", { url: url || null }));
		if (!result) return;
		if (result.ok) MessageBox.success("Die Testkarte wurde vom Workflow angenommen. Bitte im Kanal nachsehen.", { title: "Testkarte gesendet" });
		else MessageBox.error(result.steps[0]?.detail ?? "Unbekannter Fehler", { title: "Testkarte nicht gesendet" });
	}

	async onConnectSender(): Promise<void> {
		if (!await this.save()) return;
		const start = await this.run(() => post<TeamsSenderStart>("settings/integrations/teams/sender/start"));
		if (!start) return;
		const status = new Text({ text: "Warte auf die Anmeldung …" });
		const dialog = new Dialog({
			title: "Dienstkonto verbinden",
			contentWidth: "30rem",
			content: [new VBox({
				items: [
					new Text({ text: "1. Diese Seite öffnen und mit dem Dienstkonto anmelden (z. B. estrichone@firma.de):" }),
					new Link({ text: start.verificationUri, href: start.verificationUri, target: "_blank" }),
					new Text({ text: "2. Diesen Code eingeben:" }).addStyleClass("sapUiSmallMarginTop"),
					new Title({ text: start.userCode, level: "H2", titleStyle: "H2" }),
					new Text({ text: "Das Konto braucht eine Teams-Lizenz. Es sendet nur Karten (ChannelMessage.Send, ChatMessage.Send)." })
						.addStyleClass("sapUiSmallMarginTop"),
					new BusyIndicator({ size: "1rem" }).addStyleClass("sapUiSmallMarginTop"),
					status,
				],
			}).addStyleClass("sapUiSmallMargin")],
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => { clearTimeout(this.pollTimer); dialog.destroy(); },
		});
		this.getView()?.addDependent(dialog);
		dialog.open();
		const expires = new Date(start.expiresAt).getTime();
		const poll = async (): Promise<void> => {
			if (!dialog.isOpen()) return;
			if (Date.now() > expires) { status.setText("Der Code ist abgelaufen. Bitte neu starten."); return; }
			try {
				const state = await post<TeamsSenderState>("settings/integrations/teams/sender/poll");
				if (state.connected) {
					dialog.close();
					this.toast(`Dienstkonto verbunden: ${state.senderName ?? ""}`);
					void this.load();
					return;
				}
			} catch (error) {
				dialog.close();
				this.showError(error, "Dienstkonto nicht verbunden");
				return;
			}
			this.pollTimer = setTimeout(() => void poll(), Math.max(3, start.intervalSeconds) * 1000);
		};
		this.pollTimer = setTimeout(() => void poll(), Math.max(3, start.intervalSeconds) * 1000);
	}

	async onDisconnectSender(): Promise<void> {
		if (!await this.confirm("Dienstkonto trennen? Karten über Microsoft Graph werden dann nicht mehr gesendet.", { action: "Trennen", danger: true })) return;
		await this.run(() => del("settings/integrations/teams/sender"), "Dienstkonto getrennt");
		await this.load();
	}
}
