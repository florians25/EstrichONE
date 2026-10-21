import EstrichCore
import SwiftUI

/// Deckende Statuszeile (nie auf Glas, 07): unauffällig, wenn alles übertragen ist; deutlich bei Offline und Fehlern.
struct SyncStatusBar: View {
    let sync: SyncController

    var body: some View {
        if let line = statusLine {
            NavigationLink {
                SyncView(sync: sync)
            } label: {
                Label {
                    Text(line.text).font(.callout.weight(.medium)).fixedSize(horizontal: false, vertical: true)
                } icon: {
                    Image(systemName: line.symbol)
                }
                .foregroundStyle(line.color)
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(line.background)
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("syncStatusBar")
        }
    }

    private var statusLine: (text: String, symbol: String, color: Color, background: Color)? {
        let summary = sync.summary
        if summary?.hasProblems == true {
            return ("Problem bei der Übertragung – antippen für Details", "exclamationmark.triangle.fill", Theme.error, Theme.errorBackground)
        }
        if let message = sync.maintenanceMessage {
            return ("Wartung: \(message) Änderungen bleiben auf dem Gerät und werden danach übertragen.", "wrench.and.screwdriver.fill",
                    Theme.hint, Theme.hintBackground)
        }
        if sync.isOffline {
            return ("Offline – Änderungen werden gespeichert", "wifi.slash", Theme.textSecondary, Theme.surfaceAlt)
        }
        if let progress = sync.uploadProgress, progress.total > 0 {
            let percent = Int((Double(progress.sent) / Double(progress.total) * 100).rounded())
            return ("\(progress.files) \(progress.files == 1 ? "Datei wird" : "Dateien werden") hochgeladen · \(percent) %",
                    "arrow.up.circle", Theme.textSecondary, Theme.surfaceAlt)
        }
        if let summary, !summary.isSettled {
            let count = summary.pendingOperations + summary.pendingUploads
            return ("\(count) \(count == 1 ? "Änderung wird" : "Änderungen werden") übertragen", "arrow.triangle.2.circlepath", Theme.textSecondary, Theme.surfaceAlt)
        }
        return nil
    }
}

/// Mehr › Synchronisation (15, Abschnitt 4): ausstehende Vorgänge, Dateien, Konflikte, Abgelehntes.
struct SyncView: View {
    let sync: SyncController

    @State private var operations: [OutboxOperation] = []
    @State private var uploads: [PendingUpload] = []
    @State private var conflicts: [(entityType: EntityType, entityId: UUID, conflict: FieldConflict)] = []

    var body: some View {
        List {
            Section {
                LabeledContent("Zuletzt vollständig") {
                    if let date = sync.summary?.lastSuccessfulSync {
                        Text(date, format: .relative(presentation: .named))
                    } else {
                        Text("noch nie")
                    }
                }
                if sync.isOffline {
                    Label("Keine Verbindung. Alles ist auf dem Gerät gespeichert und wird übertragen, sobald der Server erreichbar ist.", systemImage: "wifi.slash")
                        .foregroundStyle(Theme.textSecondary)
                }
                Button {
                    Task { await sync.run(includeScope: true) }
                } label: {
                    HStack {
                        Label("Jetzt synchronisieren", systemImage: "arrow.triangle.2.circlepath")
                        if sync.isRunning {
                            Spacer()
                            ProgressView()
                        }
                    }
                }
                .disabled(sync.isRunning)
                .accessibilityIdentifier("syncNowButton")
            }

            let rejected = operations.filter { $0.state == .rejected }
            if !rejected.isEmpty {
                Section {
                    ForEach(rejected, id: \.seq) { operation in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(Self.describe(operation)).font(.headline)
                            Text(operation.errorMessage ?? operation.errorCode ?? "Vom Server abgelehnt").foregroundStyle(Theme.error)
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
                    Text("Alle Änderungen sind übertragen.").foregroundStyle(Theme.textSecondary)
                }
                ForEach(pending, id: \.seq) { operation in
                    Text(Self.describe(operation))
                }
            } header: {
                Text("Ausstehend")
            }

            let openUploads = uploads.filter { $0.state != .done }
            if !openUploads.isEmpty {
                Section {
                    ForEach(openUploads, id: \.fileId) { upload in
                        VStack(alignment: .leading, spacing: 6) {
                            Text(upload.state == .failed ? "Upload fehlgeschlagen" : "Datei wird übertragen")
                                .foregroundStyle(upload.state == .failed ? Theme.error : .primary)
                            ProgressView(value: Double(upload.uploadedBytes), total: Double(max(upload.size, 1)))
                            Text("\(ByteCountFormatter.string(fromByteCount: upload.uploadedBytes, countStyle: .file)) von \(ByteCountFormatter.string(fromByteCount: upload.size, countStyle: .file))")
                                .font(.footnote).foregroundStyle(Theme.textSecondary)
                        }
                    }
                } header: {
                    Text("Dateien")
                } footer: {
                    if openUploads.contains(where: { $0.state == .failed }) {
                        Button {
                            Task {
                                await sync.retryUploads()
                                await reload()
                            }
                        } label: {
                            Label("Fehlgeschlagene Uploads erneut versuchen", systemImage: "arrow.clockwise")
                        }
                        .estrichButton(.secondary)
                        .accessibilityIdentifier("retryUploadsButton")
                        .padding(.top, 6)
                    }
                }
            }
        }
        .navigationTitle("Synchronisation")
        .task(id: sync.revision) { await reload() }
        .refreshable { await sync.run(includeScope: true) }
    }

    private func reload() async {
        operations = (try? await sync.store.operations()) ?? []
        uploads = (try? await sync.store.uploads()) ?? []
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
        case .ticket: "Meldung an die IT"
        }
        let action = operation.type.split(separator: ".").last ?? ""
        return switch action {
        case "create", "announce": "\(object) angelegt"
        case "update": "\(object) geändert"
        case "delete": "\(object) gelöscht"
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

/// Hinweise des Betreibers kompakt als Banner (Startseite): laufende oder angekündigte Wartung und Ankündigungen.
/// Einzelne Hinweise lassen sich für die Sitzung ausblenden.
struct SystemNoticesBanner: View {
    let sync: SyncController
    @State private var dismissed: Set<String> = []

    var body: some View {
        let notices = sync.notices
        let announcements = (notices?.currentAnnouncements() ?? []).filter { !dismissed.contains($0.id) }
        VStack(spacing: 8) {
            if let maintenance = notices?.maintenance, maintenance.active, sync.maintenanceMessage == nil {
                NoticeCard(symbol: "wrench.and.screwdriver.fill", title: "Wartung läuft",
                           text: [maintenance.message, Self.window(maintenance)].compactMap { $0 }.joined(separator: " · "),
                           foreground: Theme.hint, background: Theme.hintBackground, onDismiss: nil)
            } else if let upcoming = notices?.upcomingMaintenance(), !dismissed.contains("maintenance") {
                NoticeCard(symbol: "calendar.badge.exclamationmark", title: "Geplante Wartung",
                           text: [Self.window(upcoming), upcoming.message].compactMap { $0 }.joined(separator: " · "),
                           foreground: Theme.hint, background: Theme.hintBackground) { dismissed.insert("maintenance") }
            }
            ForEach(announcements) { announcement in
                NoticeCard(symbol: Self.symbol(announcement.severity), title: announcement.title, text: announcement.text,
                           foreground: Self.foreground(announcement.severity), background: Self.background(announcement.severity)) {
                    dismissed.insert(announcement.id)
                }
            }
        }
        .accessibilityIdentifier("systemNotices")
    }

    static func window(_ maintenance: SystemNotices.Maintenance) -> String? {
        guard let start = maintenance.start else { return nil }
        let from = start.formatted(date: .abbreviated, time: .shortened)
        guard let end = maintenance.end else { return "ab \(from)" }
        let sameDay = Calendar.current.isDate(start, inSameDayAs: end)
        return "\(from) – \(sameDay ? end.formatted(date: .omitted, time: .shortened) : end.formatted(date: .abbreviated, time: .shortened))"
    }

    static func symbol(_ severity: SystemNotices.Announcement.Severity) -> String {
        switch severity {
        case .info: "info.circle.fill"
        case .warning: "exclamationmark.triangle.fill"
        case .critical: "exclamationmark.octagon.fill"
        }
    }

    static func foreground(_ severity: SystemNotices.Announcement.Severity) -> Color {
        switch severity {
        case .info: Theme.info
        case .warning: Theme.hint
        case .critical: Theme.error
        }
    }

    static func background(_ severity: SystemNotices.Announcement.Severity) -> Color {
        switch severity {
        case .info: Theme.infoBackground
        case .warning: Theme.hintBackground
        case .critical: Theme.errorBackground
        }
    }
}

private struct NoticeCard: View {
    let symbol: String
    let title: String
    let text: String
    let foreground: Color
    let background: Color
    let onDismiss: (() -> Void)?

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: symbol).foregroundStyle(foreground).accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.subheadline.weight(.semibold)).foregroundStyle(foreground)
                if !text.isEmpty {
                    Text(text).font(.footnote).foregroundStyle(.primary).fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
            if let onDismiss {
                Button(action: onDismiss) {
                    Image(systemName: "xmark").font(.caption.weight(.bold)).foregroundStyle(Theme.textSecondary)
                        .frame(width: 44, height: 44)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Hinweis ausblenden")
                .padding(.vertical, -12)
                .padding(.trailing, -10)
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(background, in: RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous))
        .accessibilityElement(children: .contain)
    }
}
