#if DEBUG
import EstrichCore
import Foundation

/// Steuerung für XCUITests über Umgebungsvariablen. Nur in Debug-Builds enthalten.
///
/// - `ESTRICHONE_UITEST=1`: eigener Einstellungsbereich, bei jedem Start geleert,
///   außer mit `ESTRICHONE_UITEST_KEEP=1` (Neustart-Tests)
/// - `ESTRICHONE_UITEST_SERVER_STATUS` / `ESTRICHONE_UITEST_SERVER_BODY`: feste Antwort statt Netzwerk
enum UITestSupport {
    private static var environment: [String: String] { ProcessInfo.processInfo.environment }

    static func isolatedDefaults() -> UserDefaults? {
        guard environment["ESTRICHONE_UITEST"] == "1" else { return nil }
        let suite = "de.estrichone.uitest"
        if environment["ESTRICHONE_UITEST_KEEP"] != "1" {
            UserDefaults().removePersistentDomain(forName: suite)
        }
        return UserDefaults(suiteName: suite)
    }

    static func transport() -> HTTPTransport? {
        guard let body = environment["ESTRICHONE_UITEST_SERVER_BODY"] else { return nil }
        let status = environment["ESTRICHONE_UITEST_SERVER_STATUS"].flatMap(Int.init) ?? 200
        return StubTransport(status: status, body: body)
    }

    private struct StubTransport: HTTPTransport {
        let status: Int
        let body: String

        func get(_ url: URL) async throws -> (Data, HTTPURLResponse) {
            guard let response = HTTPURLResponse(url: url, statusCode: status, httpVersion: "HTTP/1.1", headerFields: nil) else {
                throw URLError(.badServerResponse)
            }
            return (Data(body.utf8), response)
        }
    }
}
#endif
