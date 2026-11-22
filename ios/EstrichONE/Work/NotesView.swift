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
                Section {
                    TextField("Zum Beispiel: Schlüssel liegt beim Nachbarn, Haus Nr. 3", text: $text, axis: .vertical)
                        .lineLimit(6...16)
                        .font(Typo.body)
                        .focused($focused)
                        .accessibilityLabel("Notiz")
                        .accessibilityIdentifier("noteTextField")
                } footer: {
                    Text("Für alle Beteiligten der Bauakte sichtbar. Zeilenumbrüche bleiben erhalten, Telefonnummern und Links sind antippbar.")
                }
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

    @State private var expanded = false

    var body: some View {
        VStack(alignment: .leading, spacing: Space.xs) {
            if note.isPinned {
                Label("Angeheftet", systemImage: "pin.fill")
                    .labelStyle(TightLabelStyle())
                    .font(Typo.labelStrong)
                    .foregroundStyle(Theme.accent)
            }
            // Freitext gut lesbar: Zeilenumbrüche bleiben, Links und Telefonnummern antippbar, lange Notizen gekürzt.
            let text = FreeText.clean(note.text) ?? ""
            let long = FreeText.isLong(text)
            Text(FreeTextLinks.attributed(text))
                .font(Typo.body)
                .foregroundStyle(Theme.textPrimary)
                .lineSpacing(2)
                .lineLimit(long && !expanded ? 6 : nil)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
                .tint(Theme.accent)
            if long {
                Button(expanded ? "Weniger anzeigen" : "Mehr anzeigen") { withAnimation(.snappy) { expanded.toggle() } }
                    .font(Typo.subheadStrong)
                    .buttonStyle(.borderless)
                    .foregroundStyle(Theme.accent)
            }
            HStack(spacing: Space.s) {
                Text([authorName, note.createdAt?.shortStamp].compactMap { $0 }
                    .joined(separator: ", "))
                    .font(Typo.meta)
                    .foregroundStyle(Theme.textSecondary)
                Spacer(minLength: 0)
                SyncStateBadge(state: note.syncState)
            }
        }
        .padding(.vertical, Space.xs)
        .accessibilityElement(children: .contain)
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
                        Text(entry.at.shortStamp)
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
