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

    /// Zustand von Backup- und Archivserver (rote/gelbe Hinweise), zuletzt abgerufener Stand.
    private(set) var health: SystemHealth?
    /// Chat-Modul: aktiv und erlaubt, ungelesene Nachrichten (`nil`, solange unbekannt).
    private(set) var chat: ChatStatus?
    /// „Nur Ansicht“ nach 5 Minuten ohne Server (Erfassen und Ändern gesperrt).
    private(set) var isReadOnly = false
    /// Zeitpunkt der letzten Serverantwort, die Übersteuerung „auf eigene Gefahr“ und ob der Server gerade fehlt.
    private(set) var connection = ReadOnlyPolicy()

    /// Fortschritt laufender Uploads (Bytes und Anzahl Dateien), solange etwas hochgeladen wird.
    private(set) var uploadProgress: (sent: Int64, total: Int64, files: Int)?

    private var debounce: Task<Void, Never>?
    private var loop: Task<Void, Never>?
    private var progressPoll: Task<Void, Never>?
    private var clock: Task<Void, Never>?
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
        let store = store
        Task { [weak self] in
            // Letzte Serververbindung aus der vorigen Sitzung: „Nur Ansicht“ auch nach einem Neustart ohne Netz.
            if let text = try? await store.meta(Self.lastContactKey), let date = APIDate.parse(text) {
                self?.restoreLastContact(date)
            }
        }
    }

    private static let lastContactKey = "last_server_contact"

    private func restoreLastContact(_ date: Date) {
        if connection.lastContact == nil { connection.lastContact = date }
    }

    var isOffline: Bool { lastOutcome == .offline }

    /// Wartung sperrt die App (nur Nicht-Administratoren bekommen 503 `MAINTENANCE`); die Outbox bleibt erhalten.
    var isMaintenanceLocked: Bool {
        if case .maintenance = lastOutcome { return true }
        return false
    }

    /// Geplantes Ende der laufenden Wartung (aus den Systemhinweisen).
    var maintenanceEnd: Date? { notices?.maintenance?.end }

    /// Rote/gelbe Hinweise zu Backup- und Archivserver, die jetzt gelten.
    var healthBanners: [HealthBanner] { health?.banners() ?? [] }

    /// „Trotzdem offline arbeiten (auf eigene Gefahr)“ – gilt bis zum nächsten erfolgreichen Abgleich.
    func overrideReadOnly() {
        connection.overridden = true
        DiagnosticsLog.shared.record("READ_ONLY_OVERRIDDEN")
        Task { await evaluateConnection() }
    }

    /// Übersteuerung zurücknehmen: wieder „Nur Ansicht“, solange der Server fehlt.
    func endReadOnlyOverride() {
        connection.overridden = false
        Task { await evaluateConnection() }
    }

    /// Lesemodus neu bewerten (alle 15 Sekunden und nach jedem Lauf) und an die lokale Datenhaltung weitergeben.
    func evaluateConnection(now: Date = .now) async {
        let readOnly = connection.isReadOnly(at: now)
        if readOnly != isReadOnly {
            isReadOnly = readOnly
            if readOnly {
                DiagnosticsLog.shared.record("READ_ONLY_ON")
            } else {
                DiagnosticsLog.shared.record("READ_ONLY_OFF")
            }
        }
        await store.setReadOnly(readOnly)
    }

    /// Chat-Status neu laden (Modul an/aus, ungelesen).
    func refreshChat() async {
        if let fresh = try? await client.chatStatus() { chat = fresh }
    }

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
        // Backup-/Archivstatus höchstens alle 5 Minuten (der Server speichert ihn zwischen), sofort nach dem Wiederverbinden.
        let reconnected = healthFetchedWhileUnreachable && !connection.unreachable
        if let last = healthFetchedAt, Date.now.timeIntervalSince(last) < 270, !reconnected { return }
        do throws(APIError) {
            health = try await client.systemHealth()
            healthFetchedAt = .now
            healthFetchedWhileUnreachable = false
        } catch {
            // Ohne Verbindung bleibt der letzte Stand.
            healthFetchedWhileUnreachable = true
        }
    }

    /// Beim nächsten Lauf den Backup-/Archivstatus sicher neu laden (z. B. nach einer Änderung im Demomodus).
    func forceHealthRefresh() {
        healthFetchedAt = nil
    }

    private var healthFetchedAt: Date?
    private var healthFetchedWhileUnreachable = false

    func start() {
        loop?.cancel()
        startClock()
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
        clock?.cancel()
        loop = nil
        debounce = nil
        clock = nil
    }

    /// Uhr für „Nur Ansicht“: Die 5 Minuten laufen auch ohne neuen Abgleich ab.
    private func startClock() {
        clock?.cancel()
        clock = Task { [weak self] in
            while !Task.isCancelled {
                await self?.evaluateConnection()
                try? await Task.sleep(for: .seconds(15))
            }
        }
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
        connection.record(outcome, at: .now)
        if let contact = connection.lastContact, !connection.unreachable {
            try? await store.setMeta(Self.lastContactKey, APIDate.format(contact))
        }
        await evaluateConnection()
        await refreshSummary()
        await refreshNotices()
        if !isMaintenanceLocked, !connection.unreachable { await refreshChat() }
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

    #if DEBUG
    /// Screenshot-Modus: Wartung des Servers vortäuschen (`nil` beendet sie wieder).
    func simulateMaintenance(_ message: String?) {
        if let message {
            lastOutcome = .maintenance(message: message)
        } else if case .maintenance = lastOutcome {
            lastOutcome = .completed
        }
    }

    /// Screenshot-Modus: Server seit `minutes` Minuten nicht erreichbar (`nil` = verbunden).
    func simulateOutage(minutes: Double?) {
        if let minutes {
            connection = ReadOnlyPolicy(lastContact: Date.now.addingTimeInterval(-minutes * 60), unreachable: true)
            lastOutcome = .offline
        } else {
            connection = ReadOnlyPolicy(lastContact: .now)
            if lastOutcome == .offline { lastOutcome = .completed }
        }
        Task { await evaluateConnection() }
    }

    /// Screenshot-Modus: Zustand der Sicherung vorgeben.
    func simulateHealth(_ value: SystemHealth?) {
        health = value
    }
    #endif

    /// Demomodus: Der Server ist seit über 5 Minuten weg (Vorführung von „Nur Ansicht“, ohne 5 Minuten zu warten).
    func backdateLastContact(minutes: Double) {
        connection.lastContact = Date.now.addingTimeInterval(-minutes * 60)
        Task { await evaluateConnection() }
    }

    /// 2 Minuten im Normalfall; nach Fehlern 1 s, 2 s, 4 s … bis 15 Minuten, mit Zufallsanteil.
    private var nextDelay: Double {
        // Wartung: alle 30 Sekunden nachfragen, damit die Sperre nach dem Ende zügig fällt.
        if inMaintenance { return 30 }
        guard consecutiveFailures > 0 else { return 120 }
        let base = min(900, pow(2, Double(consecutiveFailures - 1)))
        let delay = base + Double.random(in: 0...(base / 2))
        // Ohne Verbindung höchstens 30 Sekunden: „Nur Ansicht“ endet bald nach der Rückkehr des Servers.
        return connection.unreachable ? min(delay, 30) : delay
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
