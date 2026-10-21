import XCTest

/// Schnellanlage mit Kunde, Fotos, Dokumentenscan und Bautagebuch (09) gegen den Testserver im App-Prozess.
final class BauakteUITests: XCTestCase {
    /// Persönliches Gerät einrichten und anmelden.
    @MainActor
    private func launchSignedIn() -> XCUIApplication {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchEnvironment = ["ESTRICHONE_UITEST": "1", "ESTRICHONE_UITEST_KEEP": "0"]
        app.launch()
        XCTAssertTrue(app.element("welcomeTitle").waitForExistence(timeout: 20))
        app.tap("enterAddressButton")
        app.type("estrichone.example.com", into: "serverAddressField")
        app.tap("checkConnectionButton")
        app.tap("useServerButton")
        app.tap("kindPersonal")
        app.tap("deviceKindContinue")
        app.type("m.huber", into: "loginField")
        app.type("Richtig-2026", into: "passwordField")
        app.tap("signInButton")
        app.dismissSavePasswordPrompt()
        XCTAssertTrue(app.element("newBauakteTile").waitForExistence(timeout: 20))
        // Neu starten (Anmeldung bleibt erhalten): Beim ersten Start nach der Installation bleibt sonst teils ein
        // Systemblatt („Passwort sichern?“) über der App liegen und verdeckt Schaltflächen und Tab-Leiste.
        app.terminate()
        app.launchEnvironment["ESTRICHONE_UITEST_KEEP"] = "1"
        app.launch()
        XCTAssertTrue(app.element("newBauakteTile").waitForExistence(timeout: 20))
        XCTAssertTrue(app.waitUntilHittable(app.element("newBauakteTile"), timeout: 10), app.debugDescription)
        return app
    }

    /// Neue Bauakte im Formular ausfüllen (Kunde ist Pflicht: hier neu erfasst) und anlegen.
    /// Das Anlageblatt muss bereits offen sein.
    @MainActor
    private func fillQuickForm(_ title: String, in app: XCUIApplication, photograph: Bool = false) {
        app.chooseSegment("Formular")
        app.type(title, into: "newTitleField")
        app.tap("newCustomerButton")
        app.type("Familie \(title)", into: "customerNameField")
        app.tap(photograph ? "createAndPhotographButton" : "createButton")
    }

    /// Bauakte über die Startseite anlegen; danach ist ihre Ansicht offen.
    @MainActor
    private func createBauakte(_ title: String, in app: XCUIApplication) {
        app.tap("newBauakteTile")
        fillQuickForm(title, in: app)
        let header = app.element("bauakteTitle")
        XCTAssertTrue(header.waitForExistence(timeout: 15), app.debugDescription)
        XCTAssertTrue(app.wait(for: header, "label == %@", title, timeout: 5))
    }

    @MainActor
    func testQuickCreateWithPhotosGetsNumberFromServer() {
        let app = launchSignedIn()

        app.tap("newBauakteTile")
        fillQuickForm("EFH Müller, Neubau", in: app, photograph: true)

        // Kamera (im Simulator mit Testfoto-Auslöser): Beschriftung für die Serie, drei Fotos, dann „Fertig (3)“.
        app.tap("captionButton", timeout: 20)
        let captionField = app.textFields.firstMatch
        XCTAssertTrue(captionField.waitForExistence(timeout: 10))
        captionField.typeText("Bad OG")
        app.tapElement(app.alerts.buttons["Übernehmen"].firstMatch, "Übernehmen")
        for _ in 0..<3 {
            app.tap("uitestCaptureButton")
        }
        let counter = app.element("photoCounter")
        XCTAssertTrue(counter.waitForExistence(timeout: 5))
        XCTAssertTrue(app.wait(for: counter, "label CONTAINS %@", "3 Fotos", timeout: 15), counter.label)
        app.tap("cameraDoneButton")

        // Bauakte ist sofort da; die Fotos tragen die Beschriftung; nach der Synchronisation kommt die Nummer vom Server.
        let title = app.element("bauakteTitle")
        XCTAssertTrue(title.waitForExistence(timeout: 15))
        XCTAssertEqual(title.label, "EFH Müller, Neubau")
        XCTAssertTrue(app.reveal(app.element("photoGrid")), app.debugDescription)
        XCTAssertTrue(app.text(containing: "Bad OG").waitForExistence(timeout: 5))
        let number = app.reveal("bauakteNumber")
        XCTAssertTrue(app.wait(for: number, "label BEGINSWITH %@", "2026-", timeout: 30), number.label)
    }

    @MainActor
    func testWizardWithMandatoryCustomerAndDuplicateCheck() {
        let app = launchSignedIn()
        app.tap("newBauakteTile")
        app.chooseSegment("Assistent")

        // Schritt 1 – Kunde ist Pflicht: ohne Kunde geht es nicht weiter.
        app.tap("wizardNextButton")
        XCTAssertTrue(app.element("newBauakteError").waitForExistence(timeout: 10), app.debugDescription)

        // Neuer Kunde „Huber“: Die Dublettenprüfung findet den vorhandenen Kunden, der wird übernommen.
        app.type("Huber", into: "customerSearchField")
        XCTAssertTrue(app.element("customerResult").waitForExistence(timeout: 15), app.debugDescription)
        app.tap("newCustomerButton")
        let similar = app.element("useSimilarCustomer")
        XCTAssertTrue(similar.waitForExistence(timeout: 20) || app.reveal(similar), app.debugDescription)
        app.tapElement(similar, "useSimilarCustomer")
        XCTAssertTrue(app.element("removeCustomerButton").waitForExistence(timeout: 10), app.debugDescription)
        XCTAssertTrue(app.text(containing: "Huber Bau GmbH").exists)
        app.tap("wizardNextButton")

        // Schritt 2 – Bauvorhaben und Baustelle, Schritt 3 – Übersicht.
        app.type("EFH Schneider", into: "newTitleField")
        app.tap("wizardNextButton")
        XCTAssertTrue(app.text(containing: "Huber Bau GmbH").waitForExistence(timeout: 10))
        app.tap("createButton")

        // Die Bauakte zeigt den Kunden mit Anruf-Schaltfläche.
        XCTAssertTrue(app.element("bauakteTitle").waitForExistence(timeout: 15), app.debugDescription)
        let customer = app.reveal("customerName")
        XCTAssertTrue(app.wait(for: customer, "label == %@", "Huber Bau GmbH", timeout: 10), app.debugDescription)
        XCTAssertTrue(app.element("callCustomerButton").exists)
    }

    @MainActor
    func testScanDocumentAndQuickDiaryEntry() {
        let app = launchSignedIn()
        createBauakte("Halle Gewerbering", in: app)

        // Scan (im Simulator mit zwei erzeugten Seiten): Seiten prüfen, Art wählen, als PDF ablegen.
        app.tap("scanDocumentButton")
        XCTAssertTrue(app.element("scanTitleField").waitForExistence(timeout: 20), app.debugDescription)
        let pages = app.descendants(matching: .any).matching(identifier: "scanPage")
        XCTAssertTrue(app.wait(for: pages.element(boundBy: 1), "exists == true", timeout: 15), app.debugDescription)
        let save = app.element("saveScanButton")
        XCTAssertTrue(app.wait(for: save, "isEnabled == true", timeout: 15))
        app.tapElement(save, "saveScanButton")

        let document = app.descendants(matching: .any).matching(NSPredicate(format: "identifier BEGINSWITH %@", "document-Lieferschein")).firstMatch
        XCTAssertTrue(document.waitForExistence(timeout: 30) || app.reveal(document), app.debugDescription)

        // Bautagebuch mit Textbaustein.
        app.tap("addDiaryButton")
        XCTAssertTrue(app.element("diaryTextField").waitForExistence(timeout: 10))
        app.tapElement(app.buttons["Estrich eingebracht"].firstMatch, "Textbaustein")
        app.tap("saveDiaryButton")
        let entry = app.staticTexts["Estrich eingebracht"].firstMatch
        XCTAssertTrue(entry.waitForExistence(timeout: 10) || app.reveal(entry), app.debugDescription)
    }

    @MainActor
    func testCreatedBauakteIsFoundBySearch() {
        let app = launchSignedIn()
        // Anlegen über die schwebende Schaltfläche der Bauaktenliste (zweiter Weg neben der Startseite).
        app.tapElement(app.tabBars.buttons["Bauakten"].firstMatch, "Reiter Bauakten")
        app.tap("newBauakteButton")
        fillQuickForm("Halle Huber", in: app)
        XCTAssertTrue(app.element("bauakteTitle").waitForExistence(timeout: 15))

        app.tapElement(app.tabBars.buttons["Bauakten"].firstMatch, "Reiter Bauakten")
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 10))
        app.tapElement(search, "Suchfeld")
        search.typeText("huber")
        XCTAssertTrue(app.staticTexts["Halle Huber"].waitForExistence(timeout: 10))
    }

    @MainActor
    func testNoteDefectAndHistory() {
        let app = launchSignedIn()
        createBauakte("Reihenhaus Mitte", in: app)

        // Notiz: sofort sichtbar.
        app.tap("addNoteButton")
        app.type("Schlüssel liegt beim Nachbarn", into: "noteTextField")
        app.tap("saveNoteButton")
        let note = app.staticTexts["Schlüssel liegt beim Nachbarn"]
        XCTAssertTrue(note.waitForExistence(timeout: 5) || app.reveal(note), app.debugDescription)

        // Mangel erfassen, dann abhaken.
        app.tapElement(app.reveal("addMenu"), "addMenu")
        app.tapElement(app.buttons["Mangel"].firstMatch, "Menüpunkt Mangel")
        app.type("Riss im Flur", into: "workItemTitleField")
        app.tap("saveWorkItemButton")
        let toggle = app.element("toggleWorkItem-Riss im Flur")
        XCTAssertTrue(toggle.waitForExistence(timeout: 5) || app.reveal(toggle), app.debugDescription)
        app.reveal(toggle)
        app.tapElement(toggle, "Mangel abhaken")
        XCTAssertTrue(app.wait(for: toggle, "label CONTAINS %@", "Wieder öffnen", timeout: 10), toggle.label)

        // CM-Messung: Belegreife sofort sichtbar.
        app.tapElement(app.reveal("addMenu"), "addMenu")
        app.tapElement(app.buttons["CM-Messung"].firstMatch, "Menüpunkt CM-Messung")
        app.type("Bad OG", into: "cmLocationField")
        app.type("1,6", into: "cmValueField")
        XCTAssertTrue(app.wait(for: app.element("readinessLabel"), "label == %@", "Belegreif", timeout: 5))
        app.tap("saveCmButton")
        let inspections = app.staticTexts["Prüfungen (1)"]
        XCTAssertTrue(inspections.waitForExistence(timeout: 5) || app.reveal(inspections), app.debugDescription)

        // Verlauf vom Server.
        app.tapElement(app.reveal("historyLink"), "historyLink")
        XCTAssertTrue(app.staticTexts["Max Huber hat die Bauakte angelegt."].waitForExistence(timeout: 15))
    }

    @MainActor
    func testReportProblemWithDiagnosticsPreview() {
        let app = launchSignedIn()
        // Hinweis des Betreibers als Banner auf der Startseite, darunter „Wer fährt wohin“ für heute.
        XCTAssertTrue(app.text(containing: "Neue App-Version").waitForExistence(timeout: 20), app.debugDescription)
        let dispatch = app.text(containing: "Halle Gewerbering")
        XCTAssertTrue(dispatch.waitForExistence(timeout: 15) || app.reveal(dispatch), app.debugDescription)
        app.tapElement(app.tabBars.buttons["Mehr"].firstMatch, "Reiter Mehr")
        app.tap("reportProblemButton")
        app.type("Fotos werden nicht übertragen", into: "ticketTitleField")
        // Vor dem Senden ist sichtbar, was mitgeht.
        app.tap("diagnosticsDisclosure")
        XCTAssertTrue(app.staticTexts["Fehlercodes"].waitForExistence(timeout: 10))
        app.tap("sendTicketButton")
        XCTAssertTrue(app.alerts["Meldung gespeichert"].waitForExistence(timeout: 15))
    }

    @MainActor
    func testInitialInspectionRightAfterCreationAndProtocol() {
        let app = launchSignedIn()
        app.tap("newBauakteTile")
        app.chooseSegment("Assistent")
        app.tap("newCustomerButton")
        app.type("Familie Berger", into: "customerNameField")
        app.tap("wizardNextButton")
        app.type("EFH Berger", into: "newTitleField")
        app.tap("wizardNextButton")

        // Beim Kunden vor Ort: Anlegen und direkt die Erstbegehung durchführen.
        app.tapElement(app.reveal("createAndInspectButton"), "createAndInspectButton")
        app.tap("startInspectionButton", timeout: 20)
        for (check, answer) in [("substrate", "ok"), ("edgeInsulation", "ok"), ("heatingPressureTest", "notOk"),
                                ("access", "ok"), ("utilities", "notApplicable")] {
            app.tap("answer-\(check)-\(answer)")
        }
        app.type("Heizkreis Bad undicht, Kunde informiert", into: "inspectionNotesField")
        app.tap("finishInspectionButton")

        // Anzeige: Protokoll mit Ergebnis und Notizen.
        XCTAssertTrue(app.element("bauakteTitle").waitForExistence(timeout: 15), app.debugDescription)
        app.tapElement(app.reveal("inspectionProtocolLink"), "inspectionProtocolLink")
        let status = app.element("inspectionStatus")
        XCTAssertTrue(status.waitForExistence(timeout: 10), app.debugDescription)
        XCTAssertTrue(status.label.contains("1 Mangel"), status.label)
        XCTAssertTrue(app.text(containing: "Heizkreis Bad undicht").waitForExistence(timeout: 5) || app.reveal(app.text(containing: "Heizkreis Bad undicht")))
    }

    @MainActor
    func testAcceptanceProtocolWithDefectAndSignatures() {
        let app = launchSignedIn()
        createBauakte("Abnahme Wagner", in: app)
        XCTAssertTrue(app.element("bauakteTitle").waitForExistence(timeout: 15), app.debugDescription)
        app.tapElement(app.reveal("addAcceptanceButton"), "addAcceptanceButton")

        // Ergebnis mit Vorbehalt, ein Mangel mit Frist, beide Unterschriften.
        app.chooseSegment("Mit Vorbehalt")
        app.tapElement(app.reveal("addAcceptanceDefectButton"), "addAcceptanceDefectButton")
        app.type("Fuge Bad nacharbeiten", into: "acceptanceDefectField")
        app.dismissKeyboard()
        for side in ["signClientButton", "signContractorButton"] {
            app.tapElement(app.reveal(side), side)
            let canvas = app.element("signatureCanvas")
            XCTAssertTrue(canvas.waitForExistence(timeout: 10), app.debugDescription)
            canvas.swipeRight()
            canvas.swipeLeft()
            app.tap("applySignatureButton")
        }
        app.tap("saveAcceptanceButton")

        // In der Bauakte: Protokoll mit Mangel; nach der Übertragung abgeschlossen und als PDF abgelegt.
        // (Die Liste bleibt an der Stelle der Abnahme stehen – der Kopf ist dann nicht im Bild.)
        XCTAssertTrue(app.element("saveAcceptanceButton").waitForNonExistence(timeout: 15), app.debugDescription)
        app.tapElement(app.reveal("acceptanceLink"), "acceptanceLink")
        XCTAssertTrue(app.text(containing: "Fuge Bad nacharbeiten").waitForExistence(timeout: 10), app.debugDescription)
        let pdf = app.element("acceptancePdfButton")
        XCTAssertTrue(pdf.waitForExistence(timeout: 90) || app.reveal(pdf), app.debugDescription)
        XCTAssertTrue(app.text(containing: "Abgeschlossen").exists, app.debugDescription)
    }

    @MainActor
    func testDemoModeWithoutServer() {
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchEnvironment = ["ESTRICHONE_UITEST": "1", "ESTRICHONE_UITEST_KEEP": "0"]
        app.launch()
        XCTAssertTrue(app.element("welcomeTitle").waitForExistence(timeout: 20))
        app.tap("tryDemoButton")

        // Ohne Eingaben angemeldet, deutlich als Demo gekennzeichnet, mit Beispieldaten.
        XCTAssertTrue(app.element("demoBanner").waitForExistence(timeout: 30), app.debugDescription)
        XCTAssertTrue(app.element("newBauakteTile").waitForExistence(timeout: 20))

        // Startseite: heutiger Einsatz als Karte mit Anruf und Route, Vorschau auf morgen.
        let job = app.element("todayJob-EFH Schneider, Neubau")
        XCTAssertTrue(job.waitForExistence(timeout: 20) || app.reveal(job), app.debugDescription)
        XCTAssertTrue(app.element("todayJobCall").exists, app.debugDescription)
        XCTAssertTrue(app.element("todayJobRoute").exists, app.debugDescription)
        XCTAssertTrue(app.reveal(app.element("tomorrowPreview")), app.debugDescription)

        // „Wer fährt wohin“ nach Kolonnen: Kolonnen mit Farbe, Stärke und Abwesenden.
        app.tapElement(app.reveal("dispatchAllButton"), "dispatchAllButton")
        XCTAssertTrue(app.element("crewChip-Kolonne Huber").waitForExistence(timeout: 15), app.debugDescription)
        XCTAssertTrue(app.text(containing: "fehlt: Tobias Lang").waitForExistence(timeout: 5), app.debugDescription)
        app.navigationBars.buttons.element(boundBy: 0).tap()

        app.tapElement(app.tabBars.buttons["Bauakten"].firstMatch, "Reiter Bauakten")
        let efh = app.staticTexts["EFH Schneider, Neubau"].firstMatch
        XCTAssertTrue(efh.waitForExistence(timeout: 20), app.debugDescription)
        app.tapElement(efh, "EFH Schneider")
        XCTAssertTrue(app.element("bauakteTitle").waitForExistence(timeout: 15), app.debugDescription)

        // Fotos: Vorschaubild antippen, Großansicht lädt das Bild, Blättern zum nächsten Foto.
        let thumb = app.element("photoThumb-0")
        XCTAssertTrue(thumb.waitForExistence(timeout: 10) || app.reveal(thumb), app.debugDescription)
        app.tapElement(thumb, "erstes Foto")
        XCTAssertTrue(app.element("photoFullImage").waitForExistence(timeout: 20), app.debugDescription)
        XCTAssertTrue(app.staticTexts["1 von 3"].waitForExistence(timeout: 5), app.debugDescription)
        app.swipeLeft()
        XCTAssertTrue(app.staticTexts["2 von 3"].waitForExistence(timeout: 10), app.debugDescription)
        XCTAssertTrue(app.element("photoFullImage").waitForExistence(timeout: 20), app.debugDescription)
        XCTAssertFalse(app.element("photoUnavailable").exists)
        app.tap("closePhotoViewer")

        // Dokument: Vorschau per QuickLook mit dem PDF vom (Demo-)Server.
        let document = app.descendants(matching: .any).matching(NSPredicate(format: "identifier BEGINSWITH %@", "document-Lieferschein")).firstMatch
        XCTAssertTrue(document.waitForExistence(timeout: 5) || app.reveal(document), app.debugDescription)
        app.tapElement(document, "Lieferschein")
        let quickLookDone = app.buttons.matching(NSPredicate(
            format: "identifier == %@ OR label IN %@", "QLOverlayDoneButtonAccessibilityIdentifier", ["Fertig", "Done"])).firstMatch
        XCTAssertTrue(quickLookDone.waitForExistence(timeout: 20), app.debugDescription)
        XCTAssertFalse(app.alerts["Vorschau nicht möglich"].exists)
        quickLookDone.tap()
        XCTAssertTrue(quickLookDone.waitForNonExistence(timeout: 10), app.debugDescription)
        XCTAssertTrue(document.waitForExistence(timeout: 10), app.debugDescription)

        // Verlassen: zurück zur Einrichtung, Demodaten gelöscht.
        app.tap("endDemoButton")
        XCTAssertTrue(app.element("welcomeTitle").waitForExistence(timeout: 20), app.debugDescription)
    }
}
