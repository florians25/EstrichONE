import EstrichCore
import Foundation

/// Tankbelege im Demomodus: Fahrzeuge wie in der Disposition, einige Belege in allen Ständen; neue Belege aus der App
/// (`fuelReceipt.create`) erscheinen mit Nummer in „Meine Tankbelege“.
final class DemoFuel {
    private static func id(_ number: Int) -> String { String(format: "0193a1b2-de00-7000-8000-00000000%04d", number) }

    private let vehicles: [(id: String, kind: String, name: String, identifier: String, tank: Double?, km: Int?)] = [
        (id(5003), "vehicle", "Pritsche", "LA-EO 7", 80, 48_000), (id(5002), "vehicle", "Sprinter", "LA-EO 12", 75, 61_420),
        (id(5001), "machine", "Estrichpumpe 1", "Mixokret M3", 60, nil),
    ]
    private var receipts: [[String: Any]]
    private var counter = 18

    init() {
        let calendar = Calendar.current
        func day(_ offset: Int) -> String { CalendarDay.string(calendar.date(byAdding: .day, value: -offset, to: Date()) ?? Date()) }
        receipts = [
            Self.receipt(9101, "TB-2026-0017", Self.id(5003), "Pritsche", "LA-EO 7", day(2), 47_380, 58.2, 98.88, "diesel", "fuelCard", "new", "Neu"),
            Self.receipt(9102, "TB-2026-0014", Self.id(5003), "Pritsche", "LA-EO 7", day(9), 46_790, 61.0, 104.92, "diesel", "private", "checked", "Geprüft",
                         consumption: 10.3),
            Self.receipt(9103, "TB-2026-0011", Self.id(5002), "Sprinter", "LA-EO 12", day(15), 61_420, 12.0, 11.88, "adBlue", "fuelCard", "settled", "Gebucht"),
            Self.receipt(9104, "TB-2026-0008", Self.id(5003), "Pritsche", "LA-EO 7", day(21), 46_200, 64.5, 107.07, "diesel", "private", "rejected",
                         "Abgelehnt", reason: "Betrag auf dem Foto nicht lesbar – bitte neu fotografieren."),
            Self.receipt(9105, "TB-2026-0005", Self.id(5003), "Pritsche", "LA-EO 7", day(28), 45_600, 59.1, 99.29, "diesel", "private", "settled", "Erstattet"),
        ]
    }

    private static func receipt(_ number: Int, _ text: String, _ vehicle: String, _ name: String, _ plate: String, _ date: String, _ km: Int,
                                _ liters: Double, _ amount: Double, _ fuel: String, _ payment: String, _ status: String, _ statusText: String,
                                consumption: Double? = nil, reason: String? = nil) -> [String: Any] {
        ["id": id(number), "number": text, "vehicleId": vehicle, "vehicleName": name, "vehicleIdentifier": plate, "date": date, "odometerKm": km,
         "liters": liters, "amountGross": amount, "fuelType": fuel, "paymentMethod": payment, "station": "Aral Landshut Nord", "status": status,
         "statusText": statusText, "rejectionReason": reason ?? NSNull(), "imageFileId": NSNull(), "consumptionPer100Km": consumption ?? NSNull(),
         "warnings": [] as [Any]]
    }

    /// `GET fuel-receipts/options`: heute mit der Pritsche eingeplant.
    func options() -> [String: Any] {
        ["vehicles": vehicles.map { v -> [String: Any] in
            ["id": v.id, "kind": v.kind, "name": v.name, "identifier": v.identifier, "tankCapacityLiters": v.tank ?? NSNull(),
             "lastOdometerKm": v.km ?? NSNull(), "lastFuelType": v.kind == "vehicle" ? "diesel" : NSNull()]
        }, "suggestedVehicleId": Self.id(5003), "today": CalendarDay.string(Date())]
    }

    func mine() -> [[String: Any]] { receipts }

    /// Neuer Beleg aus der App (Outbox); liefert den Stand wie der Server.
    func add(id: String, payload: [String: Any]) -> [String: Any] {
        counter += 1
        let vehicle = vehicles.first { $0.id == (payload["vehicleId"] as? String)?.lowercased() }
        var entity = payload
        entity["id"] = id
        entity["number"] = String(format: "TB-2026-%04d", counter)
        entity["vehicleName"] = vehicle?.name ?? "Fahrzeug"
        entity["vehicleIdentifier"] = vehicle?.identifier ?? NSNull()
        entity["status"] = "new"
        entity["statusText"] = "Neu"
        entity["rejectionReason"] = NSNull()
        entity["consumptionPer100Km"] = NSNull()
        entity["warnings"] = [] as [Any]
        receipts.insert(entity, at: 0)
        return entity
    }
}
