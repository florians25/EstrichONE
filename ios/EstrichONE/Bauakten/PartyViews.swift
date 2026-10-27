import EstrichCore
import SwiftUI

/// Kontakt der Bauakte als Karte: Initialen, Name, Rolle, Firma, alle Nummern und E-Mail zum Antippen, Anschrift mit
/// Route. Hauptkontakt mit Stern. Bearbeiten, Rolle ändern, Hauptkontakt und Entfernen über Menü und Wischen.
struct PartyCard: View {
    let party: BauakteParty
    var canEdit = true
    var onEdit: () -> Void = {}
    var onRole: (PartyRole) -> Void = { _ in }
    var onPrimary: () -> Void = {}
    var onRemove: () -> Void = {}
    @Environment(\.openURL) private var openURL

    var body: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            HStack(alignment: .top, spacing: Space.m) {
                ZStack {
                    Circle().fill(party.isPrimary ? Theme.accentFill : Theme.accentSoft)
                    Text(party.initials.isEmpty ? "?" : party.initials)
                        .font(.system(size: 16, weight: .semibold, design: .rounded))
                        .foregroundStyle(party.isPrimary ? Theme.onAccent : Theme.accent)
                }
                .frame(width: 44, height: 44)
                .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 3) {
                    Text(party.name)
                        .font(Typo.headline)
                        .foregroundStyle(Theme.textPrimary)
                        .fixedSize(horizontal: false, vertical: true)
                    if let company = party.company, company != party.name {
                        Text(company).font(Typo.subhead).foregroundStyle(Theme.textSecondary).lineLimit(2)
                    }
                    HStack(spacing: Space.xs) {
                        Chip(text: party.role.title, tone: .accent)
                        if party.isPrimary { Chip(text: "Hauptkontakt", symbol: "star.fill", tone: .neutral) }
                        if party.syncState != .synced { SyncStateBadge(state: party.syncState) }
                    }
                    .padding(.top, 2)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                if canEdit {
                    Menu { menuItems } label: {
                        Image(systemName: "ellipsis")
                            .font(.body.weight(.semibold))
                            .frame(width: Size.minTap, height: Size.minTap)
                            .contentShape(Rectangle())
                    }
                    .foregroundStyle(Theme.textSecondary)
                    .accessibilityLabel("Aktionen für \(party.name)")
                }
            }
            let phones = party.allPhones
            if !phones.isEmpty || party.email != nil || party.address != nil || party.note != nil {
                VStack(spacing: 0) {
                    ForEach(Array(phones.enumerated()), id: \.offset) { index, phone in
                        if index > 0 { InsetDivider() }
                        linkRow(symbol: phone.kind == .mobile ? "iphone" : "phone", label: phone.kind.title, value: phone.number,
                                url: ContactLink.phone(phone.number))
                    }
                    if let email = party.email {
                        if !phones.isEmpty { InsetDivider() }
                        linkRow(symbol: "envelope", label: "E-Mail", value: email, url: ContactLink.email(email))
                    }
                    if let address = party.address {
                        if !phones.isEmpty || party.email != nil { InsetDivider() }
                        linkRow(symbol: "map", label: "Anschrift", value: address, url: MapsLink.url(address: address))
                    }
                    if let note = party.note {
                        if !phones.isEmpty || party.email != nil || party.address != nil { InsetDivider() }
                        HStack(alignment: .top, spacing: Space.m) {
                            Image(systemName: "text.alignleft").foregroundStyle(Theme.textTertiary).frame(width: 22)
                            Text(note).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                        .padding(.vertical, Space.s + 2)
                    }
                }
                .padding(.horizontal, Space.m)
                .background(Theme.fill.opacity(0.6), in: RoundedRectangle(cornerRadius: Radius.m, style: .continuous))
            }
        }
        .padding(.vertical, Space.xs)
        .contextMenu { if canEdit { menuItems } }
        .swipeActions(edge: .trailing) {
            if canEdit {
                Button("Entfernen", systemImage: "trash", role: .destructive, action: onRemove)
                Button("Bearbeiten", systemImage: "pencil", action: onEdit).tint(Theme.accentFill)
            }
        }
        .swipeActions(edge: .leading) {
            if canEdit, !party.isPrimary {
                Button("Hauptkontakt", systemImage: "star", action: onPrimary).tint(Theme.warning)
            }
        }
        .accessibilityIdentifier("partyCard-\(party.name)")
    }

    @ViewBuilder
    private var menuItems: some View {
        Button("Bearbeiten", systemImage: "pencil", action: onEdit)
        Menu("Rolle ändern", systemImage: "person.text.rectangle") {
            ForEach(PartyRole.allCases, id: \.self) { role in
                Button { onRole(role) } label: {
                    if role == party.role { Label(role.title, systemImage: "checkmark") } else { Text(role.title) }
                }
            }
        }
        if !party.isPrimary {
            Button("Als Hauptkontakt", systemImage: "star", action: onPrimary)
        }
        if let phone = party.allPhones.first {
            Button("Nummer kopieren", systemImage: "doc.on.doc") { UIPasteboard.general.string = phone.number }
        }
        Divider()
        Button("Aus der Bauakte entfernen", systemImage: "trash", role: .destructive, action: onRemove)
    }

    private func linkRow(symbol: String, label: String, value: String, url: URL?) -> some View {
        Button {
            if let url { openURL(url) }
        } label: {
            HStack(spacing: Space.m) {
                Image(systemName: symbol).foregroundStyle(Theme.accent).frame(width: 22)
                VStack(alignment: .leading, spacing: 1) {
                    Text(label).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                    Text(value).font(Typo.subhead).foregroundStyle(url == nil ? Theme.textPrimary : Theme.accent).lineLimit(2)
                        .multilineTextAlignment(.leading)
                }
                Spacer(minLength: 0)
            }
            .frame(minHeight: Size.minTap)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(label): \(value)")
    }
}

/// Kontakt hinzufügen oder bearbeiten: bestehenden Kontakt aus dem Kundenstamm suchen oder neu erfassen –
/// mit Dublettenprüfung gegen den Kundenstamm des Geräts. Wird offline gespeichert und per Abgleich übertragen.
struct PartyEditView: View {
    let bauakteId: UUID
    /// `nil` = neuer Kontakt.
    let party: BauakteParty?
    let done: (String?) -> Void

    enum Mode: String, CaseIterable, Identifiable {
        case existing = "Aus Kundenstamm"
        case new = "Neu erfassen"
        var id: String { rawValue }
    }

    @Environment(AppModel.self) private var model
    @State private var mode: Mode = .existing
    @State private var edit = PartyEdit()
    @State private var search = ""
    @State private var candidates: [Contact] = []
    @State private var duplicates: [Contact] = []
    @State private var linked: Contact?
    @State private var error: String?
    @State private var saving = false
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            Form {
                if party == nil {
                    Section {
                        Picker("Art", selection: $mode) {
                            ForEach(Mode.allCases) { Text($0.rawValue).tag($0) }
                        }
                        .pickerStyle(.segmented)
                        .listRowBackground(Color.clear)
                        .listRowInsets(EdgeInsets())
                    }
                }
                if mode == .existing, party == nil, linked == nil {
                    existingSection
                } else {
                    if let linked {
                        Section {
                            HStack(spacing: Space.m) {
                                Avatar(name: linked.name, isCompany: linked.isCompany, size: 40)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(linked.name).font(Typo.body)
                                    Text([linked.customerNumber, linked.addressLine].compactMap { $0 }.joined(separator: " · "))
                                        .font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1)
                                }
                                Spacer()
                                if party == nil {
                                    Button("Ändern") {
                                        self.linked = nil
                                        edit.contactId = nil
                                    }
                                    .font(Typo.subheadStrong)
                                }
                            }
                        } header: {
                            Text("Aus dem Kundenstamm")
                        } footer: {
                            Text("Name, Nummern und Anschrift kommen aus dem Kundenstamm. Eigene Angaben unten ergänzen sie für diese Bauakte.")
                        }
                    }
                    roleSection
                    detailsSection
                    if !duplicates.isEmpty, linked == nil {
                        duplicateSection
                    }
                }
                if let error {
                    Section { Text(error).foregroundStyle(Theme.danger) }
                }
            }
            .navigationTitle(party == nil ? "Kontakt hinzufügen" : "Kontakt bearbeiten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { done(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(saving || (mode == .existing && linked == nil && party == nil))
                        .accessibilityIdentifier("savePartyButton")
                }
            }
            .task { await start() }
            .task(id: search) { await loadCandidates() }
            .task(id: "\(edit.name)|\(edit.phone)|\(edit.mobile)|\(edit.email)") { await checkDuplicates() }
        }
    }

    private var existingSection: some View {
        Section {
            TextField("Name, Firma, Ort oder Nummer", text: $search)
                .focused($focused)
                .textInputAutocapitalization(.words)
                .autocorrectionDisabled()
            if candidates.isEmpty {
                Text(search.isEmpty ? "Keine Kontakte im Kundenstamm dieses Geräts." : "Nichts gefunden – „Neu erfassen“ wählen.")
                    .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
            }
            ForEach(candidates.prefix(30)) { contact in
                Button { use(contact) } label: {
                    HStack(spacing: Space.m) {
                        Avatar(name: contact.name, isCompany: contact.isCompany, size: 36)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(contact.name).font(Typo.body).foregroundStyle(Theme.textPrimary)
                            let line = [contact.customerNumber, contact.city, contact.phone].compactMap { $0 }.joined(separator: " · ")
                            if !line.isEmpty { Text(line).font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1) }
                        }
                        Spacer()
                        Image(systemName: "plus.circle").foregroundStyle(Theme.accent)
                    }
                }
            }
        } header: {
            Text("Bestehenden Kontakt suchen")
        }
    }

    private var roleSection: some View {
        Section {
            Picker("Rolle", selection: $edit.role) {
                ForEach(PartyRole.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            Toggle("Hauptkontakt der Bauakte", isOn: $edit.isPrimary)
        } footer: {
            Text("Es gibt höchstens einen Hauptkontakt – ein bisheriger verliert die Markierung.")
        }
    }

    private var detailsSection: some View {
        Group {
            Section("Person") {
                TextField(linked == nil ? "Name (Pflicht)" : "Name wie im Kundenstamm", text: $edit.name)
                    .textContentType(.name)
                    .accessibilityIdentifier("partyNameField")
                TextField("Firma", text: $edit.company).textContentType(.organizationName)
            }
            Section("Erreichbarkeit") {
                TextField("Telefon", text: $edit.phone).keyboardType(.phonePad).textContentType(.telephoneNumber)
                TextField("Mobil", text: $edit.mobile).keyboardType(.phonePad)
                TextField("E-Mail", text: $edit.email).keyboardType(.emailAddress).textInputAutocapitalization(.never)
                    .autocorrectionDisabled().textContentType(.emailAddress)
            }
            Section("Anschrift") {
                TextField("Straße und Nr.", text: $edit.street).textContentType(.streetAddressLine1)
                HStack {
                    TextField("PLZ", text: $edit.postalCode).keyboardType(.numberPad).frame(maxWidth: 90)
                    TextField("Ort", text: $edit.city)
                }
            }
            Section("Notiz") {
                TextField("z. B. erreichbar ab 7 Uhr, Schlüssel bei …", text: $edit.note, axis: .vertical).lineLimit(2...5)
            }
        }
    }

    private var duplicateSection: some View {
        Section {
            ForEach(duplicates.prefix(3)) { contact in
                Button { use(contact) } label: {
                    HStack(spacing: Space.m) {
                        Image(systemName: "person.2.badge.gearshape").foregroundStyle(Theme.warning).frame(width: 22)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(contact.name).font(Typo.body).foregroundStyle(Theme.textPrimary)
                            Text([contact.customerNumber, contact.phone, contact.email].compactMap { $0 }.joined(separator: " · "))
                                .font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1)
                        }
                        Spacer()
                        Text("Verwenden").font(Typo.subheadStrong).foregroundStyle(Theme.accent)
                    }
                }
            }
        } header: {
            Text("Bereits im Kundenstamm?")
        } footer: {
            Text("Gleicher Name, gleiche Nummer oder E-Mail. „Verwenden“ verknüpft den vorhandenen Kontakt statt einer Dublette.")
        }
    }

    private func start() async {
        if let party, let entity = try? await model.active?.service?.store.entity(.bauakteParty, party.id) {
            edit = PartyEdit(entity)
            if let id = party.contactId { linked = try? await model.active?.service?.contact(id) }
            mode = .new
        } else if party == nil {
            focused = true
        }
    }

    private func use(_ contact: Contact) {
        linked = contact
        edit.contactId = contact.id
        if edit.name.trimmingCharacters(in: .whitespaces).isEmpty || mode == .existing { edit.name = "" }
        mode = .new
        duplicates = []
    }

    private func loadCandidates() async {
        guard party == nil, let service = model.active?.service else { return }
        try? await Task.sleep(for: .milliseconds(150))
        candidates = (try? await service.partyCandidates(search: search, bauakteId: bauakteId)) ?? []
    }

    private func checkDuplicates() async {
        guard linked == nil, party == nil, let service = model.active?.service else { return }
        try? await Task.sleep(for: .milliseconds(300))
        let phone = edit.phone.isEmpty ? edit.mobile : edit.phone
        duplicates = (try? await service.similarCustomers(to: NewCustomer(name: edit.name, phone: phone, email: edit.email))) ?? []
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        do throws(BauakteError) {
            try await service.saveParty(edit, id: party?.id, bauakteId: bauakteId)
            model.active?.sync?.localChangeMade()
            done(party == nil ? "Kontakt hinzugefügt" : "Kontakt gesichert")
        } catch {
            self.error = error.message
        }
    }
}

/// Kontakt im Bearbeitungsblatt: neu (ohne Eintrag) oder vorhanden.
struct PartyEditing: Identifiable {
    let id = UUID()
    var party: BauakteParty?
}
