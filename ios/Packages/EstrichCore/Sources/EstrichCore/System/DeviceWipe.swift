import Foundation

/// App zurücksetzen: was gelöscht wird und wann. Plattformneutral, damit es unter macOS und Linux testbar ist; das
/// Zusammenspiel mit Schlüsselbund, Push und Mitteilungen steuert die App (`AppWipe`).
///
/// Zurückgesetzt wird
/// - auf Wunsch (Mehr › App zurücksetzen, Anmelde-, Warte- und Fehlerbildschirme),
/// - mit dem Schalter „Beim nächsten Start zurücksetzen“ in der Einstellungen-App (Notausgang, falls die App nicht mehr
///   bis zu einem Bildschirm mit der Schaltfläche kommt),
/// - automatisch beim ersten Start nach einer Neuinstallation: iOS behält Schlüsselbund-Einträge beim Löschen der App,
///   die Einstellungen dagegen nicht. Fehlt der Installationsmerker, Schlüsselbund-Reste sind aber da, stammen sie aus
///   einer früheren Installation und werden gelöscht.
public enum DeviceWipe {
    /// Schlüsselbund-Dienste der App: Betrieb und Demomodus.
    /// `de.estrichone.unlock`: Face-ID-/Gerätecode-Freigabe des Datenschlüssels persönlicher Geräte.
    public static let keychainServices = ["de.estrichone.device", "de.estrichone.demo", "de.estrichone.unlock"]
    /// Einstellungsbereich des Demomodus.
    public static let demoSuite = "de.estrichone.demo"
    /// Merker in den Standard-Einstellungen: Die App lief in dieser Installation schon einmal.
    public static let installMarkerKey = "estrichone.installMarker"
    /// Schalter in der Einstellungen-App (Settings.bundle).
    public static let resetOnLaunchKey = "estrichone.resetOnNextLaunch"

    public enum Reason: String, Sendable, Equatable {
        /// Auf Wunsch in der App.
        case requested
        /// Schalter in der Einstellungen-App.
        case settingsSwitch
        /// Reste einer früheren Installation im Schlüsselbund.
        case reinstall
    }

    /// Entscheidung beim Start: Muss vor dem Aufbau des App-Modells gelöscht werden?
    /// `hasKeychainRemnants` wird nur abgefragt, wenn der Installationsmerker fehlt.
    public static func launchReason(defaults: UserDefaults, hasKeychainRemnants: () -> Bool) -> Reason? {
        if defaults.bool(forKey: resetOnLaunchKey) { return .settingsSwitch }
        if defaults.object(forKey: installMarkerKey) == nil, hasKeychainRemnants() { return .reinstall }
        return nil
    }

    /// Setzt den Installationsmerker (nach jedem Start und nach jedem Zurücksetzen).
    public static func markInstalled(_ defaults: UserDefaults, at date: Date = .now) {
        defaults.set(date.timeIntervalSince1970, forKey: installMarkerKey)
        defaults.removeObject(forKey: resetOnLaunchKey)
    }

    /// Löscht den Inhalt der Ordner; die Ordner selbst bleiben (Caches, tmp und Documents erwartet das System).
    /// Gibt zurück, was sich nicht löschen ließ.
    @discardableResult
    public static func emptyDirectories(_ directories: [URL], fileManager: FileManager = .default) -> [URL] {
        var failed: [URL] = []
        for directory in directories {
            guard let items = try? fileManager.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil,
                                                                    options: []) else { continue }
            for item in items {
                do { try fileManager.removeItem(at: item) } catch { failed.append(item) }
            }
        }
        return failed
    }

    /// Löscht die Ordner samt Inhalt (Datenbereiche der App). Fehlende Ordner sind kein Fehler.
    @discardableResult
    public static func removeDirectories(_ directories: [URL], fileManager: FileManager = .default) -> [URL] {
        var failed: [URL] = []
        for directory in directories where fileManager.fileExists(atPath: directory.path) {
            do { try fileManager.removeItem(at: directory) } catch { failed.append(directory) }
        }
        return failed
    }

    /// Leert einen Einstellungsbereich vollständig: zuerst Schlüssel für Schlüssel (wirkt sofort im Speicher), dann die
    /// Ablage des Bereichs.
    public static func erase(_ defaults: UserDefaults, domain: String) {
        for key in (defaults.persistentDomain(forName: domain) ?? [:]).keys {
            defaults.removeObject(forKey: key)
        }
        defaults.removePersistentDomain(forName: domain)
    }
}
