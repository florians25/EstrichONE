import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Mein Einsatz")
struct PlanningTests {
    private func makeStore() throws -> LocalStore {
        let directory = FileManager.default.temporaryDirectory.appending(path: "planning-\(UUID().uuidString)")
        return LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: SymmetricKey(size: .bits256),
                                                          migrations: LocalStore.schema))
    }

    private static let json = """
        {"from":"2026-11-02","to":"2026-11-08","assignments":[
         {"id":"01a00000-0000-7000-8000-000000000002","day":"2026-11-02","startTime":"09:30:00","activity":"Randdämmung","bauakteId":"01a00000-0000-7000-8000-0000000000b2",
          "bauakteNumber":null,"bauakteTitle":"EFH Müller","bauakteAddress":null,"crewId":null,"crewName":null,"crewColor":null,"people":[],"resources":[],
          "notes":null,"warnings":[],"version":1},
         {"id":"01a00000-0000-7000-8000-000000000001","day":"2026-11-02","startTime":"07:00:00","activity":"Estrich einbringen EG","bauakteId":"01a00000-0000-7000-8000-0000000000b1",
          "bauakteNumber":"2026-014","bauakteTitle":"Halle Nord","bauakteAddress":"Landshut","crewId":"01a00000-0000-7000-8000-0000000000c1","crewName":"Kolonne Huber",
          "crewColor":"#0062CC","people":[{"id":"01a00000-0000-7000-8000-0000000000a1","name":"Max Huber"}],
          "resources":[{"id":"01a00000-0000-7000-8000-0000000000d1","name":"Mixokret 2","kind":"pump"}],"notes":null,"warnings":["Max Huber ist im Urlaub."],"version":1}],
         "absences":[]}
        """

    @Test("Liest die Serverantwort, sortiert nach Beginn und hält den Stand offline")
    func decodeAndCache() async throws {
        let range = try JSONCoding.decoder.decode(PlanningRange.self, from: Data(Self.json.utf8))
        let store = try makeStore()
        let planning = MyPlanning(store: store, now: { Date(timeIntervalSince1970: 1_793_000_000) })
        #expect(await planning.cached() == nil)

        // Ohne Server (client nil) bleibt es beim gespeicherten Stand; zuerst einen Stand speichern.
        try await store.setMeta("my_planning", String(decoding: try JSONCoding.encoder.encode(StoredProbe(range: range, loadedAt: Date())), as: UTF8.self))
        let snapshot = try #require(await planning.load(using: nil))
        #expect(snapshot.isCurrent == false)
        let today = snapshot.assignments(on: "2026-11-02")
        #expect(today.map(\.activity) == ["Estrich einbringen EG", "Randdämmung"])
        #expect(today.first?.startText == "07:00")
        #expect(today.first?.resources.first?.name == "Mixokret 2")
        #expect(today.first?.warnings == ["Max Huber ist im Urlaub."])
    }

    private struct StoredProbe: Encodable {
        let range: PlanningRange
        let loadedAt: Date
    }
}

@Suite("Wer fährt wohin nach Kolonnen")
struct DispatchCrewTests {
    @Test("Kolonnen mit Fahrzeugen, Abwesenden und Doppelbelegung; ältere Antwort ohne Kolonnen bleibt lesbar")
    func decodeCrews() throws {
        let site = "0193a1b2-de00-7000-8000-0000000b0001"
        let other = "0193a1b2-de00-7000-8000-0000000b0002"
        let json = """
        {"day":"2026-10-21","canPlan":true,"mySiteIds":["\(site)"],"people":[],"vehicles":[],"appointments":[],
         "sites":[{"bauakteId":"\(site)","number":"2026-041","title":"EFH","address":null,"latitude":null,"longitude":null,
           "siteContactName":null,"siteContactPhone":null,"startTime":"07:00:00","jobs":[],"people":[],"vehicles":[],
           "warnings":["Kolonne Huber ist an dem Tag auch auf Halle eingeplant."],"navigationUrl":null,
           "crews":[{"id":"0193a1b2-de00-7000-8000-000000006001","name":"Kolonne Huber","color":"#E07A1F",
                     "assignmentId":"0193a1b2-de00-7000-8000-000000003001","activity":"Estrich EG","startTime":"07:00:00","endTime":"12:00:00"}]}],
         "crews":[{"id":"0193a1b2-de00-7000-8000-000000006001","name":"Kolonne Huber","color":"#E07A1F","leaderName":"Max Huber",
                   "memberCount":3,"vehicles":[{"id":"0193a1b2-de00-7000-8000-000000005001","name":"Pumpe 1","kind":"machine"}],
                   "siteIds":["\(site)","\(other)"],"warnings":["Überschneidung: EFH und Halle."],"absentMembers":[{"id":"0193a1b2-de00-7000-8000-000000004004","name":"Tobias","note":"Urlaub"}]}]}
        """
        let board = try JSONCoding.decoder.decode(DispatchBoard.self, from: Data(json.utf8))
        let crew = try #require(board.crews.first)
        #expect(crew.isDoubleBooked && crew.memberCount == 3 && crew.vehicles.first?.name == "Pumpe 1")
        #expect(crew.absentMembers.first?.note == "Urlaub")
        #expect(board.sites.first?.crews.first?.timeText == "07:00–12:00")
        #expect(board.unassignedCrews.isEmpty)

        let old = #"{"day":"2026-10-21","sites":[],"people":[],"vehicles":[],"mySiteIds":[],"canPlan":false}"#
        #expect(try JSONCoding.decoder.decode(DispatchBoard.self, from: Data(old.utf8)).crews.isEmpty)
    }

    @Test("Kolonne verlegen bzw. zusätzlich einplanen: Ziel crew, keep, Tätigkeit und Beginn")
    func encodeMove() throws {
        let move = DispatchMove(target: .crew, id: UUID(), to: UUID(), from: nil, keep: true, activity: "Estrich OG", startTime: "07:30:00",
                                endTime: "12:00:00")
        let object = try #require(try JSONSerialization.jsonObject(with: JSONCoding.encoder.encode(move)) as? [String: Any])
        #expect(object["target"] as? String == "crew")
        #expect(object["keep"] as? Bool == true)
        #expect(object["activity"] as? String == "Estrich OG" && object["startTime"] as? String == "07:30:00")
        #expect(object["endTime"] as? String == "12:00:00")
        let pool = try JSONCoding.decoder.decode([DispatchPoolEntry].self, from: Data("""
            [{"bauakteId":"0193a1b2-de00-7000-8000-0000000b0001","number":"2026-041","title":"EFH","address":null,"status":"planned","crewIds":[]}]
            """.utf8))
        #expect(pool.first?.title == "EFH")
    }
}
