import EstrichCore
import Foundation
import Observation

/// Steuert die Synchronisation des angemeldeten Benutzers (15, Abschnitt 1): beim Anmelden und App-Start,
/// 2 Sekunden nach jeder lokalen Änderung, regelmäßig im Vordergrund und nach Fehlern mit wachsendem Abstand.
@MainActor
@Observable
final class SyncController {
    let store: LocalStore
    let files: FileVault
    private let engine: SyncEngine
    private let client: AuthorizedClient

    private(set) var summary: SyncSummary?
    private(set) var isRunning = false
    private(set) var lastOutcome: SyncOutcome?
    /// Wird nach jedem Lauf erhöht, damit Ansichten ihre Daten neu laden.
    private(set) var revision = 0
    /// Hinweise des Betreibers (Ankündigungen, Wartung); zuletzt abgerufener Stand.
    private(set) var notices: SystemNotices?

    private var debounce: Task<Void, Never>?
    private var loop: Task<Void, Never>?
    private var consecutiveFailures = 0
    private var inMaintenance = false

    init(account: UnlockedAccount, client: AuthorizedClient, storage: AppStorage) throws {
        let directory = storage.userDirectory(account.user.id)
        store = try LocalStore.open(url: directory.appending(path: "data.sealed"), account: account)
        files = FileVault(directory: directory.appending(path: "files"), key: account.dataKey)
        engine = SyncEngine(store: store, transport: APISyncTransport(client: client), files: files)
        self.client = client
    }

    var isOffline: Bool { lastOutcome == .offline }

    /// Server in Wartung: Synchronisation pausiert, Arbeiten auf dem Gerät geht weiter.
    var maintenanceMessage: String? {
        if case let .maintenance(message) = lastOutcome {
            return message ?? notices?.maintenance?.message ?? "Der Server wird gewartet."
        }
        return nil
    }

    /// Hinweise neu laden (Start, Vordergrund, mit jeder Synchronisation). Ohne Verbindung bleibt der letzte Stand.
    func refreshNotices() async {
        if let fresh = try? await client.systemNotices() { notices = fresh }
    }

    func start() {
        loop?.cancel()
        loop = Task { [weak self] in
            // Erster Lauf mit Sichtbarkeitsliste (Rechteentzug), danach regelmäßig.
            await self?.run(includeScope: true)
            while !Task.isCancelled {
                let delay = self?.nextDelay ?? 300
                try? await Task.sleep(for: .seconds(delay))
                await self?.run()
            }
        }
    }

    func stop() {
        loop?.cancel()
        debounce?.cancel()
        loop = nil
        debounce = nil
    }

    /// Nach einer lokalen Änderung: entprellt synchronisieren.
    func localChangeMade() {
        revision += 1
        Task { await refreshSummary() }
        debounce?.cancel()
        debounce = Task { [weak self] in
            try? await Task.sleep(for: .seconds(2))
            guard !Task.isCancelled else { return }
            await self?.run()
        }
    }

    func run(includeScope: Bool = false) async {
        isRunning = true
        let outcome = await engine.synchronize(includeScope: includeScope)
        lastOutcome = outcome
        // Nur Codes und Zahlen ins Diagnoseprotokoll (14, Abschnitt 3).
        switch outcome {
        case .completed: DiagnosticsLog.shared.record("SYNC_COMPLETED")
        case .offline: DiagnosticsLog.shared.record("SYNC_OFFLINE")
        case .retryLater: DiagnosticsLog.shared.record("SYNC_RETRY_LATER")
        case .maintenance: DiagnosticsLog.shared.record("SYNC_MAINTENANCE")
        case let .failed(error):
            DiagnosticsLog.shared.record("SYNC_FAILED", ["attempt": consecutiveFailures + 1])
            DiagnosticsLog.shared.recordError(code: error.code ?? "SYNC_FAILED")
        }
        // Wartung ist kein Fehler: kein wachsender Abstand und keine Fehlermeldungen, nur ruhiges Nachfragen.
        if case .maintenance = outcome {
            consecutiveFailures = 0
            inMaintenance = true
        } else {
            inMaintenance = false
            consecutiveFailures = outcome == .completed ? 0 : consecutiveFailures + 1
        }
        await refreshSummary()
        await refreshNotices()
        revision += 1
        isRunning = false
    }

    func refreshSummary() async {
        summary = try? await store.summary()
    }

    /// 5 Minuten im Normalfall; nach Fehlern 1 s, 2 s, 4 s … bis 15 Minuten, mit Zufallsanteil.
    private var nextDelay: Double {
        if inMaintenance { return 120 }
        guard consecutiveFailures > 0 else { return 300 }
        let base = min(900, pow(2, Double(consecutiveFailures - 1)))
        return base + Double.random(in: 0...(base / 2))
    }
}
