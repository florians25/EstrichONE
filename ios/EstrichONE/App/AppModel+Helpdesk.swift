import EstrichCore
import UIKit

/// „Problem melden“ (14): Diagnosedaten zusammenstellen, melden mit und ohne Anmeldung.
extension AppModel {
    var helpdesk: HelpdeskService? {
        guard let active, let service = active.service else { return nil }
        return HelpdeskService(store: service.store, files: service.files, userId: active.user.id)
    }

    /// Ein eingerichtetes Gerät kann auch ohne Anmeldung melden (14, Abschnitt 5).
    var canReportWithoutSignIn: Bool { deviceRegistration != nil && accounts != nil }

    func diagnostics() async -> DiagnosticsReport {
        let summary = try? await active?.sync?.store.summary()
        let free = (try? URL.homeDirectory.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey]))?
            .volumeAvailableCapacityForImportantUsage
        let network: String = switch active?.sync?.lastOutcome {
        case .offline: "offline"
        case .some: "verbunden"
        case nil: "unbekannt"
        }
        return DiagnosticsReport(
            appVersion: AppEnvironment.shortVersion,
            build: Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "?",
            platform: "iOS", osVersion: UIDevice.current.systemVersion, deviceModel: AppEnvironment.modelIdentifier,
            deviceKind: deviceRegistration?.kind, deviceId: deviceRegistration?.deviceId, sync: summary, network: network,
            freeStorageBytes: free, log: .shared)
    }

    func report(_ ticket: NewTicket, includeDiagnostics: Bool, attachments: [Data]) async throws(TicketError) -> UUID {
        guard let helpdesk else { throw .storage }
        let diagnostics = includeDiagnostics ? await diagnostics() : nil
        let id = try await helpdesk.report(ticket, diagnostics: diagnostics, attachments: attachments)
        DiagnosticsLog.shared.record("TICKET_QUEUED", ["attachments": attachments.count])
        active?.sync?.localChangeMade()
        return id
    }

    func reportWithoutSignIn(_ ticket: NewTicket, reporterName: String, includeDiagnostics: Bool) async throws(TicketError) -> String {
        guard let accounts, let deviceId = deviceRegistration?.deviceId else { throw .notRegistered }
        let diagnostics = includeDiagnostics ? await diagnostics() : nil
        return try await DeviceTicketReporter.send(ticket, reporterName: reporterName, deviceId: deviceId, keys: accounts.keys,
                                                   api: accounts.api, diagnostics: diagnostics)
    }
}
