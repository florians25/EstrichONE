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
    /// Im Demomodus hochgeladene Inhalte (Anhänge im Helpdesk), damit die App sie wieder anzeigen kann.
    private var uploaded: [String: Data] = [:]
    /// Zuletzt gemeldeter Stand je Datensatz, damit Änderungen (`*.update`) zusammengeführt zurückkommen.
    private var entities: [String: [String: Any]] = [:]
    private var currentUserId = "0193a1b2-0000-7000-8000-00000000aaaa"

    /// Beispieldaten im Demomodus (`nil` in UI-Tests).
    private let demo: DemoContent?
    /// Typ der Beispieldatensätze (für den ersten Abgleich).
    private var demoTypes: [String: String] = [:]
    /// Planung im Demomodus (Wer fährt wohin, Schnelldisposition).
    private lazy var planning = DemoPlanning()
    /// Meldungen im Demomodus (Helpdesk).
    private lazy var helpdesk = DemoHelpdesk(userId: currentUserId)
    /// Postfach im Demomodus (Benachrichtigungen).
    private lazy var notifications = DemoNotifications()
    /// Chat im Demomodus.
    private lazy var chat = DemoChat(userId: currentUserId)

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
        if demo != nil, let interrupted = try demoDisruption(path, request: request) { return interrupted }
        let body = request.httpBody.flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] } ?? [:]
        if let raw = demoFile(path, method: request.httpMethod ?? "GET") {
            let response = HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!
            return (raw, response)
        }
        if demo != nil, request.httpMethod == "PUT", path.hasPrefix("/api/v1/files/"), path.hasSuffix("/content"), let content = request.httpBody {
            let id = String(path.split(separator: "/")[3]).lowercased()
            lock.withLock { uploaded[id] = content }
        }
        let cursor = request.url.flatMap { URLComponents(url: $0, resolvingAgainstBaseURL: false) }?
            .queryItems?.first { $0.name == "cursor" }?.value
        let (status, json) = lock.withLock {
            path.hasPrefix("/api/v1/files/") ? routeFile(path, method: request.httpMethod ?? "GET", bytes: request.httpBody?.count ?? 0)
                                             : route(path, method: request.httpMethod ?? "GET", body: body, cursor: cursor)
        }
        let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: nil)!
        return (Data(json.utf8), response)
    }

    /// Vorgeführte Störungen (Menü „Demo“): Server nicht erreichbar bzw. Wartung (503 `MAINTENANCE`, wie MaintenanceGate).
    private func demoDisruption(_ path: String, request: URLRequest) throws -> (Data, HTTPURLResponse)? {
        let switches = DemoSwitches.shared
        if switches.outage { throw URLError(.cannotConnectToHost) }
        guard switches.maintenance else { return nil }
        let open = ["/api/v1/system/notices", "/api/v1/server-info", "/api/v1/auth"]
        guard !open.contains(where: { path.hasPrefix($0) }) else { return nil }
        let body = #"{"status":503,"title":"Wartungsarbeiten","detail":"Update auf Version 0.2 – EstrichONE ist in Kürze wieder verfügbar.","code":"MAINTENANCE","endsAt":"\#(APIDate.format(switches.maintenanceEndsAt))"}"#
        let response = HTTPURLResponse(url: request.url!, statusCode: 503, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/problem+json"])!
        return (Data(body.utf8), response)
    }

    /// Inhalte der Beispieldateien und das Bauakte-PDF im Demomodus.
    private func demoFile(_ path: String, method: String) -> Data? {
        guard let demo, method == "GET" else { return nil }
        if path.hasPrefix("/api/v1/bauakten/"), path.hasSuffix("/report.pdf") { return demo.pdf }
        let parts = path.split(separator: "/")
        guard parts.count >= 5, parts[2] == "files", parts.last == "content" else { return nil }
        let id = String(parts[3]).lowercased()
        return demo.content(of: id) ?? lock.withLock { uploaded[id] }
    }

    private func route(_ path: String, method: String, body: [String: Any], cursor: String?) -> (Int, String) {
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
                entity["version"] = type.hasSuffix(".save") ? ((entities[entityId]?["version"] as? Int) ?? 0) + 1 : 1
                if type == "workitem.create" {
                    entity["status"] = "open"
                    entity["createdByUserId"] = currentUserId
                }
                if type == "ticket.create", demo != nil {
                    helpdesk.add(id: entityId, payload: entity)
                }
                if type == "note.create" {
                    entity["authorUserId"] = currentUserId
                    entity["isPinned"] = false
                    entity["createdAt"] = APIDate.format(Date())
                }
                if type == "acceptance.create" {
                    // Wie der Server: Unterschriften nur als Kennzeichen; beide vorhanden → abgeschlossen (ohne PDF im Demomodus).
                    let client = (entity["clientSignature"] as? [String: Any])?["png"] is String
                    let contractor = (entity["contractorSignature"] as? [String: Any])?["png"] is String
                    entity["clientSignature"] = nil
                    entity["contractorSignature"] = nil
                    entity["hasClientSignature"] = client
                    entity["hasContractorSignature"] = contractor
                    entity["performedByUserId"] = currentUserId
                    entity["state"] = client && contractor ? "completed" : "recorded"
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
        case let dayPath where demo != nil && dayPath.hasPrefix("/api/v1/planning/day/"):
            return json(200, planning.dayPlan(String(dayPath.dropFirst("/api/v1/planning/day/".count))))
        case "/api/v1/planning/batch" where demo != nil:
            let (status, result) = planning.batch(body)
            return json(status, result)
        case "/api/v1/planning/releases" where demo != nil && method == "POST":
            let (status, result) = planning.release(body, by: "Max Huber")
            return json(status, result)
        case let releasePath where demo != nil && method == "DELETE" && releasePath.hasPrefix("/api/v1/planning/releases/"):
            planning.withdrawRelease(String(releasePath.dropFirst("/api/v1/planning/releases/".count)))
            return (204, "")
        case let movePath where demo != nil && movePath.hasPrefix("/api/v1/planning/assignments/") && movePath.hasSuffix("/next-free-day"):
            let (status, result) = planning.nextFreeDay(movePath.split(separator: "/").map(String.init)[4].lowercased())
            return json(status, result)
        case let swapPath where demo != nil && swapPath.hasPrefix("/api/v1/planning/days/") && swapPath.hasSuffix("/swap-crews"):
            return json(200, planning.swapCrews(day: swapPath.split(separator: "/").map(String.init)[4], body: body))
        case "/api/v1/system/health-banner" where demo != nil:
            return json(200, DemoSwitches.shared.health())
        case "/api/v1/system/notices" where demo != nil && DemoSwitches.shared.maintenance:
            let end = APIDate.format(DemoSwitches.shared.maintenanceEndsAt)
            return (200, #"{"announcements":[],"maintenance":{"active":true,"message":"Update auf Version 0.2 – EstrichONE ist in Kürze wieder verfügbar.","startsAt":"\#(APIDate.format(Date()))","endsAt":"\#(end)","adminsMayWork":true},"serverTime":"\#(APIDate.format(Date()))"}"#)
        case "/api/v1/chat/status" where demo != nil:
            return json(200, chat.status())
        case "/api/v1/chat/conversations" where demo != nil:
            return json(200, chat.list())
        case "/api/v1/chat/contacts" where demo != nil:
            return json(200, chat.contacts())
        case "/api/v1/chat/conversations/direct" where demo != nil:
            let (status, item) = chat.direct(with: body["userId"] as? String ?? "")
            return json(status, item)
        case let chatPath where demo != nil && chatPath.hasPrefix("/api/v1/chat/conversations/"):
            // /api/v1/chat/conversations/{id}/messages|read
            let parts = chatPath.split(separator: "/").map(String.init)
            guard parts.count == 6 else { return problem(404, "NOT_FOUND", "Nicht gefunden") }
            let id = parts[4].lowercased()
            switch (parts[5], method) {
            case ("messages", "POST"):
                let (status, message) = chat.send(body["text"] as? String ?? "", to: id)
                return json(status, message)
            case ("messages", _): return json(200, chat.messages(of: id))
            case ("read", _):
                chat.markRead(id)
                return (204, "")
            default: return problem(404, "NOT_FOUND", "Nicht gefunden")
            }
        case "/api/v1/notifications" where demo != nil:
            return json(200, notifications.inbox())
        case "/api/v1/notifications/read-all" where demo != nil:
            notifications.markAllRead()
            return (204, "")
        case let notificationPath where demo != nil && notificationPath.hasPrefix("/api/v1/notifications/devices"):
            return (204, "")
        case let notificationPath where demo != nil && notificationPath.hasPrefix("/api/v1/notifications/") && notificationPath.hasSuffix("/read"):
            notifications.markRead(notificationPath.split(separator: "/").map(String.init)[3].lowercased())
            return (204, "")
        case "/api/v1/planning/backlog" where demo != nil:
            return json(200, planning.backlog())
        case "/api/v1/planning/resources" where demo != nil:
            return json(200, planning.resources())
        case let crewPath where demo != nil && crewPath.hasPrefix("/api/v1/planning/crews/"):
            // /api/v1/planning/crews/{id}/days/{day}/order|swap
            let parts = crewPath.split(separator: "/").map(String.init)
            guard parts.count == 8 else { return problem(404, "NOT_FOUND", "Nicht gefunden") }
            return json(200, parts[7] == "order" ? planning.order(crewId: parts[4].lowercased(), day: parts[6], body: body)
                                                 : planning.swap(crewId: parts[4].lowercased(), day: parts[6], body: body))
        case "/api/v1/access/me/permissions" where demo != nil:
            // Demo als Bauleiter mit Planung und IT-Bearbeitung, damit alle Funktionen sichtbar sind.
            return json(200, ["bauakten.read", "bauakten.edit", "bauakten.create", "contacts.read", "planning.read", "planning.manage",
                              "helpdesk.agent"])
        case "/api/v1/files" where demo != nil:
            // Eigene Datei ohne Bauakte ankündigen (Anhang einer Antwort im Helpdesk).
            if let id = (body["id"] as? String)?.lowercased() { files[id] = ((body["sizeBytes"] as? Int) ?? 0, 0) }
            return json(200, body)
        case "/api/v1/tickets" where demo != nil:
            return json(200, helpdesk.list(all: true))
        case let ticketPath where demo != nil && ticketPath.hasPrefix("/api/v1/tickets/"):
            let parts = ticketPath.split(separator: "/").map(String.init)
            switch (parts.count, parts.last) {
            case (4, "mine"): return json(200, helpdesk.list(all: false))
            case (4, "device"): return (200, #"{"number":"T-2026-0001"}"#)
            case (4, _) where method == "PATCH":
                return helpdesk.update(parts[3].lowercased(), body: body).map { json(200, $0) } ?? problem(404, "NOT_FOUND", "Nicht gefunden")
            case (4, _): return helpdesk.detail(parts[3].lowercased()).map { json(200, $0) } ?? problem(404, "NOT_FOUND", "Nicht gefunden")
            case (5, "comments"): return helpdesk.comment(parts[3].lowercased(), body: body).map { json(200, $0) } ?? problem(404, "NOT_FOUND", "Nicht gefunden")
            case (5, "reopen"): return helpdesk.update(parts[3].lowercased(), body: ["status": "inProgress"]).map { json(200, $0) }
                ?? problem(404, "NOT_FOUND", "Nicht gefunden")
            default: return problem(404, "NOT_FOUND", "Nicht gefunden")
            }
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

    private func json(_ status: Int, _ object: Any) -> (Int, String) {
        let data = (try? JSONSerialization.data(withJSONObject: object)) ?? Data("{}".utf8)
        return (status, String(decoding: data, as: UTF8.self))
    }

    private func problem(_ status: Int, _ code: String, _ detail: String) -> (Int, String) {
        (status, #"{"status":\#(status),"title":"Fehler","detail":"\#(detail)","code":"\#(code)"}"#)
    }
}
