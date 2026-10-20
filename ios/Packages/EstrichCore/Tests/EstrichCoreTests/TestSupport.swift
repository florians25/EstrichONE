import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
@testable import EstrichCore

/// Antwortet je nach Methode und Pfad; merkt sich alle Anfragen.
final class RoutingTransport: HTTPTransport, @unchecked Sendable {
    struct Reply {
        let status: Int
        let body: String

        static func json(_ body: String, status: Int = 200) -> Reply { Reply(status: status, body: body) }
        static func problem(_ status: Int, code: String, detail: String = "Abgelehnt") -> Reply {
            Reply(status: status, body: #"{"status":\#(status),"title":"Fehler","detail":"\#(detail)","code":"\#(code)"}"#)
        }
        static let noContent = Reply(status: 204, body: "")
    }

    typealias Handler = (URLRequest) throws -> Reply

    private let lock = NSLock()
    private var routes: [String: Handler] = [:]
    private(set) var requests: [URLRequest] = []

    func on(_ method: String, _ path: String, _ handler: @escaping Handler) {
        lock.withLock { routes["\(method) \(path)"] = handler }
    }

    func on(_ method: String, _ path: String, reply: Reply) {
        on(method, path) { _ in reply }
    }

    func count(_ method: String, _ path: String) -> Int {
        lock.withLock { requests.filter { $0.httpMethod == method && $0.url?.path() == path }.count }
    }

    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let key = "\(request.httpMethod ?? "GET") \(request.url?.path() ?? "")"
        let handler = lock.withLock { () -> Handler? in
            requests.append(request)
            return routes[key]
        }
        guard let handler else { throw URLError(.cannotFindHost) }
        let reply = try handler(request)
        let response = HTTPURLResponse(url: request.url!, statusCode: reply.status, httpVersion: "HTTP/1.1", headerFields: nil)!
        return (Data(reply.body.utf8), response)
    }
}

extension URLRequest {
    func jsonBody() -> [String: Any] {
        guard let body = httpBody, let object = try? JSONSerialization.jsonObject(with: body) as? [String: Any] else { return [:] }
        return object
    }
}

enum Samples {
    static let userId = UUID(uuidString: "0193a1b2-0000-7000-8000-00000000aaaa")!
    static let deviceId = UUID(uuidString: "0193a1b2-0000-7000-8000-00000000dddd")!
    static let server = try! ServerAddress.parse("estrichone.example.com")

    static func tokenJSON(access: String = "eo1_access", refresh: String = "eor1_refresh", expiresIn: TimeInterval = 600,
                          userId: UUID = userId, name: String = "Max Huber", mustChange: Bool = false, now: Date = Date()) -> String {
        """
        {"accessToken":"\(access)","accessTokenExpiresAt":"\(APIDate.format(now.addingTimeInterval(expiresIn)))",
         "refreshToken":"\(refresh)","refreshTokenExpiresAt":"\(APIDate.format(now.addingTimeInterval(86400)))",
         "user":{"id":"\(userId.uuidString)","displayName":"\(name)","userName":"m.huber","email":null},
         "mustChangePassword":\(mustChange)}
        """
    }

    static func refreshJSON(access: String, refresh: String, now: Date = Date()) -> String {
        """
        {"accessToken":"\(access)","accessTokenExpiresAt":"\(APIDate.format(now.addingTimeInterval(600)))",
         "refreshToken":"\(refresh)","refreshTokenExpiresAt":"\(APIDate.format(now.addingTimeInterval(86400)))"}
        """
    }

    static func deviceJSON(kind: String, status: String, id: UUID = deviceId) -> String {
        """
        {"id":"\(id.uuidString)","name":"Fahrzeug 3","platform":"ios","kind":"\(kind)","status":"\(status)","model":null,
         "osVersion":"26.0","appVersion":"0.2.0","assignedUserId":null,"registeredAt":"2026-10-20T06:10:24.7660538+00:00",
         "approvedAt":null,"lastSeenAt":null,"version":1}
        """
    }

    static func tokens(_ json: String = tokenJSON()) -> TokenResponse {
        try! JSONCoding.decoder.decode(TokenResponse.self, from: Data(json.utf8))
    }

    static func temporaryStorage() -> AppStorage {
        AppStorage(root: FileManager.default.temporaryDirectory.appending(path: "estrichcore-\(UUID().uuidString)", directoryHint: .isDirectory))
    }
}
