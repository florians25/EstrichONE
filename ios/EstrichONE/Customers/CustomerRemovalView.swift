import EstrichCore
import SwiftUI

/// Kunden löschen oder archivieren (nur online, Rechte `contacts.delete` bzw. `contacts.edit`). Lädt die Bezüge des
/// Kunden: Ohne Bezüge wird nach Eintippen der Kundennummer endgültig gelöscht; mit Bauakten, Anrufen o. ä. nennt das
/// Blatt die Bezüge und bietet stattdessen Archivieren an. Danach entfernt bzw. aktualisiert der Abgleich den Kunden
/// auf dem Gerät (Tombstone des Sync-Protokolls).
struct CustomerRemovalView: View {
    let contact: Contact
    let canDelete: Bool
    let canArchive: Bool
    /// `true`: gelöscht, `false`: archiviert.
    let onDone: (Bool) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var references: CustomerReferences?
    @State private var error: String?
    @State private var confirm = ""
    @State private var busy = false

    var body: some View {
        NavigationStack {
            Form {
                if let references {
                    content(references)
                } else if error == nil {
                    Section {
                        HStack(spacing: Space.s) {
                            ProgressView()
                            Text("Verknüpfte Daten werden geprüft …").foregroundStyle(Theme.textSecondary)
                        }
                    }
                }
                if let error {
                    Section { Text(error).foregroundStyle(Theme.danger) }
                }
            }
            .navigationTitle(references?.deletable == false ? "Nicht löschbar" : "Kunde löschen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
            }
            .task { await load() }
        }
    }

    @ViewBuilder
    private func content(_ refs: CustomerReferences) -> some View {
        if refs.deletable {
            Section {
                Text(Self.deletableText(name: contact.name, number: refs.customerNumber))
            }
            if canDelete {
                Section {
                    TextField(refs.confirmText, text: $confirm)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("customerDeleteConfirm")
                } header: {
                    Text(refs.customerNumber == nil ? "Zum Bestätigen den Namen eintippen" : "Zum Bestätigen die Kundennummer \(refs.confirmText) eintippen")
                }
                Section {
                    Button("Endgültig löschen", role: .destructive) {
                        Task { await run(deleted: true) { client in try await client.deleteCustomer(refs.id, confirm: confirm) } }
                    }
                    .disabled(busy || !CustomerRemoval.confirms(confirm, refs.confirmText))
                    .accessibilityIdentifier("customerDeleteButton")
                }
            } else {
                Section { Text("Zum Löschen fehlt die Berechtigung „Kunden löschen“.").foregroundStyle(Theme.textSecondary) }
            }
        } else {
            Section {
                Text(Self.blockedText(summary: refs.summary))
                ForEach(refs.references, id: \.kind) { reference in
                    VStack(alignment: .leading, spacing: 2) {
                        LabeledContent(reference.label, value: "\(reference.count)")
                        ForEach(reference.items) { item in
                            Text(item.text).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                        }
                    }
                }
            } header: {
                Text("Kann nicht gelöscht werden")
            }
        }
        if canArchive, !refs.archived {
            Section {
                Button {
                    Task { await run(deleted: false) { client in try await client.archiveCustomer(refs.id) } }
                } label: {
                    Label(refs.deletable ? "Stattdessen archivieren" : "Archivieren", systemImage: "archivebox")
                }
                .disabled(busy)
                .accessibilityIdentifier("customerArchiveButton")
            }
        }
    }

    static func deletableText(name: String, number: String?) -> String {
        var text = "\(name) hat keine Bauakten, Anrufe oder andere verknüpfte Daten. Stammdaten, Telefonnummern, "
        text += "E-Mail-Adressen, Notiz und Ansprechpartner werden endgültig entfernt."
        if let number { text += " Die Kundennummer \(number) wird nicht erneut vergeben." }
        return text
    }

    static func blockedText(summary: String) -> String {
        "Noch verknüpft: \(summary). Stattdessen archivieren: Der Kunde bleibt mit allen Unterlagen erhalten, "
            + "erscheint aber nicht mehr in Auswahllisten und standardmäßig nicht in der Kundenkartei."
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do {
            references = try await client.customerReferences(contact.id)
        } catch {
            self.error = "Löschen und Archivieren gehen nur mit Verbindung zum Server. \(error.message)"
        }
    }

    private func run(deleted: Bool, _ action: (AuthorizedClient) async throws -> Void) async {
        guard let client = model.active?.client else { return }
        busy = true
        defer { busy = false }
        do {
            try await action(client)
        } catch let failure as APIError {
            self.error = failure.message
            return
        } catch {
            self.error = APIError.offline.message
            return
        }
        // Der Abgleich holt den Tombstone (gelöscht) bzw. den archivierten Stand.
        await model.active?.sync?.run()
        onDone(deleted)
        dismiss()
    }
}
