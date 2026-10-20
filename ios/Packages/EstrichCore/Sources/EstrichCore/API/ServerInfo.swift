import Foundation

/// Antwort von `GET /api/v1/server-info` (contracts/openapi/v1.json, Schema `ServerInfo`).
public struct ServerInfo: Codable, Hashable, Sendable {
    public let product: String
    public let serverName: String
    public let serverVersion: String
    public let apiVersions: [String]
    public let minClientVersion: String
    public let authMethods: [String]
    public let instanceEpoch: UUID
    public let setupRequired: Bool

    public init(product: String, serverName: String, serverVersion: String, apiVersions: [String], minClientVersion: String,
                authMethods: [String], instanceEpoch: UUID, setupRequired: Bool) {
        self.product = product
        self.serverName = serverName
        self.serverVersion = serverVersion
        self.apiVersions = apiVersions
        self.minClientVersion = minClientVersion
        self.authMethods = authMethods
        self.instanceEpoch = instanceEpoch
        self.setupRequired = setupRequired
    }

    public static let productName = "EstrichONE"
}
