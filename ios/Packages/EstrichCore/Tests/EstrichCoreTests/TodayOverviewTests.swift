import Foundation
import Testing
@testable import EstrichCore

@Suite("Heute: Tageszusammenfassung, Leistung, Freitext")
struct TodayOverviewTests {
    private let siteA = UUID(uuidString: "0193a1b2-0000-7000-8000-00000000000a")!
    private let siteB = UUID(uuidString: "0193a1b2-0000-7000-8000-00000000000b")!
    private let crewId = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000000c1")!
    private let absentId = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000000d1")!

    private func board() throws -> DispatchBoard {
        let json = """
        {"day":"2026-11-20","sites":[],"vehicles":[],"mySiteIds":["\(siteA.uuidString)"],"canPlan":false,
         "people":[{"id":"\(UUID().uuidString)","name":"Max Huber","crewName":"Kolonne Huber","siteIds":["\(siteA.uuidString)"]},
                   {"id":"\(absentId.uuidString)","name":"Tobias Lang","crewName":"Kolonne Huber","siteIds":[]}],
         "crews":[{"id":"\(crewId.uuidString)","name":"Kolonne Huber","color":"#1E6FD9","leaderName":"Max Huber","memberCount":3,
                   "vehicles":[{"id":"\(UUID().uuidString)","name":"MB Sprinter","kind":"vehicle"}],
                   "siteIds":["\(siteA.uuidString)"],
                   "absentMembers":[{"id":"\(absentId.uuidString)","name":"Tobias Lang","note":"Urlaub"}],"warnings":[]}]}
        """
        return try JSONCoding.decoder.decode(DispatchBoard.self, from: Data(json.utf8))
    }

    @Test("Einsätze, Beginn, Kolonne mit Besetzung und Fläche aus der eigenen Planung")
    func fromAssignments() throws {
        let jobs = [
            TodayOverview.Job(bauakteId: siteB, start: "12:30", crewName: "Kolonne Huber", people: ["Max Huber", "Anna Berg"], resources: ["Estrichpumpe"]),
            TodayOverview.Job(bauakteId: siteA, start: "07:00", crewName: "Kolonne Huber", people: ["Max Huber"], resources: ["MB Sprinter"]),
        ]
        let overview = TodayOverview.make(jobs: jobs, board: try board(), areas: [siteA: 120, siteB: 85.5])
        #expect(overview.jobCount == 2)
        #expect(overview.jobsText == "2 Einsätze auf 2 Baustellen")
        #expect(overview.firstStart == "07:00")
        #expect(overview.crewName == "Kolonne Huber")
        #expect(overview.crewColor == "#1E6FD9")
        #expect(overview.people == ["Max Huber", "Anna Berg"])
        #expect(overview.crewText == "Kolonne Huber, 2 Personen")
        #expect(overview.vehicles == ["Estrichpumpe", "MB Sprinter"])
        #expect(overview.absent == ["Tobias Lang, Urlaub"])
        #expect(overview.areaText == "205,5 m²")
    }

    @Test("Ohne Personen im Einsatz: Besetzung und Fahrzeuge der Kolonne laut Disposition, ohne Abwesende")
    func fromBoard() throws {
        let overview = TodayOverview.make(jobs: [TodayOverview.Job(bauakteId: siteA, start: nil, crewName: nil)], board: try board())
        #expect(overview.jobsText == "1 Einsatz")
        #expect(overview.crewName == "Kolonne Huber")
        #expect(overview.people == ["Max Huber"])
        #expect(overview.vehicles == ["MB Sprinter"])
        #expect(overview.firstStart == nil)
        #expect(overview.areaText == nil)
    }

    @Test("Ohne Einsätze leer")
    func empty() {
        let overview = TodayOverview.make(jobs: [], board: nil)
        #expect(overview.isEmpty)
        #expect(overview.crewText == nil)
    }

    @Test("Leistung: Fläche, Estrichart, Dicke, Heizung")
    func keyFacts() {
        var project = BauakteProject()
        #expect(project.keyFactsText == nil)
        project.totalArea = 120
        project.screedType = .ct
        project.nominalThicknessMm = 65
        project.underfloorHeating = true
        #expect(project.keyFactsText == "120 m², Zementestrich CT, 65 mm, Fußbodenheizung")
        project.underfloorHeating = false
        project.totalArea = 0
        #expect(project.keyFactsText == "Zementestrich CT, 65 mm")
    }

    @Test("Freitext: Zeilenumbrüche bleiben, Leerzeilen zusammengefasst, lange Texte erkannt")
    func freeText() {
        #expect(FreeText.clean("  \n ") == nil)
        #expect(FreeText.clean(nil) == nil)
        #expect(FreeText.clean(" Schlüssel beim Nachbarn\r\nHaus 3\n\n\n\nPumpe an der Einfahrt ") == "Schlüssel beim Nachbarn\nHaus 3\n\nPumpe an der Einfahrt")
        #expect(!FreeText.isLong("Zufahrt nur bis 7,5 t"))
        #expect(FreeText.isLong((1...8).map { "Zeile \($0)" }.joined(separator: "\n")))
        #expect(FreeText.isLong(String(repeating: "Estrich ", count: 60)))
    }
}
