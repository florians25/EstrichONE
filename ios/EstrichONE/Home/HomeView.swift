import EstrichCore
import SwiftUI

/// Startseite im Stil der Handwerker-Apps (Vorbild Streit): Kopf in Navy mit Person und Gerät, „Mein Einsatz heute“,
/// darunter Kacheln für die häufigsten Abläufe. Große Flächen, gut mit Arbeitshandschuhen zu treffen.
struct HomeView: View {
    let openTab: (MainTabView.Tab) -> Void

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
                VStack(alignment: .leading, spacing: 20) {
                    HomeHeader()
                    if let sync = model.active?.sync {
                        SyncStatusBar(sync: sync)
                            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                            .padding(.horizontal)
                    }
                    TodayAssignments(snapshot: planning) { path.append(.bauakte($0)) }
                        .padding(.horizontal)
                    tiles
                        .padding(.horizontal)
                    if !recent.isEmpty {
                        VStack(alignment: .leading, spacing: 8) {
                            SectionTitle("Zuletzt geöffnet")
                            ForEach(recent) { bauakte in
                                Button { path.append(.bauakte(bauakte.id)) } label: {
                                    BauakteRow(bauakte: bauakte)
                                        .padding(12)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                        .background(.background, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                                }
                                .buttonStyle(.plain)
                            }
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

    private var tiles: some View {
        VStack(alignment: .leading, spacing: 10) {
            SectionTitle("Baustelle")
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 100, maximum: 160), spacing: 12)], spacing: 12) {
                HomeTile(title: "Neue Bauakte", symbol: "folder.badge.plus", accent: Theme.success) { creating = true }
                    .accessibilityIdentifier("newBauakteTile")
                HomeTile(title: "Bauakten", symbol: "building.2") { openTab(.bauakten) }
                HomeTile(title: "Fotos", symbol: "camera") { openTab(.capture) }
                HomeTile(title: "Meine Aufgaben", symbol: "checklist", badge: openTasks) { path.append(.tasks) }
                    .accessibilityIdentifier("myTasksTile")
                HomeTile(title: "Meine Woche", symbol: "calendar") { path.append(.week) }
                    .accessibilityIdentifier("myWeekTile")
                HomeTile(title: "Synchronisation", symbol: "arrow.triangle.2.circlepath") { path.append(.sync) }
                HomeTile(title: "Problem melden", symbol: "exclamationmark.bubble", accent: Theme.hint) { reporting = true }
            }
        }
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

/// Kopf in Navy: Firma, Person, Gerät; Benutzerwechsel auf Poolgeräten als Glas-Schaltfläche.
struct HomeHeader: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Image(systemName: "square.stack.3d.up.fill").font(.title3).foregroundStyle(.white.opacity(0.9))
                Text("EstrichONE").font(.headline).foregroundStyle(.white)
                Spacer()
                if let server = model.server {
                    Text(server.info.serverName).font(.footnote).foregroundStyle(.white.opacity(0.75)).lineLimit(1)
                }
            }
            if let user = model.active?.user {
                HStack(spacing: 12) {
                    Initials(text: LocalUser.initials(of: user.displayName))
                        .overlay(Circle().strokeBorder(.white.opacity(0.7), lineWidth: 1.5))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(greeting).font(.subheadline).foregroundStyle(.white.opacity(0.8))
                        Text(user.displayName).font(.title2.weight(.semibold)).foregroundStyle(.white)
                            .accessibilityIdentifier("currentUserName")
                        if let device = model.deviceRegistration {
                            Text(device.name).font(.caption).foregroundStyle(.white.opacity(0.7))
                        }
                    }
                    Spacer()
                    if model.isPoolDevice {
                        Button { model.lock() } label: {
                            Label("Wechseln", systemImage: "person.2").font(.subheadline.weight(.semibold))
                        }
                        .glassOrBordered()
                        .tint(.white)
                        .accessibilityIdentifier("switchUserButton")
                    }
                }
            }
        }
        .padding(.horizontal)
        .padding(.top, 12)
        .padding(.bottom, 22)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            LinearGradient(colors: [Theme.navy, Color(hex: 0x12396F)], startPoint: .topLeading, endPoint: .bottomTrailing)
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

/// Kachel: weißes Feld, Linien-Symbol in Navy, Beschriftung darunter (wie die Programmkacheln im Desktop).
struct HomeTile: View {
    let title: String
    let symbol: String
    var accent: Color = Theme.navy
    var badge = 0
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 10) {
                Image(systemName: symbol)
                    .font(.system(size: 28, weight: .light))
                    .foregroundStyle(accent == Theme.navy ? Theme.navyText : accent)
                    .frame(height: 34)
                Text(title)
                    .font(.footnote.weight(.medium))
                    .multilineTextAlignment(.center)
                    .lineLimit(2, reservesSpace: true)
                    .foregroundStyle(.primary)
            }
            .frame(maxWidth: .infinity, minHeight: 104)
            .padding(.horizontal, 6)
            .background(.background, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Theme.line.opacity(0.6)))
            .overlay(alignment: .topTrailing) {
                if badge > 0 {
                    Text("\(badge)")
                        .font(.caption2.weight(.bold)).monospacedDigit()
                        .foregroundStyle(.white)
                        .padding(.horizontal, 7).padding(.vertical, 3)
                        .background(Theme.error, in: Capsule())
                        .padding(8)
                }
            }
            .shadow(color: .black.opacity(0.05), radius: 2, y: 1)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(badge > 0 ? "\(title), \(badge) offen" : title)
    }
}

struct SectionTitle: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.subheadline.weight(.bold))
            .foregroundStyle(Theme.navyText)
            .accessibilityAddTraits(.isHeader)
    }
}

/// „Mein Einsatz heute“: aus der Planung des Büros; offline mit Hinweis auf den Stand.
struct TodayAssignments: View {
    let snapshot: MyPlanning.Snapshot?
    let open: (UUID) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                SectionTitle("Mein Einsatz heute")
                Spacer()
                if let snapshot, !snapshot.isCurrent {
                    Text("Stand \(snapshot.loadedAt.formatted(date: .abbreviated, time: .shortened))")
                        .font(.caption).foregroundStyle(Theme.textSecondary)
                }
            }
            let today = snapshot?.assignments(on: CalendarDay.string(.now)) ?? []
            if today.isEmpty {
                Label(snapshot == nil ? "Noch keine Planung geladen." : "Heute ist für Sie nichts eingeplant.", systemImage: "calendar")
                    .font(.subheadline)
                    .foregroundStyle(Theme.textSecondary)
                    .padding(14)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(.background, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
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
                VStack(spacing: 2) {
                    Text(assignment.startText ?? "–").font(.headline.monospacedDigit()).foregroundStyle(Theme.navyText)
                    Text("Uhr").font(.caption2).foregroundStyle(Theme.textSecondary)
                }
                .frame(width: 52)
                VStack(alignment: .leading, spacing: 4) {
                    Text([assignment.bauakteNumber, assignment.bauakteTitle].compactMap { $0 }.joined(separator: " · "))
                        .font(.headline).foregroundStyle(.primary)
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
                Image(systemName: "chevron.right").foregroundStyle(Theme.textSecondary)
            }
            .padding(14)
            .background(.background, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .overlay(alignment: .leading) {
                UnevenRoundedRectangle(topLeadingRadius: 14, bottomLeadingRadius: 14).fill(Theme.navy).frame(width: 5)
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

extension View {
    /// Liquid Glass ab iOS 26, sonst eine umrandete Schaltfläche.
    @ViewBuilder
    func glassOrBordered() -> some View {
        #if compiler(>=6.2)
        if #available(iOS 26, *) {
            self.buttonStyle(.glass)
        } else {
            self.buttonStyle(.bordered)
        }
        #else
        self.buttonStyle(.bordered)
        #endif
    }
}
