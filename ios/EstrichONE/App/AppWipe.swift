import EstrichCore
import Foundation
import SwiftUI
import UserNotifications

/// Zentrale Löschroutine „App zurücksetzen“ – unabhängig davon, in welchem Zustand die App ist (angemeldet, gesperrt,
/// Datenbank nicht lesbar, Einrichtung halb fertig). Was gelöscht wird und wann: `DeviceWipe` (EstrichCore).
///
/// Ablauf auf Wunsch (`AppSession.reset`): Synchronisation stoppen und Push abmelden (`AppModel.shutdownForWipe`),
/// Oberfläche abbauen, dann `eraseLocalData()`, danach ein frisches App-Modell – die App startet bei der Einrichtung.
@MainActor
enum AppWipe {
    /// Schlüsselbund-Dienste, die gelöscht werden (Debug-Builds zusätzlich die der UI-Tests).
    static var keychainServices: [String] {
        #if DEBUG
        DeviceWipe.keychainServices + ["de.estrichone.uitest"]
        #else
        DeviceWipe.keychainServices
        #endif
    }

    /// Löscht alles, was die App auf dem Gerät abgelegt hat. Darf nur laufen, wenn kein App-Modell mehr Daten offen hat.
    static func eraseLocalData() {
        // Schlüsselbund: alle Einträge der Dienste, nicht nur bekannte Schlüsselnamen.
        for service in keychainServices {
            do {
                try KeychainSecretStore(service: service).removeAll()
            } catch {
                DiagnosticsLog.shared.record("WIPE_KEYCHAIN_FAILED", ["status": Int((error as? SecretStoreError)?.status ?? 0)])
            }
        }

        // Dateien: Datenbereiche (Betrieb, Demo) samt Ordner, Systemordner nur leeren.
        let support = URL.applicationSupportDirectory
        DeviceWipe.removeDirectories([
            support.appending(path: "EstrichONE", directoryHint: .isDirectory),
            support.appending(path: "EstrichONE-Demo", directoryHint: .isDirectory),
        ])
        DeviceWipe.emptyDirectories([support, URL.cachesDirectory, FileManager.default.temporaryDirectory, URL.documentsDirectory])

        // Netzwerk-Zwischenspeicher und Cookies (die App nutzt sie nicht, Systemdienste ggf. schon).
        URLCache.shared.removeAllCachedResponses()
        HTTPCookieStorage.shared.cookies?.forEach { HTTPCookieStorage.shared.deleteCookie($0) }

        // Mitteilungen und Zahl am App-Symbol.
        let center = UNUserNotificationCenter.current()
        center.removeAllPendingNotificationRequests()
        center.removeAllDeliveredNotifications()
        center.setBadgeCount(0) { _ in }
        PushCenter.shared.resetAfterWipe()

        // Einstellungen: App (inkl. Demomodus-Merker und Schalter aus der Einstellungen-App) und Demo-Bereich.
        if let bundle = Bundle.main.bundleIdentifier {
            DeviceWipe.erase(.standard, domain: bundle)
        }
        if let demo = UserDefaults(suiteName: DeviceWipe.demoSuite) {
            DeviceWipe.erase(demo, domain: DeviceWipe.demoSuite)
        }
        DeviceWipe.markInstalled(.standard)
    }

    /// Prüfung beim Start, bevor das App-Modell Daten öffnet: Schalter „Beim nächsten Start zurücksetzen“ oder Reste
    /// einer früheren Installation im Schlüsselbund. Gibt den Grund zurück, wenn gelöscht wurde.
    @discardableResult
    static func launchCheck() -> DeviceWipe.Reason? {
        #if DEBUG
        // UI-Tests verwalten ihren Bereich selbst.
        if UITestSupport.isActive {
            DeviceWipe.markInstalled(.standard)
            return nil
        }
        #endif
        let reason = DeviceWipe.launchReason(defaults: .standard) {
            keychainServices.contains { KeychainSecretStore(service: $0).hasItems }
        }
        if let reason {
            eraseLocalData()
            DiagnosticsLog.shared.record("APP_WIPED", ["reinstall": reason == .reinstall ? 1 : 0])
        } else {
            DeviceWipe.markInstalled(.standard)
        }
        return reason
    }
}

/// Bestätigung „App zurücksetzen“ – gleicher Text überall (Mehr, Anmeldung, Warten, Sperre, Fehler).
struct ResetAppConfirmation: ViewModifier {
    @Binding var isPresented: Bool
    @Environment(AppModel.self) private var model

    func body(content: Content) -> some View {
        content
            .confirmationDialog("App zurücksetzen?", isPresented: $isPresented, titleVisibility: .visible) {
                Button("Alles löschen und neu einrichten", role: .destructive) { model.requestReset() }
                    .accessibilityIdentifier("confirmResetApp")
            } message: {
                Text("Die App löscht Server, Gerät, alle Personen, Fotos und noch nicht übertragene Daten auf diesem Gerät und startet bei der Einrichtung. Auf dem Server bleibt alles erhalten.")
            }
    }
}

extension View {
    func resetAppConfirmation(isPresented: Binding<Bool>) -> some View {
        modifier(ResetAppConfirmation(isPresented: isPresented))
    }
}

/// Unauffällige Schaltfläche „App zurücksetzen“ für Bildschirme vor bzw. außerhalb der Anmeldung (Notausgang).
struct ResetAppButton: View {
    @State private var confirm = false

    var body: some View {
        Button(role: .destructive) { confirm = true } label: {
            Label("App zurücksetzen", systemImage: "arrow.counterclockwise")
                .font(Typo.subheadStrong)
        }
        .tint(Theme.danger)
        .frame(minHeight: 44)
        .accessibilityIdentifier("resetAppButton")
        .resetAppConfirmation(isPresented: $confirm)
    }
}

/// Wird während des Zurücksetzens gezeigt (wenige Augenblicke).
struct ResettingView: View {
    var body: some View {
        VStack(spacing: 16) {
            ProgressView().controlSize(.large)
            Text("App wird zurückgesetzt …").font(Typo.headline)
            Text("Daten, Schlüssel und Einstellungen werden gelöscht.")
                .font(.subheadline)
                .foregroundStyle(Theme.textSecondary)
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(uiColor: .systemBackground))
        .accessibilityElement(children: .combine)
    }
}
