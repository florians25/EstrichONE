import AVFoundation
import CoreTransferable
import EstrichCore
import PhotosUI
import SwiftUI
import UIKit
import UniformTypeIdentifiers

/// Videos für eine Bauakte: aufnehmen (höchstens 2 Minuten) oder aus der Mediathek wählen, beschriften, auf
/// 720p mit etwa 2 Mbit/s (HEVC, sonst H.264) verkleinern und wie Fotos offline speichern; der Upload läuft über die Datei-Synchronisation.
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
                            Label("Auf die ersten 2 Minuten gekürzt.", systemImage: "scissors").foregroundStyle(Theme.warning)
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

/// Verkleinert Videos für die Übertragung: höchstens 720p, HEVC (Fallback H.264) mit etwa 2 Mbit/s, Ton AAC mono
/// 64 kbit/s, MP4, höchstens 2 Minuten – rechnerisch rund 15 MB je Minute (vorher 1080p mit 6–10 Mbit/s, 45–75 MB).
/// Gelingt das Umkodieren nicht, verkleinert der Systemexport auf 1280 × 720 (H.264).
enum VideoCompressor {
    static let maxDuration: Double = 120
    /// Längere und kürzere Kante des Ergebnisses höchstens (720p, Hoch- wie Querformat).
    static let maxLongEdge: CGFloat = 1280
    static let maxShortEdge: CGFloat = 720
    static let videoBitrate = 2_000_000
    static let audioBitrate = 64_000
    /// Rechnerische Größe je Minute: (Video + Ton) × 60 s / 8 ≈ 15,5 MB (ohne den geringen Containeranteil).
    static var bytesPerMinute: Int { (videoBitrate + audioBitrate) * 60 / 8 }

    struct Result: Sendable, Equatable {
        let url: URL
        let duration: Double
        let size: Int64
        let wasTrimmed: Bool
    }

    /// Zielgröße für die Kodierung (im gespeicherten, nicht gedrehten Bild): Seitenverhältnis bleibt, nie vergrößert,
    /// gerade Pixelzahlen (verlangen die Kodierer).
    static func targetSize(for natural: CGSize) -> CGSize {
        let width = abs(natural.width), height = abs(natural.height)
        guard width > 0, height > 0 else { return CGSize(width: maxLongEdge, height: maxShortEdge) }
        let long = max(width, height), short = min(width, height)
        let scale = min(1, maxLongEdge / long, maxShortEdge / short)
        func even(_ value: CGFloat) -> CGFloat { max(2, (value * scale / 2).rounded() * 2) }
        return CGSize(width: even(width), height: even(height))
    }

    static func compress(_ source: URL) async -> Result? {
        let asset = AVURLAsset(url: source)
        guard let duration = try? await asset.load(.duration).seconds, duration > 0 else { return nil }
        let trimmed = duration > maxDuration + 0.5
        let range = CMTimeRange(start: .zero, duration: CMTime(seconds: min(duration, maxDuration), preferredTimescale: 600))
        let output = FileManager.default.temporaryDirectory.appending(path: "Video-\(UUID().uuidString).mp4")
        var written = await transcode(asset, range: range, to: output)
        if !written {
            try? FileManager.default.removeItem(at: output)
            written = await export(asset, range: range, to: output)
        }
        let size = (try? FileManager.default.attributesOfItem(atPath: output.path())[.size] as? Int64) ?? 0
        guard written, size > 0 else {
            try? FileManager.default.removeItem(at: output)
            return nil
        }
        return Result(url: output, duration: min(duration, maxDuration), size: size, wasTrimmed: trimmed)
    }

    /// Umkodieren mit fester Ziel-Bitrate (AVAssetReader → AVAssetWriter).
    private static func transcode(_ asset: AVURLAsset, range: CMTimeRange, to output: URL) async -> Bool {
        guard let videoTrack = try? await asset.loadTracks(withMediaType: .video).first,
              let natural = try? await videoTrack.load(.naturalSize),
              let transform = try? await videoTrack.load(.preferredTransform),
              let reader = try? AVAssetReader(asset: asset),
              let writer = try? AVAssetWriter(outputURL: output, fileType: .mp4) else { return false }
        reader.timeRange = range
        writer.shouldOptimizeForNetworkUse = true

        let size = targetSize(for: natural)
        func videoSettings(_ codec: AVVideoCodecType) -> [String: Any] {
            var compression: [String: Any] = [AVVideoAverageBitRateKey: videoBitrate, AVVideoMaxKeyFrameIntervalKey: 60,
                                              AVVideoExpectedSourceFrameRateKey: 30]
            if codec == .h264 { compression[AVVideoProfileLevelKey] = AVVideoProfileLevelH264HighAutoLevel }
            return [AVVideoCodecKey: codec, AVVideoWidthKey: Int(size.width), AVVideoHeightKey: Int(size.height),
                    AVVideoScalingModeKey: AVVideoScalingModeResizeAspect, AVVideoCompressionPropertiesKey: compression]
        }
        let hevc = videoSettings(.hevc)
        let settings = writer.canApply(outputSettings: hevc, forMediaType: .video) ? hevc : videoSettings(.h264)
        guard writer.canApply(outputSettings: settings, forMediaType: .video) else { return false }
        let videoInput = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
        videoInput.transform = transform
        videoInput.expectsMediaDataInRealTime = false
        let videoOutput = AVAssetReaderTrackOutput(track: videoTrack, outputSettings: [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange,
        ])
        videoOutput.alwaysCopiesSampleData = false
        guard reader.canAdd(videoOutput), writer.canAdd(videoInput) else { return false }
        reader.add(videoOutput)
        writer.add(videoInput)
        var pumps = [SamplePump(input: videoInput, output: videoOutput, label: "video")]

        if let audioTrack = try? await asset.loadTracks(withMediaType: .audio).first {
            let audioOutput = AVAssetReaderAudioMixOutput(audioTracks: [audioTrack], audioSettings: [
                AVFormatIDKey: kAudioFormatLinearPCM, AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false,
                AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false, AVSampleRateKey: 44_100, AVNumberOfChannelsKey: 1,
            ])
            let audioSettings: [String: Any] = [AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: 44_100, AVNumberOfChannelsKey: 1,
                                                AVEncoderBitRateKey: audioBitrate]
            if reader.canAdd(audioOutput), writer.canApply(outputSettings: audioSettings, forMediaType: .audio) {
                let audioInput = AVAssetWriterInput(mediaType: .audio, outputSettings: audioSettings)
                audioInput.expectsMediaDataInRealTime = false
                if writer.canAdd(audioInput) {
                    reader.add(audioOutput)
                    writer.add(audioInput)
                    pumps.append(SamplePump(input: audioInput, output: audioOutput, label: "audio"))
                }
            }
        }

        guard writer.startWriting() else { return false }
        guard reader.startReading() else {
            writer.cancelWriting()
            return false
        }
        writer.startSession(atSourceTime: range.start)
        let complete = await withTaskGroup(of: Bool.self) { group in
            for pump in pumps { group.addTask { await pump.run() } }
            var all = true
            for await done in group { all = all && done }
            return all
        }
        guard complete, reader.status == .completed else {
            reader.cancelReading()
            writer.cancelWriting()
            return false
        }
        await writer.finishWriting()
        return writer.status == .completed
    }

    /// Rückfallebene: Systemexport auf 1280 × 720 (H.264).
    private static func export(_ asset: AVURLAsset, range: CMTimeRange, to output: URL) async -> Bool {
        guard let session = AVAssetExportSession(asset: asset, presetName: AVAssetExportPreset1280x720) else { return false }
        session.timeRange = range
        session.shouldOptimizeForNetworkUse = true
        if #available(iOS 18, *) {
            do {
                try await session.export(to: output, as: .mp4)
                return true
            } catch {
                return false
            }
        } else {
            session.outputURL = output
            session.outputFileType = .mp4
            await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
                session.exportAsynchronously { continuation.resume() }
            }
            return session.status == .completed
        }
    }
}

/// Überträgt die Proben einer Spur vom Leser zum Schreiber, sobald dieser bereit ist. Alle Zugriffe laufen auf der
/// eigenen seriellen Warteschlange (darum `@unchecked Sendable`).
private final class SamplePump: @unchecked Sendable {
    private let input: AVAssetWriterInput
    private let output: AVAssetReaderOutput
    private let queue: DispatchQueue
    private var finished = false

    init(input: AVAssetWriterInput, output: AVAssetReaderOutput, label: String) {
        self.input = input
        self.output = output
        queue = DispatchQueue(label: "de.estrichone.video.\(label)")
    }

    /// `true`, wenn alle Proben übernommen wurden.
    func run() async -> Bool {
        await withCheckedContinuation { (continuation: CheckedContinuation<Bool, Never>) in
            input.requestMediaDataWhenReady(on: queue) { [self] in
                guard !finished else { return }
                while input.isReadyForMoreMediaData {
                    guard let buffer = output.copyNextSampleBuffer() else {
                        finish(continuation, success: true)
                        return
                    }
                    if !input.append(buffer) {
                        finish(continuation, success: false)
                        return
                    }
                }
            }
        }
    }

    private func finish(_ continuation: CheckedContinuation<Bool, Never>, success: Bool) {
        finished = true
        input.markAsFinished()
        continuation.resume(returning: success)
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
        // Aufnahme in Kameraqualität (nur vorübergehend auf dem Gerät); gespeichert und übertragen wird die auf
        // 720p/2 Mbit/s umkodierte Fassung (VideoCompressor). .typeMedium wäre nur 480 × 360, die 720p-Stufe des
        // Pickers zeichnet nur Einzelbilder mit sehr hoher Datenrate auf.
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
