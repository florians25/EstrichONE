import Foundation

/// Grund, aus dem eine Sitzung serverseitig nicht mehr gilt (docs/reengineering/11, Abschnitt 5). Nur dann verlangt die
/// App wieder das Passwort – sonst genügt Face ID/Touch ID/Gerätecode bzw. die Pool-PIN.
public enum SessionEnd: Equatable, Sendable {
    /// Sitzung ungültig ohne genaueren Grund (z. B. Gerätesignatur passt nicht mehr).
    case signedOut
    /// Länger nicht genutzt (Web-Einstellungen › Anmeldung › Apps).
    case expired
    /// Passwort geändert oder zurückgesetzt – alle Sitzungen der Person wurden beendet.
    case passwordChanged
    /// Abgemeldet bzw. im IT Center beendet.
    case revoked
    /// Anmeldedaten wurden doppelt verwendet – aus Sicherheitsgründen beendet.
    case reused
    case userDisabled
    case deviceLocked
    case deviceRevoked

    /// Codes der Erneuerung (`auth/refresh`).
    init?(code: String?) {
        switch code {
        case "USER_DISABLED": self = .userDisabled
        case "DEVICE_LOCKED": self = .deviceLocked
        case "DEVICE_REVOKED": self = .deviceRevoked
        case "SESSION_EXPIRED": self = .expired
        case "PASSWORD_CHANGED": self = .passwordChanged
        case "SESSION_REVOKED": self = .revoked
        case "REFRESH_TOKEN_REUSED": self = .reused
        case "INVALID_REFRESH_TOKEN", "DEVICE_PROOF_INVALID": self = .signedOut
        default: return nil
        }
    }

    /// Codes einer gewöhnlichen Anfrage, die ohne Rückfrage feststehen. `UNAUTHORIZED` gehört nicht dazu: Das kann auch
    /// ein Zugriffstoken sein, das eine parallele Erneuerung gerade ersetzt hat – darüber entscheidet die Erneuerung.
    init?(requestCode code: String?) {
        switch code {
        case "USER_DISABLED": self = .userDisabled
        case "DEVICE_LOCKED": self = .deviceLocked
        case "DEVICE_REVOKED": self = .deviceRevoked
        default: return nil
        }
    }

    /// Erklärung für die Anmeldung mit Passwort.
    public var message: String {
        switch self {
        case .signedOut: "Die Anmeldung auf diesem Gerät gilt nicht mehr. Bitte einmal mit Passwort anmelden."
        case .expired: "Sie waren längere Zeit nicht angemeldet. Bitte einmal mit Passwort anmelden – danach genügt wieder Face ID bzw. der Gerätecode."
        case .passwordChanged: "Ihr Passwort wurde geändert. Bitte mit dem neuen Passwort anmelden."
        case .revoked: "Die Sitzung auf diesem Gerät wurde beendet (abgemeldet, z. B. im IT Center). Bitte erneut anmelden."
        case .reused: "Die Sitzung wurde aus Sicherheitsgründen beendet. Bitte erneut anmelden."
        case .userDisabled: "Ihr Benutzerkonto ist gesperrt. Bitte an die Administration wenden."
        case .deviceLocked: "Dieses Gerät ist gesperrt. Bitte an die IT-Administration wenden."
        case .deviceRevoked: "Dieses Gerät wurde aus EstrichONE entfernt. Die App muss neu eingerichtet werden."
        }
    }

    /// Mit Passwort neu anmelden (statt Hinweis „gesperrt“ bzw. „entfernt“).
    public var requiresPassword: Bool {
        switch self {
        case .signedOut, .expired, .passwordChanged, .revoked, .reused: true
        case .userDisabled, .deviceLocked, .deviceRevoked: false
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
    private var hasEnded = false
    private let retryDelayMilliseconds: Int

    /// - Parameters:
    ///   - keys: Geräteschlüssel, wenn die Sitzung an ein Gerät gebunden ist (Erneuerung nur mit Signatur).
    ///   - persist: speichert erneuerte Tokens (verschlüsselt im Tresor des Benutzers).
    ///   - ended: wird aufgerufen, wenn der Server die Sitzung nicht mehr akzeptiert.
    public init(api: APIClient, session: StoredSession, keys: DeviceKeys?,
                now: @escaping @Sendable () -> Date = Date.init,
                retryDelayMilliseconds: Int = 1_500,
                persist: @escaping @Sendable (StoredSession) async -> Void,
                ended: @escaping @Sendable (SessionEnd) async -> Void) {
        self.retryDelayMilliseconds = retryDelayMilliseconds
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
        let token = session.accessToken
        do {
            return try await call(api, token)
        } catch where error.status == 401 && SessionEnd(requestCode: error.code) == nil {
            // Abgelaufen oder „UNAUTHORIZED“: Erneuern klärt, ob die Sitzung noch gilt. Hat eine parallele Anfrage das
            // Token inzwischen schon ersetzt, genügt die Wiederholung mit dem neuen.
            if session.accessToken == token {
                try await refresh()
            }
            do {
                return try await call(api, session.accessToken)
            } catch {
                if error.status == 401, let end = SessionEnd(requestCode: error.code) {
                    await report(end)
                }
                throw error
            }
        } catch {
            if error.status == 401, let end = SessionEnd(requestCode: error.code) {
                await report(end)
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

    /// Erneuert mit Gerätesignatur. Netzfehler beenden nichts (der alte Refresh-Token bleibt, der Server stellt bei
    /// einer verlorenen Antwort neu aus); nur ein eindeutiger Code des Servers beendet die Sitzung.
    private func performRefresh() async throws {
        for attempt in 0..<2 {
            let refreshToken = session.refreshToken
            let signature = try keys.map { try DeviceProof.refresh(refreshToken, keys: $0) }
            do {
                let refreshed: RefreshResponse = try await api.post(
                    "auth/refresh", body: RefreshRequest(refreshToken: refreshToken, deviceSignature: signature))
                session.apply(refreshed)
                await persist(session)
                return
            } catch where error.code == "REFRESH_IN_PROGRESS" && attempt == 0 {
                // Gleichzeitig erneuert (zweite App-Instanz, Wiederholung): kurz warten, dann noch einmal.
                try? await Task.sleep(for: .milliseconds(retryDelayMilliseconds))
            } catch {
                if let end = SessionEnd(code: error.code) {
                    await report(end)
                }
                throw error
            }
        }
        throw APIError.server(status: 409, code: "REFRESH_IN_PROGRESS", message: nil)
    }

    /// Meldet das Ende genau einmal, auch wenn mehrere Anfragen gleichzeitig scheitern.
    private func report(_ end: SessionEnd) async {
        guard !hasEnded else { return }
        hasEnded = true
        await ended(end)
    }
}
