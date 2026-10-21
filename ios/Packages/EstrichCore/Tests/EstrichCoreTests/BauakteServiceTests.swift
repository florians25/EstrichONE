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

    @Test("Bauvorhaben: Werte und Zeitraum bei der Anlage, Änderung als ganzes project-Objekt, Kurzangaben für den Kopf")
    func projectValues() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        var project = BauakteProject()
        project.totalArea = 182.5
        project.screedType = .ct
        project.nominalThicknessMm = 50
        project.construction = .heated
        project.strengthClass = "  C25-F4 "
        project.waterOnSite = true

        await #expect(throws: BauakteError.invalidPeriod) {
            try await service.create(NewBauakte(title: "EFH", plannedStart: "2026-11-10", plannedEnd: "2026-11-01"))
        }
        let id = try await service.create(NewBauakte(title: "EFH", project: project, plannedStart: "2026-11-02", plannedEnd: "2026-11-06"))
        let local = try #require(try await service.bauakte(id))
        #expect(local.project.totalArea == 182.5 && local.project.strengthClass == "C25-F4" && local.plannedEnd == "2026-11-06")
        #expect(local.project.badges == ["182,5 m²", "CT 50 mm", "Heizestrich"])

        _ = await engine.synchronize()
        #expect(server.entity(id)?["project"]?["screedType"]?.string == "ct")
        #expect(server.entity(id)?["project"]?["powerOnSite"] == .null)
        #expect(server.entity(id)?["plannedStart"]?.string == "2026-11-02")

        project.priority = .urgent
        project.totalArea = nil
        try await service.updateProject(project, of: id)
        #expect(try await service.bauakte(id)?.project.priority == .urgent)
        _ = await engine.synchronize()
        #expect(server.entity(id)?["project"]?["priority"]?.string == "urgent")
        #expect(server.entity(id)?["project"]?["totalArea"] == .null)
        #expect(BauakteProject(.null).isEmpty)
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

    @Test("Fotos mit Beschriftung: vor der Übertragung in der Ankündigung, danach als file.update")
    func photoCaptions() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Beschriftung"))
        let first = try await service.addPhoto(Data([0xFF, 0xD8, 1, 2, 3]), to: bauakte, capturedAt: Date(), caption: "  Bad OG ")
        let second = try await service.addPhoto(Data([0xFF, 0xD8, 4, 5, 6]), to: bauakte, capturedAt: Date(), caption: "   ")
        try await service.setCaption("Flur EG", of: second)
        #expect(try await service.files(of: bauakte).first { $0.id == first }?.caption == "Bad OG")

        #expect(await engine.synchronize() == .completed)
        #expect(server.entity(first)?["caption"]?.string == "Bad OG")
        #expect(server.entity(second)?["caption"]?.string == "Flur EG")
        #expect(try await service.store.operations().isEmpty, "Anlage und Beschriftung sind eine Ankündigung")

        try await service.setCaption("Flur EG, Randdämmung", of: second)
        #expect(try await service.store.operations().map(\.type) == ["file.update"])
        #expect(await engine.synchronize() == .completed)
        #expect(server.entity(second)?["caption"]?.string == "Flur EG, Randdämmung")
        #expect(try await service.files(of: bauakte).first { $0.id == second }?.syncState == .synced)
    }

    @Test("Video: offline gespeichert, als video angekündigt und in Teilen hochgeladen")
    func video() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Video"))
        let movie = Data([0, 0, 0, 0x18] + Array("ftypmp42".utf8) + (0..<(SyncEngine.chunkSize + 1000)).map { UInt8($0 % 251) })

        let id = try await service.addVideo(movie, to: bauakte, capturedAt: Date(), caption: "Estrich wird eingebracht")

        let local = try #require(try await service.files(of: bauakte).first)
        #expect(local.isVideo && !local.isPhoto && local.originalName.hasSuffix(".mp4") && local.caption == "Estrich wird eingebracht")
        #expect(await engine.synchronize() == .completed)
        #expect(server.uploaded(id) == movie, "zwei Teile, vollständig")
        #expect(server.entity(id)?["kind"]?.string == "video")
        #expect(server.entity(id)?["contentType"]?.string == "video/mp4")
    }

    @Test("Unübertragene Aufnahme verwerfen; übertragene bleibt")
    func discardUnsent() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Verwerfen"))
        let blurry = try await service.addPhoto(Data([0xFF, 0xD8, 9]), to: bauakte, capturedAt: Date())
        #expect(try await service.discardUnsentFile(blurry))
        #expect(try await service.files(of: bauakte).isEmpty)
        #expect(service.localContent(of: blurry) == nil)
        #expect(try await service.store.uploads().isEmpty)

        let kept = try await service.addPhoto(Data([0xFF, 0xD8, 8]), to: bauakte, capturedAt: Date())
        #expect(await engine.synchronize() == .completed)
        #expect(try await service.discardUnsentFile(kept) == false)
        #expect(try await service.files(of: bauakte).map(\.id) == [kept])
    }

    @Test("Scan als PDF: Titel, Dokumentart, offline gespeichert und wie Fotos hochgeladen")
    func scannedDocument() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Dokumente"))
        let pdf = Data("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n".utf8)

        await #expect(throws: BauakteError.titleRequired) {
            try await service.addDocument(pdf, title: "  ", category: .delivery, to: bauakte)
        }
        let id = try await service.addDocument(pdf, title: "Lieferschein 4711: CT-C25/F4", category: .delivery, to: bauakte)
        _ = try await service.addPhoto(Data([0xFF, 0xD8, 1]), to: bauakte, capturedAt: Date())

        let documents = try await service.documents(of: bauakte)
        #expect(documents.map(\.id) == [id])
        let document = try #require(documents.first)
        #expect(document.isDocument && document.isPDF)
        #expect(document.category == .delivery)
        #expect(document.displayName == "Lieferschein 4711: CT-C25/F4")
        #expect(document.originalName == "Lieferschein 4711_ CT-C25_F4.pdf")
        #expect(document.syncState == .pending)

        #expect(await engine.synchronize() == .completed)
        #expect(server.uploaded(id) == pdf)
        #expect(server.entity(id)?["kind"]?.string == "document")
        #expect(server.entity(id)?["category"]?.string == "delivery")
        #expect(try await service.documents(of: bauakte).first?.syncState == .synced)

        try await service.setCategory(.acceptance, of: id)
        #expect(await engine.synchronize() == .completed)
        #expect(server.entity(id)?["category"]?.string == "acceptance")
    }

    @Test("Wartung des Servers: Synchronisation pausiert, nichts geht verloren, danach wird alles übertragen")
    func maintenance() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Wartung"))
        server.inMaintenance = true

        #expect(await engine.synchronize() == .maintenance(message: "Wartung bis 22 Uhr"))
        #expect(try await service.store.summary().pendingOperations == 1)
        #expect(try await service.store.summary().hasProblems == false)

        server.inMaintenance = false
        #expect(await engine.synchronize() == .completed)
        #expect(server.entity(bauakte) != nil)
    }

    @Test("Systemhinweise: Zeitfenster, unbekannte Stufe, Wartungsankündigung")
    func systemNotices() throws {
        let json = """
        {"announcements":[
           {"id":"a1","title":"Neue Version","text":"Ab Montag","severity":"info","startsAt":null,"endsAt":null},
           {"id":"a2","title":"Alt","text":"vorbei","severity":"warning","startsAt":null,"endsAt":"2026-01-01T00:00:00+00:00"},
           {"id":"a3","title":"Seltsam","text":"x","severity":"purple","startsAt":"2026-10-01T00:00:00Z","endsAt":null}],
         "maintenance":{"active":false,"message":"Update","startsAt":"2026-10-25T20:00:00+00:00","endsAt":"2026-10-25T22:00:00+00:00","adminsMayWork":true},
         "serverTime":"2026-10-20T10:00:00+00:00"}
        """
        let notices = try JSONCoding.decoder.decode(SystemNotices.self, from: Data(json.utf8))
        let now = try #require(APIDate.parse("2026-10-20T10:00:00+00:00"))
        #expect(notices.currentAnnouncements(at: now).map(\.id) == ["a1", "a3"])
        #expect(notices.announcements[2].severity == .info)
        #expect(notices.upcomingMaintenance(at: now)?.message == "Update")
        let after = try #require(APIDate.parse("2026-10-26T00:00:00+00:00"))
        #expect(notices.upcomingMaintenance(at: after) == nil)
    }

    @Test("Lücke im Feed: neu sichtbare Bauakte wird samt Inhalt nachgeladen, entzogene verschwindet")
    func gapFilling() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let planned = UUID.v7()
        let note = UUID.v7()
        server.serverSideExisting("bauakte", id: planned, data: ["id": JSONValue(planned), "title": "Eingeplant", "number": "2026-077", "version": 3])
        server.serverSideExisting("note", id: note, data: ["id": JSONValue(note), "bauakteId": JSONValue(planned), "text": "Schlüssel beim Nachbarn"])

        #expect(await engine.synchronize(includeScope: true) == .completed)
        #expect(try await service.bauakte(planned)?.number == "2026-077")
        #expect(try await service.notes(of: planned).map(\.text) == ["Schlüssel beim Nachbarn"])

        server.visibleBauakten = []
        #expect(await engine.synchronize(includeScope: true) == .completed)
        #expect(try await service.bauakte(planned) == nil)
        #expect(await engine.load(bauakte: planned) == .completed, "nicht sichtbar: nichts zu laden, kein Fehler")
        #expect(try await service.bauakte(planned) == nil)
    }

    @Test("Abgelehnte Operation nach Klärung erneut senden; fehlgeschlagene Uploads erneut versuchen")
    func retries() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Wiederholen"))
        server.rejectNext = ("BAUAKTE_NOT_FOUND", "Die Bauakte ist nicht sichtbar.")
        #expect(await engine.synchronize() == .completed)
        let rejected = try #require(try await service.store.operations().first)
        #expect(rejected.state == .rejected)
        #expect(try await service.bauakte(bauakte)?.syncState == .problem)

        try await service.store.retryRejected(rejected)
        #expect(await engine.synchronize() == .completed)
        #expect(try await service.store.operations().isEmpty)
        #expect(server.entity(bauakte) != nil)
        #expect(try await service.store.retryFailedUploads() == 0)
    }

    @Test("Status ändern und stornieren: sofort lokal, Begründung Pflicht, Storno vom Server gewinnt")
    func statusAndCancellation() async throws {
        let server = SimulatedSyncServer()
        let (service, engine, _, _) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Status"))
        try await service.changeStatus(.inProgress, of: bauakte)
        #expect(try await service.bauakte(bauakte)?.status == .inProgress, "auch vor der ersten Übertragung")
        #expect(await engine.synchronize() == .completed)
        #expect(server.entity(bauakte)?["status"]?.string == "inProgress")

        await #expect(throws: BauakteError.cancellationReasonRequired) { try await service.changeStatus(.cancelled, reason: "  ", of: bauakte) }
        try await service.changeStatus(.cancelled, reason: "Kunde hat abgesagt", of: bauakte)
        let cancelled = try #require(try await service.bauakte(bauakte))
        #expect(cancelled.status == .cancelled && cancelled.cancellationReason == "Kunde hat abgesagt")
        #expect(try await service.store.operations().first?.payload["baseStatus"]?.string == "inProgress")
        #expect(await engine.synchronize() == .completed)
        #expect(server.entity(bauakte)?["cancellationReason"]?.string == "Kunde hat abgesagt")

        // Gerät kennt „In Ausführung“, im Büro wurde inzwischen storniert: der Wechsel wird abgelehnt, „Storniert“ bleibt.
        let other = try await service.create(NewBauakte(title: "Andere"))
        try await service.changeStatus(.inProgress, of: other)
        #expect(await engine.synchronize() == .completed)
        server.serverSideChange("bauakte", id: other, data: (server.entity(other) ?? [:]).merging(["status": "cancelled", "cancellationReason": "Büro"]))
        try await service.changeStatus(.completed, of: other)
        #expect(await engine.synchronize() == .completed)
        #expect(try await service.store.operations().first?.errorCode == "ALREADY_CANCELLED")
        #expect(try await service.bauakte(other)?.status == .cancelled)
    }

    @Test("Dateinamen: Endung und Anzeigename")
    func fileNames() {
        let pdf = FileItem.splitName("Aufmaß EG.pdf")
        #expect(pdf.base == "Aufmaß EG" && pdf.ext == "pdf")
        let plain = FileItem.splitName("ohne")
        #expect(plain.base == "ohne" && plain.ext.isEmpty)
        let hidden = FileItem.splitName(".hidden")
        #expect(hidden.base == ".hidden" && hidden.ext.isEmpty)
        #expect(BauakteService.fileName(for: "a/b\\c") == "a_b_c")
        #expect(BauakteService.fileName(for: "  ") == "Dokument")
        #expect(FileItem.fileExtension(forContentType: "image/webp") == "webp")
        #expect(FileItem.fileExtension(forContentType: "image/HEIF") == "heic")
        #expect(FileItem.fileExtension(forContentType: "application/pdf; charset=binary") == "pdf")
        #expect(FileItem.fileExtension(forContentType: "video/quicktime") == "mov")
        #expect(FileItem.fileExtension(forContentType: "image/x-unknown") == "jpg")
        #expect(FileItem.fileExtension(forContentType: "application/octet-stream") == "bin")
    }
}

@Suite("Kunden in der Schnellanlage")
struct CustomerTests {
    @Test("Suche über Name und Kundennummer; zuletzt gewählte Kunden zuerst")
    func customers() async throws {
        let server = SimulatedSyncServer()
        let directory = FileManager.default.temporaryDirectory.appending(path: "kunden-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        let service = BauakteService(store: store, files: files, userId: UUID.v7())
        let huber = UUID.v7()
        let bau = UUID.v7()
        server.serverSideChange("contact", id: huber, data: ["id": JSONValue(huber), "name": "Fa. Huber", "customerNumber": "K-1042", "city": "Landshut"])
        server.serverSideChange("contact", id: bau, data: ["id": JSONValue(bau), "name": "Bau GmbH", "customerNumber": "K-7", "city": "Ergolding"])
        _ = await SyncEngine(store: store, transport: server, files: files).synchronize()

        #expect(try await service.contacts().map(\.name) == ["Bau GmbH", "Fa. Huber"])
        #expect(try await service.contacts(search: "k-1042").map(\.id) == [huber])
        _ = try await service.create(NewBauakte(title: "Halle", customerId: huber))
        #expect(try await service.recentCustomers().map(\.id) == [huber])
    }

    private func makeService(_ server: SimulatedSyncServer) throws -> (BauakteService, SyncEngine) {
        let directory = FileManager.default.temporaryDirectory.appending(path: "kunden-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        return (BauakteService(store: store, files: files, userId: UUID.v7()), SyncEngine(store: store, transport: server, files: files))
    }

    @Test("Neuer Kunde bei der Anlage: offline erfasst, vor der Bauakte übertragen und verknüpft")
    func newCustomerWithBauakte() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server)
        let customer = NewCustomer(name: " Familie Schneider ", phone: "0871 123456", email: "schneider@example.com",
                                   street: "Ahornweg 3", postalCode: "84028", city: "Landshut")

        let bauakte = try await service.create(NewBauakte(title: "EFH Schneider"), newCustomer: customer)

        let local = try #require(try await service.bauakte(bauakte))
        let customerId = try #require(local.customerId)
        let contact = try #require(try await service.contact(customerId))
        #expect(contact.name == "Familie Schneider")
        #expect(contact.phone == "0871 123456")
        #expect(contact.addressLine == "Ahornweg 3, 84028 Landshut")
        #expect(contact.syncState == .pending)
        #expect(try await service.recentCustomers().map(\.id) == [customerId])
        #expect(try await service.store.operations().map(\.type) == ["contact.create", "bauakte.create"])

        #expect(await engine.synchronize() == .completed)
        #expect(server.entity(customerId)?["name"]?.string == "Familie Schneider")
        #expect(server.entity(customerId)?["kind"]?.string == "person")
        #expect(server.entity(bauakte)?["customerId"]?.string == customerId.uuidString.lowercased())
        #expect(try await service.contact(customerId)?.syncState == .synced)
    }

    @Test("Ungültige Kundendaten werden vor dem Speichern abgelehnt; die Bauakte entsteht dann nicht")
    func invalidCustomer() async throws {
        let (service, _) = try makeService(SimulatedSyncServer())
        await #expect(throws: BauakteError.customerNameRequired) {
            try await service.create(NewBauakte(title: "Halle"), newCustomer: NewCustomer(name: "  "))
        }
        await #expect(throws: BauakteError.invalidEmail) {
            try await service.create(NewBauakte(title: "Halle"), newCustomer: NewCustomer(name: "Maier", email: "maier.de"))
        }
        await #expect(throws: BauakteError.titleRequired) {
            try await service.create(NewBauakte(title: ""), newCustomer: NewCustomer(name: "Maier"))
        }
        #expect(try await service.bauakten().isEmpty)
        #expect(try await service.contacts().isEmpty)
    }

    @Test("Dublettenhinweis: gleicher Name, Telefonnummer oder E-Mail")
    func similarCustomers() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server)
        let huber = UUID.v7()
        server.serverSideChange("contact", id: huber, data: ["id": JSONValue(huber), "name": "Fa. Huber Bau", "phone": "+49 871 4711", "email": "info@huber.example"])
        _ = await engine.synchronize()

        #expect(try await service.similarCustomers(to: NewCustomer(name: "huber  bau")).map(\.id) == [huber])
        #expect(try await service.similarCustomers(to: NewCustomer(name: "X", phone: "0871 4711")).isEmpty, "zu kurz für einen Vergleich")
        #expect(try await service.similarCustomers(to: NewCustomer(name: "Maier", phone: "871-4711")).map(\.id) == [huber])
        #expect(try await service.similarCustomers(to: NewCustomer(name: "Maier", email: "INFO@huber.example")).map(\.id) == [huber])
        #expect(try await service.similarCustomers(to: NewCustomer(name: "Ma")).isEmpty)
    }

    @Test("Kunde nachträglich zuordnen")
    func assignLater() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server)
        let bauakte = try await service.create(NewBauakte(title: "Ohne Kunde"))
        #expect(await engine.synchronize() == .completed)
        let customer = try await service.createCustomer(NewCustomer(name: "Bau GmbH", isCompany: true))
        try await service.assignCustomer(customer, to: bauakte)
        #expect(try await service.store.operations().map(\.type) == ["contact.create", "bauakte.update"])
        #expect(await engine.synchronize() == .completed)
        #expect(server.entity(bauakte)?["customerId"]?.string == customer.uuidString.lowercased())
        #expect(server.entity(customer)?["kind"]?.string == "company")
    }
}
