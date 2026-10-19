import EstrichCore
import Foundation
import Testing
@testable import EstrichONE

private struct UnusedTransport: HTTPTransport {
    func get(_ url: URL) async throws -> (Data, HTTPURLResponse) {
        throw URLError(.notConnectedToInternet)
    }
}

@MainActor
@Suite("App-Zustand")
struct AppModelTests {
    private let suiteName = "estrichone.apptests.\(UUID().uuidString)"
    private let defaults: UserDefaults

    init() throws {
        defaults = try #require(UserDefaults(suiteName: suiteName))
    }

    private func makeModel() -> AppModel {
        AppModel(store: ServerStore(defaults: defaults),
                 checker: ServerConnectionChecker(transport: UnusedTransport(), appVersion: SemanticVersion(major: 1, minor: 0, patch: 0)))
    }

    private func server(_ host: String) throws -> VerifiedServer {
        VerifiedServer(
            address: try ServerAddress.parse(host),
            info: ServerInfo(product: "EstrichONE", serverName: "Estrichbau Beispiel GmbH", serverVersion: "1.0.0",
                             apiVersions: ["v1"], minClientVersion: "0.1.0", authMethods: ["local"],
                             instanceEpoch: UUID(), setupRequired: false))
    }

    @Test("ein bestätigter Server bleibt nach einem Neustart eingerichtet")
    func confirmPersists() throws {
        let verified = try server("estrichone.example.com")
        try makeModel().confirm(verified, enrollment: nil)
        #expect(makeModel().server == verified)
        defaults.removePersistentDomain(forName: suiteName)
    }

    @Test("der Einrichtungscode gilt nur für den Server aus demselben QR-Code")
    func enrollmentCodeMatchesServer() throws {
        let model = makeModel()
        let link = try EnrollmentLink.parse("estrichone://enroll?v=1&server=estrichone.example.com&code=k7qx-m2pa")
        try model.confirm(try server("estrichone.example.com"), enrollment: link)
        #expect(model.enrollmentCode == "K7QX-M2PA")

        model.resetServer()
        try model.confirm(try server("anderer.example.com"), enrollment: link)
        #expect(model.enrollmentCode == nil)
        defaults.removePersistentDomain(forName: suiteName)
    }

    @Test("ein Einrichtungslink startet die Prüfung nur, solange kein Server eingerichtet ist")
    func deepLink() throws {
        let model = makeModel()
        let url = try #require(URL(string: "estrichone://enroll?v=1&server=https://estrichone.example.com"))

        model.handle(url: url)
        #expect(model.pendingEnrollment?.server.host == "estrichone.example.com")
        #expect(model.notice == nil)

        model.pendingEnrollment = nil
        try model.confirm(try server("estrichone.example.com"), enrollment: nil)
        model.handle(url: url)
        #expect(model.pendingEnrollment == nil)
        #expect(model.notice != nil)
        defaults.removePersistentDomain(forName: suiteName)
    }

    @Test("ungültige Links werden mit einer Meldung abgelehnt")
    func invalidDeepLink() throws {
        let model = makeModel()
        model.handle(url: try #require(URL(string: "estrichone://enroll?v=1&server=http://estrichone.example.com")))
        #expect(model.pendingEnrollment == nil)
        #expect(model.notice == ServerAddressError.insecure.message)
        model.hasNotice = false
        #expect(model.notice == nil)
    }
}
