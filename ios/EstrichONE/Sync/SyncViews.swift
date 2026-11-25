import EstrichCore
import SwiftUI

/// Ruhige Zeile über allen Reitern, solange Fotos, Videos oder Dokumente noch nicht beim Server sind
/// („3 Fotos warten auf Übertragung“), und deutlich bei Fehlern, mit „Erneut senden“. Reine Einträge (Bautagebuch,
/// Mängel) sind in Sekunden übertragen und zeigen nur das Symbol an ihrer Zeile. Kurze Übertragungen blenden die Zeile
/// gar nicht erst ein. „Details“ öffnet die Seite „Synchronisation“.
struct TransferStrip: View {
    let sync: SyncController

    @State private var shown: Line?
    @State private var showsDetails = false

    struct Line: Equatable {
        let text: String
        let symbol: String
        let isProblem: Bool
        /// Fehlgeschlagene Dateien lassen sich hier direkt erneut senden.
        let canRetry: Bool
    }

    /// Was jetzt zu zeigen wäre; `nil`, wenn alles übertragen ist.
    static func line(queue: TransferQueue, isRunning: Bool, isOffline: Bool) -> Line? {
        if let problem = queue.problemText {
            return Line(text: "\(problem).", symbol: "exclamationmark.triangle.fill", isProblem: true, canRetry: queue.failedFiles > 0)
        }
        guard queue.photos + queue.videos + queue.documents > 0 else { return nil }
        if isOffline, let text = queue.waitingText {
            return Line(text: "\(text). Sie liegen sicher auf dem Gerät.", symbol: "arrow.up.circle", isProblem: false, canRetry: false)
        }
        guard let text = isRunning ? queue.sendingText : queue.waitingText else { return nil }
        return Line(text: "\(text).", symbol: "arrow.up.circle", isProblem: false, canRetry: false)
    }

    var body: some View {
        let candidate = Self.line(queue: sync.transfer, isRunning: sync.isRunning, isOffline: sync.isOffline)
        Group {
            if let shown {
                StripRow(tone: shown.isProblem ? .danger : .neutral, symbol: shown.symbol, text: shown.text,
                         actionTitle: shown.canRetry ? "Erneut senden" : "Details") {
                    if shown.canRetry {
                        Task { await sync.retryUploads() }
                    } else {
                        showsDetails = true
                    }
                }
                .accessibilityIdentifier("transferStrip")
            }
        }
        .task(id: candidate) {
            // Probleme und das Ende sofort; eine neue Übertragung erst, wenn sie länger als einen Moment dauert.
            if candidate == nil || candidate?.isProblem == true || shown != nil {
                shown = candidate
            } else {
                try? await Task.sleep(for: .seconds(3))
                if !Task.isCancelled { shown = candidate }
            }
        }
        .sheet(isPresented: $showsDetails) {
            NavigationStack {
                SyncView(sync: sync)
                    .toolbar {
                        ToolbarItem(placement: .confirmationAction) { Button("Fertig") { showsDetails = false } }
                    }
            }
        }
    }
}

/// Mehr › Synchronisation (15, Abschnitt 4): ausstehende Vorgänge, Dateien, Konflikte, Abgelehntes.
struct SyncView: View {
    let sync: SyncController

    @State private var operations: [OutboxOperation] = []
    @State private var uploads: [PendingUpload] = []
    /// Was eine wartende Datei ist und zu welcher Baustelle sie gehört.
    @State private var fileInfo: [UUID: (title: String, site: String?)] = [:]
    @State private var conflicts: [(entityType: EntityType, entityId: UUID, conflict: FieldConflict)] = []

    var body: some View {
        List {
            Section {
                statusCard
            }
            let rejected = operations.filter { $0.state == .rejected }
            if !rejected.isEmpty {
                Section {
                    ForEach(rejected, id: \.seq) { operation in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(Self.describe(operation)).font(.headline)
                            Text(BauakteViewOnly.message(forCode: operation.errorCode) ?? operation.errorMessage ?? operation.errorCode ?? "Vom Server abgelehnt")
                                .foregroundStyle(Theme.danger)
                            HStack(spacing: 8) {
                                Button {
                                    Task {
                                        await sync.retry(operation)
                                        await reload()
                                    }
                                } label: {
                                    Label("Erneut senden", systemImage: "arrow.clockwise")
                                }
                                .estrichButton(.secondary)
                                .accessibilityIdentifier("retryOperationButton")
                                Button(role: .destructive) {
                                    Task {
                                        try? await sync.store.discardRejected(operation)
                                        await reload()
                                    }
                                } label: {
                                    Label("Verwerfen", systemImage: "trash")
                                }
                                .estrichButton(.destructive)
                            }
                            .padding(.top, 2)
                        }
                        .swipeActions {
                            Button("Verwerfen", role: .destructive) {
                                Task {
                                    try? await sync.store.discardRejected(operation)
                                    await reload()
                                }
                            }
                        }
                    }
                } header: {
                    Text("Abgelehnt")
                } footer: {
                    Text("Diese Vorgänge hat der Server nicht übernommen. Sie werden nicht erneut gesendet. Zum Entfernen nach links wischen.")
                }
            }

            if !conflicts.isEmpty {
                Section {
                    ForEach(Array(conflicts.enumerated()), id: \.offset) { _, item in
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Feld „\(Self.fieldName(item.conflict.field))“ wurde inzwischen anders geändert").font(.headline)
                            Text("Gilt jetzt: \(Self.display(item.conflict.serverValue))")
                            Text("Ihre Eingabe: \(Self.display(item.conflict.clientValue))").foregroundStyle(Theme.textSecondary)
                        }
                        .swipeActions {
                            Button("Erledigt") {
                                Task {
                                    try? await sync.store.dismissConflicts(entityId: item.entityId)
                                    await reload()
                                }
                            }
                        }
                    }
                } header: {
                    Text("Konflikte")
                }
            }

            let pending = operations.filter { $0.state != .rejected }
            Section {
                if pending.isEmpty {
                    Label("Alle Änderungen sind übertragen.", systemImage: "checkmark.circle")
                        .foregroundStyle(Theme.textSecondary)
                }
                ForEach(pending, id: \.seq) { operation in
                    Label(Self.describe(operation), systemImage: "arrow.up.circle")
                        .foregroundStyle(Theme.textPrimary)
                }
            } header: {
                Text("Ausstehend (\(pending.count))")
            }

            let openUploads = uploads.filter { $0.state != .done }
            if !openUploads.isEmpty {
                Section {
                    ForEach(openUploads, id: \.fileId) { upload in
                        uploadRow(upload)
                    }
                    if openUploads.contains(where: { $0.state == .failed && UploadFailure.canRetry(code: $0.errorCode) }) {
                        Button {
                            Task {
                                await sync.retryUploads()
                                await reload()
                            }
                        } label: {
                            Label("Erneut senden", systemImage: "arrow.clockwise")
                        }
                        .estrichButton(.secondary, size: .regular, fullWidth: true)
                        .accessibilityIdentifier("retryUploadsButton")
                    }
                } header: {
                    Text("Dateien (\(openUploads.count))")
                } footer: {
                    Text("Fotos, Videos und Dokumente liegen verschlüsselt auf dem Gerät, bis sie vollständig beim Server sind. Die Übertragung wird automatisch fortgesetzt, auch nach einem Neustart der App.")
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Synchronisation")
        .task(id: sync.revision) { await reload() }
        .refreshable { await sync.run(includeScope: true) }
    }

    private var state: (symbol: String, title: String, detail: String, tint: Color) {
        let last = sync.summary?.lastSuccessfulSync.map { "Zuletzt vollständig \($0.formatted(.relative(presentation: .named)))" }
            ?? "Noch kein vollständiger Abgleich"
        if sync.isRunning { return ("arrow.triangle.2.circlepath", "Wird abgeglichen …", last, Theme.accent) }
        if let message = sync.maintenanceMessage {
            return ("wrench.and.screwdriver.fill", "Server in Wartung", "\(message) Alles bleibt auf dem Gerät und wird danach übertragen.", Theme.warning)
        }
        if sync.isOffline {
            return ("icloud.slash", "Offline", "Keine Verbindung. Alles ist auf dem Gerät gespeichert und wird übertragen, sobald der Server erreichbar ist.",
                    Theme.textSecondary)
        }
        if sync.summary?.hasProblems == true { return ("exclamationmark.icloud.fill", "Bitte prüfen", "Einige Änderungen wurden nicht übernommen. Details siehe unten.", Theme.danger) }
        switch sync.lastOutcome {
        case .retryLater?:
            return ("clock.arrow.circlepath", "Abgleich unterbrochen", "Der Server ist gerade ausgelastet. Der nächste Versuch läuft automatisch; alles bleibt auf dem Gerät gespeichert.", Theme.warning)
        case let .failed(error)?:
            return ("exclamationmark.icloud", "Abgleich unterbrochen", "\(error.message) Der nächste Versuch läuft automatisch; alles bleibt auf dem Gerät gespeichert.", Theme.warning)
        default:
            break
        }
        if let summary = sync.summary, !summary.isSettled {
            let count = summary.pendingOperations + summary.pendingUploads
            let title = sync.transfer.waitingText ?? (count == 1 ? "1 Änderung wartet" : "\(count) Änderungen warten")
            return ("icloud.and.arrow.up", title, last, Theme.accent)
        }
        return ("checkmark.icloud.fill", "Alles übertragen", last, Theme.success)
    }

    private var statusCard: some View {
        let current = state
        return VStack(alignment: .leading, spacing: Space.l) {
            HStack(spacing: Space.m) {
                IconBadge(symbol: current.symbol, tint: current.tint, background: current.tint.opacity(0.12), size: 52)
                    .symbolEffect(.pulse, isActive: sync.isRunning)
                VStack(alignment: .leading, spacing: 3) {
                    Text(current.title).font(Typo.headline).foregroundStyle(Theme.textPrimary)
                    Text(current.detail).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            if let progress = sync.uploadProgress, progress.total > 0 {
                VStack(alignment: .leading, spacing: Space.xs) {
                    ProgressBar(value: Double(progress.sent) / Double(progress.total))
                    Text("\(progress.files) \(progress.files == 1 ? "Datei" : "Dateien"), \(ByteCountFormatter.string(fromByteCount: progress.sent, countStyle: .file)) von \(ByteCountFormatter.string(fromByteCount: progress.total, countStyle: .file))")
                        .font(Typo.meta).foregroundStyle(Theme.textSecondary)
                }
            }
            Button {
                Task { await sync.run(includeScope: true) }
            } label: {
                ActionLabel("Jetzt synchronisieren", systemImage: "arrow.triangle.2.circlepath", isBusy: sync.isRunning)
            }
            .primaryActionStyle()
            .disabled(sync.isRunning)
            .accessibilityIdentifier("syncNowButton")
        }
        .padding(.vertical, Space.s)
    }

    /// Datei in der Warteschlange: was es ist und zu welcher Baustelle, Fortschritt bzw. verständlicher Fehler.
    private func uploadRow(_ upload: PendingUpload) -> some View {
        let info = fileInfo[upload.fileId]
        let failed = upload.state == .failed
        return VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                Image(systemName: failed ? "exclamationmark.triangle.fill" : Self.symbol(for: upload))
                    .foregroundStyle(failed ? Theme.danger : Theme.accent)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 2) {
                    Text(info?.title ?? Self.kindTitle(for: upload)).font(Typo.bodyStrong).foregroundStyle(Theme.textPrimary)
                    if let site = info?.site {
                        Text(site).font(Typo.subhead).foregroundStyle(Theme.textSecondary).lineLimit(1)
                    }
                }
            }
            if failed {
                Text(UploadFailure.message(code: upload.errorCode))
                    .font(Typo.subhead)
                    .foregroundStyle(Theme.danger)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                ProgressView(value: Double(upload.uploadedBytes), total: Double(max(upload.size, 1)))
                Text(upload.state == .uploading
                     ? "\(ByteCountFormatter.string(fromByteCount: upload.uploadedBytes, countStyle: .file)) von \(ByteCountFormatter.string(fromByteCount: upload.size, countStyle: .file))"
                     : "Wartet auf Übertragung, \(ByteCountFormatter.string(fromByteCount: upload.size, countStyle: .file))")
                    .font(Typo.meta).foregroundStyle(Theme.textSecondary)
            }
        }
        .padding(.vertical, Space.xxs)
        .accessibilityElement(children: .combine)
    }

    private static func symbol(for upload: PendingUpload) -> String {
        switch TransferQueue.kind(ofContentType: upload.contentType) {
        case .photo: "photo"
        case .video: "video"
        case .document: "doc"
        }
    }

    private static func kindTitle(for upload: PendingUpload) -> String {
        switch TransferQueue.kind(ofContentType: upload.contentType) {
        case .photo: "Foto"
        case .video: "Video"
        case .document: "Dokument"
        }
    }

    private func reload() async {
        operations = (try? await sync.store.operations()) ?? []
        uploads = (try? await sync.store.uploads()) ?? []
        var info: [UUID: (title: String, site: String?)] = [:]
        for upload in uploads where upload.state != .done {
            guard let entity = try? await sync.store.entity(.file, upload.fileId) else { continue }
            let file = FileItem(entity)
            var site: String?
            if let bauakteId = file.bauakteId, let bauakte = try? await sync.store.entity(.bauakte, bauakteId) {
                site = Bauakte(bauakte).title
            }
            let kind = Self.kindTitle(for: upload)
            info[upload.fileId] = (file.caption.map { "\(kind): \($0)" } ?? kind, site)
        }
        fileInfo = info
        conflicts = (try? await sync.store.conflicts()) ?? []
        await sync.refreshSummary()
    }

    static func describe(_ operation: OutboxOperation) -> String {
        let object: String = switch operation.entityType {
        case .bauakte: "Bauakte"
        case .note: "Notiz"
        case .file: "Foto/Datei"
        case .workItem: "Aufgabe"
        case .contact: "Kontakt"
        case .screedType: "Estrichart"
        case .diaryEntry: "Bautagebuch-Eintrag"
        case .inspection: "Prüfung"
        case .acceptance: "Abnahmeprotokoll"
        case .valueListItem: "Werteliste"
        case .formSchema: "Formular"
        case .ticket: "Meldung an die IT"
        case .room: "Raum"
        case .bauakteParty: "Beteiligter"
        case .fuelReceipt: "Tankbeleg"
        }
        let action = operation.type.split(separator: ".").last ?? ""
        return switch action {
        case "create", "announce": "\(object) angelegt"
        case "update": "\(object) geändert"
        case "delete": "\(object) gelöscht"
        case "setStatus":
            operation.payload["status"]?.string == "cancelled"
                ? "\(object) storniert"
                : "Status auf „\(operation.payload["status"]?.string.flatMap(BauakteStatus.init(rawValue:))?.title ?? "–")“ gesetzt"
        default: object
        }
    }

    static func fieldName(_ field: String) -> String {
        [
            "title": "Bezeichnung", "city": "Ort", "street": "Straße", "postalCode": "PLZ", "description": "Beschreibung",
            "status": "Status", "customerId": "Kunde", "responsibleUserId": "Zuständig", "dueDate": "Fällig", "assigneeUserId": "Zugewiesen",
        ][field] ?? field
    }

    static func display(_ value: JSONValue?) -> String {
        switch value {
        case let .string(text): text
        case let .number(number): number.formatted()
        case let .bool(flag): flag ? "ja" : "nein"
        case .none, .null: "–"
        default: "…"
        }
    }
}
