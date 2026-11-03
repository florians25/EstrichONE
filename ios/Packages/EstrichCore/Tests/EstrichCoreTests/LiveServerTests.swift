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

    @Test("Einmalpasswort und Zwei-Faktor-Anmeldung: Pflichtwechsel serverseitig, Code-Abfrage, Wiederherstellungscode, Erneuern ohne Code")
    func oneTimePasswordAndTwoFactor() async throws {
        let admin = try await syncingAccount().client
        let suffix = UUID().uuidString.prefix(6).lowercased()
        let userName = "zf.\(suffix)"
        let userBody = try JSONSerialization.data(withJSONObject: [
            "displayName": "Zwei Faktor \(suffix)", "userName": userName, "roleIds": ["01a00000-0000-7000-8000-000000000002"],
        ])
        let created: LiveCreatedUserWithPassword = try await admin.send(APIRequest(method: "POST", path: "users", body: userBody, contentType: "application/json"))

        // Einmalpasswort: Bis zum Wechsel lehnt der Server alles andere ab (auch die Geräteregistrierung).
        let service = makeService()
        let first = try await service.signIn(login: userName, password: created.oneTimePassword, clientName: "CI 2FA")
        #expect(first.mustChangePassword)
        do {
            _ = try await service.bindDevice(tokens: first, kind: .personal, enrollmentCode: nil, description: description)
            Issue.record("Gerät trotz Einmalpasswort registriert")
        } catch {
            guard case let .api(apiError) = error else { throw error }
            #expect(apiError.status == 403 && apiError.code == "PASSWORD_CHANGE_REQUIRED")
        }
        let newPassword = "Zwei-Faktor-CI-2026!"
        try await service.changePassword(tokens: first, current: created.oneTimePassword, new: newPassword)

        // Zwei-Faktor einrichten (wie im Web) – mit der Sitzung, die gerade das Passwort gewechselt hat.
        let setup: LiveTwoFactorSetup = try await service.api.post("auth/two-factor/setup", body: ["currentPassword": newPassword],
                                                                   accessToken: first.accessToken)
        let secret = try #require(LiveTotp.base32Decode(setup.secret))
        let step = LiveTotp.step(Date())
        let enabled: LiveRecoveryCodes = try await service.api.post("auth/two-factor/enable", body: ["code": LiveTotp.code(secret, step: step)],
                                                                    accessToken: first.accessToken)
        #expect(enabled.recoveryCodes.count == 10)

        // Neue Anmeldung: ohne Code → TWO_FACTOR_REQUIRED, falscher Code → TWO_FACTOR_INVALID, richtiger Code → Sitzung.
        do {
            _ = try await service.signIn(login: userName, password: newPassword, clientName: "CI 2FA")
            Issue.record("Anmeldung ohne Code angenommen")
        } catch {
            guard case let .api(apiError) = error else { throw error }
            #expect(apiError.requiresTwoFactorCode)
        }
        do {
            _ = try await service.signIn(login: userName, password: newPassword, clientName: "CI 2FA", twoFactorCode: LiveTotp.code(secret, step: step + 5))
            Issue.record("Falscher Code angenommen")
        } catch {
            guard case let .api(apiError) = error else { throw error }
            #expect(apiError.isInvalidTwoFactorCode)
        }
        let tokens = try await service.signIn(login: userName, password: newPassword, clientName: "CI 2FA",
                                              twoFactorCode: LiveTotp.code(secret, step: step + 1))
        #expect(!tokens.mustChangePassword)
        _ = try await service.bindDevice(tokens: tokens, kind: .personal, enrollmentCode: nil, description: description)
        let account = try service.createVault(for: tokens, pin: nil)
        let client = authorized(service, account)
        // Bestehende Sitzung: Erneuern (Gerätesignatur) braucht nie einen Code.
        try await client.refreshNow()
        #expect(await client.session.refreshToken != tokens.refreshToken)

        // Wiederherstellungscode (groß geschrieben, wie abgetippt) gilt genau einmal.
        let recovery = enabled.recoveryCodes[0].uppercased()
        _ = try await makeService().signIn(login: userName, password: newPassword, clientName: "CI 2FA", twoFactorCode: recovery)
        do {
            _ = try await makeService().signIn(login: userName, password: newPassword, clientName: "CI 2FA", twoFactorCode: recovery)
            Issue.record("Wiederherstellungscode zweimal angenommen")
        } catch {
            guard case let .api(apiError) = error else { throw error }
            #expect(apiError.isInvalidTwoFactorCode)
        }
        await service.signOut(account, client: client)
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
                                                             photoFileIds: [], remarks: "Zufahrt nur mit kleinem Lkw",
                                                             notes: "Erste Zeile\nZweite Zeile")
        let local = try #require(try await service.inspections(of: bauakte).first { $0.id == cm })
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)

        let synced = try #require(try await service.inspections(of: bauakte).first { $0.id == cm })
        #expect(synced.limitCmPercent == local.limitCmPercent && synced.limitCmPercent == 0.3)
        #expect(synced.isReadyForCovering == true && local.isReadyForCovering == true)
        #expect(try await service.inspections(of: bauakte).first { $0.id == initial }?.checks[.access] == .notOk)
        #expect(try await service.inspections(of: bauakte).first { $0.id == initial }?.notes == "Erste Zeile\nZweite Zeile")
        #expect(try await service.diary(of: bauakte).first?.weather == .cloudy)

        let history = try await device.client.history(of: bauakte)
        #expect(history.contains { $0.text.hasSuffix("hat das Bautagebuch für den 20.10.2026 ergänzt.") })
        #expect(history.contains { $0.text.contains("0,3 CM-% bei Grenzwert 0,3 CM-% – belegreif.") })
    }

    @Test("Kundenkartei über den echten Server: Anlage mit mehreren Nummern, Änderung per contact.update")
    func customerCard() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        var edit = CustomerEdit(name: "Kartei Integrationstest")
        edit.phones = [ContactPhone(kind: .mobile, number: "0171 999"), ContactPhone(kind: .office, number: "0871 999", label: "Büro")]
        edit.emails = [ContactEmail(address: "kartei@example.com", label: "privat")]
        edit.preferredChannel = .mobile
        let id = try await service.saveCustomer(edit)
        #expect(await device.engine.synchronize() == .completed)
        var synced = try #require(try await service.contact(id))
        #expect(synced.syncState == .synced && synced.phones.map(\.number) == ["0171 999", "0871 999"] && synced.preferredChannel == .mobile)

        var changed = CustomerEdit(synced)
        changed.city = "Landshut"
        changed.notes = "Zweite Zeile\nmit Umbruch"
        changed.phones.removeFirst()
        try await service.saveCustomer(changed, id: id)
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)
        synced = try #require(try await service.contact(id))
        #expect(synced.city == "Landshut" && synced.phones.map(\.number) == ["0871 999"] && synced.phone == "0871 999")
        #expect(synced.emails.first?.label == "privat")
    }

    @Test("Formular-Schema und Wertelisten über den echten Server; Anlage mit Werten aus dem Schema")
    func formSchemaLive() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        #expect(await device.engine.synchronize() == .completed)
        let schema = try #require(await service.formSchema(using: device.client))
        #expect(!schema.fields.isEmpty)
        let lists = await service.valueLists()
        #expect(lists.label("screedType", "ct") != nil)
        let area = try #require(schema.fields.first { $0.key == "totalArea" })
        let values = FormSchema.setting(.number(120), for: area, in: [:])
        let id = try await service.create(NewBauakte(title: "Schema Integrationstest", projectData: values))
        #expect(await device.engine.synchronize() == .completed)
        let synced = try #require(try await service.bauakte(id))
        #expect(synced.syncState == .synced && synced.project.totalArea == 120)
    }

    @Test("Bauvorhaben über den echten Server: Werte und Zeitraum bei der Anlage, Änderung per bauakte.update")
    func projectValues() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        var project = BauakteProject()
        project.objectType = .efh
        project.totalArea = 182.5
        project.screedType = .caf
        project.construction = .heated
        project.nominalThicknessMm = 45
        project.underfloorHeating = true
        let id = try await service.create(NewBauakte(title: "Bauvorhaben Integrationstest", project: project,
                                                     plannedStart: "2026-11-02", plannedEnd: "2026-11-06"))
        #expect(await device.engine.synchronize() == .completed)
        var synced = try #require(try await service.bauakte(id))
        #expect(synced.syncState == .synced && synced.project == project && synced.plannedStart == "2026-11-02" && synced.plannedEnd == "2026-11-06")

        project.priority = .urgent
        project.siteAccess = "Zufahrt über Hof"
        try await service.updateProject(project, of: id)
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)
        synced = try #require(try await service.bauakte(id))
        #expect(synced.project == project)
    }

    @Test("Abnahmeprotokoll über den echten Server: Mangel mit Frist, beide Unterschriften, PDF; zweites Protokoll mit Kundenlink")
    func acceptanceProtocol() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        let bauakte = try await service.create(NewBauakte(title: "Abnahme Integrationstest"))
        // Gültiges PNG (1 × 1 Pixel), wie es die Unterschriftsfläche liefert.
        let png = try #require(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="))
        let signed = try await service.addAcceptance(NewAcceptance(
            clientName: "Herr Schneider", contractorName: "Huber Estrich", scope: "Zementestrich EG", result: .acceptedWithReservations,
            defects: [AcceptanceDefect(title: "Fuge Bad nacharbeiten", dueDate: "2026-11-15")], clientSignature: png, contractorSignature: png),
            to: bauakte)
        let open = try await service.addAcceptance(NewAcceptance(result: .accepted, contractorSignature: png), to: bauakte)
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)

        let done = try #require(try await service.acceptance(signed))
        #expect(done.syncState == .synced && done.isSigned && done.result == .acceptedWithReservations)
        #expect(done.defectWorkItemIds.count == 1)
        #expect(done.state == .completed && done.pdfFileId != nil)
        #expect(try await service.workItems(of: bauakte).contains { $0.kind == .defect && $0.dueDate == "2026-11-15" })

        let link = try await device.client.customerLink(for: open, email: nil)
        #expect(link.url.contains("/") && !link.emailed)
        _ = await device.engine.synchronize()
        #expect(try await service.acceptance(open)?.state == .awaitingCustomer)
        #expect(try await service.acceptance(open)?.hasClientSignature == false)
    }

    @Test("Neuer Kunde bei der Schnellanlage, gescanntes Dokument und Fotobeschriftung über den echten Server")
    func customerAndDocuments() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        let bauakte = try await service.create(NewBauakte(title: "Kunde Integrationstest"),
                                               newCustomer: NewCustomer(name: "Familie Integrationstest", phone: "0871 4711",
                                                                        email: "kunde@example.com", city: "Landshut"))
        let pdf = Data("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n".utf8)
        let document = try await service.addDocument(pdf, title: "Lieferschein 4711", category: .delivery, to: bauakte)
        let png = try #require(Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="))
        let photo = try await service.addPhoto(png, to: bauakte, capturedAt: Date(), contentType: "image/png", caption: "Bad OG")
        let movie = Data([0, 0, 0, 0x18] + Array("ftypisom".utf8) + Array(repeating: 0, count: 4096))
        let video = try await service.addVideo(movie, to: bauakte, capturedAt: Date(), caption: "Einbringen")

        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)
        let synced = try #require(try await service.bauakte(bauakte))
        let customerId = try #require(synced.customerId)
        let customer = try #require(try await service.contact(customerId))
        #expect(customer.name == "Familie Integrationstest" && customer.phone == "0871 4711" && customer.syncState == .synced)
        let duplicates = try await device.client.contactDuplicates(of: NewCustomer(name: "Familie Integrationstest", phone: "0871 4711"))
        #expect(duplicates.contains { $0.id == customerId })
        #expect(await device.engine.load(bauakte: bauakte) == .completed)
        try await device.client.assignToMe(bauakte)
        let week = try await device.client.planning(from: "2026-10-19", to: "2026-10-25")
        #expect(week.from == "2026-10-19")
        let board = try await device.client.dispatch(day: "2026-10-20")
        #expect(board.day == "2026-10-20")
        try await service.changeStatus(.inProgress, of: bauakte)
        #expect(await device.engine.synchronize() == .completed)
        try await service.changeStatus(.cancelled, reason: "Kunde hat abgesagt", of: bauakte)
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)
        let cancelled = try #require(try await service.bauakte(bauakte))
        #expect(cancelled.status == .cancelled && cancelled.cancellationReason == "Kunde hat abgesagt")
        let notices = try await device.client.systemNotices()
        #expect(notices.maintenance?.active != true)
        let found = try await device.client.searchContacts("familie integrationstest")
        #expect(found.contains { $0.id == customerId && $0.phone == "0871 4711" && !$0.isCompany })
        let stored = try #require(try await service.documents(of: bauakte).first)
        #expect(stored.id == document && stored.category == .delivery && stored.caption == "Lieferschein 4711")

        try await service.setCaption("Bad OG, Randdämmstreifen", of: photo)
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.summary().isSettled)
        #expect(try await service.files(of: bauakte).first { $0.id == photo }?.caption == "Bad OG, Randdämmstreifen")
        #expect(try await device.store.uploads().allSatisfy { $0.state == .done })
        #expect(try await service.files(of: bauakte).first { $0.id == video }?.isStoredOnServer == true)
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

    @Test("Kontakte der Bauakte über den echten Server: offline anlegen, Hauptkontakt wechseln, entfernen (bauakteParty.save/delete)")
    func partiesLive() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        let bauakte = try await service.create(NewBauakte(title: "Kontakte Integrationstest", city: "Landshut"))
        let manager = try await service.saveParty(PartyEdit(role: .siteManager, name: "Anna Lechner", company: "Lechner Bau", phone: "0871 1",
                                                            mobile: "0171 2", email: "a@lechner.example", isPrimary: true), bauakteId: bauakte)
        let site = try await service.saveParty(PartyEdit(role: .siteContact, name: "Poldi Huber", street: "Ahornweg 3", postalCode: "84028",
                                                         city: "Landshut"), bauakteId: bauakte)
        #expect(await device.engine.synchronize() == .completed)
        #expect(try await device.store.operations().isEmpty)

        struct Party: Decodable { let id: UUID; let name: String; let isPrimary: Bool; let mobile: String?; let address: String? }
        var parties: [Party] = try await device.client.get("bauakten/\(bauakte.key)/parties")
        #expect(parties.first?.id == manager && parties.first?.isPrimary == true && parties.first?.mobile == "0171 2")
        #expect(parties.first { $0.id == site }?.address?.contains("Ahornweg 3") == true)

        var edit = PartyEdit(try #require(try await device.store.entity(.bauakteParty, site)))
        edit.isPrimary = true
        try await service.saveParty(edit, id: site, bauakteId: bauakte)
        #expect(await device.engine.synchronize() == .completed)
        parties = try await device.client.get("bauakten/\(bauakte.key)/parties")
        #expect(parties.first?.id == site && parties.filter(\.isPrimary).count == 1)
        #expect(try await service.parties(of: bauakte).first?.id == site)

        try await service.deleteParty(manager)
        #expect(await device.engine.synchronize() == .completed)
        parties = try await device.client.get("bauakten/\(bauakte.key)/parties")
        #expect(parties.map(\.id) == [site])
    }

    @Test("Schnelldisposition über den echten Server: zuweisen mit Konfliktprüfung, Reihenfolge, verschieben, entfernen")
    func quickDispatchLive() async throws {
        let device = try await syncingAccount()
        let service = BauakteService(store: device.store, files: device.files, userId: device.userId)
        let first = try await service.create(NewBauakte(title: "Dispo A Integrationstest", city: "Landshut"))
        let second = try await service.create(NewBauakte(title: "Dispo B Integrationstest", city: "Ergolding"))
        #expect(await device.engine.synchronize() == .completed)

        let crewId = UUID.v7()
        let crewBody = try JSONSerialization.data(withJSONObject: [
            "name": "Kolonne CI \(crewId.key.prefix(4))", "leaderUserId": device.userId.key, "memberUserIds": [device.userId.key], "color": "#0062CC",
        ])
        let _: JSONValue = try await device.client.send(APIRequest(method: "PUT", path: "planning/crews/\(crewId.key)", body: crewBody,
                                                                   contentType: "application/json"))
        let calendar = Calendar.current
        let day = CalendarDay.string(calendar.date(byAdding: .day, value: 20, to: Date())!)
        let next = CalendarDay.string(calendar.date(byAdding: .day, value: 21, to: Date())!)
        let a = UUID.v7()
        let b = UUID.v7()
        let assignA = PlanChange.assign(id: a, bauakteId: first, title: "Dispo A", crewId: crewId, day: day, activity: "Estrich einbringen",
                                        startTime: "07:00:00", endTime: "11:00:00")
        _ = try await device.client.check(assignA)
        _ = try await device.client.apply(assignA)
        _ = try await device.client.apply(.assign(id: b, bauakteId: second, title: "Dispo B", crewId: crewId, day: day, activity: "Erstbegehung",
                                                   startTime: "12:00:00", endTime: nil))
        var plan = try await device.client.dayPlan(day)
        var crew = try #require(plan.crew(crewId))
        #expect(crew.stops.map(\.assignmentId) == [a, b] && crew.stops.first?.timeText == "07:00–11:00")
        #expect(plan.canPlan && crew.isMine)

        // Reihenfolge tauschen, Uhrzeiten bleiben in der Reihenfolge.
        let ordered = try await device.client.apply(.order(crewId: crewId, day: day, assignmentIds: [b, a], startTimes: ["07:00:00", "12:00:00"]))
        crew = try #require(ordered.plan?.crew(crewId))
        #expect(crew.stops.map(\.assignmentId) == [b, a] && crew.stops.first?.startText == "07:00")

        _ = try await device.client.apply(.move(id: a, title: "Dispo A", day: next, crewId: nil, startTime: "08:00:00", endTime: nil, setTime: true))
        plan = try await device.client.dayPlan(next)
        #expect(plan.crew(crewId)?.stops.first { $0.assignmentId == a }?.startText == "08:00")

        _ = try await device.client.apply(.remove(id: a, title: "Dispo A"))
        _ = try await device.client.apply(.remove(id: b, title: "Dispo B"))
        #expect(try await device.client.dayPlan(next).crew(crewId)?.stops.isEmpty ?? true)
        #expect(try await device.client.dayPlan(day).crew(crewId)?.stops.isEmpty ?? true)
    }

    @Test("Chat über den echten Server: Gruppe, Warteschlange mit PDF, Zitat, Erwähnung, Reaktion, Bearbeiten, Anheften, Suche, Medien, Stumm")
    func chatLive() async throws {
        let device = try await syncingAccount()
        let client = device.client
        // Modul einschalten (Administrator, Standard ist aus).
        let settings = try JSONSerialization.data(withJSONObject: ["enabled": true, "retentionDays": 90, "showPreview": true, "editWindowMinutes": 15])
        let _: NoContent = try await client.send(APIRequest(method: "PUT", path: "chat/admin/settings", body: settings, contentType: "application/json"))
        let status = try await client.chatStatus()
        #expect(status.isAvailable && status.canAdmin && status.editWindowMinutes == 15)
        try await client.acknowledgeChatPrivacy(version: status.privacyNoticeVersion)
        #expect(try await client.chatStatus().privacyAccepted)

        // Zweite Person (Büro, mit Chat-Recht) – eine Gruppe braucht mindestens eine weitere Person.
        let suffix = UUID().uuidString.prefix(6).lowercased()
        let userBody = try JSONSerialization.data(withJSONObject: [
            "displayName": "Chat Kollege \(suffix)", "userName": "chat.\(suffix)", "roleIds": ["01a00000-0000-7000-8000-000000000002"],
        ])
        let colleague: LiveCreatedUser = try await client.send(APIRequest(method: "POST", path: "users", body: userBody, contentType: "application/json"))
        let group = try await client.createChatGroup(ChatGroupChange(title: "CI Chat \(suffix)", description: "Integrationstest",
                                                                     memberIds: [colleague.user.id], announcementOnly: false))
        #expect(group.chatKind == .group && group.canWrite)
        let detail = try await client.chatConversation(group.id)
        #expect(detail.canManage && detail.description == "Integrationstest" && detail.isAdmin(device.userId))

        // Warteschlange: PDF hochladen, dann senden; doppeltes Senden derselben clientMessageId bleibt eine Nachricht.
        let outbox = ChatOutbox(store: device.store, files: device.files)
        let pdf = Data("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n".utf8)
        let document = ChatOutboxItem.Attachment(id: UUID.v7(), kind: "document", name: "Aufmass.pdf", contentType: "application/pdf",
                                                 sizeBytes: Int64(pdf.count))
        let item = ChatOutboxItem(conversationId: group.id, text: "Aufmaß anbei", attachments: [document])
        try await outbox.enqueue(item, contents: [document.id: pdf])
        let flushed = await outbox.flush(client: client)
        let failure = await outbox.items().first?.failure ?? (flushed.offline ? "offline" : "")
        let first = try #require(flushed.sent.first, "nicht gesendet: \(failure)")
        #expect(first.attachments.count == 1 && first.attachments.first?.isPhoto == false && first.clientMessageId == item.id)
        let again = try await client.sendChatMessage(item.body, to: group.id)
        #expect(again.id == first.id)

        let content = try await client.chatAttachmentContent(try #require(first.attachments.first).id)
        #expect(content.starts(with: Data("%PDF".utf8)))

        // Antwort mit Zitat und Erwähnung, Standort.
        let reply = try await client.sendChatMessage(ChatSendBody(text: "@Ich selbst passt", clientMessageId: UUID.v7(), replyToId: first.id,
                                                                  mentionUserIds: [device.userId],
                                                                  location: ChatLocation(latitude: 48.37, longitude: 10.89, label: "Baustelle")), to: group.id)
        #expect(reply.replyTo?.id == first.id && reply.location?.label == "Baustelle")

        let reacted = try await client.reactToChatMessage(first.id, emoji: "👍")
        #expect(reacted.reactions.first { $0.emoji == "👍" }?.mine == true)
        let edited = try await client.editChatMessage(first.id, text: "Aufmaß anbei (Halle 2)")
        #expect(edited.text == "Aufmaß anbei (Halle 2)" && edited.editedAt != nil)
        let pinned = try await client.pinChatMessage(first.id, pinned: true)
        #expect(pinned.pinned)
        #expect(try await client.chatConversation(group.id).pinned.contains { $0.id == first.id })

        let page = try await client.chatPage(group.id, limit: 50)
        #expect(page.items.count >= 2 && page.items.last?.id == reply.id)
        let hits = try await client.searchChat("Halle 2", conversationId: group.id)
        #expect(hits.contains { $0.message.id == first.id })
        let media = try await client.chatMedia(group.id, kind: "document")
        #expect(media.contains { $0.messageId == first.id })

        try await client.muteChat(group.id, muted: true, until: Date().addingTimeInterval(3600))
        #expect(try await client.chatConversations().first { $0.id == group.id }?.isMuted() == true)
        try await client.muteChat(group.id, muted: false)
        try await client.markChatRead(group.id)
        try await client.deleteChatMessage(reply.id)
        #expect(try await client.chatMessage(reply.id).deleted)
        let request = try await client.streamRequest("chat/events")
        #expect(request.value(forHTTPHeaderField: "Accept") == "text/event-stream")
    }
}

private struct LiveCreatedUserWithPassword: Decodable, Sendable {
    let oneTimePassword: String
}

private struct LiveTwoFactorSetup: Decodable, Sendable {
    let secret: String
}

private struct LiveRecoveryCodes: Decodable, Sendable {
    let recoveryCodes: [String]
}

/// TOTP nach RFC 6238 (HMAC-SHA1, 6 Ziffern, 30 s) – wie eine Authenticator-App, nur für den Test.
private enum LiveTotp {
    static func step(_ date: Date) -> UInt64 { UInt64(date.timeIntervalSince1970) / 30 }

    static func code(_ secret: Data, step: UInt64) -> String {
        var counter = step.bigEndian
        let message = withUnsafeBytes(of: &counter) { Data($0) }
        let hash = Array(HMAC<Insecure.SHA1>.authenticationCode(for: message, using: SymmetricKey(data: secret)))
        let offset = Int(hash[hash.count - 1] & 0x0F)
        let binary = (UInt32(hash[offset] & 0x7F) << 24) | (UInt32(hash[offset + 1]) << 16) | (UInt32(hash[offset + 2]) << 8) | UInt32(hash[offset + 3])
        return String(format: "%06u", binary % 1_000_000)
    }

    static func base32Decode(_ text: String) -> Data? {
        let alphabet = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567")
        var buffer: UInt32 = 0
        var bits = 0
        var bytes: [UInt8] = []
        for character in text.uppercased() where character != "=" && character != " " {
            guard let value = alphabet.firstIndex(of: character) else { return nil }
            buffer = (buffer << 5) | UInt32(value)
            bits += 5
            if bits >= 8 {
                bits -= 8
                bytes.append(UInt8((buffer >> UInt32(bits)) & 0xFF))
            }
        }
        return Data(bytes)
    }
}

private struct LiveCreatedUser: Decodable, Sendable {
    struct User: Decodable, Sendable {
        let id: UUID
    }

    let user: User
}
