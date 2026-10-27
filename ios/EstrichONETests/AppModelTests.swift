import EstrichCore
import Foundation
import Testing
@testable import EstrichONE

@MainActor
@Suite("App-Ablauf", .serialized)
struct AppModelTests {
    private let suiteName = "estrichone.apptests.\(UUID().uuidString)"
    private let root = FileManager.default.temporaryDirectory.appending(path: "estrichone-apptests-\(UUID().uuidString)")
    private let secrets = InMemorySecretStore()

    private func environment(deviceStatus: String = "active") -> AppEnvironment {
        AppEnvironment(
            defaults: UserDefaults(suiteName: suiteName)!,
            transport: FakeServer(serverInfoStatus: 200, serverInfoBody: FakeServer.defaultServerInfo,
                                  password: "Richtig-2026", newDeviceStatus: deviceStatus),
            secrets: secrets, storageRoot: root, pinIterations: 1_000,
            personalLockAfter: 15 * 60, poolInactivityTimeout: 5 * 60, skipsDeviceOwnerAuthentication: true)
    }

    private func verifiedServer() async throws -> VerifiedServer {
        let checker = ServerConnectionChecker(transport: environment().transport, appVersion: SemanticVersion(major: 1, minor: 0, patch: 0))
        return try await checker.check(try ServerAddress.parse("estrichone.example.com")).get()
    }

    private func cleanUp() {
        UserDefaults().removePersistentDomain(forName: suiteName)
        try? FileManager.default.removeItem(at: root)
    }

    @Test("Persönliches Gerät: Anmeldung, Sperre, Entsperren, Neustart, Abmelden")
    func personalDevice() async throws {
        defer { cleanUp() }
        let model = AppModel(environment: environment())
        #expect(model.screen == .onboarding)

        try model.confirm(try await verifiedServer(), enrollment: nil)
        #expect(model.screen == .deviceKind)
        model.chooseKind(.personal)
        #expect(model.screen == .signIn(userName: nil))

        await #expect(throws: AccountError.self) { try await model.signIn(login: "m.huber", password: "falsch") }
        try await model.signIn(login: "m.huber", password: "Richtig-2026")
        #expect(model.screen == .home)
        #expect(model.active?.user.displayName == "Max Huber")
        #expect(model.deviceRegistration?.kind == .personal)

        model.lock()
        #expect(model.screen == .appLocked)
        #expect(model.active == nil)
        await model.unlockPersonal()
        #expect(model.screen == .home)

        // Neustart: Server und Gerät sind bekannt, die App startet gesperrt.
        let restarted = AppModel(environment: environment())
        #expect(restarted.screen == .appLocked)

        await model.signOut()
        #expect(model.screen == .signIn(userName: nil))
        #expect(model.deviceState.users.isEmpty)
    }

    @Test("Poolgerät per Einrichtungscode: PIN, Benutzerwechsel, zweite Person, falsche PIN")
    func poolDevice() async throws {
        defer { cleanUp() }
        let model = AppModel(environment: environment())
        let link = try EnrollmentLink.parse("estrichone://enroll?v=1&server=estrichone.example.com&code=K7QX-M2PA&type=pool")

        try model.confirm(try await verifiedServer(), enrollment: link)
        #expect(model.screen == .signIn(userName: nil))
        #expect(model.hasEnrollmentCode)

        try await model.signIn(login: "m.huber", password: "Richtig-2026")
        #expect(model.screen == .choosePIN)
        await #expect(throws: AccountError.pin(.trivial)) { try await model.choosePIN("111111") }
        try await model.choosePIN("482913")
        #expect(model.screen == .home)
        #expect(model.deviceRegistration?.kind == .pool)
        #expect(model.deviceRegistration?.name == "Fahrzeug 3")
        #expect(model.deviceRegistration?.status == .active)

        model.lock()
        #expect(model.screen == .userPicker)
        model.addPerson()
        try await model.signIn(login: "lukas", password: "Richtig-2026")
        try await model.choosePIN("573920")
        #expect(model.active?.user.displayName == "Lukas Brandl")

        model.lock()
        let max = try #require(model.deviceState.users.first { $0.displayName == "Max Huber" })
        model.selectUser(max)
        #expect(model.screen == .pinEntry(max.id))
        await #expect(throws: AccountError.vault(.wrongPIN)) { try await model.unlock(userId: max.id, pin: "573920") }
        try await model.unlock(userId: max.id, pin: "482913")
        #expect(model.active?.user.displayName == "Max Huber")
    }

    @Test("Poolgerät sperrt beim Wechsel in den Hintergrund sofort")
    func poolLocksInBackground() async throws {
        defer { cleanUp() }
        let model = AppModel(environment: environment())
        try model.confirm(try await verifiedServer(), enrollment: EnrollmentLink.parse("estrichone://enroll?v=1&server=estrichone.example.com&code=K7QX-M2PA&type=pool"))
        try await model.signIn(login: "m.huber", password: "Richtig-2026")
        try await model.choosePIN("482913")

        model.didEnterBackground(at: .now)

        #expect(model.screen == .userPicker)
        #expect(model.active == nil)
    }

    @Test("Persönliches Gerät sperrt erst nach 15 Minuten im Hintergrund")
    func personalLocksAfterTimeout() async throws {
        defer { cleanUp() }
        let model = AppModel(environment: environment())
        try model.confirm(try await verifiedServer(), enrollment: nil)
        model.chooseKind(.personal)
        try await model.signIn(login: "m.huber", password: "Richtig-2026")
        let start = Date.now

        model.didEnterBackground(at: start)
        model.didBecomeActive(at: start.addingTimeInterval(60))
        #expect(model.screen == .home)

        model.didEnterBackground(at: start)
        model.didBecomeActive(at: start.addingTimeInterval(15 * 60))
        #expect(model.screen == .appLocked)
    }

    @Test("Poolgerät ohne Einrichtungscode wartet auf Freigabe")
    func poolWaitsForApproval() async throws {
        defer { cleanUp() }
        let model = AppModel(environment: environment())
        try model.confirm(try await verifiedServer(), enrollment: nil)
        model.chooseKind(.pool)
        try await model.signIn(login: "m.huber", password: "Richtig-2026")
        try await model.choosePIN("482913")

        #expect(model.screen == .waitingForApproval)
    }

    @Test("Einrichtungslinks nur ohne eingerichteten Server; ungültige Links mit Meldung")
    func deepLinks() async throws {
        defer { cleanUp() }
        let model = AppModel(environment: environment())
        model.handle(url: try #require(URL(string: "estrichone://enroll?v=1&server=http://estrichone.example.com")))
        #expect(model.notice == ServerAddressError.insecure.message)
        model.hasNotice = false

        let url = try #require(URL(string: "estrichone://enroll?v=1&server=https://estrichone.example.com&code=K7QX-M2PA"))
        model.handle(url: url)
        #expect(model.pendingEnrollment?.enrollmentCode == "K7QX-M2PA")

        model.pendingEnrollment = nil
        try model.confirm(try await verifiedServer(), enrollment: nil)
        model.handle(url: url)
        #expect(model.pendingEnrollment == nil)
        #expect(model.notice != nil)
    }

    @Test("Zurücksetzen löscht Server, Gerät und Personen")
    func reset() async throws {
        defer { cleanUp() }
        let model = AppModel(environment: environment())
        try model.confirm(try await verifiedServer(), enrollment: nil)
        model.chooseKind(.personal)
        try await model.signIn(login: "m.huber", password: "Richtig-2026")

        model.resetApp()

        #expect(model.screen == .onboarding)
        #expect(AppModel(environment: environment()).screen == .onboarding)
        #expect(!FileManager.default.fileExists(atPath: root.path()))
    }

    @Test("App zurücksetzen geht an die App-Sitzung; Herunterfahren gibt Sync, Tresor und Server frei")
    func requestReset() async throws {
        defer { cleanUp() }
        let model = AppModel(environment: environment())
        try model.confirm(try await verifiedServer(), enrollment: nil)
        model.chooseKind(.personal)
        try await model.signIn(login: "m.huber", password: "Richtig-2026")
        #expect(model.active != nil)

        var requests = 0
        model.onResetRequest = { requests += 1 }
        model.requestReset()
        #expect(requests == 1)

        await model.shutdownForWipe()
        #expect(model.active == nil)
        #expect(model.accounts == nil)
        #expect(model.server == nil)
        #expect(model.screen == .onboarding)
    }

    @Test("Zähler an der Glocke wie am App-Symbol")
    func unreadBadge() {
        #expect(UnreadBadge.text(7) == "7")
        #expect(UnreadBadge.text(250) == "99+")
        #expect(UnreadBadge.appBadge(250) == 250)
    }
}
