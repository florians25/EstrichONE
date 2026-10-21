import EstrichCore
import SwiftUI

/// Bauakte auf dem Gerät (07, Abschnitt 2). Schwerpunkt Baustelle: Kopf, Aktionsleiste (Foto, Scannen, Bautagebuch,
/// Erfassen), Kunde und Baustelle mit Anruf und Route, Fotos, Dokumente, Bautagebuch, Aufgaben und Mängel, Prüfungen,
/// Notizen, Verlauf.
struct BauakteDetailView: View {
    let bauakteId: UUID

    @Environment(AppModel.self) private var model
    @State private var bauakte: Bauakte?
    @State private var photos: [FileItem] = []
    @State private var documents: [FileItem] = []
    @State private var notes: [Note] = []
    @State private var workItems: [WorkItem] = []
    @State private var customer: Contact?
    @State private var names: [UUID: String] = [:]
    @State private var addingNote = false
    @State private var diary: [DiaryEntry] = []
    @State private var inspections: [Inspection] = []
    @State private var form: CaptureForm?
    @State private var capturing = false
    @State private var scanning = false
    @State private var choosingCustomer = false
    @State private var viewing: FileItem?
    @State private var previewing: FileItem?
    @State private var playing: FileItem?
    @State private var recordingVideo = false
    @State private var cancelling = false
    @State private var cancelReason = ""
    @State private var statusError: String?
    @State private var editingDocument: FileItem?

    /// Was über das „Erfassen“-Menü bzw. die Aktionsleiste erfasst wird.
    private enum CaptureForm: String, Identifiable {
        case task, defect, diary, cmMeasurement, initialInspection
        var id: String { rawValue }
    }

    var body: some View {
        Group {
            if let bauakte {
                List {
                    header(bauakte)
                    actionBar
                    contactSection(bauakte)
                    photoSection
                    documentSection
                    diarySection
                    workSection
                    inspectionSection
                    noteSection
                    Section {
                        NavigationLink {
                            HistoryView(bauakteId: bauakteId)
                        } label: {
                            Label("Verlauf", systemImage: "clock.arrow.circlepath")
                        }
                        .accessibilityIdentifier("historyLink")
                    }
                }
                .listSectionSpacing(.compact)
                .navigationTitle(bauakte.number ?? "Neue Bauakte")
                .navigationBarTitleDisplayMode(.inline)
                .fullScreenCover(isPresented: $capturing) {
                    CameraView(bauakte: bauakte) { _ in capturing = false }
                }
                .fullScreenCover(isPresented: $scanning) {
                    ScanFlowView(bauakte: bauakte) { _ in scanning = false }
                }
                .sheet(item: $viewing) { file in
                    PhotoViewer(photos: photos.filter(\.isPhoto), selection: file.id) { Task { await reload() } }
                }
                .sheet(isPresented: $recordingVideo) {
                    VideoCaptureView(bauakte: bauakte) { _ in recordingVideo = false }
                }
                .fullScreenCover(item: $playing) { file in
                    VideoPreview(file: file)
                }
                .sheet(item: $editingDocument) { file in
                    EditDocumentView(file: file) { editingDocument = nil }
                }
                .sheet(isPresented: $addingNote) {
                    NewNoteView(bauakteId: bauakteId) { addingNote = false }
                }
                .sheet(isPresented: $choosingCustomer) {
                    CustomerPickerView(onSelect: { contact in
                        choosingCustomer = false
                        Task { await assign(contact) }
                    })
                }
                .sheet(item: $form) { form in
                    switch form {
                    case .task: NewWorkItemView(bauakteId: bauakteId, initialKind: .task) { self.form = nil }
                    case .defect: NewWorkItemView(bauakteId: bauakteId, initialKind: .defect) { self.form = nil }
                    case .diary: NewDiaryEntryView(bauakteId: bauakteId) { self.form = nil }
                    case .cmMeasurement: NewCmMeasurementView(bauakteId: bauakteId) { self.form = nil }
                    case .initialInspection: InitialInspectionView(bauakte: bauakte) { self.form = nil }
                    }
                }
                .filePreview($previewing)
                .alert("Bauakte stornieren?", isPresented: $cancelling) {
                    TextField("Grund (Pflicht)", text: $cancelReason)
                        .accessibilityIdentifier("cancelReasonField")
                    Button("Stornieren", role: .destructive) { Task { await change(to: .cancelled, reason: cancelReason) } }
                        .disabled(cancelReason.trimmingCharacters(in: .whitespaces).isEmpty)
                    Button("Abbrechen", role: .cancel) {}
                } message: {
                    Text("Die Bauakte bleibt mit Verlauf erhalten. Löschen ist nur im Büro möglich.")
                }
            } else {
                MissingBauakteView(bauakteId: bauakteId) { await reload() }
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

    // MARK: Abschnitte

    private func header(_ bauakte: Bauakte) -> some View {
        Section {
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text(bauakte.number ?? "Neu · noch ohne Nummer")
                        .font(.subheadline.monospacedDigit().weight(.semibold))
                        .foregroundStyle(Theme.textSecondary)
                        .accessibilityIdentifier("bauakteNumber")
                    Spacer()
                    Menu {
                        ForEach(BauakteStatus.allCases.filter { $0 != bauakte.status && $0 != .cancelled
                                                                 && !(bauakte.status == .cancelled && $0 == .completed) }, id: \.self) { status in
                            Button(status.title) { Task { await change(to: status) } }
                        }
                        if bauakte.status != .cancelled {
                            Divider()
                            Button("Stornieren …", systemImage: "xmark.circle", role: .destructive) {
                                cancelReason = ""
                                cancelling = true
                            }
                            .accessibilityIdentifier("cancelBauakteButton")
                        }
                    } label: {
                        HStack(spacing: 4) {
                            BauakteStatusChip(status: bauakte.status)
                            Image(systemName: "chevron.down").font(.caption2.weight(.bold)).foregroundStyle(Theme.textSecondary)
                        }
                        .frame(minHeight: 44)
                        .contentShape(Rectangle())
                    }
                    .accessibilityLabel("Status: \(bauakte.status.title). Ändern")
                    .accessibilityIdentifier("statusMenu")
                }
                Text(bauakte.title)
                    .font(.title3.weight(.semibold))
                    .accessibilityIdentifier("bauakteTitle")
                if bauakte.status == .cancelled {
                    Label("Storniert: \(bauakte.cancellationReason ?? "ohne Angabe")", systemImage: "xmark.circle")
                        .font(.footnote).foregroundStyle(Theme.error)
                }
                if let statusError {
                    Text(statusError).font(.footnote).foregroundStyle(Theme.error)
                }
                if let description = bauakte.description {
                    Text(description).font(.subheadline).foregroundStyle(Theme.textSecondary)
                }
                SyncStateBadge(state: bauakte.syncState, showsSynced: true)
            }
            .padding(.vertical, 2)
        }
    }

    /// Aktionsleiste im Inhalt statt unterer Werkzeugleiste: die Leiste läge unter Tab-Leiste und schwebender
    /// „Neue Bauakte“-Schaltfläche (iOS 26).
    private var actionBar: some View {
        Section {
            HStack(spacing: 8) {
                Button { capturing = true } label: { Label("Foto", systemImage: "camera.fill") }
                    .buttonStyle(ActionTileStyle(rank: .accent))
                    .accessibilityIdentifier("addPhotoButton")
                Button { scanning = true } label: { Label("Scannen", systemImage: "doc.viewfinder") }
                    .buttonStyle(ActionTileStyle(rank: .primary))
                    .accessibilityIdentifier("scanDocumentButton")
                Button { form = .diary } label: { Label("Tagebuch", systemImage: "book.fill") }
                    .buttonStyle(ActionTileStyle())
                    .accessibilityIdentifier("addDiaryButton")
                Button { addingNote = true } label: { Label("Notiz", systemImage: "square.and.pencil") }
                    .buttonStyle(ActionTileStyle())
                    .accessibilityIdentifier("addNoteButton")
                Menu {
                    Button("Video", systemImage: "video") { recordingVideo = true }
                    Button("Aufgabe", systemImage: "checklist") { form = .task }
                    Button("Mangel", systemImage: "exclamationmark.triangle") { form = .defect }
                    Button("CM-Messung", systemImage: "drop") { form = .cmMeasurement }
                    Button("Erstbegehung", systemImage: "checkmark.rectangle.stack") { form = .initialInspection }
                } label: {
                    Label("Mehr", systemImage: "plus")
                }
                .buttonStyle(ActionTileStyle())
                .accessibilityIdentifier("addMenu")
            }
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets(top: 2, leading: 0, bottom: 2, trailing: 0))
        }
    }

    private func contactSection(_ bauakte: Bauakte) -> some View {
        Section {
            if let customer {
                CustomerSummary(contact: customer)
            } else {
                Button { choosingCustomer = true } label: {
                    Label("Kunde zuordnen", systemImage: "person.crop.circle.badge.plus")
                }
                .accessibilityIdentifier("assignCustomerButton")
            }
            HStack(spacing: 12) {
                Image(systemName: "mappin.and.ellipse")
                    .font(.subheadline)
                    .foregroundStyle(Theme.navyText)
                    .frame(width: 34, height: 34)
                    .background(Theme.navyBackground, in: Circle())
                    .accessibilityHidden(true)
                if let address = bauakte.addressLine {
                    Text(address).font(.subheadline)
                } else {
                    Text(bauakte.hasLocation ? "Standort gespeichert, Adresse fehlt" : "Adresse fehlt – bitte ergänzen, sobald bekannt.")
                        .font(.subheadline)
                        .foregroundStyle(Theme.hint)
                }
                Spacer(minLength: 4)
                if let url = MapsLink.url(for: bauakte) {
                    Link(destination: url) { Label("Route starten", systemImage: "car.fill") }
                        .buttonStyle(IconButtonStyle(rank: .primary))
                        .accessibilityIdentifier("navigateButton")
                }
            }
            if let phone = bauakte.siteContactPhone, let url = ContactLink.phone(phone) {
                HStack(spacing: 12) {
                    Image(systemName: "person.badge.shield.checkmark")
                        .font(.subheadline)
                        .foregroundStyle(Theme.navyText)
                        .frame(width: 34, height: 34)
                        .background(Theme.navyBackground, in: Circle())
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(bauakte.siteContactName ?? "Ansprechpartner vor Ort").font(.subheadline.weight(.medium))
                        Text(phone).font(.caption).foregroundStyle(Theme.textSecondary)
                    }
                    Spacer(minLength: 4)
                    Link(destination: url) { Label("Ansprechpartner anrufen", systemImage: "phone.fill") }
                        .buttonStyle(IconButtonStyle(rank: .accent))
                }
            }
        } header: {
            Text("Kunde und Baustelle")
        }
    }

    private var photoSection: some View {
        Section {
            if photos.isEmpty {
                Button { capturing = true } label: {
                    Label("Noch keine Fotos – jetzt aufnehmen", systemImage: "camera")
                }
            } else {
                PhotoGrid(photos: photos) { file in
                    if file.isVideo { playing = file } else { viewing = file }
                }
                    .listRowInsets(EdgeInsets(top: 8, leading: 8, bottom: 8, trailing: 8))
            }
        } header: {
            SectionHeader("Fotos und Videos", count: photos.count) {
                if !photos.isEmpty {
                    Button { capturing = true } label: { Label("Foto", systemImage: "plus") }
                        .font(.subheadline.weight(.semibold))
                }
            }
        }
    }

    private var documentSection: some View {
        Section {
            if documents.isEmpty {
                Button { scanning = true } label: {
                    Label("Lieferschein, Plan oder Abnahme scannen", systemImage: "doc.viewfinder")
                }
            }
            ForEach(documents) { document in
                Button { previewing = document } label: {
                    DocumentRow(file: document, isLoading: previewing?.id == document.id)
                }
                .buttonStyle(.plain)
                .accessibilityIdentifier("document-\(document.displayName)")
                .contextMenu {
                    Button("Ansehen", systemImage: "eye") { previewing = document }
                    Button("Titel und Art ändern", systemImage: "pencil") { editingDocument = document }
                }
                .swipeActions {
                    Button { editingDocument = document } label: { Label("Bearbeiten", systemImage: "pencil") }
                        .tint(Theme.navyLight)
                }
            }
        } header: {
            SectionHeader("Dokumente", count: documents.count) {
                if !documents.isEmpty {
                    Button { scanning = true } label: { Label("Scannen", systemImage: "plus") }
                        .font(.subheadline.weight(.semibold))
                }
            }
        }
    }

    private var diarySection: some View {
        Section {
            if diary.isEmpty {
                Button { form = .diary } label: {
                    Label("Ersten Tagebucheintrag schreiben", systemImage: "book")
                }
            }
            ForEach(diary) { entry in
                DiaryRow(entry: entry, authorName: entry.authorUserId.flatMap { names[$0] })
            }
        } header: {
            SectionHeader("Bautagebuch", count: diary.count) {
                if !diary.isEmpty {
                    Button { form = .diary } label: { Label("Eintrag", systemImage: "plus") }
                        .font(.subheadline.weight(.semibold))
                }
            }
        }
    }

    private var workSection: some View {
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
    }

    private var inspectionSection: some View {
        Section {
            if inspections.isEmpty {
                Text("Noch keine Erstbegehung oder CM-Messung.").foregroundStyle(Theme.textSecondary)
            }
            ForEach(inspections) { inspection in
                if inspection.kind == .initialInspection, let bauakte {
                    NavigationLink {
                        InspectionProtocolView(inspection: inspection, bauakte: bauakte,
                                               performerName: inspection.performedByUserId.flatMap { names[$0] })
                    } label: {
                        InspectionRow(inspection: inspection, performerName: inspection.performedByUserId.flatMap { names[$0] })
                    }
                    .accessibilityIdentifier("inspectionProtocolLink")
                } else {
                    InspectionRow(inspection: inspection, performerName: inspection.performedByUserId.flatMap { names[$0] })
                }
            }
        } header: {
            SectionHeader("Prüfungen", count: inspections.count) {
                Button { form = .initialInspection } label: { Label("Erstbegehung", systemImage: "plus") }
                    .font(.subheadline.weight(.semibold))
                    .accessibilityIdentifier("addInspectionButton")
            }
        }
    }

    private var noteSection: some View {
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
    }

    // MARK: Laden und Ändern

    private func reload() async {
        guard let service = model.active?.service else { return }
        bauakte = try? await service.bauakte(bauakteId)
        let files = (try? await service.files(of: bauakteId)) ?? []
        photos = files.filter { $0.isPhoto || $0.isVideo }
        documents = files.filter(\.isDocument).sorted { ($0.createdAt ?? .distantPast) > ($1.createdAt ?? .distantPast) }
        notes = (try? await service.notes(of: bauakteId)) ?? []
        workItems = (try? await service.workItems(of: bauakteId)) ?? []
        diary = (try? await service.diary(of: bauakteId)) ?? []
        inspections = (try? await service.inspections(of: bauakteId)) ?? []
        customer = if let id = bauakte?.customerId { try? await service.contact(id) } else { nil }
        let directory = (try? await service.cachedDirectory()) ?? []
        names = Dictionary(directory.map { ($0.id, $0.displayName) }, uniquingKeysWith: { first, _ in first })
    }

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }

    /// Status ändern bzw. stornieren – sofort lokal, Übertragung und Konfliktprüfung über die Synchronisation.
    private func change(to status: BauakteStatus, reason: String? = nil) async {
        do throws(BauakteError) {
            try await model.active?.service?.changeStatus(status, reason: reason, of: bauakteId)
            statusError = nil
            model.active?.sync?.localChangeMade()
            await reload()
        } catch {
            statusError = error.message
        }
    }

    private func assign(_ contact: Contact) async {
        try? await model.active?.service?.assignCustomer(contact.id, to: bauakteId)
        model.active?.sync?.localChangeMade()
        await reload()
    }
}

/// Raster der Fotos; jedes zeigt Beschriftung und Übertragungszustand.
struct PhotoGrid: View {
    let photos: [FileItem]
    let onSelect: (FileItem) -> Void

    var body: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 84), spacing: 6)], spacing: 6) {
            ForEach(photos) { photo in
                Button {
                    onSelect(photo)
                } label: {
                    PhotoThumbnail(file: photo)
                }
                .buttonStyle(.plain)
                .accessibilityLabel([photo.caption, photo.capturedAt.map { "Foto vom \($0.formatted(date: .abbreviated, time: .shortened))" } ?? "Foto"]
                    .compactMap { $0 }.joined(separator: ", "))
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
                    Image(systemName: file.isVideo ? "video" : "photo").foregroundStyle(Theme.textSecondary)
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
                if let caption = file.caption {
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
            .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
            .overlay(alignment: .topTrailing) {
                if file.syncState != .synced {
                    Image(systemName: file.syncState == .problem ? "exclamationmark.triangle.fill" : "arrow.up.circle.fill")
                        .foregroundStyle(.white, file.syncState == .problem ? Theme.error : Theme.navy)
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
                            if current.syncState == .pending {
                                Button(role: .destructive) { confirmDiscard = true } label: {
                                    Label("Verwerfen", systemImage: "trash")
                                }
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
                ToolbarItem(placement: .confirmationAction) { Button("Fertig") { dismiss() } }
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
            .confirmationDialog("Foto verwerfen?", isPresented: $confirmDiscard, titleVisibility: .visible) {
                Button("Foto löschen", role: .destructive) { Task { await discard() } }
            } message: {
                Text("Das Foto ist noch nicht übertragen und wird nur auf diesem Gerät gelöscht.")
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
        if (try? await service.discardUnsentFile(current.id)) == true {
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

    var body: some View {
        ZStack {
            if let image {
                ZoomableImage(image: image)
            } else if done {
                Text("Das Foto ist auf diesem Gerät nicht verfügbar und noch nicht auf dem Server.")
                    .foregroundStyle(.white).multilineTextAlignment(.center).padding()
            } else {
                ProgressView().tint(.white)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .task(id: "\(file.id)|\(file.isStoredOnServer)") {
            image = await model.active?.thumbnails?.fullImage(for: file)
            if let image { loaded(image) }
            done = true
        }
    }
}

/// Bauakte fehlt auf dem Gerät (z. B. eingeplant, aber noch nicht übertragen, oder nicht zugewiesen): laden oder
/// „Mir zuweisen“ statt einer leeren Seite.
struct MissingBauakteView: View {
    let bauakteId: UUID
    let reload: () async -> Void

    @Environment(AppModel.self) private var model
    @State private var working = false
    @State private var message: String?
    @State private var triedLoading = false

    var body: some View {
        ContentUnavailableView {
            Label(triedLoading ? "Bauakte nicht verfügbar" : "Bauakte wird geladen …", systemImage: "folder.badge.questionmark")
        } description: {
            Text(message ?? (triedLoading
                ? "Sie ist Ihnen nicht zugewiesen oder noch nicht übertragen. Sie können sie sich selbst zuweisen."
                : "Die Bauakte liegt noch nicht auf diesem Gerät."))
        } actions: {
            if triedLoading {
                Button {
                    Task { await assignToMe() }
                } label: {
                    ActionLabel("Mir zuweisen", systemImage: "person.badge.plus", isBusy: working)
                }
                .estrichButton(.primary, size: .regular)
                .disabled(working)
                .accessibilityIdentifier("assignToMeButton")
                Button {
                    Task { await load() }
                } label: {
                    Label("Erneut laden", systemImage: "arrow.clockwise")
                }
                .estrichButton(.tertiary)
                .disabled(working)
            } else {
                ProgressView()
            }
        }
        .task { await load() }
    }

    private func load() async {
        guard let sync = model.active?.sync else { return }
        working = true
        defer { working = false }
        let outcome = await sync.load(bauakte: bauakteId)
        await reload()
        triedLoading = true
        message = outcome == .offline ? "Ohne Verbindung kann die Bauakte nicht geladen werden. Bitte später erneut versuchen." : nil
    }

    private func assignToMe() async {
        guard let active = model.active, let sync = active.sync else { return }
        working = true
        defer { working = false }
        do throws(APIError) {
            try await active.client.assignToMe(bauakteId)
            _ = await sync.load(bauakte: bauakteId)
            await reload()
        } catch {
            message = error == .offline
                ? "Ohne Verbindung nicht möglich. Bitte später erneut versuchen."
                : (error.status == 404 ? "Diese Bauakte können Sie sich nicht selbst zuweisen. Bitte im Büro melden." : error.message)
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
