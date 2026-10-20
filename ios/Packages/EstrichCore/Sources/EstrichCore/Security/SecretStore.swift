import Foundation
#if canImport(Security)
import Security
#endif

/// Ablage für Geheimnisse des Geräts (Geräteschlüssel, Datenschlüssel persönlicher Geräte).
public protocol SecretStore: Sendable {
    func data(for key: String) throws -> Data?
    func set(_ data: Data, for key: String) throws
    func remove(_ key: String) throws
}

public struct SecretStoreError: Error, Equatable, Sendable {
    public let status: Int32
}

/// Für Tests und Vorschauen.
public final class InMemorySecretStore: SecretStore, @unchecked Sendable {
    private var values: [String: Data] = [:]
    private let lock = NSLock()

    public init() {}

    public func data(for key: String) throws -> Data? { lock.withLock { values[key] } }
    public func set(_ data: Data, for key: String) throws { lock.withLock { values[key] = data } }
    public func remove(_ key: String) throws { _ = lock.withLock { values.removeValue(forKey: key) } }
}

#if canImport(Security)
/// Schlüsselbund mit `WhenUnlockedThisDeviceOnly`: nicht in Backups, nicht auf andere Geräte übertragbar.
public struct KeychainSecretStore: SecretStore {
    private let service: String

    public init(service: String = "de.estrichone.device") {
        self.service = service
    }

    public func data(for key: String) throws -> Data? {
        var query = baseQuery(key)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw SecretStoreError(status: status) }
        return result as? Data
    }

    public func set(_ data: Data, for key: String) throws {
        let update = [kSecValueData as String: data] as CFDictionary
        var status = SecItemUpdate(baseQuery(key) as CFDictionary, update)
        if status == errSecItemNotFound {
            var add = baseQuery(key)
            add[kSecValueData as String] = data
            add[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
            status = SecItemAdd(add as CFDictionary, nil)
        }
        guard status == errSecSuccess else { throw SecretStoreError(status: status) }
    }

    public func remove(_ key: String) throws {
        let status = SecItemDelete(baseQuery(key) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw SecretStoreError(status: status) }
    }

    private func baseQuery(_ key: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: key]
    }
}
#endif
