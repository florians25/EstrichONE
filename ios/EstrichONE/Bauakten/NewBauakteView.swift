import CoreLocation
import EstrichCore
import SwiftUI

/// Schnellanlage auf der Baustelle (09): ein Bildschirm, nur die Bezeichnung ist Pflicht, offline möglich.
/// Der Kunde wird direkt hier gewählt oder neu erfasst; ein neuer Kunde wird mit der Bauakte übertragen und verknüpft.
struct NewBauakteView: View {
    /// Neue Bauakte angelegt; `true`, wenn direkt fotografiert werden soll.
    let onCreated: (UUID, Bool) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var input = NewBauakte(title: "")
    @State private var customer: Contact?
    @State private var newCustomer: NewCustomer?
    @State private var similarCustomers: [Contact] = []
    @State private var recentCustomers: [Contact] = []
    @State private var showsCustomerSearch = false
    @State private var showsDescription = false
    @State private var locating = false
    @State private var locationHint: String?
    @State private var similar: [Bauakte] = []
    @State private var errorMessage: String?
    @State private var saving = false
    @State private var location = LocationProvider()
    @FocusState private var titleFocused: Bool

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("z. B. EFH Müller, Neubau", text: $input.title)
                        .focused($titleFocused)
                        .submitLabel(.done)
                        .font(.body.weight(.medium))
                        .accessibilityIdentifier("newTitleField")
                    if input.title.isEmpty, let suggestion {
                        Button("Vorschlag: \(suggestion)") { input.title = suggestion }
                            .font(.callout)
                    }
                } header: {
                    Text("Bauvorhaben *")
                }

                customerSection

                if newCustomer != nil {
                    SimilarCustomersSection(similar: similarCustomers) { contact in
                        withAnimation {
                            newCustomer = nil
                            customer = contact
                        }
                    }
                }

                Section {
                    Button {
                        Task { await useCurrentLocation() }
                    } label: {
                        HStack {
                            Label("Aktuellen Standort übernehmen", systemImage: "location.fill")
                            if locating {
                                Spacer()
                                ProgressView()
                            }
                        }
                    }
                    .disabled(locating)
                    if let locationHint {
                        Text(locationHint).font(.footnote).foregroundStyle(Theme.textSecondary)
                    }
                    TextField("Straße, Nr.", text: binding(\.street))
                        .textContentType(.fullStreetAddress)
                    HStack {
                        TextField("PLZ", text: binding(\.postalCode))
                            .keyboardType(.numberPad)
                            .textContentType(.postalCode)
                            .frame(maxWidth: 90)
                        TextField("Ort", text: binding(\.city))
                            .textContentType(.addressCity)
                    }
                    if let customer, customer.addressLine != nil, input.street == nil, input.city == nil {
                        Button {
                            input.street = customer.street
                            input.postalCode = customer.postalCode
                            input.city = customer.city
                        } label: {
                            Label("Adresse des Kunden übernehmen", systemImage: "arrow.down.doc")
                        }
                    }
                } header: {
                    Text("Baustelle")
                } footer: {
                    Text("Empfohlen, aber nicht Pflicht – die Adresse kann später ergänzt werden.")
                }

                Section {
                    Picker("Art", selection: $input.template) {
                        ForEach(BauakteTemplate.allCases, id: \.self) { template in
                            Text(template.title).tag(template)
                        }
                    }
                    .pickerStyle(.segmented)
                    DisclosureGroup("Beschreibung hinzufügen", isExpanded: $showsDescription) {
                        TextField("Beschreibung", text: binding(\.description), axis: .vertical)
                            .lineLimit(2...6)
                    }
                }

                if !similar.isEmpty {
                    Section {
                        ForEach(similar) { bauakte in
                            Button {
                                onCreated(bauakte.id, false)
                            } label: {
                                Label {
                                    VStack(alignment: .leading) {
                                        Text("Ähnliche Bauakte: \(bauakte.number.map { "\($0) · " } ?? "")\(bauakte.title)")
                                        if let address = bauakte.addressLine { Text(address).font(.footnote) }
                                        Text("Öffnen statt neu anlegen").font(.footnote.weight(.semibold))
                                    }
                                } icon: {
                                    Image(systemName: "exclamationmark.triangle.fill")
                                }
                                .foregroundStyle(Theme.hint)
                            }
                            .accessibilityIdentifier("similarBauakte")
                        }
                    }
                }

                if let errorMessage {
                    Section { StatusMessage(kind: .error, text: errorMessage).accessibilityIdentifier("newBauakteError") }
                }
            }
            .navigationTitle("Neue Bauakte")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
            }
            .safeAreaInset(edge: .bottom) {
                HStack(spacing: 10) {
                    Button {
                        Task { await create(photograph: false) }
                    } label: {
                        Label("Anlegen", systemImage: "checkmark")
                    }
                    .estrichButton(.secondary, size: .regular, fullWidth: true)
                    .accessibilityIdentifier("createButton")
                    Button {
                        Task { await create(photograph: true) }
                    } label: {
                        Label("Anlegen + Foto", systemImage: "camera.fill")
                    }
                    .estrichButton(.primary, size: .regular, fullWidth: true)
                    .accessibilityIdentifier("createAndPhotographButton")
                }
                .disabled(saving)
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(.bar)
            }
            .onAppear { titleFocused = true }
            .task { recentCustomers = (try? await model.active?.service?.recentCustomers()) ?? [] }
            .task(id: "\(input.title)|\(input.street ?? "")|\(input.city ?? "")|\(input.latitude ?? 0)") { await findSimilar() }
            .task(id: "\(newCustomer?.name ?? "")|\(newCustomer?.phone ?? "")|\(newCustomer?.email ?? "")") { await findSimilarCustomers() }
            .sheet(isPresented: $showsCustomerSearch) {
                CustomerPickerView(onSelect: { contact in
                    customer = contact
                    newCustomer = nil
                    showsCustomerSearch = false
                }, onNew: { name in
                    newCustomer = NewCustomer(name: name)
                    customer = nil
                    showsCustomerSearch = false
                })
            }
            .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
        }
        .presentationDetents([.large])
    }

    /// Kunde: vorhandenen wählen (Suche, zuletzt gewählte) oder neu erfassen.
    @ViewBuilder
    private var customerSection: some View {
        Section {
            if let customer {
                HStack {
                    CustomerSummary(contact: customer, showsActions: false)
                    Button {
                        withAnimation { self.customer = nil }
                    } label: {
                        Label("Kunde entfernen", systemImage: "xmark")
                    }
                    .buttonStyle(IconButtonStyle())
                    .accessibilityIdentifier("removeCustomerButton")
                }
            } else if newCustomer != nil {
                CustomerFields(customer: Binding(get: { newCustomer ?? NewCustomer() }, set: { newCustomer = $0 }),
                               siteAddress: (street: input.street, postalCode: input.postalCode, city: input.city))
                Button(role: .destructive) {
                    withAnimation { newCustomer = nil }
                } label: {
                    Label("Neuen Kunden verwerfen", systemImage: "trash")
                        .font(.subheadline)
                }
            } else {
                Button {
                    showsCustomerSearch = true
                } label: {
                    Label("Vorhandenen Kunden suchen", systemImage: "magnifyingglass")
                }
                .accessibilityIdentifier("customerSearchButton")
                Button {
                    withAnimation { newCustomer = NewCustomer() }
                } label: {
                    Label("Neuen Kunden erfassen", systemImage: "person.crop.circle.badge.plus")
                }
                .accessibilityIdentifier("newCustomerButton")
                if !recentCustomers.isEmpty {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 8) {
                            ForEach(recentCustomers) { contact in
                                Button {
                                    withAnimation { customer = contact }
                                } label: {
                                    Label(contact.name, systemImage: "clock.arrow.circlepath")
                                }
                                .estrichButton(.secondary)
                            }
                        }
                    }
                }
            }
        } header: {
            Text(newCustomer != nil ? "Neuer Kunde" : "Kunde")
        } footer: {
            if newCustomer != nil {
                Text("Wird mit der Bauakte gespeichert und beim nächsten Abgleich übertragen. Die Kundennummer vergibt das Büro.")
            }
        }
    }

    /// „Straße Nr., Ort“ als Bezeichnung, sobald eine Adresse vorhanden ist (09, Abschnitt 3).
    private var suggestion: String? {
        guard let street = input.street, !street.isEmpty else { return nil }
        return [street, input.city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: ", ")
    }

    private func binding(_ keyPath: WritableKeyPath<NewBauakte, String?>) -> Binding<String> {
        Binding(get: { input[keyPath: keyPath] ?? "" }, set: { input[keyPath: keyPath] = $0.isEmpty ? nil : $0 })
    }

    private func useCurrentLocation() async {
        locating = true
        defer { locating = false }
        do throws(LocationProvider.Failure) {
            let current = try await location.currentLocation()
            input.latitude = current.coordinate.latitude
            input.longitude = current.coordinate.longitude
            if let address = await location.address(for: current) {
                input.street = address.street ?? input.street
                input.postalCode = address.postalCode ?? input.postalCode
                input.city = address.city ?? input.city
                locationHint = "Adresse aus dem Standort übernommen – bitte kurz prüfen."
            } else {
                // Offline: nur Koordinaten (09, Abschnitt 3).
                locationHint = "Standort gespeichert. Die Adresse kann ohne Verbindung nicht ermittelt werden."
            }
        } catch .denied {
            locationHint = "Kein Zugriff auf den Standort. Die Adresse kann von Hand eingegeben werden."
        } catch {
            locationHint = "Der Standort konnte nicht bestimmt werden."
        }
    }

    private func findSimilar() async {
        try? await Task.sleep(for: .milliseconds(400))
        guard !Task.isCancelled, let service = model.active?.service else { return }
        let candidate = DuplicateFinder.Candidate(title: input.title, street: input.street, city: input.city,
                                                  latitude: input.latitude, longitude: input.longitude)
        similar = (try? await service.similar(to: candidate)) ?? []
    }

    private func findSimilarCustomers() async {
        guard let newCustomer else {
            similarCustomers = []
            return
        }
        try? await Task.sleep(for: .milliseconds(300))
        guard !Task.isCancelled else { return }
        similarCustomers = (try? await model.active?.service?.similarCustomers(to: newCustomer)) ?? []
    }

    private func create(photograph: Bool) async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        var data = input
        data.customerId = customer?.id
        do throws(BauakteError) {
            let id = try await service.create(data, newCustomer: newCustomer)
            model.active?.sync?.localChangeMade()
            onCreated(id, photograph)
        } catch {
            errorMessage = error.message
        }
    }
}
