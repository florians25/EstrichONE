import Foundation

/// Aufbereitung der Systemhinweise für die Anzeige (wie im Web, `web/webapp/util/noticeView.ts`): Art und Farbe,
/// verständlicher Zeitraum („Sa., 08.11., 22:00–23:30 Uhr“), relative Angabe („in 2 Tagen“, „läuft seit 10 Min.“),
/// Reihenfolge und Ausblenden. Plattformneutral und unit-getestet; die App zeichnet daraus Karten und das Details-Blatt.
public struct NoticeItem: Sendable, Equatable, Identifiable {
    public enum Kind: String, Sendable, Equatable {
        case maintenanceRunning, critical, maintenanceSoon, warning, maintenance, info

        /// Kleiner = wichtiger.
        public var rank: Int {
            switch self {
            case .maintenanceRunning: 0
            case .critical: 1
            case .maintenanceSoon: 2
            case .warning: 3
            case .maintenance: 4
            case .info: 5
            }
        }
    }

    /// Farbe: Information (blau), Warnung (gelb), Fehler (rot).
    public enum Tone: String, Sendable, Equatable {
        case information, warning, error
    }

    /// Schlüssel zum Ausblenden; ändert sich mit dem Inhalt (dann erscheint der Hinweis wieder).
    public let id: String
    public let kind: Kind
    public let title: String
    public let text: String
    public let period: String
    public let relative: String
    public let remaining: String
    public let source: String
    public let start: Date?
    public let end: Date?

    public var tone: Tone {
        switch kind {
        case .critical: .error
        case .maintenanceRunning, .maintenanceSoon, .warning: .warning
        case .maintenance, .info: .information
        }
    }

    /// Störungen und laufende Wartung lassen sich nicht ausblenden.
    public var isDismissible: Bool { kind != .critical && kind != .maintenanceRunning }

    /// Kurzbezeichnung der Art.
    public var label: String {
        switch kind {
        case .maintenanceRunning: "Wartung läuft"
        case .critical: "Störung"
        case .maintenanceSoon: "Wartung in Kürze"
        case .warning: "Wichtiger Hinweis"
        case .maintenance: "Geplante Wartung"
        case .info: "Information"
        }
    }

    /// SF Symbol je Art.
    public var symbol: String {
        switch kind {
        case .maintenanceRunning: "wrench.and.screwdriver.fill"
        case .critical: "exclamationmark.octagon.fill"
        case .maintenanceSoon, .maintenance: "calendar.badge.clock"
        case .warning: "exclamationmark.triangle.fill"
        case .info: "info.circle.fill"
        }
    }

    /// Hat einen Countdown (geplante Wartung bzw. laufende mit Ende).
    public var isTimed: Bool { start != nil || end != nil }
}

public enum NoticePresentation {
    private static let weekdays = ["So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa."]

    private static func two(_ value: Int) -> String { value < 10 ? "0\(value)" : "\(value)" }

    /// „Sa., 08.11.“ – mit Jahr, wenn es nicht das laufende ist.
    public static func shortDate(_ date: Date, now: Date, calendar: Calendar = .current) -> String {
        let c = calendar.dateComponents([.weekday, .day, .month, .year], from: date)
        let year = c.year != calendar.component(.year, from: now) ? String(c.year ?? 0) : ""
        return "\(weekdays[((c.weekday ?? 1) - 1) % 7]), \(two(c.day ?? 0)).\(two(c.month ?? 0)).\(year)"
    }

    private static func clock(_ date: Date, calendar: Calendar) -> String {
        let c = calendar.dateComponents([.hour, .minute], from: date)
        return "\(two(c.hour ?? 0)):\(two(c.minute ?? 0))"
    }

    /// „Sa., 08.11., 22:00–23:30 Uhr“, über Mitternacht „Sa., 07.11., 22:00 Uhr – So., 08.11., 02:00 Uhr“,
    /// nur Beginn „ab …“, nur Ende „bis …“, ohne Angaben leer.
    public static func period(start: Date?, end: Date?, now: Date, calendar: Calendar = .current) -> String {
        switch (start, end) {
        case let (start?, end?):
            if calendar.isDate(start, inSameDayAs: end) {
                return "\(shortDate(start, now: now, calendar: calendar)), \(clock(start, calendar: calendar))–\(clock(end, calendar: calendar)) Uhr"
            }
            return "\(shortDate(start, now: now, calendar: calendar)), \(clock(start, calendar: calendar)) Uhr – "
                + "\(shortDate(end, now: now, calendar: calendar)), \(clock(end, calendar: calendar)) Uhr"
        case let (start?, nil):
            return "ab \(shortDate(start, now: now, calendar: calendar)), \(clock(start, calendar: calendar)) Uhr"
        case let (nil, end?):
            return "bis \(shortDate(end, now: now, calendar: calendar)), \(clock(end, calendar: calendar)) Uhr"
        case (nil, nil):
            return ""
        }
    }

    /// „10 Min.“, „1 Std. 30 Min.“, „5 Std.“, „1 Tag“, „2 Tagen“ (Dativ) bzw. „2 Tage“ (Akkusativ).
    public static func duration(_ seconds: TimeInterval, dative: Bool = true) -> String {
        let minutes = max(1, Int((seconds / 60).rounded()))
        if minutes < 60 { return "\(minutes) Min." }
        if seconds < 86_400 {
            let hours = minutes / 60
            let rest = minutes % 60
            return hours < 3 && rest >= 5 ? "\(hours) Std. \(rest) Min." : "\(Int((Double(minutes) / 60).rounded())) Std."
        }
        let days = Int((seconds / 86_400).rounded())
        return days == 1 ? "1 Tag" : "\(days) \(dative ? "Tagen" : "Tage")"
    }

    /// Vorher „in 45 Min.“ / „heute, in 5 Std.“ / „morgen“ / „in 2 Tagen“, währenddessen „läuft seit 10 Min.“, danach „beendet“.
    public static func relative(start: Date?, end: Date?, now: Date, calendar: Calendar = .current) -> String {
        if let end, now >= end { return "beendet" }
        guard let start else { return "" }
        let diff = start.timeIntervalSince(now)
        if diff > 0 {
            if diff < 60 { return "beginnt gleich" }
            if diff < 3600 { return "in \(duration(diff))" }
            let days = calendar.dateComponents([.day], from: calendar.startOfDay(for: now), to: calendar.startOfDay(for: start)).day ?? 0
            if days == 0 { return "heute, in \(duration(diff))" }
            if days == 1 { return "morgen" }
            return "in \(days) Tagen"
        }
        let since = now.timeIntervalSince(start)
        return since < 60 ? "läuft gerade an" : "läuft seit \(duration(since))"
    }

    /// „noch 20 Min.“, „noch 3 Tage“; leer ohne Ende oder danach.
    public static func remaining(end: Date?, now: Date) -> String {
        guard let end else { return "" }
        let left = end.timeIntervalSince(now)
        return left > 0 ? "noch \(duration(left, dative: false))" : ""
    }

    /// Kurzer, stabiler Inhaltsschlüssel (FNV-1a, wie im Web).
    public static func contentHash(_ text: String) -> String {
        var hash: UInt32 = 0x811C_9DC5
        for unit in text.utf16 {
            hash ^= UInt32(unit)
            hash = hash &* 0x0100_0193
        }
        return String(hash, radix: 36)
    }

    private static func sourceText(_ source: String?) -> String {
        switch source {
        case "console": "Betreiber"
        case "instance": "Verwaltung"
        default: ""
        }
    }

    public static func item(_ a: SystemNotices.Announcement, now: Date, calendar: Calendar = .current) -> NoticeItem {
        let kind: NoticeItem.Kind = switch a.severity {
        case .critical: .critical
        case .warning: .warning
        case .info: .info
        }
        let start = a.startsAt.flatMap(APIDate.parse)
        let end = a.endsAt.flatMap(APIDate.parse)
        let upcoming = start.map { $0 > now } ?? false
        return NoticeItem(
            id: "a:\(a.id):\(contentHash("\(a.severity.rawValue)|\(a.title)|\(a.text)|\(a.startsAt ?? "")|\(a.endsAt ?? "")"))",
            kind: kind, title: a.title, text: a.text,
            period: period(start: start, end: end, now: now, calendar: calendar),
            relative: upcoming ? relative(start: start, end: end, now: now, calendar: calendar) : "",
            remaining: upcoming ? "" : remaining(end: end, now: now),
            source: sourceText(a.source), start: start, end: end)
    }

    public static func item(_ m: SystemNotices.Maintenance, now: Date, calendar: Calendar = .current) -> NoticeItem {
        let start = m.start
        let end = m.end
        let kind: NoticeItem.Kind
        if m.active || (start.map { $0 <= now } ?? false) {
            kind = .maintenanceRunning
        } else if let start, start.timeIntervalSince(now) <= 3600 {
            kind = .maintenanceSoon
        } else {
            kind = .maintenance
        }
        let running = kind == .maintenanceRunning
        return NoticeItem(
            // „Bald“ mit eigenem Schlüssel: Wer früh ausblendet, wird eine Stunde vorher noch einmal erinnert.
            id: "m:\(m.startsAt):\(kind.rawValue):\(contentHash("\(m.message ?? "")|\(m.endsAt ?? "")"))",
            kind: kind,
            title: running ? "Wartungsarbeiten laufen" : "Geplante Wartung",
            text: m.message ?? "EstrichONE ist in dieser Zeit nicht erreichbar. Bitte rechtzeitig abgleichen – Erfasstes bleibt sicher auf dem Gerät.",
            period: period(start: start, end: end, now: now, calendar: calendar),
            relative: relative(start: start, end: end, now: now, calendar: calendar),
            remaining: running ? remaining(end: end, now: now) : "",
            source: sourceText(m.source), start: start, end: end)
    }

    /// Alle aktuellen Hinweise in Anzeige-Reihenfolge (laufende Wartung, Störung, Wartung in Kürze, Warnung, geplante Wartung, Info).
    public static func items(_ notices: SystemNotices?, now: Date, calendar: Calendar = .current) -> [NoticeItem] {
        guard let notices else { return [] }
        var list = notices.currentAnnouncements(at: now).map { item($0, now: now, calendar: calendar) }
        if let m = notices.maintenance, m.active || (m.end.map { $0 > now } ?? true) {
            list.append(item(m, now: now, calendar: calendar))
        }
        return list.enumerated().sorted { ($0.element.kind.rank, $0.offset) < ($1.element.kind.rank, $1.offset) }.map(\.element)
    }

    /// Ausgeblendete (nur ausblendbare) fallen weg; eingeklappt bleiben der wichtigste und alle nicht ausblendbaren sichtbar.
    public static func selection(_ items: [NoticeItem], dismissed: Set<String>, expanded: Bool) -> (shown: [NoticeItem], hidden: Int) {
        let open = items.filter { !$0.isDismissible || !dismissed.contains($0.id) }
        if expanded || open.count <= 1 { return (open, 0) }
        let shown = open.enumerated().filter { $0.offset == 0 || !$0.element.isDismissible }.map(\.element)
        return (shown, open.count - shown.count)
    }

    /// „1 weiterer Hinweis“, „3 weitere Hinweise“.
    public static func moreText(_ count: Int) -> String {
        count == 1 ? "1 weiterer Hinweis" : "\(count) weitere Hinweise"
    }
}
