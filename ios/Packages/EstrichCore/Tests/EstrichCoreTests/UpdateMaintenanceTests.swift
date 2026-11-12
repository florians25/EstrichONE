import Foundation
import Testing
@testable import EstrichCore

@Suite("Update-Ablauf: Ankündigung, Wartungsbildschirm, Freigabe")
struct UpdateMaintenanceTests {
    private let now = Date(timeIntervalSince1970: 1_794_477_600) // 2026-11-12T10:00:00Z

    private func at(_ seconds: Double) -> String { ISO8601DateFormatter().string(from: now.addingTimeInterval(seconds)) }

    private func state(_ phase: String, _ from: Double, _ to: Double?, message: String? = nil) -> UpdateMaintenance {
        UpdateMaintenance(id: "u1", phase: phase, fromVersion: "1.0.15", targetVersion: "1.0.16", startedAt: at(-120),
                          phaseStartedAt: at(from), phaseEndsAt: to.map(at), message: message)
    }

    @Test("Feld update in /system/notices, unbekannte Phasen und unvollständige Angaben werden ignoriert")
    func decode() throws {
        let json = """
        {"announcements":[],"maintenance":null,"serverTime":"2026-11-12T10:00:00Z","update":{"id":"u1","phase":"announce",
        "fromVersion":"1.0.15","targetVersion":"1.0.16","startedAt":"2026-11-12T10:00:00Z","phaseStartedAt":"2026-11-12T10:00:00Z",
        "phaseEndsAt":"2026-11-12T10:01:00Z","expiresAt":"2026-11-12T16:00:00Z","source":"update.sh"}}
        """
        let notices = try JSONCoding.decoder.decode(SystemNotices.self, from: Data(json.utf8))
        #expect(notices.activeUpdate?.phase == "announce")
        #expect(notices.activeUpdate?.targetVersion == "1.0.16")
        let odd = try JSONCoding.decoder.decode(SystemNotices.self, from: Data(#"{"announcements":[],"update":{"phase":"bunt"}}"#.utf8))
        #expect(odd.activeUpdate == nil)
        let none = try JSONCoding.decoder.decode(SystemNotices.self, from: Data(#"{"announcements":[]}"#.utf8))
        #expect(none.activeUpdate == nil)
    }

    @Test("Ankündigung mit Countdown, danach Wartungsbildschirm")
    func announce() {
        #expect(UpdatePresentation.clock(59) == "0:59")
        #expect(UpdatePresentation.clock(59.2) == "1:00")
        #expect(UpdatePresentation.clock(-4) == "0:00")
        let view = UpdatePresentation.screen(state("announce", -1, 59), now: now)
        #expect(view.banner && !view.page)
        #expect(view.bannerText == "Update auf Version 1.0.16 beginnt in 0:59 – bitte Arbeit speichern. Offene Änderungen werden jetzt übertragen.")
        #expect(view.steps.map(\.state) == [.now, .todo, .todo])
        let late = UpdatePresentation.screen(state("announce", -61, -1), now: now)
        #expect(late.page && late.title == "Update wird eingespielt")
    }

    @Test("Phasen, Fortschritt, Rücknahme, Fehlschlag und Hinweis nach der Freigabe")
    func phases() {
        let update = UpdatePresentation.screen(state("update", -30, 30), now: now)
        #expect(update.countdownLabel == "Mindestens noch" && update.countdown == "0:30")
        #expect(abs(update.progress - 0.5) < 0.01)
        #expect(update.steps.map(\.state) == [.done, .now, .todo])
        let finish = UpdatePresentation.screen(state("finish", -15, 45), now: now)
        #expect(finish.title == "Update wird abgeschlossen" && finish.countdownLabel == "Freigabe in" && finish.countdown == "0:45")
        #expect(UpdatePresentation.screen(state("rollback", 0, nil), now: now).steps[2].label == "Rücknahme")
        let failed = UpdatePresentation.screen(state("failed", 0, nil, message: "Datenbank startet nicht."), now: now)
        #expect(failed.failed && failed.text == "Datenbank startet nicht. Der Administrator ist informiert.")
        #expect(failed.countdownLabel == "Gesperrt, bis das Problem behoben ist")
        #expect(UpdatePresentation.doneText(version: "1.0.16", failed: false) == "EstrichONE wurde auf Version 1.0.16 aktualisiert. Bitte erneut anmelden.")
    }
}
