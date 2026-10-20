@preconcurrency import AVFoundation
import UIKit

/// Serienaufnahme mit AVCaptureSession (07, Abschnitt 3): ohne Rückfrage nach jedem Foto, JPEG mit EXIF
/// (docs/sync-protokoll.md, Abschnitt 6: Qualität 0,85).
final class CameraSession: NSObject, @unchecked Sendable, AVCapturePhotoCaptureDelegate {
    let session = AVCaptureSession()
    private let output = AVCapturePhotoOutput()
    private let queue = DispatchQueue(label: "de.estrichone.camera")
    private var configured = false
    /// Aufgenommene Fotos (JPEG) in Aufnahmereihenfolge.
    let photos: AsyncStream<Data>
    private let continuation: AsyncStream<Data>.Continuation

    override init() {
        (photos, continuation) = AsyncStream.makeStream(of: Data.self)
        super.init()
    }

    static var isAvailable: Bool { AVCaptureDevice.default(for: .video) != nil }

    static func requestAccess() async -> Bool {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized: true
        case .notDetermined: await AVCaptureDevice.requestAccess(for: .video)
        default: false
        }
    }

    func start() {
        queue.async { [self] in
            if !configured {
                configure()
            }
            if !session.isRunning { session.startRunning() }
        }
    }

    func stop() {
        queue.async { [self] in
            if session.isRunning { session.stopRunning() }
        }
    }

    func capture() {
        queue.async { [self] in
            guard session.isRunning else { return }
            let settings = AVCapturePhotoSettings(format: [
                AVVideoCodecKey: AVVideoCodecType.jpeg,
                AVVideoCompressionPropertiesKey: [AVVideoQualityKey: 0.85],
            ])
            settings.flashMode = output.supportedFlashModes.contains(.auto) ? .auto : .off
            output.capturePhoto(with: settings, delegate: self)
        }
    }

    private func configure() {
        session.beginConfiguration()
        session.sessionPreset = .photo
        if let device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back),
           let input = try? AVCaptureDeviceInput(device: device), session.canAddInput(input) {
            session.addInput(input)
        }
        if session.canAddOutput(output) {
            session.addOutput(output)
            output.maxPhotoQualityPrioritization = .balanced
        }
        session.commitConfiguration()
        configured = true
    }

    func photoOutput(_ output: AVCapturePhotoOutput, didFinishProcessingPhoto photo: AVCapturePhoto, error: Error?) {
        guard error == nil, let data = photo.fileDataRepresentation() else { return }
        continuation.yield(data)
    }
}
