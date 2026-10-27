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
    @State private var editingRoom: RoomEditing?
    @State private var deletingRoom: Room?

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
            case .rooms: rooms
            case .protocols: protocols
            case .project: project
            case .contacts: contacts
            case .notes: notes
            }
        }
        .listStyle(.insetGrouped)
        .listSectionSpacing(.compact)
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
        .sheet(item: $editingRoom) { editing in
            RoomEditView(bauakteId: bauakte.id, room: editing.room, defaults: RoomInput(defaults: bauakte.project), floors: floorChoices) { message in
                editingRoom = nil
                if let message { toast = message }
                Task { await reload() }
            }
        }
        .confirmationDialog("Raum löschen?", isPresented: Binding { deletingRoom != nil } set: { if !$0 { deletingRoom = nil } },
                            titleVisibility: .visible, presenting: deletingRoom) { room in
            Button("„\(room.name)“ löschen", role: .destructive) { Task { await delete(room) } }
        } message: { _ in
            Text("Der Raum wird aus der Bauakte entfernt – auch im Büro.")
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
        case .rooms:
            Button { editingRoom = RoomEditing() } label: { Label("Raum hinzufügen", systemImage: "plus") }
                .accessibilityIdentifier("addRoomButton")
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
                Text(DiaryRow.dayTitle(entry.day))
            }
        }
    }

    // MARK: Räume und Flächen

    @ViewBuilder
    private var rooms: some View {
        if content.rooms.isEmpty {
            Section {
                EmptyStateView(symbol: "square.split.2x2", title: "Noch keine Räume",
                               message: "Räume mit Fläche, Estrichdicke und Heizung erfassen – die Summen rechnet die App.",
                               actionTitle: "Raum hinzufügen", actionSymbol: "plus") { editingRoom = RoomEditing() }
            }
        } else {
            Section {
                RoomTotalsView(rooms: content.rooms)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())
            } footer: {
                if let planned = bauakte.project.totalArea {
                    Text(Self.areaComparison(planned: planned, measured: RoomTotals(content.rooms).area))
                }
            }
            ForEach(roomFloors) { group in
                Section {
                    ForEach(group.rooms) { room in
                        roomRow(room)
                    }
                } header: {
                    HStack {
                        Text(group.name)
                        Spacer()
                        Text("\(RoomTotals(group.rooms).area.formatted(.number.precision(.fractionLength(0...1)))) m²").monospacedDigit()
                    }
                }
            }
        }
    }

    private func roomRow(_ room: Room) -> some View {
        Button { editingRoom = RoomEditing(room: room) } label: { RoomRow(room: room) }
            .buttonStyle(.plain)
            .accessibilityIdentifier("room-\(room.name)")
            .swipeActions(edge: .leading, allowsFullSwipe: true) {
                if let next = room.status.next {
                    Button { Task { await setStatus(next, of: room) } } label: { Label(next.title, systemImage: next.symbol) }
                        .tint(next == .ready ? Theme.success : Theme.accentFill)
                }
            }
            .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                Button(role: .destructive) { deletingRoom = room } label: { Label("Löschen", systemImage: "trash") }
            }
            .contextMenu {
                Button("Bearbeiten", systemImage: "pencil") { editingRoom = RoomEditing(room: room) }
                Picker("Stand", selection: Binding { room.status } set: { status in Task { await setStatus(status, of: room) } }) {
                    ForEach(RoomStatus.allCases) { status in Label(status.title, systemImage: status.symbol).tag(status) }
                }
                Button("Löschen", systemImage: "trash", role: .destructive) { deletingRoom = room }
            }
    }

    /// Fläche der Räume gegen die Fläche aus den Basisdaten – fällt eine Lücke auf, steht sie da.
    static func areaComparison(planned: Double, measured: Double) -> String {
        func m2(_ value: Double) -> String { "\(value.formatted(.number.precision(.fractionLength(0...1)))) m²" }
        let gap = planned - measured
        if abs(gap) < 0.05 { return "Passt zur Fläche aus den Basisdaten (\(m2(planned)))." }
        return gap > 0 ? "Laut Basisdaten \(m2(planned)) – in den Räumen fehlen noch \(m2(gap))."
                       : "In den Räumen \(m2(-gap)) mehr als in den Basisdaten (\(m2(planned)))."
    }

    /// Räume je Geschoss in Bauabfolge.
    private var roomFloors: [RoomFloorGroup] {
        var groups: [RoomFloorGroup] = []
        for room in content.rooms {
            let name = room.floor?.compactText ?? "Ohne Geschoss"
            if let index = groups.firstIndex(where: { $0.name == name }) {
                groups[index].rooms.append(room)
            } else {
                groups.append(RoomFloorGroup(name: name, rooms: [room]))
            }
        }
        return groups
    }

    /// Geschosse zur Auswahl: aus den Basisdaten („KG, EG, OG“), sonst die üblichen, plus bereits verwendete.
    private var floorChoices: [String] {
        let planned = (bauakte.project.floors ?? "").split(whereSeparator: { $0 == "," || $0 == ";" || $0 == "/" })
            .map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        var result = planned.isEmpty ? ["KG", "EG", "OG", "DG"] : planned
        for floor in content.rooms.compactMap({ $0.floor?.compactText }) where !result.contains(floor) {
            result.append(floor)
        }
        return Array(result.prefix(5))
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
        if let description = bauakte.description?.compactText {
            Section("Beschreibung") { Text(description).font(Typo.body) }
        }
        // Der geplante Zeitraum steht als erste Zeile in den Basisdaten.
        ProjectSection(bauakte: bauakte, schema: schema, lists: lists) { action = .project }
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
        if !content.parties.isEmpty {
            Section {
                ForEach(content.parties) { party in
                    PartyRow(party: party)
                }
            } header: {
                Text("Beteiligte (\(content.parties.count))")
            } footer: {
                Text("Bauherr, Bauleitung, Planer und Lieferanten pflegt das Büro.")
            }
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
                DetailRow("Änderungsverlauf", subtitle: "Wer hat was wann geändert", symbol: "clock.arrow.circlepath") { Chevron() }
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("historyLink")
        }
    }

    // MARK: Ändern

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }

    private func setStatus(_ status: RoomStatus, of room: Room) async {
        var input = RoomInput(room)
        input.status = status
        do throws(RoomError) {
            try await model.active?.service?.updateRoom(room, to: input)
            model.active?.sync?.localChangeMade()
            toast = "\(room.name): \(status.title)"
        } catch {
            toast = error.message
        }
        await reload()
    }

    private func delete(_ room: Room) async {
        do throws(RoomError) {
            try await model.active?.service?.deleteRoom(room.id)
            model.active?.sync?.localChangeMade()
            toast = "Raum gelöscht"
        } catch {
            toast = error.message
        }
        deletingRoom = nil
        await reload()
    }

    private func assign(_ contact: Contact) async {
        try? await model.active?.service?.assignCustomer(contact.id, to: bauakte.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }
}

/// Beteiligter: Rolle, Name und Firma, Anruf mit einem Tipp; E-Mail und Nummer im Kontextmenü.
struct PartyRow: View {
    let party: BauakteParty
    @Environment(\.openURL) private var openURL

    var body: some View {
        HStack(spacing: Space.m) {
            Avatar(name: party.company ?? party.name, isCompany: party.company != nil, size: 40)
            VStack(alignment: .leading, spacing: 2) {
                Text(party.role.title)
                    .font(Typo.labelStrong)
                    .foregroundStyle(Theme.accent)
                Text(party.name)
                    .font(Typo.body)
                    .foregroundStyle(Theme.textPrimary)
                let details = [party.company == party.name ? nil : party.company, party.phone].compactMap { $0 }
                if !details.isEmpty {
                    Text(details.joined(separator: " · "))
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                }
                if let note = party.note?.compactText {
                    Text(note).font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(2)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if let url = ContactLink.phone(party.phone) {
                Button { openURL(url) } label: { Label("\(party.name) anrufen", systemImage: "phone.fill") }
                    .buttonStyle(IconButtonStyle())
            } else if let url = ContactLink.email(party.email) {
                Button { openURL(url) } label: { Label("E-Mail an \(party.name)", systemImage: "envelope.fill") }
                    .buttonStyle(IconButtonStyle())
            }
        }
        .padding(.vertical, Space.xxs)
        .contextMenu {
            if let url = ContactLink.phone(party.phone) {
                Button("Anrufen", systemImage: "phone") { openURL(url) }
            }
            if let url = ContactLink.email(party.email) {
                Button("E-Mail schreiben", systemImage: "envelope") { openURL(url) }
            }
            if let phone = party.phone {
                Button("Nummer kopieren", systemImage: "doc.on.doc") { UIPasteboard.general.string = phone }
            }
        }
    }
}

/// Räume eines Geschosses.
private struct RoomFloorGroup: Identifiable {
    let name: String
    var rooms: [Room]
    var id: String { name }
}

/// Raum im Bearbeitungsblatt: neu (ohne Raum) oder vorhanden.
private struct RoomEditing: Identifiable {
    let id = UUID()
    var room: Room?
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
