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
    /// Dienst (`kSecAttrService`) der Einträge.
    public var serviceName: String { service }

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

#if canImport(Security)
extension KeychainSecretStore {
    /// Sind unter diesem Dienst Einträge gespeichert (z. B. Reste einer früheren Installation)?
    public var hasItems: Bool {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: serviceName,
                                    kSecMatchLimit as String: kSecMatchLimitOne, kSecReturnAttributes as String: true]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        // Gesperrtes Gerät: Einträge existieren, sind nur gerade nicht lesbar.
        return status == errSecSuccess || status == errSecInteractionNotAllowed
    }

    /// Löscht alle Einträge dieses Dienstes – nur über Klasse und Dienst, unabhängig von Schlüsselnamen früherer Versionen.
    public func removeAll() throws {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: serviceName]
        // iOS löscht alle passenden Einträge auf einmal; zur Sicherheit wiederholen, bis nichts mehr da ist.
        for _ in 0..<8 {
            let status = SecItemDelete(query as CFDictionary)
            if status == errSecItemNotFound { return }
            guard status == errSecSuccess else { throw SecretStoreError(status: status) }
        }
    }
}
#endif
