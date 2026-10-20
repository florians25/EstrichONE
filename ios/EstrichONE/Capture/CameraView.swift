@preconcurrency import AVFoundation
import EstrichCore
import PhotosUI
import SwiftUI

/// Serienaufnahme für eine Bauakte (09, W-2): Auslöser, Zähler, „Fertig (n)“. Oben die Zuordnung (antippen, um die
/// Bauakte zu wechseln) und eine Beschriftung, die für alle folgenden Fotos gilt (z. B. „Bad OG“).
/// Glas nur auf den Bedienelementen über dem Kamerabild (07, Abschnitt 1).
struct CameraView: View {
    let onFinish: (Int) -> Void
    /// Aus der Erstbegehung heraus bleibt die Zuordnung fest.
    let allowsChangingBauakte: Bool

    @Environment(AppModel.self) private var model
    @State private var bauakte: Bauakte
    @State private var caption = ""
    @State private var captionDraft = ""
    @State private var editingCaption = false
    @State private var choosingBauakte = false
    @State private var lastThumbnail: UIImage?

    @State private var camera = CameraSession()
    @State private var count = 0
    @State private var cameraState: CameraState = .starting
    @State private var errorMessage: String?
    @State private var pickerItems: [PhotosPickerItem] = []
    @State private var flash = false

    init(bauakte: Bauakte, allowsChangingBauakte: Bool = true, onFinish: @escaping (Int) -> Void) {
        _bauakte = State(initialValue: bauakte)
        self.allowsChangingBauakte = allowsChangingBauakte
        self.onFinish = onFinish
    }

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
        VStack(spacing: 0) {
            HStack(alignment: .top, spacing: 10) {
                VStack(alignment: .leading, spacing: 8) {
                    Button {
                        choosingBauakte = true
                    } label: {
                        HStack(spacing: 8) {
                            VStack(alignment: .leading, spacing: 0) {
                                Text(bauakte.number ?? "Neue Bauakte").font(.caption2.weight(.semibold)).opacity(0.85)
                                Text(bauakte.title).font(.subheadline.weight(.semibold)).lineLimit(1)
                            }
                            if allowsChangingBauakte {
                                Image(systemName: "chevron.down").font(.caption.weight(.bold))
                            }
                        }
                    }
                    .overlayControlStyle()
                    .disabled(!allowsChangingBauakte)
                    .tint(.white)
                    .accessibilityLabel("Bauakte: \(bauakte.title). Antippen zum Wechseln")
                    .accessibilityIdentifier("cameraTargetButton")

                    Button {
                        captionDraft = caption
                        editingCaption = true
                    } label: {
                        Label(caption.isEmpty ? "Beschriftung" : caption, systemImage: caption.isEmpty ? "text.badge.plus" : "tag.fill")
                            .lineLimit(1)
                    }
                    .overlayControlStyle()
                    .tint(.white)
                    .accessibilityIdentifier("captionButton")
                }
                Spacer()
                Button {
                    finish()
                } label: {
                    Text(count == 0 ? "Schließen" : "Fertig (\(count))")
                }
                .estrichButton(count == 0 ? .secondary : .accent)
                .accessibilityIdentifier("cameraDoneButton")
            }
            .padding(.horizontal)
            .padding(.top, 8)

            Spacer()

            if let errorMessage {
                StatusMessage(kind: .error, text: errorMessage).padding(.horizontal)
            }

            HStack(alignment: .center) {
                PhotosPicker(selection: $pickerItems, maxSelectionCount: 20, matching: .images) {
                    Image(systemName: "photo.on.rectangle")
                        .font(.title3)
                        .frame(width: 52, height: 52)
                }
                .overlayControlStyle()
                .tint(.white)
                .accessibilityLabel("Aus Fotos auswählen")

                Spacer()

                Button {
                    shutter()
                } label: {
                    Circle()
                        .strokeBorder(.white, lineWidth: 4)
                        .background(Circle().fill(.white.opacity(cameraState == .running ? 0.92 : 0.3)).padding(7))
                        .frame(width: 76, height: 76)
                }
                .buttonStyle(ShutterButtonStyle())
                .disabled(cameraState != .running)
                .accessibilityLabel("Foto aufnehmen")
                .accessibilityIdentifier("shutterButton")

                Spacer()

                ZStack(alignment: .topTrailing) {
                    Group {
                        if let lastThumbnail {
                            Image(uiImage: lastThumbnail).resizable().scaledToFill()
                        } else {
                            Color.black.opacity(0.5)
                        }
                    }
                    .frame(width: 52, height: 52)
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(.white.opacity(0.8), lineWidth: 1.5))
                    Text("\(count)")
                        .font(.caption.monospacedDigit().weight(.bold))
                        .foregroundStyle(Theme.onAccent)
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(Theme.accent, in: Capsule())
                        .offset(x: 6, y: -6)
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("\(count) Fotos aufgenommen")
                .accessibilityIdentifier("photoCounter")
            }
            .padding(.horizontal, 28)
            .padding(.bottom, 20)

            #if DEBUG
            if UITestSupport.isActive {
                Button("Testfoto aufnehmen") { Task { await save(UITestSupport.samplePhoto()) } }
                    .estrichButton(.secondary)
                    .accessibilityIdentifier("uitestCaptureButton")
                    .padding(.bottom, 12)
            }
            #endif
        }
        .alert("Beschriftung", isPresented: $editingCaption) {
            TextField("z. B. Bad OG, Randdämmstreifen", text: $captionDraft)
                .accessibilityIdentifier("captionField")
            Button("Übernehmen") { caption = captionDraft.trimmingCharacters(in: .whitespaces) }
                .accessibilityIdentifier("applyCaptionButton")
            if !caption.isEmpty {
                Button("Entfernen", role: .destructive) { caption = "" }
            }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Gilt für alle folgenden Fotos dieser Serie.")
        }
        .sheet(isPresented: $choosingBauakte) {
            BauaktePickerView(selected: bauakte.id) { chosen in
                bauakte = chosen
                choosingBauakte = false
            }
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
            try await service.addPhoto(data, to: bauakte.id, capturedAt: .now, caption: caption.isEmpty ? nil : caption)
            count += 1
            lastThumbnail = await Task.detached { UIImage(data: data)?.preparingThumbnail(of: CGSize(width: 120, height: 120)) }.value
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

/// Auslöser: sichtbares Eindrücken und kräftige Haptik.
private struct ShutterButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.9 : 1)
            .animation(.spring(response: 0.2, dampingFraction: 0.6), value: configuration.isPressed)
            .sensoryFeedback(.impact(weight: .medium), trigger: configuration.isPressed) { _, pressed in pressed }
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
