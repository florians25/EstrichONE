import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif

/// Geräteschlüssel (docs/reengineering/11, Abschnitt 1): signiert Nachweise gegenüber dem Server
/// und umhüllt die Datenschlüssel der Benutzer. Der private Teil verlässt das Gerät nie.
public protocol DeviceKeys: Sendable {
    /// Öffentlicher Signaturschlüssel als SubjectPublicKeyInfo (DER) – so erwartet ihn der Server.
    var signingPublicKey: Data { get }
    /// ECDSA P-256 mit SHA-256, Signatur im Rohformat (r‖s, 64 Byte).
    func sign(_ message: Data) throws -> Data
    /// Verschlüsselt Daten so, dass nur dieses Gerät sie wieder öffnen kann (ECIES).
    func seal(_ plaintext: Data) throws -> Data
    func open(_ sealed: Data) throws -> Data
}

public enum DeviceKeyError: Error, Equatable, Sendable {
    case malformed
    case cannotOpen
}

/// Nachweise, die der Server prüft (Präfixe wie in `Device.cs`).
public enum DeviceProof {
    public static func refresh(_ refreshToken: String, keys: DeviceKeys) throws -> String {
        try keys.sign(Data(("EstrichONE refresh v1\n" + refreshToken).utf8)).base64EncodedString()
    }

    public static func attach(_ accessToken: String, keys: DeviceKeys) throws -> String {
        try keys.sign(Data(("EstrichONE attach v1\n" + accessToken).utf8)).base64EncodedString()
    }
}

/// ECIES nach dem Muster ephemerer Schlüssel → Schlüsselvereinbarung → HKDF-SHA256 → AES-GCM.
/// Ausgabe: 65 Byte öffentlicher Ephemeral-Schlüssel (X9.63) + AES-GCM (Nonce, Chiffrat, Tag).
enum ECIES {
    private static let info = Data("EstrichONE device seal v1".utf8)

    static func seal(_ plaintext: Data, to recipient: P256.KeyAgreement.PublicKey) throws -> Data {
        let ephemeral = P256.KeyAgreement.PrivateKey()
        let secret = try ephemeral.sharedSecretFromKeyAgreement(with: recipient)
        let key = derive(secret, ephemeral: ephemeral.publicKey)
        guard let combined = try AES.GCM.seal(plaintext, using: key).combined else { throw DeviceKeyError.malformed }
        return ephemeral.publicKey.x963Representation + combined
    }

    static func open(_ sealed: Data, agreement: (P256.KeyAgreement.PublicKey) throws -> SharedSecret) throws -> Data {
        guard sealed.count > 65 else { throw DeviceKeyError.malformed }
        let ephemeral: P256.KeyAgreement.PublicKey
        do {
            ephemeral = try P256.KeyAgreement.PublicKey(x963Representation: sealed.prefix(65))
        } catch {
            throw DeviceKeyError.malformed
        }
        do {
            let key = derive(try agreement(ephemeral), ephemeral: ephemeral)
            return try AES.GCM.open(AES.GCM.SealedBox(combined: sealed.dropFirst(65)), using: key)
        } catch {
            throw DeviceKeyError.cannotOpen
        }
    }

    private static func derive(_ secret: SharedSecret, ephemeral: P256.KeyAgreement.PublicKey) -> SymmetricKey {
        secret.hkdfDerivedSymmetricKey(using: SHA256.self, salt: ephemeral.x963Representation, sharedInfo: info, outputByteCount: 32)
    }
}

/// Software-Schlüssel, gespeichert im Schlüsselbund. Nur für den Simulator und Tests; echte Geräte nutzen
/// die Secure Enclave (`SecureEnclaveDeviceKeys`).
public struct SoftwareDeviceKeys: DeviceKeys {
    private let signing: P256.Signing.PrivateKey
    private let agreement: P256.KeyAgreement.PrivateKey

    public init() {
        signing = P256.Signing.PrivateKey()
        agreement = P256.KeyAgreement.PrivateKey()
    }

    public init(serialized: Data) throws {
        guard serialized.count == 64 else { throw DeviceKeyError.malformed }
        do {
            signing = try P256.Signing.PrivateKey(rawRepresentation: serialized.prefix(32))
            agreement = try P256.KeyAgreement.PrivateKey(rawRepresentation: serialized.suffix(32))
        } catch {
            throw DeviceKeyError.malformed
        }
    }

    public var serialized: Data { signing.rawRepresentation + agreement.rawRepresentation }

    public var signingPublicKey: Data { signing.publicKey.derRepresentation }

    public func sign(_ message: Data) throws -> Data {
        try signing.signature(for: message).rawRepresentation
    }

    public func seal(_ plaintext: Data) throws -> Data {
        try ECIES.seal(plaintext, to: agreement.publicKey)
    }

    public func open(_ sealed: Data) throws -> Data {
        try ECIES.open(sealed) { try agreement.sharedSecretFromKeyAgreement(with: $0) }
    }
}

#if canImport(CryptoKit) && os(iOS)
/// Schlüssel in der Secure Enclave. Gespeichert wird nur deren `dataRepresentation`: ein Verweis, der
/// ausschließlich auf diesem Gerät nutzbar ist.
public struct SecureEnclaveDeviceKeys: DeviceKeys {
    private let signing: SecureEnclave.P256.Signing.PrivateKey
    private let agreement: SecureEnclave.P256.KeyAgreement.PrivateKey

    public static var isAvailable: Bool { SecureEnclave.isAvailable }

    public init() throws {
        signing = try SecureEnclave.P256.Signing.PrivateKey()
        agreement = try SecureEnclave.P256.KeyAgreement.PrivateKey()
    }

    public init(serialized: Data) throws {
        let parts = try JSONDecoder().decode([Data].self, from: serialized)
        guard parts.count == 2 else { throw DeviceKeyError.malformed }
        signing = try SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: parts[0])
        agreement = try SecureEnclave.P256.KeyAgreement.PrivateKey(dataRepresentation: parts[1])
    }

    public var serialized: Data {
        (try? JSONEncoder().encode([signing.dataRepresentation, agreement.dataRepresentation])) ?? Data()
    }

    public var signingPublicKey: Data { signing.publicKey.derRepresentation }

    public func sign(_ message: Data) throws -> Data {
        try signing.signature(for: message).rawRepresentation
    }

    public func seal(_ plaintext: Data) throws -> Data {
        try ECIES.seal(plaintext, to: agreement.publicKey)
    }

    public func open(_ sealed: Data) throws -> Data {
        try ECIES.open(sealed) { try agreement.sharedSecretFromKeyAgreement(with: $0) }
    }
}
#endif
