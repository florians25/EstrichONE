import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

/// „Nur ansehen“: Änderungen an nicht zugeteilten Bauakten kommen gar nicht erst in die Warteschlange.
@Suite("Nur ansehen (nicht zugeteilt)", .serialized)
struct ViewOnlyTests {
    private let userId = UUID.v7()

    private func makeService() throws -> BauakteService {
        let directory = FileManager.default.temporaryDirectory.appending(path: "viewonly-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        return BauakteService(store: store, files: files, userId: userId)
    }

    private func serverBauakte(_ id: UUID, title: String) -> SyncChange {
        SyncChange(entityType: "bauakte", id: id, deleted: false,
                   data: ["id": JSONValue(id), "title": .string(title), "status": "planned", "version": 1])
    }

    @Test("Sichtbar, aber nicht zugeteilt: Notiz, Raum und Status werden abgewiesen; nach dem Eintragen erlaubt")
    func blocksUntilJoined() async throws {
        let service = try makeService()
        let assigned = UUID.v7()
        let foreign = UUID.v7()
        try await service.store.apply(PullResponse(changes: [serverBauakte(assigned, title: "Eigene"), serverBauakte(foreign, title: "Fremde")],
                                                   cursor: "1:1", hasMore: false, visibleBauakteIds: [assigned, foreign],
                                                   assignedBauakteIds: [assigned]))

        #expect(try await service.store.isViewOnly(bauakte: foreign))
        #expect(try await service.store.isViewOnly(bauakte: assigned) == false)

        await #expect(throws: BauakteError.notAssigned) { try await service.addNote("Darf ich?", to: foreign) }
        await #expect(throws: RoomError.notAssigned) { try await service.addRoom(RoomInput(name: "Bad"), to: foreign) }
        await #expect(throws: BauakteError.notAssigned) { try await service.changeStatus(.inProgress, of: foreign) }
        #expect(try await service.store.operations().isEmpty)

        // Zugeteilte Bauakte: wie gewohnt.
        try await service.addNote("Material da", to: assigned)

        // „Mich als Beteiligten eintragen“.
        try await service.store.setViewOnly(bauakte: foreign, false)
        try await service.addNote("Jetzt dabei", to: foreign)
        #expect(try await service.store.operations().count == 2)
        #expect(BauakteNotAssignedError.message == BauakteViewOnly.banner)
    }

    @Test("Ältere Server ohne Angabe der bearbeitbaren Bauakten: alles bleibt bearbeitbar")
    func olderServer() async throws {
        let service = try makeService()
        let bauakte = UUID.v7()
        try await service.store.apply(PullResponse(changes: [serverBauakte(bauakte, title: "Alt")], cursor: "1:1", hasMore: false,
                                                   visibleBauakteIds: [bauakte]))
        #expect(try await service.store.isViewOnly(bauakte: bauakte) == false)
        try await service.addNote("Geht", to: bauakte)
    }

    @Test("Online-Kopf einer nicht zugeteilten Bauakte und verständliche Ablehnung")
    func remoteAndRejection() throws {
        let id = UUID.v7()
        let remote = try #require(RemoteBauakte(remote: ["id": JSONValue(id), "title": "Halle Nord", "status": "inProgress",
                                                         "isAssigned": false, "canEdit": false]))
        #expect(remote.bauakte.title == "Halle Nord" && remote.bauakte.status == .inProgress)
        #expect(!remote.isAssigned && !remote.canEdit)
        #expect(RemoteBauakte(remote: ["id": JSONValue(id), "title": "Alt"])?.canEdit == true)
        #expect(BauakteViewOnly.message(forCode: "BAUAKTE_NOT_ASSIGNED")?.contains("Beteiligter") == true)
        #expect(BauakteViewOnly.message(forCode: "VALIDATION_FAILED") == nil)
    }
}
