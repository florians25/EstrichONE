@preconcurrency import AVFoundation
import AVKit
import EstrichCore
import CoreLocation
import PhotosUI
import SwiftUI

/// Serienaufnahme für eine Bauakte (09, W-2): Die Kamera bleibt offen, Auslöser, Zähler, „Fertig (n)“. Oben die Zuordnung
/// (antippen, um die Bauakte zu wechseln), der Raum oder Bereich und eine Beschriftung; über dem Auslöser die Art der
/// Fotos (Vorher, Einbau, Nachher, Mangel). Alles gilt für die folgenden Fotos der Serie, bis es geändert wird, und steht
/// als Beschriftung am Foto. Aufnahmezeit und, wenn erlaubt, der Standort werden mitgespeichert; der Bildschirm bleibt an. Ab iOS 18 lösen auch
/// die Lautstärketasten bzw. die Kamerasteuerung aus – mit Handschuhen sicherer als der Bildschirm. Ohne Netz zeigt ein
/// Hinweis, dass die Fotos sicher auf dem Gerät bleiben.
/// Glas nur auf den Bedienelementen über dem Kamerabild (07, Abschnitt 1).
struct CameraView: View {
    let onFinish: (Int) -> Void
    /// Aus der Erstbegehung heraus bleibt die Zuordnung fest.
    let allowsChangingBauakte: Bool

    @Environment(AppModel.self) private var model
    @State private var bauakte: Bauakte
    /// Art der Fotos und Raum bzw. Bereich für die laufende Serie.
    @State private var phase: PhotoPhase?
    @State private var room = ""
    @State private var roomDraft = ""
    @State private var editingRoom = false
    @State private var roomSuggestions: [String] = []
    /// Standort als Aufnahmeort speichern (abschaltbar, die Wahl bleibt erhalten).
    @SwiftUI.AppStorage("photoLocationEnabled") private var storesLocation = true
    @State private var locator = LocationProvider()
    @State private var location: CLLocation?
    @Environment(\.shotScene) private var shotScene
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
    /// Ausgelöste, aber noch nicht gespeicherte Fotos: „Fertig“ wartet auf sie.
    @State private var pending = PendingShots()

    init(bauakte: Bauakte, allowsChangingBauakte: Bool = true, caption: String = "", onFinish: @escaping (Int) -> Void) {
        _bauakte = State(initialValue: bauakte)
        _caption = State(initialValue: caption)
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
        .task(id: "\(bauakte.id)|\(storesLocation)") {
            await loadSeries()
            await trackLocation()
        }
        .onChange(of: series) { _, new in rememberSeries(new) }
        .keepsScreenAwake()
        .onDisappear { camera.stop() }
        .onChange(of: pending) { closeIfSettled() }
        .onChange(of: pickerItems) { _, items in
            Task { await importPicked(items) }
        }
        .sensoryFeedback(.success, trigger: count)
        .modifier(HardwareShutter(isEnabled: cameraState == .running && !pending.isClosing) { shutter() })
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

                    // Aus einem Protokoll heraus (Erstbegehung, Abnahme) gibt das Formular die Beschriftung vor.
                    if allowsChangingBauakte {
                    Menu {
                        ForEach(roomSuggestions, id: \.self) { name in
                            Button(name) { room = name }
                        }
                        Button("Anderer Bereich …", systemImage: "pencil") {
                            roomDraft = room
                            editingRoom = true
                        }
                        if !room.isEmpty {
                            Button("Ohne Raum", systemImage: "xmark", role: .destructive) { room = "" }
                        }
                    } label: {
                        Label(room.isEmpty ? "Raum oder Bereich" : room, systemImage: room.isEmpty ? "square.dashed" : "square.split.bottomrightquarter.fill")
                            .lineLimit(1)
                            .font(.subheadline.weight(.semibold))
                            .labelStyle(TightLabelStyle())
                            .foregroundStyle(.white)
                            .padding(.horizontal, 14)
                            .frame(minHeight: Size.minTap)
                            .background(.black.opacity(0.5), in: Capsule())
                            .overlay(Capsule().strokeBorder(.white.opacity(0.2)))
                            .contentShape(Capsule())
                    }
                    .accessibilityIdentifier("roomButton")
                    }

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

                    if model.active?.sync?.isOffline == true {
                        Label("Offline. Fotos bleiben auf dem Gerät.", systemImage: "wifi.slash")
                            .font(.footnote.weight(.semibold))
                            .foregroundStyle(.white)
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .background(.black.opacity(0.6), in: Capsule())
                            .accessibilityIdentifier("cameraOfflineHint")
                    }
                }
                Spacer()
                VStack(alignment: .trailing, spacing: 8) {
                Button {
                    finish()
                } label: {
                    if pending.isClosing {
                        HStack(spacing: 6) {
                            ProgressView().tint(Theme.onAccent)
                            Text("Speichert …")
                        }
                    } else {
                        Text(count == 0 && pending.count == 0 ? "Schließen" : "Fertig (\(count + pending.count))")
                    }
                }
                .estrichButton(count == 0 && pending.count == 0 ? .secondary : .accent)
                .disabled(pending.isClosing)
                .accessibilityIdentifier("cameraDoneButton")

                Button { storesLocation.toggle() } label: {
                    Image(systemName: storesLocation ? (location == nil ? "location" : "location.fill") : "location.slash")
                        .frame(width: 24, height: 24)
                }
                .overlayControlStyle()
                .tint(.white)
                .accessibilityLabel(storesLocation ? "Standort wird als Aufnahmeort gespeichert. Antippen zum Ausschalten"
                                                   : "Standort wird nicht gespeichert. Antippen zum Einschalten")
                .accessibilityIdentifier("cameraLocationButton")
                }
            }
            .padding(.horizontal)
            .padding(.top, 8)

            Spacer()

            if let errorMessage {
                StatusMessage(kind: .error, text: errorMessage).padding(.horizontal)
            }

            // Art der Fotos: mit dem Daumen erreichbar, eine Auswahl gilt für die ganze Serie.
            if allowsChangingBauakte {
            HStack(spacing: 8) {
                ForEach(PhotoPhase.allCases) { option in
                    Button {
                        phase = phase == option ? nil : option
                    } label: {
                        Text(option.title)
                            .font(.subheadline.weight(.semibold))
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)
                            .foregroundStyle(phase == option ? Color.black : Color.white)
                            .frame(maxWidth: .infinity, minHeight: Size.minTap)
                            .background(phase == option ? Color.white : Color.black.opacity(0.55), in: Capsule())
                            .overlay(Capsule().strokeBorder(.white.opacity(phase == option ? 0 : 0.35)))
                            .contentShape(Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(phase == option ? .isSelected : [])
                    .accessibilityIdentifier("photoPhase-\(option.rawValue)")
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 14)
            .sensoryFeedback(.selection, trigger: phase)
            .accessibilityElement(children: .contain)
            .accessibilityLabel("Art der Fotos")
            }

            HStack(alignment: .center) {
                PhotosPicker(selection: $pickerItems, maxSelectionCount: 20, matching: .images) {
                    Image(systemName: "photo.on.rectangle")
                        .font(.title3)
                        .frame(width: 48, height: 48)
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
                        .frame(width: 70, height: 70)
                }
                .buttonStyle(ShutterButtonStyle())
                .disabled(cameraState != .running || pending.isClosing)
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
                    .frame(width: 48, height: 48)
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
        .alert("Raum oder Bereich", isPresented: $editingRoom) {
            TextField("z. B. Bad OG, Flur, Garage", text: $roomDraft)
                .accessibilityIdentifier("roomField")
            Button("Übernehmen") { room = roomDraft.trimmingCharacters(in: .whitespaces) }
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

    /// Vorgaben der Serie; ihre Beschriftung steht an jedem Foto („Einbau, Bad OG, Randdämmstreifen“).
    private var series: PhotoSeries { PhotoSeries(phase: phase, room: room, note: caption) }

    /// Räume der Bauakte als Vorschläge; Art und Raum der letzten Serie dieser Bauakte (bis zu zwölf Stunden) übernehmen.
    private func loadSeries() async {
        guard let service = model.active?.service, let store = model.active?.sync?.store else { return }
        let rooms = (try? await service.rooms(of: bauakte.id)) ?? []
        roomSuggestions = PhotoSeries.roomSuggestions(rooms.map { (name: $0.name, floor: $0.floor) })
        #if DEBUG
        if shotScene == "kamera-serie" {
            phase = .installation
            room = roomSuggestions.first ?? "Bad OG"
            return
        }
        #endif
        guard allowsChangingBauakte, shotScene == nil, phase == nil, room.isEmpty,
              let stored = await DraftStore(store: store).load(PhotoSeries.self, kind: DraftKind.photoSeries, scope: bauakte.id, maxAge: 12 * 3600)
        else { return }
        phase = stored.value.phase
        room = stored.value.room
    }

    private func rememberSeries(_ value: PhotoSeries) {
        guard allowsChangingBauakte, shotScene == nil, let store = model.active?.sync?.store else { return }
        let id = bauakte.id
        // Die Beschriftung gehört zum Aufrufer (z. B. Prüfpunkt der Erstbegehung) und wird nicht gemerkt.
        Task { await DraftStore(store: store).save(PhotoSeries(phase: value.phase, room: value.room), kind: DraftKind.photoSeries, scope: id) }
    }

    /// Standort für die Fotos: beim Öffnen bestimmen und alle zwei Minuten auffrischen, solange die Kamera offen ist.
    /// Nur wenn eingeschaltet; die Erlaubnis erfragt das System beim ersten Mal. Ohne Erlaubnis bleiben die Fotos ohne Ort.
    private func trackLocation() async {
        guard storesLocation, shotScene == nil, !Self.isUITest else {
            location = nil
            return
        }
        while !Task.isCancelled {
            if let fresh = try? await locator.currentLocation() { location = fresh }
            try? await Task.sleep(for: .seconds(120))
        }
    }

    private static var isUITest: Bool {
        #if DEBUG
        UITestSupport.isActive
        #else
        false
        #endif
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
            if let data { await save(data) }
            pending.shotSettled()
        }
    }

    private func shutter() {
        withAnimation(.easeOut(duration: 0.12)) { flash = true }
        Task {
            try? await Task.sleep(for: .milliseconds(120))
            withAnimation { flash = false }
        }
        pending.shutterPressed()
        camera.capture()
    }

    /// Erst nach dem dauerhaften Speichern zählt das Foto (09, Abschnitt 5, Schritt 3).
    private func save(_ data: Data) async {
        guard let service = model.active?.service else { return }
        do {
            let prepared = await Task.detached(priority: .userInitiated) { PhotoCompressor.prepare(data) }.value
            let place = storesLocation ? location : nil
            try await service.addPhoto(prepared, to: bauakte.id, capturedAt: .now, latitude: place?.coordinate.latitude,
                                       longitude: place?.coordinate.longitude, caption: series.caption)
            count += 1
            lastThumbnail = await Task.detached { UIImage(data: data)?.preparingThumbnail(of: CGSize(width: 120, height: 120)) }.value
            errorMessage = nil
            model.active?.sync?.localChangeMade()
        } catch is ReadOnlyModeError {
            errorMessage = ReadOnlyModeError.message
        } catch {
            errorMessage = "Das Foto konnte nicht gespeichert werden. Bitte freien Speicher prüfen."
        }
    }

    private func importPicked(_ items: [PhotosPickerItem]) async {
        for item in items {
            // Aufnahmedaten (Datum, Ort) bleiben erhalten; HEIC wird zu JPEG, große Bilder auf 3000 px (PhotoCompressor).
            guard let data = try? await item.loadTransferable(type: Data.self), UIImage(data: data) != nil else { continue }
            await save(data)
        }
        pickerItems = []
    }

    /// Schließt erst, wenn alle ausgelösten Fotos gespeichert sind – sonst gingen die letzten Fotos einer Serie verloren
    /// (Aufnahme und Verarbeitung dauern nach dem Auslösen noch einen Moment). Sicherheitshalber nach spätestens 10 s.
    private func finish() {
        pending.requestClose()
        closeIfSettled()
        Task {
            try? await Task.sleep(for: .seconds(10))
            pending.forceClose()
        }
    }

    private func closeIfSettled() {
        guard pending.takeClose() else { return }
        camera.stop()
        onFinish(count)
    }
}

/// Ausgelöste Fotos, die noch nicht dauerhaft gespeichert sind, und der Wunsch zu schließen. Geschlossen wird genau
/// einmal, und erst, wenn nichts mehr unterwegs ist (oder nach Ablauf der Wartezeit).
struct PendingShots: Equatable {
    private(set) var count = 0
    private(set) var isClosing = false
    private var forced = false
    private var closed = false

    mutating func shutterPressed() {
        count += 1
    }

    mutating func shotSettled() {
        count = max(0, count - 1)
    }

    mutating func requestClose() {
        isClosing = true
    }

    mutating func forceClose() {
        if isClosing { forced = true }
    }

    /// `true` genau einmal: dann darf die Kamera schließen.
    mutating func takeClose() -> Bool {
        guard isClosing, !closed, count == 0 || forced else { return false }
        closed = true
        return true
    }
}

/// Lautstärketasten und Kamerasteuerung als Auslöser (ab iOS 18; davor nur der Bildschirm).
private struct HardwareShutter: ViewModifier {
    let isEnabled: Bool
    let action: @MainActor () -> Void

    func body(content: Content) -> some View {
        if #available(iOS 18, *) {
            content.onCameraCaptureEvent(isEnabled: isEnabled) { event in
                let ended = event.phase == .ended
                MainActor.assumeIsolated {
                    if ended { action() }
                }
            }
        } else {
            content
        }
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
