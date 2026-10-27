import EstrichCore
import SwiftUI

/// „Wer fährt wohin“ je Kolonne (`GET /planning/day/{day}`): Tag wählen, je Kolonne Besetzung, Fahrzeuge und die
/// Baustellen in Fahrtreihenfolge mit Uhrzeit, Adresse (Route), Ansprechpartner (Anruf) und Notizen. Meine Kolonne steht
/// oben und ist hervorgehoben; Freigabestand und Änderungen seit der Freigabe sind markiert.
///
/// Schnelldisposition (Recht zur Planung): Baustelle zuweisen, verschieben (Tag/Kolonne/Uhrzeit), entfernen,
/// Reihenfolge ziehen, Mitarbeiter oder Fahrzeug tauschen – über dieselben Endpunkte wie das Cockpit im Web. Ohne
/// Verbindung werden Änderungen vorgemerkt und beim nächsten Laden gesendet.
struct DispatchView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var day: Date
    @State private var plan: DayPlan?
    @State private var loadedAt: Date?
    @State private var isCurrent = true
    @State private var message: String?
    @State private var loading = false
    @State private var pending: [PlanningOutbox.Entry] = []
    @State private var toast: String?
    @State private var notice: [String] = []
    @State private var sheet: PlanSheet?
    @State private var removing: DayPlan.Stop?
    @State private var choosingDate = false
    @State private var editMode: EditMode = .inactive
    @State private var showsPending = false
    @State private var releasing = false

    init(initialDay: Date = Calendar.current.startOfDay(for: .now)) {
        _day = State(initialValue: Calendar.current.startOfDay(for: initialDay))
    }

    private var dayText: String { CalendarDay.string(day) }

    var body: some View {
        List {
            Section {
                DayStrip(day: $day) { choosingDate = true }
            }
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets(top: 0, leading: 0, bottom: 0, trailing: 0))
            statusSection
            if let plan {
                ForEach(plan.sortedCrews) { crew in crewSection(crew, plan: plan) }
                if !plan.withoutCrew.isEmpty {
                    Section {
                        ForEach(plan.withoutCrew) { stop in stopRow(stop, crew: nil, plan: plan) }
                    } header: {
                        Text("Ohne Kolonne")
                    }
                }
                peopleSection(plan)
            } else if !loading, let message {
                Section { EmptyStateView(symbol: "calendar.badge.exclamationmark", title: "Keine Planung", message: message) }
            }
        }
        .listStyle(.insetGrouped)
        .listSectionSpacing(.compact)
        .environment(\.editMode, $editMode)
        .frame(maxWidth: sizeClass == .regular ? 820 : .infinity)
        .frame(maxWidth: .infinity)
        .background(Theme.canvas)
        .overlay { if loading && plan == nil { ProgressView() } }
        .navigationTitle("Wer fährt wohin")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if plan?.canPlan == true, plan?.crews.contains(where: { $0.stops.count > 1 }) == true {
                ToolbarItem(placement: .primaryAction) {
                    Button(editMode.isEditing ? "Fertig" : "Reihenfolge") {
                        withAnimation { editMode = editMode.isEditing ? .inactive : .active }
                    }
                    .accessibilityIdentifier("reorderButton")
                }
            }
        }
        .task(id: dayText) { await load() }
        .refreshable { await load() }
        .sheet(item: $sheet) { sheet in sheetView(sheet) }
        .sheet(isPresented: $choosingDate) {
            NavigationStack {
                DatePicker("Tag", selection: $day, displayedComponents: .date)
                    .datePickerStyle(.graphical)
                    .padding()
                    .navigationTitle("Tag wählen")
                    .navigationBarTitleDisplayMode(.inline)
                    .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Fertig") { choosingDate = false } } }
            }
            .presentationDetents([.medium, .large])
        }
        .sheet(isPresented: $showsPending) { PendingPlanChangesView(entries: $pending, outbox: outbox) }
        .confirmationDialog("Aus dem Plan nehmen?", isPresented: Binding { removing != nil } set: { if !$0 { removing = nil } },
                            titleVisibility: .visible, presenting: removing) { stop in
            Button("„\(stop.title)“ entfernen", role: .destructive) {
                Task { await perform(.remove(id: stop.assignmentId, title: stop.title)) }
            }
        } message: { stop in
            Text("\(stop.activity) am \(day.formatted(.dateTime.weekday(.wide).day().month())) wird gelöscht.")
        }
        .toast($toast)
        .sensoryFeedback(.success, trigger: plan)
    }

    // MARK: Abschnitte

    @ViewBuilder
    private var statusSection: some View {
        let lines = statusLines
        if !lines.isEmpty {
            Section {
                ForEach(lines.indices, id: \.self) { index in lines[index] }
            }
            .listRowInsets(EdgeInsets(top: Space.xs, leading: 0, bottom: Space.xs, trailing: 0))
            .listRowBackground(Color.clear)
            .listRowSeparator(.hidden)
        }
    }

    private var statusLines: [AnyView] {
        var lines: [AnyView] = []
        if !isCurrent, let loadedAt {
            lines.append(AnyView(NoticeLine(tone: .neutral, text: "Ohne Verbindung – Stand \(loadedAt.formatted(date: .abbreviated, time: .shortened))",
                                            symbol: "wifi.slash")))
        }
        if !pending.isEmpty {
            let failed = pending.contains { $0.error != nil }
            lines.append(AnyView(NoticeLine(tone: failed ? .danger : .warning,
                                            text: failed ? "Vorgemerkte Änderung abgelehnt" : "\(pending.count) \(pending.count == 1 ? "Änderung" : "Änderungen") vorgemerkt – wird gesendet, sobald Verbindung besteht",
                                            symbol: "tray.and.arrow.up", actionTitle: "Anzeigen") { showsPending = true }))
        }
        if let holiday = plan?.holiday {
            lines.append(AnyView(NoticeLine(tone: .info, text: "Feiertag: \(holiday)", symbol: "calendar")))
        }
        if let plan, plan.stopCount > 0 || plan.release != nil {
            if let release = plan.release {
                let when = release.releasedAt.formatted(.dateTime.day().month(.abbreviated).hour().minute())
                let text = release.changedCount > 0
                    ? "Freigegeben \(when) · \(release.changedCount) \(release.changedCount == 1 ? "Änderung" : "Änderungen") seit Freigabe"
                    : "Freigegeben \(when) von \(release.releasedByName)"
                lines.append(AnyView(NoticeLine(tone: release.changedCount > 0 ? .warning : .success, text: text,
                                                symbol: release.changedCount > 0 ? "arrow.triangle.2.circlepath" : "checkmark.seal",
                                                actionTitle: plan.canPlan ? (release.changedCount > 0 ? "Erneut freigeben" : "Freigabe") : nil,
                                                action: plan.canPlan ? { sheet = .release } : nil)
                    .accessibilityIdentifier("releaseStatus")))
                for removed in release.removed {
                    lines.append(AnyView(NoticeLine(tone: .neutral, text: "Entfallen: \(removed.title) – \(removed.activity)", symbol: "minus.circle")))
                }
            } else {
                lines.append(AnyView(NoticeLine(tone: .neutral, text: "Entwurf – noch nicht freigegeben", symbol: "pencil.and.outline",
                                                actionTitle: plan.canPlan ? "Freigeben" : nil, action: plan.canPlan ? { sheet = .release } : nil)
                    .accessibilityIdentifier("releaseStatus")))
            }
        }
        for warning in notice {
            lines.append(AnyView(NoticeLine(tone: .warning, text: warning, symbol: "exclamationmark.triangle")))
        }
        return lines
    }

    @ViewBuilder
    private func crewSection(_ crew: DayPlan.Crew, plan: DayPlan) -> some View {
        Section {
            CrewCardHeader(crew: crew, canPlan: plan.canPlan && !editMode.isEditing, canSwapCrews: plan.crews.count > 1) { action in
                switch action {
                case .assign: sheet = .assign(crew: crew)
                case .swapPerson: sheet = .swap(crew: crew, vehicle: false)
                case .swapVehicle: sheet = .swap(crew: crew, vehicle: true)
                case .swapCrews: sheet = .swapCrews(crew: crew)
                }
            }
            .listRowBackground(crew.isMine ? Theme.accentSoft : Theme.surface)
            if crew.stops.isEmpty {
                Text(crew.available.isEmpty ? "Heute nicht besetzt" : "Noch keine Baustelle")
                    .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
            }
            ForEach(crew.stops) { stop in stopRow(stop, crew: crew, plan: plan) }
                .onMove { from, to in
                    var ids = crew.stops.map(\.assignmentId)
                    ids.move(fromOffsets: from, toOffset: to)
                    // Uhrzeiten bleiben in ihrer Reihenfolge, die Baustellen tauschen die Plätze.
                    let times = crew.stops.map(\.startTime)
                    Task { await perform(.order(crewId: crew.id, day: dayText, assignmentIds: ids, startTimes: times)) }
                }
                .moveDisabled(!plan.canPlan)
            if plan.canPlan, !editMode.isEditing {
                Button { sheet = .assign(crew: crew) } label: { Label("Baustelle zuweisen", systemImage: "plus") }
                    .accessibilityIdentifier("assign-\(crew.name)")
            }
        }
    }

    private func stopRow(_ stop: DayPlan.Stop, crew: DayPlan.Crew?, plan: DayPlan) -> some View {
        StopRow(stop: stop, tint: Color(hexString: crew?.color) ?? Theme.accent)
            .swipeActions(edge: .trailing) {
                if plan.canPlan {
                    Button("Entfernen", systemImage: "trash", role: .destructive) { removing = stop }
                    Button("Verschieben", systemImage: "arrow.right.circle") { sheet = .move(stop: stop, crewId: crew?.id) }
                        .tint(Theme.accentFill)
                    if crew != nil {
                        Button("Freier Tag", systemImage: "calendar.badge.plus") {
                            Task { await perform(.nextFreeDay(id: stop.assignmentId, title: stop.title)) }
                        }
                        .tint(Theme.success)
                    }
                }
            }
            .swipeActions(edge: .leading) {
                if plan.canPlan {
                    Button("Morgen", systemImage: "forward") {
                        let next = CalendarDay.string(Calendar.current.date(byAdding: .day, value: 1, to: day) ?? day)
                        Task { await perform(.move(id: stop.assignmentId, title: stop.title, day: next, crewId: nil, startTime: nil, endTime: nil,
                                                   setTime: false)) }
                    }
                    .tint(Theme.warning)
                }
            }
            .contextMenu {
                if plan.canPlan {
                    Button("Verschieben", systemImage: "arrow.right.circle") { sheet = .move(stop: stop, crewId: crew?.id) }
                    Button("Uhrzeit ändern", systemImage: "clock") { sheet = .move(stop: stop, crewId: crew?.id) }
                    if crew != nil {
                        Button("Auf nächsten freien Tag", systemImage: "calendar.badge.plus") {
                            Task { await perform(.nextFreeDay(id: stop.assignmentId, title: stop.title)) }
                        }
                    }
                    Button("Aus dem Plan nehmen", systemImage: "trash", role: .destructive) { removing = stop }
                }
            }
    }

    @ViewBuilder
    private func peopleSection(_ plan: DayPlan) -> some View {
        if !plan.free.isEmpty || !plan.absent.isEmpty {
            Section {
                if !plan.free.isEmpty {
                    LabeledContent {
                        Text(plan.free.map(\.name).joined(separator: ", ")).multilineTextAlignment(.trailing)
                    } label: {
                        Label("Frei", systemImage: "person.badge.clock")
                    }
                }
                if !plan.absent.isEmpty {
                    LabeledContent {
                        Text(plan.absent.map { "\($0.name) (\($0.absenceNote ?? "abwesend"))" }.joined(separator: ", "))
                            .multilineTextAlignment(.trailing)
                    } label: {
                        Label("Abwesend", systemImage: "person.fill.xmark")
                    }
                }
            } header: {
                Text("Mitarbeiter")
            }
            .font(Typo.subhead)
        }
    }

    @ViewBuilder
    private func sheetView(_ sheet: PlanSheet) -> some View {
        if let plan {
            switch sheet {
            case let .assign(crew):
                AssignStopSheet(crew: crew, day: day, plan: plan) { change in
                    self.sheet = nil
                    if let change { Task { await perform(change) } }
                }
            case let .move(stop, crewId):
                MoveStopSheet(stop: stop, crewId: crewId, day: day, plan: plan) { change in
                    self.sheet = nil
                    if let change { Task { await perform(change) } }
                }
            case let .swap(crew, vehicle):
                SwapRosterSheet(crew: crew, day: dayText, plan: plan, vehicle: vehicle) { change in
                    self.sheet = nil
                    if let change { Task { await perform(change) } }
                }
            case let .swapCrews(crew):
                SwapCrewsSheet(crew: crew, day: dayText, plan: plan) { change in
                    self.sheet = nil
                    if let change { Task { await perform(change) } }
                }
            case .release:
                ReleasePlanSheet(day: day, plan: plan) { action in
                    self.sheet = nil
                    if let action { Task { await release(action) } }
                }
            }
        }
    }

    // MARK: Laden und Ändern

    private var outbox: PlanningOutbox? { model.active?.sync.map { PlanningOutbox(store: $0.store) } }

    private func cacheKey(_ day: String) -> String { "day_plan_\(day)" }

    private func load() async {
        guard let active = model.active else { return }
        loading = true
        defer { loading = false }
        notice = []
        if let outbox, !(await outbox.entries()).isEmpty {
            let result = await outbox.flush(using: active.client)
            if result.sent > 0 { toast = "\(result.sent) vorgemerkte \(result.sent == 1 ? "Änderung" : "Änderungen") gesendet" }
            notice = result.warnings
        }
        pending = await outbox?.entries() ?? []
        let requested = dayText
        do throws(APIError) {
            let fresh = try await active.client.dayPlan(requested)
            guard requested == dayText else { return }
            plan = fresh
            loadedAt = .now
            isCurrent = true
            message = nil
            if let data = try? JSONCoding.encoder.encode(StoredPlan(plan: fresh, loadedAt: .now)) {
                try? await active.sync?.store.setMeta(cacheKey(requested), String(decoding: data, as: UTF8.self))
            }
        } catch {
            guard requested == dayText else { return }
            if error.status == 403 {
                plan = nil
                message = "Für die Planung fehlt Ihnen die Berechtigung."
                return
            }
            if let text = try? await active.sync?.store.meta(cacheKey(requested)),
               let stored = try? JSONCoding.decoder.decode(StoredPlan.self, from: Data(text.utf8)) {
                plan = stored.plan
                loadedAt = stored.loadedAt
                isCurrent = false
            } else {
                plan = nil
                isCurrent = false
                message = error == .offline ? "Ohne Verbindung ist die Planung für diesen Tag nicht verfügbar." : error.message
            }
        }
        if let shotScene, plan?.canPlan == true, sheet == nil, let crew = plan?.sortedCrews.first {
            try? await Task.sleep(for: .milliseconds(400))
            switch shotScene {
            case "dispo-zuweisen": sheet = .assign(crew: crew)
            case "dispo-verschieben":
                if let stop = plan?.crews.flatMap(\.stops).first { sheet = .move(stop: stop, crewId: plan?.crew(of: stop.assignmentId)?.id) }
            case "dispo-tauschen": sheet = .swap(crew: crew, vehicle: false)
            case "dispo-fahrzeug": sheet = .swap(crew: crew, vehicle: true)
            case "dispo-kolonnen": sheet = .swapCrews(crew: crew)
            case "dispo-freigabe": sheet = .release
            default: break
            }
        }
    }

    /// Online sofort an den Server; ohne Verbindung vormerken.
    private func perform(_ change: PlanChange) async {
        guard let active = model.active else { return }
        removing = nil
        do throws(APIError) {
            let result = try await active.client.apply(change)
            notice = result.warnings
            if let fresh = result.plan, fresh.day == dayText {
                plan = fresh
            }
            toast = Self.doneText(change)
            if let movedTo = result.movedTo {
                toast = "Verschoben auf \(CalendarDay.date(movedTo)?.formatted(.dateTime.weekday(.wide).day().month()) ?? movedTo)"
            }
            await load()
            notice = result.warnings
        } catch {
            if error == .offline, let outbox {
                try? await outbox.add(change)
                pending = await outbox.entries()
                toast = "Ohne Verbindung – vorgemerkt"
            } else {
                toast = error.serverMessage ?? error.message
            }
        }
    }

    private static func doneText(_ change: PlanChange) -> String {
        switch change {
        case .assign: "Eingeplant"
        case .move: "Verschoben"
        case .remove: "Aus dem Plan genommen"
        case .order: "Reihenfolge gesichert"
        case .swap: "Besetzung geändert"
        case .nextFreeDay: "Auf den nächsten freien Tag gelegt"
        case .swapCrews: "Kolonnen getauscht"
        }
    }

    /// Freigeben bzw. Freigabe zurücknehmen – nur online (hält den aktuellen Stand fest).
    private func release(_ action: ReleasePlanSheet.Action) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            switch action {
            case let .release(from, to, note):
                let infos = try await client.releasePlan(from: from, to: to, note: note)
                toast = infos.count > 1 ? "\(infos.count) Tage freigegeben" : "Plan freigegeben"
            case let .withdraw(day):
                try await client.withdrawRelease(day: day)
                toast = "Freigabe zurückgenommen"
            }
            await load()
        } catch {
            toast = error == .offline ? "Freigeben geht nur mit Verbindung." : (error.serverMessage ?? error.message)
        }
    }

    private struct StoredPlan: Codable {
        let plan: DayPlan
        let loadedAt: Date
    }
}

/// Offene Blätter der Schnelldisposition.
enum PlanSheet: Identifiable {
    case assign(crew: DayPlan.Crew)
    case move(stop: DayPlan.Stop, crewId: UUID?)
    case swap(crew: DayPlan.Crew, vehicle: Bool)
    case swapCrews(crew: DayPlan.Crew)
    case release

    var id: String {
        switch self {
        case let .assign(crew): "assign-\(crew.id)"
        case let .move(stop, _): "move-\(stop.id)"
        case let .swap(crew, vehicle): "swap-\(crew.id)-\(vehicle)"
        case let .swapCrews(crew): "swap-crews-\(crew.id)"
        case .release: "release"
        }
    }
}

// MARK: Bausteine

/// Tagesleiste: heute und die nächsten 13 Tage als Kapseln, dazu Kalender für jeden anderen Tag.
struct DayStrip: View {
    @Binding var day: Date
    let openCalendar: () -> Void

    var body: some View {
        let calendar = Calendar.current
        let today = calendar.startOfDay(for: .now)
        let days = (-1..<14).compactMap { calendar.date(byAdding: .day, value: $0, to: today) }
        HStack(spacing: Space.s) {
            ScrollViewReader { proxy in
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: Space.s) {
                        ForEach(days, id: \.self) { date in
                            let selected = calendar.isDate(date, inSameDayAs: day)
                            Button { withAnimation(.snappy) { day = date } } label: {
                                VStack(spacing: 1) {
                                    Text(title(date)).font(Typo.labelStrong)
                                    Text(date.formatted(.dateTime.day())).font(.title3.weight(.semibold).monospacedDigit())
                                }
                                .frame(width: 56, height: 54)
                                .foregroundStyle(selected ? Theme.onAccent : (calendar.isDateInWeekend(date) ? Theme.textTertiary : Theme.textPrimary))
                                .background(selected ? Theme.accentFill : Theme.surface, in: RoundedRectangle(cornerRadius: Radius.m, style: .continuous))
                            }
                            .buttonStyle(.plain)
                            .id(date)
                            .accessibilityLabel(date.formatted(date: .complete, time: .omitted))
                            .accessibilityAddTraits(selected ? .isSelected : [])
                        }
                    }
                    .padding(.vertical, Space.xs)
                }
                .onAppear { proxy.scrollTo(calendar.startOfDay(for: day), anchor: .center) }
                .onChange(of: day) { _, new in withAnimation { proxy.scrollTo(calendar.startOfDay(for: new), anchor: .center) } }
            }
            Button(action: openCalendar) { Label("Tag wählen", systemImage: "calendar") }
                .buttonStyle(IconButtonStyle())
        }
    }

    private func title(_ date: Date) -> String {
        let calendar = Calendar.current
        if calendar.isDateInToday(date) { return "Heute" }
        if calendar.isDateInTomorrow(date) { return "Morgen" }
        return date.formatted(.dateTime.weekday(.abbreviated))
    }
}

/// Kopf einer Kolonne: Farbe, Name, „Meine Kolonne“, Leitung, Besetzung (Abwesende markiert), Fahrzeuge, Konflikte.
struct CrewCardHeader: View {
    enum Action { case assign, swapPerson, swapVehicle, swapCrews }

    let crew: DayPlan.Crew
    let canPlan: Bool
    var canSwapCrews = true
    let action: (Action) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            HStack(alignment: .center, spacing: Space.s) {
                RoundedRectangle(cornerRadius: 2).fill(Color(hexString: crew.color) ?? Theme.accent).frame(width: 4, height: 22)
                Text(crew.name).font(Typo.headline).foregroundStyle(Theme.textPrimary)
                if crew.isMine { Chip(text: "Meine Kolonne", symbol: "person.fill", tone: .accent) }
                Spacer(minLength: 0)
            }
            .frame(minHeight: 30)
            if !crew.members.isEmpty {
                FlowLayout(spacing: 6) {
                    ForEach(crew.members) { member in
                        if let note = member.absenceNote {
                            Chip(text: "\(member.name) · \(note)", symbol: "person.fill.xmark", tone: .warning)
                        } else {
                            Chip(text: member.name + (member.name == crew.leaderName ? " (Leitung)" : ""), tone: .neutral)
                        }
                    }
                    ForEach(crew.vehicles) { vehicle in
                        Chip(text: vehicle.name, symbol: vehicle.kind == "vehicle" ? "car.fill" : "wrench.and.screwdriver.fill", tone: .neutral)
                    }
                    if crew.rosterChanged { Chip(text: "Besetzung geändert", symbol: "arrow.triangle.swap", tone: .accent) }
                }
            }
            if let note = crew.rosterNote {
                Text(note).font(Typo.meta).foregroundStyle(Theme.textSecondary)
            }
            ForEach(crew.conflicts, id: \.self) { warning in
                Label(warning, systemImage: "exclamationmark.triangle.fill")
                    .labelStyle(TightLabelStyle())
                    .font(Typo.metaStrong)
                    .foregroundStyle(Theme.warning)
            }
            if canPlan {
                // Tauschen direkt sichtbar (kein verstecktes „…“-Menü), Beschriftung immer vollständig.
                FlowLayout(spacing: Space.s) {
                    CrewActionButton(title: "Mitarbeiter tauschen", symbol: "person.2.badge.gearshape") { action(.swapPerson) }
                        .accessibilityIdentifier("swapPerson-\(crew.name)")
                    CrewActionButton(title: "Fahrzeug tauschen", symbol: "truck.box") { action(.swapVehicle) }
                        .accessibilityIdentifier("swapVehicle-\(crew.name)")
                    if canSwapCrews {
                        CrewActionButton(title: "Kolonnen tauschen", symbol: "arrow.left.arrow.right") { action(.swapCrews) }
                            .accessibilityIdentifier("swapCrews-\(crew.name)")
                    }
                }
                .padding(.top, Space.xxs)
            }
        }
        .padding(.vertical, Space.xs)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("crew-\(crew.name)")
    }
}

/// Kleine Schaltfläche im Kopf einer Kolonne: Symbol und vollständiger Text (bricht bei großer Schrift um statt zu kürzen).
private struct CrewActionButton: View {
    let title: String
    let symbol: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label(title, systemImage: symbol)
                .labelStyle(TightLabelStyle())
                .font(Typo.metaStrong)
                .foregroundStyle(Theme.accent)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.horizontal, Space.s + 2)
                .padding(.vertical, Space.xs + 2)
                .frame(minHeight: 32)
                .background(Theme.surface.opacity(0.9), in: RoundedRectangle(cornerRadius: Radius.s, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: Radius.s, style: .continuous).strokeBorder(Theme.separator))
                .contentShape(RoundedRectangle(cornerRadius: Radius.s, style: .continuous))
        }
        .buttonStyle(.borderless)
    }
}

/// Baustelle in der Fahrtreihenfolge: Nummer, Uhrzeit, Titel, Tätigkeit, Adresse mit Route, Ansprechpartner mit Anruf,
/// Notiz, Hinweise, Markierung „Neu“/„Geändert“ seit der Freigabe.
struct StopRow: View {
    let stop: DayPlan.Stop
    var tint: Color = Theme.accent
    @Environment(\.openURL) private var openURL

    var body: some View {
        HStack(alignment: .top, spacing: Space.m) {
            Text("\(stop.order)")
                .font(.subheadline.weight(.bold).monospacedDigit())
                .foregroundStyle(tint)
                .frame(width: 28, height: 28)
                .background(tint.opacity(0.14), in: Circle())
                .accessibilityLabel("Station \(stop.order)")
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: Space.s) {
                    Text(stop.timeText).font(Typo.subheadStrong.monospacedDigit()).foregroundStyle(Theme.textPrimary)
                    if let change = stop.change {
                        Chip(text: change == "new" ? "Neu" : "Geändert", tone: .accent)
                    }
                }
                Text([stop.number, stop.title].compactMap { $0 }.joined(separator: " · "))
                    .font(Typo.body.weight(.semibold))
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                Text(stop.activity).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                if let address = stop.address {
                    Button {
                        if let url = stop.mapsURL { openURL(url) }
                    } label: {
                        Label(address, systemImage: "arrow.triangle.turn.up.right.diamond")
                            .labelStyle(TightLabelStyle())
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.accent)
                            .multilineTextAlignment(.leading)
                    }
                    .buttonStyle(.borderless)
                    .accessibilityHint("Route in Karten starten")
                }
                if let name = stop.siteContactName ?? stop.siteContactPhone {
                    Button {
                        if let url = ContactLink.phone(stop.siteContactPhone) { openURL(url) }
                    } label: {
                        Label([name, stop.siteContactName == nil ? nil : stop.siteContactPhone].compactMap { $0 }.joined(separator: " · "),
                              systemImage: "phone")
                            .labelStyle(TightLabelStyle())
                            .font(Typo.subhead)
                            .foregroundStyle(stop.siteContactPhone == nil ? Theme.textSecondary : Theme.accent)
                    }
                    .buttonStyle(.borderless)
                    .disabled(stop.siteContactPhone == nil)
                }
                if let notes = stop.notes {
                    Label(notes, systemImage: "text.bubble")
                        .labelStyle(TightLabelStyle())
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                }
                if !stop.resources.isEmpty {
                    Text(stop.resources.map(\.name).joined(separator: ", ")).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                }
                ForEach(stop.warnings, id: \.self) { warning in
                    Label(warning, systemImage: "exclamationmark.triangle.fill")
                        .labelStyle(TightLabelStyle())
                        .font(Typo.metaStrong)
                        .foregroundStyle(Theme.warning)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.vertical, Space.xs)
        .accessibilityIdentifier("stop-\(stop.title)")
    }
}

/// Vorgemerkte Änderungen ansehen; abgelehnte verwerfen.
struct PendingPlanChangesView: View {
    @Binding var entries: [PlanningOutbox.Entry]
    let outbox: PlanningOutbox?
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                ForEach(entries) { entry in
                    VStack(alignment: .leading, spacing: 4) {
                        Text(entry.change.summary).font(Typo.body)
                        Text("Erfasst \(entry.createdAt.formatted(date: .abbreviated, time: .shortened))").font(Typo.meta).foregroundStyle(Theme.textSecondary)
                        if let error = entry.error {
                            Label(error, systemImage: "xmark.octagon.fill").font(Typo.metaStrong).foregroundStyle(Theme.danger)
                        }
                    }
                    .swipeActions {
                        Button("Verwerfen", systemImage: "trash", role: .destructive) {
                            Task {
                                try? await outbox?.discard(entry.id)
                                entries = await outbox?.entries() ?? []
                            }
                        }
                    }
                }
            }
            .overlay { if entries.isEmpty { ContentUnavailableView("Nichts vorgemerkt", systemImage: "checkmark.circle") } }
            .navigationTitle("Vorgemerkt")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Fertig") { dismiss() } } }
        }
        .presentationDetents([.medium, .large])
    }
}
