import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif

/// Datentresor eines Benutzers auf diesem Gerät (docs/reengineering/11, Abschnitt 4.3; ADR 0012).
///
/// Ein zufälliger Datenschlüssel verschlüsselt alles, was dem Benutzer gehört. Er liegt doppelt umhüllt vor:
/// innen mit einem PIN-Schlüssel (nur Poolgeräte), außen mit dem Geräteschlüssel. Ohne PIN ist der Tresor
/// auch für die App nicht lesbar, ohne dieses Gerät lässt sich die PIN nicht durchprobieren.
public struct VaultEnvelope: Codable, Equatable, Sendable {
    public static let pinIterations = 210_000

    public let version: Int
    public let pinProtected: Bool
    public let salt: Data?
    public let iterations: Int?
    public let sealedKey: Data
}

public enum VaultError: Error, Equatable, Sendable {
    case wrongPIN
    case pinRequired
    /// Der Tresor gehört zu einem anderen Gerät oder ist beschädigt.
    case unreadable
}

public enum UserVault {
    /// Legt einen Tresor an. `pin == nil` für persönliche Geräte.
    public static func create(pin: String?, keys: DeviceKeys, iterations: Int = VaultEnvelope.pinIterations)
        throws -> (envelope: VaultEnvelope, dataKey: SymmetricKey) {
        let dataKey = SymmetricKey(size: .bits256)
        let raw = dataKey.withUnsafeBytes { Data($0) }
        var inner = raw
        var salt: Data?
        if let pin {
            let newSalt = Data(SymmetricKey(size: .bits128).withUnsafeBytes { Data($0) })
            guard let combined = try AES.GCM.seal(raw, using: pinKey(pin, salt: newSalt, iterations: iterations)).combined else {
                throw VaultError.unreadable
            }
            inner = combined
            salt = newSalt
        }
        let envelope = VaultEnvelope(version: 1, pinProtected: pin != nil, salt: salt, iterations: pin == nil ? nil : iterations,
                                     sealedKey: try keys.seal(inner))
        return (envelope, dataKey)
    }

    public static func unlock(_ envelope: VaultEnvelope, pin: String?, keys: DeviceKeys) throws(VaultError) -> SymmetricKey {
        let inner: Data
        do {
            inner = try keys.open(envelope.sealedKey)
        } catch {
            throw .unreadable
        }
        guard envelope.pinProtected else { return SymmetricKey(data: inner) }
        guard let pin else { throw .pinRequired }
        guard let salt = envelope.salt, let iterations = envelope.iterations else { throw .unreadable }
        do {
            let raw = try AES.GCM.open(AES.GCM.SealedBox(combined: inner), using: pinKey(pin, salt: salt, iterations: iterations))
            return SymmetricKey(data: raw)
        } catch {
            throw .wrongPIN
        }
    }

    /// Verschlüsselt Inhalte des Benutzers (Sitzung, später Datenbank-Schlüssel und Fotos) mit dem Datenschlüssel.
    public static func encrypt(_ plaintext: Data, with dataKey: SymmetricKey) throws -> Data {
        guard let combined = try AES.GCM.seal(plaintext, using: dataKey).combined else { throw VaultError.unreadable }
        return combined
    }

    public static func decrypt(_ ciphertext: Data, with dataKey: SymmetricKey) throws(VaultError) -> Data {
        do {
            return try AES.GCM.open(AES.GCM.SealedBox(combined: ciphertext), using: dataKey)
        } catch {
            throw .unreadable
        }
    }

    private static func pinKey(_ pin: String, salt: Data, iterations: Int) -> SymmetricKey {
        SymmetricKey(data: PBKDF2.sha256(password: Data(pin.utf8), salt: salt, iterations: iterations))
    }
}
