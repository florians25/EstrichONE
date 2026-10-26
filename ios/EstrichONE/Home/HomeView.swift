import EstrichCore
import SwiftUI

/// Heute-Seite: kompakter Kopf (Begrüßung, Datum, Abgleich), Hinweise nur wenn vorhanden, drei gleich große
/// Schnellzugriffe (Wer fährt wohin, Aufgaben, Meine Woche), „Mein Einsatz heute“ als aufgeräumte Karten mit
/// Akte · Anrufen · Route, darunter kompakt Morgen, offene Aufgaben und zuletzt geöffnet – ohne Wiederholungen.
/// iPad: Einsätze links, der Rest rechts.
struct HomeView: View {
    let openTab: (MainTabView.Tab) -> Void
    var openCapture: (CaptureMode) -> Void = { _ in }

    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var path: [HomeRoute] = []
    @State private var planning: MyPlanning.Snapshot?
    @State private var dispatch: DispatchData.Day?
    @State private var tasks: [WorkItem] = []
    @State private var recent: [Bauakte] = []
    @State private var bauakten: [UUID: Bauakte] = [:]
    /// „2026-041 · EFH Schneider“ je Bauakte der offenen Aufgaben.
    @State private var taskBauakten: [UUID: String] = [:]
    @State private var loaded = false

    private var isWide: Bool { sizeClass == .regular }

    private var jobs: [TodayJob] {
        TodayJob.make(assignments: planning?.assignments(on: CalendarDay.string(.now)) ?? [], board: dispatch?.board, bauakten: bauakten)
    }

    private var tomorrow: [Assignment] {
        let day = Calendar.current.date(byAdding: .day, value: 1, to: .now) ?? .now
        return planning?.assignments(on: CalendarDay.string(day)) ?? []
    }

    var body: some View {
        NavigationStack(path: $path) {
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    VStack(alignment: .leading, spacing: 10) {
                        HomeHeader { path.append(.sync) }
                        if let sync = model.active?.sync {
                            SyncStatusBar(sync: sync)
                                .clipShape(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous))
                            SystemNoticesBanner(sync: sync)
                        }
                    }
                    quickLinks
                    if isWide {
                        HStack(alignment: .top, spacing: 24) {
                            todaySection.frame(maxWidth: .infinity, alignment: .leading)
                            VStack(alignment: .leading, spacing: 24) {
                                tomorrowSection
                                tasksSection
                                recentSection
                            }
                            .frame(width: 360)
                        }
                    } else {
                        todaySection
                        tomorrowSection
                        tasksSection
                        recentSection
                    }
                }
                .padding(.horizontal, isWide ? 24 : 16)
                .padding(.top, 8)
                .padding(.bottom, 32)
                .frame(maxWidth: 1200)
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
            .task(id: model.active?.sync?.revision) { await reload() }
            .refreshable {
                await model.active?.sync?.run()
                await reload()
            }
        }
    }

    // MARK: Abschnitte

    /// Drei gleich große, beschriftete Schnellzugriffe.
    private var quickLinks: some View {
        HStack(spacing: 10) {
            QuickLink(title: "Wer fährt wohin", symbol: "car.2") { path.append(.dispatch) }
                .accessibilityIdentifier("dispatchRow")
            QuickLink(title: "Aufgaben", symbol: "checklist", badge: tasks.count) { path.append(.tasks) }
                .accessibilityIdentifier("myTasksTile")
            QuickLink(title: "Meine Woche", symbol: "calendar") { path.append(.week) }
                .accessibilityIdentifier("myWeekTile")
        }
    }

    private var todaySection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                SectionTitle("Mein Einsatz heute")
                Spacer()
                if let planning, !planning.isCurrent {
                    Label("Stand \(planning.loadedAt.formatted(date: .omitted, time: .shortened))", systemImage: "wifi.slash")
                        .labelStyle(TightLabelStyle())
                        .font(.caption2).foregroundStyle(Theme.textSecondary)
                }
            }
            if !loaded {
                TodayJobCard.placeholder
            } else if jobs.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    Label("Heute ist für Sie nichts eingeplant.", systemImage: "calendar")
                        .font(.body)
                    if let next = tomorrow.first {
                        Text("Morgen: \([next.startText, next.bauakteTitle].compactMap { $0 }.joined(separator: " · "))")
                            .font(.subheadline).foregroundStyle(Theme.textSecondary)
                    }
                }
                .padding(16)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card()
            } else {
                ForEach(jobs) { job in
                    TodayJobCard(job: job) { path.append(.bauakte(job.bauakteId)) }
                }
            }
        }
        .accessibilityIdentifier("todayAssignments")
    }

    /// Morgen: eine Zeile je Einsatz (nur wenn heute etwas ansteht; sonst steht morgen schon im Leerzustand).
    @ViewBuilder
    private var tomorrowSection: some View {
        let items = tomorrow
        if loaded, !items.isEmpty, !jobs.isEmpty || items.count > 1 {
            VStack(alignment: .leading, spacing: 10) {
                SectionTitle("Morgen")
                VStack(spacing: 0) {
                    ForEach(Array(items.prefix(4).enumerated()), id: \.element.id) { index, item in
                        if index > 0 { Divider().padding(.leading, 72) }
                        compactRow(time: item.startText, title: item.bauakteTitle,
                                   subtitle: [item.activity, item.crewName].compactMap { $0 }.joined(separator: " · ")) {
                            path.append(.bauakte(item.bauakteId))
                        }
                    }
                }
                .card()
            }
            .accessibilityIdentifier("tomorrowPreview")
        }
    }

    @ViewBuilder
    private var tasksSection: some View {
        if !tasks.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                HStack(alignment: .firstTextBaseline) {
                    SectionTitle("Offene Aufgaben")
                    Spacer()
                    Button("Alle (\(tasks.count))") { path.append(.tasks) }.font(.subheadline.weight(.semibold))
                }
                VStack(spacing: 0) {
                    ForEach(Array(tasks.prefix(3).enumerated()), id: \.element.id) { index, item in
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

    /// Zuletzt geöffnet, ohne die Baustellen, die oben schon als Einsatz stehen.
    @ViewBuilder
    private var recentSection: some View {
        let shown = Set(jobs.map(\.bauakteId))
        let items = recent.filter { !shown.contains($0.id) }.prefix(3)
        if !items.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                SectionTitle("Zuletzt geöffnet")
                VStack(spacing: 0) {
                    ForEach(Array(items.enumerated()), id: \.element.id) { index, bauakte in
                        if index > 0 { Divider().padding(.leading, 16) }
                        Button { path.append(.bauakte(bauakte.id)) } label: {
                            HStack(spacing: 10) {
                                BauakteRow(bauakte: bauakte)
                                Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
                            }
                            .padding(.horizontal, 16).padding(.vertical, 10)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(RowButtonStyle())
                    }
                }
                .card()
            }
        }
    }

    private func compactRow(time: String?, title: String, subtitle: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 12) {
                Text(time ?? "–")
                    .font(.subheadline.monospacedDigit().weight(.semibold))
                    .frame(width: 48, alignment: .leading)
                VStack(alignment: .leading, spacing: 1) {
                    Text(title).font(.subheadline.weight(.semibold)).lineLimit(1)
                    if !subtitle.isEmpty {
                        Text(subtitle).font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(1)
                    }
                }
                Spacer(minLength: 0)
                Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
            }
            .padding(.horizontal, 16).padding(.vertical, 10)
            .contentShape(Rectangle())
        }
        .buttonStyle(RowButtonStyle())
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        // Zuerst alles Lokale (sofort sichtbar), danach die Planung vom Server.
        recent = Array(((try? await service.recentBauakten()) ?? []).prefix(6))
        tasks = (try? await service.myWorkItems()) ?? []
        if let store = model.active?.sync?.store, planning == nil {
            planning = await MyPlanning(store: store).cached()
        }
        loaded = loaded || planning != nil
        dispatch = await DispatchData.load(day: CalendarDay.string(.now), model: model)
        if let store = model.active?.sync?.store {
            planning = await MyPlanning(store: store).load(using: model.active?.client)
        }
        loaded = true
        var ids = Set((planning?.assignments(on: CalendarDay.string(.now)) ?? []).map(\.bauakteId))
        ids.formUnion(dispatch?.board?.mySiteIds ?? [])
        ids.formUnion(tasks.compactMap(\.bauakteId))
        var loadedAkten: [UUID: Bauakte] = [:]
        for id in ids {
            if let bauakte = try? await service.bauakte(id) { loadedAkten[id] = bauakte }
        }
        bauakten = loadedAkten
        var names: [UUID: String] = [:]
        for item in tasks {
            if let id = item.bauakteId, let bauakte = loadedAkten[id] {
                names[id] = [bauakte.number, bauakte.title].compactMap { $0 }.joined(separator: " · ")
            }
        }
        taskBauakten = names
    }
}

/// Schnellzugriff: Symbol über Text, gleich groß, ruhig; optional mit Zähler.
struct QuickLink: View {
    let title: String
    let symbol: String
    var badge = 0
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 6) {
                Image(systemName: symbol)
                    .font(.title3)
                    .foregroundStyle(Theme.actionPrimary)
                    .frame(height: 24)
                    .overlay(alignment: .topTrailing) {
                        if badge > 0 {
                            Text("\(badge)")
                                .font(.caption2.weight(.bold).monospacedDigit())
                                .foregroundStyle(.white)
                                .padding(.horizontal, 5).padding(.vertical, 1)
                                .background(Theme.actionPrimary, in: Capsule())
                                .offset(x: 14, y: -6)
                        }
                    }
                Text(title)
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(Color.primary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }
            .frame(maxWidth: .infinity, minHeight: 72)
            .background(Theme.card, in: RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
            .contentShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
        }
        .buttonStyle(RowButtonStyle())
        .accessibilityLabel(badge > 0 ? "\(title), \(badge) offen" : title)
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

/// Karte für einen Einsatz: Uhrzeit groß links, Baustelle, Adresse, Kolonne (Punkt + Name), Tätigkeit, Hinweise;
/// darunter drei gleich große Aktionen Akte · Anrufen · Route.
struct TodayJobCard: View {
    let job: TodayJob
    let open: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Button(action: open) {
                HStack(alignment: .top, spacing: 14) {
                    VStack(alignment: .leading, spacing: 0) {
                        Text(job.startText ?? "–")
                            .font(.title2.monospacedDigit().weight(.bold))
                        if let end = job.endText {
                            Text("bis \(end)").font(.caption.monospacedDigit()).foregroundStyle(Theme.textSecondary)
                        }
                    }
                    .frame(width: 72, alignment: .leading)
                    VStack(alignment: .leading, spacing: 4) {
                        Text([job.number, job.title].compactMap { $0 }.joined(separator: " · "))
                            .font(.headline)
                            .lineLimit(2)
                            .multilineTextAlignment(.leading)
                        if let address = job.address {
                            Text(address).font(.subheadline).foregroundStyle(Theme.textSecondary).lineLimit(2)
                        }
                        HStack(spacing: 6) {
                            if let crew = job.crewName {
                                Circle().fill(job.crewColor ?? Theme.textSecondary).frame(width: 8, height: 8)
                                Text(crew).font(.subheadline.weight(.medium))
                                Text("·").foregroundStyle(Theme.textSecondary)
                            }
                            Text(job.activity).font(.subheadline).foregroundStyle(Theme.textSecondary)
                        }
                        .lineLimit(1)
                        if job.status != nil, let status = job.status, status == .cancelled {
                            Text("Bauakte storniert").font(.caption.weight(.semibold)).foregroundStyle(Theme.error)
                        }
                        ForEach(job.warnings, id: \.self) { warning in
                            Label(warning, systemImage: "exclamationmark.triangle").labelStyle(TightLabelStyle())
                                .font(.caption.weight(.medium)).foregroundStyle(Theme.hint)
                        }
                    }
                    Spacer(minLength: 0)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("todayJob-\(job.title)")

            HStack(spacing: 8) {
                Button(action: open) { Label("Akte", systemImage: "folder") }
                    .estrichButton(.secondary, size: .compact, fullWidth: true)
                    .accessibilityIdentifier("todayJobOpen")
                action("Anrufen", "phone", job.phone, id: "todayJobCall")
                action("Route", "arrow.triangle.turn.up.right.diamond", job.route, id: "todayJobRoute")
            }
            .lineLimit(1)
        }
        .padding(16)
        .card()
    }

    @ViewBuilder
    private func action(_ title: String, _ symbol: String, _ url: URL?, id: String) -> some View {
        if let url {
            Link(destination: url) { Label(title, systemImage: symbol) }
                .estrichButton(.secondary, size: .compact, fullWidth: true)
                .accessibilityIdentifier(id)
        } else {
            Button {} label: { Label(title, systemImage: symbol) }
                .estrichButton(.secondary, size: .compact, fullWidth: true)
                .disabled(true)
        }
    }

    /// Platzhalter beim ersten Laden.
    static var placeholder: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("08:00  Baustelle wird geladen").font(.title3.weight(.bold))
            Text("Straße 1, 00000 Ort").font(.subheadline)
            Text("Kolonne · Tätigkeit").font(.subheadline)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
        .redacted(reason: .placeholder)
    }
}

/// Hauptaktion der Startseite, die eine Baustelle braucht.
enum SiteTask: String, Identifiable {
    case open, diary, inspection, acceptance, defect
    var id: String { rawValue }
    var title: String {
        switch self {
        case .open: "Baustelle öffnen"
        case .diary: "Bautagebuch – Baustelle"
        case .inspection: "Erstbegehung – Baustelle"
        case .acceptance: "Abnahme – Baustelle"
        case .defect: "Mangel – Baustelle"
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
                    .font(.body)
                    .foregroundStyle(Theme.actionPrimary)
                    .frame(width: 28, height: 28)
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

/// Kompakter Kopf: Begrüßung und Datum in einer Zeile, rechts Abgleich (antippen = Details) und – auf Poolgeräten –
/// Personenwechsel als Symbol.
struct HomeHeader: View {
    var onSync: () -> Void = {}

    @Environment(AppModel.self) private var model

    var body: some View {
        HStack(alignment: .center, spacing: 10) {
            if let user = model.active?.user {
                ViewThatFits(in: .horizontal) {
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        greetingText(user)
                        dateText
                    }
                    VStack(alignment: .leading, spacing: 0) {
                        greetingText(user)
                        dateText
                    }
                }
            }
            Spacer(minLength: 4)
            Button(action: onSync) {
                Image(systemName: syncState.symbol)
                    .font(.body.weight(.semibold))
                    .foregroundStyle(syncState.tint)
                    .frame(width: 40, height: 40)
                    .background(Theme.card, in: Circle())
            }
            .accessibilityLabel("Abgleich: \(syncState.text)")
            .accessibilityIdentifier("syncStatusIcon")
            if model.isPoolDevice {
                Button { model.lock() } label: {
                    Image(systemName: "person.2")
                        .font(.body.weight(.semibold))
                        .foregroundStyle(Theme.actionPrimary)
                        .frame(width: 40, height: 40)
                        .background(Theme.card, in: Circle())
                }
                .accessibilityLabel("Person wechseln")
                .accessibilityIdentifier("switchUserButton")
            }
        }
    }

    private func greetingText(_ user: UserSummary) -> some View {
        Text("\(greeting), \(Self.firstName(user.displayName))")
            .font(.title2.weight(.bold))
            .lineLimit(1)
            .minimumScaleFactor(0.8)
            .accessibilityLabel(user.displayName)
            .accessibilityIdentifier("currentUserName")
    }

    private var dateText: some View {
        Text(Date.now.formatted(.dateTime.weekday(.abbreviated).day().month(.abbreviated)))
            .font(.subheadline)
            .foregroundStyle(Theme.textSecondary)
            .lineLimit(1)
    }

    private var syncState: (symbol: String, text: String, tint: Color) {
        guard let sync = model.active?.sync else { return ("icloud.slash", "nicht verbunden", Theme.textSecondary) }
        if sync.isRunning { return ("arrow.triangle.2.circlepath", "wird abgeglichen", Theme.actionPrimary) }
        if sync.isOffline { return ("wifi.slash", "offline", Theme.textSecondary) }
        if sync.summary?.hasProblems == true { return ("exclamationmark.icloud", "bitte prüfen", Theme.error) }
        if let summary = sync.summary, !summary.isSettled { return ("icloud.and.arrow.up", "wird übertragen", Theme.actionPrimary) }
        return ("checkmark.icloud", "aktuell", Theme.textSecondary)
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
