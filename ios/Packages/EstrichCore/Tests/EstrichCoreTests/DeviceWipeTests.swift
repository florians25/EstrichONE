import Foundation
import Testing
@testable import EstrichCore

@Suite("App zurücksetzen")
struct DeviceWipeTests {
    private func makeDefaults() -> (UserDefaults, String) {
        let name = "estrichone.wipetests.\(UUID().uuidString)"
        return (UserDefaults(suiteName: name)!, name)
    }

    @Test("Start: Schalter in den Einstellungen, Neuinstallation mit Schlüsselbund-Resten, normaler Start")
    func launchReason() {
        let (defaults, name) = makeDefaults()
        defer { DeviceWipe.erase(defaults, domain: name) }

        // Erster Start ohne Reste: nichts löschen, Schlüsselbund wird abgefragt.
        var asked = false
        #expect(DeviceWipe.launchReason(defaults: defaults) { asked = true; return false } == nil)
        #expect(asked)

        // Neuinstallation: Merker fehlt, Schlüsselbund hat Einträge.
        #expect(DeviceWipe.launchReason(defaults: defaults) { true } == .reinstall)

        // Nach dem Start ist der Merker gesetzt: Schlüsselbund-Einträge sind dann die eigenen.
        DeviceWipe.markInstalled(defaults)
        asked = false
        #expect(DeviceWipe.launchReason(defaults: defaults) { asked = true; return true } == nil)
        #expect(!asked)

        // Schalter in der Einstellungen-App hat Vorrang; der Merker setzt ihn zurück.
        defaults.set(true, forKey: DeviceWipe.resetOnLaunchKey)
        #expect(DeviceWipe.launchReason(defaults: defaults) { false } == .settingsSwitch)
        DeviceWipe.markInstalled(defaults)
        #expect(!defaults.bool(forKey: DeviceWipe.resetOnLaunchKey))
        #expect(DeviceWipe.launchReason(defaults: defaults) { false } == nil)
    }

    @Test("Einstellungsbereich wird vollständig geleert")
    func eraseDefaults() {
        let (defaults, name) = makeDefaults()
        defaults.set("https://estrich.example", forKey: "server")
        defaults.set(true, forKey: "estrichone.demoMode")
        DeviceWipe.markInstalled(defaults)

        DeviceWipe.erase(defaults, domain: name)

        #expect(defaults.object(forKey: "server") == nil)
        #expect(defaults.object(forKey: "estrichone.demoMode") == nil)
        #expect(defaults.object(forKey: DeviceWipe.installMarkerKey) == nil)
        #expect(UserDefaults(suiteName: name)?.object(forKey: "server") == nil)
    }

    @Test("Datenordner werden gelöscht, Systemordner geleert")
    func files() throws {
        let fileManager = FileManager.default
        let base = fileManager.temporaryDirectory.appendingPathComponent("wipe-\(UUID().uuidString)", isDirectory: true)
        defer { try? fileManager.removeItem(at: base) }
        let data = base.appendingPathComponent("EstrichONE", isDirectory: true)
        let demo = base.appendingPathComponent("EstrichONE-Demo", isDirectory: true)
        let caches = base.appendingPathComponent("Caches", isDirectory: true)
        for folder in [data.appendingPathComponent("vault"), demo, caches.appendingPathComponent("thumbs")] {
            try fileManager.createDirectory(at: folder, withIntermediateDirectories: true)
        }
        try Data("db".utf8).write(to: data.appendingPathComponent("vault/store.sqlite"))
        try Data("x".utf8).write(to: caches.appendingPathComponent("thumbs/a.jpg"))
        try Data("y".utf8).write(to: caches.appendingPathComponent("b.tmp"))

        let missing = base.appendingPathComponent("gibt-es-nicht", isDirectory: true)
        #expect(DeviceWipe.removeDirectories([data, demo, missing]).isEmpty)
        #expect(DeviceWipe.emptyDirectories([caches, missing]).isEmpty)

        #expect(!fileManager.fileExists(atPath: data.path))
        #expect(!fileManager.fileExists(atPath: demo.path))
        #expect(fileManager.fileExists(atPath: caches.path))
        #expect(try fileManager.contentsOfDirectory(atPath: caches.path).isEmpty)
    }

    @Test("Zähler: ausgeschrieben bis 99, darüber 99+, Ansage für Bedienungshilfen")
    func badge() {
        #expect(UnreadBadge.text(0) == nil)
        #expect(UnreadBadge.text(-3) == nil)
        #expect(UnreadBadge.text(1) == "1")
        #expect(UnreadBadge.text(99) == "99")
        #expect(UnreadBadge.text(100) == "99+")
        #expect(UnreadBadge.text(4_711) == "99+")
        #expect(UnreadBadge.accessibilityLabel(0) == "Benachrichtigungen, keine ungelesen")
        #expect(UnreadBadge.accessibilityLabel(1) == "Benachrichtigungen, 1 ungelesen")
        #expect(UnreadBadge.accessibilityLabel(120) == "Benachrichtigungen, 120 ungelesen")
        #expect(UnreadBadge.appBadge(-1) == 0)
    }
}
