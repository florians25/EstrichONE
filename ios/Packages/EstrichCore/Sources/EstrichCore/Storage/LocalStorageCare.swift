import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif

/// Grenzen für Fotos und Videos aus den Apps (docs/speicher.md, Abschnitt „Apps“). Fotos bleiben JPEG und Videos H.264,
/// weil der Server HEIC nicht zuverlässig in Vorschaustufen wandelt und Browser HEVC nicht überall abspielen – die
/// Web-Anzeige darf nie kaputtgehen. Gespart wird über Kantenlänge, Qualität und Bitrate.
public enum MediaLimits {
    /// Längste Kante von Fotos auf dem Gerät (vorher 4096; reicht für A4 mit gut 200 dpi und für Mängelfotos).
    public static let photoMaxEdge = 3000
    /// JPEG-Qualität beim Verkleinern bzw. Neukodieren.
    public static let photoQuality = 0.78
    /// JPEGs bis zu dieser Größe und Kantenlänge bleiben unverändert (kein zweites Komprimieren kleiner Bilder).
    public static let keepJPEGBelowBytes = 900_000

    /// Videos: höchstens 720p, H.264 mit etwa 2 Mbit/s, Ton AAC mono 64 kbit/s, höchstens 2 Minuten.
    public static let videoMaxLongEdge = 1280
    public static let videoMaxShortEdge = 720
    public static let videoBitrate = 2_000_000
    public static let audioBitrate = 64_000
    public static let videoMaxDuration: Double = 120
    /// Rechnerische Größe je Minute: (Video + Ton) × 60 s / 8 ≈ 15,5 MB.
    public static var videoBytesPerMinute: Int { (videoBitrate + audioBitrate) * 60 / 8 }

    /// Muss ein Foto neu kodiert werden? Nein, wenn es schon ein kleines JPEG innerhalb der Kantenlänge ist.
    public static func photoNeedsReencoding(isJPEG: Bool, longEdge: Int, bytes: Int) -> Bool {
        !(isJPEG && longEdge > 0 && longEdge <= photoMaxEdge && bytes <= keepJPEGBelowBytes)
    }

    /// Zielkante beim Verkleinern: nie vergrößern.
    public static func photoTargetEdge(longEdge: Int) -> Int {
        longEdge > 0 ? min(longEdge, photoMaxEdge) : photoMaxEdge
    }

    /// Zielgröße eines Videos (Breite, Höhe): Seitenverhältnis bleibt, nie vergrößert, gerade Pixelzahlen.
    public static func videoTargetSize(width: Double, height: Double) -> (width: Int, height: Int) {
        let w = abs(width), h = abs(height)
        guard w > 0, h > 0 else { return (videoMaxLongEdge, videoMaxShortEdge) }
        let scale = min(1, Double(videoMaxLongEdge) / max(w, h), Double(videoMaxShortEdge) / min(w, h))
        func even(_ value: Double) -> Int { max(2, Int((value * scale / 2).rounded()) * 2) }
        return (even(w), even(h))
    }
}

/// Wie lange hochgeladene Fotos und Videos zusätzlich auf dem Gerät bleiben: als Absicherung, falls der Server aus
/// einem Backup zurückgeholt wird (der Abgleich liefert sie dann nach). Danach zeigt die App die Vorschau aus dem
/// Vorschau-Zwischenspeicher bzw. lädt vom Server. Noch nicht hochgeladene Dateien sind nie betroffen.
public struct LocalCopyRetention: Sendable, Equatable {
    /// Höchstalter nach dem Upload (vorher 7 Tage).
    public var maxAge: TimeInterval
    /// Höchstens so viel für bereits hochgeladene Kopien; darüber fallen die ältesten zuerst weg.
    public var maxBytes: Int64

    public static let standard = LocalCopyRetention(maxAge: 2 * 24 * 3600, maxBytes: 150 * 1024 * 1024)

    public init(maxAge: TimeInterval, maxBytes: Int64) {
        self.maxAge = maxAge
        self.maxBytes = maxBytes
    }

    /// Kopien, die jetzt weg dürfen: zu alt, oder über dem Platzbudget (älteste zuerst).
    public func expired(_ copies: [UploadedCopy], now: Date) -> [UUID] {
        var result: [UUID] = []
        var kept: Int64 = 0
        // Neueste zuerst behalten, bis das Budget erschöpft ist.
        for copy in copies.sorted(by: { $0.completedAt > $1.completedAt }) {
            if now.timeIntervalSince(copy.completedAt) > maxAge || kept + copy.size > maxBytes {
                result.append(copy.fileId)
            } else {
                kept += copy.size
            }
        }
        return result
    }
}

/// Bereits hochgeladene Datei, deren Kopie noch auf dem Gerät liegt.
public struct UploadedCopy: Sendable, Equatable {
    public let fileId: UUID
    public let size: Int64
    public let completedAt: Date

    public init(fileId: UUID, size: Int64, completedAt: Date) {
        self.fileId = fileId
        self.size = size
        self.completedAt = completedAt
    }
}

/// Least recently used: Welche Einträge fallen weg, damit höchstens `limit` Bytes bleiben?
public enum LRUTrim {
    public struct Entry: Sendable, Equatable {
        public let name: String
        public let size: Int64
        public let lastAccess: Date

        public init(name: String, size: Int64, lastAccess: Date) {
            self.name = name
            self.size = size
            self.lastAccess = lastAccess
        }
    }

    /// Die am längsten nicht benutzten zuerst, bis der Rest ins Limit passt.
    public static func victims(_ entries: [Entry], limit: Int64) -> [String] {
        var total = entries.reduce(Int64(0)) { $0 + $1.size }
        var result: [String] = []
        for entry in entries.sorted(by: { ($0.lastAccess, $0.name) < ($1.lastAccess, $1.name) }) where total > limit {
            result.append(entry.name)
            total -= entry.size
        }
        return result
    }
}

/// Vorschaubilder auf dem Datenträger – verschlüsselt wie die Fotos (11, Abschnitt 4.3), mit Größenlimit und LRU.
/// So bleiben Raster und Listen auch offline gefüllt, nachdem die Originale nach dem Upload vom Gerät entfernt wurden.
public struct ThumbnailDiskCache: Sendable {
    public static let defaultLimit: Int64 = 40 * 1024 * 1024

    private let directory: URL
    private let key: SymmetricKey
    public let limit: Int64
    private let now: @Sendable () -> Date

    public init(directory: URL, key: SymmetricKey, limit: Int64 = defaultLimit, now: @escaping @Sendable () -> Date = Date.init) {
        self.directory = directory
        self.key = key
        self.limit = limit
        self.now = now
    }

    public func store(_ data: Data, for name: String) {
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let sealed = try UserVault.encrypt(data, with: key)
            #if os(iOS)
            try sealed.write(to: url(name), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
            #else
            try sealed.write(to: url(name), options: .atomic)
            #endif
            touch(name)
        } catch {
            // Nur ein Zwischenspeicher: ohne Platz wird die Vorschau beim nächsten Mal neu erzeugt.
        }
    }

    /// Inhalt (oder `nil`); ein Treffer zählt als Benutzung (LRU).
    public func read(_ name: String) -> Data? {
        guard let sealed = try? Data(contentsOf: url(name)), let data = try? UserVault.decrypt(sealed, with: key) else { return nil }
        touch(name)
        return data
    }

    /// Auf das Limit kürzen; liefert die freigegebenen Bytes.
    @discardableResult
    public func trim() -> Int64 {
        let entries = self.entries()
        let victims = Set(LRUTrim.victims(entries, limit: limit))
        var freed: Int64 = 0
        for entry in entries where victims.contains(entry.name) {
            if (try? FileManager.default.removeItem(at: directory.appending(path: entry.name))) != nil { freed += entry.size }
        }
        return freed
    }

    /// Alles löschen (Mehr › Gerät, Server und App › „Cache leeren“).
    @discardableResult
    public func clear() -> Int64 {
        let size = DirectorySize.bytes(at: directory)
        try? FileManager.default.removeItem(at: directory)
        return size
    }

    public var size: Int64 { DirectorySize.bytes(at: directory) }

    private func entries() -> [LRUTrim.Entry] {
        let keys: [URLResourceKey] = [.fileSizeKey, .contentModificationDateKey]
        let files = (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: keys)) ?? []
        return files.compactMap { file in
            guard let values = try? file.resourceValues(forKeys: Set(keys)) else { return nil }
            return LRUTrim.Entry(name: file.lastPathComponent, size: Int64(values.fileSize ?? 0),
                                 lastAccess: values.contentModificationDate ?? .distantPast)
        }
    }

    private func touch(_ name: String) {
        try? FileManager.default.setAttributes([.modificationDate: now()], ofItemAtPath: url(name).path())
    }

    private func url(_ name: String) -> URL {
        let safe = name.filter { $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" }
        return directory.appending(path: "\(safe).thumb")
    }
}

/// Temporäre Dateien (Videoaufnahmen, Kopien aus der Mediathek, Dokumentvorschauen) aufräumen. Gespeicherte Fotos und
/// Videos liegen nie hier, sondern verschlüsselt in der Ablage – hier wird also nie etwas Unhochgeladenes gelöscht.
public enum TemporaryFiles {
    /// Entfernt Einträge des Ordners, die zuletzt vor `cutoff` geändert wurden; liefert die freigegebenen Bytes.
    @discardableResult
    public static func clean(_ directory: URL, olderThan cutoff: Date) -> Int64 {
        let keys: [URLResourceKey] = [.contentModificationDateKey, .isDirectoryKey]
        let items = (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: keys)) ?? []
        var freed: Int64 = 0
        for item in items {
            guard let values = try? item.resourceValues(forKeys: Set(keys)),
                  let modified = values.contentModificationDate, modified < cutoff else { continue }
            let size = values.isDirectory == true ? DirectorySize.bytes(at: item) : DirectorySize.bytes(of: item)
            if (try? FileManager.default.removeItem(at: item)) != nil { freed += size }
        }
        return freed
    }
}

/// Belegter Platz eines Ordners (rekursiv) bzw. einer Datei.
public enum DirectorySize {
    public static func bytes(at directory: URL) -> Int64 {
        guard let enumerator = FileManager.default.enumerator(at: directory, includingPropertiesForKeys: [.fileSizeKey, .isRegularFileKey]) else {
            return 0
        }
        var total: Int64 = 0
        for case let file as URL in enumerator {
            guard let values = try? file.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey]), values.isRegularFile == true else { continue }
            total += Int64(values.fileSize ?? 0)
        }
        return total
    }

    public static func bytes(of file: URL) -> Int64 {
        Int64((try? file.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0)
    }
}

/// Speicheranzeige unter Mehr › Gerät, Server und App.
public struct StorageUsage: Sendable, Equatable {
    /// Alles, was EstrichONE auf dem Gerät belegt (Daten, Fotos, Vorschauen, temporäre Dateien).
    public let total: Int64
    /// Davon noch nicht hochgeladen (nie per „Cache leeren“ gelöscht).
    public let pendingUpload: Int64
    /// Davon freigebbar: hochgeladene Kopien, Vorschauen, temporäre Dateien.
    public let releasable: Int64

    public init(total: Int64, pendingUpload: Int64, releasable: Int64) {
        self.total = total
        self.pendingUpload = pendingUpload
        self.releasable = min(releasable, max(0, total - pendingUpload))
    }
}
