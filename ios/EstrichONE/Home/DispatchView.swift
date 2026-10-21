import EstrichCore
import SwiftUI
import UniformTypeIdentifiers

/// „Wer fährt wohin“: Baustellen werden Kolonnen zugeordnet. Je Kolonne (Farbe, Leitung, Stärke, Fahrzeuge, Abwesende)
/// ihre Baustellen des Tages mit Beginn, Adresse und Route; darunter Baustellen ohne Kolonne. Mit dem Recht zur Planung:
/// „Baustelle zuordnen …“ je Kolonne, Baustelle in eine andere Kolonne verschieben oder herausnehmen. iPad: Kolonnen als
/// Spalten, Baustellen-Karten per Ziehen zwischen den Kolonnen und aus dem Vorrat. Ohne Verbindung bleibt der zuletzt
/// geladene Stand des Tages sichtbar.
struct DispatchView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var day = Date.now
    @State private var data: DispatchData.Day?
    @State private var loading = false
    @State private var moveError: String?
    @State private var assigningTo: DispatchBoard.Crew?

    var body: some View {
        Group {
            if sizeClass == .regular, let board = data?.board, !board.crews.isEmpty {
                VStack(spacing: 0) {
                    dayBar.padding(.horizontal).padding(.vertical, 8)
                    statusLines.padding(.horizontal)
                    columns(board)
                }
                .background(Theme.surfaceAlt)
            } else {
                list
            }
        }
        .overlay { if loading && data == nil { ProgressView() } }
        .navigationTitle("Wer fährt wohin")
        .navigationBarTitleDisplayMode(.inline)
        .task(id: CalendarDay.string(day)) { await load() }
        .refreshable { await load() }
        .sensoryFeedback(.success, trigger: data?.board)
        .sheet(item: $assigningTo) { crew in
            if let board = data?.board {
                AssignSiteView(crew: crew, board: board) { bauakteId in
                    assigningTo = nil
                    if let bauakteId { Task { await assign(bauakteId, to: crew.id, from: nil) } }
                }
            }
        }
    }

    // MARK: iPhone: Abschnitte je Kolonne

    private var list: some View {
        List {
            Section { dayBar }
            statusLines
            if let message = data?.message {
                Text(message).foregroundStyle(Theme.textSecondary)
            } else if let board = data?.board, board.sites.isEmpty, board.crews.isEmpty {
                ContentUnavailableView("Nichts eingeplant", systemImage: "calendar", description: Text("Für diesen Tag gibt es keine Einsätze."))
            }
            if let board = data?.board {
                ForEach(board.crews) { crew in
                    Section {
                        let assigned = board.crewSites(crew)
                        if assigned.isEmpty {
                            Text("Noch keine Baustelle").font(.subheadline).foregroundStyle(Theme.textSecondary)
                        }
                        ForEach(assigned) { site in
                            siteRow(site, crew: crew, board: board)
                        }
                        if board.canPlan {
                            Button { assigningTo = crew } label: { Label("Baustelle zuordnen …", systemImage: "plus") }
                                .accessibilityIdentifier("assignSite-\(crew.name)")
                        }
                    } header: {
                        CrewHeader(crew: crew, board: board)
                    }
                }
                let pool = board.sitesWithoutCrew
                if !pool.isEmpty {
                    Section {
                        ForEach(pool) { site in siteRow(site, crew: nil, board: board) }
                    } header: {
                        Text("Ohne Kolonne")
                    }
                }
            }
        }
    }

    @ViewBuilder
    private func siteRow(_ site: DispatchBoard.Site, crew: DispatchBoard.Crew?, board: DispatchBoard) -> some View {
        DispatchSiteRow(site: site, isMine: board.mySiteIds.contains(site.id))
            .contextMenu { if board.canPlan { moveMenu(site, crew: crew, board: board) } }
            .swipeActions(edge: .trailing) {
                if board.canPlan, let crew {
                    Button("Entfernen", systemImage: "minus.circle", role: .destructive) {
                        Task { await assign(site.id, to: nil, from: crew.id) }
                    }
                }
            }
        if board.canPlan {
            Menu {
                moveMenu(site, crew: crew, board: board)
            } label: {
                Label(crew == nil ? "Kolonne zuordnen" : "Andere Kolonne", systemImage: "arrow.left.arrow.right")
                    .font(.footnote.weight(.semibold))
            }
            .accessibilityIdentifier("moveSite-\(site.title)")
        }
    }

    /// Baustelle einer anderen Kolonne zuordnen bzw. aus der Kolonne nehmen.
    @ViewBuilder
    private func moveMenu(_ site: DispatchBoard.Site, crew: DispatchBoard.Crew?, board: DispatchBoard) -> some View {
        Section(crew == nil ? "Kolonne zuordnen" : "In Kolonne verschieben") {
            ForEach(board.crews.filter { $0.id != crew?.id }) { other in
                Button(other.name) { Task { await assign(site.id, to: other.id, from: crew?.id) } }
            }
        }
        if let crew {
            Button("Aus \(crew.name) nehmen", systemImage: "minus.circle", role: .destructive) {
                Task { await assign(site.id, to: nil, from: crew.id) }
            }
        }
    }

    // MARK: iPad: Kolonnen als Spalten

    private func columns(_ board: DispatchBoard) -> some View {
        ScrollView(.horizontal) {
            HStack(alignment: .top, spacing: 14) {
                ForEach(board.crews) { crew in
                    column(title: { CrewHeader(crew: crew, board: board) }, sites: board.crewSites(crew), crew: crew, board: board)
                }
                column(title: { Text("Ohne Kolonne").font(.subheadline.weight(.semibold)).foregroundStyle(Theme.textSecondary) },
                       sites: board.sitesWithoutCrew, crew: nil, board: board)
            }
            .padding()
        }
    }

    private func column<Title: View>(@ViewBuilder title: () -> Title, sites: [DispatchBoard.Site], crew: DispatchBoard.Crew?,
                                     board: DispatchBoard) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            title()
            ScrollView {
                VStack(spacing: 8) {
                    ForEach(sites) { site in
                        DispatchSiteRow(site: site, isMine: board.mySiteIds.contains(site.id))
                            .padding(10)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .card()
                            .contextMenu { if board.canPlan { moveMenu(site, crew: crew, board: board) } }
                            .draggable(SiteDrag(bauakteId: site.id, fromCrewId: crew?.id).text)
                    }
                    if sites.isEmpty {
                        Text(board.canPlan ? "Baustelle hierher ziehen" : "Keine Baustelle")
                            .font(.footnote).foregroundStyle(Theme.textSecondary)
                            .frame(maxWidth: .infinity, minHeight: 60)
                            .overlay(RoundedRectangle(cornerRadius: Theme.cardRadius).strokeBorder(Theme.line, style: StrokeStyle(lineWidth: 1, dash: [5])))
                    }
                    if board.canPlan, let crew {
                        Button { assigningTo = crew } label: { Label("Baustelle zuordnen …", systemImage: "plus") }
                            .estrichButton(.tertiary)
                    }
                }
            }
        }
        .frame(width: 300)
        .frame(maxHeight: .infinity, alignment: .top)
        .padding(10)
        .background(Color(uiColor: .secondarySystemGroupedBackground).opacity(0.6), in: RoundedRectangle(cornerRadius: Theme.cardRadius))
        .dropDestination(for: String.self) { items, _ in
            guard board.canPlan, let drag = items.first.flatMap(SiteDrag.init(text:)), drag.fromCrewId != crew?.id else { return false }
            Task { await assign(drag.bauakteId, to: crew?.id, from: drag.fromCrewId) }
            return true
        }
    }

    // MARK: Gemeinsam

    private var dayBar: some View {
        VStack(spacing: 6) {
            HStack(spacing: 8) {
                Button { shift(-1) } label: { Label("Vorheriger Tag", systemImage: "chevron.left") }
                    .buttonStyle(IconButtonStyle())
                DatePicker("Tag", selection: $day, displayedComponents: .date)
                    .labelsHidden()
                    .frame(maxWidth: .infinity)
                Button { shift(1) } label: { Label("Nächster Tag", systemImage: "chevron.right") }
                    .buttonStyle(IconButtonStyle())
            }
            if !Calendar.current.isDateInToday(day) {
                Button("Heute") { day = .now }
            }
        }
    }

    @ViewBuilder
    private var statusLines: some View {
        if let data, !data.isCurrent {
            Label("Ohne Verbindung – Stand \(data.loadedAt.formatted(date: .abbreviated, time: .shortened))", systemImage: "wifi.slash")
                .font(.footnote).foregroundStyle(Theme.textSecondary)
        }
        if let moveError {
            StatusMessage(kind: .error, text: moveError)
        }
    }

    private func shift(_ days: Int) {
        day = Calendar.current.date(byAdding: .day, value: days, to: day) ?? day
    }

    private func load() async {
        loading = true
        defer { loading = false }
        data = await DispatchData.load(day: CalendarDay.string(day), model: model)
    }

    /// Baustelle einer Kolonne zuordnen (`to`), aus einer Kolonne nehmen (`from`) oder beides (verschieben).
    private func assign(_ bauakteId: UUID, to crewId: UUID?, from oldCrewId: UUID?) async {
        guard let client = model.active?.client else { return }
        let dayText = CalendarDay.string(day)
        do throws(APIError) {
            var board: DispatchBoard?
            if let crewId {
                board = try await client.move(DispatchMove(target: .crew, id: crewId, to: bauakteId, from: nil, keep: true), on: dayText)
            }
            if let oldCrewId {
                board = try await client.move(DispatchMove(target: .crew, id: oldCrewId, to: nil, from: bauakteId), on: dayText)
            }
            if let board { data = DispatchData.Day(board: board, loadedAt: .now, isCurrent: true, message: nil) }
            moveError = nil
        } catch {
            moveError = error == .offline ? "Planen geht nur mit Verbindung zum Server." : error.message
            await load()
        }
    }
}

extension DispatchBoard {
    /// Baustellen einer Kolonne an diesem Tag (meine zuerst, dann nach Beginn).
    func crewSites(_ crew: Crew) -> [Site] {
        sortedSites.filter { site in crew.siteIds.contains(site.id) || site.crews.contains { $0.id == crew.id } }
    }

    /// Baustellen, denen noch keine Kolonne zugeordnet ist.
    var sitesWithoutCrew: [Site] {
        let assigned = Set(crews.flatMap(\.siteIds))
        return sortedSites.filter { $0.crews.isEmpty && !assigned.contains($0.id) }
    }

    /// Kolonnen mit Baustellen zuerst; ohne Kolonnen (ältere Server) alle Baustellen in einer Gruppe.
    var crewGroups: [(crew: Crew?, sites: [Site])] {
        let groups: [(crew: Crew?, sites: [Site])] = crews.map { (crew: Optional($0), sites: crewSites($0)) }.filter { !$0.sites.isEmpty }
        let rest = sitesWithoutCrew
        return rest.isEmpty ? groups : groups + [(crew: nil, sites: rest)]
    }
}

/// Kopf einer Kolonne: Farbe, Name, Leitung, Stärke, Fahrzeuge, Abwesende, Hinweis bei Doppelbelegung.
struct CrewHeader: View {
    let crew: DispatchBoard.Crew
    let board: DispatchBoard

    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            Circle().fill(Color(hexString: crew.color) ?? Theme.accent).frame(width: 12, height: 12).padding(.top, 3)
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text(crew.name).font(.subheadline.weight(.bold)).foregroundStyle(Theme.navyText)
                    if crew.isDoubleBooked {
                        Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(Theme.hint)
                            .accessibilityLabel("Mehrere Baustellen")
                    }
                }
                let details = [crew.leaderName, crew.memberCount > 0 ? "\(crew.memberCount) Pers." : nil,
                               crew.vehicles.isEmpty ? nil : crew.vehicles.map(\.name).joined(separator: ", ")].compactMap { $0 }
                if !details.isEmpty {
                    Text(details.joined(separator: " · ")).font(.caption).foregroundStyle(Theme.textSecondary)
                }
                if !crew.absentMembers.isEmpty {
                    Text("fehlt: " + crew.absentMembers.map { [$0.name, $0.note].compactMap { $0 }.joined(separator: " – ") }.joined(separator: ", "))
                        .font(.caption).foregroundStyle(Theme.hint)
                }
            }
        }
        .textCase(nil)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("crew-\(crew.name)")
    }
}

/// Auswahl einer Baustelle für eine Kolonne: Baustellen des Tages und aktive Bauakten auf dem Gerät, mit Suche.
struct AssignSiteView: View {
    let crew: DispatchBoard.Crew
    let board: DispatchBoard
    let onDone: (UUID?) -> Void

    @Environment(AppModel.self) private var model
    @State private var search = ""
    @State private var bauakten: [Bauakte] = []

    var body: some View {
        NavigationStack {
            List {
                let daySites = board.sortedSites.filter { !crew.siteIds.contains($0.id) && matches([$0.number, $0.title, $0.address]) }
                if !daySites.isEmpty {
                    Section("Baustellen des Tages") {
                        ForEach(daySites) { site in
                            Button { onDone(site.id) } label: {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text([site.number, site.title].compactMap { $0 }.joined(separator: " · ")).font(.subheadline.weight(.semibold))
                                    Text(site.crews.isEmpty ? "ohne Kolonne" : site.crews.map(\.name).joined(separator: ", "))
                                        .font(.caption).foregroundStyle(Theme.textSecondary)
                                }
                            }
                        }
                    }
                }
                let known = Set(board.sites.map(\.id))
                let others = bauakten.filter { !known.contains($0.id) }
                if !others.isEmpty {
                    Section("Weitere aktive Bauakten") {
                        ForEach(others) { bauakte in
                            Button { onDone(bauakte.id) } label: { BauakteRow(bauakte: bauakte) }
                        }
                    }
                }
            }
            .searchable(text: $search, prompt: "Nummer, Bauvorhaben oder Adresse")
            .navigationTitle("Baustelle für \(crew.name)")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { onDone(nil) } } }
            .task(id: search) {
                let all = (try? await model.active?.service?.bauakten(search: search.isEmpty ? nil : search)) ?? []
                bauakten = all.filter { [BauakteStatus.planned, .inProgress, .drying].contains($0.status) }
            }
        }
    }

    private func matches(_ fields: [String?]) -> Bool {
        let query = search.trimmingCharacters(in: .whitespaces).lowercased()
        return query.isEmpty || fields.compactMap { $0?.lowercased() }.contains { $0.contains(query) }
    }
}

/// Gezogene Baustelle mit ihrer bisherigen Kolonne (als Text, damit kein eigener Datentyp registriert werden muss).
struct SiteDrag {
    let bauakteId: UUID
    let fromCrewId: UUID?

    init(bauakteId: UUID, fromCrewId: UUID?) {
        self.bauakteId = bauakteId
        self.fromCrewId = fromCrewId
    }

    init?(text: String) {
        let parts = text.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
        guard parts.count == 3, parts[0] == "estrichone-site", let id = UUID(uuidString: parts[1]) else { return nil }
        bauakteId = id
        fromCrewId = UUID(uuidString: parts[2])
    }

    var text: String { "estrichone-site|\(bauakteId.uuidString)|\(fromCrewId?.uuidString ?? "")" }
}

/// Laden und Zwischenspeichern der Tafel (auch für die Startseite).
enum DispatchData {
    struct Day: Equatable {
        let board: DispatchBoard?
        let loadedAt: Date
        let isCurrent: Bool
        /// Hinweis statt Liste (z. B. fehlendes Recht).
        let message: String?
    }

    @MainActor
    static func load(day: String, model: AppModel) async -> Day? {
        guard let active = model.active else { return nil }
        let key = "dispatch_board_\(day)"
        do throws(APIError) {
            let board = try await active.client.dispatch(day: day)
            if let encoded = try? JSONCoding.encoder.encode(board) {
                try? await active.sync?.store.setMeta(key, String(decoding: encoded, as: UTF8.self))
            }
            return Day(board: board, loadedAt: .now, isCurrent: true, message: nil)
        } catch {
            if error.status == 403 {
                return Day(board: nil, loadedAt: .now, isCurrent: true, message: "Für die Planung fehlt Ihnen die Berechtigung.")
            }
            if let text = try? await active.sync?.store.meta(key),
               let cached = try? JSONCoding.decoder.decode(DispatchBoard.self, from: Data(text.utf8)) {
                return Day(board: cached, loadedAt: .now, isCurrent: false, message: nil)
            }
            return Day(board: nil, loadedAt: .now, isCurrent: false, message: "Ohne Verbindung ist die Planung für diesen Tag nicht verfügbar.")
        }
    }
}

/// Baustelle in „Wer fährt wohin“: Beginn, Bezeichnung, Einsätze, Adresse mit Navigation und Ansprechpartner.
struct DispatchSiteRow: View {
    let site: DispatchBoard.Site
    var isMine = false

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text(site.startText ?? "–")
                .font(.subheadline.monospacedDigit().weight(.bold))
                .foregroundStyle(Theme.navyText)
                .frame(width: 46, alignment: .leading)
            NavigationLink(value: HomeRoute.bauakte(site.bauakteId)) {
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        Text([site.number, site.title].compactMap { $0 }.joined(separator: " · "))
                            .font(.subheadline.weight(.semibold)).lineLimit(2)
                        if isMine {
                            Text("Ich").font(.caption2.weight(.bold)).foregroundStyle(Theme.onAccent)
                                .padding(.horizontal, 6).padding(.vertical, 1)
                                .background(Theme.accent, in: Capsule())
                        }
                    }
                    if !site.crews.isEmpty {
                        HStack(spacing: 8) {
                            ForEach(site.crews) { crew in
                                HStack(spacing: 4) {
                                    Circle().fill(Color(hexString: crew.color) ?? Theme.accent).frame(width: 8, height: 8)
                                    Text(crew.name).font(.caption.weight(.semibold))
                                }
                            }
                        }
                        .foregroundStyle(Theme.navyText)
                    }
                    ForEach(site.jobs) { job in
                        Text([job.activity, job.crewName, [job.startText, job.endText].compactMap { $0 }.joined(separator: "–")]
                            .compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " · "))
                            .font(.footnote)
                    }
                    if let address = site.address {
                        Label(address, systemImage: "mappin.and.ellipse").labelStyle(TightLabelStyle())
                            .font(.caption).foregroundStyle(Theme.textSecondary).lineLimit(1)
                    }
                    ForEach(site.warnings, id: \.self) { warning in
                        Label(warning, systemImage: "exclamationmark.triangle").labelStyle(TightLabelStyle())
                            .font(.caption).foregroundStyle(Theme.hint)
                    }
                }
            }
            if let url = site.navigationUrl.flatMap(URL.init(string:)) {
                Link(destination: url) { Label("Route starten", systemImage: "car.fill") }
                    .buttonStyle(IconButtonStyle(rank: .primary))
            }
        }
        .padding(.vertical, 2)
    }
}

/// Einfacher Fließumbruch für Chips.
struct ChipFlow<Content: View>: View {
    @ViewBuilder let content: () -> Content

    var body: some View {
        FlowLayout(spacing: 6) { content() }
    }
}

private struct FlowLayout: Layout {
    var spacing: CGFloat

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 320
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowHeight: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > 0, x + size.width > width {
                x = 0
                y += rowHeight + spacing
                rowHeight = 0
            }
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
        return CGSize(width: width, height: y + rowHeight)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var rowHeight: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > bounds.minX, x + size.width > bounds.maxX {
                x = bounds.minX
                y += rowHeight + spacing
                rowHeight = 0
            }
            subview.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(size))
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
    }
}
