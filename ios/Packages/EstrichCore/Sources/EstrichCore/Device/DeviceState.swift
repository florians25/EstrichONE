import Foundation

/// Registrierung dieses Geräts beim Server. Enthält keine personenbezogenen Fachdaten.
public struct DeviceRegistration: Codable, Equatable, Sendable {
    public let deviceId: UUID
    public var kind: DeviceKind
    public var name: String
    public var status: DeviceStatus
}

/// Person, die dieses Gerät benutzt hat (nur Name für die Benutzerauswahl, keine Daten).
public struct LocalUser: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public var displayName: String
    public var userName: String
    public var lastUsedAt: Date
    public var failedPINAttempts: Int
    /// Nach zu vielen falschen PINs: Anmeldung nur noch online mit Passwort.
    public var requiresPasswordLogin: Bool

    public var initials: String { Self.initials(of: displayName) }

    public static func initials(of name: String) -> String {
        name.split(separator: " ").prefix(2).compactMap { $0.first.map(String.init) }.joined().uppercased()
    }
}

/// Gerätebereich: Registrierung und bekannte Benutzer (docs/reengineering/11, Abschnitt 4.3, Ordner `device/`).
public struct DeviceState: Codable, Equatable, Sendable {
    public var registration: DeviceRegistration?
    public var users: [LocalUser] = []

    public init() {}

    /// Zuletzt angemeldete zuerst.
    public var usersByRecency: [LocalUser] { users.sorted { $0.lastUsedAt > $1.lastUsedAt } }

    public mutating func upsert(_ summary: UserSummary, at date: Date) {
        if let index = users.firstIndex(where: { $0.id == summary.id }) {
            users[index].displayName = summary.displayName
            users[index].userName = summary.userName
            users[index].lastUsedAt = date
            users[index].failedPINAttempts = 0
            users[index].requiresPasswordLogin = false
        } else {
            users.append(LocalUser(id: summary.id, displayName: summary.displayName, userName: summary.userName,
                                   lastUsedAt: date, failedPINAttempts: 0, requiresPasswordLogin: false))
        }
    }
}

/// Dateiablage der App: `device/state.json` und je Benutzer `users/<id>/`.
public struct AppStorage: Sendable {
    public let root: URL

    public init(root: URL) {
        self.root = root
    }

    var stateURL: URL { root.appending(path: "device/state.json") }

    public func userDirectory(_ id: UUID) -> URL { root.appending(path: "users/\(id.uuidString.lowercased())", directoryHint: .isDirectory) }

    public func loadState() -> DeviceState {
        guard let data = try? Data(contentsOf: stateURL) else { return DeviceState() }
        return (try? JSONCoding.decoder.decode(DeviceState.self, from: data)) ?? DeviceState()
    }

    public func save(_ state: DeviceState) throws {
        try write(JSONCoding.encoder.encode(state), to: stateURL)
    }

    func write(_ data: Data, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        #if os(iOS)
        try data.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        #else
        try data.write(to: url, options: .atomic)
        #endif
    }

    /// Löscht alles: Gerätebereich und alle Benutzer (App zurücksetzen, Gerät entfernt).
    public func eraseAll() throws {
        if FileManager.default.fileExists(atPath: root.path()) {
            try FileManager.default.removeItem(at: root)
        }
    }

    public func eraseUser(_ id: UUID) throws {
        let directory = userDirectory(id)
        if FileManager.default.fileExists(atPath: directory.path()) {
            try FileManager.default.removeItem(at: directory)
        }
    }
}
