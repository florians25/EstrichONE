import XCTest

/// Hauptablauf der Server-Einrichtung (10, Schritt 1–2) gegen eine feste Serverantwort.
/// Die App fragt dafür kein Netzwerk an (UITestSupport, nur in Debug-Builds).
final class OnboardingUITests: XCTestCase {
    private let serverInfo = """
    {"product":"EstrichONE","serverName":"Estrichbau Beispiel GmbH","serverVersion":"0.1.0","apiVersions":["v1"],
     "minClientVersion":"0.1.0","authMethods":["local"],"instanceEpoch":"0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b","setupRequired":false}
    """

    @MainActor
    private func launch(status: Int = 200, body: String, keepState: Bool = false) -> XCUIApplication {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchEnvironment = [
            "ESTRICHONE_UITEST": "1",
            "ESTRICHONE_UITEST_KEEP": keepState ? "1" : "0",
            "ESTRICHONE_UITEST_SERVER_STATUS": String(status),
            "ESTRICHONE_UITEST_SERVER_BODY": body,
        ]
        app.launch()
        return app
    }

    @MainActor
    private func element(_ identifier: String, in app: XCUIApplication) -> XCUIElement {
        app.descendants(matching: .any).matching(identifier: identifier).firstMatch
    }

    @MainActor
    private func enterAddress(_ address: String, in app: XCUIApplication) {
        XCTAssertTrue(element("welcomeTitle", in: app).waitForExistence(timeout: 15))
        element("enterAddressButton", in: app).tap()

        let field = element("serverAddressField", in: app)
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.tap()
        field.typeText(address)
        element("checkConnectionButton", in: app).tap()
    }

    @MainActor
    func testServerIsConfirmedAndRemembered() {
        var app = launch(body: serverInfo)
        enterAddress("estrichone.example.com", in: app)

        let name = element("confirmServerName", in: app)
        XCTAssertTrue(name.waitForExistence(timeout: 5))
        XCTAssertEqual(name.label, "Estrichbau Beispiel GmbH")

        element("useServerButton", in: app).tap()
        XCTAssertTrue(element("connectedServerName", in: app).waitForExistence(timeout: 5))

        // Nach einem Neustart ist der Server weiterhin eingerichtet.
        app.terminate()
        app = launch(body: serverInfo, keepState: true)
        XCTAssertTrue(element("connectedServerName", in: app).waitForExistence(timeout: 15))
        XCTAssertFalse(element("welcomeTitle", in: app).exists)
    }

    @MainActor
    func testForeignServerIsRejected() {
        let app = launch(status: 404, body: "<html>Not Found</html>")
        enterAddress("www.example.com", in: app)

        let error = element("serverError", in: app)
        XCTAssertTrue(error.waitForExistence(timeout: 5))
        XCTAssertTrue(error.label.contains("Unter dieser Adresse läuft kein EstrichONE-Server."), error.label)
        XCTAssertFalse(element("useServerButton", in: app).exists)
    }

    @MainActor
    func testUnencryptedAddressIsRejected() {
        let app = launch(body: serverInfo)
        enterAddress("http://estrichone.example.com", in: app)

        let error = element("serverError", in: app)
        XCTAssertTrue(error.waitForExistence(timeout: 5))
        XCTAssertTrue(error.label.contains("keine verschlüsselte Verbindung"), error.label)
    }
}
