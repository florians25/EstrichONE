import Foundation

/// Einsatz aus der Planung (`AssignmentView` des Servers). Planen geschieht im Büro; die App zeigt nur an.
public struct Assignment: Codable, Equatable, Identifiable, Sendable {
    public struct Person: Codable, Equatable, Sendable {
        public let id: UUID
        public let name: String
    }

    public struct Resource: Codable, Equatable, Sendable {
        public let id: UUID
        public let name: String
        public let kind: String
    }

    public let id: UUID
    /// Kalendertag `yyyy-MM-dd`.
    public let day: String
    /// `HH:mm:ss` oder `nil`.
    public let startTime: String?
    public let activity: String
    public let bauakteId: UUID
    public let bauakteNumber: String?
    public let bauakteTitle: String
    public let bauakteAddress: String?
    public let crewName: String?
    public let people: [Person]
    public let resources: [Resource]
    public let notes: String?
    public let warnings: [String]

    /// „07:00“
    public var startText: String? { startTime.map { String($0.prefix(5)) } }
}

public struct PlanningRange: Codable, Equatable, Sendable {
    public let from: String
    public let to: String
    public let assignments: [Assignment]
}

/// „Mein Einsatz“: online vom Server, zuletzt geladener Stand bleibt offline sichtbar (mit Zeitpunkt).
public struct MyPlanning: Sendable {
    public let store: LocalStore
    private let now: @Sendable () -> Date

    public init(store: LocalStore, now: @escaping @Sendable () -> Date = Date.init) {
        self.store = store
        self.now = now
    }

    public struct Snapshot: Equatable, Sendable {
        public let range: PlanningRange
        public let loadedAt: Date
        public let isCurrent: Bool

        public func assignments(on day: String) -> [Assignment] {
            range.assignments.filter { $0.day == day }.sorted { ($0.startTime ?? "99") < ($1.startTime ?? "99") }
        }
    }

    /// Heute und die nächsten 6 Tage laden; bei fehlender Verbindung den gespeicherten Stand liefern.
    public func load(using client: AuthorizedClient?, calendar: Calendar = .current) async -> Snapshot? {
        let today = CalendarDay.string(now(), calendar: calendar)
        let end = CalendarDay.string(calendar.date(byAdding: .day, value: 6, to: now()) ?? now(), calendar: calendar)
        if let client {
            do throws(APIError) {
                let range: PlanningRange = try await client.get("planning/mine", query: [
                    URLQueryItem(name: "from", value: today), URLQueryItem(name: "to", value: end),
                ])
                try? await save(range)
                return Snapshot(range: range, loadedAt: now(), isCurrent: true)
            } catch {
                // Offline oder Recht fehlt: gespeicherten Stand zeigen.
            }
        }
        return await cached()
    }

    public func cached() async -> Snapshot? {
        guard let text = try? await store.meta("my_planning"), let data = text.data(using: .utf8),
              let stored = try? JSONCoding.decoder.decode(Stored.self, from: data) else { return nil }
        return Snapshot(range: stored.range, loadedAt: stored.loadedAt, isCurrent: false)
    }

    private func save(_ range: PlanningRange) async throws {
        let data = try JSONCoding.encoder.encode(Stored(range: range, loadedAt: now()))
        try await store.setMeta("my_planning", String(decoding: data, as: UTF8.self))
    }

    private struct Stored: Codable {
        let range: PlanningRange
        let loadedAt: Date
    }
}
