import Foundation

/// Noch nicht gesendete Nachricht (offline oder Server nicht erreichbar) samt Anhängen. Liegt verschlüsselt auf dem
/// Gerät (Text in der lokalen Datenbank, Anhänge im Datei-Tresor) und wird beim nächsten Kontakt gesendet. Die ID ist
/// zugleich `clientMessageId`: Wiederholungen erzeugen keine doppelten Nachrichten.
public struct ChatOutboxItem: Codable, Sendable, Equatable, Identifiable {
    public struct Attachment: Codable, Sendable, Equatable, Identifiable {
        public let id: UUID
        public let kind: String
        public let name: String
        public let contentType: String
        public let sizeBytes: Int64
        /// ID beim Server nach dem Hochladen (die lokale `id` bleibt der Schlüssel im Tresor).
        public var serverId: UUID?

        public var uploaded: Bool { serverId != nil }

        public init(id: UUID, kind: String, name: String, contentType: String, sizeBytes: Int64, serverId: UUID? = nil) {
            self.id = id
            self.kind = kind
            self.name = name
            self.contentType = contentType
            self.sizeBytes = sizeBytes
            self.serverId = serverId
        }

        public var asAttachment: ChatAttachment {
            ChatAttachment(id: id, kind: kind, name: name, contentType: contentType, sizeBytes: sizeBytes)
        }
    }

    public let id: UUID
    public let conversationId: UUID
    public var text: String
    public var attachments: [Attachment]
    public var replyToId: UUID?
    public var replyToSender: String?
    public var replyToText: String?
    public var mentionUserIds: [UUID]
    public var location: ChatLocation?
    public let createdAt: Date
    public var attempts: Int
    /// Endgültig abgelehnt (z. B. keine Schreibrechte mehr): bleibt sichtbar, bis die Person sie löscht oder erneut sendet.
    public var failure: String?

    public init(id: UUID = UUID.v7(), conversationId: UUID, text: String, attachments: [Attachment] = [], replyToId: UUID? = nil,
                replyToSender: String? = nil, replyToText: String? = nil, mentionUserIds: [UUID] = [], location: ChatLocation? = nil,
                createdAt: Date = Date(), attempts: Int = 0, failure: String? = nil) {
        self.id = id
        self.conversationId = conversationId
        self.text = text
        self.attachments = attachments
        self.replyToId = replyToId
        self.replyToSender = replyToSender
        self.replyToText = replyToText
        self.mentionUserIds = mentionUserIds
        self.location = location
        self.createdAt = createdAt
        self.attempts = attempts
        self.failure = failure
    }

    public var body: ChatSendBody {
        ChatSendBody(text: text, clientMessageId: id, attachmentFileIds: attachments.map { $0.serverId ?? $0.id }, replyToId: replyToId,
                     mentionUserIds: mentionUserIds, location: location)
    }

    /// Anzeige im Verlauf, bis der Server die Nachricht bestätigt.
    public func preview(senderId: UUID?, senderName: String?, sending: Bool) -> ChatMessage {
        ChatMessage(id: id, conversationId: conversationId, senderId: senderId, senderName: senderName, text: text, createdAt: createdAt,
                    state: failure != nil ? .failed : (sending ? .sending : .queued), mine: true, clientMessageId: id,
                    attachments: attachments.map(\.asAttachment),
                    replyTo: replyToId.map { ChatReplyPreview(id: $0, senderName: replyToSender, text: replyToText ?? "") },
                    mentions: mentionUserIds, location: location)
    }
}

/// Ergebnis eines Sendeversuchs der Warteschlange.
public struct ChatFlushResult: Sendable {
    public var sent: [ChatMessage] = []
    public var rejected: [UUID] = []
    /// Ohne Verbindung abgebrochen (Rest bleibt in der Warteschlange).
    public var offline = false

    public init() {}
}

/// Warteschlange und Entwürfe des Chats (verschlüsselt in der lokalen Datenbank des Benutzers).
public actor ChatOutbox {
    private let store: LocalStore
    private let files: FileVault
    private var cache: [ChatOutboxItem]?
    private var flushing = false

    static let outboxKey = "chat_outbox"
    static let draftsKey = "chat_drafts"

    public init(store: LocalStore, files: FileVault) {
        self.store = store
        self.files = files
    }

    public func items() async -> [ChatOutboxItem] {
        if let cache { return cache }
        var loaded: [ChatOutboxItem] = []
        if let text = try? await store.meta(Self.outboxKey), let data = text.data(using: .utf8) {
            loaded = (try? JSONCoding.decoder.decode([ChatOutboxItem].self, from: data)) ?? []
        }
        cache = loaded
        return loaded
    }

    public func items(in conversationId: UUID) async -> [ChatOutboxItem] {
        await items().filter { $0.conversationId == conversationId }
    }

    /// Legt die Nachricht ab: Anhänge zuerst verschlüsselt in den Tresor, dann der Eintrag.
    public func enqueue(_ item: ChatOutboxItem, contents: [UUID: Data]) async throws {
        for attachment in item.attachments {
            guard let data = contents[attachment.id] else { continue }
            _ = try files.store(data, for: attachment.id)
        }
        var list = await items()
        list.removeAll { $0.id == item.id }
        list.append(item)
        try await save(list)
    }

    /// Erneut senden (nach einer Ablehnung): Fehler zurücksetzen.
    public func retry(_ id: UUID) async throws {
        var list = await items()
        guard let index = list.firstIndex(where: { $0.id == id }) else { return }
        list[index].failure = nil
        list[index].attempts = 0
        try await save(list)
    }

    /// Entfernen (gesendet oder von der Person verworfen) – mit den Anhängen im Tresor.
    public func remove(_ id: UUID) async {
        var list = await items()
        guard let index = list.firstIndex(where: { $0.id == id }) else { return }
        for attachment in list[index].attachments { files.remove(attachment.id) }
        list.remove(at: index)
        try? await save(list)
    }

    /// Inhalt eines wartenden Anhangs (Vorschau im Verlauf).
    public func content(of attachmentId: UUID) -> Data? {
        try? files.read(attachmentId)
    }

    /// Sendet alles der Reihe nach: erst die Anhänge, dann die Nachricht. Ohne Verbindung bleibt der Rest liegen,
    /// Ablehnungen des Servers (4xx) werden markiert und blockieren die übrigen Nachrichten nicht.
    public func flush(client: AuthorizedClient) async -> ChatFlushResult {
        var result = ChatFlushResult()
        guard !flushing else { return result }
        flushing = true
        defer { flushing = false }
        for original in await items() where original.failure == nil {
            var item = original
            do throws(APIError) {
                for index in item.attachments.indices where !item.attachments[index].uploaded {
                    let attachment = item.attachments[index]
                    guard let data = try? files.read(attachment.id) else {
                        throw APIError.server(status: 410, code: "ATTACHMENT_MISSING", message: "Der Anhang „\(attachment.name)“ ist auf dem Gerät nicht mehr vorhanden.")
                    }
                    let stored = try await client.uploadChatAttachment(conversationId: item.conversationId, name: attachment.name,
                                                                       contentType: attachment.contentType, data: data)
                    item.attachments[index].serverId = stored.id
                    await replace(item)
                }
                let message = try await client.sendChatMessage(item.body, to: item.conversationId)
                result.sent.append(message)
                await remove(item.id)
            } catch {
                if error == .offline || error == .certificateInvalid || (error.status ?? 0) >= 500 || error.status == 408 || error.status == 429 {
                    item.attempts += 1
                    await replace(item)
                    result.offline = true
                    return result
                }
                item.attempts += 1
                item.failure = error.serverMessage ?? error.message
                await replace(item)
                result.rejected.append(item.id)
            }
        }
        return result
    }

    private func replace(_ item: ChatOutboxItem) async {
        var list = await items()
        guard let index = list.firstIndex(where: { $0.id == item.id }) else { return }
        list[index] = item
        try? await save(list)
    }

    private func save(_ list: [ChatOutboxItem]) async throws {
        cache = list
        let data = try JSONCoding.encoder.encode(list)
        try await store.setMeta(Self.outboxKey, String(decoding: data, as: UTF8.self))
    }

    // MARK: Entwürfe

    private var drafts: [String: String]?

    private func loadDrafts() async -> [String: String] {
        if let drafts { return drafts }
        var loaded: [String: String] = [:]
        if let text = try? await store.meta(Self.draftsKey), let data = text.data(using: .utf8) {
            loaded = (try? JSONCoding.decoder.decode([String: String].self, from: data)) ?? [:]
        }
        drafts = loaded
        return loaded
    }

    public func draft(for conversationId: UUID) async -> String? {
        await loadDrafts()[conversationId.key]
    }

    /// Entwurf merken (leerer Text löscht ihn).
    public func setDraft(_ text: String, for conversationId: UUID) async {
        var all = await loadDrafts()
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { all[conversationId.key] = nil } else { all[conversationId.key] = String(text.prefix(ChatLimits.maxLength)) }
        guard all != drafts else { return }
        drafts = all
        if let data = try? JSONCoding.encoder.encode(all) {
            try? await store.setMeta(Self.draftsKey, String(decoding: data, as: UTF8.self))
        }
    }

    public func allDrafts() async -> [UUID: String] {
        var result: [UUID: String] = [:]
        for (key, value) in await loadDrafts() {
            if let id = UUID(uuidString: key) { result[id] = value }
        }
        return result
    }
}

/// `@Erwähnungen`: wer im Text genannt ist, und Vorschläge beim Tippen von „@…“.
public enum ChatMentions {
    /// Personen, deren Name (voll oder Vorname) mit „@“ im Text steht.
    public static func mentioned(in text: String, members: [ChatMember]) -> [UUID] {
        let lowered = text.lowercased()
        guard lowered.contains("@") else { return [] }
        return members.filter { member in
            let full = member.displayName.lowercased()
            guard !full.isEmpty else { return false }
            if lowered.contains("@" + full) { return true }
            guard let first = full.split(separator: " ").first.map(String.init) else { return false }
            return contains(token: "@" + first, in: lowered)
        }
        .map(\.id)
    }

    /// Angefangene Erwähnung am Ende des Textes („Danke @Lu“ → „Lu“), sonst `nil`.
    public static func pendingQuery(in text: String) -> String? {
        guard let at = text.lastIndex(of: "@") else { return nil }
        if at > text.startIndex {
            let before = text[text.index(before: at)]
            guard before.isWhitespace || before.isNewline else { return nil }
        }
        let query = text[text.index(after: at)...]
        guard !query.contains(where: { $0.isNewline }), query.count <= 24, query.filter({ $0 == " " }).count <= 1 else { return nil }
        return String(query)
    }

    /// Ersetzt die angefangene Erwähnung durch den vollen Namen.
    public static func complete(_ text: String, with member: ChatMember) -> String {
        guard let at = text.lastIndex(of: "@") else { return text + "@" + member.displayName + " " }
        return String(text[..<at]) + "@" + member.displayName + " "
    }

    /// Vorschläge zur angefangenen Erwähnung.
    public static func suggestions(for query: String, members: [ChatMember], excluding userId: UUID?) -> [ChatMember] {
        let term = query.lowercased()
        let matches = members.filter { member in
            guard member.id != userId else { return false }
            if term.isEmpty { return true }
            let name = member.displayName.lowercased()
            return name.hasPrefix(term) || name.split(separator: " ").contains { $0.hasPrefix(term) }
        }
        return Array(matches.prefix(5))
    }

    private static func contains(token: String, in text: String) -> Bool {
        var searchRange = text.startIndex..<text.endIndex
        while let range = text.range(of: token, range: searchRange) {
            let next = range.upperBound
            if next == text.endIndex || !(text[next].isLetter || text[next].isNumber) { return true }
            searchRange = next..<text.endIndex
        }
        return false
    }
}
