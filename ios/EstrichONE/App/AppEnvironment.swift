import EstrichCore
import Foundation
import LocalAuthentication
import UIKit

/// Bündelt alles, was sich zwischen Release-, Debug- und UI-Test-Builds unterscheidet.
@MainActor
struct AppEnvironment {
    let defaults: UserDefaults
    let transport: HTTPTransport
    let secrets: SecretStore
    let storageRoot: URL
    let pinIterations: Int
    /// Persönliches Gerät: Sperre nach so langer Zeit im Hintergrund (Richtlinie, Standard 15 Minuten; 11, Abschnitt 3).
    let personalLockAfter: TimeInterval
    /// Poolgerät: Sperre nach Inaktivität im Vordergrund (Standard 5 Minuten; 11, Abschnitt 4.1).
    let poolInactivityTimeout: TimeInterval
    /// UI-Tests überspringen Face ID (im Simulator nicht bedienbar).
    let skipsDeviceOwnerAuthentication: Bool

    static func live() -> AppEnvironment {
        #if DEBUG
        if let environment = UITestSupport.makeEnvironment() {
            return environment
        }
        #endif
        return AppEnvironment(
            defaults: .standard,
            // Ohne Cookies und ohne Zwischenspeicher.
            transport: URLSession(configuration: .ephemeral),
            secrets: KeychainSecretStore(),
            storageRoot: URL.applicationSupportDirectory.appending(path: "EstrichONE", directoryHint: .isDirectory),
            pinIterations: VaultEnvelope.pinIterations,
            personalLockAfter: 15 * 60,
            poolInactivityTimeout: 5 * 60,
            skipsDeviceOwnerAuthentication: false)
    }

    var appVersion: SemanticVersion {
        SemanticVersion(Self.shortVersion) ?? SemanticVersion(major: 0, minor: 0, patch: 0)
    }

    static var shortVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    }

    static var appVersionText: String {
        let build = Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "?"
        return "\(shortVersion) (\(build))"
    }

    /// `http://` zu lokalen Testservern gibt es nur in Debug-Builds (10, Abschnitt 4).
    static var allowsInsecureLocalServers: Bool {
        #if DEBUG
        true
        #else
        false
        #endif
    }

    func makeAccountService(for server: VerifiedServer) -> AccountService? {
        guard let keys = try? DeviceKeyFactory.loadOrCreate(secrets: secrets) else { return nil }
        return AccountService(api: APIClient(server: server.address, transport: transport),
                              storage: AppStorage(root: storageRoot), keys: keys, secrets: secrets, pinIterations: pinIterations)
    }

    /// „iPhone von Max“ (10, Schritt 4). Ein Name aus dem Einrichtungscode hat Vorrang (setzt der Server).
    func deviceName(for user: UserSummary?) -> String {
        let model = UIDevice.current.model
        guard let firstName = user?.displayName.split(separator: " ").first else { return model }
        return "\(model) von \(firstName)"
    }

    func deviceDescription(for user: UserSummary) -> DeviceDescription {
        DeviceDescription(name: deviceName(for: user), model: Self.modelIdentifier,
                          osVersion: UIDevice.current.systemVersion, appVersion: Self.shortVersion)
    }

    /// Modellkennung wie `iPhone17,1` – offen bereitgestellt, kein Fingerprinting (11, Abschnitt 1).
    static var modelIdentifier: String {
        var info = utsname()
        uname(&info)
        return withUnsafeBytes(of: &info.machine) { buffer in
            String(decoding: buffer.prefix { $0 != 0 }, as: UTF8.self)
        }
    }

    /// Face ID, Touch ID oder Gerätecode. Gibt nur den lokal gespeicherten Schlüssel frei, ersetzt nie die
    /// Serveranmeldung (07, Abschnitt 3).
    func authenticateDeviceOwner() async -> Bool {
        if skipsDeviceOwnerAuthentication { return true }
        let context = LAContext()
        var error: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthentication, error: &error) else {
            // Ohne Gerätecode gibt es keine Sperre, die die App nutzen könnte; der Schlüsselbund bleibt geschützt.
            return true
        }
        do {
            return try await context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: "EstrichONE entsperren")
        } catch {
            return false
        }
    }
}
