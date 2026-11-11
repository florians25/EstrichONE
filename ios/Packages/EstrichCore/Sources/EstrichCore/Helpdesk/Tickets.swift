import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif

enum SHA256Hex {
    static func digest(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }
}

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

public enum TicketPriority: String, CaseIterable, Identifiable, Codable, Sendable {
    case low, normal, high, urgent

    public var id: String { rawValue }
    public var title: String {
        switch self {
        case .low: "Niedrig"
        case .normal: "Normal"
        case .high: "Hoch"
        case .urgent: "Dringend"
        }
    }
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
    public var reporterDisplayName: String? = nil
    public var assigneeUserId: UUID? = nil
    /// Unterkategorie (im Web gepflegt), z. B. „Drucker“.
    public var subcategoryName: String? = nil
    /// Ampel der Bearbeitung: met, breached, paused, overdue, dueSoon, ok.
    public var slaState: String? = nil
    /// Nächstes Ziel (erste Antwort bzw. Lösung).
    public var dueAt: Date? = nil
    public var dueKind: String? = nil
    public var rating: Int? = nil
    public var groupId: UUID? = nil
    public var groupName: String? = nil
    public var subcategoryId: UUID? = nil
    /// Eingang: selfService, phone, email, inPerson (bei Telefon, E-Mail, vor Ort hat die IT die Meldung aufgenommen).
    public var channel: String? = nil
    public var createdByName: String? = nil

    /// „Telefon, aufgenommen von Anna Becker“ – nur, wenn die IT die Meldung erfasst hat.
    public var intakeText: String? {
        guard let label = TicketChannel.label(channel) else { return nil }
        return createdByName.map { "\(label), aufgenommen von \($0)" } ?? label
    }

    /// Offen = noch nicht gelöst oder geschlossen.
    public var isOpen: Bool { status != .resolved && status != .closed }

    /// Kurzer Hinweis für die Bearbeitung, wenn ein Ziel drängt (sonst `nil`).
    public var slaHint: String? {
        let what = dueKind == "response" ? "Antwort" : "Lösung"
        switch slaState {
        case "overdue": return "\(what) überfällig"
        case "dueSoon": return "\(what) bald fällig"
        case "paused": return "wartet auf Melder"
        default: return nil
        }
    }
}

/// Eingangskanal einer Meldung.
public enum TicketChannel: String, CaseIterable, Identifiable, Sendable {
    case phone, email, inPerson

    public var id: String { rawValue }
    public var title: String {
        switch self {
        case .phone: "Telefon"
        case .email: "E-Mail"
        case .inPerson: "Vor Ort"
        }
    }

    public var symbol: String {
        switch self {
        case .phone: "phone"
        case .email: "envelope"
        case .inPerson: "person"
        }
    }

    /// Lesbarer Kanal; `nil` bei „selbst gemeldet“ oder unbekannt.
    public static func label(_ raw: String?) -> String? { raw.flatMap(TicketChannel.init(rawValue:))?.title }
}

/// Verknüpfte Anleitung der Wissensdatenbank.
public struct TicketArticleRef: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let title: String
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
        timeline = try? container.decodeIfPresent([TicketTimelineEntry].self, forKey: .timeline)
        articles = try? container.decodeIfPresent([TicketArticleRef].self, forKey: .articles)
        mergedIntoNumber = try container.decodeIfPresent(String.self, forKey: .mergedIntoNumber)
        rating = try container.decodeIfPresent(Int.self, forKey: .rating)
        ratingComment = try container.decodeIfPresent(String.self, forKey: .ratingComment)
        canRate = try container.decodeIfPresent(Bool.self, forKey: .canRate)
    }

    private enum CodingKeys: String, CodingKey {
        case summary, description, resolution, comments, events, timeline, articles, mergedIntoNumber, rating, ratingComment, canRate
    }

    /// Anleitungen, die die IT mit dem Ticket verknüpft hat (`nil` bei älteren Servern, siehe ``linkedArticles``).
    public let articles: [TicketArticleRef]?
    /// Doppelt gemeldet: Die Bearbeitung läuft in diesem Ticket weiter.
    public let mergedIntoNumber: String?
    /// Bewertung des Melders nach der Lösung (1–5).
    public let rating: Int?
    public let ratingComment: String?
    /// Der Abrufende ist Melder, das Ticket ist gelöst und Bewertungen sind eingeschaltet (`nil` bei älteren Servern).
    public let canRate: Bool?

    public var linkedArticles: [TicketArticleRef] { articles ?? [] }
    public var isRatable: Bool { canRate ?? false }

    /// Verlauf des Servers (fertige deutsche Sätze); `nil` bei älteren Servern.
    public let timeline: [TicketTimelineEntry]?

    /// Verlauf für die Anzeige, älteste zuerst, ohne interne Notizen: vom Server oder aus Antworten und Ereignissen.
    public var history: [TicketTimelineEntry] { history(includeInternal: false) }

    /// Verlauf; die Bearbeitung sieht auch interne Notizen.
    public func history(includeInternal: Bool) -> [TicketTimelineEntry] {
        if let timeline {
            return timeline.filter { includeInternal || !$0.isInternal }.sorted { $0.at < $1.at }
        }
        let fromComments = comments.filter { includeInternal || !$0.isInternal }.map {
            TicketTimelineEntry(id: $0.id, at: $0.createdAt, kind: "comment", actorUserId: $0.authorUserId, actorName: $0.authorName,
                                summary: "\($0.authorName ?? "Jemand") hat geantwortet.", text: $0.text, isInternal: $0.isInternal, isReporter: false,
                                attachmentFileIds: [])
        }
        let fromEvents = events.map {
            TicketTimelineEntry(id: UUID(), at: $0.at, kind: $0.kind, actorUserId: nil, actorName: $0.actorName, summary: $0.text,
                                text: nil, isInternal: false, isReporter: false, attachmentFileIds: [])
        }
        return (fromComments + fromEvents).sorted { $0.at < $1.at }
    }
}

/// Eintrag im Ticketverlauf: Antwort (mit `text`) oder Ereignis (nur `summary`).
public struct TicketTimelineEntry: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let at: Date
    public let kind: String
    public let actorUserId: UUID?
    public let actorName: String?
    public let summary: String
    public let text: String?
    public let isInternal: Bool
    public let isReporter: Bool
    public let attachmentFileIds: [UUID]

    public init(id: UUID, at: Date, kind: String, actorUserId: UUID?, actorName: String?, summary: String, text: String?,
                isInternal: Bool, isReporter: Bool, attachmentFileIds: [UUID]) {
        self.id = id
        self.at = at
        self.kind = kind
        self.actorUserId = actorUserId
        self.actorName = actorName
        self.summary = summary
        self.text = text
        self.isInternal = isInternal
        self.isReporter = isReporter
        self.attachmentFileIds = attachmentFileIds
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(UUID.self, forKey: .id)
        at = try container.decode(Date.self, forKey: .at)
        kind = try container.decode(String.self, forKey: .kind)
        actorUserId = try container.decodeIfPresent(UUID.self, forKey: .actorUserId)
        actorName = try container.decodeIfPresent(String.self, forKey: .actorName)
        summary = try container.decodeIfPresent(String.self, forKey: .summary) ?? ""
        text = try container.decodeIfPresent(String.self, forKey: .text)
        isInternal = try container.decodeIfPresent(Bool.self, forKey: .isInternal) ?? false
        isReporter = try container.decodeIfPresent(Bool.self, forKey: .isReporter) ?? false
        attachmentFileIds = try container.decodeIfPresent([UUID].self, forKey: .attachmentFileIds) ?? []
    }

    private enum CodingKeys: String, CodingKey {
        case id, at, kind, actorUserId, actorName, summary, text, isInternal, isReporter, attachmentFileIds
    }

    /// Antwort (Sprechblase) statt Ereigniszeile.
    public var isComment: Bool { text?.isEmpty == false }
}

/// Eingaben von „Problem melden“. Pflicht ist nur der Titel (14, Abschnitt 2).
public struct NewTicket: Sendable {
    public var title: String
    public var description: String?
    public var category: TicketCategory
    /// „Dringend – ich kann nicht arbeiten“.
    public var reporterBlocked: Bool
    public var priority: TicketPriority?
    /// Unterkategorie aus `helpdesk/options` (optional).
    public var subcategoryId: UUID?

    public init(title: String, description: String? = nil, category: TicketCategory = .other, reporterBlocked: Bool = false,
                priority: TicketPriority? = nil, subcategoryId: UUID? = nil) {
        self.title = title
        self.description = description
        self.category = category
        self.reporterBlocked = reporterBlocked
        self.priority = priority
        self.subcategoryId = subcategoryId
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
            "priority": JSONValue((ticket.priority ?? (ticket.reporterBlocked ? .urgent : nil))?.rawValue),
            "diagnostics": diagnostics.map { .object($0.payload) } ?? .null, "attachmentFileIds": .array(attachmentIds),
            "subcategoryId": JSONValue(ticket.subcategoryId),
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

    public func comment(on id: UUID, text: String, attachmentFileIds: [UUID] = [], isInternal: Bool = false) async throws(APIError) -> TicketDetail {
        try await post("tickets/\(id.key)/comments", body: CommentBody(text: text, isInternal: isInternal, attachmentFileIds: attachmentFileIds))
    }

    /// Tickets für die Bearbeitung (Recht `helpdesk.agent`): `open`, `mine`, `unassigned`, `all`.
    public func agentTickets(filter: String = "open") async throws(APIError) -> [TicketSummary] {
        try await get("tickets", query: [URLQueryItem(name: "filter", value: filter)])
    }

    /// Status, Priorität, Zuweisung, Lösung ändern (Bearbeitung).
    public func updateTicket(_ id: UUID, _ change: TicketUpdate) async throws(APIError) -> TicketDetail {
        let body: Data
        do { body = try JSONCoding.encoder.encode(change) } catch { throw .invalidResponse }
        return try await send(APIRequest(method: "PATCH", path: "tickets/\(id.key)", body: body, contentType: "application/json"))
    }

    /// Bild als eigene Datei ohne Bauakte hochladen (für Antworten mit Anhang): ankündigen, Inhalt senden.
    public func uploadLooseImage(_ jpeg: Data, name: String = "Foto.jpg", now: Date = Date()) async throws(APIError) -> UUID {
        let id = UUID.v7(now: now)
        let announce = AnnounceLooseFile(id: id, bauakteId: nil, kind: "photo", originalName: name, contentType: "image/jpeg",
                                         sizeBytes: Int64(jpeg.count), sha256: SHA256Hex.digest(jpeg), capturedAt: now)
        let _: JSONValue = try await post("files", body: announce)
        let _: JSONValue = try await send(APIRequest(method: "PUT", path: "files/\(id.key)/content", body: jpeg, contentType: "image/jpeg"))
        return id
    }
}

/// Änderung eines Tickets durch die Bearbeitung (`UpdateTicketRequest`); `nil` = unverändert.
public struct TicketUpdate: Encodable, Sendable {
    public var status: TicketStatus?
    public var priority: TicketPriority?
    public var category: TicketCategory?
    public var assigneeUserId: UUID?
    public var unassign: Bool
    public var resolution: String?
    public var groupId: UUID?
    public var clearGroup: Bool
    public var subcategoryId: UUID?
    public var clearSubcategory: Bool

    public init(status: TicketStatus? = nil, priority: TicketPriority? = nil, category: TicketCategory? = nil, assigneeUserId: UUID? = nil,
                unassign: Bool = false, resolution: String? = nil, groupId: UUID? = nil, clearGroup: Bool = false, subcategoryId: UUID? = nil,
                clearSubcategory: Bool = false) {
        self.status = status
        self.priority = priority
        self.category = category
        self.assigneeUserId = assigneeUserId
        self.unassign = unassign
        self.resolution = resolution
        self.groupId = groupId
        self.clearGroup = clearGroup
        self.subcategoryId = subcategoryId
        self.clearSubcategory = clearSubcategory
    }
}

/// Bearbeiter (Zuweisen) mit Anzahl offener Tickets.
public struct HelpdeskAgent: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let displayName: String
    public var openTickets: Int? = nil
}

/// Bearbeitungsgruppe (z. B. IT, Büro).
public struct HelpdeskGroup: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let name: String
    public var isActive: Bool? = nil
}

/// Textbaustein der Bearbeitung (gemeinsam oder eigener).
public struct CannedResponse: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let name: String
    public let text: String
    public var isPersonal: Bool? = nil

    /// Platzhalter wie im Web füllen: {Melder}, {Vorname}, {Ticket}, {Titel}, {Bearbeiter}.
    public static func fill(_ text: String, reporter: String?, ticket: String, title: String, agent: String?) -> String {
        var name = (reporter ?? "").trimmingCharacters(in: .whitespaces)
        if name.hasSuffix(" (laut Eingabe)") { name = String(name.dropLast(" (laut Eingabe)".count)) }
        let first = name.split(separator: " ").first.map(String.init) ?? ""
        return text.replacingOccurrences(of: "{Melder}", with: name.isEmpty ? "zusammen" : name)
            .replacingOccurrences(of: "{Vorname}", with: first.isEmpty ? "zusammen" : first)
            .replacingOccurrences(of: "{Ticket}", with: ticket)
            .replacingOccurrences(of: "{Titel}", with: title)
            .replacingOccurrences(of: "{Bearbeiter}", with: agent ?? "Ihre IT")
    }
}

/// Person, für die die IT eine Meldung aufnimmt (`GET helpdesk/reporters`).
public struct HelpdeskReporterPerson: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let displayName: String
    public var userName: String? = nil
    public var email: String? = nil
    public var phone: String? = nil
}

/// Anleitung der Wissensdatenbank.
public struct KnowledgeArticle: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let title: String
    public let body: String
    public var category: TicketCategory? = nil

    /// Absätze für die Anzeige (Leerzeilen trennen).
    public var paragraphs: [String] {
        body.components(separatedBy: "\n\n").map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }
    }
}

/// „Meldung aufnehmen“: Die IT legt nach Anruf, E-Mail oder Besuch ein Ticket im Namen des Melders an.
public struct TicketIntake: Encodable, Sendable {
    public let id: UUID
    public let title: String
    public let description: String?
    public let category: TicketCategory
    public let channel: String
    public let source: String
    public let reporterUserId: UUID?
    public let reporterName: String?
    public let reporterBlocked: Bool
    public let priority: TicketPriority
    public let subcategoryId: UUID?
    public let assigneeUserId: UUID?

    public init(title: String, description: String?, category: TicketCategory, channel: TicketChannel, reporterUserId: UUID?, reporterName: String?,
                priority: TicketPriority = .normal, reporterBlocked: Bool = false, subcategoryId: UUID? = nil, assigneeUserId: UUID? = nil,
                now: Date = Date()) {
        id = UUID.v7(now: now)
        self.title = title.trimmingCharacters(in: .whitespacesAndNewlines)
        let text = description?.trimmingCharacters(in: .whitespacesAndNewlines)
        self.description = text?.isEmpty == false ? text : nil
        self.category = category
        self.channel = channel.rawValue
        source = "ios"
        self.reporterUserId = reporterUserId
        let name = reporterName?.trimmingCharacters(in: .whitespacesAndNewlines)
        self.reporterName = reporterUserId == nil && name?.isEmpty == false ? name : nil
        self.reporterBlocked = reporterBlocked
        self.priority = priority
        self.subcategoryId = subcategoryId
        self.assigneeUserId = assigneeUserId
    }
}

extension AuthorizedClient {
    public func helpdeskAgents() async throws(APIError) -> [HelpdeskAgent] {
        try await get("helpdesk/agents")
    }

    public func helpdeskGroups() async throws(APIError) -> [HelpdeskGroup] {
        let groups: [HelpdeskGroup] = try await get("helpdesk/groups")
        return groups.filter { $0.isActive ?? true }
    }

    public func cannedResponses() async throws(APIError) -> [CannedResponse] {
        try await get("helpdesk/responses")
    }

    /// Melder suchen (Name, Benutzername, E-Mail, Telefon).
    public func helpdeskReporters(query: String) async throws(APIError) -> [HelpdeskReporterPerson] {
        let text = query.trimmingCharacters(in: .whitespaces)
        return try await get("helpdesk/reporters", query: [URLQueryItem(name: "limit", value: "30")] + (text.isEmpty ? [] : [URLQueryItem(name: "q", value: text)]))
    }

    /// Meldung im Namen eines Melders anlegen (Telefon, E-Mail, vor Ort).
    public func recordTicket(_ intake: TicketIntake) async throws(APIError) -> TicketDetail {
        try await post("tickets/on-behalf", body: intake)
    }

    public func knowledgeArticle(_ id: UUID) async throws(APIError) -> KnowledgeArticle {
        try await get("helpdesk/articles/\(id.key)")
    }
}

struct AnnounceLooseFile: Encodable, Sendable {
    let id: UUID
    let bauakteId: UUID?
    let kind: String
    let originalName: String
    let contentType: String
    let sizeBytes: Int64
    let sha256: String
    let capturedAt: Date
}

/// Gelesen-Stand der eigenen Meldungen: Neue Antworten der IT erkennt die App am Zeitpunkt der letzten Aktivität.
public struct TicketReadState: Sendable {
    public let store: LocalStore

    public init(store: LocalStore) { self.store = store }

    private func seen() async -> [String: Date] {
        guard let text = try? await store.meta("ticket_seen"), let data = text.data(using: .utf8) else { return [:] }
        return (try? JSONCoding.decoder.decode([String: Date].self, from: data)) ?? [:]
    }

    /// Tickets mit Aktivität nach dem letzten Öffnen (beim ersten Laden gilt alles als gelesen).
    public func unread(_ tickets: [TicketSummary]) async -> Set<UUID> {
        let seen = await seen()
        if seen.isEmpty {
            await markRead(tickets)
            return []
        }
        return Set(tickets.filter { ticket in
            guard let last = seen[ticket.id.key] else { return ticket.status == .waitingForReporter }
            return ticket.lastActivityAt > last.addingTimeInterval(1)
        }.map(\.id))
    }

    public func markRead(_ tickets: [TicketSummary]) async {
        var seen = await seen()
        for ticket in tickets { seen[ticket.id.key] = max(seen[ticket.id.key] ?? .distantPast, ticket.lastActivityAt) }
        if seen.isEmpty { seen["_"] = Date() }
        if let data = try? JSONCoding.encoder.encode(seen) {
            try? await store.setMeta("ticket_seen", String(decoding: data, as: UTF8.self))
        }
    }
}

extension AuthorizedClient {
    public func reopen(_ id: UUID) async throws(APIError) -> TicketDetail {
        try await post("tickets/\(id.key)/reopen")
    }

    /// Zufriedenheit nach der Lösung (Melder, 1–5 Sterne).
    public func rate(_ id: UUID, rating: Int, comment: String?) async throws(APIError) -> TicketDetail {
        try await post("tickets/\(id.key)/rating", body: TicketRating(rating: min(max(rating, 1), 5), comment: comment))
    }

    /// Kategorien mit Unterkategorien zum Melden.
    public func helpdeskOptions() async throws(APIError) -> HelpdeskOptions {
        try await get("helpdesk/options")
    }
}

struct TicketRating: Encodable, Sendable {
    let rating: Int
    let comment: String?

    init(rating: Int, comment: String?) {
        self.rating = rating
        let text = comment?.trimmingCharacters(in: .whitespacesAndNewlines)
        self.comment = text?.isEmpty == false ? text : nil
    }
}

public struct HelpdeskSubcategory: Decodable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let category: TicketCategory
    public let name: String
}

/// Was die App zum Melden braucht (`GET helpdesk/options`).
public struct HelpdeskOptions: Decodable, Equatable, Sendable {
    public struct Category: Decodable, Equatable, Sendable {
        public let key: TicketCategory
        public let name: String
        public let subcategories: [HelpdeskSubcategory]
    }

    public let categories: [Category]
    public let ratingEnabled: Bool

    public func subcategories(of category: TicketCategory) -> [HelpdeskSubcategory] {
        categories.first { $0.key == category }?.subcategories ?? []
    }
}

struct CommentBody: Encodable, Sendable {
    let text: String
    let isInternal: Bool
    var attachmentFileIds: [UUID] = []
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
