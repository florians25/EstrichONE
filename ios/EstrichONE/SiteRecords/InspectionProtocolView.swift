import EstrichCore
import PencilKit
import QuickLook
import SwiftUI

enum InspectionProtocol {
    /// Beschriftung des Fotos mit der Unterschrift (der Server kennt kein eigenes Feld dafür).
    static let signatureCaption = "Unterschrift Kunde"
}

/// Protokoll einer Erstbegehung (auch im Büro erfasste): Ergebnis, Datum, Ersteller, Prüfpunkte mit Fotos, Notizen,
/// Unterschrift. Korrigieren legt eine neue Fassung an; das PDF kommt vom Server (Bauakte-PDF, Teil „Prüfungen“).
/// iPad: Prüfpunkte links, Einzelheiten des gewählten Punkts rechts.
struct InspectionProtocolView: View {
    let inspection: Inspection
    let bauakte: Bauakte
    var performerName: String?
    /// Direkt das PDF öffnen (Schnellzugriff aus dem Kopf der Bauakte).
    var opensPDF = false

    @Environment(AppModel.self) private var model
    @State private var schema: FormSchema?
    @State private var lists = ValueLists([])
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var photos: [FileItem] = []
    @State private var selected: InitialCheck?
    @State private var correcting = false
    @State private var viewing: FileItem?
    @State private var pdf: FileItem?
    @State private var pdfURL: URL?
    @State private var pdfMessage: String?
    @State private var loadingPDF = false

    var body: some View {
        Group {
            if sizeClass == .regular, !inspection.notRequired, inspection.schemaVersion == nil {
                HStack(spacing: 0) {
                    List(selection: $selected) {
                        summarySection
                        checksSection(selectable: true)
                        notesSection
                    }
                    .frame(width: 380)
                    Divider()
                    checkDetail(selected ?? InitialCheck.allCases[0])
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            } else {
                List {
                    summarySection
                    if !inspection.notRequired {
                        if inspection.schemaVersion != nil {
                            SchemaInspectionResultSections(inspection: inspection, schema: schema, lists: lists)
                        } else {
                            checksSection(selectable: false)
                        }
                    }
                    notesSection
                    otherPhotosSection
                }
            }
        }
        .navigationTitle("Erstbegehung")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    Button("Korrigieren", systemImage: "pencil") { correcting = true }
                        .accessibilityIdentifier("correctInspectionButton")
                    Button("Als PDF ansehen", systemImage: "doc.richtext") { Task { await openPDF() } }
                        .accessibilityIdentifier("inspectionPDFButton")
                } label: {
                    Label("Aktionen", systemImage: "ellipsis.circle")
                }
                .accessibilityIdentifier("inspectionActions")
            }
        }
        .sheet(isPresented: $correcting) {
            InspectionEntryView(bauakte: bauakte, correcting: inspection) { correcting = false }
        }
        .sheet(item: $viewing) { file in
            PhotoViewer(photos: photos, selection: file.id) { Task { await loadPhotos() } }
        }
        .quickLookPreview($pdfURL)
        .task { if opensPDF, pdfURL == nil { await openPDF() } }
        .task {
            guard inspection.schemaVersion != nil, let service = model.active?.service else { return }
            schema = await service.formSchema(key: "erstbegehung", using: nil)
            lists = await service.valueLists()
        }
        .alert("PDF nicht verfügbar", isPresented: Binding(get: { pdfMessage != nil }, set: { if !$0 { pdfMessage = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(pdfMessage ?? "")
        }
        .overlay { if loadingPDF { ProgressView("PDF wird geladen …").padding().background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12)) } }
        .task(id: model.active?.sync?.revision) { await loadPhotos() }
    }

    // MARK: Abschnitte

    private var summarySection: some View {
        Section {
            HStack(spacing: Space.m) {
                IconBadge(symbol: statusSymbol, tint: statusColor, background: statusColor.opacity(0.14), size: 44)
                VStack(alignment: .leading, spacing: 2) {
                    Text(statusText).font(Typo.headline).foregroundStyle(Theme.textPrimary).accessibilityIdentifier("inspectionStatus")
                    Text([inspection.performedAt?.formatted(date: .long, time: .shortened), performerName].compactMap { $0 }.joined(separator: " · "))
                        .font(Typo.meta).foregroundStyle(Theme.textSecondary)
                    if inspection.correctsInspectionId != nil {
                        Label("Korrigierte Fassung", systemImage: "pencil.circle")
                            .labelStyle(TightLabelStyle())
                            .font(Typo.meta)
                            .foregroundStyle(Theme.textSecondary)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                SyncStateBadge(state: inspection.syncState, showsSynced: true)
            }
            .padding(.vertical, Space.xs)
        } header: {
            Text(bauakte.number.map { "\($0) · \(bauakte.title)" } ?? bauakte.title)
        }
    }

    private func checksSection(selectable: Bool) -> some View {
        Section("Prüfpunkte") {
            ForEach(InitialCheck.allCases) { check in
                let result = inspection.checks[check]
                let checkPhotos = photos(for: check)
                Group {
                    if selectable {
                        checkRow(check, result: result, photoCount: checkPhotos.count).tag(check)
                    } else {
                        VStack(alignment: .leading, spacing: 8) {
                            checkRow(check, result: result, photoCount: checkPhotos.count)
                            if !checkPhotos.isEmpty {
                                PhotoGrid(photos: checkPhotos) { viewing = $0 }
                            }
                        }
                    }
                }
                .accessibilityIdentifier("protocolCheck-\(check.rawValue)")
            }
        }
    }

    private func checkRow(_ check: InitialCheck, result: CheckResult?, photoCount: Int) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text(check.title).font(.subheadline)
                if photoCount > 0 {
                    Label("\(photoCount) \(photoCount == 1 ? "Foto" : "Fotos")", systemImage: "photo")
                        .labelStyle(TightLabelStyle()).font(.caption).foregroundStyle(Theme.textSecondary)
                }
            }
            Spacer(minLength: 6)
            ResultChip(result: result)
        }
    }

    private func checkDetail(_ check: InitialCheck) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                Text(check.title).font(.title3.weight(.semibold))
                ResultChip(result: inspection.checks[check])
                let checkPhotos = photos(for: check)
                if checkPhotos.isEmpty {
                    Text("Keine Fotos zu diesem Punkt.").foregroundStyle(Theme.textSecondary)
                } else {
                    PhotoGrid(photos: checkPhotos) { viewing = $0 }
                }
                let others = otherPhotos
                if !others.isEmpty {
                    Divider()
                    Text("Weitere Fotos und Unterschrift").font(.headline)
                    PhotoGrid(photos: others) { viewing = $0 }
                }
            }
            .padding(24)
        }
    }

    @ViewBuilder
    private var notesSection: some View {
        Section("Notizen") {
            if let notes = inspection.notes, !notes.isEmpty {
                Text(notes.compactText ?? "").textSelection(.enabled).accessibilityIdentifier("inspectionNotes")
            } else {
                Text("Keine Notizen.").foregroundStyle(Theme.textSecondary)
            }
        }
        if let remarks = inspection.remarks, !remarks.isEmpty {
            Section(inspection.notRequired ? "Begründung" : "Bemerkung") {
                Text(remarks).textSelection(.enabled)
            }
        }
    }

    @ViewBuilder
    private var otherPhotosSection: some View {
        let others = otherPhotos
        if !others.isEmpty {
            Section("Weitere Fotos und Unterschrift") {
                PhotoGrid(photos: others) { viewing = $0 }
                    .listRowInsets(EdgeInsets(top: 8, leading: 8, bottom: 8, trailing: 8))
            }
        }
    }

    // MARK: Daten

    private func photos(for check: InitialCheck) -> [FileItem] {
        photos.filter { $0.caption == check.title }
    }

    private var otherPhotos: [FileItem] {
        let titles = Set(InitialCheck.allCases.map(\.title))
        return photos.filter { !titles.contains($0.caption ?? "") }
    }

    private var statusText: String {
        if inspection.notRequired { return "Keine Erstbegehung notwendig" }
        let defects = inspection.checks.values.filter { $0 == .notOk }.count
        return defects == 0 ? "Erstbegehung ohne Mängel" : "Erstbegehung: \(defects) \(defects == 1 ? "Mangel" : "Mängel")"
    }

    private var statusSymbol: String {
        inspection.notRequired ? "minus.circle.fill" : (inspection.hasDefects ? "exclamationmark.triangle.fill" : "checkmark.seal.fill")
    }

    private var statusColor: Color {
        inspection.notRequired ? Theme.textSecondary : (inspection.hasDefects ? Theme.warning : Theme.success)
    }

    private func loadPhotos() async {
        guard let service = model.active?.service else { return }
        let ids = Set(inspection.photoFileIds)
        photos = ((try? await service.files(of: bauakte.id)) ?? []).filter { ids.contains($0.id) }
    }

    /// Bauakte-PDF mit dem Teil „Prüfungen“ vom Server, angezeigt mit QuickLook (Teilen über QuickLook).
    private func openPDF() async {
        guard let client = model.active?.client else { return }
        loadingPDF = true
        defer { loadingPDF = false }
        do throws(APIError) {
            let data: Data = try await client.get("bauakten/\(bauakte.id.uuidString.lowercased())/report.pdf",
                                                  query: [URLQueryItem(name: "parts", value: "pruefungen")])
            let folder = FileManager.default.temporaryDirectory.appending(path: "Vorschau", directoryHint: .isDirectory)
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let url = folder.appending(path: "Erstbegehung \(bauakte.number ?? bauakte.title).pdf")
            do {
                try data.write(to: url, options: [.atomic, .completeFileProtection])
                pdfURL = url
            } catch {
                pdfMessage = "Das PDF konnte nicht gespeichert werden."
            }
        } catch {
            pdfMessage = error == .offline
                ? "Das PDF erstellt der Server – bitte mit Verbindung erneut versuchen."
                : (error.status == 404 ? "Die Bauakte ist noch nicht übertragen. Bitte nach der Synchronisation erneut versuchen." : error.message)
        }
    }
}

private struct ResultChip: View {
    let result: CheckResult?

    var body: some View {
        let (text, foreground, background): (String, Color, Color) = switch result {
        case .ok: ("In Ordnung", Theme.success, Theme.successSoft)
        case .notOk: ("Mangel", Theme.warning, Theme.warningSoft)
        case .notApplicable: ("Entfällt", Theme.textSecondary, Theme.fill)
        case nil: ("Nicht geprüft", Theme.textSecondary, Theme.fill)
        }
        Text(text)
            .font(.caption.weight(.semibold))
            .foregroundStyle(foreground)
            .padding(.horizontal, 8).padding(.vertical, 3)
            .background(background, in: Capsule())
    }
}

/// Unterschrift mit Finger oder Apple Pencil (PencilKit); liefert ein PNG oder `nil` bei Abbruch.
struct SignatureView: View {
    var title = "Unterschrift des Kunden"
    let onDone: (Data?) -> Void
    @State private var canvas = PKCanvasView()
    @State private var isEmpty = true

    var body: some View {
        NavigationStack {
            VStack(spacing: 12) {
                Text("Bitte hier unterschreiben").font(.subheadline).foregroundStyle(Theme.textSecondary)
                SignatureCanvas(canvas: canvas) { isEmpty = $0 }
                    .frame(height: 260)
                    .background(Color.white, in: RoundedRectangle(cornerRadius: 12))
                    .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Theme.separator))
                    .overlay(alignment: .bottom) {
                        Rectangle().fill(Theme.separator).frame(height: 1).padding(.horizontal, 24).padding(.bottom, 56)
                    }
                    .accessibilityIdentifier("signatureCanvas")
                Button("Löschen", role: .destructive) {
                    canvas.drawing = PKDrawing()
                    isEmpty = true
                }
                .disabled(isEmpty)
                Spacer()
            }
            .padding()
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { onDone(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Übernehmen") { onDone(render()) }.disabled(isEmpty)
                        .accessibilityIdentifier("applySignatureButton")
                }
            }
        }
    }

    /// Schwarze Unterschrift auf Weiß, unabhängig vom Dunkelmodus.
    private func render() -> Data? {
        let bounds = canvas.bounds.isEmpty ? CGRect(x: 0, y: 0, width: 600, height: 260) : canvas.bounds
        let traits = UITraitCollection(userInterfaceStyle: .light)
        var image = UIImage()
        traits.performAsCurrent {
            image = canvas.drawing.image(from: bounds, scale: 2)
        }
        let renderer = UIGraphicsImageRenderer(size: bounds.size)
        return renderer.pngData { context in
            UIColor.white.setFill()
            context.fill(CGRect(origin: .zero, size: bounds.size))
            image.draw(in: CGRect(origin: .zero, size: bounds.size))
        }
    }
}

private struct SignatureCanvas: UIViewRepresentable {
    let canvas: PKCanvasView
    let onChange: (Bool) -> Void

    func makeUIView(context: Context) -> PKCanvasView {
        canvas.drawingPolicy = .anyInput
        canvas.tool = PKInkingTool(.pen, color: .black, width: 3)
        canvas.backgroundColor = .clear
        canvas.isOpaque = false
        canvas.overrideUserInterfaceStyle = .light
        canvas.delegate = context.coordinator
        return canvas
    }

    func updateUIView(_ uiView: PKCanvasView, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(onChange: onChange) }

    /// PencilKit-Delegate (wie alle UIScrollView-Delegates) läuft auf dem Hauptthread.
    @MainActor
    final class Coordinator: NSObject, PKCanvasViewDelegate {
        private let onChange: (Bool) -> Void

        init(onChange: @escaping (Bool) -> Void) {
            self.onChange = onChange
        }

        func canvasViewDrawingDidChange(_ canvasView: PKCanvasView) {
            onChange(canvasView.drawing.bounds.isEmpty)
        }
    }
}
