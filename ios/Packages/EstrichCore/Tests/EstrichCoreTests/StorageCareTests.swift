import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Speicherschonend speichern", .serialized)
struct StorageCareTests {
    private func temporaryDirectory() -> URL {
        FileManager.default.temporaryDirectory.appending(path: "storage-\(UUID().uuidString)", directoryHint: .isDirectory)
    }

    // MARK: Komprimierungsparameter

    @Test("Fotos: höchstens 3000 px, JPEG; kleine JPEGs bleiben unverändert, alles andere wird neu kodiert")
    func photoLimits() {
        #expect(MediaLimits.photoMaxEdge == 3000)
        #expect(MediaLimits.photoQuality >= 0.7 && MediaLimits.photoQuality <= 0.85)
        // Kamerafoto 12 MP (4032 px, rund 3,5 MB): verkleinern.
        #expect(MediaLimits.photoNeedsReencoding(isJPEG: true, longEdge: 4032, bytes: 3_500_000))
        #expect(MediaLimits.photoTargetEdge(longEdge: 4032) == 3000)
        // Bereits kleines JPEG: unverändert.
        #expect(!MediaLimits.photoNeedsReencoding(isJPEG: true, longEdge: 1600, bytes: 400_000))
        // Großes JPEG innerhalb der Kante: neu kodieren (Qualität), aber nie vergrößern.
        #expect(MediaLimits.photoNeedsReencoding(isJPEG: true, longEdge: 2000, bytes: 2_500_000))
        #expect(MediaLimits.photoTargetEdge(longEdge: 2000) == 2000)
        // HEIC aus der Mediathek: immer nach JPEG (Web, Berichte, PDF).
        #expect(MediaLimits.photoNeedsReencoding(isJPEG: false, longEdge: 1200, bytes: 200_000))
        #expect(MediaLimits.photoTargetEdge(longEdge: 0) == 3000)
    }

    private func target(_ width: Double, _ height: Double) -> String {
        let size = MediaLimits.videoTargetSize(width: width, height: height)
        return "\(size.width)×\(size.height)"
    }

    @Test("Videos: 720p, gerade Pixelzahlen, Seitenverhältnis bleibt, nie vergrößert; rund 15 MB je Minute")
    func videoLimits() {
        #expect(target(1920, 1080) == "1280×720")
        #expect(target(1080, 1920) == "720×1280")
        #expect(target(3840, 2160) == "1280×720")
        #expect(target(640, 480) == "640×480")
        #expect(target(1441, 1081) == "960×720")
        #expect(target(0, 0) == "1280×720")
        #expect(MediaLimits.videoBytesPerMinute == 15_480_000)
        #expect(MediaLimits.videoMaxDuration == 120)
    }

    // MARK: Aufbewahrung nach dem Upload

    @Test("Hochgeladene Kopien: nach 2 Tagen bzw. über 150 MB weg, älteste zuerst")
    func retention() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        let policy = LocalCopyRetention(maxAge: 2 * 86_400, maxBytes: 100)
        let fresh = UploadedCopy(fileId: UUID(), size: 40, completedAt: now.addingTimeInterval(-60))
        let yesterday = UploadedCopy(fileId: UUID(), size: 40, completedAt: now.addingTimeInterval(-86_400))
        let older = UploadedCopy(fileId: UUID(), size: 40, completedAt: now.addingTimeInterval(-90_000))
        let stale = UploadedCopy(fileId: UUID(), size: 1, completedAt: now.addingTimeInterval(-3 * 86_400))
        let expired = policy.expired([older, stale, fresh, yesterday], now: now)
        #expect(Set(expired) == [older.fileId, stale.fileId])
        #expect(LocalCopyRetention.standard.maxAge == 2 * 86_400)
        #expect(LocalCopyRetention.standard.maxBytes == 150 * 1024 * 1024)
    }

    @Test("Sync: nach dem Upload bleibt die Kopie zunächst, nach Ablauf der Frist wird sie entfernt; Wartendes nie")
    func syncRemovesOnlyUploadedCopies() async throws {
        let directory = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let key = SymmetricKey(size: .bits256)
        let clock = TestClock()
        let server = SimulatedSyncServer()
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema),
                               now: { clock.now })
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        let engine = SyncEngine(store: store, transport: server, files: files, now: { clock.now })

        let bauakte = UUID.v7()
        let fields: [String: JSONValue] = ["title": "Halle"]
        try await store.create(.bauakte, id: bauakte, data: JSONValue.object(fields).merging(["id": JSONValue(bauakte), "number": nil]), payload: fields)
        let uploaded = try await announce(Data("Foto 1".utf8), bauakte: bauakte, store: store, files: files)
        #expect(await engine.synchronize() == .completed)
        #expect(files.contains(uploaded))
        #expect(try await store.pendingUploadBytes() == 0)

        // Offline aufgenommen: bleibt, egal wie alt.
        server.inMaintenance = true
        let waiting = try await announce(Data("Foto 2, noch nicht hochgeladen".utf8), bauakte: bauakte, store: store, files: files)
        #expect(try await store.pendingUploadBytes() == Int64("Foto 2, noch nicht hochgeladen".utf8.count))

        clock.now = clock.now.addingTimeInterval(3 * 86_400)
        server.inMaintenance = false
        _ = await engine.synchronize()
        #expect(!files.contains(uploaded))
        #expect(server.uploaded(waiting) != nil)
        #expect(files.contains(waiting), "frisch hochgeladen: Kopie bleibt noch")

        // „Cache leeren“: auch die frische, bereits hochgeladene Kopie geht, Unhochgeladenes nie.
        server.inMaintenance = true
        let offline = try await announce(Data("Foto 3".utf8), bauakte: bauakte, store: store, files: files)
        let freed = try await engine.releaseUploadedCopies()
        #expect(freed == Int64("Foto 2, noch nicht hochgeladen".utf8.count))
        #expect(!files.contains(waiting))
        #expect(files.contains(offline))
        #expect(try await store.uploads().map(\.fileId) == [offline])
    }

    private func announce(_ data: Data, bauakte: UUID, store: LocalStore, files: FileVault) async throws -> UUID {
        let id = UUID.v7()
        let sha = try files.store(data, for: id)
        let fields: [String: JSONValue] = ["bauakteId": JSONValue(bauakte), "kind": "photo", "originalName": "a.jpg",
                                           "contentType": "image/jpeg", "sizeBytes": .number(Double(data.count)), "sha256": .string(sha)]
        try await store.create(.file, id: id, data: JSONValue.object(fields).merging(["id": JSONValue(id)]), payload: fields, operation: "file.announce")
        try await store.enqueueUpload(fileId: id, size: Int64(data.count), sha256: sha, contentType: "image/jpeg")
        return id
    }

    // MARK: Vorschau-Zwischenspeicher

    @Test("LRU: die am längsten nicht benutzten fallen zuerst, bis das Limit passt")
    func lruVictims() {
        let base = Date(timeIntervalSince1970: 1_000)
        let entries = [
            LRUTrim.Entry(name: "a", size: 30, lastAccess: base.addingTimeInterval(30)),
            LRUTrim.Entry(name: "b", size: 30, lastAccess: base),
            LRUTrim.Entry(name: "c", size: 30, lastAccess: base.addingTimeInterval(60)),
            LRUTrim.Entry(name: "d", size: 30, lastAccess: base.addingTimeInterval(10)),
        ]
        #expect(LRUTrim.victims(entries, limit: 60) == ["b", "d"])
        #expect(LRUTrim.victims(entries, limit: 120).isEmpty)
        #expect(LRUTrim.victims(entries, limit: 0) == ["b", "d", "a", "c"])
    }

    @Test("Vorschauen: verschlüsselt auf dem Datenträger, Lesen zählt als Benutzung, Limit mit LRU")
    func thumbnailCache() throws {
        let directory = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let clock = TestClock()
        let key = SymmetricKey(size: .bits256)
        let payload = Data(repeating: 7, count: 1000)
        // Je Eintrag 1000 Bytes + 28 Bytes AES-GCM: Platz für zwei.
        let cache = ThumbnailDiskCache(directory: directory, key: key, limit: 2100, now: { clock.now })
        cache.store(payload, for: "eins-120")
        clock.now = clock.now.addingTimeInterval(10)
        cache.store(payload, for: "zwei-120")
        clock.now = clock.now.addingTimeInterval(10)
        #expect(cache.read("eins-120") == payload)
        clock.now = clock.now.addingTimeInterval(10)
        cache.store(payload, for: "drei-120")

        // Nur Chiffrat auf dem Datenträger.
        let raw = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
        #expect(raw.count == 3)
        for file in raw { #expect(try Data(contentsOf: file) != payload) }

        #expect(cache.trim() == 1028)
        #expect(cache.read("zwei-120") == nil, "am längsten nicht benutzt")
        #expect(cache.read("eins-120") == payload)
        #expect(cache.read("drei-120") == payload)
        #expect(cache.read("../../etwas") == nil)

        #expect(cache.clear() == 2056)
        #expect(cache.size == 0)
        #expect(cache.read("eins-120") == nil)
    }

    // MARK: Temporäre Dateien und Anzeige

    @Test("Temporäre Dateien: nur alte Einträge, auch Ordner; neue bleiben")
    func temporaryFiles() throws {
        let directory = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        try FileManager.default.createDirectory(at: directory.appending(path: "Vorschau/x", directoryHint: .isDirectory), withIntermediateDirectories: true)
        try Data(count: 500).write(to: directory.appending(path: "Vorschau/x/a.pdf"))
        try Data(count: 300).write(to: directory.appending(path: "Video-alt.mp4"))
        try Data(count: 200).write(to: directory.appending(path: "Aufnahme-neu.mov"))
        let old = Date(timeIntervalSinceNow: -2 * 86_400)
        for name in ["Vorschau", "Video-alt.mp4"] {
            try FileManager.default.setAttributes([.modificationDate: old], ofItemAtPath: directory.appending(path: name).path())
        }

        #expect(DirectorySize.bytes(at: directory) == 1000)
        #expect(TemporaryFiles.clean(directory, olderThan: Date(timeIntervalSinceNow: -86_400)) == 800)
        let rest = try FileManager.default.contentsOfDirectory(atPath: directory.path())
        #expect(rest == ["Aufnahme-neu.mov"])
    }

    @Test("Speicheranzeige: Freigebbares nie größer als der Rest ohne Unhochgeladenes")
    func usage() {
        let usage = StorageUsage(total: 100, pendingUpload: 70, releasable: 50)
        #expect(usage.releasable == 30)
        #expect(StorageUsage(total: 100, pendingUpload: 0, releasable: 20).releasable == 20)
    }
}

/// Verstellbare Uhr für Tests.
private final class TestClock: @unchecked Sendable {
    private let lock = NSLock()
    private var value = Date(timeIntervalSince1970: 1_800_000_000)

    var now: Date {
        get { lock.withLock { value } }
        set { lock.withLock { value = newValue } }
    }
}
