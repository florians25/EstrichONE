import Foundation
import Testing
@testable import EstrichCore

@Suite("Benachrichtigungen und Freigabe")
struct NotificationTests {
    private func makeClient(_ transport: RoutingTransport) -> AuthorizedClient {
        AuthorizedClient(api: APIClient(server: Samples.server, transport: transport), session: StoredSession(Samples.tokens()), keys: nil,
                         persist: { _ in }, ended: { _ in })
    }

    @Test("Links aus Push und Postfach führen zum Objekt")
    func routes() {
        let id = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000b0001")!
        #expect(NotificationRoute(link: "/bauakten/\(id.uuidString.lowercased())") == .bauakte(id))
        #expect(NotificationRoute(link: "estrichone://bauakte/\(id.uuidString)") == .bauakte(id))
        #expect(NotificationRoute(link: "https://estrich.example/app/helpdesk/\(id.uuidString)") == .ticket(id))
        #expect(NotificationRoute(link: "/planung/cockpit?day=2026-11-04") == .planning(day: "2026-11-04"))
        #expect(NotificationRoute(link: nil, objectType: "Bauakte", objectId: id) == .bauakte(id))
        #expect(NotificationRoute(link: "/irgendwo") == .inbox)
        #expect(NotificationRoute(link: nil) == nil)
    }

    @Test("Postfach als Seite oder Liste; Gelesen-Stand")
    func inbox() throws {
        let item = #"{"id":"0193a1b2-0000-7000-8000-000000000001","title":"Plan freigegeben","body":"Mi 4.11.","link":"/planung","urgency":"high","createdAt":"2026-11-03T16:00:00Z","readAt":null}"#
        let page = try JSONCoding.decoder.decode(NotificationInbox.self, from: Data(#"{"items":[\#(item)],"unreadCount":3}"#.utf8))
        #expect(page.unreadCount == 3 && page.items.first?.isUrgent == true && page.items.first?.route == .planning(day: nil))
        let list = try JSONCoding.decoder.decode(NotificationInbox.self, from: Data("[\(item)]".utf8))
        #expect(list.unreadCount == 1 && list.items.first?.isRead == false)
    }

    @Test("Gerät registrieren, Freigabe, nächster freier Tag und Kolonnentausch an die Endpunkte des Servers")
    func requests() async throws {
        let transport = RoutingTransport()
        let assignment = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000a0001")!
        let crewA = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000c0001")!
        let crewB = UUID(uuidString: "0193a1b2-0000-7000-8000-0000000c0002")!
        transport.on("POST", "/api/v1/notifications/devices", reply: .noContent)
        transport.on("DELETE", "/api/v1/notifications/devices/abc123", reply: .noContent)
        transport.on("POST", "/api/v1/planning/releases",
                     reply: .json(#"[{"day":"2026-11-04","releasedAt":"2026-11-03T16:00:00Z","releasedByName":"Büro","note":"Bitte beachten","changedCount":0,"removed":[]}]"#))
        transport.on("DELETE", "/api/v1/planning/releases/2026-11-04", reply: .noContent)
        transport.on("POST", "/api/v1/planning/assignments/\(assignment.key)/next-free-day",
                     reply: .json(#"{"id":"\#(assignment.key)","day":"2026-11-06","activity":"Estrich"}"#))
        transport.on("POST", "/api/v1/planning/days/2026-11-04/swap-crews",
                     reply: .json(#"{"day":"2026-11-04","canPlan":true,"crews":[]}"#))
        let client = makeClient(transport)

        try await client.registerPushDevice(PushDeviceRegistration(token: PushDeviceRegistration.hex(Data([0xab, 0xc1, 0x23])), sandbox: true,
                                                                   bundleId: "de.estrichone.EstrichONE", deviceName: "iPhone", appVersion: "0.1.0"))
        try await client.unregisterPushDevice(token: "abc123")
        let releases = try await client.releasePlan(from: "2026-11-04", to: "2026-11-04", note: "  Bitte beachten ")
        #expect(releases.first?.note == "Bitte beachten")
        try await client.withdrawRelease(day: "2026-11-04")
        let moved = try await client.apply(.nextFreeDay(id: assignment, title: "Halle"))
        #expect(moved.movedTo == "2026-11-06")
        let swapped = try await client.apply(.swapCrews(day: "2026-11-04", crewA: crewA, crewB: crewB, text: "Huber ↔ Kaya"))
        #expect(swapped.plan?.day == "2026-11-04")

        let bodies = transport.requests.map { $0.jsonBody() }
        #expect(bodies[0]["platform"] as? String == "ios" && bodies[0]["token"] as? String == "abc123" && bodies[0]["sandbox"] as? Bool == true)
        #expect(bodies[2]["from"] as? String == "2026-11-04" && bodies[2]["note"] as? String == "Bitte beachten")
        #expect(bodies[5]["crewA"] as? String == crewA.key && bodies[5]["crewB"] as? String == crewB.key)
        #expect(PlanChange.nextFreeDay(id: assignment, title: "Halle").summary == "Halle auf den nächsten freien Tag legen")
    }
}
