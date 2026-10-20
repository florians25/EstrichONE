import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Kryptografie")
struct SecurityTests {
    private func hex(_ data: Data) -> String { data.map { String(format: "%02x", $0) }.joined() }

    @Test("PBKDF2-HMAC-SHA256 entspricht den Referenzwerten (gegengeprüft mit Python hashlib)")
    func pbkdf2Vectors() {
        let password = Data("password".utf8)
        let salt = Data("salt".utf8)
        #expect(hex(PBKDF2.sha256(password: password, salt: salt, iterations: 1)) == "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b")
        #expect(hex(PBKDF2.sha256(password: password, salt: salt, iterations: 2)) == "ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43")
        #expect(hex(PBKDF2.sha256(password: password, salt: salt, iterations: 4096)) == "c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a")
        let long = PBKDF2.sha256(password: Data("passwordPASSWORDpassword".utf8), salt: Data("saltSALTsaltSALTsaltSALTsaltSALTsalt".utf8),
                                 iterations: 4096, keyLength: 40)
        #expect(hex(long) == "348c89dbcbd32b2f32d814b8116e84cf2b17347ebc1800181c4e2a1fb8dd53e1c635518c7dac47e9")
    }

    @Test("Gerätesignatur ist mit dem öffentlichen Schlüssel (SPKI) prüfbar")
    func signature() throws {
        let keys = SoftwareDeviceKeys()
        let message = Data("EstrichONE refresh v1\neor1_abc".utf8)
        let signature = try keys.sign(message)
        let publicKey = try P256.Signing.PublicKey(derRepresentation: keys.signingPublicKey)

        #expect(signature.count == 64)
        #expect(publicKey.isValidSignature(try P256.Signing.ECDSASignature(rawRepresentation: signature), for: message))
        #expect(!publicKey.isValidSignature(try P256.Signing.ECDSASignature(rawRepresentation: signature), for: Data("anders".utf8)))
    }

    @Test("Versiegelte Daten öffnet nur dasselbe Gerät")
    func sealing() throws {
        let keys = SoftwareDeviceKeys()
        let sealed = try keys.seal(Data("geheim".utf8))

        #expect(try keys.open(sealed) == Data("geheim".utf8))
        #expect(throws: DeviceKeyError.cannotOpen) { try SoftwareDeviceKeys().open(sealed) }
        #expect(try SoftwareDeviceKeys(serialized: keys.serialized).open(sealed) == Data("geheim".utf8))
    }

    @Test("PIN-Regeln", arguments: [
        ("482913", nil), ("12345", PINPolicy.Violation.wrongLength), ("12a456", .notDigits), ("123456", .trivial),
        ("654321", .trivial), ("111111", .trivial), ("121212", .trivial), ("123123", .trivial), ("890123", .trivial),
    ] as [(String, PINPolicy.Violation?)])
    func pinPolicy(pin: String, violation: PINPolicy.Violation?) {
        if let violation {
            #expect(throws: violation) { try PINPolicy.validate(pin) }
        } else {
            #expect(throws: Never.self) { try PINPolicy.validate(pin) }
        }
    }

    @Test("Tresor mit PIN: richtige PIN öffnet, falsche nicht, fremdes Gerät nie")
    func vaultWithPIN() throws {
        let keys = SoftwareDeviceKeys()
        let (envelope, dataKey) = try UserVault.create(pin: "482913", keys: keys, iterations: 1_000)
        let secret = try UserVault.encrypt(Data("Sitzung".utf8), with: dataKey)

        let unlocked = try UserVault.unlock(envelope, pin: "482913", keys: keys)
        #expect(try UserVault.decrypt(secret, with: unlocked) == Data("Sitzung".utf8))
        #expect(throws: VaultError.wrongPIN) { try UserVault.unlock(envelope, pin: "482914", keys: keys) }
        #expect(throws: VaultError.pinRequired) { try UserVault.unlock(envelope, pin: nil, keys: keys) }
        #expect(throws: VaultError.unreadable) { try UserVault.unlock(envelope, pin: "482913", keys: SoftwareDeviceKeys()) }
    }

    @Test("Der gespeicherte Tresor enthält den Datenschlüssel nicht im Klartext")
    func vaultCiphertext() throws {
        let keys = SoftwareDeviceKeys()
        let (envelope, dataKey) = try UserVault.create(pin: "482913", keys: keys, iterations: 1_000)
        let raw = dataKey.withUnsafeBytes { Data($0) }
        let stored = try JSONCoding.encoder.encode(envelope)

        #expect(stored.range(of: raw) == nil)
        #expect(stored.range(of: Data(raw.base64EncodedString().utf8)) == nil)
    }

    @Test("Tresor ohne PIN (persönliches Gerät)")
    func vaultWithoutPIN() throws {
        let keys = SoftwareDeviceKeys()
        let (envelope, dataKey) = try UserVault.create(pin: nil, keys: keys)
        let unlocked = try UserVault.unlock(envelope, pin: nil, keys: keys)

        #expect(unlocked.withUnsafeBytes { Data($0) } == dataKey.withUnsafeBytes { Data($0) })
        #expect(envelope.pinProtected == false)
    }
}
