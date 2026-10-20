import CoreLocation
import EstrichCore
import SwiftUI

/// Schnellanlage auf der Baustelle (09): ein Bildschirm, nur die Bezeichnung ist Pflicht, offline möglich.
struct NewBauakteView: View {
    /// Neue Bauakte angelegt; `true`, wenn direkt fotografiert werden soll.
    let onCreated: (UUID, Bool) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var input = NewBauakte(title: "")
    @State private var customer: Contact?
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
                        .frame(minHeight: 44)
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
                } header: {
                    Text("Baustelle")
                } footer: {
                    Text("Empfohlen, aber nicht Pflicht – die Adresse kann später ergänzt werden.")
                }

                Section {
                    if let customer {
                        HStack {
                            Text(customer.name)
                            Spacer()
                            Button("Entfernen", role: .destructive) { self.customer = nil }
                                .font(.callout)
                        }
                    } else {
                        Button {
                            showsCustomerSearch = true
                        } label: {
                            Label("Kunde suchen …", systemImage: "magnifyingglass").frame(minHeight: 44)
                        }
                        if !recentCustomers.isEmpty {
                            ScrollView(.horizontal, showsIndicators: false) {
                                HStack {
                                    ForEach(recentCustomers) { contact in
                                        Button(contact.name) { customer = contact }
                                            .buttonStyle(.bordered)
                                    }
                                }
                            }
                        }
                    }
                } header: {
                    Text("Kunde (optional)")
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
                    Section { StatusMessage(kind: .error, text: errorMessage) }
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
                HStack(spacing: 12) {
                    Button {
                        Task { await create(photograph: false) }
                    } label: {
                        ActionLabel("Anlegen")
                    }
                    .secondaryActionStyle()
                    .accessibilityIdentifier("createButton")
                    Button {
                        Task { await create(photograph: true) }
                    } label: {
                        ActionLabel("Anlegen und fotografieren", systemImage: "camera.fill")
                    }
                    .primaryActionStyle()
                    .accessibilityIdentifier("createAndPhotographButton")
                }
                .disabled(saving)
                .padding(.horizontal, 16)
                .padding(.vertical, 10)
                .background(.bar)
            }
            .onAppear { titleFocused = true }
            .task { recentCustomers = (try? await model.active?.service?.recentCustomers()) ?? [] }
            .task(id: "\(input.title)|\(input.street ?? "")|\(input.city ?? "")|\(input.latitude ?? 0)") { await findSimilar() }
            .sheet(isPresented: $showsCustomerSearch) {
                CustomerSearchView { contact in
                    customer = contact
                    showsCustomerSearch = false
                }
            }
            .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
        }
        .presentationDetents([.large])
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

    private func create(photograph: Bool) async {
        guard let service = model.active?.service else { return }
        saving = true
        defer { saving = false }
        var data = input
        data.customerId = customer?.id
        do throws(BauakteError) {
            let id = try await service.create(data)
            model.active?.sync?.localChangeMade()
            onCreated(id, photograph)
        } catch {
            errorMessage = error.message
        }
    }
}

/// Kundensuche über die synchronisierten Kontakte (offline).
struct CustomerSearchView: View {
    let onSelect: (Contact) -> Void
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""
    @State private var contacts: [Contact] = []

    var body: some View {
        NavigationStack {
            List(contacts) { contact in
                Button {
                    onSelect(contact)
                } label: {
                    VStack(alignment: .leading) {
                        Text(contact.name).foregroundStyle(.primary)
                        Text([contact.customerNumber, contact.city].compactMap { $0 }.joined(separator: " · "))
                            .font(.footnote).foregroundStyle(Theme.textSecondary)
                    }
                }
            }
            .overlay {
                if contacts.isEmpty {
                    ContentUnavailableView("Keine Kunden", systemImage: "person.crop.circle.badge.questionmark",
                                           description: Text("Auf diesem Gerät sind keine passenden Kunden vorhanden. Der Kunde kann im Büro nachgetragen werden."))
                }
            }
            .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Name oder Kundennummer")
            .navigationTitle("Kunde")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } } }
            .task(id: search) { contacts = (try? await model.active?.service?.contacts(search: search)) ?? [] }
        }
    }
}
