import EstrichCore
import Foundation
import Observation
import UIKit

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

    /// Fortschritt laufender Uploads (Bytes und Anzahl Dateien), solange etwas hochgeladen wird.
    private(set) var uploadProgress: (sent: Int64, total: Int64, files: Int)?

    private var debounce: Task<Void, Never>?
    private var loop: Task<Void, Never>?
    private var progressPoll: Task<Void, Never>?
    /// Während eines Laufs kam eine lokale Änderung dazu: direkt danach noch einmal synchronisieren.
    private var rerunRequested = false
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
            // Jeder Lauf mit Sichtbarkeitsliste: Rechteentzug und neu sichtbare Bauakten (z. B. eingeplant) sofort.
            await self?.run()
            while !Task.isCancelled {
                let delay = self?.nextDelay ?? 120
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

    func run(includeScope: Bool = true) async {
        if isRunning {
            rerunRequested = true
            return
        }
        isRunning = true
        startProgressPolling()
        var outcome = await engine.synchronize(includeScope: includeScope)
        while rerunRequested, outcome == .completed {
            rerunRequested = false
            outcome = await engine.synchronize(includeScope: false)
        }
        rerunRequested = false
        progressPoll?.cancel()
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
        await refreshUploadProgress()
    }

    /// Eine Bauakte sofort vollständig vom Server laden (z. B. nach „Mir zuweisen“ oder wenn sie noch fehlt).
    func load(bauakte id: UUID) async -> SyncOutcome {
        let outcome = await engine.load(bauakte: id)
        revision += 1
        return outcome
    }

    /// Abgelehnte Operation erneut senden (z. B. nach Klärung im Büro oder „Mir zuweisen“).
    func retry(_ operation: OutboxOperation) async {
        try? await store.retryRejected(operation)
        localChangeMade()
    }

    /// Fehlgeschlagene Uploads erneut versuchen.
    func retryUploads() async {
        _ = try? await store.retryFailedUploads()
        localChangeMade()
    }

    /// Beim Wechsel in den Hintergrund: noch laufende Übertragungen (z. B. Fotos) mit der Hintergrundzeit des
    /// Systems abschließen, statt sie bis zum nächsten Öffnen liegen zu lassen.
    func runInBackground() {
        guard summary.map({ !$0.isSettled }) ?? true else { return }
        let application = UIApplication.shared
        let task = BackgroundTask()
        task.identifier = application.beginBackgroundTask(withName: "EstrichONE-Synchronisation") {
            MainActor.assumeIsolated { task.end() }
        }
        Task {
            await run(includeScope: false)
            task.end()
        }
    }

    private func startProgressPolling() {
        progressPoll?.cancel()
        progressPoll = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refreshUploadProgress()
                try? await Task.sleep(for: .seconds(1))
            }
        }
    }

    private func refreshUploadProgress() async {
        let active = ((try? await store.uploads()) ?? []).filter { $0.state == .waiting || $0.state == .uploading }
        uploadProgress = active.isEmpty ? nil
            : (active.reduce(0) { $0 + $1.uploadedBytes }, active.reduce(0) { $0 + $1.size }, active.count)
    }

    /// 2 Minuten im Normalfall; nach Fehlern 1 s, 2 s, 4 s … bis 15 Minuten, mit Zufallsanteil.
    private var nextDelay: Double {
        if inMaintenance { return 120 }
        guard consecutiveFailures > 0 else { return 120 }
        let base = min(900, pow(2, Double(consecutiveFailures - 1)))
        return base + Double.random(in: 0...(base / 2))
    }
}

/// Hintergrundzeit des Systems; wird genau einmal beendet.
@MainActor
private final class BackgroundTask {
    var identifier = UIBackgroundTaskIdentifier.invalid

    func end() {
        guard identifier != .invalid else { return }
        UIApplication.shared.endBackgroundTask(identifier)
        identifier = .invalid
    }
}
