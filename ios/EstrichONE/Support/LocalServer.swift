import EstrichCore
import Foundation
import UIKit

/// Server im App-Prozess – ohne Netzwerk: für UI-Tests und für den Demomodus (App-Prüfung durch Apple, Ausprobieren).
/// Beantwortet die Endpunkte wie der echte Server; alles bleibt im Arbeitsspeicher, nichts verlässt das Gerät.
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
    /// Status des registrierten Geräts, wie ihn der echte Server bei /devices/me meldet.
    private var deviceStatus = "active"
    private var counter = 0
    /// Angekündigte Dateien: Größe und empfangene Bytes.
    private var files: [String: (size: Int, received: Int)] = [:]
    /// Zuletzt gemeldeter Stand je Datensatz, damit Änderungen (`*.update`) zusammengeführt zurückkommen.
    private var entities: [String: [String: Any]] = [:]
    private var currentUserId = "0193a1b2-0000-7000-8000-00000000aaaa"

    /// Beispieldaten im Demomodus (`nil` in UI-Tests).
    private let demo: DemoContent?
    /// Typ der Beispieldatensätze (für den ersten Abgleich).
    private var demoTypes: [String: String] = [:]

    init(serverInfoStatus: Int, serverInfoBody: String, password: String, newDeviceStatus: String, demo: DemoContent? = nil) {
        self.serverInfoStatus = serverInfoStatus
        self.serverInfoBody = serverInfoBody
        self.password = password
        self.newDeviceStatus = newDeviceStatus
        self.demo = demo
        for change in demo?.changes() ?? [] {
            guard let id = (change["id"] as? String)?.lowercased(), let type = change["entityType"] as? String,
                  let data = change["data"] as? [String: Any] else { continue }
            entities[id] = data
            demoTypes[id] = type
        }
    }

    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        try await Task.sleep(for: .milliseconds(150))
        let path = request.url?.path() ?? ""
        let body = request.httpBody.flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] } ?? [:]
        if let raw = demoFile(path, method: request.httpMethod ?? "GET") {
            let response = HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!
            return (raw, response)
        }
        let cursor = request.url.flatMap { URLComponents(url: $0, resolvingAgainstBaseURL: false) }?
            .queryItems?.first { $0.name == "cursor" }?.value
        let (status, json) = lock.withLock {
            path.hasPrefix("/api/v1/files/") ? routeFile(path, method: request.httpMethod ?? "GET", bytes: request.httpBody?.count ?? 0)
                                             : route(path, body: body, cursor: cursor)
        }
        let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: nil)!
        return (Data(json.utf8), response)
    }

    /// Inhalte der Beispieldateien und das Bauakte-PDF im Demomodus.
    private func demoFile(_ path: String, method: String) -> Data? {
        guard let demo, method == "GET" else { return nil }
        if path.hasPrefix("/api/v1/bauakten/"), path.hasSuffix("/report.pdf") { return demo.pdf }
        let parts = path.split(separator: "/")
        guard parts.count >= 5, parts[2] == "files", parts.last == "content" else { return nil }
        return demo.content(of: String(parts[3]).lowercased())
    }

    private func route(_ path: String, body: [String: Any], cursor: String?) -> (Int, String) {
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
            currentUserId = id
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
                deviceStatus = "active"
                return (201, device(status: deviceStatus))
            }
            deviceKind = body["kind"] as? String ?? "personal"
            deviceName = body["name"] as? String ?? "iPhone"
            deviceStatus = deviceKind == "pool" ? "pending" : newDeviceStatus
            return (201, device(status: deviceStatus))
        case "/api/v1/devices/attach", "/api/v1/devices/me":
            return (200, device(status: deviceStatus))
        case "/api/v1/sync/push":
            // Nimmt jede Operation an und meldet den Datensatz so zurück, wie der Client ihn gesendet hat.
            let operations = body["operations"] as? [[String: Any]] ?? []
            let results = operations.map { operation -> [String: Any] in
                let type = operation["type"] as? String ?? ""
                let entityId = (operation["entityId"] as? String ?? "").lowercased()
                if type.hasSuffix(".setStatus") {
                    var entity = entities[entityId] ?? ["id": entityId]
                    let payload = operation["payload"] as? [String: Any] ?? [:]
                    entity["status"] = payload["status"]
                    entity["cancellationReason"] = payload["status"] as? String == "cancelled" ? payload["reason"] : NSNull()
                    entity["version"] = ((entity["version"] as? Int) ?? 1) + 1
                    entities[entityId] = entity
                    return ["opId": operation["opId"] ?? "", "status": "applied", "version": entity["version"] ?? 2, "entity": entity]
                }
                if type.hasSuffix(".update") {
                    var entity = entities[entityId] ?? ["id": entityId]
                    let changes = (operation["payload"] as? [String: Any])?["changes"] as? [String: Any] ?? [:]
                    entity.merge(changes) { _, new in new }
                    entity["version"] = ((entity["version"] as? Int) ?? 1) + 1
                    entities[entityId] = entity
                    return ["opId": operation["opId"] ?? "", "status": "applied", "version": entity["version"] ?? 2, "entity": entity]
                }
                var entity = operation["payload"] as? [String: Any] ?? [:]
                entity["id"] = operation["entityId"]
                entity["version"] = 1
                if type == "workitem.create" {
                    entity["status"] = "open"
                    entity["createdByUserId"] = currentUserId
                }
                if type == "note.create" {
                    entity["authorUserId"] = currentUserId
                    entity["isPinned"] = false
                    entity["createdAt"] = APIDate.format(Date())
                }
                if (operation["type"] as? String) == "bauakte.create" {
                    entity["number"] = "2026-0\(10 + counter)"
                    entity["status"] = "planned"
                }
                if (operation["type"] as? String) == "file.announce", let id = operation["entityId"] as? String {
                    files[id.lowercased()] = ((entity["sizeBytes"] as? Int) ?? 0, 0)
                    entity["state"] = "awaitingUpload"
                }
                entities[entityId] = entity
                return ["opId": operation["opId"] ?? "", "status": "applied", "version": 1, "entity": entity]
            }
            let json = (try? JSONSerialization.data(withJSONObject: ["results": results])).map { String(decoding: $0, as: UTF8.self) }
            return (200, json ?? #"{"results":[]}"#)
        case "/api/v1/sync/pull" where demo != nil:
            // Demomodus: beim ersten Abgleich alle Beispieldatensätze im aktuellen Stand, danach nur noch Bestätigungen.
            guard cursor == nil else { return (200, #"{"changes":[],"cursor":"demo:1","hasMore":false,"visibleBauakteIds":null}"#) }
            let changes = demoTypes.compactMap { id, type -> [String: Any]? in
                entities[id].map { ["entityType": type, "id": id, "deleted": false, "data": $0] }
            }
            let page: [String: Any] = ["changes": changes, "cursor": "demo:1", "hasMore": false, "visibleBauakteIds": NSNull()]
            let json = (try? JSONSerialization.data(withJSONObject: page)).map { String(decoding: $0, as: UTF8.self) }
            return (200, json ?? #"{"changes":[],"cursor":"demo:1","hasMore":false,"visibleBauakteIds":null}"#)
        case "/api/v1/tickets/mine" where demo != nil:
            return (200, demo?.tickets(now: APIDate.format(Date())).list ?? "[]")
        case "/api/v1/tickets/\(DemoContent.ticketId)" where demo != nil:
            return (200, demo?.tickets(now: APIDate.format(Date())).detail ?? "{}")
        case let demoPath where demo != nil && demoPath.hasPrefix("/api/v1/planning/dispatch/"):
            return (200, demo?.dispatchBoard(day: CalendarDay.string(Date())) ?? "{}")
        case "/api/v1/planning/mine" where demo != nil, "/api/v1/planning/range" where demo != nil:
            return (200, demo?.myPlanning(day: CalendarDay.string(Date())) ?? "{}")
        case "/api/v1/sync/pull":
            // Ein Bestandskunde für die Kundensuche der Schnellanlage.
            return (200, """
            {"changes":[{"entityType":"contact","id":"0193a1b2-0000-7000-8000-0000000c0001","deleted":false,
              "data":{"id":"0193a1b2-0000-7000-8000-0000000c0001","kind":"company","name":"Huber Bau GmbH","customerNumber":"K-1042",
                      "street":"Industriestr. 1","postalCode":"84028","city":"Landshut","phone":"0871 4711",
                      "email":"info@huber.example","notes":null,"version":1}}],
             "cursor":"1:0","hasMore":false,"visibleBauakteIds":null}
            """)
        case "/api/v1/planning/range", "/api/v1/planning/mine":
            let today = CalendarDay.string(Date())
            return (200, """
            {"from":"\(today)","to":"\(today)","assignments":[{"id":"0193a1b2-0000-7000-8000-0000000a0001","day":"\(today)",
              "startTime":"07:00:00","activity":"Estrich einbringen","bauakteId":"0193a1b2-0000-7000-8000-0000000b0001",
              "bauakteNumber":"2026-090","bauakteTitle":"Halle Gewerbering","bauakteAddress":"Gewerbering 3, 84030 Ergolding",
              "crewName":"Kolonne Huber","people":[{"id":"\(currentUserId)","name":"Max Huber"}],"resources":[],"notes":null,"warnings":[]}],
             "absences":[]}
            """)
        case let dispatchPath where dispatchPath.hasPrefix("/api/v1/planning/dispatch/"):
            let today = CalendarDay.string(Date())
            return (200, """
            {"day":"\(today)","canPlan":false,"mySiteIds":["0193a1b2-0000-7000-8000-0000000b0001"],
             "sites":[{"bauakteId":"0193a1b2-0000-7000-8000-0000000b0001","number":"2026-090","title":"Halle Gewerbering",
               "address":"Gewerbering 3, 84030 Ergolding","latitude":null,"longitude":null,"siteContactName":null,"siteContactPhone":null,
               "startTime":"07:00:00","navigationUrl":null,"warnings":[],
               "jobs":[{"assignmentId":"0193a1b2-0000-7000-8000-0000000a0001","kind":"screed","activity":"Estrich einbringen",
                 "startTime":"07:00:00","endTime":null,"crewId":null,"crewName":"Kolonne Huber","crewColor":null,"notes":null}],
               "people":[{"id":"\(currentUserId)","name":"Max Huber"}],"vehicles":[]}],
             "people":[{"id":"\(currentUserId)","name":"Max Huber","crewId":null,"crewName":"Kolonne Huber","crewColor":null,
               "siteIds":["0193a1b2-0000-7000-8000-0000000b0001"],"note":null}],
             "vehicles":[],"appointments":[]}
            """)
        case "/api/v1/sync/snapshot":
            return (200, #"{"changes":[],"missing":[]}"#)
        case "/api/v1/system/notices":
            return (200, """
            {"announcements":[{"id":"uitest-1","title":"Neue App-Version","text":"Dokumente lassen sich jetzt direkt scannen.",
              "severity":"info","startsAt":null,"endsAt":null}],
             "maintenance":null,"serverTime":"\(APIDate.format(Date()))"}
            """)
        case "/api/v1/tickets/mine":
            return (200, "[]")
        case "/api/v1/tickets/device":
            return (200, #"{"number":"T-2026-0001"}"#)
        case "/api/v1/directory/users":
            return (200, """
            [{"id":"0193a1b2-0000-7000-8000-00000000bbbb","displayName":"Lukas Brandl","isActive":true},
             {"id":"0193a1b2-0000-7000-8000-00000000aaaa","displayName":"Max Huber","isActive":true}]
            """)
        case let historyPath where historyPath.hasPrefix("/api/v1/bauakten/") && historyPath.hasSuffix("/history"):
            return (200, """
            [{"at":"\(APIDate.format(Date()))","action":"bauakte.created","actorUserId":"\(currentUserId)","actorName":"Max Huber",
              "text":"Max Huber hat die Bauakte angelegt."}]
            """)
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
