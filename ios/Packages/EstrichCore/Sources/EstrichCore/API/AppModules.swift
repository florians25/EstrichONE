import Foundation

/// Module der Suite (Systemeinstellungen › Module im Web). Abgeschaltete Module blendet die App aus: Reiter, Einträge unter
/// „Mehr“, Karten auf „Heute“ und Abschnitte der Bauakte. Fehlende Schlüssel gelten als eingeschaltet (ältere Server).
public struct AppModules: Codable, Sendable, Equatable {
    public static let calls = "calls"
    public static let planning = "planning"
    public static let machines = "machines"
    public static let map = "map"
    public static let materials = "materials"
    public static let orders = "orders"
    public static let staff = "staff"
    public static let absence = "absence"
    public static let inspections = "inspections"
    public static let diary = "diary"
    public static let acceptance = "acceptance"
    public static let tasks = "tasks"
    public static let defects = "defects"
    public static let approvals = "approvals"
    public static let dossiers = "dossiers"
    public static let helpdesk = "helpdesk"
    public static let feedback = "feedback"

    /// Schalter je Modul (Schlüssel wie auf dem Server).
    public var states: [String: Bool]

    public init(states: [String: Bool] = [:]) { self.states = states }

    /// Alles eingeschaltet (Standard, solange nichts bekannt ist).
    public static let allOn = AppModules()

    public func isOn(_ key: String) -> Bool { states[key] ?? true }

    /// Mindestens eines der Module ist an (z. B. Aufgaben oder Mängel).
    public func anyOn(_ keys: String...) -> Bool { keys.contains { isOn($0) } }

    /// Antwort von `GET settings/modules`.
    struct Response: Decodable, Sendable {
        struct Module: Decodable, Sendable {
            let key: String
            let enabled: Bool
            let core: Bool?
        }
        let modules: [Module]
    }

    /// Zustand aus der Serverantwort (`GET settings/modules`); Kernmodule sind immer an.
    public static func decode(_ data: Data) throws -> AppModules {
        let response = try JSONDecoder().decode(Response.self, from: data)
        return AppModules(states: Dictionary(response.modules.map { ($0.key, $0.core == true || $0.enabled) }, uniquingKeysWith: { _, last in last }))
    }
}

/// Lädt den Modulzustand vom Server und hält ihn für den Offline-Betrieb im verschlüsselten Speicher.
public struct ModuleSettingsStore: Sendable {
    static let metaKey = "app_modules"
    public let store: LocalStore

    public init(store: LocalStore) { self.store = store }

    /// Zuletzt bekannter Stand; ohne gespeicherten Stand ist alles an.
    public func cached() async -> AppModules {
        guard let text = (try? await store.meta(Self.metaKey)) ?? nil, let data = text.data(using: .utf8),
              let modules = try? JSONDecoder().decode(AppModules.self, from: data) else { return .allOn }
        return modules
    }

    /// Vom Server laden und zwischenspeichern; ohne Verbindung (oder bei älteren Servern) der letzte Stand.
    public func load(using client: AuthorizedClient?) async -> AppModules {
        if let client, let response: AppModules.Response = try? await client.get("settings/modules") {
            let modules = AppModules(states: Dictionary(response.modules.map { ($0.key, $0.core == true || $0.enabled) },
                                                        uniquingKeysWith: { _, last in last }))
            if let data = try? JSONEncoder().encode(modules), let text = String(data: data, encoding: .utf8) {
                try? await store.setMeta(Self.metaKey, text)
            }
            return modules
        }
        return await cached()
    }
}
