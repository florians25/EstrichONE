import Foundation

/// Benachrichtigung im Postfach des Benutzers (`GET /notifications`, docs/benachrichtigungen.md): Ticket-Antwort,
/// Genehmigung, Plan freigegeben/geändert, Zuweisung, Frist oder eine Nachricht aus dem Büro.
public struct InboxNotification: Decodable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public let category: String?
    /// `low`, `normal` oder `high`.
    public let urgency: String?
    public let title: String
    public let body: String?
    /// Ziel in der Anwendung, z. B. `/bauakten/{id}` oder `/helpdesk/{id}`.
    public let link: String?
    public let objectType: String?
    public let objectId: UUID?
    public let createdAt: Date
    public var readAt: Date?

    public var isRead: Bool { readAt != nil }
    public var isUrgent: Bool { urgency == "high" }
    /// Ziel in der App (Bauakte, Meldung, Planung), sonst `nil`.
    public var route: NotificationRoute? { NotificationRoute(link: link, objectType: objectType, objectId: objectId) }

    public init(id: UUID, category: String?, urgency: String?, title: String, body: String?, link: String?, objectType: String? = nil,
                objectId: UUID? = nil, createdAt: Date, readAt: Date?) {
        self.id = id
        self.category = category
        self.urgency = urgency
        self.title = title
        self.body = body
        self.link = link
        self.objectType = objectType
        self.objectId = objectId
        self.createdAt = createdAt
        self.readAt = readAt
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        category = try c.decodeIfPresent(String.self, forKey: .category)
        urgency = try c.decodeIfPresent(String.self, forKey: .urgency)
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? "Benachrichtigung"
        body = try c.decodeIfPresent(String.self, forKey: .body)
        link = try c.decodeIfPresent(String.self, forKey: .link)
        objectType = try c.decodeIfPresent(String.self, forKey: .objectType)
        objectId = try? c.decodeIfPresent(UUID.self, forKey: .objectId)
        createdAt = try c.decodeIfPresent(Date.self, forKey: .createdAt) ?? Date()
        if let date = try c.decodeIfPresent(Date.self, forKey: .readAt) {
            readAt = date
        } else if try c.decodeIfPresent(Bool.self, forKey: .isRead) == true {
            readAt = createdAt
        } else {
            readAt = nil
        }
    }

    private enum CodingKeys: String, CodingKey {
        case id, category, urgency, title, body, link, objectType, objectId, createdAt, readAt, isRead
    }
}

/// Seite des Postfachs. Der Server liefert `{ items, unreadCount }`; eine reine Liste wird ebenfalls verstanden.
public struct NotificationInbox: Decodable, Equatable, Sendable {
    public var items: [InboxNotification]
    public var unreadCount: Int

    public init(items: [InboxNotification], unreadCount: Int? = nil) {
        self.items = items
        self.unreadCount = unreadCount ?? items.filter { !$0.isRead }.count
    }

    public init(from decoder: any Decoder) throws {
        if let list = try? decoder.singleValueContainer().decode([InboxNotification].self) {
            self.init(items: list)
            return
        }
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let list = try c.decodeIfPresent([InboxNotification].self, forKey: .items) ?? []
        self.init(items: list, unreadCount: try c.decodeIfPresent(Int.self, forKey: .unreadCount))
    }

    private enum CodingKeys: String, CodingKey {
        case items, unreadCount
    }
}

/// Ziel einer Benachrichtigung in der App – aus dem Link (`/bauakten/{id}`, `estrichone://bauakte/{id}`, …) oder aus
/// Objekttyp und -ID. Unbekannte Links öffnen das Postfach.
public enum NotificationRoute: Equatable, Sendable, Hashable, Identifiable {
    case bauakte(UUID)
    case ticket(UUID)
    /// Planung („Wer fährt wohin“) für einen Tag (`JJJJ-MM-TT`), `nil` = heute.
    case planning(day: String?)
    case tasks
    /// Chat: Unterhaltung (`nil` = Liste der Unterhaltungen).
    case chat(UUID?)
    case inbox

    public var id: Self { self }

    public init?(link: String?, objectType: String? = nil, objectId: UUID? = nil) {
        if let link, let route = Self.parse(link) {
            self = route
            return
        }
        switch (objectType?.lowercased(), objectId) {
        case let ("bauakte"?, id?): self = .bauakte(id)
        case let ("ticket"?, id?): self = .ticket(id)
        case ("assignment"?, _), ("plan"?, _), ("planday"?, _), ("planning"?, _): self = .planning(day: nil)
        case ("workitem"?, _), ("task"?, _): self = .tasks
        case let ("chatconversation"?, id), let ("chat"?, id): self = .chat(id)
        case ("approval"?, _): self = .inbox
        default:
            guard link != nil else { return nil }
            self = .inbox
        }
    }

    private static func parse(_ link: String) -> NotificationRoute? {
        guard let components = URLComponents(string: link.trimmingCharacters(in: .whitespaces)) else { return nil }
        // estrichone://bauakte/{id} → host „bauakte“ + Pfad; https://server/bauakten/{id} → nur Pfad.
        var parts = components.path.split(separator: "/").map(String.init)
        if components.scheme == "estrichone", let host = components.host { parts.insert(host, at: 0) }
        // Präfixe der Weboberfläche ohne eigene Bedeutung („/app/…“, „/it-center/helpdesk/…“).
        while let first = parts.first, ["app", "it-center"].contains(first.lowercased()) { parts.removeFirst() }
        guard let head = parts.first?.lowercased() else { return nil }
        let id = parts.dropFirst().first.flatMap { UUID(uuidString: $0) }
        let day = components.queryItems?.first { $0.name == "day" || $0.name == "tag" }?.value
        switch head {
        case "bauakte", "bauakten": return id.map { .bauakte($0) }
        case "helpdesk", "tickets", "ticket", "meldungen": return id.map { .ticket($0) }
        case "planung", "planning", "plan", "disposition": return .planning(day: day ?? parts.dropFirst().last.flatMap { $0.count == 10 ? $0 : nil })
        case "aufgaben", "tasks": return .tasks
        case "chat", "nachrichten", "messenger": return .chat(id)
        case "benachrichtigungen", "notifications", "postfach": return .inbox
        default: return nil
        }
    }
}

/// Registrierung des Geräts für Pushnachrichten (`POST /notifications/devices`).
public struct PushDeviceRegistration: Encodable, Equatable, Sendable {
    public let platform: String
    public let token: String
    /// APNs-Umgebung: Entwicklungs-Builds (Xcode, TestFlight nicht) nutzen die Sandbox.
    public let sandbox: Bool
    public let bundleId: String?
    public let deviceName: String?
    public let appVersion: String?

    public init(token: String, sandbox: Bool, bundleId: String?, deviceName: String?, appVersion: String?) {
        platform = "ios"
        self.token = token
        self.sandbox = sandbox
        self.bundleId = bundleId
        self.deviceName = deviceName
        self.appVersion = appVersion
    }

    /// Gerätetoken von APNs als Hex-Text.
    public static func hex(_ token: Data) -> String {
        token.map { String(format: "%02x", $0) }.joined()
    }
}

private struct EmptyBody: Encodable, Sendable {}

extension AuthorizedClient {
    public func notifications() async throws(APIError) -> NotificationInbox {
        try await get("notifications")
    }

    public func markNotificationRead(_ id: UUID) async throws(APIError) {
        let _: NoContent = try await post("notifications/\(id.key)/read", body: EmptyBody())
    }

    public func markAllNotificationsRead() async throws(APIError) {
        let _: NoContent = try await post("notifications/read-all", body: EmptyBody())
    }

    public func registerPushDevice(_ registration: PushDeviceRegistration) async throws(APIError) {
        let _: NoContent = try await post("notifications/devices", body: registration)
    }

    /// Meldet das Gerät ab (beim Abmelden oder wenn Pushnachrichten ausgeschaltet werden): `DELETE /notifications/devices?token=`.
    public func unregisterPushDevice(token: String) async throws(APIError) {
        let _: NoContent = try await send(APIRequest(method: "DELETE", path: "notifications/devices", query: [URLQueryItem(name: "token", value: token)]))
    }
}

/// Auswahl je Ereignis (`GET/PUT /notifications/preferences`, docs/benachrichtigungen.md): aus, nur Postfach oder
/// Postfach und Push. Pflicht-Ereignisse lassen sich nicht abbestellen, nur die Pushnachricht lässt sich abschalten.
public struct NotificationPreference: Codable, Equatable, Sendable, Identifiable {
    public enum Delivery: String, Codable, Sendable, CaseIterable {
        case off, inbox, push
    }

    public let event: String
    public let group: String
    public let label: String
    public let description: String?
    public let adminOnly: Bool
    public let mandatory: Bool
    public var delivery: Delivery

    public var id: String { event }

    public init(event: String, group: String, label: String, description: String?, adminOnly: Bool, mandatory: Bool, delivery: Delivery) {
        self.event = event
        self.group = group
        self.label = label
        self.description = description
        self.adminOnly = adminOnly
        self.mandatory = mandatory
        self.delivery = delivery
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        event = try c.decode(String.self, forKey: .event)
        group = try c.decodeIfPresent(String.self, forKey: .group) ?? "Allgemein"
        label = try c.decodeIfPresent(String.self, forKey: .label) ?? event
        description = try c.decodeIfPresent(String.self, forKey: .description)
        adminOnly = try c.decodeIfPresent(Bool.self, forKey: .adminOnly) ?? false
        mandatory = try c.decodeIfPresent(Bool.self, forKey: .mandatory) ?? false
        delivery = (try? c.decode(Delivery.self, forKey: .delivery)) ?? .push
    }

    private enum CodingKeys: String, CodingKey {
        case event, group, label, description, adminOnly, mandatory, delivery
    }
}

private struct NotificationPreferenceList: Codable, Sendable {
    let items: [NotificationPreference]
}

private struct NotificationPreferenceChange: Encodable, Sendable {
    let event: String
    let delivery: NotificationPreference.Delivery
}

private struct NotificationPreferenceChanges: Encodable, Sendable {
    let items: [NotificationPreferenceChange]
}

extension AuthorizedClient {
    public func notificationPreferences() async throws(APIError) -> [NotificationPreference] {
        let list: NotificationPreferenceList = try await get("notifications/preferences")
        return list.items
    }

    /// Speichert geänderte Einträge und liefert den neuen Stand.
    public func saveNotificationPreferences(_ items: [NotificationPreference]) async throws(APIError) -> [NotificationPreference] {
        let body = NotificationPreferenceChanges(items: items.map { NotificationPreferenceChange(event: $0.event, delivery: $0.delivery) })
        let response: NotificationPreferenceList = try await send(APIRequest(
            method: "PUT", path: "notifications/preferences", body: try? JSONCoding.encoder.encode(body), contentType: "application/json"))
        return response.items
    }
}
