import Foundation

/// Bereich „Erstbegehung“ der Bauakte (wie der Reiter „Erstbegehung“ der Dokumentation im Büro): Status mit „Geplant“
/// (nächster Einsatz mit Terminart Erstbegehung), Termin, alle Protokolle, Fotos und abgelegte Protokoll-PDFs.
public struct InitialInspectionOverview: Sendable {
    public enum Status: String, Sendable, Equatable {
        case open, planned, done, waived

        /// Gleiche Texte wie im Büro.
        public var title: String {
            switch self {
            case .open: "Offen"
            case .planned: "Geplant"
            case .done: "Durchgeführt"
            case .waived: "Nicht erforderlich"
            }
        }
    }

    public let status: Status
    /// Nächster Einsatz „Erstbegehung“ ab heute.
    public let appointment: Assignment?
    /// Alle Erstbegehungen (auch Korrekturen), neueste zuerst.
    public let protocols: [Inspection]
    /// Fotos der neuesten Erstbegehung.
    public let photoIds: [UUID]

    /// `today` als Kalendertag `yyyy-MM-dd`.
    public init(bauakte: Bauakte, inspections: [Inspection], assignments: [Assignment], today: String) {
        let initial = inspections.filter { $0.kind == .initialInspection }
            .sorted { ($0.performedAt ?? .distantPast) > ($1.performedAt ?? .distantPast) }
        protocols = initial
        photoIds = initial.first?.photoFileIds ?? []
        let next = assignments.filter { $0.bauakteId == bauakte.id && $0.isInitialInspection && $0.day >= today }
            .sorted { ($0.day, $0.startTime ?? "") < ($1.day, $1.startTime ?? "") }.first
        appointment = next
        switch InitialInspectionState(bauakte: bauakte, inspections: inspections) {
        case .done: status = .done
        case .waived: status = .waived
        case .open: status = next == nil ? .open : .planned
        }
    }

    /// „Do., 19.11.2026, 08:00 · Kolonne Nord, Max Huber“
    public static func appointmentText(_ assignment: Assignment) -> String {
        var text = assignment.day
        let parser = DateFormatter()
        parser.locale = Locale(identifier: "en_US_POSIX")
        parser.dateFormat = "yyyy-MM-dd"
        if let date = parser.date(from: assignment.day) {
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: "de_DE")
            formatter.dateFormat = "EE, dd.MM.yyyy"
            text = formatter.string(from: date)
        }
        if let start = assignment.startText { text += ", \(start)" }
        var who: [String] = []
        for name in [assignment.crewName].compactMap({ $0 }) + assignment.people.map(\.name) where !who.contains(name) {
            who.append(name)
        }
        return who.isEmpty ? text : "\(text) · \(who.joined(separator: ", "))"
    }

    /// Vom Server abgelegte Erstbegehungsprotokolle (PDF, Ordner „Erstbegehung“, Titel „Erstbegehung“).
    public static func isProtocolDocument(_ file: FileItem) -> Bool {
        file.isDocument && (file.originalName.hasPrefix("Erstbegehungsprotokoll") || (file.caption?.hasPrefix("Erstbegehung") ?? false))
    }
}
