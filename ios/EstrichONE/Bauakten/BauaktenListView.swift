import EstrichCore
import SwiftUI

/// Reiter „Baustellen“: oben Suche und Filter (Heute, Laufend, Archiv, Alle) als Segment, darunter kompakte Zeilen mit
/// Bezeichnung, farbigem Status, Nummer, Ort und nächstem Termin. Laufende nach Stand gruppiert, heute im Einsatz zuerst,
/// innerhalb einer Gruppe nach dem nächsten Termin. Alles offline aus der lokalen Ablage; „Alle im Betrieb“ online im Menü.
/// iPad: Liste links, Bauakte rechts.
struct BauaktenListView: View {
    enum Scope: String, CaseIterable, Identifiable {
        case today, running, archive, all, company
        var id: String { rawValue }
        /// Im Segment oben (ohne „Alle im Betrieb“, das steht im Menü).
        static let segments: [Scope] = [.today, .running, .archive, .all]
        var segmentTitle: String {
            switch self {
            case .today: "Heute"
            case .running: "Laufend"
            case .archive: "Archiv"
            case .all, .company: "Alle"
            }
        }
        var title: String {
            switch self {
            case .today: "Heute im Einsatz"
            case .running: "Laufende"
            case .archive: "Abgeschlossen und storniert"
            case .all: "Alle"
            case .company: "Alle im Betrieb (online)"
            }
        }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.shotScene) private var shotScene
    @Environment(\.openURL) private var openURL
    @State private var search = ""
    @State private var scope: Scope = .running
    @State private var all: [Bauakte] = []
    @State private var customers: [UUID: String] = [:]
    @State private var today: [UUID: String] = [:]
    /// Nächster Termin je Bauakte ab heute (Tag `yyyy-MM-dd` und Text „Morgen, 07:30, Kolonne Huber“).
    @State private var upcoming: [UUID: (day: String, text: String)] = [:]
    @State private var loaded = false
    @State private var path: [UUID] = []
    @State private var selection: UUID?
    @State private var creating = false
    @State private var captureFor: Bauakte?
    @State private var inspectFor: Bauakte?
    /// Nicht zugeteilte Bauakten vom Server (nur online, nur ansehen) – bei Suche und „Alle im Betrieb“.
    @State private var others: [RemoteBauakte] = []
    /// Recht zur Planung (`planning.manage`, zuletzt bekannter Stand): „Einplanen“ beim Wischen und im Kontextmenü.
    @State private var canPlan = false
    @State private var planFor: Bauakte?
    @State private var toast: String?

    private static let groups: [BauakteStatus] = [.inProgress, .drying, .planned, .inquiry]

    var body: some View {
        Group {
            if sizeClass == .regular {
                // iPad: Liste links, Bauakte rechts (Querformat zweispaltig, Hochformat einblendbar).
                NavigationSplitView {
                    list
                        .navigationSplitViewColumnWidth(min: 320, ideal: 380, max: 460)
                } detail: {
                    NavigationStack {
                        if let selection {
                            BauakteDetailView(bauakteId: selection).id(selection)
                        } else {
                            ContentUnavailableView("Baustelle wählen", systemImage: "building.2",
                                                   description: Text("Links eine Bauakte antippen oder mit ⌘N eine neue anlegen."))
                        }
                    }
                }
                .navigationSplitViewStyle(.balanced)
            } else {
                NavigationStack(path: $path) {
                    list
                        .navigationDestination(for: UUID.self) { BauakteDetailView(bauakteId: $0) }
                }
            }
        }
        .sheet(isPresented: $creating) {
            NewBauakteView { id, next in
                creating = false
                open(id)
                if next.followUp != .open {
                    Task {
                        // Erst wenn das Anlageblatt geschlossen ist, Kamera bzw. Erstbegehung öffnen.
                        try? await Task.sleep(for: .milliseconds(600))
                        let created = try? await model.active?.service?.bauakte(id)
                        if next.followUp == .photo { captureFor = created } else { inspectFor = created }
                    }
                }
            }
        }
        .fullScreenCover(item: $captureFor) { bauakte in
            CameraView(bauakte: bauakte) { _ in captureFor = nil }
        }
        .sheet(item: $inspectFor) { bauakte in
            InspectionEntryView(bauakte: bauakte) { inspectFor = nil }
        }
        .sheet(item: $planFor) { bauakte in
            QuickPlanSheet(target: QuickPlanSheet.Target(bauakte)) { result in
                planFor = nil
                guard let result else { return }
                Task {
                    let outcome = await QuickPlanRunner.perform(result.change, confirmation: result.confirmation, model: model)
                    toast = outcome.message
                    if outcome.sent { await reload() }
                }
            }
        }
        .toast($toast)
        .task(id: "\(search)|\(scope.rawValue)|\(model.active?.sync?.revision ?? 0)") { await reload() }
    }

    // MARK: Liste

    @ViewBuilder
    private var list: some View {
        Group {
            if sizeClass == .regular {
                List(selection: $selection) { content }
            } else {
                List { content }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Baustellen")
        .searchable(text: $search, prompt: "Nummer, Bauvorhaben, Kunde, Ort")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { creating = true } label: { Label("Neue Bauakte", systemImage: "plus") }
                    .keyboardShortcut("n", modifiers: .command)
                    .accessibilityIdentifier("newBauakteButton")
            }
            ToolbarItem(placement: .secondaryAction) {
                Picker("Anzeigen", selection: $scope) {
                    ForEach(Scope.allCases) { Text($0.title).tag($0) }
                }
                .accessibilityIdentifier("archiveToggle")
            }
        }
        .rootToolbar()
        .refreshable { await model.active?.sync?.run() }
    }

    private var visible: [Bauakte] {
        let archived: (Bauakte) -> Bool = { $0.status == .completed || $0.status == .cancelled }
        if !search.isEmpty { return all }
        switch scope {
        case .today: return all.filter { today[$0.id] != nil }
        case .running: return all.filter { !archived($0) }
        case .archive: return all.filter(archived)
        case .all: return all
        case .company: return all.filter { !archived($0) }
        }
    }

    /// Filter als Segment über der Liste (nicht bei der Suche).
    @ViewBuilder
    private var filterBar: some View {
        if search.isEmpty {
            Section {
                Picker("Filter", selection: $scope) {
                    ForEach(Scope.segments) { Text($0.segmentTitle).tag($0) }
                }
                .pickerStyle(.segmented)
                .accessibilityIdentifier("bauakteFilter")
            }
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets(top: 0, leading: 0, bottom: 0, trailing: 0))
            .listRowSeparator(.hidden)
        }
    }

    /// Sortierung: heute im Einsatz zuerst, dann nach Stand (laufend vor geplant), dann nach nächstem Termin und Nummer.
    private func sorted(_ list: [Bauakte]) -> [Bauakte] {
        list.sorted { lhs, rhs in
            let left = (today[lhs.id] == nil ? 1 : 0, lhs.status.sortRank, upcoming[lhs.id]?.day ?? "9999", lhs.number ?? "")
            let right = (today[rhs.id] == nil ? 1 : 0, rhs.status.sortRank, upcoming[rhs.id]?.day ?? "9999", rhs.number ?? "")
            return left < right
        }
    }

    @ViewBuilder
    private var content: some View {
        filterBar
        localContent
        if !others.isEmpty {
            Section {
                ForEach(others) { item in
                    NavigationLink(value: item.id) {
                        HStack(spacing: Space.s) {
                            BauakteRow(bauakte: item.bauakte)
                            Spacer(minLength: 0)
                            Image(systemName: "eye")
                                .foregroundStyle(Theme.textSecondary)
                                .accessibilityLabel("Nur ansehen")
                        }
                    }
                    .accessibilityIdentifier("onlineBauakteRow")
                }
            } header: {
                Text("Weitere Bauakten (online)")
            } footer: {
                Text("Nicht zugeteilt, nur ansehen. Zum Bearbeiten in der Bauakte „\(BauakteViewOnly.join)“ wählen.")
            }
        }
    }

    @ViewBuilder
    private var localContent: some View {
        let shown = visible
        if !loaded {
            Section {
                ForEach(0..<5, id: \.self) { _ in
                    VStack(alignment: .leading, spacing: Space.s) {
                        SkeletonBar(width: 200, height: 16)
                        SkeletonBar(width: 150, height: 12)
                        SkeletonBar(width: 90, height: 12)
                    }
                    .padding(.vertical, Space.xs)
                }
            }
            .pulsing()
        } else if shown.isEmpty {
            if search.isEmpty {
                ContentUnavailableView {
                    Label(scope == .archive ? "Keine abgeschlossenen Bauakten" : scope == .today ? "Heute kein Einsatz" : "Keine laufenden Baustellen",
                          systemImage: scope == .today ? "calendar" : "building.2")
                } description: {
                    Text(scope == .archive ? "Abgeschlossene und stornierte Bauakten erscheinen hier."
                         : scope == .today ? "Baustellen, auf denen Sie heute eingeplant sind, erscheinen hier."
                         : "Neue Bauakten legen Sie mit „+“ an, auch ohne Verbindung.")
                } actions: {
                    if scope != .archive, scope != .today {
                        Button { creating = true } label: { Label("Neue Bauakte", systemImage: "plus") }
                            .estrichButton(.primary, size: .regular)
                    }
                }
                .listRowBackground(Color.clear)
            } else {
                ContentUnavailableView.search(text: search)
                    .listRowBackground(Color.clear)
            }
        } else if !search.isEmpty || scope != .running {
            Section(search.isEmpty ? "\(scope.title) (\(shown.count))" : "Treffer (\(shown.count))") {
                rows(sorted(shown))
            }
        } else {
            let todays = shown.filter { today[$0.id] != nil }
            if !todays.isEmpty {
                Section("Heute im Einsatz (\(todays.count))") { rows(sorted(todays)) }
            }
            ForEach(Self.groups, id: \.self) { status in
                let group = sorted(shown.filter { $0.status == status && today[$0.id] == nil })
                if !group.isEmpty {
                    Section("\(status.shortTitle) (\(group.count))") { rows(group) }
                }
            }
            let archived = all.count - shown.count
            if archived > 0 {
                Section {
                    Button { scope = .archive } label: {
                        HStack {
                            Label("Abgeschlossen und storniert", systemImage: "archivebox")
                                .foregroundStyle(Theme.textPrimary)
                            Spacer()
                            Text("\(archived)").foregroundStyle(Theme.textSecondary)
                            Chevron()
                        }
                    }
                    .accessibilityIdentifier("archiveRow")
                }
            }
        }
    }

    private func rows(_ list: [Bauakte]) -> some View {
        ForEach(list) { bauakte in
            NavigationLink(value: bauakte.id) {
                BauakteRow(bauakte: bauakte, customerName: bauakte.customerId.flatMap { customers[$0] },
                           today: today[bauakte.id] ?? upcoming[bauakte.id]?.text)
            }
            .swipeActions(edge: .leading) {
                Button { captureFor = bauakte } label: { Label("Foto", systemImage: "camera.fill") }
                    .tint(Theme.accentFill)
            }
            .swipeActions(edge: .trailing) {
                if plans(bauakte) {
                    Button { planFor = bauakte } label: { Label("Einplanen", systemImage: "calendar.badge.plus") }
                        .tint(Theme.accentFill)
                }
                if let url = MapsLink.url(for: bauakte) {
                    Button { openURL(url) } label: { Label("Route", systemImage: "arrow.triangle.turn.up.right.diamond") }
                        .tint(Theme.textSecondary)
                }
            }
            .contextMenu {
                Button("Öffnen", systemImage: "folder") { open(bauakte.id) }
                Button("Foto aufnehmen", systemImage: "camera") { captureFor = bauakte }
                if plans(bauakte) {
                    Button("Einplanen", systemImage: "calendar.badge.plus") { planFor = bauakte }
                }
                if let url = MapsLink.url(for: bauakte) {
                    Button("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond") { openURL(url) }
                }
                if let url = ContactLink.phone(bauakte.siteContactPhone) {
                    Button("Ansprechpartner anrufen", systemImage: "phone") { openURL(url) }
                }
            }
        }
    }

    /// „Einplanen“ nur für die Planung und nicht für stornierte Bauakten.
    private func plans(_ bauakte: Bauakte) -> Bool {
        canPlan && bauakte.status != .cancelled
    }

    private func open(_ id: UUID) {
        if sizeClass == .regular { selection = id } else { path.append(id) }
    }

    // MARK: Laden

    private func reload() async {
        guard let service = model.active?.service else { return }
        all = (try? await service.bauakten(search: search)) ?? []
        // Alle sehen alle Bauakten: nicht zugeteilte (nicht auf dem Gerät) online dazuholen.
        if (scope == .company || !search.isEmpty), let client = model.active?.client {
            let local = Set(all.map(\.id))
            let found = (try? await client.onlineBauakten(search: search, includeOld: scope == .archive || scope == .all)) ?? []
            others = found.filter { !local.contains($0.id) }
        } else {
            others = []
        }
        let contacts = (try? await service.contacts()) ?? []
        customers = Dictionary(contacts.map { ($0.id, $0.name) }, uniquingKeysWith: { first, _ in first })
        if let store = model.active?.sync?.store {
            let granted = await MyPermissions(store: store).cached()
            let modules: AppModules = model.active?.sync?.modules ?? .allOn
            canPlan = modules.isOn(AppModules.planning) && granted.contains("planning.manage")
            let day = CalendarDay.string(.now)
            let planned = (await MyPlanning(store: store).cached())?.range.assignments
                .filter { $0.day >= day }
                .sorted { ($0.day, $0.startTime ?? "99") < ($1.day, $1.startTime ?? "99") } ?? []
            var texts: [UUID: String] = [:]
            var next: [UUID: (day: String, text: String)] = [:]
            for assignment in planned where next[assignment.bauakteId] == nil {
                let text = TodayGlance.appointmentText(day: assignment.day, start: assignment.startText, detail: assignment.crewName, today: day)
                next[assignment.bauakteId] = (assignment.day, text)
                if assignment.day == day { texts[assignment.bauakteId] = text }
            }
            today = texts
            upcoming = next
        }
        loaded = true
        applyShotScene()
    }

    /// Screenshot-Modus: Baustelle oder Anlage öffnen.
    private func applyShotScene() {
        guard let scene = shotScene, scene != "baustellen", path.isEmpty, selection == nil, !creating else { return }
        if scene.hasPrefix("neu-") {
            creating = true
        } else if scene == "erstbegehung-offen", let planned = all.first(where: { $0.status == .planned && !$0.initialInspectionWaived }) {
            // Hinweiskarte „Erstbegehung noch nicht ausgeführt“ mit „Erstbegehung nicht erforderlich“.
            open(planned.id)
        } else if scene == "erstbegehung-entfaellt", let waived = all.first(where: \.initialInspectionWaived) {
            open(waived.id)
        } else if let first = all.first(where: { $0.status == .inProgress }) ?? all.first {
            open(first.id)
        }
    }
}

/// Navigation in der Karten-App des Systems statt eingebetteter Fremdkarte (02, Abschnitt 4).
enum MapsLink {
    static func url(for bauakte: Bauakte) -> URL? {
        var components = URLComponents(string: "https://maps.apple.com/")
        if let latitude = bauakte.latitude, let longitude = bauakte.longitude {
            components?.queryItems = [URLQueryItem(name: "daddr", value: "\(latitude),\(longitude)")]
        } else if let address = bauakte.addressLine {
            components?.queryItems = [URLQueryItem(name: "daddr", value: address)]
        } else {
            return nil
        }
        return components?.url
    }

    /// Route zu einer Adresse ohne Koordinaten.
    static func url(address: String?) -> URL? {
        guard let address, !address.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
        var components = URLComponents(string: "https://maps.apple.com/")
        components?.queryItems = [URLQueryItem(name: "daddr", value: address)]
        return components?.url
    }
}

/// Telefon und E-Mail als Systemlinks (Anruf über die Telefon-App, E-Mail über die Mail-App).
enum ContactLink {
    static func phone(_ number: String?) -> URL? {
        guard let number else { return nil }
        let digits = number.filter { $0.isNumber || $0 == "+" }
        return digits.count >= 3 ? URL(string: "tel:\(digits)") : nil
    }

    static func email(_ address: String?) -> URL? {
        guard let address = address?.trimmingCharacters(in: .whitespaces), address.contains("@") else { return nil }
        return URL(string: "mailto:\(address.addingPercentEncoding(withAllowedCharacters: .urlUserAllowed) ?? address)")
    }
}
