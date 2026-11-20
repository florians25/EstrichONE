import Foundation
import Testing
@testable import EstrichCore

@Suite("Heute auf einen Blick")
struct TodayGlanceTests {
    private var calendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        return calendar
    }

    private func at(_ hour: Int, _ minute: Int) -> Date {
        calendar.date(from: DateComponents(year: 2026, month: 11, day: 20, hour: hour, minute: minute))!
    }

    private func entity(_ type: EntityType, _ data: [String: JSONValue]) -> LocalEntity {
        LocalEntity(type: type, id: UUID(), data: .object(data), syncState: .synced)
    }

    @Test("Phase: bevorstehend, läuft, vorbei, ohne Uhrzeit")
    func phase() {
        #expect(TodayGlance.phase(start: "07:00", end: "13:00", now: at(6, 35), calendar: calendar) == .upcoming(minutes: 25))
        #expect(TodayGlance.phase(start: "07:00", end: "13:00", now: at(9, 41), calendar: calendar) == .running)
        #expect(TodayGlance.phase(start: "07:00", end: "13:00", now: at(13, 0), calendar: calendar) == .finished)
        #expect(TodayGlance.phase(start: "07:00:00", end: nil, now: at(18, 0), calendar: calendar) == .running)
        #expect(TodayGlance.phase(start: nil, end: nil, now: at(9, 0), calendar: calendar) == .unknown)
        #expect(TodayGlance.Phase.upcoming(minutes: 25).text == "in 25 Min.")
        #expect(TodayGlance.Phase.upcoming(minutes: 260).text == "in 4 Std.")
        #expect(TodayGlance.Phase.running.text == "Jetzt")
        #expect(TodayGlance.Phase.unknown.text == nil)
    }

    @Test("Aufgeklappt: der laufende, sonst der nächste, sonst der letzte Einsatz")
    func focus() {
        #expect(TodayGlance.focusIndex([.finished, .running, .upcoming(minutes: 200)]) == 1)
        #expect(TodayGlance.focusIndex([.finished, .upcoming(minutes: 20), .upcoming(minutes: 200)]) == 1)
        #expect(TodayGlance.focusIndex([.finished, .finished]) == 1)
        #expect(TodayGlance.focusIndex([]) == nil)
    }

    @Test("Entfernung: Meter, Kilometer mit Komma, gerundet")
    func distance() {
        #expect(TodayGlance.distanceText(meters: 830) == "850 m")
        #expect(TodayGlance.distanceText(meters: 1840) == "1,8 km")
        #expect(TodayGlance.distanceText(meters: 23_400) == "23 km")
        let meters = TodayGlance.distance(fromLatitude: 48.5468, longitude: 12.1350, toLatitude: 48.5372, longitude: 12.1521)
        #expect(meters > 1500 && meters < 1800)
    }

    @Test("Kennzahlen: offene Mängel und Aufgaben, Belegreife, Wetter von heute")
    func site() {
        let items = [
            WorkItem(entity(.workItem, ["kind": "defect", "status": "open", "title": "Riss", "dueDate": "2026-11-20"])),
            WorkItem(entity(.workItem, ["kind": "task", "status": "inProgress", "title": "Randdämmung", "dueDate": "2026-11-21"])),
            WorkItem(entity(.workItem, ["kind": "task", "status": "done", "title": "Folie"])),
        ]
        let rooms = [
            Room(entity(.room, ["name": "Bad", "status": "laid"])),
            Room(entity(.room, ["name": "Flur", "status": "planned"])),
        ]
        let diary = [
            DiaryEntry(entity(.diaryEntry, ["day": "2026-11-19", "text": "gestern", "weather": "rain", "temperatureCelsius": 8])),
            DiaryEntry(entity(.diaryEntry, ["day": "2026-11-20", "text": "heute", "weather": "sunny", "temperatureCelsius": 15])),
        ]
        let glance = SiteGlance(items: items, rooms: rooms, inspections: [], diary: diary, today: "2026-11-20")
        #expect(glance.defectsText == "1 Mangel")
        #expect(glance.tasksText == "1 Aufgabe")
        #expect(glance.due == 1)
        #expect(glance.readiness == .laid(laid: 1, total: 2))
        #expect(glance.readiness?.text == "1 von 2 Räumen eingebracht")
        #expect(glance.weatherText == "Sonnig, 15 °C")
        #expect(!glance.isEmpty)

        let none = SiteGlance(items: [], rooms: [], inspections: [], diary: [diary[0]], today: "2026-11-20")
        #expect(none.isEmpty)
    }

    @Test("Belegreife: die letzte CM-Messung zählt vor den Räumen")
    func readiness() {
        let older = Inspection(entity(.inspection, ["kind": "cmMeasurement", "performedAt": "2026-11-10T08:00:00Z",
                                                     "measuredCmPercent": 0.8, "limitCmPercent": 0.3, "isReadyForCovering": false]))
        let newer = Inspection(entity(.inspection, ["kind": "cmMeasurement", "performedAt": "2026-11-19T08:00:00Z",
                                                     "measuredCmPercent": 0.3, "limitCmPercent": 0.3, "isReadyForCovering": true]))
        let rooms = [Room(entity(.room, ["name": "Bad", "status": "ready"]))]
        let glance = SiteGlance(items: [], rooms: rooms, inspections: [older, newer], diary: [], today: "2026-11-20")
        #expect(glance.readiness == .ready(cm: 0.3))
        #expect(glance.readiness?.text == "Belegreif (0,3 CM-%)")
        #expect(glance.readiness?.isReady == true)

        let roomsOnly = SiteGlance(items: [], rooms: rooms + [Room(entity(.room, ["name": "Flur", "status": "laid"]))], inspections: [],
                                   diary: [], today: "2026-11-20")
        #expect(roomsOnly.readiness?.text == "1 von 2 Räumen belegreif")
    }
}
