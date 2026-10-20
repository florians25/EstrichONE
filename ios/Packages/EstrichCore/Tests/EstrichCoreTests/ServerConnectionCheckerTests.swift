import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import Testing
@testable import EstrichCore

/// Liefert eine feste Antwort oder einen festen Fehler und merkt sich die angefragte URL.
private final class StubTransport: HTTPTransport, @unchecked Sendable {
    enum Reply {
        case response(status: Int, body: String)
        case failure(Error)
    }

    private let reply: Reply
    private(set) var requestedURL: URL?

    init(_ reply: Reply) {
        self.reply = reply
    }

    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let url = request.url!
        requestedURL = url
        switch reply {
        case let .response(status, body):
            let response = HTTPURLResponse(url: url, statusCode: status, httpVersion: "HTTP/1.1", headerFields: nil)!
            return (Data(body.utf8), response)
        case let .failure(error):
            throw error
        }
    }
}

private func serverInfoJSON(product: String = "EstrichONE", apiVersions: String = #"["v1"]"#,
                            minClientVersion: String = "1.0.0", setupRequired: Bool = false) -> String {
    """
    {"product":"\(product)","serverName":"Estrichbau Müller GmbH","serverVersion":"1.0.0",
     "apiVersions":\(apiVersions),"minClientVersion":"\(minClientVersion)","authMethods":["local"],
     "instanceEpoch":"0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b","setupRequired":\(setupRequired),"futureField":42}
    """
}

@Suite("Verbindungsprüfung")
struct ServerConnectionCheckerTests {
    private let address = try! ServerAddress.parse("estrichone.firma.de/sub")

    private func check(_ reply: StubTransport.Reply, appVersion: String = "1.0.0") async -> (Result<VerifiedServer, ServerCheckError>, StubTransport) {
        let transport = StubTransport(reply)
        let checker = ServerConnectionChecker(transport: transport, appVersion: SemanticVersion(appVersion)!)
        return (await checker.check(address), transport)
    }

    @Test("übernimmt einen passenden Server mit Name und Version")
    func success() async throws {
        let (result, transport) = await check(.response(status: 200, body: serverInfoJSON()))
        let server = try result.get()
        #expect(server.address == address)
        #expect(server.info.serverName == "Estrichbau Müller GmbH")
        #expect(server.info.authMethods == ["local"])
        #expect(transport.requestedURL?.absoluteString == "https://estrichone.firma.de/sub/api/v1/server-info")
    }

    @Test("erkennt fremde Server")
    func foreignServer() async {
        #expect(await check(.response(status: 404, body: "<html>")).0 == .failure(.notEstrichOne))
        #expect(await check(.response(status: 200, body: "<html></html>")).0 == .failure(.notEstrichOne))
        #expect(await check(.response(status: 200, body: serverInfoJSON(product: "Anderes"))).0 == .failure(.notEstrichOne))
    }

    @Test("meldet eine zu alte App")
    func appTooOld() async {
        let (result, _) = await check(.response(status: 200, body: serverInfoJSON(minClientVersion: "1.4")), appVersion: "1.3.9")
        #expect(result == .failure(.appTooOld(minimum: "1.4.0")))
    }

    @Test("meldet einen zu alten Server")
    func serverTooOld() async {
        let (result, _) = await check(.response(status: 200, body: serverInfoJSON(apiVersions: #"["v0"]"#)))
        #expect(result == .failure(.serverTooOld))
    }

    @Test("meldet einen noch nicht eingerichteten Server")
    func notSetUp() async {
        #expect(await check(.response(status: 200, body: serverInfoJSON(setupRequired: true))).0 == .failure(.notSetUp))
        let uninitialized = #"{"status":503,"code":"SERVER_NOT_INITIALIZED"}"#
        #expect(await check(.response(status: 503, body: uninitialized)).0 == .failure(.notSetUp))
    }

    @Test("meldet andere Statuscodes unverändert")
    func unexpectedStatus() async {
        #expect(await check(.response(status: 503, body: "")).0 == .failure(.unexpectedResponse(status: 503)))
        #expect(await check(.response(status: 500, body: "{}")).0 == .failure(.unexpectedResponse(status: 500)))
    }

    @Test("unterscheidet Zertifikatsfehler von fehlender Erreichbarkeit")
    func networkErrors() async {
        #expect(await check(.failure(URLError(.serverCertificateUntrusted))).0 == .failure(.certificateInvalid))
        #expect(await check(.failure(URLError(.serverCertificateHasBadDate))).0 == .failure(.certificateInvalid))
        #expect(await check(.failure(URLError(.timedOut))).0 == .failure(.unreachable))
        #expect(await check(.failure(URLError(.cannotFindHost))).0 == .failure(.unreachable))
    }
}
