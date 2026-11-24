import CoreLocation
import EstrichCore
import SwiftUI

/// „Heute“, der Arbeitsplatz für den Tag, nach dem Arbeitsablauf aufgebaut: oben das Datum mit dem Abgleich, eine
/// Abwesende des Tages und alle Mitteilungen als eine kompakte Zeile (Störungen rot), für Planer darunter die
/// Disposition des Tages als eine Zeile. Dann die
/// Arbeit: der laufende bzw. nächste Einsatz als Hauptkarte mit Schnellaktionen (Route, Anrufen, Chat, Bauakte), weitere
/// Einsätze als ruhige Zeilen. Darunter offene Aufgaben und Freigaben, Planung, morgen und zuletzt geöffnet.
/// Lokale Daten erscheinen sofort, frische Planung kommt im Hintergrund dazu.
struct HomeView: View {
    /// Öffnet den Reiter „Chat“ (mit Unterhaltung bzw. der Liste).
    var openChat: (UUID?) -> Void = { _ in }

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.shotScene) private var shotScene
    @Environment(\.dynamicTypeSize) private var typeSize
    @State private var path: [HomeRoute] = []
    @State private var planning: MyPlanning.Snapshot?
    @State private var dispatch: DispatchData.Day?
    /// Recht zur Planung (`planning.manage`, zuletzt bekannter Stand): „Disposition heute“ oben statt „Wer fährt wohin“ unten.
    @State private var canPlan = false
    /// Tagesplan für „Disposition heute“ (nur Planer).
    @State private var todayPlan: DayPlan?
    @State private var todayPlanLoaded = false
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
    /// Leistung, Zugang, angeheftete Notizen und Chat der heutigen Baustellen.
    @State private var extras: [UUID: TodaySiteExtras] = [:]
    /// Offene Urlaubsanträge, über die man entscheiden darf (online).
    @State private var pendingApprovals = 0
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

    /// „Ihr Tag“ aus den Einsätzen, der Disposition und den Flächen der Baustellen.
    private var overview: TodayOverview {
        let list = jobs
        let areas = Dictionary(list.compactMap { job in bauakten[job.bauakteId]?.project.totalArea.map { (job.bauakteId, $0) } },
                               uniquingKeysWith: { first, _ in first })
        return TodayOverview.make(jobs: list.map { TodayOverview.Job(bauakteId: $0.bauakteId, start: $0.startText, crewName: $0.crewName,
                                                                     people: $0.people, resources: $0.resources) },
                                  board: dispatch?.board, areas: areas)
    }

    private func phase(of job: TodayJob) -> TodayGlance.Phase {
        TodayGlance.phase(start: job.startText, end: job.endText, now: now)
    }

    /// Hauptkarte: der laufende, sonst der nächste, sonst der letzte Einsatz des Tages.
    private func focusJob(in list: [TodayJob]) -> TodayJob? {
        TodayGlance.focusIndex(list.map(phase(of:))).map { list[$0] }
    }

    private var chatAvailable: Bool { model.active?.sync?.chat?.isAvailable ?? false }

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
                    headerSection
                    if modules.isOn(AppModules.planning) { jobsSection }
                    tasksSection
                    planningSection
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
                    // Screenshot-Modus: „heute-unten“ zeigt den unteren Teil.
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
                case .approvals: AbsenceOverviewView(opensApprovals: true)
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

    // MARK: Kopf: Datum, Mein Tag, Mitteilungen

    /// Datum mit Abgleich als Überschrift; darin „Mein Tag“ in einer Zeile und Mitteilungen bzw. Störungen als je eine
    /// kompakte Zeile. Ohne Einsätze und ohne Mitteilungen bleibt nur die Überschrift (keine leere Fläche).
    private var headerSection: some View {
        Section {
            if modules.isOn(AppModules.planning), loaded, !jobs.isEmpty {
                dayStrip
            }
            statusRows
            dispatchRow
        } header: {
            HStack(alignment: .firstTextBaseline, spacing: Space.s) {
                Text(Date.now.formatted(.dateTime.weekday(.wide).day().month(.wide)))
                    .accessibilityIdentifier("todayDate")
                Spacer(minLength: Space.s)
                syncHint
            }
            .textCase(nil)
        }
    }

    /// App-Update, Wartung, Übertragungsfehler, Mitteilungen der Verwaltung und Teams, jeweils höchstens eine Zeile.
    /// „Offline“ steht schon rechts neben dem Datum und braucht keine eigene Zeile.
    @ViewBuilder
    private var statusRows: some View {
        if model.updates.showsBanner {
            AppUpdateNotice(center: model.updates)
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets(top: Space.xs, leading: 0, bottom: Space.xs, trailing: 0))
        }
        if let sync = model.active?.sync {
            if let message = sync.maintenanceMessage {
                Button { path.append(.sync) } label: {
                    TodayStatusRow(tone: .warning, symbol: "wrench.and.screwdriver.fill", title: "Wartung des Servers",
                                   subtitle: "\(message) Erfasstes bleibt auf dem Gerät.")
                }
                .buttonStyle(.plain)
                .accessibilityIdentifier("maintenanceRow")
            } else if sync.summary?.hasProblems == true {
                Button { path.append(.sync) } label: {
                    TodayStatusRow(tone: .error, symbol: "exclamationmark.icloud.fill", title: "Übertragung prüfen",
                                   subtitle: "Einige Änderungen wurden nicht übernommen.")
                }
                .buttonStyle(.plain)
                .listRowBackground(Theme.dangerSoft)
                .accessibilityIdentifier("syncProblemRow")
            }
            TodayNoticesRow(sync: sync)
        }
        if let notice = model.active?.sync?.teams?.rolloutNotice, !notice.isEmpty {
            TeamsRolloutNotice(text: notice)
        }
    }

    /// Planer: Disposition des Tages als eine Zeile unter den Mitteilungen („3 Kolonnen, freigegeben“); Tippen öffnet die
    /// Tagesansicht mit der Schnelldisposition.
    @ViewBuilder
    private var dispatchRow: some View {
        if modules.isOn(AppModules.planning), canPlan {
            Button { path.append(.dispatch) } label: {
                HStack(spacing: Space.m) {
                    Image(systemName: "car.2")
                        .font(.body.weight(SymbolWeight.control))
                        .foregroundStyle(Theme.accent)
                        .frame(width: 28)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Disposition heute")
                            .font(Typo.subheadStrong)
                            .foregroundStyle(Theme.textPrimary)
                        Text(todayPlan.map { DispatchSummary.text($0) } ?? (todayPlanLoaded ? "Plan für heute nicht verfügbar" : "Wird geladen"))
                            .font(Typo.meta)
                            .foregroundStyle(Theme.textSecondary)
                            .lineLimit(2)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    if let plan = todayPlan {
                        switch DispatchSummary.tone(plan) {
                        case .success:
                            Image(systemName: "checkmark.seal.fill").foregroundStyle(Theme.success).accessibilityLabel("Freigegeben")
                        case .warning:
                            Image(systemName: "arrow.triangle.2.circlepath").foregroundStyle(Theme.warning).accessibilityLabel("Geändert seit Freigabe")
                        case .neutral:
                            EmptyView()
                        }
                    }
                    Chevron()
                }
                .frame(minHeight: Size.minTap)
                .contentShape(Rectangle())
                .accessibilityElement(children: .combine)
            }
            .buttonStyle(.plain)
            .accessibilityHint("Öffnet die Disposition des Tages")
            .accessibilityIdentifier("dispatchTile")
        }
    }

    /// Wer aus der Kolonne heute fehlt, als eine Zeile unter dem Datum (keine Zählerkarten).
    @ViewBuilder
    private var dayStrip: some View {
        let day = overview
        if !day.absent.isEmpty {
            Label("Heute abwesend: \(day.absent.joined(separator: "; "))", systemImage: "person.crop.circle.badge.xmark")
                .labelStyle(TightLabelStyle())
                .font(Typo.meta)
                .foregroundStyle(Theme.warning)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityIdentifier("todaySummaryAbsent")
        }
    }

    /// „…“ oben rechts: Abgleich (mit Stand) und Microsoft Teams.
    private var moreMenu: some View {
        let state = syncState
        return Menu {
            Button { path.append(.sync) } label: {
                Label("Abgleich: \(state.text)", systemImage: state.symbol)
            }
            .accessibilityIdentifier("syncStatusIcon")
            // Der Chat hat einen eigenen Reiter; Microsoft Teams öffnet die Teams-App.
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

    // MARK: Einsätze

    /// Die Arbeit: Hauptkarte für den laufenden bzw. nächsten Einsatz, weitere Einsätze als Zeilen darunter.
    @ViewBuilder
    private var jobsSection: some View {
        if !loaded {
            Section {
                TodayJobCard.placeholder
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
            }
        } else if jobs.isEmpty {
            Section("Einsätze") {
                // Leerzustand: eine ruhige Zeile mit dem Ausblick auf morgen.
                HStack(spacing: Space.m) {
                    Image(systemName: "calendar")
                        .font(.body.weight(SymbolWeight.content))
                        .foregroundStyle(Theme.textSecondary)
                        .frame(width: 28)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Heute ist nichts für Sie eingeplant.")
                        if let next = tomorrow.first {
                            Text("Morgen \([next.startText, next.bauakteTitle].compactMap { $0 }.joined(separator: ", "))")
                                .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                        }
                    }
                }
                .padding(.vertical, Space.xs)
            }
            .accessibilityIdentifier("todayAssignments")
        } else {
            let list = jobs
            let focus = focusJob(in: list)
            if let focus {
                let focusPhase = phase(of: focus)
                Section(focusPhase == .running ? "Läuft jetzt" : focusPhase == .finished ? "Zuletzt" : "Nächster Einsatz") {
                    jobCard(focus)
                }
                .accessibilityIdentifier("todayAssignments")
            }
            let others = list.filter { $0.id != focus?.id }
            if !others.isEmpty {
                Section("Weitere Einsätze heute (\(others.count))") {
                    ForEach(others) { job in jobRow(job) }
                }
                .accessibilityIdentifier("todayMoreAssignments")
            }
        }
    }

    private func jobCard(_ job: TodayJob) -> some View {
        let canCapture = bauakten[job.bauakteId] != nil
        return TodayJobCard(job: job, glance: glances[job.bauakteId], extras: extras[job.bauakteId], distance: distance(to: job),
                            phase: phase(of: job), canCapture: canCapture, chatAvailable: chatAvailable) { action in
            perform(action, for: job)
        }
        .listRowInsets(EdgeInsets())
        .listRowBackground(Color.clear)
        .listRowSeparator(.hidden)
    }

    /// Weiterer Einsatz als Zeile; Wischen nach rechts: Foto, Mangel; nach links: Route, Anrufen.
    private func jobRow(_ job: TodayJob) -> some View {
        let canCapture = bauakten[job.bauakteId] != nil
        return Button { perform(.open, for: job) } label: {
            TodayJobRow(job: job, phase: phase(of: job), distance: distance(to: job))
        }
        .buttonStyle(.plain)
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
        .contextMenu {
            if job.route != nil {
                Button("Route starten", systemImage: "arrow.triangle.turn.up.right.diamond") { perform(.route, for: job) }
            }
            if job.phone != nil {
                Button(job.contactName.map { "\($0) anrufen" } ?? "Anrufen", systemImage: "phone") { perform(.call, for: job) }
            }
            if canCapture {
                Button("Foto aufnehmen", systemImage: "camera") { perform(.photo, for: job) }
                Button("Bautagebuch-Eintrag", systemImage: "book") { perform(.diary, for: job) }
            }
            Button("Bauakte öffnen", systemImage: "folder") { perform(.open, for: job) }
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
        case .chat: openChat(extras[job.bauakteId]?.chat?.id)
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

    // MARK: Aufgaben, Freigaben, Planung

    /// Offene Aufgaben und Mängel, Anträge zur Freigabe: normale Listenzeilen mit Zahl rechts.
    @ViewBuilder
    private var tasksSection: some View {
        let defects = items.filter { $0.kind == .defect }.count
        let showsTasks = modules.anyOn(AppModules.tasks, AppModules.defects)
        if showsTasks || pendingApprovals > 0 {
            Section("Aufgaben und Freigaben") {
                // Systemeinstellungen, Module: Zeilen abgeschalteter Module entfallen.
                if showsTasks {
                    NavigationLink(value: HomeRoute.tasks) {
                        overviewRow("Meine Aufgaben", symbol: "checklist", value: loaded ? "\(items.count)" : "",
                                    detail: defects > 0 ? (defects == 1 ? "davon 1 Mangel" : "davon \(defects) Mängel") : (loaded && items.isEmpty ? "Alles erledigt" : nil),
                                    detailTint: defects > 0 ? Theme.danger : Theme.textSecondary)
                    }
                    .accessibilityIdentifier("myTasksTile")
                }
                if pendingApprovals > 0 {
                    NavigationLink(value: HomeRoute.approvals) {
                        overviewRow("Anträge genehmigen", symbol: "checkmark.seal", value: "\(pendingApprovals)",
                                    detail: pendingApprovals == 1 ? "1 Antrag wartet auf Sie" : "\(pendingApprovals) Anträge warten auf Sie",
                                    detailTint: Theme.warning)
                    }
                    .accessibilityIdentifier("approvalsTile")
                }
            }
            .accessibilityIdentifier("quickAccess")
        }
    }

    /// Wer fährt wohin (Planer erreichen die Disposition schon oben) und Meine Woche.
    @ViewBuilder
    private var planningSection: some View {
        if modules.isOn(AppModules.planning) {
            Section("Planung") {
                if !canPlan {
                    NavigationLink(value: HomeRoute.dispatch) {
                        overviewRow("Wer fährt wohin", symbol: "car.2", value: dispatch == nil ? "" : "\(dispatch?.board?.crewGroups.count ?? 0)")
                    }
                    .accessibilityIdentifier("dispatchTile")
                }
                NavigationLink(value: HomeRoute.week) {
                    overviewRow("Meine Woche", symbol: "calendar", value: planning == nil ? "" : "\(weekCount)")
                }
                .accessibilityIdentifier("myWeekTile")
            }
        }
    }

    private func overviewRow(_ title: String, symbol: String, value: String, detail: String? = nil,
                             detailTint: Color = Theme.textSecondary) -> some View {
        HStack {
            Label {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title)
                    if let detail {
                        Text(detail).font(Typo.subhead).foregroundStyle(detailTint)
                    }
                }
            } icon: {
                Image(systemName: symbol)
                    .foregroundStyle(Theme.accent)
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
                            Text(item.startText ?? "ganztägig")
                                .monospacedDigit()
                                .foregroundStyle(Theme.textSecondary)
                                .frame(minWidth: 52, alignment: .leading)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(item.bauakteTitle)
                                Text([item.activity, item.crewName].compactMap { $0 }.joined(separator: ", "))
                                    .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                                if let address = item.bauakteAddress {
                                    Text(address)
                                        .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                                        .lineLimit(1)
                                }
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
        canPlan = await MyPermissions(store: store).cached().contains("planning.manage")
        if canPlan, todayPlan == nil { todayPlan = await DayPlanStore.cached(day: day, model: model)?.plan }
        await loadLocal(service)
        loaded = true

        planning = await MyPlanning(store: store).load(using: model.active?.client) ?? planning
        dispatch = await DispatchData.load(day: day, model: model) ?? dispatch
        if canPlan, modules.isOn(AppModules.planning) {
            todayPlan = await DayPlanStore.load(day: day, model: model) ?? todayPlan
            todayPlanLoaded = true
        }
        await loadLocal(service)
        await loadOnline()
    }

    /// Mit Verbindung: frische Chat-Liste (letzte Nachricht je Baustelle) und offene Anträge zur Genehmigung.
    private func loadOnline() async {
        guard let active = model.active else { return }
        if active.sync?.chat?.isAvailable == true, let fresh = try? await active.client.chatConversationsWithData() {
            try? await active.sync?.store.setMeta(ChatListView.cacheKey, String(decoding: fresh.data, as: UTF8.self))
            applyChats(fresh.items)
        }
        if modules.isOn(AppModules.absence) {
            pendingApprovals = ((try? await active.client.pendingAbsenceRequests()) ?? []).count
        }
    }

    /// Letzte Nachricht je Baustelle aus der Chat-Liste übernehmen.
    private func applyChats(_ list: [ChatConversation]) {
        let bySite = Dictionary(list.compactMap { conversation in conversation.bauakteId.map { ($0, conversation) } },
                                uniquingKeysWith: { lhs, rhs in (lhs.lastMessageAt ?? .distantPast) >= (rhs.lastMessageAt ?? .distantPast) ? lhs : rhs })
        for (id, conversation) in bySite where extras[id] != nil || jobs.contains(where: { $0.bauakteId == id }) {
            extras[id, default: TodaySiteExtras()].chat = conversation.lastMessageText == nil ? nil : conversation
        }
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

        // Leistung, Zugang und angeheftete Notizen der heutigen Baustellen; Chat aus der zuletzt geladenen Liste.
        var siteExtras: [UUID: TodaySiteExtras] = [:]
        for id in Set(jobs.map(\.bauakteId)) {
            guard let bauakte = bauakten[id] else { continue }
            let pinned = ((try? await service.notes(of: id)) ?? []).filter(\.isPinned).compactMap { FreeText.clean($0.text) }
            siteExtras[id] = TodaySiteExtras(facts: bauakte.project.keyFactsText, access: FreeText.clean(bauakte.project.siteAccess),
                                         pinned: pinned, chat: extras[id]?.chat)
        }
        extras = siteExtras
        if let store = model.active?.sync?.store, model.active?.sync?.chat?.isAvailable != false,
           let text = try? await store.meta(ChatListView.cacheKey), let cached = ChatConversation.decodeList(Data(text.utf8)) {
            applyChats(cached)
        }
    }

    static func firstName(_ name: String) -> String {
        name.split(separator: " ").first.map(String.init) ?? name
    }
}
