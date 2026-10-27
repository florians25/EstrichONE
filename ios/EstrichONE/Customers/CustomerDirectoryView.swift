import Contacts
import ContactsUI
import EstrichCore
import SwiftUI

/// Kundenkartei: Suche (Name, Ort, Telefon), Filter Firma/Privat, alphabetische Abschnitte mit Index, Wischaktionen
/// (Anrufen, Nachricht, E-Mail, Route). Offline lesbar aus der verschlüsselten Ablage; Anlage und Änderung über die
/// Synchronisation. iPad: Liste links, Kundenkarte rechts.
struct CustomerDirectoryView: View {
    enum Filter: String, CaseIterable, Identifiable {
        case all, companies, people
        var id: String { rawValue }
        var title: String {
            switch self {
            case .all: "Alle"
            case .companies: "Firmen"
            case .people: "Privat"
            }
        }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.openURL) private var openURL
    @Environment(\.shotScene) private var shotScene
    @State private var search = ""
    @State private var filter: Filter = .all
    @State private var contacts: [Contact] = []
    /// Laufende Baustellen je Kunde.
    @State private var running: [UUID: Int] = [:]
    @State private var loaded = false
    @State private var selection: UUID?
    @State private var path: [UUID] = []
    @State private var creating = false

    var body: some View {
        Group {
            if sizeClass == .regular {
                NavigationSplitView {
                    list.navigationSplitViewColumnWidth(min: 320, ideal: 380, max: 460)
                } detail: {
                    NavigationStack {
                        if let selection {
                            CustomerCardView(contactId: selection).id(selection)
                        } else {
                            ContentUnavailableView("Kunde wählen", systemImage: "person.crop.rectangle.stack",
                                                   description: Text("Links einen Kunden antippen oder neu anlegen."))
                        }
                    }
                }
                .navigationSplitViewStyle(.balanced)
            } else {
                NavigationStack(path: $path) {
                    list.navigationDestination(for: UUID.self) { CustomerCardView(contactId: $0) }
                }
            }
        }
        .sheet(isPresented: $creating) {
            CustomerEditView(contact: nil) { id in
                creating = false
                guard let id else { return }
                if sizeClass == .regular { selection = id } else { path.append(id) }
            }
        }
        .task(id: "\(search)|\(model.active?.sync?.revision ?? 0)") { await reload() }
    }

    private var filtered: [Contact] {
        contacts.filter { contact in
            switch filter {
            case .all: true
            case .companies: contact.isCompany
            case .people: !contact.isCompany
            }
        }
    }

    private var groups: [(letter: String, contacts: [Contact])] {
        let grouped = Dictionary(grouping: filtered) { Self.letter(of: $0.name) }
        return grouped.keys.sorted { $0 == "#" ? false : ($1 == "#" ? true : $0 < $1) }
            .map { ($0, (grouped[$0] ?? []).sorted { $0.name.localizedCompare($1.name) == .orderedAscending }) }
    }

    @ViewBuilder
    private var list: some View {
        ScrollViewReader { proxy in
            Group {
                if sizeClass == .regular {
                    List(selection: $selection) { rows }
                } else {
                    List { rows }
                }
            }
            .listStyle(.insetGrouped)
            .listSectionSpacing(.compact)
            .overlay(alignment: .trailing) {
                if groups.count > 4, search.isEmpty {
                    LetterIndex(letters: groups.map(\.letter)) { letter in
                        withAnimation { proxy.scrollTo("section-\(letter)", anchor: .top) }
                    }
                    .padding(.trailing, 2)
                }
            }
        }
        .navigationTitle("Kunden")
        .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Name, Ort oder Telefon")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { creating = true } label: { Label("Neuer Kunde", systemImage: "plus") }
                    .accessibilityIdentifier("newCustomerToolbarButton")
            }
        }
        .rootToolbar()
        .refreshable { await model.active?.sync?.run() }
    }

    @ViewBuilder
    private var rows: some View {
        Section {
            Picker("Filter", selection: $filter) {
                ForEach(Filter.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets())
            .accessibilityIdentifier("customerFilter")
        }
        if !loaded {
            Section {
                ForEach(0..<6, id: \.self) { _ in
                    HStack(spacing: Space.m) {
                        Circle().fill(Theme.fillStrong).frame(width: 44, height: 44)
                        VStack(alignment: .leading, spacing: Space.s) {
                            SkeletonBar(width: 160, height: 14)
                            SkeletonBar(width: 110, height: 11)
                        }
                    }
                    .padding(.vertical, Space.xs)
                }
            }
            .pulsing()
        } else if filtered.isEmpty {
            if search.isEmpty {
                ContentUnavailableView {
                    Label("Noch keine Kunden", systemImage: "person.2")
                } description: {
                    Text("Kunden erscheinen nach der Synchronisation oder lassen sich hier anlegen – auch ohne Verbindung.")
                } actions: {
                    Button { creating = true } label: { Label("Neuer Kunde", systemImage: "plus") }
                        .estrichButton(.primary, size: .regular)
                }
                .listRowBackground(Color.clear)
            } else {
                ContentUnavailableView.search(text: search)
                    .listRowBackground(Color.clear)
            }
        }
        ForEach(groups, id: \.letter) { group in
            Section {
                ForEach(group.contacts) { contact in
                    NavigationLink(value: contact.id) {
                        CustomerRow(contact: contact, running: running[contact.id] ?? 0)
                    }
                    .swipeActions(edge: .leading, allowsFullSwipe: true) {
                        if let url = ContactLink.phone(CustomerCardView.preferredPhone(contact)) {
                            Button { openURL(url) } label: { Label("Anrufen", systemImage: "phone.fill") }.tint(Theme.accentFill)
                        }
                        if let url = CustomerDirectoryView.messageURL(contact) {
                            Button { openURL(url) } label: { Label("Nachricht", systemImage: "message.fill") }.tint(Theme.textSecondary)
                        }
                    }
                    .swipeActions(edge: .trailing) {
                        if let url = ContactLink.email(contact.email) {
                            Button { openURL(url) } label: { Label("E-Mail", systemImage: "envelope.fill") }.tint(Theme.textSecondary)
                        }
                        if let url = MapsLink.url(address: contact.addressLine) {
                            Button { openURL(url) } label: { Label("Route", systemImage: "car.fill") }.tint(Theme.textTertiary)
                        }
                    }
                    .contextMenu {
                        if let url = ContactLink.phone(CustomerCardView.preferredPhone(contact)) {
                            Button("Anrufen", systemImage: "phone") { openURL(url) }
                        }
                        if let url = ContactLink.email(contact.email) {
                            Button("E-Mail schreiben", systemImage: "envelope") { openURL(url) }
                        }
                        if let url = MapsLink.url(address: contact.addressLine) {
                            Button("Route starten", systemImage: "car") { openURL(url) }
                        }
                    }
                }
            } header: {
                Text(group.letter)
            }
            .id("section-\(group.letter)")
        }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        let query = search.trimmingCharacters(in: .whitespaces)
        contacts = (try? await service.contacts(search: query.isEmpty ? nil : query)) ?? []
        let bauakten = (try? await service.bauakten()) ?? []
        var counts: [UUID: Int] = [:]
        for bauakte in bauakten where bauakte.status != .completed && bauakte.status != .cancelled {
            if let customer = bauakte.customerId { counts[customer, default: 0] += 1 }
        }
        running = counts
        loaded = true
        // Screenshot-Modus: Kundenkarte des Privatkunden öffnen (iPad: auch bei „kunden“ rechts eine Karte zeigen).
        if shotScene == "kundenkarte" || (shotScene == "kunden" && sizeClass == .regular), path.isEmpty, selection == nil,
           let first = contacts.first(where: { !$0.isCompany }) ?? contacts.first {
            if sizeClass == .regular { selection = first.id } else { path.append(first.id) }
        }
    }

    static func letter(of name: String) -> String {
        let first = name.trimmingCharacters(in: .whitespaces).folding(options: .diacriticInsensitive, locale: .current).prefix(1).uppercased()
        return first.first?.isLetter == true ? first : "#"
    }

    /// SMS an die erste Mobilnummer (sonst keine).
    static func messageURL(_ contact: Contact) -> URL? {
        guard let mobile = contact.phones.first(where: { $0.kind == .mobile })?.number else { return nil }
        let digits = mobile.filter { $0.isNumber || $0 == "+" }
        return digits.count >= 3 ? URL(string: "sms:\(digits)") : nil
    }
}

/// Listenzeile: Avatar, Name, darunter Ort und Kundennummer; rechts die Zahl laufender Baustellen.
struct CustomerRow: View {
    let contact: Contact
    var running = 0
    /// Ausgewählte Zeile (iPad-Seitenleiste): weiß auf Akzentfarbe.
    @Environment(\.backgroundProminence) private var prominence

    var body: some View {
        let selected = prominence == .increased
        HStack(spacing: Space.m) {
            Avatar(name: contact.name, isCompany: contact.isCompany, size: 44)
            VStack(alignment: .leading, spacing: 2) {
                Text(contact.name)
                    .font(Typo.headline)
                    .foregroundStyle(selected ? Color.white : Theme.textPrimary)
                    .lineLimit(2)
                let details = [contact.isCompany ? "Firma" : nil, contact.city, contact.customerNumber].compactMap { $0?.isEmpty == false ? $0 : nil }
                Text(details.isEmpty ? "Keine weiteren Angaben" : details.joined(separator: " · "))
                    .font(Typo.subhead)
                    .foregroundStyle(selected ? Color.white.opacity(0.85) : Theme.textSecondary)
                    .lineLimit(1)
                // Laufende Baustellen unter dem Namen statt als Kapsel daneben – so wird in schmalen Spalten nichts abgeschnitten.
                if running > 0 {
                    Label(running == 1 ? "1 laufende Baustelle" : "\(running) laufende Baustellen", systemImage: "building.2")
                        .labelStyle(TightLabelStyle())
                        .font(Typo.metaStrong)
                        .foregroundStyle(selected ? Color.white : Theme.accent)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: Space.s)
            SyncStateBadge(state: contact.syncState)
        }
        .padding(.vertical, Space.xs)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("customerRow-\(contact.name)")
    }
}

/// Avatar eines Kunden (ältere Aufrufer).
struct CustomerAvatar: View {
    let contact: Contact
    var size: CGFloat = 44

    var body: some View {
        Avatar(name: contact.name, isCompany: contact.isCompany, size: size)
    }
}

/// Seitlicher Buchstaben-Index zum Springen.
private struct LetterIndex: View {
    let letters: [String]
    let jump: (String) -> Void

    var body: some View {
        VStack(spacing: 1) {
            ForEach(letters, id: \.self) { letter in
                Button(letter) { jump(letter) }
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(Theme.accent)
                    .frame(width: 20, height: 16)
                    .accessibilityLabel("Zu \(letter) springen")
            }
        }
        .padding(.vertical, 6)
        .background(.ultraThinMaterial, in: Capsule())
    }
}

/// Kundenkarte wie in der Kontakte-App: großer Avatar, Name, Art und Kundennummer, runde Schnellaktionen mit
/// Beschriftung; darunter alle Nummern und E-Mail-Adressen, Anschriften mit Route, Notizen und die Bauakten des Kunden.
struct CustomerCardView: View {
    let contactId: UUID

    @Environment(AppModel.self) private var model
    @Environment(\.openURL) private var openURL
    @State private var contact: Contact?
    @State private var bauakten: [Bauakte] = []
    @State private var loaded = false
    @State private var editing = false
    @State private var creatingBauakte = false
    @State private var openedBauakte: UUID?
    @State private var exporting = false

    var body: some View {
        List {
            if let contact {
                Section {
                    header(contact)
                }
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets(top: 0, leading: 0, bottom: Space.s, trailing: 0))
                if !contact.phones.isEmpty {
                    Section("Telefon") {
                        ForEach(contact.phones) { phone in
                            if let url = ContactLink.phone(phone.number) {
                                Button { openURL(url) } label: {
                                    channelRow(title: [phone.kind.title, phone.label].compactMap { $0 }.joined(separator: " · "), value: phone.number,
                                               symbol: phone.kind == .mobile ? "iphone" : (phone.kind == .fax ? "faxmachine" : "phone"),
                                               preferred: Self.isPreferred(phone, contact))
                                }
                            }
                        }
                    }
                }
                if !contact.emails.isEmpty {
                    Section("E-Mail") {
                        ForEach(contact.emails) { email in
                            if let url = ContactLink.email(email.address) {
                                Button { openURL(url) } label: {
                                    channelRow(title: email.label ?? "E-Mail", value: email.address, symbol: "envelope",
                                               preferred: contact.preferredChannel == .email && email == contact.emails.first)
                                }
                            }
                        }
                    }
                }
                Section("Anschrift") {
                    addressRow("Anschrift", contact.addressLine)
                    if let billing = contact.billingAddress?.line { addressRow("Rechnung", billing) }
                    if let delivery = contact.deliveryAddress?.line { addressRow("Lieferung", delivery) }
                }
                if contact.website != nil || contact.notes != nil {
                    Section("Weitere Angaben") {
                        if let website = contact.website, let url = URL(string: website.hasPrefix("http") ? website : "https://\(website)") {
                            Link(destination: url) { Label(website, systemImage: "globe") }
                        }
                        if let notes = contact.notes?.compactText {
                            Label(notes, systemImage: "text.bubble").font(Typo.subhead)
                        }
                    }
                }
                Section {
                    ForEach(bauakten) { bauakte in
                        NavigationLink {
                            BauakteDetailView(bauakteId: bauakte.id)
                        } label: {
                            BauakteRow(bauakte: bauakte)
                        }
                    }
                    Button { creatingBauakte = true } label: {
                        Label("Neue Bauakte für \(contact.name)", systemImage: "folder.badge.plus")
                    }
                    .accessibilityIdentifier("newBauakteForCustomerButton")
                } header: {
                    Text("Bauakten (\(bauakten.count))")
                }
            } else if loaded {
                ContentUnavailableView("Kunde nicht auf diesem Gerät", systemImage: "person.crop.circle.badge.questionmark")
                    .listRowBackground(Color.clear)
            } else {
                Section { SkeletonCard(lines: 3) }
                    .listRowBackground(Color.clear)
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle(contact?.name ?? "Kunde")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if let contact {
                ToolbarItem(placement: .primaryAction) {
                    Menu {
                        Button("Bearbeiten", systemImage: "pencil") { editing = true }
                        Button("In Kontakte übernehmen", systemImage: "person.crop.circle.badge.plus") { exporting = true }
                        if let url = ContactLink.phone(Self.preferredPhone(contact)) {
                            Button("Anrufen", systemImage: "phone") { openURL(url) }
                        }
                    } label: {
                        Label("Aktionen", systemImage: "ellipsis.circle")
                    }
                    .accessibilityIdentifier("customerActions")
                }
            }
        }
        .sheet(isPresented: $editing) {
            if let contact {
                CustomerEditView(contact: contact) { _ in
                    editing = false
                    Task { await reload() }
                }
            }
        }
        .sheet(isPresented: $creatingBauakte) {
            NewBauakteView(initialCustomer: contact) { id, _ in
                creatingBauakte = false
                openedBauakte = id
            }
        }
        .sheet(isPresented: $exporting) {
            if let contact {
                SystemContactSheet(contact: contact) { exporting = false }
                    .ignoresSafeArea()
            }
        }
        .navigationDestination(item: $openedBauakte) { BauakteDetailView(bauakteId: $0) }
        .task(id: model.active?.sync?.revision) { await reload() }
    }

    private func header(_ contact: Contact) -> some View {
        VStack(spacing: Space.m) {
            Avatar(name: contact.name, isCompany: contact.isCompany, size: 84)
            VStack(spacing: Space.xs) {
                let prefix = [contact.salutation, contact.title].compactMap { $0 }.joined(separator: " ")
                if !prefix.isEmpty, prefix != contact.name.split(separator: " ").first.map(String.init) {
                    Text(prefix).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                }
                Text(contact.name)
                    .font(Typo.title)
                    .foregroundStyle(Theme.textPrimary)
                    .multilineTextAlignment(.center)
                    .accessibilityIdentifier("customerCardName")
                Text([contact.isCompany ? "Firma" : "Privat", contact.customerNumber.map { "Kd.-Nr. \($0)" } ?? "noch ohne Kundennummer"]
                    .joined(separator: " · "))
                    .font(Typo.subhead)
                    .foregroundStyle(Theme.textSecondary)
                if let channel = contact.preferredChannel {
                    Chip(text: "Bevorzugt: \(channel.title)", symbol: "star.fill", tone: .accent)
                        .padding(.top, 2)
                }
            }
            HStack(spacing: Space.s) {
                quickAction("Anrufen", "phone.fill", ContactLink.phone(Self.preferredPhone(contact)))
                quickAction("Nachricht", "message.fill", CustomerDirectoryView.messageURL(contact))
                quickAction("E-Mail", "envelope.fill", ContactLink.email(contact.email))
                quickAction("Route", "car.fill", MapsLink.url(address: contact.addressLine))
            }
            SyncStateBadge(state: contact.syncState)
        }
        .frame(maxWidth: .infinity)
        .padding(.top, Space.s)
    }

    /// Schnellaktion wie in der Kontakte-App; ohne Ziel ausgegraut.
    private func quickAction(_ title: String, _ symbol: String, _ url: URL?) -> some View {
        Button {
            if let url { openURL(url) }
        } label: {
            Label(title, systemImage: symbol)
        }
        .buttonStyle(ActionTileStyle())
        .disabled(url == nil)
        .accessibilityLabel(title)
    }

    private func channelRow(title: String, value: String, symbol: String, preferred: Bool) -> some View {
        HStack(spacing: Space.m) {
            Image(systemName: symbol)
                .font(.body.weight(SymbolWeight.content))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 26)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                Text(value).font(Typo.body).foregroundStyle(Theme.accent)
            }
            Spacer(minLength: 0)
            if preferred {
                Image(systemName: "star.fill").foregroundStyle(Theme.warning).accessibilityLabel("Bevorzugt")
            }
        }
        .contentShape(Rectangle())
    }

    @ViewBuilder
    private func addressRow(_ title: String, _ value: String?) -> some View {
        HStack(spacing: Space.m) {
            Image(systemName: "mappin.and.ellipse")
                .font(.body.weight(SymbolWeight.content))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 26)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                Text(value ?? "Keine Anschrift hinterlegt").font(Typo.body)
                    .foregroundStyle(value == nil ? Theme.textSecondary : Theme.textPrimary)
            }
            Spacer(minLength: 0)
            if let url = MapsLink.url(address: value) {
                Link(destination: url) { Label("Route", systemImage: "arrow.triangle.turn.up.right.diamond.fill") }
                    .buttonStyle(IconButtonStyle())
            }
        }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        contact = try? await service.contact(contactId)
        bauakten = ((try? await service.bauakten(ofCustomer: contactId)) ?? [])
            .sorted { ($0.status.sortRank, $0.number ?? "") < ($1.status.sortRank, $1.number ?? "") }
        loaded = true
    }

    static func preferredPhone(_ contact: Contact) -> String? {
        switch contact.preferredChannel {
        case .mobile: contact.phones.first { $0.kind == .mobile }?.number ?? contact.phone
        case .phone: contact.phones.first { $0.kind != .mobile && $0.kind != .fax }?.number ?? contact.phone
        default: contact.phone
        }
    }

    static func isPreferred(_ phone: ContactPhone, _ contact: Contact) -> Bool {
        guard contact.preferredChannel == .phone || contact.preferredChannel == .mobile else { return false }
        return phone.number == preferredPhone(contact)
    }
}

/// Kunde anlegen oder bearbeiten: Art, Name, Anrede, Anschrift, beliebig viele Nummern und E-Mail-Adressen mit Art,
/// bevorzugter Kontaktweg, Website, Notiz. Neu angelegt: Dublettenprüfung lokal und – mit Verbindung – auf dem Server.
struct CustomerEditView: View {
    let contact: Contact?
    let onDone: (UUID?) -> Void

    @Environment(AppModel.self) private var model
    @State private var edit = CustomerEdit()
    @State private var loaded = false
    @State private var similar: [Contact] = []
    @State private var error: String?
    @State private var saving = false

    var body: some View {
        NavigationStack {
            Form {
                if let error {
                    Section { StatusMessage(kind: .error, text: error).accessibilityIdentifier("customerEditError") }
                }
                Section {
                    Picker("Art", selection: $edit.isCompany) {
                        Text("Privat").tag(false)
                        Text("Firma").tag(true)
                    }
                    .pickerStyle(.segmented)
                    TextField(edit.isCompany ? "Firmenname" : "Name", text: $edit.name)
                        .textContentType(edit.isCompany ? .organizationName : .name)
                        .accessibilityIdentifier("customerEditName")
                    if !edit.isCompany {
                        HStack {
                            TextField("Anrede", text: $edit.salutation)
                            TextField("Titel", text: $edit.title)
                        }
                    }
                }
                if contact == nil {
                    SimilarCustomersSection(similar: similar) { existing in onDone(existing.id) }
                }
                Section("Anschrift") {
                    TextField("Straße und Hausnummer", text: $edit.street).textContentType(.fullStreetAddress)
                    HStack {
                        TextField("PLZ", text: $edit.postalCode).keyboardType(.numberPad).frame(maxWidth: 90)
                        TextField("Ort", text: $edit.city).textContentType(.addressCity)
                    }
                }
                Section {
                    ForEach($edit.phones) { $phone in
                        HStack {
                            Picker("Art", selection: $phone.kind) {
                                ForEach(ContactPhoneKind.allCases) { Text($0.title).tag($0) }
                            }
                            .labelsHidden()
                            .fixedSize()
                            TextField("Nummer", text: $phone.number).keyboardType(.phonePad).textContentType(.telephoneNumber)
                        }
                    }
                    .onDelete { edit.phones.remove(atOffsets: $0) }
                    Button { edit.phones.append(ContactPhone(kind: edit.phones.isEmpty ? .mobile : .landline, number: "")) } label: {
                        Label("Telefonnummer hinzufügen", systemImage: "plus")
                    }
                    .accessibilityIdentifier("addPhoneButton")
                } header: {
                    Text("Telefon")
                }
                Section {
                    ForEach($edit.emails) { $email in
                        TextField("E-Mail-Adresse", text: $email.address)
                            .keyboardType(.emailAddress).textContentType(.emailAddress).textInputAutocapitalization(.never)
                    }
                    .onDelete { edit.emails.remove(atOffsets: $0) }
                    Button { edit.emails.append(ContactEmail(address: "")) } label: {
                        Label("E-Mail-Adresse hinzufügen", systemImage: "plus")
                    }
                } header: {
                    Text("E-Mail")
                }
                Section("Weitere Angaben") {
                    Picker("Bevorzugter Kontaktweg", selection: $edit.preferredChannel) {
                        Text("–").tag(ContactChannel?.none)
                        ForEach(ContactChannel.allCases) { Text($0.title).tag(Optional($0)) }
                    }
                    TextField("Website", text: $edit.website).keyboardType(.URL).textInputAutocapitalization(.never)
                    TextField("Notiz", text: $edit.notes, axis: .vertical).lineLimit(2...6)
                }
            }
            .navigationTitle(contact == nil ? "Neuer Kunde" : "Kunde bearbeiten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { onDone(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }.disabled(saving)
                        .accessibilityIdentifier("saveCustomerButton")
                }
            }
            .onAppear {
                guard !loaded else { return }
                loaded = true
                if let contact { edit = CustomerEdit(contact) }
            }
            .task(id: "\(edit.name)|\(edit.phones.first?.number ?? "")|\(edit.emails.first?.address ?? "")|\(edit.city)") {
                await checkDuplicates()
            }
        }
    }

    private func checkDuplicates() async {
        guard contact == nil, let active = model.active else { return }
        try? await Task.sleep(for: .milliseconds(300))
        guard !Task.isCancelled else { return }
        let input = edit.asNewCustomer
        let local = (try? await active.service?.similarCustomers(to: input)) ?? []
        similar = local
        guard input.name.trimmingCharacters(in: .whitespaces).count >= 3,
              let remote = try? await active.client.contactDuplicates(of: input), !Task.isCancelled else { return }
        similar = CustomerChooser.merged(local, remote)
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        do throws(BauakteError) {
            let id = try await service.saveCustomer(edit, id: contact?.id)
            model.active?.sync?.localChangeMade()
            onDone(id)
        } catch {
            self.error = error.message
        }
    }
}

/// Kunden in die Kontakte-App übernehmen (Systemdialog „Neuer Kontakt“, vorausgefüllt).
struct SystemContactSheet: UIViewControllerRepresentable {
    let contact: Contact
    let onDone: () -> Void

    func makeUIViewController(context: Context) -> UINavigationController {
        let controller = CNContactViewController(forNewContact: Self.makeContact(contact))
        controller.delegate = context.coordinator
        return UINavigationController(rootViewController: controller)
    }

    func updateUIViewController(_ controller: UINavigationController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(onDone: onDone) }

    final class Coordinator: NSObject, CNContactViewControllerDelegate, @unchecked Sendable {
        let onDone: () -> Void
        init(onDone: @escaping () -> Void) { self.onDone = onDone }

        nonisolated func contactViewController(_ viewController: CNContactViewController, didCompleteWith contact: CNContact?) {
            MainActor.assumeIsolated { self.onDone() }
        }
    }

    static func makeContact(_ contact: Contact) -> CNMutableContact {
        let result = CNMutableContact()
        if contact.isCompany {
            result.organizationName = contact.name
            result.contactType = .organization
        } else {
            let parts = contact.name.split(separator: " ")
            result.givenName = parts.dropLast().joined(separator: " ")
            result.familyName = parts.last.map(String.init) ?? contact.name
            result.namePrefix = [contact.salutation, contact.title].compactMap { $0 }.joined(separator: " ")
        }
        result.phoneNumbers = contact.phones.map { phone in
            let label = switch phone.kind {
            case .mobile: CNLabelPhoneNumberMobile
            case .fax: CNLabelPhoneNumberWorkFax
            case .office: CNLabelWork
            case .private: CNLabelHome
            case .landline: CNLabelPhoneNumberMain
            }
            return CNLabeledValue(label: label, value: CNPhoneNumber(stringValue: phone.number))
        }
        result.emailAddresses = contact.emails.map { CNLabeledValue(label: CNLabelWork, value: $0.address as NSString) }
        if contact.addressLine != nil {
            let address = CNMutablePostalAddress()
            address.street = contact.street ?? ""
            address.postalCode = contact.postalCode ?? ""
            address.city = contact.city ?? ""
            address.country = "Deutschland"
            result.postalAddresses = [CNLabeledValue(label: CNLabelWork, value: address)]
        }
        if let website = contact.website {
            result.urlAddresses = [CNLabeledValue(label: CNLabelWork, value: website as NSString)]
        }
        return result
    }
}
