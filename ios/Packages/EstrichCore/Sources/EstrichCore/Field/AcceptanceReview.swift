import Foundation

/// Zusammenfassung eines Abnahmeprotokolls vor dem Abschluss: was im Protokoll steht (so sieht es auch der Kunde),
/// was noch fehlt und worauf hingewiesen wird. Pflicht sind das Ergebnis und bei Vorbehalt oder Verweigerung mindestens
/// ein Mangel oder eine Notiz; alles Übrige sind Hinweise, mit denen das Protokoll trotzdem gesichert werden kann.
public struct AcceptanceReview: Equatable, Sendable {
    public struct Line: Equatable, Sendable, Identifiable {
        public let title: String
        public let value: String
        public var id: String { title }
    }

    public struct Defect: Equatable, Sendable, Identifiable {
        public let id: UUID
        public let title: String
        /// „bis 03.12.2026“ oder `nil` ohne Frist.
        public let due: String?
    }

    /// Angaben des Protokolls in der Reihenfolge des Dokuments.
    public let lines: [Line]
    public let defects: [Defect]
    public let notes: String?
    public let photoCount: Int
    /// Pflichtangaben, die fehlen: Das Protokoll kann so nicht gesichert werden.
    public let missing: [String]
    /// Hinweise: sinnvoll, aber nicht zwingend.
    public let hints: [String]

    public var canFinish: Bool { missing.isEmpty }

    public init(_ input: NewAcceptance, bauakteTitle: String, bauakteNumber: String?) {
        func clean(_ text: String?) -> String? {
            let trimmed = text?.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed?.isEmpty == false ? trimmed : nil
        }
        var lines = [Line(title: "Bauvorhaben", value: [bauakteNumber, bauakteTitle].compactMap { $0 }.joined(separator: ", "))]
        if let location = clean(input.location) { lines.append(Line(title: "Ort", value: location)) }
        if let scope = clean(input.scope) { lines.append(Line(title: "Abgenommene Leistung", value: scope)) }
        if let result = input.result { lines.append(Line(title: "Ergebnis", value: result.title)) }
        if let client = clean(input.clientName) { lines.append(Line(title: "Auftraggeber", value: client)) }
        if let contractor = clean(input.contractorName) { lines.append(Line(title: "Auftragnehmer", value: contractor)) }
        let defects: [Defect] = input.defects.compactMap { defect in
            guard let title = clean(defect.title) else { return nil }
            return Defect(id: defect.id, title: title, due: defect.dueDate.flatMap(Self.dueText))
        }
        let notes = clean(input.notes)

        var missing: [String] = []
        if input.result == nil { missing.append("Ergebnis der Abnahme wählen") }
        if let result = input.result, result != .accepted, defects.isEmpty, notes == nil {
            missing.append("Bei Vorbehalt oder Verweigerung mindestens einen Mangel oder eine Notiz erfassen")
        }

        var hints: [String] = []
        if clean(input.scope) == nil { hints.append("Die abgenommene Leistung ist nicht beschrieben.") }
        if clean(input.clientName) == nil { hints.append("Der Name des Auftraggebers fehlt.") }
        if input.clientSignature == nil {
            hints.append("Ohne Unterschrift des Auftraggebers bleibt das Protokoll offen. Er kann später über einen Freigabelink zustimmen.")
        }
        if input.contractorSignature == nil { hints.append("Die Unterschrift des Auftragnehmers fehlt.") }

        self.lines = lines
        self.defects = defects
        self.notes = notes
        self.photoCount = input.photoFileIds.count
        self.missing = missing
        self.hints = hints
    }

    /// `2026-12-03` wird zu „bis 03.12.2026“.
    static func dueText(_ day: String) -> String? {
        let parts = day.split(separator: "-")
        guard parts.count == 3 else { return nil }
        return "bis \(parts[2]).\(parts[1]).\(parts[0])"
    }
}
