import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

/// Ein Benutzer mit Datenbank, Dateiablage und Synchronisation gegen den simulierten Server.
private struct Device {
    let directory: URL
    let key: SymmetricKey
    let store: LocalStore
    let files: FileVault
    let engine: SyncEngine

    init(server: SimulatedSyncServer, directory: URL = FileManager.default.temporaryDirectory.appending(path: "sync-\(UUID().uuidString)"),
         key: SymmetricKey = SymmetricKey(size: .bits256)) throws {
        self.directory = directory
        self.key = key
        store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        files = FileVault(directory: directory.appending(path: "files"), key: key)
        engine = SyncEngine(store: store, transport: server, files: files)
    }

    /// Wie nach einem App-Neustart: gleiche Dateien, neue Objekte.
    func restarted(server: SimulatedSyncServer) throws -> Device {
        try Device(server: server, directory: directory, key: key)
    }

    func createBauakte(_ title: String, city: String? = nil) async throws -> UUID {
        let id = UUID.v7()
        let fields: [String: JSONValue] = ["title": .string(title), "city": JSONValue(city)]
        try await store.create(.bauakte, id: id, data: JSONValue.object(fields).merging(["id": JSONValue(id), "number": nil]), payload: fields)
        return id
    }
}

@Suite("Synchronisation", .serialized)
struct SyncTests {
    @Test("Offline angelegt: sofort lokal sichtbar, nach dem Push mit Bauaktennummer vom Server")
    func offlineCreate() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let id = try await device.createBauakte("EFH Müller, Neubau", city: "Landshut")

        let before = try #require(try await device.store.entity(.bauakte, id))
        #expect(before.syncState == .pending)
        #expect(try await device.store.summary().pendingOperations == 1)

        #expect(await device.engine.synchronize() == .completed)

        let after = try #require(try await device.store.entity(.bauakte, id))
        #expect(after.syncState == .synced)
        #expect(after.data["number"]?.string == "2026-001")
        #expect(try await device.store.summary().isSettled)
    }

    @Test("Änderungen vor dem Senden: Anlage wird ergänzt, Folgeänderungen behalten den ältesten base-Wert")
    func coalescing() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let id = try await device.createBauakte("Halle", city: "Ergolding")
        try await device.store.update(.bauakte, id: id, changes: ["city": "Landshut"])
        let operations = try await device.store.operations()
        #expect(operations.count == 1)
        #expect(operations[0].payload["city"]?.string == "Landshut")

        _ = await device.engine.synchronize()
        try await device.store.update(.bauakte, id: id, changes: ["city": "Altdorf"])
        try await device.store.update(.bauakte, id: id, changes: ["city": "Essenbach", "title": "Halle 2"])

        let update = try #require(try await device.store.operations().first)
        #expect(update.type == "bauakte.update")
        #expect(update.payload["changes"]?["city"]?.string == "Essenbach")
        #expect(update.payload["base"]?["city"]?.string == "Landshut")
        #expect(update.payload["base"]?["title"]?.string == "Halle")
        #expect(try await device.store.entity(.bauakte, id)?.data["city"]?.string == "Essenbach")
    }

    @Test("Paralleles Ändern: anderes Feld wird übernommen, gleiches Feld wird Konflikt, Serverwert bleibt")
    func conflict() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let id = try await device.createBauakte("Halle", city: "Landshut")
        _ = await device.engine.synchronize()

        // Büro ändert die Stadt, Baustelle ändert Stadt und Beschreibung.
        let current = try #require(server.entity(id))
        server.serverSideChange("bauakte", id: id, data: current.merging(["city": "Ergolding", "version": 2]))
        try await device.store.update(.bauakte, id: id, changes: ["city": "Altdorf", "description": "Zufahrt über Hof"])

        #expect(await device.engine.synchronize() == .completed)

        let entity = try #require(try await device.store.entity(.bauakte, id))
        #expect(entity.data["city"]?.string == "Ergolding")
        #expect(entity.data["description"]?.string == "Zufahrt über Hof")
        let conflicts = try await device.store.conflicts()
        #expect(conflicts.map(\.conflict.field) == ["city"])
        #expect(conflicts.first?.conflict.clientValue?.string == "Altdorf")
    }

    @Test("Verbindungsabbruch vor dem Empfang: alles bleibt, später wird mit derselben opId gesendet")
    func failureBeforeReceipt() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let id = try await device.createBauakte("Halle")
        let opId = try #require(try await device.store.operations().first?.opId)
        server.nextPushFailure = .beforeReceipt

        #expect(await device.engine.synchronize() == .offline)
        #expect(try await device.store.operations().first?.state == .pending)

        #expect(await device.engine.synchronize() == .completed)
        #expect(server.receivedOpIds == [opId])
        #expect(server.entity(id) != nil)
    }

    @Test("Antwort geht nach der Annahme verloren: Wiederholung ergibt „duplicate“, nichts doppelt")
    func failureAfterAcceptance() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let id = try await device.createBauakte("Halle")
        server.nextPushFailure = .afterAcceptance

        #expect(await device.engine.synchronize() == .offline)
        #expect(await device.engine.synchronize() == .completed)

        #expect(server.receivedOpIds.count == 2)
        #expect(Set(server.receivedOpIds).count == 1)
        #expect(try await device.store.entity(.bauakte, id)?.data["number"]?.string == "2026-001")
        #expect(try await device.store.summary().isSettled)
    }

    @Test("App-Neustart während des Sendens: Einträge werden wieder gesendet")
    func restartWhileSending() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        _ = try await device.createBauakte("Halle")
        _ = try await device.store.takeOperations()
        #expect(try await device.store.operations().first?.state == .sending)

        let restarted = try device.restarted(server: server)
        #expect(await restarted.engine.synchronize() == .completed)
        #expect(try await restarted.store.summary().isSettled)
    }

    @Test("Endgültig abgelehnt: bleibt sichtbar mit Problem, wird nie erneut gesendet; RETRY_LATER wartet")
    func rejection() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let id = try await device.createBauakte("Halle")
        server.rejectNext = ("PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.")

        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.entity(.bauakte, id)?.syncState == .problem)
        let rejected = try #require(try await device.store.operations().first)
        #expect(rejected.state == .rejected)
        #expect(rejected.errorCode == "PERMISSION_DENIED")

        _ = await device.engine.synchronize()
        #expect(server.receivedOpIds.count == 1)

        let other = try await device.createBauakte("Garage")
        server.rejectNext = ("RETRY_LATER", "Später")
        #expect(await device.engine.synchronize() == .retryLater)
        #expect(await device.engine.synchronize() == .completed)
        #expect(server.entity(other) != nil)
    }

    @Test("Pull: Änderungen anderer, Löschungen und ungesendete eigene Änderungen bleiben sichtbar")
    func pull() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let fromOffice = UUID.v7()
        let deletedLater = UUID.v7()
        server.serverSideChange("bauakte", id: fromOffice, data: ["id": JSONValue(fromOffice), "title": "Schule", "city": "Moosburg", "version": 1])
        server.serverSideChange("bauakte", id: deletedLater, data: ["id": JSONValue(deletedLater), "title": "Alt", "version": 1])
        let note = UUID.v7()
        server.serverSideChange("note", id: note, data: ["id": JSONValue(note), "bauakteId": JSONValue(deletedLater), "text": "Hinweis", "version": 1])

        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.entities(.bauakte).count == 2)
        #expect(try await device.store.entities(.note, parent: deletedLater).count == 1)

        try await device.store.update(.bauakte, id: fromOffice, changes: ["description": "Zufahrt Nord"])
        server.serverSideDelete("bauakte", id: deletedLater)
        server.serverSideChange("bauakte", id: fromOffice, data: ["id": JSONValue(fromOffice), "title": "Grundschule", "city": "Moosburg", "version": 2])
        server.nextPushFailure = .beforeReceipt
        _ = await device.engine.synchronize()
        // Push scheitert, Pull findet nicht statt – lokaler Stand unverändert.
        #expect(try await device.store.entity(.bauakte, fromOffice)?.data["title"]?.string == "Schule")

        #expect(await device.engine.synchronize() == .completed)
        let school = try #require(try await device.store.entity(.bauakte, fromOffice))
        #expect(school.data["title"]?.string == "Grundschule")
        #expect(school.data["description"]?.string == "Zufahrt Nord")
        #expect(try await device.store.entity(.bauakte, deletedLater) == nil)
        #expect(try await device.store.entities(.note, parent: deletedLater).isEmpty)
    }

    @Test("Rechte entzogen: nicht mehr sichtbare Bauakten verschwinden samt Notizen")
    func scopeRemoval() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let keep = UUID.v7()
        let revoke = UUID.v7()
        for id in [keep, revoke] {
            server.serverSideChange("bauakte", id: id, data: ["id": JSONValue(id), "title": "Akte", "version": 1])
        }
        let note = UUID.v7()
        server.serverSideChange("note", id: note, data: ["id": JSONValue(note), "bauakteId": JSONValue(revoke), "text": "x", "version": 1])
        _ = await device.engine.synchronize()

        server.visibleBauakten = [keep]
        #expect(await device.engine.synchronize(includeScope: true) == .completed)

        #expect(try await device.store.entities(.bauakte).map(\.id) == [keep])
        #expect(try await device.store.entity(.note, note) == nil)
    }

    @Test("Lokale Suche über Nummer, Bezeichnung und Ort")
    func search() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        _ = try await device.createBauakte("EFH Müller", city: "Landshut")
        _ = try await device.createBauakte("Halle Huber", city: "Ergolding")
        _ = await device.engine.synchronize()

        #expect(try await device.store.entities(.bauakte, search: "landsh").count == 1)
        #expect(try await device.store.entities(.bauakte, search: "2026-002").first?.data["title"]?.string == "Halle Huber")
        #expect(try await device.store.entities(.bauakte, search: "huber ergolding").count == 1)
        #expect(try await device.store.entities(.bauakte, search: "50%").isEmpty)
    }

    @Test("Foto: verschlüsselt gespeichert, angekündigt, in Teilen hochgeladen und nach Abbruch fortgesetzt")
    func photoUpload() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let bauakte = try await device.createBauakte("Halle")
        let photo = Data((0..<(SyncEngine.chunkSize + 1500)).map { UInt8($0 % 251) })
        let fileId = UUID.v7()
        let sha = try device.files.store(photo, for: fileId)
        let fields: [String: JSONValue] = ["bauakteId": JSONValue(bauakte), "kind": "photo", "originalName": "IMG_0001.jpg",
                                           "contentType": "image/jpeg", "sizeBytes": .number(Double(photo.count)), "sha256": .string(sha)]
        try await device.store.create(.file, id: fileId, data: JSONValue.object(fields).merging(["id": JSONValue(fileId)]),
                                      payload: fields, operation: "file.announce")
        try await device.store.enqueueUpload(fileId: fileId, size: Int64(photo.count), sha256: sha, contentType: "image/jpeg")

        server.failUploadAfterBytes = 1_000_000
        #expect(await device.engine.synchronize() == .offline)
        #expect(try await device.store.uploads().first?.uploadedBytes ?? 0 < Int64(photo.count))

        #expect(await device.engine.synchronize() == .completed)
        #expect(server.uploaded(fileId) == photo)
        #expect(try await device.store.uploads().first?.state == .done)
        #expect(try await device.store.entity(.file, fileId)?.syncState == .synced)
    }

    @Test("Server aus Backup wiederhergestellt: Neuabgleich, Outbox bleibt, verlorene Fotos werden nachgeliefert")
    func restoreFromBackup() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let bauakte = try await device.createBauakte("Halle")
        let photo = Data("JPEG-Inhalt".utf8)
        let fileId = UUID.v7()
        let sha = try device.files.store(photo, for: fileId)
        let fields: [String: JSONValue] = ["bauakteId": JSONValue(bauakte), "kind": "photo", "originalName": "a.jpg",
                                           "contentType": "image/jpeg", "sizeBytes": .number(Double(photo.count)), "sha256": .string(sha)]
        try await device.store.create(.file, id: fileId, data: .object(fields), payload: fields, operation: "file.announce")
        try await device.store.enqueueUpload(fileId: fileId, size: Int64(photo.count), sha256: sha, contentType: "image/jpeg")
        #expect(await device.engine.synchronize() == .completed)

        server.restoreFromBackup(losing: [fileId])
        let unsent = try await device.createBauakte("Neu nach Restore")

        #expect(await device.engine.synchronize() == .completed)

        #expect(server.entity(unsent) != nil)
        #expect(server.uploaded(fileId) == photo)
        #expect(try await device.store.entity(.file, fileId) != nil)
        #expect(try await device.store.summary().isSettled)
    }

    @Test("Auf dem Datenträger liegt nur Chiffrat; nach dem Neustart ist alles wieder da (11, AK-3)")
    func encryptedAtRest() async throws {
        let server = SimulatedSyncServer()
        let device = try Device(server: server)
        let id = try await device.createBauakte("Geheimes Bauvorhaben Huber", city: "Landshut")
        let fileId = UUID.v7()
        try device.files.store(Data("Fotoinhalt Huber".utf8), for: fileId)

        let files = FileManager.default.enumerator(at: device.directory, includingPropertiesForKeys: nil)?.compactMap { $0 as? URL } ?? []
        #expect(!files.isEmpty)
        for file in files where !file.hasDirectoryPath {
            let content = try Data(contentsOf: file)
            #expect(content.range(of: Data("Huber".utf8)) == nil, "Klartext in \(file.lastPathComponent)")
            #expect(content.range(of: Data("SQLite format".utf8)) == nil, "Unverschlüsselte Datenbank in \(file.lastPathComponent)")
        }

        let restarted = try device.restarted(server: server)
        #expect(try await restarted.store.entity(.bauakte, id)?.data["title"]?.string == "Geheimes Bauvorhaben Huber")
        #expect(throws: (any Error).self) {
            _ = try EncryptedDatabase(url: device.directory.appending(path: "data.sealed"), key: SymmetricKey(size: .bits256), migrations: LocalStore.schema)
        }
    }

    @Test("UUIDv7: Version, Variante und zeitliche Ordnung")
    func uuidV7() {
        let earlier = UUID.v7(now: Date(timeIntervalSince1970: 1_800_000_000))
        let later = UUID.v7(now: Date(timeIntervalSince1970: 1_800_000_001))
        #expect(earlier.uuidString.lowercased().dropFirst(14).first == "7")
        #expect(["8", "9", "a", "b"].contains(earlier.uuidString.lowercased().dropFirst(19).first.map(String.init) ?? ""))
        #expect(earlier.uuidString < later.uuidString)
    }
}
