import XCTest

/// Robuste Bedienung für die UI-Tests. Im CI-Simulator laufen Übergänge (Blätter, Navigation, Tastatur) teils
/// spürbar langsamer: Es wird daher nie „blind“ getippt, sondern auf Existenz, Erreichbarkeit und Fokus gewartet,
/// und verdeckte oder außerhalb liegende Elemente werden zuerst sichtbar gemacht.
extension XCUIApplication {
    @MainActor
    func element(_ identifier: String) -> XCUIElement {
        descendants(matching: .any).matching(identifier: identifier).firstMatch
    }

    /// Wartet, bis die Bedingung für das Element erfüllt ist (ohne Testfehler).
    @MainActor
    @discardableResult
    func wait(for element: XCUIElement, _ format: String, _ arguments: CVarArg..., timeout: TimeInterval = 10) -> Bool {
        let expectation = XCTNSPredicateExpectation(predicate: NSPredicate(format: format, argumentArray: arguments), object: element)
        return XCTWaiter().wait(for: [expectation], timeout: timeout) == .completed
    }

    /// Wartet, bis das Element erreichbar ist. Schließt dabei verspätete Systemblätter („Passwort sichern?“).
    @MainActor
    @discardableResult
    func waitUntilHittable(_ element: XCUIElement, timeout: TimeInterval = 8) -> Bool {
        let start = Date()
        var dismissedPrompt = false
        while Date().timeIntervalSince(start) < timeout {
            if element.exists, element.isHittable { return true }
            if !dismissedPrompt, Date().timeIntervalSince(start) > 2.5 {
                dismissLateSystemPrompt()
                dismissedPrompt = true
            }
            RunLoop.current.run(until: Date().addingTimeInterval(0.25))
        }
        return element.exists && element.isHittable
    }

    /// Tippt ein Element an, sobald es existiert und erreichbar ist.
    @MainActor
    func tapElement(_ target: XCUIElement, _ name: String, timeout: TimeInterval = 15,
                    file: StaticString = #filePath, line: UInt = #line) {
        // Nicht gerenderte Listenzeilen existieren erst, wenn sie in den sichtbaren Bereich gescrollt sind.
        guard target.waitForExistence(timeout: min(timeout, 6)) || reveal(target) || target.waitForExistence(timeout: timeout) else {
            XCTFail("\(name) erscheint nicht\n\(debugDescription)", file: file, line: line)
            return
        }
        if waitUntilHittable(target) {
            target.tap()
            return
        }
        // Häufigste Ursache: Tastatur samt darüberliegender Leiste verdeckt das Element.
        if dismissKeyboard(), waitUntilHittable(target, timeout: 4) {
            target.tap()
            return
        }
        // Noch in Bewegung oder teilweise verdeckt: Mitte des Rahmens antippen, falls sie im Fenster liegt;
        // sonst scrollt XCUITest das Element selbst heran.
        let frame = target.frame
        let window = windows.firstMatch.frame
        let center = CGPoint(x: frame.midX, y: frame.midY)
        let keyboard = keyboards.firstMatch
        let underKeyboard = keyboard.exists && keyboard.frame.contains(center)
        if !frame.isEmpty, window.contains(center), !underKeyboard {
            target.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).tap()
        } else {
            target.tap()
        }
    }

    /// Schließt die Tastatur über ihre Eingabetaste. `true`, wenn eine Tastatur offen war.
    @MainActor
    @discardableResult
    func dismissKeyboard() -> Bool {
        let keyboard = keyboards.firstMatch
        guard keyboard.exists else { return false }
        let returnKey = keyboard.buttons.matching(NSPredicate(format: "identifier IN %@", ["Return", "Search", "Done", "Go"])).firstMatch
        if returnKey.exists { returnKey.tap() }
        _ = keyboard.waitForNonExistence(timeout: 3)
        return true
    }

    @MainActor
    func tap(_ identifier: String, timeout: TimeInterval = 15, file: StaticString = #filePath, line: UInt = #line) {
        tapElement(element(identifier), identifier, timeout: timeout, file: file, line: line)
    }

    /// Text eingeben, nachdem das Feld den Tastaturfokus hat.
    @MainActor
    func type(_ text: String, into identifier: String, file: StaticString = #filePath, line: UInt = #line) {
        let field = element(identifier)
        tapElement(field, identifier, file: file, line: line)
        if !wait(for: field, "hasKeyboardFocus == true", timeout: 3) {
            field.tap()
            wait(for: field, "hasKeyboardFocus == true", timeout: 3)
        }
        field.typeText(text)
    }

    /// Scrollt die aktuelle Liste, bis das Element existiert und erreichbar ist (erst nach unten, dann nach oben).
    @MainActor
    @discardableResult
    func reveal(_ target: XCUIElement) -> Bool {
        if target.exists, target.isHittable { return true }
        for _ in 0..<8 {
            swipeUp(velocity: .slow)
            if target.waitForExistence(timeout: 0.5), target.isHittable { return true }
        }
        for _ in 0..<14 {
            swipeDown(velocity: .slow)
            if target.waitForExistence(timeout: 0.5), target.isHittable { return true }
        }
        return target.exists && target.isHittable
    }

    /// Segment eines Umschalters wählen (z. B. „Formular“ statt „Assistent“).
    @MainActor
    func chooseSegment(_ title: String, file: StaticString = #filePath, line: UInt = #line) {
        let segment = segmentedControls.buttons[title].firstMatch
        tapElement(segment, "Segment \(title)", file: file, line: line)
        wait(for: segment, "isSelected == true", timeout: 3)
    }

    @MainActor
    func reveal(_ identifier: String) -> XCUIElement {
        let target = element(identifier)
        reveal(target)
        return target
    }

    /// Statischer Text, der den Inhalt enthält (Zeilen kombinieren ihre Beschriftungen teils).
    @MainActor
    func text(containing content: String) -> XCUIElement {
        descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", content)).firstMatch
    }
}
