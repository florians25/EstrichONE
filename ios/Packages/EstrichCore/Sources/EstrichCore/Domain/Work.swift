import Foundation

/// Notiz einer Bauakte (Server: `NoteDto`).
public struct Note: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let authorUserId: UUID?
    public let text: String
    public let isPinned: Bool
    public let createdAt: Date?
    public let editedAt: Date?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        bauakteId = data["bauakteId"]?.string.flatMap(UUID.init(uuidString:))
        authorUserId = data["authorUserId"]?.string.flatMap(UUID.init(uuidString:))
        text = data["text"]?.string ?? ""
        isPinned = data["isPinned"]?.bool ?? false
        createdAt = data["createdAt"]?.string.flatMap(APIDate.parse)
        editedAt = data["editedAt"]?.string.flatMap(APIDate.parse)
        syncState = entity.syncState
    }
}

/// Aufgabe oder Mangel (Server: `WorkItemKind`).
public enum WorkItemKind: String, CaseIterable, Identifiable, Sendable {
    case task
    case defect

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .task: "Aufgabe"
        case .defect: "Mangel"
        }
    }
}

public enum WorkItemStatus: String, CaseIterable, Sendable {
    case open
    case inProgress
    case done

    public var title: String {
        switch self {
        case .open: "Offen"
        case .inProgress: "In Arbeit"
        case .done: "Erledigt"
        }
    }
}

/// Aufgabe oder Mangel, mit oder ohne Bauakte (Server: `WorkItemDto`).
public struct WorkItem: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let kind: WorkItemKind
    public let status: WorkItemStatus
    public let title: String
    public let description: String?
    /// Kalendertag ohne Uhrzeit (`yyyy-MM-dd`).
    public let dueDate: String?
    public let assigneeUserId: UUID?
    public let createdByUserId: UUID?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        bauakteId = data["bauakteId"]?.string.flatMap(UUID.init(uuidString:))
        kind = data["kind"]?.string.flatMap(WorkItemKind.init(rawValue:)) ?? .task
        status = data["status"]?.string.flatMap(WorkItemStatus.init(rawValue:)) ?? .open
        title = data["title"]?.string ?? ""
        description = data["description"]?.string
        dueDate = data["dueDate"]?.string
        assigneeUserId = data["assigneeUserId"]?.string.flatMap(UUID.init(uuidString:))
        createdByUserId = data["createdByUserId"]?.string.flatMap(UUID.init(uuidString:))
        syncState = entity.syncState
    }

    public var isDone: Bool { status == .done }

    /// Fällig heute oder früher und nicht erledigt.
    public func isDue(on day: String) -> Bool {
        guard !isDone, let dueDate else { return false }
        return dueDate <= day
    }
}

/// Eingaben für eine neue Aufgabe bzw. einen neuen Mangel.
public struct NewWorkItem: Sendable {
    public var kind: WorkItemKind
    public var title: String
    public var description: String?
    public var dueDate: String?
    public var assigneeUserId: UUID?
    public var bauakteId: UUID?

    public init(kind: WorkItemKind, title: String, description: String? = nil, dueDate: String? = nil, assigneeUserId: UUID? = nil,
                bauakteId: UUID? = nil) {
        self.kind = kind
        self.title = title
        self.description = description
        self.dueDate = dueDate
        self.assigneeUserId = assigneeUserId
        self.bauakteId = bauakteId
    }
}

/// Eintrag im Verlauf einer Bauakte (`GET /bauakten/{id}/history`). Den Satz formuliert der Server.
public struct BauakteHistoryEntry: Decodable, Equatable, Sendable {
    public let at: Date
    public let action: String
    public let actorUserId: UUID?
    public let actorName: String?
    public let text: String
}

/// Name eines Benutzers (`GET /directory/users`), z. B. für „Zugewiesen an“.
public struct DirectoryUser: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let displayName: String
    public let isActive: Bool

    public init(id: UUID, displayName: String, isActive: Bool) {
        self.id = id
        self.displayName = displayName
        self.isActive = isActive
    }
}

/// Kalendertage im Format des Servers (`DateOnly`).
public enum CalendarDay {
    public static func string(_ date: Date, calendar: Calendar = .current) -> String {
        let parts = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
    }

    public static func date(_ text: String, calendar: Calendar = .current) -> Date? {
        let parts = text.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        return calendar.date(from: DateComponents(year: parts[0], month: parts[1], day: parts[2]))
    }
}
