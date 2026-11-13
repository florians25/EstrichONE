import EstrichCore
import SwiftUI

/// „Heute“ – der Arbeitsplatz für den Tag, als ruhige Standardliste: Hinweise nur bei Bedarf, der Einsatz mit Adresse,
/// Ansprechpartner und „Foto“, darunter „Überblick“ (Meine Aufgaben, Wer fährt wohin, Meine Woche mit Zahl), morgen und
/// zuletzt geöffnet. Lokale Daten erscheinen sofort, frische Planung kommt im Hintergrund dazu (offline lesbar).
struct HomeView: View {
    /// Öffnet das Blatt „Erfassen“.
    var openCapture: () -> Void = {}

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.shotScene) private var shotScene
    @State private var path: [HomeRoute] = []
    @State private var planning: MyPlanning.Snapshot?
    @State private var dispatch: DispatchData.Day?
    @State private var items: [WorkItem] = []
    @State private var recent: [Bauakte] = []
    @State private var bauakten: [UUID: Bauakte] = [:]
    @State private var loaded = false
    @State private var cameraFor: Bauakte?
    @State private var diaryFor: Bauakte?
    @State private var defectFor: Bauakte?
    @State private var toast: String?
    @State private var push = PushCenter.shared

    private var isWide: Bool { sizeClass == .regular }

    /// Module der Suite (zuletzt bekannter Stand; ohne Angabe alles an).
    private var modules: AppModules { model.active?.sync?.modules ?? .allOn }

    private var jobs: [TodayJob] {
        TodayJob.make(assignments: planning?.assignments(on: CalendarDay.string(.now)) ?? [], board: dispatch?.board, bauakten: bauakten)
    }

    private var tomorrow: [Assignment] {
        let day = Calendar.current.date(byAdding: .day, value: 1, to: .now) ?? .now
        return planning?.assignments(on: CalendarDay.string(day)) ?? []
    }

    private var weekCount: Int {
        let today = CalendarDay.string(.now)
        return planning?.range.assignments.filter { $0.day >= today }.count ?? 0
    }

    var body: some View {
        NavigationStack(path: $path) {
            ScrollViewReader { proxy in
                List {
                    noticeRows
                    if modules.isOn(AppModules.planning) { todaySection }
                    overviewSection
                    if modules.isOn(AppModules.planning) { tomorrowSection }
                    recentSection
                    Color.clear.frame(height: 1)
                        .listRowBackground(Color.clear)
                        .listRowInsets(EdgeInsets())
                        .id("bottom")
                }
                .listStyle(.insetGrouped)
                .listSectionSpacing(.compact)
                .contentMargins(.horizontal, isWide ? Space.xl : 0, for: .scrollContent)
                .task(id: loaded) {
                    // Screenshot-Modus: unteren Teil zeigen.
                    guard loaded, shotScene == "heute-unten" else { return }
                    try? await Task.sleep(for: .milliseconds(300))
                    proxy.scrollTo("bottom", anchor: .bottom)
                }
            }
            .navigationTitle("Heute")
            .toolbar {
                // Eine sichtbare Aktion (Benachrichtigungen); Abgleich, Chat und Teams im Menü „…“.
                ToolbarItem(placement: .primaryAction) { moreMenu }
                ToolbarItem(placement: .primaryAction) {
                    NotificationBell(count: shotScene == "heute-glocke" ? 128 : push.unreadCount) { path.append(.inbox) }
                }
            }
            .rootToolbar(showsMaintenance: false)
            .navigationDestination(for: HomeRoute.self) { route in
                switch route {
                case let .bauakte(id): BauakteDetailView(bauakteId: id)
                case .tasks: MyTasksView()
                case .week: MyWeekView()
                case .sync: if let sync = model.active?.sync { SyncView(sync: sync) }
                case .dispatch: DispatchView()
                case .inbox: NotificationInboxView()
                case .chat: ChatListView()
                }
            }
            .task(id: model.active?.sync?.revision) { await reload() }
            .refreshable {
                await model.active?.sync?.run()
                await reload()
            }
            .fullScreenCover(item: $cameraFor) { bauakte in
                CameraView(bauakte: bauakte) { count in
                    cameraFor = nil
                    if count > 0 { toast = count == 1 ? "Foto gespeichert" : "\(count) Fotos gespeichert" }
                }
            }
            .sheet(item: $diaryFor) { bauakte in
                NewDiaryEntryView(bauakteId: bauakte.id) { diaryFor = nil }
            }
            .sheet(item: $defectFor) { bauakte in
                NewWorkItemView(bauakteId: bauakte.id, initialKind: .defect) { defectFor = nil }
            }
            .toast($toast)
        }
    }

    // MARK: Kopf und Hinweise

    /// Hinweise nur, wenn es etwas zu sagen gibt (Wartung, Übertragungsfehler, offline, Systemmeldungen, neue Version).
    /// Der Demomodus ist oben links gekennzeichnet („Demo“) und wird hier nicht noch einmal erklärt.
    @ViewBuilder
    private var noticeRows: some View {
        Section {
            Group {
                if model.updates.showsBanner {
                    AppUpdateNotice(center: model.updates)
                }
                if let sync = model.active?.sync {
                    if let message = sync.maintenanceMessage {
                        Callout(tone: .warning, title: "Wartung des Servers",
                                message: "\(message) Sie können weiterarbeiten. Erfasste Daten bleiben auf dem Gerät und werden danach übertragen.",
                                symbol: "wrench.and.screwdriver.fill")
                    } else if sync.summary?.hasProblems == true {
                        Callout(tone: .danger, title: "Übertragung prüfen", message: "Einige Änderungen wurden nicht übernommen.",
                                actionTitle: "Details ansehen") { path.append(.sync) }
                    } else if sync.isOffline, !sync.isReadOnly, !sync.connection.isLongOffline(at: .now) {
                        Callout(tone: .neutral, title: "Offline", message: "Alles wird auf dem Gerät gespeichert und übertragen, sobald wieder Verbindung besteht.",
                                symbol: "wifi.slash")
                    }
                    SystemNoticesBanner(sync: sync)
                }
                if let notice = model.active?.sync?.teams?.rolloutNotice, !notice.isEmpty {
                    TeamsRolloutNotice(text: notice)
                }
            }
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets(top: Space.xs, leading: 0, bottom: Space.xs, trailing: 0))
            .listRowSeparator(.hidden)
        } header: {
            Text(Date.now.formatted(.dateTime.weekday(.wide).day().month(.wide)))
                .accessibilityIdentifier("todayDate")
        }
    }

    /// „…“ oben rechts: Abgleich (mit Stand), Chat bzw. Microsoft Teams.
    private var moreMenu: some View {
        let state = syncState
        return Menu {
            Button { path.append(.sync) } label: {
                Label("Abgleich: \(state.text)", systemImage: state.symbol)
            }
            .accessibilityIdentifier("syncStatusIcon")
            // Modus „Microsoft Teams“: statt Chat „In Teams öffnen“ (das Archiv bleibt unter Mehr erreichbar).
            if let chat = model.active?.sync?.chat, chat.isAvailable, !chat.readOnly {
                Button { path.append(.chat) } label: {
                    Label(chat.unreadCount > 0 ? "Chat (\(chat.unreadCount) neu)" : "Chat", systemImage: "bubble.left.and.bubble.right")
                }
                .accessibilityIdentifier("chatButton")
            }
            if let teams = model.active?.sync?.teams, teams.available {
                OpenInTeamsButton(targets: teams.homeTargets)
            }
        } label: {
            Label("Weitere Aktionen", systemImage: state.problem ? "exclamationmark.icloud" : "ellipsis.circle")
                .foregroundStyle(state.problem ? Theme.danger : Theme.accent)
        }
        .accessibilityLabel("Weitere Aktionen, Abgleich: \(state.text)")
        .accessibilityIdentifier("homeMoreMenu")
    }

    private var syncState: (symbol: String, text: String, problem: Bool) {
        guard let sync = model.active?.sync else { return ("icloud.slash", "Nicht verbunden", false) }
        if sync.isRunning { return ("arrow.triangle.2.circlepath", "Wird abgeglichen", false) }
        if sync.maintenanceMessage != nil { return ("wrench.and.screwdriver", "Wartung", false) }
        if sync.isOffline { return ("icloud.slash", "Offline", false) }
        if sync.summary?.hasProblems == true { return ("exclamationmark.icloud", "Bitte prüfen", true) }
        if let summary = sync.summary, !summary.isSettled { return ("icloud.and.arrow.up", "Wird übertragen", false) }
        return ("checkmark.icloud", "Alles übertragen", false)
    }

    // MARK: Einsatz heute

    private var todaySection: some View {
        Section {
            if !loaded {
                TodayJobCard.placeholder
                    .listRowInsets(EdgeInsets())
            } else if jobs.isEmpty {
                // Leerzustand: eine Zeile (Meine Woche steht direkt darunter).
                VStack(alignment: .leading, spacing: 2) {
                    Text("Heute ist nichts für Sie eingeplant.")
                    if let next = tomorrow.first {
                        Text("Morgen \([next.startText, next.bauakteTitle].compactMap { $0 }.joined(separator: " · "))")
                            .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                    }
                }
                .padding(.vertical, Space.xs)
            } else {
                ForEach(jobs) { job in
                    TodayJobCard(job: job, canCapture: bauakten[job.bauakteId] != nil) { action in
                        perform(action, for: job)
                    }
                    .listRowInsets(EdgeInsets())
                }
            }
        } header: {
            HStack {
                Text(jobs.count > 1 ? "Meine Einsätze" : "Mein Einsatz")
                Spacer()
                if let planning, !planning.isCurrent {
                    Text("Stand \(planning.loadedAt.shortStamp)")
                }
            }
        }
        .accessibilityIdentifier("todayAssignments")
    }

    private func perform(_ action: TodayJobCard.Action, for job: TodayJob) {
        let bauakte = bauakten[job.bauakteId]
        switch action {
        case .open: path.append(.bauakte(job.bauakteId))
        case .photo: cameraFor = bauakte
        case .diary: diaryFor = bauakte
        case .defect: defectFor = bauakte
        }
    }

    // MARK: Überblick

    /// Die drei Wege des Tages als normale Listenzeilen mit Zahl rechts: Meine Aufgaben, Wer fährt wohin, Meine Woche.
    @ViewBuilder
    private var overviewSection: some View {
        let defects = items.filter { $0.kind == .defect }.count
        let showsTasks = modules.anyOn(AppModules.tasks, AppModules.defects)
        let showsPlanning = modules.isOn(AppModules.planning)
        if showsTasks || showsPlanning {
            Section("Überblick") {
                // Systemeinstellungen › Module: Zeilen abgeschalteter Module entfallen.
                if showsTasks {
                    NavigationLink(value: HomeRoute.tasks) {
                        overviewRow("Meine Aufgaben", symbol: "checklist", value: loaded ? "\(items.count)" : "–",
                                    detail: defects > 0 ? (defects == 1 ? "davon 1 Mangel" : "davon \(defects) Mängel") : nil)
                    }
                    .accessibilityIdentifier("myTasksTile")
                }
                if showsPlanning {
                    NavigationLink(value: HomeRoute.dispatch) {
                        overviewRow("Wer fährt wohin", symbol: "car.2", value: dispatch == nil ? "–" : "\(dispatch?.board?.crewGroups.count ?? 0)")
                    }
                    .accessibilityIdentifier("dispatchTile")
                    NavigationLink(value: HomeRoute.week) {
                        overviewRow("Meine Woche", symbol: "calendar", value: planning == nil ? "–" : "\(weekCount)")
                    }
                    .accessibilityIdentifier("myWeekTile")
                }
            }
            .accessibilityIdentifier("quickAccess")
        }
    }

    private func overviewRow(_ title: String, symbol: String, value: String, detail: String? = nil) -> some View {
        HStack {
            Label {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title)
                    if let detail {
                        Text(detail).font(Typo.subhead).foregroundStyle(Theme.warning)
                    }
                }
            } icon: {
                Image(systemName: symbol)
            }
            Spacer()
            Text(value)
                .monospacedDigit()
                .foregroundStyle(Theme.textSecondary)
        }
        .frame(minHeight: Size.minTap)
        .accessibilityElement(children: .combine)
    }

    // MARK: Morgen, zuletzt geöffnet

    @ViewBuilder
    private var tomorrowSection: some View {
        let list = tomorrow
        if loaded, !list.isEmpty, !jobs.isEmpty || list.count > 1 {
            Section("Morgen") {
                ForEach(list.prefix(3)) { item in
                    NavigationLink(value: HomeRoute.bauakte(item.bauakteId)) {
                        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
                            Text(item.startText ?? "–")
                                .monospacedDigit()
                                .foregroundStyle(Theme.textSecondary)
                                .frame(minWidth: 52, alignment: .leading)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(item.bauakteTitle)
                                Text([item.activity, item.crewName].compactMap { $0 }.joined(separator: " · "))
                                    .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                            }
                        }
                        .padding(.vertical, Space.xxs)
                    }
                }
            }
            .accessibilityIdentifier("tomorrowPreview")
        }
    }

    @ViewBuilder
    private var recentSection: some View {
        let shown = Set(jobs.map(\.bauakteId))
        let list = Array(recent.filter { !shown.contains($0.id) }.prefix(3))
        if !list.isEmpty {
            Section("Zuletzt geöffnet") {
                ForEach(list) { bauakte in
                    NavigationLink(value: HomeRoute.bauakte(bauakte.id)) {
                        BauakteRow(bauakte: bauakte)
                    }
                }
            }
        }
    }

    // MARK: Laden

    /// Zuerst der lokale Stand (sofort), dann frische Planung vom Server.
    private func reload() async {
        guard let service = model.active?.service, let store = model.active?.sync?.store else { return }
        let day = CalendarDay.string(.now)
        if planning == nil { planning = await MyPlanning(store: store).cached() }
        if dispatch == nil { dispatch = await DispatchData.cached(day: day, model: model) }
        await loadLocal(service)
        loaded = true

        planning = await MyPlanning(store: store).load(using: model.active?.client) ?? planning
        dispatch = await DispatchData.load(day: day, model: model) ?? dispatch
        await loadLocal(service)
    }

    private func loadLocal(_ service: BauakteService) async {
        items = ((try? await service.myWorkItems()) ?? []).sorted { lhs, rhs in
            // Mängel und Fälliges zuerst.
            if (lhs.kind == .defect) != (rhs.kind == .defect) { return lhs.kind == .defect }
            return (lhs.dueDate ?? "9999") < (rhs.dueDate ?? "9999")
        }
        recent = (try? await service.recentBauakten(limit: 6)) ?? []
        var ids = Set((planning?.assignments(on: CalendarDay.string(.now)) ?? []).map(\.bauakteId))
        ids.formUnion(dispatch?.board?.mySiteIds ?? [])
        ids.formUnion(items.compactMap(\.bauakteId))
        var found: [UUID: Bauakte] = [:]
        for id in ids {
            if let bauakte = try? await service.bauakte(id) { found[id] = bauakte }
        }
        bauakten = found
    }

    static func firstName(_ name: String) -> String {
        name.split(separator: " ").first.map(String.init) ?? name
    }
}
