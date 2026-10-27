import Foundation

/// Mini-Chat zwischen Büro und Mitarbeitern (Modul „Chat“, docs/chat.md): nur Text, Einzel- und Gruppenunterhaltungen.
/// Inhalte liegen verschlüsselt auf dem Server; die App hält sie nur im Arbeitsspeicher.
public struct ChatStatus: Decodable, Sendable, Equatable {
    /// Modul im Betrieb eingeschaltet.
    public let enabled: Bool
    /// Person darf chatten (Recht `chat.use`).
    public let canUse: Bool
    public let unreadCount: Int

    public init(enabled: Bool, canUse: Bool = true, unreadCount: Int = 0) {
        self.enabled = enabled
        self.canUse = canUse
        self.unreadCount = unreadCount
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? c.decodeIfPresent(Bool.self, forKey: .moduleEnabled) ?? false
        canUse = try c.decodeIfPresent(Bool.self, forKey: .canUse) ?? c.decodeIfPresent(Bool.self, forKey: .allowed) ?? true
        unreadCount = try c.decodeIfPresent(Int.self, forKey: .unreadCount) ?? c.decodeIfPresent(Int.self, forKey: .unread) ?? 0
    }

    private enum CodingKeys: String, CodingKey {
        case enabled, moduleEnabled, canUse, allowed, unreadCount, unread
    }

    public var isAvailable: Bool { enabled && canUse }
}

public struct ChatMember: Decodable, Sendable, Equatable, Identifiable, Hashable {
    public let id: UUID
    public let displayName: String

    public init(id: UUID, displayName: String) {
        self.id = id
        self.displayName = displayName
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .userId) ?? c.decode(UUID.self, forKey: .id)
        displayName = try c.decodeIfPresent(String.self, forKey: .displayName) ?? c.decodeIfPresent(String.self, forKey: .name) ?? ""
    }

    private enum CodingKeys: String, CodingKey {
        case id, userId, displayName, name
    }
}

/// Nachricht. `state`: `sent` (beim Server), `delivered` (bei allen angekommen), `read` (von allen gelesen).
public struct ChatMessage: Decodable, Sendable, Equatable, Identifiable {
    public enum State: String, Sendable, Equatable {
        case sending, sent, delivered, read, failed
    }

    public let id: UUID
    public let conversationId: UUID?
    public let senderId: UUID?
    public let senderName: String?
    public let text: String
    public let createdAt: Date
    public var state: State
    /// Eigene Nachricht (vom Server gesetzt; `nil` bei älteren Antworten – dann zählt der Absender).
    public let mine: Bool?
    /// Gelöscht: Text entfernt, Platzhalter bleibt.
    public let deleted: Bool
    /// Gruppen, eigene Nachrichten: gelesen von `readBy` der `recipients` Empfänger.
    public let readBy: Int
    public let recipients: Int
    /// ID aus der Warteschlange der App (doppeltes Senden ist unschädlich).
    public let clientMessageId: UUID?

    public init(id: UUID, conversationId: UUID?, senderId: UUID?, senderName: String?, text: String, createdAt: Date, state: State,
                mine: Bool? = nil, deleted: Bool = false, readBy: Int = 0, recipients: Int = 0, clientMessageId: UUID? = nil) {
        self.id = id
        self.conversationId = conversationId
        self.senderId = senderId
        self.senderName = senderName
        self.text = text
        self.createdAt = createdAt
        self.state = state
        self.mine = mine
        self.deleted = deleted
        self.readBy = readBy
        self.recipients = recipients
        self.clientMessageId = clientMessageId
    }

    /// Eigene Nachricht: laut Server bzw. über den Absender.
    public func isMine(userId: UUID?) -> Bool {
        mine ?? (senderId != nil && senderId == userId)
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        conversationId = try? c.decodeIfPresent(UUID.self, forKey: .conversationId)
        senderId = (try? c.decodeIfPresent(UUID.self, forKey: .senderId)) ?? (try? c.decodeIfPresent(UUID.self, forKey: .senderUserId))
        senderName = try c.decodeIfPresent(String.self, forKey: .senderName)
        deleted = (try? c.decodeIfPresent(Bool.self, forKey: .deleted)) ?? false
        text = (try? c.decodeIfPresent(String.self, forKey: .text)) ?? (try? c.decodeIfPresent(String.self, forKey: .body)) ?? ""
        mine = try? c.decodeIfPresent(Bool.self, forKey: .mine)
        readBy = (try? c.decodeIfPresent(Int.self, forKey: .readBy)) ?? 0
        recipients = (try? c.decodeIfPresent(Int.self, forKey: .recipients)) ?? 0
        clientMessageId = try? c.decodeIfPresent(UUID.self, forKey: .clientMessageId)
        createdAt = try c.decodeIfPresent(Date.self, forKey: .createdAt) ?? c.decodeIfPresent(Date.self, forKey: .sentAt) ?? Date()
        if let raw = (try? c.decodeIfPresent(String.self, forKey: .status)) ?? (try? c.decodeIfPresent(String.self, forKey: .state)), let known = State(rawValue: raw.lowercased()) {
            state = known
        } else if (try? c.decodeIfPresent(Date.self, forKey: .readAt)) != nil || (try? c.decodeIfPresent(Bool.self, forKey: .readByAll)) == true {
            state = .read
        } else if (try? c.decodeIfPresent(Date.self, forKey: .deliveredAt)) != nil || (try? c.decodeIfPresent(Bool.self, forKey: .deliveredToAll)) == true {
            state = .delivered
        } else {
            state = .sent
        }
    }

    private enum CodingKeys: String, CodingKey {
        case id, conversationId, senderId, senderUserId, senderName, text, body, createdAt, sentAt, state, status, readAt, deliveredAt, readByAll, deliveredToAll
        case mine, deleted, readBy, recipients, clientMessageId
    }
}

/// Unterhaltung in der Liste: Titel, letzte Nachricht, ungelesene Nachrichten.
public struct ChatConversation: Decodable, Sendable, Equatable, Identifiable, Hashable {
    public let id: UUID
    public let title: String
    /// `direct` oder `group`.
    public let kind: String
    public let members: [ChatMember]
    public let lastMessageText: String?
    public let lastMessageSender: String?
    public let lastMessageAt: Date?
    public var unreadCount: Int

    public var isGroup: Bool { kind != "direct" }

    public init(id: UUID, title: String, kind: String, members: [ChatMember] = [], lastMessageText: String?, lastMessageSender: String?,
                lastMessageAt: Date?, unreadCount: Int) {
        self.id = id
        self.title = title
        self.kind = kind
        self.members = members
        self.lastMessageText = lastMessageText
        self.lastMessageSender = lastMessageSender
        self.lastMessageAt = lastMessageAt
        self.unreadCount = unreadCount
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? c.decodeIfPresent(String.self, forKey: .name) ?? "Unterhaltung"
        kind = (try c.decodeIfPresent(String.self, forKey: .kind) ?? c.decodeIfPresent(String.self, forKey: .type) ?? "group").lowercased()
        members = (try? c.decodeIfPresent([ChatMember].self, forKey: .members)) ?? []
        let last = try? c.decodeIfPresent(ChatMessage.self, forKey: .lastMessage)
        if let last {
            lastMessageText = last.deleted ? "Nachricht gelöscht" : last.text
            lastMessageSender = last.mine == true ? "Ich" : last.senderName
        } else {
            lastMessageText = (try? c.decodeIfPresent(String.self, forKey: .lastMessageText)) ?? nil
            lastMessageSender = nil
        }
        lastMessageAt = (try? c.decodeIfPresent(Date.self, forKey: .lastActivityAt)) ?? last?.createdAt
            ?? (try? c.decodeIfPresent(Date.self, forKey: .lastMessageAt))
        unreadCount = try c.decodeIfPresent(Int.self, forKey: .unreadCount) ?? 0
    }

    private enum CodingKeys: String, CodingKey {
        case id, title, name, kind, type, members, lastMessage, lastMessageText, lastMessageAt, lastActivityAt, unreadCount
    }

    public static func == (lhs: ChatConversation, rhs: ChatConversation) -> Bool {
        lhs.id == rhs.id && lhs.unreadCount == rhs.unreadCount && lhs.lastMessageAt == rhs.lastMessageAt && lhs.title == rhs.title
    }

    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
}

/// Liste oder Seite `{ items }`.
struct ChatList<Item: Decodable & Sendable>: Decodable, Sendable {
    let items: [Item]

    init(from decoder: any Decoder) throws {
        if let list = try? decoder.singleValueContainer().decode([Item].self) {
            items = list
            return
        }
        let c = try decoder.container(keyedBy: CodingKeys.self)
        items = try c.decodeIfPresent([Item].self, forKey: .items) ?? c.decodeIfPresent([Item].self, forKey: .messages) ?? []
    }

    private enum CodingKeys: String, CodingKey {
        case items, messages
    }
}

/// Höchstlänge einer Nachricht (wie im Web).
public enum ChatLimits {
    public static let maxLength = 4000

    /// Text zum Senden: getrimmt, nicht leer, höchstens `maxLength` Zeichen.
    public static func clean(_ text: String) -> String? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }
        return String(trimmed.prefix(maxLength))
    }
}

private struct ChatSendBody: Encodable, Sendable {
    let text: String
    let clientMessageId: String
}

private struct ChatEmptyBody: Encodable, Sendable {}

private struct ChatDirectBody: Encodable, Sendable {
    let userId: String
}

extension AuthorizedClient {
    /// Modul aktiv und Recht vorhanden? Kennt der Server den Chat nicht (404) oder ist er aus (403), ist er nicht verfügbar.
    public func chatStatus() async throws(APIError) -> ChatStatus {
        do throws(APIError) {
            let status: ChatStatus = try await get("chat/status")
            return status
        } catch {
            if error.status == 404 || error.status == 403 { return ChatStatus(enabled: false, canUse: false) }
            throw error
        }
    }

    public func chatConversations() async throws(APIError) -> [ChatConversation] {
        let list: ChatList<ChatConversation> = try await get("chat/conversations")
        return list.items
    }

    /// Verlauf, älteste zuerst. Mit `after` nur neue Nachrichten seit dem letzten Abruf (Polling, docs/chat.md).
    public func chatMessages(_ conversationId: UUID, limit: Int = 50, after: Date? = nil) async throws(APIError) -> [ChatMessage] {
        var query = [URLQueryItem(name: "limit", value: String(limit))]
        if let after { query.append(URLQueryItem(name: "after", value: APIDate.format(after))) }
        let list: ChatList<ChatMessage> = try await get("chat/conversations/\(conversationId.key)/messages", query: query)
        return list.items.sorted { $0.createdAt < $1.createdAt }
    }

    /// Personen, mit denen ein Einzelchat möglich ist.
    public func chatContacts() async throws(APIError) -> [ChatMember] {
        try await get("chat/contacts")
    }

    /// Einzelchat öffnen (vorhandenen oder neuen).
    public func startDirectChat(with userId: UUID) async throws(APIError) -> ChatConversation {
        try await post("chat/conversations/direct", body: ChatDirectBody(userId: userId.key))
    }

    /// Sendet eine Textnachricht. `clientMessageId` macht doppeltes Senden (Wiederholung nach Zeitüberschreitung) unschädlich.
    public func sendChatMessage(_ text: String, to conversationId: UUID, clientMessageId: UUID) async throws(APIError) -> ChatMessage {
        try await post("chat/conversations/\(conversationId.key)/messages", body: ChatSendBody(text: text, clientMessageId: clientMessageId.key))
    }

    public func markChatRead(_ conversationId: UUID) async throws(APIError) {
        let _: NoContent = try await post("chat/conversations/\(conversationId.key)/read", body: ChatEmptyBody())
    }
}
