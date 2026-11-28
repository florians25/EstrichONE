import EstrichCore
import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

/// Eingangsbeleg zur Bauakte hochladen (Modul „Eingangsbelege“, nur online): Foto aus der Mediathek oder Datei (PDF,
/// XRechnung). Der Server liest E-Rechnungen und erkennt doppelte Dateien; an Lexware Office gibt das Büro den Beleg weiter.
/// Sichtbar mit dem Recht „Eingangsbelege hochladen“.
struct PurchaseUploadSection: View {
    let bauakteId: UUID

    @Environment(AppModel.self) private var model
    @State private var canUpload = false
    @State private var photo: PhotosPickerItem?
    @State private var importing = false
    @State private var busy = false
    @State private var message: String?

    var body: some View {
        Group {
            if canUpload {
                Section {
                    PhotosPicker(selection: $photo, matching: .images) {
                        Label("Foto eines Belegs hochladen", systemImage: "camera")
                    }
                    .disabled(busy)
                    .accessibilityIdentifier("purchasePhoto")
                    Button {
                        importing = true
                    } label: {
                        Label("PDF oder XRechnung hochladen", systemImage: "doc.badge.plus")
                    }
                    .disabled(busy)
                    .accessibilityIdentifier("purchaseFile")
                    if busy {
                        ProgressView("Wird hochgeladen")
                    }
                } header: {
                    Text("Eingangsbelege")
                } footer: {
                    Text("Lieferschein, Rechnung oder Quittung zu dieser Bauakte. Das Büro prüft den Beleg und gibt ihn an Lexware Office weiter.")
                }
            }
        }
        .task(id: model.active?.sync?.revision ?? 0) {
            if let store = model.active?.sync?.store {
                canUpload = await MyPermissions(store: store).cached().contains("purchases.edit")
            }
        }
        .onChange(of: photo) { _, item in
            guard let item else { return }
            Task {
                defer { photo = nil }
                guard let raw = try? await item.loadTransferable(type: Data.self) else {
                    message = "Das Foto konnte nicht gelesen werden."
                    return
                }
                await upload(Self.jpeg(raw) ?? raw, fileName: "Beleg.jpg")
            }
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.pdf, .xml]) { result in
            guard case let .success(url) = result else { return }
            let access = url.startAccessingSecurityScopedResource()
            defer { if access { url.stopAccessingSecurityScopedResource() } }
            guard let data = try? Data(contentsOf: url) else {
                message = "Die Datei konnte nicht gelesen werden."
                return
            }
            Task { await upload(data, fileName: url.lastPathComponent) }
        }
        .alert("Eingangsbeleg", isPresented: Binding(get: { message != nil }, set: { if !$0 { message = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(message ?? "")
        }
    }

    private static let offline = "Belege lassen sich nur mit Verbindung zum Server hochladen."

    /// Fotos gehen als JPEG an den Server (HEIC kennt er nicht); große Fotos werden stärker komprimiert.
    private static func jpeg(_ data: Data) -> Data? {
        guard let image = UIImage(data: data) else { return nil }
        for quality in [0.8, 0.6, 0.4] as [CGFloat] {
            if let jpeg = image.jpegData(compressionQuality: quality), jpeg.count <= Lexware.purchaseMaxBytes { return jpeg }
        }
        return image.jpegData(compressionQuality: 0.3)
    }

    private func upload(_ data: Data, fileName: String) async {
        if let problem = Lexware.purchaseUploadProblem(byteCount: data.count) {
            message = problem
            return
        }
        guard let client = model.active?.client else {
            message = Self.offline
            return
        }
        busy = true
        defer { busy = false }
        do throws(APIError) {
            message = Lexware.purchaseUploadText(try await client.uploadPurchase(data, fileName: fileName, bauakteId: bauakteId))
        } catch {
            message = error == .offline ? Self.offline : error.message
        }
    }
}
