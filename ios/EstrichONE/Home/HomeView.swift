import CoreLocation
import EstrichCore
import SwiftUI

/// „Heute“ – der Arbeitsplatz für den Tag, als ruhige Standardliste: oben Datum und dezent der Abgleich, Hinweise nur bei
/// Bedarf, dann alle Einsätze des Tages – der laufende bzw. nächste aufgeklappt mit Kennzahlen (Mängel, Aufgaben,
/// Belegreife, Wetter), Entfernung und vier großen Handgriffen, die übrigen kompakt mit Route und Anruf. Wischen und langes
/// Drücken bieten dieselben Handgriffe. Darunter „Überblick“ (Meine Aufgaben, Wer fährt wohin, Meine Woche), morgen und
/// zuletzt geöffnet. Lokale Daten erscheinen sofort, frische Planung kommt im Hintergrund dazu (offline lesbar).
struct HomeView: View {
    /// Öffnet das Blatt „Erfassen“.
    var openCapture: () -> Void = {}

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.shotScene) private var shotScene
    @Environment(\.dynamicTypeSize) private var typeSize
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
    /// Kennzahlen der heutigen Baustellen (lokal).
    @State private var glances: [UUID: SiteGlance] = [:]
    /// Vom Nutzer aufgeklappter Einsatz; sonst der laufende bzw. nächste.
    @State private var expandedJob: UUID?
    /// Uhr für „Jetzt“/„in 2 Std.“ (minütlich; im Screenshot-Modus fest 9:41).
    @State private var now = Date.now
    /// Zuletzt bekannter Standort (nur wenn schon erlaubt) für die Entfernung.
    @State private var here: CLLocation?
    @Environment(\.openURL) private var openURL

    private var isWide: Bool { sizeClass == .regular }

    /// Module der Suite (zuletzt bekannter Stand; ohne Angabe alles an).
    private var modules: AppModules { model.active?.sync?.modules ?? .allOn }

    private var jobs: [TodayJob] {
        TodayJob.make(assignments: planning?.assignments(on: CalendarDay.string(.now)) ?? [], board: dispatch?.board, bauakten: bauakten)
    }

    private func phase(of job: TodayJob) -> TodayGlance.Phase {
        TodayGlance.phase(start: job.startText, end: job.endText, now: now)
    }

    /// Aufgeklappt: die Wahl des Nutzers, sonst der laufende bzw. nächste Einsatz.
    private func isExpanded(_ job: TodayJob, in list: [TodayJob]) -> Bool {
        if list.count == 1 { return true }
        if let expandedJob, list.contains(where: { $0.id == expandedJob }) { return expandedJob == job.id }
        guard let index = TodayGlance.focusIndex(list.map(phase(of:))) else { return false }
        return list[index].id == job.id
    }

    private func distance(to job: TodayJob) -> String? {
        guard let here, let latitude = job.latitude, let longitude = job.longitude else { return nil }
        return TodayGlance.distanceText(meters: TodayGlance.distance(fromLatitude: here.coordinate.latitude, longitude: here.coordinate.longitude,
                                                                     toLatitude: latitude, longitude: longitude))
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
                    // Hinweise stehen im Abschnitt des Einsatzes (unter dem Datum); ohne Planung in einem eigenen Abschnitt.
                    // So entsteht ohne Hinweis keine leere Lücke oben.
                    if modules.isOn(AppModules.planning) { todaySection } else { Section { noticeRows } }
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
                .task(id: loaded) {
                    // Screenshot-Modus: „heute-zweiter“ klappt den zweiten Einsatz auf, „heute-unten“ zeigt den unteren Teil.
                    if loaded, shotScene == "heute-zweiter" { expandedJob = jobs.dropFirst().first?.id }
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
            .task(id: shotScene) {
                // Uhr für „Jetzt“/„in 2 Std.“: minütlich; für Screenshots fest 9:41 (wie die Statusleiste).
                while !Task.isCancelled {
                    now = shotScene == nil ? .now : (Calendar.current.date(bySettingHour: 9, minute: 41, second: 0, of: .now) ?? .now)
                    try? await Task.sleep(for: .seconds(60))
                }
            }
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
    private var noticeRows: some View {
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
            noticeRows
            if !loaded {
                TodayJobCard.placeholder
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
            } else if jobs.isEmpty {
                // Leerzustand: eine Zeile (Meine Woche steht direkt darunter).
                VStack(alignment: .leading, spacing: 2) {
                    Text("Heute ist nichts für Sie eingeplant.")
                    if let next = tomorrow.first {
                        Text("Morgen \([next.startText, next.bauakteTitle].compactMap { $0 }.joined(separator: ", "))")
                            .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                    }
                }
                .padding(.vertical, Space.xs)
            } else {
                let list = jobs
                ForEach(list) { job in
                    jobRow(job, in: list)
                }
            }
        } header: {
            // Datum als Überschrift der Einsätze, rechts dezent der Abgleich (Tipp öffnet ihn); bei altem Stand mit Uhrzeit.
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                Text(Date.now.formatted(.dateTime.weekday(.wide).day().month(.wide)))
                    .accessibilityIdentifier("todayDate")
                Spacer(minLength: Space.s)
                syncHint
            }
            .textCase(nil)
        }
        .accessibilityIdentifier("todayAssignments")
    }

    /// Ein Einsatz als Karte; Wischen nach rechts: Foto, Mangel – nach links: Route, Anrufen.
    private func jobRow(_ job: TodayJob, in list: [TodayJob]) -> some View {
        let canCapture = bauakten[job.bauakteId] != nil
        return TodayJobCard(job: job, glance: glances[job.bauakteId], distance: distance(to: job), phase: phase(of: job),
                            expanded: isExpanded(job, in: list), canCapture: canCapture,
                            onExpand: { withAnimation(.snappy) { expandedJob = job.id } }) { action in
            perform(action, for: job)
        }
        .listRowInsets(EdgeInsets(top: Space.xs, leading: 0, bottom: Space.xs, trailing: 0))
        .listRowBackground(Color.clear)
        .listRowSeparator(.hidden)
        // Wischen: nach rechts Foto bzw. Mangel, nach links Route bzw. Anrufen.
        .swipeActions(edge: .leading, allowsFullSwipe: true) {
            if canCapture {
                Button("Foto", systemImage: "camera") { perform(.photo, for: job) }
                    .tint(Theme.accentFill)
                Button("Mangel", systemImage: "exclamationmark.triangle") { perform(.defect, for: job) }
                    .tint(Theme.warning)
            }
        }
        .swipeActions(edge: .trailing, allowsFullSwipe: true) {
            if job.route != nil {
                Button("Route", systemImage: "arrow.triangle.turn.up.right.diamond") { perform(.route, for: job) }
                    .tint(Theme.accentFill)
            }
            if job.phone != nil {
                Button("Anrufen", systemImage: "phone") { perform(.call, for: job) }
                    .tint(Theme.success)
            }
        }
    }

    private func perform(_ action: TodayJobCard.Action, for job: TodayJob) {
        let bauakte = bauakten[job.bauakteId]
        switch action {
        case .open: path.append(.bauakte(job.bauakteId))
        case .photo: cameraFor = bauakte
        case .diary: diaryFor = bauakte
        case .defect: defectFor = bauakte
        case .route: if let url = job.route { openURL(url) }
        case .call: if let url = job.phone { openURL(url) }
        }
    }

    /// Abgleich in einer Zeile: Symbol und kurzer Text, bei Problemen rot. Alter Planungsstand mit Uhrzeit.
    private var syncHint: some View {
        let state = syncState
        let stale: String? = planning.flatMap { $0.isCurrent ? nil : "Stand \($0.loadedAt.shortStamp)" }
        let spoken = "Abgleich: \(state.text)" + (stale.map { ", Planung \($0)" } ?? "")
        return Button { path.append(.sync) } label: {
            Group {
                // Sehr große Schrift: nur das Symbol, damit das Datum Platz hat.
                if typeSize.isAccessibilitySize {
                    Image(systemName: state.symbol)
                } else {
                    Label(stale ?? state.text, systemImage: state.symbol)
                        .labelStyle(TightLabelStyle())
                        .lineLimit(1)
                }
            }
            .foregroundStyle(state.problem ? Theme.danger : Theme.textSecondary)
        }
        .buttonStyle(.plain)
        .font(Typo.meta)
        .textCase(nil)
        .disabled(model.active?.sync == nil)
        .accessibilityLabel(spoken)
        .accessibilityIdentifier("todaySyncHint")
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
                                Text([item.activity, item.crewName].compactMap { $0 }.joined(separator: ", "))
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
                    // Weniger Tippen: Foto direkt aus der Liste (Wischen nach rechts oder lange drücken).
                    .swipeActions(edge: .leading, allowsFullSwipe: true) {
                        Button("Foto", systemImage: "camera") { cameraFor = bauakte }
                            .tint(Theme.accent)
                    }
                    .contextMenu {
                        Button("Foto aufnehmen", systemImage: "camera") { cameraFor = bauakte }
                        Button("Bautagebuch-Eintrag", systemImage: "book") { diaryFor = bauakte }
                        if let url = MapsLink.url(for: bauakte) {
                            Link(destination: url) { Label("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond") }
                        }
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
        here = shotScene != nil ? CLLocation(latitude: 48.5468, longitude: 12.1350) : LocationProvider.recentLocation()
        var ids = Set((planning?.assignments(on: CalendarDay.string(.now)) ?? []).map(\.bauakteId))
        ids.formUnion(dispatch?.board?.mySiteIds ?? [])
        ids.formUnion(items.compactMap(\.bauakteId))
        var found: [UUID: Bauakte] = [:]
        for id in ids {
            if let bauakte = try? await service.bauakte(id) { found[id] = bauakte }
        }
        bauakten = found

        // Kennzahlen der heutigen Baustellen: offene Mängel und Aufgaben, Belegreife, Wetter laut Tagebuch.
        let today = CalendarDay.string(.now)
        var sites: [UUID: SiteGlance] = [:]
        for id in Set(jobs.map(\.bauakteId)) {
            sites[id] = SiteGlance(items: (try? await service.workItems(of: id)) ?? [], rooms: (try? await service.rooms(of: id)) ?? [],
                                   inspections: (try? await service.inspections(of: id)) ?? [], diary: (try? await service.diary(of: id)) ?? [],
                                   today: today)
        }
        glances = sites
    }

    static func firstName(_ name: String) -> String {
        name.split(separator: " ").first.map(String.init) ?? name
    }
}
