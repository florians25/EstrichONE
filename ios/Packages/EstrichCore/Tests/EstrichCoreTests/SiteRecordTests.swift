import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Bautagebuch und Prüfungen", .serialized)
struct SiteRecordTests {
    private let userId = UUID.v7()

    private func makeService(server: SimulatedSyncServer) throws -> (BauakteService, SyncEngine) {
        let directory = FileManager.default.temporaryDirectory.appending(path: "site-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        return (BauakteService(store: store, files: files, userId: userId), SyncEngine(store: store, transport: server, files: files))
    }

    @Test("Bautagebuch: offline erfasst, neuester Tag zuerst, Operation diary.create")
    func diary() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "Halle"))

        await #expect(throws: SiteRecordError.textRequired) { try await service.addDiaryEntry(NewDiaryEntry(day: "2026-10-20", text: " "), to: bauakte) }
        let older = try await service.addDiaryEntry(NewDiaryEntry(day: "2026-10-19", text: "Randdämmung gestellt"), to: bauakte)
        let newer = try await service.addDiaryEntry(NewDiaryEntry(day: "2026-10-20", text: "Estrich eingebracht", weather: .rain,
                                                                  temperatureCelsius: 9, workersPresent: 3), to: bauakte)

        let entries = try await service.diary(of: bauakte)
        #expect(entries.map(\.id) == [newer, older])
        #expect(entries.first?.weather == .rain && entries.first?.temperatureCelsius == 9 && entries.first?.workersPresent == 3)
        #expect(try await service.store.operations().filter { $0.entityType == .diaryEntry }.map(\.type) == ["diary.create", "diary.create"])

        _ = await engine.synchronize()
        #expect(server.entity(newer)?["temperatureCelsius"]?.number == 9)
    }

    @Test("CM-Messung: Belegreife sofort lokal, Grenzwerte wie auf dem Server")
    func cmMeasurement() async throws {
        let (service, _) = try makeService(server: SimulatedSyncServer())
        let bauakte = try await service.create(NewBauakte(title: "EFH"))

        await #expect(throws: SiteRecordError.locationRequired) {
            try await service.addCmMeasurement(to: bauakte, location: " ", binder: .cement, heated: false, thicknessMm: 65, measuredCmPercent: 1.5)
        }
        await #expect(throws: SiteRecordError.invalidMeasurement) {
            try await service.addCmMeasurement(to: bauakte, location: "Bad", binder: .cement, heated: false, thicknessMm: 65, measuredCmPercent: 25)
        }
        try await service.addCmMeasurement(to: bauakte, location: "Bad", binder: .cement, heated: true, thicknessMm: 65, measuredCmPercent: 1.9)

        let measurement = try #require(try await service.inspections(of: bauakte).first)
        #expect(measurement.kind == .cmMeasurement)
        #expect(measurement.limitCmPercent == 1.8)
        #expect(measurement.isReadyForCovering == false)
        #expect(ScreedBinderKind.calciumSulfate.limit(heated: false) == 0.5)
    }

    @Test("Erstbegehung: „nicht notwendig“ nur mit Begründung; Prüfpunkte und Fotos werden gesendet")
    func initialInspection() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "EFH"))

        await #expect(throws: SiteRecordError.reasonRequired) {
            try await service.addInitialInspection(to: bauakte, notRequired: true, checks: [:], photoFileIds: [], remarks: nil)
        }
        await #expect(throws: SiteRecordError.checksRequired) {
            try await service.addInitialInspection(to: bauakte, notRequired: false, checks: [:], photoFileIds: [], remarks: nil)
        }
        let photo = UUID.v7()
        let id = try await service.addInitialInspection(to: bauakte, notRequired: false,
                                                        checks: [.substrate: .ok, .edgeInsulation: .notOk], photoFileIds: [photo],
                                                        remarks: "Randdämmung fehlt im Bad",
                                                        notes: "  Heizkreis Bad undicht.\nKunde informiert, Termin Installateur Mo.  ")
        let local = try #require(try await service.inspections(of: bauakte).first)
        #expect(local.hasDefects)
        #expect(local.notes == "Heizkreis Bad undicht.\nKunde informiert, Termin Installateur Mo.")
        #expect(local.photoFileIds == [photo])

        _ = await engine.synchronize()
        #expect(server.entity(id)?["checks"]?["edgeInsulation"]?.string == "notOk")
        #expect(server.entity(id)?["kind"]?.string == "initialInspection")
        #expect(server.entity(id)?["notes"]?.string?.contains("\n") == true)
    }

    @Test("Abnahme: Ergebnis Pflicht, Vorbehalt nur mit Mangel oder Notiz, Mängel als Aufgaben mit Frist vor dem Protokoll gesendet")
    func acceptance() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "EFH"))
        let png = Data([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) + Data(repeating: 0, count: 32)

        await #expect(throws: AcceptanceError.resultRequired) {
            try await service.addAcceptance(NewAcceptance(), to: bauakte)
        }
        await #expect(throws: AcceptanceError.reservationsRequired) {
            try await service.addAcceptance(NewAcceptance(result: .acceptedWithReservations, defects: [AcceptanceDefect(title: "  ")]), to: bauakte)
        }
        await #expect(throws: AcceptanceError.invalidSignature) {
            try await service.addAcceptance(NewAcceptance(result: .accepted, clientSignature: Data("kein png".utf8)), to: bauakte)
        }

        let id = try await service.addAcceptance(NewAcceptance(
            location: "Ahornweg 3", clientName: "Herr Schneider", contractorName: "Huber Estrich", scope: "Zementestrich EG und OG",
            result: .acceptedWithReservations, notes: "Restarbeiten Treppe", photoFileIds: [],
            defects: [AcceptanceDefect(title: "Fuge Bad nacharbeiten", dueDate: "2026-11-15")],
            clientSignature: png, contractorSignature: png), to: bauakte)
        let local = try #require(try await service.acceptances(of: bauakte).first)
        #expect(local.id == id && local.result == .acceptedWithReservations && local.isSigned)
        #expect(local.defectWorkItemIds.count == 1)
        let defect = try #require(try await service.workItems(of: bauakte).first)
        #expect(defect.kind == .defect && defect.dueDate == "2026-11-15" && local.defectWorkItemIds == [defect.id])

        _ = await engine.synchronize()
        let sent = try #require(server.entity(id))
        #expect(sent["result"]?.string == "acceptedWithReservations")
        #expect(sent["clientSignature"]?["png"]?.string == png.base64EncodedString())
        #expect(sent["defectWorkItemIds"]?.array?.first?.string == defect.id.key)
        #expect(server.entity(defect.id)?["kind"]?.string == "defect")
        #expect(try await service.acceptances(of: bauakte).first?.syncState == .synced)
    }

    @Test("Erstbegehung nach Schema: Pflichtfelder, Werte, Bemerkungen und Fotos je Prüfpunkt werden gesendet")
    func schemaInspection() async throws {
        let server = SimulatedSyncServer()
        let (service, engine) = try makeService(server: server)
        let bauakte = try await service.create(NewBauakte(title: "EFH"))
        let schema = FormSchema(key: "erstbegehung", version: 2, groups: [
            FormSchema.Group(key: "pruef", label: "Prüfpunkte", fields: [
                FormSchema.Field(key: "substrate", label: "Untergrund", type: .check, path: "substrate", required: true, photos: true),
                FormSchema.Field(key: "kellerfenster", label: "Kellerfenster dicht", type: .check, path: "kellerfenster"),
            ]),
            FormSchema.Group(key: "werte", label: "Messwerte", fields: [
                FormSchema.Field(key: "measuredArea", label: "Fläche", type: .number, path: "measuredArea", required: true, unit: "m²",
                                 target: "project.totalArea"),
            ]),
        ])
        await #expect(throws: SiteRecordError.fieldRequired("Untergrund")) {
            try await service.addSchemaInspection(to: bauakte, schema: schema, notRequired: false, checks: ["kellerfenster": .ok],
                                                  values: [:], checkRemarks: [:], checkPhotos: [:], photoFileIds: [], remarks: nil)
        }
        let photo = UUID.v7()
        let id = try await service.addSchemaInspection(
            to: bauakte, schema: schema, notRequired: false, checks: ["substrate": .notOk, "kellerfenster": .ok],
            values: ["measuredArea": .number(118.5)], checkRemarks: ["substrate": " Risse im Flur ", "kellerfenster": " "],
            checkPhotos: ["substrate": [photo]], photoFileIds: [], remarks: nil)
        let local = try #require(try await service.inspections(of: bauakte).first)
        #expect(local.hasDefects && local.checkResults["kellerfenster"] == .ok && local.schemaVersion == 2)
        #expect(local.checkRemarks == ["substrate": "Risse im Flur"] && local.checkPhotos["substrate"] == [photo])
        #expect(local.photoFileIds == [photo] && local.values["measuredArea"]?.number == 118.5)
        _ = await engine.synchronize()
        #expect(server.entity(id)?["values"]?["measuredArea"]?.number == 118.5)
        #expect(server.entity(id)?["checks"]?["kellerfenster"]?.string == "ok")
    }
}

