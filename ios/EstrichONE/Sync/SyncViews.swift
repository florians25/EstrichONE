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
        if sync.isOffline {
            return ("Offline – Änderungen werden gespeichert", "wifi.slash", Theme.textSecondary, Theme.surfaceAlt)
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
                            Text(upload.state == .failed ? "Upload fehlgeschlagen" : "Foto wird übertragen")
                                .foregroundStyle(upload.state == .failed ? Theme.error : .primary)
                            ProgressView(value: Double(upload.uploadedBytes), total: Double(max(upload.size, 1)))
                            Text("\(ByteCountFormatter.string(fromByteCount: upload.uploadedBytes, countStyle: .file)) von \(ByteCountFormatter.string(fromByteCount: upload.size, countStyle: .file))")
                                .font(.footnote).foregroundStyle(Theme.textSecondary)
                        }
                    }
                } header: {
                    Text("Dateien")
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
