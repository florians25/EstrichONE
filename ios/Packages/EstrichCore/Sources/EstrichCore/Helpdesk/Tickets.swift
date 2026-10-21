import Foundation

public enum TicketCategory: String, CaseIterable, Identifiable, Codable, Sendable {
    case signIn, sync, appError, deviceDefect, officeIt, access, other

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .signIn: "Anmeldung"
        case .sync: "Synchronisation"
        case .appError: "App-Fehler"
        case .deviceDefect: "Gerät defekt"
        case .officeIt: "Drucker/Büro-IT"
        case .access: "Zugang/Rechte"
        case .other: "Sonstiges"
        }
    }
}

public enum TicketStatus: String, CaseIterable, Codable, Sendable {
    case new, inProgress, waitingForReporter, resolved, closed

    public var title: String {
        switch self {
        case .new: "Neu"
        case .inProgress: "In Bearbeitung"
        case .waitingForReporter: "Rückfrage an Sie"
        case .resolved: "Gelöst"
        case .closed: "Geschlossen"
        }
    }
}

public enum TicketPriority: String, Codable, Sendable {
    case low, normal, high, urgent
}

/// Ticket in Listen (`TicketSummary` des Servers).
public struct TicketSummary: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let number: String
    public let title: String
    public let category: TicketCategory
    public let priority: TicketPriority
    public let status: TicketStatus
    public let reporterBlocked: Bool
    public let assigneeName: String?
    public let createdAt: Date
    public let lastActivityAt: Date
}

public struct TicketComment: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let authorUserId: UUID
    public let authorName: String?
    public let text: String
    public let isInternal: Bool
    public let createdAt: Date
}

/// Statuswechsel, Zuweisung und andere Ereignisse eines Tickets (`TicketEventDto`).
public struct TicketEvent: Decodable, Equatable, Sendable {
    public let actorName: String?
    public let kind: String
    public let fromValue: String?
    public let toValue: String?
    public let at: Date

    /// Lesbarer Satz für den Verlauf.
    public var text: String {
        let who = actorName ?? "System"
        switch kind {
        case "created": return "\(who) hat die Meldung erstellt."
        case "status", "statusChanged":
            return "\(who) hat den Status auf „\(Self.status(toValue)?.title ?? toValue ?? "–")“ gesetzt."
        case "assigned", "assignee", "assigneeChanged":
            return toValue.map { "\(who) hat die Meldung \($0) zugewiesen." } ?? "\(who) hat die Zuweisung aufgehoben."
        case "priority", "priorityChanged": return "\(who) hat die Priorität geändert."
        case "category", "categoryChanged": return "\(who) hat die Kategorie geändert."
        case "reopened": return "\(who) hat die Meldung wieder geöffnet."
        case "resolved": return "\(who) hat eine Lösung eingetragen."
        default: return "\(who): \(kind)"
        }
    }

    /// Der Server schreibt Statuswerte als Enum-Namen („InProgress“), die App nutzt camelCase.
    static func status(_ value: String?) -> TicketStatus? {
        guard let value, let first = value.first else { return nil }
        return TicketStatus(rawValue: first.lowercased() + value.dropFirst())
    }
}

public struct TicketDetail: Decodable, Equatable, Sendable {
    public let summary: TicketSummary
    public let description: String?
    public let resolution: String?
    public let comments: [TicketComment]
    public let events: [TicketEvent]

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        summary = try container.decode(TicketSummary.self, forKey: .summary)
        description = try container.decodeIfPresent(String.self, forKey: .description)
        resolution = try container.decodeIfPresent(String.self, forKey: .resolution)
        comments = try container.decodeIfPresent([TicketComment].self, forKey: .comments) ?? []
        events = (try? container.decodeIfPresent([TicketEvent].self, forKey: .events)) ?? []
    }

    private enum CodingKeys: String, CodingKey {
        case summary, description, resolution, comments, events
    }

    /// Kommentare (ohne interne Notizen) und Ereignisse zeitlich sortiert.
    public var timeline: [TimelineItem] {
        (comments.filter { !$0.isInternal }.map { TimelineItem.comment($0) } + events.map { TimelineItem.event($0) })
            .sorted { $0.date < $1.date }
    }

    public enum TimelineItem: Equatable, Sendable {
        case comment(TicketComment)
        case event(TicketEvent)

        public var date: Date {
            switch self {
            case let .comment(comment): comment.createdAt
            case let .event(event): event.at
            }
        }
    }
}

/// Eingaben von „Problem melden“. Pflicht ist nur der Titel (14, Abschnitt 2).
public struct NewTicket: Sendable {
    public var title: String
    public var description: String?
    public var category: TicketCategory
    /// „Dringend – ich kann nicht arbeiten“.
    public var reporterBlocked: Bool

    public init(title: String, description: String? = nil, category: TicketCategory = .other, reporterBlocked: Bool = false) {
        self.title = title
        self.description = description
        self.category = category
        self.reporterBlocked = reporterBlocked
    }

    var cleanTitle: String { title.trimmingCharacters(in: .whitespacesAndNewlines) }

    var cleanDescription: String? {
        let text = description?.trimmingCharacters(in: .whitespacesAndNewlines)
        return text?.isEmpty == false ? text : nil
    }
}

public enum TicketError: Error, Equatable, Sendable {
    case titleRequired
    case storage
    case notRegistered
    case server(String)

    public var message: String {
        switch self {
        case .titleRequired: "Bitte kurz beschreiben, was passiert ist."
        case .storage: "Die Meldung konnte auf diesem Gerät nicht gespeichert werden."
        case .notRegistered: "Dieses Gerät ist noch nicht eingerichtet. Bitte wenden Sie sich direkt an die IT."
        case let .server(message): message
        }
    }
}

/// Meldungen eines angemeldeten Benutzers: lokal zuerst (Outbox `ticket.create`), Anhänge über die Upload-Warteschlange.
public struct HelpdeskService: Sendable {
    public let store: LocalStore
    public let files: FileVault
    public let userId: UUID
    private let now: @Sendable () -> Date

    public init(store: LocalStore, files: FileVault, userId: UUID, now: @escaping @Sendable () -> Date = Date.init) {
        self.store = store
        self.files = files
        self.userId = userId
        self.now = now
    }

    @discardableResult
    public func report(_ ticket: NewTicket, diagnostics: DiagnosticsReport?, attachments: [Data] = [],
                       source: String = "ios") async throws(TicketError) -> UUID {
        guard !ticket.cleanTitle.isEmpty else { throw .titleRequired }
        let photos = BauakteService(store: store, files: files, userId: userId, now: now)
        var attachmentIds: [JSONValue] = []
        do {
            for image in attachments.prefix(5) {
                attachmentIds.append(JSONValue(try await photos.addPhoto(image, to: nil, capturedAt: now())))
            }
        } catch {
            throw .storage
        }
        let id = UUID.v7(now: now())
        let payload: [String: JSONValue] = [
            "title": .string(ticket.cleanTitle), "description": JSONValue(ticket.cleanDescription), "category": .string(ticket.category.rawValue),
            "reporterBlocked": .bool(ticket.reporterBlocked), "source": .string(source),
            "diagnostics": diagnostics.map { .object($0.payload) } ?? .null, "attachmentFileIds": .array(attachmentIds),
        ]
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "number": .null, "status": .string(TicketStatus.new.rawValue), "createdAt": .string(APIDate.format(now())),
        ])
        do {
            try await store.create(.ticket, id: id, data: local, payload: payload)
        } catch {
            throw .storage
        }
        return id
    }

    /// Eigene Meldungen, die noch auf dem Gerät warten oder gerade übertragen wurden.
    public func localReports() async throws -> [(id: UUID, title: String, number: String?, state: SyncState)] {
        try await store.entities(.ticket).map { ($0.id, $0.data["title"]?.string ?? "", $0.data["number"]?.string, $0.syncState) }
    }
}

extension AuthorizedClient {
    public func myTickets() async throws(APIError) -> [TicketSummary] {
        try await get("tickets/mine")
    }

    public func ticket(_ id: UUID) async throws(APIError) -> TicketDetail {
        try await get("tickets/\(id.key)")
    }

    public func comment(on id: UUID, text: String) async throws(APIError) -> TicketDetail {
        try await post("tickets/\(id.key)/comments", body: CommentBody(text: text, isInternal: false))
    }

    public func reopen(_ id: UUID) async throws(APIError) -> TicketDetail {
        try await post("tickets/\(id.key)/reopen")
    }
}

struct CommentBody: Encodable, Sendable {
    let text: String
    let isInternal: Bool
}

/// „Problem melden“ ohne Anmeldung (14, Abschnitt 5): Das eingerichtete Gerät signiert die Meldung.
public enum DeviceTicketReporter {
    public static let proofPrefix = "EstrichONE ticket v1\n"

    /// Gleiche Nachricht wie auf dem Server (`TicketEndpoints.DeviceProofMessage`).
    public static func proofMessage(deviceId: UUID, timestamp: String, ticketId: UUID, title: String, description: String?) -> String {
        let digest = FileVault.sha256(Data((title + "\n" + (description ?? "")).utf8))
        return proofPrefix + deviceId.key + "\n" + timestamp + "\n" + ticketId.key + "\n" + digest
    }

    public static func send(_ ticket: NewTicket, reporterName: String?, deviceId: UUID, keys: DeviceKeys, api: APIClient,
                            diagnostics: DiagnosticsReport?, now: Date = Date()) async throws(TicketError) -> String {
        guard !ticket.cleanTitle.isEmpty else { throw .titleRequired }
        let ticketId = UUID.v7(now: now)
        let timestamp = APIDate.format(now)
        let message = proofMessage(deviceId: deviceId, timestamp: timestamp, ticketId: ticketId, title: ticket.cleanTitle,
                                   description: ticket.cleanDescription)
        let signature: Data
        do {
            signature = try keys.sign(Data(message.utf8))
        } catch {
            throw .notRegistered
        }
        let body = DeviceTicketBody(
            deviceId: deviceId, timestamp: timestamp, signature: signature.base64EncodedString(),
            reporterName: reporterName?.trimmingCharacters(in: .whitespacesAndNewlines),
            ticket: .init(id: ticketId, title: ticket.cleanTitle, description: ticket.cleanDescription, category: ticket.category,
                          source: "ios", reporterBlocked: ticket.reporterBlocked, diagnostics: diagnostics.map { .object($0.payload) }))
        do throws(APIError) {
            let response: DeviceTicketResponse = try await api.post("tickets/device", body: body)
            return response.number
        } catch {
            switch error {
            case .offline: throw .server("Keine Verbindung zum Server. Bitte später erneut senden oder die IT anrufen.")
            case let .server(status, _, _) where status == 401: throw .notRegistered
            case let .server(status, _, _) where status == 429:
                throw .server("Von diesem Gerät wurden gerade mehrere Meldungen gesendet. Bitte in einer Stunde erneut versuchen.")
            default: throw .server(error.message)
            }
        }
    }
}

struct DeviceTicketBody: Encodable, Sendable {
    struct Ticket: Encodable, Sendable {
        let id: UUID
        let title: String
        let description: String?
        let category: TicketCategory
        let source: String
        let reporterBlocked: Bool
        let diagnostics: JSONValue?
    }

    let deviceId: UUID
    let timestamp: String
    let signature: String
    let reporterName: String?
    let ticket: Ticket
}

struct DeviceTicketResponse: Decodable, Sendable {
    let number: String
}
