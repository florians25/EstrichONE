import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import Testing
@testable import EstrichCore

/// Gegen einen echten, eingerichteten Server (CI-Job „EstrichCore gegen Server“).
/// `ESTRICHONE_LIVE_SERVER`, `ESTRICHONE_LIVE_LOGIN`, `ESTRICHONE_LIVE_PASSWORD` (Administrator).
@Suite("Echter Server", .serialized, .enabled(if: ProcessInfo.processInfo.environment["ESTRICHONE_LIVE_SERVER"] != nil))
struct LiveServerTests {
    private let environment = ProcessInfo.processInfo.environment
    private let server: ServerAddress
    private let login: String
    private let password: String
    private let description = DeviceDescription(name: "Integrationstest", model: "CI", osVersion: "Linux", appVersion: "0.2.0")

    init() throws {
        server = try ServerAddress.parse(environment["ESTRICHONE_LIVE_SERVER"] ?? "", allowInsecureLocal: true)
        login = environment["ESTRICHONE_LIVE_LOGIN"] ?? "admin"
        password = environment["ESTRICHONE_LIVE_PASSWORD"] ?? ""
    }

    private func makeService(keys: DeviceKeys = SoftwareDeviceKeys()) -> AccountService {
        AccountService(api: APIClient(server: server, transport: URLSession(configuration: .ephemeral)),
                       storage: Samples.temporaryStorage(), keys: keys, secrets: InMemorySecretStore(), pinIterations: 1_000)
    }

    private func authorized(_ service: AccountService, _ account: UnlockedAccount, ended: Recorder<SessionEnd> = Recorder()) -> AuthorizedClient {
        AuthorizedClient(api: service.api, session: account.session, keys: service.keys, persist: { _ in }, ended: { ended.append($0) })
    }

    @Test("Verbindungsprüfung erkennt den Server")
    func serverCheck() async throws {
        let result = await ServerConnectionChecker(transport: URLSession(configuration: .ephemeral),
                                                   appVersion: SemanticVersion(major: 0, minor: 2, patch: 0)).check(server)
        let verified = try result.get()
        #expect(verified.info.product == "EstrichONE")
        #expect(verified.info.setupRequired == false)
    }

    @Test("Persönliches Gerät: Registrierung, Erneuerung mit Gerätesignatur, Abmeldung")
    func personalDevice() async throws {
        let service = makeService()
        let tokens = try await service.signIn(login: login, password: password, clientName: "CI")
        let registration = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        #expect(registration.status == .active)
        let account = try service.createVault(for: tokens, pin: nil)
        let client = authorized(service, account)

        try await client.refreshNow()
        let device: DeviceResponse = try await client.get("devices/me")
        #expect(device.id == registration.deviceId)
        #expect(await client.session.refreshToken != tokens.refreshToken)

        await service.signOut(account, client: client)
        await #expect(throws: APIError.self) {
            let _: DeviceResponse = try await client.get("devices/me")
        }
    }

    @Test("Poolgerät per Einrichtungscode, zweite Person per Signatur, Code nur einmal")
    func poolDeviceWithEnrollmentCode() async throws {
        // Administrator erzeugt im „IT Center“ einen Code.
        let admin = makeService()
        let adminTokens = try await admin.signIn(login: login, password: password, clientName: "CI Admin")
        _ = try await admin.bindDevice(tokens: adminTokens, kind: .personal, enrollmentCode: nil, description: description)
        let adminClient = authorized(admin, try admin.createVault(for: adminTokens, pin: nil))
        let code: EnrollmentCodeResponse = try await adminClient.post("devices/enrollment-codes",
                                                                      body: CreateEnrollmentCodeRequest(kind: .pool, deviceName: "Fahrzeug CI"))
        let link = try EnrollmentLink.parse(try #require(code.link), allowInsecureLocal: true)
        #expect(link.enrollmentCode == code.code)
        #expect(link.deviceKind == .pool)

        // Neues Gerät löst den Code ein: sofort freigegeben, Name aus dem IT Center.
        let pool = makeService()
        let first = try await pool.signIn(login: login, password: password, clientName: "Fahrzeug")
        let registration = try await pool.bindDevice(tokens: first, kind: .personal, enrollmentCode: link.enrollmentCode, description: description)
        #expect(registration.kind == .pool)
        #expect(registration.status == .active)
        #expect(registration.name == "Fahrzeug CI")
        _ = try pool.createVault(for: first, pin: "482913")

        // Zweite Anmeldung auf demselben Gerät: Bindung per Signatur, danach Zugriff mit Erneuerung.
        let second = try await pool.signIn(login: login, password: password, clientName: "Fahrzeug")
        let attached = try await pool.bindDevice(tokens: second, kind: .pool, enrollmentCode: nil, description: description)
        #expect(attached.deviceId == registration.deviceId)
        let client = authorized(pool, try pool.createVault(for: second, pin: "573920"))
        try await client.refreshNow()

        // Ein zweites Gerät kann denselben Code nicht mehr verwenden.
        let other = makeService()
        let otherTokens = try await other.signIn(login: login, password: password, clientName: "Anderes")
        await #expect(throws: AccountError.self) {
            _ = try await other.bindDevice(tokens: otherTokens, kind: .pool, enrollmentCode: code.code, description: description)
        }
    }

    /// Angemeldetes Gerät mit eigener verschlüsselter Datenbank und Synchronisation gegen den echten Server.
    private func syncingDevice() async throws -> (store: LocalStore, files: FileVault, engine: SyncEngine) {
        let device = try await syncingAccount()
        return (device.store, device.files, device.engine)
    }

    private func syncingAccount() async throws -> (store: LocalStore, files: FileVault, engine: SyncEngine, client: AuthorizedClient, userId: UUID) {
        let service = makeService()
        let tokens = try await service.signIn(login: login, password: password, clientName: "CI Sync")
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        let account = try service.createVault(for: tokens, pin: nil)
        let directory = service.storage.userDirectory(account.user.id)
        let store = try LocalStore.open(url: directory.appending(path: "data.sealed"), account: account)
        let files = FileVault(directory: directory.appending(path: "files"), key: account.dataKey)
        let client = authorized(service, account)
        let engine = SyncEngine(store: store, transport: APISyncTransport(client: client), files: files)
        return (store, files, engine, client, account.user.id)
    }

    @Test("Notiz, Mangel und Erledigt über den echten Server; Verlauf und Verzeichnis")
    func workAndHistory() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        let bauakte = try await service.create(NewBauakte(title: "Verlauf Integrationstest"))
        try await service.addNote("Zugang über den Hof", to: bauakte)
        let defect = try await service.addWorkItem(NewWorkItem(kind: .defect, title: "Riss im Flur", dueDate: "2026-11-02",
                                                              assigneeUserId: device.userId, bauakteId: bauakte))
        #expect(await device.engine.synchronize() == .completed)
        try await service.setStatus(.done, of: defect)
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)

        let item = try #require(try await service.workItems(of: bauakte).first)
        #expect(item.kind == .defect && item.status == .done && item.dueDate == "2026-11-02")

        let history = try await device.client.history(of: bauakte)
        #expect(Set(history.map(\.action)) == ["bauakte.created", "bauakte.note_added", "bauakte.workitem_created", "bauakte.workitem_done"])
        #expect(history.first?.text.hasSuffix("hat einen Mangel als behoben markiert.") == true)

        let users = try await device.client.directoryUsers()
        #expect(users.contains { $0.id == device.userId && $0.isActive })
    }

    @Test("Synchronisation: offline anlegen, Nummer vom Server, Notiz und Foto, zweites Gerät sieht alles, Felder zusammengeführt")
    func synchronization() async throws {
        let first = try await syncingDevice()
        let second = try await syncingDevice()
        #expect(await second.engine.synchronize(includeScope: true) == .completed)

        // Erstes Gerät, offline erfasst.
        let bauakte = UUID.v7()
        let fields: [String: JSONValue] = ["title": "EFH Integrationstest", "city": "Landshut"]
        try await first.store.create(.bauakte, id: bauakte, data: JSONValue.object(fields).merging(["id": JSONValue(bauakte)]), payload: fields)
        let note = UUID.v7()
        let noteFields: [String: JSONValue] = ["bauakteId": JSONValue(bauakte), "text": "Estrich CT-C25-F4, 65 mm"]
        try await first.store.create(.note, id: note, data: JSONValue.object(noteFields).merging(["id": JSONValue(note)]), payload: noteFields)
        // 1×1-PNG: Der Server prüft den Inhalt anhand der Signatur.
        let photo = try #require(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="))
        let fileId = UUID.v7()
        let sha = try first.files.store(photo, for: fileId)
        let fileFields: [String: JSONValue] = ["bauakteId": JSONValue(bauakte), "kind": "photo", "originalName": "foto.png",
                                               "contentType": "image/png", "sizeBytes": .number(Double(photo.count)), "sha256": .string(sha)]
        try await first.store.create(.file, id: fileId, data: .object(fileFields), payload: fileFields, operation: "file.announce")
        try await first.store.enqueueUpload(fileId: fileId, size: Int64(photo.count), sha256: sha, contentType: "image/png")

        #expect(await first.engine.synchronize() == .completed)
        let created = try #require(try await first.store.entity(.bauakte, bauakte))
        let number = try #require(created.data["number"]?.string)
        #expect(number.range(of: #"^\d{4}-\d{3,}$"#, options: .regularExpression) != nil)
        #expect(try await first.store.summary().isSettled)
        #expect(try await first.store.uploads().first?.state == .done)

        // Zweites Gerät holt alles.
        #expect(await second.engine.synchronize() == .completed)
        #expect(try await second.store.entity(.bauakte, bauakte)?.data["number"]?.string == number)
        #expect(try await second.store.entities(.note, parent: bauakte).first?.data["text"]?.string == "Estrich CT-C25-F4, 65 mm")
        #expect(try await second.store.entity(.file, fileId)?.data["state"]?.string == "stored")

        // Parallel verschiedene Felder ändern: beide Änderungen bleiben.
        try await first.store.update(.bauakte, id: bauakte, changes: ["city": "Ergolding"])
        try await second.store.update(.bauakte, id: bauakte, changes: ["description": "Zufahrt über den Hof"])
        #expect(await first.engine.synchronize() == .completed)
        #expect(await second.engine.synchronize() == .completed)
        #expect(await first.engine.synchronize() == .completed)
        for device in [first, second] {
            let merged = try #require(try await device.store.entity(.bauakte, bauakte))
            #expect(merged.data["city"]?.string == "Ergolding")
            #expect(merged.data["description"]?.string == "Zufahrt über den Hof")
        }

        // Gleiches Feld parallel: Konflikt, der Serverwert bleibt.
        try await first.store.update(.bauakte, id: bauakte, changes: ["city": "Altdorf"])
        try await second.store.update(.bauakte, id: bauakte, changes: ["city": "Essenbach"])
        #expect(await first.engine.synchronize() == .completed)
        #expect(await second.engine.synchronize() == .completed)
        #expect(try await second.store.conflicts().map(\.conflict.field) == ["city"])
        #expect(try await second.store.entity(.bauakte, bauakte)?.data["city"]?.string == "Altdorf")
    }

    @Test("Bautagebuch, CM-Messung und Erstbegehung über den echten Server; Belegreife stimmt mit der App überein")
    func siteRecords() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        let bauakte = try await service.create(NewBauakte(title: "Prüfungen Integrationstest"))
        try await service.addDiaryEntry(NewDiaryEntry(day: "2026-10-20", text: "Estrich eingebracht", weather: .cloudy, temperatureCelsius: 12), to: bauakte)
        let cm = try await service.addCmMeasurement(to: bauakte, location: "Bad", binder: .calciumSulfate, heated: true, thicknessMm: 50,
                                                    measuredCmPercent: 0.3)
        let initial = try await service.addInitialInspection(to: bauakte, notRequired: false, checks: [.substrate: .ok, .access: .notOk],
                                                             photoFileIds: [], remarks: "Zufahrt nur mit kleinem Lkw")
        let local = try #require(try await service.inspections(of: bauakte).first { $0.id == cm })
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)

        let synced = try #require(try await service.inspections(of: bauakte).first { $0.id == cm })
        #expect(synced.limitCmPercent == local.limitCmPercent && synced.limitCmPercent == 0.3)
        #expect(synced.isReadyForCovering == true && local.isReadyForCovering == true)
        #expect(try await service.inspections(of: bauakte).first { $0.id == initial }?.checks[.access] == .notOk)
        #expect(try await service.diary(of: bauakte).first?.weather == .cloudy)

        let history = try await device.client.history(of: bauakte)
        #expect(history.contains { $0.text.hasSuffix("hat das Bautagebuch für den 20.10.2026 ergänzt.") })
        #expect(history.contains { $0.text.contains("0,3 CM-% bei Grenzwert 0,3 CM-% – belegreif.") })
    }

    @Test("Helpdesk: Meldung mit Diagnose über den Sync, eigene Liste, Meldung ohne Anmeldung mit Gerätesignatur")
    func helpdesk() async throws {
        let device = try await syncingAccount()
        let helpdesk = HelpdeskService(store: device.store, files: device.files, userId: device.userId)
        let log = DiagnosticsLog()
        log.record("SYNC_PUSH", ["ops": 1])
        log.recordError(code: "SYNC_UPLOAD_FAILED")
        let diagnostics = DiagnosticsReport(appVersion: "1.0.0", build: "1", platform: "iOS", osVersion: "26.0", deviceModel: "iPhone16,2",
                                            deviceKind: .personal, deviceId: UUID.v7(), sync: try await device.store.summary(),
                                            network: "WLAN", freeStorageBytes: 1_000_000, log: log)
        let id = try await helpdesk.report(NewTicket(title: "Integrationstest Helpdesk", category: .sync), diagnostics: diagnostics)
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled, "Der Server hat die Diagnosedaten angenommen")
        let mine = try await device.client.myTickets()
        #expect(mine.contains { $0.id == id && $0.status == .new && $0.number.hasPrefix("T-") })

        // Ohne Anmeldung: dasselbe Gerät signiert.
        let keys = SoftwareDeviceKeys()
        let service = makeService(keys: keys)
        let tokens = try await service.signIn(login: login, password: password, clientName: "CI Helpdesk")
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        let deviceId = try #require(service.state.registration?.deviceId)
        let number = try await DeviceTicketReporter.send(NewTicket(title: "Anmeldung klappt nicht", description: "Passwort abgelehnt", category: .signIn),
                                                         reporterName: "CI", deviceId: deviceId, keys: keys, api: service.api, diagnostics: nil)
        #expect(number.hasPrefix("T-"))
        await #expect(throws: TicketError.notRegistered) {
            try await DeviceTicketReporter.send(NewTicket(title: "Fremdes Gerät"), reporterName: nil, deviceId: deviceId,
                                                keys: SoftwareDeviceKeys(), api: service.api, diagnostics: nil)
        }
    }

    @Test("Mein Einsatz: im Büro geplant, in der App sichtbar und offline zwischengespeichert")
    func myPlanning() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        let bauakte = try await service.create(NewBauakte(title: "Einsatz Integrationstest", city: "Landshut"))
        #expect(await device.engine.synchronize() == .completed)

        // Das Büro plant (hier derselbe Administrator) für heute.
        let today = CalendarDay.string(Date())
        let body = try JSONSerialization.data(withJSONObject: [
            "bauakteId": bauakte.key, "day": today, "startTime": "07:00:00", "activity": "Estrich einbringen EG", "userIds": [device.userId.key],
        ])
        struct Saved: Decodable { let id: UUID }
        let _: Saved = try await device.client.send(APIRequest(method: "PUT", path: "planning/assignments/\(UUID.v7().key)", body: body,
                                                               contentType: "application/json"))

        let planning = MyPlanning(store: device.store)
        let online = try #require(await planning.load(using: device.client))
        #expect(online.isCurrent)
        let mine = online.assignments(on: today)
        #expect(mine.contains { $0.bauakteId == bauakte && $0.activity == "Estrich einbringen EG" && $0.startText == "07:00" })
        let offline = try #require(await planning.cached())
        #expect(offline.assignments(on: today) == mine)
    }
}
