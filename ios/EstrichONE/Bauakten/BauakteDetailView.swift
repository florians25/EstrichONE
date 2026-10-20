import EstrichCore
import SwiftUI

/// Bauakte auf dem Gerät (07, Abschnitt 2): Kopf, Kunde, Navigation, Aufgaben und Mängel, Notizen, Fotos, Verlauf.
struct BauakteDetailView: View {
    let bauakteId: UUID

    @Environment(AppModel.self) private var model
    @State private var bauakte: Bauakte?
    @State private var photos: [FileItem] = []
    @State private var notes: [Note] = []
    @State private var workItems: [WorkItem] = []
    @State private var customer: Contact?
    @State private var names: [UUID: String] = [:]
    @State private var addingNote = false
    @State private var addingWorkItem: WorkItemKind?
    @State private var capturing = false
    @State private var viewing: FileItem?

    var body: some View {
        Group {
            if let bauakte {
                List {
                    Section {
                        VStack(alignment: .leading, spacing: 6) {
                            HStack {
                                Text(bauakte.number ?? "Neu · noch ohne Nummer")
                                    .font(.subheadline.monospacedDigit().weight(.semibold))
                                    .foregroundStyle(Theme.textSecondary)
                                    .accessibilityIdentifier("bauakteNumber")
                                Spacer()
                                BauakteStatusChip(status: bauakte.status)
                            }
                            Text(bauakte.title)
                                .font(.title2.weight(.semibold))
                                .accessibilityIdentifier("bauakteTitle")
                            SyncStateBadge(state: bauakte.syncState, showsSynced: true)
                        }
                        .padding(.vertical, 4)

                        if let customer {
                            LabeledContent("Kunde", value: customer.name)
                        }
                        if let address = bauakte.addressLine {
                            LabeledContent("Baustelle", value: address)
                        } else {
                            Label("Adresse fehlt – bitte ergänzen, sobald bekannt.", systemImage: "exclamationmark.circle")
                                .foregroundStyle(Theme.hint)
                        }
                        if let url = MapsLink.url(for: bauakte) {
                            Link(destination: url) {
                                Label("Navigation starten", systemImage: "car")
                            }
                        }
                        if let description = bauakte.description {
                            Text(description).foregroundStyle(Theme.textSecondary)
                        }
                    }

                    Section {
                        if workItems.isEmpty {
                            Text("Keine Aufgaben oder Mängel.").foregroundStyle(Theme.textSecondary)
                        }
                        ForEach(workItems) { item in
                            WorkItemRow(item: item, assigneeName: item.assigneeUserId.flatMap { names[$0] }) {
                                Task { await toggle(item) }
                            }
                        }
                    } header: {
                        Text("Aufgaben und Mängel (\(workItems.filter { !$0.isDone }.count) offen)")
                    }

                    Section {
                        if notes.isEmpty {
                            Text("Noch keine Notizen.").foregroundStyle(Theme.textSecondary)
                        }
                        ForEach(notes) { note in
                            NoteRow(note: note, authorName: note.authorUserId.flatMap { names[$0] })
                        }
                    } header: {
                        Text("Notizen (\(notes.count))")
                    }

                    Section {
                        if photos.isEmpty {
                            Text("Noch keine Fotos.").foregroundStyle(Theme.textSecondary)
                        } else {
                            PhotoGrid(photos: photos) { viewing = $0 }
                                .listRowInsets(EdgeInsets(top: 8, leading: 8, bottom: 8, trailing: 8))
                        }
                    } header: {
                        Text("Fotos (\(photos.count))")
                    }

                    Section {
                        NavigationLink {
                            HistoryView(bauakteId: bauakteId)
                        } label: {
                            Label("Verlauf", systemImage: "clock.arrow.circlepath")
                        }
                        .accessibilityIdentifier("historyLink")
                    }
                }
                .navigationTitle(bauakte.number ?? "Neue Bauakte")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItemGroup(placement: .bottomBar) {
                        Button {
                            capturing = true
                        } label: {
                            Label("Foto", systemImage: "camera")
                        }
                        .accessibilityIdentifier("addPhotoButton")
                        Spacer()
                        Button {
                            addingNote = true
                        } label: {
                            Label("Notiz", systemImage: "square.and.pencil")
                        }
                        .accessibilityIdentifier("addNoteButton")
                        Spacer()
                        Menu {
                            Button("Aufgabe", systemImage: "checklist") { addingWorkItem = .task }
                            Button("Mangel", systemImage: "exclamationmark.triangle") { addingWorkItem = .defect }
                        } label: {
                            Label("Aufgabe oder Mangel", systemImage: "plus.circle")
                        }
                        .accessibilityIdentifier("addWorkItemMenu")
                    }
                }
                .fullScreenCover(isPresented: $capturing) {
                    CameraView(bauakte: bauakte) { _ in capturing = false }
                }
                .sheet(item: $viewing) { file in
                    PhotoViewer(file: file)
                }
                .sheet(isPresented: $addingNote) {
                    NewNoteView(bauakteId: bauakteId) { addingNote = false }
                }
                .sheet(item: $addingWorkItem) { kind in
                    NewWorkItemView(bauakteId: bauakteId, initialKind: kind) { addingWorkItem = nil }
                }
            } else {
                ContentUnavailableView("Bauakte nicht gefunden", systemImage: "folder.badge.questionmark",
                                       description: Text("Sie ist auf diesem Gerät nicht (mehr) vorhanden."))
            }
        }
        .task(id: model.active?.sync?.revision) { await reload() }
        .task {
            try? await model.active?.service?.markOpened(bauakteId)
            // Namen für Verfasser und Zuständige; danach neu anzeigen.
            if !(await DirectoryCache.load(model)).isEmpty { await reload() }
        }
        .refreshable { await model.active?.sync?.run() }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        bauakte = try? await service.bauakte(bauakteId)
        photos = ((try? await service.files(of: bauakteId)) ?? []).filter(\.isPhoto)
        notes = (try? await service.notes(of: bauakteId)) ?? []
        workItems = (try? await service.workItems(of: bauakteId)) ?? []
        customer = if let id = bauakte?.customerId { try? await service.contact(id) } else { nil }
        let directory = (try? await service.cachedDirectory()) ?? []
        names = Dictionary(directory.map { ($0.id, $0.displayName) }, uniquingKeysWith: { first, _ in first })
    }

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }
}

/// Raster der Fotos; jedes zeigt seinen Übertragungszustand.
struct PhotoGrid: View {
    let photos: [FileItem]
    let onSelect: (FileItem) -> Void

    var body: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 100), spacing: 6)], spacing: 6) {
            ForEach(photos) { photo in
                Button {
                    onSelect(photo)
                } label: {
                    PhotoThumbnail(file: photo)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(photo.capturedAt.map { "Foto vom \($0.formatted(date: .abbreviated, time: .shortened))" } ?? "Foto")
            }
        }
        .accessibilityIdentifier("photoGrid")
    }
}

struct PhotoThumbnail: View {
    let file: FileItem
    @Environment(AppModel.self) private var model
    @State private var image: UIImage?

    var body: some View {
        Color(uiColor: .secondarySystemBackground)
            .aspectRatio(1, contentMode: .fit)
            .overlay {
                if let image {
                    Image(uiImage: image).resizable().scaledToFill()
                } else {
                    Image(systemName: "photo").foregroundStyle(Theme.textSecondary)
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
            .overlay(alignment: .bottomTrailing) {
                if file.syncState != .synced {
                    Image(systemName: file.syncState == .problem ? "exclamationmark.triangle.fill" : "arrow.up.circle.fill")
                        .foregroundStyle(.white, file.syncState == .problem ? Theme.error : Theme.navy)
                        .font(.title3)
                        .padding(4)
                        .accessibilityLabel(file.syncState == .problem ? "Problem bei der Übertragung" : "Wird übertragen")
                }
            }
            .task(id: file.id) { image = await model.active?.thumbnails?.thumbnail(for: file, size: 120) }
    }
}

struct PhotoViewer: View {
    let file: FileItem
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var image: UIImage?
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            ZStack {
                Color.black.ignoresSafeArea()
                if let image {
                    Image(uiImage: image).resizable().scaledToFit()
                } else if loaded {
                    Text("Das Foto ist auf diesem Gerät nicht verfügbar und noch nicht auf dem Server.")
                        .foregroundStyle(.white).multilineTextAlignment(.center).padding()
                } else {
                    ProgressView().tint(.white)
                }
            }
            .toolbar {
                ToolbarItem(placement: .confirmationAction) { Button("Fertig") { dismiss() } }
            }
            .task {
                image = await model.active?.thumbnails?.fullImage(for: file)
                loaded = true
            }
        }
    }
}
