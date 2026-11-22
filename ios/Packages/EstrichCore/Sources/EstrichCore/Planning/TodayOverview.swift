import Foundation

/// Tageszusammenfassung oben auf „Heute“: Einsätze und Baustellen, Beginn der ersten Baustelle, Kolonne mit Besetzung,
/// Fahrzeuge und Geräte, Abwesende und die Fläche der heutigen Baustellen. Nur aus Daten, die schon auf dem Gerät liegen
/// (eigene Planung, „Wer fährt wohin“, Basisdaten der Bauakten).
public struct TodayOverview: Equatable, Sendable {
    /// Ein Einsatz des Tages (aus der eigenen Planung bzw. „Wer fährt wohin“).
    public struct Job: Equatable, Sendable {
        public let bauakteId: UUID
        /// „07:00“
        public let start: String?
        public let crewName: String?
        public let people: [String]
        public let resources: [String]

        public init(bauakteId: UUID, start: String?, crewName: String?, people: [String] = [], resources: [String] = []) {
            self.bauakteId = bauakteId
            self.start = start
            self.crewName = crewName
            self.people = people
            self.resources = resources
        }
    }

    public let jobCount: Int
    public let siteCount: Int
    /// Beginn des ersten Einsatzes („07:00“).
    public let firstStart: String?
    public let crewName: String?
    /// Farbe der Kolonne (Hex), falls bekannt.
    public let crewColor: String?
    public let leaderName: String?
    /// Besetzung heute (ohne Abwesende).
    public let people: [String]
    /// Fahrzeuge und Geräte.
    public let vehicles: [String]
    /// Abwesende der Kolonne („Tobias Lang, Urlaub“).
    public let absent: [String]
    /// Summe der Flächen der heutigen Baustellen in m² (nur wenn mindestens eine bekannt ist).
    public let totalArea: Double?

    public var isEmpty: Bool { jobCount == 0 }

    /// „2 Einsätze auf 2 Baustellen“ bzw. „1 Einsatz“.
    public var jobsText: String {
        let jobs = jobCount == 1 ? "1 Einsatz" : "\(jobCount) Einsätze"
        guard siteCount > 1 else { return jobs }
        return "\(jobs) auf \(siteCount) Baustellen"
    }

    /// „240 m²“
    public var areaText: String? { totalArea.map { "\(Self.number($0)) m²" } }

    /// „Kolonne Huber, 4 Personen“
    public var crewText: String? {
        let count = people.isEmpty ? nil : (people.count == 1 ? "1 Person" : "\(people.count) Personen")
        let parts = [crewName, count].compactMap { $0 }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }

    public static func make(jobs: [Job], board: DispatchBoard?, areas: [UUID: Double] = [:]) -> TodayOverview {
        let sites = unique(jobs.map(\.bauakteId))
        let starts = jobs.compactMap(\.start).filter { !$0.isEmpty }.sorted()
        // Kolonne: die häufigste in den Einsätzen, sonst die Kolonne, die heute auf meinen Baustellen ist.
        let named = jobs.compactMap(\.crewName)
        let counts = Dictionary(named.map { ($0, 1) }, uniquingKeysWith: +)
        var crewName: String?
        var best = 0
        for name in unique(named) where (counts[name] ?? 0) > best {
            best = counts[name] ?? 0
            crewName = name
        }
        let mine = Set(board?.mySiteIds ?? []).union(sites)
        let crew = board?.crews.first { $0.name == crewName }
            ?? (crewName == nil ? board?.crews.first { !Set($0.siteIds).isDisjoint(with: mine) } : nil)
        if crewName == nil { crewName = crew?.name }

        let absentIds = Set(crew?.absentMembers.map(\.id) ?? [])
        var people = unique(jobs.flatMap(\.people))
        if people.isEmpty, let board, let crewName {
            people = board.people.filter { $0.crewName == crewName && !absentIds.contains($0.id) }.map(\.name)
        }
        var vehicles = unique(jobs.flatMap(\.resources))
        if vehicles.isEmpty, let crew { vehicles = crew.vehicles.map(\.name) }
        let absent = (crew?.absentMembers ?? []).map { member in
            [member.name, member.note?.trimmingCharacters(in: .whitespacesAndNewlines)].compactMap { $0?.isEmpty == false ? $0 : nil }
                .joined(separator: ", ")
        }
        let known = sites.compactMap { areas[$0] }.filter { $0 > 0 }
        return TodayOverview(jobCount: jobs.count, siteCount: sites.count, firstStart: starts.first, crewName: crewName,
                             crewColor: crew?.color, leaderName: crew?.leaderName, people: people, vehicles: vehicles, absent: absent,
                             totalArea: known.isEmpty ? nil : known.reduce(0, +))
    }

    private static func unique<T: Hashable>(_ list: [T]) -> [T] {
        var seen = Set<T>()
        return list.filter { seen.insert($0).inserted }
    }

    /// Zahl in deutscher Schreibweise ohne Tausenderpunkt: „120“, „85,5“.
    static func number(_ value: Double) -> String {
        let rounded = (value * 10).rounded() / 10
        if rounded == rounded.rounded() { return String(Int(rounded)) }
        return String(format: "%.1f", rounded).replacingOccurrences(of: ".", with: ",")
    }
}

extension BauakteProject {
    /// Leistung auf einen Blick für die Kolonne: „120 m², Zementestrich CT, 65 mm, Fußbodenheizung“; `nil` ohne Angaben.
    public var keyFactsText: String? {
        let parts: [String?] = [
            totalArea.flatMap { $0 > 0 ? "\(TodayOverview.number($0)) m²" : nil },
            screedType?.title,
            nominalThicknessMm.flatMap { $0 > 0 ? "\($0) mm" : nil },
            underfloorHeating == true ? "Fußbodenheizung" : nil,
        ]
        let text = parts.compactMap { $0 }.joined(separator: ", ")
        return text.isEmpty ? nil : text
    }
}

/// Freitexte (Beschreibung, Zugang und Besonderheiten, Notizen): Aufbereitung für die Anzeige.
public enum FreeText {
    /// Leerzeichen am Rand weg, mehr als eine Leerzeile zusammengefasst; `nil` bei leerem Text. Zeilenumbrüche bleiben.
    public static func clean(_ text: String?) -> String? {
        guard let text else { return nil }
        var result = text.replacingOccurrences(of: "\r\n", with: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
        while result.contains("\n\n\n") { result = result.replacingOccurrences(of: "\n\n\n", with: "\n\n") }
        return result.isEmpty ? nil : result
    }

    /// Lang genug für „Mehr anzeigen“: mehr Zeilen als `lines` oder (bei Fließtext) deutlich mehr als `lines` Zeilen à 45 Zeichen.
    public static func isLong(_ text: String, lines: Int = 6) -> Bool {
        let rows = text.split(separator: "\n", omittingEmptySubsequences: false)
        let estimated = rows.reduce(0) { $0 + max(1, Int((Double($1.count) / 45).rounded(.up))) }
        return estimated > lines
    }
}
