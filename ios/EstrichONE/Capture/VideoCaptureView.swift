import AVFoundation
import CoreTransferable
import EstrichCore
import PhotosUI
import SwiftUI
import UIKit
import UniformTypeIdentifiers

/// Videos für eine Bauakte: aufnehmen (höchstens 2 Minuten) oder aus der Mediathek wählen, beschriften, auf
/// 1080p (HEVC, sonst H.264) verkleinern und wie Fotos offline speichern; der Upload läuft über die Datei-Synchronisation.
struct VideoCaptureView: View {
    let bauakte: Bauakte
    /// `true`, wenn ein Video gespeichert wurde.
    let onFinish: (Bool) -> Void

    @Environment(AppModel.self) private var model
    @State private var recording = false
    @State private var pickerItem: PhotosPickerItem?
    @State private var source: URL?
    @State private var prepared: VideoCompressor.Result?
    @State private var preparing = false
    @State private var caption = ""
    @State private var saving = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    if VideoRecorderView.isAvailable {
                        Button { recording = true } label: {
                            Label("Video aufnehmen (bis 2 Minuten)", systemImage: "video.fill")
                        }
                        .accessibilityIdentifier("recordVideoButton")
                    }
                    PhotosPicker(selection: $pickerItem, matching: .videos) {
                        Label("Video aus der Mediathek wählen", systemImage: "photo.on.rectangle")
                    }
                    .accessibilityIdentifier("pickVideoButton")
                } header: {
                    Text("Video für \(bauakte.number.map { "\($0) · " } ?? "")\(bauakte.title)")
                } footer: {
                    if !VideoRecorderView.isAvailable {
                        Text("Auf diesem Gerät ist keine Kamera verfügbar – Videos können aus der Mediathek übernommen werden.")
                    }
                }

                if preparing {
                    Section {
                        HStack(spacing: 10) {
                            ProgressView()
                            Text("Video wird für die Übertragung verkleinert …").foregroundStyle(Theme.textSecondary)
                        }
                    }
                }

                if let prepared {
                    Section {
                        LabeledContent("Länge", value: Duration.seconds(prepared.duration).formatted(.time(pattern: .minuteSecond)))
                        LabeledContent("Größe", value: ByteCountFormatter.string(fromByteCount: prepared.size, countStyle: .file))
                        if prepared.wasTrimmed {
                            Label("Auf die ersten 2 Minuten gekürzt.", systemImage: "scissors").foregroundStyle(Theme.hint)
                        }
                        TextField("Beschriftung, z. B. Estrich wird eingebracht", text: $caption)
                            .accessibilityIdentifier("videoCaptionField")
                    } header: {
                        Text("Bereit zum Speichern")
                    }
                }

                if let errorMessage {
                    Section { StatusMessage(kind: .error, text: errorMessage) }
                }
            }
            .navigationTitle("Video")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { finish(saved: false) }.disabled(saving)
                }
            }
            .safeAreaInset(edge: .bottom) {
                Button {
                    Task { await save() }
                } label: {
                    ActionLabel(saving ? "Wird gespeichert …" : "Video speichern", systemImage: "square.and.arrow.down", isBusy: saving)
                }
                .primaryActionStyle()
                .disabled(prepared == nil || saving || preparing)
                .accessibilityIdentifier("saveVideoButton")
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(.bar)
            }
            .fullScreenCover(isPresented: $recording) {
                VideoRecorderView { url in
                    recording = false
                    if let url { Task { await prepare(url) } }
                }
                .ignoresSafeArea()
            }
            .onChange(of: pickerItem) { _, item in
                guard let item else { return }
                Task {
                    defer { pickerItem = nil }
                    do {
                        if let movie = try await item.loadTransferable(type: PickedMovie.self) {
                            await prepare(movie.url)
                        }
                    } catch {
                        errorMessage = "Das Video konnte nicht aus der Mediathek geladen werden."
                    }
                }
            }
        }
        .interactiveDismissDisabled(saving || prepared != nil)
    }

    private func prepare(_ url: URL) async {
        errorMessage = nil
        cleanUp()
        source = url
        preparing = true
        defer { preparing = false }
        if let result = await VideoCompressor.compress(url) {
            prepared = result
        } else {
            errorMessage = "Das Video konnte nicht verarbeitet werden."
        }
    }

    private func save() async {
        guard let prepared, let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        do {
            let data = try Data(contentsOf: prepared.url)
            try await service.addVideo(data, to: bauakte.id, capturedAt: .now, contentType: "video/mp4",
                                       caption: caption.isEmpty ? nil : caption)
            model.active?.sync?.localChangeMade()
            finish(saved: true)
        } catch {
            errorMessage = "Das Video konnte nicht gespeichert werden. Bitte freien Speicher prüfen."
        }
    }

    private func finish(saved: Bool) {
        cleanUp()
        onFinish(saved)
    }

    /// Temporäre Dateien (Aufnahme, Kopie aus der Mediathek, verkleinerte Fassung) entfernen.
    private func cleanUp() {
        for url in [source, prepared?.url].compactMap({ $0 }) {
            try? FileManager.default.removeItem(at: url)
        }
        source = nil
        prepared = nil
    }
}

/// Video aus der Mediathek als Datei (Kopie im temporären Ordner).
struct PickedMovie: Transferable {
    let url: URL

    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(contentType: .movie) { movie in
            SentTransferredFile(movie.url)
        } importing: { received in
            let copy = FileManager.default.temporaryDirectory.appending(path: "Video-\(UUID().uuidString).\(received.file.pathExtension)")
            try FileManager.default.copyItem(at: received.file, to: copy)
            return PickedMovie(url: copy)
        }
    }
}

/// Verkleinert Videos für die Übertragung: höchstens 1080p, HEVC (Fallback H.264), MP4, höchstens 2 Minuten.
enum VideoCompressor {
    static let maxDuration: Double = 120

    struct Result: Sendable, Equatable {
        let url: URL
        let duration: Double
        let size: Int64
        let wasTrimmed: Bool
    }

    static func compress(_ source: URL) async -> Result? {
        let asset = AVURLAsset(url: source)
        guard let duration = try? await asset.load(.duration).seconds, duration > 0 else { return nil }
        guard let session = AVAssetExportSession(asset: asset, presetName: AVAssetExportPresetHEVC1920x1080)
            ?? AVAssetExportSession(asset: asset, presetName: AVAssetExportPreset1920x1080) else { return nil }
        let output = FileManager.default.temporaryDirectory.appending(path: "Video-\(UUID().uuidString).mp4")
        let trimmed = duration > maxDuration + 0.5
        if trimmed {
            session.timeRange = CMTimeRange(start: .zero, duration: CMTime(seconds: maxDuration, preferredTimescale: 600))
        }
        session.shouldOptimizeForNetworkUse = true
        if #available(iOS 18, *) {
            do {
                try await session.export(to: output, as: .mp4)
            } catch {
                return nil
            }
        } else {
            session.outputURL = output
            session.outputFileType = .mp4
            await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
                session.exportAsynchronously { continuation.resume() }
            }
            guard session.status == .completed else { return nil }
        }
        let size = (try? FileManager.default.attributesOfItem(atPath: output.path())[.size] as? Int64) ?? 0
        guard size > 0 else { return nil }
        return Result(url: output, duration: min(duration, maxDuration), size: size, wasTrimmed: trimmed)
    }
}

/// Videoaufnahme der System-Kamera (höchstens 2 Minuten). Liefert die Datei oder `nil` bei Abbruch.
struct VideoRecorderView: UIViewControllerRepresentable {
    let onFinish: @MainActor (URL?) -> Void

    static var isAvailable: Bool {
        UIImagePickerController.isSourceTypeAvailable(.camera)
            && (UIImagePickerController.availableMediaTypes(for: .camera) ?? []).contains(UTType.movie.identifier)
    }

    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController()
        picker.sourceType = .camera
        picker.mediaTypes = [UTType.movie.identifier]
        picker.cameraCaptureMode = .video
        picker.videoMaximumDuration = VideoCompressor.maxDuration
        picker.videoQuality = .typeHigh
        picker.delegate = context.coordinator
        return picker
    }

    func updateUIViewController(_ picker: UIImagePickerController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(onFinish: onFinish) }

    /// Die Kamera ruft auf dem Hauptthread zurück; die Aufnahme wird vor der Rückgabe in den eigenen temporären Ordner
    /// verschoben, weil das System seine Datei danach löscht.
    final class Coordinator: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate, @unchecked Sendable {
        private let onFinish: @MainActor (URL?) -> Void

        init(onFinish: @escaping @MainActor (URL?) -> Void) {
            self.onFinish = onFinish
        }

        nonisolated func imagePickerController(_ picker: UIImagePickerController,
                                               didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
            var result: URL?
            if let recorded = info[.mediaURL] as? URL {
                let copy = FileManager.default.temporaryDirectory.appending(path: "Aufnahme-\(UUID().uuidString).\(recorded.pathExtension)")
                if (try? FileManager.default.moveItem(at: recorded, to: copy)) != nil { result = copy }
            }
            let url = result
            MainActor.assumeIsolated { self.onFinish(url) }
        }

        nonisolated func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
            MainActor.assumeIsolated { self.onFinish(nil) }
        }
    }
}
