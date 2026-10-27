import Foundation

/// Stand eines Raums (Server: `RoomStatus`).
public enum RoomStatus: String, CaseIterable, Identifiable, Sendable {
    case planned
    /// Estrich eingebracht.
    case laid
    /// Belegreif (CM-Messung bestanden).
    case ready

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .planned: "Geplant"
        case .laid: "Eingebracht"
        case .ready: "Belegreif"
        }
    }
}

/// Raum mit Fläche und Estrichaufbau (Sync-Entität `room`, Server: `RoomDto`).
public struct Room: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let name: String
    public let floor: String?
    public let areaSquareMeters: Double?
    public let thicknessMillimeters: Int?
    public let insulationMillimeters: Int?
    public let underfloorHeating: Bool
    public let status: RoomStatus
    public let notes: String?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        bauakteId = data["bauakteId"]?.string.flatMap(UUID.init(uuidString:))
        name = data["name"]?.string ?? ""
        floor = data["floor"]?.string
        areaSquareMeters = data["areaSquareMeters"]?.number
        thicknessMillimeters = data["thicknessMillimeters"]?.number.map { Int($0) }
        insulationMillimeters = data["insulationMillimeters"]?.number.map { Int($0) }
        underfloorHeating = data["underfloorHeating"]?.bool ?? false
        status = data["status"]?.string.flatMap(RoomStatus.init(rawValue:)) ?? .planned
        notes = data["notes"]?.string
        syncState = entity.syncState
    }
}

/// Eingaben für einen Raum (anlegen und ändern). Regeln wie auf dem Server: Name 1–100 Zeichen, Fläche nicht negativ,
/// Estrichdicke 10–300 mm, Dämmung 0–500 mm.
public struct RoomInput: Equatable, Sendable {
    public var name: String
    public var floor: String
    public var areaSquareMeters: Double?
    public var thicknessMillimeters: Int?
    public var insulationMillimeters: Int?
    public var underfloorHeating: Bool
    public var status: RoomStatus
    public var notes: String

    public init(name: String = "", floor: String = "", areaSquareMeters: Double? = nil, thicknessMillimeters: Int? = nil,
                insulationMillimeters: Int? = nil, underfloorHeating: Bool = false, status: RoomStatus = .planned, notes: String = "") {
        self.name = name
        self.floor = floor
        self.areaSquareMeters = areaSquareMeters
        self.thicknessMillimeters = thicknessMillimeters
        self.insulationMillimeters = insulationMillimeters
        self.underfloorHeating = underfloorHeating
        self.status = status
        self.notes = notes
    }

    public init(_ room: Room) {
        self.init(name: room.name, floor: room.floor ?? "", areaSquareMeters: room.areaSquareMeters,
                  thicknessMillimeters: room.thicknessMillimeters, insulationMillimeters: room.insulationMillimeters,
                  underfloorHeating: room.underfloorHeating, status: room.status, notes: room.notes ?? "")
    }

    public func validate() throws(RoomError) {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed.count <= 100 else { throw .nameRequired }
        if let areaSquareMeters, areaSquareMeters < 0 || areaSquareMeters > 100_000 { throw .invalidArea }
        if let thicknessMillimeters, !(10...300).contains(thicknessMillimeters) { throw .invalidThickness }
        if let insulationMillimeters, !(0...500).contains(insulationMillimeters) { throw .invalidInsulation }
    }

    /// Felder im Format des Servers.
    var fields: [String: JSONValue] {
        func text(_ value: String) -> JSONValue {
            let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed.isEmpty ? .null : .string(trimmed)
        }
        return [
            "name": .string(name.trimmingCharacters(in: .whitespacesAndNewlines)),
            "floor": text(floor),
            "areaSquareMeters": areaSquareMeters.map { .number(($0 * 100).rounded() / 100) } ?? .null,
            "thicknessMillimeters": thicknessMillimeters.map { .number(Double($0)) } ?? .null,
            "insulationMillimeters": insulationMillimeters.map { .number(Double($0)) } ?? .null,
            "underfloorHeating": .bool(underfloorHeating),
            "status": .string(status.rawValue),
            "notes": text(notes),
        ]
    }
}

public enum RoomError: Error, Equatable, Sendable {
    case nameRequired
    case invalidArea
    case invalidThickness
    case invalidInsulation
    case storage
    /// „Nur Ansicht“: Server länger nicht erreichbar, Ändern gesperrt.
    case readOnly

    static func from(_ error: any Error) -> Self {
        if let error = error as? Self { return error }
        return error is ReadOnlyModeError ? .readOnly : .storage
    }

    public var message: String {
        switch self {
        case .readOnly: ReadOnlyModeError.message
        case .nameRequired: "Bitte den Raum benennen (z. B. „Bad OG“)."
        case .invalidArea: "Die Fläche muss zwischen 0 und 100 000 m² liegen."
        case .invalidThickness: "Die Estrichdicke muss zwischen 10 und 300 mm liegen."
        case .invalidInsulation: "Die Dämmung muss zwischen 0 und 500 mm liegen."
        case .storage: "Der Raum konnte auf diesem Gerät nicht gespeichert werden."
        }
    }
}

/// Geschosse in der Reihenfolge des Baus: Keller, Erdgeschoss, Obergeschosse, Dach – unbekannte danach, ohne Angabe zuletzt.
public enum RoomFloor {
    public static func rank(_ floor: String?) -> Int {
        let key = (floor ?? "").trimmingCharacters(in: .whitespaces).uppercased().replacingOccurrences(of: " ", with: "")
        switch key {
        case "": return 1000
        case "KG", "UG", "KELLER": return 0
        case "EG": return 10
        case "OG", "1.OG": return 20
        case "2.OG": return 30
        case "3.OG": return 40
        case "DG": return 80
        case "SPITZBODEN": return 90
        default: return 500
        }
    }
}

/// Summen über die Räume einer Bauakte.
public struct RoomTotals: Equatable, Sendable {
    public let count: Int
    public let area: Double
    public let heatedArea: Double
    public let laid: Int
    public let ready: Int

    public init(_ rooms: [Room]) {
        count = rooms.count
        area = rooms.compactMap(\.areaSquareMeters).reduce(0, +)
        heatedArea = rooms.filter(\.underfloorHeating).compactMap(\.areaSquareMeters).reduce(0, +)
        laid = rooms.filter { $0.status == .laid }.count
        ready = rooms.filter { $0.status == .ready }.count
    }
}

extension BauakteService {
    /// Räume einer Bauakte, nach Geschoss (von unten nach oben) und Name.
    public func rooms(of bauakteId: UUID) async throws -> [Room] {
        try await store.entities(.room, parent: bauakteId).map(Room.init)
            .sorted { (RoomFloor.rank($0.floor), $0.floor ?? "", $0.name.lowercased()) < (RoomFloor.rank($1.floor), $1.floor ?? "", $1.name.lowercased()) }
    }

    /// Raum anlegen – sofort lokal, übertragen per `room.create`.
    @discardableResult
    public func addRoom(_ input: RoomInput, to bauakteId: UUID) async throws(RoomError) -> UUID {
        try input.validate()
        let id = UUID.v7(now: currentDate())
        var payload = input.fields
        payload["bauakteId"] = JSONValue(bauakteId)
        let local = JSONValue.object(payload).merging(["id": JSONValue(id), "version": 0, "updatedAt": .string(APIDate.format(currentDate()))])
        do {
            try await store.create(.room, id: id, data: local, payload: payload)
        } catch {
            throw .from(error)
        }
        return id
    }

    /// Raum ändern: nur geänderte Felder (feldweise Konfliktprüfung auf dem Server).
    public func updateRoom(_ room: Room, to input: RoomInput) async throws(RoomError) {
        try input.validate()
        let before = RoomInput(room).fields
        let changes = input.fields.filter { before[$0.key] != $0.value }
        guard !changes.isEmpty else { return }
        do {
            try await store.update(.room, id: room.id, changes: changes)
        } catch {
            throw .from(error)
        }
    }

    public func deleteRoom(_ id: UUID) async throws(RoomError) {
        do {
            try await store.delete(.room, id: id)
        } catch {
            throw .from(error)
        }
    }
}
