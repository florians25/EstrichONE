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
    @Environment(\.shotScene) private var shotScene
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
    /// Screenshot-Modus „protokoll“: Protokoll der Erstbegehung geöffnet.
    @State private var openedInspection: UUID?
    @State private var deletingRoom: Room?
    @State private var editingParty: PartyEditing?
    @State private var removingParty: BauakteParty?

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
        .sheet(item: $editingParty) { editing in
            PartyEditView(bauakteId: bauakte.id, party: editing.party) { message in
                editingParty = nil
                if let message { toast = message }
                Task { await reload() }
            }
        }
        .confirmationDialog("Kontakt entfernen?", isPresented: Binding { removingParty != nil } set: { if !$0 { removingParty = nil } },
                            titleVisibility: .visible, presenting: removingParty) { party in
            Button("„\(party.name)“ entfernen", role: .destructive) { Task { await remove(party) } }
        } message: { _ in
            Text("Der Kontakt wird aus dieser Bauakte entfernt. Im Kundenstamm bleibt er erhalten.")
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
        .navigationDestination(item: $openedInspection) { id in
            if let inspection = content.inspections.first(where: { $0.id == id }) {
                InspectionProtocolView(inspection: inspection, bauakte: bauakte,
                                       performerName: inspection.performedByUserId.flatMap { content.names[$0] })
            }
        }
        .filePreview($previewing)
        .toast($toast)
        .task {
            // Screenshot-Modus „raum“: einen Raum zum Bearbeiten öffnen; „protokoll“: Protokoll der Erstbegehung.
            if shotScene == "raum", area == .rooms, editingRoom == nil, let room = content.rooms.first(where: { $0.notes != nil }) ?? content.rooms.first {
                try? await Task.sleep(for: .milliseconds(400))
                editingRoom = RoomEditing(room: room)
            }
            if shotScene == "kontakt-neu", area == .contacts, editingParty == nil {
                try? await Task.sleep(for: .milliseconds(400))
                editingParty = PartyEditing()
            }
            if shotScene == "protokoll", area == .protocols, openedInspection == nil, let inspection = content.latestInitialInspection {
                try? await Task.sleep(for: .milliseconds(400))
                openedInspection = inspection.id
            }
        }
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
            Menu {
                Button("Kontakt hinzufügen", systemImage: "person.badge.plus") { editingParty = PartyEditing() }
                    .accessibilityIdentifier("addPartyButton")
                Button(content.customer == nil ? "Kunde zuordnen" : "Kunde ändern", systemImage: "person.crop.circle.badge.plus") {
                    choosingCustomer = true
                }
            } label: { Label("Hinzufügen", systemImage: "plus") }
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
                // Abstand zum Zeilenrand, damit die Rundung der Fotos zur Rundung der Gruppe passt (nichts wirkt abgeschnitten).
                .listRowInsets(EdgeInsets(top: Space.m, leading: Space.m, bottom: Space.m, trailing: Space.m))
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
        partySections
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

    // MARK: Kontakte der Bauakte

    /// Hauptkontakt zuerst, dann nach Rolle gruppiert (Bauherr, Ansprechpartner vor Ort, Bauleiter, Architekt …).
    @ViewBuilder
    private var partySections: some View {
        let parties = content.parties
        if parties.isEmpty {
            Section {
                EmptyStateView(symbol: "person.2", title: "Noch keine Kontakte",
                               message: "Bauleiter, Architekt, Ansprechpartner vor Ort – mit Nummer und Anschrift griffbereit auf der Baustelle.",
                               actionTitle: "Kontakt hinzufügen", actionSymbol: "person.badge.plus") { editingParty = PartyEditing() }
            } header: {
                Text("Kontakte")
            }
        } else {
            let groups = PartyRole.allCases.compactMap { role -> (PartyRole, [BauakteParty])? in
                let members = parties.filter { $0.role == role && !$0.isPrimary }
                return members.isEmpty ? nil : (role, members)
            }
            if let primary = parties.first(where: \.isPrimary) {
                Section {
                    partyCard(primary)
                } header: {
                    Text("Hauptkontakt")
                }
            }
            ForEach(groups, id: \.0) { role, members in
                Section {
                    ForEach(members) { partyCard($0) }
                } header: {
                    Text(members.count == 1 ? role.title : "\(role.title) (\(members.count))")
                }
            }
            Section {
                Button { editingParty = PartyEditing() } label: { Label("Kontakt hinzufügen", systemImage: "person.badge.plus") }
            }
        }
    }

    private func partyCard(_ party: BauakteParty) -> some View {
        PartyCard(party: party,
                  onEdit: { editingParty = PartyEditing(party: party) },
                  onRole: { role in Task { await change(party) { $0.role = role } } },
                  onPrimary: { Task { await change(party) { $0.isPrimary = true } } },
                  onRemove: { removingParty = party })
    }

    private func change(_ party: BauakteParty, _ update: (inout PartyEdit) -> Void) async {
        guard let service = model.active?.service, let entity = try? await service.store.entity(.bauakteParty, party.id) else { return }
        var edit = PartyEdit(entity)
        update(&edit)
        do throws(BauakteError) {
            try await service.saveParty(edit, id: party.id, bauakteId: bauakte.id)
            model.active?.sync?.localChangeMade()
            toast = edit.isPrimary && !party.isPrimary ? "\(party.name) ist Hauptkontakt" : "\(party.name): \(edit.role.title)"
        } catch {
            toast = error.message
        }
        await reload()
    }

    private func remove(_ party: BauakteParty) async {
        do throws(BauakteError) {
            try await model.active?.service?.deleteParty(party.id)
            model.active?.sync?.localChangeMade()
            toast = "Kontakt entfernt"
        } catch {
            toast = error.message
        }
        removingParty = nil
        await reload()
    }

    private func assign(_ contact: Contact) async {
        try? await model.active?.service?.assignCustomer(contact.id, to: bauakte.id)
        model.active?.sync?.localChangeMade()
        await reload()
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
