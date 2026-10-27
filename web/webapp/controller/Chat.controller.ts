import BaseController from "./BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import type HTML from "sap/ui/core/HTML";
import type TextArea from "sap/m/TextArea";
import type ScrollContainer from "sap/m/ScrollContainer";
import type ListItemBase from "sap/m/ListItemBase";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import Label from "sap/m/Label";
import List from "sap/m/List";
import Select from "sap/m/Select";
import MultiComboBox from "sap/m/MultiComboBox";
import StandardListItem from "sap/m/StandardListItem";
import SearchField from "sap/m/SearchField";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import Item from "sap/ui/core/Item";
import encodeXML from "sap/base/security/encodeXML";
import type { RouteMatch } from "../util/router";
import { currentRoute } from "../util/navigation";
import { uuidv7 } from "../util/ids";
import {
	accentOf, chatStatus, clock, contacts, conversation, conversations, createGroup, crews, dayKey, dayLabel, deleteConversation, deleteMessage,
	initialsOf, listTime, markRead, messages, mute, previewText, send, startDirect, startsRun, updateGroup,
	type ChatConversation, type ChatMemberState, type ChatMessage, type ChatPerson,
} from "../util/chat";

interface Row extends ChatConversation {
	initials: string;
	icon: string;
	accent: string;
	time: string;
	preview: string;
}

interface LocalMessage extends ChatMessage {
	pending?: boolean;
	failed?: boolean;
}

const LIST_POLL = 15_000;
const ACTIVE_POLL = 4_000;

const ICON_CHECK = '<svg viewBox="0 0 16 11" aria-hidden="true"><path d="M1 5.5l3.5 3.5L11 2.5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const ICON_CHECKS = '<svg viewBox="0 0 20 11" aria-hidden="true"><path d="M1 5.5l3.5 3.5L11 2.5M8.5 8.4l.6.6L15.5 2.5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const ICON_CLOCK = '<svg viewBox="0 0 12 12" aria-hidden="true"><circle cx="6" cy="6" r="4.7" fill="none" stroke="currentColor" stroke-width="1.2"/><path d="M6 3.4V6l1.8 1.2" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>';

/**
 * Chat (Modul): Unterhaltungsliste links, Verlauf rechts – wie ein Messenger, nur Text. Neue Nachrichten werden
 * regelmäßig abgefragt (geöffnete Unterhaltung alle 4 s, Liste alle 15 s); Lesestand und Häkchen kommen vom Server.
 * @namespace estrichone.controller
 */
export default class Chat extends BaseController {
	private items: LocalMessage[] = [];
	private members: ChatMemberState[] = [];
	private hasMore = false;
	private listTimer: ReturnType<typeof setInterval> | undefined;
	private activeTimer: ReturnType<typeof setInterval> | undefined;
	private loadingActive = false;
	private stickToBottom = true;

	onInit(): void {
		this.local({
			list: [] as Row[], filtered: [] as Row[], query: "", activeId: "", active: null as null | Record<string, unknown>, draft: "", loaded: false,
			canAdmin: false, retentionDays: 90, retentionHint: "",
		});
		const html = this.byId("chatMessages") as HTML;
		html.attachAfterRendering(() => {
			const dom = html.getDomRef() as HTMLElement | null;
			if (!dom) return;
			if (!dom.dataset.bound) {
				dom.dataset.bound = "1";
				dom.addEventListener("click", (event) => this.onLogClick(event));
			}
			this.renderLog(false);
		});
		const input = this.byId("chatInput") as TextArea;
		input.addEventDelegate({
			onkeydown: (event: KeyboardEvent) => {
				if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
					event.preventDefault();
					void this.onSend();
				}
			},
		});
		const scroll = this.byId("chatScroll") as ScrollContainer;
		scroll.addEventDelegate({
			onAfterRendering: () => {
				const dom = scroll.getDomRef() as HTMLElement | null;
				if (dom && !dom.dataset.bound) {
					dom.dataset.bound = "1";
					dom.addEventListener("scroll", () => {
						this.stickToBottom = dom.scrollHeight - dom.scrollTop - dom.clientHeight < 80;
					});
				}
			},
		});
		document.addEventListener("visibilitychange", () => {
			if (document.visibilityState === "visible" && this.isCurrent()) void this.refreshActive();
		});
	}

	onExit(): void {
		this.stopTimers();
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const id = match.params.id ?? "";
		const m = this.model();
		this.startTimers();
		const status = await chatStatus().catch(() => null);
		if (status) {
			m.setProperty("/canAdmin", status.canAdmin);
			m.setProperty("/retentionDays", status.retentionDays);
			m.setProperty("/retentionHint", `Verschlüsselt gespeichert · Löschung nach ${status.retentionDays} Tagen`);
		}
		await this.loadList();
		if (id !== m.getProperty("/activeId")) {
			await this.open(id);
		} else if (id) {
			await this.refreshActive();
		}
	}

	// ---- Liste ---------------------------------------------------------------------------------------------------

	private isCurrent(): boolean {
		return currentRoute()?.route.name.startsWith("chat") ?? false;
	}

	private startTimers(): void {
		this.listTimer ??= setInterval(() => {
			if (!this.isCurrent()) return this.stopTimers();
			void this.loadList();
		}, LIST_POLL);
		this.activeTimer ??= setInterval(() => {
			if (!this.isCurrent()) return this.stopTimers();
			if (document.visibilityState === "visible") void this.refreshActive();
		}, ACTIVE_POLL);
	}

	private stopTimers(): void {
		clearInterval(this.listTimer);
		clearInterval(this.activeTimer);
		this.listTimer = undefined;
		this.activeTimer = undefined;
	}

	private toRow(c: ChatConversation): Row {
		return {
			...c,
			initials: c.kind === "group" ? "" : initialsOf(c.title),
			icon: c.kind === "group" ? "sap-icon://group" : "",
			accent: accentOf(c.kind === "group" ? c.id : c.partnerUserId ?? c.id),
			time: listTime(c.lastMessage?.createdAt ?? c.lastActivityAt),
			preview: previewText(c).replace(/^Du: /, "Sie: "),
		};
	}

	private async loadList(): Promise<void> {
		const m = this.model();
		try {
			const list = (await conversations()).map((c) => this.toRow(c));
			const activeId = m.getProperty("/activeId") as string;
			// Geöffnete Unterhaltung gilt als gelesen, sobald sie sichtbar ist.
			for (const row of list) if (row.id === activeId && document.visibilityState === "visible") row.unreadCount = 0;
			m.setProperty("/list", list);
			m.setProperty("/loaded", true);
			this.applyFilter();
			const active = list.find((r) => r.id === activeId);
			if (active) this.setActive(active);
		} catch (error) {
			if (!m.getProperty("/loaded")) this.showError(error);
		}
	}

	private applyFilter(): void {
		const m = this.model();
		const q = (m.getProperty("/query") as string).trim().toLowerCase();
		const list = m.getProperty("/list") as Row[];
		m.setProperty("/filtered", q ? list.filter((r) => r.title.toLowerCase().includes(q) || r.members.some((p) => p.name.toLowerCase().includes(q))) : list);
	}

	onSearch(event: Event): void {
		this.model().setProperty("/query", String(event.getParameter("newValue" as never) ?? ""));
		this.applyFilter();
	}

	onSelect(event: Event): void {
		const item = event.getParameter("listItem" as never) as ListItemBase | undefined;
		const row = item?.getBindingContext("view")?.getObject() as Row | undefined;
		if (row) this.nav(`/chat/${row.id}`);
	}

	onBack(): void {
		this.nav("/chat");
	}

	// ---- Unterhaltung ----------------------------------------------------------------------------------------------

	private setActive(row: Row): void {
		this.model().setProperty("/active", {
			id: row.id, title: row.title, initials: row.initials, icon: row.icon, accent: row.accent, muted: row.muted, kind: row.kind,
			subtitle: row.kind === "group"
				? `${row.members.length} Mitglieder${row.crewId ? " · Kolonne" : ""}: ${row.members.map((p) => p.name.split(" ")[0]).join(", ")}`
				: "Einzelchat · nur Text",
		});
	}

	private async open(id: string): Promise<void> {
		const m = this.model();
		m.setProperty("/activeId", id);
		(this.byId("chatRoot") as Control | undefined)?.toggleStyleClass("eoChatHasActive", !!id);
		this.items = [];
		this.members = [];
		this.hasMore = false;
		this.renderLog(true);
		if (!id) {
			m.setProperty("/active", null);
			return;
		}
		const row = (m.getProperty("/list") as Row[]).find((r) => r.id === id);
		if (row) {
			this.setActive(row);
		} else {
			try {
				const detail = await conversation(id);
				this.setActive(this.toRow(detail.conversation));
			} catch (error) {
				this.showError(error);
				this.nav("/chat", true);
				return;
			}
		}
		m.setProperty("/draft", "");
		this.stickToBottom = true;
		await this.refreshActive(true);
		setTimeout(() => (this.byId("chatInput") as TextArea | undefined)?.focus(), 50);
	}

	private async refreshActive(initial = false): Promise<void> {
		const id = this.model().getProperty("/activeId") as string;
		if (!id || this.loadingActive) return;
		this.loadingActive = true;
		try {
			const server = this.items.filter((x) => !x.pending && !x.failed);
			const last = server[server.length - 1];
			const page = initial || !last ? await messages(id, { limit: 60 }) : await messages(id, { after: last.createdAt, limit: 200 });
			if (id !== this.model().getProperty("/activeId")) return;
			this.members = page.members;
			if (initial || !last) {
				this.hasMore = page.hasMore;
				this.items = [...page.items, ...this.items.filter((x) => x.pending || x.failed)];
			} else {
				const known = new Set(this.items.map((x) => x.id));
				const fresh = page.items.filter((x) => !known.has(x.id) && !this.items.some((p) => p.pending && p.clientMessageId && p.clientMessageId === x.clientMessageId));
				this.items = [...server, ...fresh, ...this.items.filter((x) => x.pending || x.failed)];
				// Häkchen eigener Nachrichten aus dem aktuellen Lesestand neu berechnen.
				this.restatus();
			}
			this.renderLog(initial);
			const unreadFromOthers = page.items.some((x) => !x.mine);
			if ((initial || unreadFromOthers) && document.visibilityState === "visible") {
				await markRead(id).catch(() => undefined);
				this.clearUnread(id);
			}
		} catch (error) {
			if (initial) this.showError(error);
		} finally {
			this.loadingActive = false;
		}
	}

	/** Status eigener Nachrichten aus dem Lese-/Zustellstand der übrigen Mitglieder. */
	private restatus(): void {
		for (const x of this.items) {
			if (!x.mine || x.deleted || x.pending || x.failed) continue;
			const others = this.members.filter((s) => s.userId !== x.senderUserId);
			const read = others.filter((s) => s.readUpTo && s.readUpTo >= x.createdAt).length;
			const delivered = others.filter((s) => s.deliveredUpTo && s.deliveredUpTo >= x.createdAt).length;
			x.readBy = read;
			x.recipients = others.length;
			x.status = others.length && read === others.length ? "read" : others.length && delivered === others.length ? "delivered" : "sent";
		}
	}

	private clearUnread(id: string): void {
		const m = this.model();
		const list = (m.getProperty("/list") as Row[]).map((r) => (r.id === id ? { ...r, unreadCount: 0 } : r));
		m.setProperty("/list", list);
		this.applyFilter();
		document.dispatchEvent(new CustomEvent("estrichone-chat"));
		document.dispatchEvent(new CustomEvent("estrichone-notices"));
	}

	// ---- Darstellung des Verlaufs ---------------------------------------------------------------------------------

	private renderLog(scrollToEnd: boolean): void {
		const dom = (this.byId("chatMessages") as HTML | undefined)?.getDomRef() as HTMLElement | null;
		if (!dom) return;
		const scroll = (this.byId("chatScroll") as ScrollContainer).getDomRef() as HTMLElement | null;
		const before = scroll ? scroll.scrollHeight - scroll.scrollTop : 0;
		const group = this.model().getProperty("/active/kind") === "group";
		const parts: string[] = [];
		if (this.hasMore) parts.push('<div class="eoChatMoreWrap"><button type="button" class="eoChatMore" data-action="more">Ältere Nachrichten laden</button></div>');
		else if (this.items.length) parts.push('<div class="eoChatStart">Beginn der Unterhaltung · nur Text · verschlüsselt gespeichert</div>');
		let previous: LocalMessage | undefined;
		for (const x of this.items) {
			if (!previous || dayKey(previous.createdAt) !== dayKey(x.createdAt)) {
				parts.push(`<div class="eoChatDay" role="separator"><span>${encodeXML(dayLabel(x.createdAt))}</span></div>`);
			}
			const run = startsRun(previous, x);
			const side = x.mine ? "eoChatMine" : "eoChatTheirs";
			const sender = group && !x.mine && run
				? `<div class="eoChatSender eoChatSender${accentOf(x.senderUserId).replace("Accent", "")}">${encodeXML(x.senderName)}</div>` : "";
			const text = x.deleted
				? '<span class="eoChatText eoChatDeleted">Nachricht gelöscht</span>'
				: `<span class="eoChatText">${encodeXML(x.text ?? "Nachricht nicht lesbar (Schlüssel fehlt)")}</span>`;
			let state = "";
			if (x.mine && !x.deleted) {
				if (x.failed) state = '<span class="eoChatTick eoChatFailed" title="Nicht gesendet">!</span>';
				else if (x.pending) state = `<span class="eoChatTick" title="Wird gesendet">${ICON_CLOCK}</span>`;
				else if (x.status === "read") state = `<span class="eoChatTick eoChatRead" title="${group ? "Von allen gelesen" : "Gelesen"}">${ICON_CHECKS}</span>`;
				else if (x.status === "delivered") state = `<span class="eoChatTick" title="Zugestellt${group && x.readBy ? ` · gelesen von ${x.readBy} von ${x.recipients}` : ""}">${ICON_CHECKS}</span>`;
				else state = `<span class="eoChatTick" title="Gesendet">${ICON_CHECK}</span>`;
			}
			const actions = x.failed
				? `<button type="button" class="eoChatAction" data-action="retry" data-id="${encodeXML(x.id)}">Erneut senden</button><button type="button" class="eoChatAction" data-action="discard" data-id="${encodeXML(x.id)}">Verwerfen</button>`
				: (x.mine || this.model().getProperty("/canAdmin")) && !x.deleted && !x.pending
					? `<button type="button" class="eoChatDel" data-action="delete" data-id="${encodeXML(x.id)}" title="Nachricht löschen" aria-label="Nachricht löschen">&#xe03d;</button>` : "";
			parts.push(`<div class="eoChatMsg ${side}${run ? " eoChatRun" : ""}" data-id="${encodeXML(x.id)}">${sender}`
				+ `<div class="eoChatRow">${x.mine ? actions : ""}<div class="eoChatBubble">${text}<span class="eoChatMeta">${encodeXML(clock(x.createdAt))}${state}</span></div>${x.mine ? "" : actions}</div></div>`);
			previous = x;
		}
		dom.innerHTML = parts.join("");
		if (!scroll) return;
		if (scrollToEnd || this.stickToBottom) {
			scroll.scrollTop = scroll.scrollHeight;
		} else {
			scroll.scrollTop = scroll.scrollHeight - before;
		}
	}

	private onLogClick(event: MouseEvent): void {
		const target = (event.target as HTMLElement).closest<HTMLElement>("[data-action]");
		if (!target) return;
		const id = target.dataset.id ?? "";
		switch (target.dataset.action) {
			case "more": void this.loadOlder(); break;
			case "delete": void this.removeMessage(id); break;
			case "retry": void this.retry(id); break;
			case "discard":
				this.items = this.items.filter((x) => x.id !== id);
				this.renderLog(false);
				break;
		}
	}

	private async loadOlder(): Promise<void> {
		const id = this.model().getProperty("/activeId") as string;
		const first = this.items.find((x) => !x.pending && !x.failed);
		if (!id || !first) return;
		try {
			const page = await messages(id, { before: first.createdAt, limit: 60 });
			this.hasMore = page.hasMore;
			this.items = [...page.items, ...this.items];
			this.stickToBottom = false;
			this.renderLog(false);
		} catch (error) {
			this.showError(error);
		}
	}

	private async removeMessage(id: string): Promise<void> {
		const item = this.items.find((x) => x.id === id);
		if (!item) return;
		const ok = await this.confirm(item.mine ? "Diese Nachricht für alle löschen?" : `Nachricht von ${item.senderName} für alle löschen? Das wird im Audit-Log vermerkt (ohne Inhalt).`,
			{ title: "Nachricht löschen", action: "Löschen", danger: true });
		if (!ok) return;
		try {
			await deleteMessage(id);
			item.deleted = true;
			item.text = null;
			this.renderLog(false);
		} catch (error) {
			this.showError(error);
		}
	}

	// ---- Senden ----------------------------------------------------------------------------------------------------

	async onSend(): Promise<void> {
		const m = this.model();
		const id = m.getProperty("/activeId") as string;
		const text = (m.getProperty("/draft") as string).trim();
		if (!id || !text) return;
		m.setProperty("/draft", "");
		const clientId = uuidv7();
		const local: LocalMessage = {
			id: `local-${clientId}`, conversationId: id, senderUserId: "", senderName: "", text, createdAt: new Date().toISOString(), mine: true,
			deleted: false, status: null, readBy: 0, recipients: 0, clientMessageId: clientId, pending: true,
		};
		this.items.push(local);
		this.stickToBottom = true;
		this.renderLog(true);
		await this.deliver(local);
	}

	private async deliver(local: LocalMessage): Promise<void> {
		try {
			const sent = await send(local.conversationId, local.text ?? "", local.clientMessageId!);
			const index = this.items.indexOf(local);
			if (index >= 0) this.items.splice(index, 1, sent);
			// Schon über die Abfrage eingetroffen: nur einmal zeigen.
			const seen = new Set<string>();
			this.items = this.items.filter((x) => (seen.has(x.id) ? false : (seen.add(x.id), true)));
			this.renderLog(true);
			void this.loadList();
		} catch (error) {
			local.pending = false;
			local.failed = true;
			this.renderLog(false);
			this.showError(error, "Nachricht nicht gesendet");
		}
	}

	private async retry(id: string): Promise<void> {
		const item = this.items.find((x) => x.id === id);
		if (!item) return;
		item.failed = false;
		item.pending = true;
		this.renderLog(false);
		await this.deliver(item);
	}

	// ---- Aktionen im Kopf ------------------------------------------------------------------------------------------

	async onMute(): Promise<void> {
		const m = this.model();
		const id = m.getProperty("/activeId") as string;
		const muted = !m.getProperty("/active/muted");
		try {
			await mute(id, muted);
			m.setProperty("/active/muted", muted);
			this.toast(muted ? "Stumm geschaltet – keine Hinweise für diese Unterhaltung" : "Hinweise wieder eingeschaltet");
			await this.loadList();
		} catch (error) {
			this.showError(error);
		}
	}

	async onMembers(): Promise<void> {
		const id = this.model().getProperty("/activeId") as string;
		let detail;
		try {
			detail = await conversation(id);
		} catch (error) {
			this.showError(error);
			return;
		}
		const list = new List({ showSeparators: "Inner" });
		for (const member of detail.members) {
			list.addItem(new StandardListItem({
				title: member.name,
				description: member.readUpTo ? `Zuletzt gelesen: ${listTime(member.readUpTo)}` : "Noch nichts gelesen",
				info: member.fromCrew ? "über Kolonne" : "",
				icon: "sap-icon://person-placeholder",
			}));
		}
		const dialog: Dialog = new Dialog({
			title: detail.conversation.kind === "group" ? `${detail.conversation.title} – Mitglieder` : "Teilnehmer",
			contentWidth: "28rem",
			content: [list],
			buttons: [
				...(detail.canManage ? [new Button({ text: "Gruppe bearbeiten", icon: "sap-icon://edit", press: () => { dialog.close(); void this.editGroup(detail.conversation, detail.members); } })] : []),
				new Button({ text: "Schließen", type: "Emphasized", press: () => dialog.close() }),
			],
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	async onDeleteConversation(): Promise<void> {
		const m = this.model();
		const id = m.getProperty("/activeId") as string;
		const ok = await this.confirm(`„${String(m.getProperty("/active/title"))}“ mit allen Nachrichten für alle Teilnehmer löschen?`,
			{ title: "Unterhaltung löschen", action: "Löschen", danger: true });
		if (!ok) return;
		try {
			await deleteConversation(id);
			this.toast("Unterhaltung gelöscht");
			this.nav("/chat", true);
		} catch (error) {
			this.showError(error);
		}
	}

	// ---- Neue Unterhaltungen ---------------------------------------------------------------------------------------

	async onNewDirect(): Promise<void> {
		let people: ChatPerson[];
		try {
			people = await contacts();
		} catch (error) {
			this.showError(error);
			return;
		}
		const list = new List({ noDataText: "Niemand gefunden.", mode: "None", showSeparators: "Inner" });
		const fill = (q: string) => {
			list.destroyItems();
			for (const p of people.filter((x) => x.name.toLowerCase().includes(q.toLowerCase()))) {
				list.addItem(new StandardListItem({
					title: p.name, icon: "sap-icon://person-placeholder", type: "Active",
					press: async () => {
						dialog.close();
						try {
							const c = await startDirect(p.id);
							await this.loadList();
							this.nav(`/chat/${c.id}`);
						} catch (error) {
							this.showError(error);
						}
					},
				}));
			}
		};
		const search = new SearchField({ placeholder: "Name suchen", liveChange: (e) => fill(String(e.getParameter("newValue") ?? "")) });
		const dialog: Dialog = new Dialog({
			title: "Neuer Einzelchat", contentWidth: "26rem", contentHeight: "26rem",
			content: [new VBox({ items: [search, list] }).addStyleClass("sapUiSmallMargin")],
			buttons: [new Button({ text: "Abbrechen", press: () => dialog.close() })],
			afterClose: () => dialog.destroy(),
		});
		fill("");
		this.v().addDependent(dialog);
		dialog.open();
	}

	async onNewGroup(): Promise<void> {
		await this.editGroup(null, []);
	}

	private async editGroup(existing: ChatConversation | null, current: ChatMemberState[]): Promise<void> {
		let people: ChatPerson[];
		let crewList: { id: string; name: string; members: number }[] = [];
		try {
			[people, crewList] = await Promise.all([contacts(), existing ? Promise.resolve([]) : crews().catch(() => [])]);
		} catch (error) {
			this.showError(error);
			return;
		}
		const me = current.find((s) => !people.some((p) => p.id === s.userId));
		const title = new Input({ value: existing?.title ?? "", maxLength: 120, placeholder: "z. B. Kolonne Nord, Büro und Bauleitung" });
		const crew = new Select({ width: "100%", items: [new Item({ key: "", text: "Keine – Personen frei wählen" }),
			...crewList.map((c) => new Item({ key: c.id, text: `${c.name} (${c.members})` }))] });
		const members = new MultiComboBox({ width: "100%", placeholder: "Personen hinzufügen",
			items: people.map((p) => new Item({ key: p.id, text: p.name })),
			selectedKeys: current.filter((s) => s.userId !== me?.userId).map((s) => s.userId) });
		crew.attachChange(() => {
			const name = crew.getSelectedItem()?.getText().replace(/ \(\d+\)$/, "") ?? "";
			if (crew.getSelectedKey() && !title.getValue()) title.setValue(name);
		});
		const dialog: Dialog = new Dialog({
			title: existing ? "Gruppe bearbeiten" : "Neue Gruppe",
			contentWidth: "30rem",
			content: [new VBox({ items: [
				new Label({ text: "Name", labelFor: title, required: true }), title,
				...(existing ? [] : [new Label({ text: "Kolonne (Mitglieder folgen der Kolonne)", labelFor: crew }).addStyleClass("sapUiSmallMarginTop"), crew]),
				new Label({ text: existing?.crewId ? "Weitere Personen (Kolonnenmitglieder kommen automatisch dazu)" : "Mitglieder", labelFor: members })
					.addStyleClass("sapUiSmallMarginTop"), members,
				new Text({ text: "Sie selbst sind immer Mitglied. Nur Personen mit Recht „Chat nutzen“ sind wählbar." }).addStyleClass("eoMuted sapUiSmallMarginTop"),
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: existing ? "Speichern" : "Anlegen", type: "Emphasized", press: async () => {
				const name = title.getValue().trim();
				if (!name) {
					title.setValueState("Error");
					title.setValueStateText("Bitte einen Namen angeben.");
					return;
				}
				try {
					const keys = members.getSelectedKeys();
					if (existing) {
						await updateGroup(existing.id, { title: name, memberIds: me ? [...keys, me.userId] : keys });
						dialog.close();
						await this.loadList();
						this.toast("Gruppe gespeichert");
					} else {
						const c = await createGroup(name, keys, crew.getSelectedKey() || null);
						dialog.close();
						await this.loadList();
						this.nav(`/chat/${c.id}`);
					}
				} catch (error) {
					this.showError(error);
				}
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}
}
