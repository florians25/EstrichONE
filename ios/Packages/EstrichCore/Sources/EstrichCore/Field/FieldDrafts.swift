import Foundation

/// Entwürfe der Erfassung auf der Baustelle (Bautagebuch, Mangel, Notiz, Abnahme, CM-Messung): Die Eingaben werden
/// laufend verschlüsselt in der Datenbank des Geräts abgelegt und nach einem Abbruch oder Neustart der App wieder
/// angeboten. Ein Entwurf gehört zu einer Art und einer Bauakte; nach dem Sichern oder Verwerfen wird er entfernt.
public struct DraftStore: Sendable {
    /// Entwürfe gelten zwei Wochen; ältere werden nicht mehr angeboten.
    public static let standardMaxAge: TimeInterval = 14 * 86_400

    private let store: LocalStore
    private let now: @Sendable () -> Date

    public init(store: LocalStore, now: @escaping @Sendable () -> Date = Date.init) {
        self.store = store
        self.now = now
    }

    /// Schlüssel in der Tabelle `meta`.
    static func key(kind: String, scope: UUID?) -> String {
        "draft.\(kind).\(scope?.uuidString.lowercased() ?? "ohne")"
    }

    private struct Envelope<Value: Codable>: Codable {
        let savedAt: Date
        let value: Value
    }

    /// Entwurf ablegen (ersetzt den vorigen derselben Art und Bauakte).
    public func save<Value: Codable & Sendable>(_ value: Value, kind: String, scope: UUID?) async {
        guard let data = try? JSONCoding.encoder.encode(Envelope(savedAt: now(), value: value)) else { return }
        try? await store.setMeta(Self.key(kind: kind, scope: scope), String(decoding: data, as: UTF8.self))
    }

    /// Gespeicherter Entwurf mit Zeitpunkt; `nil`, wenn keiner da ist, er zu alt ist oder nicht mehr lesbar.
    public func load<Value: Codable & Sendable>(_ type: Value.Type, kind: String, scope: UUID?,
                                                maxAge: TimeInterval = DraftStore.standardMaxAge) async -> (value: Value, savedAt: Date)? {
        guard let text = try? await store.meta(Self.key(kind: kind, scope: scope)), !text.isEmpty,
              let envelope = try? JSONCoding.decoder.decode(Envelope<Value>.self, from: Data(text.utf8)),
              now().timeIntervalSince(envelope.savedAt) <= maxAge else { return nil }
        return (envelope.value, envelope.savedAt)
    }

    /// Entwurf entfernen (nach dem Sichern oder wenn er verworfen wurde).
    public func discard(kind: String, scope: UUID?) async {
        try? await store.setMeta(Self.key(kind: kind, scope: scope), "")
    }
}

/// Entwurf eines Formulars: speicherbar, vergleichbar und mit der Auskunft, ob überhaupt etwas eingegeben wurde.
public protocol FieldDraft: Codable, Equatable, Sendable {
    /// Leere Entwürfe werden nicht aufbewahrt.
    var isEmpty: Bool { get }
}

/// Arten der Entwürfe (ein Wert je Formular).
public enum DraftKind {
    public static let diary = "diary"
    public static let workItem = "workitem"
    public static let note = "note"
    public static let acceptance = "acceptance"
    public static let cmMeasurement = "cm"
    public static let photoSeries = "photoseries"
    public static let inspection = "inspection"
}

/// Entwurf einer Erstbegehung nach dem Schema des Büros: bewertete Prüfpunkte, Bemerkungen, Messwerte, Fotos und Notizen.
public struct InspectionDraft: FieldDraft {
    public var notRequired: Bool
    public var reason: String
    /// Ergebnis je Prüfpunkt (`ok`, `notOk`, `notApplicable`).
    public var checks: [String: String]
    public var remarks: [String: String]
    public var values: [String: JSONValue]
    public var checkPhotos: [String: [UUID]]
    public var photos: [UUID]
    public var notes: String

    public init(notRequired: Bool, reason: String, checks: [String: String], remarks: [String: String], values: [String: JSONValue],
                checkPhotos: [String: [UUID]], photos: [UUID], notes: String) {
        self.notRequired = notRequired
        self.reason = reason
        self.checks = checks
        self.remarks = remarks
        self.values = values
        self.checkPhotos = checkPhotos
        self.photos = photos
        self.notes = notes
    }

    public var isEmpty: Bool {
        !notRequired && checks.isEmpty && values.isEmpty && photos.isEmpty && checkPhotos.values.allSatisfy(\.isEmpty)
            && ([reason, notes] + remarks.values).allSatisfy { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }
}

/// Entwurf eines Bautagebuch-Eintrags.
public struct DiaryDraft: FieldDraft {
    public var day: String
    public var text: String
    public var weather: String?
    public var temperature: Int?
    public var workers: Int

    public init(day: String, text: String, weather: String? = nil, temperature: Int? = nil, workers: Int = 0) {
        self.day = day
        self.text = text
        self.weather = weather
        self.temperature = temperature
        self.workers = workers
    }

    /// Leere Entwürfe werden nicht aufbewahrt.
    public var isEmpty: Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
}

/// Entwurf einer Aufgabe oder eines Mangels.
public struct WorkItemDraft: FieldDraft {
    public var kind: String
    public var title: String
    public var details: String

    public init(kind: String, title: String, details: String) {
        self.kind = kind
        self.title = title
        self.details = details
    }

    public var isEmpty: Bool {
        title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && details.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }
}

/// Entwurf einer Notiz.
public struct NoteDraft: FieldDraft {
    public var text: String

    public init(text: String) {
        self.text = text
    }

    public var isEmpty: Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
}

/// Entwurf einer CM-Messung.
public struct CmDraft: FieldDraft {
    public var location: String
    public var binder: String
    public var heated: Bool
    public var thickness: Int
    public var measured: String
    public var remarks: String

    public init(location: String, binder: String, heated: Bool, thickness: Int, measured: String, remarks: String) {
        self.location = location
        self.binder = binder
        self.heated = heated
        self.thickness = thickness
        self.measured = measured
        self.remarks = remarks
    }

    public var isEmpty: Bool {
        [location, measured, remarks].allSatisfy { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }
}

/// Entwurf eines Abnahmeprotokolls, einschließlich der schon geleisteten Unterschriften.
public struct AcceptanceDraft: FieldDraft {
    public struct Defect: Codable, Equatable, Sendable {
        public var title: String
        public var dueDate: String?

        public init(title: String, dueDate: String?) {
            self.title = title
            self.dueDate = dueDate
        }
    }

    public var clientName: String
    public var contractorName: String
    public var location: String
    public var scope: String
    public var result: String?
    public var notes: String
    public var defects: [Defect]
    public var photoIds: [UUID]
    public var clientSignature: Data?
    public var contractorSignature: Data?

    public init(clientName: String, contractorName: String, location: String, scope: String, result: String?, notes: String,
                defects: [Defect], photoIds: [UUID], clientSignature: Data?, contractorSignature: Data?) {
        self.clientName = clientName
        self.contractorName = contractorName
        self.location = location
        self.scope = scope
        self.result = result
        self.notes = notes
        self.defects = defects
        self.photoIds = photoIds
        self.clientSignature = clientSignature
        self.contractorSignature = contractorSignature
    }

    /// Nur Vorbelegtes (Namen, Ort) ist noch kein Entwurf.
    public var isEmpty: Bool {
        result == nil && scope.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && notes.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && defects.allSatisfy { $0.title.trimmingCharacters(in: .whitespaces).isEmpty }
            && photoIds.isEmpty && clientSignature == nil && contractorSignature == nil
    }
}
