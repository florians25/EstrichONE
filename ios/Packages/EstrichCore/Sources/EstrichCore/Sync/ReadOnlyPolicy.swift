import Foundation

/// „Nur Ansicht“: Ist der Server länger als 5 Minuten nicht erreichbar, sperrt die App Erfassen und Ändern, damit nicht
/// stundenlang Daten nur auf dem Gerät entstehen. Die Person kann auf eigene Gefahr trotzdem offline weiterarbeiten;
/// sobald der Server wieder erreichbar ist und ein Abgleich gelaufen ist, gilt wieder der Normalbetrieb.
public struct ReadOnlyPolicy: Sendable, Equatable {
    public static let threshold: TimeInterval = 5 * 60

    /// Letzte Antwort des Servers (erfolgreicher Abgleich, Wartung oder fachliche Ablehnung).
    public var lastContact: Date?
    /// Server gerade nicht erreichbar (letzter Lauf ohne Verbindung bzw. Server gestört).
    public var unreachable: Bool
    /// „Trotzdem offline arbeiten (auf eigene Gefahr)“ – gilt bis zur nächsten Verbindung.
    public var overridden: Bool

    public init(lastContact: Date? = nil, unreachable: Bool = false, overridden: Bool = false) {
        self.lastContact = lastContact
        self.unreachable = unreachable
        self.overridden = overridden
    }

    /// Ohne Verbindung seit mindestens 5 Minuten (und nicht übersteuert).
    public func isReadOnly(at now: Date) -> Bool {
        guard unreachable, !overridden, let lastContact else { return false }
        return now.timeIntervalSince(lastContact) >= Self.threshold
    }

    /// Ohne Verbindung seit mindestens 5 Minuten, unabhängig von der Übersteuerung (Hinweis „auf eigene Gefahr“).
    public func isLongOffline(at now: Date) -> Bool {
        guard unreachable, let lastContact else { return false }
        return now.timeIntervalSince(lastContact) >= Self.threshold
    }

    /// Ergebnis eines Abgleichs übernehmen. Ein vollständiger Abgleich beendet auch die Übersteuerung.
    public mutating func record(_ outcome: SyncOutcome, at now: Date) {
        switch outcome {
        case .completed:
            lastContact = now
            unreachable = false
            overridden = false
        case .maintenance, .failed:
            // Der Server hat geantwortet; Wartung sperrt die App gesondert.
            lastContact = now
            unreachable = false
        case .offline, .retryLater:
            unreachable = true
            if lastContact == nil { lastContact = now }
        }
    }
}

/// Fehler beim Speichern, solange die App im Modus „Nur Ansicht“ ist.
public struct ReadOnlyModeError: LocalizedError, Equatable, Sendable {
    public init() {}
    public var errorDescription: String? { Self.message }
    public static let message = "Nur Ansicht: Der Server ist seit über 5 Minuten nicht erreichbar. Erfassen und Ändern sind gesperrt, bis die Verbindung wieder steht."
}
