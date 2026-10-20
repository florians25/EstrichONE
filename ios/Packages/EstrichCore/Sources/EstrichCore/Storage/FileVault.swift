import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif

/// Fotos und Dokumente eines Benutzers, je Datei mit AES-GCM verschlüsselt (11, Abschnitt 4.3).
/// Die Fotomediathek des Systems wird nie verwendet.
public struct FileVault: Sendable {
    private let directory: URL
    private let key: SymmetricKey

    public init(directory: URL, key: SymmetricKey) {
        self.directory = directory
        self.key = key
    }

    /// Speichert den Inhalt sofort dauerhaft und liefert seine SHA-256-Prüfsumme (hex).
    @discardableResult
    public func store(_ data: Data, for fileId: UUID) throws -> String {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let sealed = try UserVault.encrypt(data, with: key)
        #if os(iOS)
        try sealed.write(to: url(fileId), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        #else
        try sealed.write(to: url(fileId), options: .atomic)
        #endif
        return Self.sha256(data)
    }

    public func read(_ fileId: UUID) throws -> Data {
        try UserVault.decrypt(Data(contentsOf: url(fileId)), with: key)
    }

    public func contains(_ fileId: UUID) -> Bool {
        FileManager.default.fileExists(atPath: url(fileId).path())
    }

    public func remove(_ fileId: UUID) {
        try? FileManager.default.removeItem(at: url(fileId))
    }

    public static func sha256(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    private func url(_ fileId: UUID) -> URL {
        directory.appending(path: "\(fileId.uuidString.lowercased()).sealed")
    }
}
