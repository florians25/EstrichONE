import EstrichCore
import QuickLook
import SwiftUI

/// Zeile eines Dokuments: Art als Symbol, Titel, Art · Datum · Größe, Übertragungszustand.
struct DocumentRow: View {
    let file: FileItem
    var isLoading = false

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: file.category?.symbol ?? (file.isPDF ? "doc.richtext" : "doc"))
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(Theme.navyText)
                .frame(width: 34, height: 40)
                .background(Theme.navyBackground, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(file.displayName)
                    .font(.body.weight(.medium))
                    .foregroundStyle(.primary)
                    .lineLimit(2)
                Text(details)
                    .font(.caption)
                    .foregroundStyle(Theme.textSecondary)
                    .lineLimit(1)
                SyncStateBadge(state: file.syncState)
            }
            Spacer(minLength: 4)
            if isLoading {
                ProgressView()
            } else {
                Image(systemName: "eye")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(.tertiary)
                    .accessibilityHidden(true)
            }
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityHint("Öffnet die Vorschau")
    }

    private var details: String {
        var parts: [String] = []
        if let category = file.category { parts.append(category.title) }
        if let date = file.capturedAt ?? file.createdAt { parts.append(date.formatted(date: .abbreviated, time: .omitted)) }
        if file.sizeBytes > 0 { parts.append(ByteCountFormatter.string(fromByteCount: file.sizeBytes, countStyle: .file)) }
        return parts.joined(separator: " · ")
    }
}

/// Vorschau von Dateien (PDF, Bilder) mit QuickLook. Der Inhalt kommt aus der verschlüsselten Ablage des Geräts
/// oder vom Server; für die Vorschau entsteht eine geschützte temporäre Kopie, die beim Schließen gelöscht wird.
@MainActor
enum FilePreview {
    enum Failure: Error {
        case notAvailable
    }

    static func temporaryCopy(of file: FileItem, using model: AppModel) async throws(Failure) -> URL {
        guard let active = model.active, let service = active.service else { throw .notAvailable }
        var content = service.localContent(of: file.id)
        if content == nil, file.isStoredOnServer {
            let downloaded: Data? = try? await active.client.get("files/\(file.id.uuidString.lowercased())/content")
            content = downloaded
        }
        guard let content else { throw .notAvailable }
        let folder = FileManager.default.temporaryDirectory
            .appending(path: "Vorschau", directoryHint: .isDirectory)
            .appending(path: file.id.uuidString, directoryHint: .isDirectory)
        let name = String(file.displayName.map { "/:\\".contains($0) ? "_" : $0 }.prefix(80))
        let url = folder.appending(path: "\(name).\(file.fileExtension)")
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try content.write(to: url, options: [.atomic, .completeFileProtection])
        } catch {
            throw .notAvailable
        }
        return url
    }

    /// Entfernt alle temporären Vorschaukopien.
    static func removeCopies() {
        try? FileManager.default.removeItem(at: FileManager.default.temporaryDirectory.appending(path: "Vorschau", directoryHint: .isDirectory))
    }
}

/// Öffnet Dateien per QuickLook: `preview(file)` lädt und zeigt, Fehler erscheinen als Meldung.
struct FilePreviewModifier: ViewModifier {
    @Binding var file: FileItem?
    @Environment(AppModel.self) private var model
    @State private var url: URL?
    @State private var failed = false

    func body(content: Content) -> some View {
        content
            .quickLookPreview($url)
            .task(id: file?.id) {
                guard let file else { return }
                do throws(FilePreview.Failure) {
                    url = try await FilePreview.temporaryCopy(of: file, using: model)
                } catch {
                    failed = true
                    self.file = nil
                }
            }
            .onChange(of: url) { _, new in
                if new == nil {
                    file = nil
                    FilePreview.removeCopies()
                }
            }
            .alert("Vorschau nicht möglich", isPresented: $failed) {
                Button("OK", role: .cancel) {}
            } message: {
                Text("Die Datei ist auf diesem Gerät nicht vorhanden und noch nicht auf dem Server. Bitte nach der Übertragung erneut versuchen.")
            }
    }
}

extension View {
    func filePreview(_ file: Binding<FileItem?>) -> some View {
        modifier(FilePreviewModifier(file: file))
    }
}

/// Dokumentart und Titel eines Dokuments ändern.
struct EditDocumentView: View {
    let file: FileItem
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var title = ""
    @State private var category: DocumentCategory = .other

    var body: some View {
        NavigationStack {
            Form {
                TextField("Titel", text: $title)
                Picker("Dokumentart", selection: $category) {
                    ForEach(DocumentCategory.allCases) { Label($0.title, systemImage: $0.symbol).tag($0) }
                }
            }
            .navigationTitle("Dokument")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen", action: onDone) }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(title.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
            .onAppear {
                title = file.displayName
                category = file.category ?? .other
            }
        }
        .presentationDetents([.medium])
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        if title != file.displayName { try? await service.setCaption(title, of: file.id) }
        if category != file.category { try? await service.setCategory(category, of: file.id) }
        model.active?.sync?.localChangeMade()
        onDone()
    }
}
