import Foundation

/// Kraftstoff eines Tankbelegs (Werte wie auf dem Server).
public enum FuelType: String, Codable, CaseIterable, Identifiable, Sendable {
    case diesel, petrol, adBlue, other

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .diesel: "Diesel"
        case .petrol: "Benzin"
        case .adBlue: "AdBlue"
        case .other: "Sonstiges"
        }
    }
}

/// Wie bezahlt wurde; „privat ausgelegt“ wird nach der Prüfung erstattet.
public enum FuelPaymentMethod: String, Codable, CaseIterable, Identifiable, Sendable {
    case fuelCard, companyCard
    case privatelyPaid = "private"

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .fuelCard: "Tankkarte"
        case .companyCard: "Firmenkarte"
        case .privatelyPaid: "Privat ausgelegt"
        }
    }
}

public enum FuelReceiptStatus: String, Codable, CaseIterable, Sendable {
    case new, checked, settled, rejected
}

/// Hinweis des Servers zur Plausibilität (Kilometerstand, Verbrauch, Doppelbeleg, Tankgröße, Foto).
public struct FuelWarning: Codable, Equatable, Sendable {
    public let code: String
    public let text: String

    public init(code: String, text: String) {
        self.code = code
        self.text = text
    }
}

/// Eigener Tankbeleg, wie ihn der Server liefert (`GET fuel-receipts/mine`, Ergebnis von `fuelReceipt.create`).
public struct FuelReceipt: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let number: String
    public let vehicleId: UUID
    public let vehicleName: String
    public let vehicleIdentifier: String?
    public let date: String
    public let odometerKm: Int?
    public let liters: Double
    public let amountGross: Double
    public let fuelType: FuelType
    public let paymentMethod: FuelPaymentMethod
    public let station: String?
    public let status: FuelReceiptStatus
    public let statusText: String
    public let rejectionReason: String?
    public let imageFileId: UUID?
    public let consumptionPer100Km: Double?
    public let warnings: [FuelWarning]

    public init(id: UUID, number: String, vehicleId: UUID, vehicleName: String, vehicleIdentifier: String?, date: String, odometerKm: Int?,
                liters: Double, amountGross: Double, fuelType: FuelType, paymentMethod: FuelPaymentMethod, station: String?,
                status: FuelReceiptStatus, statusText: String, rejectionReason: String? = nil, imageFileId: UUID? = nil,
                consumptionPer100Km: Double? = nil, warnings: [FuelWarning] = []) {
        self.id = id
        self.number = number
        self.vehicleId = vehicleId
        self.vehicleName = vehicleName
        self.vehicleIdentifier = vehicleIdentifier
        self.date = date
        self.odometerKm = odometerKm
        self.liters = liters
        self.amountGross = amountGross
        self.fuelType = fuelType
        self.paymentMethod = paymentMethod
        self.station = station
        self.status = status
        self.statusText = statusText
        self.rejectionReason = rejectionReason
        self.imageFileId = imageFileId
        self.consumptionPer100Km = consumptionPer100Km
        self.warnings = warnings
    }

    public var vehicleText: String { vehicleIdentifier.map { "\(vehicleName) (\($0))" } ?? vehicleName }
}

/// Fahrzeug zur Auswahl (aus „Geräte & Fahrzeuge“) mit letztem bekannten Kilometerstand.
public struct FuelVehicleOption: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let kind: String
    public let name: String
    public let identifier: String?
    public let tankCapacityLiters: Double?
    public let lastOdometerKm: Int?
    public let lastFuelType: FuelType?

    public init(id: UUID, kind: String = "vehicle", name: String, identifier: String?, tankCapacityLiters: Double? = nil, lastOdometerKm: Int? = nil,
                lastFuelType: FuelType? = nil) {
        self.id = id
        self.kind = kind
        self.name = name
        self.identifier = identifier
        self.tankCapacityLiters = tankCapacityLiters
        self.lastOdometerKm = lastOdometerKm
        self.lastFuelType = lastFuelType
    }

    public var title: String { identifier.map { "\(name) (\($0))" } ?? name }
}

/// `GET fuel-receipts/options`: Fahrzeuge und das heutige Dispo-Fahrzeug (Vorbelegung).
public struct FuelOptions: Codable, Equatable, Sendable {
    public let vehicles: [FuelVehicleOption]
    public let suggestedVehicleId: UUID?
    public let today: String

    public init(vehicles: [FuelVehicleOption], suggestedVehicleId: UUID?, today: String) {
        self.vehicles = vehicles
        self.suggestedVehicleId = suggestedVehicleId
        self.today = today
    }
}

public enum FuelReceiptError: Error, Equatable, Sendable {
    case vehicleRequired
    case litersRequired
    case amountRequired
    case invalidDate
    case invalidOdometer
    case storage

    public var message: String {
        switch self {
        case .vehicleRequired: "Bitte das Fahrzeug wählen."
        case .litersRequired: "Bitte die getankte Menge in Litern angeben."
        case .amountRequired: "Bitte den Betrag brutto angeben."
        case .invalidDate: "Das Datum des Belegs liegt in der Zukunft."
        case .invalidOdometer: "Bitte einen gültigen Kilometerstand angeben."
        case .storage: "Der Beleg konnte auf diesem Gerät nicht gespeichert werden."
        }
    }
}

/// Eingaben in der App (Formular „Tankbeleg“). Zahlen als Text, wie getippt (Komma oder Punkt).
public struct FuelReceiptDraft: Equatable, Sendable {
    public var vehicleId: UUID?
    public var date: Date
    public var odometer: String
    public var liters: String
    public var amount: String
    public var fuelType: FuelType
    public var paymentMethod: FuelPaymentMethod
    public var station: String
    public var notes: String

    public init(vehicleId: UUID? = nil, date: Date = Date(), odometer: String = "", liters: String = "", amount: String = "",
                fuelType: FuelType = .diesel, paymentMethod: FuelPaymentMethod = .fuelCard, station: String = "", notes: String = "") {
        self.vehicleId = vehicleId
        self.date = date
        self.odometer = odometer
        self.liters = liters
        self.amount = amount
        self.fuelType = fuelType
        self.paymentMethod = paymentMethod
        self.station = station
        self.notes = notes
    }

    /// Vorbelegung: heutiges Dispo-Fahrzeug, sonst das einzige; Kraftstoff wie zuletzt bei diesem Fahrzeug.
    public static func prefilled(from options: FuelOptions?, now: Date = Date()) -> FuelReceiptDraft {
        var draft = FuelReceiptDraft(date: now)
        guard let options else { return draft }
        draft.vehicleId = options.suggestedVehicleId ?? (options.vehicles.count == 1 ? options.vehicles.first?.id : nil)
        if let vehicle = options.vehicles.first(where: { $0.id == draft.vehicleId }), let last = vehicle.lastFuelType {
            draft.fuelType = last
        }
        return draft
    }

    public var litersValue: Double? { Self.number(liters) }
    public var amountValue: Double? { Self.number(amount) }
    public var odometerValue: Int? {
        let digits = odometer.filter { !$0.isWhitespace && $0 != "." && $0 != "," }
        return digits.isEmpty ? nil : Int(digits)
    }

    /// Preis je Liter, sobald Menge und Betrag stimmen.
    public var pricePerLiter: Double? {
        guard let liters = litersValue, let amount = amountValue, liters > 0 else { return nil }
        return (amount / liters * 1000).rounded() / 1000
    }

    /// Erster Fehler oder `nil`.
    public func validate(now: Date = Date(), calendar: Calendar = .current) -> FuelReceiptError? {
        if vehicleId == nil { return .vehicleRequired }
        guard let liters = litersValue, liters > 0, liters <= 5000 else { return .litersRequired }
        guard let amount = amountValue, amount > 0, amount <= 50000 else { return .amountRequired }
        if calendar.startOfDay(for: date) > calendar.startOfDay(for: now) { return .invalidDate }
        if !odometer.trimmingCharacters(in: .whitespaces).isEmpty, odometerValue == nil || (odometerValue ?? 0) > 5_000_000 { return .invalidOdometer }
        return nil
    }

    /// Hinweise vor dem Speichern (blockieren nicht): Kilometerstand kleiner als zuletzt, mehr als der Tank fasst, Literpreis auffällig.
    public func hints(for vehicle: FuelVehicleOption?) -> [String] {
        var result: [String] = []
        if let km = odometerValue, let last = vehicle?.lastOdometerKm, km < last {
            result.append("Der Kilometerstand ist kleiner als zuletzt (\(Self.grouped(last)) km).")
        }
        if fuelType != .adBlue, let liters = litersValue, let tank = vehicle?.tankCapacityLiters, liters > tank {
            result.append("Mehr Liter, als in den Tank passen (\(Self.text(tank)) l).")
        }
        if fuelType != .adBlue, let price = pricePerLiter, price < 0.8 || price > 3.5 {
            result.append("Der Preis je Liter (\(Self.text(price)) €) wirkt ungewöhnlich – bitte Menge und Betrag prüfen.")
        }
        return result
    }

    /// Nutzdaten für `fuelReceipt.create` (Datum als Kalendertag, Zahlen gerundet).
    public func payload(imageFileId: UUID?, source: String, calendar: Calendar = .current) -> [String: JSONValue] {
        [
            "vehicleId": JSONValue(vehicleId), "date": .string(CalendarDay.string(date, calendar: calendar)),
            "odometerKm": odometerValue.map { .number(Double($0)) } ?? .null,
            "liters": JSONValue(litersValue.map { ($0 * 100).rounded() / 100 }), "amountGross": JSONValue(amountValue.map { ($0 * 100).rounded() / 100 }),
            "fuelType": .string(fuelType.rawValue), "paymentMethod": .string(paymentMethod.rawValue),
            "station": JSONValue(Self.clean(station)), "notes": JSONValue(Self.clean(notes)), "imageFileId": JSONValue(imageFileId),
            "source": .string(source),
        ]
    }

    /// „61,37“, „61.37“, „1.234,56“ → Zahl.
    public static func number(_ text: String) -> Double? {
        var value = text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "€", with: "").replacingOccurrences(of: " ", with: "")
        if value.isEmpty { return nil }
        if value.contains(","), value.contains(".") {
            value = value.replacingOccurrences(of: ".", with: "")
        }
        value = value.replacingOccurrences(of: ",", with: ".")
        return Double(value)
    }

    /// Deutsche Schreibweise mit höchstens drei Nachkommastellen („1,699“, „80“).
    static func text(_ value: Double) -> String {
        var text = String(format: "%.3f", value)
        while text.hasSuffix("0") { text.removeLast() }
        if text.hasSuffix(".") { text.removeLast() }
        return text.replacingOccurrences(of: ".", with: ",")
    }

    /// Tausenderpunkte („48.210“).
    static func grouped(_ value: Int) -> String {
        let digits = String(abs(value))
        var result = ""
        for (index, character) in digits.reversed().enumerated() {
            if index > 0, index % 3 == 0 { result.append(".") }
            result.append(character)
        }
        return (value < 0 ? "-" : "") + String(result.reversed())
    }

    private static func clean(_ text: String) -> String? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
}

/// Vorschläge aus der Texterkennung eines Belegfotos (Betrag, Liter, Datum, Kraftstoff). Nur Vorschläge – der Mensch bestätigt.
public struct FuelReceiptSuggestion: Equatable, Sendable {
    public var amount: Double?
    public var liters: Double?
    /// Kalendertag „JJJJ-MM-TT“.
    public var date: String?
    public var fuelType: FuelType?

    public init(amount: Double? = nil, liters: Double? = nil, date: String? = nil, fuelType: FuelType? = nil) {
        self.amount = amount
        self.liters = liters
        self.date = date
        self.fuelType = fuelType
    }

    public var isEmpty: Bool { amount == nil && liters == nil && date == nil && fuelType == nil }
}

/// Liest typische deutsche Tankquittungen: „Summe EUR 104,27“, „61,37 l“, „Datum 18.11.2026“, „Diesel“/„Super E10“/„AdBlue“.
public enum FuelReceiptTextParser {
    public static func suggest(from lines: [String], today: Date = Date(), calendar: Calendar = .current) -> FuelReceiptSuggestion {
        var suggestion = FuelReceiptSuggestion()
        let lowered = lines.map { $0.lowercased() }
        let all = lowered.joined(separator: "\n")

        if all.contains("adblue") {
            suggestion.fuelType = .adBlue
        } else if all.contains("diesel") {
            suggestion.fuelType = .diesel
        } else if ["super", "benzin", "e10", "e5", "v-power", "ultimate 102"].contains(where: { all.contains($0) }) {
            suggestion.fuelType = .petrol
        }

        // Liter: Zahl mit „l“, „ltr“ oder „liter“ dahinter.
        for line in lowered {
            if let value = firstNumber(in: line, pattern: #"(\d{1,4}[.,]\d{1,3})\s*(?:l|ltr|liter)\b"#), value > 0, value < 2000 {
                suggestion.liters = value
                break
            }
        }

        // Betrag: Zeile mit Summe/Gesamt/Betrag/Total/zu zahlen, sonst der größte Eurobetrag.
        let keywords = ["summe", "gesamt", "betrag", "total", "zu zahlen", "zahlbetrag", "bar", "ec-karte", "kartenzahlung"]
        let money = #"(\d{1,5}[.,]\d{2})(?!\d)"#
        for (index, line) in lowered.enumerated() where keywords.contains(where: { line.contains($0) }) {
            if let value = lastNumber(in: line, pattern: money), value > 0 {
                suggestion.amount = value
                break
            }
            // Betrag in der nächsten Zeile (Spalten der Quittung).
            if index + 1 < lowered.count, let value = lastNumber(in: lowered[index + 1], pattern: money), value > 0 {
                suggestion.amount = value
                break
            }
        }
        if suggestion.amount == nil {
            let euro = lowered.filter { $0.contains("eur") || $0.contains("€") }
            suggestion.amount = euro.compactMap { lastNumber(in: $0, pattern: money) }.filter { $0 < 5000 }.max()
        }
        if let amount = suggestion.amount, let liters = suggestion.liters, amount == liters {
            suggestion.liters = nil
        }

        // Datum: erstes gültiges Datum, nicht in der Zukunft und höchstens ein Jahr alt.
        let datePattern = #"(\d{1,2})\.(\d{1,2})\.(\d{2,4})"#
        if let regex = try? NSRegularExpression(pattern: datePattern) {
            outer: for line in lines {
                for match in regex.matches(in: line, range: NSRange(line.startIndex..., in: line)) {
                    let parts = (1...3).compactMap { Range(match.range(at: $0), in: line).flatMap { Int(line[$0]) } }
                    guard parts.count == 3 else { continue }
                    let year = parts[2] < 100 ? 2000 + parts[2] : parts[2]
                    guard let date = calendar.date(from: DateComponents(year: year, month: parts[1], day: parts[0])),
                          calendar.component(.day, from: date) == parts[0], date <= today,
                          date > calendar.date(byAdding: .year, value: -1, to: today) ?? today else { continue }
                    suggestion.date = CalendarDay.string(date, calendar: calendar)
                    break outer
                }
            }
        }
        return suggestion
    }

    private static func matches(in line: String, pattern: String) -> [Double] {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return [] }
        return regex.matches(in: line, range: NSRange(line.startIndex..., in: line)).compactMap { match in
            Range(match.range(at: 1), in: line).flatMap { FuelReceiptDraft.number(String(line[$0])) }
        }
    }

    private static func firstNumber(in line: String, pattern: String) -> Double? { matches(in: line, pattern: pattern).first }

    private static func lastNumber(in line: String, pattern: String) -> Double? { matches(in: line, pattern: pattern).last }
}

/// Tankbelege erfassen: lokal zuerst (Outbox `fuelReceipt.create`), das Belegfoto als eigene Datei ohne Bauakte über die
/// Upload-Warteschlange (wie Fotos). Die Belegnummer vergibt der Server.
public struct FuelReceiptService: Sendable {
    public let store: LocalStore
    public let files: FileVault
    public let userId: UUID
    private let now: @Sendable () -> Date

    public init(store: LocalStore, files: FileVault, userId: UUID, now: @escaping @Sendable () -> Date = Date.init) {
        self.store = store
        self.files = files
        self.userId = userId
        self.now = now
    }

    @discardableResult
    public func record(_ draft: FuelReceiptDraft, photo: Data?, vehicle: FuelVehicleOption?, source: String = "ios") async throws(FuelReceiptError) -> UUID {
        if let error = draft.validate(now: now()) { throw error }
        var imageId: UUID?
        if let photo {
            do {
                imageId = try await BauakteService(store: store, files: files, userId: userId, now: now)
                    .addPhoto(photo, to: nil, capturedAt: now(), caption: "Tankbeleg")
            } catch {
                throw .storage
            }
        }
        let id = UUID.v7(now: now())
        let payload = draft.payload(imageFileId: imageId, source: source)
        let local = JSONValue.object(payload).merging([
            "id": JSONValue(id), "number": .null, "status": .string(FuelReceiptStatus.new.rawValue), "statusText": "Neu",
            "vehicleName": JSONValue(vehicle?.name), "vehicleIdentifier": JSONValue(vehicle?.identifier), "createdAt": .string(APIDate.format(now())),
        ])
        do {
            try await store.create(.fuelReceipt, id: id, data: local, payload: payload)
        } catch {
            throw .storage
        }
        return id
    }

    /// Belege auf dem Gerät (noch nicht oder gerade übertragen), neueste zuerst.
    public func localReceipts() async throws -> [LocalFuelReceipt] {
        try await store.entities(.fuelReceipt).map { entity in
            LocalFuelReceipt(id: entity.id, number: entity.data["number"]?.string, date: entity.data["date"]?.string ?? "",
                             vehicleName: entity.data["vehicleName"]?.string ?? "Fahrzeug", liters: entity.data["liters"]?.number ?? 0,
                             amountGross: entity.data["amountGross"]?.number ?? 0, state: entity.syncState)
        }.sorted { $0.date > $1.date }
    }
}

/// Beleg auf dem Gerät (Anzeige „wird übertragen“).
public struct LocalFuelReceipt: Equatable, Identifiable, Sendable {
    public let id: UUID
    public let number: String?
    public let date: String
    public let vehicleName: String
    public let liters: Double
    public let amountGross: Double
    public let state: SyncState
}

/// Fahrzeuge für die Erfassung offline: zuletzt geladene Auswahl im verschlüsselten Speicher.
public struct FuelOptionsStore: Sendable {
    static let metaKey = "fuel_options"
    public let store: LocalStore

    public init(store: LocalStore) { self.store = store }

    public func cached() async -> FuelOptions? {
        guard let text = (try? await store.meta(Self.metaKey)) ?? nil, let data = text.data(using: .utf8) else { return nil }
        return try? JSONCoding.decoder.decode(FuelOptions.self, from: data)
    }

    /// Vom Server laden und merken; ohne Verbindung der letzte Stand.
    public func load(using client: AuthorizedClient?) async -> FuelOptions? {
        if let client, let options = try? await client.fuelOptions() {
            if let data = try? JSONCoding.encoder.encode(options), let text = String(data: data, encoding: .utf8) {
                try? await store.setMeta(Self.metaKey, text)
            }
            return options
        }
        return await cached()
    }
}

extension AuthorizedClient {
    public func fuelOptions() async throws(APIError) -> FuelOptions {
        try await get("fuel-receipts/options")
    }

    public func myFuelReceipts() async throws(APIError) -> [FuelReceipt] {
        try await get("fuel-receipts/mine")
    }
}
