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

    /// Wie oben, aber auch im SpringBoard: Beim ersten Start nach der Installation erscheint das Blatt teils verspätet
    /// dort. Nur verwenden, wenn ein Element sichtbar, aber verdeckt ist.
    @MainActor
    func dismissLateSystemPrompt() {
        dismissSavePasswordPrompt(timeout: 1)
        let springboard = XCUIApplication(bundleIdentifier: "com.apple.springboard")
        let notNow = springboard.buttons.matching(NSPredicate(format: "label IN %@", ["Not Now", "Jetzt nicht", "Nicht jetzt"])).firstMatch
        if notNow.waitForExistence(timeout: 2) {
            notNow.tap()
            _ = notNow.waitForNonExistence(timeout: 4)
        }
    }
}
