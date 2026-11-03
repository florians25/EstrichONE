import Foundation

/// Ereignis aus dem Echtzeit-Kanal des Chats (`GET /chat/events`, Server-Sent Events). Die App lädt daraufhin gezielt
/// nach (neue Nachrichten der Unterhaltung, Liste); der Kanal selbst trägt nur Kennungen und – bei „schreibt“ – den Namen.
public struct ChatEvent: Sendable, Equatable {
    public enum Kind: Sendable, Equatable {
        /// Neue Nachricht.
        case message
        /// Nachricht geändert (bearbeitet, gelöscht, Reaktion, angeheftet).
        case messageChanged
        /// Lese- bzw. Zustellstand geändert.
        case read
        /// Jemand schreibt gerade.
        case typing
        /// Unterhaltung geändert (Name, Mitglieder, Stummschaltung, neu, entfernt).
        case conversation
        /// Status (Modul, ungelesen) geändert.
        case status
        /// Verbindungstest des Servers.
        case ping
        case other(String)
    }

    public let kind: Kind
    public let conversationId: UUID?
    public let messageId: UUID?
    public let userId: UUID?
    public let userName: String?

    public init(kind: Kind, conversationId: UUID? = nil, messageId: UUID? = nil, userId: UUID? = nil, userName: String? = nil) {
        self.kind = kind
        self.conversationId = conversationId
        self.messageId = messageId
        self.userId = userId
        self.userName = userName
    }

    /// Aus Ereignisname und JSON-Daten. Unbekannte Namen bleiben `.other`; fehlerhafte Daten nur ohne Kennungen.
    public init(name: String, data: String) {
        let lowered = name.lowercased()
        switch lowered {
        case "message", "message.created", "chat.message", "message_created": kind = .message
        case "message.updated", "message.changed", "message.deleted", "message.reaction", "message.pinned", "reaction": kind = .messageChanged
        case "read", "receipt", "message.read", "delivered": kind = .read
        case "typing": kind = .typing
        case "conversation", "conversation.updated", "conversation.created", "conversation.deleted", "conversation.removed", "membership": kind = .conversation
        case "status", "unread": kind = .status
        case "ping", "heartbeat", "hello", "ready": kind = .ping
        default: kind = .other(lowered)
        }
        let object = (try? JSONSerialization.jsonObject(with: Data(data.utf8))) as? [String: Any] ?? [:]
        func uuid(_ keys: String...) -> UUID? {
            for key in keys {
                if let text = object[key] as? String, let id = UUID(uuidString: text) { return id }
            }
            return nil
        }
        conversationId = uuid("conversationId", "conversation")
        messageId = uuid("messageId", "id")
        userId = uuid("userId", "senderUserId")
        userName = (object["userName"] as? String) ?? (object["name"] as? String) ?? (object["displayName"] as? String)
    }
}

/// Liest Server-Sent Events zeilenweise (W3C): `event:`, `data:` (mehrzeilig), `id:`, `retry:`; Leerzeile beendet ein
/// Ereignis, Zeilen mit `:` sind Kommentare (Keep-alive hinter Proxys).
public struct ServerSentEventParser: Sendable {
    public struct Event: Sendable, Equatable {
        public let name: String
        public let data: String
        public let id: String?
    }

    private var name = ""
    private var data: [String] = []
    private var lastId: String?
    /// Vom Server gewünschter Abstand für Neuverbindungen (Millisekunden).
    public private(set) var retryMilliseconds: Int?
    /// Letzte Ereignis-ID (für `Last-Event-ID` beim Wiederverbinden).
    public var lastEventId: String? { lastId }

    public init() {}

    /// Eine Zeile ohne Zeilenende; liefert ein vollständiges Ereignis bei der Leerzeile.
    public mutating func feed(line raw: String) -> Event? {
        let line = raw.hasSuffix("\r") ? String(raw.dropLast()) : raw
        if line.isEmpty {
            defer {
                name = ""
                data = []
            }
            guard !data.isEmpty else { return nil }
            return Event(name: name.isEmpty ? "message" : name, data: data.joined(separator: "\n"), id: lastId)
        }
        if line.hasPrefix(":") { return nil }
        let field: Substring
        var value: Substring
        if let colon = line.firstIndex(of: ":") {
            field = line[..<colon]
            value = line[line.index(after: colon)...]
            if value.hasPrefix(" ") { value = value.dropFirst() }
        } else {
            field = Substring(line)
            value = ""
        }
        switch field {
        case "event": name = String(value)
        case "data": data.append(String(value))
        case "id": lastId = String(value)
        case "retry": retryMilliseconds = Int(value)
        default: break
        }
        return nil
    }
}

/// Wann wie oft nachgefragt wird: mit Echtzeit-Kanal selten (Sicherheitsnetz), ohne ihn wie bisher.
public enum ChatPolling {
    /// Geöffnete Unterhaltung.
    public static func conversationInterval(live: Bool) -> Duration { live ? .seconds(30) : .seconds(5) }
    /// Liste der Unterhaltungen.
    public static func listInterval(live: Bool) -> Duration { live ? .seconds(60) : .seconds(15) }
    /// Wartezeit vor dem n-ten Wiederverbinden (1, 2, 4 … höchstens 60 Sekunden).
    public static func reconnectDelay(attempt: Int, serverRetryMilliseconds: Int? = nil) -> Duration {
        if let serverRetryMilliseconds, attempt <= 1 { return .milliseconds(max(500, serverRetryMilliseconds)) }
        let seconds = min(60, 1 << min(max(attempt - 1, 0), 6))
        return .seconds(seconds)
    }
}
