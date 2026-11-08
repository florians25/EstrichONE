import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import Testing
@testable import EstrichCore

@Suite("Chat: Nachrichten, Echtzeit, Warteschlange", .serialized)
struct ChatTests {
    @Test("Nachricht mit Fotos, Zitat, Reaktionen, Standort und Erwähnung")
    func richMessage() throws {
        let page = try JSONCoding.decoder.decode(ChatPage.self, from: Data("""
        {"items":[{"id":"0193a1b2-0000-7000-8000-0000000c0b10","conversationId":"0193a1b2-0000-7000-8000-0000000c0a01",
          "senderUserId":"0193a1b2-0000-7000-8000-0000000c0c02","senderName":"Sabine Wagner","text":"@Max Huber bitte prüfen",
          "createdAt":"2026-11-02T07:12:03Z","mine":false,"deleted":false,"editedAt":"2026-11-02T07:13:00Z","pinned":true,
          "attachments":[{"fileId":"0193a1b2-0000-7000-8000-0000000c0f01","kind":"photo","originalName":"Foto.jpg","contentType":"image/jpeg","sizeBytes":120000}],
          "replyTo":{"id":"0193a1b2-0000-7000-8000-0000000c0b09","senderName":"Max Huber","text":"Halle 2 fertig"},
          "reactions":[{"emoji":"👍","count":2,"mine":true}],
          "mentionUserIds":["0193a1b2-0000-7000-8000-0000000c0c01"],
          "location":{"latitude":48.37,"longitude":10.89,"label":"Baustelle"}}],"hasMore":true}
        """.utf8))
        let message = try #require(page.items.first)
        #expect(page.hasMore)
        #expect(message.attachments.first?.isPhoto == true && message.attachments.first?.name == "Foto.jpg")
        #expect(message.replyTo?.text == "Halle 2 fertig" && message.reactions.first?.mine == true)
        #expect(message.pinned && message.editedAt != nil && message.mentions.count == 1)
        #expect(message.location?.mapsURL?.absoluteString.contains("48.37,10.89") == true)
        let photoOnly = ChatMessage(id: UUID(), conversationId: nil, senderId: nil, senderName: nil, text: "", createdAt: Date(), state: .sent,
                                    attachments: [ChatAttachment(id: UUID(), kind: "photo", name: "a.jpg", contentType: "image/jpeg", sizeBytes: 1)])
        #expect(photoOnly.summary == "Foto")
    }

    @Test("Unterhaltungen: Arten, Stummschaltung, Ankündigungsgruppe")
    func conversations() throws {
        let list = try JSONCoding.decoder.decode(ChatList<ChatConversation>.self, from: Data("""
        [{"id":"0193a1b2-0000-7000-8000-0000000c0a01","title":"Alle Mitarbeiter","kind":"announcement","canWrite":false,"muted":true},
         {"id":"0193a1b2-0000-7000-8000-0000000c0a02","title":"Praxis Dr. Maier","kind":"site","bauakteId":"0193a1b2-0000-7000-8000-0000000b0001",
          "mutedUntil":"2099-01-01T00:00:00Z","unreadMentions":1}]
        """.utf8)).items
        #expect(list[0].chatKind == .announcement && !list[0].canWrite && list[0].isMuted())
        #expect(list[1].chatKind == .site && list[1].bauakteId != nil && list[1].mentioned && list[1].isMuted())
        let now = Date()
        #expect(ChatMuteOption.hour.until(from: now) == now.addingTimeInterval(3600) && ChatMuteOption.always.until(from: now) == nil)
    }

    @Test("Formen des Servers: Gruppeninfo verschachtelt, Art abgeleitet, Suchtreffer, Anhang")
    func serverShapes() throws {
        let detail = try JSONCoding.decoder.decode(ChatConversationDetail.self, from: Data("""
        {"conversation":{"id":"0193a1b2-0000-7000-8000-0000000c0a03","kind":"group","title":"Praxis","crewId":null,"members":[],
          "lastActivityAt":"2026-11-02T07:00:00Z","unreadCount":0,"muted":false,"description":"Baustelle","bauakteId":"0193a1b2-0000-7000-8000-0000000b0001",
          "announcementOnly":false,"imageUpdatedAt":"2026-11-02T06:00:00Z","isAdmin":true,"canPost":true,"unreadMentions":0},
         "members":[{"userId":"0193a1b2-0000-7000-8000-0000000c0c01","name":"Max","fromCrew":false,"isAdmin":true,"readUpTo":null}],
         "canManage":true,"pinned":[],"bauakteTitle":"Praxis Dr. Maier"}
        """.utf8))
        #expect(detail.chatKind == .site && detail.canManage && detail.members.first?.isAdmin == true && detail.description == "Baustelle")
        #expect(detail.imageUpdatedAt != nil && detail.bauakteTitle == "Praxis Dr. Maier")
        let announcement = try JSONCoding.decoder.decode(ChatConversation.self, from: Data("""
        {"id":"0193a1b2-0000-7000-8000-0000000c0a04","kind":"group","title":"Alle","announcementOnly":true,"canPost":false,"unreadCount":0}
        """.utf8))
        #expect(announcement.chatKind == .announcement && !announcement.canWrite)
        let hits = try JSONCoding.decoder.decode(ChatList<ChatSearchHit>.self, from: Data("""
        {"items":[{"messageId":"0193a1b2-0000-7000-8000-0000000c0b10","conversationId":"0193a1b2-0000-7000-8000-0000000c0a01",
          "conversationTitle":"Kolonne","senderName":"Lukas","snippet":"… Pumpe 2 …","createdAt":"2026-11-02T07:12:03Z"}],"searched":10,"truncated":false}
        """.utf8)).items
        #expect(hits.first?.message.text == "… Pumpe 2 …" && hits.first?.conversationTitle == "Kolonne")
        let attachment = try JSONCoding.decoder.decode(ChatAttachment.self, from: Data(#"{"id":"0193a1b2-0000-7000-8000-0000000c0f01","kind":"document","fileName":"Plan.pdf","contentType":"application/pdf","sizeBytes":5}"#.utf8))
        #expect(attachment.name == "Plan.pdf" && !attachment.isPhoto)
    }

    @Test("Bearbeiten nur eigene Nachrichten im Zeitfenster")
    func editWindow() {
        let me = UUID()
        let created = Date().addingTimeInterval(-10 * 60)
        let message = ChatMessage(id: UUID(), conversationId: nil, senderId: me, senderName: nil, text: "Hallo", createdAt: created, state: .sent)
        #expect(message.canEdit(userId: me, windowMinutes: 15))
        #expect(!message.canEdit(userId: me, windowMinutes: 5))
        #expect(!message.canEdit(userId: UUID(), windowMinutes: 15))
    }

    @Test("Server-Sent Events: Ereignisse, mehrzeilige Daten, Kommentare")
    func serverSentEvents() {
        var parser = ServerSentEventParser()
        var events: [ServerSentEventParser.Event] = []
        for line in [": keep-alive", "event: message", "id: 42", #"data: {"conversationId":"0193a1b2-0000-7000-8000-0000000c0a01","#,
                     #"data: "messageId":"0193a1b2-0000-7000-8000-0000000c0b10"}"#, "", "retry: 3000", "event: typing",
                     #"data: {"conversationId":"0193a1b2-0000-7000-8000-0000000c0a01","userName":"Lukas"}"#, "\r"] {
            if let event = parser.feed(line: line) { events.append(event) }
        }
        #expect(events.count == 2 && parser.lastEventId == "42" && parser.retryMilliseconds == 3000)
        let first = ChatEvent(name: events[0].name, data: events[0].data)
        #expect(first.kind == .message && first.conversationId != nil && first.messageId != nil)
        let typing = ChatEvent(name: events[1].name, data: events[1].data)
        #expect(typing.kind == .typing && typing.userName == "Lukas")
        #expect(ChatPolling.reconnectDelay(attempt: 1) == .seconds(1) && ChatPolling.reconnectDelay(attempt: 10) == .seconds(60))
    }

    @Test("@Erwähnungen erkennen und vervollständigen")
    func mentions() {
        let lukas = ChatMember(id: UUID(), displayName: "Lukas Brandl")
        let sabine = ChatMember(id: UUID(), displayName: "Sabine Wagner")
        #expect(ChatMentions.mentioned(in: "Danke @Lukas, passt", members: [lukas, sabine]) == [lukas.id])
        #expect(ChatMentions.mentioned(in: "@sabine wagner bitte", members: [lukas, sabine]) == [sabine.id])
        #expect(ChatMentions.mentioned(in: "@Lukasz", members: [lukas]).isEmpty)
        #expect(ChatMentions.pendingQuery(in: "Hallo @Lu") == "Lu" && ChatMentions.pendingQuery(in: "mail@firma") == nil)
        #expect(ChatMentions.complete("Hallo @Lu", with: lukas) == "Hallo @Lukas Brandl ")
        #expect(ChatMentions.suggestions(for: "br", members: [lukas, sabine], excluding: nil) == [lukas])
    }

    @Test("Warteschlange: Anhang zuerst, dann Nachricht; offline bleibt alles liegen, Ablehnung blockiert nicht")
    func outbox() async throws {
        let directory = FileManager.default.temporaryDirectory.appending(path: "chat-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        let outbox = ChatOutbox(store: store, files: files)
        let conversation = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000c0a01")!
        let photo = ChatOutboxItem.Attachment(id: UUID.v7(), kind: "photo", name: "Foto.jpg", contentType: "image/jpeg", sizeBytes: 3)
        let item = ChatOutboxItem(conversationId: conversation, text: "Halle 2", attachments: [photo])
        try await outbox.enqueue(item, contents: [photo.id: Data([1, 2, 3])])
        await outbox.setDraft("Entwurf", for: conversation)

        let transport = RoutingTransport()
        let session = StoredSession(Samples.tokens(Samples.tokenJSON()))
        let client = AuthorizedClient(api: APIClient(server: Samples.server, transport: transport), session: session, keys: nil,
                                      persist: { _ in }, ended: { _ in })
        // Ohne Verbindung: nichts gesendet, Eintrag bleibt.
        let offline = await outbox.flush(client: client)
        #expect(offline.offline && offline.sent.isEmpty)
        #expect(await outbox.items().count == 1)

        let serverPhoto = "0193a1b2-0000-7000-8000-0000000c0f99"
        transport.on("POST", "/api/v1/chat/conversations/\(conversation.key)/attachments") { request in
            #expect(request.url?.query?.contains("fileName=Foto.jpg") == true && request.httpBody == Data([1, 2, 3]))
            return .json(#"{"id":"\#(serverPhoto)","kind":"photo","fileName":"Foto.jpg","contentType":"image/jpeg","sizeBytes":3}"#, status: 201)
        }
        transport.on("POST", "/api/v1/chat/conversations/\(conversation.key)/messages") { request in
            let body = request.jsonBody()
            if body["text"] as? String == "Halle 2" {
                #expect(body["clientMessageId"] as? String == item.id.key)
                #expect(body["attachmentFileIds"] as? [String] == [serverPhoto])
            }
            return .json(#"{"id":"0193a1b2-0000-7000-8000-0000000c0b77","text":"Halle 2","createdAt":"2026-11-02T07:12:03Z","mine":true,"status":"sent"}"#, status: 201)
        }
        let sent = await outbox.flush(client: client)
        #expect(sent.sent.count == 1 && !sent.offline)
        #expect(await outbox.items().isEmpty)
        #expect(await outbox.content(of: photo.id) == nil)
        // Einmal ohne Verbindung versucht, einmal hochgeladen – danach nicht erneut.
        #expect(transport.count("POST", "/api/v1/chat/conversations/\(conversation.key)/attachments") == 2)

        // Ablehnung: markiert, die nächste Nachricht geht trotzdem.
        let other = UUID()
        try await outbox.enqueue(ChatOutboxItem(conversationId: other, text: "abgelehnt"), contents: [:])
        try await outbox.enqueue(ChatOutboxItem(conversationId: conversation, text: "geht"), contents: [:])
        transport.on("POST", "/api/v1/chat/conversations/\(other.key)/messages", reply: .problem(403, code: "PERMISSION_DENIED", detail: "Nur Admins schreiben."))
        let mixed = await outbox.flush(client: client)
        #expect(mixed.rejected.count == 1 && mixed.sent.count == 1)
        #expect(await outbox.items().first?.failure == "Nur Admins schreiben.")

        // Entwürfe überstehen ein neues Öffnen der Datenbank.
        let reopened = ChatOutbox(store: store, files: files)
        #expect(await reopened.draft(for: conversation) == "Entwurf")
        #expect(await reopened.items().count == 1)
    }
}
