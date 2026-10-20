import Foundation

/// Status einer Bauakte (Server: `BauakteStatus`, docs/reengineering/05, Abschnitt 2.3).
public enum BauakteStatus: String, CaseIterable, Sendable {
    case inquiry
    case planned
    case inProgress
    case drying
    case completed
    case cancelled

    public var title: String {
        switch self {
        case .inquiry: "Anfrage"
        case .planned: "Geplant"
        case .inProgress: "In Ausführung"
        case .drying: "Belegreif / Trocknung"
        case .completed: "Abgeschlossen"
        case .cancelled: "Storniert"
        }
    }
}

/// Art der Bauakte bei der Schnellanlage (09, Abschnitt 2). Wird als `templateKey` gespeichert.
public enum BauakteTemplate: String, CaseIterable, Sendable {
    case standard
    case newBuild = "neubau"
    case renovation = "sanierung"
    case commercial = "gewerbe"

    public var title: String {
        switch self {
        case .standard: "Standard"
        case .newBuild: "Neubau"
        case .renovation: "Sanierung"
        case .commercial: "Gewerbe"
        }
    }
}

/// Bauakte für die Anzeige, gelesen aus dem lokalen Stand.
public struct Bauakte: Identifiable, Equatable, Sendable {
    public let id: UUID
    /// Vergibt der Server bei der Synchronisation; vorher `nil` („Neu · noch ohne Nummer“).
    public let number: String?
    public let title: String
    public let status: BauakteStatus
    public let street: String?
    public let postalCode: String?
    public let city: String?
    public let latitude: Double?
    public let longitude: Double?
    public let description: String?
    public let customerId: UUID?
    public let templateKey: String?
    public let updatedAt: Date?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        number = data["number"]?.string
        title = data["title"]?.string ?? ""
        status = data["status"]?.string.flatMap(BauakteStatus.init(rawValue:)) ?? .planned
        street = data["street"]?.string
        postalCode = data["postalCode"]?.string
        city = data["city"]?.string
        latitude = data["latitude"]?.number
        longitude = data["longitude"]?.number
        description = data["description"]?.string
        customerId = data["customerId"]?.string.flatMap(UUID.init(uuidString:))
        templateKey = data["templateKey"]?.string
        updatedAt = data["updatedAt"]?.string.flatMap(APIDate.parse)
        syncState = entity.syncState
    }

    /// „Musterstr. 12, 84028 Landshut“ – nur vorhandene Teile.
    public var addressLine: String? {
        let place = [postalCode, city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " ")
        let parts = [street, place].compactMap { $0?.isEmpty == false ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }

    public var hasLocation: Bool { latitude != nil && longitude != nil }
}

/// Foto, Video oder Dokument einer Bauakte.
public struct FileItem: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let kind: String
    public let originalName: String
    public let contentType: String
    public let sizeBytes: Int64
    public let caption: String?
    public let capturedAt: Date?
    /// `stored`, sobald der Server den Inhalt vollständig hat.
    public let isStoredOnServer: Bool
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        bauakteId = data["bauakteId"]?.string.flatMap(UUID.init(uuidString:))
        kind = data["kind"]?.string ?? "photo"
        originalName = data["originalName"]?.string ?? ""
        contentType = data["contentType"]?.string ?? "application/octet-stream"
        sizeBytes = Int64(data["sizeBytes"]?.number ?? 0)
        caption = data["caption"]?.string
        capturedAt = data["capturedAt"]?.string.flatMap(APIDate.parse)
        isStoredOnServer = data["state"]?.string == "stored"
        syncState = entity.syncState
    }

    public var isPhoto: Bool { kind == "photo" }
}
