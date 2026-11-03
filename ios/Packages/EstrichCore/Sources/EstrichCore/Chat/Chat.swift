import Foundation

/// Chat des Betriebs (Modul „Chat“, docs/chat.md): Einzel-, Gruppen-, Kolonnen-, Baustellen- und Ankündigungsgruppen,
/// Fotos und Dokumente, Standort, Zitate, Reaktionen, Bearbeiten/Löschen, Anheften. Keine Sprachnachrichten.
/// Inhalte liegen verschlüsselt auf dem Server; die App hält den Verlauf nur im Arbeitsspeicher. Lokal (verschlüsselt)
/// liegen nur noch nicht gesendete Nachrichten samt Anhängen und Entwürfe (`ChatOutbox`).
public struct ChatStatus: Decodable, Sendable, Equatable {
    /// Modul im Betrieb eingeschaltet.
    public let enabled: Bool
    /// Person darf chatten (Recht `chat.use`).
    public let canUse: Bool
    /// Recht `chat.admin` (Gruppen anlegen, Moderation).
    public let canAdmin: Bool
    public let unreadCount: Int
    /// Unterhaltungen mit ungelesenen Nachrichten (Zahl am App-Symbol).
    public let unreadConversations: Int
    public let retentionDays: Int?
    /// Text in Pushnachrichten erlaubt (Betrieb).
    public let showPreview: Bool
    /// Zeitfenster zum Bearbeiten eigener Nachrichten in Minuten.
    public let editWindowMinutes: Int
    /// Stand des Datenschutzhinweises; die App zeigt ihn beim ersten Öffnen bzw. nach einer Änderung.
    public let privacyNoticeVersion: Int
    /// Datenschutzhinweis schon bestätigt (Server).
    public let privacyAccepted: Bool

    public init(enabled: Bool, canUse: Bool = true, canAdmin: Bool = false, unreadCount: Int = 0, unreadConversations: Int? = nil,
                retentionDays: Int? = nil, showPreview: Bool = false, editWindowMinutes: Int = 15, privacyNoticeVersion: Int = 1,
                privacyAccepted: Bool = false) {
        self.enabled = enabled
        self.canUse = canUse
        self.canAdmin = canAdmin
        self.unreadCount = unreadCount
        self.unreadConversations = unreadConversations ?? (unreadCount > 0 ? 1 : 0)
        self.retentionDays = retentionDays
        self.showPreview = showPreview
        self.editWindowMinutes = editWindowMinutes
        self.privacyNoticeVersion = privacyNoticeVersion
        self.privacyAccepted = privacyAccepted
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? c.decodeIfPresent(Bool.self, forKey: .moduleEnabled) ?? false
        canUse = try c.decodeIfPresent(Bool.self, forKey: .canUse) ?? c.decodeIfPresent(Bool.self, forKey: .allowed) ?? true
        canAdmin = (try? c.decodeIfPresent(Bool.self, forKey: .canAdmin)) ?? false
        unreadCount = try c.decodeIfPresent(Int.self, forKey: .unreadCount) ?? c.decodeIfPresent(Int.self, forKey: .unread) ?? 0
        unreadConversations = (try? c.decodeIfPresent(Int.self, forKey: .unreadConversations)) ?? (unreadCount > 0 ? 1 : 0)
        retentionDays = try? c.decodeIfPresent(Int.self, forKey: .retentionDays)
        showPreview = (try? c.decodeIfPresent(Bool.self, forKey: .showPreview)) ?? false
        editWindowMinutes = (try? c.decodeIfPresent(Int.self, forKey: .editWindowMinutes)) ?? 15
        privacyNoticeVersion = (try? c.decodeIfPresent(Int.self, forKey: .privacyNoticeVersion)) ?? 1
        privacyAccepted = (try? c.decodeIfPresent(Bool.self, forKey: .privacyAccepted)) ?? false
    }

    private enum CodingKeys: String, CodingKey {
        case enabled, moduleEnabled, canUse, allowed, canAdmin, unreadCount, unread, unreadConversations, retentionDays, showPreview
        case editWindowMinutes, privacyNoticeVersion, privacyAccepted
    }

    public var isAvailable: Bool { enabled && canUse }
}

public struct ChatMember: Decodable, Sendable, Equatable, Identifiable, Hashable {
    public let id: UUID
    public let displayName: String
    /// Gruppenadmin (darf Name, Bild, Beschreibung und Mitglieder ändern).
    public let isAdmin: Bool
    /// Über die Kolonne bzw. Bauakte automatisch dabei.
    public let automatic: Bool
    /// Gelesen bis (Lesebestätigung je Mitglied; `nil`, wenn abgeschaltet oder unbekannt).
    public let readUpTo: Date?
    /// Rolle/Funktion zur Anzeige, z. B. „Bauleitung“.
    public let role: String?

    public init(id: UUID, displayName: String, isAdmin: Bool = false, automatic: Bool = false, readUpTo: Date? = nil, role: String? = nil) {
        self.id = id
        self.displayName = displayName
        self.isAdmin = isAdmin
        self.automatic = automatic
        self.readUpTo = readUpTo
        self.role = role
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .userId) ?? c.decode(UUID.self, forKey: .id)
        displayName = try c.decodeIfPresent(String.self, forKey: .displayName) ?? c.decodeIfPresent(String.self, forKey: .name) ?? ""
        let roleName = try? c.decodeIfPresent(String.self, forKey: .role)
        isAdmin = (try? c.decodeIfPresent(Bool.self, forKey: .isAdmin)) ?? (try? c.decodeIfPresent(Bool.self, forKey: .admin)) ?? (roleName == "admin")
        let fromCrew = try? c.decodeIfPresent(Bool.self, forKey: .fromCrew)
        let fromBauakte = try? c.decodeIfPresent(Bool.self, forKey: .fromBauakte)
        automatic = (try? c.decodeIfPresent(Bool.self, forKey: .automatic)) ?? (fromCrew == true || fromBauakte == true)
        readUpTo = try? c.decodeIfPresent(Date.self, forKey: .readUpTo)
        role = (try? c.decodeIfPresent(String.self, forKey: .label)) ?? (roleName == "admin" || roleName == "member" ? nil : roleName)
    }

    private enum CodingKeys: String, CodingKey {
        case id, userId, displayName, name, isAdmin, admin, role, fromCrew, fromBauakte, automatic, readUpTo, label
    }
}

/// Foto oder Dokument an einer Nachricht. Inhalt: `GET /chat/attachments/{id}/content` (Vorschau `?variant=thumb`).
public struct ChatAttachment: Codable, Sendable, Equatable, Identifiable, Hashable {
    public let id: UUID
    /// `photo` oder `document`.
    public let kind: String
    public let name: String
    public let contentType: String
    public let sizeBytes: Int64
    public let width: Int?
    public let height: Int?

    public var isPhoto: Bool { kind == "photo" || contentType.hasPrefix("image/") }

    public init(id: UUID, kind: String, name: String, contentType: String, sizeBytes: Int64, width: Int? = nil, height: Int? = nil) {
        self.id = id
        self.kind = kind
        self.name = name
        self.contentType = contentType
        self.sizeBytes = sizeBytes
        self.width = width
        self.height = height
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .fileId) ?? c.decode(UUID.self, forKey: .id)
        contentType = (try? c.decodeIfPresent(String.self, forKey: .contentType)) ?? "application/octet-stream"
        kind = (try? c.decodeIfPresent(String.self, forKey: .kind)) ?? (contentType.hasPrefix("image/") ? "photo" : "document")
        name = (try? c.decodeIfPresent(String.self, forKey: .fileName)) ?? (try? c.decodeIfPresent(String.self, forKey: .name))
            ?? (try? c.decodeIfPresent(String.self, forKey: .originalName))
            ?? (kind == "photo" ? "Foto.jpg" : "Dokument.pdf")
        sizeBytes = (try? c.decodeIfPresent(Int64.self, forKey: .sizeBytes)) ?? (try? c.decodeIfPresent(Int64.self, forKey: .size)) ?? 0
        width = try? c.decodeIfPresent(Int.self, forKey: .width)
        height = try? c.decodeIfPresent(Int.self, forKey: .height)
    }

    public func encode(to encoder: any Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(kind, forKey: .kind)
        try c.encode(name, forKey: .name)
        try c.encode(contentType, forKey: .contentType)
        try c.encode(sizeBytes, forKey: .sizeBytes)
        try c.encodeIfPresent(width, forKey: .width)
        try c.encodeIfPresent(height, forKey: .height)
    }

    private enum CodingKeys: String, CodingKey {
        case id, fileId, kind, name, originalName, fileName, contentType, sizeBytes, size, width, height
    }
}

/// Geteilter Standort bzw. Baustellenadresse (Karte, Route).
public struct ChatLocation: Codable, Sendable, Equatable, Hashable {
    public let latitude: Double
    public let longitude: Double
    /// z. B. „Baustelle Praxis Dr. Maier“ oder „Mein Standort“.
    public let label: String?
    public let address: String?

    public init(latitude: Double, longitude: Double, label: String? = nil, address: String? = nil) {
        self.latitude = latitude
        self.longitude = longitude
        self.label = label
        self.address = address
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        latitude = try c.decodeIfPresent(Double.self, forKey: .latitude) ?? c.decode(Double.self, forKey: .lat)
        longitude = try c.decodeIfPresent(Double.self, forKey: .longitude) ?? c.decode(Double.self, forKey: .lng)
        label = try? c.decodeIfPresent(String.self, forKey: .label)
        address = try? c.decodeIfPresent(String.self, forKey: .address)
    }

    public func encode(to encoder: any Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(latitude, forKey: .latitude)
        try c.encode(longitude, forKey: .longitude)
        try c.encodeIfPresent(label, forKey: .label)
        try c.encodeIfPresent(address, forKey: .address)
    }

    private enum CodingKeys: String, CodingKey {
        case latitude, longitude, lat, lng, label, address
    }

    /// Route in Apple Karten.
    public var mapsURL: URL? {
        var components = URLComponents(string: "https://maps.apple.com/")
        var items = [URLQueryItem(name: "ll", value: "\(latitude),\(longitude)"), URLQueryItem(name: "daddr", value: "\(latitude),\(longitude)")]
        if let label { items.append(URLQueryItem(name: "q", value: label)) }
        components?.queryItems = items
        return components?.url
    }
}

/// Zitierte Nachricht (Antwort).
public struct ChatReplyPreview: Decodable, Sendable, Equatable, Hashable {
    public let id: UUID
    public let senderName: String?
    public let text: String
    public let deleted: Bool
    /// `photo`/`document`, wenn die zitierte Nachricht einen Anhang hat.
    public let attachmentKind: String?

    public init(id: UUID, senderName: String?, text: String, deleted: Bool = false, attachmentKind: String? = nil) {
        self.id = id
        self.senderName = senderName
        self.text = text
        self.deleted = deleted
        self.attachmentKind = attachmentKind
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .messageId) ?? c.decode(UUID.self, forKey: .id)
        senderName = try? c.decodeIfPresent(String.self, forKey: .senderName)
        deleted = (try? c.decodeIfPresent(Bool.self, forKey: .deleted)) ?? false
        text = (try? c.decodeIfPresent(String.self, forKey: .text)) ?? (try? c.decodeIfPresent(String.self, forKey: .excerpt)) ?? ""
        attachmentKind = (try? c.decodeIfPresent(String.self, forKey: .attachment)) ?? (try? c.decodeIfPresent(String.self, forKey: .attachmentKind))
    }

    private enum CodingKeys: String, CodingKey {
        case id, messageId, senderName, text, excerpt, deleted, attachmentKind, attachment
    }
}

/// Reaktion (Emoji) mit Anzahl; `mine`, wenn die eigene darunter ist.
public struct ChatReaction: Decodable, Sendable, Equatable, Hashable, Identifiable {
    public let emoji: String
    public let count: Int
    public let mine: Bool
    public let names: [String]

    public var id: String { emoji }

    public init(emoji: String, count: Int, mine: Bool, names: [String] = []) {
        self.emoji = emoji
        self.count = count
        self.mine = mine
        self.names = names
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        emoji = try c.decode(String.self, forKey: .emoji)
        names = (try? c.decodeIfPresent([String].self, forKey: .names)) ?? (try? c.decodeIfPresent([String].self, forKey: .userNames)) ?? []
        count = (try? c.decodeIfPresent(Int.self, forKey: .count)) ?? max(names.count, 1)
        mine = (try? c.decodeIfPresent(Bool.self, forKey: .mine)) ?? false
    }

    private enum CodingKeys: String, CodingKey {
        case emoji, count, mine, names, userNames
    }

    /// Reaktionen, wie sie WhatsApp anbietet – ruhig und eindeutig.
    public static let quick = ["👍", "❤️", "😂", "😮", "🙏", "✅"]
}

/// Nachricht. `state`: `sent` (beim Server), `delivered` (bei allen angekommen), `read` (von allen gelesen).
public struct ChatMessage: Decodable, Sendable, Equatable, Identifiable {
    public enum State: String, Sendable, Equatable {
        /// `queued`: liegt in der Warteschlange (offline), `sending`: wird gerade übertragen.
        case queued, sending, sent, delivered, read, failed
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
    public let attachments: [ChatAttachment]
    public let replyTo: ChatReplyPreview?
    public let reactions: [ChatReaction]
    /// Zuletzt bearbeitet („bearbeitet“).
    public let editedAt: Date?
    public let pinned: Bool
    /// Erwähnte Personen (`@Name`).
    public let mentions: [UUID]
    public let location: ChatLocation?
    /// Weitergeleitet.
    public let forwarded: Bool
    /// Hinweis des Systems („Lukas wurde hinzugefügt“), keine Sprechblase.
    public let isSystem: Bool

    public init(id: UUID, conversationId: UUID?, senderId: UUID?, senderName: String?, text: String, createdAt: Date, state: State,
                mine: Bool? = nil, deleted: Bool = false, readBy: Int = 0, recipients: Int = 0, clientMessageId: UUID? = nil,
                attachments: [ChatAttachment] = [], replyTo: ChatReplyPreview? = nil, reactions: [ChatReaction] = [], editedAt: Date? = nil,
                pinned: Bool = false, mentions: [UUID] = [], location: ChatLocation? = nil, forwarded: Bool = false, isSystem: Bool = false) {
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
        self.attachments = attachments
        self.replyTo = replyTo
        self.reactions = reactions
        self.editedAt = editedAt
        self.pinned = pinned
        self.mentions = mentions
        self.location = location
        self.forwarded = forwarded
        self.isSystem = isSystem
    }

    /// Eigene Nachricht: laut Server bzw. über den Absender.
    public func isMine(userId: UUID?) -> Bool {
        mine ?? (senderId != nil && senderId == userId)
    }

    /// Eigene Nachricht im Zeitfenster und nicht gelöscht: bearbeitbar.
    public func canEdit(userId: UUID?, windowMinutes: Int, now: Date = Date()) -> Bool {
        isMine(userId: userId) && !deleted && !isSystem && location == nil && state != .queued && state != .failed
            && now.timeIntervalSince(createdAt) <= Double(windowMinutes) * 60
    }

    /// Kurzer Text für Listen, Zitate und Pushnachrichten.
    public var summary: String {
        if deleted { return "Nachricht gelöscht" }
        if !text.isEmpty { return text }
        if let location { return "📍 " + (location.label ?? location.address ?? "Standort") }
        if let first = attachments.first {
            if attachments.count > 1 { return first.isPhoto ? "📷 \(attachments.count) Fotos" : "📄 \(attachments.count) Dateien" }
            return first.isPhoto ? "📷 Foto" : "📄 " + first.name
        }
        return ""
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
        attachments = (try? c.decodeIfPresent([ChatAttachment].self, forKey: .attachments)) ?? []
        replyTo = (try? c.decodeIfPresent(ChatReplyPreview.self, forKey: .replyTo)) ?? (try? c.decodeIfPresent(ChatReplyPreview.self, forKey: .quote))
        reactions = (try? c.decodeIfPresent([ChatReaction].self, forKey: .reactions)) ?? []
        editedAt = try? c.decodeIfPresent(Date.self, forKey: .editedAt)
        let pinnedAt = try? c.decodeIfPresent(Date.self, forKey: .pinnedAt)
        pinned = (try? c.decodeIfPresent(Bool.self, forKey: .pinned)) ?? (pinnedAt != nil)
        if let ids = try? c.decodeIfPresent([UUID].self, forKey: .mentions) {
            mentions = ids
        } else if let people = try? c.decodeIfPresent([ChatMember].self, forKey: .mentions) {
            mentions = people.map(\.id)
        } else {
            mentions = (try? c.decodeIfPresent([UUID].self, forKey: .mentionedUserIds)) ?? (try? c.decodeIfPresent([UUID].self, forKey: .mentionUserIds)) ?? []
        }
        location = try? c.decodeIfPresent(ChatLocation.self, forKey: .location)
        let forwardedFrom = try? c.decodeIfPresent(UUID.self, forKey: .forwardedFromId)
        forwarded = (try? c.decodeIfPresent(Bool.self, forKey: .forwarded)) ?? (forwardedFrom != nil)
        let kind = (try? c.decodeIfPresent(String.self, forKey: .kind)) ?? (try? c.decodeIfPresent(String.self, forKey: .type))
        isSystem = kind == "system"
    }

    private enum CodingKeys: String, CodingKey {
        case id, conversationId, senderId, senderUserId, senderName, text, body, createdAt, sentAt, state, status, readAt, deliveredAt, readByAll, deliveredToAll
        case mine, deleted, readBy, recipients, clientMessageId
        case attachments, replyTo, quote, reactions, editedAt, pinned, pinnedAt, mentions, mentionUserIds, mentionedUserIds, location, forwarded, forwardedFromId, kind, type
    }
}

/// Art einer Unterhaltung.
public enum ChatKind: String, Sendable, Equatable, CaseIterable {
    /// Einzelchat.
    case direct
    /// Freie Gruppe.
    case group
    /// Kolonnengruppe (Mitglieder folgen der Kolonne).
    case crew
    /// Baustellengruppe (je Bauakte: Team und Bauleitung).
    case site
    /// Ankündigungen: nur Admins schreiben.
    case announcement

    public init(raw: String?) {
        switch raw?.lowercased() {
        case "direct": self = .direct
        case "crew": self = .crew
        case "site", "bauakte", "construction": self = .site
        case "announcement", "announcements", "broadcast": self = .announcement
        default: self = .group
        }
    }

    public var title: String {
        switch self {
        case .direct: "Einzelchat"
        case .group: "Gruppe"
        case .crew: "Kolonnengruppe"
        case .site: "Baustellengruppe"
        case .announcement: "Ankündigungen"
        }
    }

    public var symbol: String {
        switch self {
        case .direct: "person.fill"
        case .group: "person.3.fill"
        case .crew: "truck.box.fill"
        case .site: "building.2.fill"
        case .announcement: "megaphone.fill"
        }
    }
}

/// Unterhaltung in der Liste: Titel, letzte Nachricht, ungelesene Nachrichten.
public struct ChatConversation: Decodable, Sendable, Equatable, Identifiable, Hashable {
    public let id: UUID
    public let title: String
    /// `direct`, `group`, `crew`, `site`, `announcement`.
    public let kind: String
    public let members: [ChatMember]
    public let lastMessageText: String?
    public let lastMessageSender: String?
    public let lastMessageAt: Date?
    public var unreadCount: Int
    /// Stumm bis (`distantFuture` = immer); `nil` = nicht stumm.
    public var mutedUntil: Date?
    /// Ungelesene Erwähnung der eigenen Person (durchbricht die Stummschaltung).
    public let mentioned: Bool
    /// Stand des Gruppenbilds (`GET /chat/conversations/{id}/image`); `nil` = kein Bild.
    public let imageUpdatedAt: Date?
    /// Verknüpfte Bauakte (Baustellengruppe).
    public let bauakteId: UUID?
    /// Eigene Person darf schreiben (Ankündigungsgruppen: nur Admins).
    public let canWrite: Bool
    /// Angeheftet in der Liste (oben).
    public let pinned: Bool

    public var chatKind: ChatKind { ChatKind(raw: kind) }
    public var isGroup: Bool { chatKind != .direct }

    public func isMuted(at now: Date = Date()) -> Bool {
        guard let mutedUntil else { return false }
        return mutedUntil > now
    }

    public init(id: UUID, title: String, kind: String, members: [ChatMember] = [], lastMessageText: String?, lastMessageSender: String?,
                lastMessageAt: Date?, unreadCount: Int, mutedUntil: Date? = nil, mentioned: Bool = false, imageUpdatedAt: Date? = nil,
                bauakteId: UUID? = nil, canWrite: Bool = true, pinned: Bool = false) {
        self.id = id
        self.title = title
        self.kind = kind
        self.members = members
        self.lastMessageText = lastMessageText
        self.lastMessageSender = lastMessageSender
        self.lastMessageAt = lastMessageAt
        self.unreadCount = unreadCount
        self.mutedUntil = mutedUntil
        self.mentioned = mentioned
        self.imageUpdatedAt = imageUpdatedAt
        self.bauakteId = bauakteId
        self.canWrite = canWrite
        self.pinned = pinned
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? c.decodeIfPresent(String.self, forKey: .name) ?? "Unterhaltung"
        // Server: `direct` oder `group`; die Art ergibt sich aus Kolonne, Bauakte bzw. „nur Admins schreiben“.
        let rawKind = (try c.decodeIfPresent(String.self, forKey: .kind) ?? c.decodeIfPresent(String.self, forKey: .type) ?? "group").lowercased()
        let crew = try? c.decodeIfPresent(UUID.self, forKey: .crewId)
        let site = try? c.decodeIfPresent(UUID.self, forKey: .bauakteId)
        let announcementOnly = (try? c.decodeIfPresent(Bool.self, forKey: .announcementOnly)) ?? false
        if rawKind == "direct" {
            kind = "direct"
        } else if announcementOnly {
            kind = ChatKind.announcement.rawValue
        } else if site != nil {
            kind = ChatKind.site.rawValue
        } else if crew != nil {
            kind = ChatKind.crew.rawValue
        } else {
            kind = rawKind
        }
        members = (try? c.decodeIfPresent([ChatMember].self, forKey: .members)) ?? []
        let last = try? c.decodeIfPresent(ChatMessage.self, forKey: .lastMessage)
        if let last {
            lastMessageText = last.summary
            lastMessageSender = last.isSystem ? nil : (last.mine == true ? "Ich" : last.senderName)
        } else {
            lastMessageText = (try? c.decodeIfPresent(String.self, forKey: .lastMessageText)) ?? nil
            lastMessageSender = nil
        }
        lastMessageAt = (try? c.decodeIfPresent(Date.self, forKey: .lastActivityAt)) ?? last?.createdAt
            ?? (try? c.decodeIfPresent(Date.self, forKey: .lastMessageAt))
        unreadCount = try c.decodeIfPresent(Int.self, forKey: .unreadCount) ?? 0
        if let until = try? c.decodeIfPresent(Date.self, forKey: .mutedUntil) {
            mutedUntil = until
        } else if (try? c.decodeIfPresent(Bool.self, forKey: .muted)) == true {
            mutedUntil = .distantFuture
        } else {
            mutedUntil = nil
        }
        let mentionCount = (try? c.decodeIfPresent(Int.self, forKey: .unreadMentions)) ?? 0
        mentioned = (try? c.decodeIfPresent(Bool.self, forKey: .mentioned)) ?? (mentionCount > 0)
        imageUpdatedAt = try? c.decodeIfPresent(Date.self, forKey: .imageUpdatedAt)
        bauakteId = site
        canWrite = (try? c.decodeIfPresent(Bool.self, forKey: .canPost)) ?? (try? c.decodeIfPresent(Bool.self, forKey: .canWrite)) ?? true
        pinned = (try? c.decodeIfPresent(Bool.self, forKey: .pinned)) ?? false
    }

    private enum CodingKeys: String, CodingKey {
        case id, title, name, kind, type, members, lastMessage, lastMessageText, lastMessageAt, lastActivityAt, unreadCount
        case muted, mutedUntil, mentioned, unreadMentions, imageUpdatedAt, bauakteId, crewId, announcementOnly, canWrite, canPost, pinned
    }

    public static func == (lhs: ChatConversation, rhs: ChatConversation) -> Bool {
        lhs.id == rhs.id && lhs.unreadCount == rhs.unreadCount && lhs.lastMessageAt == rhs.lastMessageAt && lhs.title == rhs.title
            && lhs.mutedUntil == rhs.mutedUntil && lhs.lastMessageText == rhs.lastMessageText && lhs.imageUpdatedAt == rhs.imageUpdatedAt
    }

    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
}

/// Kopf einer Unterhaltung für die Info-Seite: Beschreibung, Bild, Mitglieder mit Admins, Rechte.
public struct ChatConversationDetail: Decodable, Sendable, Equatable {
    public let id: UUID
    public let title: String
    public let kind: String
    public let description: String?
    public let imageUpdatedAt: Date?
    public let members: [ChatMember]
    /// Eigene Person darf die Gruppe verwalten (Gruppenadmin oder `chat.admin`).
    public let canManage: Bool
    public let canWrite: Bool
    /// Nur Admins schreiben (Ankündigungsgruppe, Server: `announcementOnly`).
    public let onlyAdminsWrite: Bool
    public let bauakteId: UUID?
    public let bauakteTitle: String?
    public let crewId: UUID?
    public let crewName: String?
    public var mutedUntil: Date?
    /// Lesebestätigungen je Mitglied sichtbar.
    public let readReceipts: Bool
    /// Angeheftete Nachrichten.
    public let pinned: [ChatMessage]

    public var chatKind: ChatKind { ChatKind(raw: kind) }

    public init(id: UUID, title: String, kind: String, description: String? = nil, imageUpdatedAt: Date? = nil, members: [ChatMember] = [],
                canManage: Bool = false, canWrite: Bool = true, onlyAdminsWrite: Bool = false, bauakteId: UUID? = nil, bauakteTitle: String? = nil,
                crewId: UUID? = nil, crewName: String? = nil, mutedUntil: Date? = nil, readReceipts: Bool = true, pinned: [ChatMessage] = []) {
        self.id = id
        self.title = title
        self.kind = kind
        self.description = description
        self.imageUpdatedAt = imageUpdatedAt
        self.members = members
        self.canManage = canManage
        self.canWrite = canWrite
        self.onlyAdminsWrite = onlyAdminsWrite
        self.bauakteId = bauakteId
        self.bauakteTitle = bauakteTitle
        self.crewId = crewId
        self.crewName = crewName
        self.mutedUntil = mutedUntil
        self.readReceipts = readReceipts
        self.pinned = pinned
    }

    /// Server: `{ conversation: {…}, members, canManage, pinned, bauakteTitle }`; ältere Form flach.
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let base: ChatConversation
        let head: KeyedDecodingContainer<CodingKeys>
        if c.contains(.conversation) {
            base = try c.decode(ChatConversation.self, forKey: .conversation)
            head = try c.nestedContainer(keyedBy: CodingKeys.self, forKey: .conversation)
        } else {
            base = try ChatConversation(from: decoder)
            head = c
        }
        id = base.id
        title = base.title
        kind = base.kind
        mutedUntil = base.mutedUntil
        imageUpdatedAt = base.imageUpdatedAt
        bauakteId = base.bauakteId
        canWrite = base.canWrite
        description = try? head.decodeIfPresent(String.self, forKey: .description)
        members = (try? c.decodeIfPresent([ChatMember].self, forKey: .members)) ?? base.members
        canManage = (try? c.decodeIfPresent(Bool.self, forKey: .canManage)) ?? false
        onlyAdminsWrite = (try? head.decodeIfPresent(Bool.self, forKey: .announcementOnly)) ?? (base.chatKind == .announcement)
        bauakteTitle = try? c.decodeIfPresent(String.self, forKey: .bauakteTitle)
        crewId = try? head.decodeIfPresent(UUID.self, forKey: .crewId)
        crewName = try? head.decodeIfPresent(String.self, forKey: .crewName)
        readReceipts = (try? c.decodeIfPresent(Bool.self, forKey: .readReceipts)) ?? true
        pinned = (try? c.decodeIfPresent([ChatMessage].self, forKey: .pinned)) ?? []
    }

    private enum CodingKeys: String, CodingKey {
        case conversation, description, members, canManage, announcementOnly, bauakteTitle, crewId, crewName, readReceipts, pinned
    }

    public func isAdmin(_ userId: UUID?) -> Bool { members.contains { $0.id == userId && $0.isAdmin } }
}

/// Seite des Verlaufs: aufsteigend sortiert, `hasMore` für ältere Nachrichten.
public struct ChatPage: Decodable, Sendable {
    public let items: [ChatMessage]
    public let hasMore: Bool
    public let members: [ChatMember]

    public init(items: [ChatMessage], hasMore: Bool, members: [ChatMember] = []) {
        self.items = items
        self.hasMore = hasMore
        self.members = members
    }

    public init(from decoder: any Decoder) throws {
        if let list = try? decoder.singleValueContainer().decode([ChatMessage].self) {
            items = list.sorted { $0.createdAt < $1.createdAt }
            hasMore = false
            members = []
            return
        }
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let raw = try c.decodeIfPresent([ChatMessage].self, forKey: .items) ?? c.decodeIfPresent([ChatMessage].self, forKey: .messages) ?? []
        items = raw.sorted { $0.createdAt < $1.createdAt }
        hasMore = (try? c.decodeIfPresent(Bool.self, forKey: .hasMore)) ?? false
        members = (try? c.decodeIfPresent([ChatMember].self, forKey: .members)) ?? []
    }

    private enum CodingKeys: String, CodingKey {
        case items, messages, hasMore, members
    }
}

/// Treffer der Suche über alle eigenen Unterhaltungen.
public struct ChatSearchHit: Decodable, Sendable, Identifiable, Equatable, Hashable {
    public let message: ChatMessage
    public let conversationId: UUID
    public let conversationTitle: String

    public var id: UUID { message.id }

    public init(message: ChatMessage, conversationId: UUID, conversationTitle: String) {
        self.message = message
        self.conversationId = conversationId
        self.conversationTitle = conversationTitle
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        if let nested = try? c.decodeIfPresent(ChatMessage.self, forKey: .message) {
            message = nested
        } else if let messageId = try? c.decode(UUID.self, forKey: .messageId) {
            // Server: `{ messageId, conversationId, conversationTitle, senderName, snippet, createdAt }`.
            message = ChatMessage(id: messageId, conversationId: try? c.decode(UUID.self, forKey: .conversationId),
                                  senderId: nil, senderName: try? c.decodeIfPresent(String.self, forKey: .senderName),
                                  text: (try? c.decodeIfPresent(String.self, forKey: .snippet)) ?? "",
                                  createdAt: (try? c.decodeIfPresent(Date.self, forKey: .createdAt)) ?? Date(), state: .sent)
        } else {
            message = try ChatMessage(from: decoder)
        }
        conversationId = try c.decodeIfPresent(UUID.self, forKey: .conversationId) ?? message.conversationId ?? UUID()
        conversationTitle = (try? c.decodeIfPresent(String.self, forKey: .conversationTitle)) ?? "Unterhaltung"
    }

    public func hash(into hasher: inout Hasher) { hasher.combine(id) }

    private enum CodingKeys: String, CodingKey {
        case message, conversationId, conversationTitle, messageId, senderName, snippet, createdAt
    }
}

/// Eintrag der Medien-/Dokumente-Übersicht einer Unterhaltung.
public struct ChatMediaItem: Decodable, Sendable, Identifiable, Equatable {
    public let attachment: ChatAttachment
    public let messageId: UUID
    public let senderName: String?
    public let createdAt: Date

    public var id: UUID { attachment.id }

    public init(attachment: ChatAttachment, messageId: UUID, senderName: String?, createdAt: Date) {
        self.attachment = attachment
        self.messageId = messageId
        self.senderName = senderName
        self.createdAt = createdAt
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        if let nested = try? c.decode(ChatAttachment.self, forKey: .attachment) {
            attachment = nested
        } else {
            attachment = try ChatAttachment(from: decoder)
        }
        messageId = try c.decode(UUID.self, forKey: .messageId)
        senderName = try? c.decodeIfPresent(String.self, forKey: .senderName)
        createdAt = (try? c.decodeIfPresent(Date.self, forKey: .createdAt)) ?? Date()
    }

    private enum CodingKeys: String, CodingKey {
        case attachment, messageId, senderName, createdAt
    }

    /// Medien aus einem geladenen Verlauf (wenn der Server keine Übersicht anbietet).
    public static func collect(from messages: [ChatMessage]) -> [ChatMediaItem] {
        messages.filter { !$0.deleted }.flatMap { message in
            message.attachments.map { ChatMediaItem(attachment: $0, messageId: message.id, senderName: message.senderName, createdAt: message.createdAt) }
        }
        .sorted { $0.createdAt > $1.createdAt }
    }
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
    /// Höchstens so viele Anhänge je Nachricht.
    public static let maxAttachments = 10
    /// Dokumente bis 25 MB.
    public static let maxDocumentBytes = 25 * 1024 * 1024

    /// Text zum Senden: getrimmt, nicht leer, höchstens `maxLength` Zeichen.
    public static func clean(_ text: String) -> String? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }
        return String(trimmed.prefix(maxLength))
    }
}

/// Stummschalten wie im Messenger: 1 Stunde, 8 Stunden, 1 Woche, immer.
public enum ChatMuteOption: String, CaseIterable, Sendable, Identifiable {
    case hour, eightHours, week, always

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .hour: "1 Stunde"
        case .eightHours: "8 Stunden"
        case .week: "1 Woche"
        case .always: "Immer"
        }
    }

    /// Ende der Stummschaltung (`nil` = immer).
    public func until(from now: Date = Date()) -> Date? {
        switch self {
        case .hour: now.addingTimeInterval(3600)
        case .eightHours: now.addingTimeInterval(8 * 3600)
        case .week: now.addingTimeInterval(7 * 86400)
        case .always: nil
        }
    }
}

/// Neue Nachricht (Text, Anhänge, Zitat, Erwähnungen, Standort).
public struct ChatSendBody: Codable, Sendable, Equatable {
    public var text: String
    public var clientMessageId: String
    public var attachmentFileIds: [String]?
    public var replyToId: String?
    public var mentionUserIds: [String]?
    public var location: ChatLocation?

    public init(text: String, clientMessageId: UUID, attachmentFileIds: [UUID] = [], replyToId: UUID? = nil, mentionUserIds: [UUID] = [],
                location: ChatLocation? = nil) {
        self.text = text
        self.clientMessageId = clientMessageId.key
        self.attachmentFileIds = attachmentFileIds.isEmpty ? nil : attachmentFileIds.map(\.key)
        self.replyToId = replyToId?.key
        self.mentionUserIds = mentionUserIds.isEmpty ? nil : mentionUserIds.map(\.key)
        self.location = location
    }
}

private struct ChatEmptyBody: Encodable, Sendable {}

private struct ChatDirectBody: Encodable, Sendable {
    let userId: String
}

private struct ChatTextBody: Encodable, Sendable {
    let text: String
}

private struct ChatReactionBody: Encodable, Sendable {
    let emoji: String?
}

private struct ChatPinBody: Encodable, Sendable {
    let pinned: Bool
}

private struct ChatForwardBody: Encodable, Sendable {
    let conversationIds: [String]
}

private struct ChatMuteBody: Encodable, Sendable {
    let muted: Bool
    let until: Date?
}

private struct ChatToBauakteBody: Encodable, Sendable {
    let bauakteId: String?
    let caption: String?
}

/// Neue Gruppe bzw. Änderung einer Gruppe (`nil` = unverändert).
public struct ChatGroupChange: Encodable, Sendable, Equatable {
    public var title: String?
    public var description: String?
    public var memberIds: [String]?
    public var adminIds: [String]?
    /// Nur Admins schreiben (Ankündigungsgruppe).
    public var announcementOnly: Bool?
    public var crewId: String?

    public init(title: String? = nil, description: String? = nil, memberIds: [UUID]? = nil, adminIds: [UUID]? = nil,
                announcementOnly: Bool? = nil, crewId: UUID? = nil) {
        self.title = title
        self.description = description
        self.memberIds = memberIds?.map(\.key)
        self.adminIds = adminIds?.map(\.key)
        self.announcementOnly = announcementOnly
        self.crewId = crewId?.key
    }
}

/// Kolonne für Kolonnengruppen.
public struct ChatCrew: Decodable, Sendable, Identifiable, Equatable, Hashable {
    public let id: UUID
    public let name: String

    public init(id: UUID, name: String) {
        self.id = id
        self.name = name
    }
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

    public func chatConversation(_ id: UUID) async throws(APIError) -> ChatConversationDetail {
        try await get("chat/conversations/\(id.key)")
    }

    /// Verlauf, älteste zuerst. Mit `after` nur neue/geänderte Nachrichten seit dem letzten Abruf (Polling), mit
    /// `before` ältere (Nachladen beim Hochscrollen).
    public func chatPage(_ conversationId: UUID, limit: Int = 50, before: Date? = nil, after: Date? = nil) async throws(APIError) -> ChatPage {
        var query = [URLQueryItem(name: "limit", value: String(limit))]
        if let before { query.append(URLQueryItem(name: "before", value: APIDate.format(before))) }
        if let after { query.append(URLQueryItem(name: "after", value: APIDate.format(after))) }
        return try await get("chat/conversations/\(conversationId.key)/messages", query: query)
    }

    /// Verlauf ohne Seiteninformation (ältere Aufrufer).
    public func chatMessages(_ conversationId: UUID, limit: Int = 50, after: Date? = nil) async throws(APIError) -> [ChatMessage] {
        try await chatPage(conversationId, limit: limit, after: after).items
    }

    /// Personen, mit denen ein Einzelchat möglich ist.
    public func chatContacts() async throws(APIError) -> [ChatMember] {
        let list: ChatList<ChatMember> = try await get("chat/contacts")
        return list.items
    }

    /// Einzelchat öffnen (vorhandenen oder neuen).
    public func startDirectChat(with userId: UUID) async throws(APIError) -> ChatConversation {
        try await post("chat/conversations/direct", body: ChatDirectBody(userId: userId.key))
    }

    /// Sendet eine Textnachricht. `clientMessageId` macht doppeltes Senden (Wiederholung nach Zeitüberschreitung) unschädlich.
    public func sendChatMessage(_ text: String, to conversationId: UUID, clientMessageId: UUID) async throws(APIError) -> ChatMessage {
        try await sendChatMessage(ChatSendBody(text: text, clientMessageId: clientMessageId), to: conversationId)
    }

    public func sendChatMessage(_ body: ChatSendBody, to conversationId: UUID) async throws(APIError) -> ChatMessage {
        try await post("chat/conversations/\(conversationId.key)/messages", body: body)
    }

    public func markChatRead(_ conversationId: UUID) async throws(APIError) {
        let _: NoContent = try await post("chat/conversations/\(conversationId.key)/read", body: ChatEmptyBody())
    }

    /// Eigene Nachricht im Zeitfenster bearbeiten („bearbeitet“).
    public func editChatMessage(_ id: UUID, text: String) async throws(APIError) -> ChatMessage {
        try await send(APIRequest(method: "PUT", path: "chat/messages/\(id.key)", body: Self.json(ChatTextBody(text: text)), contentType: "application/json"))
    }

    /// Für alle löschen (eigene; `chat.admin` bzw. Gruppenadmin: jede).
    public func deleteChatMessage(_ id: UUID) async throws(APIError) {
        let _: NoContent = try await send(APIRequest(method: "DELETE", path: "chat/messages/\(id.key)"))
    }

    /// Eigene Reaktion setzen; `nil` nimmt sie zurück. Eine Reaktion je Person und Nachricht.
    public func reactToChatMessage(_ id: UUID, emoji: String?) async throws(APIError) -> ChatMessage {
        try await send(APIRequest(method: "PUT", path: "chat/messages/\(id.key)/reaction", body: Self.json(ChatReactionBody(emoji: emoji)),
                                  contentType: "application/json"))
    }

    /// Nachricht anheften bzw. lösen (oben in der Unterhaltung).
    public func pinChatMessage(_ id: UUID, pinned: Bool) async throws(APIError) -> ChatMessage {
        let _: NoContent = try await post("chat/messages/\(id.key)/pin", body: ChatPinBody(pinned: pinned))
        return try await chatMessage(id)
    }

    /// Eine Nachricht (nach den Ereignissen `message.updated`, `reaction`, `message.deleted`).
    public func chatMessage(_ id: UUID) async throws(APIError) -> ChatMessage {
        try await get("chat/messages/\(id.key)")
    }

    /// In andere Unterhaltungen weiterleiten (Text, Anhänge und Standort, gekennzeichnet als „weitergeleitet“).
    public func forwardChatMessage(_ id: UUID, to conversationIds: [UUID]) async throws(APIError) {
        let _: NoContent = try await post("chat/messages/\(id.key)/forward", body: ChatForwardBody(conversationIds: conversationIds.map(\.key)))
    }

    /// Stummschalten (`until` `nil` = immer) bzw. wieder einschalten.
    public func muteChat(_ conversationId: UUID, muted: Bool, until: Date? = nil) async throws(APIError) {
        let _: NoContent = try await post("chat/conversations/\(conversationId.key)/mute", body: ChatMuteBody(muted: muted, until: muted ? until : nil))
    }

    /// Suche in allen eigenen Unterhaltungen bzw. in einer.
    public func searchChat(_ term: String, conversationId: UUID? = nil) async throws(APIError) -> [ChatSearchHit] {
        var query = [URLQueryItem(name: "q", value: term)]
        if let conversationId { query.append(URLQueryItem(name: "conversationId", value: conversationId.key)) }
        let list: ChatList<ChatSearchHit> = try await get("chat/search", query: query)
        return list.items
    }

    /// Fotos bzw. Dokumente einer Unterhaltung (`kind` `photo`/`document`, `nil` = alle).
    public func chatMedia(_ conversationId: UUID, kind: String? = nil) async throws(APIError) -> [ChatMediaItem] {
        let query = kind.map { [URLQueryItem(name: "kind", value: $0)] } ?? []
        let list: ChatList<ChatMediaItem> = try await get("chat/conversations/\(conversationId.key)/attachments", query: query)
        return list.items
    }

    /// Inhalt eines Anhangs (`thumb`: Vorschaubild).
    public func chatAttachmentContent(_ id: UUID, thumbnail: Bool = false) async throws(APIError) -> Data {
        try await get("chat/attachments/\(id.key)/content", query: thumbnail ? [URLQueryItem(name: "variant", value: "thumb")] : [])
    }

    /// Anhang hochladen (Foto/PDF roh): Der Server prüft den Typ, verdichtet, speichert verschlüsselt und vergibt die ID,
    /// die beim Senden in `attachmentFileIds` steht. Nicht gesendete Uploads verfallen nach 24 Stunden.
    public func uploadChatAttachment(conversationId: UUID, name: String, contentType: String, data: Data) async throws(APIError) -> ChatAttachment {
        try await send(APIRequest(method: "POST", path: "chat/conversations/\(conversationId.key)/attachments",
                                  query: [URLQueryItem(name: "fileName", value: name)], body: data, contentType: contentType))
    }

    /// Gruppenbild setzen (Bild roh; der Server verkleinert auf 256 px).
    public func setChatGroupImage(_ conversationId: UUID, jpeg: Data) async throws(APIError) -> ChatConversation {
        try await send(APIRequest(method: "PUT", path: "chat/conversations/\(conversationId.key)/image", body: jpeg, contentType: "image/jpeg"))
    }

    public func removeChatGroupImage(_ conversationId: UUID) async throws(APIError) {
        let _: NoContent = try await send(APIRequest(method: "DELETE", path: "chat/conversations/\(conversationId.key)/image"))
    }

    public func chatGroupImage(_ conversationId: UUID) async throws(APIError) -> Data {
        try await get("chat/conversations/\(conversationId.key)/image")
    }

    /// Baustellen-Gruppe der Bauakte anlegen bzw. die vorhandene öffnen (Team und Bauleitung automatisch).
    public func openBauakteChatGroup(_ bauakteId: UUID) async throws(APIError) -> ChatConversation {
        try await post("chat/bauakten/\(bauakteId.key)/group", body: ChatEmptyBody())
    }

    /// „In Bauakte übernehmen“ auf dem Server: Foto (mit Vorschauen) bzw. Dokument der Bauakte; ohne `bauakteId` die
    /// Bauakte der Baustellen-Gruppe.
    public func copyChatAttachmentToBauakte(_ attachmentId: UUID, bauakteId: UUID?, caption: String?) async throws(APIError) {
        let _: NoContent = try await post("chat/attachments/\(attachmentId.key)/bauakte",
                                          body: ChatToBauakteBody(bauakteId: bauakteId?.key, caption: caption))
    }

    /// Neue Gruppe (Gruppe, Kolonne, Baustelle, Ankündigungen).
    public func createChatGroup(_ change: ChatGroupChange) async throws(APIError) -> ChatConversation {
        try await post("chat/conversations", body: change)
    }

    /// Gruppe ändern: Name, Beschreibung, Bild, Mitglieder, Admins, „nur Admins schreiben“.
    public func updateChatGroup(_ id: UUID, _ change: ChatGroupChange) async throws(APIError) -> ChatConversationDetail {
        try await send(APIRequest(method: "PUT", path: "chat/conversations/\(id.key)", body: Self.json(change), contentType: "application/json"))
    }

    /// Gruppe verlassen.
    public func leaveChat(_ id: UUID) async throws(APIError) {
        let _: NoContent = try await post("chat/conversations/\(id.key)/leave", body: ChatEmptyBody())
    }

    /// Kolonnen für Kolonnengruppen.
    public func chatCrews() async throws(APIError) -> [ChatCrew] {
        let list: ChatList<ChatCrew> = try await get("chat/crews")
        return list.items
    }

    /// „… schreibt“ melden (höchstens alle paar Sekunden).
    public func sendChatTyping(_ conversationId: UUID) async throws(APIError) {
        let _: NoContent = try await post("chat/conversations/\(conversationId.key)/typing", body: ChatEmptyBody())
    }

    /// Bestätigt den Datenschutzhinweis (einmal je Person und Stand).
    public func acknowledgeChatPrivacy(version: Int) async throws(APIError) {
        let _: NoContent = try await post("chat/privacy-notice", body: ["version": version])
    }

    private static func json<Body: Encodable>(_ body: Body) throws(APIError) -> Data {
        do { return try JSONCoding.encoder.encode(body) } catch { throw .invalidResponse }
    }
}
