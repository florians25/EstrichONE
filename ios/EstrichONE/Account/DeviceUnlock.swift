import CryptoKit
import EstrichCore
import Foundation
import LocalAuthentication
import Security

/// Biometrie des Geräts – für Beschriftungen („Mit Face ID entsperren“) und Symbole.
enum BiometryKind: Equatable {
    case faceID, touchID, opticID, none

    /// Was das Gerät kann (auch wenn die Person es in der App ausgeschaltet hat).
    static var current: BiometryKind {
        let context = LAContext()
        var error: NSError?
        _ = context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error)
        switch context.biometryType {
        case .faceID: return .faceID
        case .touchID: return .touchID
        case .opticID: return .opticID
        default: return .none
        }
    }

    var name: String {
        switch self {
        case .faceID: "Face ID"
        case .touchID: "Touch ID"
        case .opticID: "Optic ID"
        case .none: "Gerätecode"
        }
    }

    var symbol: String {
        switch self {
        case .faceID: "faceid"
        case .touchID: "touchid"
        case .opticID: "opticid"
        case .none: "lock.open"
        }
    }
}

/// Ergebnis des Entsperrens über den Schlüsselbund.
enum UnlockKeyResult: Sendable {
    case key(SymmetricKey)
    /// Abgebrochen (z. B. „Abbrechen“ im Systemdialog) – gesperrt bleiben, nichts melden.
    case cancelled
    /// Keine Freigabe gespeichert (ältere App-Version, Gerätecode erst neu gesetzt) – einmal über den bisherigen Weg.
    case missing
    case failed
}

/// Freigabe des Datenschlüssels eines persönlichen Geräts im Schlüsselbund, geschützt durch das System:
///
/// - „Mit Face ID entsperren“ an: `SecAccessControl` mit `.biometryCurrentSet` **oder** `.devicePasscode`. Werden
///   Gesichter bzw. Finger hinzugefügt oder entfernt, akzeptiert iOS Face ID für diesen Eintrag nicht mehr – entsperrt
///   wird dann einmal mit dem Gerätecode, danach legt die App den Eintrag mit den neuen Merkmalen neu an.
/// - aus: nur `.devicePasscode` (Gerätecode, nie Face ID).
///
/// Immer `WhenPasscodeSetThisDeviceOnly`: nicht in Backups, nicht auf andere Geräte übertragbar, verschwindet, wenn der
/// Gerätecode entfernt wird. Das Passwort ist zum Entsperren nie nötig – nur wenn der Server die Sitzung beendet hat.
struct UnlockKeychain {
    static let service = "de.estrichone.unlock"
    let defaults: UserDefaults

    private static func domainStateKey(_ userId: UUID) -> String { "estrichone.lock.domain.\(userId.uuidString)" }

    /// Prüfsumme der eingerichteten Gesichter/Finger (ändert sich bei jeder Änderung).
    static var currentDomainState: Data? {
        let context = LAContext()
        var error: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) else { return nil }
        return context.evaluatedPolicyDomainState
    }

    /// Ist auf dem Gerät ein Gerätecode gesetzt? Ohne ihn gibt es keine Systemsperre, die die App nutzen könnte.
    static var hasDevicePasscode: Bool {
        LAContext().canEvaluatePolicy(.deviceOwnerAuthentication, error: nil)
    }

    @discardableResult
    func save(_ key: SymmetricKey, userId: UUID, biometric: Bool) -> Bool {
        remove(userId: userId)
        let flags: SecAccessControlCreateFlags = biometric ? [.biometryCurrentSet, .or, .devicePasscode] : [.devicePasscode]
        var error: Unmanaged<CFError>?
        guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly, flags, &error) else {
            return false
        }
        var query = Self.query(userId)
        query[kSecValueData as String] = key.withUnsafeBytes { Data($0) }
        query[kSecAttrAccessControl as String] = access
        let status = SecItemAdd(query as CFDictionary, nil)
        if status == errSecSuccess {
            defaults.set(biometric ? Self.currentDomainState : nil, forKey: Self.domainStateKey(userId))
        }
        return status == errSecSuccess
    }

    /// Zeigt Face ID/Touch ID bzw. die Gerätecode-Abfrage des Systems und gibt den Datenschlüssel frei.
    static func load(userId: UUID, reason: String) async -> UnlockKeyResult {
        // SecItemCopyMatching wartet auf die Bestätigung im Systemdialog: nicht auf dem Hauptthread.
        let (status, data) = await Task.detached(priority: .userInitiated) { () -> (OSStatus, Data?) in
            let context = LAContext()
            context.localizedReason = reason
            context.localizedFallbackTitle = "Gerätecode eingeben"
            var request = UnlockKeychain.query(userId)
            request[kSecReturnData as String] = true
            request[kSecMatchLimit as String] = kSecMatchLimitOne
            request[kSecUseAuthenticationContext as String] = context
            var result: CFTypeRef?
            let status = SecItemCopyMatching(request as CFDictionary, &result)
            return (status, result as? Data)
        }.value
        switch status {
        case errSecSuccess:
            guard let data, data.count == 32 else { return .failed }
            return .key(SymmetricKey(data: data))
        case errSecUserCanceled: return .cancelled
        case errSecItemNotFound: return .missing
        default: return .failed
        }
    }

    /// Liegt eine Freigabe vor? Fragt nur Attribute ab – ohne Systemdialog.
    func exists(userId: UUID) -> Bool {
        let context = LAContext()
        context.interactionNotAllowed = true
        var query = Self.query(userId)
        query[kSecReturnAttributes as String] = true
        query[kSecUseAuthenticationContext as String] = context
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        return status == errSecSuccess || status == errSecInteractionNotAllowed
    }

    /// Haben sich die eingerichteten Gesichter/Finger seit dem Anlegen der Freigabe geändert?
    func biometryChanged(userId: UUID) -> Bool {
        BiometryEnrollment.hasChanged(stored: defaults.data(forKey: Self.domainStateKey(userId)), current: Self.currentDomainState)
    }

    func remove(userId: UUID) {
        SecItemDelete(Self.query(userId) as CFDictionary)
    }

    private static func query(_ userId: UUID) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: userId.uuidString]
    }
}
