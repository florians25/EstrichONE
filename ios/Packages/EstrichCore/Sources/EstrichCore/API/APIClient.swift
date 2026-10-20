import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Fehler eines API-Aufrufs mit verständlicher Meldung (docs/reengineering/05, Abschnitt 6).
public enum APIError: Error, Equatable, Sendable {
    /// Keine Verbindung (kein Netz, Zeitüberschreitung, Server nicht erreichbar).
    case offline
    case certificateInvalid
    /// Antwort des Servers im Fehlerformat RFC 9457 mit stabilem `code`.
    case server(status: Int, code: String?, message: String?)
    case invalidResponse

    public var code: String? {
        if case let .server(_, code, _) = self { code } else { nil }
    }

    public var status: Int? {
        if case let .server(status, _, _) = self { status } else { nil }
    }

    public var message: String {
        switch self {
        case .offline:
            "Keine Verbindung zum Server. Bitte Netzverbindung prüfen und erneut versuchen."
        case .certificateInvalid:
            "Das Sicherheitszertifikat des Servers ist ungültig. Die Verbindung wurde aus Sicherheitsgründen abgebrochen."
        case let .server(status, _, message):
            message ?? "Der Server hat die Anfrage abgelehnt (Status \(status))."
        case .invalidResponse:
            "Der Server antwortet nicht wie erwartet."
        }
    }

    static func from(_ error: Error) -> APIError {
        if let apiError = error as? APIError { return apiError }
        return ServerConnectionChecker.classify(error) == .certificateInvalid ? .certificateInvalid : .offline
    }
}

/// Leere Antwort (`204 No Content`).
public struct NoContent: Decodable, Sendable {
    public init() {}
}

/// Schlanker JSON-Client für die EstrichONE-API v1 (ADR 0012).
public struct APIClient: Sendable {
    public let server: ServerAddress
    private let transport: HTTPTransport

    public init(server: ServerAddress, transport: HTTPTransport) {
        self.server = server
        self.transport = transport
    }

    public func get<Response: Decodable & Sendable>(_ path: String, accessToken: String? = nil) async throws(APIError) -> Response {
        try await perform("GET", path, body: nil, accessToken: accessToken)
    }

    public func post<Response: Decodable & Sendable>(_ path: String, accessToken: String? = nil) async throws(APIError) -> Response {
        try await perform("POST", path, body: nil, accessToken: accessToken)
    }

    public func post<Response: Decodable & Sendable, Body: Encodable & Sendable>(
        _ path: String, body: Body, accessToken: String? = nil
    ) async throws(APIError) -> Response {
        let data: Data
        do {
            data = try JSONCoding.encoder.encode(body)
        } catch {
            throw .invalidResponse
        }
        return try await perform("POST", path, body: data, accessToken: accessToken)
    }

    private func perform<Response: Decodable & Sendable>(
        _ method: String, _ path: String, body: Data?, accessToken: String?
    ) async throws(APIError) -> Response {
        var request = URLRequest(url: server.url(for: "api/v1/" + path))
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let accessToken {
            request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        }
        if let body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = body
        }

        let data: Data
        let response: HTTPURLResponse
        do {
            (data, response) = try await transport.send(request)
        } catch {
            throw APIError.from(error)
        }

        guard (200..<300).contains(response.statusCode) else {
            let problem = try? JSONCoding.decoder.decode(Problem.self, from: data)
            throw .server(status: response.statusCode, code: problem?.code, message: problem?.detail ?? problem?.title)
        }

        if Response.self == NoContent.self, let empty = NoContent() as? Response {
            return empty
        }
        do {
            return try JSONCoding.decoder.decode(Response.self, from: data)
        } catch {
            throw .invalidResponse
        }
    }

    private struct Problem: Decodable {
        let title: String?
        let detail: String?
        let code: String?
    }
}

/// JSON-Format der API: camelCase, Aufzählungen als Text, Zeitpunkte ISO 8601 mit bis zu 7 Nachkommastellen (.NET).
public enum JSONCoding {
    public static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let text = try container.decode(String.self)
            guard let date = APIDate.parse(text) else {
                throw DecodingError.dataCorruptedError(in: container, debugDescription: "Kein ISO-8601-Zeitpunkt: \(text)")
            }
            return date
        }
        return decoder
    }()

    public static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(APIDate.format(date))
        }
        return encoder
    }()
}

/// ISO-8601-Zeitpunkte im Format der API.
public enum APIDate {
    /// Akzeptiert `2026-10-20T06:10:24Z`, `…24.7660538Z` und `…24.766+02:00`.
    public static func parse(_ text: String) -> Date? {
        // Nachkommastellen auf Millisekunden kürzen: Der Foundation-Parser verarbeitet nur drei.
        var normalized = text
        if let dot = text.firstIndex(of: ".") {
            let fractionEnd = text[dot...].dropFirst().firstIndex { !$0.isNumber } ?? text.endIndex
            let digits = text[text.index(after: dot)..<fractionEnd]
            let millis = String((digits + "000").prefix(3))
            normalized = String(text[..<dot]) + "." + millis + String(text[fractionEnd...])
        }
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = normalized.contains(".") ? [.withInternetDateTime, .withFractionalSeconds] : [.withInternetDateTime]
        return formatter.date(from: normalized)
    }

    public static func format(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
