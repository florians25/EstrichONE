import EstrichCore
import PhotosUI
import SwiftUI
import VisionKit

/// Dokumentenscanner des Systems (VisionKit): mehrseitig, Kantenerkennung und Entzerrung automatisch.
/// Liefert die Seiten als JPEG; leer bei Abbruch oder Fehler.
struct DocumentCameraView: UIViewControllerRepresentable {
    let onFinish: @MainActor ([Data]) -> Void

    static var isAvailable: Bool { VNDocumentCameraViewController.isSupported }

    func makeUIViewController(context: Context) -> VNDocumentCameraViewController {
        let controller = VNDocumentCameraViewController()
        controller.delegate = context.coordinator
        return controller
    }

    func updateUIViewController(_ controller: VNDocumentCameraViewController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(onFinish: onFinish) }

    /// Der Scanner ruft die Methoden auf dem Hauptthread auf; die Ergebnisse gehen ohne Umweg an die Ansicht.
    final class Coordinator: NSObject, VNDocumentCameraViewControllerDelegate, @unchecked Sendable {
        private let onFinish: @MainActor ([Data]) -> Void

        init(onFinish: @escaping @MainActor ([Data]) -> Void) {
            self.onFinish = onFinish
        }

        nonisolated func documentCameraViewController(_ controller: VNDocumentCameraViewController, didFinishWith scan: VNDocumentCameraScan) {
            var pages: [Data] = []
            for index in 0..<scan.pageCount {
                if let data = scan.imageOfPage(at: index).jpegData(compressionQuality: 0.9) { pages.append(data) }
            }
            let result = pages
            MainActor.assumeIsolated { self.onFinish(result) }
        }

        nonisolated func documentCameraViewControllerDidCancel(_ controller: VNDocumentCameraViewController) {
            MainActor.assumeIsolated { self.onFinish([]) }
        }

        nonisolated func documentCameraViewController(_ controller: VNDocumentCameraViewController, didFailWithError error: any Error) {
            MainActor.assumeIsolated { self.onFinish([]) }
        }
    }
}

/// Ablauf „Dokument scannen“ für eine Bauakte: scannen → Seiten prüfen, umsortieren, löschen → Titel und Dokumentart
/// → als ein PDF in den Dokumenten der Bauakte ablegen (lokal zuerst, Übertragung wie bei Fotos).
/// Ohne Dokumentkamera (Simulator, manche iPads): Seiten aus Fotos übernehmen.
struct ScanFlowView: View {
    let bauakte: Bauakte
    /// `true`, wenn ein Dokument gespeichert wurde.
    let onFinish: (Bool) -> Void

    private enum Stage {
        case camera
        case review
    }

    @State private var stage: Stage = DocumentCameraView.isAvailable ? .camera : .review
    @State private var pages: [ScanPage] = []
    @State private var preparing = false

    var body: some View {
        Group {
            switch stage {
            case .camera:
                DocumentCameraView { images in
                    Task { await received(images) }
                }
                .ignoresSafeArea()
            case .review:
                ScanReviewView(bauakte: bauakte, pages: $pages, preparing: preparing,
                               onScanMore: DocumentCameraView.isAvailable ? { stage = .camera } : nil,
                               onAddImages: { images in Task { await add(images) } },
                               onCancel: { onFinish(false) },
                               onSaved: { onFinish(true) })
            }
        }
        .task {
            #if DEBUG
            // UI-Tests: Der Simulator hat keine Dokumentkamera – zwei erzeugte Seiten statt eines Scans.
            if UITestSupport.isActive, !DocumentCameraView.isAvailable, pages.isEmpty {
                await add([UITestSupport.samplePhoto(), UITestSupport.samplePhoto()])
            }
            #endif
        }
    }

    private func received(_ images: [Data]) async {
        if images.isEmpty, pages.isEmpty {
            onFinish(false)
            return
        }
        stage = .review
        await add(images)
    }

    private func add(_ images: [Data]) async {
        guard !images.isEmpty else { return }
        preparing = true
        let prepared = await Task.detached(priority: .userInitiated) { images.compactMap(ScanPDFBuilder.preparePage) }.value
        pages.append(contentsOf: prepared)
        preparing = false
    }
}

/// Seiten prüfen und als PDF ablegen.
struct ScanReviewView: View {
    let bauakte: Bauakte
    @Binding var pages: [ScanPage]
    let preparing: Bool
    let onScanMore: (() -> Void)?
    let onAddImages: ([Data]) -> Void
    let onCancel: () -> Void
    let onSaved: () -> Void

    @Environment(AppModel.self) private var model
    @State private var title = ""
    @State private var titleEdited = false
    @State private var category: DocumentCategory = .delivery
    @State private var recognizesText = true
    @State private var saving = false
    @State private var errorMessage: String?
    @State private var pickerItems: [PhotosPickerItem] = []

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Titel", text: Binding(get: { title }, set: { title = $0; titleEdited = true }))
                        .accessibilityIdentifier("scanTitleField")
                    Picker("Dokumentart", selection: $category) {
                        ForEach(DocumentCategory.allCases) { Label($0.title, systemImage: $0.symbol).tag($0) }
                    }
                    .accessibilityIdentifier("scanCategoryPicker")
                } header: {
                    Text("Dokument für \(bauakte.number.map { "\($0) · " } ?? "")\(bauakte.title)")
                }

                Section {
                    if pages.isEmpty, !preparing {
                        Text(onScanMore == nil
                             ? "Auf diesem Gerät gibt es keinen Dokumentenscanner. Seiten können aus Fotos übernommen werden."
                             : "Noch keine Seiten.")
                            .font(.subheadline)
                            .foregroundStyle(Theme.textSecondary)
                    }
                    ForEach(Array(pages.enumerated()), id: \.element.id) { index, page in
                        HStack(spacing: 12) {
                            ScanPageThumbnail(page: page)
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Seite \(index + 1)").font(.body.weight(.medium))
                                Text(ByteCountFormatter.string(fromByteCount: Int64(page.jpeg.count), countStyle: .file))
                                    .font(.caption).foregroundStyle(Theme.textSecondary)
                            }
                        }
                        .accessibilityIdentifier("scanPage")
                    }
                    .onMove { pages.move(fromOffsets: $0, toOffset: $1) }
                    .onDelete { pages.remove(atOffsets: $0) }
                    if preparing {
                        HStack { ProgressView(); Text("Seiten werden aufbereitet …").foregroundStyle(Theme.textSecondary) }
                    }
                    if let onScanMore {
                        Button(action: onScanMore) {
                            Label("Weitere Seiten scannen", systemImage: "doc.viewfinder")
                        }
                    }
                    PhotosPicker(selection: $pickerItems, maxSelectionCount: 30, matching: .images) {
                        Label("Seiten aus Fotos hinzufügen", systemImage: "photo.on.rectangle")
                    }
                } header: {
                    SectionHeader("Seiten", count: pages.count) {
                        if pages.count > 1 { EditButton().font(.subheadline) }
                    }
                } footer: {
                    Text("Reihenfolge über „Bearbeiten“ ändern, einzelne Seiten nach links wischen zum Löschen.")
                }

                Section {
                    Toggle("Text erkennen (durchsuchbares PDF)", isOn: $recognizesText)
                } footer: {
                    Text("Die Texterkennung läuft auf dem Gerät, auch ohne Verbindung.")
                }

                if let errorMessage {
                    Section { StatusMessage(kind: .error, text: errorMessage) }
                }
            }
            .navigationTitle("Dokument scannen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen", action: onCancel).disabled(saving)
                }
            }
            .safeAreaInset(edge: .bottom) {
                Button {
                    Task { await save() }
                } label: {
                    ActionLabel(saving ? "Wird gespeichert …" : "Als PDF ablegen", systemImage: "doc.badge.plus", isBusy: saving)
                }
                .primaryActionStyle()
                .disabled(saving || preparing || pages.isEmpty || title.trimmingCharacters(in: .whitespaces).isEmpty)
                .accessibilityIdentifier("saveScanButton")
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(.bar)
            }
            .onAppear { updateDefaultTitle() }
            .onChange(of: category) { updateDefaultTitle() }
            .onChange(of: pickerItems) { _, items in
                guard !items.isEmpty else { return }
                Task {
                    var images: [Data] = []
                    for item in items {
                        if let data = try? await item.loadTransferable(type: Data.self) { images.append(data) }
                    }
                    pickerItems = []
                    onAddImages(images)
                }
            }
            .interactiveDismissDisabled(saving || !pages.isEmpty)
        }
    }

    /// Titel aus Dokumentart und Datum, solange er nicht von Hand geändert wurde.
    private func updateDefaultTitle() {
        guard !titleEdited else { return }
        title = "\(category.title) \(Date.now.formatted(date: .numeric, time: .omitted))"
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        let pages = pages
        let title = title.trimmingCharacters(in: .whitespaces)
        let recognizesText = recognizesText
        let author = model.active?.user.displayName
        let pdf = await Task.detached(priority: .userInitiated) { () -> Data? in
            let text = recognizesText ? pages.map(ScanPDFBuilder.recognizeText) : []
            return ScanPDFBuilder.makePDF(pages: pages, text: text, title: title, author: author)
        }.value
        guard let pdf else {
            errorMessage = "Das PDF konnte nicht erstellt werden."
            return
        }
        do throws(BauakteError) {
            try await service.addDocument(pdf, title: title, category: category, to: bauakte.id)
            model.active?.sync?.localChangeMade()
            onSaved()
        } catch {
            errorMessage = error.message
        }
    }
}

/// Verkleinerte Vorschau einer Scanseite (einmal dekodiert).
private struct ScanPageThumbnail: View {
    let page: ScanPage
    @State private var image: UIImage?

    var body: some View {
        RoundedRectangle(cornerRadius: 6, style: .continuous)
            .fill(Color(uiColor: .secondarySystemBackground))
            .frame(width: 48, height: 64)
            .overlay {
                if let image {
                    Image(uiImage: image).resizable().scaledToFit()
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Theme.separator))
            .task(id: page.id) {
                let data = page.jpeg
                image = await Task.detached { UIImage(data: data)?.preparingThumbnail(of: CGSize(width: 96, height: 128)) }.value
            }
            .accessibilityHidden(true)
    }
}
