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
