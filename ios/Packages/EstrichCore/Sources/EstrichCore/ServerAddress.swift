import Foundation

/// Adresse eines EstrichONE-Servers, wie sie der Benutzer eingibt oder aus dem QR-Code übernimmt.
///
/// Regeln (docs/reengineering/10-server-onboarding.md, Schritt 2):
/// - fehlt das Schema, wird `https://` ergänzt;
/// - `http://` ist nur in Entwicklereinstellungen für lokale Testserver erlaubt;
/// - Benutzername/Passwort, Abfrageparameter und Fragmente in der Adresse werden abgelehnt;
/// - ein Pfadpräfix (Server unter `https://firma.de/estrichone`) ist erlaubt.
public struct ServerAddress: Hashable, Sendable, Codable {
    public let baseURL: URL

    public init(validated baseURL: URL) {
        self.baseURL = baseURL
    }

    public var host: String { baseURL.host() ?? baseURL.absoluteString }

    /// Vollständige URL eines API-Pfads, z. B. `api/v1/server-info`.
    public func url(for path: String) -> URL {
        baseURL.appending(path: path)
    }

    public static func parse(_ input: String, allowInsecureLocal: Bool = false) throws(ServerAddressError) -> ServerAddress {
        var text = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { throw .empty }
        if !text.contains("://") {
            text = "https://" + text
        }

        guard var components = URLComponents(string: text),
              let scheme = components.scheme?.lowercased(),
              let host = components.host, !host.isEmpty
        else { throw .invalid }

        guard components.user == nil, components.password == nil,
              components.query == nil, components.fragment == nil
        else { throw .invalid }

        switch scheme {
        case "https":
            break
        case "http" where allowInsecureLocal && Self.isLocalDevelopmentHost(host):
            break
        case "http":
            throw .insecure
        default:
            throw .invalid
        }

        components.scheme = scheme
        components.host = host.lowercased()
        while components.path.hasSuffix("/") {
            components.path.removeLast()
        }

        guard let url = components.url else { throw .invalid }
        return ServerAddress(validated: url)
    }

    /// Nur diese Ziele dürfen in Entwicklereinstellungen unverschlüsselt angesprochen werden.
    static func isLocalDevelopmentHost(_ host: String) -> Bool {
        let host = host.lowercased()
        return host == "localhost" || host == "127.0.0.1" || host == "::1" || host == "10.0.2.2" || host.hasSuffix(".local")
    }
}

public enum ServerAddressError: Error, Equatable, Sendable {
    case empty
    case invalid
    case insecure

    public var message: String {
        switch self {
        case .empty: "Bitte die Adresse des EstrichONE-Servers eingeben."
        case .invalid: "Das ist keine gültige Serveradresse. Beispiel: estrichone.meinefirma.de"
        case .insecure: "Der Server verwendet keine verschlüsselte Verbindung. Aus Sicherheitsgründen ist das nicht erlaubt."
        }
    }
}
