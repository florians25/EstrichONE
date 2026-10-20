import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif

/// Datenbank eines Benutzers, verschlüsselt mit dem Datenschlüssel seines Tresors (11, Abschnitt 4.3; ADR 0013).
///
/// Die Datenbank liegt im Arbeitsspeicher. Nach jeder Änderung wird ihr Abbild mit AES-GCM verschlüsselt und
/// atomar geschrieben: Auf dem Datenträger gibt es entweder den alten oder den neuen Stand, nie Klartext.
public final class EncryptedDatabase {
    public let database: SQLiteDatabase
    private let url: URL
    private let key: SymmetricKey

    public init(url: URL, key: SymmetricKey, migrations: [(SQLiteDatabase) throws -> Void]) throws {
        self.url = url
        self.key = key
        let image: Data?
        if FileManager.default.fileExists(atPath: url.path()) {
            image = try UserVault.decrypt(Data(contentsOf: url), with: key)
        } else {
            image = nil
        }
        database = try SQLiteDatabase(image: image)

        let current = database.userVersion
        if current < migrations.count {
            try database.transaction {
                for migration in migrations[current...] {
                    try migration(database)
                }
            }
            database.userVersion = migrations.count
            try persist()
        }
    }

    /// Schreibt den aktuellen Stand verschlüsselt auf den Datenträger.
    public func persist() throws {
        let sealed = try UserVault.encrypt(try database.serialize(), with: key)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        #if os(iOS)
        try sealed.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        #else
        try sealed.write(to: url, options: .atomic)
        #endif
    }

    /// Änderung in einer Transaktion, danach sofort gespeichert. Schlägt das Speichern fehl (z. B. Speicher voll),
    /// wird der Fehler gemeldet; der Stand bleibt im Arbeitsspeicher und wird beim nächsten Schreiben mitgespeichert.
    @discardableResult
    public func write<T>(_ work: (SQLiteDatabase) throws -> T) throws -> T {
        let result = try database.transaction { try work(database) }
        try persist()
        return result
    }
}
