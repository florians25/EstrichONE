import EstrichCore
import Foundation
import Observation

/// Zustand der App auf oberster Ebene: Ist ein Server eingerichtet, und kam ein Einrichtungslink herein?
@MainActor
@Observable
final class AppModel {
    private(set) var server: VerifiedServer?

    /// Per Link oder QR-Code übergebener Einrichtungscode, der auf die Verbindungsprüfung wartet.
    var pendingEnrollment: EnrollmentLink?

    /// Einrichtungscode des bestätigten Servers. Die Geräteregistrierung (AP-11) löst ihn ein;
    /// bis dahin wird er bewusst nicht gespeichert, denn er ist einmalig und nach 15 Minuten wertlos.
    private(set) var enrollmentCode: String?

    /// Hinweis für den Benutzer, etwa wenn ein Einrichtungslink nicht angewendet werden kann.
    var notice: String?

    var hasNotice: Bool {
        get { notice != nil }
        set { if !newValue { notice = nil } }
    }

    let checker: ServerConnectionChecker
    private let store: ServerStore

    init(store: ServerStore, checker: ServerConnectionChecker) {
        self.store = store
        self.checker = checker
        server = store.load()
    }

    /// Der Benutzer hat den geprüften Server sichtbar bestätigt (10, Schritt 2).
    func confirm(_ server: VerifiedServer, enrollment: EnrollmentLink?) throws {
        try store.save(server)
        enrollmentCode = enrollment?.server == server.address ? enrollment?.enrollmentCode : nil
        self.server = server
    }

    /// Setzt die Einrichtung zurück. Solange die App keine eigenen Daten speichert, ist das gefahrlos;
    /// mit der Offline-Speicherung (AP-12) kommen die Schutzregeln aus 10, Abschnitt 2 hinzu.
    func resetServer() {
        store.clear()
        server = nil
        enrollmentCode = nil
        pendingEnrollment = nil
    }

    /// Verarbeitet `estrichone://enroll?…` aus der Kamera-App, einer E-Mail oder einem anderen Programm.
    func handle(url: URL) {
        let link: EnrollmentLink
        do throws(EnrollmentLinkError) {
            link = try EnrollmentLink.parse(url.absoluteString, allowInsecureLocal: AppEnvironment.allowsInsecureLocalServers)
        } catch {
            notice = error.message
            return
        }

        guard server == nil else {
            // Ein Serverwechsel darf nie nebenbei über einen Link passieren (10, Abschnitt 2).
            notice = "Diese App ist bereits mit einem Server verbunden. Den Server wechseln Sie unter „Server wechseln“."
            return
        }
        pendingEnrollment = link
    }
}
