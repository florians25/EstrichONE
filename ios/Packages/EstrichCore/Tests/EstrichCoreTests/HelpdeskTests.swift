import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Helpdesk", .serialized)
struct HelpdeskTests {
    private let userId = UUID.v7()

    private func makeStore() throws -> (LocalStore, FileVault, SyncEngine, SimulatedSyncServer) {
        let directory = FileManager.default.temporaryDirectory.appending(path: "helpdesk-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        let server = SimulatedSyncServer()
        return (store, files, SyncEngine(store: store, transport: server, files: files), server)
    }

    private func report(log: DiagnosticsLog, sync: SyncSummary? = nil) -> DiagnosticsReport {
        DiagnosticsReport(appVersion: "1.0.4", build: "112", platform: "iOS", osVersion: "26.1", deviceModel: "iPhone16,2",
                          deviceKind: .pool, deviceId: UUID(uuidString: "7d1e0000-0000-7000-8000-000000000001"), sync: sync,
                          network: "WLAN", freeStorageBytes: 1_200_000_000, log: log)
    }

    @Test("14, AK-3: Diagnosepaket enthält keine Bauakten-, Kunden- oder Tokendaten")
    func diagnosticsContainNoContent() async throws {
        let (store, files, _, _) = try makeStore()
        let service = BauakteService(store: store, files: files, userId: userId)
        // Präparierte Inhalte mit eindeutigen Zeichenfolgen.
        let bauakte = try await service.create(NewBauakte(title: "GEHEIM-Bauakte-4711", street: "Kundenweg 99", city: "Geheimstadt"))
        try await service.addNote("GEHEIM-Notiz Schlüssel unter der Matte", to: bauakte)
        try await store.setMeta("session", "eo1_GEHEIMTOKEN eor1_GEHEIMREFRESH")

        let log = DiagnosticsLog()
        log.record("SYNC_PUSH", ["ops": 2])
        log.record("UPLOAD_FAILED", ["status": 503])
        log.recordError(code: "SYNC_UPLOAD_FAILED")
        log.recordError(code: "GEHEIM-Bauakte-4711 Kunde Müller")   // Freitext wird nie übernommen
        let diagnostics = report(log: log, sync: try await store.summary())

        let json = try JSONValue.object(diagnostics.payload).encoded()
        for secret in ["GEHEIM", "Kundenweg", "Geheimstadt", "Müller", "eo1_", "eor1_", "Matte", bauakte.key] {
            #expect(!json.contains(secret), "\(secret) im Diagnosepaket")
        }
        #expect(json.contains("SYNC_UPLOAD_FAILED"))
        #expect(json.contains("UNKNOWN_ERROR"))
        #expect(diagnostics.payload["deviceId"]?.string == "7d1e0000")
        #expect(diagnostics.log.allSatisfy { $0.range(of: #"^[0-9TZ:\-]+ [A-Z_]+( [a-z]+=\d+)*$"#, options: .regularExpression) != nil })
        #expect(diagnostics.displayLines.contains { $0.label == "Synchronisation" })
    }

    @Test("Meldung offline: sofort gespeichert, mit Foto und Diagnose genau einmal gesendet (14, AK-1)")
    func offlineReport() async throws {
        let (store, files, engine, server) = try makeStore()
        let helpdesk = HelpdeskService(store: store, files: files, userId: userId)
        await #expect(throws: TicketError.titleRequired) { try await helpdesk.report(NewTicket(title: "  "), diagnostics: nil) }

        let id = try await helpdesk.report(NewTicket(title: " Fotos werden nicht übertragen ", description: "seit heute", category: .sync,
                                                     reporterBlocked: true),
                                           diagnostics: report(log: DiagnosticsLog()), attachments: [Data([0xFF, 0xD8, 0xFF, 0xE0])])
        #expect(try await helpdesk.localReports().map(\.id) == [id])
        #expect(try await store.uploads().count == 1)

        _ = await engine.synchronize()
        let sent = try #require(server.entity(id))
        #expect(sent["title"]?.string == "Fotos werden nicht übertragen")
        #expect(sent["attachmentFileIds"]?.array?.count == 1)
        #expect(sent["diagnostics"]?["appVersion"]?.string == "1.0.4")
        #expect(sent["reporterBlocked"] == .bool(true))
        #expect(try await store.operations().isEmpty)
        _ = await engine.synchronize()
        #expect(server.receivedOpIds.count == Set(server.receivedOpIds).count, "Nichts wird doppelt gesendet")
    }

    @Test("Meldung ohne Anmeldung: gleiche signierte Nachricht wie der Server erwartet")
    func deviceProofMessage() throws {
        let device = try #require(UUID(uuidString: "01A00000-0000-7000-8000-0000000000AA"))
        let ticket = try #require(UUID(uuidString: "01A00000-0000-7000-8000-0000000000BB"))
        let message = DeviceTicketReporter.proofMessage(deviceId: device, timestamp: "2026-10-20T08:00:00Z", ticketId: ticket,
                                                        title: "Anmeldung klappt nicht", description: nil)
        // SHA-256 von „Anmeldung klappt nicht\n“ (gegengeprüft mit Python hashlib).
        #expect(message == "EstrichONE ticket v1\n01a00000-0000-7000-8000-0000000000aa\n2026-10-20T08:00:00Z\n01a00000-0000-7000-8000-0000000000bb\n"
                + "e5360dd7a389719821eb43b30b668577ad1288225b3fe35bf8a46ca141b8464c")
    }
}
