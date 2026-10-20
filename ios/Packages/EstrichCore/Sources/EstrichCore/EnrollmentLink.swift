import Foundation

/// Inhalt des QR-Codes aus dem IT Center:
/// `estrichone://enroll?v=1&server=https://estrichone.firma.de&code=K7QX-M2PA&type=pool`
///
/// Der QR-Code enthält kein Geheimnis von Dauer: Der Einrichtungscode ist einmalig und kurzlebig
/// (docs/reengineering/10-server-onboarding.md, Schritt 2A). Unbekannte Parameter werden ignoriert.
public struct EnrollmentLink: Hashable, Sendable {
    public let server: ServerAddress
    public let enrollmentCode: String?
    public let deviceKind: DeviceKind?

    public static func parse(_ text: String, allowInsecureLocal: Bool = false) throws(EnrollmentLinkError) -> EnrollmentLink {
        guard let components = URLComponents(string: text.trimmingCharacters(in: .whitespacesAndNewlines)),
              components.scheme?.lowercased() == "estrichone",
              components.host?.lowercased() == "enroll"
        else { throw .notAnEstrichOneCode }

        let items = Dictionary((components.queryItems ?? []).map { ($0.name.lowercased(), $0.value ?? "") }, uniquingKeysWith: { first, _ in first })
        guard items["v"] == "1" else { throw .unsupportedVersion }
        guard let serverText = items["server"] else { throw .notAnEstrichOneCode }

        let server: ServerAddress
        do throws(ServerAddressError) {
            server = try ServerAddress.parse(serverText, allowInsecureLocal: allowInsecureLocal)
        } catch {
            throw .invalidServer(error)
        }

        let code = items["code"].flatMap { $0.isEmpty ? nil : $0.uppercased() }
        let kind = items["type"].flatMap(DeviceKind.init(rawValue:))
        return EnrollmentLink(server: server, enrollmentCode: code, deviceKind: kind)
    }
}

public enum EnrollmentLinkError: Error, Equatable, Sendable {
    case notAnEstrichOneCode
    case unsupportedVersion
    case invalidServer(ServerAddressError)

    public var message: String {
        switch self {
        case .notAnEstrichOneCode: "Dieser QR-Code ist kein EstrichONE-Einrichtungscode."
        case .unsupportedVersion: "Dieser Einrichtungscode stammt von einer neueren Serverversion. Bitte die App aktualisieren."
        case let .invalidServer(error): error.message
        }
    }
}
