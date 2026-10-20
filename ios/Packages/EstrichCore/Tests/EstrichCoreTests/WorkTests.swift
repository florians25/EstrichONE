import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Notizen, Aufgaben und Mängel", .serialized)
struct WorkTests {
    private let userId = UUID.v7()

    private func makeService(server: SimulatedSyncServer) throws -> (BauakteService, SyncEngine) {
        let directory = FileManager.default.temporaryDirectory.appending(path: "work-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        return (BauakteService(store: store, files: files, userId: userId), SyncEngine(store: store, transport: server, files: files))
    }

    @Test("Notiz offline schreiben: sofort sichtbar, nach dem Abgleich beim Server")
    func notes() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Halle Nord"))

        await #expect(throws: BauakteError.textRequired) { try await service.addNote("  \n ", to: bauakte) }
        let first = try await service.addNote("Zugang über die Garage", to: bauakte)
        let second = try await service.addNote(" Schlüssel beim Nachbarn ", to: bauakte)

        let local = try await service.notes(of: bauakte)
        #expect(local.map(\.text) == ["Schlüssel beim Nachbarn", "Zugang über die Garage"], "Neueste zuerst")
        #expect(local.allSatisfy { $0.authorUserId == userId && $0.syncState == .pending })

        _ = await engine.synchronize()
        #expect(server.entity(first)?["text"]?.string == "Zugang über die Garage")
        #expect(server.entity(second)?["bauakteId"]?.string == bauakte.key)
        #expect(try await service.notes(of: bauakte).allSatisfy { $0.syncState == .synced })
    }

    @Test("Mangel erfassen und als erledigt markieren; Reihenfolge offen vor erledigt, dann nach Fälligkeit")
    func workItems() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "EFH Müller"))
        let colleague = UUID.v7()

        await #expect(throws: BauakteError.titleRequired) { try await service.addWorkItem(NewWorkItem(kind: .task, title: " ")) }
        let defect = try await service.addWorkItem(NewWorkItem(kind: .defect, title: "Riss im Flur", dueDate: "2026-10-22", bauakteId: bauakte))
        let later = try await service.addWorkItem(NewWorkItem(kind: .task, title: "Randdämmstreifen kürzen", dueDate: "2026-10-30", bauakteId: bauakte))
        let undated = try await service.addWorkItem(NewWorkItem(kind: .task, title: "Abnahme vereinbaren", bauakteId: bauakte))
        let foreign = try await service.addWorkItem(NewWorkItem(kind: .task, title: "Für Kollegen", assigneeUserId: colleague, bauakteId: bauakte))
        // Offline, vor dem ersten Abgleich: sofort in „Meine Aufgaben“; Erledigtes und Fremdes nicht.
        let doneOffline = try await service.addWorkItem(NewWorkItem(kind: .task, title: "Schon erledigt"))
        try await service.setStatus(.done, of: doneOffline)
        #expect(Set(try await service.myWorkItems().map(\.id)) == [defect, later, undated])
        #expect(Set(try await service.myWorkItems(includeDone: true).map(\.id)) == [defect, later, undated, doneOffline])
        _ = await engine.synchronize()

        try await service.setStatus(.done, of: later)
        let items = try await service.workItems(of: bauakte)
        #expect(items.map(\.id) == [defect, undated, foreign, later])
        #expect(items.first?.kind == .defect)
        #expect(items.first?.isDue(on: "2026-10-22") == true)
        #expect(items.first?.isDue(on: "2026-10-21") == false)


        _ = await engine.synchronize()
        #expect(server.entity(later)?["status"]?.string == "done")
        #expect(server.entity(foreign)?["assigneeUserId"]?.string == colleague.key)
    }

    @Test("Verzeichnis wird für Namen offline zwischengespeichert")
    func directoryCache() async throws {
        let (service, _) = try makeService(server: SimulatedSyncServer())
        #expect(try await service.cachedDirectory().isEmpty)
        let users = [DirectoryUser(id: userId, displayName: "Max Huber", isActive: true)]
        try await service.saveDirectory(users)
        #expect(try await service.cachedDirectory() == users)
    }

    @Test("Kalendertage im Serverformat")
    func calendarDays() throws {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        let date = try #require(CalendarDay.date("2026-03-09", calendar: calendar))
        #expect(CalendarDay.string(date, calendar: calendar) == "2026-03-09")
        #expect(CalendarDay.date("2026-3", calendar: calendar) == nil)
    }
}
