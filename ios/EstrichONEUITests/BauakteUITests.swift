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
        // Nach Anmeldung, Sheets und Systemdialogen laufen noch Animationen: erst tippen, wenn das Element erreichbar ist.
        // Kurz warten, bis Animationen fertig sind; Elemente außerhalb des sichtbaren Bereichs scrollt tap() selbst heran.
        let hittable = XCTNSPredicateExpectation(predicate: NSPredicate(format: "isHittable == true"), object: target)
        if XCTWaiter().wait(for: [hittable], timeout: 5) != .completed {
            // Meist liegt ein verspätetes Systemblatt („Passwort sichern?“) darüber.
            app.dismissSavePasswordPrompt(timeout: 3)
        }
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
        app.dismissSavePasswordPrompt()
        XCTAssertTrue(element("newBauakteTile", in: app).waitForExistence(timeout: 15))
        return app
    }

    @MainActor
    func testQuickCreateWithPhotosGetsNumberFromServer() {
        let app = launchSignedIn()

        tap("newBauakteTile", in: app)
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
        // Fotos stehen weiter unten in der Liste (nach Aktionszeile, Aufgaben, Notizen).
        let grid = element("photoGrid", in: app)
        for _ in 0..<4 where !grid.exists {
            app.swipeUp()
        }
        XCTAssertTrue(grid.waitForExistence(timeout: 5))
        for _ in 0..<4 { app.swipeDown() }
        let number = element("bauakteNumber", in: app)
        let hasNumber = NSPredicate(format: "label BEGINSWITH %@", "2026-")
        expectation(for: hasNumber, evaluatedWith: number)
        waitForExpectations(timeout: 20)
    }

    @MainActor
    func testCreatedBauakteIsFoundBySearch() {
        let app = launchSignedIn()
        tap("newBauakteTile", in: app)
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

    @MainActor
    func testNoteDefectAndHistory() {
        let app = launchSignedIn()
        tap("newBauakteTile", in: app)
        type("Reihenhaus Mitte", into: "newTitleField", in: app)
        tap("createButton", in: app)
        XCTAssertTrue(element("bauakteTitle", in: app).waitForExistence(timeout: 10))

        // Notiz: sofort sichtbar.
        tap("addNoteButton", in: app)
        type("Schlüssel liegt beim Nachbarn", into: "noteTextField", in: app)
        tap("saveNoteButton", in: app)
        XCTAssertTrue(app.staticTexts["Schlüssel liegt beim Nachbarn"].waitForExistence(timeout: 5))

        // Mangel erfassen, dann abhaken.
        tap("addMenu", in: app)
        app.buttons["Mangel"].firstMatch.tap()
        type("Riss im Flur", into: "workItemTitleField", in: app)
        tap("saveWorkItemButton", in: app)
        let toggle = element("toggleWorkItem-Riss im Flur", in: app)
        XCTAssertTrue(toggle.waitForExistence(timeout: 5), app.debugDescription)
        toggle.tap()
        let reopen = NSPredicate(format: "label CONTAINS %@", "Wieder öffnen")
        expectation(for: reopen, evaluatedWith: toggle)
        waitForExpectations(timeout: 10)

        // CM-Messung: Belegreife sofort sichtbar.
        tap("addMenu", in: app)
        app.buttons["CM-Messung"].firstMatch.tap()
        type("Bad OG", into: "cmLocationField", in: app)
        type("1,6", into: "cmValueField", in: app)
        XCTAssertEqual(element("readinessLabel", in: app).label, "Belegreif")
        tap("saveCmButton", in: app)
        XCTAssertTrue(app.staticTexts["Prüfungen (1)"].waitForExistence(timeout: 5))

        // Verlauf vom Server.
        app.swipeUp()
        tap("historyLink", in: app)
        XCTAssertTrue(app.staticTexts["Max Huber hat die Bauakte angelegt."].waitForExistence(timeout: 10))
    }

    @MainActor
    func testReportProblemWithDiagnosticsPreview() {
        let app = launchSignedIn()
        app.tabBars.buttons["Mehr"].tap()
        tap("reportProblemButton", in: app)
        type("Fotos werden nicht übertragen", into: "ticketTitleField", in: app)
        // Vor dem Senden ist sichtbar, was mitgeht.
        tap("diagnosticsDisclosure", in: app)
        XCTAssertTrue(app.staticTexts["Fehlercodes"].waitForExistence(timeout: 5))
        tap("sendTicketButton", in: app)
        XCTAssertTrue(app.alerts["Meldung gespeichert"].waitForExistence(timeout: 10))
    }
}
