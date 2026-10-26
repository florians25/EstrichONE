import EstrichCore
import SwiftUI

/// Startseite als Arbeitsplatz für den Tag: Kopf in Navy mit Person und Gerät, Schnellaktionen,
/// „Meine Einsätze heute“ als große Karten mit Anruf, Route und Sprung in die Akte, Vorschau auf morgen,
/// „Wer fährt wohin“, offene Aufgaben und zuletzt geöffnete Bauakten.
/// iPhone: eine Spalte. iPad: Dashboard mit Hauptspalte (Einsätze, Disposition) und Seitenspalte (Aktionen, Aufgaben, Navigation).
struct HomeView: View {
    let openTab: (MainTabView.Tab) -> Void
    var openCapture: (CaptureMode) -> Void = { _ in }

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var path: [HomeRoute] = []
    @State private var creating = false
    @State private var reporting = false
    @State private var captureFor: Bauakte?
    @State private var inspectFor: Bauakte?
    /// Hauptaktion, für die noch die Baustelle gewählt wird.
    @State private var siteTask: SiteTask?
    @State private var diaryFor: Bauakte?
    @State private var acceptanceFor: Bauakte?
    @State private var planning: MyPlanning.Snapshot?
    @State private var openTasks = 0
    @State private var tasks: [WorkItem] = []
    @State private var recent: [Bauakte] = []
    @State private var dispatch: DispatchData.Day?
    @State private var bauakten: [UUID: Bauakte] = [:]
    /// „2026-041 · EFH Schneider“ je Bauakte der offenen Aufgaben.
    @State private var taskBauakten: [UUID: String] = [:]

    private var isWide: Bool { sizeClass == .regular }

    var body: some View {
        NavigationStack(path: $path) {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    HomeHeader(todayCount: planning == nil && dispatch == nil ? nil : TodayJob.make(
                                   assignments: planning?.assignments(on: CalendarDay.string(.now)) ?? [], board: dispatch?.board, bauakten: bauakten).count,
                               openTasks: openTasks, onTasks: { path.append(.tasks) }, onSync: { path.append(.sync) })
                    if let sync = model.active?.sync {
                        VStack(spacing: 10) {
                            SyncStatusBar(sync: sync)
                                .clipShape(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous))
                            SystemNoticesBanner(sync: sync)
                        }
                        .padding(.horizontal)
                    }
                    if isWide {
                        HStack(alignment: .top, spacing: 20) {
                            VStack(alignment: .leading, spacing: 18) {
                                todaySection
                                tomorrowSection
                                dispatchCard
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            VStack(alignment: .leading, spacing: 18) {
                                quickActions
                                tasksSection
                                recentSection
                                navigation
                            }
                            .frame(width: 340)
                        }
                        .padding(.horizontal, 20)
                    } else {
                        VStack(alignment: .leading, spacing: 18) {
                            quickActions
                            todaySection
                            tomorrowSection
                            dispatchCard
                            tasksSection
                            navigation
                            recentSection
                        }
                        .padding(.horizontal)
                    }
                }
                .padding(.bottom, 24)
                // Großes iPad quer: Inhalte nicht beliebig breit ziehen.
                .frame(maxWidth: 1280)
                .frame(maxWidth: .infinity)
            }
            .background(Theme.surfaceAlt)
            // Ohne Navigationsleiste: Statusleiste mit Material hinterlegen, damit nichts darunter durchläuft.
            .safeAreaInset(edge: .top, spacing: 0) { Color.clear.frame(height: 0).background(.bar) }
            .toolbar(.hidden, for: .navigationBar)
            .navigationDestination(for: HomeRoute.self) { route in
                switch route {
                case let .bauakte(id): BauakteDetailView(bauakteId: id)
                case .tasks: MyTasksView()
                case .week: MyWeekView()
                case .sync: if let sync = model.active?.sync { SyncView(sync: sync) }
                case .dispatch: DispatchView()
                }
            }
            .sheet(isPresented: $creating) {
                NewBauakteView { id, next in
                    creating = false
                    path.append(.bauakte(id))
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
            .sheet(isPresented: $reporting) { ReportProblemView { reporting = false } }
            .fullScreenCover(item: $captureFor) { bauakte in
                CameraView(bauakte: bauakte) { _ in captureFor = nil }
            }
            .sheet(item: $inspectFor) { bauakte in
                InspectionEntryView(bauakte: bauakte) { inspectFor = nil }
            }
            .sheet(item: $siteTask) { task in
                BauakteQuickPicker(task: task, todayIds: TodayJob.make(
                    assignments: planning?.assignments(on: CalendarDay.string(.now)) ?? [], board: dispatch?.board, bauakten: bauakten)
                    .map(\.bauakteId)) { bauakte in
                    siteTask = nil
                    guard let bauakte else { return }
                    Task {
                        // Erst wenn die Auswahl geschlossen ist, das nächste Blatt öffnen.
                        try? await Task.sleep(for: .milliseconds(450))
                        switch task {
                        case .open: path.append(.bauakte(bauakte.id))
                        case .diary: diaryFor = bauakte
                        case .inspection: inspectFor = bauakte
                        case .acceptance: acceptanceFor = bauakte
                        }
                    }
                }
            }
            .sheet(item: $diaryFor) { bauakte in
                NewDiaryEntryView(bauakteId: bauakte.id) { diaryFor = nil }
            }
            .sheet(item: $acceptanceFor) { bauakte in
                NewAcceptanceView(bauakte: bauakte) { acceptanceFor = nil }
            }
            .task(id: model.active?.sync?.revision) { await reload() }
            .refreshable {
                await model.active?.sync?.run()
                await reload()
            }
        }
    }

    // MARK: Abschnitte

    /// Schnellaktionen: anlegen (Hauptaktion), fotografieren und scannen. iPad: zwei Spalten mit „Problem melden“.
    private var quickActions: some View {
        let columns = Array(repeating: GridItem(.flexible(), spacing: 10), count: isWide ? 2 : 3)
        return VStack(spacing: 10) {
            LazyVGrid(columns: columns, spacing: 10) {
                Button { openToday() } label: { Label("Meine Baustelle", systemImage: "hammer.fill") }
                    .buttonStyle(ActionTileStyle(rank: .primary))
                    .accessibilityIdentifier("myTodayTile")
                Button { openCapture(.photo) } label: { Label("Foto", systemImage: "camera.fill") }
                    .buttonStyle(ActionTileStyle(rank: .accent))
                    .accessibilityIdentifier("photoTile")
                Button { openCapture(.document) } label: { Label("Scannen", systemImage: "doc.viewfinder") }
                    .buttonStyle(ActionTileStyle(rank: .secondary))
                    .accessibilityIdentifier("scanTile")
                Button { siteTask = .diary } label: { Label("Bautagebuch", systemImage: "book.fill") }
                    .buttonStyle(ActionTileStyle(rank: .secondary))
                    .accessibilityIdentifier("diaryTile")
                Button { siteTask = .inspection } label: { Label("Erstbegehung", systemImage: "checklist") }
                    .buttonStyle(ActionTileStyle(rank: .secondary))
                    .accessibilityIdentifier("inspectionTile")
                Button { siteTask = .acceptance } label: { Label("Abnahme", systemImage: "signature") }
                    .buttonStyle(ActionTileStyle(rank: .secondary))
                    .accessibilityIdentifier("acceptanceTile")
            }
            Button { creating = true } label: { ActionLabel("Neue Bauakte", systemImage: "folder.badge.plus") }
                .estrichButton(.secondary, size: .regular, fullWidth: true)
                .keyboardShortcut("n", modifiers: .command)
                .accessibilityIdentifier("newBauakteTile")
        }
    }

    /// Baustelle von heute öffnen; ohne Einsatz heute: auswählen.
    private func openToday() {
        let jobs = TodayJob.make(assignments: planning?.assignments(on: CalendarDay.string(.now)) ?? [], board: dispatch?.board, bauakten: bauakten)
        if let first = jobs.first, Set(jobs.map(\.bauakteId)).count == 1 {
            path.append(.bauakte(first.bauakteId))
        } else {
            siteTask = .open
        }
    }

    /// „Meine Einsätze heute“: aus meiner Planung, ergänzt um Kolonne, Ansprechpartner und Route aus „Wer fährt wohin“
    /// sowie den Status der Akte. Ohne eigene Planung, aber mit Baustellen meiner Kolonne: diese Baustellen.
    private var todaySection: some View {
        let jobs = TodayJob.make(
            assignments: planning?.assignments(on: CalendarDay.string(.now)) ?? [],
            board: dispatch?.board, bauakten: bauakten)
        return VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                SectionTitle("Meine Einsätze heute")
                Spacer()
                if let planning, !planning.isCurrent {
                    Label("Stand \(planning.loadedAt.formatted(date: .abbreviated, time: .shortened))", systemImage: "wifi.slash")
                        .labelStyle(TightLabelStyle())
                        .font(.caption2).foregroundStyle(Theme.textSecondary)
                }
            }
            if jobs.isEmpty {
                Label(planning == nil && dispatch == nil ? "Planung wird geladen …" : "Heute ist für Sie nichts eingeplant.",
                      systemImage: "calendar")
                    .font(.subheadline)
                    .foregroundStyle(Theme.textSecondary)
                    .padding(.horizontal, 14)
                    .frame(maxWidth: .infinity, minHeight: 52, alignment: .leading)
                    .card()
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 300), spacing: 12, alignment: .top)], spacing: 12) {
                    ForEach(jobs) { job in
                        TodayJobCard(job: job) { path.append(.bauakte(job.bauakteId)) }
                    }
                }
            }
        }
        .accessibilityIdentifier("todayAssignments")
    }

    /// Kurzer Blick auf morgen: Uhrzeit, Baustelle, Tätigkeit.
    @ViewBuilder
    private var tomorrowSection: some View {
        let tomorrow = Calendar.current.date(byAdding: .day, value: 1, to: .now) ?? .now
        let items = planning?.assignments(on: CalendarDay.string(tomorrow)) ?? []
        if !items.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline) {
                    SectionTitle("Morgen, \(tomorrow.formatted(.dateTime.weekday(.wide)))")
                    Spacer()
                    Button("Meine Woche") { path.append(.week) }
                        .font(.subheadline.weight(.semibold))
                }
                VStack(spacing: 0) {
                    ForEach(Array(items.prefix(4).enumerated()), id: \.element.id) { index, item in
                        if index > 0 { Divider().padding(.leading, 70) }
                        Button { path.append(.bauakte(item.bauakteId)) } label: {
                            HStack(spacing: 12) {
                                Text(item.startText ?? "–")
                                    .font(.subheadline.monospacedDigit().weight(.semibold))
                                    .foregroundStyle(Theme.navyText)
                                    .frame(width: 46, alignment: .leading)
                                VStack(alignment: .leading, spacing: 1) {
                                    Text(item.bauakteTitle).font(.subheadline.weight(.semibold)).lineLimit(1)
                                    Text([item.activity, item.crewName].compactMap { $0 }.joined(separator: " · "))
                                        .font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(1)
                                }
                                Spacer(minLength: 0)
                                Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
                            }
                            .padding(.horizontal, 14).padding(.vertical, 9)
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

    /// „Wer fährt wohin“: je Kolonne ihre Baustellen des Tages, meine markiert; alles über „Alle“.
    private var dispatchCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                SectionTitle("Wer fährt wohin – heute")
                Spacer()
                Button("Alle") { path.append(.dispatch) }
                    .font(.subheadline.weight(.semibold))
                    .accessibilityIdentifier("dispatchAllButton")
            }
            VStack(alignment: .leading, spacing: 0) {
                let groups = dispatch?.board?.crewGroups ?? []
                let total = dispatch?.board?.sites.count ?? 0
                if groups.isEmpty {
                    Text(dispatch?.message ?? (dispatch == nil ? "Planung wird geladen …" : "Heute ist nichts eingeplant."))
                        .font(.subheadline).foregroundStyle(Theme.textSecondary)
                        .padding(.horizontal, 14)
                        .frame(maxWidth: .infinity, minHeight: 48, alignment: .leading)
                }
                ForEach(Array(groups.prefix(isWide ? 6 : 4).enumerated()), id: \.offset) { index, group in
                    if index > 0 { Divider() }
                    Group {
                        if let crew = group.crew, let board = dispatch?.board {
                            CrewHeader(crew: crew, board: board)
                        } else {
                            Text(dispatch?.board?.crews.isEmpty == false ? "Ohne Kolonne" : "Baustellen")
                                .font(.subheadline.weight(.bold)).foregroundStyle(Theme.textSecondary)
                        }
                    }
                    .padding(.horizontal, 14).padding(.top, 10)
                    ForEach(group.sites) { site in
                        DispatchSiteRow(site: site, isMine: dispatch?.board?.mySiteIds.contains(site.id) == true,
                                        slot: group.crew.flatMap { crew in site.crews.first { $0.id == crew.id } })
                            .padding(.leading, 14).padding(.trailing, 8).padding(.vertical, 6)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                if groups.count > (isWide ? 6 : 4) {
                    Divider()
                    Button { path.append(.dispatch) } label: {
                        Text("Alle \(total) Baustellen ansehen")
                            .font(.subheadline.weight(.semibold)).foregroundStyle(Theme.actionPrimary)
                            .padding(.horizontal, 14).frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                    }
                    .buttonStyle(RowButtonStyle())
                }
            }
            .padding(.bottom, 6)
            .card()
        }
        .accessibilityIdentifier("dispatchCard")
    }

    /// Offene Aufgaben und Mängel (die dringendsten) zum direkten Abhaken.
    @ViewBuilder
    private var tasksSection: some View {
        if !tasks.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline) {
                    SectionTitle("Offene Aufgaben")
                    Spacer()
                    Button("Alle (\(openTasks))") { path.append(.tasks) }.font(.subheadline.weight(.semibold))
                }
                VStack(spacing: 0) {
                    ForEach(Array(tasks.prefix(isWide ? 5 : 3).enumerated()), id: \.element.id) { index, item in
                        if index > 0 { Divider().padding(.leading, 58) }
                        WorkItemRow(item: item, showsBauakte: item.bauakteId.flatMap { taskBauakten[$0] },
                                    onOpen: item.bauakteId.map { id in { path.append(.bauakte(id)) } }) {
                            Task {
                                try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
                                model.active?.sync?.localChangeMade()
                                await reload()
                            }
                        }
                        .padding(.leading, 4).padding(.trailing, 14)
                    }
                }
                .card()
            }
        }
    }

    @ViewBuilder
    private var recentSection: some View {
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
        }
    }

    private var navigation: some View {
        VStack(spacing: 0) {
            NavigationRow(title: "Meine Aufgaben", symbol: "checklist", color: Color(hex: 0xE07A1F), badge: openTasks) { path.append(.tasks) }
                .accessibilityIdentifier("myTasksTile")
            Divider().padding(.leading, 54)
            NavigationRow(title: "Wer fährt wohin", symbol: "car.2.fill", color: Color(hex: 0x0E7490)) { path.append(.dispatch) }
                .accessibilityIdentifier("dispatchRow")
            Divider().padding(.leading, 54)
            NavigationRow(title: "Meine Woche", symbol: "calendar", color: Color(hex: 0x2F80ED)) { path.append(.week) }
                .accessibilityIdentifier("myWeekTile")
            if !isWide {
                Divider().padding(.leading, 54)
                NavigationRow(title: "Problem melden", symbol: "exclamationmark.bubble.fill", color: Color(hex: 0xC2410C)) { reporting = true }
            }
        }
        .card()
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        recent = Array(((try? await service.recentBauakten()) ?? []).prefix(isWide ? 5 : 3))
        tasks = (try? await service.myWorkItems()) ?? []
        openTasks = tasks.count
        dispatch = await DispatchData.load(day: CalendarDay.string(.now), model: model)
        if let store = model.active?.sync?.store {
            planning = await MyPlanning(store: store).load(using: model.active?.client)
        }
        // Status und Ansprechpartner der heutigen Akten aus der lokalen Ablage.
        var ids = Set((planning?.assignments(on: CalendarDay.string(.now)) ?? []).map(\.bauakteId))
        ids.formUnion(dispatch?.board?.mySiteIds ?? [])
        var loaded: [UUID: Bauakte] = [:]
        for id in ids {
            if let bauakte = try? await service.bauakte(id) { loaded[id] = bauakte }
        }
        bauakten = loaded
        var names: [UUID: String] = [:]
        for id in Set(tasks.compactMap(\.bauakteId)) {
            var found = loaded[id]
            if found == nil { found = try? await service.bauakte(id) }
            if let bauakte = found {
                names[id] = [bauakte.number, bauakte.title].compactMap { $0 }.joined(separator: " · ")
            }
        }
        taskBauakten = names
    }
}

/// Ein Einsatz von heute, zusammengeführt aus meiner Planung, „Wer fährt wohin“ und der lokalen Akte.
struct TodayJob: Identifiable {
    let id: UUID
    let bauakteId: UUID
    let startText: String?
    let endText: String?
    let activity: String
    let number: String?
    let title: String
    let address: String?
    let crewName: String?
    let crewColor: Color?
    let team: [String]
    let warnings: [String]
    let notes: String?
    let contactName: String?
    let phone: URL?
    let route: URL?
    let status: BauakteStatus?

    var timeText: String {
        switch (startText, endText) {
        case let (start?, end?): "\(start)–\(end)"
        case let (start?, nil): "ab \(start)"
        default: "ganztägig"
        }
    }

    static func make(assignments: [Assignment], board: DispatchBoard?, bauakten: [UUID: Bauakte]) -> [TodayJob] {
        let sites = Dictionary((board?.sites ?? []).map { ($0.bauakteId, $0) }, uniquingKeysWith: { first, _ in first })
        let named = (board?.crews ?? []).map { ($0.name, $0.color) } + (board?.sites ?? []).flatMap(\.crews).map { ($0.name, $0.color) }
        let crewColors = Dictionary(named.compactMap { name, color in Color(hexString: color).map { (name, $0) } },
                                    uniquingKeysWith: { first, _ in first })
        func color(_ job: DispatchBoard.Job?, crewName: String?) -> Color? {
            Color(hexString: job?.crewColor) ?? crewName.flatMap { crewColors[$0] }
        }
        func links(_ site: DispatchBoard.Site?, _ bauakte: Bauakte?, address: String?) -> (String?, URL?, URL?) {
            let name = site?.siteContactName ?? bauakte?.siteContactName
            let phone = ContactLink.phone(site?.siteContactPhone ?? bauakte?.siteContactPhone)
            let route = site?.navigationUrl.flatMap(URL.init(string:))
                ?? bauakte.flatMap(MapsLink.url(for:))
                ?? MapsLink.url(address: address)
            return (name, phone, route)
        }

        if !assignments.isEmpty {
            return assignments.map { assignment in
                let site = sites[assignment.bauakteId]
                let job = site?.jobs.first { $0.assignmentId == assignment.id }
                let bauakte = bauakten[assignment.bauakteId]
                let address = assignment.bauakteAddress ?? site?.address
                let (name, phone, route) = links(site, bauakte, address: address)
                let team = assignment.people.map(\.name) + assignment.resources.map(\.name)
                return TodayJob(
                    id: assignment.id, bauakteId: assignment.bauakteId,
                    startText: assignment.startText ?? job?.startText, endText: job?.endText,
                    activity: assignment.activity, number: assignment.bauakteNumber, title: assignment.bauakteTitle,
                    address: address, crewName: assignment.crewName ?? job?.crewName,
                    crewColor: color(job, crewName: assignment.crewName ?? job?.crewName),
                    team: team, warnings: assignment.warnings + (site?.warnings ?? []).filter { !assignment.warnings.contains($0) },
                    notes: assignment.notes ?? job?.notes, contactName: name, phone: phone, route: route, status: bauakte?.status)
            }
        }
        // Ohne eigene Einsätze: Baustellen, auf denen ich (z. B. über meine Kolonne) heute eingeplant bin.
        let mine = (board?.sortedSites ?? []).filter { board?.mySiteIds.contains($0.bauakteId) == true }
        return mine.flatMap { site -> [TodayJob] in
            let bauakte = bauakten[site.bauakteId]
            let (name, phone, route) = links(site, bauakte, address: site.address)
            return site.jobs.map { job in
                TodayJob(
                    id: job.assignmentId, bauakteId: site.bauakteId, startText: job.startText ?? site.startText, endText: job.endText,
                    activity: job.activity, number: site.number, title: site.title, address: site.address,
                    crewName: job.crewName, crewColor: color(job, crewName: job.crewName),
                    team: site.people.map(\.name) + site.vehicles.map(\.name), warnings: site.warnings, notes: job.notes,
                    contactName: name, phone: phone, route: route, status: bauakte?.status)
            }
        }
    }
}

/// Karte für einen Einsatz: Zeit und Status, Tätigkeit, Baustelle, Kolonne in ihrer Farbe, Hinweise und
/// die drei Handgriffe vor Ort – Anrufen, Route, Akte öffnen.
struct TodayJobCard: View {
    let job: TodayJob
    let open: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Button(action: open) {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(alignment: .firstTextBaseline) {
                        Text(job.timeText)
                            .font(.title3.monospacedDigit().weight(.bold))
                            .foregroundStyle(Theme.navyText)
                        Spacer(minLength: 8)
                        if let status = job.status { BauakteStatusChip(status: status) }
                    }
                    Text(job.activity).font(.headline).foregroundStyle(.primary)
                    Text([job.number, job.title].compactMap { $0 }.joined(separator: " · "))
                        .font(.subheadline).foregroundStyle(.primary).lineLimit(2)
                    if let address = job.address {
                        Label(address, systemImage: "mappin.and.ellipse").labelStyle(TightLabelStyle())
                            .font(.footnote).foregroundStyle(Theme.textSecondary).lineLimit(2)
                    }
                    if job.crewName != nil || !job.team.isEmpty {
                        VStack(alignment: .leading, spacing: 4) {
                            if let crew = job.crewName {
                                HStack(spacing: 5) {
                                    Circle().fill(job.crewColor ?? Theme.accent).frame(width: 9, height: 9)
                                    Text(crew).font(.caption.weight(.semibold))
                                }
                                .padding(.horizontal, 8).padding(.vertical, 3)
                                .background((job.crewColor ?? Theme.accent).opacity(0.14), in: Capsule())
                            }
                            if !job.team.isEmpty {
                                Text(job.team.joined(separator: ", "))
                                    .font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(2)
                            }
                        }
                    }
                    if let contact = job.contactName {
                        Label(contact, systemImage: "person.crop.circle").labelStyle(TightLabelStyle())
                            .font(.caption).foregroundStyle(Theme.textSecondary)
                    }
                    if let notes = job.notes?.compactText {
                        Label("Notiz: \(notes)", systemImage: "text.bubble").labelStyle(TightLabelStyle())
                            .font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(3)
                    }
                    ForEach(job.warnings, id: \.self) { warning in
                        Label(warning, systemImage: "exclamationmark.triangle.fill").labelStyle(TightLabelStyle())
                            .font(.caption.weight(.medium)).foregroundStyle(Theme.hint)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("todayJob-\(job.title)")

            HStack(spacing: 8) {
                Button(action: open) { Label("Akte", systemImage: "folder") }
                    .estrichButton(.primary)
                    .accessibilityIdentifier("todayJobOpen")
                if let phone = job.phone {
                    Link(destination: phone) { Label("Anrufen", systemImage: "phone.fill") }
                        .estrichButton(.secondary)
                        .accessibilityIdentifier("todayJobCall")
                }
                if let route = job.route {
                    Link(destination: route) { Label("Route", systemImage: "car.fill") }
                        .estrichButton(.secondary)
                        .accessibilityIdentifier("todayJobRoute")
                }
                Spacer(minLength: 0)
            }
        }
        .padding(14)
        .padding(.leading, 4)
        .card()
        .overlay(alignment: .leading) {
            UnevenRoundedRectangle(topLeadingRadius: Theme.cardRadius, bottomLeadingRadius: Theme.cardRadius)
                .fill(job.crewColor ?? Theme.accent).frame(width: 5)
        }
    }
}

/// Hauptaktion der Startseite, die eine Baustelle braucht.
enum SiteTask: String, Identifiable {
    case open, diary, inspection, acceptance
    var id: String { rawValue }
    var title: String {
        switch self {
        case .open: "Baustelle öffnen"
        case .diary: "Bautagebuch – Baustelle"
        case .inspection: "Erstbegehung – Baustelle"
        case .acceptance: "Abnahme – Baustelle"
        }
    }
}

/// Baustelle wählen: heutige Einsätze zuerst, dann zuletzt geöffnete und alle aktiven, mit Suche.
struct BauakteQuickPicker: View {
    let task: SiteTask
    let todayIds: [UUID]
    let onDone: (Bauakte?) -> Void

    @Environment(AppModel.self) private var model
    @State private var search = ""
    @State private var today: [Bauakte] = []
    @State private var others: [Bauakte] = []

    var body: some View {
        NavigationStack {
            List {
                if !today.isEmpty, search.isEmpty {
                    Section("Heute") { rows(today) }
                }
                Section(search.isEmpty ? "Weitere Baustellen" : "Treffer") {
                    if others.isEmpty {
                        Text("Keine Bauakte gefunden.").foregroundStyle(Theme.textSecondary)
                    }
                    rows(others)
                }
            }
            .searchable(text: $search, prompt: "Nummer, Bauvorhaben oder Adresse")
            .navigationTitle(task.title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { onDone(nil) } } }
            .task(id: search) { await load() }
        }
    }

    private func rows(_ list: [Bauakte]) -> some View {
        ForEach(list) { bauakte in
            Button { onDone(bauakte) } label: {
                HStack {
                    BauakteRow(bauakte: bauakte)
                    Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        }
    }

    private func load() async {
        guard let service = model.active?.service else { return }
        let all = (try? await service.bauakten(search: search.isEmpty ? nil : search)) ?? []
        let active = all.filter { $0.status != .cancelled && $0.status != .completed }
        let todaySet = Set(todayIds)
        var seen = Set<UUID>()
        today = todayIds.filter { seen.insert($0).inserted }.compactMap { id in all.first { $0.id == id } }
        others = active.filter { !todaySet.contains($0.id) || !search.isEmpty }
    }
}

enum HomeRoute: Hashable {
    case bauakte(UUID)
    case tasks
    case week
    case sync
    case dispatch
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
    /// Heutige Einsätze bzw. offene Aufgaben (nil = noch nicht geladen).
    var todayCount: Int?
    var openTasks: Int?
    var onTasks: () -> Void = {}
    var onSync: () -> Void = {}

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 28, style: .continuous)
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 8) {
                BrandMark(onDark: true)
                    .frame(width: 22, height: 22)
                    .accessibilityHidden(true)
                Text(model.server?.info.serverName ?? "EstrichONE")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(.white.opacity(0.8))
                    .lineLimit(1)
                Spacer(minLength: 8)
                if model.isPoolDevice {
                    Button { model.lock() } label: {
                        Image(systemName: "person.2.fill")
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(.white)
                            .frame(width: 40, height: 40)
                            .background(.white.opacity(0.14), in: Circle())
                    }
                    .accessibilityLabel("Person wechseln")
                    .accessibilityIdentifier("switchUserButton")
                }
            }
            if let user = model.active?.user {
                VStack(alignment: .leading, spacing: 2) {
                    Text("\(greeting), \(Self.firstName(user.displayName))")
                        .font(.largeTitle.weight(.bold))
                        .foregroundStyle(.white)
                        .lineLimit(2)
                        .minimumScaleFactor(0.8)
                        .accessibilityLabel(user.displayName)
                        .accessibilityIdentifier("currentUserName")
                    Text(Date.now.formatted(.dateTime.weekday(.wide).day().month(.wide)))
                        .font(.subheadline)
                        .foregroundStyle(.white.opacity(0.75))
                }
            }
            HStack(spacing: 8) { metrics }
        }
        .padding(20)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background {
            shape.fill(LinearGradient(colors: colorScheme == .dark ? [Color(hex: 0x0A2246), Color(hex: 0x123A6E)] : [Theme.navy, Theme.navyLight],
                                      startPoint: .topLeading, endPoint: .bottomTrailing))
        }
        .overlay(shape.strokeBorder(.white.opacity(colorScheme == .dark ? 0.08 : 0)))
        .shadow(color: Theme.navy.opacity(colorScheme == .dark ? 0 : 0.22), radius: 16, y: 8)
        .padding(.horizontal, sizeClass == .regular ? 20 : 16)
        .padding(.top, 8)
    }

    @ViewBuilder
    private var metrics: some View {
        MetricCapsule(symbol: "calendar", value: todayCount.map(String.init) ?? "–", label: "heute")
            .accessibilityLabel("\(todayCount ?? 0) Einsätze heute")
        Button(action: onTasks) {
            MetricCapsule(symbol: "checklist", value: openTasks.map(String.init) ?? "–", label: "offen")
                .accessibilityLabel("\(openTasks ?? 0) offene Aufgaben")
        }
        .buttonStyle(.plain)
        Button(action: onSync) {
            MetricCapsule(symbol: syncState.symbol, value: nil, label: "")
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Abgleich: \(syncState.text)")
        .accessibilityIdentifier("syncStatusIcon")
    }

    private var syncState: (symbol: String, text: String) {
        guard let sync = model.active?.sync else { return ("icloud.slash", "Nicht verbunden") }
        if sync.isRunning { return ("arrow.triangle.2.circlepath", "Wird abgeglichen") }
        if sync.isOffline { return ("wifi.slash", "Offline") }
        if sync.summary?.hasProblems == true { return ("exclamationmark.icloud", "Bitte prüfen") }
        if let summary = sync.summary, !summary.isSettled { return ("icloud.and.arrow.up", "Wird übertragen") }
        return ("checkmark.icloud", "Aktuell")
    }

    private var greeting: String {
        switch Calendar.current.component(.hour, from: .now) {
        case 4..<11: "Guten Morgen"
        case 11..<17: "Guten Tag"
        default: "Guten Abend"
        }
    }

    static func firstName(_ name: String) -> String {
        name.split(separator: " ").first.map(String.init) ?? name
    }
}

/// Kennzahl als Glas-Kapsel auf dem dunkelblauen Kopf.
private struct MetricCapsule: View {
    let symbol: String
    let value: String?
    let label: String

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: symbol).font(.subheadline.weight(.semibold))
            if let value {
                Text(value).font(.subheadline.weight(.bold).monospacedDigit())
            }
            if !label.isEmpty {
                Text(label).font(.subheadline)
            }
        }
        .foregroundStyle(.white)
        .lineLimit(1)
        .fixedSize()
        .padding(.horizontal, 12)
        .frame(minHeight: 36)
        .background(.white.opacity(0.14), in: Capsule())
        .overlay(Capsule().strokeBorder(.white.opacity(0.18)))
        .accessibilityElement(children: .combine)
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
    @State private var opened: UUID?

    var body: some View {
        List {
            if items.isEmpty {
                ContentUnavailableView("Keine offenen Aufgaben", systemImage: "checkmark.circle", description: Text("Alles erledigt."))
            }
            ForEach(items) { item in
                WorkItemRow(item: item, showsBauakte: item.bauakteId.flatMap { titles[$0] },
                            onOpen: item.bauakteId.map { id in { opened = id } }) {
                    Task { await toggle(item) }
                }
            }
        }
        .navigationTitle("Meine Aufgaben")
        .navigationDestination(item: $opened) { BauakteDetailView(bauakteId: $0) }
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
