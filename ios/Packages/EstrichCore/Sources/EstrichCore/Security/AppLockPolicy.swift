import Foundation

/// Wann ein persönliches Gerät die App sperrt (Mehr › Sperre). Entsperrt wird mit Face ID/Touch ID bzw. dem Gerätecode,
/// nie mit dem Passwort – das braucht es nur, wenn der Server die Sitzung beendet hat (`SessionEnd`).
public enum LockDelay: Int, CaseIterable, Identifiable, Sendable {
    case immediately = 0
    case oneMinute = 60
    case fiveMinutes = 300
    case fifteenMinutes = 900
    case oneHour = 3600

    public var id: Int { rawValue }
    public var seconds: TimeInterval { TimeInterval(rawValue) }

    public var title: String {
        switch self {
        case .immediately: "Sofort"
        case .oneMinute: "Nach 1 Minute"
        case .fiveMinutes: "Nach 5 Minuten"
        case .fifteenMinutes: "Nach 15 Minuten"
        case .oneHour: "Nach 1 Stunde"
        }
    }
}

/// Einstellungen der App-Sperre auf persönlichen Geräten, gespeichert in den Benutzereinstellungen des Geräts.
/// Poolgeräte sperren unabhängig davon beim Verlassen der App sofort und nach 5 Minuten Inaktivität.
public struct AppLockPreferences: Sendable {
    public static let biometricKey = "estrichone.lock.biometric"
    public static let delayKey = "estrichone.lock.delay"

    /// Mit Face ID/Touch ID entsperren (aus: nur Gerätecode).
    public var biometricUnlock: Bool
    public var delay: LockDelay

    public init(biometricUnlock: Bool = true, delay: LockDelay = .fifteenMinutes) {
        self.biometricUnlock = biometricUnlock
        self.delay = delay
    }

    public static func load(from defaults: UserDefaults) -> AppLockPreferences {
        let biometric = defaults.object(forKey: biometricKey) as? Bool ?? true
        let delay = (defaults.object(forKey: delayKey) as? Int).flatMap(LockDelay.init(rawValue:)) ?? .fifteenMinutes
        return AppLockPreferences(biometricUnlock: biometric, delay: delay)
    }

    public func save(to defaults: UserDefaults) {
        defaults.set(biometricUnlock, forKey: Self.biometricKey)
        defaults.set(delay.rawValue, forKey: Self.delayKey)
    }

    /// Beim Verlassen der App sofort sperren?
    public var locksOnBackground: Bool { delay == .immediately }

    /// Beim Zurückkehren sperren, wenn die App mindestens so lange im Hintergrund war.
    public func shouldLock(backgroundSince: Date, now: Date) -> Bool {
        now.timeIntervalSince(backgroundSince) >= delay.seconds
    }
}

/// Prüfsumme der eingerichteten Gesichter/Finger (`LAContext.evaluatedPolicyDomainState`). Ändert sie sich, gilt die
/// Face-ID-Freigabe nicht mehr: einmal Gerätecode, danach wird sie mit den neuen Merkmalen neu angelegt.
public enum BiometryEnrollment {
    public static func hasChanged(stored: Data?, current: Data?) -> Bool {
        guard let stored, let current else { return false }
        return stored != current
    }
}
