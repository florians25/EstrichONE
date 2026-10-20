import Foundation

/// Beliebiger JSON-Wert. Der lokale Speicher hält Datensätze so, wie der Server sie liefert, damit neue Felder
/// ohne App-Update erhalten bleiben und das feldweise Zusammenführen (docs/sync-protokoll.md, Abschnitt 3)
/// unabhängig vom Datentyp funktioniert.
public enum JSONValue: Codable, Hashable, Sendable {
    case null
    case bool(Bool)
    case number(Double)
    case string(String)
    case array([JSONValue])
    case object([String: JSONValue])

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Double.self) {
            self = .number(value)
        } else if let value = try? container.decode(String.self) {
            self = .string(value)
        } else if let value = try? container.decode([JSONValue].self) {
            self = .array(value)
        } else {
            self = .object(try container.decode([String: JSONValue].self))
        }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .null: try container.encodeNil()
        case let .bool(value): try container.encode(value)
        case let .number(value):
            // Ganze Zahlen ohne „.0“, damit Versionen und IDs exakt so beim Server ankommen.
            if value.rounded() == value, abs(value) < 9_007_199_254_740_992 {
                try container.encode(Int64(value))
            } else {
                try container.encode(value)
            }
        case let .string(value): try container.encode(value)
        case let .array(value): try container.encode(value)
        case let .object(value): try container.encode(value)
        }
    }

    public subscript(key: String) -> JSONValue? {
        if case let .object(object) = self { object[key] } else { nil }
    }

    public var string: String? { if case let .string(value) = self { value } else { nil } }
    public var number: Double? { if case let .number(value) = self { value } else { nil } }
    public var bool: Bool? { if case let .bool(value) = self { value } else { nil } }
    public var object: [String: JSONValue]? { if case let .object(value) = self { value } else { nil } }
    public var isNull: Bool { self == .null }

    public init(_ string: String?) { self = string.map(JSONValue.string) ?? .null }
    public init(_ number: Double?) { self = number.map(JSONValue.number) ?? .null }
    public init(_ bool: Bool) { self = .bool(bool) }
    public init(_ uuid: UUID?) { self = uuid.map { .string($0.uuidString.lowercased()) } ?? .null }

    /// Überträgt Felder aus `changes` in ein Objekt.
    public func merging(_ changes: [String: JSONValue]) -> JSONValue {
        var object = self.object ?? [:]
        for (key, value) in changes {
            object[key] = value
        }
        return .object(object)
    }

    public func encoded() throws -> String {
        String(decoding: try JSONCoding.encoder.encode(self), as: UTF8.self)
    }

    public static func decode(_ text: String) throws -> JSONValue {
        try JSONCoding.decoder.decode(JSONValue.self, from: Data(text.utf8))
    }

    /// Wandelt ein `Encodable` (z. B. eine Antwort-Struktur) in einen JSON-Wert.
    public static func from(_ value: some Encodable) throws -> JSONValue {
        try JSONCoding.decoder.decode(JSONValue.self, from: JSONCoding.encoder.encode(value))
    }

    /// Liest einen JSON-Wert als `Decodable`-Typ (z. B. für die Anzeige).
    public func decode<T: Decodable>(as type: T.Type) throws -> T {
        try JSONCoding.decoder.decode(T.self, from: JSONCoding.encoder.encode(self))
    }
}

extension JSONValue: ExpressibleByStringLiteral, ExpressibleByFloatLiteral, ExpressibleByIntegerLiteral,
    ExpressibleByBooleanLiteral, ExpressibleByNilLiteral, ExpressibleByDictionaryLiteral, ExpressibleByArrayLiteral {
    public init(stringLiteral value: String) { self = .string(value) }
    public init(floatLiteral value: Double) { self = .number(value) }
    public init(integerLiteral value: Int) { self = .number(Double(value)) }
    public init(booleanLiteral value: Bool) { self = .bool(value) }
    public init(nilLiteral: ()) { self = .null }
    public init(dictionaryLiteral elements: (String, JSONValue)...) { self = .object(Dictionary(uniqueKeysWithValues: elements)) }
    public init(arrayLiteral elements: JSONValue...) { self = .array(elements) }
}
