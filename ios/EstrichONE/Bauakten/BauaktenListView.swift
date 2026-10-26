import EstrichCore
import SwiftUI

/// Reiter „Baustellen“: laufende Bauakten nach Stand gruppiert (heute im Einsatz zuerst), Suche über Nummer, Bezeichnung
/// und Adresse – alles offline aus der lokalen Ablage. Abgeschlossene und stornierte über „Anzeigen“.
/// iPad: Liste links, Bauakte rechts.
struct BauaktenListView: View {
    enum Scope: String, CaseIterable, Identifiable {
        case running, archive, all
        var id: String { rawValue }
        var title: String {
            switch self {
            case .running: "Laufende"
            case .archive: "Abgeschlossen und storniert"
            case .all: "Alle"
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
    @State private var loaded = false
    @State private var path: [UUID] = []
    @State private var selection: UUID?
    @State private var creating = false
    @State private var captureFor: Bauakte?
    @State private var inspectFor: Bauakte?

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
                if next != .open {
                    Task {
                        // Erst wenn das Anlageblatt geschlossen ist, Kamera bzw. Erstbegehung öffnen.
                        try? await Task.sleep(for: .milliseconds(600))
                        let created = try? await model.active?.service?.bauakte(id)
                        if next == .photograph { captureFor = created } else { inspectFor = created }
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
        .task(id: "\(search)|\(model.active?.sync?.revision ?? 0)") { await reload() }
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
        .searchable(text: $search, prompt: "Nummer, Bauvorhaben oder Adresse")
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
        case .running: return all.filter { !archived($0) }
        case .archive: return all.filter(archived)
        case .all: return all
        }
    }

    @ViewBuilder
    private var content: some View {
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
                    Label(scope == .archive ? "Keine abgeschlossenen Bauakten" : "Keine laufenden Baustellen", systemImage: "building.2")
                } description: {
                    Text(scope == .archive ? "Abgeschlossene und stornierte Bauakten erscheinen hier." : "Neue Baustellen legen Sie mit „+“ an – auch ohne Verbindung.")
                } actions: {
                    if scope != .archive {
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
            Section(search.isEmpty ? scope.title : "Treffer (\(shown.count))") {
                rows(shown.sorted { ($0.status.sortRank, $0.number ?? "") < ($1.status.sortRank, $1.number ?? "") })
            }
        } else {
            let todays = shown.filter { today[$0.id] != nil }
            if !todays.isEmpty {
                Section("Heute im Einsatz") { rows(todays) }
            }
            ForEach(Self.groups, id: \.self) { status in
                let group = shown.filter { $0.status == status && today[$0.id] == nil }
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
                BauakteRow(bauakte: bauakte, customerName: bauakte.customerId.flatMap { customers[$0] }, today: today[bauakte.id])
            }
            .swipeActions(edge: .leading) {
                Button { captureFor = bauakte } label: { Label("Foto", systemImage: "camera.fill") }
                    .tint(Theme.accentFill)
            }
            .swipeActions(edge: .trailing) {
                if let url = MapsLink.url(for: bauakte) {
                    Button { openURL(url) } label: { Label("Route", systemImage: "arrow.triangle.turn.up.right.diamond") }
                        .tint(Theme.textSecondary)
                }
            }
            .contextMenu {
                Button("Öffnen", systemImage: "folder") { open(bauakte.id) }
                Button("Foto aufnehmen", systemImage: "camera") { captureFor = bauakte }
                if let url = MapsLink.url(for: bauakte) {
                    Button("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond") { openURL(url) }
                }
                if let url = ContactLink.phone(bauakte.siteContactPhone) {
                    Button("Ansprechpartner anrufen", systemImage: "phone") { openURL(url) }
                }
            }
        }
    }

    private func open(_ id: UUID) {
        if sizeClass == .regular { selection = id } else { path.append(id) }
    }

    // MARK: Laden

    private func reload() async {
        guard let service = model.active?.service else { return }
        all = (try? await service.bauakten(search: search)) ?? []
        let contacts = (try? await service.contacts()) ?? []
        customers = Dictionary(contacts.map { ($0.id, $0.name) }, uniquingKeysWith: { first, _ in first })
        if let store = model.active?.sync?.store {
            let assignments = (await MyPlanning(store: store).cached())?.assignments(on: CalendarDay.string(.now)) ?? []
            var texts: [UUID: String] = [:]
            for assignment in assignments where texts[assignment.bauakteId] == nil {
                texts[assignment.bauakteId] = ["Heute", assignment.startText, assignment.crewName].compactMap { $0 }.joined(separator: " · ")
            }
            today = texts
        }
        loaded = true
        applyShotScene()
    }

    /// Screenshot-Modus: Baustelle oder Anlage öffnen.
    private func applyShotScene() {
        guard let scene = shotScene, scene != "baustellen", path.isEmpty, selection == nil, !creating else { return }
        if scene.hasPrefix("neu-") {
            creating = true
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
