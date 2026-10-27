import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Räume und Flächen", .serialized)
struct RoomTests {
    private let userId = UUID.v7()

    private func makeService(server: SimulatedSyncServer) throws -> (BauakteService, SyncEngine) {
        let directory = FileManager.default.temporaryDirectory.appending(path: "rooms-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        return (BauakteService(store: store, files: files, userId: userId), SyncEngine(store: store, transport: server, files: files))
    }

    @Test("Anlegen offline: Regeln wie auf dem Server, sortiert nach Geschoss und Name, Operation room.create")
    func create() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "EFH Schneider"))

        await #expect(throws: RoomError.nameRequired) { try await service.addRoom(RoomInput(name: "  "), to: bauakte) }
        await #expect(throws: RoomError.invalidArea) { try await service.addRoom(RoomInput(name: "Bad", areaSquareMeters: -2), to: bauakte) }
        await #expect(throws: RoomError.invalidThickness) { try await service.addRoom(RoomInput(name: "Bad", thicknessMillimeters: 5), to: bauakte) }

        let bad = try await service.addRoom(RoomInput(name: " Bad ", floor: "OG", areaSquareMeters: 8.456, thicknessMillimeters: 65,
                                                       underfloorHeating: true, status: .laid, notes: " Fliesen folgen "), to: bauakte)
        let kitchen = try await service.addRoom(RoomInput(name: "Küche", floor: "EG", areaSquareMeters: 14), to: bauakte)
        let cellar = try await service.addRoom(RoomInput(name: "Waschküche", floor: "KG"), to: bauakte)
        let attic = try await service.addRoom(RoomInput(name: "Studio", floor: "DG"), to: bauakte)

        let all = try await service.rooms(of: bauakte)
        #expect(all.map(\.id) == [cellar, kitchen, bad, attic])
        try await service.deleteRoom(cellar)
        try await service.deleteRoom(attic)
        let rooms = try await service.rooms(of: bauakte)
        #expect(rooms.map(\.id) == [kitchen, bad])
        let saved = try #require(rooms.last)
        #expect(saved.name == "Bad" && saved.notes == "Fliesen folgen" && saved.areaSquareMeters == 8.46 && saved.status == .laid)
        #expect(saved.syncState == .pending)
        #expect(try await service.store.operations().filter { $0.entityType == .room }.map(\.type) == ["room.create", "room.create"])

        let totals = RoomTotals(rooms)
        #expect(totals.count == 2 && abs(totals.area - 22.46) < 0.001 && totals.heatedArea == 8.46 && totals.laid == 1)

        _ = await engine.synchronize()
        #expect(server.entity(bad)?["bauakteId"]?.string == bauakte.uuidString.lowercased())
        #expect(try await service.rooms(of: bauakte).allSatisfy { $0.syncState == .synced })
    }

    @Test("App-Update mit neuer Datensatzart: Räume, die eine ältere Version im Feed übersprungen hat, kommen per Neuabgleich")
    func resyncAfterUpdate() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = UUID.v7()
        let room = UUID.v7()
        server.serverSideChange("bauakte", id: bauakte, data: ["id": JSONValue(bauakte), "title": "EFH", "status": "planned", "version": 1])
        server.serverSideChange("room", id: room, data: ["id": JSONValue(room), "bauakteId": JSONValue(bauakte), "name": "Bad", "status": "laid",
                                                         "version": 1])

        // Erstinstallation: nichts nachzuholen.
        #expect(try await service.store.beginResyncForNewTypesIfNeeded() == false)
        #expect(await engine.synchronize() == .completed)
        #expect(try await service.rooms(of: bauakte).map(\.id) == [room])
        #expect(try await service.store.beginResyncForNewTypesIfNeeded() == false)

        // Stand einer älteren Version: Raum im Feed übersprungen, Cursor dahinter, „room“ unbekannt.
        let cursor = try #require(try await service.store.cursor)
        try await service.store.apply(PullResponse(changes: [SyncChange(entityType: "room", id: room, deleted: true, data: nil)],
                                                   cursor: cursor, hasMore: false, visibleBauakteIds: nil))
        try await service.store.setMeta("feed_types", "bauakte,contact,file,note,workItem")
        #expect(try await service.rooms(of: bauakte).isEmpty)

        #expect(await engine.synchronize() == .completed)
        #expect(try await service.rooms(of: bauakte).map(\.id) == [room])
        #expect(try await service.bauakte(bauakte)?.title == "EFH")
        #expect(try await service.store.beginResyncForNewTypesIfNeeded() == false)
    }

    @Test("Ändern sendet nur geänderte Felder; Löschen einer nie gesendeten Anlage braucht keinen Server")
    func updateAndDelete() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Halle"))
        let hall = try await service.addRoom(RoomInput(name: "Halle", areaSquareMeters: 600), to: bauakte)
        _ = await engine.synchronize()

        let room = try #require(try await service.rooms(of: bauakte).first)
        var input = RoomInput(room)
        input.status = .ready
        input.areaSquareMeters = 612.5
        try await service.updateRoom(room, to: input)
        let update = try #require(try await service.store.operations().last)
        #expect(update.type == "room.update")
        #expect(Set(update.payload["changes"]?.object?.keys.map { $0 } ?? []) == ["status", "areaSquareMeters"])

        _ = await engine.synchronize()
        #expect(server.entity(hall)?["status"]?.string == "ready")
        #expect(server.entity(hall)?["areaSquareMeters"]?.number == 612.5)

        let draft = try await service.addRoom(RoomInput(name: "Lager"), to: bauakte)
        try await service.deleteRoom(draft)
        #expect(try await service.store.operations().filter { $0.entityId == draft }.isEmpty)

        try await service.deleteRoom(hall)
        #expect(try await service.rooms(of: bauakte).isEmpty)
        _ = await engine.synchronize()
        // Gelöschte Datensätze liefert der Testserver als leeren Wert.
        #expect(server.entity(hall)?["name"]?.string == nil)
        #expect(try await service.store.operations().isEmpty)
    }
}
