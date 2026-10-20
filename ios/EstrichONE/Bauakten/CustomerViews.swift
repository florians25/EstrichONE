import EstrichCore
import SwiftUI

/// Eingabefelder für einen neuen Kunden (Zeilen eines Formularabschnitts).
struct CustomerFields: View {
    @Binding var customer: NewCustomer
    /// Adresse der Baustelle, falls schon erfasst – oft wohnt der Bauherr dort.
    var siteAddress: (street: String?, postalCode: String?, city: String?)?

    var body: some View {
        Picker("Art", selection: $customer.isCompany) {
            Text("Privatperson").tag(false)
            Text("Firma").tag(true)
        }
        .pickerStyle(.segmented)
        .accessibilityIdentifier("customerKindPicker")
        TextField(customer.isCompany ? "Firmenname *" : "Vor- und Nachname *", text: $customer.name)
            .textContentType(customer.isCompany ? .organizationName : .name)
            .submitLabel(.next)
            .accessibilityIdentifier("customerNameField")
        TextField("Telefon", text: text(\.phone))
            .keyboardType(.phonePad)
            .textContentType(.telephoneNumber)
            .accessibilityIdentifier("customerPhoneField")
        TextField("E-Mail", text: text(\.email))
            .keyboardType(.emailAddress)
            .textContentType(.emailAddress)
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .accessibilityIdentifier("customerEmailField")
        TextField("Straße, Nr.", text: text(\.street))
            .textContentType(.fullStreetAddress)
        HStack {
            TextField("PLZ", text: text(\.postalCode))
                .keyboardType(.numberPad)
                .textContentType(.postalCode)
                .frame(maxWidth: 90)
            TextField("Ort", text: text(\.city))
                .textContentType(.addressCity)
        }
        if let siteAddress, siteAddress.street != nil || siteAddress.city != nil, customer.street == nil, customer.city == nil {
            Button {
                customer.street = siteAddress.street
                customer.postalCode = siteAddress.postalCode
                customer.city = siteAddress.city
            } label: {
                Label("Adresse der Baustelle übernehmen", systemImage: "arrow.down.doc")
                    .font(.subheadline)
            }
        }
    }

    private func text(_ keyPath: WritableKeyPath<NewCustomer, String?>) -> Binding<String> {
        Binding(get: { customer[keyPath: keyPath] ?? "" }, set: { customer[keyPath: keyPath] = $0.isEmpty ? nil : $0 })
    }
}

/// Kurzdarstellung eines Kunden mit Anruf- und E-Mail-Schaltflächen.
struct CustomerSummary: View {
    let contact: Contact
    var showsActions = true

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: contact.isCompany ? "building.2.fill" : "person.fill")
                .font(.subheadline)
                .foregroundStyle(Theme.navyText)
                .frame(width: 34, height: 34)
                .background(Theme.navyBackground, in: Circle())
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(contact.name)
                    .font(.body.weight(.semibold))
                    .accessibilityIdentifier("customerName")
                let details = [contact.customerNumber, contact.phone, contact.addressLine].compactMap { $0 }
                if !details.isEmpty {
                    Text(details.joined(separator: " · "))
                        .font(.caption)
                        .foregroundStyle(Theme.textSecondary)
                        .lineLimit(2)
                }
                SyncStateBadge(state: contact.syncState)
            }
            Spacer(minLength: 4)
            if showsActions {
                if let url = ContactLink.phone(contact.phone) {
                    Link(destination: url) { Label("Kunde anrufen", systemImage: "phone.fill") }
                        .buttonStyle(IconButtonStyle(rank: .accent))
                        .accessibilityIdentifier("callCustomerButton")
                }
                if let url = ContactLink.email(contact.email) {
                    Link(destination: url) { Label("E-Mail an Kunden", systemImage: "envelope.fill") }
                        .buttonStyle(IconButtonStyle())
                }
            }
        }
    }
}

/// Kundensuche über die synchronisierten Kontakte (offline). Ohne Treffer: neuen Kunden anlegen.
/// Mit `onNew` übernimmt der Aufrufer die Anlage (z. B. als Teil der Schnellanlage), sonst wird der Kunde hier
/// sofort lokal angelegt und ausgewählt.
struct CustomerPickerView: View {
    let onSelect: (Contact) -> Void
    var onNew: ((String) -> Void)?

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""
    @State private var contacts: [Contact] = []
    @State private var creating = false

    var body: some View {
        NavigationStack {
            List {
                Section {
                    Button {
                        if let onNew {
                            onNew(search.trimmingCharacters(in: .whitespaces))
                        } else {
                            creating = true
                        }
                    } label: {
                        Label(search.isEmpty ? "Neuen Kunden anlegen" : "„\(search.trimmingCharacters(in: .whitespaces))“ als neuen Kunden anlegen",
                              systemImage: "person.crop.circle.badge.plus")
                    }
                    .accessibilityIdentifier("createCustomerFromSearch")
                }
                Section {
                    ForEach(contacts) { contact in
                        Button {
                            onSelect(contact)
                        } label: {
                            CustomerSummary(contact: contact, showsActions: false)
                                .foregroundStyle(.primary)
                        }
                        .accessibilityIdentifier("customerResult")
                    }
                } footer: {
                    if contacts.isEmpty {
                        Text(search.isEmpty ? "Auf diesem Gerät sind noch keine Kunden vorhanden." : "Kein passender Kunde auf diesem Gerät.")
                    }
                }
            }
            .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Name, Kundennummer, Ort oder Telefon")
            .navigationTitle("Kunde")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } } }
            .task(id: "\(search)|\(model.active?.sync?.revision ?? 0)") {
                contacts = (try? await model.active?.service?.contacts(search: search)) ?? []
            }
            .navigationDestination(isPresented: $creating) {
                NewCustomerForm(initialName: search.trimmingCharacters(in: .whitespaces)) { contact in
                    onSelect(contact)
                }
            }
        }
    }
}

/// Neuer Kunde als eigenes Formular (z. B. nachträglich aus der Bauakte), sofort lokal gespeichert.
struct NewCustomerForm: View {
    let initialName: String
    let onCreated: (Contact) -> Void

    @Environment(AppModel.self) private var model
    @State private var customer = NewCustomer()
    @State private var similar: [Contact] = []
    @State private var errorMessage: String?
    @State private var saving = false

    var body: some View {
        Form {
            Section {
                CustomerFields(customer: $customer)
            } footer: {
                Text("Die Kundennummer vergibt das Büro. Der Kunde wird mit der nächsten Synchronisation übertragen.")
            }
            SimilarCustomersSection(similar: similar) { onCreated($0) }
            if let errorMessage {
                Section { StatusMessage(kind: .error, text: errorMessage) }
            }
        }
        .navigationTitle("Neuer Kunde")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("Sichern") { Task { await save() } }
                    .disabled(saving || customer.name.trimmingCharacters(in: .whitespaces).isEmpty)
                    .accessibilityIdentifier("saveCustomerButton")
            }
        }
        .onAppear { if customer.name.isEmpty { customer.name = initialName } }
        .task(id: "\(customer.name)|\(customer.phone ?? "")|\(customer.email ?? "")") {
            try? await Task.sleep(for: .milliseconds(300))
            guard !Task.isCancelled else { return }
            similar = (try? await model.active?.service?.similarCustomers(to: customer)) ?? []
        }
        .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
    }

    private func save() async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        do throws(BauakteError) {
            let id = try await service.createCustomer(customer)
            model.active?.sync?.localChangeMade()
            if let contact = try? await service.contact(id) { onCreated(contact) }
        } catch {
            errorMessage = error.message
        }
    }
}

/// Hinweis auf mögliche Dubletten mit „Diesen verwenden“.
struct SimilarCustomersSection: View {
    let similar: [Contact]
    let use: (Contact) -> Void

    var body: some View {
        if !similar.isEmpty {
            Section {
                ForEach(similar.prefix(3)) { contact in
                    Button {
                        use(contact)
                    } label: {
                        HStack {
                            CustomerSummary(contact: contact, showsActions: false)
                                .foregroundStyle(.primary)
                            Text("Verwenden").font(.subheadline.weight(.semibold)).foregroundStyle(Theme.actionPrimary)
                        }
                    }
                    .accessibilityIdentifier("useSimilarCustomer")
                }
            } header: {
                Label("Bereits vorhanden?", systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(Theme.hint)
            }
        }
    }
}
