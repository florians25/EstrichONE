import EstrichCore
import SwiftUI

/// Name des Bildschirms, den der Screenshot-Modus gerade zeigt (z. B. „bauakte“, „neu-2“); sonst `nil`.
/// Ansichten lesen den Wert, um für die Aufnahme den passenden Zustand herzustellen (Reiter, geöffnete Seite, Blatt).
/// Gesetzt wird er nur in Debug-Builds (`ScreenshotDirector`); in Release-Builds ist er immer `nil`.
private struct ShotSceneKey: EnvironmentKey {
    static let defaultValue: String? = nil
}

extension EnvironmentValues {
    var shotScene: String? {
        get { self[ShotSceneKey.self] }
        set { self[ShotSceneKey.self] = newValue }
    }
}

/// Aufnahmen für den App Store (Launch-Argument `-EstrichAppStore YES`, nur Debug-Builds): ohne Demo-Kennzeichnung,
/// damit die Bilder die App wie im Betrieb zeigen. In Release-Builds immer `false`.
enum ShotOptions {
    static var hidesDemoMarks: Bool {
        #if DEBUG
        UserDefaults.standard.bool(forKey: "EstrichAppStore")
        #else
        false
        #endif
    }
}

#if DEBUG
/// Automatische Screenshots für die visuelle Prüfung in der CI (ios/scripts/screenshots.sh). Nur in Debug-Builds.
///
/// Start mit dem Launch-Argument `-EstrichShots heute,baustellen,bauakte,…`: Die App startet den Demomodus mit frischen
/// Beispieldaten, wartet auf den ersten Abgleich und zeigt die Bildschirme nacheinander. Je Bildschirm schreibt sie
/// `ready:<name>` in `Documents/shot-state` und wartet, bis das Skript `Documents/shot-next` anlegt (nach den Aufnahmen
/// hell und dunkel). Zum Schluss steht dort `done`.
@MainActor
@Observable
final class ScreenshotDirector {
    let scenes: [String]
    private(set) var current: String?

    private init(scenes: [String]) {
        self.scenes = scenes
    }

    static func fromLaunchArguments() -> ScreenshotDirector? {
        guard let list = UserDefaults.standard.string(forKey: "EstrichShots") else { return nil }
        let scenes = list.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        return scenes.isEmpty ? nil : ScreenshotDirector(scenes: scenes)
    }

    /// Frischer Demomodus: Einstellungen, Daten und Geräteschlüssel der Demo löschen.
    static func resetDemoData() {
        DemoSwitches.shared.reset()
        let suite = "de.estrichone.demo"
        UserDefaults().removePersistentDomain(forName: suite)
        try? FileManager.default.removeItem(at: URL.applicationSupportDirectory.appending(path: "EstrichONE-Demo", directoryHint: .isDirectory))
        try? KeychainSecretStore(service: suite).remove(DeviceKeyFactory.storageKey)
        UserDefaults.standard.removeObject(forKey: "newBauakteMode")
        UserDefaults.standard.removeObject(forKey: "inspectionMode")
    }

    private var folder: URL { URL.documentsDirectory }
    private var stateURL: URL { folder.appending(path: "shot-state") }
    private var nextURL: URL { folder.appending(path: "shot-next") }

    /// Zeigt alle Bildschirme nacheinander, sobald Anmeldung und erster Abgleich der Demo fertig sind.
    func run(model: @escaping @MainActor () -> AppModel?) async {
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try? FileManager.default.removeItem(at: nextURL)
        write("starting")
        var waited = 0
        while waited < 600, model()?.active?.sync?.lastOutcome == nil {
            try? await Task.sleep(for: .milliseconds(100))
            waited += 1
        }
        // Keine weiteren Abgleiche während der Aufnahmen: der Stand bleibt ruhig.
        model()?.active?.sync?.stop()
        try? await Task.sleep(for: .milliseconds(800))
        for scene in scenes {
            // „…-quer“: gleicher Bildschirm im Querformat (iPad; gedreht wird der Simulator vom Skript).
            let base = scene.hasSuffix("-quer") ? String(scene.dropLast(5)) : scene
            prepare(base, model: model())
            current = base
            try? await Task.sleep(for: .milliseconds(Self.settleMilliseconds(scene)))
            write("ready:\(scene)")
            var polls = 0
            while polls < 900 {
                if FileManager.default.fileExists(atPath: nextURL.path()) {
                    try? FileManager.default.removeItem(at: nextURL)
                    break
                }
                try? await Task.sleep(for: .milliseconds(100))
                polls += 1
            }
        }
        current = nil
        write("done")
    }

    /// Zustand außerhalb der Ansichten (z. B. Wartung des Servers).
    private func prepare(_ scene: String, model: AppModel?) {
        let sync = model?.active?.sync
        if scene.hasPrefix("wartung") {
            sync?.simulateMaintenance("Update auf Version 0.3 – voraussichtlich bis 7:30 Uhr.")
        } else {
            sync?.simulateMaintenance(nil)
        }
        // „stoerung“: Backup- und Archivserver ausgefallen, Server seit 12 Minuten nicht erreichbar (Nur Ansicht).
        if scene.hasPrefix("stoerung") {
            let now = Date()
            sync?.simulateHealth(SystemHealth(
                backupTarget: .init(configured: true, reachable: false, lastSuccessAt: now.addingTimeInterval(-31 * 3600), severity: "critical"),
                archive: .init(configured: true, reachable: false, lastSuccessAt: now.addingTimeInterval(-9 * 86400), severity: "warning")))
            sync?.simulateOutage(minutes: 12)
        } else {
            sync?.simulateHealth(nil)
            sync?.simulateOutage(minutes: nil)
        }
    }

    /// Blätter und Seitenwechsel brauchen etwas länger, bis alles steht.
    private static func settleMilliseconds(_ scene: String) -> Int {
        if scene == "postfach" { return 2600 }
        let sheets = ["erfassen", "neu-", "erstbegehung", "abnahme", "foto-", "tagebuch-neu", "mangel-neu", "raum", "basisdaten-neu", "kontakt-neu", "dispo-"]
        if scene.hasSuffix("-quer") { return 3200 }
        // Bereiche öffnen sich nach dem Laden der Bauakte per Navigation – die Animation braucht etwas länger.
        if scene.hasPrefix("bereich-") || scene == "protokoll" || scene.hasPrefix("chat") { return 2800 }
        if scene == "kontakt-neu" || scene.hasPrefix("dispo-") { return 3400 }
        return sheets.contains { scene.hasPrefix($0) } ? 2800 : 2000
    }

    private func write(_ text: String) {
        let temporary = folder.appending(path: "shot-state.tmp")
        do {
            try Data(text.utf8).write(to: temporary)
            try? FileManager.default.removeItem(at: stateURL)
            try FileManager.default.moveItem(at: temporary, to: stateURL)
        } catch {
            print("ScreenshotDirector: \(error)")
        }
    }
}
#endif
