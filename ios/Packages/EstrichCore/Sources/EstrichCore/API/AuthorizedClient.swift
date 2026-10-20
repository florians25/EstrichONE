import Foundation

/// Grund, aus dem eine Sitzung serverseitig nicht mehr gilt (docs/reengineering/11, Abschnitt 5).
public enum SessionEnd: Equatable, Sendable {
    case signedOut
    case userDisabled
    case deviceLocked
    case deviceRevoked

    init?(code: String?) {
        switch code {
        case "USER_DISABLED": self = .userDisabled
        case "DEVICE_LOCKED": self = .deviceLocked
        case "DEVICE_REVOKED": self = .deviceRevoked
        case "UNAUTHORIZED", "SESSION_EXPIRED", "REFRESH_TOKEN_REUSED", "INVALID_REFRESH_TOKEN", "DEVICE_PROOF_INVALID": self = .signedOut
        default: return nil
        }
    }

    public var message: String {
        switch self {
        case .signedOut: "Die Anmeldung ist abgelaufen. Bitte erneut anmelden."
        case .userDisabled: "Ihr Benutzerkonto ist gesperrt. Bitte an die Administration wenden."
        case .deviceLocked: "Dieses Gerät ist gesperrt. Bitte an die IT-Administration wenden."
        case .deviceRevoked: "Dieses Gerät wurde aus EstrichONE entfernt. Die App muss neu eingerichtet werden."
        }
    }
}

/// API-Zugriff für einen angemeldeten Benutzer: erneuert das Zugriffstoken rechtzeitig (mit Gerätesignatur,
/// genau einmal auch bei parallelen Anfragen) und meldet, wenn die Sitzung serverseitig beendet wurde.
public actor AuthorizedClient {
    public private(set) var session: StoredSession
    private let api: APIClient
    private let keys: DeviceKeys?
    private let now: @Sendable () -> Date
    private let persist: @Sendable (StoredSession) async -> Void
    private let ended: @Sendable (SessionEnd) async -> Void
    private var refreshing: Task<Void, Error>?

    /// - Parameters:
    ///   - keys: Geräteschlüssel, wenn die Sitzung an ein Gerät gebunden ist (Erneuerung nur mit Signatur).
    ///   - persist: speichert erneuerte Tokens (verschlüsselt im Tresor des Benutzers).
    ///   - ended: wird aufgerufen, wenn der Server die Sitzung nicht mehr akzeptiert.
    public init(api: APIClient, session: StoredSession, keys: DeviceKeys?,
                now: @escaping @Sendable () -> Date = Date.init,
                persist: @escaping @Sendable (StoredSession) async -> Void,
                ended: @escaping @Sendable (SessionEnd) async -> Void) {
        self.api = api
        self.session = session
        self.keys = keys
        self.now = now
        self.persist = persist
        self.ended = ended
    }

    public func get<Response: Decodable & Sendable>(_ path: String, query: [URLQueryItem] = []) async throws(APIError) -> Response {
        try await perform { api, token throws(APIError) in try await api.get(path, query: query, accessToken: token) }
    }

    public func send<Response: Decodable & Sendable>(_ request: APIRequest) async throws(APIError) -> Response {
        try await perform { api, token throws(APIError) in try await api.perform(request, accessToken: token) }
    }

    public func post<Response: Decodable & Sendable>(_ path: String) async throws(APIError) -> Response {
        try await perform { api, token throws(APIError) in try await api.post(path, accessToken: token) }
    }

    public func post<Response: Decodable & Sendable, Body: Encodable & Sendable>(_ path: String, body: Body) async throws(APIError) -> Response {
        try await perform { api, token throws(APIError) in try await api.post(path, body: body, accessToken: token) }
    }

    /// Erneuert die Tokens jetzt (z. B. beim App-Start, um den Gerätestatus zu prüfen).
    public func refreshNow() async throws(APIError) {
        try await refresh()
    }

    private func perform<Response: Sendable>(
        _ call: @Sendable (APIClient, String) async throws(APIError) -> Response
    ) async throws(APIError) -> Response {
        if session.accessTokenExpiresAt.timeIntervalSince(now()) < 60 {
            try await refresh()
        }
        do {
            return try await call(api, session.accessToken)
        } catch where error.code == "ACCESS_TOKEN_EXPIRED" {
            try await refresh()
            return try await call(api, session.accessToken)
        } catch {
            if let end = SessionEnd(code: error.code), error.status == 401 {
                await ended(end)
            }
            throw error
        }
    }

    private func refresh() async throws(APIError) {
        if let refreshing {
            do {
                try await refreshing.value
            } catch {
                throw APIError.from(error)
            }
            return
        }

        let task = Task { try await performRefresh() }
        refreshing = task
        defer { refreshing = nil }
        do {
            try await task.value
        } catch {
            throw APIError.from(error)
        }
    }

    private func performRefresh() async throws {
        let refreshToken = session.refreshToken
        let signature = try keys.map { try DeviceProof.refresh(refreshToken, keys: $0) }
        do {
            let refreshed: RefreshResponse = try await api.post(
                "auth/refresh", body: RefreshRequest(refreshToken: refreshToken, deviceSignature: signature))
            session.apply(refreshed)
            await persist(session)
        } catch {
            if let end = SessionEnd(code: error.code) {
                await ended(end)
            }
            throw error
        }
    }
}
