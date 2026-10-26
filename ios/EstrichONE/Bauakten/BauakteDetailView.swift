import EstrichCore
import SwiftUI

/// Bauakte auf dem Gerät (07, Abschnitt 2). Schwerpunkt Baustelle: Kopf, Aktionsleiste (Foto, Scannen, Bautagebuch,
/// Erfassen), Kunde und Baustelle mit Anruf und Route, Fotos, Dokumente, Bautagebuch, Aufgaben und Mängel, Prüfungen,
/// Notizen, Verlauf.
struct BauakteDetailView: View {
    let bauakteId: UUID

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
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
    @State private var acceptances: [Acceptance] = []
    @State private var showsInspectionPDF = false
    @State private var schema: FormSchema?
    @State private var lists = ValueLists([])
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
        case task, defect, diary, cmMeasurement, initialInspection, acceptance, project
        var id: String { rawValue }
    }

    var body: some View {
        Group {
            if let bauakte {
                List {
                    header(bauakte)
                    actionBar
                    contactSection(bauakte)
                    ProjectSection(bauakte: bauakte, schema: schema, lists: lists) { form = .project }
                    photoSection
                    documentSection
                    diarySection
                    workSection
                    inspectionSection
                    acceptanceSection
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
                .navigationDestination(isPresented: $showsInspectionPDF) {
                    if let inspection = latestInitialInspection {
                        InspectionProtocolView(inspection: inspection, bauakte: bauakte,
                                               performerName: inspection.performedByUserId.flatMap { names[$0] }, opensPDF: true)
                    }
                }
                .navigationTitle(bauakte.number ?? "Neue Bauakte")
                .navigationBarTitleDisplayMode(.inline)
                .fullScreenCover(isPresented: $capturing) {
                    CameraView(bauakte: bauakte) { _ in capturing = false }
                }
                .fullScreenCover(isPresented: $scanning) {
                    ScanFlowView(bauakte: bauakte) { _ in scanning = false }
                }
                .fullScreenCover(item: $viewing) { file in
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
                    case .acceptance: NewAcceptanceView(bauakte: bauakte, customerName: customer?.name) { self.form = nil }
                    case .project: EditProjectView(bauakte: bauakte, schema: schema, lists: lists) { self.form = nil; Task { await reload() } }
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

    /// Kopf als weiche Karte im Markenblau: Nummer und Titel groß, Status als farbige Kapsel (Menü), Kurzangaben als
    /// runde Chips, Schnellaktionen als runde Symbolknöpfe. iPad: Kunde, Baustelle und Zeitraum nebeneinander.
    private func header(_ bauakte: Bauakte) -> some View {
        let shape = RoundedRectangle(cornerRadius: 24, style: .continuous)
        let projectBadges = schema?.badges(for: bauakte.projectData, lists: lists) ?? bauakte.project.badges
        let badges = projectBadges + [Self.period(bauakte)].compactMap { $0 }
        return Section {
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .center) {
                    Text(bauakte.number ?? "Neu · noch ohne Nummer")
                        .font(.subheadline.monospacedDigit().weight(.semibold))
                        .foregroundStyle(.white.opacity(0.75))
                        .accessibilityIdentifier("bauakteNumber")
                    Spacer()
                    statusMenu(bauakte)
                }
                Text(bauakte.title)
                    .font(.title2.weight(.bold))
                    .foregroundStyle(.white)
                    .multilineTextAlignment(.leading)
                    .accessibilityIdentifier("bauakteTitle")
                if bauakte.status == .cancelled {
                    Label("Storniert: \(bauakte.cancellationReason ?? "ohne Angabe")", systemImage: "xmark.circle")
                        .font(.footnote.weight(.semibold)).foregroundStyle(.white)
                }
                if let statusError {
                    Text(statusError).font(.footnote).foregroundStyle(.white)
                        .padding(8).background(Theme.error.opacity(0.8), in: RoundedRectangle(cornerRadius: 8))
                }
                if sizeClass == .regular {
                    HStack(alignment: .top, spacing: 24) {
                        headerInfo("Kunde", customer?.name ?? "–", symbol: "person.crop.circle")
                        headerInfo("Baustelle", bauakte.addressLine ?? "Adresse fehlt", symbol: "mappin.and.ellipse")
                        headerInfo("Zeitraum", Self.period(bauakte) ?? "nicht geplant", symbol: "calendar")
                    }
                } else if let address = bauakte.addressLine {
                    Label(address, systemImage: "mappin.and.ellipse").labelStyle(TightLabelStyle())
                        .font(.footnote).foregroundStyle(.white.opacity(0.85)).lineLimit(2)
                }
                if !projectBadges.isEmpty || (sizeClass != .regular && !badges.isEmpty) {
                    ChipFlow {
                        ForEach(sizeClass == .regular ? projectBadges : badges, id: \.self) { badge in
                            Text(badge)
                                .font(.caption.weight(.semibold))
                                .foregroundStyle(.white)
                                .padding(.horizontal, 10).padding(.vertical, 4)
                                .background(.white.opacity(0.16), in: Capsule())
                        }
                    }
                    .accessibilityIdentifier("projectBadges")
                }
                HStack(spacing: 12) {
                    if let phone = ContactLink.phone(bauakte.siteContactPhone ?? customer?.phone) {
                        Link(destination: phone) { roundAction("Anrufen", symbol: "phone.fill") }
                            .accessibilityIdentifier("headerCallButton")
                    }
                    if let route = MapsLink.url(for: bauakte) {
                        Link(destination: route) { roundAction("Route", symbol: "car.fill") }
                            .accessibilityIdentifier("headerRouteButton")
                    }
                    Button { capturing = true } label: { roundAction("Foto", symbol: "camera.fill") }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("headerPhotoButton")
                    Button { scanning = true } label: { roundAction("Scannen", symbol: "doc.viewfinder") }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("headerScanButton")
                    if latestInitialInspection != nil {
                        Button { showsInspectionPDF = true } label: { roundAction("Erstbegehung (PDF)", symbol: "doc.richtext") }
                            .buttonStyle(.plain)
                            .accessibilityIdentifier("headerInspectionPDFButton")
                    }
                    Spacer(minLength: 0)
                    SyncStateBadge(state: bauakte.syncState, showsSynced: true)
                        .padding(.horizontal, 8).padding(.vertical, 4)
                        .background(.white, in: Capsule())
                }
                if let description = bauakte.description {
                    Text(description).font(.subheadline).foregroundStyle(.white.opacity(0.85))
                }
            }
            .padding(18)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
                LinearGradient(colors: [Theme.navy, Theme.navyLight], startPoint: .topLeading, endPoint: .bottomTrailing), in: shape)
            .overlay(shape.strokeBorder(.white.opacity(0.08)))
            .shadow(color: Theme.navy.opacity(0.18), radius: 12, y: 6)
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets(top: 4, leading: 0, bottom: 8, trailing: 0))
        }
    }

    private func statusMenu(_ bauakte: Bauakte) -> some View {
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
                Image(systemName: "chevron.down").font(.caption2.weight(.bold)).foregroundStyle(.white.opacity(0.8))
            }
            .padding(.leading, 2).padding(.trailing, 8).padding(.vertical, 2)
            .background(.white.opacity(0.92), in: Capsule())
            .frame(minHeight: 44)
            .contentShape(Rectangle())
        }
        .accessibilityLabel("Status: \(bauakte.status.title). Ändern")
        .accessibilityIdentifier("statusMenu")
    }

    private func headerInfo(_ title: String, _ value: String, symbol: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Label(title, systemImage: symbol).labelStyle(TightLabelStyle())
                .font(.caption.weight(.semibold)).foregroundStyle(.white.opacity(0.7))
            Text(value).font(.subheadline.weight(.medium)).foregroundStyle(.white).lineLimit(2)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func roundAction(_ title: String, symbol: String) -> some View {
        Image(systemName: symbol)
            .font(.body.weight(.semibold))
            .foregroundStyle(.white)
            .frame(width: 44, height: 44)
            .background(.white.opacity(0.16), in: Circle())
            .overlay(Circle().strokeBorder(.white.opacity(0.25)))
            .contentShape(Circle())
            .hoverEffect(.highlight)
            .accessibilityLabel(title)
    }

    /// „2.–6. Nov.“ bzw. „ab 2. Nov.“
    static func period(_ bauakte: Bauakte) -> String? {
        guard let start = bauakte.plannedStart.flatMap({ CalendarDay.date($0) }) else { return nil }
        let format = Date.FormatStyle.dateTime.day().month(.abbreviated)
        if let end = bauakte.plannedEnd.flatMap({ CalendarDay.date($0) }), end != start {
            return "\(start.formatted(format)) – \(end.formatted(format))"
        }
        return "ab \(start.formatted(format))"
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

    /// Neueste Erstbegehung (Korrekturen sind neuere Fassungen).
    private var latestInitialInspection: Inspection? {
        inspections.first { $0.kind == .initialInspection }
    }

    private var acceptanceSection: some View {
        Section {
            if acceptances.isEmpty {
                Text("Noch kein Abnahmeprotokoll.").foregroundStyle(Theme.textSecondary)
            }
            ForEach(acceptances) { acceptance in
                NavigationLink {
                    AcceptanceDetailView(acceptanceId: acceptance.id, bauakteId: bauakteId)
                } label: {
                    AcceptanceRow(acceptance: acceptance)
                }
                .accessibilityIdentifier("acceptanceLink")
            }
        } header: {
            SectionHeader("Abnahme", count: acceptances.count) {
                Button { form = .acceptance } label: { Label("Abnahme", systemImage: "plus") }
                    .font(.subheadline.weight(.semibold))
                    .accessibilityIdentifier("addAcceptanceButton")
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
        acceptances = (try? await service.acceptances(of: bauakteId)) ?? []
        if schema == nil { schema = await service.formSchema(using: model.active?.client) }
        lists = await service.valueLists()
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
