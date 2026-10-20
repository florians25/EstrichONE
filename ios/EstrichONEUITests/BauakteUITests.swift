import XCTest

/// Schnellanlage und Fotos (09) gegen den Testserver im App-Prozess.
final class BauakteUITests: XCTestCase {
    @MainActor
    private func element(_ identifier: String, in app: XCUIApplication) -> XCUIElement {
        app.descendants(matching: .any).matching(identifier: identifier).firstMatch
    }

    @MainActor
    private func tap(_ identifier: String, in app: XCUIApplication, timeout: TimeInterval = 10) {
        let target = element(identifier, in: app)
        XCTAssertTrue(target.waitForExistence(timeout: timeout), "\(identifier) erscheint nicht\n\(app.debugDescription)")
        target.tap()
    }

    @MainActor
    private func type(_ text: String, into identifier: String, in app: XCUIApplication) {
        let field = element(identifier, in: app)
        XCTAssertTrue(field.waitForExistence(timeout: 10), "\(identifier) erscheint nicht")
        field.tap()
        field.typeText(text)
    }

    /// Persönliches Gerät einrichten und anmelden.
    @MainActor
    private func launchSignedIn() -> XCUIApplication {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchEnvironment = ["ESTRICHONE_UITEST": "1", "ESTRICHONE_UITEST_KEEP": "0"]
        app.launch()
        XCTAssertTrue(element("welcomeTitle", in: app).waitForExistence(timeout: 15))
        tap("enterAddressButton", in: app)
        type("estrichone.example.com", into: "serverAddressField", in: app)
        tap("checkConnectionButton", in: app)
        tap("useServerButton", in: app)
        tap("kindPersonal", in: app)
        tap("deviceKindContinue", in: app)
        type("m.huber", into: "loginField", in: app)
        type("Richtig-2026", into: "passwordField", in: app)
        tap("signInButton", in: app)
        XCTAssertTrue(element("newBauakteButton", in: app).waitForExistence(timeout: 15))
        return app
    }

    @MainActor
    func testQuickCreateWithPhotosGetsNumberFromServer() {
        let app = launchSignedIn()

        tap("newBauakteButton", in: app)
        type("EFH Müller, Neubau", into: "newTitleField", in: app)
        tap("createAndPhotographButton", in: app)

        // Kamera (im Simulator mit Testfoto-Auslöser): drei Fotos, dann „Fertig (3)“.
        for _ in 0..<3 {
            tap("uitestCaptureButton", in: app)
        }
        let counter = element("photoCounter", in: app)
        XCTAssertTrue(counter.waitForExistence(timeout: 5))
        let threePhotos = NSPredicate(format: "label CONTAINS %@", "3 Fotos")
        expectation(for: threePhotos, evaluatedWith: counter)
        waitForExpectations(timeout: 10)
        tap("cameraDoneButton", in: app)

        // Bauakte ist sofort da; nach der Synchronisation trägt sie die Nummer vom Server.
        XCTAssertEqual(element("bauakteTitle", in: app).label, "EFH Müller, Neubau")
        XCTAssertTrue(element("photoGrid", in: app).waitForExistence(timeout: 5))
        let number = element("bauakteNumber", in: app)
        let hasNumber = NSPredicate(format: "label BEGINSWITH %@", "2026-")
        expectation(for: hasNumber, evaluatedWith: number)
        waitForExpectations(timeout: 20)
    }

    @MainActor
    func testCreatedBauakteIsFoundBySearch() {
        let app = launchSignedIn()
        tap("newBauakteButton", in: app)
        type("Halle Huber", into: "newTitleField", in: app)
        tap("createButton", in: app)
        XCTAssertTrue(element("bauakteTitle", in: app).waitForExistence(timeout: 10))

        app.tabBars.buttons["Bauakten"].tap()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5))
        search.tap()
        search.typeText("huber")
        XCTAssertTrue(app.staticTexts["Halle Huber"].waitForExistence(timeout: 5))
    }
}
