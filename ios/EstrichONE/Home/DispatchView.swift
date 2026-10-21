import EstrichCore
import SwiftUI
import UniformTypeIdentifiers

/// „Wer fährt wohin“: welche Kolonne fährt auf welche Baustelle (meine zuerst), Team und Fahrzeuge als Detail. Mit dem Recht
/// zur Planung lassen sich Kolonnen verlegen, zusätzlich einplanen oder herausnehmen – per Menü, auf dem iPad auch per
/// Ziehen der Kolonnen-Chips auf die Baustellen. Ohne Verbindung bleibt der zuletzt geladene Stand des Tages sichtbar.
struct DispatchView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var day = Date.now
    @State private var data: DispatchData.Day?
    @State private var loading = false
    @State private var moveError: String?

    var body: some View {
        List {
            Section {
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
            if let data, !data.isCurrent {
                Label("Ohne Verbindung – Stand \(data.loadedAt.formatted(date: .abbreviated, time: .shortened))", systemImage: "wifi.slash")
                    .font(.footnote).foregroundStyle(Theme.textSecondary)
            }
            if let moveError {
                StatusMessage(kind: .error, text: moveError).listRowInsets(EdgeInsets())
            }
            if let message = data?.message {
                Text(message).foregroundStyle(Theme.textSecondary)
            } else if let board = data?.board, board.sites.isEmpty {
                ContentUnavailableView("Nichts eingeplant", systemImage: "calendar", description: Text("Für diesen Tag gibt es keine Einsätze."))
            }
            if let board = data?.board {
                if !board.crews.isEmpty, sizeClass != .regular || !board.canPlan {
                    Section {
                        crewPalette(board)
                    } header: {
                        Text("Kolonnen")
                    } footer: {
                        if board.canPlan { Text("Kolonne antippen, um sie zu verlegen, zusätzlich einzuplanen oder herauszunehmen.") }
                    }
                }
                ForEach(board.sortedSites) { site in
                    Section {
                        DispatchSiteRow(site: site, isMine: board.mySiteIds.contains(site.id))
                        if !site.crews.isEmpty {
                            ChipFlow {
                                ForEach(site.crews) { crew in
                                    crewChip(crew.id, crew.name, color: crew.color, detail: crew.activity, board: board, from: site.id)
                                        .draggable(DispatchDrag(target: .crew, id: crew.id, from: site.id).text)
                                }
                            }
                            .padding(.vertical, 2)
                        }
                        let team = (site.people.map(\.name) + site.vehicles.map(\.name)).joined(separator: ", ")
                        if !team.isEmpty {
                            Label(team, systemImage: "person.2").labelStyle(TightLabelStyle())
                                .font(.caption).foregroundStyle(Theme.textSecondary)
                        }
                    }
                    .dropDestination(for: String.self) { items, _ in
                        guard board.canPlan, let drag = items.first.flatMap(DispatchDrag.init(text:)), drag.from != site.id else { return false }
                        Task { await move(drag.target, drag.id, to: site.id, from: drag.from) }
                        return true
                    }
                    .accessibilityIdentifier("dispatchSite-\(site.title)")
                }
            }
        }
        .safeAreaInset(edge: .top) {
            // iPad mit Planungsrecht: Kolonnen immer sichtbar, zum Ziehen auf die Baustellen.
            if sizeClass == .regular, let board = data?.board, board.canPlan, !board.crews.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Kolonnen – auf eine Baustelle ziehen oder antippen").font(.caption.weight(.semibold))
                        .foregroundStyle(Theme.textSecondary)
                    crewPalette(board)
                }
                .padding(.horizontal).padding(.vertical, 8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(.bar)
            }
        }
        .overlay { if loading && data == nil { ProgressView() } }
        .navigationTitle("Wer fährt wohin")
        .navigationBarTitleDisplayMode(.inline)
        .task(id: CalendarDay.string(day)) { await load() }
        .refreshable { await load() }
        .sensoryFeedback(.success, trigger: data?.board)
    }

    /// Alle Kolonnen des Tages mit Leitung, Stärke, Fahrzeugen, Abwesenheiten und Doppelbelegung.
    private func crewPalette(_ board: DispatchBoard) -> some View {
        ChipFlow {
            ForEach(board.crews) { crew in
                crewChip(crew.id, crew.name, color: crew.color, detail: Self.details(of: crew, on: board), board: board,
                         from: crew.siteIds.count == 1 ? crew.siteIds.first : nil, warning: crew.isDoubleBooked)
                    .draggable(DispatchDrag(target: .crew, id: crew.id, from: crew.siteIds.count == 1 ? crew.siteIds.first : nil).text)
            }
        }
        .padding(.vertical, 2)
    }

    static func details(of crew: DispatchBoard.Crew, on board: DispatchBoard) -> String {
        var details: [String] = []
        if let leader = crew.leaderName { details.append(leader) }
        if crew.memberCount > 0 { details.append("\(crew.memberCount) Pers.") }
        if !crew.vehicles.isEmpty { details.append(crew.vehicles.map(\.name).joined(separator: ", ")) }
        if !crew.absentMembers.isEmpty {
            details.append("fehlt: " + crew.absentMembers.map { [$0.name, $0.note].compactMap { $0 }.joined(separator: " – ") }.joined(separator: ", "))
        }
        let sites = crew.siteIds.compactMap { id in board.sites.first { $0.id == id }?.title }
        details.append(sites.isEmpty ? "frei" : sites.joined(separator: " + "))
        return details.joined(separator: " · ")
    }

    /// Kolonne als farbiger Chip; mit Planungsrecht als Menü: verlegen, zusätzlich einplanen, herausnehmen.
    @ViewBuilder
    private func crewChip(_ crewId: UUID, _ name: String, color: String?, detail: String?, board: DispatchBoard, from: UUID?, warning: Bool = false) -> some View {
        let tint = Color(hexString: color) ?? Theme.accent
        let label = HStack(spacing: 6) {
            Circle().fill(tint).frame(width: 10, height: 10)
            VStack(alignment: .leading, spacing: 0) {
                Text(name).font(.footnote.weight(.semibold))
                if let detail, !detail.isEmpty {
                    Text(detail).font(.caption2).foregroundStyle(Theme.textSecondary).lineLimit(1)
                }
            }
            if warning {
                Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(Theme.hint).accessibilityLabel("Doppelt eingeplant")
            }
        }
        .padding(.horizontal, 10).padding(.vertical, 5)
        .frame(minHeight: 36)
        .background(tint.opacity(0.14), in: Capsule())
        .overlay(Capsule().strokeBorder(tint.opacity(0.5)))
        .foregroundStyle(Theme.navyText)
        // Bereits anderswo eingeplant (Auswahl ohne Ausgangsbaustelle): zusätzlich einplanen statt alles zu verlegen.
        let alsoElsewhere = from == nil && board.crews.first { $0.id == crewId }?.siteIds.isEmpty == false
        if board.canPlan {
            Menu {
                Section(from == nil ? "Einplanen auf" : "Verlegen nach") {
                    ForEach(board.sortedSites.filter { $0.id != from }) { site in
                        Button(site.title) { Task { await move(.crew, crewId, to: site.id, from: from, keep: alsoElsewhere) } }
                    }
                }
                if from != nil {
                    Menu("Zusätzlich einplanen auf") {
                        ForEach(board.sortedSites.filter { $0.id != from }) { site in
                            Button(site.title) { Task { await move(.crew, crewId, to: site.id, from: from, keep: true) } }
                        }
                    }
                    Button("Aus dem Einsatz nehmen", systemImage: "minus.circle", role: .destructive) {
                        Task { await move(.crew, crewId, to: nil, from: from) }
                    }
                }
            } label: {
                label
            }
            .accessibilityIdentifier("crewChip-\(name)")
        } else {
            label.accessibilityElement(children: .combine).accessibilityIdentifier("crewChip-\(name)")
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

    private func move(_ target: DispatchMove.Target, _ id: UUID, to site: UUID?, from: UUID?, keep: Bool = false) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            let board = try await client.move(DispatchMove(target: target, id: id, to: site, from: from, keep: keep), on: CalendarDay.string(day))
            data = DispatchData.Day(board: board, loadedAt: .now, isCurrent: true, message: nil)
            moveError = nil
        } catch {
            moveError = error == .offline ? "Umsetzen geht nur mit Verbindung zum Server." : error.message
        }
    }
}

/// Gezogene Person bzw. gezogenes Fahrzeug (als Text, damit kein eigener Datentyp registriert werden muss).
struct DispatchDrag {
    let target: DispatchMove.Target
    let id: UUID
    let from: UUID?

    init(target: DispatchMove.Target, id: UUID, from: UUID?) {
        self.target = target
        self.id = id
        self.from = from
    }

    init?(text: String) {
        let parts = text.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
        guard parts.count == 4, parts[0] == "estrichone-dispatch", let target = DispatchMove.Target(rawValue: parts[1]),
              let id = UUID(uuidString: parts[2]) else { return nil }
        self.target = target
        self.id = id
        self.from = UUID(uuidString: parts[3])
    }

    var text: String { "estrichone-dispatch|\(target.rawValue)|\(id.uuidString)|\(from?.uuidString ?? "")" }
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
