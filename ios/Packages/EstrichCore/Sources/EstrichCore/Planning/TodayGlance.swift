import Foundation

/// „Heute“ auf einen Blick: Zeitphase eines Einsatzes, Kennzahlen der Baustelle (offene Mängel und Aufgaben,
/// Belegreife, Wetter laut Bautagebuch) und Entfernung. Alles aus Daten, die schon auf dem Gerät liegen.
public enum TodayGlance {
    /// Wo ein Einsatz im Tagesablauf steht.
    public enum Phase: Equatable, Sendable {
        /// Beginnt noch (Minuten bis zum Beginn).
        case upcoming(minutes: Int)
        /// Läuft gerade.
        case running
        /// Vorbei (Ende überschritten).
        case finished
        /// Ohne Uhrzeit.
        case unknown

        /// Kurzer Text für die Kennzeichnung („Jetzt“, „in 25 Min.“, „in 2 Std.“, „Vorbei“); `nil` ohne Uhrzeit.
        public var text: String? {
            switch self {
            case .running: return "Jetzt"
            case .finished: return "Vorbei"
            case .unknown: return nil
            case let .upcoming(minutes):
                if minutes < 60 { return "in \(max(minutes, 1)) Min." }
                return minutes < 120 ? "in 1 Std." : "in \(minutes / 60) Std."
            }
        }
    }

    /// Phase aus „HH:mm“ (Beginn, Ende) am heutigen Tag. Ohne Ende gilt der Einsatz ab Beginn als laufend.
    public static func phase(start: String?, end: String?, now: Date, calendar: Calendar = .current) -> Phase {
        func minutes(_ text: String?) -> Int? {
            guard let text else { return nil }
            let parts = text.split(separator: ":").prefix(2).compactMap { Int($0) }
            return parts.count == 2 ? parts[0] * 60 + parts[1] : nil
        }
        guard let begin = minutes(start) else { return .unknown }
        let parts = calendar.dateComponents([.hour, .minute], from: now)
        let current = (parts.hour ?? 0) * 60 + (parts.minute ?? 0)
        if current < begin { return .upcoming(minutes: begin - current) }
        if let finish = minutes(end), finish > begin, current >= finish { return .finished }
        return .running
    }

    /// Der Einsatz, der aufgeklappt gezeigt wird: der laufende, sonst der nächste, sonst der letzte.
    public static func focusIndex(_ phases: [Phase]) -> Int? {
        guard !phases.isEmpty else { return nil }
        if let running = phases.lastIndex(of: .running) { return running }
        if let next = phases.firstIndex(where: { phase in
            if case .upcoming = phase { return true }
            return false
        }) { return next }
        return phases.count - 1
    }

    /// Entfernung in Luftlinie: „850 m“, „1,8 km“, „23 km“.
    public static func distanceText(meters: Double) -> String {
        if meters < 1000 { return "\(max(Int((meters / 50).rounded()) * 50, 50)) m" }
        if meters < 10000 { return String(format: "%.1f km", meters / 1000).replacingOccurrences(of: ".", with: ",") }
        return "\(Int((meters / 1000).rounded())) km"
    }

    /// Luftlinie in Metern (Haversine).
    public static func distance(fromLatitude lat1: Double, longitude lon1: Double, toLatitude lat2: Double, longitude lon2: Double) -> Double {
        let radius = 6_371_000.0
        let dLat = (lat2 - lat1) * .pi / 180
        let dLon = (lon2 - lon1) * .pi / 180
        let a = sin(dLat / 2) * sin(dLat / 2) + cos(lat1 * .pi / 180) * cos(lat2 * .pi / 180) * sin(dLon / 2) * sin(dLon / 2)
        return radius * 2 * atan2(a.squareRoot(), (1 - a).squareRoot())
    }
}

/// Kennzahlen einer Baustelle für die Karte auf „Heute“.
public struct SiteGlance: Equatable, Sendable {
    /// Stand der Belegreife.
    public enum Readiness: Equatable, Sendable {
        /// Letzte CM-Messung bestanden (Messwert in CM-%).
        case ready(cm: Double?)
        /// Letzte CM-Messung nicht bestanden.
        case notReady(cm: Double?)
        /// Räume: so viele von allen belegreif.
        case rooms(ready: Int, total: Int)
        /// Räume: so viele von allen eingebracht (noch keiner belegreif).
        case laid(laid: Int, total: Int)

        public var text: String {
            switch self {
            case let .ready(cm): cm.map { "Belegreif (\(Self.percent($0)) CM-%)" } ?? "Belegreif"
            case let .notReady(cm): cm.map { "Noch nicht belegreif (\(Self.percent($0)) CM-%)" } ?? "Noch nicht belegreif"
            case let .rooms(ready, total): "\(ready) von \(total) Räumen belegreif"
            case let .laid(laid, total): "\(laid) von \(total) Räumen eingebracht"
            }
        }

        public var isReady: Bool {
            switch self {
            case .ready: true
            case let .rooms(ready, total): ready == total
            default: false
            }
        }

        static func percent(_ value: Double) -> String {
            String(format: "%.1f", value).replacingOccurrences(of: ".", with: ",")
        }
    }

    public let openDefects: Int
    public let openTasks: Int
    /// Aufgaben und Mängel, die heute oder früher fällig sind.
    public let due: Int
    public let readiness: Readiness?
    /// Wetter laut heutigem Bautagebuch-Eintrag.
    public let weather: DiaryWeather?
    public let temperatureCelsius: Int?

    public init(openDefects: Int = 0, openTasks: Int = 0, due: Int = 0, readiness: Readiness? = nil, weather: DiaryWeather? = nil,
                temperatureCelsius: Int? = nil) {
        self.openDefects = openDefects
        self.openTasks = openTasks
        self.due = due
        self.readiness = readiness
        self.weather = weather
        self.temperatureCelsius = temperatureCelsius
    }

    /// Aus den lokalen Daten einer Bauakte (`today` = `yyyy-MM-dd`).
    public init(items: [WorkItem], rooms: [Room], inspections: [Inspection], diary: [DiaryEntry], today: String) {
        let open = items.filter { !$0.isDone }
        let measurement = inspections
            .filter { $0.kind == .cmMeasurement && !$0.notRequired && $0.isReadyForCovering != nil }
            .max { ($0.performedAt ?? .distantPast) < ($1.performedAt ?? .distantPast) }
        let totals = RoomTotals(rooms)
        var readiness: Readiness?
        if let measurement, let ready = measurement.isReadyForCovering {
            readiness = ready ? Readiness.ready(cm: measurement.measuredCmPercent) : Readiness.notReady(cm: measurement.measuredCmPercent)
        } else if totals.count > 0, totals.ready > 0 {
            readiness = .rooms(ready: totals.ready, total: totals.count)
        } else if totals.count > 0, totals.laid > 0 {
            readiness = .laid(laid: totals.laid, total: totals.count)
        }
        let entry = diary.filter { $0.day == today }
            .max { ($0.createdAt ?? .distantPast) < ($1.createdAt ?? .distantPast) }
        self.init(openDefects: open.filter { $0.kind == .defect }.count, openTasks: open.filter { $0.kind == .task }.count,
                  due: open.filter { $0.isDue(on: today) }.count, readiness: readiness, weather: entry?.weather,
                  temperatureCelsius: entry?.temperatureCelsius)
    }

    /// „1 Mangel“, „3 Mängel“.
    public var defectsText: String? {
        openDefects == 0 ? nil : (openDefects == 1 ? "1 Mangel" : "\(openDefects) Mängel")
    }

    /// „1 Aufgabe“, „2 Aufgaben“.
    public var tasksText: String? {
        openTasks == 0 ? nil : (openTasks == 1 ? "1 Aufgabe" : "\(openTasks) Aufgaben")
    }

    /// „Sonnig, 15 °C“.
    public var weatherText: String? {
        switch (weather, temperatureCelsius) {
        case let (weather?, temperature?): "\(weather.title), \(temperature) °C"
        case let (weather?, nil): weather.title
        case let (nil, temperature?): "\(temperature) °C"
        default: nil
        }
    }

    public var isEmpty: Bool {
        openDefects == 0 && openTasks == 0 && readiness == nil && weatherText == nil
    }
}
