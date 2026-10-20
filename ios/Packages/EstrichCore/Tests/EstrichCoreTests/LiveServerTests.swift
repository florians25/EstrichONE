import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import Testing
@testable import EstrichCore

/// Gegen einen echten, eingerichteten Server (CI-Job „EstrichCore gegen Server“).
/// `ESTRICHONE_LIVE_SERVER`, `ESTRICHONE_LIVE_LOGIN`, `ESTRICHONE_LIVE_PASSWORD` (Administrator).
@Suite("Echter Server", .serialized, .enabled(if: ProcessInfo.processInfo.environment["ESTRICHONE_LIVE_SERVER"] != nil))
struct LiveServerTests {
    private let environment = ProcessInfo.processInfo.environment
    private let server: ServerAddress
    private let login: String
    private let password: String
    private let description = DeviceDescription(name: "Integrationstest", model: "CI", osVersion: "Linux", appVersion: "0.2.0")

    init() throws {
        server = try ServerAddress.parse(environment["ESTRICHONE_LIVE_SERVER"] ?? "", allowInsecureLocal: true)
        login = environment["ESTRICHONE_LIVE_LOGIN"] ?? "admin"
        password = environment["ESTRICHONE_LIVE_PASSWORD"] ?? ""
    }

    private func makeService(keys: DeviceKeys = SoftwareDeviceKeys()) -> AccountService {
        AccountService(api: APIClient(server: server, transport: URLSession(configuration: .ephemeral)),
                       storage: Samples.temporaryStorage(), keys: keys, secrets: InMemorySecretStore(), pinIterations: 1_000)
    }

    private func authorized(_ service: AccountService, _ account: UnlockedAccount, ended: Recorder<SessionEnd> = Recorder()) -> AuthorizedClient {
        AuthorizedClient(api: service.api, session: account.session, keys: service.keys, persist: { _ in }, ended: { ended.append($0) })
    }

    @Test("Verbindungsprüfung erkennt den Server")
    func serverCheck() async throws {
        let result = await ServerConnectionChecker(transport: URLSession(configuration: .ephemeral),
                                                   appVersion: SemanticVersion(major: 0, minor: 2, patch: 0)).check(server)
        let verified = try result.get()
        #expect(verified.info.product == "EstrichONE")
        #expect(verified.info.setupRequired == false)
    }

    @Test("Persönliches Gerät: Registrierung, Erneuerung mit Gerätesignatur, Abmeldung")
    func personalDevice() async throws {
        let service = makeService()
        let tokens = try await service.signIn(login: login, password: password, clientName: "CI")
        let registration = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        #expect(registration.status == .active)
        let account = try service.createVault(for: tokens, pin: nil)
        let client = authorized(service, account)

        try await client.refreshNow()
        let device: DeviceResponse = try await client.get("devices/me")
        #expect(device.id == registration.deviceId)
        #expect(await client.session.refreshToken != tokens.refreshToken)

        await service.signOut(account, client: client)
        await #expect(throws: APIError.self) {
            let _: DeviceResponse = try await client.get("devices/me")
        }
    }

    @Test("Poolgerät per Einrichtungscode, zweite Person per Signatur, Code nur einmal")
    func poolDeviceWithEnrollmentCode() async throws {
        // Administrator erzeugt im „IT Center“ einen Code.
        let admin = makeService()
        let adminTokens = try await admin.signIn(login: login, password: password, clientName: "CI Admin")
        _ = try await admin.bindDevice(tokens: adminTokens, kind: .personal, enrollmentCode: nil, description: description)
        let adminClient = authorized(admin, try admin.createVault(for: adminTokens, pin: nil))
        let code: EnrollmentCodeResponse = try await adminClient.post("devices/enrollment-codes",
                                                                      body: CreateEnrollmentCodeRequest(kind: .pool, deviceName: "Fahrzeug CI"))
        let link = try EnrollmentLink.parse(try #require(code.link), allowInsecureLocal: true)
        #expect(link.enrollmentCode == code.code)
        #expect(link.deviceKind == .pool)

        // Neues Gerät löst den Code ein: sofort freigegeben, Name aus dem IT Center.
        let pool = makeService()
        let first = try await pool.signIn(login: login, password: password, clientName: "Fahrzeug")
        let registration = try await pool.bindDevice(tokens: first, kind: .personal, enrollmentCode: link.enrollmentCode, description: description)
        #expect(registration.kind == .pool)
        #expect(registration.status == .active)
        #expect(registration.name == "Fahrzeug CI")
        _ = try pool.createVault(for: first, pin: "482913")

        // Zweite Anmeldung auf demselben Gerät: Bindung per Signatur, danach Zugriff mit Erneuerung.
        let second = try await pool.signIn(login: login, password: password, clientName: "Fahrzeug")
        let attached = try await pool.bindDevice(tokens: second, kind: .pool, enrollmentCode: nil, description: description)
        #expect(attached.deviceId == registration.deviceId)
        let client = authorized(pool, try pool.createVault(for: second, pin: "573920"))
        try await client.refreshNow()

        // Ein zweites Gerät kann denselben Code nicht mehr verwenden.
        let other = makeService()
        let otherTokens = try await other.signIn(login: login, password: password, clientName: "Anderes")
        await #expect(throws: AccountError.self) {
            _ = try await other.bindDevice(tokens: otherTokens, kind: .pool, enrollmentCode: code.code, description: description)
        }
    }
}
