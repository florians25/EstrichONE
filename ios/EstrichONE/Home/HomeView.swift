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
    @State private var planning: MyPlanning.Snapshot?
    @State private var openTasks = 0
    @State private var tasks: [WorkItem] = []
    @State private var recent: [Bauakte] = []
    @State private var dispatch: DispatchData.Day?
    @State private var bauakten: [UUID: Bauakte] = [:]

    private var isWide: Bool { sizeClass == .regular }

    var body: some View {
        NavigationStack(path: $path) {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    HomeHeader()
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
                InitialInspectionView(bauakte: bauakte) { inspectFor = nil }
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
        return LazyVGrid(columns: columns, spacing: 10) {
            Button { creating = true } label: {
                Label("Neue Bauakte", systemImage: "folder.badge.plus")
            }
            .buttonStyle(ActionTileStyle(rank: .primary))
            .keyboardShortcut("n", modifiers: .command)
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
            if isWide {
                Button { reporting = true } label: {
                    Label("Problem melden", systemImage: "exclamationmark.bubble")
                }
                .buttonStyle(ActionTileStyle(rank: .secondary))
            }
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

    /// „Wer fährt wohin“: alle Baustellen des Tages, meine markiert; weitere über „Alle“.
    private var dispatchCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                SectionTitle("Wer fährt wohin – heute")
                Spacer()
                Button("Alle") { path.append(.dispatch) }
                    .font(.subheadline.weight(.semibold))
                    .accessibilityIdentifier("dispatchAllButton")
            }
            VStack(spacing: 0) {
                let sites = dispatch?.board?.sortedSites ?? []
                let limit = isWide ? 6 : 4
                if sites.isEmpty {
                    Text(dispatch?.message ?? (dispatch == nil ? "Planung wird geladen …" : "Heute ist nichts eingeplant."))
                        .font(.subheadline).foregroundStyle(Theme.textSecondary)
                        .padding(.horizontal, 14)
                        .frame(maxWidth: .infinity, minHeight: 48, alignment: .leading)
                }
                ForEach(Array(sites.prefix(limit).enumerated()), id: \.element.id) { index, site in
                    if index > 0 { Divider().padding(.leading, 14) }
                    DispatchSiteRow(site: site, isMine: dispatch?.board?.mySiteIds.contains(site.id) == true)
                        .padding(.horizontal, 14).padding(.vertical, 8)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                if sites.count > limit {
                    Divider().padding(.leading, 14)
                    Button { path.append(.dispatch) } label: {
                        Text("\(sites.count - limit) weitere Baustellen")
                            .font(.subheadline.weight(.semibold)).foregroundStyle(Theme.actionPrimary)
                            .padding(.horizontal, 14).frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                    }
                    .buttonStyle(RowButtonStyle())
                }
            }
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
                        if index > 0 { Divider().padding(.leading, 14) }
                        WorkItemRow(item: item, showsBauakte: nil) {
                            Task {
                                try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
                                model.active?.sync?.localChangeMade()
                                await reload()
                            }
                        }
                        .padding(.horizontal, 10).padding(.vertical, 2)
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
            NavigationRow(title: "Bauakten", symbol: "folder.fill", color: Theme.navyLight) { openTab(.bauakten) }
            Divider().padding(.leading, 54)
            NavigationRow(title: "Meine Aufgaben", symbol: "checklist", color: Color(hex: 0xE07A1F), badge: openTasks) { path.append(.tasks) }
                .accessibilityIdentifier("myTasksTile")
            Divider().padding(.leading, 54)
            NavigationRow(title: "Wer fährt wohin", symbol: "car.2.fill", color: Color(hex: 0x0E7490)) { path.append(.dispatch) }
                .accessibilityIdentifier("dispatchRow")
            Divider().padding(.leading, 54)
            NavigationRow(title: "Meine Woche", symbol: "calendar", color: Color(hex: 0x2F80ED)) { path.append(.week) }
                .accessibilityIdentifier("myWeekTile")
            Divider().padding(.leading, 54)
            NavigationRow(title: "Synchronisation", symbol: "arrow.triangle.2.circlepath", color: Color(hex: 0x1F9D63)) { path.append(.sync) }
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
        let crewColors = Dictionary((board?.crews ?? []).compactMap { crew in Color(hexString: crew.color).map { (crew.name, $0) } },
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
                        HStack(spacing: 6) {
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
                                    .font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(1)
                            }
                        }
                    }
                    if let contact = job.contactName {
                        Label(contact, systemImage: "person.crop.circle").labelStyle(TightLabelStyle())
                            .font(.caption).foregroundStyle(Theme.textSecondary)
                    }
                    if let notes = job.notes, !notes.isEmpty {
                        Text(notes).font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(3)
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
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                BrandMark(onDark: true)
                    .frame(width: 26, height: 26)
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
