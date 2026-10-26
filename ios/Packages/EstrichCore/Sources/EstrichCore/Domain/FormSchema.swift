import Foundation

/// Eintrag einer Werteliste (Sync-Entität `valueListItem`).
public struct ValueListItem: Equatable, Sendable, Identifiable {
    public let id: UUID
    public let list: String
    public let key: String
    public let label: String
    public let short: String?
    public let sortOrder: Int
    public let isActive: Bool

    init?(_ data: JSONValue, id: UUID) {
        guard let list = data["list"]?.string, let key = data["key"]?.string else { return nil }
        self.id = id
        self.list = list
        self.key = key
        label = data["label"]?.string ?? key
        short = data["short"]?.string
        sortOrder = data["sortOrder"]?.number.map { Int($0) } ?? 0
        isActive = data["isActive"]?.bool ?? true
    }

    public init(list: String, key: String, label: String, short: String? = nil, sortOrder: Int = 0, isActive: Bool = true) {
        id = UUID()
        self.list = list
        self.key = key
        self.label = label
        self.short = short
        self.sortOrder = sortOrder
        self.isActive = isActive
    }
}

/// Wertelisten nach Listenname.
public struct ValueLists: Equatable, Sendable {
    public let items: [String: [ValueListItem]]

    public init(_ items: [ValueListItem]) {
        self.items = Dictionary(grouping: items, by: \.list).mapValues { $0.sorted { ($0.sortOrder, $0.label) < ($1.sortOrder, $1.label) } }
    }

    /// Angebotene Werte: nur aktive; ein vorhandener, inzwischen inaktiver Wert bleibt sichtbar.
    public func options(_ list: String, keeping current: String? = nil) -> [ValueListItem] {
        (items[list] ?? []).filter { $0.isActive || $0.key == current }
    }

    public func label(_ list: String, _ key: String) -> String? {
        items[list]?.first { $0.key == key }?.label
    }

    public func short(_ list: String, _ key: String) -> String? {
        items[list]?.first { $0.key == key }.map { $0.short ?? $0.label }
    }
}

/// Formular-Schema der Basisdaten (`GET /forms/bauakte` bzw. Sync-Entität `formSchema`, key „bauakte“).
public struct FormSchema: Equatable, Sendable {
    public enum FieldType: String, Sendable {
        case text, textarea, number, integer, select, boolean, date
    }

    public struct Field: Equatable, Sendable, Identifiable {
        public let key: String
        public let label: String
        public let type: FieldType
        public let list: String?
        /// `project.<key>` (eingebaut) oder `project.custom.<key>` (eigenes Feld).
        public let path: String
        public let required: Bool
        public let unit: String?
        public let help: String?
        public let badge: Bool
        public var id: String { path }

        /// Weg innerhalb von `project`: `["totalArea"]` bzw. `["custom", "fliesen"]`.
        public var projectPath: [String] {
            let parts = path.split(separator: ".").map(String.init)
            return parts.first == "project" ? Array(parts.dropFirst()) : parts
        }
    }

    public struct Group: Equatable, Sendable, Identifiable {
        public let key: String
        public let label: String
        public let fields: [Field]
        public var id: String { key }
    }

    public let key: String
    public let version: Int
    public let groups: [Group]

    public init?(_ data: JSONValue) {
        guard let groups = data["groups"]?.array else { return nil }
        key = data["key"]?.string ?? "bauakte"
        version = data["version"]?.number.map { Int($0) } ?? 0
        self.groups = groups.compactMap { group in
            guard let key = group["key"]?.string else { return nil }
            let fields = (group["fields"]?.array ?? []).compactMap { field -> Field? in
                guard let key = field["key"]?.string, let type = field["type"]?.string.flatMap(FieldType.init(rawValue:)) else { return nil }
                return Field(key: key, label: field["label"]?.string ?? key, type: type, list: field["list"]?.string,
                             path: field["path"]?.string ?? "project.custom.\(key)", required: field["required"]?.bool ?? false,
                             unit: field["unit"]?.string, help: field["help"]?.string, badge: field["badge"]?.bool ?? false)
            }
            return Group(key: key, label: group["label"]?.string ?? key, fields: fields)
        }
    }

    public var fields: [Field] { groups.flatMap(\.fields) }

    /// Fehlende Pflichtangaben (Beschriftungen), in Anzeigereihenfolge.
    public func missingRequired(in project: [String: JSONValue]) -> [String] {
        fields.filter { $0.required && Self.isEmpty(Self.value($0, in: project)) }.map(\.label)
    }

    /// Kurzangaben für den Kopf (Felder mit `badge`), z. B. „182,5 m²“, „Zementestrich CT“.
    public func badges(for project: [String: JSONValue], lists: ValueLists) -> [String] {
        fields.filter(\.badge).compactMap { field in
            Self.display(field, Self.value(field, in: project), lists: lists, short: true)
        }
    }

    public static func value(_ field: Field, in project: [String: JSONValue]) -> JSONValue {
        var current: JSONValue = .object(project)
        for part in field.projectPath {
            current = current[part] ?? .null
        }
        return current
    }

    public static func setting(_ value: JSONValue, for field: Field, in project: [String: JSONValue]) -> [String: JSONValue] {
        let path = field.projectPath
        guard let first = path.first else { return project }
        var result = project
        if path.count == 1 {
            result[first] = value
        } else {
            var nested = result[first]?.object ?? [:]
            nested[path[1]] = value
            result[first] = .object(nested)
        }
        return result
    }

    public static func isEmpty(_ value: JSONValue) -> Bool {
        switch value {
        case .null: true
        case let .string(text): text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        default: false
        }
    }

    /// Anzeigewert: Werteliste → Beschriftung, Zahl mit Einheit, ja/nein, Datum.
    public static func display(_ field: Field, _ value: JSONValue, lists: ValueLists, short: Bool = false) -> String? {
        switch value {
        case .null: return nil
        case let .string(text):
            guard !text.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
            if field.type == .select, let list = field.list {
                return (short ? lists.short(list, text) : lists.label(list, text)) ?? text
            }
            if field.type == .date, let date = CalendarDay.date(text) {
                return date.formatted(date: .abbreviated, time: .omitted)
            }
            return text
        case let .number(number):
            let text = field.type == .integer ? String(Int(number))
                : number.formatted(.number.precision(.fractionLength(0...2)).locale(Locale(identifier: "de_DE")))
            return [text, field.unit].compactMap { $0 }.joined(separator: " ")
        case let .bool(flag):
            return short ? (flag ? field.label : nil) : (flag ? "ja" : "nein")
        default:
            return nil
        }
    }
}

extension BauakteService {
    /// Schema der Basisdaten: aus dem Sync, sonst der zuletzt geladene Stand; `nil` = feste Felder verwenden.
    public func formSchema(using client: AuthorizedClient? = nil) async -> FormSchema? {
        if let synced = try? await store.entities(.formSchema).first(where: { $0.data["key"]?.string == "bauakte" }),
           let schema = FormSchema(synced.data) {
            return schema
        }
        if let client, let fresh: JSONValue = try? await client.get("forms/bauakte"), let schema = FormSchema(fresh) {
            if let data = try? JSONCoding.encoder.encode(fresh) {
                try? await store.setMeta("form_schema_bauakte", String(decoding: data, as: UTF8.self))
            }
            return schema
        }
        guard let text = (try? await store.meta("form_schema_bauakte")) ?? nil,
              let cached = try? JSONCoding.decoder.decode(JSONValue.self, from: Data(text.utf8)) else { return nil }
        return FormSchema(cached)
    }

    /// Wertelisten aus dem Sync.
    public func valueLists() async -> ValueLists {
        let entities = (try? await store.entities(.valueListItem)) ?? []
        return ValueLists(entities.compactMap { ValueListItem($0.data, id: $0.id) })
    }

    /// Werte des Bauvorhabens als Ganzes setzen (inkl. `custom`), `bauakte.update` mit `changes.project`.
    public func updateProject(json: [String: JSONValue], of id: UUID) async throws(BauakteError) {
        do {
            try await store.update(.bauakte, id: id, changes: ["project": .object(json)])
        } catch {
            throw .storage
        }
    }
}
