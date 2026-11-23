import Foundation
import Testing
@testable import EstrichCore

@Suite("Bauakte: Ablauf als Fortschritt")
struct BauakteWorkflowTests {
    @Test("Neue Baustelle: Erstbegehung ist der nächste Schritt, alles andere offen")
    func fresh() {
        let flow = BauakteWorkflow(.init(status: .planned, initialInspection: .open))
        #expect(flow.steps.map(\.kind) == [.initialInspection, .laying, .readiness, .acceptance])
        #expect(flow.steps.map(\.state) == [.next, .open, .open, .open])
        #expect(flow.next?.kind == .initialInspection)
        #expect(flow.next?.detail == "Noch kein Termin")
        #expect(flow.progressText == "0 von 4 erledigt")
    }

    @Test("Teilweise eingebracht: Erstbegehung erledigt, Einbringen ist dran")
    func partlyLaid() {
        let flow = BauakteWorkflow(.init(status: .inProgress, initialInspection: .done, roomCount: 11, roomsLaid: 5))
        #expect(flow.steps[0].state == .done)
        #expect(flow.steps[1].state == .next)
        #expect(flow.steps[1].detail == "5 von 11 Räumen eingebracht")
        #expect(flow.steps[2].detail == "Noch keine CM-Messung")
        #expect(flow.finishedCount == 1)
    }

    @Test("Nicht erforderlich, Trocknung, Messung bestanden, Abnahme offen")
    func readyForAcceptance() {
        let flow = BauakteWorkflow(.init(status: .drying, initialInspection: .waived, lastMeasurementReady: true,
                                         readinessText: "Belegreif (1,6 CM-%)"))
        #expect(flow.steps.map(\.state) == [.skipped, .done, .done, .next])
        #expect(flow.steps[1].detail == "Eingebracht")
        #expect(flow.steps[2].detail == "Belegreif (1,6 CM-%)")
        #expect(flow.next?.kind == .acceptance)
        #expect(flow.progressText == "3 von 4 erledigt")
    }

    @Test("Abgeschlossen: alles erledigt, ohne nächsten Schritt; Module blenden Schritte aus")
    func completedAndModules() {
        let done = BauakteWorkflow(.init(status: .completed, initialInspection: .done, acceptanceCount: 1, acceptanceResult: "Ohne Vorbehalt"))
        #expect(done.next == nil)
        #expect(done.steps.last?.detail == "Abgenommen, ohne vorbehalt")
        let lean = BauakteWorkflow(.init(status: .planned, initialInspection: .open, showsInspections: false, showsAcceptance: false))
        #expect(lean.steps.map(\.kind) == [.laying])
        #expect(lean.next?.kind == .laying)
        let cancelled = BauakteWorkflow(.init(status: .cancelled, initialInspection: .open))
        #expect(cancelled.next == nil)
    }
}
