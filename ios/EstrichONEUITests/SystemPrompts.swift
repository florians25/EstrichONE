import XCTest

extension XCUIApplication {
    /// Nach der Anmeldung mit Passwort bietet iOS „Passwort sichern?“ an. Das Blatt liegt über der App
    /// (PIN-Tasten, Schnellanlage); für echte Benutzer ist es erwünscht, im Test wird es geschlossen.
    @MainActor
    func dismissSavePasswordPrompt(timeout: TimeInterval = 4) {
        let notNow = buttons.matching(NSPredicate(format: "label IN %@", ["Not Now", "Jetzt nicht", "Nicht jetzt"])).firstMatch
        if notNow.waitForExistence(timeout: timeout) {
            notNow.tap()
            _ = notNow.waitForNonExistence(timeout: 4)
        }
    }
}
