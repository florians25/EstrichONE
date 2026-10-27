import Foundation

/// Ergebnis der Abnahme (VOB/B § 12).
public enum AcceptanceResult: String, CaseIterable, Identifiable, Sendable {
    case accepted
    case acceptedWithReservations
    case refused

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .accepted: "Abgenommen"
        case .acceptedWithReservations: "Mit Vorbehalt"
        case .refused: "Verweigert"
        }
    }
}

/// Stand des Protokolls auf dem Server.
public enum AcceptanceState: String, Sendable {
    /// Erfasst, Unterschrift des Auftraggebers fehlt noch.
    case recorded
    /// Freigabelink an den Kunden verschickt.
    case awaitingCustomer
    /// Unterschrieben bzw. freigegeben, PDF in der Bauakte.
    case completed

    public var title: String {
        switch self {
        case .recorded: "Erfasst"
        case .awaitingCustomer: "Freigabe durch Kunden ausstehend"
        case .completed: "Abgeschlossen"
        }
    }
}

/// Abnahmeprotokoll einer Bauakte (Entität `acceptance`).
public struct Acceptance: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let performedByUserId: UUID?
    public let performedAt: Date?
    public let location: String?
    public let clientName: String?
    public let contractorName: String?
    public let scope: String?
    public let result: AcceptanceResult
    public let notes: String?
    public let photoFileIds: [UUID]
    public let defectWorkItemIds: [UUID]
    public let hasClientSignature: Bool
    public let hasContractorSignature: Bool
    /// Vor der Übertragung: lokal `recorded` bzw. `completed`, je nach Unterschriften.
    public let state: AcceptanceState
    public let customerEmail: String?
    public let customerLinkExpiresAt: Date?
    public let customerDecision: String?
    public let customerDecisionName: String?
    public let customerComment: String?
    /// PDF des Protokolls in der Bauakte (Kategorie „Abnahme“), sobald abgeschlossen.
    public let pdfFileId: UUID?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        func uuid(_ key: String) -> UUID? { data[key]?.string.flatMap(UUID.init(uuidString:)) }
        func uuids(_ key: String) -> [UUID] { (data[key]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) } }
        id = entity.id
        bauakteId = uuid("bauakteId")
        performedByUserId = uuid("performedByUserId")
        performedAt = data["performedAt"]?.string.flatMap(APIDate.parse)
        location = data["location"]?.string
        clientName = data["clientName"]?.string
        contractorName = data["contractorName"]?.string
        scope = data["scope"]?.string
        result = data["result"]?.string.flatMap(AcceptanceResult.init(rawValue:)) ?? .accepted
        notes = data["notes"]?.string
        photoFileIds = uuids("photoFileIds")
        defectWorkItemIds = uuids("defectWorkItemIds")
        // Lokal angelegt: Unterschriften liegen als Base64 im Datensatz; vom Server nur als Kennzeichen.
        hasClientSignature = data["hasClientSignature"]?.bool ?? (data["clientSignature"]?["png"]?.string != nil)
        hasContractorSignature = data["hasContractorSignature"]?.bool ?? (data["contractorSignature"]?["png"]?.string != nil)
        state = data["state"]?.string.flatMap(AcceptanceState.init(rawValue:)) ?? .recorded
        customerEmail = data["customerEmail"]?.string
        customerLinkExpiresAt = data["customerLinkExpiresAt"]?.string.flatMap(APIDate.parse)
        customerDecision = data["customerDecision"]?.string
        customerDecisionName = data["customerDecisionName"]?.string
        customerComment = data["customerComment"]?.string
        pdfFileId = uuid("pdfFileId")
        syncState = entity.syncState
    }

    /// Unterschrift beider Seiten vorhanden – das Protokoll kann ohne Kundenlink abgeschlossen werden.
    public var isSigned: Bool { hasClientSignature && hasContractorSignature }
}

/// Mangel, der mit der Abnahme als Aufgabe (`workitem.create`, Art „Mangel“) mit Frist angelegt wird.
public struct AcceptanceDefect: Identifiable, Equatable, Sendable {
    public let id: UUID
    public var title: String
    /// Frist im Format `JJJJ-MM-TT`.
    public var dueDate: String?

    public init(id: UUID = UUID(), title: String, dueDate: String? = nil) {
        self.id = id
        self.title = title
        self.dueDate = dueDate
    }
}

/// Eingaben für ein neues Abnahmeprotokoll.
public struct NewAcceptance: Sendable {
    public var location: String?
    public var clientName: String?
    public var contractorName: String?
    public var scope: String?
    public var result: AcceptanceResult?
    public var notes: String?
    public var photoFileIds: [UUID]
    public var defects: [AcceptanceDefect]
    /// Unterschriften als PNG.
    public var clientSignature: Data?
    public var contractorSignature: Data?

    public init(location: String? = nil, clientName: String? = nil, contractorName: String? = nil, scope: String? = nil,
                result: AcceptanceResult? = nil, notes: String? = nil, photoFileIds: [UUID] = [], defects: [AcceptanceDefect] = [],
                clientSignature: Data? = nil, contractorSignature: Data? = nil) {
        self.location = location
        self.clientName = clientName
        self.contractorName = contractorName
        self.scope = scope
        self.result = result
        self.notes = notes
        self.photoFileIds = photoFileIds
        self.defects = defects
        self.clientSignature = clientSignature
        self.contractorSignature = contractorSignature
    }

    /// Größte zulässige Unterschrift (wie der Server).
    public static let maxSignatureBytes = 300_000

    public func validate() throws(AcceptanceError) {
        guard let result else { throw .resultRequired }
        if result != .accepted, defects.allSatisfy({ $0.title.trimmingCharacters(in: .whitespaces).isEmpty }),
           (notes ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            throw .reservationsRequired
        }
        for signature in [clientSignature, contractorSignature].compactMap({ $0 }) {
            guard signature.count <= Self.maxSignatureBytes, signature.starts(with: [0x89, 0x50, 0x4E, 0x47]) else { throw .invalidSignature }
        }
    }
}

public enum AcceptanceError: Error, Equatable, Sendable {
    case resultRequired
    case reservationsRequired
    case invalidSignature
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
        case .resultRequired: "Bitte das Ergebnis der Abnahme wählen."
        case .reservationsRequired: "Bei Vorbehalt oder Verweigerung bitte mindestens einen Mangel oder eine Notiz erfassen."
        case .invalidSignature: "Die Unterschrift ist zu groß oder kein PNG-Bild."
        case .storage: "Das Protokoll konnte auf diesem Gerät nicht gespeichert werden."
        }
    }
}

/// Antwort auf `POST /acceptances/{id}/customer-link`.
public struct AcceptanceLink: Decodable, Equatable, Sendable {
    public let url: String
    public let expiresAt: Date
    public let emailed: Bool
}

extension BauakteService {
    public func acceptances(of bauakteId: UUID) async throws -> [Acceptance] {
        try await store.entities(.acceptance, parent: bauakteId).map(Acceptance.init)
            .sorted { ($0.performedAt ?? .distantPast) > ($1.performedAt ?? .distantPast) }
    }

    public func acceptance(_ id: UUID) async throws -> Acceptance? {
        try await store.entity(.acceptance, id).map(Acceptance.init)
    }

    /// Legt das Protokoll lokal an: zuerst die Mängel als Aufgaben mit Frist, danach `acceptance.create` mit deren IDs –
    /// in dieser Reihenfolge überträgt die Synchronisation auch.
    @discardableResult
    public func addAcceptance(_ input: NewAcceptance, to bauakteId: UUID) async throws(AcceptanceError) -> UUID {
        try input.validate()
        var defectIds: [UUID] = []
        for defect in input.defects where !defect.title.trimmingCharacters(in: .whitespaces).isEmpty {
            do throws(BauakteError) {
                defectIds.append(try await addWorkItem(NewWorkItem(kind: .defect, title: defect.title, dueDate: defect.dueDate, bauakteId: bauakteId)))
            } catch {
                throw .from(error)
            }
        }
        func text(_ value: String?) -> JSONValue {
            let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines)
            return JSONValue(trimmed?.isEmpty == false ? trimmed : nil)
        }
        let timestamp = APIDate.format(currentDate())
        func signature(_ png: Data?) -> JSONValue {
            guard let png else { return .null }
            return .object(["png": .string(png.base64EncodedString()), "signedAt": .string(timestamp)])
        }
        let id = UUID.v7(now: currentDate())
        let payload: [String: JSONValue] = [
            "bauakteId": JSONValue(bauakteId), "performedAt": .string(timestamp), "location": text(input.location),
            "clientName": text(input.clientName), "contractorName": text(input.contractorName), "scope": text(input.scope),
            "roomIds": .array([]), "result": .string(input.result?.rawValue ?? AcceptanceResult.accepted.rawValue),
            "notes": JSONValue(Self.cleanNotes(input.notes)), "photoFileIds": .array(input.photoFileIds.map { JSONValue($0) }),
            "defectWorkItemIds": .array(defectIds.map { JSONValue($0) }),
            "clientSignature": signature(input.clientSignature), "contractorSignature": signature(input.contractorSignature),
        ]
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "performedByUserId": JSONValue(userId), "state": .string(AcceptanceState.recorded.rawValue),
            "hasClientSignature": .bool(input.clientSignature != nil), "hasContractorSignature": .bool(input.contractorSignature != nil),
            "version": 0,
        ])
        do {
            try await store.create(.acceptance, id: id, data: local, payload: payload)
        } catch {
            throw .from(error)
        }
        return id
    }
}

private struct CustomerLinkRequest: Encodable, Sendable {
    let email: String?
}

extension AuthorizedClient {
    /// Freigabelink für den Kunden (ohne Anmeldung, befristet); mit E-Mail-Adresse verschickt ihn der Server.
    public func customerLink(for acceptanceId: UUID, email: String?) async throws(APIError) -> AcceptanceLink {
        let trimmed = email?.trimmingCharacters(in: .whitespaces)
        return try await post("acceptances/\(acceptanceId.key)/customer-link",
                              body: CustomerLinkRequest(email: trimmed?.isEmpty == false ? trimmed : nil))
    }
}
