import EstrichCore
import SwiftUI

/// Inhalt eines Bereichs der Bauakte als eigene Seite (iPhone) bzw. rechte Spalte (iPad): Listen mit Leerzuständen,
/// Hinzufügen oben rechts, Wischen und Kontextmenüs für die häufigsten Handgriffe.
struct BauakteAreaView: View {
    let area: BauakteArea
    let content: BauakteContent
    var schema: FormSchema?
    var lists = ValueLists([])
    /// In der breiten Darstellung neben der Übersicht (ohne eigenen Seitentitel).
    var embedded = false
    let reload: () async -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.openURL) private var openURL
    @State private var action: BauakteAction?
    @State private var toast: String?
    @State private var viewing: FileItem?
    @State private var playing: FileItem?
    @State private var previewing: FileItem?
    @State private var editingDocument: FileItem?
    @State private var showsDone = false
    @State private var choosingCustomer = false
    @State private var showsHistory = false

    private var bauakte: Bauakte { content.bauakte }

    var body: some View {
        List {
            if embedded {
                Text(area.title)
                    .font(Typo.title)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: Space.s, leading: Space.xs, bottom: 0, trailing: 0))
                    .accessibilityAddTraits(.isHeader)
            }
            switch area {
            case .media: media
            case .work: work
            case .diary: diary
            case .protocols: protocols
            case .project: project
            case .contacts: contacts
            case .notes: notes
            }
        }
        .listStyle(.insetGrouped)
        .modifier(AreaTitle(title: area.title, embedded: embedded))
        .toolbar { ToolbarItem(placement: .primaryAction) { addMenu } }
        .bauakteActions(bauakte, customerName: content.customer?.name, schema: schema, lists: lists, action: $action) { message in
            if let message { toast = message }
            Task { await reload() }
        }
        .fullScreenCover(item: $viewing) { file in
            PhotoViewer(photos: content.photos.filter(\.isPhoto), selection: file.id) { Task { await reload() } }
        }
        .fullScreenCover(item: $playing) { file in VideoPreview(file: file) }
        .sheet(item: $editingDocument) { file in
            EditDocumentView(file: file) { editingDocument = nil }
        }
        .sheet(isPresented: $choosingCustomer) {
            CustomerPickerView(onSelect: { contact in
                choosingCustomer = false
                Task { await assign(contact) }
            })
        }
        .navigationDestination(isPresented: $showsHistory) { HistoryView(bauakteId: bauakte.id) }
        .filePreview($previewing)
        .toast($toast)
        .refreshable {
            await model.active?.sync?.run()
            await reload()
        }
    }

    // MARK: Hinzufügen

    @ViewBuilder
    private var addMenu: some View {
        switch area {
        case .media:
            Menu {
                Button(BauakteAction.photo.title, systemImage: "camera") { action = .photo }
                Button(BauakteAction.video.title, systemImage: "video") { action = .video }
                Button(BauakteAction.scan.title, systemImage: "doc.viewfinder") { action = .scan }
            } label: { Label("Hinzufügen", systemImage: "plus") }
        case .work:
            Menu {
                Button(BauakteAction.defect.title, systemImage: "exclamationmark.triangle") { action = .defect }
                Button(BauakteAction.task.title, systemImage: "checklist.unchecked") { action = .task }
            } label: { Label("Hinzufügen", systemImage: "plus") }
        case .diary:
            Button { action = .diary } label: { Label("Neuer Eintrag", systemImage: "plus") }
        case .protocols:
            Menu {
                Button(BauakteAction.inspection.title, systemImage: "checklist") { action = .inspection }
                    .accessibilityIdentifier("addInspectionButton")
                Button(BauakteAction.acceptance.title, systemImage: "signature") { action = .acceptance }
                    .accessibilityIdentifier("addAcceptanceButton")
                Button(BauakteAction.measurement.title, systemImage: "drop") { action = .measurement }
            } label: { Label("Hinzufügen", systemImage: "plus") }
        case .project:
            Button("Bearbeiten") { action = .project }
                .accessibilityIdentifier("editProjectButton")
        case .contacts:
            Button { choosingCustomer = true } label: { Label(content.customer == nil ? "Kunde zuordnen" : "Kunde ändern", systemImage: "person.crop.circle.badge.plus") }
        case .notes:
            Button { action = .note } label: { Label("Neue Notiz", systemImage: "plus") }
        }
    }

    // MARK: Fotos und Dokumente

    @ViewBuilder
    private var media: some View {
        Section {
            if content.photos.isEmpty {
                EmptyStateView(symbol: "camera", title: "Noch keine Fotos",
                               message: "Fotos dokumentieren den Stand vor Ort – Randdämmung, Heizung, Untergrund.",
                               actionTitle: "Foto aufnehmen", actionSymbol: "camera") { action = .photo }
            } else {
                PhotoGrid(photos: content.photos) { file in
                    if file.isVideo { playing = file } else { viewing = file }
                }
                .listRowInsets(EdgeInsets(top: 4, leading: 4, bottom: 4, trailing: 4))
            }
        } header: {
            Text("Fotos und Videos (\(content.photos.count))")
        }
        Section {
            if content.documents.isEmpty {
                EmptyStateView(symbol: "doc.viewfinder", title: "Noch keine Dokumente",
                               message: "Lieferschein, Plan oder Abnahme einfach abfotografieren – wird als PDF mit Text gespeichert.",
                               actionTitle: "Dokument scannen", actionSymbol: "doc.viewfinder") { action = .scan }
            }
            ForEach(content.documents) { document in
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
                        .tint(Theme.accentFill)
                }
            }
        } header: {
            Text("Dokumente (\(content.documents.count))")
        }
    }

    // MARK: Aufgaben und Mängel

    @ViewBuilder
    private var work: some View {
        let shown = content.workItems.filter { showsDone ? $0.isDone : !$0.isDone }
        Section {
            Picker("Anzeigen", selection: $showsDone) {
                Text("Offen (\(content.openItems.count))").tag(false)
                Text("Erledigt (\(content.workItems.count - content.openItems.count))").tag(true)
            }
            .pickerStyle(.segmented)
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets())
        }
        if shown.isEmpty {
            Section {
                EmptyStateView(symbol: showsDone ? "checkmark.circle" : "checklist",
                               title: showsDone ? "Noch nichts erledigt" : "Keine offenen Punkte",
                               message: showsDone ? nil : "Mängel und Aufgaben erscheinen hier – mit Frist und Zuständigem.",
                               actionTitle: showsDone ? nil : "Mangel melden", actionSymbol: "exclamationmark.triangle") { action = .defect }
            }
        }
        let defects = shown.filter { $0.kind == .defect }
        let tasks = shown.filter { $0.kind != .defect }
        if !defects.isEmpty {
            Section("Mängel (\(defects.count))") { workRows(defects) }
        }
        if !tasks.isEmpty {
            Section("Aufgaben (\(tasks.count))") { workRows(tasks) }
        }
    }

    private func workRows(_ items: [WorkItem]) -> some View {
        ForEach(items) { item in
            WorkItemRow(item: item, assigneeName: item.assigneeUserId.flatMap { content.names[$0] }) {
                Task { await toggle(item) }
            }
            .swipeActions(edge: .leading, allowsFullSwipe: true) {
                Button { Task { await toggle(item) } } label: {
                    Label(item.isDone ? "Wieder öffnen" : "Erledigt", systemImage: item.isDone ? "arrow.uturn.backward" : "checkmark")
                }
                .tint(item.isDone ? Theme.textSecondary : Theme.success)
            }
        }
    }

    // MARK: Bautagebuch

    @ViewBuilder
    private var diary: some View {
        if content.diary.isEmpty {
            Section {
                EmptyStateView(symbol: "book", title: "Noch kein Eintrag",
                               message: "Was ist heute passiert? Mit Textbausteinen in wenigen Sekunden erfasst.",
                               actionTitle: "Ersten Eintrag schreiben", actionSymbol: "square.and.pencil") { action = .diary }
            }
        }
        ForEach(content.diary) { entry in
            Section {
                DiaryRow(entry: entry, authorName: entry.authorUserId.flatMap { content.names[$0] })
            } header: {
                Text(CalendarDay.date(entry.day)?.formatted(.dateTime.weekday(.wide).day().month(.wide).year()) ?? entry.day)
            }
        }
    }

    // MARK: Protokolle

    @ViewBuilder
    private var protocols: some View {
        Section {
            if content.inspections.filter({ $0.kind == .initialInspection }).isEmpty {
                EmptyStateView(symbol: "checklist", title: "Erstbegehung noch offen",
                               message: "Untergrund, Randdämmung, Heizung, Zufahrt – vor Ort Punkt für Punkt prüfen.",
                               actionTitle: "Erstbegehung durchführen", actionSymbol: "checklist") { action = .inspection }
            }
            ForEach(content.inspections.filter { $0.kind == .initialInspection }) { inspection in
                NavigationLink {
                    InspectionProtocolView(inspection: inspection, bauakte: bauakte,
                                           performerName: inspection.performedByUserId.flatMap { content.names[$0] })
                } label: {
                    InspectionRow(inspection: inspection, performerName: inspection.performedByUserId.flatMap { content.names[$0] })
                }
                .accessibilityIdentifier("inspectionProtocolLink")
            }
        } header: {
            Text("Erstbegehung")
        }
        Section {
            if content.acceptances.isEmpty {
                EmptyStateView(symbol: "signature", title: "Noch keine Abnahme",
                               message: "Abnahmeprotokoll mit Mängeln, Fotos und Unterschriften – auch ohne Verbindung.",
                               actionTitle: "Abnahme erfassen", actionSymbol: "signature") { action = .acceptance }
            }
            ForEach(content.acceptances) { acceptance in
                NavigationLink {
                    AcceptanceDetailView(acceptanceId: acceptance.id, bauakteId: bauakte.id)
                } label: {
                    AcceptanceRow(acceptance: acceptance)
                }
                .accessibilityIdentifier("acceptanceLink")
            }
        } header: {
            Text("Abnahme")
        }
        Section {
            if content.measurements.isEmpty {
                Button { action = .measurement } label: { Label("CM-Messung erfassen", systemImage: "drop") }
            }
            ForEach(content.measurements) { inspection in
                InspectionRow(inspection: inspection, performerName: inspection.performedByUserId.flatMap { content.names[$0] })
            }
        } header: {
            Text("CM-Messungen")
        } footer: {
            Text("Belegreife nach den gängigen Merkblättern; Vorgaben des Bodenbelagsherstellers können strenger sein.")
        }
    }

    // MARK: Basisdaten

    @ViewBuilder
    private var project: some View {
        ProjectSection(bauakte: bauakte, schema: schema, lists: lists) { action = .project }
        if let description = bauakte.description?.compactText {
            Section("Beschreibung") { Text(description).font(Typo.body) }
        }
        if let period = BauakteDetailView.period(bauakte) {
            Section("Geplanter Zeitraum") { Label(period, systemImage: "calendar") }
        }
    }

    // MARK: Kontakte

    @ViewBuilder
    private var contacts: some View {
        Section {
            if let customer = content.customer {
                NavigationLink {
                    CustomerCardView(contactId: customer.id)
                } label: {
                    CustomerSummary(contact: customer, showsActions: false)
                }
                contactActions(phone: customer.phone, email: customer.email, address: customer.addressLine)
            } else {
                EmptyStateView(symbol: "person.crop.circle.badge.plus", title: "Kein Kunde zugeordnet",
                               message: "Der Kunde wird mit der Bauakte übertragen und im Büro verknüpft.",
                               actionTitle: "Kunde zuordnen", actionSymbol: "person.crop.circle.badge.plus") { choosingCustomer = true }
                    .accessibilityIdentifier("assignCustomerButton")
            }
        } header: {
            Text("Kunde")
        }
        Section {
            if bauakte.siteContactName == nil, bauakte.siteContactPhone == nil {
                Text("Kein Ansprechpartner vor Ort hinterlegt – pflegt das Büro.")
                    .font(Typo.subhead)
                    .foregroundStyle(Theme.textSecondary)
            } else {
                DetailRow(bauakte.siteContactName ?? "Ansprechpartner vor Ort", subtitle: bauakte.siteContactPhone,
                          symbol: "person.badge.shield.checkmark") {
                    if let url = ContactLink.phone(bauakte.siteContactPhone) {
                        Link(destination: url) { Label("Anrufen", systemImage: "phone.fill") }
                            .buttonStyle(IconButtonStyle())
                    }
                }
            }
        } header: {
            Text("Vor Ort")
        }
        Section {
            DetailRow(bauakte.addressLine ?? "Adresse fehlt", subtitle: bauakte.hasLocation ? "Standort gespeichert" : nil,
                      symbol: "mappin.and.ellipse", titleColor: bauakte.addressLine == nil ? Theme.warning : Theme.textPrimary) {
                if let url = MapsLink.url(for: bauakte) {
                    Link(destination: url) { Label("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond.fill") }
                        .buttonStyle(IconButtonStyle())
                        .accessibilityIdentifier("navigateButton")
                }
            }
        } header: {
            Text("Baustelle")
        }
    }

    private func contactActions(phone: String?, email: String?, address: String?) -> some View {
        HStack(spacing: Space.s) {
            if let url = ContactLink.phone(phone) {
                Button { openURL(url) } label: { Label("Anrufen", systemImage: "phone.fill") }
                    .buttonStyle(ActionTileStyle())
                    .accessibilityIdentifier("callCustomerButton")
            }
            if let url = ContactLink.email(email) {
                Button { openURL(url) } label: { Label("E-Mail", systemImage: "envelope.fill") }
                    .buttonStyle(ActionTileStyle())
            }
            if let url = MapsLink.url(address: address) {
                Button { openURL(url) } label: { Label("Route", systemImage: "car.fill") }
                    .buttonStyle(ActionTileStyle())
            }
        }
        .listRowInsets(EdgeInsets(top: Space.s, leading: Space.s, bottom: Space.s, trailing: Space.s))
    }

    // MARK: Notizen und Verlauf

    @ViewBuilder
    private var notes: some View {
        Section {
            if content.notes.isEmpty {
                EmptyStateView(symbol: "note.text", title: "Noch keine Notizen",
                               message: "Schlüssel beim Nachbarn, Absprachen mit dem Kunden – für alle in der Akte sichtbar.",
                               actionTitle: "Notiz schreiben", actionSymbol: "square.and.pencil") { action = .note }
            }
            ForEach(content.notes) { note in
                NoteRow(note: note, authorName: note.authorUserId.flatMap { content.names[$0] })
            }
        } header: {
            Text("Notizen (\(content.notes.count))")
        }
        Section {
            Button { showsHistory = true } label: {
                HStack {
                    Label("Verlauf – wer hat was wann geändert", systemImage: "clock.arrow.circlepath")
                        .foregroundStyle(Theme.textPrimary)
                    Spacer()
                    Chevron()
                }
            }
            .accessibilityIdentifier("historyLink")
        }
    }

    // MARK: Ändern

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }

    private func assign(_ contact: Contact) async {
        try? await model.active?.service?.assignCustomer(contact.id, to: bauakte.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }
}

/// Seitentitel nur, wenn der Bereich eine eigene Seite ist.
private struct AreaTitle: ViewModifier {
    let title: String
    let embedded: Bool

    func body(content: Content) -> some View {
        if embedded {
            content
        } else {
            content.navigationTitle(title).navigationBarTitleDisplayMode(.inline)
        }
    }
}
