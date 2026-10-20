import Foundation

public enum DiaryWeather: String, CaseIterable, Identifiable, Sendable {
    case sunny, cloudy, rain, snow, frost, wind

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .sunny: "Sonnig"
        case .cloudy: "Bewölkt"
        case .rain: "Regen"
        case .snow: "Schnee"
        case .frost: "Frost"
        case .wind: "Wind"
        }
    }

    public var symbol: String {
        switch self {
        case .sunny: "sun.max"
        case .cloudy: "cloud"
        case .rain: "cloud.rain"
        case .snow: "cloud.snow"
        case .frost: "thermometer.snowflake"
        case .wind: "wind"
        }
    }
}

/// Bautagebuch-Eintrag (Server: `DiaryEntryDto`).
public struct DiaryEntry: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let authorUserId: UUID?
    /// Kalendertag (`yyyy-MM-dd`).
    public let day: String
    public let text: String
    public let weather: DiaryWeather?
    public let temperatureCelsius: Int?
    public let workersPresent: Int?
    public let createdAt: Date?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        bauakteId = data["bauakteId"]?.string.flatMap(UUID.init(uuidString:))
        authorUserId = data["authorUserId"]?.string.flatMap(UUID.init(uuidString:))
        day = data["day"]?.string ?? ""
        text = data["text"]?.string ?? ""
        weather = data["weather"]?.string.flatMap(DiaryWeather.init(rawValue:))
        temperatureCelsius = data["temperatureCelsius"]?.number.map { Int($0) }
        workersPresent = data["workersPresent"]?.number.map { Int($0) }
        createdAt = data["createdAt"]?.string.flatMap(APIDate.parse)
        syncState = entity.syncState
    }
}

public struct NewDiaryEntry: Sendable {
    public var day: String
    public var text: String
    public var weather: DiaryWeather?
    public var temperatureCelsius: Int?
    public var workersPresent: Int?

    public init(day: String, text: String, weather: DiaryWeather? = nil, temperatureCelsius: Int? = nil, workersPresent: Int? = nil) {
        self.day = day
        self.text = text
        self.weather = weather
        self.temperatureCelsius = temperatureCelsius
        self.workersPresent = workersPresent
    }
}

public enum InspectionKind: String, Sendable {
    case initialInspection
    case cmMeasurement

    public var title: String {
        switch self {
        case .initialInspection: "Erstbegehung"
        case .cmMeasurement: "CM-Messung"
        }
    }
}

public enum ScreedBinderKind: String, CaseIterable, Identifiable, Sendable {
    case cement
    case calciumSulfate

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .cement: "Zementestrich (CT)"
        case .calciumSulfate: "Calciumsulfatestrich (CA)"
        }
    }

    /// Grenzwert der Belegreife in CM-% – derselbe wie auf dem Server (`Inspection.LimitCmPercent`). Der Server
    /// bleibt maßgeblich; die App zeigt den Wert offline schon vor der Übertragung an.
    public func limit(heated: Bool) -> Double {
        switch (self, heated) {
        case (.cement, false): 2.0
        case (.cement, true): 1.8
        case (.calciumSulfate, false): 0.5
        case (.calciumSulfate, true): 0.3
        }
    }
}

public enum CheckResult: String, CaseIterable, Identifiable, Sendable {
    case ok
    case notOk
    case notApplicable

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .ok: "In Ordnung"
        case .notOk: "Mangel"
        case .notApplicable: "Entfällt"
        }
    }
}

/// Feste Punkte der Erstbegehung (wie `Inspection.InitialChecks` auf dem Server).
public enum InitialCheck: String, CaseIterable, Identifiable, Sendable {
    case substrate, edgeInsulation, heatingPressureTest, access, utilities

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .substrate: "Untergrund tragfähig, eben und sauber"
        case .edgeInsulation: "Randdämmstreifen vorhanden"
        case .heatingPressureTest: "Fußbodenheizung abgedrückt (Protokoll)"
        case .access: "Zufahrt und Pumpenstandort möglich"
        case .utilities: "Wasser und Strom vorhanden"
        }
    }
}

/// Prüfung (Server: `InspectionDto`). Nur anlegen; Korrektur = neue Prüfung mit Verweis.
public struct Inspection: Identifiable, Equatable, Sendable {
    public let id: UUID
    public let bauakteId: UUID?
    public let kind: InspectionKind
    public let performedByUserId: UUID?
    public let performedAt: Date?
    public let correctsInspectionId: UUID?
    public let remarks: String?
    public let notRequired: Bool
    public let checks: [InitialCheck: CheckResult]
    public let photoFileIds: [UUID]
    public let location: String?
    public let binder: ScreedBinderKind?
    public let heated: Bool?
    public let thicknessMm: Int?
    public let measuredCmPercent: Double?
    public let limitCmPercent: Double?
    public let isReadyForCovering: Bool?
    public let syncState: SyncState

    public init(_ entity: LocalEntity) {
        let data = entity.data
        id = entity.id
        bauakteId = data["bauakteId"]?.string.flatMap(UUID.init(uuidString:))
        kind = data["kind"]?.string.flatMap(InspectionKind.init(rawValue:)) ?? .initialInspection
        performedByUserId = data["performedByUserId"]?.string.flatMap(UUID.init(uuidString:))
        performedAt = data["performedAt"]?.string.flatMap(APIDate.parse)
        correctsInspectionId = data["correctsInspectionId"]?.string.flatMap(UUID.init(uuidString:))
        remarks = data["remarks"]?.string
        notRequired = data["notRequired"]?.bool ?? false
        var checks: [InitialCheck: CheckResult] = [:]
        for (key, value) in data["checks"]?.object ?? [:] {
            if let check = InitialCheck(rawValue: key), let result = value.string.flatMap(CheckResult.init(rawValue:)) {
                checks[check] = result
            }
        }
        self.checks = checks
        photoFileIds = (data["photoFileIds"]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) }
        location = data["location"]?.string
        binder = data["binder"]?.string.flatMap(ScreedBinderKind.init(rawValue:))
        heated = data["heated"]?.bool
        thicknessMm = data["thicknessMm"]?.number.map { Int($0) }
        measuredCmPercent = data["measuredCmPercent"]?.number
        // Vor der Übertragung fehlen die Serverwerte: lokal mit derselben Regel berechnen.
        let isHeated = data["heated"]?.bool ?? false
        let localLimit = data["binder"]?.string.flatMap(ScreedBinderKind.init(rawValue:)).map { $0.limit(heated: isHeated) }
        let limit = data["limitCmPercent"]?.number ?? localLimit
        limitCmPercent = limit
        if let ready = data["isReadyForCovering"]?.bool {
            isReadyForCovering = ready
        } else if let measured = data["measuredCmPercent"]?.number, let limit {
            isReadyForCovering = measured <= limit
        } else {
            isReadyForCovering = nil
        }
        syncState = entity.syncState
    }

    /// Erstbegehung mit mindestens einem Mangel.
    public var hasDefects: Bool { checks.values.contains(.notOk) }
}

public enum SiteRecordError: Error, Equatable, Sendable {
    case textRequired
    case locationRequired
    case reasonRequired
    case checksRequired
    case invalidMeasurement
    case storage

    public var message: String {
        switch self {
        case .textRequired: "Bitte beschreiben, was heute auf der Baustelle geschah."
        case .locationRequired: "Bitte den Messort (Raum) angeben."
        case .reasonRequired: "Bitte begründen, warum keine Erstbegehung notwendig ist."
        case .checksRequired: "Bitte mindestens einen Prüfpunkt bewerten."
        case .invalidMeasurement: "Der Messwert muss zwischen 0 und 20 CM-% liegen."
        case .storage: "Die Daten konnten auf diesem Gerät nicht gespeichert werden."
        }
    }
}
