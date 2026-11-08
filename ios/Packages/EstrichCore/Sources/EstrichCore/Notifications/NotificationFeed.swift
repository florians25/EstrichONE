import Foundation

/// Art einer Benachrichtigung fürs Postfach: Filter-Chips, Symbol und Farbe. Fasst die Kategorien des Servers
/// (`message`, `ticket`, `approval`, `planning`, `bauakte`, `deadline`, `system`, `chat`, `work`, `absence`) und ältere
/// Schreibweisen (`plan`, `planning.released`, `broadcast`, …) zu wenigen, verständlichen Gruppen zusammen.
public enum NotificationKind: String, CaseIterable, Sendable, Hashable, Identifiable {
    case planning, bauakte, work, chat, ticket, office, personal, system

    public var id: String { rawValue }

    /// Name des Filters („Planung“, „Bauakten“, …).
    public var title: String {
        switch self {
        case .planning: "Planung"
        case .bauakte: "Bauakten"
        case .work: "Aufgaben"
        case .chat: "Chat"
        case .ticket: "Meldungen"
        case .office: "Büro"
        case .personal: "Personal"
        case .system: "System"
        }
    }

    /// SF Symbol der Art (Filter-Chip, Gruppenkopf).
    public var symbol: String {
        switch self {
        case .planning: "calendar.badge.clock"
        case .bauakte: "folder"
        case .work: "checklist"
        case .chat: "bubble.left.and.bubble.right"
        case .ticket: "lifepreserver"
        case .office: "megaphone"
        case .personal: "sun.max"
        case .system: "server.rack"
        }
    }

    /// Art zur Kategorie des Servers; Unbekanntes zählt zum Büro (von Hand gesendete Nachrichten).
    public init(category: String?) {
        let key = (category ?? "").lowercased()
        let head = key.split(whereSeparator: { $0 == "." || $0 == "_" || $0 == "-" }).first.map(String.init) ?? key
        switch head {
        case "planning", "plan", "planchange", "planday", "disposition": self = .planning
        case "bauakte", "assignment", "approval", "document", "acceptance", "abnahme", "comment", "reply": self = .bauakte
        case "work", "deadline", "defect", "mangel", "task": self = .work
        case "chat", "chatmessage": self = .chat
        case "ticket", "helpdesk", "feedback": self = .ticket
        case "absence", "leave", "vacation": self = .personal
        case "system", "maintenance", "backup", "archive", "peerupdate": self = .system
        default: self = .office
        }
    }

    /// Symbol für eine einzelne Kategorie – feiner als die Art (Freigabe, Frist, Abnahme …).
    public static func symbol(for category: String?) -> String {
        let key = (category ?? "").lowercased()
        switch key {
        case "approval", "approval.requested", "approval.decided", "document.approval": return "checkmark.seal"
        case "deadline", "deadline.due": return "clock.badge.exclamationmark"
        case "acceptance", "acceptance.signed", "abnahme": return "signature"
        case "defect", "defect.assigned", "mangel": return "exclamationmark.triangle"
        case "comment", "reply", "bauakte.comment": return "text.bubble"
        case "maintenance", "maintenance.announced", "system.maintenance": return "wrench.and.screwdriver"
        default:
            if key.hasPrefix("backup") || key.hasPrefix("archive") { return "externaldrive" }
            return NotificationKind(category: category).symbol
        }
    }
}

/// Zeitraum im Postfach: Heute, Gestern, Diese Woche (Kalenderwoche ab Montag), Älter.
public enum NotificationPeriod: Int, CaseIterable, Sendable, Comparable, Identifiable {
    case today, yesterday, thisWeek, older

    public var id: Int { rawValue }

    public var title: String {
        switch self {
        case .today: "Heute"
        case .yesterday: "Gestern"
        case .thisWeek: "Diese Woche"
        case .older: "Älter"
        }
    }

    public static func < (lhs: Self, rhs: Self) -> Bool { lhs.rawValue < rhs.rawValue }

    public init(_ date: Date, now: Date, calendar: Calendar = NotificationFeed.calendar) {
        if calendar.isDate(date, inSameDayAs: now) || date > now {
            self = .today
        } else if let yesterday = calendar.date(byAdding: .day, value: -1, to: now), calendar.isDate(date, inSameDayAs: yesterday) {
            self = .yesterday
        } else if calendar.isDate(date, equalTo: now, toGranularity: .weekOfYear) {
            self = .thisWeek
        } else {
            self = .older
        }
    }
}

/// Auswahl oben im Postfach: Alle oder nur ungelesene, optional eine Art, dazu der Suchtext.
public struct NotificationFilter: Equatable, Sendable, Hashable {
    public var unreadOnly: Bool
    public var kind: NotificationKind?
    public var query: String

    public init(unreadOnly: Bool = false, kind: NotificationKind? = nil, query: String = "") {
        self.unreadOnly = unreadOnly
        self.kind = kind
        self.query = query
    }

    public var isActive: Bool { unreadOnly || kind != nil || !query.trimmingCharacters(in: .whitespaces).isEmpty }

    /// Trifft der Eintrag die Auswahl? Suche: jedes Wort muss in Titel, Text, Absender oder Art vorkommen
    /// (ohne Groß-/Kleinschreibung und Akzente: „mangel“ findet „Mängel“).
    public func matches(_ item: InboxNotification) -> Bool {
        if unreadOnly, item.isRead { return false }
        if let kind, item.kind != kind { return false }
        let words = Self.fold(query).split(whereSeparator: \.isWhitespace)
        guard !words.isEmpty else { return true }
        let haystack = Self.fold([item.title, item.body, item.senderName, item.kind.title].compactMap { $0 }.joined(separator: " "))
        return words.allSatisfy { haystack.contains($0) }
    }

    static func fold(_ text: String) -> String {
        text.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: Locale(identifier: "de_DE"))
    }
}

/// Abschnitt der Liste: Zeitraum oder Art mit den Einträgen (neueste zuerst).
public struct NotificationSection: Equatable, Sendable, Identifiable {
    public let id: String
    public let title: String
    public let symbol: String?
    public var items: [InboxNotification]

    public var unreadCount: Int { items.filter { !$0.isRead }.count }
}

/// Gruppierung der Liste.
public enum NotificationGrouping: String, CaseIterable, Sendable, Identifiable {
    case time, kind

    public var id: String { rawValue }
    public var title: String { self == .time ? "Nach Zeit" : "Nach Art" }
}

/// Aufbereitung des Postfachs (testbarer Kern der Ansicht): filtern, gruppieren, zählen.
public enum NotificationFeed {
    /// Deutscher Kalender (Woche ab Montag) in der Zeitzone des Geräts.
    public static var calendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.locale = Locale(identifier: "de_DE")
        calendar.firstWeekday = 2
        calendar.minimumDaysInFirstWeek = 4
        calendar.timeZone = .current
        return calendar
    }

    public static func filter(_ items: [InboxNotification], _ filter: NotificationFilter) -> [InboxNotification] {
        items.filter(filter.matches).sorted(by: newestFirst)
    }

    /// Abschnitte in fester Reihenfolge; leere fallen weg.
    public static func sections(_ items: [InboxNotification], grouping: NotificationGrouping = .time, now: Date = .now,
                                calendar: Calendar = NotificationFeed.calendar) -> [NotificationSection] {
        let sorted = items.sorted(by: newestFirst)
        switch grouping {
        case .time:
            let groups = Dictionary(grouping: sorted) { NotificationPeriod($0.createdAt, now: now, calendar: calendar) }
            return NotificationPeriod.allCases.compactMap { period in
                groups[period].map { NotificationSection(id: "period-\(period.rawValue)", title: period.title, symbol: nil, items: $0) }
            }
        case .kind:
            let groups = Dictionary(grouping: sorted) { $0.kind }
            // Arten mit ungelesenen Einträgen zuerst, sonst nach der neuesten Benachrichtigung.
            return groups.map { kind, list in NotificationSection(id: "kind-\(kind.rawValue)", title: kind.title, symbol: kind.symbol, items: list) }
                .sorted { lhs, rhs in
                    if (lhs.unreadCount > 0) != (rhs.unreadCount > 0) { return lhs.unreadCount > 0 }
                    return (lhs.items.first?.createdAt ?? .distantPast) > (rhs.items.first?.createdAt ?? .distantPast)
                }
        }
    }

    /// Arten, die im Postfach vorkommen (für die Filter-Chips), in fester Reihenfolge.
    public static func kinds(in items: [InboxNotification]) -> [NotificationKind] {
        let present = Set(items.map(\.kind))
        return NotificationKind.allCases.filter(present.contains)
    }

    /// Ungelesene je Art (Zahl am Chip).
    public static func unreadCounts(_ items: [InboxNotification]) -> [NotificationKind: Int] {
        items.filter { !$0.isRead }.reduce(into: [:]) { $0[$1.kind, default: 0] += 1 }
    }

    /// Text für leere Ergebnisse je nach Auswahl.
    public static func emptyMessage(_ filter: NotificationFilter) -> (title: String, message: String) {
        if !filter.query.trimmingCharacters(in: .whitespaces).isEmpty {
            return ("Keine Treffer", "Keine Benachrichtigung enthält „\(filter.query.trimmingCharacters(in: .whitespaces))“.")
        }
        if filter.unreadOnly {
            return ("Alles gelesen", filter.kind.map { "Keine ungelesenen Benachrichtigungen unter „\($0.title)“." }
                ?? "Keine ungelesenen Benachrichtigungen.")
        }
        if let kind = filter.kind { return ("Nichts unter „\(kind.title)“", "Hier ist gerade nichts.") }
        return ("Keine Benachrichtigungen", "Hier erscheinen Freigaben der Planung, Antworten im Helpdesk und Nachrichten aus dem Büro.")
    }

    /// Zeit in der Zeile, kurz wie in Mail: „jetzt“, „vor 12 Min.“, „09:41“, „Gestern“, „Montag“, „28.10.“, „28.10.25“.
    public static func timeText(_ date: Date, now: Date = .now, calendar: Calendar = NotificationFeed.calendar) -> String {
        let seconds = now.timeIntervalSince(date)
        if seconds < 60 { return "jetzt" }
        if seconds < 3600 { return "vor \(Int(seconds / 60)) Min." }
        switch NotificationPeriod(date, now: now, calendar: calendar) {
        case .today: return format(date, "HH:mm", calendar)
        case .yesterday: return "Gestern"
        case .thisWeek: return format(date, "EEEE", calendar)
        case .older:
            let sameYear = calendar.component(.year, from: date) == calendar.component(.year, from: now)
            return format(date, sameYear ? "d.M." : "d.M.yy", calendar)
        }
    }

    /// Vollständiger Zeitpunkt (Einzelansicht): „Mittwoch, 4. November 2026, 09:48 Uhr“.
    public static func fullTimeText(_ date: Date, calendar: Calendar = NotificationFeed.calendar) -> String {
        format(date, "EEEE, d. MMMM yyyy, HH:mm", calendar) + " Uhr"
    }

    private static func format(_ date: Date, _ pattern: String, _ calendar: Calendar) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "de_DE")
        formatter.calendar = calendar
        formatter.timeZone = calendar.timeZone
        formatter.dateFormat = pattern
        return formatter.string(from: date)
    }

    static func newestFirst(_ lhs: InboxNotification, _ rhs: InboxNotification) -> Bool {
        lhs.createdAt == rhs.createdAt ? lhs.id.uuidString > rhs.id.uuidString : lhs.createdAt > rhs.createdAt
    }
}
