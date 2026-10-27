import EstrichCore
import SwiftUI

/// Uhrzeiten der Planung: `HH:mm:ss` ↔ `Date` (nur Stunde und Minute zählen).
enum PlanTime {
    static func date(_ text: String?, fallback: String = "07:00") -> Date {
        let parts = (text ?? fallback).split(separator: ":").compactMap { Int($0) }
        return Calendar.current.date(bySettingHour: parts.first ?? 7, minute: parts.dropFirst().first ?? 0, second: 0, of: .now) ?? .now
    }

    static func text(_ date: Date) -> String {
        let parts = Calendar.current.dateComponents([.hour, .minute], from: date)
        return String(format: "%02d:%02d:00", parts.hour ?? 0, parts.minute ?? 0)
    }

    /// Beginn nach der letzten Station der Kolonne (sonst 07:00).
    static func suggestedStart(for crew: DayPlan.Crew) -> String {
        crew.stops.compactMap { $0.endTime ?? $0.startTime }.max() ?? "07:00:00"
    }
}

/// Konfliktvorschau beim Planen: prüft die Änderung beim Server, ohne zu speichern.
struct ConflictPreview: View {
    let warnings: [String]?
    let checking: Bool

    var body: some View {
        if checking {
            HStack(spacing: Space.s) {
                ProgressView()
                Text("Prüfe Konflikte …").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
            }
        } else if let warnings {
            if warnings.isEmpty {
                Label("Keine Konflikte", systemImage: "checkmark.circle.fill").font(Typo.subhead).foregroundStyle(Theme.success)
            } else {
                ForEach(warnings, id: \.self) { warning in
                    Label(warning, systemImage: "exclamationmark.triangle.fill").font(Typo.subhead).foregroundStyle(Theme.warning)
                }
            }
        } else {
            Label("Konfliktprüfung braucht Verbindung", systemImage: "wifi.slash").font(Typo.subhead).foregroundStyle(Theme.textSecondary)
        }
    }
}

/// Baustelle einer Kolonne für den Tag zuweisen: Vorrat (ungeplante zuerst) mit Suche, Tätigkeit, Uhrzeit, Konfliktvorschau.
struct AssignStopSheet: View {
    let crew: DayPlan.Crew
    let day: Date
    let plan: DayPlan
    let done: (PlanChange?) -> Void

    private struct Choice: Identifiable, Equatable {
        let id: UUID
        let title: String
        let subtitle: String?
        let isFree: Bool
    }

    static let activities = ["Estrich einbringen", "Randdämmung und Folie", "Erstbegehung", "CM-Messung und Belegreife", "Nacharbeiten",
                             "Schleifen und Grundieren"]

    @Environment(AppModel.self) private var model
    @State private var search = ""
    @State private var choices: [Choice] = []
    @State private var selected: Choice?
    @State private var activity = Self.activities[0]
    @State private var start = Date.now
    @State private var hasEnd = false
    @State private var end = Date.now
    @State private var warnings: [String]?
    @State private var checking = false
    @State private var offline = false
    @State private var newId = UUID.v7()

    var body: some View {
        NavigationStack {
            Form {
                if let selected {
                    Section("\(crew.name) · \(day.formatted(.dateTime.weekday(.abbreviated).day().month()))") {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(selected.title).font(Typo.bodyStrong)
                                if let subtitle = selected.subtitle { Text(subtitle).font(Typo.meta).foregroundStyle(Theme.textSecondary) }
                            }
                            Spacer()
                            Button("Ändern") { self.selected = nil }.font(Typo.subheadStrong)
                        }
                    }
                    Section("Einsatz") {
                        Picker("Tätigkeit", selection: $activity) {
                            ForEach(Self.activities, id: \.self) { Text($0).tag($0) }
                        }
                        DatePicker("Beginn", selection: $start, displayedComponents: .hourAndMinute)
                        Toggle("Ende festlegen", isOn: $hasEnd.animation())
                        if hasEnd { DatePicker("Ende", selection: $end, in: start..., displayedComponents: .hourAndMinute) }
                    }
                    Section("Prüfung") { ConflictPreview(warnings: warnings, checking: checking) }
                } else {
                    Section {
                        if choices.isEmpty {
                            Text(offline ? "Ohne Verbindung: Bauakten auf diesem Gerät." : "Keine passenden Bauvorhaben.")
                                .font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                        }
                        ForEach(choices) { choice in
                            Button { selected = choice } label: {
                                HStack(spacing: Space.m) {
                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(choice.title).font(Typo.body).foregroundStyle(Theme.textPrimary)
                                        if let subtitle = choice.subtitle {
                                            Text(subtitle).font(Typo.meta).foregroundStyle(Theme.textSecondary).lineLimit(1)
                                        }
                                    }
                                    Spacer()
                                    if choice.isFree { Chip(text: "Ungeplant", tone: .accent) }
                                }
                            }
                            .accessibilityIdentifier("pool-\(choice.title)")
                        }
                    } header: {
                        Text("Bauvorhaben für \(crew.name) · \(day.formatted(.dateTime.weekday(.abbreviated).day().month()))")
                    }
                }
            }
            .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Bauvorhaben suchen")
            .navigationTitle("Zuweisen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { done(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Einplanen") { done(change) }
                        .disabled(selected == nil)
                        .accessibilityIdentifier("confirmAssign")
                }
            }
            .task { await loadChoices() }
            .task(id: search) { await loadChoices() }
            .task(id: checkKey) { await check() }
            .onAppear {
                start = PlanTime.date(PlanTime.suggestedStart(for: crew))
                end = Calendar.current.date(byAdding: .hour, value: 4, to: start) ?? start
            }
        }
    }

    private var change: PlanChange? {
        guard let selected else { return nil }
        return .assign(id: newId, bauakteId: selected.id, title: selected.title, crewId: crew.id, day: CalendarDay.string(day), activity: activity,
                       startTime: PlanTime.text(start), endTime: hasEnd ? PlanTime.text(end) : nil)
    }

    private var checkKey: String { "\(selected?.id.uuidString ?? "")|\(PlanTime.text(start))|\(hasEnd)|\(PlanTime.text(end))|\(activity)" }

    private func loadChoices() async {
        guard let active = model.active else { return }
        try? await Task.sleep(for: .milliseconds(200))
        let planned = Set(plan.crews.flatMap(\.stops).filter { plan.crew(of: $0.assignmentId)?.id == crew.id }.map(\.bauakteId))
        let text = search.trimmingCharacters(in: .whitespaces).lowercased()
        do throws(APIError) {
            let backlog = try await active.client.backlog()
            offline = false
            choices = backlog
                .filter { !planned.contains($0.bauakteId) }
                .filter { text.isEmpty || "\($0.number ?? "") \($0.title) \($0.city ?? "") \($0.customerName ?? "")".lowercased().contains(text) }
                .sorted { ($0.isFree ? 0 : 1, $0.plannedStart ?? "9999") < ($1.isFree ? 0 : 1, $1.plannedStart ?? "9999") }
                .prefix(60)
                .map { entry in
                    let area = entry.totalAreaSquareMeters.flatMap { $0 > 0 ? "\(Int($0.rounded())) m²" : nil }
                    return Choice(id: entry.bauakteId, title: [entry.number, entry.title].compactMap { $0 }.joined(separator: " · "),
                                  subtitle: [entry.address, area].compactMap { $0 }.joined(separator: " · "), isFree: entry.isFree)
                }
        } catch {
            offline = error == .offline
            let local = (try? await active.service?.bauakten(search: text.isEmpty ? nil : text)) ?? []
            choices = local.prefix(60).map { Choice(id: $0.id, title: [$0.number, $0.title].compactMap { $0 }.joined(separator: " · "),
                                                    subtitle: $0.addressLine, isFree: false) }
        }
    }

    private func check() async {
        guard let change, let client = model.active?.client else { warnings = nil; return }
        checking = true
        defer { checking = false }
        try? await Task.sleep(for: .milliseconds(350))
        do throws(APIError) {
            warnings = try await client.check(change)
        } catch {
            warnings = error == .offline ? nil : [error.serverMessage ?? error.message]
        }
    }
}

/// Einsatz verschieben: anderer Tag, andere Kolonne und/oder andere Uhrzeit – mit Konfliktvorschau.
struct MoveStopSheet: View {
    let stop: DayPlan.Stop
    let crewId: UUID?
    let day: Date
    let plan: DayPlan
    let done: (PlanChange?) -> Void

    @Environment(AppModel.self) private var model
    @State private var targetDay = Date.now
    @State private var targetCrew: UUID?
    @State private var changeTime = false
    @State private var start = Date.now
    @State private var hasEnd = false
    @State private var end = Date.now
    @State private var warnings: [String]?
    @State private var checking = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(stop.title).font(Typo.bodyStrong)
                        Text("\(stop.activity) · \(stop.timeText)").font(Typo.meta).foregroundStyle(Theme.textSecondary)
                    }
                }
                Section("Tag") {
                    HStack(spacing: Space.s) {
                        quickDay("Morgen", 1)
                        quickDay("Übermorgen", 2)
                        quickDay("+1 Woche", 7)
                    }
                    DatePicker("Tag", selection: $targetDay, displayedComponents: .date)
                }
                Section("Kolonne") {
                    Picker("Kolonne", selection: $targetCrew) {
                        if crewId == nil { Text("Ohne Kolonne").tag(UUID?.none) }
                        ForEach(plan.crews) { crew in Text(crew.name).tag(UUID?.some(crew.id)) }
                    }
                }
                Section("Uhrzeit") {
                    Toggle("Uhrzeit ändern", isOn: $changeTime.animation())
                    if changeTime {
                        DatePicker("Beginn", selection: $start, displayedComponents: .hourAndMinute)
                        Toggle("Ende festlegen", isOn: $hasEnd.animation())
                        if hasEnd { DatePicker("Ende", selection: $end, in: start..., displayedComponents: .hourAndMinute) }
                    }
                }
                if change != nil {
                    Section("Prüfung") { ConflictPreview(warnings: warnings, checking: checking) }
                }
            }
            .navigationTitle("Verschieben")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { done(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Verschieben") { done(change) }
                        .disabled(change == nil)
                        .accessibilityIdentifier("confirmMove")
                }
            }
            .onAppear {
                targetDay = day
                targetCrew = crewId
                start = PlanTime.date(stop.startTime)
                hasEnd = stop.endTime != nil
                end = PlanTime.date(stop.endTime, fallback: "16:00")
            }
            .task(id: "\(CalendarDay.string(targetDay))|\(targetCrew?.uuidString ?? "")|\(changeTime)|\(PlanTime.text(start))|\(hasEnd)") {
                await check()
            }
        }
        .presentationDetents([.large])
    }

    private func quickDay(_ title: String, _ offset: Int) -> some View {
        let date = Calendar.current.date(byAdding: .day, value: offset, to: day) ?? day
        let selected = Calendar.current.isDate(date, inSameDayAs: targetDay)
        return Button(title) { targetDay = date }
            .buttonStyle(.bordered)
            .tint(selected ? Theme.accent : Theme.textSecondary)
            .font(Typo.subheadStrong)
    }

    /// `nil`, solange nichts geändert ist.
    private var change: PlanChange? {
        let newDay = CalendarDay.string(targetDay)
        let dayChanged = newDay != CalendarDay.string(day)
        let crewChanged = targetCrew != crewId
        guard dayChanged || crewChanged || changeTime else { return nil }
        return .move(id: stop.assignmentId, title: stop.title, day: dayChanged ? newDay : nil, crewId: crewChanged ? targetCrew : nil,
                     startTime: changeTime ? PlanTime.text(start) : nil, endTime: changeTime && hasEnd ? PlanTime.text(end) : nil,
                     setTime: changeTime)
    }

    private func check() async {
        guard let change, let client = model.active?.client else { warnings = []; return }
        checking = true
        defer { checking = false }
        try? await Task.sleep(for: .milliseconds(350))
        do throws(APIError) {
            warnings = try await client.check(change)
        } catch {
            warnings = error == .offline ? nil : [error.serverMessage ?? error.message]
        }
    }
}

/// Tagesbesetzung tauschen: wer fällt aus, wer kommt dafür (freie Mitarbeiter zuerst, dann aus anderen Kolonnen –
/// die tauschen im Gegenzug). Bei Fahrzeugen: anderes Fahrzeug/Gerät für den Tag.
struct SwapRosterSheet: View {
    let crew: DayPlan.Crew
    let day: String
    let plan: DayPlan
    let vehicle: Bool
    let done: (PlanChange?) -> Void

    private struct Option: Identifiable, Hashable {
        let id: UUID
        let name: String
        let detail: String?
    }

    @Environment(AppModel.self) private var model
    @State private var outgoing: UUID?
    @State private var incoming: UUID?
    @State private var resources: [PlanningResource] = []

    var body: some View {
        NavigationStack {
            Form {
                Section(vehicle ? "Bisheriges Fahrzeug" : "Wer fällt aus?") {
                    Picker(vehicle ? "Fahrzeug" : "Mitarbeiter", selection: $outgoing) {
                        Text(vehicle ? "Keins (zusätzlich)" : "Niemand (zusätzlich)").tag(UUID?.none)
                        ForEach(outOptions) { option in
                            Text(option.detail.map { "\(option.name) – \($0)" } ?? option.name).tag(UUID?.some(option.id))
                        }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                }
                Section {
                    Picker(vehicle ? "Fahrzeug" : "Mitarbeiter", selection: $incoming) {
                        Text("Bitte wählen").tag(UUID?.none)
                        ForEach(inOptions) { option in
                            Text(option.detail.map { "\(option.name) – \($0)" } ?? option.name).tag(UUID?.some(option.id))
                        }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                } header: {
                    Text(vehicle ? "Stattdessen" : "Wer kommt dafür?")
                } footer: {
                    Text(vehicle ? "Gilt nur für diesen Tag." : "Gilt nur für diesen Tag. Ist die Person in einer anderen Kolonne, wechselt die ausfallende Person im Gegenzug dorthin.")
                }
            }
            .navigationTitle(vehicle ? "Fahrzeug" : "Besetzung")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { done(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Tauschen") { done(change) }
                        .disabled(incoming == nil && outgoing == nil)
                        .accessibilityIdentifier("confirmSwap")
                }
            }
            .task {
                if vehicle, let client = model.active?.client { resources = (try? await client.planningResources()) ?? [] }
                if outgoing == nil { outgoing = vehicle ? crew.vehicles.first?.id : (crew.absent.first ?? crew.members.first)?.id }
            }
        }
        .presentationDetents([.large])
    }

    private var outOptions: [Option] {
        vehicle ? crew.vehicles.map { Option(id: $0.id, name: $0.name, detail: nil) }
                : crew.members.map { Option(id: $0.id, name: $0.name, detail: $0.absenceNote) }
    }

    private var inOptions: [Option] {
        if vehicle {
            let own = Set(crew.vehicles.map(\.id))
            let used = Dictionary(plan.crews.flatMap { c in c.vehicles.map { ($0.id, c.name) } }, uniquingKeysWith: { first, _ in first })
            return resources.filter { $0.isActive && !own.contains($0.id) }
                .map { Option(id: $0.id, name: $0.name, detail: used[$0.id].map { "bei \($0)" } ?? $0.identifier) }
        }
        let own = Set(crew.members.map(\.id))
        let free = plan.free.filter { !own.contains($0.id) }.map { Option(id: $0.id, name: $0.name, detail: "frei") }
        let others = plan.crews.filter { $0.id != crew.id }.flatMap { other in
            other.available.filter { !own.contains($0.id) }.map { Option(id: $0.id, name: $0.name, detail: other.name) }
        }
        return free + others
    }

    private var change: PlanChange? {
        let names = Dictionary((outOptions + inOptions).map { ($0.id, $0.name) }, uniquingKeysWith: { first, _ in first })
        let outName = outgoing.flatMap { names[$0] }
        let inName = incoming.flatMap { names[$0] }
        let text: String
        if let outName, let inName {
            text = "\(crew.name): \(inName) statt \(outName)"
        } else if let inName {
            text = "\(crew.name): \(inName) zusätzlich"
        } else if let outName {
            text = "\(crew.name): ohne \(outName)"
        } else {
            text = crew.name
        }
        return vehicle
            ? .swap(crewId: crew.id, day: day, fromUserId: nil, toUserId: nil, fromResourceId: outgoing, toResourceId: incoming, text: text)
            : .swap(crewId: crew.id, day: day, fromUserId: outgoing, toUserId: incoming, fromResourceId: nil, toResourceId: nil, text: text)
    }
}
