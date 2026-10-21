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

/// „Wer fährt wohin“ für einen Tag (`GET /planning/dispatch/{day}`): Baustellen mit Einsätzen, Personen und Fahrzeugen.
public struct DispatchBoard: Codable, Equatable, Sendable {
    public struct Ref: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
    }

    public struct Job: Codable, Equatable, Sendable, Identifiable {
        public let assignmentId: UUID
        public let activity: String
        public let startTime: String?
        public let endTime: String?
        public let crewName: String?
        /// Farbe der Kolonne als `#RRGGBB`.
        public let crewColor: String?
        public let notes: String?

        public var id: UUID { assignmentId }
        public var startText: String? { startTime.map { String($0.prefix(5)) } }
        public var endText: String? { endTime.map { String($0.prefix(5)) } }
    }

    public struct Site: Codable, Equatable, Sendable, Identifiable {
        public let bauakteId: UUID
        public let number: String?
        public let title: String
        public let address: String?
        public let siteContactName: String?
        public let siteContactPhone: String?
        public let startTime: String?
        public let jobs: [Job]
        public let people: [Ref]
        public let vehicles: [Ref]
        /// Eingeplante Kolonnen (Disposition nach Kolonnen).
        public let crews: [SiteCrew]
        public let warnings: [String]
        public let navigationUrl: String?

        public var id: UUID { bauakteId }
        public var startText: String? { startTime.map { String($0.prefix(5)) } }
    }

    public struct Person: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        public let crewName: String?
        public let siteIds: [UUID]
        public let note: String?
    }

    /// Einsatz einer Kolonne auf einer Baustelle.
    public struct SiteCrew: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        public let color: String?
        public let assignmentId: UUID?
        public let activity: String?
        public let startTime: String?

        public var startText: String? { startTime.map { String($0.prefix(5)) } }
    }

    public struct VehicleRef: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        public let kind: String?
    }

    public struct AbsentMember: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        public let note: String?
    }

    /// Kolonne des Tages; `siteIds` = Baustellen des Tages (mehr als eine = doppelt eingeplant).
    public struct Crew: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        public let color: String?
        public let leaderName: String?
        public let memberCount: Int
        public let vehicles: [VehicleRef]
        public let siteIds: [UUID]
        public let absentMembers: [AbsentMember]

        public var isDoubleBooked: Bool { siteIds.count > 1 }
    }

    public struct Vehicle: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        public let identifier: String?
        public let siteIds: [UUID]
    }

    public let day: String
    public let sites: [Site]
    public let people: [Person]
    public let vehicles: [Vehicle]
    public let crews: [Crew]
    public let mySiteIds: [UUID]
    public let canPlan: Bool

    /// Meine Baustellen zuerst, dann nach Beginn.
    public var sortedSites: [Site] {
        let mine = Set(mySiteIds)
        return sites.sorted { lhs, rhs in
            if mine.contains(lhs.id) != mine.contains(rhs.id) { return mine.contains(lhs.id) }
            return (lhs.startTime ?? "99") < (rhs.startTime ?? "99")
        }
    }

    /// Personen ohne Einsatz an diesem Tag (für die Schnellplanung).
    public var unassignedPeople: [Person] { people.filter { $0.siteIds.isEmpty } }
    public var unassignedVehicles: [Vehicle] { vehicles.filter { $0.siteIds.isEmpty } }
    public var unassignedCrews: [Crew] { crews.filter { $0.siteIds.isEmpty } }
}

// Tolerant lesen: fehlende Listen gelten als leer (der Server stellt die Disposition gerade auf Kolonnen um).
extension DispatchBoard {
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        day = try c.decode(String.self, forKey: .day)
        sites = try c.decodeIfPresent([Site].self, forKey: .sites) ?? []
        people = try c.decodeIfPresent([Person].self, forKey: .people) ?? []
        vehicles = try c.decodeIfPresent([Vehicle].self, forKey: .vehicles) ?? []
        crews = try c.decodeIfPresent([Crew].self, forKey: .crews) ?? []
        mySiteIds = try c.decodeIfPresent([UUID].self, forKey: .mySiteIds) ?? []
        canPlan = try c.decodeIfPresent(Bool.self, forKey: .canPlan) ?? false
    }
}

extension DispatchBoard.Site {
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        bauakteId = try c.decode(UUID.self, forKey: .bauakteId)
        number = try c.decodeIfPresent(String.self, forKey: .number)
        title = try c.decode(String.self, forKey: .title)
        address = try c.decodeIfPresent(String.self, forKey: .address)
        siteContactName = try c.decodeIfPresent(String.self, forKey: .siteContactName)
        siteContactPhone = try c.decodeIfPresent(String.self, forKey: .siteContactPhone)
        startTime = try c.decodeIfPresent(String.self, forKey: .startTime)
        jobs = try c.decodeIfPresent([DispatchBoard.Job].self, forKey: .jobs) ?? []
        people = try c.decodeIfPresent([DispatchBoard.Ref].self, forKey: .people) ?? []
        vehicles = try c.decodeIfPresent([DispatchBoard.Ref].self, forKey: .vehicles) ?? []
        crews = try c.decodeIfPresent([DispatchBoard.SiteCrew].self, forKey: .crews) ?? []
        warnings = try c.decodeIfPresent([String].self, forKey: .warnings) ?? []
        navigationUrl = try c.decodeIfPresent(String.self, forKey: .navigationUrl)
    }
}

extension DispatchBoard.Crew {
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        name = try c.decode(String.self, forKey: .name)
        color = try c.decodeIfPresent(String.self, forKey: .color)
        leaderName = try c.decodeIfPresent(String.self, forKey: .leaderName)
        memberCount = try c.decodeIfPresent(Int.self, forKey: .memberCount) ?? 0
        vehicles = try c.decodeIfPresent([DispatchBoard.VehicleRef].self, forKey: .vehicles) ?? []
        siteIds = try c.decodeIfPresent([UUID].self, forKey: .siteIds) ?? []
        absentMembers = try c.decodeIfPresent([DispatchBoard.AbsentMember].self, forKey: .absentMembers) ?? []
    }
}

/// Kolonne (mit Fahrzeugen und Tätigkeit), Person oder Fahrzeug auf eine andere Baustelle setzen;
/// `to == nil` mit `from` = herausnehmen, `keep` = zusätzlich einplanen statt verlegen.
public struct DispatchMove: Encodable, Sendable {
    public enum Target: String, Encodable, Sendable {
        case person
        case vehicle
        case crew
    }

    public let target: Target
    public let id: UUID
    public let toBauakteId: UUID?
    public let fromBauakteId: UUID?
    public let keep: Bool
    /// Nur Kolonne: Tätigkeit und Beginn (`HH:mm:ss`) für den neuen Einsatz.
    public let activity: String?
    public let startTime: String?

    public init(target: Target, id: UUID, to: UUID?, from: UUID?, keep: Bool = false, activity: String? = nil, startTime: String? = nil) {
        self.target = target
        self.id = id
        self.toBauakteId = to
        self.fromBauakteId = from
        self.keep = keep
        self.activity = activity
        self.startTime = startTime
    }
}

extension AuthorizedClient {
    public func dispatch(day: String) async throws(APIError) -> DispatchBoard {
        try await get("planning/dispatch/\(day)")
    }

    /// Schnellplanung (Recht `planning.manage`): liefert die neue Tafel.
    public func move(_ move: DispatchMove, on day: String) async throws(APIError) -> DispatchBoard {
        try await post("planning/dispatch/\(day)/moves", body: move)
    }
}
