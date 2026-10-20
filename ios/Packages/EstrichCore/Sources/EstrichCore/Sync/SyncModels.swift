import Foundation

/// Entitätstypen des Änderungsfeeds (docs/sync-protokoll.md, Abschnitt 5).
public enum EntityType: String, CaseIterable, Sendable {
    case bauakte
    case note
    case file
    case workItem
    case contact
    case screedType
    case diaryEntry
    case inspection
    /// Nur ausgehend (eigene Meldungen bis zur Übertragung); der Server liefert Tickets nicht über den Feed.
    case ticket

    /// Präfix der Operationstypen (`bauakte.create`, `workitem.update`, `diary.create`).
    var operationPrefix: String {
        switch self {
        case .workItem: "workitem"
        case .diaryEntry: "diary"
        default: rawValue
        }
    }

    init?(operationType: String) {
        let prefix = operationType.split(separator: ".").first.map(String.init) ?? ""
        guard let type = EntityType.allCases.first(where: { $0.operationPrefix == prefix }) else { return nil }
        self = type
    }
}

/// Übertragungszustand eines Datensatzes für die Anzeige (15, Abschnitt 4).
public enum SyncState: String, Sendable {
    /// Lokal gespeichert, wartet auf Übertragung.
    case pending
    case synced
    /// Abgelehnt oder Upload fehlgeschlagen – Erklärung in der Synchronisationsansicht.
    case problem
}

public struct LocalEntity: Sendable, Equatable {
    public let type: EntityType
    public let id: UUID
    public let data: JSONValue
    public let syncState: SyncState
}

/// Operation der Outbox (docs/sync-protokoll.md, Abschnitt 2).
public struct OutboxOperation: Sendable, Equatable {
    public enum State: String, Sendable {
        case pending
        case sending
        case rejected
    }

    public let seq: Int64
    public let opId: UUID
    public let type: String
    public let entityType: EntityType
    public let entityId: UUID
    public let baseVersion: Int64?
    public let payload: JSONValue
    public let state: State
    public let errorCode: String?
    public let errorMessage: String?
}

public struct PushOperation: Codable, Sendable {
    public let opId: UUID
    public let type: String
    public let entityId: UUID
    public let baseVersion: Int64?
    public let payload: JSONValue
}

public struct PushRequest: Codable, Sendable {
    public let operations: [PushOperation]
}

public struct FieldConflict: Codable, Sendable, Equatable {
    public let field: String
    public let serverValue: JSONValue?
    public let clientValue: JSONValue?
}

public struct PushResult: Codable, Sendable {
    public enum Status: String, Codable, Sendable {
        case applied
        case duplicate
        case conflict
        case rejected
    }

    public let opId: UUID
    public let status: Status
    public let code: String?
    public let message: String?
    public let version: Int64?
    public let conflicts: [FieldConflict]?
    public let entity: JSONValue?
}

public struct PushResponse: Codable, Sendable {
    public let results: [PushResult]
}

public struct SyncChange: Codable, Sendable {
    public let entityType: String
    public let id: UUID
    public let deleted: Bool
    public let data: JSONValue?
}

public struct PullResponse: Codable, Sendable {
    public let changes: [SyncChange]
    public let cursor: String
    public let hasMore: Bool
    public let visibleBauakteIds: [UUID]?
}

public struct UploadStatus: Codable, Sendable {
    public let id: UUID
    public let receivedBytes: Int64
    public let sizeBytes: Int64
    public let state: String
}

/// Datei in der Upload-Warteschlange (Abschnitt 6 des Protokolls).
public struct PendingUpload: Sendable, Equatable {
    public enum State: String, Sendable {
        case waiting
        case uploading
        case done
        case failed
    }

    public let fileId: UUID
    public let size: Int64
    public let sha256: String
    public let contentType: String
    public let uploadedBytes: Int64
    public let state: State
    public let errorCode: String?
}

/// Überblick für Kopfzeile und Synchronisationsansicht.
public struct SyncSummary: Sendable, Equatable {
    public let pendingOperations: Int
    public let rejectedOperations: Int
    public let pendingUploads: Int
    public let failedUploads: Int
    public let conflicts: Int
    public let lastSuccessfulSync: Date?

    public var isSettled: Bool { pendingOperations == 0 && pendingUploads == 0 }
    public var hasProblems: Bool { rejectedOperations > 0 || failedUploads > 0 || conflicts > 0 }
}

extension UUID {
    /// UUIDv7 (RFC 9562): Zeitstempel in Millisekunden + Zufall. Vom Client vergebene IDs (ADR 0006).
    public static func v7(now: Date = Date()) -> UUID {
        var bytes = [UInt8](repeating: 0, count: 16)
        let millis = UInt64(max(0, now.timeIntervalSince1970 * 1000))
        for index in 0..<6 {
            bytes[index] = UInt8((millis >> (8 * (5 - index))) & 0xFF)
        }
        var generator = SystemRandomNumberGenerator()
        for index in 6..<16 {
            bytes[index] = UInt8.random(in: 0...255, using: &generator)
        }
        bytes[6] = (bytes[6] & 0x0F) | 0x70
        bytes[8] = (bytes[8] & 0x3F) | 0x80
        return UUID(uuid: (bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
                           bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]))
    }

    /// Kleinbuchstaben wie der Server (Vergleich und Speicherung).
    var key: String { uuidString.lowercased() }
}
