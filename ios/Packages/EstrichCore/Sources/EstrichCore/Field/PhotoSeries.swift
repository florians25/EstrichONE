import Foundation

/// Art eines Baustellenfotos innerhalb einer Serie.
public enum PhotoPhase: String, CaseIterable, Identifiable, Codable, Sendable {
    case before
    case installation
    case after
    case defect

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .before: "Vorher"
        case .installation: "Einbau"
        case .after: "Nachher"
        case .defect: "Mangel"
        }
    }
}

/// Vorgaben einer Fotoserie: Art, Raum oder Bereich und eine freie Beschriftung. Sie gelten für alle folgenden Fotos,
/// bis sie geändert werden, und stehen als Beschriftung am Foto („Einbau, Bad OG, Randdämmstreifen“). So lassen sich
/// die Fotos im Büro ohne Nacharbeit zuordnen.
public struct PhotoSeries: Codable, Equatable, Sendable {
    public var phase: PhotoPhase?
    public var room: String
    public var note: String

    public init(phase: PhotoPhase? = nil, room: String = "", note: String = "") {
        self.phase = phase
        self.room = room
        self.note = note
    }

    public var isEmpty: Bool { phase == nil && Self.clean(room) == nil && Self.clean(note) == nil }

    /// Beschriftung der Fotos: Art, Raum, Beschriftung, durch Komma getrennt; `nil`, wenn nichts gewählt ist.
    public var caption: String? {
        let parts = [phase?.title, Self.clean(room), Self.clean(note)].compactMap { $0 }
        return parts.isEmpty ? nil : String(parts.joined(separator: ", ").prefix(500))
    }

    /// Art eines Fotos aus seiner Beschriftung (erster Teil vor dem Komma), z. B. zum Filtern.
    public static func phase(ofCaption caption: String?) -> PhotoPhase? {
        guard let first = caption?.split(separator: ",").first.map({ $0.trimmingCharacters(in: .whitespaces) }) else { return nil }
        return PhotoPhase.allCases.first { $0.title == first }
    }

    /// Vorschläge für „Raum oder Bereich“: die Räume der Bauakte (mit Geschoss), ohne Doppelte, in ihrer Reihenfolge.
    public static func roomSuggestions(_ rooms: [(name: String, floor: String?)]) -> [String] {
        var seen = Set<String>()
        var result: [String] = []
        for room in rooms {
            guard let name = clean(room.name) else { continue }
            let floor = clean(room.floor ?? "")
            let title = floor.map { name.localizedCaseInsensitiveContains($0) ? name : "\(name) \($0)" } ?? name
            if seen.insert(title.lowercased()).inserted { result.append(title) }
        }
        return result
    }

    static func clean(_ text: String) -> String? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
}
