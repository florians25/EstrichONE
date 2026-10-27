import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Backup-Hinweise, Nur Ansicht, Chat")
struct SystemStateTests {
    private let berlin = TimeZone(identifier: "Europe/Berlin")!

    private func health(_ json: String) throws -> SystemHealth {
        try JSONCoding.decoder.decode(SystemHealth.self, from: Data(json.utf8))
    }

    @Test("Rot bei Ausfall des Backup-Ziels, gelb beim Archiv – nur wenn eingerichtet")
    func banners() throws {
        let now = APIDate.parse("2026-10-27T12:00:00Z")!
        let failing = try health("""
        {"backupTarget":{"configured":true,"reachable":false,"lastSuccessAt":"2026-10-26T02:15:00Z","severity":"critical"},
         "archive":{"configured":true,"reachable":false,"lastSuccessAt":"2026-10-20T01:00:00Z","severity":"warning"}}
        """)
        let banners = failing.banners(at: now, timeZone: berlin)
        #expect(banners.map(\.level) == [.critical, .warning])
        #expect(banners[0].text == "ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup 26.10.2026 03:15 Uhr). Bitte geben Sie umgehend Ihrem Admin Bescheid.")
        #expect(banners[1].text == "Hinweis: Archiv-Server ausgefallen oder nicht erreichbar (Letzte Archivierung 20.10.2026 03:00 Uhr). Bitte informieren Sie Ihren Admin.")

        let notConfigured = try health(#"{"backupTarget":{"configured":false,"reachable":false},"archive":{"configured":false}}"#)
        #expect(notConfigured.banners(at: now).isEmpty)
        let fine = try health(#"{"backupTarget":{"configured":true,"reachable":true,"lastSuccessAt":"2026-10-27T02:00:00Z","severity":"ok"}}"#)
        #expect(fine.banners(at: now).isEmpty)
        // Server mit fertigen Hinweisen (docs/backup.md): genau diese, in dieser Reihenfolge.
        let server = try health("""
        {"backup":{"configured":true,"reachable":false,"severity":"critical"},"archive":{"configured":false},
         "banners":[{"key":"backup","severity":"critical","text":"ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup 26.10.2026 22:30 Uhr). Bitte geben Sie umgehend Ihrem Admin Bescheid."}],
         "checkedAt":"2026-10-27T21:31:00Z"}
        """)
        #expect(server.banners(at: now).map(\.level) == [.critical] && server.banners(at: now).first?.text.hasSuffix("Ihrem Admin Bescheid.") == true)
        #expect(try health(#"{"backup":{"configured":true,"reachable":false},"banners":[]}"#).banners(at: now).isEmpty)
        // Ohne Bewertung des Servers: letzte Sicherung älter als 26 Stunden.
        let stale = try health(#"{"backupTarget":{"configured":true,"reachable":true,"lastSuccessAt":"2026-10-26T08:00:00Z"}}"#)
        #expect(stale.banners(at: now).map(\.level) == [.critical])
    }

    @Test("Nach 5 Minuten ohne Server: Nur Ansicht; Übersteuerung bis zum nächsten Abgleich")
    func readOnly() {
        let start = Date(timeIntervalSince1970: 1_000_000)
        var policy = ReadOnlyPolicy()
        policy.record(.completed, at: start)
        policy.record(.offline, at: start.addingTimeInterval(60))
        #expect(!policy.isReadOnly(at: start.addingTimeInterval(299)))
        #expect(policy.isReadOnly(at: start.addingTimeInterval(300)))
        policy.overridden = true
        #expect(!policy.isReadOnly(at: start.addingTimeInterval(900)) && policy.isLongOffline(at: start.addingTimeInterval(900)))
        policy.record(.completed, at: start.addingTimeInterval(1000))
        #expect(!policy.overridden && !policy.isReadOnly(at: start.addingTimeInterval(2000)))
        // Wartung: Server antwortet – kein Nur-Ansicht-Modus (die Wartung sperrt gesondert).
        policy.record(.maintenance(message: nil), at: start.addingTimeInterval(3000))
        #expect(!policy.isReadOnly(at: start.addingTimeInterval(9000)))
    }

    @Test("Nur Ansicht sperrt lokale Änderungen, Lesen bleibt möglich")
    func storeGate() async throws {
        let directory = FileManager.default.temporaryDirectory.appending(path: "readonly-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: SymmetricKey(size: .bits256),
                                                               migrations: LocalStore.schema))
        let id = UUID.v7()
        await store.setReadOnly(true)
        await #expect(throws: ReadOnlyModeError.self) {
            try await store.create(.bauakte, id: id, data: ["id": .string(id.key), "title": "X"], payload: ["title": "X"])
        }
        #expect(try await store.operations().isEmpty)
        await store.setReadOnly(false)
        try await store.create(.bauakte, id: id, data: ["id": .string(id.key), "title": "X"], payload: ["title": "X"])
        #expect(try await store.operations().count == 1)
        #expect(BauakteError.from(ReadOnlyModeError()) == .readOnly)
    }

    @Test("Chat: Unterhaltungen, Nachrichten mit Zustellstatus, Deep Link")
    func chat() throws {
        let conversations = try JSONCoding.decoder.decode(ChatList<ChatConversation>.self, from: Data("""
        {"items":[{"id":"0193a1b2-0000-7000-8000-0000000c0a01","title":"Kolonne Huber","kind":"group","unreadCount":2,
          "lastMessage":{"id":"0193a1b2-0000-7000-8000-0000000c0b01","text":"Pumpe steht bereit","senderName":"Büro","createdAt":"2026-10-27T06:00:00Z"}}]}
        """.utf8)).items
        #expect(conversations.first?.isGroup == true && conversations.first?.unreadCount == 2 && conversations.first?.lastMessageText == "Pumpe steht bereit")
        let messages = try JSONCoding.decoder.decode(ChatList<ChatMessage>.self, from: Data("""
        [{"id":"0193a1b2-0000-7000-8000-0000000c0b02","text":"Danke","createdAt":"2026-10-27T06:01:00Z","readAt":"2026-10-27T06:02:00Z"},
         {"id":"0193a1b2-0000-7000-8000-0000000c0b03","text":"Bis gleich","createdAt":"2026-10-27T06:03:00Z","state":"delivered"}]
        """.utf8)).items
        #expect(messages.map(\.state) == [.read, .delivered])
        // Aufbau laut docs/chat.md (ChatMessageResponse).
        let real = try JSONCoding.decoder.decode(ChatList<ChatMessage>.self, from: Data("""
        {"items":[{"id":"0193a1b2-0000-7000-8000-0000000c0b04","conversationId":"0193a1b2-0000-7000-8000-0000000c0a01",
          "senderUserId":"0193a1b2-0000-7000-8000-0000000c0c01","senderName":"Max Huber","text":"Halle 2 ist fertig.",
          "createdAt":"2026-10-28T07:12:03Z","mine":true,"deleted":false,"status":"read","readBy":3,"recipients":5,"clientMessageId":null},
         {"id":"0193a1b2-0000-7000-8000-0000000c0b05","conversationId":"0193a1b2-0000-7000-8000-0000000c0a01",
          "senderUserId":"0193a1b2-0000-7000-8000-0000000c0c02","senderName":"Büro","text":null,"createdAt":"2026-10-28T07:13:00Z",
          "mine":false,"deleted":true,"status":null,"readBy":0,"recipients":0}],"hasMore":false,"members":[]}
        """.utf8)).items
        #expect(real[0].isMine(userId: nil) && real[0].state == .read && real[0].readBy == 3 && real[0].recipients == 5)
        #expect(!real[1].isMine(userId: nil) && real[1].deleted && real[1].text.isEmpty)
        #expect(ChatLimits.clean("  \n ") == nil && ChatLimits.clean(" Hallo ") == "Hallo")
        let id = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000c0a01")!
        #expect(NotificationRoute(link: "/chat/\(id.key)") == .chat(id))
        #expect(NotificationRoute(link: "/chat") == .chat(nil))
        #expect(NotificationRoute(link: nil, objectType: "chatConversation", objectId: id) == .chat(id))
    }
}
