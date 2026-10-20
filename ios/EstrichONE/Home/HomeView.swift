import EstrichCore
import SwiftUI

/// Startseite: kompakter Kopf in Navy mit Person und Gerät, drei Schnellaktionen, „Mein Einsatz heute“,
/// Navigation als ruhige Liste und zuletzt geöffnete Bauakten. Normale iOS-Größen, gut lesbar mit Dynamic Type.
struct HomeView: View {
    let openTab: (MainTabView.Tab) -> Void
    var openCapture: (CaptureMode) -> Void = { _ in }

    @Environment(AppModel.self) private var model
    @State private var path: [HomeRoute] = []
    @State private var creating = false
    @State private var reporting = false
    @State private var captureFor: Bauakte?
    @State private var planning: MyPlanning.Snapshot?
    @State private var openTasks = 0
    @State private var recent: [Bauakte] = []

    var body: some View {
        NavigationStack(path: $path) {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    HomeHeader()
                    if let sync = model.active?.sync {
                        SyncStatusBar(sync: sync)
                            .clipShape(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous))
                            .padding(.horizontal)
                        SystemNoticesBanner(sync: sync)
                            .padding(.horizontal)
                    }
                    quickActions
                        .padding(.horizontal)
                    TodayAssignments(snapshot: planning) { path.append(.bauakte($0)) }
                        .padding(.horizontal)
                    navigation
                        .padding(.horizontal)
                    if !recent.isEmpty {
                        VStack(alignment: .leading, spacing: 8) {
                            SectionTitle("Zuletzt geöffnet")
                            VStack(spacing: 0) {
                                ForEach(Array(recent.enumerated()), id: \.element.id) { index, bauakte in
                                    if index > 0 { Divider().padding(.leading, 14) }
                                    Button { path.append(.bauakte(bauakte.id)) } label: {
                                        HStack(spacing: 10) {
                                            BauakteRow(bauakte: bauakte)
                                            Image(systemName: "chevron.right")
                                                .font(.footnote.weight(.semibold))
                                                .foregroundStyle(.tertiary)
                                        }
                                        .padding(.horizontal, 14)
                                        .padding(.vertical, 10)
                                        .contentShape(Rectangle())
                                    }
                                    .buttonStyle(RowButtonStyle())
                                }
                            }
                            .card()
                        }
                        .padding(.horizontal)
                    }
                }
                .padding(.bottom, 24)
            }
            .background(Theme.surfaceAlt)
            .toolbar(.hidden, for: .navigationBar)
            .navigationDestination(for: HomeRoute.self) { route in
                switch route {
                case let .bauakte(id): BauakteDetailView(bauakteId: id)
                case .tasks: MyTasksView()
                case .week: MyWeekView()
                case .sync: if let sync = model.active?.sync { SyncView(sync: sync) }
                }
            }
            .sheet(isPresented: $creating) {
                NewBauakteView { id, photograph in
                    creating = false
                    path.append(.bauakte(id))
                    if photograph {
                        Task {
                            try? await Task.sleep(for: .milliseconds(600))
                            captureFor = try? await model.active?.service?.bauakte(id)
                        }
                    }
                }
            }
            .sheet(isPresented: $reporting) { ReportProblemView { reporting = false } }
            .fullScreenCover(item: $captureFor) { bauakte in
                CameraView(bauakte: bauakte) { _ in captureFor = nil }
            }
            .task(id: model.active?.sync?.revision) { await reload() }
            .refreshable {
                await model.active?.sync?.run()
                await reload()
            }
        }
    }

    /// Drei Schnellaktionen: anlegen (Hauptaktion), fotografieren und scannen (Akzent bzw. Nebenaktion).
    private var quickActions: some View {
        HStack(spacing: 10) {
            Button { creating = true } label: {
                Label("Neue Bauakte", systemImage: "folder.badge.plus")
            }
            .buttonStyle(ActionTileStyle(rank: .primary))
            .accessibilityIdentifier("newBauakteTile")
            Button { openCapture(.photo) } label: {
                Label("Foto", systemImage: "camera.fill")
            }
            .buttonStyle(ActionTileStyle(rank: .accent))
            .accessibilityIdentifier("photoTile")
            Button { openCapture(.document) } label: {
                Label("Scannen", systemImage: "doc.viewfinder")
            }
            .buttonStyle(ActionTileStyle(rank: .secondary))
            .accessibilityIdentifier("scanTile")
        }
    }

    private var navigation: some View {
        VStack(spacing: 0) {
            NavigationRow(title: "Bauakten", symbol: "folder.fill", color: Theme.navyLight) { openTab(.bauakten) }
            Divider().padding(.leading, 54)
            NavigationRow(title: "Meine Aufgaben", symbol: "checklist", color: Color(hex: 0xE07A1F), badge: openTasks) { path.append(.tasks) }
                .accessibilityIdentifier("myTasksTile")
            Divider().padding(.leading, 54)
            NavigationRow(title: "Meine Woche", symbol: "calendar", color: Color(hex: 0x2F80ED)) { path.append(.week) }
                .accessibilityIdentifier("myWeekTile")
            Divider().padding(.leading, 54)
            NavigationRow(title: "Synchronisation", symbol: "arrow.triangle.2.circlepath", color: Color(hex: 0x1F9D63)) { path.append(.sync) }
            Divider().padding(.leading, 54)
            NavigationRow(title: "Problem melden", symbol: "exclamationmark.bubble.fill", color: Color(hex: 0xC2410C)) { reporting = true }
        }
        .card()
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        recent = Array(((try? await service.recentBauakten()) ?? []).prefix(3))
        openTasks = ((try? await service.myWorkItems()) ?? []).count
        if let store = model.active?.sync?.store {
            planning = await MyPlanning(store: store).load(using: model.active?.client)
        }
    }
}

enum HomeRoute: Hashable {
    case bauakte(UUID)
    case tasks
    case week
    case sync
}

/// Zeile wie in den Einstellungen: farbiges Symbol, Titel, Zähler, Pfeil. 44 pt Mindesthöhe.
struct NavigationRow: View {
    let title: String
    let symbol: String
    var color: Color = Theme.navyLight
    var badge = 0
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 12) {
                Image(systemName: symbol)
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(.white)
                    .frame(width: 28, height: 28)
                    .background(color, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                    .accessibilityHidden(true)
                Text(title)
                    .foregroundStyle(.primary)
                Spacer()
                if badge > 0 {
                    Text("\(badge)")
                        .font(.caption.weight(.bold)).monospacedDigit()
                        .foregroundStyle(.white)
                        .padding(.horizontal, 7).padding(.vertical, 2)
                        .background(Theme.error, in: Capsule())
                }
                Image(systemName: "chevron.right")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(.tertiary)
            }
            .padding(.horizontal, 14)
            .frame(minHeight: 48)
            .contentShape(Rectangle())
        }
        .buttonStyle(RowButtonStyle())
        .accessibilityLabel(badge > 0 ? "\(title), \(badge) offen" : title)
    }
}

/// Zeilen in Karten: beim Drücken grau hinterlegt wie Listenzeilen.
struct RowButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(configuration.isPressed ? Color(uiColor: .systemFill) : .clear)
    }
}

/// Kopf in Navy: Marke, Server, Person, Gerät; Benutzerwechsel auf Poolgeräten.
struct HomeHeader: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                BrandMark()
                    .frame(width: 22, height: 22)
                    .accessibilityHidden(true)
                Text("EstrichONE").font(.subheadline.weight(.bold)).foregroundStyle(.white)
                Spacer()
                if let server = model.server {
                    Text(server.info.serverName).font(.caption).foregroundStyle(.white.opacity(0.75)).lineLimit(1)
                }
            }
            if let user = model.active?.user {
                HStack(spacing: 12) {
                    Initials(text: LocalUser.initials(of: user.displayName))
                        .overlay(Circle().strokeBorder(.white.opacity(0.6), lineWidth: 1.5))
                    VStack(alignment: .leading, spacing: 1) {
                        Text(greeting).font(.caption).foregroundStyle(.white.opacity(0.8))
                        Text(user.displayName).font(.title3.weight(.semibold)).foregroundStyle(.white)
                            .accessibilityIdentifier("currentUserName")
                        if let device = model.deviceRegistration {
                            Text(device.name).font(.caption2).foregroundStyle(.white.opacity(0.7))
                        }
                    }
                    Spacer()
                    if model.isPoolDevice {
                        Button { model.lock() } label: {
                            Label("Wechseln", systemImage: "person.2")
                        }
                        .overlayControlStyle()
                        .tint(.white)
                        .accessibilityIdentifier("switchUserButton")
                    }
                }
            }
        }
        .padding(.horizontal)
        .padding(.top, 8)
        .padding(.bottom, 16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            LinearGradient(colors: [Theme.navy, Theme.navyLight], startPoint: .topLeading, endPoint: .bottomTrailing)
                .ignoresSafeArea(edges: .top)
        )
    }

    private var greeting: String {
        switch Calendar.current.component(.hour, from: .now) {
        case 4..<11: "Guten Morgen"
        case 11..<17: "Guten Tag"
        default: "Guten Abend"
        }
    }
}

struct SectionTitle: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.footnote.weight(.semibold))
            .textCase(.uppercase)
            .foregroundStyle(Theme.textSecondary)
            .padding(.leading, 4)
            .accessibilityAddTraits(.isHeader)
    }
}

/// „Mein Einsatz heute“: aus der Planung des Büros; offline mit Hinweis auf den Stand.
struct TodayAssignments: View {
    let snapshot: MyPlanning.Snapshot?
    let open: (UUID) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                SectionTitle("Mein Einsatz heute")
                Spacer()
                if let snapshot, !snapshot.isCurrent {
                    Text("Stand \(snapshot.loadedAt.formatted(date: .abbreviated, time: .shortened))")
                        .font(.caption2).foregroundStyle(Theme.textSecondary)
                }
            }
            let today = snapshot?.assignments(on: CalendarDay.string(.now)) ?? []
            if today.isEmpty {
                Label(snapshot == nil ? "Noch keine Planung geladen." : "Heute ist für Sie nichts eingeplant.", systemImage: "calendar")
                    .font(.subheadline)
                    .foregroundStyle(Theme.textSecondary)
                    .padding(.horizontal, 14)
                    .frame(maxWidth: .infinity, minHeight: 48, alignment: .leading)
                    .card()
            }
            ForEach(today) { assignment in
                AssignmentCard(assignment: assignment) { open(assignment.bauakteId) }
            }
        }
        .accessibilityIdentifier("todayAssignments")
    }
}

struct AssignmentCard: View {
    let assignment: Assignment
    let open: () -> Void

    var body: some View {
        Button(action: open) {
            HStack(alignment: .top, spacing: 12) {
                VStack(spacing: 0) {
                    Text(assignment.startText ?? "–").font(.subheadline.monospacedDigit().weight(.bold)).foregroundStyle(Theme.navyText)
                    Text("Uhr").font(.caption2).foregroundStyle(Theme.textSecondary)
                }
                .frame(width: 46)
                VStack(alignment: .leading, spacing: 3) {
                    Text([assignment.bauakteNumber, assignment.bauakteTitle].compactMap { $0 }.joined(separator: " · "))
                        .font(.subheadline.weight(.semibold)).foregroundStyle(.primary)
                    Text(assignment.activity).font(.subheadline)
                    if let address = assignment.bauakteAddress {
                        Label(address, systemImage: "mappin.and.ellipse").font(.caption).foregroundStyle(Theme.textSecondary)
                    }
                    let crew = [assignment.crewName, assignment.resources.map(\.name).joined(separator: ", ")]
                        .compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " · ")
                    if !crew.isEmpty {
                        Label(crew, systemImage: "person.3").font(.caption).foregroundStyle(Theme.textSecondary)
                    }
                    ForEach(assignment.warnings, id: \.self) { warning in
                        Label(warning, systemImage: "exclamationmark.triangle").font(.caption).foregroundStyle(Theme.hint)
                    }
                }
                Spacer(minLength: 0)
                Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
            }
            .padding(12)
            .card()
            .overlay(alignment: .leading) {
                UnevenRoundedRectangle(topLeadingRadius: Theme.cardRadius, bottomLeadingRadius: Theme.cardRadius)
                    .fill(Theme.accent).frame(width: 4)
            }
        }
        .buttonStyle(.plain)
    }
}

/// Meine Woche: Einsätze der nächsten 7 Tage aus der Planung.
struct MyWeekView: View {
    @Environment(AppModel.self) private var model
    @State private var snapshot: MyPlanning.Snapshot?

    var body: some View {
        List {
            if let snapshot, !snapshot.isCurrent {
                Label("Ohne Verbindung – Stand \(snapshot.loadedAt.formatted(date: .abbreviated, time: .shortened))", systemImage: "wifi.slash")
                    .foregroundStyle(Theme.textSecondary)
            }
            ForEach(days, id: \.self) { day in
                let items = snapshot?.assignments(on: day) ?? []
                Section(CalendarDay.date(day)?.formatted(.dateTime.weekday(.wide).day().month(.wide)) ?? day) {
                    if items.isEmpty {
                        Text("Nichts eingeplant").foregroundStyle(Theme.textSecondary)
                    }
                    ForEach(items) { assignment in
                        NavigationLink(value: HomeRoute.bauakte(assignment.bauakteId)) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("\(assignment.startText ?? "") \(assignment.bauakteTitle)").font(.headline)
                                Text(assignment.activity).font(.subheadline)
                            }
                        }
                    }
                }
            }
        }
        .navigationTitle("Meine Woche")
        .task {
            if let store = model.active?.sync?.store {
                snapshot = await MyPlanning(store: store).load(using: model.active?.client)
            }
        }
    }

    private var days: [String] {
        (0..<7).compactMap { Calendar.current.date(byAdding: .day, value: $0, to: .now) }.map { CalendarDay.string($0) }
    }
}

/// Meine Aufgaben: offen, mir zugewiesen oder von mir ohne Zuständigen angelegt.
struct MyTasksView: View {
    @Environment(AppModel.self) private var model
    @State private var items: [WorkItem] = []
    @State private var titles: [UUID: String] = [:]

    var body: some View {
        List {
            if items.isEmpty {
                ContentUnavailableView("Keine offenen Aufgaben", systemImage: "checkmark.circle", description: Text("Alles erledigt."))
            }
            ForEach(items) { item in
                WorkItemRow(item: item, showsBauakte: item.bauakteId.flatMap { titles[$0] }) {
                    Task { await toggle(item) }
                }
            }
        }
        .navigationTitle("Meine Aufgaben")
        .task(id: model.active?.sync?.revision) { await reload() }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        items = (try? await service.myWorkItems()) ?? []
        let all = (try? await service.bauakten()) ?? []
        titles = Dictionary(all.map { ($0.id, [$0.number, $0.title].compactMap { $0 }.joined(separator: " · ")) }, uniquingKeysWith: { a, _ in a })
    }

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }
}
