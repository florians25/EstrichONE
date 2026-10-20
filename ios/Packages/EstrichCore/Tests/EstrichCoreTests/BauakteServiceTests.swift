import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Bauakten und Fotos", .serialized)
struct BauakteServiceTests {
    private let userId = UUID.v7()

    private func makeService(server: SimulatedSyncServer, directory: URL = FileManager.default.temporaryDirectory.appending(path: "bauakten-\(UUID().uuidString)"),
                             key: SymmetricKey = SymmetricKey(size: .bits256)) throws -> (BauakteService, SyncEngine, URL, SymmetricKey) {
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        return (BauakteService(store: store, files: files, userId: userId), SyncEngine(store: store, transport: server, files: files), directory, key)
    }

    @Test("Schnellanlage: nur die Bezeichnung ist Pflicht, Nummer folgt vom Server")
    func quickCreate() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)

        await #expect(throws: BauakteError.titleRequired) { try await service.create(NewBauakte(title: "   ")) }
        let id = try await service.create(NewBauakte(title: "  EFH Müller, Neubau ", street: "Musterstr. 12", city: "Landshut", template: .newBuild))

        let local = try #require(try await service.bauakte(id))
        #expect(local.title == "EFH Müller, Neubau")
        #expect(local.number == nil)
        #expect(local.addressLine == "Musterstr. 12, Landshut")
        #expect(local.syncState == .pending)
        #expect(try await service.recentBauakten().map(\.id) == [id])

        _ = await engine.synchronize()
        let synced = try #require(try await service.bauakte(id))
        #expect(synced.number == "2026-001")
        #expect(server.entity(id)?["templateKey"]?.string == "neubau")
        #expect(server.entity(id)?["postalCode"] == .null)
    }

    @Test("Doppelanlage: gleiche Adresse in anderer Schreibweise oder ähnlicher Name in der Nähe")
    func duplicates() async throws {
        let (service, _, _, _) = try makeService(server: SimulatedSyncServer())
        let existing = try await service.create(NewBauakte(title: "EFH Müller", street: "Musterstraße 12", city: "Landshut",
                                                           latitude: 48.5370, longitude: 12.1520))
        _ = try await service.create(NewBauakte(title: "Halle Huber", street: "Gewerbering 3", city: "Ergolding"))

        let sameAddress = try await service.similar(to: .init(title: "Neubau", street: "Muster-Str. 12", city: "landshut", latitude: nil, longitude: nil))
        #expect(sameAddress.map(\.id) == [existing])
        let nearby = try await service.similar(to: .init(title: "efh mueller", street: nil, city: nil, latitude: 48.5372, longitude: 12.1521))
        #expect(nearby.isEmpty, "ue ≠ ü: kein Treffer über den Namen")
        let nearbySame = try await service.similar(to: .init(title: "EFH Müller", street: nil, city: nil, latitude: 48.5372, longitude: 12.1521))
        #expect(nearbySame.map(\.id) == [existing])
        let farAway = try await service.similar(to: .init(title: "EFH Müller", street: nil, city: nil, latitude: 48.60, longitude: 12.20))
        #expect(farAway.isEmpty)
        let otherNumber = try await service.similar(to: .init(title: "x", street: "Musterstraße 14", city: "Landshut", latitude: nil, longitude: nil))
        #expect(otherNumber.isEmpty)
    }

    @Test("Foto: zuerst verschlüsselt gespeichert, dann angekündigt und hochgeladen")
    func photo() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Halle"))
        let jpeg = Data([0xFF, 0xD8, 0xFF, 0xE0] + Array(repeating: 7, count: 5000))
        let captured = Date(timeIntervalSince1970: 1_800_000_000)

        let fileId = try await service.addPhoto(jpeg, to: bauakte, capturedAt: captured, latitude: 48.5, longitude: 12.1)

        let photos = try await service.files(of: bauakte)
        #expect(photos.map(\.id) == [fileId])
        #expect(photos[0].syncState == .pending)
        #expect(photos[0].originalName.hasPrefix("Foto_"))
        #expect(service.localContent(of: fileId) == jpeg)

        #expect(await engine.synchronize() == .completed)
        #expect(server.uploaded(fileId) == jpeg)
        #expect(server.entity(fileId)?["capturedAt"]?.string.flatMap(APIDate.parse) == captured)
        #expect(try await service.files(of: bauakte).first?.syncState == .synced)
    }

    @Test("09, AK-2: im Flugmodus angelegt, 20 Fotos, App während des Uploads beendet – alles genau einmal auf dem Server")
    func airplaneModeTwentyPhotos() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, directory, key) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Flugmodus"))
        var photos: [UUID: Data] = [:]
        for index in 0..<20 {
            let jpeg = Data([0xFF, 0xD8] + (0..<2000).map { UInt8(($0 + index) % 256) })
            photos[try await service.addPhoto(jpeg, to: bauakte, capturedAt: Date())] = jpeg
        }

        // Erster Versuch bricht mitten im Upload ab (App beendet / Funkloch).
        server.failUploadAfterBytes = 700
        #expect(await engine.synchronize() == .offline)

        // Neustart der App: neue Objekte auf denselben Dateien.
        let (restarted, restartedEngine, _, _) = try makeService(server: server, directory: directory, key: key)
        #expect(await restartedEngine.synchronize() == .completed)

        #expect(server.entity(bauakte) != nil)
        for (id, data) in photos {
            #expect(server.uploaded(id) == data)
        }
        #expect(Set(server.receivedOpIds).count == 21)
        #expect(try await restarted.store.summary().isSettled)
    }
}
