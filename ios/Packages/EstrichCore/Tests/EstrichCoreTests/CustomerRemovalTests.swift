import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Kunden löschen und archivieren", .serialized)
struct CustomerRemovalTests {
    private func makeService(_ server: SimulatedSyncServer) throws -> (BauakteService, SyncEngine) {
        let directory = FileManager.default.temporaryDirectory.appending(path: "kunden-loeschen-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        return (BauakteService(store: store, files: files, userId: UUID.v7()), SyncEngine(store: store, transport: server, files: files))
    }

    @Test("Archivierte Kunden: nicht in Auswahllisten, aber weiter an der Bauakte; Tombstone entfernt gelöschte Kunden")
    func archivedAndDeleted() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server)
        let active = UUID.v7()
        let archived = UUID.v7()
        let deleted = UUID.v7()
        server.serverSideChange("contact", id: active, data: ["id": JSONValue(active), "name": "Aktiv GmbH"])
        server.serverSideChange("contact", id: archived, data: ["id": JSONValue(archived), "name": "Archiv KG", "archivedAt": "2026-11-01T08:00:00Z"])
        server.serverSideChange("contact", id: deleted, data: ["id": JSONValue(deleted), "name": "Weg GmbH"])
        _ = await engine.synchronize()

        #expect(try await service.contacts().map(\.name) == ["Aktiv GmbH", "Archiv KG", "Weg GmbH"])
        #expect(try await service.activeContacts().map(\.name) == ["Aktiv GmbH", "Weg GmbH"])
        let kg = try #require(try await service.contact(archived))
        #expect(kg.isArchived)
        #expect(kg.anonymizedAt == nil)

        server.serverSideDelete("contact", id: deleted)
        server.serverSideChange("contact", id: archived, data: ["id": JSONValue(archived), "name": "Archiv KG"])
        _ = await engine.synchronize()
        #expect(try await service.contact(deleted) == nil)
        #expect(try await service.activeContacts().map(\.name) == ["Aktiv GmbH", "Archiv KG"])
    }

    @Test("Bezüge beschreiben, Bestätigung prüfen, Antwort lesen")
    func describeAndConfirm() throws {
        let refs = try JSONCoding.decoder.decode(CustomerReferences.self, from: Data("""
            {"id":"01a00000-0000-7000-8000-000000000001","name":"Huber","customerNumber":"600001","deletable":false,"confirmText":"600001",
             "archived":false,"anonymized":false,"references":[
               {"kind":"bauakten","label":"Bauakten","count":2,"link":"/bauakten?kunde=x","items":[{"id":"01a00000-0000-7000-8000-000000000002","text":"B-1 · Halle","link":null}]},
               {"kind":"parties","label":"Projektbeteiligung","count":1,"link":null,"items":[]},
               {"kind":"calls","label":"Anrufe und Anfragen","count":3,"link":null,"items":[]}]}
            """.utf8))
        #expect(refs.summary == "2 Bauakten, 1 Projektbeteiligung und 3 Anrufe und Anfragen")
        #expect(refs.references[0].items.first?.text == "B-1 · Halle")
        #expect(CustomerRemoval.describe([]) == "")
        #expect(CustomerRemoval.confirms(" 600001 ", "600001"))
        #expect(CustomerRemoval.confirms("familie huber", "Familie Huber"))
        #expect(!CustomerRemoval.confirms("", ""))
        #expect(!CustomerRemoval.confirms("60000", "600001"))
    }
}
