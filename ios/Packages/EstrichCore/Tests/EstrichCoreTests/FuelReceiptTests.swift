import Foundation
#if canImport(CryptoKit)
import CryptoKit
#else
import Crypto
#endif
import Testing
@testable import EstrichCore

@Suite("Tankbelege", .serialized)
struct FuelReceiptTests {
    private let userId = UUID.v7()
    private let sprinter = FuelVehicleOption(id: UUID(uuidString: "0193a1b2-0000-7000-8000-0000000f0001")!, name: "Sprinter 3", identifier: "LA-EB 300",
                                             tankCapacityLiters: 80, lastOdometerKm: 48_000, lastFuelType: .diesel)

    private func makeStore() throws -> (LocalStore, FileVault, SyncEngine, SimulatedSyncServer) {
        let directory = FileManager.default.temporaryDirectory.appending(path: "fuel-\(UUID().uuidString)")
        let key = SymmetricKey(size: .bits256)
        let store = LocalStore(database: try EncryptedDatabase(url: directory.appending(path: "data.sealed"), key: key, migrations: LocalStore.schema))
        let files = FileVault(directory: directory.appending(path: "files"), key: key)
        let server = SimulatedSyncServer()
        return (store, files, SyncEngine(store: store, transport: server, files: files), server)
    }

    @Test("Vorbelegung mit dem heutigen Dispo-Fahrzeug und dem zuletzt getankten Kraftstoff")
    func prefill() {
        let pump = FuelVehicleOption(id: UUID(), kind: "pump", name: "Mixokret 2", identifier: nil)
        let options = FuelOptions(vehicles: [pump, sprinter], suggestedVehicleId: sprinter.id, today: "2026-11-20")
        let draft = FuelReceiptDraft.prefilled(from: options)
        #expect(draft.vehicleId == sprinter.id)
        #expect(draft.fuelType == .diesel)
        #expect(draft.paymentMethod == .fuelCard)
        #expect(FuelReceiptDraft.prefilled(from: FuelOptions(vehicles: [pump, sprinter], suggestedVehicleId: nil, today: "")).vehicleId == nil)
        #expect(FuelReceiptDraft.prefilled(from: FuelOptions(vehicles: [sprinter], suggestedVehicleId: nil, today: "")).vehicleId == sprinter.id)
        #expect(FuelPaymentMethod.privatelyPaid.rawValue == "private")
    }

    @Test("Prüfung und Hinweise: Pflichtfelder, Zahlen mit Komma, km rückwärts, Tankgröße, Literpreis")
    func validation() {
        var draft = FuelReceiptDraft(vehicleId: nil, liters: "61,37", amount: "104,27")
        #expect(draft.validate() == .vehicleRequired)
        draft.vehicleId = sprinter.id
        #expect(draft.validate() == nil)
        #expect(draft.pricePerLiter == 1.699)
        draft.liters = ""
        #expect(draft.validate() == .litersRequired)
        draft.liters = "61.37"
        draft.amount = "0"
        #expect(draft.validate() == .amountRequired)
        draft.amount = "1.234,56"
        #expect(draft.amountValue == 1234.56)
        draft.date = Date().addingTimeInterval(3 * 86_400)
        #expect(draft.validate() == .invalidDate)
        draft = FuelReceiptDraft(vehicleId: sprinter.id, odometer: "47.900", liters: "95", amount: "20")
        #expect(draft.odometerValue == 47_900)
        let hints = draft.hints(for: sprinter)
        #expect(hints.count == 3)
        #expect(hints[0].contains("48.000 km"))
        #expect(hints[1].contains("80 l"))
        draft.fuelType = .adBlue
        #expect(draft.hints(for: sprinter).count == 1, "AdBlue: weder Tankgröße noch Literpreis")
    }

    @Test("Texterkennung: Betrag, Liter, Datum und Kraftstoff aus einer typischen Quittung")
    func textParser() throws {
        let calendar = Calendar(identifier: .gregorian)
        let today = try #require(calendar.date(from: DateComponents(year: 2026, month: 11, day: 20)))
        let lines = ["ARAL Tankstelle", "Landshut Nord", "Datum 18.11.2026 07:12", "Säule 4", "Diesel", "61,37 l x 1,699 EUR/l", "104,27 A",
                     "Summe EUR 104,27", "Kartenzahlung"]
        let suggestion = FuelReceiptTextParser.suggest(from: lines, today: today, calendar: calendar)
        #expect(suggestion.amount == 104.27)
        #expect(suggestion.liters == 61.37)
        #expect(suggestion.date == "2026-11-18")
        #expect(suggestion.fuelType == .diesel)

        let other = FuelReceiptTextParser.suggest(from: ["Super E10", "Menge 42,10 Liter", "Gesamt", "73,50", "01.01.2030"], today: today, calendar: calendar)
        #expect(other.fuelType == .petrol)
        #expect(other.liters == 42.10)
        #expect(other.amount == 73.50)
        #expect(other.date == nil, "Datum in der Zukunft wird nicht vorgeschlagen")
        #expect(FuelReceiptTextParser.suggest(from: ["Vielen Dank"], today: today, calendar: calendar).isEmpty)
    }

    @Test("Offline erfasst: Foto und Beleg in der Warteschlange, genau einmal gesendet")
    func offlineRecord() async throws {
        let (store, files, engine, server) = try makeStore()
        let service = FuelReceiptService(store: store, files: files, userId: userId)
        await #expect(throws: FuelReceiptError.vehicleRequired) {
            try await service.record(FuelReceiptDraft(liters: "10", amount: "17"), photo: nil, vehicle: nil)
        }
        let draft = FuelReceiptDraft(vehicleId: sprinter.id, odometer: "48 210", liters: "61,37", amount: "104,27", paymentMethod: .privatelyPaid,
                                     station: " Aral Landshut Nord ")
        let id = try await service.record(draft, photo: Data([0xFF, 0xD8, 0xFF, 0xE0]), vehicle: sprinter)
        #expect(try await store.uploads().count == 1)
        let local = try await service.localReceipts()
        #expect(local.map(\.id) == [id])
        #expect(local[0].vehicleName == "Sprinter 3")
        #expect(local[0].state == .pending)

        _ = await engine.synchronize()
        let sent = try #require(server.entity(id))
        #expect(sent["vehicleId"]?.string == sprinter.id.uuidString.lowercased())
        #expect(sent["odometerKm"]?.number == 48_210)
        #expect(sent["liters"]?.number == 61.37)
        #expect(sent["amountGross"]?.number == 104.27)
        #expect(sent["paymentMethod"]?.string == "private")
        #expect(sent["station"]?.string == "Aral Landshut Nord")
        #expect(sent["imageFileId"]?.string != nil)
        #expect(sent["source"]?.string == "ios")
        #expect(server.receivedOpIds.count == 2, "Ankündigung des Fotos und Beleg")
        #expect(try await store.operations().isEmpty)
        #expect(EntityType(operationType: "fuelReceipt.create") == .fuelReceipt)
    }

    @Test("Antwort des Servers wird gelesen (Status, Hinweise)")
    func decodeResponse() throws {
        let json = """
        [{"id":"0193a1b2-0000-7000-8000-0000000f1001","number":"TB-2026-0001","vehicleId":"0193a1b2-0000-7000-8000-0000000f0001",
          "vehicleName":"Sprinter 3","vehicleIdentifier":"LA-EB 300","userId":"0193a1b2-0000-7000-8000-00000000aaaa","userName":"Max Huber",
          "date":"2026-11-18","odometerKm":48210,"liters":61.37,"amountGross":104.27,"pricePerLiter":1.699,"fuelType":"diesel",
          "paymentMethod":"private","station":null,"notes":null,"imageFileId":null,"imageStored":false,"status":"rejected","statusText":"Abgelehnt",
          "rejectionReason":"Betrag unleserlich","checkedByName":"Sabine Wagner","checkedAt":"2026-11-19T08:00:00+00:00","settledByName":null,
          "settledAt":null,"distanceKm":610,"consumptionPer100Km":10.1,"warnings":[{"code":"missing_image","text":"Kein Belegfoto vorhanden."}],
          "source":"ios","canEdit":true,"createdAt":"2026-11-18T07:15:00+00:00","version":2}]
        """
        let receipts = try JSONCoding.decoder.decode([FuelReceipt].self, from: Data(json.utf8))
        #expect(receipts[0].status == .rejected)
        #expect(receipts[0].paymentMethod == .privatelyPaid)
        #expect(receipts[0].rejectionReason == "Betrag unleserlich")
        #expect(receipts[0].vehicleText == "Sprinter 3 (LA-EB 300)")
        #expect(receipts[0].warnings.first?.code == "missing_image")
    }
}
