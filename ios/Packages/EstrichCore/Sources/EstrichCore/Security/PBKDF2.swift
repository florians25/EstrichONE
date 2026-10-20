import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif

/// PBKDF2-HMAC-SHA256 (RFC 8018) für die PIN-Ableitung (ADR 0012).
public enum PBKDF2 {
    public static func sha256(password: Data, salt: Data, iterations: Int, keyLength: Int = 32) -> Data {
        precondition(iterations > 0 && keyLength > 0)
        let key = SymmetricKey(data: password)
        // Der vorbereitete HMAC-Zustand wird kopiert, statt den Schlüssel in jeder Runde neu zu verarbeiten.
        let prepared = HMAC<SHA256>(key: key)
        var output = Data()
        var block: UInt32 = 1
        while output.count < keyLength {
            var first = prepared
            first.update(data: salt)
            first.update(data: withUnsafeBytes(of: block.bigEndian) { Data($0) })
            var u = [UInt8](first.finalize())
            var t = u
            if iterations > 1 {
                for _ in 1..<iterations {
                    var next = prepared
                    next.update(data: u)
                    u = [UInt8](next.finalize())
                    for index in t.indices {
                        t[index] ^= u[index]
                    }
                }
            }
            output.append(contentsOf: t)
            block += 1
        }
        return output.prefix(keyLength)
    }
}
