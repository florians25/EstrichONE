#if DEBUG
import EstrichCore
import Foundation
import UIKit

/// Steuerung für XCUITests über Umgebungsvariablen. Nur in Debug-Builds enthalten.
///
/// - `ESTRICHONE_UITEST=1`: eigener Einstellungs- und Datenbereich, bei jedem Start geleert,
///   außer mit `ESTRICHONE_UITEST_KEEP=1` (Neustart-Tests)
/// - `ESTRICHONE_UITEST_SERVER_STATUS` / `ESTRICHONE_UITEST_SERVER_BODY`: feste Antwort für `server-info`
/// - `ESTRICHONE_UITEST_PASSWORD`: Passwort, das der Testserver annimmt (Standard „Richtig-2026“)
/// - `ESTRICHONE_UITEST_DEVICE_STATUS`: Status neu registrierter Geräte ohne Einrichtungscode (`active`/`pending`)
enum UITestSupport {
    private static var environment: [String: String] { ProcessInfo.processInfo.environment }

    static var isActive: Bool { environment["ESTRICHONE_UITEST"] == "1" }

    /// Erzeugtes JPEG statt Kamera (der Simulator hat keine Kamera).
    @MainActor
    static func samplePhoto() -> Data {
        let renderer = UIGraphicsImageRenderer(size: CGSize(width: 640, height: 480))
        let image = renderer.image { context in
            UIColor(red: 0.04, green: 0.18, blue: 0.36, alpha: 1).setFill()
            context.fill(CGRect(x: 0, y: 0, width: 640, height: 480))
            UIColor.white.setFill()
            context.fill(CGRect(x: 200, y: 140, width: 240, height: 200))
        }
        return image.jpegData(compressionQuality: 0.85) ?? Data()
    }

    @MainActor
    static func makeEnvironment() -> AppEnvironment? {
        guard environment["ESTRICHONE_UITEST"] == "1" else { return nil }
        let keep = environment["ESTRICHONE_UITEST_KEEP"] == "1"
        let suite = "de.estrichone.uitest"
        let root = URL.applicationSupportDirectory.appending(path: "EstrichONE-UITest", directoryHint: .isDirectory)
        let secrets = KeychainSecretStore(service: suite)
        if !keep {
            UserDefaults().removePersistentDomain(forName: suite)
            try? FileManager.default.removeItem(at: root)
            try? secrets.remove(DeviceKeyFactory.storageKey)
        }
        return AppEnvironment(
            defaults: UserDefaults(suiteName: suite) ?? .standard,
            transport: FakeServer(
                serverInfoStatus: environment["ESTRICHONE_UITEST_SERVER_STATUS"].flatMap(Int.init) ?? 200,
                serverInfoBody: environment["ESTRICHONE_UITEST_SERVER_BODY"] ?? FakeServer.defaultServerInfo,
                password: environment["ESTRICHONE_UITEST_PASSWORD"] ?? "Richtig-2026",
                newDeviceStatus: environment["ESTRICHONE_UITEST_DEVICE_STATUS"] ?? "active"),
            secrets: secrets,
            storageRoot: root,
            pinIterations: 1_000,
            personalLockAfter: 15 * 60,
            poolInactivityTimeout: 5 * 60,
            skipsDeviceOwnerAuthentication: true)
    }
}

/// Minimaler Testserver im Prozess: beantwortet die Endpunkte der Einrichtung wie der echte Server.
/// Der echte Server wird in der CI separat gegen EstrichCore getestet (ios/scripts/test-core-against-server.sh).
final class FakeServer: HTTPTransport, @unchecked Sendable {
    static let defaultServerInfo = """
    {"product":"EstrichONE","serverName":"Estrichbau Beispiel GmbH","serverVersion":"0.2.0","apiVersions":["v1"],
     "minClientVersion":"0.1.0","authMethods":["local"],"instanceEpoch":"0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b","setupRequired":false}
    """

    private let serverInfoStatus: Int
    private let serverInfoBody: String
    private let password: String
    private let newDeviceStatus: String
    private let lock = NSLock()
    private var deviceKind = "personal"
    private var deviceName = "iPhone"
    private var counter = 0
    /// Angekündigte Dateien: Größe und empfangene Bytes.
    private var files: [String: (size: Int, received: Int)] = [:]

    init(serverInfoStatus: Int, serverInfoBody: String, password: String, newDeviceStatus: String) {
        self.serverInfoStatus = serverInfoStatus
        self.serverInfoBody = serverInfoBody
        self.password = password
        self.newDeviceStatus = newDeviceStatus
    }

    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        try await Task.sleep(for: .milliseconds(150))
        let path = request.url?.path() ?? ""
        let body = request.httpBody.flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] } ?? [:]
        let (status, json) = lock.withLock {
            path.hasPrefix("/api/v1/files/") ? routeFile(path, method: request.httpMethod ?? "GET", bytes: request.httpBody?.count ?? 0)
                                             : route(path, body: body)
        }
        let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: nil)!
        return (Data(json.utf8), response)
    }

    private func route(_ path: String, body: [String: Any]) -> (Int, String) {
        counter += 1
        switch path {
        case "/api/v1/server-info":
            return (serverInfoStatus, serverInfoBody)
        case "/api/v1/auth/login":
            guard body["password"] as? String == password else {
                return problem(401, "INVALID_CREDENTIALS", "Anmeldename oder Passwort ist nicht korrekt.")
            }
            let login = (body["login"] as? String ?? "").lowercased()
            let name = login.contains("lukas") ? "Lukas Brandl" : "Max Huber"
            let id = login.contains("lukas") ? "0193a1b2-0000-7000-8000-00000000bbbb" : "0193a1b2-0000-7000-8000-00000000aaaa"
            return (200, tokens(userId: id, name: name, userName: login))
        case "/api/v1/auth/refresh":
            return (200, """
            {"accessToken":"eo1_\(counter)","accessTokenExpiresAt":"\(APIDate.format(Date().addingTimeInterval(600)))",
             "refreshToken":"eor1_\(counter)","refreshTokenExpiresAt":"\(APIDate.format(Date().addingTimeInterval(86400)))"}
            """)
        case "/api/v1/auth/logout":
            return (204, "")
        case "/api/v1/devices/register":
            if let code = body["enrollmentCode"] as? String {
                guard code == "K7QX-M2PA" else {
                    return problem(422, "ENROLLMENT_CODE_INVALID", "Der Einrichtungscode ist abgelaufen oder wurde bereits verwendet.")
                }
                deviceKind = "pool"
                deviceName = "Fahrzeug 3"
                return (201, device(status: "active"))
            }
            deviceKind = body["kind"] as? String ?? "personal"
            deviceName = body["name"] as? String ?? "iPhone"
            return (201, device(status: deviceKind == "pool" ? "pending" : newDeviceStatus))
        case "/api/v1/devices/attach", "/api/v1/devices/me":
            return (200, device(status: "active"))
        case "/api/v1/sync/push":
            // Nimmt jede Operation an und meldet den Datensatz so zurück, wie der Client ihn gesendet hat.
            let operations = body["operations"] as? [[String: Any]] ?? []
            let results = operations.map { operation -> [String: Any] in
                var entity = operation["payload"] as? [String: Any] ?? [:]
                entity["id"] = operation["entityId"]
                entity["version"] = 1
                if (operation["type"] as? String) == "bauakte.create" {
                    entity["number"] = "2026-0\(10 + counter)"
                    entity["status"] = "planned"
                }
                if (operation["type"] as? String) == "file.announce", let id = operation["entityId"] as? String {
                    files[id.lowercased()] = ((entity["sizeBytes"] as? Int) ?? 0, 0)
                    entity["state"] = "awaitingUpload"
                }
                return ["opId": operation["opId"] ?? "", "status": "applied", "version": 1, "entity": entity]
            }
            let json = (try? JSONSerialization.data(withJSONObject: ["results": results])).map { String(decoding: $0, as: UTF8.self) }
            return (200, json ?? #"{"results":[]}"#)
        case "/api/v1/sync/pull":
            return (200, #"{"changes":[],"cursor":"1:0","hasMore":false,"visibleBauakteIds":null}"#)
        default:
            return problem(404, "NOT_FOUND", "Nicht gefunden")
        }
    }

    /// `GET /files/{id}/upload-status` und `PUT /files/{id}/content`.
    private func routeFile(_ path: String, method: String, bytes: Int) -> (Int, String) {
        let parts = path.split(separator: "/")
        guard parts.count >= 5 else { return problem(404, "NOT_FOUND", "Nicht gefunden") }
        let id = String(parts[3]).lowercased()
        guard var file = files[id] else { return problem(404, "FILE_NOT_FOUND", "Datei unbekannt") }
        if method == "PUT" {
            file.received += bytes
            files[id] = file
        }
        let state = file.received >= file.size ? "stored" : "awaitingUpload"
        return (200, #"{"id":"\#(id)","receivedBytes":\#(file.received),"sizeBytes":\#(file.size),"state":"\#(state)"}"#)
    }

    private func tokens(userId: String, name: String, userName: String) -> String {
        """
        {"accessToken":"eo1_\(counter)","accessTokenExpiresAt":"\(APIDate.format(Date().addingTimeInterval(600)))",
         "refreshToken":"eor1_\(counter)","refreshTokenExpiresAt":"\(APIDate.format(Date().addingTimeInterval(86400)))",
         "user":{"id":"\(userId)","displayName":"\(name)","userName":"\(userName)","email":null},"mustChangePassword":false}
        """
    }

    private func device(status: String) -> String {
        """
        {"id":"0193a1b2-0000-7000-8000-00000000dddd","name":"\(deviceName)","platform":"ios","kind":"\(deviceKind)",
         "status":"\(status)","model":null,"osVersion":null,"appVersion":null,"assignedUserId":null,
         "registeredAt":"2026-10-20T06:10:24.7660538+00:00","approvedAt":null,"lastSeenAt":null,"version":1}
        """
    }

    private func problem(_ status: Int, _ code: String, _ detail: String) -> (Int, String) {
        (status, #"{"status":\#(status),"title":"Fehler","detail":"\#(detail)","code":"\#(code)"}"#)
    }
}
#endif
