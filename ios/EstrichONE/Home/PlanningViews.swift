import EstrichCore
import SwiftUI

/// Meine Woche: Einsätze der nächsten 7 Tage aus der Planung (offline der zuletzt geladene Stand).
struct MyWeekView: View {
    @Environment(AppModel.self) private var model
    @State private var snapshot: MyPlanning.Snapshot?
    @State private var loaded = false

    var body: some View {
        List {
            if let snapshot, !snapshot.isCurrent {
                Section {
                    Label("Ohne Verbindung – Stand \(snapshot.loadedAt.formatted(date: .abbreviated, time: .shortened))", systemImage: "icloud.slash")
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                }
            }
            ForEach(days, id: \.self) { day in
                let items = snapshot?.assignments(on: day) ?? []
                Section {
                    if !loaded {
                        SkeletonBar(height: 16).pulsing()
                    } else if items.isEmpty {
                        Text("Nichts eingeplant")
                            .font(Typo.subhead)
                            .foregroundStyle(Theme.textTertiary)
                    }
                    ForEach(items) { assignment in
                        NavigationLink(value: HomeRoute.bauakte(assignment.bauakteId)) {
                            HStack(alignment: .firstTextBaseline, spacing: Space.m) {
                                Text(assignment.startText ?? "–")
                                    .font(Typo.bodyStrong.monospacedDigit())
                                    .frame(width: 52, alignment: .leading)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(assignment.bauakteTitle).font(Typo.body)
                                    Text([assignment.activity, assignment.crewName].compactMap { $0 }.joined(separator: " · "))
                                        .font(Typo.subhead)
                                        .foregroundStyle(Theme.textSecondary)
                                    if let address = assignment.bauakteAddress {
                                        Text(address).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                                    }
                                }
                            }
                            .padding(.vertical, Space.xs)
                        }
                    }
                } header: {
                    Text(title(of: day))
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Meine Woche")
        .task {
            if let store = model.active?.sync?.store {
                snapshot = await MyPlanning(store: store).cached()
                loaded = snapshot != nil
                snapshot = await MyPlanning(store: store).load(using: model.active?.client) ?? snapshot
            }
            loaded = true
        }
    }

    private var days: [String] {
        (0..<7).compactMap { Calendar.current.date(byAdding: .day, value: $0, to: .now) }.map { CalendarDay.string($0) }
    }

    private func title(of day: String) -> String {
        guard let date = CalendarDay.date(day) else { return day }
        let text = date.formatted(.dateTime.weekday(.wide).day().month(.wide))
        if Calendar.current.isDateInToday(date) { return "Heute, \(text)" }
        if Calendar.current.isDateInTomorrow(date) { return "Morgen, \(text)" }
        return text
    }
}

/// Meine Aufgaben: offen, mir zugewiesen oder von mir ohne Zuständigen angelegt – Mängel zuerst; abhaken per Kreis
/// oder Wischen, antippen öffnet die Bauakte.
struct MyTasksView: View {
    @Environment(AppModel.self) private var model
    @State private var items: [WorkItem] = []
    @State private var titles: [UUID: String] = [:]
    @State private var opened: UUID?
    @State private var loaded = false

    var body: some View {
        List {
            if loaded, items.isEmpty {
                ContentUnavailableView("Keine offenen Aufgaben", systemImage: "checkmark.circle", description: Text("Alles erledigt."))
                    .listRowBackground(Color.clear)
            }
            let defects = items.filter { $0.kind == .defect }
            let tasks = items.filter { $0.kind != .defect }
            if !defects.isEmpty {
                Section("Mängel (\(defects.count))") { rows(defects) }
            }
            if !tasks.isEmpty {
                Section("Aufgaben (\(tasks.count))") { rows(tasks) }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Meine Aufgaben")
        .navigationDestination(item: $opened) { BauakteDetailView(bauakteId: $0) }
        .task(id: model.active?.sync?.revision) { await reload() }
    }

    private func rows(_ list: [WorkItem]) -> some View {
        ForEach(list) { item in
            WorkItemRow(item: item, showsBauakte: item.bauakteId.flatMap { titles[$0] },
                        onOpen: item.bauakteId.map { id in { opened = id } }) {
                Task { await toggle(item) }
            }
            .swipeActions(edge: .leading, allowsFullSwipe: true) {
                Button { Task { await toggle(item) } } label: { Label("Erledigt", systemImage: "checkmark") }
                    .tint(Theme.success)
            }
        }
    }

    private func reload() async {
        guard let service = model.active?.service else { return }
        items = ((try? await service.myWorkItems()) ?? []).sorted { ($0.dueDate ?? "9999") < ($1.dueDate ?? "9999") }
        let all = (try? await service.bauakten()) ?? []
        titles = Dictionary(all.map { ($0.id, [$0.number, $0.title].compactMap { $0 }.joined(separator: " · ")) }, uniquingKeysWith: { a, _ in a })
        loaded = true
    }

    private func toggle(_ item: WorkItem) async {
        try? await model.active?.service?.setStatus(item.isDone ? .open : .done, of: item.id)
        model.active?.sync?.localChangeMade()
        await reload()
    }
}
