#if DEBUG
import EstrichCore
import Foundation
import UIKit

/// Steuerung für XCUITests über Umgebungsvariablen. Nur in Debug-Builds enthalten.
///
/// - `ESTRICHONE_UITEST=1`: eigener Einstellungs- und Datenbereich, bei jedem Start geleert,
///   außer mit `ESTRICHONE_UITEST_KEEP=1` (Neustart-Tests)
/// - `ESTRICHONE_UITEST_SERVER_STATUS` / `ESTRICHONE_UITEST_SERVER_BODY`: feste Antwort für `server-info`
/// - `ESTRICHONE_UITEST_PASSWORD`: Passwort, das der Testserver annimmt (Standard „Richtig-2026“)
/// - `ESTRICHONE_UITEST_DEVICE_STATUS`: Status neu registrierter Geräte ohne Einrichtungscode (`active`/`pending`)
enum UITestSupport {
    private static var environment: [String: String] { ProcessInfo.processInfo.environment }

    static var isActive: Bool { environment["ESTRICHONE_UITEST"] == "1" }

    /// Erzeugtes JPEG statt Kamera (der Simulator hat keine Kamera).
    @MainActor
    static func samplePhoto() -> Data {
        let renderer = UIGraphicsImageRenderer(size: CGSize(width: 640, height: 480))
        let image = renderer.image { context in
            UIColor(red: 0.04, green: 0.18, blue: 0.36, alpha: 1).setFill()
            context.fill(CGRect(x: 0, y: 0, width: 640, height: 480))
            UIColor.white.setFill()
            context.fill(CGRect(x: 200, y: 140, width: 240, height: 200))
        }
        return image.jpegData(compressionQuality: 0.85) ?? Data()
    }

    @MainActor
    static func makeEnvironment() -> AppEnvironment? {
        guard environment["ESTRICHONE_UITEST"] == "1" else { return nil }
        let keep = environment["ESTRICHONE_UITEST_KEEP"] == "1"
        let suite = "de.estrichone.uitest"
        let root = URL.applicationSupportDirectory.appending(path: "EstrichONE-UITest", directoryHint: .isDirectory)
        let secrets = KeychainSecretStore(service: suite)
        if !keep {
            UserDefaults.standard.removeObject(forKey: AppEnvironment.demoFlagKey)
            UserDefaults().removePersistentDomain(forName: suite)
            try? FileManager.default.removeItem(at: root)
            try? secrets.remove(DeviceKeyFactory.storageKey)
        }
        return AppEnvironment(
            defaults: UserDefaults(suiteName: suite) ?? .standard,
            transport: FakeServer(
                serverInfoStatus: environment["ESTRICHONE_UITEST_SERVER_STATUS"].flatMap(Int.init) ?? 200,
                serverInfoBody: environment["ESTRICHONE_UITEST_SERVER_BODY"] ?? FakeServer.defaultServerInfo,
                password: environment["ESTRICHONE_UITEST_PASSWORD"] ?? "Richtig-2026",
                newDeviceStatus: environment["ESTRICHONE_UITEST_DEVICE_STATUS"] ?? "active"),
            secrets: secrets,
            storageRoot: root,
            pinIterations: 1_000,
            personalLockAfter: 15 * 60,
            poolInactivityTimeout: 5 * 60,
            skipsDeviceOwnerAuthentication: true)
    }
}
#endif
