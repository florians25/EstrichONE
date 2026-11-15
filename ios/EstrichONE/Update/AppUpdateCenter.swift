import EstrichCore
import Foundation
import Observation
import UIKit

/// App-Updates auf iOS (docs/app-updates.md): prüft gegen den eigenen Server (beim Start, beim Zurückkehren gedrosselt,
/// von Hand und nach einer Ablehnung mit 426), zeigt Hinweis bzw. Sperre und führt zu App Store, TestFlight oder MDM.
/// iOS installiert nicht selbst; nach Anmeldung bzw. Update erscheint einmal „Was gibt’s Neues“.
@MainActor
@Observable
final class AppUpdateCenter {
    private(set) var check: AppUpdateCheck?
    private(set) var isChecking = false
    private(set) var checkFailed = false
    /// Neuerungen nach Anmeldung bzw. Update (Blatt „Was gibt’s Neues“).
    var whatsNew: [AppReleaseNote] = []
    /// Mehr › „Was gibt’s Neues“ ohne hinterlegte Neuerungen für die App.
    var noWhatsNew = false
    /// Stand der angezeigten Neuerungen (für „Verstanden“); `nil` = von Hand geöffnet.
    private var whatsNewVersion: String?
    private var checkedWhatsNewFor: UUID?
    /// „Später“ für diese Version (bis zum nächsten Start).
    private(set) var snoozedVersion: String?
    /// Vor dem Wechsel in den App Store: offene Änderungen werden übertragen.
    private(set) var isPreparing = false
    /// Nach dem Abgleich noch nicht übertragene Änderungen (Hinweis vor dem Wechsel).
    var unsentBeforeLeaving: Int?

    let installedVersion: String
    private let transport: HTTPTransport
    private let defaults: UserDefaults
    /// Adresse des verbundenen Servers und Abgleich offener Änderungen (setzt das App-Modell).
    @ObservationIgnored var serverAddress: @MainActor () -> ServerAddress? = { nil }
    @ObservationIgnored var unsentChanges: @MainActor () async -> Int = { 0 }
    /// Angemeldete Person und ihr Client (für „Was gibt’s Neues“); `nil` vor der Anmeldung.
    @ObservationIgnored var signedIn: @MainActor () -> (userId: UUID, client: AuthorizedClient)? = { nil }
    /// Screenshots: gezeigter Zustand bleibt stehen.
    private var frozen = false

    private static let lastCheckKey = "estrichone.update.lastCheck"

    init(transport: HTTPTransport, defaults: UserDefaults, installedVersion: String, signal: AppUpdateSignal) {
        self.transport = transport
        self.defaults = defaults
        self.installedVersion = installedVersion
        signal.onRejection { [weak self] rejection in
            guard let self else { return }
            Task { @MainActor in self.serverRejected(rejection) }
        }
    }

    /// Unter der Mindestversion: Arbeiten erst nach dem Update.
    var isBlocked: Bool { check?.status == .required }

    /// Hinweis „Version … verfügbar“ oben in der App.
    var showsBanner: Bool {
        guard let check, check.status == .recommended else { return false }
        return snoozedVersion != check.latestVersion
    }

    func snooze() {
        snoozedVersion = check?.latestVersion
    }

    func refresh(_ trigger: AppUpdateSchedule.Trigger) {
        let now = Date()
        let last = defaults.object(forKey: Self.lastCheckKey) as? Date
        guard !frozen, !isChecking, AppUpdateSchedule.isDue(lastCheck: last, now: now, trigger: trigger), let address = serverAddress() else { return }
        isChecking = true
        let api = APIClient(server: address, transport: transport)
        let version = installedVersion
        Task {
            defer { isChecking = false }
            do {
                let result = try await api.checkAppUpdate(version: version)
                guard !frozen else { return }
                defaults.set(now, forKey: Self.lastCheckKey)
                check = result
                checkFailed = false
            } catch {
                checkFailed = true
            }
        }
    }

    /// Der Server hat eine Anfrage mit 426 abgelehnt: sofort sperren, Bezugsquelle nachladen.
    func serverRejected(_ rejection: AppUpdateRejection) {
        guard !frozen, check?.status != .required else { return }
        check = AppUpdateCheck(installedVersion: installedVersion, serverVersion: check?.serverVersion ?? "",
                               latestVersion: rejection.latestVersion, minimumVersion: rejection.minimumVersion ?? "",
                               status: .required, message: rejection.message, download: check?.download ?? AppUpdateDownload(kind: "store", url: nil),
                               releaseNotes: check?.releaseNotes ?? [])
        refresh(.rejected)
    }

    /// „Aktualisieren“: offene Änderungen übertragen, dann App Store, TestFlight bzw. MDM öffnen.
    func prepareAndOpen() async {
        isPreparing = true
        let left = await unsentChanges()
        isPreparing = false
        if left > 0 {
            unsentBeforeLeaving = left
            return
        }
        openStore()
    }

    /// Bezugsquelle öffnen (ohne hinterlegten Link: App Store).
    func openStore() {
        unsentBeforeLeaving = nil
        let link = check?.download.url.flatMap(URL.init(string:)) ?? URL(string: "itms-apps://apps.apple.com")
        if let link { UIApplication.shared.open(link) }
    }

    /// Nach der Anmeldung (auch nach App- oder Server-Update) einmal je Benutzer: Neuerungen für die Apps („## Apps“)
    /// seit der zuletzt gesehenen Version. Den Stand führt der Server (geräteübergreifend; neue Benutzer still als
    /// gesehen); ohne Verbindung beim nächsten Start erneut.
    func loadWhatsNew() {
        guard !frozen, let current = signedIn(), checkedWhatsNewFor != current.userId else { return }
        checkedWhatsNewFor = current.userId
        Task {
            do {
                let state = try await current.client.whatsNew()
                guard !frozen, !state.notes.isEmpty else { return }
                whatsNewVersion = state.serverVersion
                whatsNew = state.notes
            } catch {
                checkedWhatsNewFor = nil
            }
        }
    }

    /// Mehr › „Was gibt’s Neues“: die letzten Versionen (ohne Eintrag: Hinweis).
    func openWhatsNew() {
        guard let current = signedIn() else { return }
        Task {
            let notes = (try? await current.client.recentReleaseNotes()) ?? []
            whatsNewVersion = nil
            if notes.isEmpty { noWhatsNew = true } else { whatsNew = notes }
        }
    }

    /// „Verstanden“: auf dem Server als gesehen markieren (auch für die anderen Geräte).
    func whatsNewShown() {
        whatsNew = []
        guard !frozen, let current = signedIn() else { return }
        let version = whatsNewVersion
        whatsNewVersion = nil
        Task { try? await current.client.markReleaseNotesSeen(version: version) }
    }

    #if DEBUG
    /// Screenshot-Modus: Zustände für „update-hinweis“, „update-erforderlich“, „update-neu“.
    func simulate(_ scene: String?) {
        frozen = scene?.hasPrefix("update") == true
        snoozedVersion = nil
        unsentBeforeLeaving = nil
        guard let scene, frozen else {
            check = nil
            whatsNew = []
            return
        }
        let notes = [
            AppReleaseNote(version: "1.0.13", title: "EstrichONE 1.0.13", sections: [
                AppReleaseSection(title: "Apps", items: ["Hinweis auf neue Versionen direkt in der App", "**Was gibt’s Neues:** nach jedem Update einmal die Neuerungen"]),
                AppReleaseSection(title: "Bauakten", items: ["Abnahme mit Unterschrift auch ohne Netz", "Fotos schneller hochladen"]),
            ]),
            AppReleaseNote(version: "1.0.12", title: "EstrichONE 1.0.12", sections: [
                AppReleaseSection(title: "Module", items: ["Module lassen sich in der Verwaltung ein- und ausschalten"]),
            ]),
        ]
        let required = scene == "update-erforderlich"
        check = scene == "update-neu" ? nil : AppUpdateCheck(
            installedVersion: installedVersion, serverVersion: "1.0.13", latestVersion: "1.0.13", minimumVersion: required ? "1.0.13" : "1.0.0",
            status: required ? .required : .recommended,
            message: required ? "Neue Abnahme-Funktion – bitte heute noch aktualisieren." : "Bitte bis Freitag aktualisieren.",
            download: AppUpdateDownload(kind: "store", url: "https://testflight.apple.com/join/demo"), releaseNotes: notes)
        whatsNew = scene == "update-neu" ? notes : []
    }
    #endif
}
