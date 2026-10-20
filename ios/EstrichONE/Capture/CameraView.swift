@preconcurrency import AVFoundation
import EstrichCore
import PhotosUI
import SwiftUI

/// Serienaufnahme für eine Bauakte (09, W-2): Auslöser, Zähler, „Fertig (n)“. Glas nur auf den Bedienelementen
/// über dem Kamerabild (07, Abschnitt 1).
struct CameraView: View {
    let bauakte: Bauakte
    let onFinish: (Int) -> Void

    @Environment(AppModel.self) private var model
    @State private var camera = CameraSession()
    @State private var count = 0
    @State private var cameraState: CameraState = .starting
    @State private var errorMessage: String?
    @State private var pickerItems: [PhotosPickerItem] = []
    @State private var flash = false

    private enum CameraState {
        case starting
        case running
        case unavailable
        case denied
    }

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()
            if cameraState == .running {
                CameraPreview(session: camera.session).ignoresSafeArea()
            } else if cameraState != .starting {
                VStack(spacing: 12) {
                    Image(systemName: "camera.fill").font(.system(size: 44)).foregroundStyle(.white.opacity(0.7))
                    Text(cameraState == .denied
                         ? "Die App darf die Kamera nicht verwenden. Bitte in den Einstellungen erlauben."
                         : "Auf diesem Gerät ist keine Kamera verfügbar.")
                        .foregroundStyle(.white)
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 32)
                }
            }
            if flash {
                Color.white.ignoresSafeArea().transition(.opacity)
            }
            controls
        }
        .task { await startCamera() }
        .onDisappear { camera.stop() }
        .onChange(of: pickerItems) { _, items in
            Task { await importPicked(items) }
        }
        .sensoryFeedback(.success, trigger: count)
        .statusBarHidden()
    }

    private var controls: some View {
        VStack {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(bauakte.number ?? "Neue Bauakte").font(.caption.weight(.semibold))
                    Text(bauakte.title).font(.headline).lineLimit(1)
                }
                .foregroundStyle(.white)
                .padding(10)
                .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 10))
                Spacer()
                Button {
                    finish()
                } label: {
                    Text(count == 0 ? "Schließen" : "Fertig (\(count))")
                        .font(.headline)
                        .frame(minHeight: 44)
                        .padding(.horizontal, 6)
                }
                .secondaryActionStyle()
                .accessibilityIdentifier("cameraDoneButton")
            }
            .padding()

            Spacer()

            if let errorMessage {
                StatusMessage(kind: .error, text: errorMessage).padding(.horizontal)
            }

            HStack(alignment: .center) {
                PhotosPicker(selection: $pickerItems, maxSelectionCount: 20, matching: .images) {
                    Image(systemName: "photo.on.rectangle")
                        .font(.title2)
                        .frame(width: 56, height: 56)
                }
                .secondaryActionStyle()
                .accessibilityLabel("Aus Fotos auswählen")

                Spacer()

                Button {
                    shutter()
                } label: {
                    Circle()
                        .strokeBorder(.white, lineWidth: 4)
                        .background(Circle().fill(.white.opacity(cameraState == .running ? 0.9 : 0.3)).padding(8))
                        .frame(width: 84, height: 84)
                }
                .disabled(cameraState != .running)
                .accessibilityLabel("Foto aufnehmen")
                .accessibilityIdentifier("shutterButton")

                Spacer()

                Text("\(count)")
                    .font(.title2.monospacedDigit().weight(.semibold))
                    .foregroundStyle(.white)
                    .frame(width: 56, height: 56)
                    .background(.black.opacity(0.55), in: Circle())
                    .accessibilityLabel("\(count) Fotos aufgenommen")
                    .accessibilityIdentifier("photoCounter")
            }
            .padding(.horizontal, 28)
            .padding(.bottom, 24)

            #if DEBUG
            if UITestSupport.isActive {
                Button("Testfoto aufnehmen") { Task { await save(UITestSupport.samplePhoto()) } }
                    .secondaryActionStyle()
                    .accessibilityIdentifier("uitestCaptureButton")
                    .padding(.bottom, 12)
            }
            #endif
        }
    }

    private func startCamera() async {
        guard CameraSession.isAvailable else {
            cameraState = .unavailable
            return
        }
        guard await CameraSession.requestAccess() else {
            cameraState = .denied
            return
        }
        camera.start()
        cameraState = .running
        for await data in camera.photos {
            await save(data)
        }
    }

    private func shutter() {
        withAnimation(.easeOut(duration: 0.12)) { flash = true }
        Task {
            try? await Task.sleep(for: .milliseconds(120))
            withAnimation { flash = false }
        }
        camera.capture()
    }

    /// Erst nach dem dauerhaften Speichern zählt das Foto (09, Abschnitt 5, Schritt 3).
    private func save(_ data: Data) async {
        guard let service = model.active?.service else { return }
        do {
            try await service.addPhoto(data, to: bauakte.id, capturedAt: .now)
            count += 1
            errorMessage = nil
            model.active?.sync?.localChangeMade()
        } catch {
            errorMessage = "Das Foto konnte nicht gespeichert werden. Bitte freien Speicher prüfen."
        }
    }

    private func importPicked(_ items: [PhotosPickerItem]) async {
        for item in items {
            guard let data = try? await item.loadTransferable(type: Data.self), let image = UIImage(data: data),
                  let jpeg = image.jpegData(compressionQuality: 0.85) else { continue }
            await save(jpeg)
        }
        pickerItems = []
    }

    private func finish() {
        camera.stop()
        onFinish(count)
    }
}

/// Kamerabild über `AVCaptureVideoPreviewLayer`.
private struct CameraPreview: UIViewRepresentable {
    let session: AVCaptureSession

    func makeUIView(context: Context) -> PreviewView {
        let view = PreviewView()
        view.previewLayer.session = session
        view.previewLayer.videoGravity = .resizeAspectFill
        return view
    }

    func updateUIView(_ uiView: PreviewView, context: Context) {}

    final class PreviewView: UIView {
        override class var layerClass: AnyClass { AVCaptureVideoPreviewLayer.self }
        var previewLayer: AVCaptureVideoPreviewLayer {
            layer as! AVCaptureVideoPreviewLayer
        }
    }
}
