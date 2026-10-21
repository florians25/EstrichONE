import CoreLocation
import EstrichCore
import SwiftUI

/// Schnellanlage auf der Baustelle (09), offline möglich. Standard ist ein geführter Assistent in drei Schritten
/// (Kunde → Bauvorhaben und Baustelle → Art und Übersicht); wer es eilig hat, schaltet auf das einseitige Formular
/// um (die Wahl bleibt gespeichert). Pflicht sind Bezeichnung und Kunde – vorhanden oder neu erfasst; ein neuer
/// Kunde wird mit der Bauakte übertragen und verknüpft.
struct NewBauakteView: View {
    /// Was nach der Anlage geschieht.
    enum Next {
        case open
        case photograph
        /// Man ist beim Kunden vor Ort: gleich die Erstbegehung der neuen Bauakte durchführen.
        case inspect
    }

    /// Neue (oder ähnliche, vorhandene) Bauakte; danach öffnen, fotografieren oder Erstbegehung durchführen.
    let onCreated: (UUID, Next) -> Void

    enum Mode: String, CaseIterable, Identifiable {
        case wizard
        case form
        var id: String { rawValue }
        var title: String { self == .wizard ? "Assistent" : "Formular" }
    }

    enum Step: Int, CaseIterable {
        case customer
        case site
        case details

        var title: String {
            switch self {
            case .customer: "Kunde"
            case .site: "Baustelle"
            case .details: "Übersicht"
            }
        }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @SwiftUI.AppStorage("newBauakteMode") private var mode: Mode = .wizard
    @State private var step: Step = .customer
    @State private var input = NewBauakte(title: "")
    @State private var customer: Contact?
    @State private var newCustomer: NewCustomer?
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
                    Picker("Darstellung", selection: $mode) {
                        ForEach(Mode.allCases) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .accessibilityIdentifier("newBauakteModePicker")
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 0, leading: 0, bottom: 0, trailing: 0))
                }
                if mode == .wizard {
                    Section {
                        WizardProgress(steps: Step.allCases.map(\.title), current: step.rawValue)
                            .listRowBackground(Color.clear)
                            .listRowInsets(EdgeInsets(top: 0, leading: 0, bottom: 4, trailing: 0))
                    }
                    switch step {
                    case .customer:
                        customerSection
                    case .site:
                        titleSection
                        siteSection
                    case .details:
                        summarySection
                        kindSection
                    }
                } else {
                    titleSection
                    customerSection
                    siteSection
                    kindSection
                }
                similarSection
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
            .safeAreaInset(edge: .bottom) { bottomBar }
            .onAppear { if mode == .form { titleFocused = true } }
            .onChange(of: mode) { errorMessage = nil }
            .onChange(of: step) {
                errorMessage = nil
                if step == .site, input.title.isEmpty { titleFocused = true }
            }
            .task(id: "\(input.title)|\(input.street ?? "")|\(input.city ?? "")|\(input.latitude ?? 0)") { await findSimilar() }
            .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
            .sensoryFeedback(.selection, trigger: step)
        }
        .presentationDetents([.large])
    }

    // MARK: Abschnitte

    private var titleSection: some View {
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
    }

    private var customerSection: some View {
        CustomerChooser(customer: $customer, newCustomer: $newCustomer,
                        siteAddress: (street: input.street, postalCode: input.postalCode, city: input.city))
    }

    private var siteSection: some View {
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
            if let address = customerAddress, input.street == nil, input.city == nil {
                Button {
                    input.street = address.street
                    input.postalCode = address.postalCode
                    input.city = address.city
                } label: {
                    Label("Adresse des Kunden übernehmen", systemImage: "arrow.down.doc")
                }
            }
        } header: {
            Text("Baustelle")
        } footer: {
            Text("Empfohlen, aber nicht Pflicht – die Adresse kann später ergänzt werden.")
        }
    }

    private var kindSection: some View {
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
        } header: {
            Text("Art")
        }
    }

    private var summarySection: some View {
        Section {
            LabeledContent("Bauvorhaben", value: input.title.isEmpty ? "–" : input.title)
            LabeledContent("Kunde", value: customer?.name ?? newCustomer.map { "\($0.name) (neu)" } ?? "–")
            LabeledContent("Baustelle", value: addressLine ?? "noch ohne Adresse")
            Button {
                Task { await create(next: .inspect) }
            } label: {
                Label("Anlegen und Erstbegehung jetzt durchführen", systemImage: "checklist")
            }
            .estrichButton(.secondary, size: .regular, fullWidth: true)
            .listRowBackground(Color.clear)
            .disabled(saving)
            .accessibilityIdentifier("createAndInspectButton")
        } header: {
            Text("Übersicht")
        } footer: {
            Text("Beim Kunden vor Ort? Die Erstbegehung lässt sich direkt anschließen – auch ohne Verbindung.")
        }
    }

    @ViewBuilder
    private var similarSection: some View {
        if !similar.isEmpty, mode == .form || step != .customer {
            Section {
                ForEach(similar) { bauakte in
                    Button {
                        onCreated(bauakte.id, .open)
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
    }

    /// Unten: im Assistenten Zurück/Weiter, im letzten Schritt bzw. im Formular die beiden Anlage-Schaltflächen.
    private var bottomBar: some View {
        HStack(spacing: 10) {
            if mode == .wizard, step != .details {
                if step != .customer {
                    Button { goBack() } label: { Label("Zurück", systemImage: "chevron.left") }
                        .estrichButton(.secondary, size: .regular)
                        .accessibilityIdentifier("wizardBackButton")
                }
                Button { goNext() } label: { Label("Weiter", systemImage: "chevron.right") }
                    .estrichButton(.primary, size: .regular, fullWidth: true)
                    .accessibilityIdentifier("wizardNextButton")
            } else {
                if mode == .wizard {
                    Button { goBack() } label: { Image(systemName: "chevron.left") }
                        .estrichButton(.secondary, size: .regular)
                        .accessibilityLabel("Zurück")
                        .accessibilityIdentifier("wizardBackButton")
                }
                Button {
                    Task { await create(next: .open) }
                } label: {
                    Label("Anlegen", systemImage: "checkmark")
                }
                .estrichButton(.secondary, size: .regular, fullWidth: true)
                .accessibilityIdentifier("createButton")
                Button {
                    Task { await create(next: .photograph) }
                } label: {
                    Label("Anlegen + Foto", systemImage: "camera.fill")
                }
                .estrichButton(.primary, size: .regular, fullWidth: true)
                .accessibilityIdentifier("createAndPhotographButton")
            }
        }
        .disabled(saving)
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .background(.bar)
    }

    // MARK: Ablauf

    private func goNext() {
        if let problem = problem(upTo: step) {
            errorMessage = problem
            return
        }
        if let next = Step(rawValue: step.rawValue + 1) {
            withAnimation { step = next }
        }
    }

    private func goBack() {
        if let previous = Step(rawValue: step.rawValue - 1) {
            withAnimation { step = previous }
        }
    }

    /// Fehlende Pflichtangaben bis einschließlich `step`.
    private func problem(upTo step: Step) -> String? {
        if customer == nil {
            guard let newCustomer else { return "Bitte einen Kunden wählen oder neu anlegen." }
            do throws(BauakteError) {
                try newCustomer.validate()
            } catch {
                return error.message
            }
        }
        if step.rawValue >= Step.site.rawValue, input.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return BauakteError.titleRequired.message
        }
        return nil
    }

    private var customerAddress: (street: String?, postalCode: String?, city: String?)? {
        if let customer, customer.addressLine != nil {
            return (street: customer.street, postalCode: customer.postalCode, city: customer.city)
        }
        if let newCustomer, newCustomer.street != nil || newCustomer.city != nil {
            return (street: newCustomer.street, postalCode: newCustomer.postalCode, city: newCustomer.city)
        }
        return nil
    }

    private var addressLine: String? {
        let place = [input.postalCode, input.city].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " ")
        let parts = [input.street, place].compactMap { $0?.isEmpty == false ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
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

    private func create(next: Next) async {
        guard let service = model.active?.service else { return }
        if let problem = problem(upTo: .details) {
            errorMessage = problem
            return
        }
        saving = true
        defer { saving = false }
        var data = input
        data.customerId = customer?.id
        do throws(BauakteError) {
            let id = try await service.create(data, newCustomer: newCustomer)
            model.active?.sync?.localChangeMade()
            onCreated(id, next)
        } catch {
            errorMessage = error.message
        }
    }
}
