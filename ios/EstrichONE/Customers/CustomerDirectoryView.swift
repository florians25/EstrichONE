import Contacts
import ContactsUI
import EstrichCore
import SwiftUI

/// Kundenkartei: Suche (Name, Ort, Telefon), Filter Firma/Privat, alphabetischer Index, Wischaktionen (Anrufen,
/// Nachricht, E-Mail, Route). Offline lesbar aus der verschlüsselten Ablage; Anlage und Änderung über die Synchronisation.
/// iPad: Liste links, Kundenkarte rechts.
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
    @State private var selection: UUID?
    @State private var path: [UUID] = []
    @State private var creating = false

    var body: some View {
        Group {
            if sizeClass == .regular {
                NavigationSplitView {
                    list.navigationSplitViewColumnWidth(min: 300, ideal: 360, max: 440)
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
        return grouped.keys.sorted { $0 == "#" ? false : ($1 == "#" ? true : $0 < $1) }.map { ($0, grouped[$0] ?? []) }
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
            .listStyle(.plain)
            .overlay(alignment: .trailing) {
                if groups.count > 3, search.isEmpty {
                    LetterIndex(letters: groups.map(\.letter)) { letter in
                        withAnimation { proxy.scrollTo("section-\(letter)", anchor: .top) }
                    }
                    .padding(.trailing, 2)
                }
            }
        }
        .navigationTitle("Kunden")
        .searchable(text: $search, prompt: "Name, Ort oder Telefon")
        .navigationBarTitleDisplayMode(.large)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { creating = true } label: { Label("Neuer Kunde", systemImage: "person.crop.circle.badge.plus") }
                    .accessibilityIdentifier("newCustomerToolbarButton")
            }
        }
        .refreshable { await model.active?.sync?.run() }
    }

    @ViewBuilder
    private var rows: some View {
        Picker("Filter", selection: $filter) {
            ForEach(Filter.allCases) { Text($0.title).tag($0) }
        }
        .pickerStyle(.segmented)
        .listRowSeparator(.hidden)
        .listRowBackground(Color.clear)
        .accessibilityIdentifier("customerFilter")
        if filtered.isEmpty {
            ContentUnavailableView(search.isEmpty ? "Noch keine Kunden auf diesem Gerät" : "Kein Kunde gefunden",
                                   systemImage: "person.2",
                                   description: Text(search.isEmpty ? "Kunden erscheinen nach der Synchronisation oder lassen sich hier anlegen." : "Andere Schreibweise versuchen."))
        }
        ForEach(groups, id: \.letter) { group in
            Section {
                ForEach(group.contacts) { contact in
                    NavigationLink(value: contact.id) {
                        CustomerRow(contact: contact)
                    }
                    .swipeActions(edge: .leading, allowsFullSwipe: true) {
                        if let url = ContactLink.phone(contact.phone) {
                            Button { openURL(url) } label: { Label("Anrufen", systemImage: "phone.fill") }.tint(Theme.success)
                        }
                        if let url = Self.messageURL(contact) {
                            Button { openURL(url) } label: { Label("Nachricht", systemImage: "message.fill") }.tint(Theme.navyLight)
                        }
                    }
                    .swipeActions(edge: .trailing) {
                        if let url = ContactLink.email(contact.email) {
                            Button { openURL(url) } label: { Label("E-Mail", systemImage: "envelope.fill") }.tint(Theme.navy)
                        }
                        if let url = MapsLink.url(address: contact.addressLine) {
                            Button { openURL(url) } label: { Label("Route", systemImage: "car.fill") }.tint(Theme.accentPressed)
                        }
                    }
                }
            } header: {
                Text(group.letter).font(.subheadline.weight(.bold))
            }
            .id("section-\(group.letter)")
        }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        let query = search.trimmingCharacters(in: .whitespaces)
        contacts = (try? await service.contacts(search: query.isEmpty ? nil : query)) ?? []
        // Screenshot-Modus: Kundenkarte des Privatkunden öffnen.
        if shotScene == "kundenkarte", path.isEmpty, selection == nil,
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

/// Listenzeile: Initialen, Name, darunter Ort · Kundennummer · erste Nummer; Firma als kleines Badge.
struct CustomerRow: View {
    let contact: Contact

    var body: some View {
        HStack(spacing: 12) {
            CustomerAvatar(contact: contact, size: 40)
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text(contact.name).font(.body.weight(.semibold)).lineLimit(1)
                    if contact.isCompany {
                        Text("Firma").font(.caption2.weight(.bold)).foregroundStyle(Theme.navyText)
                            .padding(.horizontal, 6).padding(.vertical, 1)
                            .background(Theme.navyBackground, in: Capsule())
                    }
                }
                let details = [contact.city, contact.customerNumber, contact.phone].compactMap { $0?.isEmpty == false ? $0 : nil }
                Text(details.isEmpty ? "Keine weiteren Angaben" : details.joined(separator: " · "))
                    .font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(1)
            }
            Spacer(minLength: 0)
            SyncStateBadge(state: contact.syncState)
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("customerRow-\(contact.name)")
    }
}

struct CustomerAvatar: View {
    let contact: Contact
    var size: CGFloat = 44

    var body: some View {
        ZStack {
            Circle().fill(LinearGradient(colors: [Theme.navyLight, Theme.navy], startPoint: .topLeading, endPoint: .bottomTrailing))
            if contact.isCompany {
                Image(systemName: "building.2.fill").font(.system(size: size * 0.4)).foregroundStyle(.white)
            } else {
                Text(LocalUser.initials(of: contact.name)).font(.system(size: size * 0.38, weight: .semibold)).foregroundStyle(.white)
            }
        }
        .frame(width: size, height: size)
        .accessibilityHidden(true)
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
                    .foregroundStyle(Theme.actionPrimary)
                    .frame(width: 20, height: 16)
                    .accessibilityLabel("Zu \(letter) springen")
            }
        }
        .padding(.vertical, 6)
        .background(.ultraThinMaterial, in: Capsule())
    }
}

/// Kundenkarte im Visitenkarten-Stil: Avatar, Name, Art und Kundennummer, bevorzugter Kontaktweg, runde Schnellaktionen;
/// darunter alle Nummern und E-Mail-Adressen mit Art, Anschriften mit Route, Notizen und die Bauakten des Kunden.
struct CustomerCardView: View {
    let contactId: UUID

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var contact: Contact?
    @State private var bauakten: [Bauakte] = []
    @State private var editing = false
    @State private var creatingBauakte = false
    @State private var openedBauakte: UUID?
    @State private var exporting = false

    var body: some View {
        List {
            if let contact {
                Section { card(contact) }
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 4, leading: 0, bottom: 8, trailing: 0))
                if !contact.phones.isEmpty {
                    Section("Telefon") {
                        ForEach(contact.phones) { phone in
                            if let url = ContactLink.phone(phone.number) {
                                Link(destination: url) {
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
                                Link(destination: url) {
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
                        if let notes = contact.notes {
                            Text(notes.compactText ?? "").font(.subheadline)
                        }
                    }
                }
                Section {
                    if bauakten.isEmpty {
                        Text("Noch keine Bauakte auf diesem Gerät.").foregroundStyle(Theme.textSecondary)
                    }
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
            } else {
                ContentUnavailableView("Kunde nicht auf diesem Gerät", systemImage: "person.crop.circle.badge.questionmark")
            }
        }
        .navigationTitle(contact?.name ?? "Kunde")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if let contact {
                ToolbarItem(placement: .primaryAction) {
                    Menu {
                        Button("Bearbeiten", systemImage: "pencil") { editing = true }
                        Button("In Kontakte übernehmen", systemImage: "person.crop.circle.badge.plus") { exporting = true }
                        if let url = ContactLink.phone(contact.phone) {
                            Link(destination: url) { Label("Anrufen", systemImage: "phone") }
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

    private func card(_ contact: Contact) -> some View {
        let shape = RoundedRectangle(cornerRadius: 24, style: .continuous)
        return VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 14) {
                CustomerAvatar(contact: contact, size: 64)
                    .overlay(Circle().strokeBorder(.white.opacity(0.6), lineWidth: 2))
                VStack(alignment: .leading, spacing: 3) {
                    let prefix = [contact.salutation, contact.title].compactMap { $0 }.joined(separator: " ")
                    if !prefix.isEmpty {
                        Text(prefix).font(.caption).foregroundStyle(.white.opacity(0.75))
                    }
                    Text(contact.name).font(.title2.weight(.bold)).foregroundStyle(.white)
                        .multilineTextAlignment(.leading)
                        .accessibilityIdentifier("customerCardName")
                    Text([contact.isCompany ? "Firma" : "Privat", contact.customerNumber.map { "Kd.-Nr. \($0)" } ?? "noch ohne Kundennummer"]
                        .joined(separator: " · "))
                        .font(.footnote).foregroundStyle(.white.opacity(0.8))
                }
            }
            if let channel = contact.preferredChannel {
                Label("Bevorzugt: \(channel.title)", systemImage: "star.fill")
                    .labelStyle(TightLabelStyle())
                    .font(.caption.weight(.semibold)).foregroundStyle(.white)
                    .padding(.horizontal, 10).padding(.vertical, 4)
                    .background(.white.opacity(0.16), in: Capsule())
            }
            HStack(spacing: 12) {
                round("Anrufen", "phone.fill", ContactLink.phone(Self.preferredPhone(contact)))
                round("Nachricht", "message.fill", CustomerDirectoryView.messageURL(contact))
                round("E-Mail", "envelope.fill", ContactLink.email(contact.email))
                round("Route", "car.fill", MapsLink.url(address: contact.addressLine))
                Spacer(minLength: 0)
                SyncStateBadge(state: contact.syncState)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(LinearGradient(colors: [Theme.navy, Theme.navyLight], startPoint: .topLeading, endPoint: .bottomTrailing), in: shape)
        .shadow(color: Theme.navy.opacity(0.18), radius: 12, y: 6)
    }

    @ViewBuilder
    private func round(_ title: String, _ symbol: String, _ url: URL?) -> some View {
        if let url {
            Link(destination: url) {
                Image(systemName: symbol)
                    .font(.body.weight(.semibold)).foregroundStyle(.white)
                    .frame(width: 44, height: 44)
                    .background(.white.opacity(0.16), in: Circle())
                    .overlay(Circle().strokeBorder(.white.opacity(0.25)))
            }
            .accessibilityLabel(title)
        }
    }

    private func channelRow(title: String, value: String, symbol: String, preferred: Bool) -> some View {
        HStack(spacing: 12) {
            Image(systemName: symbol).foregroundStyle(Theme.navyText).frame(width: 28)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(.caption).foregroundStyle(Theme.textSecondary)
                Text(value).font(.body).foregroundStyle(Theme.actionPrimary)
            }
            Spacer(minLength: 0)
            if preferred {
                Image(systemName: "star.fill").foregroundStyle(Theme.hint).accessibilityLabel("Bevorzugt")
            }
        }
    }

    @ViewBuilder
    private func addressRow(_ title: String, _ value: String?) -> some View {
        HStack(spacing: 12) {
            Image(systemName: "mappin.and.ellipse").foregroundStyle(Theme.navyText).frame(width: 28)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(.caption).foregroundStyle(Theme.textSecondary)
                Text(value ?? "Keine Anschrift hinterlegt").font(.body)
                    .foregroundStyle(value == nil ? Theme.textSecondary : Color.primary)
            }
            Spacer(minLength: 0)
            if let url = MapsLink.url(address: value) {
                Link(destination: url) { Label("Route", systemImage: "car.fill") }
                    .buttonStyle(IconButtonStyle())
            }
        }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        contact = try? await service.contact(contactId)
        bauakten = (try? await service.bauakten(ofCustomer: contactId)) ?? []
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
