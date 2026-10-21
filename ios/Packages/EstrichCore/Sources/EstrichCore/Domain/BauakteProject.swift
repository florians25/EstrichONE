import Foundation

/// Wichtige Werte des Bauvorhabens (`project` der Bauakte), schon bei der Anlage erfasst. Alles optional.
public struct BauakteProject: Equatable, Sendable {
    public enum ObjectType: String, CaseIterable, Identifiable, Sendable {
        case efh, mfh, commercial, industrial, `public`, renovation, other
        public var id: String { rawValue }
        public var title: String {
            switch self {
            case .efh: "Einfamilienhaus"
            case .mfh: "Mehrfamilienhaus"
            case .commercial: "Gewerbe"
            case .industrial: "Industrie"
            case .public: "Öffentlicher Bau"
            case .renovation: "Sanierung"
            case .other: "Sonstiges"
            }
        }
    }

    public enum ScreedType: String, CaseIterable, Identifiable, Sendable {
        case ct, caf, `as`, dry, other
        public var id: String { rawValue }
        public var title: String {
            switch self {
            case .ct: "Zementestrich CT"
            case .caf: "Calciumsulfat-Fließestrich CAF"
            case .as: "Gussasphalt AS"
            case .dry: "Trockenestrich"
            case .other: "Sonstiges"
            }
        }
        public var short: String {
            switch self {
            case .ct: "CT"
            case .caf: "CAF"
            case .as: "AS"
            case .dry: "Trocken"
            case .other: "Sonstiges"
            }
        }
    }

    public enum Construction: String, CaseIterable, Identifiable, Sendable {
        case bonded, separatingLayer, floating, heated
        public var id: String { rawValue }
        public var title: String {
            switch self {
            case .bonded: "Verbundestrich"
            case .separatingLayer: "Auf Trennlage"
            case .floating: "Schwimmend"
            case .heated: "Heizestrich"
            }
        }
    }

    public enum Priority: String, CaseIterable, Identifiable, Sendable {
        case low, normal, high, urgent
        public var id: String { rawValue }
        public var title: String {
            switch self {
            case .low: "Niedrig"
            case .normal: "Normal"
            case .high: "Hoch"
            case .urgent: "Dringend"
            }
        }
    }

    public var objectType: ObjectType?
    /// Gesamtfläche in m².
    public var totalArea: Double?
    public var screedType: ScreedType?
    public var construction: Construction?
    public var nominalThicknessMm: Int?
    /// z. B. „C25-F4“.
    public var strengthClass: String?
    public var underfloorHeating: Bool?
    public var heatingType: String?
    public var insulation: String?
    public var insulationThicknessMm: Int?
    /// z. B. „KG, EG, OG“.
    public var floors: String?
    public var priority: Priority?
    public var siteAccess: String?
    public var waterOnSite: Bool?
    public var powerOnSite: Bool?

    public init() {}

    public init(_ value: JSONValue?) {
        guard let value, value.object != nil else { return }
        func text(_ key: String) -> String? { value[key]?.string.flatMap { $0.isEmpty ? nil : $0 } }
        objectType = text("objectType").flatMap(ObjectType.init(rawValue:))
        totalArea = value["totalArea"]?.number
        screedType = text("screedType").flatMap(ScreedType.init(rawValue:))
        construction = text("construction").flatMap(Construction.init(rawValue:))
        nominalThicknessMm = value["nominalThicknessMm"]?.number.map { Int($0) }
        strengthClass = text("strengthClass")
        underfloorHeating = value["underfloorHeating"]?.bool
        heatingType = text("heatingType")
        insulation = text("insulation")
        insulationThicknessMm = value["insulationThicknessMm"]?.number.map { Int($0) }
        floors = text("floors")
        priority = text("priority").flatMap(Priority.init(rawValue:))
        siteAccess = text("siteAccess")
        waterOnSite = value["waterOnSite"]?.bool
        powerOnSite = value["powerOnSite"]?.bool
    }

    /// Für Sync-Payloads: alle Felder, leere Texte als `null`, auf die Längen des Servers gekürzt.
    public var json: JSONValue {
        func text(_ value: String?, _ max: Int) -> JSONValue {
            let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            return trimmed.isEmpty ? .null : .string(String(trimmed.prefix(max)))
        }
        return .object([
            "objectType": JSONValue(objectType?.rawValue), "totalArea": JSONValue(totalArea.map { ($0 * 100).rounded() / 100 }),
            "screedType": JSONValue(screedType?.rawValue), "construction": JSONValue(construction?.rawValue),
            "nominalThicknessMm": JSONValue(nominalThicknessMm.map(Double.init)), "strengthClass": text(strengthClass, 30),
            "underfloorHeating": underfloorHeating.map(JSONValue.bool) ?? .null, "heatingType": text(heatingType, 100),
            "insulation": text(insulation, 100), "insulationThicknessMm": JSONValue(insulationThicknessMm.map(Double.init)),
            "floors": text(floors, 100), "priority": JSONValue(priority?.rawValue), "siteAccess": text(siteAccess, 1000),
            "waterOnSite": waterOnSite.map(JSONValue.bool) ?? .null, "powerOnSite": powerOnSite.map(JSONValue.bool) ?? .null,
        ])
    }

    public var isEmpty: Bool { self == BauakteProject() }

    /// Kurzangaben für den Kopf der Bauakte, z. B. „180 m²“, „CT 50 mm“, „Heizestrich“.
    public var badges: [String] {
        var result: [String] = []
        if let totalArea { result.append("\(totalArea.formatted(.number.precision(.fractionLength(0...1)).locale(Locale(identifier: "de_DE")))) m²") }
        if let screedType {
            result.append([screedType.short, nominalThicknessMm.map { "\($0) mm" }].compactMap { $0 }.joined(separator: " "))
        } else if let nominalThicknessMm {
            result.append("\(nominalThicknessMm) mm")
        }
        if let construction { result.append(construction.title) }
        if underfloorHeating == true, construction != .heated { result.append("Fußbodenheizung") }
        if let priority, priority == .high || priority == .urgent { result.append(priority.title) }
        return result
    }
}
