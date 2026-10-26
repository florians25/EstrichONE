import EstrichCore
import SwiftUI

/// Raster der Fotos; jedes zeigt Beschriftung und Übertragungszustand.
struct PhotoGrid: View {
    let photos: [FileItem]
    var minimum: CGFloat = 96
    let onSelect: (FileItem) -> Void

    var body: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: minimum), spacing: 4)], spacing: 4) {
            ForEach(Array(photos.enumerated()), id: \.element.id) { index, photo in
                Button {
                    onSelect(photo)
                } label: {
                    PhotoThumbnail(file: photo)
                }
                .buttonStyle(.plain)
                .accessibilityIdentifier("photoThumb-\(index)")
                .accessibilityLabel([photo.caption, photo.capturedAt.map { "Foto vom \($0.formatted(date: .abbreviated, time: .shortened))" } ?? "Foto"]
                    .compactMap { $0 }.joined(separator: ", "))
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("photoGrid")
    }
}

struct PhotoThumbnail: View {
    let file: FileItem
    var radius: CGFloat = Radius.s
    var showsCaption = true
    @Environment(AppModel.self) private var model
    @State private var image: UIImage?

    var body: some View {
        Theme.fill
            .aspectRatio(1, contentMode: .fit)
            .overlay {
                if let image {
                    Image(uiImage: image).resizable().scaledToFill()
                } else {
                    Image(systemName: file.isVideo ? "video" : "photo").foregroundStyle(Theme.textTertiary)
                }
            }
            .overlay(alignment: .bottomLeading) {
                if file.isVideo {
                    Image(systemName: "play.circle.fill")
                        .font(.title)
                        .foregroundStyle(.white, .black.opacity(0.45))
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .accessibilityLabel("Video")
                }
            }
            .overlay(alignment: .bottomLeading) {
                if showsCaption, let caption = file.caption {
                    Text(caption)
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.white)
                        .lineLimit(1)
                        .padding(.horizontal, 5)
                        .padding(.vertical, 3)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(LinearGradient(colors: [.clear, .black.opacity(0.65)], startPoint: .top, endPoint: .bottom))
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
            .overlay(alignment: .topTrailing) {
                if file.syncState != .synced {
                    Image(systemName: file.syncState == .problem ? "exclamationmark.triangle.fill" : "arrow.up.circle.fill")
                        .foregroundStyle(.white, file.syncState == .problem ? Theme.danger : Theme.accentFill)
                        .font(.subheadline)
                        .padding(4)
                        .accessibilityLabel(file.syncState == .problem ? "Problem bei der Übertragung" : "Wird übertragen")
                }
            }
            .task(id: "\(file.id)|\(file.isStoredOnServer)|\(file.syncState)") {
                // Videos: keine Vorschaubild-Variante – nicht das ganze Video laden.
                guard !file.isVideo else { return }
                image = await model.active?.thumbnails?.thumbnail(for: file, size: 120)
            }
    }
}

/// Großansicht mit Blättern, Beschriftung, Teilen und Verwerfen (nur noch nicht übertragene eigene Aufnahmen).
struct PhotoViewer: View {
    let photos: [FileItem]
    let onChange: () -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var selection: UUID
    @State private var captions: [UUID: String] = [:]
    @State private var editingCaption = false
    @State private var captionDraft = ""
    @State private var confirmDiscard = false
    /// Recht `files.delete`: auch übertragene Fotos löschen.
    @State private var canDeleteTransferred = false
    @State private var images: [UUID: UIImage] = [:]

    init(photos: [FileItem], selection: UUID, onChange: @escaping () -> Void) {
        self.photos = photos
        self.onChange = onChange
        _selection = State(initialValue: selection)
    }

    private var current: FileItem? { photos.first { $0.id == selection } }

    var body: some View {
        NavigationStack {
            TabView(selection: $selection) {
                ForEach(photos) { photo in
                    PhotoPage(file: photo) { images[photo.id] = $0 }
                        .tag(photo.id)
                }
            }
            .tabViewStyle(.page(indexDisplayMode: photos.count > 1 ? .automatic : .never))
            .background(Color.black.ignoresSafeArea())
            .safeAreaInset(edge: .bottom) {
                if let current {
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text(captions[current.id] ?? current.caption ?? "Ohne Beschriftung")
                                .font(.subheadline.weight(.semibold))
                                .foregroundStyle(.white)
                                .accessibilityIdentifier("photoCaption")
                            Spacer()
                            if let date = current.capturedAt {
                                Text(date.formatted(date: .abbreviated, time: .shortened)).font(.caption).foregroundStyle(.white.opacity(0.7))
                            }
                        }
                        HStack(spacing: 10) {
                            Button {
                                captionDraft = captions[current.id] ?? current.caption ?? ""
                                editingCaption = true
                            } label: {
                                Label("Beschriften", systemImage: "tag")
                            }
                            .overlayControlStyle()
                            .tint(.white)
                            .accessibilityIdentifier("editCaptionButton")
                            if let image = images[current.id] {
                                ShareLink(item: Image(uiImage: image), preview: SharePreview(current.caption ?? "Foto", image: Image(uiImage: image))) {
                                    Label("Teilen", systemImage: "square.and.arrow.up")
                                }
                                .overlayControlStyle()
                                .tint(.white)
                            }
                            Spacer()
                            if current.syncState == .pending || canDeleteTransferred {
                                Button(role: .destructive) { confirmDiscard = true } label: {
                                    Label("Löschen", systemImage: "trash")
                                }
                                .accessibilityIdentifier("deletePhotoButton")
                                .overlayControlStyle()
                                .tint(.red)
                            }
                        }
                    }
                    .padding(.horizontal)
                    .padding(.vertical, 10)
                    .background(.black.opacity(0.6))
                }
            }
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { dismiss() }.accessibilityIdentifier("closePhotoViewer")
                }
                ToolbarItem(placement: .principal) {
                    if let index = photos.firstIndex(where: { $0.id == selection }), photos.count > 1 {
                        Text("\(index + 1) von \(photos.count)").font(.subheadline.weight(.semibold)).foregroundStyle(.white)
                    }
                }
            }
            .toolbarBackground(.black, for: .navigationBar)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .navigationBarTitleDisplayMode(.inline)
            .alert("Beschriftung", isPresented: $editingCaption) {
                TextField("z. B. Bad OG, Randdämmstreifen", text: $captionDraft)
                Button("Sichern") { Task { await saveCaption() } }
                Button("Abbrechen", role: .cancel) {}
            }
            .confirmationDialog("Foto löschen?", isPresented: $confirmDiscard, titleVisibility: .visible) {
                Button("Foto löschen", role: .destructive) { Task { await discard() } }
            } message: {
                Text(current?.syncState == .pending
                     ? "Das Foto ist noch nicht übertragen und wird nur auf diesem Gerät gelöscht."
                     : "Das Foto wird aus der Bauakte gelöscht – auch im Büro.")
            }
            .task {
                if let store = model.active?.sync?.store {
                    canDeleteTransferred = await MyPermissions(store: store).load(using: model.active?.client).contains("files.delete")
                }
            }
        }
    }

    private func saveCaption() async {
        guard let current, let service = model.active?.service else { return }
        let text = captionDraft.trimmingCharacters(in: .whitespaces)
        try? await service.setCaption(text, of: current.id)
        captions[current.id] = text.isEmpty ? "Ohne Beschriftung" : text
        model.active?.sync?.localChangeMade()
        onChange()
    }

    private func discard() async {
        guard let current, let service = model.active?.service else { return }
        if (try? await service.deleteFile(current.id)) != nil {
            model.active?.sync?.localChangeMade()
            onChange()
            dismiss()
        }
    }
}

private struct PhotoPage: View {
    let file: FileItem
    let loaded: (UIImage) -> Void
    @Environment(AppModel.self) private var model
    @State private var image: UIImage?
    @State private var done = false
    @State private var attempt = 0

    var body: some View {
        ZStack {
            if let image {
                ZoomableImage(image: image)
                    .accessibilityLabel(file.caption ?? "Foto")
                    .accessibilityIdentifier("photoFullImage")
            } else if done {
                VStack(spacing: 12) {
                    Image(systemName: "photo.badge.exclamationmark").font(.largeTitle).foregroundStyle(.white.opacity(0.8))
                    Text(file.isStoredOnServer
                         ? "Das Foto konnte nicht geladen werden. Bitte Verbindung prüfen."
                         : "Das Foto ist auf diesem Gerät nicht verfügbar und noch nicht auf dem Server.")
                        .foregroundStyle(.white).multilineTextAlignment(.center)
                    if file.isStoredOnServer {
                        Button("Erneut laden", systemImage: "arrow.clockwise") { done = false; attempt += 1 }
                            .overlayControlStyle()
                            .tint(.white)
                    }
                }
                .padding()
                .accessibilityIdentifier("photoUnavailable")
            } else {
                ProgressView().tint(.white)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .task(id: "\(file.id)|\(file.isStoredOnServer)|\(attempt)") {
            image = await model.active?.thumbnails?.fullImage(for: file)
            if let image { loaded(image) }
            done = true
        }
    }
}

/// Foto mit Zoom: Aufziehen mit zwei Fingern, Doppeltippen, verschieben im vergrößerten Zustand. Unvergrößert bleibt
/// das Wischen zum Blättern frei.
struct ZoomableImage: View {
    let image: UIImage
    @State private var scale: CGFloat = 1
    @State private var lastScale: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var lastOffset: CGSize = .zero

    var body: some View {
        Image(uiImage: image)
            .resizable()
            .scaledToFit()
            .scaleEffect(scale)
            .offset(offset)
            .gesture(
                MagnifyGesture()
                    .onChanged { value in scale = min(6, max(1, lastScale * value.magnification)) }
                    .onEnded { _ in
                        lastScale = scale
                        if scale <= 1 { reset() }
                    }
            )
            .simultaneousGesture(
                DragGesture()
                    .onChanged { value in
                        offset = CGSize(width: lastOffset.width + value.translation.width, height: lastOffset.height + value.translation.height)
                    }
                    .onEnded { _ in lastOffset = offset },
                including: scale > 1 ? .all : .none
            )
            .onTapGesture(count: 2) {
                withAnimation(.spring(response: 0.3)) {
                    if scale > 1 { reset() } else { scale = 2.5; lastScale = 2.5 }
                }
            }
            .accessibilityAddTraits(.isImage)
            .accessibilityHint("Doppeltippen zum Vergrößern")
    }

    private func reset() {
        scale = 1
        lastScale = 1
        offset = .zero
        lastOffset = .zero
    }
}
