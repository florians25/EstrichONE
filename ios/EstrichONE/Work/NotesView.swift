import EstrichCore
import SwiftUI

/// Notiz schreiben: ein Feld, sofort lokal gespeichert.
struct NewNoteView: View {
    let bauakteId: UUID
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var text = ""
    @State private var error: String?
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            Form {
                TextField("Notiz", text: $text, axis: .vertical)
                    .lineLimit(4...12)
                    .focused($focused)
                    .accessibilityIdentifier("noteTextField")
                if let error {
                    Text(error).foregroundStyle(Theme.danger)
                }
            }
            .navigationTitle("Neue Notiz")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .accessibilityIdentifier("saveNoteButton")
                }
            }
            .task { focused = true }
        }
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        do throws(BauakteError) {
            try await service.addNote(text, to: bauakteId)
            model.active?.sync?.localChangeMade()
            onDone()
        } catch {
            self.error = error.message
        }
    }
}

struct NoteRow: View {
    let note: Note
    var authorName: String?

    var body: some View {
        VStack(alignment: .leading, spacing: Space.xs) {
            if note.isPinned {
                Label("Angeheftet", systemImage: "pin.fill")
                    .labelStyle(TightLabelStyle())
                    .font(Typo.labelStrong)
                    .foregroundStyle(Theme.accent)
            }
            Text(note.text.trimmingCharacters(in: .whitespacesAndNewlines))
                .font(Typo.body)
                .foregroundStyle(Theme.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: Space.s) {
                Text([authorName, note.createdAt?.formatted(.dateTime.day().month(.abbreviated).hour().minute())].compactMap { $0 }
                    .joined(separator: " · "))
                    .font(Typo.meta)
                    .foregroundStyle(Theme.textSecondary)
                Spacer(minLength: 0)
                SyncStateBadge(state: note.syncState)
            }
        }
        .padding(.vertical, Space.xs)
        .accessibilityElement(children: .combine)
    }
}

/// Verlauf einer Bauakte; die Sätze kommen vom Server. Offline gibt es einen klaren Hinweis statt einer leeren Liste.
struct HistoryView: View {
    let bauakteId: UUID

    @Environment(AppModel.self) private var model
    @State private var entries: [BauakteHistoryEntry] = []
    @State private var state: LoadState = .loading

    enum LoadState { case loading, loaded, offline, notOnServer }

    var body: some View {
        List {
            switch state {
            case .loading:
                ProgressView().frame(maxWidth: .infinity)
            case .offline:
                Label("Der Verlauf ist nur mit Verbindung zum Server verfügbar.", systemImage: "wifi.slash")
                    .foregroundStyle(Theme.textSecondary)
            case .notOnServer:
                Label("Die Bauakte ist noch nicht übertragen. Der Verlauf beginnt mit der Übertragung.", systemImage: "arrow.up.circle")
                    .foregroundStyle(Theme.textSecondary)
            case .loaded:
                if entries.isEmpty {
                    Text("Noch keine Einträge.").foregroundStyle(Theme.textSecondary)
                }
                ForEach(entries.indices, id: \.self) { index in
                    let entry = entries[index]
                    VStack(alignment: .leading, spacing: 2) {
                        Text(entry.text.compactText ?? "")
                        Text(entry.at.formatted(date: .abbreviated, time: .shortened))
                            .font(.caption).foregroundStyle(Theme.textSecondary)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
        }
        .navigationTitle("Verlauf")
        .navigationBarTitleDisplayMode(.inline)
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            entries = try await client.history(of: bauakteId)
            state = .loaded
        } catch {
            state = error.code == "BAUAKTE_NOT_FOUND" ? .notOnServer : .offline
        }
    }
}
