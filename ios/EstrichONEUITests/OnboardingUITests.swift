import XCTest

/// Hauptabläufe der Einrichtung (10, Schritt 1–5; 11) gegen den Testserver im App-Prozess (UITestSupport).
/// Der echte Server wird separat gegen EstrichCore getestet (ios/scripts/test-core-against-server.sh).
final class OnboardingUITests: XCTestCase {
    private let password = "Richtig-2026"

    @MainActor
    private func launch(serverStatus: Int = 200, serverBody: String? = nil, keepState: Bool = false) -> XCUIApplication {
        continueAfterFailure = false
        let app = XCUIApplication()
        var environment = [
            "ESTRICHONE_UITEST": "1",
            "ESTRICHONE_UITEST_KEEP": keepState ? "1" : "0",
            "ESTRICHONE_UITEST_SERVER_STATUS": String(serverStatus),
        ]
        environment["ESTRICHONE_UITEST_SERVER_BODY"] = serverBody
        app.launchEnvironment = environment
        app.launch()
        return app
    }

    @MainActor
    private func element(_ identifier: String, in app: XCUIApplication) -> XCUIElement {
        app.descendants(matching: .any).matching(identifier: identifier).firstMatch
    }

    @MainActor
    private func tap(_ identifier: String, in app: XCUIApplication, timeout: TimeInterval = 10) {
        let target = element(identifier, in: app)
        XCTAssertTrue(target.waitForExistence(timeout: timeout), "\(identifier) erscheint nicht")
        target.tap()
    }

    @MainActor
    private func type(_ text: String, into identifier: String, in app: XCUIApplication) {
        let field = element(identifier, in: app)
        XCTAssertTrue(field.waitForExistence(timeout: 10), "\(identifier) erscheint nicht")
        field.tap()
        field.typeText(text)
    }

    @MainActor
    private func enterAddress(_ address: String, in app: XCUIApplication) {
        XCTAssertTrue(element("welcomeTitle", in: app).waitForExistence(timeout: 15))
        tap("enterAddressButton", in: app)
        type(address, into: "serverAddressField", in: app)
        tap("checkConnectionButton", in: app)
    }

    @MainActor
    private func signIn(_ login: String, in app: XCUIApplication) {
        type(login, into: "loginField", in: app)
        type(password, into: "passwordField", in: app)
        tap("signInButton", in: app)
    }

    @MainActor
    private func enterPIN(_ pin: String, in app: XCUIApplication) {
        for digit in pin {
            tap("pinKey\(digit)", in: app)
        }
    }

    // MARK: Schritt 1–2

    @MainActor
    func testForeignServerIsRejected() {
        let app = launch(serverStatus: 404, serverBody: "<html>Not Found</html>")
        enterAddress("www.example.com", in: app)

        let error = element("serverError", in: app)
        XCTAssertTrue(error.waitForExistence(timeout: 5))
        XCTAssertTrue(error.label.contains("Unter dieser Adresse läuft kein EstrichONE-Server."), error.label)
        XCTAssertFalse(element("useServerButton", in: app).exists)
    }

    @MainActor
    func testUnencryptedAddressIsRejected() {
        let app = launch()
        enterAddress("http://estrichone.example.com", in: app)

        let error = element("serverError", in: app)
        XCTAssertTrue(error.waitForExistence(timeout: 5))
        XCTAssertTrue(error.label.contains("keine verschlüsselte Verbindung"), error.label)
    }

    // MARK: Persönliches Gerät

    @MainActor
    func testPersonalDeviceSetupAndRestart() {
        var app = launch()
        enterAddress("estrichone.example.com", in: app)
        let name = element("confirmServerName", in: app)
        XCTAssertTrue(name.waitForExistence(timeout: 5))
        XCTAssertEqual(name.label, "Estrichbau Beispiel GmbH")
        tap("useServerButton", in: app)

        tap("kindPersonal", in: app)
        tap("deviceKindContinue", in: app)
        type("m.huber", into: "loginField", in: app)
        type("falsch", into: "passwordField", in: app)
        tap("signInButton", in: app)
        XCTAssertTrue(element("signInError", in: app).waitForExistence(timeout: 5))

        type(password, into: "passwordField", in: app)
        tap("signInButton", in: app)
        let user = element("currentUserName", in: app)
        XCTAssertTrue(user.waitForExistence(timeout: 10))
        XCTAssertEqual(user.label, "Max Huber")

        // Nach einem Neustart ist die App eingerichtet und gesperrt; Entsperren öffnet die eigene Sitzung.
        app.terminate()
        app = launch(keepState: true)
        XCTAssertTrue(element("currentUserName", in: app).waitForExistence(timeout: 15))
        XCTAssertFalse(element("welcomeTitle", in: app).exists)
    }

    // MARK: Poolgerät

    @MainActor
    func testPoolDeviceWithEnrollmentLinkAndTwoPeople() {
        let app = launch()
        XCTAssertTrue(element("welcomeTitle", in: app).waitForExistence(timeout: 15))

        // QR-Code aus dem IT Center, hier als Link geöffnet: Verbindungsprüfung startet sofort.
        app.open(URL(string: "estrichone://enroll?v=1&server=https://estrichone.example.com&code=K7QX-M2PA&type=pool")!)
        tap("useServerButton", in: app, timeout: 15)

        signIn("m.huber", in: app)
        XCTAssertTrue(element("choosePINTitle", in: app).waitForExistence(timeout: 10))
        enterPIN("123456", in: app)
        XCTAssertTrue(element("pinError", in: app).waitForExistence(timeout: 5), "Triviale PIN wird abgelehnt")
        enterPIN("482913", in: app)
        enterPIN("482913", in: app)
        XCTAssertTrue(element("currentUserName", in: app).waitForExistence(timeout: 15))
        XCTAssertEqual(element("currentUserName", in: app).label, "Max Huber")

        // Benutzerwechsel: Auswahl statt Fortsetzung der letzten Sitzung (11, AK-2).
        tap("switchUserButton", in: app)
        XCTAssertTrue(element("userPickerTitle", in: app).waitForExistence(timeout: 5))
        tap("addPersonButton", in: app)
        signIn("lukas", in: app)
        enterPIN("573920", in: app)
        enterPIN("573920", in: app)
        XCTAssertTrue(element("currentUserName", in: app).waitForExistence(timeout: 15))
        XCTAssertEqual(element("currentUserName", in: app).label, "Lukas Brandl")

        // Zurück zu Max: falsche PIN wird abgelehnt, richtige öffnet nur seinen Tresor.
        tap("switchUserButton", in: app)
        tap("userTile-m.huber", in: app)
        enterPIN("573920", in: app)
        XCTAssertTrue(element("pinError", in: app).waitForExistence(timeout: 10))
        enterPIN("482913", in: app)
        XCTAssertTrue(element("currentUserName", in: app).waitForExistence(timeout: 15))
        XCTAssertEqual(element("currentUserName", in: app).label, "Max Huber")
    }

    @MainActor
    func testPoolDeviceWithoutCodeWaitsForApproval() {
        let app = launch()
        enterAddress("estrichone.example.com", in: app)
        tap("useServerButton", in: app)
        tap("kindPool", in: app)
        tap("deviceKindContinue", in: app)
        signIn("m.huber", in: app)
        enterPIN("482913", in: app)
        enterPIN("482913", in: app)

        XCTAssertTrue(element("waitingTitle", in: app).waitForExistence(timeout: 15))
    }
}
