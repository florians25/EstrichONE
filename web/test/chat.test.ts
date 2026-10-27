import { describe, expect, it } from "vitest";
import { dayLabel, initialsOf, listTime, previewText, startsRun, type ChatConversation, type ChatMessage } from "../webapp/util/chat";
import { healthStrips, healthTexts } from "../webapp/util/health";

const msg = (over: Partial<ChatMessage>): ChatMessage => ({
	id: "1", conversationId: "c", senderUserId: "a", senderName: "Lukas Berger", text: "Hallo", createdAt: "2026-10-28T08:00:00Z", mine: false,
	deleted: false, status: null, readBy: 0, recipients: 0, clientMessageId: null, ...over,
});

describe("Chat", () => {
	it("zeigt Zeiten wie ein Messenger", () => {
		const now = new Date(2026, 9, 28, 15, 0);
		expect(listTime(new Date(2026, 9, 28, 9, 5).toISOString(), now)).toBe("09:05");
		expect(listTime(new Date(2026, 9, 27, 9, 5).toISOString(), now)).toBe("Gestern");
		expect(listTime(new Date(2026, 9, 20, 9, 5).toISOString(), now)).toBe("20.10.26");
		expect(dayLabel(new Date(2026, 9, 28, 1).toISOString(), now)).toBe("Heute");
	});

	it("fasst Folgen desselben Absenders zusammen", () => {
		const a = msg({ createdAt: "2026-10-28T08:00:00Z" });
		expect(startsRun(undefined, a)).toBe(true);
		expect(startsRun(a, msg({ createdAt: "2026-10-28T08:03:00Z" }))).toBe(false);
		expect(startsRun(a, msg({ createdAt: "2026-10-28T08:09:00Z" }))).toBe(true);
		expect(startsRun(a, msg({ senderUserId: "b", createdAt: "2026-10-28T08:01:00Z" }))).toBe(true);
	});

	it("Vorschau und Initialen", () => {
		const c: ChatConversation = {
			id: "c", kind: "group", title: "Kolonne Nord", crewId: null, members: [], lastActivityAt: "", unreadCount: 0, muted: false, partnerUserId: null,
			lastMessage: { id: "m", senderUserId: "a", senderName: "Lukas Berger", text: "Fertig", createdAt: "", mine: false, deleted: false },
		};
		expect(previewText(c)).toBe("Lukas: Fertig");
		expect(previewText({ ...c, lastMessage: { ...c.lastMessage!, deleted: true } })).toBe("Lukas: Nachricht gelöscht");
		expect(initialsOf("Anna-Lena Müller")).toBe("AM");
		expect(initialsOf("Büro")).toBe("BÜ");
	});
});

describe("Systemstatus-Banner", () => {
	const now = Date.parse("2026-10-28T12:00:00Z");
	it("rot nur wenn Backup eingerichtet und ausgefallen oder älter als 26 h", () => {
		expect(healthTexts({ backupTarget: { configured: false, reachable: false } }, now).backup).toBe("");
		expect(healthTexts({ backupTarget: { configured: true, reachable: true, lastSuccessAt: "2026-10-28T02:00:00Z" } }, now).backup).toBe("");
		const red = healthTexts({ backupTarget: { configured: true, reachable: false, lastSuccessAt: "2026-10-28T02:00:00Z" } }, now).backup;
		expect(red).toMatch(/^ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar \(Letztes Backup 28\.10\.2026, \d\d:00 Uhr\)\. Bitte geben Sie umgehend Ihrem Admin Bescheid\.$/);
		expect(healthTexts({ backupTarget: { configured: true, reachable: true, lastSuccessAt: "2026-10-27T08:00:00Z" } }, now).backup).not.toBe("");
	});

	it("gelb für den Archivserver", () => {
		expect(healthTexts({ archive: { configured: true, reachable: false, lastSuccessAt: null } }, now).archive)
			.toBe("Hinweis: Archiv-Server ausgefallen oder nicht erreichbar (Letzte Archivierung unbekannt). Bitte informieren Sie Ihren Admin.");
		expect(healthTexts({ archive: { configured: false, reachable: false } }, now).archive).toBe("");
	});
});

describe("Banner vom Server", () => {
	it("übernimmt banners unverändert (critical rot, warning gelb)", () => {
		const strips = healthStrips({ banners: [
			{ key: "backup", severity: "critical", text: "ACHTUNG: Backup-Server ausgefallen …" },
			{ key: "archive", severity: "warning", text: "Hinweis: Archiv-Server ausgefallen …" },
		] });
		expect(strips.map((s) => [s.key, s.type, s.text])).toEqual([
			["backup", "Error", "ACHTUNG: Backup-Server ausgefallen …"], ["archive", "Warning", "Hinweis: Archiv-Server ausgefallen …"],
		]);
		expect(healthStrips({ banners: [] })).toEqual([]);
	});
});
