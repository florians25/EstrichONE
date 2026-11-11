import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("App-Updates")
struct AppUpdateTests {
    @Test("Versionen wie auf dem Server vergleichen")
    func versions() {
        #expect(AppVersionOrder.compare("1.0.9", "1.0.10") == -1)
        #expect(AppVersionOrder.compare("1.1.0-preview.2", "1.1.0") == -1)
        #expect(AppVersionOrder.compare("1.1.0-preview.10", "1.1.0-preview.9") == 1)
        #expect(AppVersionOrder.compare("v1.0.5+7", "1.0.5") == 0)
        #expect(AppVersionOrder.compare("2", "1.9.9") == 1)
        #expect(AppVersionOrder.isValid("1.0.12"))
        #expect(!AppVersionOrder.isValid("neu"))
    }

    @Test("Prüfung gedrosselt: Start nach 30 Minuten, Rückkehr nach 6 Stunden, von Hand immer")
    func schedule() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        #expect(AppUpdateSchedule.isDue(lastCheck: nil, now: now, trigger: .resume))
        #expect(!AppUpdateSchedule.isDue(lastCheck: now.addingTimeInterval(-60), now: now, trigger: .launch))
        #expect(AppUpdateSchedule.isDue(lastCheck: now.addingTimeInterval(-AppUpdateSchedule.launchInterval), now: now, trigger: .launch))
        #expect(!AppUpdateSchedule.isDue(lastCheck: now.addingTimeInterval(-AppUpdateSchedule.launchInterval), now: now, trigger: .resume))
        #expect(AppUpdateSchedule.isDue(lastCheck: now.addingTimeInterval(-1), now: now, trigger: .manual))
        #expect(AppUpdateSchedule.isDue(lastCheck: now.addingTimeInterval(60), now: now, trigger: .resume))
    }

    @Test("„Neu in dieser Version“ nur nach einem Update")
    func whatsNew() {
        #expect(WhatsNewPolicy.since(lastRun: nil, current: "1.0.12") == nil)
        #expect(WhatsNewPolicy.since(lastRun: "1.0.12", current: "1.0.12") == nil)
        #expect(WhatsNewPolicy.since(lastRun: "1.0.13", current: "1.0.12") == nil)
        #expect(WhatsNewPolicy.since(lastRun: "1.0.10", current: "1.0.12") == "1.0.10")
    }

    @Test("Antwort des Servers lesen, unbekannter Stand sperrt nie")
    func decode() throws {
        let json = """
        {"platform":"ios","installedVersion":"1.0.10","serverVersion":"1.0.12","latestVersion":"1.0.12","minimumVersion":"1.0.0",
         "status":"recommended","message":"Bitte bis Freitag","download":{"kind":"store","url":"https://testflight.apple.com/join/abc"},
         "releaseNotes":[{"version":"1.0.12","title":"EstrichONE 1.0.12","sections":[{"title":"Apps","items":["Neu"]}]}]}
        """
        let check = try JSONCoding.decoder.decode(AppUpdateCheck.self, from: Data(json.utf8))
        #expect(check.status == .recommended)
        #expect(check.download.url == "https://testflight.apple.com/join/abc")
        #expect(check.releaseNotes.first?.sections.first?.items == ["Neu"])
        let future = try JSONCoding.decoder.decode(AppUpdateCheck.self, from: Data(
            #"{"installedVersion":"1.0.10","serverVersion":"2.0.0","status":"irgendwas"}"#.utf8))
        #expect(future.status == .current)
        #expect(future.download.kind == "none")
    }

    /// Antwortet je Pfad mit Status und Inhalt und merkt sich die Anfragen.
    private final class Recorder: HTTPTransport, @unchecked Sendable {
        let status: Int
        let body: String
        private(set) var requests: [URLRequest] = []

        init(status: Int, body: String) {
            self.status = status
            self.body = body
        }

        func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
            requests.append(request)
            return (Data(body.utf8), HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: nil)!)
        }
    }

    private final class Box: @unchecked Sendable {
        var rejection: AppUpdateRejection?
    }

    @Test("Jede Anfrage trägt die App-Version; 426 meldet die Mindestversion und gilt als „Update nötig“")
    func identifyingTransport() async throws {
        let base = Recorder(status: 426, body: #"{"title":"App-Update erforderlich","detail":"Bitte aktualisieren","code":"CLIENT_UPDATE_REQUIRED","minimumVersion":"1.0.12","latestVersion":"1.0.13"}"#)
        let signal = AppUpdateSignal()
        let box = Box()
        signal.onRejection { box.rejection = $0 }
        let transport = ClientIdentifyingTransport(base: base, version: "1.0.10", signal: signal)
        let api = APIClient(server: try ServerAddress.parse("estrich.example.com"), transport: transport)
        do throws(APIError) {
            let _: ServerInfo = try await api.get("server-info")
            Issue.record("426 erwartet")
        } catch {
            #expect(error.isUpdateRequired)
        }
        #expect(base.requests.first?.value(forHTTPHeaderField: ClientIdentifyingTransport.header) == "ios/1.0.10")
        #expect(box.rejection == AppUpdateRejection(minimumVersion: "1.0.12", latestVersion: "1.0.13", message: "Bitte aktualisieren"))
        #expect(AppUpdateRejection(status: 503, data: Data()) == nil)
    }

    @Test("Update-Prüfung und Neuerungen fragen den Server mit Plattform und Version")
    func checkRequest() async throws {
        let base = Recorder(status: 200, body: #"{"installedVersion":"1.0.10","serverVersion":"1.0.12","status":"current"}"#)
        let api = APIClient(server: try ServerAddress.parse("estrich.example.com"), transport: base)
        let check = try await api.checkAppUpdate(version: "1.0.10")
        #expect(check.status == .current)
        let url = try #require(base.requests.first?.url?.absoluteString)
        #expect(url.contains("app-updates/check"))
        #expect(url.contains("platform=ios"))
        #expect(url.contains("version=1.0.10"))
    }
}

/// Lokale Datenbank über App-Updates hinweg: Migrationen ergänzen nur, ungesendete Änderungen bleiben erhalten.
@Suite("Datenbank-Upgrade", .serialized)
struct SchemaUpgradeTests {
    private let directory = FileManager.default.temporaryDirectory.appending(path: "schema-\(UUID().uuidString)")
    private let key = SymmetricKey(size: .bits256)
    private var url: URL { directory.appending(path: "data.sealed") }

    @Test("Update auf ein Schema mit neuer Version: Outbox und Daten bleiben, Migration läuft genau einmal")
    func upgradeKeepsOutbox() async throws {
        let id = UUID.v7()
        do {
            let store = LocalStore(database: try EncryptedDatabase(url: url, key: key, migrations: LocalStore.schema))
            try await store.create(.bauakte, id: id, data: .object(["id": JSONValue(id), "title": .string("EFH Huber")]),
                                   payload: ["title": .string("EFH Huber")])
            #expect(try await store.operations().count == 1)
        }
        // Nächste App-Version hängt eine Migration an (neue Spalte, neue Tabelle).
        let next = LocalStore.schema + [{ db in
            try db.execute("ALTER TABLE outbox ADD COLUMN priority INTEGER NOT NULL DEFAULT 0")
            try db.execute("CREATE TABLE IF NOT EXISTS draft (id TEXT PRIMARY KEY, body TEXT)")
        }]
        let upgraded = try EncryptedDatabase(url: url, key: key, migrations: next)
        #expect(upgraded.database.userVersion == next.count)
        let store = LocalStore(database: upgraded)
        #expect(try await store.operations().count == 1)
        #expect(try await store.entity(.bauakte, id)?.data["title"] == .string("EFH Huber"))
        // Erneutes Öffnen: keine zweite Ausführung (ALTER TABLE würde sonst scheitern).
        _ = try EncryptedDatabase(url: url, key: key, migrations: next)
    }

    @Test("Fehlgeschlagene Migration: alter Stand bleibt vollständig erhalten, nächster Start versucht es erneut")
    func failedMigrationKeepsData() async throws {
        do {
            let store = LocalStore(database: try EncryptedDatabase(url: url, key: key, migrations: LocalStore.schema))
            let id = UUID.v7()
            try await store.create(.bauakte, id: id, data: .object(["id": JSONValue(id)]), payload: ["title": .string("Halle")])
        }
        struct Broken: Error {}
        let broken = LocalStore.schema + [{ db in
            try db.execute("CREATE TABLE half (id TEXT)")
            throw Broken()
        }]
        #expect(throws: Broken.self) { _ = try EncryptedDatabase(url: url, key: key, migrations: broken) }
        let reopened = try EncryptedDatabase(url: url, key: key, migrations: LocalStore.schema)
        #expect(reopened.database.userVersion == LocalStore.schema.count)
        #expect(try await LocalStore(database: reopened).operations().count == 1)
        #expect((try? reopened.database.query("SELECT * FROM half")) == nil)
    }

    @Test("Zurückgestufte App (z. B. TestFlight): neuere Datenbank wird geöffnet, nicht verändert")
    func downgradeLeavesDatabase() async throws {
        let newer = LocalStore.schema + [{ db in try db.execute("CREATE TABLE future (id TEXT)") }]
        do {
            let store = LocalStore(database: try EncryptedDatabase(url: url, key: key, migrations: newer))
            let id = UUID.v7()
            try await store.create(.bauakte, id: id, data: .object(["id": JSONValue(id)]), payload: ["title": .string("Neubau")])
        }
        let older = try EncryptedDatabase(url: url, key: key, migrations: LocalStore.schema)
        #expect(older.database.userVersion == newer.count)
        #expect(try await LocalStore(database: older).operations().count == 1)
    }
}
