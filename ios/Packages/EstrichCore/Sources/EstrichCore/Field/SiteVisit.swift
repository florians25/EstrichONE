import Foundation

/// Ankunft und Abschluss auf der Baustelle. Der Server führt dafür keine eigenen Felder; die Zeiten werden als
/// Bautagebuch-Eintrag vorgeschlagen („Angekommen um 07:42 Uhr.“) und auf dem Gerät gemerkt, damit „Heute“ sie zeigt
/// und beim Abschluss die Dauer vor Ort dazuschreiben kann.
public struct SiteVisit: Codable, Equatable, Sendable {
    /// Kalendertag `yyyy-MM-dd`.
    public var day: String
    public var arrivedAt: Date?
    public var finishedAt: Date?

    public init(day: String, arrivedAt: Date? = nil, finishedAt: Date? = nil) {
        self.day = day
        self.arrivedAt = arrivedAt
        self.finishedAt = finishedAt
    }

    public var isEmpty: Bool { arrivedAt == nil && finishedAt == nil }
}

public enum SiteVisitLog {
    /// „07:42“ in der Zeitzone des Geräts.
    public static func clock(_ date: Date, calendar: Calendar = .current) -> String {
        let parts = calendar.dateComponents([.hour, .minute], from: date)
        return String(format: "%02d:%02d", parts.hour ?? 0, parts.minute ?? 0)
    }

    /// „7 Std. 28 Min.“, „45 Min.“, „2 Std.“; mindestens eine Minute.
    public static func duration(from start: Date, to end: Date) -> String {
        let minutes = max(1, Int((end.timeIntervalSince(start) / 60).rounded()))
        let hours = minutes / 60
        let rest = minutes % 60
        if hours == 0 { return "\(rest) Min." }
        return rest == 0 ? "\(hours) Std." : "\(hours) Std. \(rest) Min."
    }

    /// Text des Tagebucheintrags zur Ankunft.
    public static func arrivalText(at date: Date, calendar: Calendar = .current) -> String {
        "Angekommen um \(clock(date, calendar: calendar)) Uhr."
    }

    /// Text des Tagebucheintrags zum Abschluss, mit der Dauer vor Ort, wenn die Ankunft am selben Tag bekannt ist.
    public static func finishText(at date: Date, arrivedAt: Date?, calendar: Calendar = .current) -> String {
        var text = "Fertig um \(clock(date, calendar: calendar)) Uhr."
        if let arrivedAt, arrivedAt < date, calendar.isDate(arrivedAt, inSameDayAs: date) {
            text += " Dauer vor Ort: \(duration(from: arrivedAt, to: date))"
        }
        return text
    }
}

/// Merkt Ankunft und Abschluss je Bauakte und Tag auf dem Gerät.
public struct SiteVisitStore: Sendable {
    private let store: LocalStore

    public init(store: LocalStore) {
        self.store = store
    }

    static func key(_ bauakteId: UUID, day: String) -> String {
        "visit.\(bauakteId.uuidString.lowercased()).\(day)"
    }

    public func visit(of bauakteId: UUID, day: String) async -> SiteVisit {
        guard let text = try? await store.meta(Self.key(bauakteId, day: day)), !text.isEmpty,
              let stored = try? JSONCoding.decoder.decode(SiteVisit.self, from: Data(text.utf8)) else { return SiteVisit(day: day) }
        return stored
    }

    @discardableResult
    public func markArrived(_ bauakteId: UUID, at date: Date, calendar: Calendar = .current) async -> SiteVisit {
        let day = CalendarDay.string(date, calendar: calendar)
        var current = await visit(of: bauakteId, day: day)
        current.arrivedAt = date
        current.finishedAt = nil
        await save(current, bauakteId)
        return current
    }

    @discardableResult
    public func markFinished(_ bauakteId: UUID, at date: Date, calendar: Calendar = .current) async -> SiteVisit {
        let day = CalendarDay.string(date, calendar: calendar)
        var current = await visit(of: bauakteId, day: day)
        current.finishedAt = date
        await save(current, bauakteId)
        return current
    }

    private func save(_ visit: SiteVisit, _ bauakteId: UUID) async {
        guard let data = try? JSONCoding.encoder.encode(visit) else { return }
        try? await store.setMeta(Self.key(bauakteId, day: visit.day), String(decoding: data, as: UTF8.self))
    }
}

/// Wohin Schnellzugriffe („Foto zur heutigen Baustelle“, „Bautagebuch“, „Mangel“) führen: in die Bauakte des laufenden
/// Einsatzes, sonst des nächsten heute, sonst des letzten heute.
public enum QuickTarget {
    public static func bauakteId(assignments: [Assignment], today: String, now: Date, calendar: Calendar = .current) -> UUID? {
        let todays = assignments.filter { $0.day == today }.sorted { ($0.startTime ?? "99") < ($1.startTime ?? "99") }
        guard !todays.isEmpty else { return nil }
        // Ende eines Einsatzes ist der Beginn des nächsten: So gilt immer höchstens einer als laufend.
        var phases: [TodayGlance.Phase] = []
        for (index, assignment) in todays.enumerated() {
            let end = index + 1 < todays.count ? todays[index + 1].startText : nil
            phases.append(TodayGlance.phase(start: assignment.startText, end: end, now: now, calendar: calendar))
        }
        guard let index = TodayGlance.focusIndex(phases) else { return todays.first?.bauakteId }
        return todays[index].bauakteId
    }
}
