import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

/// Zählt Aufrufe threadsicher (für Rückruf-Prüfungen).
final class Recorder<Value: Sendable>: @unchecked Sendable {
    private let lock = NSLock()
    private var stored: [Value] = []
    func append(_ value: Value) { lock.withLock { stored.append(value) } }
    var values: [Value] { lock.withLock { stored } }
}

@Suite("API-Client")
struct APIClientTests {
    @Test("liest Zeitpunkte mit 7 Nachkommastellen und Zeitzone (.NET)")
    func dates() throws {
        let date = try #require(APIDate.parse("2026-10-20T06:10:24.7660538+00:00"))
        #expect(abs(date.timeIntervalSince1970 - 1_792_476_624.766) < 0.001)
        #expect(APIDate.parse("2026-10-20T08:10:24+02:00") == APIDate.parse("2026-10-20T06:10:24Z"))
        #expect(APIDate.parse("gestern") == nil)
    }

    @Test("übersetzt Problem Details in Code und Meldung")
    func problem() async {
        let transport = RoutingTransport()
        transport.on("POST", "/api/v1/auth/login", reply: .problem(401, code: "INVALID_CREDENTIALS", detail: "Anmeldename oder Passwort ist nicht korrekt."))
        let api = APIClient(server: Samples.server, transport: transport)

        await #expect(throws: APIError.server(status: 401, code: "INVALID_CREDENTIALS", message: "Anmeldename oder Passwort ist nicht korrekt.")) {
            let _: TokenResponse = try await api.post("auth/login", body: LoginRequest(login: "x", password: "y", client: "ios", clientName: nil))
        }
    }

    @Test("Netzwerkfehler werden zu „offline“")
    func offline() async {
        let api = APIClient(server: Samples.server, transport: RoutingTransport())
        await #expect(throws: APIError.offline) {
            let _: NoContent = try await api.post("auth/logout")
        }
    }
}

@Suite("Angemeldeter Zugriff")
struct AuthorizedClientTests {
    private let keys = SoftwareDeviceKeys()

    private func makeClient(_ transport: RoutingTransport, accessExpiresIn: TimeInterval = 600,
                            persisted: Recorder<StoredSession> = Recorder(), ended: Recorder<SessionEnd> = Recorder()) -> AuthorizedClient {
        let session = StoredSession(Samples.tokens(Samples.tokenJSON(expiresIn: accessExpiresIn)))
        return AuthorizedClient(api: APIClient(server: Samples.server, transport: transport), session: session, keys: keys,
                                persist: { persisted.append($0) }, ended: { ended.append($0) })
    }

    @Test("erneuert ein bald ablaufendes Token vorab, mit Gerätesignatur über den Refresh-Token")
    func proactiveRefresh() async throws {
        let transport = RoutingTransport()
        let signatures = Recorder<String>()
        transport.on("POST", "/api/v1/auth/refresh") { request in
            let body = request.jsonBody()
            signatures.append(body["deviceSignature"] as? String ?? "")
            #expect(body["refreshToken"] as? String == "eor1_refresh")
            return .json(Samples.refreshJSON(access: "eo1_neu", refresh: "eor1_neu"))
        }
        transport.on("GET", "/api/v1/auth/me") { request in
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer eo1_neu")
            return .json(#"{"id":"\#(Samples.userId)","displayName":"Max Huber","userName":"m.huber","email":null}"#)
        }
        let persisted = Recorder<StoredSession>()
        let client = makeClient(transport, accessExpiresIn: 30, persisted: persisted)

        let me: UserSummary = try await client.get("auth/me")

        #expect(me.displayName == "Max Huber")
        #expect(persisted.values.last?.refreshToken == "eor1_neu")
        let encoded = try #require(signatures.values.first)
        let signature = try #require(Data(base64Encoded: encoded))
        let publicKey = try P256.Signing.PublicKey(derRepresentation: keys.signingPublicKey)
        #expect(publicKey.isValidSignature(try P256.Signing.ECDSASignature(rawRepresentation: signature),
                                           for: Data("EstrichONE refresh v1\neor1_refresh".utf8)))
    }

    @Test("parallele Anfragen erneuern genau einmal")
    func singleRefresh() async throws {
        let transport = RoutingTransport()
        transport.on("POST", "/api/v1/auth/refresh", reply: .json(Samples.refreshJSON(access: "eo1_neu", refresh: "eor1_neu")))
        transport.on("GET", "/api/v1/devices/me", reply: .json(Samples.deviceJSON(kind: "pool", status: "active")))
        let client = makeClient(transport, accessExpiresIn: 0)

        try await withThrowingTaskGroup(of: DeviceResponse.self) { group in
            for _ in 0..<8 {
                group.addTask { try await client.get("devices/me") }
            }
            for try await device in group {
                #expect(device.status == .active)
            }
        }

        #expect(transport.count("POST", "/api/v1/auth/refresh") == 1)
    }

    @Test("ein abgelaufenes Token führt zu Erneuerung und Wiederholung")
    func retryAfterExpiry() async throws {
        let transport = RoutingTransport()
        transport.on("POST", "/api/v1/auth/refresh", reply: .json(Samples.refreshJSON(access: "eo1_neu", refresh: "eor1_neu")))
        transport.on("GET", "/api/v1/devices/me") { request in
            request.value(forHTTPHeaderField: "Authorization") == "Bearer eo1_neu"
                ? .json(Samples.deviceJSON(kind: "personal", status: "active"))
                : .problem(401, code: "ACCESS_TOKEN_EXPIRED")
        }
        let client = makeClient(transport)

        let device: DeviceResponse = try await client.get("devices/me")

        #expect(device.kind == .personal)
        #expect(transport.count("GET", "/api/v1/devices/me") == 2)
    }

    @Test("ein gesperrtes Gerät beendet die Sitzung")
    func deviceLocked() async {
        let transport = RoutingTransport()
        transport.on("GET", "/api/v1/devices/me", reply: .problem(401, code: "DEVICE_LOCKED"))
        let ended = Recorder<SessionEnd>()
        let client = makeClient(transport, ended: ended)

        await #expect(throws: APIError.self) {
            let _: DeviceResponse = try await client.get("devices/me")
        }
        #expect(ended.values == [.deviceLocked])
    }

    @Test("ein wiederverwendeter Refresh-Token meldet das Ende der Sitzung")
    func refreshReused() async {
        let transport = RoutingTransport()
        transport.on("POST", "/api/v1/auth/refresh", reply: .problem(401, code: "REFRESH_TOKEN_REUSED"))
        let ended = Recorder<SessionEnd>()
        let client = makeClient(transport, accessExpiresIn: 0, ended: ended)

        await #expect(throws: APIError.self) {
            let _: DeviceResponse = try await client.get("devices/me")
        }
        #expect(ended.values == [.reused])
    }

    @Test("401 einer Anfrage mit soeben ersetztem Token: erneuern und wiederholen, nicht abmelden")
    func unauthorizedAfterParallelRotation() async throws {
        let transport = RoutingTransport()
        transport.on("POST", "/api/v1/auth/refresh", reply: .json(Samples.refreshJSON(access: "eo1_neu", refresh: "eor1_neu")))
        transport.on("GET", "/api/v1/devices/me") { request in
            request.value(forHTTPHeaderField: "Authorization") == "Bearer eo1_neu"
                ? .json(Samples.deviceJSON(kind: "personal", status: "active"))
                : .problem(401, code: "UNAUTHORIZED")
        }
        let ended = Recorder<SessionEnd>()
        let client = makeClient(transport, ended: ended)

        let device: DeviceResponse = try await client.get("devices/me")

        #expect(device.status == .active)
        #expect(ended.values.isEmpty)
        #expect(transport.count("POST", "/api/v1/auth/refresh") == 1)
    }

    @Test("beendete Sitzung: die Erneuerung nennt den Grund (Passwort geändert)")
    func passwordChanged() async {
        let transport = RoutingTransport()
        transport.on("GET", "/api/v1/devices/me", reply: .problem(401, code: "UNAUTHORIZED"))
        transport.on("POST", "/api/v1/auth/refresh", reply: .problem(401, code: "PASSWORD_CHANGED"))
        let ended = Recorder<SessionEnd>()
        let client = makeClient(transport, ended: ended)

        await #expect(throws: APIError.self) {
            let _: DeviceResponse = try await client.get("devices/me")
        }
        #expect(ended.values == [.passwordChanged])
        #expect(SessionEnd.passwordChanged.requiresPassword)
    }

    @Test("Erneuerung ohne Netz beendet nichts; später klappt es mit demselben Refresh-Token")
    func refreshOffline() async throws {
        let transport = RoutingTransport()
        transport.on("GET", "/api/v1/devices/me", reply: .json(Samples.deviceJSON(kind: "personal", status: "active")))
        let ended = Recorder<SessionEnd>()
        let client = makeClient(transport, accessExpiresIn: 0, ended: ended)

        await #expect(throws: APIError.offline) {
            let _: DeviceResponse = try await client.get("devices/me")
        }
        transport.on("POST", "/api/v1/auth/refresh") { request in
            #expect(request.jsonBody()["refreshToken"] as? String == "eor1_refresh")
            return .json(Samples.refreshJSON(access: "eo1_neu", refresh: "eor1_neu"))
        }
        let device: DeviceResponse = try await client.get("devices/me")

        #expect(device.status == .active)
        #expect(ended.values.isEmpty)
    }

    @Test("gleichzeitige Erneuerung (409) wird wiederholt statt abzumelden")
    func refreshInProgress() async throws {
        let transport = RoutingTransport()
        let attempts = Recorder<Int>()
        transport.on("POST", "/api/v1/auth/refresh") { _ in
            attempts.append(1)
            return attempts.values.count == 1
                ? .problem(409, code: "REFRESH_IN_PROGRESS")
                : .json(Samples.refreshJSON(access: "eo1_neu", refresh: "eor1_neu"))
        }
        transport.on("GET", "/api/v1/devices/me", reply: .json(Samples.deviceJSON(kind: "personal", status: "active")))
        let ended = Recorder<SessionEnd>()
        let session = StoredSession(Samples.tokens(Samples.tokenJSON(expiresIn: 0)))
        let client = AuthorizedClient(api: APIClient(server: Samples.server, transport: transport), session: session, keys: keys,
                                      retryDelayMilliseconds: 10, persist: { _ in }, ended: { ended.append($0) })

        let _: DeviceResponse = try await client.get("devices/me")

        #expect(attempts.values.count == 2)
        #expect(ended.values.isEmpty)
    }

    @Test("Sitzungsende wird nur einmal gemeldet, auch bei vielen parallelen Anfragen")
    func endReportedOnce() async {
        let transport = RoutingTransport()
        transport.on("GET", "/api/v1/devices/me", reply: .problem(401, code: "DEVICE_LOCKED"))
        let ended = Recorder<SessionEnd>()
        let client = makeClient(transport, ended: ended)

        await withTaskGroup(of: Void.self) { group in
            for _ in 0..<5 {
                group.addTask { let _: DeviceResponse? = try? await client.get("devices/me") }
            }
        }
        #expect(ended.values == [.deviceLocked])
    }
}
