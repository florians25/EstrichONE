import Foundation
import Testing
@testable import EstrichCore

@Suite("Systemhinweise: Zeitraum, Art, Ausblenden")
struct NoticePresentationTests {
    private let calendar: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        return calendar
    }()

    /// Ortszeit Berlin.
    private func at(_ day: Int, _ hour: Int, _ minute: Int = 0, month: Int = 11, year: Int = 2026) -> Date {
        calendar.date(from: DateComponents(year: year, month: month, day: day, hour: hour, minute: minute))!
    }

    /// Freitag, 06.11.2026, 12:00 Uhr.
    private var now: Date { at(6, 12) }

    private func decode(_ json: String) throws -> SystemNotices {
        try JSONCoding.decoder.decode(SystemNotices.self, from: Data(json.utf8))
    }

    @Test("Zeitraum: gleicher Tag, über Mitternacht, nur Beginn bzw. Ende, anderes Jahr")
    func period() {
        #expect(NoticePresentation.period(start: at(8, 22), end: at(8, 23, 30), now: now, calendar: calendar) == "So., 08.11., 22:00–23:30 Uhr")
        #expect(NoticePresentation.period(start: at(7, 22), end: at(8, 2), now: now, calendar: calendar) == "Sa., 07.11., 22:00 Uhr – So., 08.11., 02:00 Uhr")
        #expect(NoticePresentation.period(start: at(7, 6, 5), end: nil, now: now, calendar: calendar) == "ab Sa., 07.11., 06:05 Uhr")
        #expect(NoticePresentation.period(start: nil, end: at(13, 18), now: now, calendar: calendar) == "bis Fr., 13.11., 18:00 Uhr")
        #expect(NoticePresentation.period(start: nil, end: nil, now: now, calendar: calendar) == "")
        #expect(NoticePresentation.period(start: at(2, 8, month: 1, year: 2027), end: at(2, 9, month: 1, year: 2027), now: now, calendar: calendar)
            == "Sa., 02.01.2027, 08:00–09:00 Uhr")
    }

    @Test("Relative Angabe und Restdauer")
    func relative() {
        let plus = { (minutes: Double) in self.now.addingTimeInterval(minutes * 60) }
        #expect(NoticePresentation.relative(start: plus(45), end: plus(90), now: now, calendar: calendar) == "in 45 Min.")
        #expect(NoticePresentation.relative(start: at(6, 17), end: nil, now: now, calendar: calendar) == "heute, in 5 Std.")
        #expect(NoticePresentation.relative(start: at(7, 9), end: nil, now: now, calendar: calendar) == "morgen")
        #expect(NoticePresentation.relative(start: at(8, 22), end: at(8, 23), now: now, calendar: calendar) == "in 2 Tagen")
        #expect(NoticePresentation.relative(start: plus(-10), end: plus(20), now: now, calendar: calendar) == "läuft seit 10 Min.")
        #expect(NoticePresentation.relative(start: plus(-150), end: nil, now: now, calendar: calendar) == "läuft seit 2 Std. 30 Min.")
        #expect(NoticePresentation.relative(start: plus(-60), end: plus(-1), now: now, calendar: calendar) == "beendet")
        #expect(NoticePresentation.remaining(end: plus(20), now: now) == "noch 20 Min.")
        #expect(NoticePresentation.remaining(end: plus(60 * 24 * 3), now: now) == "noch 3 Tage")
        #expect(NoticePresentation.remaining(end: plus(-5), now: now) == "")
        #expect(NoticePresentation.duration(86_400) == "1 Tag")
    }

    @Test("Art, Farbe, Ausblendbarkeit und Reihenfolge")
    func kinds() throws {
        let start = APIDate.format(at(8, 22))
        let end = APIDate.format(at(8, 23, 30))
        let notices = try decode("""
        {"announcements":[
           {"id":"i","title":"Neu","text":"PDF","severity":"info","startsAt":null,"endsAt":null,"source":"instance"},
           {"id":"c","title":"Störung","text":"Mail","severity":"critical","startsAt":null,"endsAt":null,"source":"console"},
           {"id":"w","title":"Achtung","text":"x","severity":"warning","startsAt":null,"endsAt":null}],
         "maintenance":{"active":false,"message":"Update","startsAt":"\(start)","endsAt":"\(end)","adminsMayWork":true,"source":"console"},
         "serverTime":null}
        """)
        let items = NoticePresentation.items(notices, now: now, calendar: calendar)
        #expect(items.map(\.kind) == [.critical, .warning, .maintenance, .info])
        #expect(items[0].tone == .error && !items[0].isDismissible && items[0].source == "Betreiber")
        let maintenance = items[2]
        #expect(maintenance.tone == .information)
        #expect(maintenance.title == "Geplante Wartung")
        #expect(maintenance.relative == "in 2 Tagen")
        #expect(maintenance.period == "So., 08.11., 22:00–23:30 Uhr")
        #expect(maintenance.isDismissible)

        let collapsed = NoticePresentation.selection(items, dismissed: [], expanded: false)
        #expect(collapsed.shown.map(\.kind) == [.critical])
        #expect(collapsed.hidden == 3)
        let after = NoticePresentation.selection(items, dismissed: [items[0].id, items[1].id], expanded: true)
        #expect(after.shown.map(\.kind) == [.critical, .maintenance, .info])
        #expect(NoticePresentation.moreText(1) == "1 weiterer Hinweis")
    }

    @Test("Wartung in Kürze und laufende Wartung")
    func maintenanceStates() throws {
        let soon = try decode("""
        {"announcements":[],"maintenance":{"active":false,"message":null,"startsAt":"\(APIDate.format(now.addingTimeInterval(1800)))","endsAt":null,"adminsMayWork":true},"serverTime":null}
        """)
        let item = try #require(NoticePresentation.items(soon, now: now, calendar: calendar).first)
        #expect(item.kind == .maintenanceSoon && item.tone == .warning && item.relative == "in 30 Min.")
        // Früh ausgeblendet → eine Stunde vorher neuer Schlüssel.
        let early = try #require(NoticePresentation.items(soon, now: now.addingTimeInterval(-3 * 3600), calendar: calendar).first)
        #expect(early.kind == .maintenance && early.id != item.id)

        let running = try decode("""
        {"announcements":[],"maintenance":{"active":true,"message":"Umzug","startsAt":"\(APIDate.format(now.addingTimeInterval(-600)))","endsAt":"\(APIDate.format(now.addingTimeInterval(1200)))","adminsMayWork":true},"serverTime":null}
        """)
        let live = try #require(NoticePresentation.items(running, now: now, calendar: calendar).first)
        #expect(live.kind == .maintenanceRunning && !live.isDismissible)
        #expect(live.relative == "läuft seit 10 Min." && live.remaining == "noch 20 Min.")
    }
}
