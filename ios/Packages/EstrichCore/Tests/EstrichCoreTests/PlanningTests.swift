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
