import Foundation

/// Hinweise des Betreibers (`GET /api/v1/system/notices`, ohne Anmeldung): Ankündigungen und Wartung.
public struct SystemNotices: Decodable, Sendable, Equatable {
    public struct Announcement: Decodable, Sendable, Equatable, Identifiable {
        public enum Severity: String, Decodable, Sendable {
            case info
            case warning
            case critical
        }

        public let id: String
        public let title: String
        public let text: String
        public let severity: Severity
        public let startsAt: String?
        public let endsAt: String?

        public init(from decoder: any Decoder) throws {
            let container = try decoder.container(keyedBy: CodingKeys.self)
            id = try container.decode(String.self, forKey: .id)
            title = try container.decode(String.self, forKey: .title)
            text = try container.decodeIfPresent(String.self, forKey: .text) ?? ""
            // Unbekannte Stufen als Hinweis anzeigen statt die ganze Antwort zu verwerfen.
            severity = (try? container.decode(Severity.self, forKey: .severity)) ?? .info
            startsAt = try container.decodeIfPresent(String.self, forKey: .startsAt)
            endsAt = try container.decodeIfPresent(String.self, forKey: .endsAt)
        }

        private enum CodingKeys: String, CodingKey {
            case id, title, text, severity, startsAt, endsAt
        }

        /// Im Zeitfenster (fehlende Grenzen gelten als offen).
        public func isCurrent(at date: Date) -> Bool {
            if let start = startsAt.flatMap(APIDate.parse), date < start { return false }
            if let end = endsAt.flatMap(APIDate.parse), date >= end { return false }
            return true
        }
    }

    public struct Maintenance: Decodable, Sendable, Equatable {
        public let active: Bool
        public let message: String?
        public let startsAt: String
        public let endsAt: String?
        public let adminsMayWork: Bool

        public var start: Date? { APIDate.parse(startsAt) }
        public var end: Date? { endsAt.flatMap(APIDate.parse) }
    }

    public let announcements: [Announcement]
    public let maintenance: Maintenance?
    public let serverTime: String?

    /// Ankündigungen, die jetzt gelten.
    public func currentAnnouncements(at date: Date = Date()) -> [Announcement] {
        announcements.filter { $0.isCurrent(at: date) }
    }

    /// Geplante, noch nicht begonnene Wartung (Vorankündigung).
    public func upcomingMaintenance(at date: Date = Date()) -> Maintenance? {
        guard let maintenance, !maintenance.active else { return nil }
        if let end = maintenance.end, end <= date { return nil }
        return maintenance
    }
}

extension AuthorizedClient {
    /// Aktuelle Systemhinweise. Wird beim Start, im Vordergrund und mit jeder Synchronisation abgerufen.
    public func systemNotices() async throws(APIError) -> SystemNotices {
        try await get("system/notices")
    }
}
