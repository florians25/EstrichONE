import Foundation

/// Merkt sich den bestätigten Server über App-Neustarts hinweg.
///
/// Gespeichert werden nur öffentliche Angaben aus `server-info`, keine Zugangsdaten. Tokens und
/// Geräteschlüssel gehören in den Schlüsselbund (ab AP-11).
public final class ServerStore {
    static let key = "estrichone.server.v1"

    private let defaults: UserDefaults

    public init(defaults: UserDefaults) {
        self.defaults = defaults
    }

    /// Ein unlesbarer Eintrag (etwa aus einer künftigen, inkompatiblen Version) gilt als nicht eingerichtet.
    public func load() -> VerifiedServer? {
        guard let data = defaults.data(forKey: Self.key) else { return nil }
        return try? JSONDecoder().decode(VerifiedServer.self, from: data)
    }

    public func save(_ server: VerifiedServer) throws {
        defaults.set(try JSONEncoder().encode(server), forKey: Self.key)
    }

    public func clear() {
        defaults.removeObject(forKey: Self.key)
    }
}
