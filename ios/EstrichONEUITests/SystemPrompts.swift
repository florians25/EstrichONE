import XCTest

extension XCUIApplication {
    /// Nach der Anmeldung mit Passwort bietet iOS „Passwort sichern?“ an. Das Blatt liegt über der App
    /// (PIN-Tasten, Schnellanlage); für echte Benutzer ist es erwünscht, im Test wird es geschlossen.
    /// Beim ersten Start nach der Installation erscheint es teils verzögert oder im SpringBoard-Prozess.
    @MainActor
    func dismissSavePasswordPrompt(timeout: TimeInterval = 4) {
        let labels = ["Not Now", "Jetzt nicht", "Nicht jetzt"]
        let springboard = XCUIApplication(bundleIdentifier: "com.apple.springboard")
        let candidates = [self, springboard].map {
            $0.buttons.matching(NSPredicate(format: "label IN %@", labels)).firstMatch
        }
        let deadline = Date().addingTimeInterval(timeout)
        repeat {
            if let notNow = candidates.first(where: { $0.exists }) {
                notNow.tap()
                _ = notNow.waitForNonExistence(timeout: 4)
                return
            }
            Thread.sleep(forTimeInterval: 0.25)
        } while Date() < deadline
    }
}
