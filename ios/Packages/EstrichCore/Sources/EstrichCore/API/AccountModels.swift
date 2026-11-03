import Foundation

// Typen der Anmelde- und Geräteendpunkte (contracts/openapi/v1.json). `ContractTests` prüft Feldnamen
// und Pflichtfelder gegen den Vertrag.

public struct UserSummary: Codable, Hashable, Sendable {
    public let id: UUID
    public let displayName: String
    public let userName: String
    public let email: String?

    public init(id: UUID, displayName: String, userName: String, email: String?) {
        self.id = id
        self.displayName = displayName
        self.userName = userName
        self.email = email
    }
}

public struct LoginRequest: Encodable, Sendable {
    public let login: String
    public let password: String
    public let client: String
    public let clientName: String?
    /// Nur bei Konten mit Zwei-Faktor-Anmeldung, nach 401 `TWO_FACTOR_REQUIRED`: Code der Authenticator-App
    /// (6 Ziffern) oder Wiederherstellungscode (`xxxxx-xxxxx`). Fehlt er, wird das Feld nicht gesendet.
    public let twoFactorCode: String?

    public init(login: String, password: String, client: String, clientName: String?, twoFactorCode: String? = nil) {
        self.login = login
        self.password = password
        self.client = client
        self.clientName = clientName
        self.twoFactorCode = twoFactorCode
    }
}

/// Zweiter Schritt der Anmeldung (Zwei-Faktor-Anmeldung, docs/sicherheit.md).
public enum TwoFactorLogin {
    /// Der Server verlangt einen Bestätigungscode: Code abfragen und dieselbe Anmeldung mit Code wiederholen.
    public static let requiredCode = "TWO_FACTOR_REQUIRED"
    /// Code falsch, abgelaufen oder bereits verwendet: Feld leeren und erneut fragen.
    public static let invalidCode = "TWO_FACTOR_INVALID"

    /// Leerzeichen entfernen, Kleinschreibung (Wiederherstellungscodes); `nil`, wenn nichts eingegeben wurde.
    public static func normalize(_ code: String) -> String? {
        let cleaned = code.filter { !$0.isWhitespace }.lowercased()
        return cleaned.isEmpty ? nil : cleaned
    }

    /// Eingabe vollständig? 6 Ziffern bzw. Wiederherstellungscode mit 10 Zeichen (Bindestrich optional).
    public static func isComplete(_ code: String, recovery: Bool) -> Bool {
        let cleaned = code.filter { !$0.isWhitespace && $0 != "-" }
        return recovery ? cleaned.count == 10 && cleaned.allSatisfy { $0.isLetter || $0.isNumber }
            : cleaned.count == 6 && cleaned.allSatisfy { $0.isASCII && $0.isNumber }
    }
}

public extension APIError {
    /// Anmeldung braucht (noch) einen Bestätigungscode.
    var requiresTwoFactorCode: Bool { status == 401 && code == TwoFactorLogin.requiredCode }
    /// Bestätigungscode abgelehnt.
    var isInvalidTwoFactorCode: Bool { status == 401 && code == TwoFactorLogin.invalidCode }
}

public struct TokenResponse: Codable, Sendable {
    public let accessToken: String
    public let accessTokenExpiresAt: Date
    public let refreshToken: String
    public let refreshTokenExpiresAt: Date
    public let user: UserSummary
    public let mustChangePassword: Bool
}

public struct RefreshRequest: Encodable, Sendable {
    public let refreshToken: String
    public let deviceSignature: String?
}

public struct RefreshResponse: Codable, Sendable {
    public let accessToken: String
    public let accessTokenExpiresAt: Date
    public let refreshToken: String
    public let refreshTokenExpiresAt: Date
}

public struct ChangePasswordRequest: Encodable, Sendable {
    public let currentPassword: String
    public let newPassword: String
}

public enum DeviceKind: String, Codable, Sendable, CaseIterable {
    case personal
    case pool
}

public enum DeviceStatus: String, Codable, Sendable {
    case pending
    case active
    case locked
    case retired
}

public struct RegisterDeviceRequest: Encodable, Sendable {
    public let name: String
    public let platform: String
    public let kind: DeviceKind
    public let publicKey: String
    public let model: String?
    public let osVersion: String?
    public let appVersion: String?
    public let enrollmentCode: String?
}

public struct AttachDeviceRequest: Encodable, Sendable {
    public let deviceId: UUID
    public let signature: String
}

public struct DeviceResponse: Codable, Sendable, Equatable {
    public let id: UUID
    public let name: String
    public let kind: DeviceKind
    public let status: DeviceStatus
    public let assignedUserId: UUID?
}

/// Sitzung eines Benutzers auf diesem Gerät. Liegt verschlüsselt im Datentresor des Benutzers.
public struct StoredSession: Codable, Sendable, Equatable {
    public var accessToken: String
    public var accessTokenExpiresAt: Date
    public var refreshToken: String
    public var refreshTokenExpiresAt: Date
    public var user: UserSummary

    public init(_ tokens: TokenResponse) {
        accessToken = tokens.accessToken
        accessTokenExpiresAt = tokens.accessTokenExpiresAt
        refreshToken = tokens.refreshToken
        refreshTokenExpiresAt = tokens.refreshTokenExpiresAt
        user = tokens.user
    }

    public init(accessToken: String, accessTokenExpiresAt: Date, refreshToken: String, refreshTokenExpiresAt: Date, user: UserSummary) {
        self.accessToken = accessToken
        self.accessTokenExpiresAt = accessTokenExpiresAt
        self.refreshToken = refreshToken
        self.refreshTokenExpiresAt = refreshTokenExpiresAt
        self.user = user
    }

    mutating func apply(_ refreshed: RefreshResponse) {
        accessToken = refreshed.accessToken
        accessTokenExpiresAt = refreshed.accessTokenExpiresAt
        refreshToken = refreshed.refreshToken
        refreshTokenExpiresAt = refreshed.refreshTokenExpiresAt
    }
}

public struct CreateEnrollmentCodeRequest: Encodable, Sendable {
    public let kind: DeviceKind
    public let deviceName: String?

    public init(kind: DeviceKind, deviceName: String?) {
        self.kind = kind
        self.deviceName = deviceName
    }
}

public struct EnrollmentCodeResponse: Codable, Sendable {
    public let code: String
    public let kind: DeviceKind
    public let deviceName: String?
    public let expiresAt: Date
    public let link: String?
}
