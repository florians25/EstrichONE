import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Abstraktion des HTTP-Zugriffs, damit Prüfung und API-Aufrufe ohne Netzwerk testbar sind.
public protocol HTTPTransport: Sendable {
    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse)
}

extension HTTPTransport {
    public func get(_ url: URL) async throws -> (Data, HTTPURLResponse) {
        var request = URLRequest(url: url)
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        return try await send(request)
    }
}

extension URLSession: HTTPTransport {
    public func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var request = request
        request.timeoutInterval = min(request.timeoutInterval, 30)
        let (data, response) = try await data(for: request)
        guard let http = response as? HTTPURLResponse else { throw URLError(.badServerResponse) }
        return (data, http)
    }
}

/// Ergebnis der Verbindungsprüfung, das der Benutzer bestätigt (Schritt 2 der Einrichtung).
public struct VerifiedServer: Hashable, Sendable, Codable {
    public init(address: ServerAddress, info: ServerInfo) {
        self.address = address
        self.info = info
    }

    public let address: ServerAddress
    public let info: ServerInfo
}

public enum ServerCheckError: Error, Equatable, Sendable {
    case unreachable
    case certificateInvalid
    case notEstrichOne
    case appTooOld(minimum: String)
    case serverTooOld
    case notSetUp
    case unexpectedResponse(status: Int)

    /// Verständliche Meldung mit Handlungshinweis (docs/reengineering/05, Abschnitt 6).
    public var message: String {
        switch self {
        case .unreachable:
            "Server nicht erreichbar. Prüfen Sie die Adresse und ob Sie mit dem Internet bzw. dem Firmen-WLAN verbunden sind."
        case .certificateInvalid:
            "Das Sicherheitszertifikat des Servers ist ungültig. Die Verbindung wurde aus Sicherheitsgründen abgebrochen."
        case .notEstrichOne:
            "Unter dieser Adresse läuft kein EstrichONE-Server."
        case let .appTooOld(minimum):
            "Diese App-Version ist zu alt für Ihren Server (mindestens \(minimum) nötig). Bitte die App aktualisieren."
        case .serverTooOld:
            "Der Server ist zu alt für diese App. Bitte die IT-Administration informieren."
        case .notSetUp:
            "Der Server ist noch nicht eingerichtet. Bitte zuerst die Ersteinrichtung abschließen."
        case let .unexpectedResponse(status):
            "Der Server antwortet nicht wie erwartet (Status \(status))."
        }
    }
}

/// Prüft einen Server vor der Einrichtung: Erreichbarkeit, gültiges Zertifikat (durch das System),
/// EstrichONE-Kennung und Versionsverträglichkeit. Ein unbekannter Server wird nie stillschweigend übernommen.
public struct ServerConnectionChecker: Sendable {
    public static let supportedApiVersion = "v1"

    private let transport: HTTPTransport
    private let appVersion: SemanticVersion

    public init(transport: HTTPTransport, appVersion: SemanticVersion) {
        self.transport = transport
        self.appVersion = appVersion
    }

    public func check(_ address: ServerAddress) async -> Result<VerifiedServer, ServerCheckError> {
        let data: Data
        let response: HTTPURLResponse
        do {
            (data, response) = try await transport.get(address.url(for: "api/v1/server-info"))
        } catch {
            return .failure(Self.classify(error))
        }

        switch response.statusCode {
        case 200:
            break
        case 404:
            return .failure(.notEstrichOne)
        case 503 where Self.problemCode(in: data) == "SERVER_NOT_INITIALIZED":
            return .failure(.notSetUp)
        default:
            return .failure(.unexpectedResponse(status: response.statusCode))
        }

        guard let info = try? JSONDecoder().decode(ServerInfo.self, from: data), info.product == ServerInfo.productName else {
            return .failure(.notEstrichOne)
        }

        guard info.apiVersions.contains(Self.supportedApiVersion) else {
            return .failure(.serverTooOld)
        }

        if let minimum = SemanticVersion(info.minClientVersion), appVersion < minimum {
            return .failure(.appTooOld(minimum: minimum.description))
        }

        if info.setupRequired {
            return .failure(.notSetUp)
        }

        return .success(VerifiedServer(address: address, info: info))
    }

    private struct Problem: Decodable {
        let code: String?
    }

    static func problemCode(in data: Data) -> String? {
        (try? JSONDecoder().decode(Problem.self, from: data))?.code
    }

    static func classify(_ error: Error) -> ServerCheckError {
        guard let urlError = error as? URLError else { return .unreachable }
        switch urlError.code {
        case .serverCertificateUntrusted, .serverCertificateHasBadDate, .serverCertificateHasUnknownRoot,
             .serverCertificateNotYetValid, .clientCertificateRejected, .secureConnectionFailed, .appTransportSecurityRequiresSecureConnection:
            return .certificateInvalid
        default:
            return .unreachable
        }
    }
}
