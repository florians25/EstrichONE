import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import Testing
@testable import EstrichCore

@Suite("Schnelldisposition vom Handy", .serialized)
struct DayPlanTests {
    static let crew = "01a00000-0000-7000-8000-0000000000c1"
    static let other = "01a00000-0000-7000-8000-0000000000c2"
    static let stopA = "01a00000-0000-7000-8000-000000000001"
    static let stopB = "01a00000-0000-7000-8000-000000000002"

    static let json = """
        {"day":"2026-11-02","holiday":null,"canPlan":true,"myCrewIds":["\(other)"],
         "release":{"day":"2026-11-02","releasedAt":"2026-10-30T15:00:00Z","releasedByName":"Büro","note":null,"changedCount":2,
           "removed":[{"assignmentId":"01a00000-0000-7000-8000-000000000009","version":1,"title":"EFH Alt","crewName":"Kolonne Huber","activity":"Estrich"}]},
         "crews":[
          {"id":"\(crew)","name":"Kolonne Huber","color":"#0062CC","leaderName":"Max Huber",
           "members":[{"id":"01a00000-0000-7000-8000-0000000000a1","name":"Max Huber","absenceNote":null},
                      {"id":"01a00000-0000-7000-8000-0000000000a2","name":"Tobias Lang","absenceNote":"Urlaub"}],
           "vehicles":[{"id":"01a00000-0000-7000-8000-0000000000d1","name":"Sprinter","kind":"vehicle"}],
           "rosterChanged":true,"rosterNote":null,"isMine":false,"warnings":["Tobias Lang ist im Urlaub."],
           "stops":[
            {"assignmentId":"\(stopB)","order":2,"sequence":2,"startTime":"11:00:00","endTime":null,"kind":"screed","activity":"Estrich","bauakteId":"01a00000-0000-7000-8000-0000000000b2",
             "number":"2026-002","title":"EFH Müller","address":"Ahornweg 3, 84028 Landshut","latitude":null,"longitude":null,"siteContactName":"Herr Müller",
             "siteContactPhone":"0171 1","navigationUrl":null,"notes":"Schlüssel beim Nachbarn","people":[],"resources":[],"warnings":[],"change":"new","version":1},
            {"assignmentId":"\(stopA)","order":1,"sequence":1,"startTime":"07:00:00","endTime":"10:00:00","kind":"screed","activity":"Randdämmung","bauakteId":"01a00000-0000-7000-8000-0000000000b1",
             "number":null,"title":"Halle Nord","address":null,"latitude":48.5,"longitude":12.1,"siteContactName":null,"siteContactPhone":null,"navigationUrl":null,
             "notes":null,"people":[],"resources":[],"warnings":["Pumpe doppelt belegt."],"change":null,"version":3}]},
          {"id":"\(other)","name":"Kolonne Kaya","color":null,"leaderName":null,"members":[],"vehicles":[],"rosterChanged":false,
           "rosterNote":null,"isMine":true,"stops":[],"warnings":[]}],
         "withoutCrew":[],"absent":[{"id":"01a00000-0000-7000-8000-0000000000a2","name":"Tobias Lang","absenceNote":"Urlaub"}],
         "free":[{"id":"01a00000-0000-7000-8000-0000000000a3","name":"Jonas Wirth"}]}
        """

    @Test("Tagesplan je Kolonne: Reihenfolge, Uhrzeit, Navigation, Konflikte, Freigabe, meine Kolonne zuerst")
    func decode() throws {
        let plan = try JSONCoding.decoder.decode(DayPlan.self, from: Data(Self.json.utf8))
        #expect(plan.sortedCrews.map(\.name) == ["Kolonne Kaya", "Kolonne Huber"])
        let huber = try #require(plan.crew(UUID(uuidString: Self.crew)!))
        #expect(huber.stops.map(\.title) == ["Halle Nord", "EFH Müller"])
        #expect(huber.stops[0].timeText == "07:00 bis 10:00" && huber.stops[1].timeText == "ab 11:00")
        #expect(huber.stops[0].mapsURL?.absoluteString == "https://maps.apple.com/?daddr=48.5,12.1")
        #expect(huber.stops[1].mapsURL?.absoluteString.contains("Ahornweg") == true && huber.stops[1].isChanged)
        #expect(huber.available.map(\.name) == ["Max Huber"] && huber.absent.map(\.name) == ["Tobias Lang"])
        #expect(huber.conflicts == ["Tobias Lang ist im Urlaub.", "Pumpe doppelt belegt."])
        #expect(plan.release?.changedCount == 2 && plan.release?.removed.first?.title == "EFH Alt")
        #expect(plan.free.map(\.name) == ["Jonas Wirth"] && plan.stopCount == 2)
        #expect(plan.crew(of: UUID(uuidString: Self.stopB)!)?.name == "Kolonne Huber")
    }

    @Test("Kurzfassung für Heute: Kolonnen mit Einsätzen und Freigabestand")
    func summary() throws {
        let plan = try JSONCoding.decoder.decode(DayPlan.self, from: Data(Self.json.utf8))
        #expect(DispatchSummary.text(plan) == "1 Kolonne, 2 Änderungen seit Freigabe")
        #expect(DispatchSummary.tone(plan) == .warning)
        let released = try JSONCoding.decoder.decode(DayPlan.self, from: Data(Self.json.replacingOccurrences(of: "\"changedCount\":2", with: "\"changedCount\":0").utf8))
        #expect(DispatchSummary.text(released) == "1 Kolonne, freigegeben")
        #expect(DispatchSummary.tone(released) == .success)
        let draft = try JSONCoding.decoder.decode(DayPlan.self, from: Data("""
            {"day":"2026-11-02","crews":[
             {"id":"\(Self.crew)","name":"A","stops":[{"assignmentId":"\(Self.stopA)","bauakteId":"\(Self.stopA)","title":"X"}]},
             {"id":"\(Self.other)","name":"B","stops":[{"assignmentId":"\(Self.stopB)","bauakteId":"\(Self.stopB)","title":"Y"}]}]}
            """.utf8))
        #expect(DispatchSummary.text(draft) == "2 Kolonnen, noch nicht freigegeben")
        #expect(DispatchSummary.tone(draft) == .neutral)
        let empty = try JSONCoding.decoder.decode(DayPlan.self, from: Data(#"{"day":"2026-11-02"}"#.utf8))
        #expect(DispatchSummary.text(empty) == "Noch keine Einsätze geplant")
    }

    @Test("Einplanen aus der Bauakte: Vorgaben für Tätigkeit, Kolonne und Uhrzeit, Auslastung, nächster Einsatz, Bestätigung")
    func quickPlan() throws {
        let plan = try JSONCoding.decoder.decode(DayPlan.self, from: Data(Self.json.utf8))
        let huber = try #require(plan.crew(UUID(uuidString: Self.crew)!))
        let kaya = try #require(plan.crew(UUID(uuidString: Self.other)!))
        #expect(QuickPlan.defaultActivity(for: .inquiry) == "Erstbegehung")
        #expect(QuickPlan.defaultActivity(for: .planned) == "Estrich einbringen" && QuickPlan.defaultActivity(for: nil) == "Estrich einbringen")
        #expect(QuickPlan.defaultActivity(for: .drying) == "CM-Messung und Belegreife")
        #expect(QuickPlan.defaultActivity(for: .completed) == "Nacharbeiten")
        #expect(QuickPlan.activities.contains(QuickPlan.defaultActivity(for: .drying)))
        #expect(QuickPlan.loadText(kaya.stops.count) == "frei" && QuickPlan.loadText(1) == "1 Einsatz")
        #expect(QuickPlan.loadText(huber.stops.count) == "2 Einsätze")
        // Beginn nach der letzten Station, ohne Station 07:00.
        #expect(QuickPlan.suggestedStart(for: huber) == "11:00:00" && QuickPlan.suggestedStart(for: kaya) == "07:00:00")
        #expect(QuickPlan.suggestedStart(for: nil) == "07:00:00")
        // Vorbelegung: gewünschte Kolonne, sonst die eigene; unbekannte Kolonne zählt nicht.
        #expect(QuickPlan.preselectedCrew(in: plan, wanted: huber.id) == huber.id)
        #expect(QuickPlan.preselectedCrew(in: plan, wanted: nil) == kaya.id)
        #expect(QuickPlan.preselectedCrew(in: plan, wanted: UUID.v7()) == kaya.id)
        let single = try JSONCoding.decoder.decode(DayPlan.self, from: Data("""
            {"day":"2026-11-02","crews":[{"id":"\(Self.crew)","name":"A","stops":[]}]}
            """.utf8))
        #expect(QuickPlan.preselectedCrew(in: single, wanted: nil) == huber.id)
        let none = try JSONCoding.decoder.decode(DayPlan.self, from: Data("""
            {"day":"2026-11-02","crews":[{"id":"\(Self.crew)","name":"A","stops":[]},{"id":"\(Self.other)","name":"B","stops":[]}]}
            """.utf8))
        #expect(QuickPlan.preselectedCrew(in: none, wanted: nil) == nil)
        // Nächster Einsatz der Bauakte am Tag mit Kolonne (Grundlage für „Verschieben“).
        let planned = try #require(QuickPlan.planned(UUID(uuidString: "01a00000-0000-7000-8000-0000000000b2")!, in: plan))
        #expect(planned.day == "2026-11-02" && planned.stop.title == "EFH Müller" && planned.crew?.name == "Kolonne Huber")
        #expect(QuickPlan.planned(UUID.v7(), in: plan) == nil)
        #expect(QuickPlan.confirmation(day: "2026-11-02", today: "2026-11-02", start: "07:30:00", crewName: "Kolonne Huber")
                == "Eingeplant: Heute, 07:30, Kolonne Huber")
        #expect(QuickPlan.confirmation(day: "2026-11-03", today: "2026-11-02", start: nil, crewName: "Kolonne Kaya")
                == "Eingeplant: Morgen, Kolonne Kaya")
        // Ohne Verbindung für einen noch nie geladenen Tag: nur die Kolonnen, ohne Einsätze und Abwesenheiten.
        let bare = plan.crewsOnly(day: "2026-11-09")
        #expect(bare.day == "2026-11-09" && bare.crews.map(\.name) == plan.crews.map(\.name) && bare.stopCount == 0)
        #expect(bare.release == nil && bare.crews.allSatisfy { $0.absent.isEmpty && $0.warnings.isEmpty })
        #expect(bare.crew(kaya.id)?.isMine == true)
    }

    private func makeClient(_ transport: RoutingTransport) -> AuthorizedClient {
        AuthorizedClient(api: APIClient(server: Samples.server, transport: transport), session: StoredSession(Samples.tokens()), keys: nil,
                         persist: { _ in }, ended: { _ in })
    }

    @Test("Änderungen gehen an dieselben Endpunkte wie im Web; ohne Verbindung vorgemerkt, Ablehnungen bleiben sichtbar")
    func outbox() async throws {
        let directory = FileManager.default.temporaryDirectory.appending(path: "dayplan-\(UUID().uuidString)")
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: SymmetricKey(size: .bits256),
                                                               migrations: LocalStore.schema))
        let outbox = PlanningOutbox(store: store)
        let crew = UUID(uuidString: Self.crew)!
        let site = UUID.v7()
        let assignment = UUID.v7()
        try await outbox.add(.assign(id: assignment, bauakteId: site, title: "EFH Müller", crewId: crew, day: "2026-11-02",
                                     activity: "Estrich einbringen", startTime: "07:00:00", endTime: nil))
        try await outbox.add(.order(crewId: crew, day: "2026-11-02", assignmentIds: [UUID(uuidString: Self.stopB)!, UUID(uuidString: Self.stopA)!],
                                    startTimes: ["07:00:00", nil]))
        try await outbox.add(.remove(id: UUID(uuidString: Self.stopA)!, title: "Halle Nord"))
        #expect(await outbox.entries().map(\.change.summary).first?.hasPrefix("EFH Müller einplanen (") == true)

        // Ohne Verbindung bleibt alles liegen.
        let offline = RoutingTransport()
        #expect(await outbox.flush(using: makeClient(offline)) == PlanningOutbox.FlushResult(sent: 0, warnings: [], failed: 0))
        #expect(await outbox.entries().count == 3)

        let transport = RoutingTransport()
        transport.on("POST", "/api/v1/planning/batch") { request in
            let operations = request.jsonBody()["operations"] as? [[String: Any]] ?? []
            let kind = operations.first?["op"] as? String
            if kind == "delete" { return .problem(404, code: "NOT_FOUND", detail: "Der Einsatz existiert nicht mehr.") }
            let assignmentBody = operations.first?["assignment"] as? [String: Any]
            let crewId = assignmentBody?["crewId"] as? String, day = assignmentBody?["day"] as? String
            let start = assignmentBody?["startTime"] as? String
            #expect(kind == "upsert" && crewId == Self.crew)
            #expect(day == "2026-11-02" && start == "07:00:00")
            return .json(#"{"assignments":[],"deletedIds":[],"warnings":["Max Huber ist im Urlaub."],"dryRun":false}"#)
        }
        transport.on("PUT", "/api/v1/planning/crews/\(Self.crew)/days/2026-11-02/order") { request in
            let first = (request.jsonBody()["assignmentIds"] as? [String])?.first
            #expect(first == Self.stopB)
            return .json(Self.json)
        }
        let result = await outbox.flush(using: makeClient(transport))
        #expect(result.sent == 2 && result.failed == 1 && result.warnings == ["Max Huber ist im Urlaub."])
        let left = await outbox.entries()
        #expect(left.count == 1 && left[0].error == "Der Einsatz existiert nicht mehr.")
        try await outbox.discard(left[0].id)
        #expect(await outbox.entries().isEmpty)
    }

    @Test("Verschieben auf anderen Tag und andere Kolonne, Tausch in der Tagesbesetzung")
    func moveAndSwap() async throws {
        let transport = RoutingTransport()
        transport.on("POST", "/api/v1/planning/batch") { _ in
            .json(#"{"assignments":[],"deletedIds":[],"warnings":[],"dryRun":false}"#)
        }
        transport.on("POST", "/api/v1/planning/crews/\(Self.crew)/days/2026-11-02/swap") { _ in .json(Self.json) }
        let client = makeClient(transport)
        let move = PlanChange.move(id: UUID(uuidString: Self.stopA)!, title: "Halle Nord", day: "2026-11-03", crewId: UUID(uuidString: Self.other)!,
                                   startTime: "08:00:00", endTime: nil, setTime: true)
        #expect(try await client.check(move).isEmpty)
        #expect(try await client.apply(move).warnings.isEmpty)
        let swap = PlanChange.swap(crewId: UUID(uuidString: Self.crew)!, day: "2026-11-02", fromUserId: UUID(uuidString: "01a00000-0000-7000-8000-0000000000a2"),
                                   toUserId: UUID(uuidString: "01a00000-0000-7000-8000-0000000000a3"), fromResourceId: nil, toResourceId: nil,
                                   text: "Jonas Wirth statt Tobias Lang")
        #expect(try await client.apply(swap).plan?.crews.count == 2)
        #expect(swap.summary == "Jonas Wirth statt Tobias Lang")
        let bodies = transport.requests.map { $0.jsonBody() }
        let move0 = (bodies[0]["operations"] as? [[String: Any]])?.first ?? [:]
        #expect(bodies[0]["dryRun"] as? Bool == true && bodies[1]["dryRun"] as? Bool != true)
        #expect(move0["op"] as? String == "move" && move0["day"] as? String == "2026-11-03" && move0["changeCrew"] as? Bool == true)
        #expect(move0["crewId"] as? String == Self.other && move0["setTime"] as? Bool == true && move0["startTime"] as? String == "08:00:00")
        #expect(bodies[2]["fromUserId"] as? String == "01a00000-0000-7000-8000-0000000000a2")
        #expect(bodies[2]["toUserId"] as? String == "01a00000-0000-7000-8000-0000000000a3")
    }
}
