import EstrichCore
import SwiftUI

/// Einplanen in wenigen Tipps (Recht `planning.manage`): Tag (Heute, Morgen, Übermorgen oder Datum), Kolonne mit der
/// Auslastung des Tages, Tätigkeit mit Vorgabe, Uhrzeit auf Wunsch, Konfliktprüfung und „Einplanen“. Aus der Bauakte und
/// der Baustellenliste ist das Bauvorhaben vorbelegt; in der Disposition („Baustelle zuweisen“) sind Kolonne und Tag
/// vorbelegt und das Bauvorhaben wird zuerst aus dem Vorrat gewählt. Gesendet wird über dieselben Endpunkte.
struct QuickPlanSheet: View {
    /// Bauvorhaben, das eingeplant wird.
    struct Target: Identifiable, Equatable {
        let id: UUID
        let title: String
        var subtitle: String?
        var status: BauakteStatus?

        init(id: UUID, title: String, subtitle: String? = nil, status: BauakteStatus? = nil) {
            self.id = id
            self.title = title
            self.subtitle = subtitle
            self.status = status
        }

        init(_ bauakte: Bauakte) {
            self.init(id: bauakte.id, title: [bauakte.number, bauakte.title].compactMap { $0 }.joined(separator: ", "),
                      subtitle: bauakte.addressLine, status: bauakte.status)
        }
    }

    /// Ergebnis: die Änderung und der Satz für die Bestätigung.
    struct Result {
        let change: PlanChange
        let confirmation: String
    }

    private struct Choice: Identifiable, Equatable {
        let target: Target
        let isFree: Bool
        var id: UUID { target.id }
    }

    /// Vorbelegtes Bauvorhaben; ohne Angabe wird es zuerst aus dem Vorrat gewählt.
    var target: Target?
    var initialDay: Date = .now
    var initialCrew: UUID?
    let done: (Result?) -> Void

    @Environment(AppModel.self) private var model
    @State private var chosen: Target?
    @State private var day = Calendar.current.startOfDay(for: .now)
    @State private var plan: DayPlan?
    @State private var planIsCurrent = true
    @State private var loadingPlan = true
    /// Auslastung des gewählten Tages ist bekannt (ohne Verbindung nur, wenn der Tag schon einmal geladen wurde).
    @State private var loadKnown = true
    @State private var crewId: UUID?
    @State private var activity = QuickPlan.activities[0]
    @State private var hasTime = true
    @State private var timeEdited = false
    @State private var start = PlanTime.date("07:00:00")
    @State private var warnings: [String]?
    @State private var checking = false
    @State private var newId = UUID.v7()
    @State private var prepared = false
    @State private var search = ""
    @State private var choices: [Choice] = []
    @State private var poolOffline = false

    private var current: Target? { target ?? chosen }
    private var dayText: String { CalendarDay.string(day) }
    private var crew: DayPlan.Crew? { crewId.flatMap { plan?.crew($0) } }

    var body: some View {
        NavigationStack {
            Group {
                if let current {
                    form(current)
                } else {
                    pool
                }
            }
            .navigationTitle("Einplanen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { done(nil) } }
            }
        }
        .presentationDetents([.large])
        .onAppear {
            guard !prepared else { return }
            prepared = true
            day = Calendar.current.startOfDay(for: initialDay)
            crewId = initialCrew
            activity = QuickPlan.defaultActivity(for: target?.status)
        }
        .task(id: dayText) { await loadPlan() }
        .task(id: checkKey) { await check() }
    }

    // MARK: Blatt

    private func form(_ current: Target) -> some View {
        Form {
            Section {
                HStack(spacing: Space.m) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(current.title).font(Typo.bodyStrong).fixedSize(horizontal: false, vertical: true)
                        if let subtitle = current.subtitle, !subtitle.isEmpty {
                            Text(subtitle).font(Typo.meta).foregroundStyle(Theme.textSecondary).fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    Spacer(minLength: 0)
                    if target == nil {
                        Button("Ändern") { chosen = nil }
                            .font(Typo.subheadStrong)
                            .buttonStyle(.borderless)
                    }
                }
                .accessibilityIdentifier("quickPlanTarget")
            }
            Section("Tag") {
                HStack(spacing: Space.s) {
                    quickDay("Heute", 0)
                    quickDay("Morgen", 1)
                    quickDay("Übermorgen", 2)
                }
                DatePicker("Datum", selection: $day, displayedComponents: .date)
                    .accessibilityIdentifier("quickPlanDate")
            }
            Section {
                crewRows
            } header: {
                Text("Kolonne")
            } footer: {
                if plan != nil, !planIsCurrent {
                    Text(loadKnown ? "Keine Verbindung. Die Auslastung zeigt den zuletzt geladenen Stand."
                                   : "Keine Verbindung. Die Auslastung dieses Tages ist nicht bekannt.")
                }
            }
            Section("Einsatz") {
                Picker("Tätigkeit", selection: $activity) {
                    ForEach(QuickPlan.activities, id: \.self) { Text($0).tag($0) }
                }
                .accessibilityIdentifier("quickPlanActivity")
                Toggle("Uhrzeit festlegen", isOn: $hasTime.animation())
                if hasTime {
                    DatePicker("Beginn", selection: Binding(get: { start }, set: { start = $0; timeEdited = true }),
                               displayedComponents: .hourAndMinute)
                }
            }
            if change != nil {
                Section("Prüfung") {
                    if let existing = plan.flatMap({ QuickPlan.planned(current.id, in: $0) }) {
                        Label("An diesem Tag bereits eingeplant: \(existing.crew?.name ?? "ohne Kolonne"), \(existing.stop.timeText)",
                              systemImage: "info.circle")
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textSecondary)
                    }
                    ConflictPreview(warnings: warnings, checking: checking)
                }
            }
        }
        .safeAreaInset(edge: .bottom) {
            Button {
                if let result { done(result) }
            } label: {
                Text("Einplanen").frame(maxWidth: .infinity)
            }
            .estrichButton(.primary, size: .regular, fullWidth: true)
            .disabled(change == nil)
            .accessibilityIdentifier("confirmAssign")
            .padding(.horizontal, Space.l)
            .padding(.vertical, Space.s)
            .background(.bar)
        }
    }

    @ViewBuilder
    private var crewRows: some View {
        if let plan {
            if plan.crews.isEmpty {
                Text("Für diesen Tag sind keine Kolonnen angelegt.")
                    .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
            }
            ForEach(plan.sortedCrews) { candidate in
                Button { select(candidate) } label: {
                    HStack(spacing: Space.m) {
                        RoundedRectangle(cornerRadius: 2, style: .continuous)
                            .fill(Color(hexString: candidate.color) ?? Theme.accent)
                            .frame(width: 4, height: 30)
                            .accessibilityHidden(true)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(candidate.name).font(Typo.body).foregroundStyle(Theme.textPrimary)
                            if loadKnown {
                                Text(loadText(candidate))
                                    .font(Typo.subhead)
                                    .foregroundStyle(Theme.textSecondary)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        Image(systemName: "checkmark")
                            .font(.body.weight(.semibold))
                            .foregroundStyle(Theme.accent)
                            .opacity(crewId == candidate.id ? 1 : 0)
                            .accessibilityHidden(true)
                    }
                    .frame(minHeight: Size.minTap)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(crewId == candidate.id ? .isSelected : [])
                .accessibilityIdentifier("quickPlanCrew-\(candidate.name)")
            }
        } else if loadingPlan {
            HStack(spacing: Space.s) {
                ProgressView()
                Text("Kolonnen werden geladen …").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
            }
        } else {
            Text("Ohne Verbindung sind die Kolonnen noch nicht bekannt. Bitte mit Verbindung erneut versuchen.")
                .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
        }
    }

    /// „2 Einsätze“ bzw. „frei“; nicht besetzte Kolonnen sind gekennzeichnet.
    private func loadText(_ candidate: DayPlan.Crew) -> String {
        var parts = [QuickPlan.loadText(candidate.stops.count)]
        if !candidate.members.isEmpty, candidate.available.isEmpty { parts.append("nicht besetzt") }
        return parts.joined(separator: ", ")
    }

    private func quickDay(_ title: String, _ offset: Int) -> some View {
        let today = Calendar.current.startOfDay(for: .now)
        let date = Calendar.current.date(byAdding: .day, value: offset, to: today) ?? today
        let selected = Calendar.current.isDate(date, inSameDayAs: day)
        return Button(title) { day = date }
            .buttonStyle(.bordered)
            .tint(selected ? Theme.accent : Theme.textSecondary)
            .font(Typo.subheadStrong)
            .accessibilityAddTraits(selected ? .isSelected : [])
            .accessibilityIdentifier("quickPlanDay-\(offset)")
    }

    private func select(_ candidate: DayPlan.Crew) {
        crewId = candidate.id
        if !timeEdited { start = PlanTime.date(QuickPlan.suggestedStart(for: candidate)) }
    }

    // MARK: Vorrat (nur ohne vorbelegtes Bauvorhaben)

    private var pool: some View {
        List {
            Section {
                if choices.isEmpty {
                    Text(poolOffline ? "Keine Verbindung. Es stehen die Bauakten auf diesem Gerät zur Wahl." : "Keine passenden Bauvorhaben.")
                        .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                }
                ForEach(choices) { choice in
                    Button {
                        chosen = choice.target
                        activity = QuickPlan.defaultActivity(for: choice.target.status)
                    } label: {
                        HStack(spacing: Space.m) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(choice.target.title).font(Typo.body).foregroundStyle(Theme.textPrimary)
                                if let subtitle = choice.target.subtitle, !subtitle.isEmpty {
                                    Text(subtitle).font(Typo.meta).foregroundStyle(Theme.textSecondary).fixedSize(horizontal: false, vertical: true)
                                }
                            }
                            Spacer()
                            if choice.isFree { Chip(text: "Ungeplant", tone: .accent) }
                        }
                    }
                    .accessibilityIdentifier("pool-\(choice.target.title)")
                }
            } header: {
                Text("Bauvorhaben wählen")
            }
        }
        .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Bauvorhaben suchen")
        .task(id: search) { await loadChoices() }
    }

    private func loadChoices() async {
        guard let active = model.active else { return }
        try? await Task.sleep(for: .milliseconds(200))
        guard !Task.isCancelled else { return }
        let text = search.trimmingCharacters(in: .whitespaces).lowercased()
        do throws(APIError) {
            let backlog = try await active.client.backlog()
            poolOffline = false
            choices = backlog
                .filter { text.isEmpty || "\($0.number ?? "") \($0.title) \($0.city ?? "") \($0.customerName ?? "")".lowercased().contains(text) }
                .sorted { ($0.isFree ? 0 : 1, $0.plannedStart ?? "9999") < ($1.isFree ? 0 : 1, $1.plannedStart ?? "9999") }
                .prefix(60)
                .map { entry in
                    let area = entry.totalAreaSquareMeters.flatMap { $0 > 0 ? "\(Int($0.rounded())) m²" : nil }
                    return Choice(target: Target(id: entry.bauakteId, title: [entry.number, entry.title].compactMap { $0 }.joined(separator: ", "),
                                                 subtitle: [entry.address, area].compactMap { $0 }.joined(separator: ", "),
                                                 status: entry.status.flatMap { BauakteStatus(rawValue: $0) }),
                                  isFree: entry.isFree)
                }
        } catch {
            poolOffline = error == .offline
            let local = (try? await active.service?.bauakten(search: text.isEmpty ? nil : text)) ?? []
            choices = local.prefix(60).map { Choice(target: Target($0), isFree: false) }
        }
    }

    // MARK: Laden und Prüfen

    private var change: PlanChange? {
        guard let current, let crewId, plan?.crew(crewId) != nil else { return nil }
        return .assign(id: newId, bauakteId: current.id, title: current.title, crewId: crewId, day: dayText, activity: activity,
                       startTime: hasTime ? PlanTime.text(start) : nil, endTime: nil)
    }

    private var result: Result? {
        change.map {
            Result(change: $0, confirmation: QuickPlan.confirmation(day: dayText, today: CalendarDay.string(.now),
                                                                    start: hasTime ? PlanTime.text(start) : nil, crewName: crew?.name))
        }
    }

    private var checkKey: String {
        "\(current?.id.uuidString ?? "")|\(dayText)|\(crewId?.uuidString ?? "")|\(hasTime)|\(PlanTime.text(start))|\(activity)|\(plan != nil)"
    }

    /// Kolonnen und Auslastung des Tages: frisch vom Server, sonst der zuletzt geladene Stand (auch der von heute).
    private func loadPlan() async {
        guard let active = model.active else { return }
        let requested = dayText
        loadingPlan = true
        defer { if requested == dayText { loadingPlan = false } }
        var loaded: DayPlan?
        var known = true
        do throws(APIError) {
            let fresh = try await active.client.dayPlan(requested)
            await DayPlanStore.save(fresh, day: requested, model: model)
            loaded = fresh
            planIsCurrent = true
        } catch {
            planIsCurrent = false
            loaded = await DayPlanStore.cached(day: requested, model: model)?.plan
            known = loaded != nil
            if loaded == nil {
                // Ohne Stand für diesen Tag: die Kolonnen aus dem zuletzt geladenen Plan von heute, ohne Auslastung.
                loaded = await DayPlanStore.cached(day: CalendarDay.string(.now), model: model)?.plan.crewsOnly(day: requested)
            }
        }
        guard requested == dayText, !Task.isCancelled else { return }
        plan = loaded
        loadKnown = known
        guard let loaded else { return }
        if let crewId, loaded.crew(crewId) == nil { self.crewId = nil }
        if crewId == nil { crewId = QuickPlan.preselectedCrew(in: loaded, wanted: initialCrew) }
        if !timeEdited { start = PlanTime.date(QuickPlan.suggestedStart(for: crew)) }
    }

    private func check() async {
        guard let change, let client = model.active?.client else { warnings = []; return }
        checking = true
        defer { checking = false }
        try? await Task.sleep(for: .milliseconds(350))
        guard !Task.isCancelled else { return }
        do throws(APIError) {
            warnings = try await client.check(change)
        } catch {
            warnings = error == .offline ? nil : [error.serverMessage ?? error.message]
        }
    }
}

/// Sendet eine Änderung der Planung außerhalb der Disposition (Bauakte, Baustellenliste): online sofort, ohne Verbindung
/// vorgemerkt wie in der Disposition (Versand beim nächsten Öffnen der Disposition).
enum QuickPlanRunner {
    struct Outcome {
        let message: String
        /// Beim Server angekommen (nicht nur vorgemerkt).
        let sent: Bool
    }

    @MainActor
    static func perform(_ change: PlanChange, confirmation: String, model: AppModel) async -> Outcome {
        guard let active = model.active else { return Outcome(message: "Nicht angemeldet.", sent: false) }
        do throws(APIError) {
            let result = try await active.client.apply(change)
            if let store = active.sync?.store {
                // Eigene Einsätze neu laden, damit „Nächster Termin“ und die Liste den neuen Stand zeigen.
                _ = await MyPlanning(store: store).load(using: active.client)
            }
            let hint = result.warnings.first.map { " Hinweis: \($0)" } ?? ""
            return Outcome(message: confirmation + (hint.isEmpty ? "" : "." + hint), sent: true)
        } catch {
            if error == .offline, let store = active.sync?.store {
                do {
                    try await PlanningOutbox(store: store).add(change)
                    return Outcome(message: "Keine Verbindung. Vorgemerkt, Versand beim Öffnen der Disposition.",
                                   sent: false)
                } catch {
                    return Outcome(message: "Keine Verbindung. Die Änderung konnte nicht vorgemerkt werden.", sent: false)
                }
            }
            return Outcome(message: error.serverMessage ?? error.message, sent: false)
        }
    }
}
