import Foundation
import Testing
@testable import EstrichCore

@Suite("Gespeicherter Server")
struct ServerStoreTests {
    private let defaults: UserDefaults
    private let suiteName = "estrichcore.tests.\(UUID().uuidString)"

    init() throws {
        defaults = try #require(UserDefaults(suiteName: suiteName))
    }

    private var sample: VerifiedServer {
        get throws {
            VerifiedServer(
                address: try ServerAddress.parse("estrichone.firma.de"),
                info: ServerInfo(product: "EstrichONE", serverName: "Estrichbau Müller GmbH", serverVersion: "1.0.0",
                                 apiVersions: ["v1"], minClientVersion: "0.1.0", authMethods: ["local"],
                                 instanceEpoch: UUID(), setupRequired: false))
        }
    }

    @Test("übersteht einen Neustart")
    func roundTrip() throws {
        let server = try sample
        try ServerStore(defaults: defaults).save(server)
        #expect(ServerStore(defaults: defaults).load() == server)
        defaults.removePersistentDomain(forName: suiteName)
    }

    @Test("Zurücksetzen entfernt den Server")
    func clear() throws {
        let store = ServerStore(defaults: defaults)
        try store.save(try sample)
        store.clear()
        #expect(store.load() == nil)
        defaults.removePersistentDomain(forName: suiteName)
    }

    @Test("unlesbare Einträge gelten als nicht eingerichtet")
    func corrupt() {
        defaults.set(Data("kaputt".utf8), forKey: ServerStore.key)
        #expect(ServerStore(defaults: defaults).load() == nil)
        defaults.removePersistentDomain(forName: suiteName)
    }
}
