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
import CustomListItem from "sap/m/CustomListItem";
import SearchField from "sap/m/SearchField";
import Text from "sap/m/Text";
import TextAreaControl from "sap/m/TextArea";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import Switch from "sap/m/Switch";
import CheckBox from "sap/m/CheckBox";
import Avatar from "sap/m/Avatar";
import Link from "sap/m/Link";
import ActionSheet from "sap/m/ActionSheet";
import Popover from "sap/m/Popover";
import IconTabBar from "sap/m/IconTabBar";
import IconTabFilter from "sap/m/IconTabFilter";
import MessageStrip from "sap/m/MessageStrip";
import FormattedText from "sap/m/FormattedText";
import HTMLControl from "sap/ui/core/HTML";
import { session } from "../util/session";
import Item from "sap/ui/core/Item";
import IconPool from "sap/ui/core/IconPool";
import encodeXML from "sap/base/security/encodeXML";
import type { RouteMatch } from "../util/router";
import { currentRoute } from "../util/navigation";
import { uuidv7 } from "../util/ids";
import { indexOf, type MediaItem } from "../util/media";
import { openMediaViewer } from "../util/mediaViewer";
import { get, problemMessage } from "../util/api";
import MessageBox from "sap/m/MessageBox";
import { teamsHome } from "../util/teams";
import { pickTeamsChat } from "../util/teamsChat";
import { bauakteInput } from "../util/forms";
import {
	accentOf, attachmentUrl, blockUser, chatStatus, checkFile, clock, connectEvents, contacts, conversation, conversations, createGroup, crews, dayKey,
	dayLabel, deleteConversation, deleteMessage, editMessage, editMinutesLeft, formatBytes, forward, groupImageUrl, initialsOf, leaveGroup, listTime,
	mapUrl, markRead, media, mentionIdsIn, mentionQuery, message, messages, mute, MUTE_OPTIONS, mySettings, pin, previewText, QUICK_REACTIONS, react,
	removeGroupImage, richText, saveMySettings, search, send, setGroupImage, startDirect, startsRun, toBauakte, typing, updateGroup, uploadAttachment,
	type ChatAttachment, type ChatConversation, type ChatConversationDetail, type ChatEvent, type ChatLocation, type ChatMemberState, type ChatMessage,
	type ChatPerson, type ChatStatus,
} from "../util/chat";

interface Row extends ChatConversation {
	initials: string;
	icon: string;
	accent: string;
	time: string;
	preview: string;
	kindIcon: string;
	kindText: string;
}

interface LocalMessage extends ChatMessage {
	pending?: boolean;
	failed?: boolean;
	localFiles?: PendingFile[];
}

interface PendingFile {
	key: string;
	file: File;
	label: string;
	preview: string;
	isImage: boolean;
	uploaded?: ChatAttachment;
}

interface SpeechRecognitionLike {
	lang: string;
	interimResults: boolean;
	continuous: boolean;
	start(): void;
	stop(): void;
	onresult: ((event: { resultIndex: number; results: ArrayLike<{ isFinal: boolean; 0: { transcript: string } }> }) => void) | null;
	onend: (() => void) | null;
	onerror: ((event: { error: string }) => void) | null;
}

const LIST_POLL = 15_000;
const ACTIVE_POLL = 4_000;
const LIVE_LIST_POLL = 60_000;
const LIVE_ACTIVE_POLL = 20_000;

const ICON_CHECK = '<svg viewBox="0 0 16 11" aria-hidden="true"><path d="M1 5.5l3.5 3.5L11 2.5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const ICON_CHECKS = '<svg viewBox="0 0 20 11" aria-hidden="true"><path d="M1 5.5l3.5 3.5L11 2.5M8.5 8.4l.6.6L15.5 2.5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const ICON_CLOCK = '<svg viewBox="0 0 12 12" aria-hidden="true"><circle cx="6" cy="6" r="4.7" fill="none" stroke="currentColor" stroke-width="1.2"/><path d="M6 3.4V6l1.8 1.2" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>';

/** Zeichen eines SAP-Icons für die HTML-Darstellung des Verlaufs. */
function glyph(name: string): string {
	const info = IconPool.getIconInfo(name) as { content?: string } | undefined;
	return encodeXML(info?.content ?? "");
}

function speechRecognition(): (new () => SpeechRecognitionLike) | null {
	const w = window as unknown as { SpeechRecognition?: new () => SpeechRecognitionLike; webkitSpeechRecognition?: new () => SpeechRecognitionLike };
	return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

function storage(): Storage | null {
	try {
		return window.localStorage;
	} catch {
		return null;
	}
}

/**
 * Chat (Modul) wie ein Messenger: Unterhaltungsliste mit Suche, Verlauf mit Zitaten, Reaktionen, Anhängen, Standort,
 * Erwähnungen, Bearbeiten/Löschen für alle, Weiterleiten, Anheften; Gruppeninfo mit Admins, Bild und Beschreibung;
 * Echtzeit über Server-Sent Events (sonst Abfrage alle 4 s/15 s), Diktat über die Web Speech API, falls vorhanden.
 * @namespace estrichone.controller
 */
export default class Chat extends BaseController {
	private items: LocalMessage[] = [];
	private members: ChatMemberState[] = [];
	private status: ChatStatus | null = null;
	private hasMore = false;
	private listTimer: ReturnType<typeof setTimeout> | undefined;
	private activeTimer: ReturnType<typeof setTimeout> | undefined;
	private listDebounce: ReturnType<typeof setTimeout> | undefined;
	private searchDebounce: ReturnType<typeof setTimeout> | undefined;
	private typingTimer: ReturnType<typeof setTimeout> | undefined;
	private lastTypingSent = 0;
	private loadingActive = false;
	private stickToBottom = true;
	private disconnect: (() => void) | null = null;
	private live = false;
	private openReactions = "";
	private replyTo: LocalMessage | null = null;
	private editing: LocalMessage | null = null;
	private recognition: SpeechRecognitionLike | null = null;
	private dictationBase = "";
	private mentionPopover: Popover | null = null;
	private privacyShown = false;

	onInit(): void {
		this.local({
			list: [] as Row[], filtered: [] as Row[], hits: [] as unknown[], searching: false, query: "", activeId: "", active: null as null | Record<string, unknown>,
			draft: "", loaded: false, canAdmin: false, retentionDays: 90, retentionHint: "", live: false, typing: "", pinned: [] as ChatMessage[],
			pinnedPreview: "", compose: { mode: "", title: "", preview: "" }, pending: [] as PendingFile[], attachmentsEnabled: true,
			canDictate: !!speechRecognition(), listening: false, blocked: false, hint: "",
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
				if (event.key === "Enter" && !event.shiftKey && !event.isComposing && !this.mentionPopover?.isOpen()) {
					event.preventDefault();
					void this.onSend();
				} else if (event.key === "Escape" && (this.replyTo || this.editing)) {
					this.onCancelContext();
				} else if (event.key === "ArrowUp" && !this.model().getProperty("/draft")) {
					const last = [...this.items].reverse().find((x) => x.mine && x.canEdit && !x.deleted);
					if (last) {
						event.preventDefault();
						this.startEdit(last);
					}
				}
			},
			onAfterRendering: () => {
				const dom = input.getFocusDomRef() as HTMLTextAreaElement | null;
				if (dom && !dom.dataset.paste) {
					dom.dataset.paste = "1";
					dom.addEventListener("paste", (event) => {
						const files = [...(event.clipboardData?.files ?? [])];
						if (files.length) {
							event.preventDefault();
							this.addFiles(files);
						}
					});
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
		const drop = this.byId("chatDrop") as Control;
		drop.addEventDelegate({
			onAfterRendering: () => {
				const dom = drop.getDomRef() as HTMLElement | null;
				if (!dom || dom.dataset.drop) return;
				dom.dataset.drop = "1";
				const has = (e: DragEvent) => [...(e.dataTransfer?.types ?? [])].includes("Files");
				dom.addEventListener("dragover", (e) => {
					if (!has(e) || !this.canAttach()) return;
					e.preventDefault();
					dom.classList.add("eoChatDragging");
				});
				dom.addEventListener("dragleave", (e) => {
					if (e.target === dom || !dom.contains(e.relatedTarget as Node)) dom.classList.remove("eoChatDragging");
				});
				dom.addEventListener("drop", (e) => {
					dom.classList.remove("eoChatDragging");
					if (!has(e) || !this.canAttach()) return;
					e.preventDefault();
					this.addFiles([...(e.dataTransfer?.files ?? [])]);
				});
			},
		});
		document.addEventListener("visibilitychange", () => {
			if (document.visibilityState === "visible" && this.isCurrent()) void this.refreshActive();
		});
	}

	onExit(): void {
		this.stopTimers();
		this.disconnect?.();
		this.recognition?.stop();
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		const id = match.params.id ?? "";
		const m = this.model();
		this.connect();
		this.startTimers();
		const status = await chatStatus().catch(() => null);
		if (status) {
			this.status = status;
			m.setProperty("/canAdmin", status.canAdmin);
			m.setProperty("/retentionDays", status.retentionDays);
			m.setProperty("/blocked", !!status.blocked);
			m.setProperty("/attachmentsEnabled", status.attachmentsEnabled !== false);
			m.setProperty("/retentionHint", `Verschlüsselt gespeichert · Löschung nach ${status.retentionDays} Tagen`);
			// Modus „Microsoft Teams“: nur lesen und exportieren.
			m.setProperty("/archive", status.readOnly === true);
			if (!status.privacyAccepted && !this.privacyShown) this.showPrivacy(status);
		}
		await this.loadList();
		if (id !== m.getProperty("/activeId")) {
			await this.open(id, match.query?.get("nachricht") ?? undefined);
		} else if (id) {
			await this.refreshActive();
		}
	}

	// ---- Echtzeit und Abfrage ------------------------------------------------------------------------------------

	private isCurrent(): boolean {
		return currentRoute()?.route.name.startsWith("chat") ?? false;
	}

	private connect(): void {
		if (this.disconnect) return;
		this.disconnect = connectEvents((event) => this.onEvent(event), (live) => {
			this.live = live;
			this.model().setProperty("/live", live);
		});
	}

	private onEvent(event: ChatEvent): void {
		if (!this.isCurrent()) {
			this.disconnect?.();
			this.disconnect = null;
			return;
		}
		const activeId = this.model().getProperty("/activeId") as string;
		const isActive = event.conversationId === activeId;
		switch (event.type) {
			case "typing":
				if (isActive && event.userName) this.showTyping(event.userName);
				return;
			case "message":
				if (isActive) {
					this.model().setProperty("/typing", "");
					void this.refreshActive();
				}
				this.scheduleList();
				return;
			case "message.updated":
			case "reaction":
				if (isActive && event.messageId) void this.reloadMessage(event.messageId);
				if (event.type === "message.updated" && isActive) void this.loadDetail();
				return;
			case "message.deleted":
				if (isActive && event.messageId) {
					const item = this.items.find((x) => x.id === event.messageId);
					if (item) Object.assign(item, { deleted: true, text: null, attachments: [], reactions: [], location: null, pinnedAt: null });
					this.renderLog(false);
					void this.loadDetail();
				}
				this.scheduleList();
				return;
			case "read":
				if (isActive) void this.refreshActive();
				return;
			case "conversation":
				this.scheduleList();
				if (isActive) void this.loadDetail();
				return;
			case "conversation.removed":
				this.scheduleList();
				if (isActive) {
					this.toast("Diese Unterhaltung wurde gelöscht oder Sie wurden entfernt");
					this.nav("/chat", true);
				}
				return;
		}
	}

	private scheduleList(): void {
		clearTimeout(this.listDebounce);
		this.listDebounce = setTimeout(() => void this.loadList(), 300);
	}

	private showTyping(name: string): void {
		const m = this.model();
		m.setProperty("/typing", m.getProperty("/active/kind") === "group" ? `${name.split(" ")[0]} schreibt …` : "schreibt …");
		clearTimeout(this.typingTimer);
		this.typingTimer = setTimeout(() => m.setProperty("/typing", ""), 5000);
	}

	private startTimers(): void {
		const tickList = () => {
			this.listTimer = setTimeout(() => {
				if (!this.isCurrent()) return this.stopTimers();
				void this.loadList();
				tickList();
			}, this.live ? LIVE_LIST_POLL : LIST_POLL);
		};
		const tickActive = () => {
			this.activeTimer = setTimeout(() => {
				if (!this.isCurrent()) return this.stopTimers();
				if (document.visibilityState === "visible") void this.refreshActive();
				tickActive();
			}, this.live ? LIVE_ACTIVE_POLL : ACTIVE_POLL);
		};
		if (!this.listTimer) tickList();
		if (!this.activeTimer) tickActive();
	}

	private stopTimers(): void {
		clearTimeout(this.listTimer);
		clearTimeout(this.activeTimer);
		this.listTimer = undefined;
		this.activeTimer = undefined;
	}

	// ---- Liste ---------------------------------------------------------------------------------------------------

	private toRow(c: ChatConversation): Row {
		const group = c.kind === "group";
		return {
			...c,
			initials: group ? initialsOf(c.title) : initialsOf(c.title),
			icon: group ? groupImageUrl(c) || (c.bauakteId ? "sap-icon://building" : c.announcementOnly ? "sap-icon://marketing-campaign" : "sap-icon://group") : "",
			accent: accentOf(group ? c.id : c.partnerUserId ?? c.id),
			time: listTime(c.lastMessage?.createdAt ?? c.lastActivityAt),
			preview: previewText(c).replace(/^Du: /, "Sie: "),
			kindIcon: c.bauakteId ? "sap-icon://building" : c.announcementOnly ? "sap-icon://marketing-campaign" : c.crewId ? "sap-icon://shipping-status" : "",
			kindText: c.bauakteId ? "Baustellen-Gruppe" : c.announcementOnly ? "Ankündigungsgruppe" : c.crewId ? "Kolonnengruppe" : "",
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
		const m = this.model();
		const q = String(event.getParameter("newValue" as never) ?? "").trim();
		m.setProperty("/query", q);
		this.applyFilter();
		clearTimeout(this.searchDebounce);
		if (q.length < 2) {
			m.setProperty("/hits", []);
			m.setProperty("/searching", false);
			return;
		}
		m.setProperty("/searching", true);
		this.searchDebounce = setTimeout(async () => {
			try {
				const result = await search(q);
				if (m.getProperty("/query") !== q) return;
				m.setProperty("/hits", result.items.map((h) => ({
					...h, time: listTime(h.createdAt), html: `<strong>${encodeXML(h.senderName.split(" ")[0])}:</strong> ${this.highlight(h.snippet, q)}`,
				})));
			} catch {
				m.setProperty("/hits", []);
			} finally {
				m.setProperty("/searching", false);
			}
		}, 350);
	}

	private highlight(text: string, q: string): string {
		const safe = encodeXML(text);
		const needle = encodeXML(q);
		const index = safe.toLowerCase().indexOf(needle.toLowerCase());
		return index < 0 ? safe : `${safe.slice(0, index)}<em>${safe.slice(index, index + needle.length)}</em>${safe.slice(index + needle.length)}`;
	}

	onHit(event: Event): void {
		const hit = (event.getSource() as Control).getBindingContext("view")?.getObject() as { conversationId: string; messageId: string } | undefined;
		if (hit) this.jumpTo(hit.conversationId, hit.messageId);
	}

	private jumpTo(conversationId: string, messageId: string): void {
		if (conversationId === this.model().getProperty("/activeId")) void this.scrollToMessage(messageId);
		else this.nav(`/chat/${conversationId}?nachricht=${messageId}`);
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
		const m = this.model();
		const previous = m.getProperty("/active") as Record<string, unknown> | null;
		m.setProperty("/active", {
			...(previous?.id === row.id ? previous : {}),
			id: row.id, title: row.title, initials: row.initials, icon: row.icon, accent: row.accent, muted: row.muted, kind: row.kind,
			bauakteId: row.bauakteId ?? null, canPost: row.canPost !== false, isAdmin: !!row.isAdmin, announcementOnly: !!row.announcementOnly,
			mutedUntil: row.mutedUntil ?? null,
			subtitle: row.kind === "group"
				? `${row.members.length} Mitglieder${row.bauakteId ? " · Baustelle" : row.crewId ? " · Kolonne" : ""}: ${row.members.map((p) => p.name.split(" ")[0]).join(", ")}`
				: row.muted ? "Einzelchat · stumm geschaltet" : "Einzelchat",
		});
		m.setProperty("/hint", row.canPost === false ? "" : this.status?.attachmentsEnabled === false
			? "Enter senden · Umschalt+Enter neue Zeile · @ erwähnt Mitglieder"
			: "Enter senden · Umschalt+Enter neue Zeile · @ erwähnt · Fotos/PDF hineinziehen oder einfügen");
	}

	private async open(id: string, focusMessage?: string): Promise<void> {
		const m = this.model();
		this.saveDraft();
		m.setProperty("/activeId", id);
		(this.byId("chatRoot") as Control | undefined)?.toggleStyleClass("eoChatHasActive", !!id);
		this.items = [];
		this.members = [];
		this.hasMore = false;
		this.replyTo = null;
		this.editing = null;
		this.openReactions = "";
		m.setProperty("/pending", []);
		m.setProperty("/pinned", []);
		m.setProperty("/typing", "");
		this.updateContext();
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
		m.setProperty("/draft", storage()?.getItem(`eo.chat.draft.${id}`) ?? "");
		this.stickToBottom = true;
		await Promise.all([this.refreshActive(true), this.loadDetail()]);
		if (focusMessage) await this.scrollToMessage(focusMessage);
		setTimeout(() => (this.byId("chatInput") as TextArea | undefined)?.focus(), 50);
	}

	private saveDraft(): void {
		const m = this.model();
		const id = m.getProperty("/activeId") as string;
		if (!id || this.editing) return;
		const draft = (m.getProperty("/draft") as string) ?? "";
		try {
			if (draft.trim()) storage()?.setItem(`eo.chat.draft.${id}`, draft);
			else storage()?.removeItem(`eo.chat.draft.${id}`);
		} catch {
			// Entwurf ist Komfort.
		}
	}

	private async loadDetail(): Promise<void> {
		const id = this.model().getProperty("/activeId") as string;
		if (!id) return;
		try {
			const detail = await conversation(id);
			if (id !== this.model().getProperty("/activeId")) return;
			const pinned = detail.pinned ?? [];
			this.model().setProperty("/pinned", pinned);
			this.model().setProperty("/pinnedPreview", pinned.length ? this.snippet(pinned[0]) : "");
			this.model().setProperty("/active/bauakteTitle", detail.bauakteTitle ?? "");
		} catch {
			// Kopf bleibt aus der Liste.
		}
	}

	private snippet(x: ChatMessage): string {
		if (x.deleted) return "Nachricht gelöscht";
		const hint = x.attachments?.length ? (x.attachments.every((a) => a.kind === "photo") ? "Foto" : "Dokument") : x.location ? "Standort" : "";
		const text = (x.text ?? "").replace(/\s+/g, " ").trim();
		const full = [hint, text].filter(Boolean).join(" ");
		return full.length > 90 ? `${full.slice(0, 89)}…` : full;
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

	private async reloadMessage(id: string): Promise<void> {
		try {
			const fresh = await message(id);
			const index = this.items.findIndex((x) => x.id === id);
			if (index >= 0) {
				this.items.splice(index, 1, fresh);
				this.renderLog(false);
			}
		} catch {
			// Nicht mehr sichtbar.
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

	private async scrollToMessage(id: string): Promise<void> {
		for (let i = 0; i < 15 && !this.items.some((x) => x.id === id) && this.hasMore; i++) await this.loadOlder();
		const dom = (this.byId("chatMessages") as HTML).getDomRef() as HTMLElement | null;
		const target = dom?.querySelector<HTMLElement>(`[data-id="${CSS.escape(id)}"]`);
		if (!target) {
			this.toast("Nachricht nicht mehr vorhanden (gelöscht oder Löschfrist abgelaufen)");
			return;
		}
		this.stickToBottom = false;
		target.scrollIntoView({ block: "center", behavior: "smooth" });
		target.classList.add("eoChatFlash");
		setTimeout(() => target.classList.remove("eoChatFlash"), 1800);
	}

	// ---- Darstellung des Verlaufs ---------------------------------------------------------------------------------

	private memberNames(): string[] {
		return this.members.map((s) => s.name);
	}

	private renderAttachments(x: LocalMessage): string {
		const files = x.attachments ?? [];
		if (x.pending && x.localFiles?.length) {
			return `<div class="eoChatFiles">${x.localFiles.map((f) => f.isImage
				? `<div class="eoChatPhoto eoChatUploading"><img src="${encodeXML(f.preview)}" alt=""/></div>`
				: `<div class="eoChatDoc eoChatUploading"><span class="eoChatGlyph">${glyph("pdf-attachment")}</span><span class="eoChatDocName">${encodeXML(f.label)}</span></div>`).join("")}</div>`;
		}
		if (!files.length) return "";
		const photos = files.filter((a) => a.kind === "photo");
		const docs = files.filter((a) => a.kind === "document");
		const grid = photos.length
			? `<div class="eoChatPhotos eoChatPhotos${Math.min(photos.length, 4)}">${photos.slice(0, 4).map((a, i) => {
				const more = i === 3 && photos.length > 4 ? `<span class="eoChatPhotoMore">+${photos.length - 4}</span>` : "";
				return `<button type="button" class="eoChatPhoto" data-action="photo" data-id="${encodeXML(x.id)}" data-attachment="${encodeXML(a.id)}" aria-label="Foto öffnen"><img loading="lazy" src="${encodeXML(attachmentUrl(a.id, { thumbnail: a.hasThumbnail }))}" alt="${encodeXML(a.fileName)}"/>${more}</button>`;
			}).join("")}</div>` : "";
		const list = docs.map((a) => `<a class="eoChatDoc" href="${encodeXML(attachmentUrl(a.id))}" target="_blank" rel="noopener"><span class="eoChatGlyph">${glyph("pdf-attachment")}</span><span class="eoChatDocText"><span class="eoChatDocName">${encodeXML(a.fileName)}</span><span class="eoChatDocSize">PDF · ${encodeXML(formatBytes(a.sizeBytes))}</span></span><span class="eoChatGlyph eoChatDocDl" title="Herunterladen">${glyph("download")}</span></a>`).join("");
		return `<div class="eoChatFiles">${grid}${list}</div>`;
	}

	private renderLocation(location: ChatLocation): string {
		return `<a class="eoChatLocation" href="${encodeXML(mapUrl(location))}" target="_blank" rel="noopener noreferrer"><span class="eoChatGlyph">${glyph("map")}</span><span class="eoChatDocText"><span class="eoChatDocName">${encodeXML(location.label || "Standort")}</span><span class="eoChatDocSize">${location.latitude.toFixed(5)}, ${location.longitude.toFixed(5)} · Karte öffnen</span></span></a>`;
	}

	private renderLog(scrollToEnd: boolean): void {
		const dom = (this.byId("chatMessages") as HTML | undefined)?.getDomRef() as HTMLElement | null;
		if (!dom) return;
		const scroll = (this.byId("chatScroll") as ScrollContainer).getDomRef() as HTMLElement | null;
		const before = scroll ? scroll.scrollHeight - scroll.scrollTop : 0;
		const m = this.model();
		const group = m.getProperty("/active/kind") === "group";
		const canPost = m.getProperty("/active/canPost") !== false;
		const names = this.memberNames();
		const parts: string[] = [];
		if (this.hasMore) parts.push('<div class="eoChatMoreWrap"><button type="button" class="eoChatMore" data-action="more">Ältere Nachrichten laden</button></div>');
		else if (this.items.length) parts.push(`<div class="eoChatStart">Beginn der Unterhaltung · Nachrichten und Anhänge verschlüsselt gespeichert</div>`);
		let previous: LocalMessage | undefined;
		for (const x of this.items) {
			if (!previous || dayKey(previous.createdAt) !== dayKey(x.createdAt)) {
				parts.push(`<div class="eoChatDay" role="separator"><span>${encodeXML(dayLabel(x.createdAt))}</span></div>`);
			}
			const run = startsRun(previous, x);
			const side = x.mine ? "eoChatMine" : "eoChatTheirs";
			const id = encodeXML(x.id);
			const sender = group && !x.mine && run
				? `<div class="eoChatSender eoChatSender${accentOf(x.senderUserId).replace("Accent", "")}">${encodeXML(x.senderName)}</div>` : "";
			const forwarded = x.forwarded && !x.deleted ? `<div class="eoChatForwarded"><span class="eoChatGlyph eoChatGlyphInline">${glyph("forward")}</span> Weitergeleitet</div>` : "";
			const reply = x.replyTo && !x.deleted
				? `<button type="button" class="eoChatQuote" data-action="jump" data-id="${encodeXML(x.replyTo.id)}"><span class="eoChatQuoteName">${encodeXML(x.replyTo.senderName)}</span><span class="eoChatQuoteText">${encodeXML(x.replyTo.deleted ? "Nachricht gelöscht" : x.replyTo.text ?? (x.replyTo.attachment === "photo" ? "📷 Foto" : x.replyTo.attachment ? "📄 Dokument" : "…"))}</span></button>` : "";
			const body = x.deleted
				? `<span class="eoChatText eoChatDeleted"><span class="eoChatGlyph eoChatGlyphInline">${glyph("cancel")}</span> Nachricht gelöscht</span>`
				: `${this.renderAttachments(x)}${x.location ? this.renderLocation(x.location) : ""}${x.text !== null && x.text !== undefined ? `<span class="eoChatText">${richText(x.text, names)}</span>` : !x.attachments?.length && !x.location && !x.pending ? '<span class="eoChatText eoChatDeleted">Nachricht nicht lesbar (Schlüssel fehlt)</span>' : ""}`;
			let state = "";
			if (x.mine && !x.deleted) {
				if (x.failed) state = '<span class="eoChatTick eoChatFailed" title="Nicht gesendet">!</span>';
				else if (x.pending) state = `<span class="eoChatTick" title="Wird gesendet">${ICON_CLOCK}</span>`;
				else if (x.status === "read") state = `<span class="eoChatTick eoChatRead" title="${group ? "Von allen gelesen" : "Gelesen"}">${ICON_CHECKS}</span>`;
				else if (x.status === "delivered") state = `<span class="eoChatTick" title="Zugestellt${group && x.readBy ? ` · gelesen von ${x.readBy} von ${x.recipients}` : ""}">${ICON_CHECKS}</span>`;
				else state = `<span class="eoChatTick" title="Gesendet">${ICON_CHECK}</span>`;
			}
			const pinnedMark = x.pinnedAt && !x.deleted ? `<span class="eoChatPinMark" title="Angeheftet">${glyph("pushpin-off")}</span>` : "";
			const edited = x.editedAt && !x.deleted ? '<span class="eoChatEdited">bearbeitet</span>' : "";
			const meta = `<span class="eoChatMeta">${pinnedMark}${edited}${encodeXML(clock(x.createdAt))}${state}</span>`;
			const reactions = x.reactions?.length && !x.deleted
				? `<div class="eoChatReactions">${x.reactions.map((r) => `<button type="button" class="eoChatReaction${r.mine ? " eoChatReactionMine" : ""}" data-action="react" data-id="${id}" data-emoji="${encodeXML(r.emoji)}" title="${encodeXML(r.names.join(", "))}">${encodeXML(r.emoji)}${r.count > 1 ? `<span>${r.count}</span>` : ""}</button>`).join("")}</div>` : "";
			const picker = this.openReactions === x.id
				? `<div class="eoChatPicker" role="menu">${QUICK_REACTIONS.map((e) => `<button type="button" data-action="react" data-id="${id}" data-emoji="${encodeXML(e)}" aria-label="Reagieren mit ${encodeXML(e)}">${encodeXML(e)}</button>`).join("")}</div>` : "";
			let actions = "";
			if (x.failed) {
				actions = `<div class="eoChatFailedActions"><button type="button" class="eoChatAction" data-action="retry" data-id="${id}">Erneut senden</button><button type="button" class="eoChatAction" data-action="discard" data-id="${id}">Verwerfen</button></div>`;
			} else if (!x.deleted && !x.pending) {
				const tools: string[] = [];
				if (canPost) tools.push(`<button type="button" class="eoChatTool2" data-action="reply" data-id="${id}" title="Antworten" aria-label="Antworten">${glyph("response")}</button>`);
				tools.push(`<button type="button" class="eoChatTool2" data-action="picker" data-id="${id}" title="Reagieren" aria-label="Reagieren">${glyph("feedback")}</button>`);
				tools.push(`<button type="button" class="eoChatTool2" data-action="more" data-id="${id}" data-menu="1" title="Weitere Aktionen" aria-label="Weitere Aktionen">${glyph("overflow")}</button>`);
				actions = `<div class="eoChatTools">${tools.join("")}</div>`;
			}
			const mentioned = x.mentionedUserIds?.length && !x.mine && this.members.some((s) => x.mentionedUserIds!.includes(s.userId) && s.userId === this.myId()) ? " eoChatMentioned" : "";
			const rich = x.attachments?.some((a) => a.kind === "photo") && !x.text ? " eoChatBubblePhoto" : "";
			parts.push(`<div class="eoChatMsg ${side}${run ? " eoChatRun" : ""}${mentioned}" data-id="${id}">${sender}`
				+ `<div class="eoChatRow">${x.mine ? actions : ""}<div class="eoChatBubble${rich}">${forwarded}${reply}${body}${meta}</div>${x.mine ? "" : actions}</div>${reactions}${picker}</div>`);
			previous = x;
		}
		dom.innerHTML = parts.join("");
		if (!scroll) return;
		if (scrollToEnd || this.stickToBottom) {
			scroll.scrollTop = scroll.scrollHeight;
			// Bilder laden nach: danach erneut ans Ende.
			for (const img of dom.querySelectorAll("img")) img.addEventListener("load", () => { if (this.stickToBottom) scroll.scrollTop = scroll.scrollHeight; }, { once: true });
		} else {
			scroll.scrollTop = scroll.scrollHeight - before;
		}
	}

	private myId(): string {
		return session.userId ?? "";
	}

	private onLogClick(event: MouseEvent): void {
		const target = (event.target as HTMLElement).closest<HTMLElement>("[data-action]");
		if (!target) {
			if (this.openReactions) {
				this.openReactions = "";
				this.renderLog(false);
			}
			return;
		}
		const id = target.dataset.id ?? "";
		const item = this.items.find((x) => x.id === id);
		switch (target.dataset.action) {
			case "more":
				if (target.dataset.menu && item) this.openMenu(item, target);
				else void this.loadOlder();
				break;
			case "delete": if (item) void this.removeMessage(item); break;
			case "retry": void this.retry(id); break;
			case "discard":
				this.items = this.items.filter((x) => x.id !== id);
				this.renderLog(false);
				break;
			case "reply": if (item) this.startReply(item); break;
			case "picker":
				this.openReactions = this.openReactions === id ? "" : id;
				this.renderLog(false);
				break;
			case "react": if (item) void this.toggleReaction(item, target.dataset.emoji ?? ""); break;
			case "jump": void this.scrollToMessage(id); break;
			case "photo": if (item) this.openPhotos(item.attachments ?? [], target.dataset.attachment ?? ""); break;
		}
	}

	/** Weitere Aktionen zu einer Nachricht (ActionSheet an der Schaltfläche im Verlauf). */
	private openMenu(item: LocalMessage, anchor: HTMLElement): void {
		const m = this.model();
		const canPost = m.getProperty("/active/canPost") !== false;
		const canAdmin = !!m.getProperty("/canAdmin");
		const buttons: Button[] = [];
		const add = (text: string, icon: string, press: () => void, danger = false) =>
			buttons.push(new Button({ text, icon, press, type: danger ? "Reject" : "Default" }));
		if (canPost) add("Antworten", "sap-icon://response", () => this.startReply(item));
		if (item.text) add("Text kopieren", "sap-icon://copy", () => void navigator.clipboard?.writeText(item.text ?? "").then(() => this.toast("Kopiert")));
		add("Weiterleiten …", "sap-icon://forward", () => void this.forwardDialog(item));
		if (canPost) add(item.pinnedAt ? "Lösen" : "Anheften", "sap-icon://pushpin-off", () => void this.togglePin(item));
		if (item.attachments?.length && this.has("files.upload")) add("In Bauakte übernehmen …", "sap-icon://inspection", () => void this.copyToBauakte(item));
		if (item.mine && item.canEdit && item.text) add("Bearbeiten", "sap-icon://edit", () => this.startEdit(item));
		if (item.mine || canAdmin) add(item.mine ? "Für alle löschen" : "Entfernen (Moderation)", "sap-icon://delete", () => void this.removeMessage(item), true);
		const sheet = new ActionSheet({ placement: "Bottom", buttons, cancelButtonText: "Abbrechen", afterClose: () => sheet.destroy() });
		this.v().addDependent(sheet);
		sheet.openBy(anchor);
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

	private async removeMessage(item: LocalMessage): Promise<void> {
		const ok = await this.confirm(item.mine
			? "Diese Nachricht samt Anhängen für alle löschen?"
			: `Nachricht von ${item.senderName} für alle entfernen? Das wird im Audit-Log vermerkt (ohne Inhalt).`,
		{ title: item.mine ? "Für alle löschen" : "Nachricht entfernen", action: "Löschen", danger: true });
		if (!ok) return;
		try {
			await deleteMessage(item.id);
			Object.assign(item, { deleted: true, text: null, attachments: [], reactions: [], location: null, pinnedAt: null });
			this.renderLog(false);
			void this.loadDetail();
		} catch (error) {
			this.showError(error);
		}
	}

	private async toggleReaction(item: LocalMessage, emoji: string): Promise<void> {
		const mine = item.reactions?.find((r) => r.mine);
		this.openReactions = "";
		try {
			const fresh = await react(item.id, mine?.emoji === emoji ? null : emoji);
			const index = this.items.indexOf(item);
			if (index >= 0) this.items.splice(index, 1, fresh);
			this.renderLog(false);
		} catch (error) {
			this.showError(error);
		}
	}

	private async togglePin(item: LocalMessage): Promise<void> {
		try {
			await pin(item.id, !item.pinnedAt);
			item.pinnedAt = item.pinnedAt ? null : new Date().toISOString();
			this.renderLog(false);
			await this.loadDetail();
			this.toast(item.pinnedAt ? "Angeheftet" : "Gelöst");
		} catch (error) {
			this.showError(error);
		}
	}

	onPinnedJump(): void {
		const pinned = this.model().getProperty("/pinned") as ChatMessage[];
		if (!pinned.length) return;
		if (pinned.length === 1) {
			void this.scrollToMessage(pinned[0].id);
			return;
		}
		const list = new List({ showSeparators: "Inner" });
		for (const x of pinned) {
			list.addItem(new StandardListItem({ title: this.snippet(x), description: `${x.senderName} · ${listTime(x.createdAt)}`, type: "Active",
				press: () => { dialog.close(); void this.scrollToMessage(x.id); } }));
		}
		const dialog: Dialog = new Dialog({ title: "Angeheftete Nachrichten", contentWidth: "30rem", content: [list],
			endButton: new Button({ text: "Schließen", press: () => dialog.close() }), afterClose: () => dialog.destroy() });
		this.v().addDependent(dialog);
		dialog.open();
	}

	/** Fotos im Detailfenster (volle Größe, Vor/Zurück, Herunterladen). */
	private openPhotos(attachments: ChatAttachment[], startId: string): void {
		const photos = attachments.filter((a) => a.kind === "photo");
		const items: MediaItem[] = photos.map((a) => ({
			id: a.id, name: a.fileName, kind: "image", src: attachmentUrl(a.id), thumb: attachmentUrl(a.id, { thumbnail: a.hasThumbnail }),
			download: attachmentUrl(a.id, { download: true }),
			meta: `${formatBytes(a.sizeBytes)}${a.width ? ` · ${a.width} × ${a.height}` : ""} · ${listTime(a.createdAt)}`,
		}));
		openMediaViewer(items, indexOf(items, startId), { title: "Fotos" });
	}

	// ---- Antworten, Bearbeiten, Erwähnen ---------------------------------------------------------------------------

	private startReply(item: LocalMessage): void {
		this.editing = null;
		this.replyTo = item;
		this.updateContext();
		(this.byId("chatInput") as TextArea).focus();
	}

	private startEdit(item: LocalMessage): void {
		const minutes = editMinutesLeft(item.createdAt, this.status?.editWindowMinutes ?? 15);
		if (!minutes) {
			this.toast("Das Zeitfenster zum Bearbeiten ist abgelaufen");
			return;
		}
		this.saveDraft();
		this.replyTo = null;
		this.editing = item;
		this.model().setProperty("/draft", item.text ?? "");
		this.updateContext(minutes);
		(this.byId("chatInput") as TextArea).focus();
	}

	onCancelContext(): void {
		const wasEditing = !!this.editing;
		this.replyTo = null;
		this.editing = null;
		if (wasEditing) {
			const id = this.model().getProperty("/activeId") as string;
			this.model().setProperty("/draft", storage()?.getItem(`eo.chat.draft.${id}`) ?? "");
		}
		this.updateContext();
	}

	private updateContext(minutes?: number): void {
		const m = this.model();
		if (this.editing) {
			m.setProperty("/compose", { mode: "edit", title: `Nachricht bearbeiten${minutes ? ` · noch ${minutes} min` : ""}`, preview: this.snippet(this.editing) });
		} else if (this.replyTo) {
			m.setProperty("/compose", { mode: "reply", title: `Antwort an ${this.replyTo.mine ? "sich selbst" : this.replyTo.senderName}`, preview: this.snippet(this.replyTo) });
		} else {
			m.setProperty("/compose", { mode: "", title: "", preview: "" });
		}
	}

	onDraftChange(event: Event): void {
		const id = this.model().getProperty("/activeId") as string;
		const value = String(event.getParameter("value" as never) ?? "");
		if (id && value && !this.editing && Date.now() - this.lastTypingSent > 3000) {
			this.lastTypingSent = Date.now();
			void typing(id).catch(() => undefined);
		}
		if (this.model().getProperty("/active/kind") === "group") this.suggestMentions(value);
	}

	private suggestMentions(value: string): void {
		const input = this.byId("chatInput") as TextArea;
		const dom = input.getFocusDomRef() as HTMLTextAreaElement | null;
		const query = mentionQuery(value, dom?.selectionStart ?? value.length);
		const me = this.myId();
		const candidates = query === null ? [] : this.members.filter((s) => s.userId !== me && s.name.toLowerCase().includes(query.toLowerCase())).slice(0, 8);
		if (!candidates.length) {
			this.mentionPopover?.close();
			return;
		}
		if (!this.mentionPopover) {
			this.mentionPopover = new Popover({ showHeader: false, placement: "Top", contentWidth: "16rem", initialFocus: input,
				content: [new List({ showSeparators: "None" })] });
			this.v().addDependent(this.mentionPopover);
		}
		const list = this.mentionPopover.getContent()[0] as List;
		list.destroyItems();
		for (const c of candidates) {
			list.addItem(new StandardListItem({ title: c.name, icon: "sap-icon://person-placeholder", type: "Active", press: () => this.insertMention(c.name, query ?? "") }));
		}
		if (!this.mentionPopover.isOpen()) this.mentionPopover.openBy(input);
	}

	private insertMention(name: string, query: string): void {
		const m = this.model();
		const input = this.byId("chatInput") as TextArea;
		const dom = input.getFocusDomRef() as HTMLTextAreaElement | null;
		const value = m.getProperty("/draft") as string;
		const caret = dom?.selectionStart ?? value.length;
		const start = caret - query.length - 1;
		const next = `${value.slice(0, start)}@${name} ${value.slice(caret)}`;
		m.setProperty("/draft", next);
		this.mentionPopover?.close();
		setTimeout(() => {
			input.focus();
			const pos = start + name.length + 2;
			(input.getFocusDomRef() as HTMLTextAreaElement | null)?.setSelectionRange(pos, pos);
		}, 0);
	}

	// ---- Anhänge, Kamera, Standort ---------------------------------------------------------------------------------

	private canAttach(): boolean {
		const m = this.model();
		return !!m.getProperty("/activeId") && m.getProperty("/active/canPost") !== false && m.getProperty("/attachmentsEnabled") !== false && !this.editing;
	}

	onAttachMenu(event: Event): void {
		const source = event.getSource() as Control;
		const pick = (accept: string, capture?: string) => {
			const input = document.createElement("input");
			input.type = "file";
			input.accept = accept;
			input.multiple = !capture;
			if (capture) input.setAttribute("capture", capture);
			input.addEventListener("change", () => this.addFiles([...(input.files ?? [])]));
			input.click();
		};
		const buttons = [
			new Button({ text: "Fotos", icon: "sap-icon://picture", press: () => pick("image/jpeg,image/png,image/webp,image/heic,image/heif") }),
			new Button({ text: "Kamera", icon: "sap-icon://camera", press: () => pick("image/*", "environment") }),
			new Button({ text: "Dokument (PDF)", icon: "sap-icon://pdf-attachment", press: () => pick("application/pdf") }),
			new Button({ text: "Meinen Standort teilen", icon: "sap-icon://locate-me", press: () => this.shareMyLocation() }),
		];
		if (this.model().getProperty("/active/bauakteId")) {
			buttons.push(new Button({ text: "Baustellenadresse teilen", icon: "sap-icon://map-2", press: () => void this.shareSiteAddress() }));
		}
		const sheet = new ActionSheet({ placement: "Top", buttons, cancelButtonText: "Abbrechen", afterClose: () => sheet.destroy() });
		this.v().addDependent(sheet);
		sheet.openBy(source);
	}

	private addFiles(files: File[]): void {
		if (!this.canAttach() || !files.length) return;
		const m = this.model();
		const pending = [...(m.getProperty("/pending") as PendingFile[])];
		const max = this.status?.maxAttachmentBytes ?? 25 * 1024 * 1024;
		const errors: string[] = [];
		for (const file of files) {
			const problem = checkFile(file, max);
			if (problem) {
				errors.push(problem);
				continue;
			}
			if (pending.length >= 10) {
				errors.push("Höchstens 10 Anhänge je Nachricht.");
				break;
			}
			const isImage = file.type.startsWith("image/") && !/heic|heif/.test(file.type);
			pending.push({ key: uuidv7(), file, label: file.name || (isImage ? "Foto" : "Dokument"), isImage, preview: isImage ? URL.createObjectURL(file) : "" });
		}
		m.setProperty("/pending", pending);
		if (errors.length) this.showError(new Error(errors.join("\n")), "Nicht angehängt");
		(this.byId("chatInput") as TextArea).focus();
	}

	onRemovePending(event: Event): void {
		const ctx = (event.getSource() as Control).getBindingContext("view");
		const item = ctx?.getObject() as PendingFile | undefined;
		if (!item) return;
		if (item.preview) URL.revokeObjectURL(item.preview);
		const m = this.model();
		m.setProperty("/pending", (m.getProperty("/pending") as PendingFile[]).filter((p) => p.key !== item.key));
	}

	private shareMyLocation(): void {
		if (!navigator.geolocation) {
			this.toast("Standort wird von diesem Browser nicht unterstützt");
			return;
		}
		navigator.geolocation.getCurrentPosition(
			(pos) => void this.sendLocation({ latitude: pos.coords.latitude, longitude: pos.coords.longitude, label: "Mein Standort" }),
			() => this.toast("Standort nicht verfügbar (Freigabe im Browser prüfen)"),
			{ enableHighAccuracy: true, timeout: 15_000 },
		);
	}

	private async shareSiteAddress(): Promise<void> {
		const bauakteId = this.model().getProperty("/active/bauakteId") as string;
		try {
			const b = await get<{ title: string; number: string | null; street: string | null; postalCode: string | null; city: string | null; latitude: number | null; longitude: number | null }>(`bauakten/${bauakteId}`);
			if (b.latitude === null || b.longitude === null) {
				this.toast("Für diese Bauakte ist noch keine Position hinterlegt");
				return;
			}
			const address = [b.street, [b.postalCode, b.city].filter(Boolean).join(" ")].filter(Boolean).join(", ");
			await this.sendLocation({ latitude: b.latitude, longitude: b.longitude, label: address || b.title });
		} catch (error) {
			this.showError(error);
		}
	}

	private async sendLocation(location: ChatLocation): Promise<void> {
		const id = this.model().getProperty("/activeId") as string;
		if (!id) return;
		try {
			const sent = await send(id, { clientMessageId: uuidv7(), location });
			this.items.push(sent);
			this.stickToBottom = true;
			this.renderLog(true);
			this.scheduleList();
		} catch (error) {
			this.showError(error, "Standort nicht gesendet");
		}
	}

	// ---- Senden ----------------------------------------------------------------------------------------------------

	async onSend(): Promise<void> {
		const m = this.model();
		const id = m.getProperty("/activeId") as string;
		const text = (m.getProperty("/draft") as string).trim();
		const files = m.getProperty("/pending") as PendingFile[];
		if (!id || (!text && !files.length)) return;
		this.recognition?.stop();
		if (this.editing) {
			const item = this.editing;
			try {
				const fresh = await editMessage(item.id, text, mentionIdsIn(text, this.members.map((s) => ({ id: s.userId, name: s.name }))));
				const index = this.items.indexOf(item);
				if (index >= 0) this.items.splice(index, 1, fresh);
				this.onCancelContext();
				this.renderLog(false);
			} catch (error) {
				this.showError(error, "Nicht gespeichert");
			}
			return;
		}
		m.setProperty("/draft", "");
		m.setProperty("/pending", []);
		storage()?.removeItem(`eo.chat.draft.${id}`);
		const clientId = uuidv7();
		const reply = this.replyTo;
		this.replyTo = null;
		this.updateContext();
		const local: LocalMessage = {
			id: `local-${clientId}`, conversationId: id, senderUserId: "", senderName: "", text: text || null, createdAt: new Date().toISOString(), mine: true,
			deleted: false, status: null, readBy: 0, recipients: 0, clientMessageId: clientId, pending: true, localFiles: files,
			replyTo: reply ? { id: reply.id, senderUserId: reply.senderUserId, senderName: reply.senderName, text: reply.text, deleted: reply.deleted, attachment: reply.attachments?.[0]?.kind ?? null } : null,
		};
		this.items.push(local);
		this.stickToBottom = true;
		this.renderLog(true);
		await this.deliver(local);
	}

	private async deliver(local: LocalMessage): Promise<void> {
		try {
			for (const file of local.localFiles ?? []) {
				file.uploaded ??= await uploadAttachment(local.conversationId, file.file, file.label);
			}
			const text = local.text ?? "";
			const sent = await send(local.conversationId, {
				text: text || undefined, clientMessageId: local.clientMessageId!, replyToId: local.replyTo?.id ?? null,
				attachmentIds: (local.localFiles ?? []).map((f) => f.uploaded!.id),
				mentionIds: mentionIdsIn(text, this.members.map((s) => ({ id: s.userId, name: s.name }))),
			});
			for (const file of local.localFiles ?? []) if (file.preview) URL.revokeObjectURL(file.preview);
			const index = this.items.indexOf(local);
			if (index >= 0) this.items.splice(index, 1, sent);
			// Schon über Ereignis/Abfrage eingetroffen: nur einmal zeigen.
			const seen = new Set<string>();
			this.items = this.items.filter((x) => (seen.has(x.id) ? false : (seen.add(x.id), true)));
			this.renderLog(true);
			this.scheduleList();
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

	// ---- Diktat (Web Speech API) -----------------------------------------------------------------------------------

	onDictate(): void {
		const m = this.model();
		if (this.recognition) {
			this.recognition.stop();
			return;
		}
		const Recognition = speechRecognition();
		if (!Recognition) return;
		const recognition = new Recognition();
		recognition.lang = "de-DE";
		recognition.interimResults = true;
		recognition.continuous = true;
		const base = (m.getProperty("/draft") as string) ?? "";
		this.dictationBase = base && !base.endsWith(" ") ? `${base} ` : base;
		let finalText = "";
		recognition.onresult = (event) => {
			let interim = "";
			for (let i = event.resultIndex; i < event.results.length; i++) {
				const result = event.results[i];
				if (result.isFinal) finalText += result[0].transcript;
				else interim += result[0].transcript;
			}
			m.setProperty("/draft", `${this.dictationBase}${finalText}${interim}`.replace(/\s{2,}/g, " "));
		};
		recognition.onerror = (event) => {
			if (event.error === "not-allowed" || event.error === "service-not-allowed") this.toast("Mikrofon nicht freigegeben");
			else if (event.error !== "aborted" && event.error !== "no-speech") this.toast("Diktat unterbrochen");
		};
		recognition.onend = () => {
			this.recognition = null;
			m.setProperty("/listening", false);
			m.setProperty("/draft", `${this.dictationBase}${finalText}`.trimEnd());
			(this.byId("chatInput") as TextArea | undefined)?.focus();
		};
		this.recognition = recognition;
		m.setProperty("/listening", true);
		try {
			recognition.start();
		} catch {
			this.recognition = null;
			m.setProperty("/listening", false);
		}
	}

	// ---- Kopf: Stumm, Suche, Medien, Bauakte ------------------------------------------------------------------------

	onMute(event: Event): void {
		const m = this.model();
		const id = m.getProperty("/activeId") as string;
		const apply = async (muted: boolean, minutes?: number, label?: string) => {
			try {
				await mute(id, muted, minutes);
				m.setProperty("/active/muted", muted);
				this.toast(muted ? `Stumm geschaltet (${label}). @Erwähnungen werden weiterhin zugestellt.` : "Hinweise wieder eingeschaltet");
				await this.loadList();
			} catch (error) {
				this.showError(error);
			}
		};
		if (m.getProperty("/active/muted")) {
			void apply(false);
			return;
		}
		const sheet = new ActionSheet({
			title: "Stumm schalten", placement: "Bottom", cancelButtonText: "Abbrechen", afterClose: () => sheet.destroy(),
			buttons: MUTE_OPTIONS.map((o) => new Button({ text: o.label, press: () => void apply(true, o.minutes, o.label.toLowerCase()) })),
		});
		this.v().addDependent(sheet);
		sheet.openBy(event.getSource() as Control);
	}

	onOpenBauakte(): void {
		const id = this.model().getProperty("/active/bauakteId") as string;
		if (id) this.nav(`/bauakten/${id}`);
	}

	onSearchInChat(): void {
		const id = this.model().getProperty("/activeId") as string;
		const list = new List({ noDataText: "Mindestens 2 Zeichen eingeben.", showSeparators: "Inner" });
		let timer: ReturnType<typeof setTimeout> | undefined;
		const field = new SearchField({ placeholder: "Text in dieser Unterhaltung", liveChange: (e) => {
			const q = String(e.getParameter("newValue") ?? "").trim();
			clearTimeout(timer);
			timer = setTimeout(async () => {
				list.destroyItems();
				if (q.length < 2) return;
				const result = await search(q, id).catch(() => null);
				list.setNoDataText("Keine Treffer.");
				for (const hit of result?.items ?? []) {
					list.addItem(new CustomListItem({ type: "Active", press: () => { dialog.close(); void this.scrollToMessage(hit.messageId); },
						content: [new VBox({ renderType: "Bare", items: [
							new Text({ text: `${hit.senderName} · ${listTime(hit.createdAt)}` }).addStyleClass("eoMuted eoChatHitMeta"),
							new FormattedText({ htmlText: this.highlight(hit.snippet, q) }),
						] }).addStyleClass("eoChatHit")] }));
				}
			}, 300);
		} });
		const dialog: Dialog = new Dialog({
			title: "In Unterhaltung suchen", contentWidth: "32rem", contentHeight: "26rem", initialFocus: field,
			content: [new VBox({ items: [field, list] }).addStyleClass("sapUiSmallMargin")],
			endButton: new Button({ text: "Schließen", press: () => dialog.close() }), afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	async onMedia(): Promise<void> {
		const id = this.model().getProperty("/activeId") as string;
		let all: ChatAttachment[];
		try {
			all = await media(id);
		} catch (error) {
			this.showError(error);
			return;
		}
		const photos = all.filter((a) => a.kind === "photo");
		const docs = all.filter((a) => a.kind === "document");
		const grid = new HTMLControl({
			content: `<div class="eoChatMediaGrid">${photos.length ? photos.map((a) => `<button type="button" class="eoChatMediaTile" data-attachment="${encodeXML(a.id)}" title="${encodeXML(`${a.fileName} · ${listTime(a.createdAt)}`)}"><img loading="lazy" src="${encodeXML(attachmentUrl(a.id, { thumbnail: a.hasThumbnail }))}" alt="${encodeXML(a.fileName)}"/></button>`).join("") : '<p class="eoMuted">Noch keine Fotos.</p>'}</div>`,
			afterRendering: () => {
				const dom = grid.getDomRef() as HTMLElement | null;
				dom?.addEventListener("click", (e) => {
					const tile = (e.target as HTMLElement).closest<HTMLElement>("[data-attachment]");
					if (tile) this.openPhotos(photos, tile.dataset.attachment ?? "");
				});
			},
		});
		const docList = new List({ noDataText: "Noch keine Dokumente.", showSeparators: "Inner" });
		for (const a of docs) {
			docList.addItem(new StandardListItem({ title: a.fileName, description: `${formatBytes(a.sizeBytes)} · ${listTime(a.createdAt)}`,
				icon: "sap-icon://pdf-attachment", type: "Active", press: () => window.open(attachmentUrl(a.id), "_blank", "noopener") }));
		}
		const tabs = new IconTabBar({ expandable: false, items: [
			new IconTabFilter({ text: `Fotos (${photos.length})`, key: "photo", content: [grid] }),
			new IconTabFilter({ text: `Dokumente (${docs.length})`, key: "document", content: [docList] }),
		] }).addStyleClass("eoChatMediaTabs");
		const dialog: Dialog = new Dialog({
			title: "Medien und Dokumente", contentWidth: "44rem", contentHeight: "34rem", content: [tabs],
			endButton: new Button({ text: "Schließen", press: () => dialog.close() }), afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	// ---- Weiterleiten, in Bauakte übernehmen ------------------------------------------------------------------------

	private async forwardDialog(item: LocalMessage): Promise<void> {
		const rows = (this.model().getProperty("/list") as Row[]).filter((r) => r.canPost !== false);
		const list = new List({ mode: "MultiSelect", showSeparators: "Inner" });
		for (const r of rows) list.addItem(new StandardListItem({ title: r.title, description: r.kind === "group" ? `${r.members.length} Mitglieder` : "Einzelchat",
			icon: r.kind === "group" ? "sap-icon://group" : "sap-icon://person-placeholder" }).data("id", r.id));
		const dialog: Dialog = new Dialog({
			title: "Weiterleiten an …", contentWidth: "28rem", contentHeight: "28rem", content: [list],
			beginButton: new Button({ text: "Weiterleiten", type: "Emphasized", press: async () => {
				const ids = list.getSelectedItems().map((i) => String(i.data("id")));
				if (!ids.length) return;
				try {
					await forward(item.id, ids);
					dialog.close();
					this.toast(ids.length === 1 ? "Weitergeleitet" : `An ${ids.length} Unterhaltungen weitergeleitet`);
					this.scheduleList();
				} catch (error) {
					this.showError(error);
				}
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }), afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	private async copyToBauakte(item: LocalMessage): Promise<void> {
		const attachments = item.attachments ?? [];
		const linked = this.model().getProperty("/active/bauakteId") as string | null;
		const linkedTitle = (this.model().getProperty("/active/bauakteTitle") as string) || "Bauakte der Gruppe";
		let target: string | null = linked;
		const picker = bauakteInput(`chatBauakte-${uuidv7()}`, linked, linked ? linkedTitle : "", (v) => { target = v; });
		const caption = new Input({ value: item.text ? item.text.slice(0, 200) : "", maxLength: 500, placeholder: "Beschreibung (optional)" });
		const dialog: Dialog = new Dialog({
			title: attachments.length > 1 ? `${attachments.length} Anhänge in Bauakte übernehmen` : "In Bauakte übernehmen", contentWidth: "30rem",
			content: [new VBox({ items: [
				new Label({ text: "Bauakte", required: true, labelFor: picker.control }), picker.control,
				new Label({ text: "Beschreibung", labelFor: caption }).addStyleClass("sapUiSmallMarginTop"), caption,
				new Text({ text: "Fotos landen bei den Fotos, PDFs bei den Dokumenten (Kategorie „chat“)." }).addStyleClass("eoMuted sapUiSmallMarginTop"),
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Übernehmen", type: "Emphasized", press: async () => {
				const bauakteId = picker.read() ?? target;
				if (!bauakteId) {
					picker.control.setValueState("Error");
					picker.control.setValueStateText("Bitte eine Bauakte wählen.");
					return;
				}
				try {
					for (const a of attachments) await toBauakte(a.id, bauakteId, caption.getValue().trim() || undefined);
					dialog.close();
					this.toast(attachments.length > 1 ? `${attachments.length} Dateien in die Bauakte übernommen` : "In die Bauakte übernommen");
				} catch (error) {
					this.showError(error);
				}
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }), afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	// ---- Info / Gruppenverwaltung ----------------------------------------------------------------------------------

	async onInfo(): Promise<void> {
		const id = this.model().getProperty("/activeId") as string;
		let detail: ChatConversationDetail;
		try {
			detail = await conversation(id);
		} catch (error) {
			this.showError(error);
			return;
		}
		const c = detail.conversation;
		const group = c.kind === "group";
		const canAdmin = !!this.model().getProperty("/canAdmin");
		const manage = detail.canManage;
		const avatar = new Avatar({ src: groupImageUrl(c) || (group ? "sap-icon://group" : ""), initials: initialsOf(c.title), displaySize: "XL",
			backgroundColor: accentOf(group ? c.id : c.partnerUserId ?? c.id) as never, decorative: true }).addStyleClass("eoChatInfoAvatar");
		const head = new VBox({ alignItems: "Center", renderType: "Bare", items: [avatar,
			new Text({ text: c.title }).addStyleClass("eoChatInfoTitle"),
			new Text({ text: group ? `Gruppe · ${detail.members.length} Mitglieder${c.bauakteId ? " · Baustelle" : c.crewId ? " · Kolonne" : ""}${c.announcementOnly ? " · nur Admins schreiben" : ""}` : "Einzelchat" })
				.addStyleClass("eoMuted"),
		] }).addStyleClass("eoChatInfoHead");
		const content: Control[] = [head];
		if (manage) {
			const imageButtons = new HBox({ justifyContent: "Center", renderType: "Bare", items: [
				new Button({ text: "Bild ändern", icon: "sap-icon://camera", type: "Transparent", press: () => {
					const input = document.createElement("input");
					input.type = "file";
					input.accept = "image/jpeg,image/png,image/webp";
					input.addEventListener("change", async () => {
						const file = input.files?.[0];
						if (!file) return;
						try {
							const updated = await setGroupImage(c.id, file);
							avatar.setSrc(groupImageUrl(updated));
							await this.loadList();
						} catch (error) {
							this.showError(error);
						}
					});
					input.click();
				} }),
				...(c.imageUpdatedAt ? [new Button({ text: "Entfernen", type: "Transparent", press: async () => {
					await removeGroupImage(c.id).catch((e) => this.showError(e));
					avatar.setSrc("sap-icon://group");
					await this.loadList();
				} })] : []),
			] });
			content.push(imageButtons);
		}
		if (group && (c.description || manage)) {
			if (manage) {
				const description = new TextAreaControl({ value: c.description ?? "", rows: 2, growing: true, maxLength: 1000, width: "100%",
					placeholder: "Beschreibung, z. B. Zweck und Regeln der Gruppe" });
				const announce = new Switch({ state: !!c.announcementOnly, customTextOn: "Ja", customTextOff: "Nein" });
				content.push(new VBox({ renderType: "Bare", items: [
					new Label({ text: "Beschreibung", labelFor: description }), description,
					new HBox({ alignItems: "Center", justifyContent: "SpaceBetween", renderType: "Bare", items: [
						new Label({ text: "Ankündigungsgruppe (nur Admins schreiben)", labelFor: announce, wrapping: true }), announce] }).addStyleClass("sapUiSmallMarginTop"),
					new Button({ text: "Speichern", type: "Emphasized", press: async () => {
						try {
							await updateGroup(c.id, { description: description.getValue(), announcementOnly: announce.getState() });
							this.toast("Gruppe gespeichert");
							await this.loadList();
						} catch (error) {
							this.showError(error);
						}
					} }).addStyleClass("sapUiTinyMarginTop"),
				] }).addStyleClass("eoChatInfoSection"));
			} else {
				content.push(new Text({ text: c.description ?? "" }).addStyleClass("eoChatInfoSection eoChatInfoDescription"));
			}
		}
		if (c.bauakteId) {
			content.push(new HBox({ alignItems: "Center", renderType: "Bare", items: [
				new Text({ text: "Bauakte:" }).addStyleClass("eoMuted sapUiTinyMarginEnd"),
				new Link({ text: detail.bauakteTitle ?? "öffnen", press: () => { dialog.close(); this.nav(`/bauakten/${c.bauakteId}`); } }),
			] }).addStyleClass("eoChatInfoSection"));
		}
		const list = new List({ headerText: group ? "Mitglieder" : "Teilnehmer", showSeparators: "Inner" });
		for (const member of detail.members) {
			const info = [member.isAdmin ? "Admin" : "", member.fromCrew ? (c.bauakteId ? "über Bauakte" : "über Kolonne") : ""].filter(Boolean).join(" · ");
			const li = new StandardListItem({
				title: member.name,
				description: member.readUpTo ? `Zuletzt gelesen: ${listTime(member.readUpTo).includes(":") ? `heute ${listTime(member.readUpTo)}` : `${listTime(member.readUpTo)}, ${clock(member.readUpTo)}`}` : "Lesestand nicht sichtbar oder noch nichts gelesen",
				info, icon: "sap-icon://person-placeholder", type: manage || canAdmin ? "Active" : "Inactive",
				press: () => this.memberActions(c, detail, member, li, dialog),
			});
			list.addItem(li);
		}
		content.push(list);
		const buttons: Button[] = [];
		if (manage && group) buttons.push(new Button({ text: "Mitglieder …", icon: "sap-icon://add-employee", press: () => { dialog.close(); void this.editGroup(c, detail.members); } }));
		if (group && !detail.members.find((s) => s.userId === this.myId())?.fromCrew) {
			buttons.push(new Button({ text: "Verlassen", icon: "sap-icon://log", type: "Reject", press: async () => {
				if (!(await this.confirm(`Gruppe „${c.title}“ verlassen?`, { title: "Gruppe verlassen", action: "Verlassen", danger: true }))) return;
				try {
					await leaveGroup(c.id);
					dialog.close();
					this.nav("/chat", true);
				} catch (error) {
					this.showError(error);
				}
			} }));
		}
		if (manage || canAdmin) {
			buttons.push(new Button({ icon: "sap-icon://delete", tooltip: "Unterhaltung für alle löschen", type: "Transparent", press: () => { dialog.close(); void this.deleteActive(); } }));
		}
		buttons.push(new Button({ text: "Schließen", type: "Emphasized", press: () => dialog.close() }));
		const dialog: Dialog = new Dialog({
			title: group ? "Gruppeninfo" : "Info", contentWidth: "30rem", content: [new VBox({ items: content, renderType: "Bare" }).addStyleClass("eoChatInfo")],
			buttons, afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	private memberActions(c: ChatConversation, detail: ChatConversationDetail, member: ChatMemberState, anchor: Control, parent: Dialog): void {
		const canAdmin = !!this.model().getProperty("/canAdmin");
		const me = this.myId();
		if (member.userId === me) return;
		const buttons: Button[] = [];
		if (c.kind === "group" && detail.canManage) {
			const admins = detail.members.filter((s) => s.isAdmin).map((s) => s.userId);
			buttons.push(new Button({ text: member.isAdmin ? "Als Admin entfernen" : "Zum Admin machen", icon: "sap-icon://key-user-settings", press: async () => {
				const next = member.isAdmin ? admins.filter((x) => x !== member.userId) : [...admins, member.userId];
				try {
					await updateGroup(c.id, { adminIds: next });
					parent.close();
					void this.onInfo();
				} catch (error) {
					this.showError(error);
				}
			} }));
			if (!member.fromCrew) {
				buttons.push(new Button({ text: "Aus Gruppe entfernen", icon: "sap-icon://employee-rejections", type: "Reject", press: async () => {
					try {
						await updateGroup(c.id, { memberIds: detail.members.filter((s) => s.userId !== member.userId && !s.fromCrew).map((s) => s.userId) });
						parent.close();
						void this.onInfo();
					} catch (error) {
						this.showError(error);
					}
				} }));
			}
		}
		buttons.push(new Button({ text: "Einzelchat öffnen", icon: "sap-icon://discussion", press: async () => {
			try {
				const direct = await startDirect(member.userId);
				parent.close();
				this.nav(`/chat/${direct.id}`);
			} catch (error) {
				this.showError(error);
			}
		} }));
		if (canAdmin) {
			buttons.push(new Button({ text: "Im Chat sperren / entsperren …", icon: "sap-icon://locked", type: "Reject", press: async () => {
				const block = await this.confirm(`${member.name} im Chat sperren? Gesperrte Personen können lesen, aber nicht schreiben. Zum Entsperren die Person erneut wählen und „Entsperren“ verwenden.`,
					{ title: "Moderation", action: "Sperren", danger: true });
				try {
					if (block) await blockUser(member.userId, true);
					else if (await this.confirm(`Sperre für ${member.name} aufheben?`, { title: "Moderation", action: "Entsperren" })) await blockUser(member.userId, false);
					else return;
					this.toast(block ? "Gesperrt" : "Entsperrt");
				} catch (error) {
					this.showError(error);
				}
			} }));
		}
		const sheet = new ActionSheet({ title: member.name, placement: "Bottom", buttons, cancelButtonText: "Abbrechen", afterClose: () => sheet.destroy() });
		this.v().addDependent(sheet);
		sheet.openBy(anchor);
	}

	private async deleteActive(): Promise<void> {
		const m = this.model();
		const id = m.getProperty("/activeId") as string;
		const ok = await this.confirm(`„${String(m.getProperty("/active/title"))}“ mit allen Nachrichten und Anhängen für alle Teilnehmer löschen?`,
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

	// ---- Datenschutz, eigene Einstellungen ------------------------------------------------------------------------

	private showPrivacy(status: ChatStatus): void {
		this.privacyShown = true;
		const dialog: Dialog = new Dialog({
			title: "Datenschutz im Chat", contentWidth: "32rem", type: "Message", state: "Information",
			content: [new FormattedText({ htmlText: `<p>Der Chat dient der betrieblichen Abstimmung. Bitte keine privaten Gesundheitsdaten o. Ä. teilen.</p>
<ul><li>Nachrichten, Fotos und PDFs werden <strong>verschlüsselt</strong> gespeichert und nach <strong>${status.retentionDays} Tagen</strong> automatisch gelöscht.</li>
<li>Gelesen-Häkchen und „… schreibt“ sind Komfort und werden nicht ausgewertet; Lesebestätigungen können Sie abschalten.</li>
<li>Pushnachrichten zeigen Text nur, wenn Verwaltung und Sie es erlauben.</li>
<li>Diktat nutzt die Spracherkennung des Browsers; es wird kein Audio gespeichert oder gesendet.</li>
<li>Sie können Ihre Chatdaten jederzeit exportieren (Meine Chat-Einstellungen) und Löschung verlangen.</li></ul>` }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Verstanden", type: "Emphasized", press: () => {
				void saveMySettings({ privacyAccepted: true }).catch(() => undefined);
				dialog.close();
			} }),
			afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
	}

	/** Chat-Archiv (Modus „Microsoft Teams“): neuer Einzel- bzw. Gruppenchat in Teams. */
	onNewTeamsChat(): void {
		void pickTeamsChat();
	}

	/** Microsoft Teams (Team „Baustellen“ bzw. Teams allgemein) in neuem Fenster. */
	onOpenTeams(): void {
		window.open(teamsHome(session.data.teams), "_blank", "noopener");
	}

	/** Chat-Archiv: eigene Verläufe als JSON speichern (Auskunft, Datenübertragbarkeit). */
	async onExport(): Promise<void> {
		try {
			const data = await get<unknown>("chat/export");
			const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }));
			const anchor = document.createElement("a");
			anchor.href = url;
			anchor.download = `chat-archiv-${new Date().toISOString().slice(0, 10)}.json`;
			anchor.click();
			setTimeout(() => URL.revokeObjectURL(url), 10_000);
		} catch (error) {
			MessageBox.error(problemMessage(error), { title: "Export nicht möglich" });
		}
	}

	async onMySettings(): Promise<void> {
		let settings;
		try {
			settings = await mySettings();
		} catch (error) {
			this.showError(error);
			return;
		}
		const preview = new Switch({ state: settings.showPreview, enabled: settings.previewAllowedByAdmin });
		const receipts = new Switch({ state: settings.readReceipts });
		const row = (label: string, control: Control, note: string) => new HBox({ alignItems: "Center", justifyContent: "SpaceBetween", renderType: "Bare", items: [
			new VBox({ renderType: "Bare", items: [new Label({ text: label, labelFor: control, wrapping: true }), new Text({ text: note }).addStyleClass("eoMuted eoChatSmall")] })
				.addStyleClass("eoChatSettingText"), control] }).addStyleClass("eoChatSettingRow");
		const dialog: Dialog = new Dialog({
			title: "Meine Chat-Einstellungen", contentWidth: "30rem",
			content: [new VBox({ renderType: "Bare", items: [
				row("Nachrichtentext in Pushnachrichten", preview, settings.previewAllowedByAdmin ? "Sonst nur „Neue Nachricht im Chat“." : "Von der Verwaltung ausgeschaltet."),
				row("Lesebestätigungen senden", receipts, "Aus: andere sehen nicht, wann Sie gelesen haben."),
				...(settings.blocked ? [new MessageStrip({ text: "Sie sind im Chat gesperrt (nur lesen).", type: "Warning", showIcon: true })] : []),
				new HBox({ renderType: "Bare", items: [
					new Link({ text: "Meine Chatdaten exportieren (ZIP mit Anhängen)", href: "/api/v1/chat/export/archive", target: "_blank" }),
				] }).addStyleClass("sapUiSmallMarginTop"),
			] }).addStyleClass("sapUiSmallMargin")],
			beginButton: new Button({ text: "Speichern", type: "Emphasized", press: async () => {
				try {
					await saveMySettings({ showPreview: preview.getState(), readReceipts: receipts.getState() });
					dialog.close();
					this.toast("Gespeichert");
				} catch (error) {
					this.showError(error);
				}
			} }),
			endButton: new Button({ text: "Abbrechen", press: () => dialog.close() }), afterClose: () => dialog.destroy(),
		});
		this.v().addDependent(dialog);
		dialog.open();
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
		const field = new SearchField({ placeholder: "Name suchen", liveChange: (e) => fill(String(e.getParameter("newValue") ?? "")) });
		const dialog: Dialog = new Dialog({
			title: "Neuer Einzelchat", contentWidth: "26rem", contentHeight: "26rem", initialFocus: field,
			content: [new VBox({ items: [field, list] }).addStyleClass("sapUiSmallMargin")],
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
		const title = new Input({ value: existing?.title ?? "", maxLength: 120, placeholder: "z. B. Kolonne Nord, Alle Mitarbeiter" });
		const description = new TextAreaControl({ value: existing?.description ?? "", rows: 2, maxLength: 1000, width: "100%", placeholder: "Zweck der Gruppe (optional)" });
		const announce = new CheckBox({ text: "Ankündigungsgruppe (nur Admins schreiben)", selected: !!existing?.announcementOnly });
		const crew = new Select({ width: "100%", items: [new Item({ key: "", text: "Keine (Personen frei wählen)" }),
			...crewList.map((c) => new Item({ key: c.id, text: `${c.name} (${c.members})` }))] });
		const members = new MultiComboBox({ width: "100%", placeholder: "Personen hinzufügen",
			items: people.map((p) => new Item({ key: p.id, text: p.name })),
			selectedKeys: current.filter((s) => s.userId !== me?.userId && !s.fromCrew).map((s) => s.userId) });
		const all = new Button({ text: "Alle auswählen", type: "Transparent", press: () => members.setSelectedKeys(people.map((p) => p.id)) });
		crew.attachChange(() => {
			const name = crew.getSelectedItem()?.getText().replace(/ \(\d+\)$/, "") ?? "";
			if (crew.getSelectedKey() && !title.getValue()) title.setValue(name);
		});
		const dialog: Dialog = new Dialog({
			title: existing ? "Mitglieder bearbeiten" : "Neue Gruppe",
			contentWidth: "32rem",
			content: [new VBox({ items: [
				new Label({ text: "Name", labelFor: title, required: true }), title,
				...(existing ? [] : [
					new Label({ text: "Beschreibung", labelFor: description }).addStyleClass("sapUiSmallMarginTop"), description,
					new Label({ text: "Kolonne (Mitglieder folgen der Kolonne)", labelFor: crew }).addStyleClass("sapUiSmallMarginTop"), crew,
				]),
				new HBox({ alignItems: "End", justifyContent: "SpaceBetween", renderType: "Bare", items: [
					new Label({ text: existing?.crewId || existing?.bauakteId ? "Weitere Personen (automatische Mitglieder bleiben)" : "Mitglieder", labelFor: members }), all,
				] }).addStyleClass("sapUiSmallMarginTop"), members,
				...(existing ? [] : [announce]),
				new Text({ text: "Sie selbst sind immer Mitglied und Admin. Nur Personen mit Recht „Chat nutzen“ sind wählbar. Die Einladung erfolgt ohne Telefonnummern." })
					.addStyleClass("eoMuted sapUiSmallMarginTop"),
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
						void this.loadDetail();
						this.toast("Gruppe gespeichert");
					} else {
						const c = await createGroup(name, keys, crew.getSelectedKey() || null,
							{ description: description.getValue().trim() || undefined, announcementOnly: announce.getSelected() });
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
