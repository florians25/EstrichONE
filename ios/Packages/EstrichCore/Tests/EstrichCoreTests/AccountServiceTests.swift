import Foundation
import Testing
@testable import EstrichCore

@Suite("Konten auf dem Gerät")
struct AccountServiceTests {
    private let transport = RoutingTransport()
    private let storage = Samples.temporaryStorage()
    private let keys = SoftwareDeviceKeys()

    private func makeService() -> AccountService {
        AccountService(api: APIClient(server: Samples.server, transport: transport), storage: storage, keys: keys,
                       secrets: InMemorySecretStore(), pinIterations: 1_000)
    }

    private let description = DeviceDescription(name: "iPhone", model: "iPhone17,1", osVersion: "26.0", appVersion: "0.2.0")

    @Test("Erstes Gerät: Registrierung mit Einrichtungscode, Tresor mit PIN")
    func firstRegistration() async throws {
        transport.on("POST", "/api/v1/auth/login", reply: .json(Samples.tokenJSON()))
        transport.on("POST", "/api/v1/devices/register") { request in
            let body = request.jsonBody()
            #expect(body["enrollmentCode"] as? String == "K7QX-M2PA")
            #expect(body["kind"] as? String == "pool")
            #expect(body["platform"] as? String == "ios")
            #expect(Data(base64Encoded: body["publicKey"] as? String ?? "") == keys.signingPublicKey)
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer eo1_access")
            return .json(Samples.deviceJSON(kind: "pool", status: "active"), status: 201)
        }
        let service = makeService()

        let tokens = try await service.signIn(login: " m.huber ", password: "geheim", clientName: "iPhone")
        let registration = try await service.bindDevice(tokens: tokens, kind: .pool, enrollmentCode: "K7QX-M2PA", description: description)
        let account = try service.createVault(for: tokens, pin: "482913")

        #expect(registration == DeviceRegistration(deviceId: Samples.deviceId, kind: .pool, name: "Fahrzeug 3", status: .active))
        #expect(account.session.refreshToken == "eor1_refresh")
        #expect(service.state.users.map(\.displayName) == ["Max Huber"])
        #expect(transport.requests.first?.jsonBody()["login"] as? String == "m.huber")
        try? storage.eraseAll()
    }

    @Test("Weitere Anmeldung auf einem registrierten Gerät bindet per Signatur statt neu zu registrieren")
    func attach() async throws {
        transport.on("POST", "/api/v1/devices/register", reply: .json(Samples.deviceJSON(kind: "pool", status: "pending"), status: 201))
        transport.on("POST", "/api/v1/devices/attach") { request in
            #expect(request.jsonBody()["deviceId"] as? String == Samples.deviceId.uuidString)
            return .json(Samples.deviceJSON(kind: "pool", status: "active"))
        }
        let service = makeService()
        _ = try await service.bindDevice(tokens: Samples.tokens(), kind: .pool, enrollmentCode: nil, description: description)

        let second = Samples.tokens(Samples.tokenJSON(access: "eo1_zweite", userId: UUID(), name: "Lukas Brandl"))
        let registration = try await service.bindDevice(tokens: second, kind: .pool, enrollmentCode: nil, description: description)

        #expect(registration.status == .active)
        #expect(transport.count("POST", "/api/v1/devices/register") == 1)
        #expect(transport.count("POST", "/api/v1/devices/attach") == 1)
        try? storage.eraseAll()
    }

    @Test("Poolgerät verlangt eine gültige PIN")
    func poolNeedsPIN() async throws {
        transport.on("POST", "/api/v1/devices/register", reply: .json(Samples.deviceJSON(kind: "pool", status: "active"), status: 201))
        let service = makeService()
        _ = try await service.bindDevice(tokens: Samples.tokens(), kind: .pool, enrollmentCode: nil, description: description)

        #expect(throws: AccountError.vault(.pinRequired)) { try service.createVault(for: Samples.tokens(), pin: nil) }
        #expect(throws: AccountError.pin(.trivial)) { try service.createVault(for: Samples.tokens(), pin: "123456") }
        try? storage.eraseAll()
    }

    @Test("Fünf falsche PINs sperren den Tresor bis zur Anmeldung mit Passwort")
    func pinLockout() async throws {
        transport.on("POST", "/api/v1/devices/register", reply: .json(Samples.deviceJSON(kind: "pool", status: "active"), status: 201))
        let service = makeService()
        let tokens = Samples.tokens()
        _ = try await service.bindDevice(tokens: tokens, kind: .pool, enrollmentCode: nil, description: description)
        _ = try service.createVault(for: tokens, pin: "482913")

        for _ in 1..<PINPolicy.maxAttempts {
            #expect(throws: AccountError.vault(.wrongPIN)) { try service.unlock(userId: Samples.userId, pin: "000001") }
        }
        #expect(throws: AccountError.pinLocked) { try service.unlock(userId: Samples.userId, pin: "000001") }
        #expect(throws: AccountError.pinLocked) { try service.unlock(userId: Samples.userId, pin: "482913") }

        // Online-Anmeldung mit Passwort legt den Tresor neu an und hebt die Sperre auf.
        _ = try service.createVault(for: tokens, pin: "573920")
        #expect(try service.unlock(userId: Samples.userId, pin: "573920").user.id == Samples.userId)
        try? storage.eraseAll()
    }

    @Test("Erneute Anmeldung nach Sitzungsende behält Tresor und lokale (auch ungesendete) Daten")
    func reSignInKeepsData() async throws {
        transport.on("POST", "/api/v1/devices/register", reply: .json(Samples.deviceJSON(kind: "personal", status: "active"), status: 201))
        let service = makeService()
        let tokens = Samples.tokens()
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        let first = try service.createVault(for: tokens, pin: nil)
        let marker = storage.userDirectory(Samples.userId).appending(path: "data.sealed")
        try Data("ungesendet".utf8).write(to: marker)

        let again = try service.createVault(for: tokens, pin: nil)

        #expect(again.dataKey.withUnsafeBytes { Data($0) } == first.dataKey.withUnsafeBytes { Data($0) })
        #expect(try Data(contentsOf: marker) == Data("ungesendet".utf8))
        #expect(try service.unlock(userId: Samples.userId, pin: nil).session.refreshToken == "eor1_refresh")
        try? storage.eraseAll()
    }

    @Test("Entferntes Gerät: neu registrieren, gleiche Person findet ihre ungesendeten Daten wieder")
    func reRegistrationKeepsData() async throws {
        transport.on("POST", "/api/v1/devices/register", reply: .json(Samples.deviceJSON(kind: "personal", status: "active"), status: 201))
        let service = makeService()
        let tokens = Samples.tokens()
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        let first = try service.createVault(for: tokens, pin: nil)
        let marker = storage.userDirectory(Samples.userId).appending(path: "data.sealed")
        try Data("ungesendet".utf8).write(to: marker)

        service.forgetRegistration()
        #expect(service.state.registration == nil)
        await #expect(throws: AccountError.self) { _ = try service.createVault(for: tokens, pin: nil) }
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: "NEU-CODE", description: description)
        #expect(transport.count("POST", "/api/v1/devices/register") == 2)
        let again = try service.createVault(for: tokens, pin: nil)
        #expect(again.dataKey.withUnsafeBytes { Data($0) } == first.dataKey.withUnsafeBytes { Data($0) })
        #expect(try Data(contentsOf: marker) == Data("ungesendet".utf8))
        try? storage.eraseAll()
    }

    @Test("Die Sitzung liegt nur verschlüsselt auf dem Gerät")
    func sessionEncrypted() async throws {
        transport.on("POST", "/api/v1/devices/register", reply: .json(Samples.deviceJSON(kind: "personal", status: "active"), status: 201))
        let service = makeService()
        let tokens = Samples.tokens()
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        _ = try service.createVault(for: tokens, pin: nil)

        let files = FileManager.default.enumerator(at: storage.root, includingPropertiesForKeys: nil)?.compactMap { $0 as? URL } ?? []
        for file in files where !file.hasDirectoryPath {
            let content = try Data(contentsOf: file)
            #expect(content.range(of: Data("eor1_refresh".utf8)) == nil, "Klartext-Token in \(file.lastPathComponent)")
            #expect(content.range(of: Data("eo1_access".utf8)) == nil, "Klartext-Token in \(file.lastPathComponent)")
        }
        #expect(try service.unlock(userId: Samples.userId, pin: nil).session.accessToken == "eo1_access")
        try? storage.eraseAll()
    }

    @Test("Ein Tresor ist für ein anderes Gerät nicht lesbar")
    func otherDevice() async throws {
        transport.on("POST", "/api/v1/devices/register", reply: .json(Samples.deviceJSON(kind: "personal", status: "active"), status: 201))
        let service = makeService()
        let tokens = Samples.tokens()
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        _ = try service.createVault(for: tokens, pin: nil)

        let copied = AccountService(api: APIClient(server: Samples.server, transport: transport), storage: storage,
                                    keys: SoftwareDeviceKeys(), secrets: InMemorySecretStore())
        #expect(throws: AccountError.vault(.unreadable)) { try copied.unlock(userId: Samples.userId, pin: nil) }
        try? storage.eraseAll()
    }

    @Test("Abmelden beendet die Sitzung serverseitig und löscht die Daten des Benutzers")
    func signOut() async throws {
        transport.on("POST", "/api/v1/devices/register", reply: .json(Samples.deviceJSON(kind: "personal", status: "active"), status: 201))
        transport.on("POST", "/api/v1/auth/logout", reply: .noContent)
        let service = makeService()
        let tokens = Samples.tokens()
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        let account = try service.createVault(for: tokens, pin: nil)
        let client = AuthorizedClient(api: service.api, session: account.session, keys: keys, persist: { _ in }, ended: { _ in })

        await service.signOut(account, client: client)

        #expect(transport.count("POST", "/api/v1/auth/logout") == 1)
        #expect(service.state.users.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: storage.userDirectory(Samples.userId).path()))
        try? storage.eraseAll()
    }
}
