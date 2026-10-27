import Foundation

/// „Wer fährt wohin“ je Kolonne für einen Tag (`GET /planning/day/{day}`, docs/planung-api.md): Tagesbesetzung,
/// Fahrzeuge, Baustellen in Fahrtreihenfolge mit Uhrzeit, Ansprechpartner, Freigabestand. Dieselben Endpunkte nutzt
/// das Disponenten-Cockpit im Web – die Schnelldisposition vom Handy ändert dieselben Einsätze.
public struct DayPlan: Codable, Equatable, Sendable {
    public struct Member: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        /// Abwesenheit oder Termin („Urlaub“); leer = verfügbar.
        public let absenceNote: String?

        public init(id: UUID, name: String, absenceNote: String? = nil) {
            self.id = id
            self.name = name
            self.absenceNote = absenceNote
        }
    }

    public struct Ref: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        /// Nur Fahrzeuge/Geräte: `vehicle`, `machine`, …
        public let kind: String?

        public init(id: UUID, name: String, kind: String? = nil) {
            self.id = id
            self.name = name
            self.kind = kind
        }
    }

    public struct Stop: Codable, Equatable, Sendable, Identifiable {
        public let assignmentId: UUID
        /// Position in der Fahrtreihenfolge (1 = erste Baustelle).
        public let order: Int
        public let startTime: String?
        public let endTime: String?
        public let kind: String?
        public let activity: String
        public let bauakteId: UUID
        public let number: String?
        public let title: String
        public let address: String?
        public let latitude: Double?
        public let longitude: Double?
        public let siteContactName: String?
        public let siteContactPhone: String?
        public let navigationUrl: String?
        public let notes: String?
        public let people: [Ref]
        public let resources: [Ref]
        public let warnings: [String]
        /// `new` oder `changed` seit der Freigabe.
        public let change: String?
        public let version: Int64?

        public var id: UUID { assignmentId }
        public var startText: String? { startTime.map { String($0.prefix(5)) } }
        public var endText: String? { endTime.map { String($0.prefix(5)) } }
        /// „07:00–12:00“, „ab 07:00“ oder „ganztägig“.
        public var timeText: String {
            switch (startText, endText) {
            case let (start?, end?): "\(start)–\(end)"
            case let (start?, nil): "ab \(start)"
            default: "ganztägig"
            }
        }
        public var isChanged: Bool { change != nil }

        /// Navigation in Apple Karten (Koordinaten, sonst Adresse).
        public var mapsURL: URL? {
            if let latitude, let longitude { return URL(string: "https://maps.apple.com/?daddr=\(latitude),\(longitude)") }
            guard let address, !address.isEmpty,
                  let encoded = address.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) else { return nil }
            return URL(string: "https://maps.apple.com/?daddr=\(encoded)")
        }
    }

    public struct Crew: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let name: String
        public let color: String?
        public let leaderName: String?
        public let members: [Member]
        public let vehicles: [Ref]
        /// Tagesbesetzung weicht von der Stammbesetzung ab.
        public let rosterChanged: Bool
        public let rosterNote: String?
        public let isMine: Bool
        public let stops: [Stop]
        public let warnings: [String]

        public var available: [Member] { members.filter { $0.absenceNote == nil } }
        public var absent: [Member] { members.filter { $0.absenceNote != nil } }
        /// Konflikte der Kolonne und ihrer Baustellen (Doppelbelegung, Abwesenheit, Gerät belegt).
        public var conflicts: [String] {
            var seen = Set<String>()
            return (warnings + stops.flatMap(\.warnings)).filter { seen.insert($0).inserted }
        }
    }

    public struct Removed: Codable, Equatable, Sendable {
        public let assignmentId: UUID
        public let title: String
        public let crewName: String?
        public let activity: String
    }

    public struct Release: Codable, Equatable, Sendable {
        public let releasedAt: Date
        public let releasedByName: String
        public let note: String?
        /// Neue, geänderte und entfernte Einsätze seit der Freigabe.
        public let changedCount: Int
        public let removed: [Removed]
    }

    public let day: String
    public let holiday: String?
    public let release: Release?
    public let canPlan: Bool
    public let myCrewIds: [UUID]
    public let crews: [Crew]
    public let withoutCrew: [Stop]
    public let absent: [Member]
    public let free: [Ref]

    /// Meine Kolonne zuerst, dann wie geliefert (Reihenfolge der Kolonnen im Büro).
    public var sortedCrews: [Crew] {
        crews.filter(\.isMine) + crews.filter { !$0.isMine }
    }

    public var stopCount: Int { crews.reduce(withoutCrew.count) { $0 + $1.stops.count } }

    public func crew(_ id: UUID) -> Crew? { crews.first { $0.id == id } }

    /// Kolonne, in der ein Einsatz steht.
    public func crew(of assignmentId: UUID) -> Crew? { crews.first { $0.stops.contains { $0.assignmentId == assignmentId } } }
}

extension DayPlan {
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        day = try c.decode(String.self, forKey: .day)
        holiday = try c.decodeIfPresent(String.self, forKey: .holiday)
        release = try c.decodeIfPresent(Release.self, forKey: .release)
        canPlan = try c.decodeIfPresent(Bool.self, forKey: .canPlan) ?? false
        myCrewIds = try c.decodeIfPresent([UUID].self, forKey: .myCrewIds) ?? []
        crews = try c.decodeIfPresent([Crew].self, forKey: .crews) ?? []
        withoutCrew = try c.decodeIfPresent([Stop].self, forKey: .withoutCrew) ?? []
        absent = try c.decodeIfPresent([Member].self, forKey: .absent) ?? []
        free = try c.decodeIfPresent([Ref].self, forKey: .free) ?? []
    }
}

extension DayPlan.Crew {
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        name = try c.decode(String.self, forKey: .name)
        color = try c.decodeIfPresent(String.self, forKey: .color)
        leaderName = try c.decodeIfPresent(String.self, forKey: .leaderName)
        members = try c.decodeIfPresent([DayPlan.Member].self, forKey: .members) ?? []
        vehicles = try c.decodeIfPresent([DayPlan.Ref].self, forKey: .vehicles) ?? []
        rosterChanged = try c.decodeIfPresent(Bool.self, forKey: .rosterChanged) ?? false
        rosterNote = try c.decodeIfPresent(String.self, forKey: .rosterNote)
        isMine = try c.decodeIfPresent(Bool.self, forKey: .isMine) ?? false
        stops = (try c.decodeIfPresent([DayPlan.Stop].self, forKey: .stops) ?? []).sorted { $0.order < $1.order }
        warnings = try c.decodeIfPresent([String].self, forKey: .warnings) ?? []
    }
}

extension DayPlan.Stop {
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        assignmentId = try c.decode(UUID.self, forKey: .assignmentId)
        order = try c.decodeIfPresent(Int.self, forKey: .order) ?? 0
        startTime = try c.decodeIfPresent(String.self, forKey: .startTime)
        endTime = try c.decodeIfPresent(String.self, forKey: .endTime)
        kind = try c.decodeIfPresent(String.self, forKey: .kind)
        activity = try c.decodeIfPresent(String.self, forKey: .activity) ?? ""
        bauakteId = try c.decode(UUID.self, forKey: .bauakteId)
        number = try c.decodeIfPresent(String.self, forKey: .number)
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? ""
        address = try c.decodeIfPresent(String.self, forKey: .address)
        latitude = try c.decodeIfPresent(Double.self, forKey: .latitude)
        longitude = try c.decodeIfPresent(Double.self, forKey: .longitude)
        siteContactName = try c.decodeIfPresent(String.self, forKey: .siteContactName)
        siteContactPhone = try c.decodeIfPresent(String.self, forKey: .siteContactPhone)
        navigationUrl = try c.decodeIfPresent(String.self, forKey: .navigationUrl)
        notes = try c.decodeIfPresent(String.self, forKey: .notes)
        people = try c.decodeIfPresent([DayPlan.Ref].self, forKey: .people) ?? []
        resources = try c.decodeIfPresent([DayPlan.Ref].self, forKey: .resources) ?? []
        warnings = try c.decodeIfPresent([String].self, forKey: .warnings) ?? []
        change = try c.decodeIfPresent(String.self, forKey: .change)
        version = try c.decodeIfPresent(Int64.self, forKey: .version)
    }
}

// MARK: Schnelldisposition

/// Eine Änderung der Schnelldisposition. Online sofort an den Server, ohne Verbindung vorgemerkt
/// (`PlanningOutbox`) und beim nächsten Laden in der gemerkten Reihenfolge gesendet.
public enum PlanChange: Codable, Equatable, Sendable {
    /// Baustelle einer Kolonne für den Tag zuweisen (neuer Einsatz mit Client-ID).
    case assign(id: UUID, bauakteId: UUID, title: String, crewId: UUID, day: String, activity: String, startTime: String?, endTime: String?)
    /// Einsatz auf anderen Tag, andere Kolonne und/oder andere Uhrzeit setzen.
    case move(id: UUID, title: String, day: String?, crewId: UUID?, startTime: String?, endTime: String?, setTime: Bool)
    /// Einsatz entfernen.
    case remove(id: UUID, title: String)
    /// Fahrtreihenfolge (und Uhrzeiten) der Baustellen einer Kolonne am Tag.
    case order(crewId: UUID, day: String, assignmentIds: [UUID], startTimes: [String?])
    /// Mitarbeiter bzw. Fahrzeug der Kolonne für den Tag tauschen.
    case swap(crewId: UUID, day: String, fromUserId: UUID?, toUserId: UUID?, fromResourceId: UUID?, toResourceId: UUID?, text: String)

    /// Kurzer Satz für die Liste der vorgemerkten Änderungen.
    public var summary: String {
        switch self {
        case let .assign(_, _, title, _, day, _, startTime, _):
            "\(title) einplanen (\(Self.dayText(day))\(startTime.map { ", \($0.prefix(5))" } ?? ""))"
        case let .move(_, title, day, _, startTime, _, _):
            "\(title) verschieben\(day.map { " auf \(Self.dayText($0))" } ?? "")\(startTime.map { ", \($0.prefix(5))" } ?? "")"
        case let .remove(_, title): "\(title) aus dem Plan nehmen"
        case let .order(_, day, ids, _): "Reihenfolge von \(ids.count) Baustellen (\(Self.dayText(day)))"
        case let .swap(_, _, _, _, _, _, text): text
        }
    }

    /// Tag dieser Änderung (für die Anzeige am richtigen Tag); bei Verschieben der Zieltag.
    public var day: String? {
        switch self {
        case let .assign(_, _, _, _, day, _, _, _): day
        case let .move(_, _, day, _, _, _, _): day
        case .remove: nil
        case let .order(_, day, _, _): day
        case let .swap(_, day, _, _, _, _, _): day
        }
    }

    static func dayText(_ day: String) -> String {
        guard let date = CalendarDay.date(day) else { return day }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "de_DE")
        formatter.dateFormat = "EE dd.MM."
        return formatter.string(from: date)
    }
}

/// Ergebnis einer gesendeten Änderung: Hinweise des Servers (Konflikte blockieren nicht, die Planung entscheidet).
public struct PlanChangeResult: Equatable, Sendable {
    public let warnings: [String]
    /// Neuer Tagesplan, wenn der Endpunkt ihn liefert.
    public let plan: DayPlan?
}

/// Ergebnis von `POST /planning/batch`.
public struct PlanBatchResult: Decodable, Equatable, Sendable {
    public let warnings: [String]
    public let deletedIds: [UUID]
    public let dryRun: Bool

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        warnings = try c.decodeIfPresent([String].self, forKey: .warnings) ?? []
        deletedIds = try c.decodeIfPresent([UUID].self, forKey: .deletedIds) ?? []
        dryRun = try c.decodeIfPresent(Bool.self, forKey: .dryRun) ?? false
    }

    private enum CodingKeys: String, CodingKey {
        case warnings, deletedIds, dryRun
    }
}

/// Bauakte im Vorrat („Ungeplante Bauvorhaben“, `GET /planning/backlog`).
public struct BacklogEntry: Decodable, Equatable, Sendable, Identifiable {
    public let bauakteId: UUID
    public let number: String?
    public let title: String
    public let status: String?
    public let street: String?
    public let postalCode: String?
    public let city: String?
    public let plannedStart: String?
    public let customerName: String?
    public let totalAreaSquareMeters: Double?
    public let upcomingAssignments: Int
    public let nextAssignment: String?

    public var id: UUID { bauakteId }
    public var address: String? {
        let place = [postalCode, city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " ")
        let line = [street, place].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: ", ")
        return line.isEmpty ? nil : line
    }
    /// Noch nicht eingeplant.
    public var isFree: Bool { upcomingAssignments == 0 }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        bauakteId = try c.decode(UUID.self, forKey: .bauakteId)
        number = try c.decodeIfPresent(String.self, forKey: .number)
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? ""
        status = try c.decodeIfPresent(String.self, forKey: .status)
        street = try c.decodeIfPresent(String.self, forKey: .street)
        postalCode = try c.decodeIfPresent(String.self, forKey: .postalCode)
        city = try c.decodeIfPresent(String.self, forKey: .city)
        plannedStart = try c.decodeIfPresent(String.self, forKey: .plannedStart)
        customerName = try c.decodeIfPresent(String.self, forKey: .customerName)
        totalAreaSquareMeters = try c.decodeIfPresent(Double.self, forKey: .totalAreaSquareMeters)
        upcomingAssignments = try c.decodeIfPresent(Int.self, forKey: .upcomingAssignments) ?? 0
        nextAssignment = try c.decodeIfPresent(String.self, forKey: .nextAssignment)
    }

    private enum CodingKeys: String, CodingKey {
        case bauakteId, number, title, status, street, postalCode, city, plannedStart, customerName, totalAreaSquareMeters,
             upcomingAssignments, nextAssignment
    }
}

/// Fahrzeug oder Gerät (`GET /planning/resources`).
public struct PlanningResource: Decodable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public let kind: String
    public let name: String
    public let identifier: String?
    public let isActive: Bool
}

private struct JSONBody: Encodable, Sendable {
    let value: JSONValue
    func encode(to encoder: any Encoder) throws { try value.encode(to: encoder) }
}

extension AuthorizedClient {
    public func dayPlan(_ day: String) async throws(APIError) -> DayPlan {
        try await get("planning/day/\(day)")
    }

    public func backlog() async throws(APIError) -> [BacklogEntry] {
        try await get("planning/backlog")
    }

    public func planningResources() async throws(APIError) -> [PlanningResource] {
        try await get("planning/resources")
    }

    /// Konfliktvorschau ohne Speichern (`dryRun`).
    public func check(_ change: PlanChange) async throws(APIError) -> [String] {
        guard let operations = change.batchOperations else { return [] }
        let result: PlanBatchResult = try await post("planning/batch", body: JSONBody(value: ["operations": .array(operations), "dryRun": true]))
        return result.warnings
    }

    /// Sendet eine Änderung an die Endpunkte des Cockpits (gleiche wie im Web).
    public func apply(_ change: PlanChange) async throws(APIError) -> PlanChangeResult {
        switch change {
        case let .order(crewId, day, ids, times):
            var body: [String: JSONValue] = ["assignmentIds": .array(ids.map { JSONValue($0) })]
            if times.contains(where: { $0 != nil }) { body["startTimes"] = .array(times.map { JSONValue($0) }) }
            let plan: DayPlan = try await send(try Self.json("PUT", "planning/crews/\(crewId.key)/days/\(day)/order", .object(body)))
            return PlanChangeResult(warnings: [], plan: plan)
        case let .swap(crewId, day, fromUser, toUser, fromResource, toResource, _):
            let body: JSONValue = ["fromUserId": JSONValue(fromUser), "toUserId": JSONValue(toUser), "fromResourceId": JSONValue(fromResource),
                                   "toResourceId": JSONValue(toResource), "exchange": true]
            let plan: DayPlan = try await post("planning/crews/\(crewId.key)/days/\(day)/swap", body: JSONBody(value: body))
            return PlanChangeResult(warnings: [], plan: plan)
        default:
            let result: PlanBatchResult = try await post("planning/batch",
                                                         body: JSONBody(value: ["operations": .array(change.batchOperations ?? [])]))
            return PlanChangeResult(warnings: result.warnings, plan: nil)
        }
    }

    private static func json(_ method: String, _ path: String, _ body: JSONValue) throws(APIError) -> APIRequest {
        do {
            return APIRequest(method: method, path: path, body: try JSONCoding.encoder.encode(JSONBody(value: body)), contentType: "application/json")
        } catch {
            throw .invalidResponse
        }
    }
}

extension PlanChange {
    /// Operationen für `POST /planning/batch` (Upsert, Move, Delete).
    var batchOperations: [JSONValue]? {
        switch self {
        case let .assign(id, bauakteId, _, crewId, day, activity, startTime, endTime):
            let assignment: JSONValue = [
                "bauakteId": JSONValue(bauakteId), "day": .string(day), "startTime": JSONValue(startTime), "endTime": JSONValue(endTime),
                "activity": .string(activity), "crewId": JSONValue(crewId), "userIds": .null, "resourceIds": .null, "notes": .null,
            ]
            return [["op": "upsert", "id": JSONValue(id), "assignment": assignment]]
        case let .move(id, _, day, crewId, startTime, endTime, setTime):
            var op: [String: JSONValue] = ["op": "move", "id": JSONValue(id), "day": JSONValue(day)]
            if let crewId {
                op["changeCrew"] = true
                op["crewId"] = JSONValue(crewId)
            }
            if setTime {
                op["setTime"] = true
                op["startTime"] = JSONValue(startTime)
                op["endTime"] = JSONValue(endTime)
            }
            return [.object(op)]
        case let .remove(id, _):
            return [["op": "delete", "id": JSONValue(id)]]
        case .order, .swap:
            return nil
        }
    }
}

/// Vorgemerkte Änderungen der Schnelldisposition (ohne Verbindung erfasst), verschlüsselt im lokalen Speicher.
/// Gesendet wird in der Reihenfolge der Erfassung; was der Server ablehnt, bleibt mit Begründung sichtbar.
public struct PlanningOutbox: Sendable {
    public struct Entry: Codable, Equatable, Sendable, Identifiable {
        public let id: UUID
        public let change: PlanChange
        public let createdAt: Date
        /// Ablehnung des Servers (bleibt zum Verwerfen stehen).
        public var error: String?
    }

    public struct FlushResult: Equatable, Sendable {
        public var sent = 0
        public var warnings: [String] = []
        public var failed = 0
    }

    public let store: LocalStore
    private let now: @Sendable () -> Date
    private static let key = "planning_outbox"

    public init(store: LocalStore, now: @escaping @Sendable () -> Date = Date.init) {
        self.store = store
        self.now = now
    }

    public func entries() async -> [Entry] {
        guard let text = try? await store.meta(Self.key), let data = text.data(using: .utf8) else { return [] }
        return (try? JSONCoding.decoder.decode([Entry].self, from: data)) ?? []
    }

    public func add(_ change: PlanChange) async throws {
        var list = await entries()
        list.append(Entry(id: UUID.v7(now: now()), change: change, createdAt: now(), error: nil))
        try await save(list)
    }

    public func discard(_ id: UUID) async throws {
        try await save(await entries().filter { $0.id != id })
    }

    /// Sendet alle offenen Einträge. Bei fehlender Verbindung bleibt alles liegen; Ablehnungen werden markiert.
    public func flush(using client: AuthorizedClient) async -> FlushResult {
        var list = await entries()
        var result = FlushResult()
        var index = 0
        while index < list.count {
            guard list[index].error == nil else { index += 1; continue }
            do throws(APIError) {
                let outcome = try await client.apply(list[index].change)
                result.sent += 1
                result.warnings += outcome.warnings
                list.remove(at: index)
            } catch {
                if error == .offline { break }
                list[index].error = error.serverMessage ?? error.message
                result.failed += 1
                index += 1
            }
        }
        try? await save(list)
        return result
    }

    private func save(_ list: [Entry]) async throws {
        let data = try JSONCoding.encoder.encode(list)
        try await store.setMeta(Self.key, String(decoding: data, as: UTF8.self))
    }
}
