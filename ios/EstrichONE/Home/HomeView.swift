import EstrichCore
import SwiftUI

/// „Heute“ – der Arbeitsplatz für den Tag. Oben das Wichtigste: der Einsatz mit Adresse, Ansprechpartner und den
/// Handgriffen vor Ort; darunter Kennzahlen (Aufgaben, Mängel, Woche), „Wer fährt wohin“, offene Aufgaben, morgen und
/// zuletzt geöffnet. Lokale Daten erscheinen sofort, frische Planung kommt im Hintergrund dazu (offline lesbar).
/// iPad: zwei Spalten.
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
    /// „2026-041 · EFH Schneider“ je Bauakte der offenen Aufgaben.
    @State private var itemBauakten: [UUID: String] = [:]
    @State private var loaded = false
    @State private var cameraFor: Bauakte?
    @State private var diaryFor: Bauakte?
    @State private var defectFor: Bauakte?
    @State private var toast: String?
    @State private var push = PushCenter.shared

    private var isWide: Bool { sizeClass == .regular }

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
                ScrollView {
                    VStack(alignment: .leading, spacing: Space.section) {
                        VStack(alignment: .leading, spacing: Space.m) {
                            dateLine
                            notices
                        }
                        quickAccess
                        if isWide {
                            HStack(alignment: .top, spacing: Space.xl) {
                                VStack(alignment: .leading, spacing: Space.section) {
                                    todaySection
                                    dispatchSection
                                }
                                .frame(maxWidth: .infinity, alignment: .leading)
                                VStack(alignment: .leading, spacing: Space.section) {
                                    tasksSection
                                    tomorrowSection
                                    recentSection
                                }
                                .frame(width: 380)
                            }
                        } else {
                            todaySection
                            dispatchSection
                            tasksSection
                            tomorrowSection
                            recentSection
                        }
                        Color.clear.frame(height: 1).id("bottom")
                    }
                    .padding(.horizontal, isWide ? Space.xl : Space.l)
                    .padding(.top, Space.xs)
                    .padding(.bottom, Space.xxl)
                    .readableWidth()
                }
                .background(Theme.canvas)
                .task(id: loaded) {
                    // Screenshot-Modus: unteren Teil zeigen.
                    guard loaded, shotScene == "heute-unten" else { return }
                    try? await Task.sleep(for: .milliseconds(300))
                    proxy.scrollTo("bottom", anchor: .bottom)
                }
            }
            .navigationTitle("Heute")
            .toolbar {
                ToolbarItem(placement: .primaryAction) { syncButton }
                if let chat = model.active?.sync?.chat, chat.isAvailable {
                    ToolbarItem(placement: .primaryAction) {
                        ChatToolbarButton(count: chat.unreadCount) { path.append(.chat) }
                    }
                }
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

    /// Begrüßung als eigene, größere Zeile, darunter das Datum in ruhiger Schrift – bricht bei großer Schrift sauber um.
    private var dateLine: some View {
        VStack(alignment: .leading, spacing: Space.xxs) {
            if let active = model.active {
                Text("\(greeting), \(Self.firstName(active.user.displayName))")
                    .font(Typo.section)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text(Date.now.formatted(.dateTime.weekday(.wide).day().month(.wide)))
                .font(Typo.subhead)
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, Space.xs)
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("currentUserName")
    }

    @ViewBuilder
    private var notices: some View {
        if model.isDemo, !ShotOptions.hidesDemoMarks {
            NoticeLine(tone: .info, text: "Demomodus: Beispieldaten, es wird nichts gesendet.", symbol: "play.rectangle.fill",
                       actionTitle: "Beenden") { model.requestDemo(false) }
        }
        if let sync = model.active?.sync {
            if let message = sync.maintenanceMessage {
                Callout(tone: .warning, title: "Wartung des Servers",
                        message: "\(message) Sie können weiterarbeiten – alles bleibt auf dem Gerät und wird danach übertragen.",
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
    }

    private var syncButton: some View {
        let state = syncState
        return Button { path.append(.sync) } label: {
            Label(state.text, systemImage: state.symbol)
                .foregroundStyle(state.tint)
        }
        .accessibilityLabel("Abgleich: \(state.text)")
        .accessibilityIdentifier("syncStatusIcon")
    }

    private var syncState: (symbol: String, text: String, tint: Color) {
        guard let sync = model.active?.sync else { return ("icloud.slash", "Nicht verbunden", Theme.textSecondary) }
        if sync.isRunning { return ("arrow.triangle.2.circlepath", "Wird abgeglichen", Theme.accent) }
        if sync.maintenanceMessage != nil { return ("wrench.and.screwdriver", "Wartung", Theme.warning) }
        if sync.isOffline { return ("icloud.slash", "Offline", Theme.textSecondary) }
        if sync.summary?.hasProblems == true { return ("exclamationmark.icloud", "Bitte prüfen", Theme.danger) }
        if let summary = sync.summary, !summary.isSettled { return ("icloud.and.arrow.up", "Wird übertragen", Theme.accent) }
        return ("checkmark.icloud", "Alles übertragen", Theme.accent)
    }

    // MARK: Einsatz heute

    private var todaySection: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            SectionHeading(jobs.count > 1 ? "Meine Einsätze" : "Mein Einsatz") {
                if let planning, !planning.isCurrent {
                    Label("Stand \(planning.loadedAt.formatted(date: .omitted, time: .shortened))", systemImage: "icloud.slash")
                        .labelStyle(TightLabelStyle())
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                }
            }
            if !loaded {
                TodayJobCard.placeholder
            } else if jobs.isEmpty {
                emptyToday
            } else {
                ForEach(jobs) { job in
                    TodayJobCard(job: job, canCapture: bauakten[job.bauakteId] != nil) { action in
                        perform(action, for: job)
                    }
                }
            }
        }
        .accessibilityIdentifier("todayAssignments")
    }

    private var emptyToday: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            HStack(spacing: Space.m) {
                IconBadge(symbol: "sun.max", size: 40)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Heute ist nichts für Sie eingeplant.").font(Typo.headline)
                    if let next = tomorrow.first {
                        Text("Morgen \([next.startText, next.bauakteTitle].compactMap { $0 }.joined(separator: " · "))")
                            .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                    } else {
                        Text("Neue Einsätze erscheinen hier, sobald das Büro plant.")
                            .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                    }
                }
            }
            HStack(spacing: Space.s) {
                Button { path.append(.week) } label: { Label("Meine Woche", systemImage: "calendar") }
                    .estrichButton(.secondary)
                Button(action: openCapture) { Label("Erfassen", systemImage: "plus") }
                    .estrichButton(.secondary)
            }
        }
        .padding(Space.l)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
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

    // MARK: Schnellzugriff

    /// Die drei wichtigsten Wege des Tages als große Kacheln direkt oben (nicht erst unter „Mehr“):
    /// Meine Aufgaben (mit Mängeln), Wer fährt wohin, Meine Woche.
    private var quickAccess: some View {
        let tasks = items.count
        let defects = items.filter { $0.kind == .defect }.count
        let crews = dispatch?.board?.crewGroups.count ?? 0
        return HStack(alignment: .top, spacing: Space.s) {
            Button { path.append(.tasks) } label: {
                QuickAccessTile(symbol: "checklist", title: "Meine Aufgaben", value: loaded ? "\(tasks)" : "–",
                                detail: defects > 0 ? (defects == 1 ? "1 Mangel" : "\(defects) Mängel") : "offen",
                                detailTint: defects > 0 ? Theme.warning : Theme.textSecondary)
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("myTasksTile")
            Button { path.append(.dispatch) } label: {
                QuickAccessTile(symbol: "car.2", title: "Wer fährt wohin", value: dispatch == nil ? "–" : "\(crews)",
                                detail: "heute")
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("dispatchTile")
            Button { path.append(.week) } label: {
                QuickAccessTile(symbol: "calendar", title: "Meine Woche", value: planning == nil ? "–" : "\(weekCount)",
                                detail: "nächste 7 Tage")
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("myWeekTile")
        }
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("quickAccess")
    }

    // MARK: Wer fährt wohin

    @ViewBuilder
    private var dispatchSection: some View {
        let groups = dispatch?.board?.crewGroups ?? []
        if loaded, dispatch != nil {
            VStack(alignment: .leading, spacing: Space.m) {
                SectionHeading("Wer fährt wohin") {
                    MoreLink { path.append(.dispatch) }
                        .accessibilityIdentifier("dispatchAllButton")
                }
                VStack(spacing: 0) {
                    if groups.isEmpty {
                        Text(dispatch?.message ?? "Heute ist nichts eingeplant.")
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                            .padding(Space.l)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    ForEach(Array(groups.prefix(isWide ? 8 : 5).enumerated()), id: \.offset) { index, group in
                        if index > 0 { InsetDivider(leading: Space.l + 20) }
                        dispatchRow(group)
                    }
                }
                .card()
            }
            .accessibilityIdentifier("dispatchCard")
        }
    }

    private func dispatchRow(_ group: (crew: DispatchBoard.Crew?, sites: [DispatchBoard.Site])) -> some View {
        let mine = Set(dispatch?.board?.mySiteIds ?? [])
        return Button { path.append(.dispatch) } label: {
            HStack(alignment: .top, spacing: Space.m) {
                StatusDot(color: Color(hexString: group.crew?.color) ?? Theme.textTertiary, size: 10)
                    .padding(.top, 6)
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: Space.s) {
                        Text(group.crew?.name ?? "Ohne Kolonne")
                            .font(Typo.subheadStrong)
                            .foregroundStyle(Theme.textPrimary)
                        if group.sites.contains(where: { mine.contains($0.bauakteId) }) {
                            Chip(text: "Ich", tone: .accent)
                        }
                    }
                    ForEach(group.sites) { site in
                        let slot = group.crew.flatMap { crew in site.crews.first { $0.id == crew.id } }
                        Text([slot?.startText ?? site.startText, site.title].compactMap { $0 }.joined(separator: " · "))
                            .font(Typo.subhead.monospacedDigit())
                            .foregroundStyle(Theme.textSecondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                Chevron().padding(.top, 4)
            }
            .padding(.horizontal, Space.l)
            .padding(.vertical, Space.m)
            .contentShape(Rectangle())
        }
        .buttonStyle(RowButtonStyle())
    }

    // MARK: Aufgaben, morgen, zuletzt geöffnet

    @ViewBuilder
    private var tasksSection: some View {
        if !items.isEmpty {
            VStack(alignment: .leading, spacing: Space.m) {
                SectionHeading("Offene Aufgaben") {
                    MoreLink("Alle (\(items.count))") { path.append(.tasks) }
                }
                VStack(spacing: 0) {
                    ForEach(Array(items.prefix(3).enumerated()), id: \.element.id) { index, item in
                        if index > 0 { InsetDivider(leading: 60) }
                        WorkItemRow(item: item, showsBauakte: item.bauakteId.flatMap { itemBauakten[$0] },
                                    onOpen: item.bauakteId.map { id in { path.append(.bauakte(id)) } }) {
                            Task { await toggle(item) }
                        }
                        .padding(.horizontal, Space.s)
                    }
                }
                .card()
            }
        }
    }

    @ViewBuilder
    private var tomorrowSection: some View {
        let list = tomorrow
        if loaded, !list.isEmpty, !jobs.isEmpty || list.count > 1 {
            VStack(alignment: .leading, spacing: Space.m) {
                SectionHeading("Morgen") {
                    MoreLink("Woche") { path.append(.week) }
                }
                VStack(spacing: 0) {
                    ForEach(Array(list.prefix(4).enumerated()), id: \.element.id) { index, item in
                        if index > 0 { InsetDivider(leading: 80) }
                        Button { path.append(.bauakte(item.bauakteId)) } label: {
                            HStack(alignment: .firstTextBaseline, spacing: Space.m) {
                                Text(item.startText ?? "–")
                                    .font(Typo.bodyStrong.monospacedDigit())
                                    .foregroundStyle(Theme.textPrimary)
                                    .frame(width: 52, alignment: .leading)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(item.bauakteTitle).font(Typo.body).foregroundStyle(Theme.textPrimary)
                                        .fixedSize(horizontal: false, vertical: true)
                                    Text([item.activity, item.crewName].compactMap { $0 }.joined(separator: " · "))
                                        .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                                        .fixedSize(horizontal: false, vertical: true)
                                }
                                .frame(maxWidth: .infinity, alignment: .leading)
                                Chevron()
                            }
                            .padding(.horizontal, Space.l)
                            .padding(.vertical, Space.m)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(RowButtonStyle())
                    }
                }
                .card()
            }
            .accessibilityIdentifier("tomorrowPreview")
        }
    }

    @ViewBuilder
    private var recentSection: some View {
        let shown = Set(jobs.map(\.bauakteId))
        let list = Array(recent.filter { !shown.contains($0.id) }.prefix(3))
        if !list.isEmpty {
            VStack(alignment: .leading, spacing: Space.m) {
                SectionHeading("Zuletzt geöffnet")
                VStack(spacing: 0) {
                    ForEach(Array(list.enumerated()), id: \.element.id) { index, bauakte in
                        if index > 0 { InsetDivider(leading: Space.l) }
                        Button { path.append(.bauakte(bauakte.id)) } label: {
                            HStack(spacing: Space.m) {
                                BauakteRow(bauakte: bauakte)
                                Chevron()
                            }
                            .padding(.horizontal, Space.l)
                            .padding(.vertical, Space.s)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(RowButtonStyle())
                    }
                }
                .card()
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
        itemBauakten = found.mapValues { [$0.number, $0.title].compactMap { $0 }.joined(separator: " · ") }
    }

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        if let service = model.active?.service { await loadLocal(service) }
    }

    private var greeting: String {
        // Aufnahmen (Statusleiste 9:41): passender Gruß unabhängig von der Uhrzeit des Aufnahmerechners.
        switch shotScene == nil ? Calendar.current.component(.hour, from: .now) : 9 {
        case 4..<11: "Guten Morgen"
        case 11..<17: "Guten Tag"
        default: "Guten Abend"
        }
    }

    static func firstName(_ name: String) -> String {
        name.split(separator: " ").first.map(String.init) ?? name
    }
}

/// Große Kachel im Schnellzugriff der Heute-Seite: Symbol, Zahl, Titel und kurzer Zusatz – Titel vollständig (zweizeilig).
struct QuickAccessTile: View {
    let symbol: String
    let title: String
    let value: String
    let detail: String
    var detailTint: Color = Theme.textSecondary

    var body: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            HStack(alignment: .center) {
                IconBadge(symbol: symbol, size: 34)
                Spacer(minLength: Space.xs)
                Text(value)
                    .font(Typo.metric)
                    .foregroundStyle(Theme.textPrimary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(Typo.subheadStrong)
                    .foregroundStyle(Theme.textPrimary)
                    .lineLimit(2, reservesSpace: true)
                    .fixedSize(horizontal: false, vertical: true)
                Text(detail)
                    .font(Typo.meta)
                    .foregroundStyle(detailTint)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(Space.m)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card(radius: Radius.l)
        .contentShape(RoundedRectangle(cornerRadius: Radius.l, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}
