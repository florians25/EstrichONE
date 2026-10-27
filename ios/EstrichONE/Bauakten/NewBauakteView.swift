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
    /// Aus der Kundenkartei: dieser Kunde ist schon gewählt.
    var initialCustomer: Contact?

    init(initialCustomer: Contact? = nil, onCreated: @escaping (UUID, Next) -> Void) {
        self.initialCustomer = initialCustomer
        self.onCreated = onCreated
    }

    enum Mode: String, CaseIterable, Identifiable {
        case wizard
        case form
        var id: String { rawValue }
        var title: String { self == .wizard ? "Assistent" : "Formular" }
    }

    enum Step: Int, CaseIterable {
        case customer
        case site
        case project
        case details

        var title: String {
            switch self {
            case .customer: "Kunde"
            case .site: "Baustelle"
            case .project: "Bauvorhaben"
            case .details: "Übersicht"
            }
        }

        var question: String {
            switch self {
            case .customer: "Für wen bauen wir?"
            case .site: "Wo ist die Baustelle?"
            case .project: "Was wird gebaut?"
            case .details: "Alles richtig?"
            }
        }

        var hint: String {
            switch self {
            case .customer: "Vorhandenen Kunden suchen oder neu erfassen – auch ohne Verbindung."
            case .site: "Bezeichnung und Adresse. Den Standort übernimmt die App auf Knopfdruck."
            case .project: "Fläche, Estrich und Aufbau – alles optional und später ergänzbar."
            case .details: "Kurz prüfen und anlegen. Übertragen wird automatisch, sobald Netz da ist."
            }
        }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @Environment(\.shotScene) private var shotScene
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
    /// Formular-Schema des Büros (offline: zuletzt geladener Stand); ohne Schema die festen Felder.
    @State private var schema: FormSchema?
    @State private var lists = ValueLists([])
    @FocusState private var titleFocused: Bool

    var body: some View {
        NavigationStack {
            Form {
                if mode == .wizard {
                    Section {
                        VStack(alignment: .leading, spacing: Space.m) {
                            StepProgress(steps: Step.allCases.map(\.title), current: step.rawValue)
                            VStack(alignment: .leading, spacing: Space.xs) {
                                Text(step.question)
                                    .font(Typo.title)
                                    .foregroundStyle(Theme.textPrimary)
                                    .accessibilityAddTraits(.isHeader)
                                Text(step.hint)
                                    .font(Typo.subhead)
                                    .foregroundStyle(Theme.textSecondary)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                        }
                        .listRowBackground(Color.clear)
                        .listRowInsets(EdgeInsets(top: Space.s, leading: Space.xs, bottom: Space.s, trailing: Space.xs))
                    }
                    switch step {
                    case .customer:
                        customerSection
                    case .site:
                        titleSection
                        siteSection
                    case .project:
                        projectFields
                    case .details:
                        summarySection
                        kindSection
                    }
                } else {
                    titleSection
                    customerSection
                    siteSection
                    projectFields
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
                ToolbarItem(placement: .primaryAction) {
                    Menu {
                        Picker("Darstellung", selection: $mode) {
                            Label("Schritt für Schritt", systemImage: "list.number").tag(Mode.wizard)
                            Label("Alles auf einer Seite", systemImage: "list.bullet").tag(Mode.form)
                        }
                    } label: {
                        Label("Darstellung", systemImage: "slider.horizontal.3")
                    }
                    .accessibilityIdentifier("newBauakteModePicker")
                }
            }
            .safeAreaInset(edge: .bottom) { bottomBar }
            .task { await applyShotScene() }
            .onAppear {
                if customer == nil, newCustomer == nil, let initialCustomer {
                    customer = initialCustomer
                    if mode == .wizard { step = .site }
                }
                if mode == .form { titleFocused = true }
            }
            .onChange(of: mode) { errorMessage = nil }
            .onChange(of: step) {
                errorMessage = nil
                if step == .site, input.title.isEmpty { titleFocused = true }
            }
            .task(id: "\(input.title)|\(input.street ?? "")|\(input.city ?? "")|\(input.latitude ?? 0)") { await findSimilar() }
            .sensoryFeedback(.error, trigger: errorMessage) { _, new in new != nil }
            .sensoryFeedback(.selection, trigger: step)
            .task {
                guard let service = model.active?.service else { return }
                schema = await service.formSchema(using: model.active?.client)
                lists = await service.valueLists()
            }
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

    /// Fläche, Estrich, Aufbau, vor Ort, Zeitraum – alles optional.
    @ViewBuilder
    private var projectFields: some View {
        if let schema {
            SchemaProjectFields(schema: schema, lists: lists, values: Binding { input.projectData ?? [:] } set: { input.projectData = $0 })
            PlannedPeriodSection(plannedStart: $input.plannedStart, plannedEnd: $input.plannedEnd)
        } else {
            ProjectFields(project: $input.project, plannedStart: $input.plannedStart, plannedEnd: $input.plannedEnd)
        }
    }

    private var projectBadges: [String] {
        if let schema { return schema.badges(for: input.projectData ?? [:], lists: lists) }
        return input.project.badges
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
            if !projectBadges.isEmpty {
                LabeledContent("Bauvorhaben", value: projectBadges.joined(separator: " · "))
            }
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
                        .foregroundStyle(Theme.warning)
                    }
                    .accessibilityIdentifier("similarBauakte")
                }
            }
        }
    }

    /// Unten: im Assistenten Zurück/Weiter, im letzten Schritt bzw. im Formular die beiden Anlage-Schaltflächen.
    private var bottomBar: some View {
        // Alle Schaltflächen gleich groß, einzeilig.
        VStack(spacing: 8) {
            if mode == .wizard, step != .details {
                HStack(spacing: 10) {
                    if step != .customer {
                        Button { goBack() } label: { Label("Zurück", systemImage: "chevron.left") }
                            .estrichButton(.secondary, size: .regular, fullWidth: true)
                            .accessibilityIdentifier("wizardBackButton")
                    }
                    Button { goNext() } label: { Label("Weiter", systemImage: "chevron.right") }
                        .estrichButton(.primary, size: .regular, fullWidth: true)
                        .accessibilityIdentifier("wizardNextButton")
                }
            } else {
                HStack(spacing: 10) {
                    if mode == .wizard {
                        Button { goBack() } label: { Label("Zurück", systemImage: "chevron.left") }
                            .estrichButton(.secondary, size: .regular, fullWidth: true)
                            .accessibilityIdentifier("wizardBackButton")
                    }
                    Button {
                        Task { await create(next: .open) }
                    } label: {
                        Label("Anlegen", systemImage: "checkmark")
                    }
                    .estrichButton(.secondary, size: .regular, fullWidth: true)
                    .accessibilityIdentifier("createButton")
                }
                Button {
                    Task { await create(next: .photograph) }
                } label: {
                    Label("Anlegen und fotografieren", systemImage: "camera.fill")
                }
                .estrichButton(.primary, size: .regular, fullWidth: true)
                .accessibilityIdentifier("createAndPhotographButton")
            }
        }
        .lineLimit(1)
        .minimumScaleFactor(0.8)
        .disabled(saving)
        .padding(.horizontal, Space.l)
        .padding(.top, Space.m)
        .padding(.bottom, Space.s)
        .background(.bar)
    }

    /// Screenshot-Modus: Schritt „neu-2“ … „neu-4“ mit Beispieleingaben zeigen.
    private func applyShotScene() async {
        guard let scene = shotScene, scene.hasPrefix("neu-"), let number = Int(scene.dropFirst(4)), number > 1,
              let service = model.active?.service else { return }
        mode = .wizard
        if customer == nil {
            customer = ((try? await service.contacts()) ?? []).first { !$0.isCompany }
        }
        input.title = "EFH Brunner, Neubau"
        input.street = "Birkenweg 14"
        input.postalCode = "84036"
        input.city = "Landshut"
        if number >= 3 {
            input.project.objectType = .efh
            input.project.totalArea = 168
            input.projectData = ["objectType": .string("efh"), "totalArea": .number(168), "screedType": .string("ct"),
                                 "nominalThicknessMm": .number(65), "underfloorHeating": .bool(true)]
        }
        step = Step(rawValue: min(number - 1, Step.details.rawValue)) ?? .site
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
        if step.rawValue >= Step.project.rawValue, let schema {
            let missing = schema.missingRequired(in: input.projectData ?? [:])
            if !missing.isEmpty { return "Bitte noch angeben: \(missing.joined(separator: ", "))." }
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
